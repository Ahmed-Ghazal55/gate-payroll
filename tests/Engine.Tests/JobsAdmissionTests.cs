using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

public sealed class JobsTestClock : TimeProvider
{
    public DateTimeOffset Now {get;set;}=DateTimeOffset.Parse("2026-11-05T07:00:00Z");
    public override DateTimeOffset GetUtcNow()=>Now;
}
[TestClass]
public sealed class JobsAdmissionTests
{
    sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName {get;set;}="Testing";public string ApplicationName {get;set;}="Synthetic";
        public string ContentRootPath {get;set;}=Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider {get;set;}=new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
    static JobsOptions Options()=>JobsOptions.Read(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
    { ["Jobs:Enabled"]="true",["Jobs:ErpSync:Enabled"]="true",["ConnectionStrings:Jobs"]="Server=127.0.0.1,1;Database=GateJobsTest_options;Integrated Security=true",["ConnectionStrings:DefaultConnection"]="Server=127.0.0.1,1;Database=GateJobsTest_options;Integrated Security=true" }).Build(),new EnvironmentStub());
    static void WithStore(Action<Store> test)
    {
        string path=Path.Combine(Path.GetTempPath(),"gate-sync-admission-"+Guid.NewGuid().ToString("N"));
        try{test(new Store(path));}finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(path))Directory.Delete(path,true);}
    }
    static void Finish(Store store,string id,DateTimeOffset at,string mode="commit",string status="completed")
    {ErpSyncRequests.Start(store,id,at);ErpSyncRuns.Record(store,"erp-sync:"+id.Split(':')[1],at,at,status,[],mode:mode,requestId:id);}
    [TestMethod]
    public void CooldownAndActiveReservationCoverAllTriggersAndSurviveNewStoreInstances()
    {
        WithStore(store=>
        {
            var at=DateTimeOffset.Parse("2026-11-05T07:00:00Z");var options=Options();Assert.IsTrue(options.CatchUpEnabled);Assert.AreEqual(24d,options.CatchUpHours);Assert.IsFalse(options.DailyEnabled);
            string id=ErpSyncRequests.Reserve(store,options,false,"manual","syn-operator",at);ErpSyncRequests.Link(store,id,"101");
            var other=new Store(store.DirectoryPath);
            foreach(string trigger in new[]{"manual","dry_run","startup","daily"})Assert.AreEqual("sync_in_progress",Assert.ThrowsExactly<ImportFailure>(()=>ErpSyncRequests.Reserve(other,options,trigger=="dry_run",trigger,"syn-operator",at.AddHours(2))).Code);
            Finish(store,id,at);Assert.AreEqual("succeeded",store.Read(c=>ErpSyncRequests.View(c,"101")).S("status"));
            Assert.AreEqual("sync_cooldown",Assert.ThrowsExactly<ImportFailure>(()=>ErpSyncRequests.Reserve(other,options,true,"dry_run","syn-operator",at.AddMinutes(15).AddTicks(-1))).Code);
            string next=ErpSyncRequests.Reserve(other,options,false,"startup","system:startup",at.AddMinutes(15));Assert.AreNotEqual(id,next);
            Assert.AreEqual(2,store.Read(c=>Store.List(c,"audit").Count(a=>a.S("action")=="jobs.erp_sync_requested")));
        });
    }
    [TestMethod]
    public void PausingDoesNotEraseTheReservationOrCooldownAndPreventsTheQueuedCommit()
    {
        WithStore(store=>
        {
            var at=DateTimeOffset.UtcNow;var id=ErpSyncRequests.Reserve(store,Options(),false,"manual","syn-operator",at);
            var actor=UserAccess.Principal(new(){["username"]="syn-super",["role"]="SuperAdmin"});
            JobsControl.Change(store,actor,new(){["reason"]="Synthetic pause"},true,Options(),new DisabledJobsSchedule());
            var control=store.Read(c=>Store.Get(c,JobsControl.Id,JobsControl.Kind))!;Assert.AreEqual(id,control.S("activeRequestId"));Assert.AreEqual(at.ToString("O"),control.S("lastAcceptedAt"));
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Jobs:Enabled"]="true",["Jobs:ErpSync:Enabled"]="true"}).Build();
            Assert.ThrowsExactly<ImportFailure>(()=>store.Write((c,tx)=>{JobsControl.EnsureMayCommit(c,tx,config);return true;}));
        });
    }
    [TestMethod]
    public void ParallelAdmissionsAcrossStoreInstancesHaveOneWinner()
    {
        WithStore(store=>
        {
            var other=new Store(store.DirectoryPath);int admitted=0,rejected=0;var at=DateTimeOffset.UtcNow;
            Parallel.ForEach(new[]{store,other},target=>
            {try{ErpSyncRequests.Reserve(target,Options(),false,"manual","syn-operator",at);Interlocked.Increment(ref admitted);}catch(ImportFailure e){Assert.AreEqual("sync_in_progress",e.Code);Interlocked.Increment(ref rejected);}});
            Assert.AreEqual(1,admitted);Assert.AreEqual(1,rejected);Assert.AreEqual(1,store.Read(c=>Store.List(c,ErpSyncRequests.Kind).Count));
        });
    }
    [TestMethod]
    public void PreviewSuccessCannotSuppressStartupCatchUpAndCountsViewHidesActorAndPayload()
    {
        WithStore(store=>
        {
            var at=DateTimeOffset.Parse("2026-11-05T07:00:00Z");string request=ErpSyncRequests.Reserve(store,Options(),true,"dry_run","SYN_ACTOR_NOT_PUBLIC",at);ErpSyncRequests.Link(store,request,"102");
            Assert.AreEqual("queued",store.Read(c=>ErpSyncRequests.View(c,"102")).S("status"));ErpSyncRequests.Start(store,request,at);
            Assert.AreEqual("running",store.Read(c=>ErpSyncRequests.View(c,"102")).S("status"));
            store.Write((c,tx)=>{var row=Store.Get(c,request,ErpSyncRequests.Kind,tx)!;row["rawSource"]="DO_NOT_EMIT";Store.Save(c,ErpSyncRequests.Kind,row,tx);return true;});
            Finish(store,request,at,"dry_run");Assert.IsNull(store.Read(ErpSyncRequests.LastSuccess));
            var view=store.Read(c=>ErpSyncRequests.View(c,"102"));Assert.DoesNotContain("DO_NOT_EMIT",view.ToJsonString());Assert.DoesNotContain("SYN_ACTOR_NOT_PUBLIC",view.ToJsonString());
            Assert.AreEqual(404,Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>ErpSyncRequests.View(c,"103"))).Status);
            Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>ErpSyncRequests.View(c,"not-a-job")));
            string commit=ErpSyncRequests.Reserve(store,Options(),false,"manual","syn-operator",at.AddMinutes(15));Finish(store,commit,at.AddMinutes(15));Assert.AreEqual(at.AddMinutes(15),store.Read(ErpSyncRequests.LastSuccess));
        });
    }
    [TestMethod]
    public void PermissionSeedingReachesExistingUsersOnceAndRespectsExplicitRevocation()
    {
        WithStore(store=>
        {
            store.Write((c,tx)=>{Store.Save(c,"user",new(){["id"]="syn-manager",["username"]="syn-manager",["role"]="FinancialManager",["customPermissions"]=new JsonArray("pay.view")},tx);Store.Save(c,"user",new(){["id"]="syn-denied",["username"]="syn-denied",["role"]="FinancialManager",["deniedPermissions"]=new JsonArray(ErpSyncRequests.Permission)},tx);return true;});
            ErpSyncRequests.SeedPermission(store);
            var manager=store.Read(c=>Store.Get(c,"syn-manager","user"))!;Assert.IsTrue(UserAccess.EffectivePermissions(manager).Contains(ErpSyncRequests.Permission));Assert.IsTrue(manager["customPermissions"]!.AsArray().Any(p=>p?.ToString()=="pay.view"));
            Assert.IsFalse(UserAccess.EffectivePermissions(store.Read(c=>Store.Get(c,"syn-denied","user"))!).Contains(ErpSyncRequests.Permission));
            store.Write((c,tx)=>{manager["customPermissions"]=new JsonArray();Store.Save(c,"user",manager,tx);return true;});ErpSyncRequests.SeedPermission(store);
            Assert.IsFalse(UserAccess.EffectivePermissions(store.Read(c=>Store.Get(c,"syn-manager","user"))!).Contains(ErpSyncRequests.Permission));
        });
        foreach(string role in new[]{"SystemAdmin","FinancialManager"})
        {
            var user=UserAccess.Create("syn-"+role,"SyntheticPassword-123",role);Assert.IsTrue(user["customPermissions"]!.AsArray().Any(p=>p?.ToString()==ErpSyncRequests.Permission));
            var actor=UserAccess.Principal(user);Assert.IsTrue(AccessPolicy.Allowed(actor,"POST","/api/system/jobs/erp-sync/run"));Assert.IsTrue(AccessPolicy.Allowed(actor,"GET","/api/system/jobs/erp-sync/1"));Assert.IsFalse(AccessPolicy.Allowed(actor,"POST","/api/system/jobs/erp-sync/pause"));Assert.IsFalse(AccessPolicy.Allowed(actor,"GET","/api/system/jobs"));
        }
    }
}
