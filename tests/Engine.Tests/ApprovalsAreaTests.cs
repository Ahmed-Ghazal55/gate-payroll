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
        // TRIAL8/9: non-dual same actor also needs Independent (approve_own or bypass) — never automatic.
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>AccessPolicy.ValidateApproval(maker,request,new())).Status);
        var withOwn=UserAccess.Principal(new JsonObject{["username"]="same-user",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=4,["customPermissions"]=new JsonArray(AccessPolicy.ApproveOwnPermission)});
        f.Store.Write((c,tx)=>{AccessPolicy.ValidateApproval(withOwn,request,new(),"approvals.decide",c,tx,"approval.decide",request);return true;});
        // Dual: same actor blocked without bypass (approve_own never covers money-out).
        request["dualPermission"]="approvals.dual.payment_out";
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>AccessPolicy.ValidateApproval(withOwn,request,new(),"approvals.decide",dualPermission:"approvals.dual.payment_out")).Status);
        f.Store.Write((c,tx)=>{Store.Save(c,"user",new(){["id"]="u1",["username"]="same-user",["role"]="FinancialAccountant",["customPermissions"]=new JsonArray("approvals.dual.payment_out","approvals.decide")},tx);AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="same-user",["from"]=Day(0),["to"]=Day(7),["reason"]="M-0677 dual payment-out grant",["scope"]=ApprovalsArea.DualPaymentOut});return true;});
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
        f.Store.Write((c,tx)=>{
            Store.Save(c,"user",new(){["id"]="other",["username"]="other",["role"]="FinancialAccountant",["status"]="active",["department"]="FIN",["rankLevel"]=3},tx);
            Store.Save(c,"approval_request",old,tx);ApprovalsArea.EscalateOverdue(c,tx);return true;
        });
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
        f.Store.Write((c,tx)=>{
            Store.Save(c,"user",new(){["id"]="synthetic-pay-checker",["username"]="synthetic-pay-checker",["role"]="Admin",["department"]="FIN",["rankLevel"]=5},tx);
            Store.Save(c,"approval_request",req,tx);return true;
        });
        var decided=f.Store.Write((c,tx)=>ApprovalsArea.Decide(c,tx,checker,req.S("id"),new(){["decision"]="approve",["note"]="ok"}));
        Assert.AreEqual("approved",decided.S("status"));
        Assert.AreEqual(0,decided["allowedActions"]!.AsArray().Count);
    }

    [TestMethod]
    public void DualForKindIsServerDerivedAndLimitIsEnforced()
    {
        Assert.AreEqual(ApprovalsArea.DualBankFile,ApprovalsArea.DualForKind("bank_file"));
        Assert.AreEqual(ApprovalsArea.DualSharedRiderRelease,ApprovalsArea.DualForKind("shared_rider_release"));
        using var f=new PayFixture();
        var actor=UserAccess.Principal(new JsonObject{["username"]="limit-user",["role"]="FinancialAccountant",["department"]="OPS",["rankLevel"]=2,["rank"]="B",["customPermissions"]=new JsonArray("approvals.decide")});
        f.Store.Write((c,tx)=>ApprovalsArea.PutLimits(c,tx,PayFixture.Maker,new(){["items"]=new JsonArray(new JsonObject{["rank"]="B",["department"]="OPS",["limitSar"]=0})}));
        var req=new JsonObject{["id"]="synthetic-limit-request",["kind"]="generic",["requestedBy"]="other",["targetDepartment"]="OPS",["requiredRankLevel"]=1,["amountSar"]=50,["status"]="pending",["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>{AccessPolicy.ValidateApproval(actor,req,new(),"approvals.decide",c,tx);return true;})).Status);
    }

    [TestMethod]
    public void BypassGrantScopeDoesNotCoverSharedRiderRelease()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{Store.Save(c,"user",new(){["id"]="u-scope",["username"]="scoped",["role"]="FinancialAccountant",["customPermissions"]=new JsonArray(ApprovalsArea.DualSharedRiderRelease,"approvals.decide")},tx);
            AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="scoped",["from"]=Day(0),["to"]=Day(7),["reason"]="pay only",["scope"]=AccessPolicy.ScopePayApprovals});return true;});
        var actor=UserAccess.Principal(new JsonObject{["username"]="scoped",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=4,["customPermissions"]=new JsonArray(ApprovalsArea.DualSharedRiderRelease,"approvals.decide")});
        var req=new JsonObject{["id"]="synthetic-shared",["kind"]="shared_rider_release",["requestedBy"]="scoped",["targetDepartment"]="FIN",["requiredRankLevel"]=1,["dualPermission"]=ApprovalsArea.DualSharedRiderRelease};
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>{AccessPolicy.ValidateApproval(actor,req,new(),"approvals.decide",c,tx,dualPermission:ApprovalsArea.DualSharedRiderRelease);return true;})).Status);
    }

    [TestMethod]
    public void ScopeCoversRequiresExplicitMatchingScope()
    {
        Assert.IsFalse(AccessPolicy.ScopeCovers(AccessPolicy.ScopePayApprovals,null));
        Assert.IsFalse(AccessPolicy.ScopeCovers(AccessPolicy.ScopePayApprovals,""));
        Assert.IsFalse(AccessPolicy.ScopeCovers(null,ApprovalsArea.DualBankFile));
        // TRACE H2 / M-0677: pay_approvals never covers DualBankFile / DualPaymentOut — need explicit dual grant.
        Assert.IsFalse(AccessPolicy.ScopeCovers(AccessPolicy.ScopePayApprovals,ApprovalsArea.DualBankFile));
        Assert.IsFalse(AccessPolicy.ScopeCovers(AccessPolicy.ScopePayApprovals,ApprovalsArea.DualPaymentOut));
        Assert.IsTrue(AccessPolicy.ScopeCovers(ApprovalsArea.DualBankFile,ApprovalsArea.DualBankFile));
        Assert.IsTrue(AccessPolicy.ScopeCovers(AccessPolicy.ScopePayApprovals,"pay.approve"));
        Assert.IsFalse(AccessPolicy.ScopeCovers(AccessPolicy.ScopePayApprovals,ApprovalsArea.DualSharedRiderRelease));
    }

    [TestMethod]
    public void AllowedActionsApproveRequiresDepartmentAndRankLikeDecide()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var req=new JsonObject
        {
            ["id"]="synthetic-actions-dept",["kind"]="generic",["status"]="pending",["requestedBy"]="other",
            ["fromDepartment"]="OPS",["targetDepartment"]="FIN",["requiredRankLevel"]=4,["amountSar"]=10,
            ["sponsorId"]=company,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")
        };
        f.Store.Write((c,tx)=>{
            Store.Save(c,"user",new(){["id"]="other",["username"]="other",["role"]="FinancialAccountant",["status"]="active",["department"]="OPS",["rankLevel"]=2},tx);
            Store.Save(c,"approval_request",req,tx);return true;
        });
        JsonObject ScopeUser(string name,string dept,int rank)=>new()
        {
            ["username"]=name,["role"]="FinancialAccountant",["department"]=dept,["rankLevel"]=rank,
            ["customPermissions"]=new JsonArray("approvals.decide","approvals.view"),
            ["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(company)}
        };
        // OPS can see the request (fromDepartment) but Decide would 403 on target FIN — approve must not be offered.
        var wrong=UserAccess.Principal(ScopeUser("ops-viewer","OPS",5));
        var listed=f.Store.Read(c=>ApprovalsArea.ListApprovals(c,wrong,null,false,null));
        var item=listed["items"]!.AsArray().OfType<JsonObject>().Single(a=>a.S("id")==req.S("id"));
        Assert.IsFalse(item["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="approve"));
        // Matching target department and rank: approve is offered.
        var ok=UserAccess.Principal(ScopeUser("fin-viewer","FIN",5));
        listed=f.Store.Read(c=>ApprovalsArea.ListApprovals(c,ok,null,false,null));
        item=listed["items"]!.AsArray().OfType<JsonObject>().Single(a=>a.S("id")==req.S("id"));
        Assert.IsTrue(item["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="approve"));
    }

    [TestMethod]
    public void BypassGrantDoesNotOverrideDepartmentOrRank()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{
            Store.Save(c,"user",new(){["id"]="u-ops",["username"]="ops-bypass",["role"]="FinancialAccountant",["department"]="OPS",["customPermissions"]=new JsonArray(ApprovalsArea.DualPaymentOut,"approvals.decide")},tx);
            AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="ops-bypass",["from"]=Day(0),["to"]=Day(7),["reason"]="maker-checker only",["scope"]=AccessPolicy.ScopePayApprovals});
            return true;
        });
        // Wrong department even with an active pay-approvals bypass grant.
        var wrongDept=UserAccess.Principal(new JsonObject{["username"]="ops-bypass",["role"]="FinancialAccountant",["department"]="OPS",["rankLevel"]=4,["customPermissions"]=new JsonArray(ApprovalsArea.DualPaymentOut,"approvals.decide")});
        var req=new JsonObject{["id"]="synthetic-dept-rank",["kind"]="payment_out",["requestedBy"]="other-maker",["targetDepartment"]="FIN",["requiredRankLevel"]=1,["dualPermission"]=ApprovalsArea.DualPaymentOut,["amountSar"]=10};
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>{AccessPolicy.ValidateApproval(wrongDept,req,new(),"approvals.decide",c,tx,"approval.decide",req,ApprovalsArea.DualPaymentOut);return true;})).Status);
        // Correct department but rank below required — bypass still must not open it.
        var lowRank=UserAccess.Principal(new JsonObject{["username"]="ops-bypass",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=1,["customPermissions"]=new JsonArray(ApprovalsArea.DualPaymentOut,"approvals.decide")});
        req["requiredRankLevel"]=4;
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>{AccessPolicy.ValidateApproval(lowRank,req,new(),"approvals.decide",c,tx,"approval.decide",req,ApprovalsArea.DualPaymentOut);return true;})).Status);
    }
}
