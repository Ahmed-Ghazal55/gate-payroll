using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Data.Sqlite;
using Payroll;

[TestClass]
public sealed class ReleaseSafetyTests
{
    [TestMethod]
    public void UpgradePreservesEveryLegacyDocumentAndCreatesVerifiablePrivateBackup()
    {
        string directory=Path.Combine(Path.GetTempPath(),"gate-upgrade-"+Guid.NewGuid().ToString("N"));
        try
        {
            var oldStore=new Store(directory);
            oldStore.Write((c,tx)=>{foreach(string kind in new[]{"rider","user","run","ledger","debt","campaign","submission","setting"})Store.Save(c,kind,new(){["id"]=kind+"-old",["legacy"]="يبقى كما هو",["amount"]=123.45m},tx);return true;});
            string before=oldStore.Read(c=>JsonSerializer.Serialize(ReleaseSafety.Capture(c)));
            var upgraded=new Store(directory);Assert.AreEqual(before,upgraded.Read(c=>JsonSerializer.Serialize(ReleaseSafety.Capture(c))));
            string manifest=Path.Combine(directory,"release-backups",ReleaseSafety.Version,"checkpoint.json");
            var report=JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();Assert.AreEqual(before,report["before"]!.ToJsonString());
            using var backup=new SqliteConnection("Data Source="+Path.Combine(Path.GetDirectoryName(manifest)!,report.S("snapshot"))+";Mode=ReadOnly;Pooling=False");backup.Open();
            Assert.AreEqual(before,JsonSerializer.Serialize(ReleaseSafety.Capture(backup)));
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    [TestMethod]
    public void FinancialPreviewRejectsChangedRowsActorAndStaleState()
    {
        string directory=Path.Combine(Path.GetTempPath(),"gate-preview-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(directory);var rows=new JsonArray(new JsonObject{["iqama"]="2000000001",["totalAmount"]=12.50m});
            string token=store.Read(c=>FinancialImports.Issue(c,"opening",rows,"admin"));
            var input=new JsonObject{["rows"]=rows.DeepClone(),["fingerprint"]=token,["confirm"]=true};
            store.Write((c,tx)=>{FinancialImports.Verify(c,tx,input,"opening","admin");return true;});
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>{FinancialImports.Verify(c,tx,input,"opening","other");return true;}));
            input["rows"]![0]!["totalAmount"]=99;
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>{FinancialImports.Verify(c,tx,input,"opening","admin");return true;}));
            input["rows"]=rows.DeepClone();store.Write((c,tx)=>{Store.Save(c,"debt",new(){["id"]="new"},tx);return true;});
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>{FinancialImports.Verify(c,tx,input,"opening","admin");return true;}));
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    [TestMethod]
    public void ReviewerCannotReadFinancialsFilesOrDecideFinancialApprovals()
    {
        foreach(string path in new[]{"/api/finance/journals","/api/debts","/api/promissory-notes","/api/riders/2000000001/profile-360","/api/system/release-check"})Assert.IsFalse(UserAccess.Allowed("Reviewer","GET",path));
        Assert.IsFalse(UserAccess.Allowed("Reviewer","POST","/api/approvals/id/decide"));
        Assert.IsFalse(UserAccess.Allowed("Accountant","POST","/api/rider-mobile/firebase-config"));
        Assert.IsFalse(UserAccess.Allowed("Accountant","POST","/api/approvals/id/decide"));
    }
    [TestMethod]
    public void UnknownSponsorsAndNonVaultAccountsAreRejectedAndLegacyPaidBalanceSurvives()
    {
        Assert.ThrowsExactly<ApiError>(()=>FinancialCore.ResolveSponsor("unknown-sponsor"));
        Assert.ThrowsExactly<ApiError>(()=>FinancialCore.ResolveVault("1210"));
        Assert.AreEqual(200m,Debts.PaidAmount(new(){["totalAmount"]=600m,["remainingBalance"]=400m}));
    }
}
