using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class ErpSyncRunTests
{
    static ClaimsPrincipal User(string role)=>UserAccess.Principal(new(){["id"]="syn-user",["username"]="syn-user",["role"]=role});
    static void Run(Action<Store> body)
    {
        string path=Path.Combine(Path.GetTempPath(),"gate-sync-runs-"+Guid.NewGuid().ToString("N"));
        try{body(new Store(path));}finally{SqliteConnection.ClearAllPools();if(Directory.Exists(path))Directory.Delete(path,true);}
    }
    [TestMethod]
    public void RefusalStoresOneCountsRecordAndOnlyAdministratorsSeeItsNotification()
    {
        Run(store=>
        {
            string id="erp-sync:"+Guid.NewGuid().ToString("N");var at=DateTimeOffset.UtcNow;
            ErpSyncRuns.Record(store,id,at,at,"refused",[new("fuel_card",10,10,0,0,0)],"new_kind_requires_acceptance:fuel_card");
            ErpSyncRuns.Record(store,id,at,at,"refused",[new("fuel_card",10,10,0,0,0)],"new_kind_requires_acceptance:fuel_card");
            Assert.AreEqual(1,store.Read(c=>Store.List(c,ErpSyncRuns.Kind).Count));Assert.AreEqual(1,store.Read(c=>Store.List(c,"sys_notification").Count));Assert.AreEqual(0,store.Read(c=>Store.List(c,"rider").Count));
            foreach(string role in new[]{"Accountant","Reviewer"})
            {Assert.AreEqual(0,store.Read(c=>Notifications.Inbox(c,User(role)).Count));Assert.AreEqual(0,store.Read(c=>Notifications.Center(c,User(role)).Count));Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>ErpSyncRuns.Recent(c,User(role)))).Status);}
            Assert.AreEqual(1,store.Read(c=>Notifications.Inbox(c,User("Admin")).Count));Assert.AreEqual("approvals",store.Read(c=>Store.List(c,"sys_notification").Single().S("category")));
            var run=store.Read(c=>ErpSyncRuns.Recent(c,User("SuperAdmin"))).Single();Assert.AreEqual("refused",run.S("status"));Assert.AreEqual(10m,run["counts"]![0]!.N("new"));
        });
    }
    [TestMethod]
    public void LatestRunsAreBoundedAndExcludeUnexpectedPayloadFields()
    {
        Run(store=>
        {
            var at=DateTimeOffset.UtcNow;
            for(int i=0;i<3;i++)ErpSyncRuns.Record(store,"erp-sync:"+Guid.NewGuid().ToString("N"),at.AddMinutes(i),at.AddMinutes(i),"completed",[]);
            store.Write((c,tx)=>{var row=Store.List(c,ErpSyncRuns.Kind,tx).First();row["rawSource"]="DO_NOT_EMIT_PRIVATE_PAYLOAD";Store.Save(c,ErpSyncRuns.Kind,row,tx);return true;});
            var recent=store.Read(c=>ErpSyncRuns.Recent(c,User("Admin"),2));Assert.AreEqual(2,recent.Count);Assert.DoesNotContain("DO_NOT_EMIT_PRIVATE_PAYLOAD",new JsonArray(recent.Select(x=>(JsonNode)x).ToArray()).ToJsonString());
            Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>ErpSyncRuns.Recent(c,User("Admin"),101)));
        });
    }
    [TestMethod]
    public void RunRecordRejectsInvalidStatusOrUnboundedKindsBeforeAnyWrite()
    {
        Run(store=>
        {
            var at=DateTimeOffset.UtcNow;string id="erp-sync:"+Guid.NewGuid().ToString("N");
            Assert.ThrowsExactly<ApiError>(()=>ErpSyncRuns.Record(store,id,at,at,"raw",[]));
            Assert.ThrowsExactly<ApiError>(()=>ErpSyncRuns.Record(store,id,at,at,"failed",[new("private value",1,1,0,0,0)]));
            Assert.ThrowsExactly<ApiError>(()=>ErpSyncRuns.Record(store,id,at,at,"failed",[],"contains spaces and arbitrary data"));
            Assert.AreEqual(0,store.Read(c=>Store.List(c,ErpSyncRuns.Kind).Count));
        });
    }
    [TestMethod]
    public void CountsRunRecordsAndBoundedQueryWorkOnAnIsolatedLocalSqlServer()
    {
        string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if(string.IsNullOrWhiteSpace(configured)){Assert.Inconclusive("Requires an isolated local SQL test connection.");return;}
        var options=new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(configured);
        if(!System.Text.RegularExpressions.Regex.IsMatch(options.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)){Assert.Inconclusive("Loopback SQL only.");return;}
        string database="GateErpSyncTest_"+Guid.NewGuid().ToString("N"),directory=Path.Combine(Path.GetTempPath(),database);options.InitialCatalog="master";
        using var admin=new Microsoft.Data.SqlClient.SqlConnection(options.ConnectionString);admin.Open();using(var create=admin.CreateCommand()){create.CommandText="CREATE DATABASE ["+database+"]";create.ExecuteNonQuery();}
        try
        {
            options.InitialCatalog=database;var store=new Store(directory,options.ConnectionString);var now=DateTimeOffset.UtcNow;
            ErpSyncRuns.Record(store,"erp-sync:"+Guid.NewGuid().ToString("N"),now,now,"refused",[new("fuel_card",3,3,0,0,0)],"new_kind_requires_acceptance:fuel_card");
            Assert.AreEqual(1,store.Read(c=>ErpSyncRuns.Recent(c,User("Admin"),1).Count));Assert.AreEqual(1,store.Read(c=>Notifications.Inbox(c,User("Admin")).Count));Assert.AreEqual(0,store.Read(c=>Notifications.Center(c,User("Accountant")).Count));
        }
        finally
        {
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();using var drop=admin.CreateCommand();drop.CommandText="ALTER DATABASE ["+database+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+database+"]";drop.ExecuteNonQuery();if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
}
