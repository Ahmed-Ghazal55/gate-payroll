using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

// Operator-only, private snapshots. There is deliberately no public migration endpoint.
public static class StorageTransfer
{
    public static JsonArray Export(Store store)=>store.Read(c=>{
        using var tx=c.BeginTransaction(System.Data.IsolationLevel.Serializable);
        using var cmd=Store.Command(c,tx,"SELECT id,kind,body FROM records ORDER BY id");using var reader=cmd.ExecuteReader();var records=new JsonArray();
        while(reader.Read())records.Add(new JsonObject{["id"]=reader.GetString(0),["kind"]=reader.GetString(1),["body"]=JsonNode.Parse(reader.GetString(2))});
        reader.Close();tx.Commit();return records;
    });
    public static string Fingerprint(JsonArray records)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(records.ToJsonString())));
    public static int Import(Store store,JsonArray records,string fingerprint)
    {
        J.Need(Fingerprint(records)==fingerprint,"بصمة ملف النقل غير متطابقة.");
        var ids=new HashSet<string>();var userNames=new HashSet<string>();var runs=new HashSet<string>();var submissions=new HashSet<string>();
        foreach(var item in records)
        {
            var row=item as JsonObject??throw new ApiError("صيغة سجل غير صحيحة.");var body=row["body"] as JsonObject??throw new ApiError("محتوى سجل غير صالح.");
            J.Need(row.Required("id")==body.Required("id")&&row.S("id").Length<=128&&ids.Add(row.S("id")),"معرف مكرر أو غير متطابق.");
            string kind=row.Required("kind");J.Need(System.Text.RegularExpressions.Regex.IsMatch(kind,"^[a-z_]{1,40}$"),"نوع سجل غير صالح.");
            if(kind=="user")J.Need(userNames.Add(UserAccess.Normalize(body.Required("username"))),"اسم مستخدم مكرر.");
            if(kind=="run")J.Need(runs.Add(body.Required("iqama")+"|"+body.Required("period")),"مسير مكرر.");
            if(kind=="submission")J.Need(submissions.Add(body.Required("campaignId")+"|"+body.Required("iqama")),"رد مكرر.");
        }
        return store.Write((c,tx)=>{
            using(var count=Store.Command(c,tx,"SELECT COUNT(*) FROM records"))J.Need(Convert.ToInt64(count.ExecuteScalar())==0,"وجهة النقل ليست فارغة؛ لم تُستبدل بيانات قائمة.");
            foreach(var row in records.OfType<JsonObject>())Store.Save(c,row.S("kind"),(JsonObject)row["body"]!,tx);
            Store.Audit(c,tx,"storage.migrated",fingerprint,"operator");return records.Count;
        });
    }
}
