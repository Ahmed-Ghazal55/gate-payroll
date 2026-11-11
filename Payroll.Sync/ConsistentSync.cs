using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace ErpImport;

// Only allowlisted kind names can become overrides or enter a run log.
public sealed class SyncOverrides
{
    public IReadOnlyList<string> AcceptNewKinds { get; }
    public IReadOnlyList<string> AcceptLargeChanges { get; }
    public bool FullResync { get; }
    public string FullResyncReason { get; }
    public static SyncOverrides None { get; }=new([],[],false,"");
    private SyncOverrides(IEnumerable<string> newKinds,IEnumerable<string> largeChanges,bool fullResync,string fullResyncReason)
    {
        AcceptNewKinds=Array.AsReadOnly(newKinds.Order(StringComparer.Ordinal).ToArray());
        AcceptLargeChanges=Array.AsReadOnly(largeChanges.Order(StringComparer.Ordinal).ToArray());
        FullResync=fullResync;FullResyncReason=fullResyncReason;
    }
    public static SyncOverrides Named(IEnumerable<string> newKinds,IEnumerable<string> largeChanges,bool fullResync=false,string fullResyncReason="")
    {
        if(fullResync){var reason=fullResyncReason.Trim();if(reason.Length is <3 or >500||reason.Any(char.IsControl))throw new ImportFailure("full_resync_reason_required");}
        return new(newKinds.Distinct(StringComparer.Ordinal),largeChanges.Distinct(StringComparer.Ordinal),fullResync,fullResync?fullResyncReason.Trim():"");
    }
    public static SyncOverrides Parse(string[] args)
    {
        var newKinds=new HashSet<string>(StringComparer.Ordinal);var large=new HashSet<string>(StringComparer.Ordinal);
        bool full=false;string reason="";
        for(int i=0;i<args.Length;i++)
        {
            if(args[i]=="--full-resync")
            {
                full=true;
                if(i+1>=args.Length||args[i+1].StartsWith("--",StringComparison.Ordinal))throw new ImportFailure("full_resync_reason_required");
                reason=args[++i];continue;
            }
            if(args[i] is not ("--accept-new-kind" or "--accept-large-change"))continue;
            string flag=args[i];if(i+1>=args.Length||!ImportPlan.Kinds.Contains(args[i+1],StringComparer.Ordinal))throw new ImportFailure("invalid_sync_override_kind");
            (flag=="--accept-new-kind"?newKinds:large).Add(args[++i]);
        }
        return Named(newKinds,large,full,reason);
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
    public static SourceSnapshot Read(Func<SourceSnapshot> read,out int attempts,CancellationToken cancellationToken=default)
    {
        for(attempts=1;attempts<=3;attempts++)
        {
            cancellationToken.ThrowIfCancellationRequested();var first=read();cancellationToken.ThrowIfCancellationRequested();var second=read();cancellationToken.ThrowIfCancellationRequested();var a=Fingerprints(first);var b=Fingerprints(second);
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
            // Incremental empty delta / module skip leaves the kind unread: not a source wipe.
            if(preview.ReadMode!="full"&&after==0&&row.Source==0&&row.New==0&&row.Changed==0)continue;
            // Open-period re-reads are intentionally partial vs the full target baseline.
            if(preview.ReadMode!="full"&&OpenPeriodReconcileKinds.Contains(row.Kind))continue;
            if(before-after>Math.Max(before*0.02m,1m))throw new ImportFailure("source_count_drop_over_threshold:"+row.Kind);
            // Immutable readings can grow freely after their explicit first acceptance.
            if(row.Kind=="vehicle_odometer_reading"&&row.Changed==0)continue;
            if(changes>Math.Max(before*0.10m,5m))throw new ImportFailure("kind_changes_over_threshold:"+row.Kind);
        }
    }
    static readonly HashSet<string> OpenPeriodReconcileKinds=new(StringComparer.Ordinal)
    {
        "chefz_daily","chefz_settlement","chefz_order_row","ninja_daily","ninja_daily_deduction",
        "housing_residence","jahez_transaction","platform_day_usage"
    };
}
