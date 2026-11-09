using ErpImport;
using Microsoft.Data.SqlClient;
using System.Data.Common;
using System.Text.Json.Nodes;
namespace Payroll;

public interface IErpSyncRunner
{
    Task Execute(bool commit,Action<ImportSummary> previewed,CancellationToken token,string? requestId=null);
}
public sealed class ErpSyncRunner(IConfiguration config,Store store) : IErpSyncRunner
{
    public async Task Execute(bool commit,Action<ImportSummary> previewed,CancellationToken token,string? requestId=null)
    {
        string sourceConnection=config.GetConnectionString("ErpReadonly")??config["ERP_READONLY_DB"]??"";
        if(string.IsNullOrWhiteSpace(sourceConnection))throw new ImportFailure("source_connection_missing");
        // Never read private local env files from a hosted job. SQL checks the SELECT-only role too.
        using var source=Connections.Source(sourceConnection);await source.OpenAsync(token);using var sourceAbort=token.Register(()=>{try{source.Close();}catch{}});SourceReader.VerifyReadOnly(source);
        var targetOptions=new SqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection")??"");
        if(targetOptions.InitialCatalog!="db77738")throw new ImportFailure("unapproved_target");
        // Managed SNI can hang in SqlBulkCopy over a MARS session; sync never needs MARS.
        targetOptions.MultipleActiveResultSets=false;
        using var target=new SqlConnection(targetOptions.ConnectionString);await target.OpenAsync(token);
        var timing=new SyncStopwatch();
        var existing=ImportExecutor.Target(target,null,token);
        SourceWatermarks.Ensure(target);var watermarks=SourceWatermarks.Load(target);
        var overrides=requestId==null?SyncOverrides.None:store.Read(c=>ErpSyncRequests.Overrides(c,requestId));
        bool incremental=!overrides.FullResync&&watermarks.Count>0;
        SourceSnapshot snapshot=incremental
            ?SourceReader.Read(source,false,watermarks,false,token)
            :ConsistentSync.Read(()=>SourceReader.Read(source,false,null,overrides.FullResync,token),out _,token);
        timing.AddRead();
        string runId=requestId??("erp-sync:"+Guid.NewGuid().ToString("N"));
        RunSnapshot(snapshot,existing,commit?store:null,previewed,token,commit?(c,tx)=>{JobsControl.EnsureMayCommit(c,tx,config);if(requestId!=null)ErpSyncRequests.EnsureActive(c,tx,requestId);PersistWatermarks(c,tx,source,snapshot,watermarks,runId);}:null,overrides,timing);
    }
    static void PersistWatermarks(DbConnection c,DbTransaction tx,DbConnection source,SourceSnapshot snapshot,Dictionary<string,SourceWatermark> previous,string runId)
    {
        var next=new Dictionary<string,string>(snapshot.NextWatermarks??new Dictionary<string,string>(),StringComparer.OrdinalIgnoreCase);
        foreach(var ds in SourceWatermarks.ReadDatasetVersions(source,runId,DateTimeOffset.UtcNow))next[ds.TableName]=ds.LastValue;
        SourceWatermarks.Save(c,tx,SourceWatermarks.FromNext(next,runId,DateTimeOffset.UtcNow,previous));
    }
    public static ImportSummary RunSnapshot(SourceSnapshot source,Dictionary<string,JsonObject> existing,Store? target,Action<ImportSummary> previewed,CancellationToken token=default,Action<DbConnection,DbTransaction>? beforeCommit=null,SyncOverrides? overrides=null,SyncStopwatch? timing=null)
    {
        timing??=new SyncStopwatch();
        token.ThrowIfCancellationRequested();var plan=ImportPlan.Build(source,FinancialCore.Sponsors);
        foreach(var missing in OpenPeriodReconcile.MissingAsSoftDeleted(source,plan,existing))plan.Documents.Add(missing);
        timing.AddPlan();
        var preview=ImportExecutor.Run(source,plan,existing,cancellationToken:token) with{Phases=timing.Snapshot(),ReadMode=source.ReadMode,TablesSkipped=source.TablesSkipped};previewed(preview);
        // Scheduled runs have none. A manual run carries an administrator's named, audited acceptance.
        ConsistentSync.Validate(preview,existing,overrides??SyncOverrides.None);token.ThrowIfCancellationRequested();
        if(target==null)return preview;
        var committed=ImportExecutor.Run(source,plan,existing,target,token,beforeCommit);
        timing.AddWrite();
        timing.AddRptRefresh();
        var verification=target.Read(c=>ImportExecutor.Run(source,plan,ImportExecutor.Target(c,null,token),cancellationToken:token));
        if(verification.Counts.Any(c=>c.New+c.Changed!=0))throw new ImportFailure("post_load_idempotency_failed");
        return committed with{Phases=timing.Snapshot(),ReadMode=source.ReadMode,TablesSkipped=source.TablesSkipped};
    }
}
