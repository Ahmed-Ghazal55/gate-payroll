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
public sealed class JobsSqlTests
{
    sealed class SyntheticRunner : IErpSyncRunner
    {
        public int Calls,Active,Maximum;
        public async Task Execute(bool commit,Action<ImportSummary> previewed,CancellationToken token)
        {
            Assert.IsFalse(commit);int active=Interlocked.Increment(ref Active);Maximum=Math.Max(Maximum,active);
            try{await Task.Delay(100,token);Interlocked.Increment(ref Calls);throw new ImportFailure("new_kind_requires_acceptance:vehicle");}
            finally{Interlocked.Decrement(ref Active);}
        }
    }
    [TestMethod]
    public async Task SqlStorageUsesOwnSchemaSingleWorkerAndDurableCountsOnlyDryRuns()
    {
        string? connection=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if(string.IsNullOrWhiteSpace(connection)){Assert.Inconclusive("Requires an isolated local SQL test connection.");return;}
        var options=new SqlConnectionStringBuilder(connection);
        if(!System.Text.RegularExpressions.Regex.IsMatch(options.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)){Assert.Inconclusive("Loopback SQL only.");return;}
        options.InitialCatalog="master";using var admin=new SqlConnection(options.ConnectionString);await admin.OpenAsync();
        string database="GateJobsTest_"+Guid.NewGuid().ToString("N"),directory=Path.Combine(Path.GetTempPath(),database);
        using(var create=admin.CreateCommand()){create.CommandText="CREATE DATABASE ["+database+"]";await create.ExecuteNonQueryAsync();}
        try
        {
            options.InitialCatalog=database;var store=new Store(directory,options.ConnectionString);
            var builder=Host.CreateApplicationBuilder();builder.Environment.EnvironmentName="Testing";builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["Jobs:Enabled"]="true",["Jobs:ErpSync:Enabled"]="false",["ConnectionStrings:Jobs"]=options.ConnectionString,["ConnectionStrings:DefaultConnection"]=options.ConnectionString});
            builder.Services.AddSingleton(store);GateJobs.Add(builder.Services,builder.Configuration,builder.Environment);
            var runner=new SyntheticRunner();builder.Services.AddSingleton<IErpSyncRunner>(runner);
            using var host=builder.Build();await host.StartAsync();
            try
            {
                using(var registrationTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                    while(host.Services.GetRequiredService<JobStorage>().GetMonitoringApi().Servers().Count==0)await Task.Delay(100,registrationTimeout.Token);
                Assert.AreEqual(1,host.Services.GetRequiredService<JobStorage>().GetMonitoringApi().Servers().Single().WorkersCount);
                using(var c=new SqlConnection(options.ConnectionString))
                {
                    await c.OpenAsync();using var query=c.CreateCommand();query.CommandText="SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('hangfire')";Assert.IsTrue(Convert.ToInt32(await query.ExecuteScalarAsync())>0);
                }
                var schedule=host.Services.GetRequiredService<IJobsSchedule>();Assert.IsTrue(schedule.Health().Enabled);Assert.IsTrue(schedule.List().All(j=>!j.Enabled));
                using(var c=host.Services.GetRequiredService<JobStorage>().GetConnection())Assert.AreEqual(0,c.GetRecurringJobs().Count);
                schedule.EnqueueDryRun();schedule.EnqueueDryRun();
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
                while(store.Read(c=>Store.List(c,ErpSyncRuns.Kind).Count)<2)await Task.Delay(100,timeout.Token);
                Assert.AreEqual(2,runner.Calls);Assert.AreEqual(1,runner.Maximum);
                var runs=store.Read(c=>Store.List(c,ErpSyncRuns.Kind));Assert.IsTrue(runs.All(r=>r.S("status")=="refused"&&r.S("mode")=="dry_run"));
                Assert.AreEqual(2,store.Read(c=>Store.List(c,"sys_notification").Count));Assert.AreEqual(0,store.Read(c=>Store.List(c,"vehicle").Count));Assert.AreEqual(0,store.Read(c=>Store.List(c,"journal").Count));
            }
            finally{await host.StopAsync();}
        }
        finally
        {
            SqlConnection.ClearAllPools();using var drop=admin.CreateCommand();drop.CommandText="ALTER DATABASE ["+database+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+database+"]";await drop.ExecuteNonQueryAsync();
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
}
