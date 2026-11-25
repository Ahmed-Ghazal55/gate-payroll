using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;

namespace Payroll;

/// <summary>TRIAL9 / grok-TRIAL8: policy drafts store createdBy; approve uses the shared self-approval rule.</summary>
public static class PolicyLifecycle
{
    public static JsonObject Approve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        var p=Store.Get(c,id,"policy",tx)??throw new ApiError("السياسة غير موجودة.",404);
        J.Need(p.S("status")=="draft","هذه النسخة معتمدة بالفعل.");
        AccessPolicy.DemandIndependentOfMakers(actor,"policy.approve",c,tx,p,AccessPolicy.ScopePayApprovals,"createdBy");
        // ValidatePolicy lives in Program.cs local functions — re-check kind/config shape lightly here.
        string kind=p.S("kind");
        J.Need(kind is "commission" or "tiers" or "approved_pay","نوع سياسة غير صالح.");
        J.Need(p["config"] is JsonObject,"إعداد السياسة مطلوب.");
        p["status"]="approved";
        p["approvedAt"]=DateTimeOffset.UtcNow.ToString("O");
        p["approvedBy"]=actor.Identity?.Name??"";
        if(AccessPolicy.SamePerson(actor,p.S("createdBy")))
        {
            p["selfApproved"]=true;
            p["selfApprovedException"]=true;
        }
        Store.Save(c,"policy",p,tx);
        Store.Audit(c,tx,"policy.approve",id,actor.Identity?.Name??"");
        return p;
    }
}
