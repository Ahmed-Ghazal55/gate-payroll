using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

// Structured BR25.9/29 postings, separate from free-form journals and Claude's K2 request UI/API.
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
    static decimal Amount(JsonObject input){decimal value=Debts.Amount(input,"amount");J.Need(value>0&&value<=10000000,"المبلغ موجب وبدقة الهللة.");return value;}
    static string RiderCompany(JsonObject person)=>FinancialCore.FindSponsor(person.S("financeSponsorId"))?.Id??FinancialCore.FindSponsor(person.S("sponsorId"))?.Id??FinancialCore.FindSponsor(person.S("sponsor"))?.Id??"";
    static void Rider(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string company,string rider)
    {var person=Store.Get(c,J.Iqama(rider),"rider",tx)??throw new ApiError("المندوب غير موجود.",404);J.Need(ErpRiderClassification.ListedAsRider(person),"المندوب غير نشط للرواتب.");FinancialScopes.DemandRider(actor,person);J.Need(RiderCompany(person)==company,"السجل المالي للمندوب لا يطابق العملية.");}
    public static bool Visible(ClaimsPrincipal actor,JsonObject doc)=>FinancialScopes.Sponsor(actor,doc.S("companyId"))&&(doc.S("type")!="mada_topup"||FinancialScopes.Account(actor,"BANK."+FinancialCore.ResolveSponsor(doc.S("companyId")).Code));
    public static JsonObject Draft(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,ManualJournals.CreatePermission);PayValues.Fields(input,"type","companyId","date","reference","description","amount","riderIqama","bankAccountCode");
        string type=input.Required("type"),company=FinancialCore.ResolveSponsor(input.Required("companyId")).Id,date=input.Required("date");FinancialScopes.DemandSponsor(actor,company);
        J.Need(type is "mada_topup" or "salary_increase" or "salary_decrease" or "administrative_charge","نوع العملية غير معروف.");J.Need(DateOnly.TryParseExact(date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),"تاريخ صحيح مطلوب.");Open(c,tx,company,date[..7]);
        string reference=input.Required("reference"),description=input.Required("description");J.Need(reference.Length<=200&&description.Length<=1000,"المرجع أو البيان أطول من الحد.");J.Need(!Store.List(c,Kind,tx).Any(d=>d.S("companyId")==company&&d.S("reference")==reference),"مرجع العملية مكرر.");
        string bank=type=="mada_topup"?Bank(actor,company,input.Required("bankAccountCode")):"",rider=type=="mada_topup"?"":input.Required("riderIqama");if(rider!="")Rider(c,tx,actor,company,rider);
        var doc=new JsonObject{["id"]="finance-adjustment:"+Guid.NewGuid().ToString("N"),["type"]=type,["companyId"]=company,["date"]=date,["period"]=date[..7],["reference"]=reference,["description"]=description,["amount"]=Amount(input),["riderIqama"]=rider,["bankAccountCode"]=bank,["status"]="draft",["createdBy"]=actor.Identity!.Name};
        Store.Save(c,Kind,doc,tx);Store.Audit(c,tx,"finance.adjustment_drafted",doc.S("id"),actor.Identity!.Name!);return doc;
    }
    public static JsonObject Approve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        AccessPolicy.Demand(actor,ManualJournals.ApprovePermission);var doc=Store.Get(c,id,Kind,tx)??throw new ApiError("العملية غير موجودة.",404);string company=doc.S("companyId"),period=doc.S("period"),type=doc.S("type");FinancialScopes.DemandSponsor(actor,company);
        J.Need(AccessPolicy.Independent(actor,doc.S("createdBy"),"mada.approved",c,tx,doc),"يلزم مراجع مستقل.",403);if(doc.S("status")=="approved")return doc;J.Need(doc.S("status")=="draft","العملية ليست مسودة جاهزة.");Open(c,tx,company,period);
        decimal amount=doc.N("amount");string debit,credit;
        switch(type)
        {
            case "mada_topup":credit=Bank(actor,company,doc.S("bankAccountCode"));J.Need(Balance(c,tx,company,credit)>=amount,"رصيد البنك الفعلي غير كافٍ؛ الأرصدة القديمة غير المربوطة لا تُخمن.",409);debit="112404";break;
            case "salary_increase":debit="512122";credit="212103";break;
            case "salary_decrease":debit="212103";credit="512101";break;
            case "administrative_charge":debit="112505";credit="421302";break;
            default:throw new ApiError("نوع عملية غير معروف.");
        }
        if(doc.S("riderIqama")!="")Rider(c,tx,actor,company,doc.S("riderIqama"));
        var lines=new JsonArray(PayPosting.Line(debit,company,amount,0,"",doc.S("riderIqama"),period,id),PayPosting.Line(credit,company,0,amount,"",doc.S("riderIqama"),period,id));
        foreach(var line in lines.OfType<JsonObject>())line["structuredFinance"]=true;
        // A validated tag lets the legacy vault availability guard see a target-chart outflow too.
        if(type=="mada_topup")lines[1]!["legacyVaultCode"]="BANK."+FinancialCore.ResolveSponsor(company).Code;
        var journal=FinancialCore.PostJournal(c,tx,type,doc.S("description"),period,"FINANCE-ADJUSTMENT:"+id,lines,actor.Identity!.Name!,iqama:doc.S("riderIqama"),originSponsorId:company,creditorSponsorId:company,targetChart:true,payChart:true,journalDate:doc.S("date"));
        doc["status"]="approved";doc["journalEntryId"]=journal.S("id");doc["approvedBy"]=actor.Identity!.Name;Store.Save(c,Kind,doc,tx);Store.Audit(c,tx,"finance.adjustment_approved",id,actor.Identity!.Name!);return doc;
    }
    public static JsonObject PayInternet(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject request,JsonObject input)
    {
        AccessPolicy.Demand(actor,"advances.payout");string company=FinancialCore.ResolveSponsor(request.S("companyId")).Id,fulfilment=request.Required("fulfilment"),vault=request.S("vaultCode",request.S("vault"));FinancialScopes.DemandSponsor(actor,company);
        J.Need(fulfilment is "cash" or "bank" or "recharge_card","طريقة تسليم النت غير صالحة.");J.Need(request.S("approvedBy")!=""&&request.S("status")=="approved","طلب معتمد مطلوب.");J.Need(AccessPolicy.Independent(actor,request.S("requestedBy"),"advance.payout",c,tx,request),"صاحب الطلب لا يصرفه.",403);
        J.Need(input.Required("reference").Length is >2 and <=120,"مرجع الصرف غير صالح.");decimal amount=request.N("amount");J.Need(amount>0&&amount==J.Money(amount)&&request.N("monthlyInstallment")>0&&request.N("monthlyInstallment")<=amount,"مبلغ السلفة أو القسط غير صالح.");string period=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM");Open(c,tx,company,period);
        var rider=Store.Get(c,request.S("riderIqama"),"rider",tx)??throw new ApiError("المندوب غير موجود.");FinancialScopes.DemandRider(actor,rider);J.Need(RiderCompany(rider)==company,"شركة المندوب لا تطابق السلفة.");
        string funding,legacy="";decimal available;
        if(fulfilment=="recharge_card")
        {
            J.Need(vault=="MADA"&&request.S("networkCode")!=""&&request.S("packageCode")!="","مكنةMADAوالشبكة والباقة المعتمدتان مطلوبون.");funding="112404";available=Balance(c,tx,company,funding);
        }
        else
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
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/finance/accounting-adjustments",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Draft(c,tx,ctx.User,input))).WithTags("Accounting and reports").WithSummary("Draft a structured Mada top-up, salary correction or initial administrative charge");
        api.MapPost("/finance/accounting-adjustments/{id}/approve",(string id,HttpContext ctx)=>store.Write((c,tx)=>Approve(c,tx,ctx.User,id))).WithTags("Accounting and reports").WithSummary("Independently approve a structured financial operation after balance and period checks");
        api.MapGet("/finance/accounting-adjustments",(HttpContext ctx)=>{AccessPolicy.Demand(ctx.User,ManualJournals.CreatePermission);return store.Read(c=>ApiPagination.Apply(Store.List(c,Kind).Where(d=>Visible(ctx.User,d)),ctx.Request.Query,true));}).WithTags("Accounting and reports").WithSummary("List scoped structured financial adjustments");
        api.MapGet("/finance/mada/balance",(string companyId,HttpContext ctx)=>{if(!AccessPolicy.Has(ctx.User,"advances.payout")&&!AccessPolicy.Has(ctx.User,"treasury.view"))throw new ApiError("صلاحية مالية مطلوبة.",403);string company=FinancialCore.ResolveSponsor(companyId).Id;FinancialScopes.DemandSponsor(ctx.User,company);return store.Read(c=>new{balance=Balance(c,null,company,"112404")});}).WithTags("Banks and treasury").WithSummary("Read company-specific Mada recharge balance without exposing other vaults");
    }
}
