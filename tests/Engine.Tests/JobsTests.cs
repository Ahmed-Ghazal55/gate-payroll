using System.Text.Json.Nodes;
using ErpImport;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class JobsTests
{
    static IConfiguration Config(bool enabled=true,bool erp=true)=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
    {
        ["Jobs:Enabled"]=enabled.ToString(),["Jobs:ErpSync:Enabled"]=erp.ToString(),
        ["ConnectionStrings:Jobs"]="Server=127.0.0.1,1;Database=GateJobsTest_synthetic;Integrated Security=true;Encrypt=true",
        ["ConnectionStrings:DefaultConnection"]="Server=127.0.0.1,1;Database=GateJobsTest_synthetic;Integrated Security=true;Encrypt=true"
    }).Build();
    sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName {get;set;}="Testing";
        public string ApplicationName {get;set;}="Synthetic";
        public string ContentRootPath {get;set;}=Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider {get;set;}=new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
    sealed class Runner(Func<bool,Action<ImportSummary>,CancellationToken,Task> execute) : IErpSyncRunner
    {public Task Execute(bool commit,Action<ImportSummary> previewed,CancellationToken token)=>execute(commit,previewed,token);}
    sealed class Schedule : IJobsSchedule
    {
        public bool? Paused {get;private set;}
        public IReadOnlyList<JobView> List()=>[];
        public string EnqueueDryRun()=>"synthetic-job";
        public void Reconcile(bool paused)=>Paused=paused;
        public void MarkFinished(DateTimeOffset at) { }
        public JobsHealth Health()=>new(true,null,null);
    }
    static async Task WithStore(Func<Store,Task> test)
    {
        string path=Path.Combine(Path.GetTempPath(),"gate-jobs-"+Guid.NewGuid().ToString("N"));
        try{await test(new Store(path));}finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(path))Directory.Delete(path,true);}
    }
    [TestMethod]
    public void DisabledByDefaultNeverRegistersStorageOrServerAndInvalidTargetsFailClosed()
    {
        var services=new ServiceCollection();var empty=new ConfigurationBuilder().Build();GateJobs.Add(services,empty,new TestEnvironment());
        using var provider=services.BuildServiceProvider();Assert.IsFalse(provider.GetRequiredService<JobsOptions>().Enabled);
        Assert.IsInstanceOfType<DisabledJobsSchedule>(provider.GetRequiredService<IJobsSchedule>());
        Assert.IsNull(provider.GetService<JobStorage>());Assert.AreEqual(0,provider.GetServices<IHostedService>().Count());
        Assert.IsFalse(JobsOptions.Read(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Jobs:Enabled"]="true"}).Build(),new TestEnvironment()).Enabled);
        var config=Config();config["ConnectionStrings:Jobs"]="Server=127.0.0.1;Database=production;Integrated Security=true";
        Assert.ThrowsExactly<InvalidOperationException>(()=>JobsOptions.Read(config,new TestEnvironment()));
        config=Config();Assert.ThrowsExactly<InvalidOperationException>(()=>JobsOptions.Read(config,new TestEnvironment{EnvironmentName="Production"}));
    }
    [TestMethod]
    public async Task DisabledJobDoesNoWorkAndTwoConcurrentCallsExecuteOnlyOneRunner()
    {
        await WithStore(async store=>
        {
            int calls=0;var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var runner=new Runner(async(_,_,token)=>{Interlocked.Increment(ref calls);entered.TrySetResult();await release.Task.WaitAsync(token);});
            var off=new ErpSyncJob(JobsOptions.Read(Config(false),new TestEnvironment()),store,runner,new JobRunGate(),new Schedule());await off.Execute(false,default);
            Assert.AreEqual(0,calls);Assert.AreEqual(0,store.Read(c=>Store.List(c,ErpSyncRuns.Kind).Count));
            var job=new ErpSyncJob(JobsOptions.Read(Config(),new TestEnvironment()),store,runner,new JobRunGate(),new Schedule());
            var first=job.Execute(true,default);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try{await job.Execute(true,default);Assert.AreEqual(1,calls);Assert.AreEqual("job_already_running",store.Read(c=>Store.List(c,ErpSyncRuns.Kind).Single().S("refusalCode")));}
            finally{release.TrySetResult();await first;}
            Assert.AreEqual(2,store.Read(c=>Store.List(c,ErpSyncRuns.Kind).Count));Assert.IsTrue(store.Read(c=>Store.List(c,ErpSyncRuns.Kind).All(r=>r.S("mode")=="dry_run")));
        });
    }
    [TestMethod]
    public async Task ThresholdRefusalRecordsCountsAndAdminNotificationWithoutChangingBusinessRows()
    {
        await WithStore(async store=>
        {
            var source=new SourceSnapshot(new(){["Vehicles"]=Enumerable.Range(0,100).Select(i=>new JsonObject{["Id"]="syn-"+i,["AssetNumber"]="new"}).ToList()},DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"ordered_double_read_stable");
            var plan=ImportPlan.Build(source,FinancialCore.Sponsors);
            store.Write((c,tx)=>{int i=0;foreach(var d in plan.Documents){var row=d.Body.Clone();if(i++<11)row["assetNumber"]="old";Store.Save(c,d.Kind,row,tx);}return true;});
            string before=store.Read(c=>System.Text.Json.JsonSerializer.Serialize(Store.List(c,"vehicle")));
            var runner=new Runner((commit,previewed,token)=>{ErpSyncRunner.RunSnapshot(source,store.Read(ImportExecutor.Target),commit?store:null,previewed,token);return Task.CompletedTask;});
            var job=new ErpSyncJob(JobsOptions.Read(Config(),new TestEnvironment()),store,runner,new JobRunGate(),new Schedule());await job.Execute(false,default);
            var run=store.Read(c=>Store.List(c,ErpSyncRuns.Kind).Single());Assert.AreEqual("refused",run.S("status"));Assert.AreEqual("kind_changes_over_threshold:vehicle",run.S("refusalCode"));
            Assert.AreEqual(before,store.Read(c=>System.Text.Json.JsonSerializer.Serialize(Store.List(c,"vehicle"))));Assert.IsTrue(store.Read(c=>Store.List(c,"audit").All(a=>a.S("action")=="notification.system_created")));
            var admin=UserAccess.Principal(new(){["username"]="syn-admin",["role"]="Admin"});var other=UserAccess.Principal(new(){["username"]="syn-user",["role"]="Accountant"});
            Assert.AreEqual(1,store.Read(c=>Notifications.Inbox(c,admin).Count));Assert.AreEqual(0,store.Read(c=>Notifications.Inbox(c,other).Count));
        });
    }
    [TestMethod]
    public async Task SuperAdminReasonIsRequiredAndPauseIsRecheckedInsideBusinessTransaction()
    {
        await WithStore(store=>
        {
            var config=Config();var options=JobsOptions.Read(config,new TestEnvironment());var schedule=new Schedule();
            var admin=UserAccess.Principal(new(){["username"]="syn-admin",["role"]="Admin"});var super=UserAccess.Principal(new(){["username"]="syn-super",["role"]="SuperAdmin"});
            var reason=new JsonObject{["reason"]="Synthetic maintenance window"};
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>JobsControl.Change(store,admin,reason,true,options,schedule)).Status);
            Assert.ThrowsExactly<ApiError>(()=>JobsControl.Change(store,super,new(),true,options,schedule));
            JobsControl.Change(store,super,reason,true,options,schedule);Assert.IsTrue(store.Read(c=>JobsControl.Paused(c)));Assert.IsTrue(schedule.Paused);
            Assert.ThrowsExactly<ImportFailure>(()=>store.Write((c,tx)=>{JobsControl.EnsureMayCommit(c,tx,config);Store.Save(c,"vehicle",new(){["id"]="should-not-save"},tx);return true;}));
            Assert.AreEqual(0,store.Read(c=>Store.List(c,"vehicle").Count));Assert.AreEqual(1,store.Read(c=>Store.List(c,"audit").Count));
            Assert.ThrowsExactly<ApiError>(()=>JobsControl.Change(store,super,reason,false,JobsOptions.Read(Config(erp:false),new TestEnvironment()),schedule));
            JobsControl.Change(store,super,reason,false,options,schedule);Assert.IsFalse(store.Read(c=>JobsControl.Paused(c)));Assert.IsFalse(schedule.Paused);
            Assert.AreEqual(2,store.Read(c=>Store.List(c,"audit").Count));return Task.CompletedTask;
        });
    }
    [TestMethod]
    public async Task CancellationRollsBackBusinessWritesAndIsRecordedWithoutRetry()
    {
        await WithStore(async store=>
        {
            using var cancel=new CancellationTokenSource();
            Assert.ThrowsExactly<OperationCanceledException>(()=>store.Write((c,tx)=>{Store.Save(c,"vehicle",new(){["id"]="synthetic"},tx);cancel.Cancel();return true;},cancellationToken:cancel.Token));
            Assert.AreEqual(0,store.Read(c=>Store.List(c,"vehicle").Count));
            var runner=new Runner((_,_,_)=>throw new OperationCanceledException());
            var job=new ErpSyncJob(JobsOptions.Read(Config(),new TestEnvironment()),store,runner,new JobRunGate(),new Schedule());await job.Execute(true,cancel.Token);
            var run=store.Read(c=>Store.List(c,ErpSyncRuns.Kind).Single());Assert.AreEqual("failed",run.S("status"));Assert.AreEqual("job_cancelled",run.S("refusalCode"));
        });
    }
}
