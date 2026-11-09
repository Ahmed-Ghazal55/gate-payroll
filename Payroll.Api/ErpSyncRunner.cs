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
        using var target=new SqlConnection(targetOptions.ConnectionString);await target.OpenAsync(token);
        var existing=ImportExecutor.Target(target,null,token);
        var snapshot=ConsistentSync.Read(()=>SourceReader.Read(source,false,token),out _,token);
        RunSnapshot(snapshot,existing,commit?store:null,previewed,token,commit?(c,tx)=>{JobsControl.EnsureMayCommit(c,tx,config);if(requestId!=null)ErpSyncRequests.EnsureActive(c,tx,requestId);}:null,requestId==null?null:store.Read(c=>ErpSyncRequests.Overrides(c,requestId)));
    }
    public static ImportSummary RunSnapshot(SourceSnapshot source,Dictionary<string,JsonObject> existing,Store? target,Action<ImportSummary> previewed,CancellationToken token=default,Action<DbConnection,DbTransaction>? beforeCommit=null,SyncOverrides? overrides=null)
    {
        token.ThrowIfCancellationRequested();var plan=ImportPlan.Build(source,FinancialCore.Sponsors);
        var preview=ImportExecutor.Run(source,plan,existing,cancellationToken:token);previewed(preview);
        // Scheduled runs have none. A manual run carries an administrator's named, audited acceptance.
        ConsistentSync.Validate(preview,existing,overrides??SyncOverrides.None);token.ThrowIfCancellationRequested();
        if(target==null)return preview;
        var committed=ImportExecutor.Run(source,plan,existing,target,token,beforeCommit);
        var verification=target.Read(c=>ImportExecutor.Run(source,plan,ImportExecutor.Target(c,null,token),cancellationToken:token));
        if(verification.Counts.Any(c=>c.New+c.Changed!=0))throw new ImportFailure("post_load_idempotency_failed");
        return committed;
    }
}
