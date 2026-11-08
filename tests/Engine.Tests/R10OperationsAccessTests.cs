using System.Security.Claims;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class R10OperationsAccessTests
{
    static readonly string Company=PayReviewDraftTests.Company;const string Account=PayReviewDraftTests.Account,Rider=PayReviewDraftTests.Rider;
    static JsonObject Draft(PayFixture f)=>f.Store.Read(c=>PayReviewDraft.Build(c,null,PayFixture.Maker,"ninja","2026-10",Company));
    static bool Blocked(JsonObject draft,string code)=>draft["blockers"]!.AsArray().Any(b=>b.S("code")==code);
    static bool Warned(JsonObject draft,string code)=>draft["warnings"]!.AsArray().Any(b=>b.S("code")==code);
    static void SetDay(PayFixture f,decimal orders)=>f.Store.Write((c,tx)=>{var day=Store.Get(c,"synthetic-r8-day","ninja_daily",tx)!;day["orders"]=orders;Store.Save(c,"ninja_daily",day,tx);return true;});

    [TestMethod]
    public void InvoiceStaysFinalWhenErpOrdersDiffer()
    {
        using var f=PayReviewDraftTests.Setup();SetDay(f,7);
        var draft=Draft(f);Assert.IsFalse(Blocked(draft,"orders_mismatch"));Assert.IsTrue(Warned(draft,"orders_variance"));Assert.IsTrue(draft.B("canCreate"),draft["blockers"]!.ToJsonString());
        Assert.AreEqual(10m,draft["invoices"]![0]!["accounts"]![0]!["riders"]![0]!.N("orders"));Assert.AreEqual("2026-10-01",draft.S("dataThrough"));
    }
    [TestMethod]
    public void ErpDateOnlyAssignmentsAreReadLikeTheLiveData()
    {
        var a=new JsonObject{["EffectiveFrom"]="2026-10-05T00:00:00.0000000",["EffectiveTo"]="2026-10-20T00:00:00.0000000"};
        Assert.IsTrue(AssignmentSpan.Covers(a,"2026-10-05"));Assert.IsTrue(AssignmentSpan.Covers(a,"2026-10-20T00:00:00.0000000"));Assert.IsFalse(AssignmentSpan.Covers(a,"2026-10-21"));Assert.IsFalse(AssignmentSpan.Covers(a,"2026-10-04"));
        Assert.IsTrue(AssignmentSpan.Overlaps(a,"2026-10"));Assert.IsFalse(AssignmentSpan.Overlaps(a,"2026-11"));
        Assert.IsTrue(AssignmentSpan.Overlaps(new JsonObject{["EffectiveFrom"]="2026-01-01T00:00:00.0000000"},"2026-11"));
        using var f=PayReviewDraftTests.Setup();f.Store.Write((c,tx)=>{var account=Store.Get(c,Account,"platform_account",tx)!;account["assignments"]=new JsonArray(new JsonObject{["riderId"]=Rider,["EffectiveFrom"]="2026-10-01T00:00:00.0000000",["EffectiveTo"]=""});Store.Save(c,"platform_account",account,tx);return true;});
        var draft=Draft(f);Assert.IsTrue(draft.B("canCreate"),draft["blockers"]!.ToJsonString());
    }
    [TestMethod]
    public void MissingDailyUsesTheOnlyAssignedOperatorWithAWarning()
    {
        using var f=PayReviewDraftTests.Setup();f.Store.Write((c,tx)=>{using var remove=Store.Command(c,tx,"DELETE FROM records WHERE id=@id",("@id","synthetic-r8-day"));remove.ExecuteNonQuery();return true;});
        var draft=Draft(f);Assert.IsTrue(Warned(draft,"operations_missing_using_assignment"));Assert.IsTrue(draft.B("canCreate"),draft["blockers"]!.ToJsonString());
        Assert.AreEqual(Rider,draft["invoices"]![0]!["accounts"]![0]!["riders"]![0]!.S("rider"));
    }
    [TestMethod]
    public void SeveralOperatorsGetAProposalButStayBlocked()
    {
        var split=PayReviewCategory.Propose([("a",30m),("b",10m)],41,100.01m,10m);
        Assert.AreEqual(41m,split.Sum(r=>r!.N("orders")));Assert.AreEqual(100.01m,split.Sum(r=>r!.N("baseGross")));Assert.AreEqual(10m,split.Sum(r=>r!.N("deduction")));
        using var f=PayReviewDraftTests.Setup();f.Store.Write((c,tx)=>
        {
            Store.Save(c,"ninja_daily",new(){["id"]="synthetic-r10-second",["companyId"]=Company,["platformAccountId"]=Account,["riderId"]="2000000088",["date"]="2026-10-02",["orders"]=2},tx);
            Store.Save(c,"rider",new(){["id"]="2000000088",["iqama"]="2000000088",["name"]="Synthetic",["engagementType"]="External"},tx);
            var account=Store.Get(c,Account,"platform_account",tx)!;account["assignments"]!.AsArray().Add(new JsonObject{["riderId"]="2000000088",["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00",["RawEffectiveTo"]=""});Store.Save(c,"platform_account",account,tx);return true;
        });
        var draft=Draft(f);Assert.IsTrue(Blocked(draft,"allocation_required"));Assert.AreEqual(1,draft["allocationProposals"]!.AsArray().Count);Assert.IsFalse(draft.B("canCreate"));
    }
    [TestMethod]
    [DataRow("SponsoredInternal","2","1","sponsored_company_car")]
    [DataRow("External","1","2","external_company_bike")]
    [DataRow("External","","","external_private")]
    [DataRow("External","2","3","")] // Third-party vehicle: a person decides.
    [DataRow("","2","1","")]
    public void CategoryComesFromSponsorshipAndVehicleHeldInTheMonth(string engagement,string type,string owner,string expected)
    {
        using var f=new PayFixture();
        string got=f.Store.Write((c,tx)=>
        {
            var rider=new JsonObject{["id"]="2000000077",["engagementType"]=engagement};
            if(type!="")
            {
                Store.Save(c,"vehicle",new(){["id"]="synthetic-r10-vehicle",["vehicleType"]=int.Parse(type),["ownershipType"]=int.Parse(owner)},tx);
                Store.Save(c,"vehicle_assignment",new(){["id"]="synthetic-r10-hold",["riderId"]="2000000077",["vehicleId"]="synthetic-r10-vehicle",["startedAt"]="2026-10-05T00:00:00Z",["endedAt"]=""},tx);
                Store.Save(c,"vehicle_assignment",new(){["id"]="synthetic-r10-old",["riderId"]="2000000077",["vehicleId"]="synthetic-r10-none",["startedAt"]="2026-01-01T00:00:00Z",["endedAt"]="2026-02-01T00:00:00Z"},tx);
            }
            return PayReviewCategory.Derive(c,tx,rider,"2026-10");
        });
        Assert.AreEqual(expected,got);
    }
    static ClaimsPrincipal Manager(params string[] extra)=>UserAccess.Principal(new JsonObject{["id"]="synthetic-manager",["username"]="synthetic-manager",["role"]="Reviewer",["customPermissions"]=new JsonArray(extra.Select(p=>(JsonNode?)JsonValue.Create(p)).ToArray())});
    [TestMethod]
    public void DelegatedUserManagementCannotEscalateAndTemporaryPasswordLocksEverythingElse()
    {
        Assert.IsTrue(AccessPolicy.Allowed(Manager("users.manage"),"PUT","/api/users/x"));Assert.IsFalse(AccessPolicy.Allowed(Manager(),"GET","/api/users"));
        Assert.IsFalse(AccessPolicy.Allowed(Manager("users.manage"),"PUT","/api/roles/Accountant/permissions"));
        var temp=UserAccess.Create("synthetic-temp","synthetic-password-1","Admin");UserAccess.SetPassword(temp,"synthetic-password-2",true);var principal=UserAccess.Principal(temp);
        Assert.IsFalse(AccessPolicy.Allowed(principal,"GET","/api/riders"));Assert.IsTrue(AccessPolicy.Allowed(principal,"POST","/api/me/password"));Assert.IsTrue(AccessPolicy.Allowed(principal,"GET","/api/me"));
        Assert.IsTrue(UserAccess.Password(temp,"synthetic-password-2"));
    }
    [TestMethod]
    public void EditedRoleTemplateAppliesOnReadWithoutTouchingTheUser()
    {
        using var f=new PayFixture();var user=UserAccess.Create("synthetic-role-user","synthetic-password-1","Reviewer");
        var (edited,stored)=f.Store.Write((c,tx)=>
        {
            Store.Save(c,UserAccess.RolePermissionsKind,new(){["id"]="Reviewer",["permissions"]=new JsonArray("pay.view")},tx);
            return (UserAccess.EffectivePermissions(UserAccess.WithRole(c,tx,user)),user.ContainsKey(UserAccess.RoleBaseline));
        });
        CollectionAssert.AreEqual(new[]{"pay.view"},edited);Assert.IsFalse(stored);
    }
    [TestMethod]
    public void SyncOverridesNeedAnAdministratorAReasonAndKnownKinds()
    {
        var input=new JsonObject{["acceptNewKinds"]=new JsonArray("ninja_daily"),["reason"]="Reviewed the counts"};
        Assert.IsNull(ErpSyncRequests.ReadOverrides(PayFixture.Checker,new JsonObject()));
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>ErpSyncRequests.ReadOverrides(Manager("erp.sync.run"),input)).Status);
        var admin=UserAccess.Principal(new JsonObject{["id"]="a",["username"]="synthetic-admin",["role"]="Admin"});
        Assert.AreEqual("ninja_daily",ErpSyncRequests.ReadOverrides(admin,input)!["acceptNewKinds"]![0]!.ToString());
        Assert.ThrowsExactly<ApiError>(()=>ErpSyncRequests.ReadOverrides(admin,new JsonObject{["acceptNewKinds"]=new JsonArray("ninja_daily")}));
        Assert.ThrowsExactly<ApiError>(()=>ErpSyncRequests.ReadOverrides(admin,new JsonObject{["acceptNewKinds"]=new JsonArray("made_up"),["reason"]="Reviewed"}));
    }
}
