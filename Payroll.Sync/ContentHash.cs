using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace ErpImport;

public static class ContentHash
{
    static readonly HashSet<string> Ignore=new(StringComparer.Ordinal){ "contentHash","importedAt","importedBy","__targetKind","erpMissingInOpenPeriod" };
    public static string Of(JsonObject body)
    {
        var canonical=Canonical(body);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
    public static string Canonical(JsonNode? value)=>value switch
    {
        JsonObject obj=>"{"+string.Join(',',obj.Where(p=>!Ignore.Contains(p.Key)).OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>System.Text.Json.JsonSerializer.Serialize(p.Key)+":"+Canonical(p.Value)))+"}",
        JsonArray array=>"["+string.Join(',',array.Select(Canonical))+"]",
        _=>value?.ToJsonString()??"null"
    };
    public static JsonObject WithHash(JsonObject body)
    {
        var copy=body.Clone();copy.Remove("contentHash");
        copy["contentHash"]=Of(copy);return copy;
    }
}
