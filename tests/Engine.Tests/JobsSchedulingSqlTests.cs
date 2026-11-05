using ErpImport;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class JobsSchedulingSqlTests
{
    sealed class Runner : IErpSyncRunner
    {
        public int Calls;
        public Task Execute(bool commit,Action<ImportSummary> previewed,CancellationToken token,string? requestId=null)
        {Assert.IsTrue(commit);Assert.IsTrue(ErpSyncRequests.ValidId(requestId!));Interlocked.Increment(ref Calls);return Task.CompletedTask;}
    }
    [TestMethod]
    public async Task StartupUsesLastCommittedSuccessOnceAndDailyIsOptionalWithNoLegacyRecurrence()
    {
        string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if(string.IsNullOrWhiteSpace(configured)){Assert.Inconclusive("Requires an isolated local SQL test connection.");return;}
        var sql=new SqlConnectionStringBuilder(configured);
        if(!System.Text.RegularExpressions.Regex.IsMatch(sql.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)){Assert.Inconclusive("Loopback SQL only.");return;}
        sql.InitialCatalog="master";using var admin=new SqlConnection(sql.ConnectionString);await admin.OpenAsync();
        string database="GateJobsTest_"+Guid.NewGuid().ToString("N"),directory=Path.Combine(Path.GetTempPath(),database);
        using(var create=admin.CreateCommand()){create.CommandText="CREATE DATABASE ["+database+"]";await create.ExecuteNonQueryAsync();}
        try
        {
            sql.InitialCatalog=database;var store=new Store(directory,sql.ConnectionString);var clock=new JobsTestClock();var runner=new Runner();
            IHost MakeHost(bool daily,bool catchUp,double hours=24)
            {
                var builder=Host.CreateApplicationBuilder();builder.Environment.EnvironmentName="Testing";builder.Logging.ClearProviders();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["Jobs:Enabled"]="true",["Jobs:ErpSync:Enabled"]="true",["Jobs:ErpSync:DailyEnabled"]=daily.ToString(),["Jobs:ErpSync:CatchUpEnabled"]=catchUp.ToString(),["Jobs:ErpSync:CatchUpHours"]=hours.ToString(System.Globalization.CultureInfo.InvariantCulture),["ConnectionStrings:Jobs"]=sql.ConnectionString,["ConnectionStrings:DefaultConnection"]=sql.ConnectionString});
                builder.Services.AddSingleton(store);GateJobs.Add(builder.Services,builder.Configuration,builder.Environment);builder.Services.AddSingleton<TimeProvider>(clock);builder.Services.AddSingleton<IErpSyncRunner>(runner);return builder.Build();
            }
            void Success(DateTimeOffset at,string mode)=>ErpSyncRuns.Record(store,"erp-sync:"+Guid.NewGuid().ToString("N"),at,at,"completed",[],mode:mode);
            async Task WaitForRequests(int count)
            {
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(45));
                while(runner.Calls<count||store.Read(c=>Store.Get(c,JobsControl.Id,JobsControl.Kind))?.S("activeRequestId")!="")await Task.Delay(100,deadline.Token);
            }
            Success(clock.Now.AddHours(-25),"commit");Success(clock.Now,"dry_run");
            using(var host=MakeHost(false,true))
            {
                // Seed a superseded recurrence before the new startup reconciler runs.
                host.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<ErpSyncJob>("erp-sync",job=>job.Execute(false,CancellationToken.None),Cron.Daily());
                await host.StartAsync();
                try
                {
                    var schedule=host.Services.GetRequiredService<IJobsSchedule>();schedule.CatchUpOnStartup();schedule.CatchUpOnStartup();
                    Assert.AreEqual(1,store.Read(c=>Store.List(c,ErpSyncRequests.Kind).Count));
                    using(var c=host.Services.GetRequiredService<JobStorage>().GetConnection())Assert.AreEqual(0,c.GetRecurringJobs().Count);
                    await WaitForRequests(1);Assert.AreEqual(1,runner.Calls);Assert.AreEqual("startup",store.Read(c=>Store.List(c,ErpSyncRequests.Kind).Single().S("trigger")));
                    clock.Now=clock.Now.AddHours(25);schedule.CatchUpOnStartup();Assert.AreEqual(1,store.Read(c=>Store.List(c,ErpSyncRequests.Kind).Count));
                }
                finally{await host.StopAsync();}
            }
            using(var host=MakeHost(true,false))
            {
                await host.StartAsync();
                try
                {
                    Assert.AreEqual(1,store.Read(c=>Store.List(c,ErpSyncRequests.Kind).Count));
                    using(var c=host.Services.GetRequiredService<JobStorage>().GetConnection())
                    {var jobs=c.GetRecurringJobs();Assert.AreEqual(1,jobs.Count);Assert.AreEqual("erp-sync-daily",jobs[0].Id);Assert.AreEqual("0 10 * * *",jobs[0].Cron);Assert.AreEqual("Asia/Riyadh",jobs[0].TimeZoneId);}
                    host.Services.GetRequiredService<ErpSyncDailyDispatcher>().Dispatch();await WaitForRequests(2);Assert.AreEqual(2,runner.Calls);
                    Assert.IsTrue(store.Read(c=>Store.List(c,ErpSyncRequests.Kind).Any(r=>r.S("trigger")=="daily")));
                    await host.Services.GetRequiredService<ErpSyncJob>().Execute(false,default);Assert.AreEqual(2,runner.Calls);Assert.IsTrue(store.Read(c=>Store.List(c,ErpSyncRuns.Kind).Any(r=>r.S("refusalCode")=="legacy_schedule_disabled")));
                    Assert.AreEqual(0,store.Read(c=>Store.List(c,"vehicle").Count));Assert.AreEqual(0,store.Read(c=>Store.List(c,"debt").Count));
                }
                finally{await host.StopAsync();}
            }
            using(var host=MakeHost(false,true))
            {await host.StartAsync();try{Assert.AreEqual(2,store.Read(c=>Store.List(c,ErpSyncRequests.Kind).Count));}finally{await host.StopAsync();}}
            clock.Now=clock.Now.AddHours(2);
            using(var host=MakeHost(false,true,1))
            {await host.StartAsync();try{await WaitForRequests(3);Assert.AreEqual(3,runner.Calls);}finally{await host.StopAsync();}}
        }
        finally
        {
            SqlConnection.ClearAllPools();using var drop=admin.CreateCommand();drop.CommandText="ALTER DATABASE ["+database+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+database+"]";await drop.ExecuteNonQueryAsync();
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
}
