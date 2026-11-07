using System.Data.Common;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class StorageCutoverTests
{
    static JsonObject Row(string id,string kind,JsonObject? fields=null)
    {var body=fields?.Clone()??new();body["id"]=id;return new(){["id"]=id,["kind"]=kind,["body"]=body};}
    static JsonArray Source()=>new(Row("synthetic-rider","rider",new(){["name"]="Synthetic old HR",["iban"]="synthetic-bank-choice",["policyId"]="synthetic-payroll-policy"}),Row("synthetic-debt","debt",new(){["remainingBalance"]=12}),Row("synthetic-user","user",new(){["username"]="synthetic-operator"}));
    static JsonArray TargetRows()=>new(Row("synthetic-rider","rider",new(){["name"]="Synthetic current HR",["iban"]="synthetic-ERP-bank",["cityId"]="synthetic-city"}),Row("synthetic-vehicle","vehicle",new(){["plate"]="synthetic-plate"}));
    static void Seed(Store store,JsonArray rows)=>store.Write((c,tx)=>{foreach(var r in rows.OfType<JsonObject>())Store.Save(c,r.S("kind"),(JsonObject)r["body"]!,tx);return true;});

    [TestMethod]
    public void HrWinsPayrollWinsAndEveryOriginalSurvivesAtomicIdempotentMerge()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-cutover-"+Guid.NewGuid().ToString("N"));
        try{var store=new Store(dir);Seed(store,TargetRows());Exercise(store);}
        finally{SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
    }
    static void Exercise(Store store)
    {
        var target=store.Read(c=>StorageTransfer.ReadSnapshot(c));var plan=StorageTransfer.PlanMerge(Source(),target);string fingerprint=StorageTransfer.CutoverHash(plan);
        Assert.IsTrue(plan["summary"]!.AsObject().B("ready"));
        string before=store.Read(c=>StorageTransfer.CutoverHash(StorageTransfer.ReadSnapshot(c)));
        // Planning has no persistence side effects.
        Assert.AreEqual(before,store.Read(c=>StorageTransfer.CutoverHash(StorageTransfer.ReadSnapshot(c))));
        var result=store.Read(c=>StorageTransfer.ApplyMerge(c,plan,fingerprint));Assert.IsTrue(result.B("verified"));
        var rider=store.Read(c=>Store.Get(c,"synthetic-rider","rider"))!;
        Assert.AreEqual("Synthetic current HR",rider.S("name"));Assert.AreEqual("synthetic-bank-choice",rider.S("iban"));Assert.AreEqual("synthetic-payroll-policy",rider.S("policyId"));Assert.AreEqual("synthetic-city",rider.S("cityId"));
        Assert.AreEqual(64,rider["mergedFrom"]!["sqliteFingerprint"]!.GetValue<string>().Length);
        var archives=store.Read(c=>Store.List(c,"cutover_origin"));Assert.AreEqual(4,archives.Count);
        Assert.IsTrue(archives.Any(a=>a.S("side")=="source"&&a["record"]!["body"]!["name"]?.GetValue<string>()=="Synthetic old HR"));
        Assert.IsTrue(archives.Any(a=>a.S("side")=="target"&&a["record"]!["body"]!["iban"]?.GetValue<string>()=="synthetic-ERP-bank"));
        Assert.AreEqual(12m,store.Read(c=>Store.Get(c,"synthetic-debt","debt"))!.N("remainingBalance"));
        Assert.IsNotNull(store.Read(c=>Store.Get(c,"synthetic-vehicle","vehicle")));
        string after=store.Read(c=>StorageTransfer.CutoverHash(StorageTransfer.ReadSnapshot(c)));
        Assert.IsTrue(store.Read(c=>StorageTransfer.ApplyMerge(c,plan,fingerprint)).B("verified"));
        Assert.AreEqual(after,store.Read(c=>StorageTransfer.CutoverHash(StorageTransfer.ReadSnapshot(c))));
        if(store.Provider=="mssql")Assert.IsTrue(store.Read(c=>Reporting.Check(c)).B("consistent"));
    }
    [TestMethod]
    public void UnknownConflictAndCrossKindCollisionProduceOnlyHashedIdentifiers()
    {
        var source=new JsonArray(Row("synthetic-private-id","rider",new(){["unknownField"]="source"}),Row("another-private-id","user",new(){["username"]="synthetic-a"}));
        var target=new JsonArray(Row("synthetic-private-id","rider",new(){["unknownField"]="target"}),Row("another-private-id","setting"));
        var p=StorageTransfer.PlanMerge(source,target);var summary=p["summary"]!.AsObject();Assert.IsFalse(summary.B("ready"));Assert.AreEqual(2,summary["conflicts"]!.AsArray().Count);
        Assert.IsFalse(summary.ToJsonString().Contains("private-id"));Assert.IsFalse(summary.ToJsonString().Contains("unknownField"));
    }
    [TestMethod]
    public void LogicalUniquenessAndCaseCollisionAreRefusedBeforeWriting()
    {
        var p=StorageTransfer.PlanMerge(new(Row("new-user","user",new(){["username"]="SYNTHETIC-USER"})),new(Row("old-user","user",new(){["username"]="synthetic-user"})));
        Assert.IsFalse(p["summary"]!.AsObject().B("ready"));
        p=StorageTransfer.PlanMerge(new(Row("SYNTHETIC","setting")),new(Row("synthetic","setting")));Assert.IsFalse(p["summary"]!.AsObject().B("ready"));
    }
    [TestMethod]
    public void StaleOrTamperedPreviewAndUnresolvedConflictNeverWriteAnything()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-cutover-refusal-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);Seed(store,TargetRows());var plan=StorageTransfer.PlanMerge(Source(),store.Read(c=>StorageTransfer.ReadSnapshot(c)));string hash=StorageTransfer.CutoverHash(plan);
            Seed(store,new(Row("concurrent-setting","setting")));string before=store.Read(c=>StorageTransfer.CutoverHash(StorageTransfer.ReadSnapshot(c)));
            Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>StorageTransfer.ApplyMerge(c,plan,hash)));
            plan["rows"]![0]!["body"]!["injected"]=true;Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>StorageTransfer.ApplyMerge(c,plan,hash)));
            Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>StorageTransfer.ApplyMerge(c,plan,StorageTransfer.CutoverHash(plan))));
            Assert.AreEqual(before,store.Read(c=>StorageTransfer.CutoverHash(StorageTransfer.ReadSnapshot(c))));
        }
        finally{SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
    }
    [TestMethod]
    public void ReadOnlySnapshotDoesNotInitializeMissingTablesOrChangeFile()
    {
        string file=Path.Combine(Path.GetTempPath(),"gate-readonly-"+Guid.NewGuid().ToString("N")+".db");
        try
        {
            using(var c=new SqliteConnection("Data Source="+file)){c.Open();using var q=c.CreateCommand();q.CommandText="CREATE TABLE records(id TEXT PRIMARY KEY,kind TEXT NOT NULL,body TEXT NOT NULL)";q.ExecuteNonQuery();}
            byte[] before=File.ReadAllBytes(file);using(var c=new SqliteConnection("Data Source="+file+";Mode=ReadOnly;Pooling=False")){c.Open();using var tx=c.BeginTransaction(deferred:true);Assert.AreEqual(0,StorageTransfer.ReadSnapshot(c,tx).Count);tx.Commit();}
            CollectionAssert.AreEqual(before,File.ReadAllBytes(file));
        }
        finally{SqliteConnection.ClearAllPools();File.Delete(file);}
    }
    [TestMethod]
    public void SqlServerSyntheticFullCutoverAndCheckpointWithoutSQLiteFile()
    {
        string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");if(string.IsNullOrWhiteSpace(configured)){Assert.Inconclusive("Explicit isolated loopback SQL required.");return;}
        var b=new SqlConnectionStringBuilder(configured);Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(b.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        string db="GateCutoverTest_"+Guid.NewGuid().ToString("N"),dir=Path.Combine(Path.GetTempPath(),db);b.InitialCatalog="master";using var admin=new SqlConnection(b.ToString());admin.Open();using(var q=admin.CreateCommand()){q.CommandText="CREATE DATABASE ["+db+"]";q.ExecuteNonQuery();}
        try
        {
            b.InitialCatalog=db;var store=new Store(dir,b.ToString());store.Write((c,tx)=>Reporting.Rebuild(c,tx));Seed(store,TargetRows());Exercise(store);
            string before=store.Read(c=>StorageTransfer.CutoverHash(StorageTransfer.ReadSnapshot(c)));var restarted=new Store(dir,b.ToString());
            Assert.IsTrue(restarted.DataPreservationVerified);Assert.IsTrue(restarted.ReleaseCheckpointPresent);Assert.IsFalse(File.Exists(Path.Combine(dir,"payroll.db")));
            Assert.AreEqual(before,restarted.Read(c=>StorageTransfer.CutoverHash(StorageTransfer.ReadSnapshot(c))));
        }
        finally{SqlConnection.ClearAllPools();using var q=admin.CreateCommand();q.CommandText="ALTER DATABASE ["+db+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+db+"]";q.ExecuteNonQuery();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
