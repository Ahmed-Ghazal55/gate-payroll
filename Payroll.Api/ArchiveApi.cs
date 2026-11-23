using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-39: archive policies + restore-request (temporary read-only view &gt;3y, approval + expiry). No hard delete (M-0632).</summary>
public static class ArchiveApi
{
    public const string Permission="admin.archive.manage",ApprovalKind="archive_restore";
    public const int RetentionYears=3;

    public static JsonObject ListPolicies(DbConnection c,ClaimsPrincipal actor)
    {
        AccessPolicy.Demand(actor,Permission);
        ExpireStale(c,null);
        var items=new JsonArray();
        using(var cmd=Store.Command(c,null,$"SELECT id,code,name_ar,retention_years,hard_delete_allowed,restore_view_hours FROM {SchemaSql.Q(c,"archive","policy")}"))
        using(var r=cmd.ExecuteReader())
        while(r.Read())
            items.Add(new JsonObject
            {
                ["id"]=r.GetValue(0).ToString(),
                ["code"]=r.GetString(1),
                ["nameAr"]=r.GetString(2),
                ["retentionYears"]=Convert.ToInt32(r.GetValue(3),CultureInfo.InvariantCulture),
                ["hardDeleteAllowed"]=Convert.ToInt32(r.GetValue(4),CultureInfo.InvariantCulture)!=0,
                ["restoreViewHours"]=Convert.ToInt32(r.GetValue(5),CultureInfo.InvariantCulture)
            });
        var restores=new JsonArray();
        using(var cmd=Store.Command(c,null,$"""
            SELECT id,entity_type,period_from,period_to,reason_ar,status,expires_at,approval_request_id
            FROM {SchemaSql.Q(c,"archive","restore_request")}
            ORDER BY created_at DESC
            """))
        using(var r=cmd.ExecuteReader())
        while(r.Read())
            restores.Add(new JsonObject
            {
                ["id"]=r.GetValue(0).ToString(),
                ["entityType"]=r.GetString(1),
                ["periodFrom"]=Convert.ToDateTime(r.GetValue(2),CultureInfo.InvariantCulture).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
                ["periodTo"]=Convert.ToDateTime(r.GetValue(3),CultureInfo.InvariantCulture).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
                ["reasonAr"]=r.GetString(4),
                ["status"]=r.GetString(5),
                ["expiresAt"]=r.IsDBNull(6)?"":Convert.ToDateTime(r.GetValue(6),CultureInfo.InvariantCulture).ToString("O"),
                ["approvalRequestId"]=r.IsDBNull(7)?"":r.GetValue(7).ToString(),
                ["readOnly"]=true
            });
        return new()
        {
            ["items"]=items,
            ["restoreRequests"]=restores,
            ["retentionYears"]=RetentionYears,
            ["hardDeleteAllowed"]=false,
            ["allowedActions"]=new JsonArray("request_restore"),
            ["messageAr"]="لا يُحذف شيء؛ الاستعادة عرض تدقيق مؤقت للقراءة فقط بعد الاعتماد."
        };
    }

    public static JsonObject RequestRestore(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,Permission);
        string entityType=input.Required("entityType").Trim();
        J.Need(entityType.Length is >=1 and <=80,"نوع الكيان غير صالح.",400);
        string from=input.Required("periodFrom").Trim(),to=input.Required("periodTo").Trim();
        J.Need(DateOnly.TryParse(from,CultureInfo.InvariantCulture,DateTimeStyles.None,out var fromDate),"فترة غير صالحة.",400);
        J.Need(DateOnly.TryParse(to,CultureInfo.InvariantCulture,DateTimeStyles.None,out var toDate),"فترة غير صالحة.",400);
        J.Need(toDate>=fromDate,"نهاية الفترة قبل بدايتها.",400);
        var cutoff=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-RetentionYears));
        J.Need(toDate<=cutoff,$"الاستعادة فقط لبيانات أقدم من {RetentionYears} سنوات.",400);
        string reason=input.Required("reasonAr").Trim();
        J.Need(reason.Length is >=3 and <=400,"سبب الاستعادة مطلوب.",400);

        object? policyId=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"archive","policy")} WHERE code=@c",("@c","default_3y"));
        J.Need(policyId!=null,"سياسة الأرشفة غير مهيأة.",500);
        string policy=policyId switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(policyId,CultureInfo.InvariantCulture)!};
        int hours=Convert.ToInt32(SchemaSql.Scalar(c,tx,$"SELECT restore_view_hours FROM {SchemaSql.Q(c,"archive","policy")} WHERE id=@id",("@id",policy))!,CultureInfo.InvariantCulture);

        string userId=SchemaApprovals.EnsureAuthUserId(c,tx,actor.Identity!.Name!);
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        string approvalId=SchemaSql.GuidText(SchemaSql.NewId());
        Store.Save(c,"approval_request",new()
        {
            ["id"]=approvalId,
            ["kind"]=ApprovalKind,
            ["titleAr"]="طلب استعادة عرض أرشيفي",
            ["bodyAr"]=reason,
            ["targetDepartment"]="MGT",
            ["requiredRankLevel"]=4,
            ["referenceId"]=id,
            ["status"]="pending",
            ["requestedBy"]=actor.Identity!.Name!,
            ["payload"]=new JsonObject{["restoreRequestId"]=id,["entityType"]=entityType,["periodFrom"]=from,["periodTo"]=to,["restoreViewHours"]=hours}
        },tx);

        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"archive","restore_request")}
            (id,policy_id,entity_type,period_from,period_to,reason_ar,status,approval_request_id,requested_by,created_by,created_at)
            VALUES(@id,@p,@e,@f,@t,@r,'pending',@a,@u,@by,{SchemaSql.NowLiteral(c)})
            """,
            ("@id",id),("@p",policy),("@e",entityType),("@f",from),("@t",to),("@r",reason),
            ("@a",approvalId),("@u",userId),("@by",actor.Identity!.Name!));

        Store.Audit(c,tx,"archive.restore_request",id,actor.Identity!.Name!);
        return new()
        {
            ["id"]=id,
            ["status"]="pending",
            ["approvalRequestId"]=approvalId,
            ["readOnly"]=true,
            ["allowedActions"]=new JsonArray("request_restore"),
            ["messageAr"]="طُلبت استعادة عرض مؤقت؛ بانتظار اعتماد مستقل."
        };
    }

    public static void ApplyApproval(DbConnection c,DbTransaction tx,string approvalRequestId,string outcome,string decidedBy)
    {
        object? restoreId=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"archive","restore_request")} WHERE approval_request_id=@a",("@a",approvalRequestId));
        if(restoreId==null)return;
        string id=restoreId switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(restoreId,CultureInfo.InvariantCulture)!};
        if(outcome!="approved")
        {
            SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"archive","restore_request")} SET status='rejected', updated_at={SchemaSql.NowLiteral(c)} WHERE id=@id",("@id",id));
            return;
        }
        int hours=72;
        object? h=SchemaSql.Scalar(c,tx,$"""
            SELECT p.restore_view_hours FROM {SchemaSql.Q(c,"archive","restore_request")} r
            JOIN {SchemaSql.Q(c,"archive","policy")} p ON p.id=r.policy_id WHERE r.id=@id
            """,("@id",id));
        if(h!=null)hours=Convert.ToInt32(h,CultureInfo.InvariantCulture);
        // Space-separated UTC matches SQLite datetime('now') / SQL datetime2 text compares.
        string expires=DateTimeOffset.UtcNow.AddHours(hours).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff",CultureInfo.InvariantCulture);
        string? approverId=null;
        try{approverId=SchemaApprovals.EnsureAuthUserId(c,tx,decidedBy);}catch{/* optional */}
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"archive","restore_request")}
            SET status='active', approved_by=@by, expires_at=@ex, updated_at={SchemaSql.NowLiteral(c)}
            WHERE id=@id
            """,("@by",(object?)approverId??DBNull.Value),("@ex",expires),("@id",id));
        Store.Audit(c,tx,"archive.restore_approved",id,decidedBy);
    }

    static void ExpireStale(DbConnection c,DbTransaction? tx)
    {
        // Parse timestamps in process so ISO-with-T and space formats both expire on the same calendar day.
        var expired=new List<string>();
        using(var cmd=Store.Command(c,tx,$"""
            SELECT id,expires_at FROM {SchemaSql.Q(c,"archive","restore_request")}
            WHERE status='active' AND expires_at IS NOT NULL
            """))
        using(var r=cmd.ExecuteReader())
        while(r.Read())
        {
            string id=r.GetValue(0).ToString()!;
            string raw=Convert.ToString(r.GetValue(1),CultureInfo.InvariantCulture)??"";
            if(!DateTimeOffset.TryParse(raw,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var ex))
            {
                // SQLite may return DateTime already.
                if(r.GetValue(1) is DateTime dt)ex=new DateTimeOffset(DateTime.SpecifyKind(dt,DateTimeKind.Utc));
                else continue;
            }
            if(ex.ToUniversalTime()<=DateTimeOffset.UtcNow)expired.Add(id);
        }
        foreach(string id in expired)
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"archive","restore_request")}
                SET status='expired', updated_at={SchemaSql.NowLiteral(c)}
                WHERE id=@id AND status='active'
                """,("@id",id));
    }

    public static bool HasActiveRestore(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string entityType,string? restoreRequestId=null)
    {
        ExpireStale(c,tx);
        AccessPolicy.Demand(actor,Permission);
        string sql=$"""
            SELECT COUNT(1) FROM {SchemaSql.Q(c,"archive","restore_request")}
            WHERE status='active' AND entity_type=@e
            {(string.IsNullOrWhiteSpace(restoreRequestId)?"":"AND id=@id")}
            """;
        var args=new List<(string,object)>{("@e",entityType)};
        if(!string.IsNullOrWhiteSpace(restoreRequestId))args.Add(("@id",restoreRequestId));
        return Convert.ToInt64(SchemaSql.Scalar(c,tx,sql,args.ToArray())!)>0;
    }

    /// <summary>Temporary read-only restore view; requires an active unexpired approval for the entity type.</summary>
    public static JsonObject RestoreView(DbConnection c,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,Permission);
        ExpireStale(c,null);
        string entityType=input.Required("entityType").Trim();
        string restoreId=input.S("restoreRequestId");
        J.Need(HasActiveRestore(c,null,actor,entityType,string.IsNullOrWhiteSpace(restoreId)?null:restoreId),
            "يلزم اعتماد استعادة ساري غير منتهٍ لعرض الأرشيف.",403);
        string from=input.S("periodFrom"),to=input.S("periodTo");
        J.Need(DateOnly.TryParse(from,CultureInfo.InvariantCulture,DateTimeStyles.None,out var fromDate),"فترة غير صالحة.",400);
        J.Need(DateOnly.TryParse(to,CultureInfo.InvariantCulture,DateTimeStyles.None,out var toDate),"فترة غير صالحة.",400);
        var items=new JsonArray();
        if(entityType is "chat.message" or "chat")
        {
            using var cmd=Store.Command(c,null,$"""
                SELECT id,channel_id,body_ar,created_at FROM {SchemaSql.Q(c,"chat","message")}
                WHERE created_at >= @f AND created_at < @t
                ORDER BY created_at ASC
                """,
                ("@f",fromDate.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-dd HH:mm:ss.fffffff",CultureInfo.InvariantCulture)),
                ("@t",toDate.AddDays(1).ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-dd HH:mm:ss.fffffff",CultureInfo.InvariantCulture)));
            using var r=cmd.ExecuteReader();
            while(r.Read())
                items.Add(new JsonObject
                {
                    ["id"]=r.GetValue(0).ToString(),
                    ["channelId"]=r.GetValue(1).ToString(),
                    ["bodyAr"]=r.IsDBNull(2)?"":r.GetString(2),
                    ["createdAt"]=Convert.ToDateTime(r.GetValue(3),CultureInfo.InvariantCulture).ToString("O"),
                    ["readOnly"]=true
                });
        }
        else if(entityType is "audit.change_log" or "audit")
        {
            if(SchemaSql.TableExists(c,null,"audit","change_log"))
            {
                using var cmd=Store.Command(c,null,$"""
                    SELECT id,entity_type,occurred_on FROM {SchemaSql.Q(c,"audit","change_log")}
                    WHERE occurred_on >= @f AND occurred_on < @t
                    ORDER BY occurred_on ASC
                    """,
                    ("@f",fromDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
                    ("@t",toDate.AddDays(1).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));
                using var r=cmd.ExecuteReader();
                while(r.Read())
                    items.Add(new JsonObject
                    {
                        ["id"]=r.GetValue(0).ToString(),
                        ["entityType"]=r.GetValue(1).ToString(),
                        ["occurredOn"]=Convert.ToDateTime(r.GetValue(2),CultureInfo.InvariantCulture).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
                        ["readOnly"]=true
                    });
            }
        }
        else throw new ApiError("نوع كيان الاستعادة غير مدعوم.",400);
        return new()
        {
            ["items"]=items,
            ["readOnly"]=true,
            ["entityType"]=entityType,
            ["allowedActions"]=new JsonArray("view_restore"),
            ["messageAr"]="عرض أرشيفي للقراءة فقط ضمن الاعتماد الساري."
        };
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/admin/archive/policies",(HttpContext ctx)=>store.Read(c=>ListPolicies(c,ctx.User)))
            .WithTags("Administration").WithSummary("List archive retention policies and restore views");
        api.MapPost("/admin/archive/restore-request",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>RequestRestore(c,tx,ctx.User,input)))
            .WithTags("Administration").WithSummary("Request temporary read-only archive restore view");
        api.MapPost("/admin/archive/restore-view",(JsonObject input,HttpContext ctx)=>store.Read(c=>RestoreView(c,ctx.User,input)))
            .WithTags("Administration").WithSummary("Read-only archived rows under an active restore approval");
    }
}
