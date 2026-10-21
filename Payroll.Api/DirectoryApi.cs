using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

public static class RiderDirectory
{
    public static string Phone(string value)
    {
        if(string.IsNullOrWhiteSpace(value)) return "";
        string n=new(value.Where(c=>!char.IsWhiteSpace(c)&&c is not '(' and not ')' and not '-').ToArray());
        if(n.StartsWith("00",StringComparison.Ordinal)) n="+"+n[2..];
        if(n.StartsWith("05",StringComparison.Ordinal)) n="+966"+n[1..];
        if(n.StartsWith("966",StringComparison.Ordinal)) n="+"+n;
        J.Need(System.Text.RegularExpressions.Regex.IsMatch(n,@"^\+[1-9][0-9]{7,14}$"),"الجوال يجب أن يكون رقمًا دوليًا صحيحًا E.164.");return n;
    }
    public static string State(string value)=>value switch { "active" or "نشط" or "Active"=>"Active","في إجازة" or "OnLeave"=>"OnLeave","مؤرشف" or "هروب / انقطاع" or "Archived"=>"Archived","" or "مسودة" or "needs_review" or "NeedsReview"=>"NeedsReview",_=>throw new ApiError("حالة المندوب غير صالحة.") };
    public static JsonObject Normalize(JsonObject input, JsonObject? existing=null)
    {
        var r=existing?.Clone()??new JsonObject();
        string Value(string key,string legacy="")=>input.ContainsKey(key)?input.S(key):legacy!=""&&input.ContainsKey(legacy)?input.S(legacy):r.S(key);
        string iqama=DirectoryProfiles.Iqama(input.Required("iqama"));
        r["id"]=iqama;r["iqama"]=iqama;r["name"]=DirectoryProfiles.Name(input.Required("name"));
        r["phone"]=Phone(Value("phone")); r["nationality"]=Value("nationality");r["sponsor"]=Value("sponsor");r["city"]=Value("city");
        string engagement=Value("engagementType","engagement_type");
        engagement=engagement switch { "SponsoredInternal" or "كفالة الشركة"=>"SponsoredInternal","External" or "OutsideRider" or "خارجي"=>"External",""=>"",_=>throw new ApiError("نوع العلاقة غير صالح.") };
        r["engagementType"]=engagement;
        string role=Value("operationalRole","operational_role");
        role=role switch { "Car" or "سيارة"=>"Car","Motorcycle" or "دباب" or "دراجة نارية"=>"Motorcycle",""=>"",_=>"" };
        r["operationalRole"]=role;r["status"]=State(Value("status"));
        foreach(string k in new[]{"nationality","sponsor","city"}) J.Need(r.S(k).Length<=200,"قيمة وصفية طويلة جدًا.");
        r["schemaVersion"]=2;
        return r;
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/riders/sync-baseline",(JsonObject input,HttpContext ctx)=>{
            // The private baseline is uploaded by an authenticated admin, never shipped in wwwroot.
            var raw=input["riders"] as JsonArray??throw new ApiError("ارفع مصفوفة الدليل الخاصة.");
            J.Need(raw.Count is >0 and <=5000 && raw.All(x=>x is JsonObject),"حجم أو شكل دليل غير صالح.");
            var people=raw.OfType<JsonObject>().Where(x=>x.S("personRole")!="administrative"&&!x.B("isEmployee")).ToList();
            J.Need(people.Count>0,"لا توجد سجلات مناديب.");
            var ids=new HashSet<string>();var normalized=new List<JsonObject>();var errors=new JsonArray();
            for(int i=0;i<people.Count;i++)
            {
                try { var row=Normalize(people[i]);J.Need(ids.Add(row.S("id")),"إقامة مكررة في المصدر.");row["policyId"]="";row["sourceName"]=people[i].S("sourceName",row.S("name"));normalized.Add(row); }
                catch(ApiError e) { errors.Add(new JsonObject{["row"]=i+1,["error"]=e.Message}); }
            }
            string source=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw.ToJsonString())));
            object Process(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction? tx)
            {
                var existing=Store.List(c,"rider",tx).Select(x=>x.S("id")).ToHashSet();
                var added=normalized.Where(x=>!existing.Contains(x.S("id"))).ToList();
                string fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source+"|"+string.Join('|',existing.Order()))));
                if(input.S("mode")!="commit") return new { fingerprint,sourceFingerprint=source,total=people.Count,excludedAdministrative=raw.Count-people.Count,addCount=added.Count,existingCount=people.Count-added.Count,errors,rows=added };
                J.Need(errors.Count==0,"الدليل يحتوي أخطاء؛ لم تُكتب أي سجلات.");
                var prior=Store.Get(c,"baseline:"+source,"baseline_import",tx);
                if(prior!=null) return new { count=0,alreadyImported=true,batchId=prior.S("id") };
                J.Need(input.S("fingerprint")==fingerprint,"تغير الدليل منذ المعاينة؛ أعد المعاينة.");
                string batch="baseline:"+source;
                foreach(var row in added) { row["baselineSourceFingerprint"]=source;row["createdAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,"rider",row,tx!); }
                Store.Save(c,"baseline_import",new(){["id"]=batch,["sourceFingerprint"]=source,["count"]=added.Count,["at"]=DateTimeOffset.UtcNow.ToString("O")},tx!);
                Store.Audit(c,tx!,"rider.baseline_imported",batch,ctx.User.Identity!.Name!);return new{count=added.Count,alreadyImported=false,batchId=batch};
            }
            return input.S("mode")=="commit"?store.Write((c,tx)=>Process(c,tx)):store.Read(c=>Process(c,null));
        });
    }
}
