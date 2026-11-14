using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class ApprovalExceptionTests
{
    static string Day(int delta=0)=>DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).AddDays(delta).ToString("yyyy-MM-dd");
    static JsonObject User(string name="ghazal",int days=2)=>new(){["id"]="synthetic-exception-user",["username"]=name,["role"]="FinancialAccountant",["department"]="OPS",["approvalExceptionUntil"]=Day(days),["approvalExceptionReason"]="Temporary audited acceptance testing"};
    [TestMethod]
    public void DatedExceptionClaimsNoLongerOpenNewApprovalsWithoutBypassGrant()
    {
        using var f=new PayFixture();var user=User();var actor=UserAccess.Principal(user);
        var request=new JsonObject{["id"]="synthetic-self-request",["requestedBy"]="ghazal",["targetDepartment"]="FIN",["requiredRankLevel"]=5};
        Assert.IsTrue(AccessPolicy.ApprovalException(actor),"legacy claim fields still parse");
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>AccessPolicy.ValidateApproval(actor,request,new())).Status,"dated exception alone cannot self-approve");
        f.Store.Write((c,tx)=>{Store.Save(c,"user",user,tx);AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="ghazal",["from"]=Day(0),["to"]=Day(2),["reason"]="§35 grant replaces dated exception"});return true;});
        f.Store.Write((c,tx)=>{AccessPolicy.ValidateApproval(actor,request,new(),c:c,tx:tx);Store.Save(c,"approval_request",request,tx);return true;});
        Assert.IsTrue(f.Store.Read(c=>Store.Get(c,request.S("id"),"approval_request")!.B("selfApprovedException")));
        var audits=f.Store.Read(c=>Store.List(c,"audit").Where(a=>a.S("action")=="approval.bypass_maker_checker").ToList());
        Assert.IsTrue(audits.Count>=1);Assert.IsTrue(audits.All(a=>a.S("badge")=="bypass_maker_checker"&&a.S("maker")=="ghazal"));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"audit").Count(a=>a.S("action")=="approval.self_exception"&&a.S("recordId")==request.S("id"))),"new approvals never write self_exception");
        user["deniedPermissions"]=new JsonArray("approvals.decide");Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>AccessPolicy.ValidateApproval(UserAccess.Principal(user),request,new())).Status);
    }
    [TestMethod]
    public void ExceptionConfigurationRefusesNewDatesAndAllowsClear()
    {
        using var f=new PayFixture();var user=User();var input=new JsonObject{["approvalExceptionUntil"]=Day(120),["approvalExceptionReason"]="Dated project acceptance"};
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>{AccessPolicy.ConfigureApprovalException(c,tx,PayFixture.Checker,user,input);return true;}));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>{AccessPolicy.ConfigureApprovalException(c,tx,PayFixture.Maker,user,input);return true;})).Status);
        input["approvalExceptionUntil"]="";input["approvalExceptionReason"]="";
        f.Store.Write((c,tx)=>{AccessPolicy.ConfigureApprovalException(c,tx,PayFixture.Maker,user,input);return true;});
        Assert.AreEqual("",user.S("approvalExceptionUntil"));Assert.AreEqual("",user.S("approvalExceptionReason"));
        Assert.IsTrue(f.Store.Read(c=>Store.List(c,"audit").Any(a=>a.S("action")=="admin.approval_exception_cleared")));
    }
    [TestMethod]
    public void SameActorCanReviewPostAndConfirmCycleOnlyWithBypassGrant()
    {
        var(f,input,_)=PayCycleTests.Setup();using(f)
        {
            var user=User();f.Store.Write((c,tx)=>{Store.Save(c,"user",user,tx);AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="ghazal",["from"]=Day(0),["to"]=Day(2),["reason"]="Ninja October §35 grant"});return true;});
            var actor=UserAccess.Principal(user);string id=f.Store.Write((c,tx)=>PayCycles.Create(c,tx,actor,input)).S("id");
            f.Store.Write((c,tx)=>PayCycles.CaptureInputs(c,tx,actor,id));
            for(int stage=1;stage<=6;stage++){int next=stage;f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,actor,id,new(){["stage"]=next}));}
            f.Store.Write((c,tx)=>PayCycles.ReviewAll(c,tx,actor,id));
            Assert.IsTrue(f.Store.Read(c=>PayCycles.View(c,null,actor,Store.Get(c,id,PayCycles.Kind)!))["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve_salaries"));
            f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,actor,id,new(){["stage"]=7}));
            var final=f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,actor,id,new(){["stage"]=8}));Assert.AreEqual(8m,final.N("stage"));Assert.IsTrue(final.B("selfApprovedException"));
            Assert.IsTrue(f.Store.Read(c=>Store.List(c,"journal").All(j=>j.N("totalDebit")==j.N("totalCredit"))));
            var settings=f.Draft("pay_terms","ninja",PayFixture.Terms());settings["createdBy"]="ghazal";
            Assert.IsTrue(f.Store.Read(c=>PaySettings.WithActions(actor,settings,c)["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve")));
        }
    }
    [TestMethod]
    public void MakerComparisonsCannotBypassCentralHelper()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!Directory.Exists(Path.Combine(root.FullName,"Payroll.Api")))root=root.Parent;Assert.IsNotNull(root);
        var raw=new Regex("(?:string\\.Equals\\([^;\\n]*(?:createdBy|requestedBy|reviewedBy|advancedBy|classifiedBy|debtSelectedBy)|\\.S\\(\"(?:createdBy|requestedBy|reviewedBy|advancedBy|classifiedBy|debtSelectedBy)\"[^)]*\\)\\.Equals|\\.S\\(\"(?:createdBy|requestedBy)\"[^)]*\\)\\s*!=)");
        foreach(string path in Directory.EnumerateFiles(Path.Combine(root.FullName,"Payroll.Api"),"*.cs"))
        foreach(string line in File.ReadLines(path))
        {
            if(Path.GetFileName(path).StartsWith("AccessPolicy.")||!raw.IsMatch(line))continue;
            // Ownership of mutable drafts is an authorization check, not maker/checker.
            // Jahez Approve uses AccessPolicy.Independent for createdBy (not a raw inequality gate).
            bool ownership=Path.GetFileName(path) is "ManualJournals.cs" or "PaySettings.Lifecycle.cs"&&(line.Contains("المسودة لصاحبها")||line.Contains("bool author="));
            bool viaIndependent=Path.GetFileName(path)=="JahezReconciliation.cs"&&line.Contains("AccessPolicy.Independent");
            Assert.IsTrue(ownership||viaIndependent,"Raw maker comparison bypasses AccessPolicy.Independent: "+Path.GetFileName(path)+" :: "+line.Trim());
        }
    }
}
