using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ErpImport;
namespace Payroll;

// One durable admission gate covers manual, startup, daily and preview requests across API instances.
public static class ErpSyncRequests
{
    public const string Kind="erp_sync_request",Permission="erp.sync.run";
    public static readonly TimeSpan Cooldown=TimeSpan.FromMinutes(15);
    public static bool ValidId(string id)=>Regex.IsMatch(id,"^erp-sync-request:[a-f0-9]{32}$");
    public static string Reserve(Store store,JobsOptions options,bool dryRun,string trigger,string actor,DateTimeOffset at,JsonObject? overrides=null)
    {
        J.Need(options.Enabled,"خدمة المهام غير مفعلة.",409);
        if(!new[]{"manual","dry_run","startup","daily"}.Contains(trigger))throw new ImportFailure("invalid_sync_trigger");
        if(!dryRun&&!options.ErpSyncEnabled)throw new ImportFailure("erp_sync_paused_or_disabled");
        return store.Write((c,tx)=>
        {
            var control=Store.Get(c,JobsControl.Id,JobsControl.Kind,tx)??new JsonObject{["id"]=JobsControl.Id};
            if(!dryRun&&control.B("paused"))throw new ImportFailure("erp_sync_paused_or_disabled");
            if(control.S("activeRequestId")!="")throw new ImportFailure("sync_in_progress");
            if(DateTimeOffset.TryParse(control.S("lastAcceptedAt"),out var previous)&&at-previous<Cooldown)throw new ImportFailure("sync_cooldown");
            string id="erp-sync-request:"+Guid.NewGuid().ToString("N");
            Store.Save(c,Kind,new(){["id"]=id,["status"]="queued",["mode"]=dryRun?"dry_run":"commit",["trigger"]=trigger,["requestedBy"]=actor,["queuedAt"]=at.ToString("O"),["counts"]=new JsonArray(),["overrides"]=overrides?.DeepClone()},tx);if(overrides!=null)Store.Audit(c,tx,"jobs.erp_sync_override",id,actor);
            control["activeRequestId"]=id;control["lastAcceptedAt"]=at.ToString("O");Store.Save(c,JobsControl.Kind,control,tx);
            Store.Audit(c,tx,"jobs.erp_sync_requested",id,actor);return id;
        },false);
    }
    // Administrator-only, per run, with a reason. Kinds must exist; there is no permanent bypass.
    public static JsonObject? ReadOverrides(ClaimsPrincipal actor,JsonObject? input)
    {
        var fresh=input?["acceptNewKinds"] as JsonArray;var large=input?["acceptLargeChanges"] as JsonArray;
        bool full=input?.B("fullResync")==true;
        if((fresh?.Count??0)==0&&(large?.Count??0)==0&&!full)return null;
        J.Need(AccessPolicy.Administrator(actor),"قبول التجاوز لمسؤول النظام فقط.",403);
        string reason=input!.S("reason").Trim();J.Need(reason.Length is >=3 and <=500&&!reason.Any(char.IsControl),"اكتب سبب قبول التجاوز.");
        foreach(var kind in (fresh??[]).Concat(large??[]))J.Need(ImportPlan.Kinds.Contains(kind?.ToString()??""),"نوع بيانات غير معروف.");
        return new(){["acceptNewKinds"]=(fresh??[]).DeepClone(),["acceptLargeChanges"]=(large??[]).DeepClone(),["fullResync"]=full,["reason"]=reason};
    }
    public static SyncOverrides Overrides(DbConnection c,string id)
    {
        var row=Store.Get(c,id,Kind)?["overrides"] as JsonObject;if(row==null)return SyncOverrides.None;
        static IEnumerable<string> Names(JsonNode? list)=>(list as JsonArray??[]).Select(k=>k?.ToString()??"");
        return SyncOverrides.Named(Names(row["acceptNewKinds"]),Names(row["acceptLargeChanges"]),row.B("fullResync"),row.S("reason"));
    }
    public static void Link(Store store,string id,string jobId)
    {
        J.Need(Regex.IsMatch(jobId,"^[1-9][0-9]{0,19}$"),"معرف المهمة غير صالح.");
        store.Write((c,tx)=>{var row=Store.Get(c,id,Kind,tx)??throw new ImportFailure("sync_request_missing");
            if(row.S("jobId")!=""&&row.S("jobId")!=jobId)throw new ImportFailure("sync_job_identity_changed");
            var link=Store.Get(c,"erp-sync-job:"+jobId,"erp_sync_job_link",tx);
            if(link!=null&&link.S("requestId")!=id)throw new ImportFailure("sync_job_identity_changed");
            row["jobId"]=jobId;Store.Save(c,Kind,row,tx);
            Store.Save(c,"erp_sync_job_link",new(){["id"]="erp-sync-job:"+jobId,["requestId"]=id},tx);return true;},false);
    }
    public static void EnsureActive(DbConnection c,DbTransaction tx,string id)
    {
        var request=Store.Get(c,id,Kind,tx);
        if(request?.S("status")!="running"||Store.Get(c,JobsControl.Id,JobsControl.Kind,tx)?.S("activeRequestId")!=id)throw new ImportFailure("sync_request_not_active");
    }
    public static JsonObject? Start(Store store,string id,DateTimeOffset at)
    {
        if(!ValidId(id))throw new ImportFailure("invalid_sync_request");
        return store.Write((c,tx)=>
        {
            var row=Store.Get(c,id,Kind,tx)??throw new ImportFailure("sync_request_missing");
            if(row.S("status") is "succeeded" or "refused" or "failed")return null;
            if(Store.Get(c,JobsControl.Id,JobsControl.Kind,tx)?.S("activeRequestId")!=id)throw new ImportFailure("sync_request_not_active");
            row["status"]="running";row["startedAt"]=at.ToString("O");Store.Save(c,Kind,row,tx);return row;
        },false);
    }
    // Called in the same transaction as erp_sync_run and the refusal notification.
    public static void Complete(DbConnection c,DbTransaction tx,string id,JsonObject run)
    {
        var row=Store.Get(c,id,Kind,tx)??throw new ImportFailure("sync_request_missing");
        row["status"]=run.S("status")=="completed"?"succeeded":run.S("status");
        foreach(string key in new[]{"startedAt","finishedAt","refusalCode","counts"})row[key]=run[key]?.DeepClone();
        row["runId"]=run.S("id");Store.Save(c,Kind,row,tx);
        var control=Store.Get(c,JobsControl.Id,JobsControl.Kind,tx);
        if(control?.S("activeRequestId")==id){control["activeRequestId"]="";Store.Save(c,JobsControl.Kind,control,tx);}
    }
    public static JsonObject View(DbConnection c,string jobId)
    {
        J.Need(Regex.IsMatch(jobId,"^[1-9][0-9]{0,19}$"),"معرف المهمة غير صالح.");
        // Job polling uses two primary-key seeks, not a JSON scan beside the writer.
        var link=Store.Get(c,"erp-sync-job:"+jobId,"erp_sync_job_link")??throw new ApiError("المهمة غير موجودة.",404);
        var row=Store.Get(c,link.S("requestId"),Kind)??throw new ApiError("المهمة غير موجودة.",404);var view=new JsonObject();
        foreach(string key in new[]{"jobId","status","mode","queuedAt","startedAt","finishedAt","refusalCode"})view[key]=row[key]?.DeepClone();
        view["counts"]=new JsonArray((row["counts"] as JsonArray??[]).OfType<JsonObject>().Select(r=>{var count=new JsonObject();foreach(string f in new[]{"kind","source","new","changed","unchanged","rejected"})count[f]=r[f]?.DeepClone();return (JsonNode)count;}).ToArray());return view;
    }
    public static DateTimeOffset? LastSuccess(DbConnection c)
    {
        bool sqlite=c is Microsoft.Data.Sqlite.SqliteConnection;
        string condition=sqlite?"json_extract(body,'$.status')='completed' AND COALESCE(json_extract(body,'$.mode'),'commit')='commit'":"JSON_VALUE(body,'$.status')='completed' AND COALESCE(JSON_VALUE(body,'$.mode'),'commit')='commit'";
        using var query=Store.Command(c,null,sqlite?$"SELECT json_extract(body,'$.finishedAt') FROM records WHERE kind=@kind AND {condition} ORDER BY json_extract(body,'$.finishedAt') DESC LIMIT 1":$"SELECT TOP (1) JSON_VALUE(body,'$.finishedAt') FROM dbo.records WHERE kind=@kind AND {condition} ORDER BY JSON_VALUE(body,'$.finishedAt') DESC",("@kind",ErpSyncRuns.Kind));
        return DateTimeOffset.TryParse(query.ExecuteScalar()?.ToString(),out var at)?at:null;
    }
    public static void SeedPermission(Store store)
    {
        const string marker="migration:erp-sync-permission:v1";
        store.Write((c,tx)=>
        {
            if(Store.Get(c,marker,"setting",tx)!=null)return false;
            foreach(var user in Store.List(c,"user",tx).Where(u=>UserAccess.Role(u) is "SystemAdmin" or "FinancialManager"))
            {
                if((user["deniedPermissions"] as JsonArray??[]).Any(p=>p?.ToString()==Permission))continue;
                if(Grant(user)){Store.Save(c,"user",user,tx);Store.Audit(c,tx,"jobs.erp_sync_permission_seeded",user.S("id"),"system-migration");}
            }
            Store.Save(c,"setting",new(){["id"]=marker,["version"]=1},tx);return true;
        },false);
    }
    public static bool Grant(JsonObject user)
    {
        if(UserAccess.Role(user) is not ("SystemAdmin" or "FinancialManager"))return false;
        var permissions=user["customPermissions"] as JsonArray;
        if(permissions?.Any(p=>string.Equals(p?.ToString(),Permission,StringComparison.OrdinalIgnoreCase))==true)return false;
        if(permissions==null){permissions=[];user["customPermissions"]=permissions;}
        permissions.Add(Permission);return true;
    }
}
