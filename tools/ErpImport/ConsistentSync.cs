using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Payroll;
namespace ErpImport;

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
    public static void Validate(ImportSummary preview,Dictionary<string,JsonObject> existing)
    {
        foreach(var row in preview.Counts)
        {
            // Manual/certificate documents are outside the ERP-managed baseline.
            int before=existing.Values.Count(x=>x.S("__targetKind")==row.Kind&&x.S("erpSource")==row.Kind&&x.S("erpSourceId")!="");
            int after=row.New+row.Changed+row.Unchanged;
            if(before>0&&(before-after)*100L>before*2L)throw new ImportFailure("source_count_drop_over_two_percent:"+row.Kind);
            if(row.New+row.Changed>0&&(before==0||(row.New+row.Changed)*100L>before*10L))throw new ImportFailure("kind_changes_over_ten_percent:"+row.Kind);
        }
    }
}
