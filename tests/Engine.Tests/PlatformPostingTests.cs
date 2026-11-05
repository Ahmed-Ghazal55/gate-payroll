using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class PlatformPostingTests
{
    static System.Security.Claims.ClaimsPrincipal Actor(string role="Admin")=>UserAccess.Principal(new JsonObject{["username"]="synthetic-gate-admin",["role"]=role});
    [TestMethod]
    public void GatesDefaultOffPersistAndRequireAnAuditedAdministrator()
    {
        string directory=Path.Combine(Path.GetTempPath(),"gate-platform-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(directory);foreach(string code in PlatformPosting.Codes)Assert.IsFalse(store.Read(c=>PlatformPosting.Enabled(c,code)));
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>PlatformPosting.Set(c,tx,Actor("FinancialManager"),"hs",new(){["enabled"]=true})));
            store.Write((c,tx)=>PlatformPosting.Set(c,tx,Actor(),"hs",new(){["enabled"]=true}));
            store=new Store(directory);Assert.IsTrue(store.Read(c=>PlatformPosting.Enabled(c,"hs")));Assert.IsFalse(store.Read(c=>PlatformPosting.Enabled(c,"ninja")));
            Assert.AreEqual(1,store.Read(c=>Store.List(c,"audit").Count));
            store.Write((c,tx)=>PlatformPosting.Set(c,tx,Actor(),"hs",new(){["enabled"]=true}));Assert.AreEqual(1,store.Read(c=>Store.List(c,"audit").Count));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(directory,true);}
    }
    [TestMethod]
    public void HsUsesApprovedTargetAccountsAndLeavesLegacyPostingUntouched()
    {
        string directory=Path.Combine(Path.GetTempPath(),"gate-hs-chart-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(directory);string company=FinancialCore.Sponsors[0].Id;
            JsonArray Lines()=>new(FinancialCore.CreateJournalLine(PayPlatforms.For("hs").Receivable,company,100,0,targetChart:true),FinancialCore.CreateJournalLine("411102",company,0,100,targetChart:true));
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>PlatformPosting.Post(c,tx,"2026-10","synthetic-hs",Lines(),"synthetic-checker")));
            Assert.AreEqual(0,store.Read(c=>Store.List(c,"journal").Count));
            Assert.ThrowsExactly<ApiError>(()=>FinancialCore.CreateJournalLine("1241",company,100,0,targetChart:true));Assert.ThrowsExactly<ApiError>(()=>FinancialCore.CreateJournalLine("2141",company,0,100,targetChart:true));
            Assert.ThrowsExactly<ApiError>(()=>FinancialCore.CreateJournalLine("112802",company,100,0));
            store.Write((c,tx)=>PlatformPosting.Set(c,tx,Actor(),"hs",new(){["enabled"]=true}));
            var journal=store.Write((c,tx)=>PlatformPosting.Post(c,tx,"2026-10","synthetic-hs",Lines(),"synthetic-checker"));Assert.AreEqual("target",journal.S("chart"));
            var report=store.Read(c=>FinanceViews.TrialBalance(c,Actor(),company,"2026-10","2026-10","target"));
            var rows=report["companies"]![0]!["rows"]!.AsArray().OfType<JsonObject>();Assert.AreEqual(100m,rows.Single(r=>r.S("code")=="112805").N("periodDebit"));
            Assert.AreEqual(100m,FinancialCore.CreateJournalLine("1210",company,100,0).N("debit"));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(directory,true);}
    }
}
