using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Payroll;
namespace ErpImport;

// Only allowlisted kind names can become overrides or enter a run log.
public sealed class SyncOverrides
{
    public IReadOnlyList<string> AcceptNewKinds { get; }
    public IReadOnlyList<string> AcceptLargeChanges { get; }
    public static SyncOverrides None { get; }=new([],[]);
    private SyncOverrides(IEnumerable<string> newKinds,IEnumerable<string> largeChanges)
    {AcceptNewKinds=Array.AsReadOnly(newKinds.Order(StringComparer.Ordinal).ToArray());AcceptLargeChanges=Array.AsReadOnly(largeChanges.Order(StringComparer.Ordinal).ToArray());}
    public static SyncOverrides Parse(string[] args)
    {
        var newKinds=new HashSet<string>(StringComparer.Ordinal);var large=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<args.Length;i++)
        {
            if(args[i] is not ("--accept-new-kind" or "--accept-large-change"))continue;
            string flag=args[i];if(i+1>=args.Length||!ImportPlan.Kinds.Contains(args[i+1],StringComparer.Ordinal))throw new ImportFailure("invalid_sync_override_kind");
            (flag=="--accept-new-kind"?newKinds:large).Add(args[++i]);
        }
        return new(newKinds,large);
    }
}

public static class ConsistentSync
{
    static string Canonical(JsonNode? value)=>value switch
    {
        JsonObject obj=>"{"+string.Join(',',obj.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>System.Text.Json.JsonSerializer.Serialize(p.Key)+":"+Canonical(p.Value)))+"}",
        JsonArray array=>"["+string.Join(',',array.Select(Canonical))+"]",
        _=>value?.ToJsonString()??"null"
    };
    // Fingerprints stay in memory. Neither hashes nor source rows enter a run log.
    static Dictionary<string,string> Fingerprints(SourceSnapshot snapshot)=>snapshot.Tables.ToDictionary(t=>t.Key,t=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(new JsonArray(t.Value.OrderBy(r=>r.S("Id"),StringComparer.Ordinal).Select(r=>(JsonNode)r.DeepClone()).ToArray()))))));
    public static SourceSnapshot Read(Func<SourceSnapshot> read,out int attempts)
    {
        for(attempts=1;attempts<=3;attempts++)
        {
            var first=read();var second=read();var a=Fingerprints(first);var b=Fingerprints(second);
            if(a.Count==b.Count&&a.All(t=>b.GetValueOrDefault(t.Key)==t.Value))
                return second with{StartedAt=first.StartedAt,Consistency="ordered_double_read_stable"};
        }
        attempts=3;throw new ImportFailure("source_inconsistent_after_three_attempts");
    }
    public static void Validate(ImportSummary preview,Dictionary<string,JsonObject> existing,SyncOverrides? overrides=null)
    {
        overrides??=SyncOverrides.None;
        foreach(var row in preview.Counts)
        {
            // Manual/certificate documents are outside the ERP-managed baseline.
            int before=existing.Values.Count(x=>x.S("__targetKind")==row.Kind&&x.S("erpSource")==row.Kind&&x.S("erpSourceId")!="");
            int after=row.New+row.Changed+row.Unchanged;
            int changes=row.New+row.Changed;
            if(before==0)
            {
                if(changes>0&&!overrides.AcceptNewKinds.Contains(row.Kind))throw new ImportFailure("new_kind_requires_acceptance:"+row.Kind);
                continue;
            }
            if(overrides.AcceptLargeChanges.Contains(row.Kind))continue;
            if(before-after>Math.Max(before*0.02m,1m))throw new ImportFailure("source_count_drop_over_threshold:"+row.Kind);
            // Immutable readings can grow freely after their explicit first acceptance.
            if(row.Kind=="vehicle_odometer_reading"&&row.Changed==0)continue;
            if(changes>Math.Max(before*0.10m,5m))throw new ImportFailure("kind_changes_over_threshold:"+row.Kind);
        }
    }
}
