using System.Text.Json.Nodes;
namespace ErpImport;

public sealed record KindCount(string Kind,int Source,int New,int Changed,int Unchanged,int Rejected,int TargetBefore);
public sealed record ImportSummary(DateTimeOffset ReadStartedAt,DateTimeOffset ReadFinishedAt,string Consistency,bool Committed,IReadOnlyList<KindCount> Counts,Dictionary<string,Dictionary<string,int>> Rejected,Dictionary<string,int> Observations,Dictionary<string,string> Unavailable,Dictionary<string,int> SourceTables)
{
    public Dictionary<string,int> VehicleChangedFields {get;init;}=[];
    public Dictionary<string,Dictionary<string,decimal>> Totals {get;init;}=[];
}
