using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class HungerStationInvoices
{
    static IEnumerable<JsonObject> Visible(DbConnection c,ClaimsPrincipal actor,string kind,IQueryCollection query)
    {
        Scope(actor,"platforms.hs.view");var parents=Store.List(c,"platform_invoice").Where(i=>i.S("platform")=="HungerStation").ToDictionary(i=>i.S("id"),i=>i.S("companyId"));var rows=Store.List(c,kind).Where(r=>(kind is "platform_allocation" or "platform_dispute"?parents.GetValueOrDefault(r.S("invoiceId"))==r.S("companyId"):r.S("platform")=="HungerStation")&&FinancialScopes.Sponsor(actor,r.S("companyId")));
        string company=query["companyId"].ToString(),period=query["period"].ToString(),status=query["status"].ToString();
        if(company!=""){FinancialScopes.DemandSponsor(actor,company);rows=rows.Where(r=>r.S("companyId")==company);}if(period!="")rows=rows.Where(r=>r.S("period")==J.Period(period));if(status!="")rows=rows.Where(r=>r.S("status")==status);return rows.OrderBy(r=>r.S("id"),StringComparer.Ordinal);
    }
    public static JsonObject Dispute(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        Scope(actor,"platforms.hs.manage");var invoice=GetInvoice(c,tx,actor,id).Clone();J.Need(invoice.S("reversalJournalEntryId")=="","الفاتورة معكوسة؛ راجع النسخة المصححة.");var line=GetLine(c,tx,actor,input.Required("lineId"));J.Need(line.S("invoiceId")==id,"السطر لا يخص الفاتورة.");string category=input.Required("category");J.Need(category.Length<=80&&input.S("reason").Length is >0 and <=500,"فئة الاعتراض وسببه مطلوبان.");decimal amount=Amount(input.Required("amount"),false);J.Need(amount>0,"مبلغ الاعتراض يجب أن يكون موجبًا.");
        var previous=Store.List(c,"platform_dispute",tx).FirstOrDefault(d=>d.S("invoiceId")==id&&d.S("lineId")==line.S("id")&&d.S("category")==category&&d.S("status")!="resolved");if(previous!=null){J.Need(previous.N("amount")==amount,"اعتراض مفتوح بمبلغ مختلف؛ لا تعيد كتابة الأصل.");return previous;}
        var dispute=new JsonObject{["id"]="hs-dispute:"+Guid.NewGuid().ToString("N"),["invoiceId"]=id,["lineId"]=line.S("id"),["companyId"]=invoice.S("companyId"),["period"]=invoice.S("period"),["platform"]="HungerStation",["category"]=category,["amount"]=amount,["reason"]=input.S("reason"),["status"]="open",["createdBy"]=actor.Identity!.Name,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        Store.Save(c,"platform_dispute",dispute,tx);Store.Audit(c,tx,"platform.hs.dispute_created",dispute.S("id"),actor.Identity!.Name!);invoice["status"]="disputed";invoice["settlementHold"]=true;Store.Save(c,"platform_invoice",invoice,tx);Store.Audit(c,tx,"platform.hs.invoice_held",id,actor.Identity!.Name!);
        Notifications.Raise(c,tx,"notice:"+dispute.S("id"),"system","اعتراض هنقر مفتوح؛ راجع وقائع الفاتورة.","platforms.hs.view",actor.Identity!.Name!,eventKey:dispute.S("id"),companyId:invoice.S("companyId"),platform:"HungerStation");return dispute;
    }
    public static JsonObject Resolve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        string mode=input.Required("mode");if(mode=="facts")return ReviseFacts(c,tx,actor,id,input);if(mode=="request_reversal")return RequestReversal(c,tx,actor,id,input);
        J.Need(mode=="dispute","نوع معالجة غير معروف.");Scope(actor,"platforms.hs.approve");var invoice=GetInvoice(c,tx,actor,id).Clone();var dispute=Store.Get(c,input.Required("disputeId"),"platform_dispute",tx)?.Clone()??throw new ApiError("الاعتراض غير موجود.",404);J.Need(dispute.S("invoiceId")==id,"الاعتراض لا يخص الفاتورة.");Independent(actor,dispute);
        if(dispute.S("status")=="resolved")return dispute;string outcome=input.S("outcome");J.Need(outcome is "platform_credit" or "platform_rejected"&&input.S("platformReference").Length is >0 and <=120&&input.S("reason").Length is >0 and <=500,"إثبات رد المنصة ومرجعه وسببه مطلوب؛ لا يحمل CAR لأي طرف.");
        if(outcome=="platform_rejected")
        {
            // BR §23.2 exception: the platform rejected the claim. The dispute stays open (escalated); the invoice may post with W held in 112803.
            J.Need(dispute.S("category")=="CAR","رفض المنصة يسمح بالترحيل لاعتراض CAR فقط؛ باقي الاعتراضات تنتظر الحسم.");
            if(dispute.S("status")=="escalated")return dispute;
            dispute["status"]="escalated";dispute["outcome"]="platform_rejected";dispute["platformReference"]=input.S("platformReference");dispute["rejectionReason"]=input.S("reason");dispute["rejectionRecordedBy"]=actor.Identity!.Name;dispute["rejectionRecordedAt"]=DateTimeOffset.UtcNow.ToString("O");dispute["accountingReconciliation"]="held_in_disputes_112803";
            Store.Save(c,"platform_dispute",dispute,tx);Store.Audit(c,tx,"platform.hs.dispute_rejected_by_platform",dispute.S("id"),actor.Identity!.Name!);
            invoice["settlementHold"]=true;invoice["status"]=HasUnresolvedDispute(c,tx,id)?"disputed":invoice.S("approvalStatus")=="approved"?"approved":"pending_review";Store.Save(c,"platform_invoice",invoice,tx);Store.Audit(c,tx,"platform.hs.invoice_response_recorded",id,actor.Identity!.Name!);return dispute;
        }
        dispute["status"]="resolved";dispute["outcome"]="platform_credit";dispute["platformReference"]=input.S("platformReference");dispute["resolutionReason"]=input.S("reason");dispute["resolvedBy"]=actor.Identity!.Name;dispute["resolvedAt"]=DateTimeOffset.UtcNow.ToString("O");dispute["accountingReconciliation"]=invoice.S("journalEntryId")==""?"no_posted_claim":"pending_reversal_or_credit_note";Store.Save(c,"platform_dispute",dispute,tx);Store.Audit(c,tx,"platform.hs.dispute_resolved",dispute.S("id"),actor.Identity!.Name!);
        bool open=HasUnresolvedDispute(c,tx,id);invoice["settlementHold"]=open||EscalatedCar(c,tx,id)||invoice.S("journalEntryId")!="";invoice["status"]=open?"disputed":invoice.S("approvalStatus")=="approved"?"approved":"pending_review";Store.Save(c,"platform_invoice",invoice,tx);Store.Audit(c,tx,"platform.hs.invoice_response_recorded",id,actor.Identity!.Name!);return dispute;
    }
    public static void MapReview(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/platforms/hs/invoices",(HttpContext ctx)=>store.Read(c=>ApiPagination.Apply(Visible(c,ctx.User,"platform_invoice",ctx.Request.Query),ctx.Request.Query,true))).WithTags("Platforms").WithSummary("Review paginated HS invoices within company and platform scope");
        api.MapGet("/platforms/hs/disputes",(HttpContext ctx)=>store.Read(c=>ApiPagination.Apply(Visible(c,ctx.User,"platform_dispute",ctx.Request.Query),ctx.Request.Query,true))).WithTags("Platforms").WithSummary("Review formal HS disputes within scope");
        api.MapGet("/platforms/hs/allocations",(HttpContext ctx)=>store.Read(c=>ApiPagination.Apply(Visible(c,ctx.User,"platform_allocation",ctx.Request.Query),ctx.Request.Query,true))).WithTags("Platforms").WithSummary("Review explicit HS TGA allocations and revisions");
        api.MapGet("/platforms/hs/facts",(HttpContext ctx)=>store.Read(c=>
        {string kind=ctx.Request.Query["kind"].ToString();J.Need(HungerStationFacts.Kinds.Contains(kind),"حدد نوع وقائع هنقر المعتمد.");return ApiPagination.Apply(Visible(c,ctx.User,kind,ctx.Request.Query),ctx.Request.Query,true);})).WithTags("Platforms").WithSummary("Review paginated immutable approved HS facts by allowlisted kind");
        api.MapGet("/platforms/hs/proposed-journals",(HttpContext ctx)=>store.Read(c=>
        {
            Scope(ctx.User,"platforms.hs.view");string id=ctx.Request.Query["invoiceId"].ToString(),kind=ctx.Request.Query["kind"].ToString();if(id!="")return (object)Proposal(c,ctx.User,id);
            if(kind!=""){J.Need(HungerStationFacts.Kinds.Contains(kind),"نوع الوقائع غير معروف.");return ApiPagination.Apply(Visible(c,ctx.User,kind,ctx.Request.Query).Select(r=>HungerStationFacts.Proposal(kind,r)),ctx.Request.Query,true);}
            return ApiPagination.Apply(Visible(c,ctx.User,"platform_invoice",ctx.Request.Query).Select(r=>{var proposal=Proposal(c,ctx.User,r.S("id"));proposal["invoiceId"]=r.S("id");return proposal;}),ctx.Request.Query,true);
        })).WithTags("Platforms").WithSummary("Read signed invoice proposals or unpostable CR-002 fact mappings");
        api.MapPost("/platforms/hs/invoices/{id}/approve",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>input.S("reversalRequestId")!=""?ApproveReversal(c,tx,ctx.User,id,input.S("reversalRequestId")):ApproveInvoice(c,tx,ctx.User,id,input))).WithTags("Platforms").WithSummary("Independently approve signed HS facts or an open-period reversal; gated posting");
        api.MapPost("/platforms/hs/invoices/{id}/dispute",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Dispute(c,tx,ctx.User,id,input))).WithTags("Platforms").WithSummary("Raise an audited formal HS line dispute without charging a rider");
        api.MapPost("/platforms/hs/invoices/{id}/resolve",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Resolve(c,tx,ctx.User,id,input))).WithTags("Platforms").WithSummary("Propose immutable fact correction/reversal or independently record the platform credit or rejection response");
        api.MapPost("/platforms/hs/allocations/{lineId}",(string lineId,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Split(c,tx,ctx.User,lineId,input))).WithTags("Platforms").WithSummary("Propose an explicit TGA company/rider split; no default");
        api.MapPost("/platforms/hs/allocations/{id}/approve",(string id,HttpContext ctx)=>store.Write((c,tx)=>ApproveAllocation(c,tx,ctx.User,id))).WithTags("Platforms").WithSummary("Approve latest TGA split with independent finance checker");
    }
}
