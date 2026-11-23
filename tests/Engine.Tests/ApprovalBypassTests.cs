using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class ApprovalBypassTests
{
    static string Day(int delta=0)=>DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).AddDays(delta).ToString("yyyy-MM-dd");
    [TestMethod]
    public void BypassGrantLetsSameActorApproveAndIsAudited()
    {
        using var f=new PayFixture();
        // Bypass lifts maker≠checker only — actor must still match department and rank.
        var user=new JsonObject{["id"]="synthetic-bypass-user",["username"]="synthetic-approver",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=5,["customPermissions"]=new JsonArray("approvals.dual.payment_out","approvals.decide")};
        f.Store.Write((c,tx)=>{Store.Save(c,"user",user,tx);AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="synthetic-approver",["from"]=Day(0),["to"]=Day(7),["reason"]="Ninja October parallel run"});return true;});
        var actor=UserAccess.Principal(user);
        // Dual-control path (M-0672): bypass is required only for two-person actions.
        var request=new JsonObject{["id"]="synthetic-bypass-request",["requestedBy"]="synthetic-approver",["targetDepartment"]="FIN",["requiredRankLevel"]=5,["dualPermission"]="approvals.dual.payment_out"};
        f.Store.Write((c,tx)=>{AccessPolicy.ValidateApproval(actor,request,new(),"approvals.decide",c,tx,"approval.decide",request,"approvals.dual.payment_out");Store.Save(c,"approval_request",request,tx);return true;});
        Assert.IsTrue(f.Store.Read(c=>Store.Get(c,request.S("id"),"approval_request")!.B("selfApprovedException")));
        var audits=f.Store.Read(c=>Store.List(c,"audit").Where(a=>a.S("action")=="approval.bypass_maker_checker").ToList());
        Assert.IsTrue(audits.Count>=1);Assert.IsTrue(audits.All(a=>a.S("badge")=="bypass_maker_checker"&&a.S("reason")!=""));
    }
    [TestMethod]
    public void RevokeStopsBypassAndLegacyGhazalExceptionStillWorks()
    {
        using var f=new PayFixture();
        string name=PayFixture.Maker.Identity!.Name!;
        f.Store.Write((c,tx)=>{
            var maker=UserAccess.Find(c,name,tx)??Store.Get(c,"synthetic-pay-maker","user",tx)??new JsonObject{["id"]="synthetic-pay-maker",["username"]=name,["role"]="Admin"};
            maker["role"]="Admin";maker["department"]="FIN";maker["status"]=maker.S("status","active");Store.Save(c,"user",maker,tx);return true;
        });
        var grant=f.Store.Write((c,tx)=>AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]=name,["from"]=Day(0),["to"]=Day(3),["reason"]="temporary grant"}));
        f.Store.Write((c,tx)=>AccessPolicy.RevokeBypass(c,tx,PayFixture.Maker,grant.S("id")));
        Assert.IsNull(f.Store.Read(c=>AccessPolicy.ActiveBypassGrant(c,null,name)));
        var ghazal=new JsonObject{["id"]="synthetic-exception-user",["username"]="ghazal",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=2,["approvalExceptionUntil"]=Day(2),["approvalExceptionReason"]="legacy dated exception"};
        Assert.IsTrue(AccessPolicy.ApprovalException(UserAccess.Principal(ghazal)),"claim still parses for display");
        // BR §35: maker≠checker only for dual money-out paths; dated exception alone is not a bypass grant.
        ghazal["customPermissions"]=new JsonArray("approvals.dual.payment_out","approvals.decide");
        var request=new JsonObject{["id"]="synthetic-legacy-request",["requestedBy"]="ghazal",["targetDepartment"]="FIN",["requiredRankLevel"]=1,["dualPermission"]="approvals.dual.payment_out"};
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>AccessPolicy.ValidateApproval(UserAccess.Principal(ghazal),request,new(),"approvals.decide",dualPermission:"approvals.dual.payment_out")).Status,"dated exception alone cannot approve");
        f.Store.Write((c,tx)=>{Store.Save(c,"user",ghazal,tx);AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="ghazal",["from"]=Day(0),["to"]=Day(2),["reason"]="§35 grant for ghazal"});return true;});
        f.Store.Write((c,tx)=>{AccessPolicy.ValidateApproval(UserAccess.Principal(ghazal),request,new(),"approvals.decide",c,tx,dualPermission:"approvals.dual.payment_out");return true;});
    }
}
