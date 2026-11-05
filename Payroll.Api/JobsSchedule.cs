using ErpImport;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using System.Text.Json.Nodes;
namespace Payroll;

public sealed record JobView(string Id,bool Enabled,string Status,string? Cron=null,string? TimeZone=null,DateTimeOffset? LastRun=null,DateTimeOffset? NextRun=null,string? LastStatus=null);
public sealed record JobsHealth(bool Enabled,DateTimeOffset? LastErpSync,DateTimeOffset? NextErpSync);
public interface IJobsSchedule
{
    IReadOnlyList<JobView> List();
    string Enqueue(bool dryRun,string trigger,string actor);
    JsonObject Status(string jobId);
    void CatchUpOnStartup();
    void EnqueueDaily();
    void Reconcile(bool paused);
    void MarkFinished(DateTimeOffset at);
    JobsHealth Health();
}
public sealed class DisabledJobsSchedule(JobsOptions? options=null) : IJobsSchedule
{
    public static readonly string[] FutureIds=["reporting-rebuild","fcm-notifications","ninja-invoice-compare","large-imports"];
    public IReadOnlyList<JobView> List()=>[new("erp-sync",false,options?.BlockedReason is {} why?"blocked_"+why:"disabled"),new("erp-sync-daily",false,"disabled",HangfireJobsSchedule.DailyCron,HangfireJobsSchedule.Zone),..FutureIds.Select(id=>new JobView(id,false,"not_implemented"))];
    public string Enqueue(bool dryRun,string trigger,string actor)=>throw new ApiError("خدمة المهام غير مفعلة.",409);
    public JsonObject Status(string jobId)=>throw new ApiError("خدمة المهام غير مفعلة.",409);
    public void CatchUpOnStartup() { }
    public void EnqueueDaily() { }
    public void Reconcile(bool paused) { }
    public void MarkFinished(DateTimeOffset at) { }
    public JobsHealth Health()=>new(false,null,null);
}
public sealed class HangfireJobsSchedule(JobStorage storage,IRecurringJobManager recurring,IBackgroundJobClient client,JobsOptions options,Store store,TimeProvider clock) : IJobsSchedule
{
    public const string DailyCron="0 10 * * *",Zone="Asia/Riyadh";
    readonly object cacheGate=new();DateTimeOffset? last,next;int startupChecked;
    static DateTimeOffset? Utc(DateTime? value)=>value.HasValue?new DateTimeOffset(DateTime.SpecifyKind(value.Value,DateTimeKind.Utc)):null;
    public IReadOnlyList<JobView> List()
    {
        using var connection=storage.GetConnection();var daily=connection.GetRecurringJobs(["erp-sync-daily"]).SingleOrDefault();
        var control=store.Read(c=>Store.Get(c,JobsControl.Id,JobsControl.Kind));bool paused=control?.B("paused")==true;
        var active=control?.S("activeRequestId") is {} id&&id!=""?store.Read(c=>Store.Get(c,id,ErpSyncRequests.Kind)):null;
        var actor=UserAccess.Principal(new(){["username"]="job-status-reader",["role"]="Admin"});
        var recent=store.Read(c=>ErpSyncRuns.Recent(c,actor,1)).FirstOrDefault();DateTimeOffset? at=DateTimeOffset.TryParse(recent?.S("finishedAt"),out var time)?time:null;
        return [new("erp-sync",options.ErpSyncEnabled&&!paused,paused?"paused":!options.ErpSyncEnabled?"disabled":active?.S("status")??"ready",LastRun:at,LastStatus:recent?.S("status")),
            new("erp-sync-daily",options.ErpSyncEnabled&&options.DailyEnabled&&!paused,paused?"paused":!options.ErpSyncEnabled||!options.DailyEnabled?"disabled":daily?.Error!=null?"failed":"scheduled",DailyCron,Zone,Utc(daily?.LastExecution),Utc(daily?.NextExecution)),..DisabledJobsSchedule.FutureIds.Select(id=>new JobView(id,false,"not_implemented"))];
    }
    public string Enqueue(bool dryRun,string trigger,string actor)
    {
        RecoverInterruptedAdmission();
        string requestId=ErpSyncRequests.Reserve(store,options,dryRun,trigger,actor,clock.GetUtcNow());
        bool submitted=false;
        try
        {
            string jobId=client.Enqueue<ErpSyncJob>(job=>job.ExecuteRequest(requestId,CancellationToken.None,null));submitted=true;
            ErpSyncRequests.Link(store,requestId,jobId);return jobId;
        }
        catch
        {
            if(!submitted){var at=clock.GetUtcNow();ErpSyncRuns.Record(store,"erp-sync:"+requestId.Split(':')[1],at,at,"failed",[],"enqueue_failed",dryRun?"dry_run":"commit",requestId);}
            throw new ApiError("تعذر وضع المهمة في الطابور.",503);
        }
    }
    // Repair only known terminal jobs, or an unlinked admission abandoned before enqueue.
    // Never expire a queued/running linked request solely because wall-clock time passed.
    void RecoverInterruptedAdmission()
    {
        var control=store.Read(c=>Store.Get(c,JobsControl.Id,JobsControl.Kind));string id=control?.S("activeRequestId")??"";if(id=="")return;
        var request=store.Read(c=>Store.Get(c,id,ErpSyncRequests.Kind));if(request==null)throw new ImportFailure("sync_request_missing");
        string jobId=request.S("jobId"),state="";
        if(jobId!=""){using var connection=storage.GetConnection();state=connection.GetStateData(jobId)?.Name??"";}
        bool orphan=jobId==""&&request.S("status")=="queued"&&DateTimeOffset.TryParse(request.S("queuedAt"),out var queued)&&clock.GetUtcNow()-queued>TimeSpan.FromMinutes(2);
        if(!orphan&&state is not ("Failed" or "Deleted" or "Succeeded"))return;
        // A worker may have finalized after the storage read. Recheck under the same Store gate.
        store.Write((c,tx)=>
        {
            var current=Store.Get(c,id,ErpSyncRequests.Kind,tx);if(current==null||current.S("status") is "succeeded" or "refused" or "failed")return false;
            var at=clock.GetUtcNow();var start=DateTimeOffset.TryParse(current.S("startedAt",current.S("queuedAt")),out var started)?started:at;
            var run=new JsonObject{["id"]="erp-sync:"+id.Split(':')[1],["startedAt"]=start.ToString("O"),["finishedAt"]=at.ToString("O"),["status"]="failed",["mode"]=current.S("mode"),["requestId"]=id,["refusalCode"]="interrupted_job",["counts"]=new JsonArray()};
            Store.Save(c,ErpSyncRuns.Kind,run,tx);ErpSyncRequests.Complete(c,tx,id,run);return true;
        },false);
    }
    public JsonObject Status(string jobId)
    {
        var view=store.Read(c=>ErpSyncRequests.View(c,jobId));
        if(view.S("status") is "queued" or "running")
        {using var connection=storage.GetConnection();if(connection.GetStateData(jobId)?.Name is "Failed" or "Deleted")view["status"]="failed";}
        return view;
    }
    public void CatchUpOnStartup()
    {
        if(Interlocked.Exchange(ref startupChecked,1)!=0||!options.ErpSyncEnabled||!options.CatchUpEnabled||store.Read(c=>JobsControl.Paused(c)))return;
        var success=store.Read(ErpSyncRequests.LastSuccess);
        if(success.HasValue&&clock.GetUtcNow()-success.Value<=TimeSpan.FromHours(options.CatchUpHours))return;
        try{Enqueue(false,"startup","system:startup");}catch(ImportFailure e) when(e.Code is "sync_in_progress" or "sync_cooldown" or "erp_sync_paused_or_disabled") { }
    }
    public void EnqueueDaily()
    {
        if(!options.ErpSyncEnabled||!options.DailyEnabled)return;
        try{Enqueue(false,"daily","system:daily");}
        catch(ImportFailure e) when(e.Code is "sync_in_progress" or "sync_cooldown" or "erp_sync_paused_or_disabled")
        {var at=clock.GetUtcNow();ErpSyncRuns.Record(store,"erp-sync:"+Guid.NewGuid().ToString("N"),at,at,"refused",[],e.Code,"commit");}
    }
    public void Reconcile(bool paused)
    {
        recurring.RemoveIfExists("erp-sync"); // Remove the superseded three-hour recurrence on upgrade.
        if(options.ErpSyncEnabled&&options.DailyEnabled&&!paused)
            recurring.AddOrUpdate("erp-sync-daily",Job.FromExpression<ErpSyncDailyDispatcher>(job=>job.Dispatch()),DailyCron,new RecurringJobOptions{TimeZone=TimeZoneInfo.FindSystemTimeZoneById(Zone),MisfireHandling=MisfireHandlingMode.Relaxed});
        else recurring.RemoveIfExists("erp-sync-daily");
        Refresh();
    }
    void Refresh(){var daily=List().Single(j=>j.Id=="erp-sync-daily");var success=store.Read(ErpSyncRequests.LastSuccess);lock(cacheGate){last=success;next=daily.NextRun;}}
    public void MarkFinished(DateTimeOffset at){try{Refresh();}catch{lock(cacheGate)next=null;}}
    public JobsHealth Health(){lock(cacheGate)return new(options.Enabled,last,next);}
}
public sealed class JobsStartup(IJobsSchedule schedule,Store store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken){schedule.Reconcile(store.Read(c=>JobsControl.Paused(c)));schedule.CatchUpOnStartup();return Task.CompletedTask;}
    public Task StopAsync(CancellationToken cancellationToken)=>Task.CompletedTask;
}
public static class GateJobs
{
    public static void Add(IServiceCollection services,IConfiguration config,IHostEnvironment environment)
    {
        var options=JobsOptions.Read(config,environment);services.AddSingleton(options);services.AddSingleton(TimeProvider.System);services.AddSingleton<JobRunGate>();
        services.AddSingleton<IErpSyncRunner,ErpSyncRunner>();services.AddSingleton<ErpSyncJob>();services.AddSingleton<ErpSyncDailyDispatcher>();
        if(!options.Enabled){services.AddSingleton<IJobsSchedule,DisabledJobsSchedule>();return;}
        services.AddHangfire(h=>h.UseSimpleAssemblyNameTypeSerializer().UseRecommendedSerializerSettings().UseSqlServerStorage(options.StorageConnection,new Hangfire.SqlServer.SqlServerStorageOptions
        {SchemaName="hangfire",PrepareSchemaIfNecessary=true,TryAutoDetectSchemaDependentOptions=false,QueuePollInterval=TimeSpan.FromSeconds(15)}));
        services.AddSingleton<IJobsSchedule,HangfireJobsSchedule>();services.AddHostedService<JobsStartup>();
        services.AddHangfireServer(server=>{server.WorkerCount=1;server.CancellationCheckInterval=TimeSpan.FromSeconds(1);});
        // Dashboard is intentionally never mapped in any environment.
    }
}
