using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>BR §37 Jahez reconciliation: collections + payments on behalf vs summary invoice through last-transaction date; VAT as its own line; proposed journals; maker-checker.</summary>
public static class JahezReconciliation
{
    public const string Kind="jahez_reconciliation";
    public const string Receivable="112801",Clearing="212601",SettlementBank="111204",OutputVat="213201";
    static void DemandWrite(ClaimsPrincipal actor)=>J.Need(AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"treasury.transfer")||AccessPolicy.Has(actor,"treasury.jahez.receive"),"صلاحية خزينة جاهز مطلوبة.",403);
    static void RefuseDuplicateActive(DbConnection c,DbTransaction tx,string companyId,string period,string? exceptId=null)
    {
        bool clash=Store.List(c,Kind,tx).Any(d=>d.S("companyId")==companyId&&d.S("period")==period&&(d.S("status") is "draft" or "pending_approval" or "approved")&&(exceptId==null||d.S("id")!=exceptId));
        J.Need(!clash,"تقرير مطابقة جاهز لنفس الشركة والشهر موجود؛ أغلقه أو اعتمد واحدًا فقط.",409);
    }
    public static JsonObject Build(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        DemandWrite(actor);PayValues.Fields(input,"companyId","period","invoiceNet","invoiceVat","lastTransactionDate","invoiceReference","notes");
        var sponsor=FinancialCore.ResolveSponsor(input.Required("companyId"));FinancialScopes.DemandSponsor(actor,sponsor.Id);
        string period=J.Period(input.Required("period"));string lastRaw=input.Required("lastTransactionDate");
        J.Need(DateOnly.TryParseExact(lastRaw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var last),"تاريخ آخر معاملة غير صالح.");
        var monthStart=DateOnly.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);J.Need(last>=monthStart&&last<monthStart.AddMonths(1),"آخر معاملة داخل شهر الفاتورة.");
        RefuseDuplicateActive(c,tx,sponsor.Id,period);
        decimal invoiceNet=J.Money(PayValues.Number(input,"invoiceNet",-1000000000)),invoiceVat=J.Money(PayValues.Number(input,"invoiceVat"));
        J.Need(invoiceVat>=0,"ضريبة الفاتورة غير سالبة.");
        decimal deliveryFees=0,cashCollections=0,driverAdjustments=0,bonusesTips=0,paidOnBehalf=0,commission=0;
        var details=new JsonArray();
        foreach(var row in Store.List(c,"jahez_transaction",tx).Where(r=>InScope(c,tx,r,sponsor.Id)&&InRange(r.S("occurredAt"),monthStart,last)))
        {
            decimal delivery=row.N("deliveryPrice"),cash=row.N("cashAmount"),adj=row.N("driverAdjustment");
            deliveryFees=J.Money(deliveryFees+delivery);cashCollections=J.Money(cashCollections+cash);driverAdjustments=J.Money(driverAdjustments+adj);
            details.Add(Line("transaction",row.S("id"),row.S("riderId"),row.S("occurredAt"),delivery,cash,adj,0,0));
        }
        foreach(var row in Store.List(c,"jahez_earnings_statement",tx).Where(r=>InScope(c,tx,r,sponsor.Id)&&StatementInRange(r,monthStart,last)))
        {
            decimal tips=row.N("tips")+row.N("bonuses");bonusesTips=J.Money(bonusesTips+tips);
            details.Add(Line("earnings",row.S("id"),row.S("riderId"),row.S("to",row.S("from")),0,0,0,tips,0));
        }
        foreach(var row in Store.List(c,"erp_money_movement",tx).Where(r=>InScope(c,tx,r,sponsor.Id)&&InRange(r.S("occurredAt"),monthStart,last)))
        {
            // Payments the company made on behalf of Jahez (direction/purpose tagged in ERP).
            if(row.S("directionCode") is "2" or "Out" or "out"||row.S("purposeCode").Contains("Behalf",StringComparison.OrdinalIgnoreCase)||row.S("purposeCode").Contains("OnBehalf",StringComparison.OrdinalIgnoreCase)||row.N("dashboardDebtPaid")>0)
            {
                decimal amount=row.N("amount")+row.N("dashboardDebtPaid");paidOnBehalf=J.Money(paidOnBehalf+amount);
                details.Add(Line("money_movement",row.S("id"),row.S("riderId"),row.S("occurredAt"),0,0,0,0,amount));
            }
        }
        foreach(var row in Store.List(c,"erp_jahez_operation",tx).Where(r=>InScope(c,tx,r,sponsor.Id)&&InRange(r.S("executedAt"),monthStart,last)))
        {
            decimal fee=row.N("commission");if(fee==0)fee=row.N("calculatedCommission");commission=J.Money(commission+fee);
            details.Add(Line("operation_commission",row.S("id"),row.S("riderId"),row.S("executedAt"),0,0,0,0,0,fee));
        }
        // BR §37: company due − collections + paid on behalf = expected transfer net.
        decimal companyDue=J.Money(deliveryFees+bonusesTips+commission+driverAdjustments);
        decimal expectedNet=J.Money(companyDue-cashCollections+paidOnBehalf);
        decimal variance=J.Money(expectedNet-invoiceNet);
        var proposed=new JsonArray
        {
            Journal("clearing","مقاصة جاهز",Clearing,Receivable,Math.Min(Math.Max(0,cashCollections),Math.Max(0,companyDue))),
            Journal("transfer","تحويل تسوية جاهز",SettlementBank,Receivable,Math.Max(0,expectedNet)),
            Journal("vat","إثبات ضريبة مخرجات جاهز",Receivable,OutputVat,invoiceVat)
        };
        var doc=new JsonObject
        {
            ["id"]="jahez-recon:"+Guid.NewGuid().ToString("N"),["kind"]=Kind,["companyId"]=sponsor.Id,["period"]=period,
            ["invoiceReference"]=input.S("invoiceReference"),["lastTransactionDate"]=last.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
            ["invoiceNet"]=invoiceNet,["invoiceVat"]=invoiceVat,["notes"]=input.S("notes"),
            ["totals"]=new JsonObject{["deliveryFees"]=deliveryFees,["cashCollections"]=cashCollections,["driverAdjustments"]=driverAdjustments,["bonusesTips"]=bonusesTips,["paidOnBehalf"]=paidOnBehalf,["commission"]=commission,["companyDue"]=companyDue,["expectedNet"]=expectedNet,["variance"]=variance},
            ["details"]=details,["proposedJournals"]=proposed,
            ["status"]="draft",["createdBy"]=actor.Identity!.Name,["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),
            ["submittedBy"]="",["submittedAt"]="",["approvedBy"]="",["approvedAt"]="",["journalEntryIds"]=new JsonArray()
        };
        Store.Save(c,Kind,doc,tx);Store.Audit(c,tx,"jahez.reconciliation_built",doc.S("id"),actor.Identity!.Name!);return doc;
    }
    public static JsonObject Submit(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        DemandWrite(actor);var doc=Store.Get(c,id,Kind,tx)??throw new ApiError("تقرير المطابقة غير موجود.",404);
        FinancialScopes.DemandSponsor(actor,doc.S("companyId"));J.Need(doc.S("status")=="draft","التقرير ليس مسودة.");
        doc["status"]="pending_approval";doc["submittedBy"]=actor.Identity!.Name;doc["submittedAt"]=DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c,Kind,doc,tx);Store.Audit(c,tx,"jahez.reconciliation_submitted",id,actor.Identity!.Name!);return doc;
    }
    public static JsonObject DecideVariance(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        DemandWrite(actor);var doc=Store.Get(c,id,Kind,tx)??throw new ApiError("تقرير المطابقة غير موجود.",404);
        FinancialScopes.DemandSponsor(actor,doc.S("companyId"));J.Need(doc.S("status") is "draft" or "pending_approval","التقرير مغلق.");
        string decision=input.S("varianceDecision").Trim();J.Need(decision.Length is >=3 and <=500,"قرار الفرق من 3 إلى 500 حرف.");
        doc["varianceDecision"]=decision;doc["varianceDecidedBy"]=actor.Identity!.Name;doc["varianceDecidedAt"]=DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c,Kind,doc,tx);Store.Audit(c,tx,"jahez.reconciliation_variance_decided",id,actor.Identity!.Name!);return doc;
    }
    public static JsonObject Approve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        DemandWrite(actor);var doc=Store.Get(c,id,Kind,tx)??throw new ApiError("تقرير المطابقة غير موجود.",404);
        FinancialScopes.DemandSponsor(actor,doc.S("companyId"));J.Need(doc.S("status")=="pending_approval","التقرير بانتظار الاعتماد.");
        RefuseDuplicateActive(c,tx,doc.S("companyId"),doc.S("period"),doc.S("id"));
        // BR §37: approver must be Independent of both submitter and creator (§35 bypass still applies via Independent).
        AccessPolicy.ValidateApproval(actor,new(){["requestedBy"]=doc.S("submittedBy"),["targetDepartment"]="FIN",["requiredRankLevel"]=1},new JsonObject(),"treasury.jahez.receive",c,tx,"jahez.reconciliation_approved",doc);
        J.Need(AccessPolicy.Independent(actor,doc.S("createdBy"),"jahez.reconciliation_approved.createdBy",c,tx,doc),"لا يجوز اعتماد تقرير أنشأته؛ يلزم مراجع مستقل أو صلاحية تجاوز مؤرخة.",403);
        decimal variance=doc["totals"]!.AsObject().N("variance");
        J.Need(variance==0||doc.S("varianceDecision").Length>=3,"فرق المطابقة يحتاج قرارًا مكتوبًا قبل الاعتماد.");
        var ids=new JsonArray();string period=doc.S("period"),company=doc.S("companyId"),actorName=actor.Identity!.Name!;
        foreach(var j in doc["proposedJournals"]!.AsArray().OfType<JsonObject>().Where(j=>j.N("amount")>0))
        {
            // VAT posts with named accounts (BR §37). Clearing/transfer use current chart.
            string debit=j.S("debit"),credit=j.S("credit");decimal amount=j.N("amount");
            var lines=new JsonArray(
                FinancialCore.CreateJournalLine(debit,company,amount,0,j.S("labelAr"),targetChart:true,payChart:true),
                FinancialCore.CreateJournalLine(credit,company,0,amount,j.S("labelAr"),targetChart:true,payChart:true));
            var journal=FinancialCore.PostJournal(c,tx,"jahez_reconciliation",j.S("labelAr"),period,doc.S("id")+":"+j.S("code"),lines,actorName,"","",company,company,targetChart:true,payChart:true);
            ids.Add(journal.S("id"));
        }
        doc["status"]="approved";doc["approvedBy"]=actorName;doc["approvedAt"]=DateTimeOffset.UtcNow.ToString("O");doc["journalEntryIds"]=ids;
        Store.Save(c,Kind,doc,tx);Store.Audit(c,tx,"jahez.reconciliation_approved",id,actorName);return doc;
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/finance/jahez-reconciliations",(HttpContext ctx)=>
        {
            AccessPolicy.Demand(ctx.User,"treasury.view");
            return store.Read(c=>Store.List(c,Kind).Where(d=>FinancialScopes.Sponsor(ctx.User,d.S("companyId"))).OrderByDescending(d=>d.S("createdAt")));
        }).WithTags("Banks and treasury").WithSummary("List Jahez reconciliation reports");
        api.MapGet("/finance/jahez-reconciliations/{id}",(string id,HttpContext ctx)=>store.Read(c=>
        {
            AccessPolicy.Demand(ctx.User,"treasury.view");var doc=Store.Get(c,id,Kind)??throw new ApiError("تقرير المطابقة غير موجود.",404);
            FinancialScopes.DemandSponsor(ctx.User,doc.S("companyId"));return doc;
        })).WithTags("Banks and treasury").WithSummary("Read one Jahez reconciliation report");
        api.MapPost("/finance/jahez-reconciliations",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Build(c,tx,ctx.User,input))).WithTags("Banks and treasury").WithSummary("Build Jahez reconciliation report §37");
        api.MapPost("/finance/jahez-reconciliations/{id}/submit",(string id,HttpContext ctx)=>store.Write((c,tx)=>Submit(c,tx,ctx.User,id))).WithTags("Banks and treasury").WithSummary("Submit Jahez reconciliation for approval");
        api.MapPost("/finance/jahez-reconciliations/{id}/variance",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>DecideVariance(c,tx,ctx.User,id,input))).WithTags("Banks and treasury").WithSummary("Record variance decision for Jahez reconciliation");
        api.MapPost("/finance/jahez-reconciliations/{id}/approve",(string id,HttpContext ctx)=>store.Write((c,tx)=>Approve(c,tx,ctx.User,id))).WithTags("Banks and treasury").WithSummary("Approve Jahez reconciliation (maker-checker)");
    }
    static bool InScope(DbConnection c,DbTransaction tx,JsonObject row,string companyId)
    {
        if(row.S("companyId")==companyId)return true;
        string account=row.S("accountId",row.S("platformAccountId"));if(account=="")return false;
        var acc=Store.Get(c,account,"platform_account",tx);return acc!=null&&acc.S("companyId")==companyId;
    }
    static bool InRange(string occurredAt,DateOnly first,DateOnly last)
    {
        if(occurredAt=="")return false;
        if(DateTimeOffset.TryParse(occurredAt,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var at)){var timestampDay=DateOnly.FromDateTime(at.DateTime);return timestampDay>=first&&timestampDay<=last;}
        if(DateOnly.TryParse(occurredAt,CultureInfo.InvariantCulture,DateTimeStyles.None,out var dateOnly))return dateOnly>=first&&dateOnly<=last;
        return false;
    }
    static bool StatementInRange(JsonObject row,DateOnly first,DateOnly last)
    {
        string from=row.S("from"),to=row.S("to",from);return DateOnly.TryParse(from,CultureInfo.InvariantCulture,DateTimeStyles.None,out var start)&&DateOnly.TryParse(to,CultureInfo.InvariantCulture,DateTimeStyles.None,out var end)&&start>=first&&end<=last;
    }
    static JsonObject Line(string kind,string sourceId,string riderId,string at,decimal delivery,decimal cash,decimal adj,decimal tips,decimal paid,decimal commission=0)=>
        new(){["kind"]=kind,["sourceId"]=sourceId,["riderId"]=riderId,["at"]=at,["deliveryFees"]=J.Money(delivery),["cashCollections"]=J.Money(cash),["driverAdjustments"]=J.Money(adj),["bonusesTips"]=J.Money(tips),["paidOnBehalf"]=J.Money(paid),["commission"]=J.Money(commission)};
    static JsonObject Journal(string code,string label,string debit,string credit,decimal amount)=>
        new(){["code"]=code,["labelAr"]=label,["debit"]=debit,["credit"]=credit,["amount"]=J.Money(Math.Max(0,amount))};
}
