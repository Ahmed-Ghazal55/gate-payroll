using ErpImport;
using Hangfire;
namespace Payroll;

public sealed class ErpSyncJob(JobsOptions options,Store store,IErpSyncRunner runner,JobRunGate gate,IJobsSchedule schedule)
{
    [DisableConcurrentExecution(1800)]
    [AutomaticRetry(Attempts=0)]
    public async Task Execute(bool dryRun,CancellationToken cancellationToken)
    {
        if(!options.Enabled)return;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(30));var token=deadline.Token;
        string runId="erp-sync:"+Guid.NewGuid().ToString("N"),status="completed";string? code=null;
        DateTimeOffset started=DateTimeOffset.UtcNow;ImportSummary? preview=null;bool entered=false;
        try
        {
            entered=await gate.TryEnter(token);if(!entered)throw new ImportFailure("job_already_running");
            if(!dryRun&&(!options.ErpSyncEnabled||store.Read(c=>JobsControl.Paused(c))))throw new ImportFailure("erp_sync_paused_or_disabled");
            await runner.Execute(!dryRun,s=>preview=s,token);
        }
        catch(ImportFailure e){status="refused";code=e.Code;}
        catch(OperationCanceledException){status="failed";code=cancellationToken.IsCancellationRequested?"job_cancelled":"job_timeout";}
        catch(Microsoft.Data.SqlClient.SqlException e){status="failed";code=token.IsCancellationRequested?"job_timeout":"sql_"+e.Number;}
        catch {status="failed";code=token.IsCancellationRequested?"job_timeout":"operation_failed";}
        finally
        {
            // Finish counts telemetry before releasing the in-process execution slot.
            try
            {
                ErpSyncRuns.Record(store,runId,started,DateTimeOffset.UtcNow,status,(preview?.Counts??[]).Select(c=>new ErpSyncKindCount(c.Kind,c.Source,c.New,c.Changed,c.Unchanged,c.Rejected)),code,dryRun?"dry_run":"commit");
                schedule.MarkFinished(DateTimeOffset.UtcNow);
            }
            finally{if(entered)gate.Exit();}
        }
    }
}
