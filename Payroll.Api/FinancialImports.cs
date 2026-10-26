using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

// Process-scoped signed previews bind every row to its actor and current financial state.
public static class FinancialImports
{
    private static readonly byte[] Key=RandomNumberGenerator.GetBytes(32);
    private static string Canonical(JsonNode? node)=>node switch {
        JsonObject o=>"{"+string.Join(",",o.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>System.Text.Json.JsonSerializer.Serialize(x.Key)+":"+Canonical(x.Value)))+"}",
        JsonArray a=>"["+string.Join(",",a.Select(Canonical))+"]",
        JsonValue v when v.TryGetValue<decimal>(out var n)=>n.ToString("G29",CultureInfo.InvariantCulture),
        _=>node?.ToJsonString()??"null"
    };
    private static string Signature(DbConnection c,DbTransaction? tx,string kind,JsonArray rows,string actor,string expiry)
    {
        var state=new JsonArray();
        foreach(string k in new[]{"debt","rider","debt_settlement","journal","run","ledger","opening_reconciliation","debt_transfer"})
            foreach(var d in Store.List(c,k,tx).OrderBy(x=>x.S("id"),StringComparer.Ordinal))state.Add(d.Clone());
        string value=kind+"\n"+actor+"\n"+expiry+"\n"+Canonical(rows)+"\n"+Canonical(state);
        return Convert.ToHexString(HMACSHA256.HashData(Key,Encoding.UTF8.GetBytes(value)));
    }
    public static string Issue(DbConnection c,string kind,JsonArray rows,string actor)
    {
        string expiry=DateTimeOffset.UtcNow.AddMinutes(20).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return expiry+"."+Signature(c,null,kind,rows,actor,expiry);
    }
    public static void Verify(DbConnection c,DbTransaction tx,JsonObject input,string kind,string actor)
    {
        J.Need(input.B("confirm"),"أكد المعاينة قبل ترحيل الدفعة.");
        var rows=input["rows"] as JsonArray??throw new ApiError("صفوف المعاينة مطلوبة.");
        string[] parts=input.S("fingerprint").Split('.');
        bool valid=parts.Length==2&&long.TryParse(parts[0],out var expiry)&&expiry>DateTimeOffset.UtcNow.ToUnixTimeSeconds()&&parts[1].Length==64;
        if(valid){string expected=Signature(c,tx,kind,rows,actor,parts[0]);valid=CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected),Encoding.UTF8.GetBytes(parts[1]));}
        if(!valid)throw new ApiError("تغيرت البيانات أو انتهت المعاينة؛ أعد المعاينة والتأكيد.",409);
    }
    public static byte[] FileBytes(JsonObject input)
    {
        string text=input.Required("dataBase64");J.Need(text.Length<=12*1024*1024,"حد الملف 8 ميجابايت.");
        byte[] bytes=Convert.FromBase64String(text);J.Need(bytes.Length<=8*1024*1024,"حد الملف 8 ميجابايت.");return bytes;
    }
}
