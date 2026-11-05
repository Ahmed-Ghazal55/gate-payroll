using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
namespace Payroll;

public sealed record JobView(string Id,bool Enabled,string Status,string? Cron=null,string? TimeZone=null,DateTimeOffset? LastRun=null,DateTimeOffset? NextRun=null,string? LastStatus=null);
public sealed record JobsHealth(bool Enabled,DateTimeOffset? LastErpSync,DateTimeOffset? NextErpSync);
public interface IJobsSchedule
{
    IReadOnlyList<JobView> List();
    string EnqueueDryRun();
    void Reconcile(bool paused);
    void MarkFinished(DateTimeOffset at);
    JobsHealth Health();
}
public sealed class DisabledJobsSchedule : IJobsSchedule
{
    public static readonly string[] FutureIds=["reporting-rebuild","fcm-notifications","ninja-invoice-compare","large-imports"];
    public IReadOnlyList<JobView> List()=>[new("erp-sync",false,"disabled",HangfireJobsSchedule.Cron,HangfireJobsSchedule.Zone),..FutureIds.Select(id=>new JobView(id,false,"not_implemented"))];
    public string EnqueueDryRun()=>throw new ApiError("خدمة المهام غير مفعلة.",409);
    public void Reconcile(bool paused) { }
    public void MarkFinished(DateTimeOffset at) { }
    public JobsHealth Health()=>new(false,null,null);
}
public sealed class HangfireJobsSchedule(JobStorage storage,IRecurringJobManager recurring,IBackgroundJobClient client,JobsOptions options,Store store) : IJobsSchedule
{
    public const string Cron="15 */3 * * *",Zone="Asia/Riyadh";
    readonly object cacheGate=new();DateTimeOffset? last,next;
    static DateTimeOffset? Utc(DateTime? value)=>value.HasValue?new DateTimeOffset(DateTime.SpecifyKind(value.Value,DateTimeKind.Utc)):null;
    public IReadOnlyList<JobView> List()
    {
        using var connection=storage.GetConnection();var job=connection.GetRecurringJobs(["erp-sync"]).SingleOrDefault();
        bool paused=store.Read(c=>JobsControl.Paused(c));var recent=ReadLast();
        return [new("erp-sync",options.ErpSyncEnabled&&!paused,paused?"paused":!options.ErpSyncEnabled?"disabled":job?.Error!=null?"failed":"scheduled",Cron,Zone,recent.At,Utc(job?.NextExecution),recent.Status),..DisabledJobsSchedule.FutureIds.Select(id=>new JobView(id,false,"not_implemented"))];
    }
    (DateTimeOffset? At,string? Status) ReadLast()
    {
        var actor=UserAccess.Principal(new(){["username"]="job-status-reader",["role"]="Admin"});
        var run=store.Read(c=>ErpSyncRuns.Recent(c,actor,1)).FirstOrDefault();return (DateTimeOffset.TryParse(run?.S("finishedAt"),out var at)?at:null,run?.S("status"));
    }
    public string EnqueueDryRun()=>client.Enqueue<ErpSyncJob>(job=>job.Execute(true,CancellationToken.None));
    public void Reconcile(bool paused)
    {
        if(options.ErpSyncEnabled&&!paused)
            recurring.AddOrUpdate("erp-sync",Job.FromExpression<ErpSyncJob>(job=>job.Execute(false,CancellationToken.None)),Cron,new RecurringJobOptions{TimeZone=TimeZoneInfo.FindSystemTimeZoneById(Zone),MisfireHandling=MisfireHandlingMode.Relaxed});
        else recurring.RemoveIfExists("erp-sync");
        Refresh();
    }
    void Refresh(){var view=List().First();lock(cacheGate){last=view.LastRun;next=view.NextRun;}}
    public void MarkFinished(DateTimeOffset at){try{Refresh();}catch{lock(cacheGate)next=null;}lock(cacheGate)last=at;}
    public JobsHealth Health(){lock(cacheGate)return new(options.Enabled,last,next);}
}
public sealed class JobsStartup(IJobsSchedule schedule,Store store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken){schedule.Reconcile(store.Read(c=>JobsControl.Paused(c)));return Task.CompletedTask;}
    public Task StopAsync(CancellationToken cancellationToken)=>Task.CompletedTask;
}
public static class GateJobs
{
    public static void Add(IServiceCollection services,IConfiguration config,IHostEnvironment environment)
    {
        var options=JobsOptions.Read(config,environment);services.AddSingleton(options);services.AddSingleton<JobRunGate>();
        services.AddSingleton<IErpSyncRunner,ErpSyncRunner>();services.AddSingleton<ErpSyncJob>();
        if(!options.Enabled){services.AddSingleton<IJobsSchedule,DisabledJobsSchedule>();return;}
        services.AddHangfire(h=>h.UseSimpleAssemblyNameTypeSerializer().UseRecommendedSerializerSettings().UseSqlServerStorage(options.StorageConnection,new Hangfire.SqlServer.SqlServerStorageOptions
        {SchemaName="hangfire",PrepareSchemaIfNecessary=true,TryAutoDetectSchemaDependentOptions=false,QueuePollInterval=TimeSpan.FromSeconds(15)}));
        services.AddSingleton<IJobsSchedule,HangfireJobsSchedule>();services.AddHostedService<JobsStartup>();
        services.AddHangfireServer(server=>{server.WorkerCount=1;server.CancellationCheckInterval=TimeSpan.FromSeconds(1);});
        // Dashboard is intentionally never mapped in any environment.
    }
}
