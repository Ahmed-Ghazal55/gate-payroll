using System.Globalization;
using System.Text.Json.Nodes;
namespace ErpImport;

/// <summary>
/// Soft-marks target mirror rows missing from an open-period re-read.
/// Never hard-deletes. Audited later as compact change-log ops in T3.
/// </summary>
public static class OpenPeriodReconcile
{
    static readonly Dictionary<string,string> KindPeriodField=new(StringComparer.Ordinal)
    {
        ["chefz_daily"]="date",["chefz_settlement"]="periodFrom",["ninja_daily"]="date",["ninja_daily_deduction"]="date",
        ["housing_residence"]="effectiveFrom",["jahez_transaction"]="occurredAt"
    };
    public static IEnumerable<PlannedDocument> MissingAsSoftDeleted(SourceSnapshot source,ImportPlan plan,Dictionary<string,JsonObject> existing)
    {
        if(source.ReadMode=="full")yield break;
        var (from,to)=SourceReadProfiles.OpenPeriodWindow(DateTimeOffset.UtcNow);
        var seen=plan.Documents.Where(d=>KindPeriodField.ContainsKey(d.Kind)).GroupBy(d=>d.Kind).ToDictionary(g=>g.Key,g=>g.Select(d=>d.Body.S("erpSourceId")).ToHashSet(StringComparer.OrdinalIgnoreCase));
        foreach(var row in existing.Values)
        {
            string kind=row.S("__targetKind");
            if(!KindPeriodField.TryGetValue(kind,out var field))continue;
            string erpId=row.S("erpSourceId");if(erpId==""||row.B("sourceDeleted"))continue;
            if(!InWindow(row.S(field),from,to))continue;
            if(seen.TryGetValue(kind,out var ids)&&ids.Contains(erpId))continue;
            var body=row.Clone();body.Remove("__targetKind");body["sourceDeleted"]=true;body["erpMissingInOpenPeriod"]=true;
            yield return new(kind,body);
        }
    }
    static bool InWindow(string raw,DateTime from,DateTime toExclusive)
    {
        if(raw=="")return false;
        if(!DateTime.TryParse(raw,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var at)
           &&!DateTime.TryParse(raw.Split('T')[0],CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out at))return false;
        return at>=from&&at<toExclusive;
    }
}
