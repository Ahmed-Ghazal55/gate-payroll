using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Payroll;

public sealed record ErpSyncKindCount(string Kind,int Source,int New,int Changed,int Unchanged,int Rejected);
public static class ErpSyncRuns
{
    public const string Kind="erp_sync_run";
    public static JsonObject Record(Store store,string runId,DateTimeOffset startedAt,DateTimeOffset finishedAt,string status,IEnumerable<ErpSyncKindCount> counts,string? refusalCode=null)
    {
        J.Need(Regex.IsMatch(runId,"^erp-sync:[a-f0-9]{32}$")&&status is "completed" or "refused" or "failed"&&finishedAt>=startedAt,"بيانات سجل المزامنة غير صالحة.");
        J.Need(refusalCode==null||Regex.IsMatch(refusalCode,"^[A-Za-z0-9_:.-]{1,160}$"),"رمز الرفض غير صالح.");
        var rows=counts.ToList();J.Need(rows.Select(r=>r.Kind).Distinct().Count()==rows.Count&&rows.Count<=100,"أنواع المزامنة غير صالحة.");
        J.Need(rows.All(r=>Regex.IsMatch(r.Kind,"^[a-z_]{1,40}$")&&new[]{r.Source,r.New,r.Changed,r.Unchanged,r.Rejected}.All(n=>n>=0)),"أعداد المزامنة غير صالحة.");
        var run=new JsonObject{["id"]=runId,["startedAt"]=startedAt.ToString("O"),["finishedAt"]=finishedAt.ToString("O"),["createdAt"]=finishedAt.ToString("O"),["status"]=status,["refusalCode"]=refusalCode,["counts"]=new JsonArray(rows.Select(r=>(JsonNode)new JsonObject{["kind"]=r.Kind,["source"]=r.Source,["new"]=r.New,["changed"]=r.Changed,["unchanged"]=r.Unchanged,["rejected"]=r.Rejected}).ToArray())};
        return store.Write((c,tx)=>
        {
            if(Store.Get(c,runId,Kind,tx) is {} existing)return existing;
            Store.Save(c,Kind,run,tx);
            if(status=="refused")Notifications.Raise(c,tx,"notice:"+runId,"approvals","توقفت مزامنة ERP للمراجعة؛ راجع سجل المزامنة.","users.manage","erp-sync",administratorsOnly:true);
            return run;
        },false);
    }
    public static List<JsonObject> Recent(DbConnection c,ClaimsPrincipal user,int limit=20)
    {
        if(!AccessPolicy.Administrator(user))throw new ApiError("سجل المزامنة لمسؤول النظام فقط.",403);
        J.Need(limit is >=1 and <=100,"الحد بين 1 و100.");bool sqlite=c is Microsoft.Data.Sqlite.SqliteConnection;
        using var query=Store.Command(c,null,sqlite?"SELECT body FROM records WHERE kind=@kind ORDER BY json_extract(body,'$.finishedAt') DESC,id DESC LIMIT @limit":"SELECT TOP (@limit) body FROM dbo.records WHERE kind=@kind ORDER BY JSON_VALUE(body,'$.finishedAt') DESC,id DESC",("@kind",Kind),("@limit",limit));
        using var reader=query.ExecuteReader();var result=new List<JsonObject>();
        while(reader.Read())
        {
            var row=JsonNode.Parse(reader.GetString(0))!.AsObject();var view=new JsonObject();
            foreach(string field in new[]{"id","startedAt","finishedAt","status","refusalCode"})view[field]=row[field]?.DeepClone();
            view["counts"]=new JsonArray((row["counts"] as JsonArray??[]).OfType<JsonObject>().Select(r=>{var count=new JsonObject();foreach(string f in new[]{"kind","source","new","changed","unchanged","rejected"})count[f]=r[f]?.DeepClone();return (JsonNode)count;}).ToArray());result.Add(view);
        }
        return result;
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/system/erp-sync",(int? limit,HttpContext ctx)=>store.Read(c=>Recent(c,ctx.User,limit??20))).WithTags("Administration").WithSummary("Read administrator-only counts and refusal codes for recent ERP sync runs");
    }
}
