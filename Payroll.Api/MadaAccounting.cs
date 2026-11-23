using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

// Internet-advance payout + Mada balance helpers. Structured adjustment routes are gone (410).
public static class MadaAccounting
{
    public const string Kind="finance_adjustment";
    public static decimal Balance(DbConnection c,DbTransaction? tx,string company,string code)=>J.Money(Store.List(c,"journal",tx).Where(FinanceViews.Posted).Where(j=>j.S("chart")=="target").SelectMany(j=>(j["lines"] as JsonArray??[]).OfType<JsonObject>()).Where(l=>l.S("sponsorId")==company&&l.S("accountCode")==code).Sum(l=>l.N("debit")-l.N("credit")));
    static void Open(DbConnection c,DbTransaction? tx,string company,string period)=>J.Need(!Store.List(c,"accounting_period",tx).Any(p=>p.S("companyId")==company&&p.S("period")==period&&p.S("status")=="closed"),"الفترة المحاسبية مغلقة.");
    static string Bank(ClaimsPrincipal actor,string company,string raw)
    {
        var sp=FinancialCore.ResolveSponsor(company);FinancialScopes.DemandSponsor(actor,sp.Id);FinancialScopes.DemandAccount(actor,"BANK."+sp.Code);
        string code=PayPlatforms.Active(raw);J.Need(code!="111204"&&FinanceViews.Accounts.Any(a=>a.S("code")==code&&a.S("type")=="bank"),"اختر حساب بنك فعلي؛ أموال تصفيات جاهز معزولة.");return code;
    }
    static string RiderCompany(JsonObject person)=>FinancialCore.FindSponsor(person.S("financeSponsorId"))?.Id??FinancialCore.FindSponsor(person.S("sponsorId"))?.Id??FinancialCore.FindSponsor(person.S("sponsor"))?.Id??"";
    static void Rider(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string company,string rider)
    {var person=Store.Get(c,J.Iqama(rider),"rider",tx)??throw new ApiError("المندوب غير موجود.",404);J.Need(ErpRiderClassification.ListedAsRider(person),"المندوب غير نشط للرواتب.");FinancialScopes.DemandRider(actor,person);J.Need(RiderCompany(person)==company,"السجل المالي للمندوب لا يطابق العملية.");}
    public static bool Visible(ClaimsPrincipal actor,JsonObject doc)=>FinancialScopes.Sponsor(actor,doc.S("companyId"))&&(doc.S("type")!="mada_topup"||FinancialScopes.Account(actor,"BANK."+FinancialCore.ResolveSponsor(doc.S("companyId")).Code));
    public const string AdjustmentsGone="شاشة تسويات مدا والراتب والرسوم الإدارية موقوفة؛ استخدم المدفوعات وبنود التعديل وطلبات الخصم.";
    public const string MadaBalanceGone="قراءة رصيد مدا عبر المسار القديم موقوفة؛ استخدم المدفوعات ← جهاز مدا والشحن.";
    public static JsonObject Draft(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input){_=c;_=tx;_=actor;_=input;throw new ApiError(AdjustmentsGone,410);}
    public static JsonObject Approve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id){_=c;_=tx;_=actor;_=id;throw new ApiError(AdjustmentsGone,410);}
    public static JsonObject PayInternet(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject request,JsonObject input)
    {
        AccessPolicy.Demand(actor,"advances.payout");string fulfilment=request.Required("fulfilment");
        // RWP7a3: legacy card payout bypassed the new Mada wallet register — cards go through /payments/mada/cards/issue.
        if(fulfilment=="recharge_card")throw new ApiError("صرف كارت الشحن عبر المسار القديم موقف؛ استخدم المدفوعات ← إصدار كروت مدى.",410);
        // RWD1b H2: Internet Disbursement cash/bank is cash-out; closed until payment-engine disbursement path.
        DebtLedger.DemandDisburseAvailable();
        string company=FinancialCore.ResolveSponsor(request.S("companyId")).Id,vault=request.S("vaultCode",request.S("vault"));FinancialScopes.DemandSponsor(actor,company);
        J.Need(fulfilment is "cash" or "bank","طريقة تسليم النت غير صالحة.");J.Need(request.S("approvedBy")!=""&&request.S("status")=="approved","طلب معتمد مطلوب.");J.Need(AccessPolicy.Independent(actor,request.S("requestedBy"),"advance.payout",c,tx,request,scope:AccessPolicy.ScopePayApprovals),"صاحب الطلب لا يصرفه.",403);
        J.Need(input.Required("reference").Length is >2 and <=120,"مرجع الصرف غير صالح.");decimal amount=request.N("amount");J.Need(amount>0&&amount==J.Money(amount)&&request.N("monthlyInstallment")>0&&request.N("monthlyInstallment")<=amount,"مبلغ السلفة أو القسط غير صالح.");string period=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM");Open(c,tx,company,period);
        var rider=Store.Get(c,request.S("riderIqama"),"rider",tx)??throw new ApiError("المندوب غير موجود.");FinancialScopes.DemandRider(actor,rider);J.Need(RiderCompany(rider)==company,"شركة المندوب لا تطابق السلفة.");
        string funding,legacy="";decimal available;
        {
            var bank=FinancialCore.ValidateVaultForOperation(vault,"InternetAdvance","disbursement");J.Need(bank.DefaultSponsorId==company&&(fulfilment=="cash"?bank.VaultKind=="MainInternal":bank.VaultKind=="Bank"),"خزنة الصرف لا تطابق طريقة التسليم والشركة.");FinancialScopes.DemandAccount(actor,vault);legacy=vault;
            if(fulfilment=="bank"){funding=Bank(actor,company,input.Required("bankAccountCode"));available=Balance(c,tx,company,funding);}
            else{funding=FinanceViews.MapAccount(new(){["accountCode"]=vault},new(),null,null,null)??throw new ApiError("حساب النقدية غير مربوط.");available=AccountingPolicy.Balance(c,tx,vault);}
        }
        J.Need(available>=amount,"الرصيد غير كافٍ؛ الطلب يبقى معتمدًا.",409);
        string debtId="internet-debt:"+request.S("id");J.Need(Store.Get(c,debtId,"debt",tx)==null,"السلفة مصروفة من قبل.",409);
        var debt=new JsonObject{["id"]=debtId,["iqama"]=request.S("riderIqama"),["riderName"]=rider.S("name"),["debtorType"]="Rider",["category"]="InternetAdvance",["categoryAr"]="سلفة إنترنت",["sector"]="Advances",["receivableAccountCode"]="1210",["targetReceivableAccountCode"]="112503",["entryMode"]="Disbursement",["accountingVersion"]=AccountingEvents.Version,["totalCostAmount"]=amount,["totalAmount"]=amount,["remainingBalance"]=amount,["monthlyInstallment"]=request.N("monthlyInstallment"),["paidAmount"]=0,["waivedAmount"]=0,["allocationMode"]="RiderFull",["companyShareAmount"]=0,["originalCreditorSponsorId"]=company,["originSponsorId"]=company,["creditorSponsorId"]=company,["status"]="active",["paymentVaultCode"]=vault,["advanceRequestId"]=request.S("id"),["sourceRef"]="ADV-REQ:"+request.S("id"),["effectiveFrom"]=period,["postingPeriod"]=period,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        var lines=new JsonArray(PayPosting.Line("112503",company,amount,0,"",debt.S("iqama"),period,debt.S("sourceRef"),debt:debtId),PayPosting.Line(funding,company,0,amount,"",debt.S("iqama"),period,debt.S("sourceRef"),debt:debtId));foreach(var line in lines.OfType<JsonObject>())line["structuredFinance"]=true;if(legacy!="")lines[1]!["legacyVaultCode"]=legacy;
        var journal=FinancialCore.PostJournal(c,tx,"internet_advance","صرف سلفة إنترنت",period,"ADV-INTERNET:"+request.S("id"),lines,actor.Identity!.Name!,iqama:debt.S("iqama"),debtId:debtId,originSponsorId:company,creditorSponsorId:company,targetChart:true,payChart:true);
        debt["journalEntryId"]=journal.S("id");debt["journalNumber"]=journal.S("journalNumber");debt["payoutVoucherNo"]=Vouchers.RecordPayout(c,tx,debt,journal,company,input.S("odooVoucherNo")).S("voucherNo");Store.Save(c,"debt",debt,tx);Store.Audit(c,tx,"debt.internet_advance_paid",debtId,actor.Identity!.Name!);return debt;
    }
    static IResult Gone(string message)=>Results.Json(ApiErrors.Body(410,message),statusCode:410);
    public static void Map(RouteGroupBuilder api,Store store)
    {
        _=store;
        api.MapPost("/finance/accounting-adjustments",(JsonObject input)=>{_=input;return Gone(AdjustmentsGone);}).WithTags("Accounting and reports").WithSummary("Gone: structured adjustments moved to Payments / pay items / HR requests");
        api.MapPost("/finance/accounting-adjustments/{id}/approve",(string id)=>{_=id;return Gone(AdjustmentsGone);}).WithTags("Accounting and reports").WithSummary("Gone: structured adjustment approval retired");
        api.MapGet("/finance/accounting-adjustments",()=>Gone(AdjustmentsGone)).WithTags("Accounting and reports").WithSummary("Gone: structured adjustments list retired");
        api.MapGet("/finance/mada/balance",(string companyId)=>{_=companyId;return Gone(MadaBalanceGone);}).WithTags("Banks and treasury").WithSummary("Gone: Mada balance now under Payments Mada wallets");
    }
}
