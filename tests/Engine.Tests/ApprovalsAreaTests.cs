using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class ApprovalsAreaTests
{
    static string Day(int delta=0)=>DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).AddDays(delta).ToString("yyyy-MM-dd");

    [TestMethod]
    public void PermissionOnlyAllowsSameActor_DualRequiresIndependentOrBypass()
    {
        using var f=new PayFixture();
        var maker=UserAccess.Principal(new JsonObject{["username"]="same-user",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=4});
        var request=new JsonObject{["id"]="synthetic-perm-request",["requestedBy"]="same-user",["targetDepartment"]="FIN",["requiredRankLevel"]=1,["status"]="pending",["kind"]="generic",["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        // Non-dual: same actor OK (BR §35 / M-0672).
        AccessPolicy.ValidateApproval(maker,request,new());
        // Dual: same actor blocked without bypass.
        request["dualPermission"]="approvals.dual.payment_out";
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>AccessPolicy.ValidateApproval(maker,request,new(),"approvals.decide",dualPermission:"approvals.dual.payment_out")).Status);
        f.Store.Write((c,tx)=>{Store.Save(c,"user",new(){["id"]="u1",["username"]="same-user",["role"]="FinancialAccountant",["customPermissions"]=new JsonArray("approvals.dual.payment_out","approvals.decide")},tx);AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="same-user",["from"]=Day(0),["to"]=Day(7),["reason"]="M-0677 pay approvals"});return true;});
        maker=UserAccess.Principal(new JsonObject{["username"]="same-user",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=4,["customPermissions"]=new JsonArray("approvals.dual.payment_out","approvals.decide")});
        f.Store.Write((c,tx)=>{AccessPolicy.ValidateApproval(maker,request,new(),"approvals.decide",c,tx,"approval.decide",request,"approvals.dual.payment_out");return true;});
        Assert.IsTrue(request.B("selfApprovedException"));
    }

    [TestMethod]
    public void LimitsAndSlaCrud_EscalateNeverAutoApproves()
    {
        using var f=new PayFixture();
        var admin=PayFixture.Maker;
        var limits=f.Store.Read(c=>ApprovalsArea.GetLimits(c,admin));
        Assert.IsTrue(limits.Count>=5);
        f.Store.Write((c,tx)=>ApprovalsArea.PutLimits(c,tx,admin,new(){["items"]=new JsonArray(new JsonObject{["rank"]="B",["department"]="OPS",["limitSar"]=2500})}));
        Assert.AreEqual(2500m,f.Store.Read(c=>Store.List(c,ApprovalsArea.LimitsKind).Single(l=>l.S("rank")=="B"&&l.S("department")=="OPS").N("limitSar")));

        var sla=f.Store.Write((c,tx)=>ApprovalsArea.PutSla(c,tx,admin,new(){["items"]=new JsonArray(new JsonObject{["requestType"]="default",["workingDays"]=1})}));
        Assert.IsFalse(sla.OfType<JsonObject>().Single(s=>s.S("requestType")=="default").B("autoApprove"));

        var old=new JsonObject
        {
            ["id"]="synthetic-old-request",["kind"]="default",["status"]="pending",["requestedBy"]="other",
            ["targetDepartment"]="FIN",["requiredRankLevel"]=1,
            ["createdAt"]=DateTimeOffset.UtcNow.AddDays(-10).ToString("O")
        };
        f.Store.Write((c,tx)=>{Store.Save(c,"approval_request",old,tx);ApprovalsArea.EscalateOverdue(c,tx);return true;});
        Assert.AreEqual("escalated",f.Store.Read(c=>Store.Get(c,old.S("id"),"approval_request")!.S("status")));
        Assert.AreNotEqual("approved",f.Store.Read(c=>Store.Get(c,old.S("id"),"approval_request")!.S("status")));
    }

    [TestMethod]
    public void DecideApproveRejectWithAllowedActions()
    {
        using var f=new PayFixture();
        var checker=UserAccess.Principal(new JsonObject{["username"]="synthetic-pay-checker",["role"]="Admin",["department"]="FIN",["rankLevel"]=5});
        var req=new JsonObject
        {
            ["id"]="synthetic-decide-request",["kind"]="generic",["status"]="pending",["requestedBy"]="synthetic-pay-maker",
            ["targetDepartment"]="FIN",["fromDepartment"]="OPS",["requiredRankLevel"]=1,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")
        };
        f.Store.Write((c,tx)=>{Store.Save(c,"approval_request",req,tx);return true;});
        var decided=f.Store.Write((c,tx)=>ApprovalsArea.Decide(c,tx,checker,req.S("id"),new(){["decision"]="approve",["note"]="ok"}));
        Assert.AreEqual("approved",decided.S("status"));
        Assert.AreEqual(0,decided["allowedActions"]!.AsArray().Count);
    }
}
