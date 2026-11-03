using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class CompaniesTests
{
    [TestMethod]
    public void ExistingDatabasesReceiveIdempotentCompaniesAndPermissions()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-companies-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);store.Write((c,tx)=>{Store.Save(c,"setting",new(){["id"]="synthetic-existing-setting",["value"]="preserved"},tx);return true;});
            string bw=FinancialCore.Sponsors.Single(s=>s.Code=="BW").Id;
            Companies.Seed(store);var admin=UserAccess.Principal(new JsonObject{["username"]="synthetic-admin",["role"]="Admin"});
            Assert.IsTrue(AccessPolicy.Has(admin,"companies.view"));Assert.IsTrue(AccessPolicy.Has(admin,"companies.manage"));
            store.Write((c,tx)=>Companies.SaveCompany(c,tx,admin,new(){["nameAr"]="Synthetic Updated Company"},bw));
            store=new Store(dir);Companies.Seed(store);
            Assert.AreEqual(3,store.Read(c=>Store.List(c,"company").Count));
            Assert.AreEqual("Synthetic Updated Company",store.Read(c=>Store.Get(c,bw,"company")!.S("nameAr")));
            Assert.AreEqual("preserved",store.Read(c=>Store.Get(c,"synthetic-existing-setting","setting")!.S("value")));
            Assert.AreEqual(3,store.Read(c=>Store.List(c,"audit").Count(a=>a.S("action")=="company.seeded")));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
    }
    [TestMethod]
    public void CompanyChildrenEnforceScopeDatesAndRegistrationUniqueness()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-company-children-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);Companies.Seed(store);string bw=FinancialCore.Sponsors.Single(s=>s.Code=="BW").Id, ex=FinancialCore.Sponsors.Single(s=>s.Code=="EX").Id;
            var admin=UserAccess.Principal(new JsonObject{["username"]="synthetic-admin",["role"]="Admin"});
            var scoped=UserAccess.Principal(new JsonObject{["username"]="synthetic-scoped",["role"]="Reviewer",["customPermissions"]=new JsonArray("companies.view","companies.manage"),["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(ex)}});
            Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>Companies.Get(c,null,bw,scoped)));
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>Companies.SaveRegistration(c,tx,bw,admin,new(){["number"]="SYN-CR",["issueDate"]="2027-01-01",["expiryDate"]="2026-01-01"})));
            store.Write((c,tx)=>Companies.SaveRegistration(c,tx,bw,admin,new(){["number"]="SYN-CR",["expiryDate"]="2027-01-01"}));
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>Companies.SaveRegistration(c,tx,ex,admin,new(){["number"]="SYN-CR"})));
            Assert.AreEqual(1,store.Read(c=>Store.List(c,"commercial_registration").Count));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
    }
}
