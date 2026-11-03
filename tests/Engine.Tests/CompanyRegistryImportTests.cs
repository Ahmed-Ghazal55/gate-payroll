using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class CompanyRegistryImportTests
{
    static System.Security.Claims.ClaimsPrincipal Actor(string name="synthetic-admin",string role="Admin")=>UserAccess.Principal(new JsonObject{["username"]=name,["role"]=role});
    static JsonObject Input()=>new(){["companies"]=new JsonArray(FinancialCore.Sponsors.Select((s,i)=>(JsonNode)new JsonObject{["code"]=s.Code,["nameAr"]="Synthetic company",["unifiedNumber"]="7"+(i+1).ToString("D9"),["crNumber"]="4"+(i+1).ToString("D9"),["capitalSar"]=1000,["crVersion"]=2,["issueDate"]="2020-01-01",["issueDateHijri"]="1441-05-06",["annualConfirmation"]="2028-01-01",["annualConfirmationHijri"]="1449-08-04",["activitiesIncomplete"]=true,["activities"]=new JsonArray(new JsonObject{["isic"]="492311",["nameAr"]="Synthetic activity"})}).ToArray())};
    sealed class Fixture:IDisposable
    {
        public string DirectoryPath {get;}=Path.Combine(Path.GetTempPath(),"gate-certificate-"+Guid.NewGuid().ToString("N"));
        public Store Store {get;}
        public Fixture(){Store=new Store(DirectoryPath);}
        public void Dispose(){Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(DirectoryPath,true);}
    }
    static JsonObject Confirmation(JsonObject preview)=>new(){["rows"]=preview["rows"]!.DeepClone(),["fingerprint"]=preview.S("fingerprint"),["confirm"]=true};
    [TestMethod]
    public void PreviewIsReadOnlyAndCommitIsAuditedIdempotentAndAdditive()
    {
        using var f=new Fixture();string id=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>{Store.Save(c,"company",new(){["id"]=id,["customPolicy"]="preserved"},tx);return true;});
        var preview=f.Store.Read(c=>CompanyRegistryImport.Preview(c,Actor(),Input()));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"audit").Count));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"commercial_registration").Count));
        f.Store.Write((c,tx)=>CompanyRegistryImport.Commit(c,tx,Actor(),Confirmation(preview)));
        Assert.AreEqual(3,f.Store.Read(c=>Store.List(c,"company").Count));Assert.AreEqual(3,f.Store.Read(c=>Store.List(c,"commercial_registration").Count));Assert.AreEqual(6,f.Store.Read(c=>Store.List(c,"audit").Count));
        Assert.AreEqual("preserved",f.Store.Read(c=>Store.Get(c,id,"company")!.S("customPolicy")));
        Assert.IsTrue(f.Store.Read(c=>Store.Get(c,id,"company")!.B("activitiesIncomplete")));
        var next=f.Store.Read(c=>CompanyRegistryImport.Preview(c,Actor(),Input()));
        Assert.AreEqual(3,next["counts"]!["company"]!["unchanged"]!.GetValue<int>());
        f.Store.Write((c,tx)=>CompanyRegistryImport.Commit(c,tx,Actor(),Confirmation(next)));
        Assert.AreEqual(6,f.Store.Read(c=>Store.List(c,"audit").Count));
    }
    [TestMethod]
    public void SignaturesRejectTamperingOtherActorAndStaleCompanyState()
    {
        using var f=new Fixture();var preview=f.Store.Read(c=>CompanyRegistryImport.Preview(c,Actor(),Input()));
        var tampered=Confirmation(preview);tampered["rows"]![0]!["capitalSar"]=12;
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>CompanyRegistryImport.Commit(c,tx,Actor(),tampered)));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>CompanyRegistryImport.Commit(c,tx,Actor("other-admin"),Confirmation(preview))));
        f.Store.Write((c,tx)=>{Store.Save(c,"company",new(){["id"]=FinancialCore.Sponsors[0].Id,["custom"]="concurrent"},tx);return true;});
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>CompanyRegistryImport.Commit(c,tx,Actor(),Confirmation(preview))));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"audit").Count));
    }
    [TestMethod]
    public void IdentityConflictsDuplicatesAndInvalidFieldsStopBeforeWrites()
    {
        using var f=new Fixture();var duplicate=Input();duplicate["companies"]![1]!["unifiedNumber"]=duplicate["companies"]![0]!["unifiedNumber"]!.DeepClone();
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>CompanyRegistryImport.Preview(c,Actor(),duplicate)));
        var invalid=Input();invalid["companies"]![0]!["capitalSar"]="not-a-number";Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>CompanyRegistryImport.Preview(c,Actor(),invalid)));
        f.Store.Write((c,tx)=>{Store.Save(c,"company",new(){["id"]=FinancialCore.Sponsors[0].Id,["unifiedNumber"]="7"+99.ToString("D9")},tx);return true;});
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>CompanyRegistryImport.Preview(c,Actor(),Input())));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>CompanyRegistryImport.Preview(c,Actor(role:"Reviewer"),Input())));
        var manager=UserAccess.Principal(new JsonObject{["username"]="synthetic-manager",["role"]="Reviewer",["customPermissions"]=new JsonArray("companies.manage")});
        Assert.IsFalse(AccessPolicy.Allowed(manager,"POST","/api/companies/import/preview"));Assert.IsTrue(AccessPolicy.Allowed(Actor(),"POST","/api/companies/import/preview"));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"audit").Count));
    }
}
