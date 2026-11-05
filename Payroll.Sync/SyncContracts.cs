using System.Globalization;
using System.Text.Json.Nodes;
namespace ErpImport;

// Callers supply the existing finance catalog; the sync library owns no company data.
public interface ISyncSponsor
{
    string Id {get;} string Code {get;} string UnifiedNumber {get;}
    string NameAr {get;} string ShortNameAr {get;} string CommercialReg {get;}
}
internal static class SyncJson
{
    internal static string S(this JsonNode? n,string key,string fallback="")=>n?[key]?.ToString()?.Trim()??fallback;
    internal static decimal N(this JsonNode? n,string key,decimal fallback=0)=>decimal.TryParse(n.S(key),NumberStyles.Number,CultureInfo.InvariantCulture,out var v)?v:fallback;
    internal static bool B(this JsonNode? n,string key)=>bool.TryParse(n.S(key),out var v)&&v;
    internal static JsonObject Clone(this JsonObject n)=>(JsonObject)n.DeepClone();
}
