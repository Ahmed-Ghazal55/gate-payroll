using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class HungerStationInvoices
{
    public static JsonObject InvoiceView(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject invoice)
    {
        var result=invoice.Clone();var actions=new JsonArray();var blockers=new JsonArray();
        bool bypass=AccessPolicy.BypassForView(c,tx,actor,AccessPolicy.ScopeInvoiceApprovals);
        bool manage=AccessPolicy.Has(actor,"platforms.hs.manage"),approve=AccessPolicy.Has(actor,"platforms.hs.approve");
        bool independent=AccessPolicy.IndependentForView(actor,invoice.S("createdBy"),bypass);
        string status=invoice.S("status"),approval=invoice.S("approvalStatus","pending");
        if(manage&&invoice.S("reversalJournalEntryId")==""&&invoice.S("journalEntryId")=="")actions.Add("dispute");
        if(approve&&independent&&approval!="approved"&&status is "pending_review" or "approved" or "disputed")
        {
            try{PlatformPosting.Demand(c,"hs",tx);}catch(ApiError){blockers.Add("بوابة ترحيل هنقرستيشن مغلقة.");}
            if(HasUnresolvedDispute(c,tx,invoice.S("id")))blockers.Add("اعتراضات الفاتورة غير محسومة.");
            if(blockers.Count==0)actions.Add("approve");
        }
        else if(approve&&!independent)blockers.Add("اعتماد الفاتورة لمستخدم مستقل عن منشئها.");
        result["allowedActions"]=actions;result["approvalBlockers"]=blockers;return result;
    }
    public static JsonObject AllocationView(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject allocation)
    {
        var result=allocation.Clone();var actions=new JsonArray();var blockers=new JsonArray();
        bool bypass=AccessPolicy.BypassForView(c,tx,actor,AccessPolicy.ScopeInvoiceApprovals);
        if(allocation.S("status")=="pending")
        {
            if(!AccessPolicy.Has(actor,"platforms.hs.approve"))blockers.Add("صلاحية اعتماد توزيع هنقرستيشن مطلوبة.");
            else if(!AccessPolicy.IndependentForView(actor,allocation.S("requestedBy"),bypass))blockers.Add("اعتماد التوزيع لمستخدم مستقل عن مقدمه.");
            else actions.Add("approve");
        }
        result["allowedActions"]=actions;result["approvalBlockers"]=blockers;return result;
    }
    public static JsonObject DisputeView(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject dispute)
    {
        var result=dispute.Clone();var actions=new JsonArray();var blockers=new JsonArray();
        bool bypass=AccessPolicy.BypassForView(c,tx,actor,AccessPolicy.ScopeInvoiceApprovals);
        if(dispute.S("status") is "open" or "escalated")
        {
            if(!AccessPolicy.Has(actor,"platforms.hs.approve"))blockers.Add("صلاحية حسم اعتراض هنقرستيشن مطلوبة.");
            else if(!AccessPolicy.IndependentForView(actor,dispute.S("createdBy"),bypass))blockers.Add("حسم الاعتراض لمستخدم مستقل عن من رفعه.");
            else actions.Add("resolve");
        }
        result["allowedActions"]=actions;result["approvalBlockers"]=blockers;return result;
    }
}
