using System.Diagnostics;
using System.Text.Json.Nodes;
namespace ErpImport;

public sealed record KindCount(string Kind,int Source,int New,int Changed,int Unchanged,int Rejected,int TargetBefore);
/// <summary>Millisecond timings for one sync attempt. Counts only; never row bodies.</summary>
public sealed record SyncPhaseTiming(long ReadMs,long PlanMs,long WriteMs,long RptRefreshMs)
{
    public long TotalMs => ReadMs+PlanMs+WriteMs+RptRefreshMs;
    public static SyncPhaseTiming Zero { get; }=new(0,0,0,0);
}
public sealed class SyncStopwatch
{
    readonly Stopwatch _watch=Stopwatch.StartNew();
    long _read,_plan,_write,_rpt;
    long Mark(){long ms=_watch.ElapsedMilliseconds;_watch.Restart();return ms;}
    public void AddRead()=>_read+=Mark();
    public void AddPlan()=>_plan+=Mark();
    public void AddWrite()=>_write+=Mark();
    public void AddRptRefresh()=>_rpt+=Mark();
    public SyncPhaseTiming Snapshot()=>new(_read,_plan,_write,_rpt);
}
public sealed record ImportSummary(DateTimeOffset ReadStartedAt,DateTimeOffset ReadFinishedAt,string Consistency,bool Committed,IReadOnlyList<KindCount> Counts,Dictionary<string,Dictionary<string,int>> Rejected,Dictionary<string,int> Observations,Dictionary<string,string> Unavailable,Dictionary<string,int> SourceTables)
{
    public Dictionary<string,int> VehicleChangedFields {get;init;}=[];
    public Dictionary<string,Dictionary<string,int>> ChangedFields {get;init;}=[];
    public Dictionary<string,Dictionary<string,decimal>> Totals {get;init;}=[];
    public SyncPhaseTiming Phases {get;init;}=SyncPhaseTiming.Zero;
    /// <summary>full | incremental | open_period_mix</summary>
    public string ReadMode {get;init;}="full";
    public int TablesSkipped {get;init;}=0;
}
