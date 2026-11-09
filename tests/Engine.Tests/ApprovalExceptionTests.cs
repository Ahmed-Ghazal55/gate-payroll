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
    public void SelfApprovalExpiryOtherAdminAndMissingPermissionAreEnforced()
    {
        using var f=new PayFixture();var user=User();var actor=UserAccess.Principal(user);
        var request=new JsonObject{["id"]="synthetic-self-request",["requestedBy"]="ghazal",["targetDepartment"]="FIN",["requiredRankLevel"]=5};
        f.Store.Write((c,tx)=>{AccessPolicy.ValidateApproval(actor,request,new(),c:c,tx:tx);Store.Save(c,"approval_request",request,tx);return true;});
        Assert.IsTrue(f.Store.Read(c=>Store.Get(c,request.S("id"),"approval_request")!.B("selfApprovedException")));
        var audits=f.Store.Read(c=>Store.List(c,"audit").Where(a=>a.S("action")=="approval.self_exception").ToList());Assert.AreEqual(2,audits.Count);
        Assert.IsTrue(audits.All(a=>a.S("maker")=="ghazal"&&a.S("actor")=="ghazal"&&a.S("recordId")==request.S("id")&&a.S("operation")!=""));
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>AccessPolicy.ValidateApproval(UserAccess.Principal(User(days:-1)),request,new())).Status);
        var admin=User("synthetic-other-admin");admin["role"]="Admin";var other=request.Clone();other["requestedBy"]=admin.S("username");
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>AccessPolicy.ValidateApproval(UserAccess.Principal(admin),other,new(){["overrideSelf"]=true,["note"]="Old flag is not an exception"})).Status);
        user["deniedPermissions"]=new JsonArray("approvals.decide");Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>AccessPolicy.ValidateApproval(UserAccess.Principal(user),request,new())).Status);
    }
    [TestMethod]
    public void ExceptionConfigurationRequiresAdminNamedUserDateAndReason()
    {
        using var f=new PayFixture();var user=User();var input=new JsonObject{["approvalExceptionUntil"]=Day(120),["approvalExceptionReason"]="Dated project acceptance"};
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>{AccessPolicy.ConfigureApprovalException(c,tx,PayFixture.Checker,user,input);return true;}));
        f.Store.Write((c,tx)=>{AccessPolicy.ConfigureApprovalException(c,tx,PayFixture.Maker,user,input);return true;});
        input["approvalExceptionUntil"]=Day(121);Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>{AccessPolicy.ConfigureApprovalException(c,tx,PayFixture.Maker,user,input);return true;}));
        input["approvalExceptionUntil"]="";Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>{AccessPolicy.ConfigureApprovalException(c,tx,PayFixture.Maker,user,input);return true;}));
        input["approvalExceptionUntil"]=Day(2);Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>{AccessPolicy.ConfigureApprovalException(c,tx,PayFixture.Maker,User("synthetic-other-admin"),input);return true;}));
        Assert.IsTrue(UserAccess.Public(user)["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="self_approve_exception"));
    }
    [TestMethod]
    public void SameActorCanReviewPostAndConfirmCycleOnlyDuringDatedException()
    {
        var(f,input,_)=PayCycleTests.Setup();using(f)
        {
            var actor=UserAccess.Principal(User());string id=f.Store.Write((c,tx)=>PayCycles.Create(c,tx,actor,input)).S("id");
            for(int stage=1;stage<=6;stage++){int next=stage;f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,actor,id,new(){["stage"]=next}));}
            f.Store.Write((c,tx)=>PayCycles.ReviewAll(c,tx,actor,id));
            Assert.IsTrue(f.Store.Read(c=>PayCycles.View(c,null,actor,Store.Get(c,id,PayCycles.Kind)!))["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve_salaries"));
            f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,actor,id,new(){["stage"]=7}));
            var final=f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,actor,id,new(){["stage"]=8}));Assert.AreEqual(8m,final.N("stage"));Assert.IsTrue(final.B("selfApprovedException"));
            Assert.IsTrue(f.Store.Read(c=>Store.List(c,"journal").All(j=>j.N("totalDebit")==j.N("totalCredit"))));
            var settings=f.Draft("pay_terms","ninja",PayFixture.Terms());settings["createdBy"]="ghazal";Assert.IsTrue(PaySettings.WithActions(actor,settings)["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"));
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
            bool ownership=Path.GetFileName(path) is "ManualJournals.cs" or "PaySettings.Lifecycle.cs"&&(line.Contains("المسودة لصاحبها")||line.Contains("bool author="));
            Assert.IsTrue(ownership,"Raw maker comparison bypasses AccessPolicy.Independent: "+Path.GetFileName(path));
        }
    }
}
