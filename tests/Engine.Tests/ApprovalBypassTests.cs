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
        var user=new JsonObject{["id"]="synthetic-bypass-user",["username"]="synthetic-approver",["role"]="FinancialAccountant",["department"]="OPS"};
        f.Store.Write((c,tx)=>{Store.Save(c,"user",user,tx);AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="synthetic-approver",["from"]=Day(0),["to"]=Day(7),["reason"]="Ninja October parallel run"});return true;});
        var actor=UserAccess.Principal(user);
        var request=new JsonObject{["id"]="synthetic-bypass-request",["requestedBy"]="synthetic-approver",["targetDepartment"]="FIN",["requiredRankLevel"]=5};
        f.Store.Write((c,tx)=>{AccessPolicy.ValidateApproval(actor,request,new(),c:c,tx:tx);Store.Save(c,"approval_request",request,tx);return true;});
        Assert.IsTrue(f.Store.Read(c=>Store.Get(c,request.S("id"),"approval_request")!.B("selfApprovedException")));
        var audits=f.Store.Read(c=>Store.List(c,"audit").Where(a=>a.S("action")=="approval.bypass_maker_checker").ToList());
        Assert.IsTrue(audits.Count>=1);Assert.IsTrue(audits.All(a=>a.S("badge")=="bypass_maker_checker"&&a.S("reason")!=""));
    }
    [TestMethod]
    public void RevokeStopsBypassAndLegacyGhazalExceptionStillWorks()
    {
        using var f=new PayFixture();
        string name=PayFixture.Maker.Identity!.Name!;
        f.Store.Write((c,tx)=>{Store.Save(c,"user",new(){["id"]="synthetic-maker-user",["username"]=name,["role"]="Admin",["department"]="FIN"},tx);return true;});
        var grant=f.Store.Write((c,tx)=>AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]=name,["from"]=Day(0),["to"]=Day(3),["reason"]="temporary grant"}));
        f.Store.Write((c,tx)=>AccessPolicy.RevokeBypass(c,tx,PayFixture.Maker,grant.S("id")));
        Assert.IsNull(f.Store.Read(c=>AccessPolicy.ActiveBypassGrant(c,null,name)));
        var ghazal=new JsonObject{["id"]="synthetic-exception-user",["username"]="ghazal",["role"]="FinancialAccountant",["department"]="OPS",["approvalExceptionUntil"]=Day(2),["approvalExceptionReason"]="legacy dated exception"};
        Assert.IsTrue(AccessPolicy.ApprovalException(UserAccess.Principal(ghazal)));
    }
}
