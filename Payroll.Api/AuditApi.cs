using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-25: audit rebuild — data-change events only (M-0606), self-approvals report, read-summary.
/// WMRG: storage is audit.change_log / audit.read_summary_daily via Store redirects.</summary>
public static class AuditApi
{
    public const string ViewPerm="admin.audit.view",ReadSummaryPerm="admin.read_summary.view";
    public const string ChangeKind="audit",ReadSummaryKind="audit_read_summary_daily";
    static readonly Dictionary<string,string> ActionAr=new(StringComparer.OrdinalIgnoreCase)
    {
        ["create"]="إنشاء",["edit"]="تعديل",["update"]="تعديل",["approve"]="اعتماد",["reject"]="رفض",
        ["deactivate"]="إيقاف",["reactivate"]="تفعيل",["cancel"]="إلغاء",["delete"]="حذف",
        ["accounting.chart.create"]="إنشاء",["accounting.chart.edit"]="تعديل",
        ["accounting.chart.deactivate"]="إيقاف",["accounting.chart.reactivate"]="تفعيل",
        ["accounting.chart.deactivate_request"]="طلب إيقاف",
        ["approval.self_decision"]="اعتماد ذاتي",["approval.bypass_maker_checker"]="تجاوز اعتماد",
        ["approval.self_exception"]="استثناء اعتماد ذاتي",
        ["admin.user_create"]="إنشاء",["admin.user_update"]="تعديل",["admin.role_permissions"]="تعديل",
        ["admin.settings_save"]="تعديل",["notification.system_created"]="إنشاء",["notification.personal_created"]="إنشاء"
    };

    public static string LabelAr(string action)
    {
        if(ActionAr.TryGetValue(action,out var ar))return ar;
        foreach(var pair in ActionAr)
            if(action.EndsWith("."+pair.Key,StringComparison.OrdinalIgnoreCase)||action.Contains(pair.Key,StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        return "تعديل";
    }

    /// <summary>Short data-change row only. Never call for reads (M-0606).</summary>
    public static void WriteChange(DbConnection c,DbTransaction tx,string actionAr,string entityTypeAr,string entityRef,string actor,JsonObject? change=null)
    {
        var row=new JsonObject
        {
            ["id"]=Guid.NewGuid().ToString("N"),
            ["action"]=actionAr,
            ["actionAr"]=actionAr,
            ["entityTypeAr"]=entityTypeAr,
            ["entityId"]=entityRef,
            ["entityRef"]=entityRef,
            ["actor"]=actor,
            ["at"]=DateTimeOffset.UtcNow.ToString("O"),
            ["occurredAt"]=DateTimeOffset.UtcNow.ToString("O"),
            ["source"]="واجهة"
        };
        if(change!=null)
        {
            // Strip sensitive values from the short change payload.
            var safe=new JsonObject();
            foreach(var kv in change)
            {
                if(kv.Key is "iban" or "salary" or "phone" or "mobile" or "password" or "dataBase64")continue;
                safe[kv.Key]=kv.Value?.DeepClone();
            }
            row["change"]=safe;
        }
        Store.Save(c,ChangeKind,row,tx);
    }

    /// <summary>Daily read summary only — never a per-read change_log row (M-0614).</summary>
    public static void NoteRead(DbConnection c,DbTransaction tx,string userId,string area,bool sensitive)
    {
        string day=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        string id=$"read-summary:{day}:{userId}:{area}";
        var row=Store.Get(c,id,ReadSummaryKind,tx)??new JsonObject
        {
            ["id"]=id,["summaryDate"]=day,["userId"]=userId,["area"]=area,
            ["readCount"]=0,["exportCount"]=0,["sensitiveViewCount"]=0
        };
        row["readCount"]=row.N("readCount")+1;
        if(sensitive)row["sensitiveViewCount"]=row.N("sensitiveViewCount")+1;
        if(area.Contains("export",StringComparison.OrdinalIgnoreCase))row["exportCount"]=row.N("exportCount")+1;
        Store.Save(c,ReadSummaryKind,row,tx);
    }

    static JsonObject View(JsonObject a)
    {
        string action=a.S("action"),at=a.S("occurredAt",a.S("at")),entity=a.S("entityId",a.S("entityRef"));
        return new()
        {
            ["id"]=a.S("id"),
            // Legacy contract fields (enterprise/campaigns/submissions HTTP scripts).
            ["action"]=action,
            ["entityId"]=entity,
            ["at"]=at,
            ["occurredAt"]=at,
            ["actor"]=a.S("actor"),
            ["actionAr"]=a.S("actionAr",LabelAr(action)),
            ["entityTypeAr"]=a.S("entityTypeAr",a.S("entityType")),
            ["entityRef"]=a.S("entityRef",entity),
            ["change"]=a["change"]?.DeepClone()??new JsonObject{["action"]=action},
            ["source"]=a.S("source","واجهة"),
            ["allowedActions"]=new JsonArray()
        };
    }

    /// <summary>Filtered change-log rows as a plain list (ApiPagination wraps when ?page= is present).</summary>
    public static List<JsonObject> Query(DbConnection c,ClaimsPrincipal actor,string? from,string? to,string? userId,string? entityType,string? action)
    {
        AccessPolicy.Demand(actor,ViewPerm);
        IEnumerable<JsonObject> rows=Store.List(c,ChangeKind)
            .Where(a=>!a.S("action").StartsWith("read.",StringComparison.OrdinalIgnoreCase))
            .Select(View);
        if(!string.IsNullOrWhiteSpace(from)&&DateTimeOffset.TryParse(from,out var f))
            rows=rows.Where(a=>DateTimeOffset.TryParse(a.S("occurredAt"),out var at)&&at>=f);
        if(!string.IsNullOrWhiteSpace(to)&&DateTimeOffset.TryParse(to,out var t))
            rows=rows.Where(a=>DateTimeOffset.TryParse(a.S("occurredAt"),out var at)&&at<=t);
        if(!string.IsNullOrWhiteSpace(userId))rows=rows.Where(a=>string.Equals(a.S("actor"),userId,StringComparison.OrdinalIgnoreCase));
        if(!string.IsNullOrWhiteSpace(entityType))rows=rows.Where(a=>a.S("entityTypeAr").Contains(entityType,StringComparison.OrdinalIgnoreCase));
        if(!string.IsNullOrWhiteSpace(action))rows=rows.Where(a=>a.S("actionAr").Contains(action,StringComparison.OrdinalIgnoreCase)||a.S("action").Contains(action,StringComparison.OrdinalIgnoreCase)||a.ToJsonString().Contains(action,StringComparison.OrdinalIgnoreCase));
        return rows.OrderByDescending(a=>a.S("occurredAt")).ToList();
    }

    public static JsonObject List(DbConnection c,ClaimsPrincipal actor,string? from,string? to,string? userId,string? entityType,string? action,int page,int pageSize)
    {
        page=Math.Max(1,page);pageSize=Math.Clamp(pageSize,1,200);
        var list=Query(c,actor,from,to,userId,entityType,action);
        int total=list.Count;
        var items=new JsonArray(list.Skip((page-1)*pageSize).Take(pageSize).Select(a=>(JsonNode?)a).ToArray());
        return new(){["items"]=items,["page"]=page,["pageSize"]=pageSize,["total"]=total,["allowedActions"]=new JsonArray()};
    }

    public static JsonObject SelfApprovals(DbConnection c,ClaimsPrincipal actor,string? from,string? to)
    {
        AccessPolicy.Demand(actor,ViewPerm);
        var rows=Store.List(c,ChangeKind).Where(a=>
            a.B("selfApprovedException")
            ||a.S("action") is "approval.self_decision" or "approval.self_exception"
            ||a.S("action").Contains("self_",StringComparison.OrdinalIgnoreCase))
            .Select(View).ToList();
        if(!string.IsNullOrWhiteSpace(from)&&DateTimeOffset.TryParse(from,out var f))
            rows=rows.Where(a=>DateTimeOffset.TryParse(a.S("occurredAt"),out var at)&&at>=f).ToList();
        if(!string.IsNullOrWhiteSpace(to)&&DateTimeOffset.TryParse(to,out var t))
            rows=rows.Where(a=>DateTimeOffset.TryParse(a.S("occurredAt"),out var at)&&at<=t).ToList();
        return new(){["items"]=new JsonArray(rows.OrderByDescending(a=>a.S("occurredAt")).Select(a=>(JsonNode?)a).ToArray()),["allowedActions"]=new JsonArray()};
    }

    public static JsonObject ReadSummary(DbConnection c,ClaimsPrincipal actor,string? from,string? to,string? area)
    {
        AccessPolicy.Demand(actor,ReadSummaryPerm);
        IEnumerable<JsonObject> rows=Store.List(c,ReadSummaryKind);
        if(!string.IsNullOrWhiteSpace(from))rows=rows.Where(r=>string.CompareOrdinal(r.S("summaryDate"),from)>=0);
        if(!string.IsNullOrWhiteSpace(to))rows=rows.Where(r=>string.CompareOrdinal(r.S("summaryDate"),to)<=0);
        if(!string.IsNullOrWhiteSpace(area))rows=rows.Where(r=>string.Equals(r.S("area"),area,StringComparison.OrdinalIgnoreCase));
        var items=new JsonArray(rows.OrderByDescending(r=>r.S("summaryDate")).Select(r=>(JsonNode?)new JsonObject
        {
            ["summaryDate"]=r.S("summaryDate"),["userId"]=r.S("userId"),["area"]=r.S("area"),
            ["readCount"]=r.N("readCount"),["exportCount"]=r.N("exportCount"),["sensitiveViewCount"]=r.N("sensitiveViewCount"),
            ["allowedActions"]=new JsonArray()
        }).ToArray());
        return new(){["items"]=items,["allowedActions"]=new JsonArray()};
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        // Return a plain array so ApiPagination can opt-in with ?page=; keep legacy action/entityId fields.
        api.MapGet("/audit",(string? from,string? to,string? userId,string? entityType,string? action,HttpContext ctx)=>
            store.Read(c=>Query(c,ctx.User,from,to,userId,entityType,action)))
            .WithTags("Administration").WithSummary("Data-change audit log (no read events; ?page= wraps items)");
        api.MapGet("/admin/audit/self-approvals",(string? from,string? to,HttpContext ctx)=>
            store.Read(c=>SelfApprovals(c,ctx.User,from,to)))
            .WithTags("Administration").WithSummary("System Administrator self-approval report");
        api.MapGet("/admin/read-summary",(string? from,string? to,string? area,HttpContext ctx)=>
            store.Read(c=>ReadSummary(c,ctx.User,from,to,area)))
            .WithTags("Administration").WithSummary("Daily sensitive-read summary (not per-read audit rows)");
    }
}
