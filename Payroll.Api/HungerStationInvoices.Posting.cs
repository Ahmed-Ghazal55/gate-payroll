using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class HungerStationInvoices
{
    const string ActionKind="platform_invoice_action";
    const string PostingPreview="hs-invoice-posting";
    static JsonObject? LatestFacts(DbConnection c,DbTransaction? tx,string id)=>Store.List(c,ActionKind,tx).Where(a=>a.S("invoiceId")==id&&a.S("action")=="facts_revision").OrderByDescending(a=>a.N("revision")).FirstOrDefault();
    public static List<JsonObject> EffectiveLines(DbConnection c,DbTransaction? tx,string id)
    {
        var lines=Store.List(c,"platform_invoice_line",tx).Where(l=>l.S("invoiceId")==id).Select(l=>l.Clone()).ToList();var revisions=Store.List(c,ActionKind,tx).Where(a=>a.S("invoiceId")==id&&a.S("action")=="facts_revision").OrderBy(a=>a.N("revision"));
        foreach(var revision in revisions)foreach(var patch in revision["lineFacts"]!.AsArray().OfType<JsonObject>())
        {var line=lines.Single(l=>l.S("id")==patch.S("lineId"));foreach(var field in patch.Where(p=>p.Key!="lineId"))line[field.Key]=field.Value?.DeepClone();}
        return lines;
    }
    // BR §23.2 exception: a CAR claim the platform rejected stays open but lets the invoice post with W held in 112803.
    static bool RejectedCar(JsonObject d)=>d.S("category")=="CAR"&&d.S("status")=="escalated"&&d.S("outcome")=="platform_rejected"&&d.S("rejectionRecordedBy")!=""&&d.S("platformReference")!="";
    static bool CreditedCar(JsonObject d)=>d.S("category")=="CAR"&&d.S("status")=="resolved"&&d.S("outcome")=="platform_credit"&&d.S("resolvedBy")!=""&&d.S("platformReference")!="";
    static bool EscalatedCar(DbConnection c,DbTransaction? tx,string id)=>Store.List(c,"platform_dispute",tx).Any(d=>d.S("invoiceId")==id&&RejectedCar(d));
    static bool HasUnresolvedDispute(DbConnection c,DbTransaction? tx,string id)
    {
        var disputes=Store.List(c,"platform_dispute",tx).Where(d=>d.S("invoiceId")==id).ToList();
        if(disputes.Any(d=>d.S("status")!="resolved"&&!RejectedCar(d)))return true;
        // A nonzero CAR source needs a platform credit or a recorded platform rejection, even if a claim is missing.
        return Store.List(c,"platform_invoice_line",tx).Where(l=>l.S("invoiceId")==id&&l.S("lineType")=="company"&&l.N("carDeduction")!=0)
            .Any(l=>!disputes.Any(d=>d.S("lineId")==l.S("id")&&(CreditedCar(d)||RejectedCar(d))));
    }
    public static void DemandResolvedDisputes(DbConnection c,DbTransaction? tx,string id)
    {if(HasUnresolvedDispute(c,tx,id))throw new ApiError("ترحيل الفاتورة متوقف بالكامل حتى حسم الاعتراض؛ فتح بوابة الترحيل لا يتجاوز الاعتراض.",409);}
    static bool Closed(DbConnection c,DbTransaction? tx,string company,string period)=>Store.List(c,"accounting_period",tx).Any(p=>p.S("companyId")==company&&p.S("period")==period&&p.S("status")=="closed");
    static void Independent(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject facts,string makerField="createdBy")
    {var request=new JsonObject{["requestedBy"]=facts.S(makerField),["targetDepartment"]="FIN",["requiredRankLevel"]=1};AccessPolicy.ValidateApproval(actor,request,new JsonObject(),"platforms.hs.approve",c,tx,"platform.hs.approve",facts);if(facts.B("selfApprovedException"))Store.Save(c,facts.S("action")!=""?ActionKind:facts.S("invoiceId")!=""?"platform_dispute":"platform_invoice",facts,tx);}
    static JsonObject PostingRow(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        var invoice=GetInvoice(c,tx,actor,id);var all=EffectiveLines(c,tx,id);var work=all.Where(l=>l.S("lineType")=="rider").ToList();var blockers=new List<string>();var lines=new JsonArray();string company=invoice.S("companyId"),period=invoice.S("postingPeriod",invoice.S("period"));
        if(invoice.S("reversalJournalEntryId")!="")blockers.Add("invoice_reversed");if(Closed(c,tx,company,period))blockers.Add("accounting_period_closed");
        if(work.Count==0)blockers.Add("missing_rider_facts");if(work.Any(l=>l.S("scheme")=="target"))blockers.Add("target_scheme_undefined");if(work.Any(l=>l.S("scheme") is not ("salary" or "commission" or "target")))blockers.Add("unknown_scheme");
        if(invoice.N("amountExclVat")<0||invoice.N("vat")<0)blockers.Add("negative_invoice_requires_reversal");
        bool mixed=work.Select(l=>l.S("scheme")).Distinct().Count()>1;decimal commission=0,salary=0;
        foreach(var line in work.Where(l=>l.S("scheme")=="commission"))
        {
            if(!line.ContainsKey("riderNet")||!line.ContainsKey("companyCommission")){blockers.Add("missing_commission_split");continue;}
            if(line.N("riderNet")<0||line.N("companyCommission")<0){blockers.Add("negative_split_requires_cma_or_reversal");continue;}
            commission+=line.N("riderNet")+line.N("companyCommission");
            if(line.N("riderNet")>0&&(line.S("riderId")==""||Store.Get(c,line.S("riderId"),"rider",tx) is not {} rider||!ErpRiderClassification.ListedAsRider(rider)))blockers.Add("actual_rider_required");
            if(mixed&&(!line.ContainsKey("platformAmountExclVat")||line.N("platformAmountExclVat")!=line.N("riderNet")+line.N("companyCommission")))blockers.Add("mixed_scheme_gross_required");
        }
        var salaries=work.Where(l=>l.S("scheme")=="salary").ToList();
        if(mixed){if(salaries.Any(l=>!l.ContainsKey("platformAmountExclVat")||l.N("platformAmountExclVat")<0))blockers.Add("mixed_scheme_gross_required");salary=salaries.Sum(l=>l.N("platformAmountExclVat"));}
        else if(salaries.Count>0)salary=invoice.N("amountExclVat");
        if(salary+commission!=invoice.N("amountExclVat"))blockers.Add("approved_splits_do_not_reconcile");
        var allocations=Store.List(c,"platform_allocation",tx).Where(a=>a.S("invoiceId")==id&&a.S("status")=="approved").ToList();
        foreach(var line in all.Where(l=>l.N("tgaAmount")!=0))
            if(!allocations.Any(a=>a.S("lineId")==line.S("id")&&a.N("amount")==Math.Abs(line.N("tgaAmount"))))blockers.Add("tga_pending_allocation");
        decimal disputed=all.Where(l=>l.S("lineType")=="company").Sum(l=>Math.Abs(l.N("carDeduction")));bool openDispute=HasUnresolvedDispute(c,tx,id),enabled=PlatformPosting.Enabled(c,"hs",tx);
        void Add(string code,decimal debit,decimal credit,string note,string rider="")
        {if(debit==0&&credit==0)return;var line=FinancialCore.CreateJournalLine(code,company,debit,credit,note,rider,targetChart:true);line["platform"]="HungerStation";lines.Add(line);}
        if(blockers.Count==0)
        {
            Add(PayPlatforms.For("hs").Receivable,invoice.N("amountExclVat")+invoice.N("vat"),0,"فاتورة الخدمة والضريبة");Add("411102",0,salary,"إيراد نظام الراتب");
            foreach(var row in work.Where(l=>l.S("scheme")=="commission")){Add("212103",0,row.N("riderNet"),"مستحق المندوب المعتمد",row.S("riderId"));Add("411601",0,row.N("companyCommission"),"عمولة هنقر المعتمدة");}
            Add("213201",0,invoice.N("vat"),"ضريبة الفاتورة");Add("112803",disputed,0,"مبلغ CAR في حساب النزاعات دون تحميل أي طرف");Add(PayPlatforms.For("hs").Receivable,0,disputed,"فصل المبلغ المتنازع عليه");
            foreach(var allocation in allocations)
            {
                foreach(var share in allocation["riderShares"]!.AsArray().OfType<JsonObject>())Add("112502",share.N("amount"),0,"نصيب المندوب المعتمد من TGA",share.S("riderId"));
                Add("526402",allocation.N("companyAmount"),0,"نصيب الشركة المعتمد من TGA");Add(PayPlatforms.For("hs").Receivable,0,allocation.N("amount"),"TGA المخصوم من المنصة");
            }
            if(lines.Count==0)blockers.Add("no_postable_amount");
        }
        return new(){["invoiceId"]=id,["companyId"]=company,["period"]=period,["lines"]=lines,["blockers"]=new JsonArray(blockers.Distinct().Select(b=>(JsonNode?)JsonValue.Create(b)).ToArray()),["readyForApproval"]=blockers.Count==0,["postingEnabled"]=enabled,["postingBlockedByDispute"]=openDispute,["postingAllowed"]=enabled&&!openDispute&&blockers.Count==0,["postingBlockers"]=new JsonArray((!enabled?new[]{"posting_gate_disabled"}:Array.Empty<string>()).Concat(openDispute?new[]{"unresolved_invoice_dispute"}:Array.Empty<string>()).Select(b=>(JsonNode?)JsonValue.Create(b)).ToArray()),["disputedAmount"]=disputed};
    }
    public static JsonObject Proposal(DbConnection c,ClaimsPrincipal actor,string id)
    {Scope(actor,"platforms.hs.view");var rows=new JsonArray(PostingRow(c,null,actor,id));return new(){["rows"]=rows,["fingerprint"]=FinancialImports.Issue(c,PostingPreview,rows,actor.Identity!.Name!)};}
    public static JsonObject ApproveInvoice(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        Scope(actor,"platforms.hs.approve");var invoice=GetInvoice(c,tx,actor,id).Clone();Independent(c,tx,actor,invoice);var revision=LatestFacts(c,tx,id);foreach(var facts in Store.List(c,ActionKind,tx).Where(a=>a.S("invoiceId")==id&&a.S("action")=="facts_revision"))Independent(c,tx,actor,facts,"requestedBy");
        if(invoice.S("reversalJournalEntryId")!="")throw new ApiError("الفاتورة معكوسة؛ استخدم نسخة مصححة مستقلة.",409);
        if(invoice.S("approvalStatus")=="approved"&&(invoice.S("journalEntryId")!=""||!PlatformPosting.Enabled(c,"hs",tx)))return invoice;
        FinancialImports.Verify(c,tx,input,PostingPreview,actor.Identity!.Name!);J.Need(input["rows"] is JsonArray {Count:1}&&input["rows"]![0].S("invoiceId")==id,"المعاينة لا تخص الفاتورة.");var proposal=PostingRow(c,tx,actor,id);
        if(!proposal.B("readyForApproval"))throw new ApiError(proposal["blockers"]!.AsArray().Any(x=>x!.ToString()=="target_scheme_undefined")?"نظام التارجت موقوف حتى يحدد غزال التسلسل المعتمد.":"الوقائع أو توزيع TGA أو الفترة غير مكتملة للاعتماد.",409);
        if(proposal.B("postingEnabled"))DemandResolvedDisputes(c,tx,id);
        bool first=invoice.S("approvalStatus")!="approved";
        if(first){invoice["approvalStatus"]="approved";invoice["approvedBy"]=actor.Identity!.Name;invoice["approvedAt"]=DateTimeOffset.UtcNow.ToString("O");}
        bool hold=HasUnresolvedDispute(c,tx,id);invoice["settlementHold"]=hold||EscalatedCar(c,tx,id);invoice["status"]=hold?"disputed":"approved";
        if(proposal.B("postingEnabled"))
        {var journal=PlatformPosting.Post(c,tx,proposal.S("period"),"HS-INVOICE:"+id,proposal["lines"]!.DeepClone().AsArray(),actor.Identity!.Name!);invoice["journalEntryId"]=journal.S("id");invoice["accountingStatus"]="posted";invoice["postedBy"]=actor.Identity!.Name;
            foreach(var claim in Store.List(c,"platform_dispute",tx).Where(d=>d.S("invoiceId")==id&&d.S("status")=="resolved"&&d.S("outcome")=="platform_credit"))
            {var pending=claim.Clone();pending["accountingReconciliation"]="pending_reversal_or_credit_note";Store.Save(c,"platform_dispute",pending,tx);Store.Audit(c,tx,"platform.hs.credit_reconciliation_pending",pending.S("id"),actor.Identity!.Name!);invoice["settlementHold"]=true;}
        }
        else invoice["accountingStatus"]="blocked_by_gate";
        Store.Save(c,"platform_invoice",invoice,tx);Store.Audit(c,tx,first?"platform.hs.invoice_approved":"platform.hs.invoice_posted",id,actor.Identity!.Name!);
        if(revision!=null&&revision.S("status")=="pending"){var approved=revision.Clone();approved["status"]="approved";approved["approvedBy"]=actor.Identity!.Name;Store.Save(c,ActionKind,approved,tx);Store.Audit(c,tx,"platform.hs.facts_approved",approved.S("id"),actor.Identity!.Name!);}
        if(invoice.S("replacesInvoiceId")!="")PlatformDeductions.ReverseSource(c,tx,actor,invoice.S("replacesInvoiceId"),invoice);
        PlatformDeductions.ConfirmInvoice(c,tx,actor,id);return invoice;
    }
    public static JsonObject ReviseFacts(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        Scope(actor,"platforms.hs.manage");var invoice=GetInvoice(c,tx,actor,id);J.Need(invoice.S("approvalStatus")!="approved"&&invoice.S("journalEntryId")=="","الوقائع المعتمدة لا تتعدل؛ اطلب عكسًا ونسخة جديدة.");J.Need(input.S("reason").Length is >0 and <=500,"سبب المراجعة مطلوب.");
        var patches=input["lineFacts"] as JsonArray??throw new ApiError("وقائع السطور مطلوبة.");J.Need(patches.Count is >0 and <=25000&&patches.All(p=>p is JsonObject),"وقائع غير صالحة.");var normalized=new JsonArray();var seen=new HashSet<string>();
        foreach(var patch in patches.OfType<JsonObject>())
        {
            var line=GetLine(c,tx,actor,patch.Required("lineId"));J.Need(line.S("invoiceId")==id&&line.S("lineType")=="rider"&&seen.Add(line.S("id")),"سطر الوقائع لا يخص الفاتورة أو مكرر.");var row=new JsonObject{["lineId"]=line.S("id")};
            if(patch.ContainsKey("scheme"))row["scheme"]=Scheme(patch.S("scheme"));
            if(patch.ContainsKey("riderId"))row["riderId"]=ActualRider(c,patch,input.B("confirmActualRider"),invoice.S("period"),line.S("platformId"));
            foreach(string key in new[]{"riderNet","companyCommission","platformAmountExclVat"})if(patch.ContainsKey(key))row[key]=Amount(patch.S(key),false);normalized.Add(row);
        }
        var action=new JsonObject{["id"]="hs-action:"+Guid.NewGuid().ToString("N"),["invoiceId"]=id,["companyId"]=invoice.S("companyId"),["action"]="facts_revision",["lineFacts"]=normalized,["revision"]=(LatestFacts(c,tx,id)?.N("revision")??0)+1,["status"]="pending",["requestedBy"]=actor.Identity!.Name,["reason"]=input.S("reason"),["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        Store.Save(c,ActionKind,action,tx);Store.Audit(c,tx,"platform.hs.facts_proposed",action.S("id"),actor.Identity!.Name!);return action;
    }
    public static JsonObject RequestReversal(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        Scope(actor,"platforms.hs.manage");var invoice=GetInvoice(c,tx,actor,id);J.Need(invoice.S("journalEntryId")!=""&&invoice.S("reversalJournalEntryId")=="","الفاتورة ليس لها قيد قابل للعكس.");string period=J.Period(input.Required("period"));J.Need(string.CompareOrdinal(period,invoice.S("postingPeriod",invoice.S("period")))>=0&&!Closed(c,tx,invoice.S("companyId"),period),"العكس يحتاج فترة مفتوحة لا تسبق القيد.");J.Need(input.S("reason").Length is >0 and <=500,"سبب العكس مطلوب.");
        var action=new JsonObject{["id"]="hs-action:"+Guid.NewGuid().ToString("N"),["invoiceId"]=id,["companyId"]=invoice.S("companyId"),["action"]="reversal",["period"]=period,["reason"]=input.S("reason"),["status"]="pending",["requestedBy"]=actor.Identity!.Name,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};Store.Save(c,ActionKind,action,tx);Store.Audit(c,tx,"platform.hs.reversal_requested",action.S("id"),actor.Identity!.Name!);return action;
    }
    public static JsonObject ApproveReversal(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,string actionId)
    {
        Scope(actor,"platforms.hs.approve");var invoice=GetInvoice(c,tx,actor,id).Clone();var action=Store.Get(c,actionId,ActionKind,tx)?.Clone()??throw new ApiError("طلب العكس غير موجود.",404);J.Need(action.S("action")=="reversal"&&action.S("invoiceId")==id,"طلب العكس لا يخص الفاتورة.");Independent(c,tx,actor,action,"requestedBy");
        if(action.S("status")=="approved")return action;
        J.Need(action.S("status")=="pending"&&invoice.S("reversalJournalEntryId")==""&&!Closed(c,tx,invoice.S("companyId"),action.S("period")),"العكس منفذ أو الفترة مقفلة.");PlatformPosting.Demand(c,"hs",tx);
        var original=Store.Get(c,invoice.Required("journalEntryId"),"journal",tx)??throw new ApiError("القيد الأصلي غير موجود.");J.Need(original.S("chart")=="target"&&original.S("sourceRef")=="HS-INVOICE:"+id&&original["lines"]!.AsArray().OfType<JsonObject>().All(l=>l.S("sponsorId")==invoice.S("companyId"))&&!Store.List(c,"journal",tx).Any(j=>j.S("reversesJournalId")==original.S("id")),"القيد ليس قيد هنقر قابلًا للعكس.");var lines=new JsonArray();
        foreach(var line in original["lines"]!.AsArray().OfType<JsonObject>())lines.Add(FinancialCore.CreateJournalLine(line.S("accountCode"),line.S("sponsorId"),line.N("credit"),line.N("debit"),"عكس مستقل للقيد",line.S("iqama"),targetChart:true));
        var reversed=PlatformPosting.Post(c,tx,action.S("period"),"HS-REV:"+original.S("id"),lines,actor.Identity!.Name!,"عكس وقائع هنقر",original.S("id"));
        invoice["accountingStatus"]="reversed";invoice["reversalJournalEntryId"]=reversed.S("id");Store.Save(c,"platform_invoice",invoice,tx);Store.Audit(c,tx,"platform.hs.invoice_reversed",id,actor.Identity!.Name!);
        action["status"]="approved";action["approvedBy"]=actor.Identity!.Name;action["journalEntryId"]=reversed.S("id");Store.Save(c,ActionKind,action,tx);Store.Audit(c,tx,"platform.hs.reversal_approved",actionId,actor.Identity!.Name!);return action;
    }
    static void PrepareRevision(DbConnection c,ClaimsPrincipal actor,JsonObject input,JsonObject invoice)
    {
        if(input.S("replacesInvoiceId")=="")return;var parent=GetInvoice(c,null,actor,input.S("replacesInvoiceId"));
        J.Need(parent.S("companyId")==invoice.S("companyId")&&parent.S("companyCr")==invoice.S("companyCr")&&parent.S("period")==invoice.S("period")&&parent.S("reversalJournalEntryId")!="","النسخة الجديدة تحتاج عكسًا معتمدًا لنفس الشركة والسجل والفترة.");
        var reversal=Store.Get(c,parent.S("reversalJournalEntryId"),"journal")??throw new ApiError("إثبات العكس غير موجود.");J.Need(reversal.S("reversesJournalId")==parent.S("journalEntryId"),"إثبات العكس غير مطابق.");
        string newId="hs-invoice:"+Hash(parent.S("id")+"\n"+FinancialImports.Canonical(invoice));invoice["id"]=newId;invoice["replacesInvoiceId"]=parent.S("id");invoice["revision"]=parent.N("revision",1)+1;invoice["postingPeriod"]=reversal.S("period");
        foreach(var line in invoice["lines"]!.AsArray().OfType<JsonObject>())line["id"]="hs-line:"+Hash(newId+"\n"+line.S("id"));
    }
}
