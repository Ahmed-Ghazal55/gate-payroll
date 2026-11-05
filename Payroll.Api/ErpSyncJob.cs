using ErpImport;
using Hangfire;
using Hangfire.Server;
namespace Payroll;

public sealed class ErpSyncJob(JobsOptions options,Store store,IErpSyncRunner runner,JobRunGate gate,IJobsSchedule schedule,TimeProvider clock)
{
    // Keep the old serialized entry point inert while pre-update queued jobs drain.
    [DisableConcurrentExecution(1800)]
    [AutomaticRetry(Attempts=0)]
    public Task Execute(bool dryRun,CancellationToken cancellationToken)
    {
        if(options.Enabled){var at=clock.GetUtcNow();ErpSyncRuns.Record(store,"erp-sync:"+Guid.NewGuid().ToString("N"),at,at,"refused",[],"legacy_schedule_disabled",dryRun?"dry_run":"commit");}
        return Task.CompletedTask;
    }
    [DisableConcurrentExecution(1800)]
    [AutomaticRetry(Attempts=0)]
    public async Task ExecuteRequest(string requestId,CancellationToken cancellationToken,PerformContext? context)
    {
        if(!options.Enabled)return;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(30));var token=deadline.Token;
        string runId="erp-sync:"+Guid.NewGuid().ToString("N"),status="completed";string? code=null;
        DateTimeOffset started=clock.GetUtcNow();ImportSummary? preview=null;bool entered=false,ownsRequest=false,skip=false,dryRun=false;
        try
        {
            if(ErpSyncRequests.ValidId(requestId))dryRun=store.Read(c=>Store.Get(c,requestId,ErpSyncRequests.Kind))?.S("mode")=="dry_run";
            entered=await gate.TryEnter(CancellationToken.None);if(!entered)throw new ImportFailure("job_already_running");
            if(context!=null)ErpSyncRequests.Link(store,requestId,context.BackgroundJob.Id);
            var request=ErpSyncRequests.Start(store,requestId,started);
            if(request==null){skip=true;return;}
            ownsRequest=true;dryRun=request.S("mode")=="dry_run";runId="erp-sync:"+requestId.Split(':')[1];
            if(!dryRun&&(!options.ErpSyncEnabled||store.Read(c=>JobsControl.Paused(c))))throw new ImportFailure("erp_sync_paused_or_disabled");
            token.ThrowIfCancellationRequested();await runner.Execute(!dryRun,s=>preview=s,token,requestId);
        }
        catch(ImportFailure e){status="refused";code=e.Code;}
        catch(OperationCanceledException){status="failed";code=cancellationToken.IsCancellationRequested?"job_cancelled":"job_timeout";}
        catch(Microsoft.Data.SqlClient.SqlException e){status="failed";code=token.IsCancellationRequested?"job_timeout":"sql_"+e.Number;}
        catch {status="failed";code=token.IsCancellationRequested?"job_timeout":"operation_failed";}
        finally
        {
            try
            {
                if(!skip)
                {
                    ErpSyncRuns.Record(store,runId,started,clock.GetUtcNow(),status,(preview?.Counts??[]).Select(c=>new ErpSyncKindCount(c.Kind,c.Source,c.New,c.Changed,c.Unchanged,c.Rejected)),code,dryRun?"dry_run":"commit",ownsRequest?requestId:null);
                    schedule.MarkFinished(clock.GetUtcNow());
                }
            }
            finally{if(entered)gate.Exit();}
        }
    }
}
public sealed class ErpSyncDailyDispatcher(IJobsSchedule schedule)
{
    [AutomaticRetry(Attempts=0)]
    public void Dispatch()=>schedule.EnqueueDaily();
}
