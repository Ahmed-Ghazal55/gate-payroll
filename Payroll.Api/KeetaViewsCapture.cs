using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-11b: advance high-water marks on Omar keeta.vw_keeta_*_v1 views;
/// eligibility is expected-only (invoice approves; NotValid → needs review, never auto-block).</summary>
public static class KeetaViewsCapture
{
    public const string ManagePermission="pay.manage";

    public static void Ensure(DbConnection c,DbTransaction? tx=null)=>SchemaKeetaHwm.Ensure(c,tx);

    public static JsonObject Advance(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,ManagePermission);
        Ensure(c,tx);
        string view=input.Required("viewName");
        J.Need(SchemaKeetaHwm.Views.Contains(view,StringComparer.OrdinalIgnoreCase),"عرض كيتا غير معروف.",400);
        long revision=(long)input.N("revision");
        string? snapshot=NullIfEmpty(input.S("snapshotId"));
        string? created=NullIfEmpty(input.S("createdAt"));
        var current=Load(c,tx,view);
        long prior=(long)current.N("lastRevision");
        J.Need(revision>=prior,"علامة الماء العالية لا تتراجع.",409);
        if(revision==prior&&snapshot==current.S("lastSnapshotId")&&created==current.S("lastCreatedAt"))
            return WithActions(current);
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"sal","keeta_view_hwm")}
            SET last_revision=@rev,last_snapshot_id=@snap,last_created_at=@created,advanced_at=@at,updated_by=@by,updated_at=@at
            WHERE view_name=@v
            """,("@rev",revision),("@snap",(object?)snapshot??DBNull.Value),("@created",(object?)created??DBNull.Value),
            ("@at",now),("@by",actor.Identity!.Name!),("@v",view));
        Store.Audit(c,tx,"sal.keeta_hwm_advanced",view,actor.Identity!.Name!);
        return WithActions(Load(c,tx,view));
    }

    public static JsonObject Status(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string? viewName=null)
    {
        AccessPolicy.Demand(actor,"pay.view");
        Ensure(c,tx);
        var views=new JsonArray();
        foreach(string view in SchemaKeetaHwm.Views)
        {
            if(viewName!=null&&!string.Equals(view,viewName,StringComparison.OrdinalIgnoreCase))continue;
            views.Add(Load(c,tx,view));
        }
        return new(){["views"]=views,["allowedActions"]=new JsonArray("advance_keeta_hwm")};
    }

    /// <summary>Map Omar eligibility row to capture payload: expected only; NotValid → needs review (D19 / M-0694).</summary>
    public static JsonObject EligibilityCaptureRow(JsonObject source)
    {
        string status=source.S("expectedStatus",source.S("status"));
        bool notValid=string.Equals(status,"NotValid",StringComparison.OrdinalIgnoreCase);
        bool pending=string.Equals(status,"Pending",StringComparison.OrdinalIgnoreCase);
        bool valid=string.Equals(status,"Valid",StringComparison.OrdinalIgnoreCase);
        string reason=source.S("reason");
        if(reason==""&&notValid)reason="Omar eligibility NotValid — needs review; invoice decides";
        if(reason==""&&pending)reason="Omar eligibility Pending — needs review";
        return new()
        {
            ["id"]=source.S("id",source.S("snapshotId")),
            ["erpRiderId"]=source.S("erpRiderId",source.S("riderProfileId")),
            // Expected input only: do not mark ineligible for NotValid (invoice approves).
            ["eligible"]=valid||notValid||pending,
            ["needsReview"]=notValid||pending||source.B("needsReview"),
            ["expectedStatus"]=status,
            ["reason"]=reason,
            ["source"]="keeta_eligibility_view"
        };
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/sal/keeta/hwm",(HttpContext ctx)=>store.Read(c=>Status(c,null,ctx.User,ctx.Request.Query["viewName"])))
            .WithTags("Payroll").WithSummary("Read Keeta view high-water marks");
        api.MapPost("/sal/keeta/hwm/advance",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Advance(c,tx,ctx.User,input)))
            .WithTags("Payroll").WithSummary("Advance a Keeta view high-water mark without full re-scan");
    }

    static JsonObject Load(DbConnection c,DbTransaction? tx,string view)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT view_name,last_revision,last_snapshot_id,last_created_at,advanced_at
            FROM {SchemaSql.Q(c,"sal","keeta_view_hwm")} WHERE view_name=@v
            """;
        var p=cmd.CreateParameter();p.ParameterName="@v";p.Value=view;cmd.Parameters.Add(p);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"عرض كيتا غير مهيأ.",404);
        return new()
        {
            ["viewName"]=r["view_name"].ToString(),
            ["lastRevision"]=Convert.ToInt64(r["last_revision"]),
            ["lastSnapshotId"]=r["last_snapshot_id"] is DBNull?"":r["last_snapshot_id"].ToString(),
            ["lastCreatedAt"]=r["last_created_at"] is DBNull?"":r["last_created_at"].ToString(),
            ["advancedAt"]=r["advanced_at"].ToString()
        };
    }

    static JsonObject WithActions(JsonObject body)
    {body["allowedActions"]=new JsonArray("advance_keeta_hwm");return body;}
    static string? NullIfEmpty(string? s)=>string.IsNullOrWhiteSpace(s)?null:s;
}
