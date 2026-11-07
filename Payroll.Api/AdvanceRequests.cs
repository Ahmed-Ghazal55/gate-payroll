using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

// BR §25.3: supervisor request → responsible accountant (approve / reject / propose) → supervisor reply → custodian payout → debt.
public static class AdvanceRequests
{
    public const string Kind="advance_request";
    const string Review="advance.review";
    static readonly string[] Final=["rejected","cancelled","paid"];
    static string Me(ClaimsPrincipal u)=>u.Identity!.Name!;
    static string Payout(string vault)=>"advance.payout:"+vault;
    static string Category(string type)=>type switch{"cash"=>"CashAdvance","internet"=>"InternetAdvance",_=>"BankAdvance"};
    static string Type(string value){J.Need(value is "cash" or "bank" or "internet","نوع السلفة نقدية (cash) أو بنكية (bank) أو إنترنت (internet).");return value;}
    // BR §25.9: how the advance reaches the rider. Cash/bank advances carry their own type; internet chooses.
    // Custody code of the mada top-up machine; payout routing is advance.payout:MADA.
    public const string Mada="MADA";
    static string Fulfilment(JsonObject r)=>r.S("fulfilment",r.S("type"));
    static string Fulfilment(string type,string requested)
    {
        if(type!="internet"){J.Need(requested==""||requested==type,"طريقة الصرف تخص سلفة الإنترنت فقط.");return type;}
        J.Need(requested is "cash" or "bank" or "recharge_card","سلفة الإنترنت: نقدي أو بنكي أو كارت شحن (recharge_card).");return requested;
    }
    // A recharge card is priced by the approved package of the approved network (BR §27 lists).
    static (string Network,string Package,decimal Price) Card(DbConnection c,DbTransaction tx,JsonObject input)
    {
        string network=input.Required("networkCode"),package=input.Required("packageCode");
        J.Need(ConfigLists.Current(c,tx,"recharge_networks",network)!=null,"شبكة الشحن غير موجودة أو غير معتمدة.");
        var p=ConfigLists.Current(c,tx,"recharge_packages",package)??throw new ApiError("باقة الشحن غير موجودة أو غير معتمدة.");
        J.Need(p["values"].S("network")==network,"الباقة لا تخص الشبكة المختارة.");decimal price=p["values"].N("price");J.Need(price>0,"سعر الباقة غير محدد.");return (network,package,price);
    }
    static decimal Amount(JsonObject input,string key){decimal v=Debts.Amount(input,key);J.Need(v>0&&v<=100000,"مبلغ السلفة غير صالح.");return v;}
    static string Period(DateTimeOffset? at=null)=>(at??DateTimeOffset.UtcNow).ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM");
    static string Vault(string type,string sponsorId,string requested,string? fulfilment=null)
    {
        string how=fulfilment??type;
        // The mada machine custody account is posted by the payout step (BR §25.9, Codex C3); no vault is chosen here.
        if(how=="recharge_card")return Mada;
        string code=requested!=""?requested:FinancialCore.DefaultVaultForSponsor(sponsorId,how=="cash"?"MAIN":"BANK",Category(type));
        FinancialCore.ValidateVaultForOperation(code,Category(type),"disbursement");return code;
    }
    static JsonObject Get(DbConnection c,DbTransaction? tx,string id)=>Store.Get(c,id,Kind,tx)?.Clone()??throw new ApiError("طلب السلفة غير موجود.",404);
    static void Decision(JsonObject request,ClaimsPrincipal actor,string action,string note,JsonObject? snapshot=null,JsonObject? extra=null)
    {
        var row=new JsonObject{["actor"]=Me(actor),["action"]=action,["note"]=note,["at"]=DateTimeOffset.UtcNow.ToString("O"),["fromStatus"]=request.S("status"),["snapshot"]=snapshot??new JsonObject{["via"]="requester"}};
        if(extra!=null)foreach(var p in extra)row[p.Key]=p.Value?.DeepClone();
        (request["decisions"] as JsonArray)!.Add(row);
    }
    static void Save(DbConnection c,DbTransaction tx,JsonObject request,ClaimsPrincipal actor,string audit)
    {request["updatedAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,Kind,request,tx);Store.Audit(c,tx,audit,request.S("id"),Me(actor));}
    static IEnumerable<string> Users(List<JsonObject> responsible)=>responsible.Select(r=>r.S("userId"));
    static string Label(JsonObject r)=>$"{r.S("type") switch{"cash"=>"سلفة نقدية","internet"=>Fulfilment(r)=="recharge_card"?"سلفة إنترنت (كارت شحن)":"سلفة إنترنت",_=>"سلفة بنكية"}} {r.N("amount"):0.00} ر.س — {r.S("riderName")} ({r.S("riderIqama")})";
    static void Notify(DbConnection c,DbTransaction tx,JsonObject r,IEnumerable<string> users,string step,string title,ClaimsPrincipal actor,bool action)
    {Notifications.RaiseFor(c,tx,users,"advance:"+r.S("id")+":"+r.N("revision")+":"+step,title,Label(r),Me(actor),Kind,r.S("id"),action);}

    public static JsonObject Create(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,"advances.request");
        string iqama=DirectoryProfiles.Iqama(input.Required("riderIqama"));var rider=Store.Get(c,iqama,"rider",tx)??throw new ApiError("المندوب غير موجود.");
        J.Need(OrgDirectory.Supervises(c,tx,Me(actor),iqama),"المندوب ليس تحت إشرافك حسب نظام الرتب.",403);
        J.Need(!Store.List(c,Kind,tx).Any(r=>r.S("riderIqama")==iqama&&!Final.Contains(r.S("status"))),"للمندوب طلب سلفة مفتوح بالفعل.",409);
        string type=Type(input.Required("type"));string fulfilment=Fulfilment(type,input.S("fulfilment"));
        (string Network,string Package,decimal Price)? card=fulfilment=="recharge_card"?Card(c,tx,input):null;
        decimal amount=card?.Price??Amount(input,"amount");J.Need(card==null||input["amount"] is null||Debts.Amount(input,"amount")==card.Value.Price,"مبلغ كارت الشحن = سعر الباقة.");
        J.Need(input.S("reason").Trim().Length is >2 and <=500,"اكتب سبب السلفة.");
        var sponsor=FinancialCore.FindSponsor(rider.S("financeSponsorId"))??FinancialCore.FindSponsor(rider.S("sponsorId"))??FinancialCore.FindSponsor(rider.S("sponsor"))??throw new ApiError("سجل كفالة المندوب غير محدد.");
        string vault=Vault(type,sponsor.Id,input.S("vaultCode"),fulfilment);
        var reviewers=OrgDirectory.Responsible(c,tx,Review);J.Need(reviewers.Count>0,"لا يوجد محاسب مكلّف بمراجعة السلف؛ راجع التوجيه في الأقسام والرتب.",409);
        var r=new JsonObject{["id"]="adv:"+Guid.NewGuid().ToString("N"),["riderIqama"]=iqama,["riderName"]=rider.S("name"),["companyId"]=sponsor.Id,["type"]=type,["amount"]=amount,["vaultCode"]=vault,["reason"]=input.S("reason").Trim(),
            ["fulfilment"]=fulfilment,["odooVoucherNo"]=input.S("odooVoucherNo"),["networkCode"]=card?.Network,["packageCode"]=card?.Package,
            ["status"]="under_review",["revision"]=1,["requestedBy"]=Me(actor),["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["decisions"]=new JsonArray()};
        Decision(r,actor,"submitted",r.S("reason"));Save(c,tx,r,actor,"advance.requested");
        Notify(c,tx,r,Users(reviewers),"review","طلب سلفة جديد بانتظار مراجعتك",actor,true);return r;
    }

    public static JsonObject Summary(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject r)
    {
        if(r.S("type")=="internet")FinancialScopes.DemandSponsor(actor,r.S("companyId"));
        string iqama=r.S("riderIqama"),period=Period();
        var debts=Store.List(c,"debt",tx).Where(d=>d.S("iqama")==iqama&&d.S("status") is "active" or "pending_approval").ToList();
        decimal open=J.Money(debts.Sum(d=>d.N("remainingBalance")));
        decimal planned=J.Money(debts.Where(d=>d.S("status")=="active"&&string.CompareOrdinal(d.S("effectiveFrom"),period)<=0).Sum(d=>Math.Min(d.N("monthlyInstallment"),d.N("remainingBalance"))));
        // Last payroll settlement: the latest approved legacy run or approved pay cycle.
        string lastRun=Store.List(c,"run",tx).Where(x=>x.S("iqama")==iqama&&x.S("approvedAt")!="").Select(x=>x.S("approvedAt")).DefaultIfEmpty("").Max()??"";
        var riderCycles=Store.List(c,PayCycles.LineKind,tx).Where(l=>l.S("iqama")==iqama).Select(l=>l.S("cycleId")).ToHashSet();
        string lastCycle=Store.List(c,PayCycles.Kind,tx).Where(x=>x.S("status")=="approved"&&riderCycles.Contains(x.S("id"))).Select(x=>x.S("approvedAt",x.S("updatedAt",x.S("createdAt")))).DefaultIfEmpty("").Max()??"";
        string last=string.CompareOrdinal(lastRun,lastCycle)>=0?lastRun:lastCycle;
        var since=Store.List(c,"debt",tx).Where(d=>d.S("iqama")==iqama&&d.S("category") is "Advance" or "CashAdvance" or "BankAdvance"&&string.CompareOrdinal(d.S("createdAt"),last)>0)
            .Select(d=>new JsonObject{["debtId"]=d.S("id"),["category"]=d.S("category"),["amount"]=d.N("totalAmount"),["remaining"]=d.N("remainingBalance"),["status"]=d.S("status"),["createdAt"]=d.S("createdAt")}).ToList();
        decimal? expected=null;JsonObject statement=new(){["available"]=false};
        try{var s=PayStatements.Build(c,iqama,period,actor,false,tx);expected=s["net"] is JsonValue nv&&nv.TryGetValue<decimal>(out var net)?net:null;statement=new(){["available"]=expected!=null,["cards"]=s["cards"]?.DeepClone(),["net"]=expected};}
        catch(ApiError){statement["reason"]="لا يوجد كشف لهذا الشهر بعد";}
        decimal orders=Store.List(c,"work",tx).Where(w=>w.S("iqama")==iqama&&w.S("period")==period).Sum(w=>w.N("orders"));
        decimal? forecast=expected==null?null:J.Money(expected.Value-planned);
        string other=Fulfilment(r)=="cash"?"bank":"cash";string otherVault="";try{otherVault=Vault(r.S("type")=="internet"?"internet":other,r.S("companyId"),"",other);}catch(ApiError){}
        return new(){["requestId"]=r.S("id"),["period"]=period,["rider"]=new JsonObject{["iqama"]=iqama,["name"]=r.S("riderName"),["companyId"]=r.S("companyId")},
            ["ordersThisMonth"]=orders,["statement"]=statement,["openDebtBalance"]=open,["openDebts"]=debts.Count,["plannedInstallments"]=planned,
            ["lastPayrollSettlementAt"]=last,["advancesSinceLastSettlement"]=new JsonArray(since.Select(x=>(JsonNode)x).ToArray()),
            ["forecastNet"]=forecast,["amount"]=r.N("amount"),["covered"]=forecast!=null&&forecast>=r.N("amount"),["coverageKnown"]=forecast!=null,
            ["fulfilment"]=Fulfilment(r),["networkCode"]=r.S("networkCode"),["packageCode"]=r.S("packageCode"),
            ["vaults"]=new JsonArray(new JsonObject{["type"]=Fulfilment(r),["code"]=r.S("vaultCode"),["balance"]=r.S("vaultCode")==Mada?0m:AccountingPolicy.Balance(c,tx,r.S("vaultCode")),["selected"]=true},
                otherVault==""?new JsonObject{["type"]=other,["code"]="",["balance"]=0m,["selected"]=false}:new JsonObject{["type"]=other,["code"]=otherVault,["balance"]=AccountingPolicy.Balance(c,tx,otherVault),["selected"]=false})};
    }

    public static JsonObject ReviewRequest(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        AccessPolicy.Demand(actor,"advances.review");var r=Get(c,tx,id);J.Need(r.S("status")=="under_review","الطلب ليس بانتظار المراجعة.",409);
        if(input.ContainsKey("revision"))J.Need((int)input.N("revision")==(int)r.N("revision"),"تغير الطلب؛ حدّث الصفحة.",409);
        J.Need(r.S("requestedBy")!=Me(actor),"صاحب الطلب لا يعتمده (فصل المسؤوليات).",403);
        var who=OrgDirectory.DemandResponsible(c,tx,actor,Review);string action=input.Required("action"),note=input.S("note").Trim();
        Notifications.CloseTasks(c,tx,Kind,id,Me(actor));
        if(action=="approve")
        {
            decimal installment=input.ContainsKey("monthlyInstallment")?Debts.Amount(input,"monthlyInstallment"):r.N("amount");J.Need(installment>0&&installment<=r.N("amount"),"القسط الشهري يجب أن يكون بين صفر ومبلغ السلفة.");
            var summary=Summary(c,tx,actor,r);string coverage=input.S("coverageReason").Trim();
            if(!summary.B("covered"))J.Need(coverage.Length is >4 and <=500,"الصافي المتوقع لا يغطي السلفة أو غير معروف؛ اكتب سبب الاعتماد.");
            var custodians=OrgDirectory.Responsible(c,tx,Payout(r.S("vaultCode")));J.Need(custodians.Count>0,"لا يوجد مسؤول صرف مكلّف لهذه الخزنة أو البنك.",409);
            r["monthlyInstallment"]=installment;r["coverageAtApproval"]=summary.B("covered");r["forecastAtApproval"]=summary["forecastNet"]?.DeepClone();r["coverageReason"]=coverage;r["approvedBy"]=Me(actor);r["approvedAt"]=DateTimeOffset.UtcNow.ToString("O");
            Decision(r,actor,"approved",note,who,new(){["monthlyInstallment"]=installment,["coverageReason"]=coverage});r["status"]="approved";Save(c,tx,r,actor,"advance.approved");
            Notify(c,tx,r,Users(custodians),"payout","سلفة معتمدة بانتظار الصرف",actor,true);Notify(c,tx,r,[r.S("requestedBy")],"approved","تم اعتماد طلب السلفة",actor,false);
        }
        else if(action=="reject")
        {
            J.Need(note.Length is >2 and <=500,"اكتب سبب الرفض.");Decision(r,actor,"rejected",note,who);r["status"]="rejected";Save(c,tx,r,actor,"advance.rejected");
            Notify(c,tx,r,[r.S("requestedBy")],"rejected","تم رفض طلب السلفة",actor,false);
        }
        else if(action=="propose")
        {
            J.Need(note.Length is >2 and <=500,"اكتب سبب اقتراح التعديل.");var p=input["proposed"] as JsonObject??throw new ApiError("حدد التعديل المقترح.");
            string type=p.ContainsKey("type")?Type(p.S("type")):r.S("type");decimal amount=p.ContainsKey("amount")?Amount(p,"amount"):r.N("amount");J.Need(type=="internet"==(r.S("type")=="internet"),"لا تحول سلفة الإنترنت لنوع آخر؛ ارفض وأنشئ طلبًا جديدًا.");string vault=Vault(type,r.S("companyId"),p.S("vaultCode",type==r.S("type")?r.S("vaultCode"):""),type=="internet"?Fulfilment(r):null);
            J.Need(type!=r.S("type")||amount!=r.N("amount")||vault!=r.S("vaultCode"),"الاقتراح لا يغير شيئًا.");
            var proposal=new JsonObject{["type"]=type,["amount"]=amount,["vaultCode"]=vault,["proposedBy"]=Me(actor)};
            Decision(r,actor,"proposed",note,who,new(){["proposal"]=proposal.DeepClone()});r["proposal"]=proposal;r["status"]="change_proposed";Save(c,tx,r,actor,"advance.change_proposed");
            Notify(c,tx,r,[r.S("requestedBy")],"proposal","المحاسب اقترح تعديل طلب السلفة — بانتظار ردك",actor,true);
        }
        else throw new ApiError("الإجراء approve أو reject أو propose.");
        return r;
    }

    public static JsonObject Respond(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        AccessPolicy.Demand(actor,"advances.request");var r=Get(c,tx,id);J.Need(r.S("status")=="change_proposed","لا يوجد اقتراح بانتظار ردك.",409);
        J.Need(r.S("requestedBy")==Me(actor)&&OrgDirectory.Supervises(c,tx,Me(actor),r.S("riderIqama")),"الرد لصاحب الطلب مع إشراف سارٍ على المندوب فقط.",403);string action=input.Required("action"),note=input.S("note").Trim();
        Notifications.CloseTasks(c,tx,Kind,id,Me(actor));
        if(action=="accept")
        {
            var p=r["proposal"]!.AsObject();Decision(r,actor,"accepted_proposal",note);r["type"]=p.S("type");r["amount"]=p.N("amount");r["vaultCode"]=p.S("vaultCode");r["revision"]=r.N("revision")+1;r.Remove("proposal");r["status"]="under_review";
            Save(c,tx,r,actor,"advance.proposal_accepted");Notify(c,tx,r,Users(OrgDirectory.Responsible(c,tx,Review)),"review","المشرف وافق على التعديل — الطلب بانتظار مراجعتك",actor,true);
        }
        else if(action=="reject"){Decision(r,actor,"rejected_proposal",note);r["status"]="cancelled";Save(c,tx,r,actor,"advance.proposal_rejected");
            Notify(c,tx,r,Users(OrgDirectory.Responsible(c,tx,Review)),"cancelled","المشرف رفض التعديل وأُلغي الطلب",actor,false);}
        else throw new ApiError("الإجراء accept أو reject.");
        return r;
    }

    public static JsonObject PayOut(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        AccessPolicy.Demand(actor,"advances.payout");var r=Get(c,tx,id);J.Need(r.S("status")=="approved","الطلب غير معتمد للصرف.",409);
        J.Need(r.S("requestedBy")!=Me(actor),"صاحب الطلب لا يصرفه.",403);var who=OrgDirectory.DemandResponsible(c,tx,actor,Payout(r.S("vaultCode",r.S("vault"))));
        if(r.S("type")=="internet")
        {
            var paid=MadaAccounting.PayInternet(c,tx,actor,r,input);Notifications.CloseTasks(c,tx,Kind,id,Me(actor));Decision(r,actor,"paid",input.S("note"),who,new(){["reference"]=input.S("reference")});
            r["status"]="paid";r["debtId"]=paid.S("id");r["journalEntryId"]=paid.S("journalEntryId");r["payoutVoucherNo"]=paid.S("payoutVoucherNo");r["paidBy"]=Me(actor);r["paidAt"]=DateTimeOffset.UtcNow.ToString("O");r["payoutReference"]=input.S("reference");Save(c,tx,r,actor,"advance.paid");Notify(c,tx,r,[r.S("requestedBy"),r.S("approvedBy")],"paid","تم صرف سلفة الإنترنت",actor,false);return r;
        }
        string reference=input.Required("reference").Trim();J.Need(reference.Length is >2 and <=120,"مرجع الصرف غير صالح.");
        string vault=r.S("vaultCode");decimal amount=r.N("amount");
        if(AccountingPolicy.Balance(c,tx,vault)<amount)throw new ApiError("رصيد الخزنة أو البنك غير كافٍ للصرف؛ الطلب يبقى معتمدًا.",409);
        var rider=Store.Get(c,r.S("riderIqama"),"rider",tx)??throw new ApiError("المندوب غير موجود.");string category=Category(r.S("type"));var meta=FinancialCore.ResolveCategory(category);
        var sponsor=FinancialCore.ResolveSponsor(r.S("companyId"));var vaultAcc=FinancialCore.ValidateVaultForOperation(vault,category,"disbursement");var origin=FinancialCore.ResolveSponsor(vaultAcc.DefaultSponsorId);
        var debt=new JsonObject{["id"]=Guid.NewGuid().ToString("N"),["iqama"]=r.S("riderIqama"),["riderName"]=rider.S("name"),["debtorType"]="Rider",["category"]=category,["platform"]="",["categoryAr"]=meta.NameAr,["sector"]=meta.Sector,
            ["expenseAccountCode"]=meta.ExpenseAccountCode,["receivableAccountCode"]=meta.ReceivableAccountCode,["allocationMode"]="RiderFull",["entryMode"]="Disbursement",["recognitionAccountCode"]="",["accountingVersion"]=AccountingEvents.Version,
            ["originalCreditorSponsorId"]=sponsor.Id,["totalCostAmount"]=amount,["companyShareAmount"]=0m,["totalAmount"]=amount,["paidAmount"]=0m,["waivedAmount"]=0m,["remainingBalance"]=amount,["monthlyInstallment"]=r.N("monthlyInstallment"),
            ["sponsorshipSponsorId"]=sponsor.Id,["sponsorshipSponsorNameAr"]=sponsor.ShortNameAr,["originSponsorId"]=origin.Id,["originSponsorNameAr"]=origin.ShortNameAr,["creditorSponsorId"]=sponsor.Id,["creditorSponsorNameAr"]=sponsor.ShortNameAr,
            ["paymentVaultCode"]=vault,["initiatingDepartment"]="OPS",["requireMutualApproval"]=false,["status"]="active",["notes"]="طلب سلفة "+r.S("id")+" — "+r.S("reason"),["sourceRef"]="ADV-REQ:"+r.S("id"),
            ["advanceRequestId"]=r.S("id"),["payoutOdooVoucherNo"]=input.S("odooVoucherNo"),["approvedBy"]=r.S("approvedBy"),["paidBy"]=Me(actor),["payoutReference"]=reference,
            ["effectiveFrom"]=Period(),["postingPeriod"]=Period(),["createdBy"]=Me(actor),["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        J.Need(!Store.List(c,"debt",tx).Any(d=>d.S("sourceRef")==debt.S("sourceRef")),"السلفة مصروفة من قبل.",409);
        Store.Save(c,"debt",debt,tx);Debts.PostCreationJournalIfNeeded(c,tx,debt,Me(actor));Store.Audit(c,tx,"debt.created",debt.S("id"),Me(actor));
        var saved=Store.Get(c,debt.S("id"),"debt",tx)!;
        Notifications.CloseTasks(c,tx,Kind,id,Me(actor));Decision(r,actor,"paid",input.S("note"),who,new(){["reference"]=reference});
        r["payoutVoucherNo"]=saved.S("payoutVoucherNo");
        r["status"]="paid";r["debtId"]=debt.S("id");r["journalEntryId"]=saved.S("journalEntryId");r["paidBy"]=Me(actor);r["paidAt"]=DateTimeOffset.UtcNow.ToString("O");r["payoutReference"]=reference;Save(c,tx,r,actor,"advance.paid");
        Notify(c,tx,r,[r.S("requestedBy"),r.S("approvedBy")],"paid","تم صرف السلفة",actor,false);return r;
    }

    public static bool CanSee(DbConnection c,ClaimsPrincipal user,JsonObject r)
    {
        if(AccessPolicy.Administrator(user))return true;
        if(AccessPolicy.Has(user,"advances.request")&&r.S("requestedBy")==Me(user)&&OrgDirectory.Supervises(c,null,Me(user),r.S("riderIqama")))return true;
        if(r.S("type")=="internet"&&!FinancialScopes.Sponsor(user,r.S("companyId")))return false;
        if(AccessPolicy.Has(user,"advances.review")&&OrgDirectory.Responsible(c,null,Review).Any(x=>x.S("userId")==Me(user)))return true;
        return AccessPolicy.Has(user,"advances.payout")&&OrgDirectory.Responsible(c,null,Payout(r.S("vaultCode",r.S("vault")))).Any(x=>x.S("userId")==Me(user));
    }
    public static JsonObject Read(DbConnection c,ClaimsPrincipal actor,string id)
    {var r=Get(c,null,id);J.Need(CanSee(c,actor,r),"طلب السلفة غير موجود.",404);return r;}
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/advance-requests",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Create(c,tx,ctx.User,input))).WithTags("Debts and advances").WithSummary("Supervisor requests a cash or bank advance for a supervised rider");
        api.MapGet("/advance-requests",(string? role,string? status,HttpContext ctx)=>store.Read(c=>
        {
            string me=Me(ctx.User);var rows=Store.List(c,Kind).Where(r=>CanSee(c,ctx.User,r));
            if(role=="mine")rows=rows.Where(r=>r.S("requestedBy")==me);
            else if(role=="review")rows=rows.Where(r=>OrgDirectory.Responsible(c,null,Review).Any(x=>x.S("userId")==me));
            else if(role=="payout")rows=rows.Where(r=>r.S("status")=="approved"&&OrgDirectory.Responsible(c,null,Payout(r.S("vaultCode",r.S("vault")))).Any(x=>x.S("userId")==me));
            if(!string.IsNullOrWhiteSpace(status))rows=rows.Where(r=>r.S("status")==status);
            return rows.OrderByDescending(r=>r.S("createdAt")).ToList();
        })).WithTags("Debts and advances").WithSummary("List advance requests visible to the caller (role=mine|review|payout)");
        api.MapGet("/advance-requests/{id}",(string id,HttpContext ctx)=>store.Read(c=>Read(c,ctx.User,id))).WithTags("Debts and advances").WithSummary("Read one advance request with its decision history");
        api.MapGet("/advance-requests/{id}/summary",(string id,HttpContext ctx)=>store.Read(c=>
        {AccessPolicy.Demand(ctx.User,"advances.review");var r=Get(c,null,id);J.Need(AccessPolicy.Administrator(ctx.User)||OrgDirectory.Responsible(c,null,Review).Any(x=>x.S("userId")==Me(ctx.User)),"ملخص المندوب للمحاسب المكلّف فقط.",403);return Summary(c,null,ctx.User,r);}))
            .WithTags("Debts and advances").WithSummary("Rider performance, debt and forecast summary plus vault balances for the reviewer");
        api.MapPost("/advance-requests/{id}/review",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>ReviewRequest(c,tx,ctx.User,id,input))).WithTags("Debts and advances").WithSummary("Responsible accountant approves, rejects or proposes a change");
        api.MapPost("/advance-requests/{id}/respond",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Respond(c,tx,ctx.User,id,input))).WithTags("Debts and advances").WithSummary("Supervisor accepts or rejects the accountant's proposal");
        api.MapPost("/advance-requests/{id}/payout",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>PayOut(c,tx,ctx.User,id,input))).WithTags("Debts and advances").WithSummary("Vault custodian pays out the approved advance; creates the debt and journal");
    }
}
