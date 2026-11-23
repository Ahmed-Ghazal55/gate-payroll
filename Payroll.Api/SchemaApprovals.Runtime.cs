using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>WMRG: JSON↔SQL adapters for W1 approval tables. Unlimited ceilings use a finite SQL amount
/// because auth.approval_limit.limit_amount is NOT NULL (W1); API still exposes unlimited=true.</summary>
public static partial class SchemaApprovals
{
    public const decimal UnlimitedCeiling=999_999_999m;
    const string RequestKind="approval_request";

    public static bool IsApprovalKind(string kind)=>kind is "approval_limit" or "sla_policy" or "approval_decision" or "approval_bypass_grant" or "approval_request";

    /// <summary>Resolve auth.user for an existing identity. Never invent users or Admin bridges for strangers.</summary>
    public static string EnsureAuthUserId(DbConnection c,DbTransaction tx,string username,string? department=null,int? rankLevel=null)
    {
        string display=username.Trim();
        string name=UserAccess.Normalize(display);
        J.Need(display.Length is >=1 and <=80,"اسم المستخدم مطلوب لسجل الاعتماد.",400);
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"auth","user")} WHERE username=@u OR username=@n",("@u",display),("@n",name));
        if(existing is not null and not DBNull)return existing switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(existing,CultureInfo.InvariantCulture)!};

        // Bridge only a known document user, keeping their real role/status — never auto-create Admins or strangers.
        var doc=Store.List(c,"user",tx).FirstOrDefault(u=>UserAccess.Normalize(u.S("username"))==name)
            ??throw new ApiError("المستخدم غير موجود؛ لا يُنشأ حساب تلقائيًا.",400);
        string roleCode=UserAccess.Role(doc);
        object? role=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"auth","role")} WHERE code=@c",("@c",roleCode));
        J.Need(role is not null and not DBNull,"دور المستخدم غير مهيأ في كتالوج الصلاحيات.",500);
        string roleId=role switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(role,CultureInfo.InvariantCulture)!};
        string id=Guid.TryParse(doc.S("id"),out var docGuid)?SchemaSql.GuidText(docGuid):SchemaSql.GuidText(SchemaSql.NewId());
        int status=UserAccess.Status(doc) switch{"suspended"=>2,"disabled"=>3,_=>1};
        int? rank=rankLevel??(int.TryParse(doc.S("rankLevel"),NumberStyles.Integer,CultureInfo.InvariantCulture,out var rl)?rl:null);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"auth","user")}
            (id,username,role_id,security_version,status,department,rank_level,created_at)
            VALUES(@id,@u,@role,1,@st,@dept,@rank,{SchemaSql.NowLiteral(c)})
            """,
            ("@id",id),("@u",doc.S("username")),("@role",roleId),("@st",status),
            ("@dept",(object?)(department??doc.S("department"))??DBNull.Value),
            ("@rank",(object?)rank??DBNull.Value));
        return id;
    }

    public static bool HasLimits(DbConnection c,DbTransaction? tx)
        =>SchemaSql.TableExists(c,tx,"auth","approval_limit")
          &&Convert.ToInt64(SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"auth","approval_limit")}")!)>0;

    public static bool HasSla(DbConnection c,DbTransaction? tx)
        =>SchemaSql.TableExists(c,tx,"org","sla_policy")
          &&Convert.ToInt64(SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"org","sla_policy")}")!)>0;

    public static List<JsonObject> ListLimits(DbConnection c,DbTransaction? tx)
    {
        if(!SchemaSql.TableExists(c,tx,"auth","approval_limit"))return [];
        var rows=new List<JsonObject>();
        using var cmd=Store.Command(c,tx,$"SELECT id,rank,department,limit_amount FROM {SchemaSql.Q(c,"auth","approval_limit")}");
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
        {
            decimal amount=Convert.ToDecimal(reader["limit_amount"],CultureInfo.InvariantCulture);
            bool unlimited=amount>=UnlimitedCeiling;
            rows.Add(new()
            {
                ["id"]=reader["id"].ToString(),
                ["rank"]=reader["rank"].ToString(),
                ["department"]=reader["department"].ToString(),
                ["limitSar"]=unlimited?null:JsonValue.Create(amount),
                ["unlimited"]=unlimited
            });
        }
        return rows;
    }

    public static JsonObject? FindLimit(DbConnection c,DbTransaction? tx,string rank,string department)
        =>ListLimits(c,tx).FirstOrDefault(l=>l.S("rank")==rank&&l.S("department")==department);

    public static void UpsertLimit(DbConnection c,DbTransaction tx,JsonObject row)
    {
        if(!SchemaSql.TableExists(c,tx,"auth","approval_limit"))return;
        bool unlimited=row.B("unlimited")||row["limitSar"] is null;
        decimal amount=unlimited?UnlimitedCeiling:row.N("limitSar");
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"auth","approval_limit")} WHERE rank=@r AND department=@d",
            ("@r",row.S("rank")),("@d",row.S("department")));
        string sqlId=existing?.ToString()??(Guid.TryParse(row.S("id"),out var gid)?SchemaSql.GuidText(gid):SchemaSql.GuidText(SchemaSql.NewId()));
        if(existing==null)
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"auth","approval_limit")}
                (id,rank,department,limit_amount,created_at,updated_by,updated_at)
                VALUES(@id,@r,@d,@lim,{SchemaSql.NowLiteral(c)},@by,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",sqlId),("@r",row.S("rank")),("@d",row.S("department")),("@lim",amount),
                ("@by",(object?)row.S("updatedBy")??DBNull.Value));
        else
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"auth","approval_limit")}
                SET limit_amount=@lim, updated_by=@by, updated_at={SchemaSql.NowLiteral(c)}
                WHERE rank=@r AND department=@d
                """,
                ("@r",row.S("rank")),("@d",row.S("department")),("@lim",amount),
                ("@by",(object?)row.S("updatedBy")??DBNull.Value));
        row["id"]=sqlId;row["unlimited"]=unlimited;if(unlimited)row["limitSar"]=null;
    }

    public static List<JsonObject> ListSla(DbConnection c,DbTransaction? tx)
    {
        if(!SchemaSql.TableExists(c,tx,"org","sla_policy"))return [];
        var rows=new List<JsonObject>();
        using var cmd=Store.Command(c,tx,$"SELECT id,request_type,working_days FROM {SchemaSql.Q(c,"org","sla_policy")}");
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
            rows.Add(new()
            {
                ["id"]=reader["id"].ToString(),
                ["requestType"]=reader["request_type"].ToString(),
                ["workingDays"]=Convert.ToInt32(reader["working_days"],CultureInfo.InvariantCulture),
                ["autoApprove"]=false
            });
        return rows;
    }

    public static void UpsertSla(DbConnection c,DbTransaction tx,JsonObject row)
    {
        if(!SchemaSql.TableExists(c,tx,"org","sla_policy"))return;
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"org","sla_policy")} WHERE request_type=@t",("@t",row.S("requestType")));
        string sqlId=existing?.ToString()??(Guid.TryParse(row.S("id"),out var gid)?SchemaSql.GuidText(gid):SchemaSql.GuidText(SchemaSql.NewId()));
        if(existing==null)
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"org","sla_policy")}
                (id,request_type,working_days,created_at,updated_by,updated_at)
                VALUES(@id,@t,@d,{SchemaSql.NowLiteral(c)},@by,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",sqlId),("@t",row.S("requestType")),("@d",(int)row.N("workingDays",3)),
                ("@by",(object?)row.S("updatedBy")??DBNull.Value));
        else
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"org","sla_policy")}
                SET working_days=@d, updated_by=@by, updated_at={SchemaSql.NowLiteral(c)}
                WHERE request_type=@t
                """,
                ("@t",row.S("requestType")),("@d",(int)row.N("workingDays",3)),
                ("@by",(object?)row.S("updatedBy")??DBNull.Value));
        row["id"]=sqlId;row["autoApprove"]=false;
    }

    public static List<JsonObject> ListRequests(DbConnection c,DbTransaction? tx)
    {
        if(!SchemaSql.TableExists(c,tx,"approvals","request"))return [];
        var rows=new List<JsonObject>();
        using var cmd=Store.Command(c,tx,$"""
            SELECT r.id,r.kind,r.title_ar,r.body_ar,r.from_department,r.target_department,r.required_rank_level,
                   r.reference_id,r.status,r.payload_json,r.requested_by,r.created_at,u.username AS requested_by_name
            FROM {SchemaSql.Q(c,"approvals","request")} r
            LEFT JOIN {SchemaSql.Q(c,"auth","user")} u ON u.id=r.requested_by
            ORDER BY r.created_at DESC
            """);
        using var reader=cmd.ExecuteReader();
        while(reader.Read())rows.Add(ProjectRequest(reader));
        return rows;
    }

    public static JsonObject? GetRequest(DbConnection c,DbTransaction? tx,string id)
    {
        if(!SchemaSql.TableExists(c,tx,"approvals","request")||!Guid.TryParse(id,out _))return null;
        using var cmd=Store.Command(c,tx,$"""
            SELECT r.id,r.kind,r.title_ar,r.body_ar,r.from_department,r.target_department,r.required_rank_level,
                   r.reference_id,r.status,r.payload_json,r.requested_by,r.created_at,u.username AS requested_by_name
            FROM {SchemaSql.Q(c,"approvals","request")} r
            LEFT JOIN {SchemaSql.Q(c,"auth","user")} u ON u.id=r.requested_by
            WHERE r.id=@id
            """,("@id",Guid.TryParse(id,out var g)?SchemaSql.GuidText(g):id));
        using var reader=cmd.ExecuteReader();
        return reader.Read()?ProjectRequest(reader):null;
    }

    static JsonObject ProjectRequest(DbDataReader reader)
    {
        var payload=reader["payload_json"] is string pj&&pj!=""?JsonNode.Parse(pj)!.AsObject():new JsonObject();
        string status=reader["status"].ToString()!;
        if(payload.S("displayStatus")!="")status=payload.S("displayStatus");
        string reference=payload.S("referenceId");
        if(reference==""&&reader["reference_id"] is not DBNull)reference=reader["reference_id"].ToString()!;
        var row=new JsonObject
        {
            ["id"]=reader["id"].ToString(),
            ["kind"]=reader["kind"].ToString(),
            ["titleAr"]=reader["title_ar"].ToString(),
            ["bodyAr"]=reader["body_ar"] is DBNull?"":reader["body_ar"].ToString(),
            ["fromDepartment"]=reader["from_department"] is DBNull?"":reader["from_department"].ToString(),
            ["targetDepartment"]=reader["target_department"].ToString(),
            ["requiredRankLevel"]=Convert.ToInt32(reader["required_rank_level"],CultureInfo.InvariantCulture),
            ["referenceId"]=reference,
            ["status"]=status,
            ["payload"]=payload.DeepClone(),
            ["requestedBy"]=reader["requested_by_name"] is DBNull?payload.S("requestedByName"):reader["requested_by_name"].ToString(),
            ["createdAt"]=reader["created_at"].ToString()
        };
        foreach(string key in new[]{"sponsorId","companyId","platform","amountSar","total","fileContentHash","dualPermission","decidedBy","decidedAt","decisionNote","selfApprovedException","administrativeSelfOverride","escalatedAt","escalationNoteAr"})
            if(payload[key]!=null&&row[key]==null)row[key]=payload[key]!.DeepClone();
        if(row["amountSar"]==null&&payload.N("amountSar",payload.N("amount",payload.N("total")))!=0)
            row["amountSar"]=payload.N("amountSar",payload.N("amount",payload.N("total")));
        return row;
    }

    public static void SaveRequest(DbConnection c,DbTransaction tx,JsonObject row)
    {
        if(!SchemaSql.TableExists(c,tx,"approvals","request"))return;
        string actorName=row.S("requestedBy",row.S("createdBy","system"));
        string userId=EnsureAuthUserId(c,tx,actorName);
        string id=Guid.TryParse(row.S("id"),out var gid)?SchemaSql.GuidText(gid):SchemaSql.GuidText(SchemaSql.NewId());
        row["id"]=id;
        var payload=row["payload"] as JsonObject??new JsonObject();
        foreach(string key in new[]{"sponsorId","companyId","platform","amountSar","total","fileContentHash","dualPermission","decidedBy","decidedAt","decisionNote","selfApprovedException","administrativeSelfOverride","escalatedAt","escalationNoteAr","referenceId"})
            if(row[key]!=null)payload[key]=row[key]!.DeepClone();
        payload["referenceId"]=row.S("referenceId");
        payload["requestedByName"]=actorName;
        string display=row.S("status");
        string sqlStatus=display switch
        {
            "suspended"=>"pending",
            "auto_approved"=>"approved",
            "approved" or "rejected" or "cancelled" or "escalated" or "pending"=>display,
            _=>"pending"
        };
        if(display is "suspended" or "auto_approved")payload["displayStatus"]=display;
        else payload.Remove("displayStatus");
        object? refId=Guid.TryParse(row.S("referenceId"),out var rid)?SchemaSql.GuidText(rid):(object)DBNull.Value;
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"approvals","request")} WHERE id=@id",("@id",id));
        string createdAt=row.S("createdAt");
        if(createdAt==""||!DateTimeOffset.TryParse(createdAt,out _))createdAt=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        row["createdAt"]=createdAt;
        string title=row.S("titleAr");if(title=="")title=row.S("kind","طلب اعتماد");
        if(existing==null)
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"approvals","request")}
                (id,kind,title_ar,body_ar,from_department,target_department,required_rank_level,reference_id,status,payload_json,requested_by,created_at,updated_at)
                VALUES(@id,@kind,@title,@body,@from,@to,@rank,@ref,@status,@payload,@by,@created,@created)
                """,
                ("@id",id),("@kind",row.S("kind")),("@title",title),
                ("@body",(object?)row.S("bodyAr")??DBNull.Value),
                ("@from",(object?)row.S("fromDepartment")??DBNull.Value),
                ("@to",row.S("targetDepartment","FIN")),
                ("@rank",(int)row.N("requiredRankLevel",1)),("@ref",refId),
                ("@status",sqlStatus),("@payload",payload.ToJsonString()),("@by",userId),("@created",createdAt));
        else
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"approvals","request")}
                SET kind=@kind, title_ar=@title, body_ar=@body, from_department=@from, target_department=@to,
                    required_rank_level=@rank, reference_id=@ref, status=@status, payload_json=@payload,
                    updated_at={SchemaSql.NowLiteral(c)}
                WHERE id=@id
                """,
                ("@id",id),("@kind",row.S("kind")),("@title",title),
                ("@body",(object?)row.S("bodyAr")??DBNull.Value),
                ("@from",(object?)row.S("fromDepartment")??DBNull.Value),
                ("@to",row.S("targetDepartment","FIN")),
                ("@rank",(int)row.N("requiredRankLevel",1)),("@ref",refId),
                ("@status",sqlStatus),("@payload",payload.ToJsonString()));
    }

    public static List<JsonObject> ListDecisions(DbConnection c,DbTransaction? tx)
    {
        if(!SchemaSql.TableExists(c,tx,"approvals","decision"))return [];
        var rows=new List<JsonObject>();
        using var cmd=Store.Command(c,tx,$"""
            SELECT d.id,d.request_id,d.decided_by,d.decided_at,d.outcome,d.membership_snapshot_json,d.bypass_grant_id,u.username AS decided_by_name
            FROM {SchemaSql.Q(c,"approvals","decision")} d
            LEFT JOIN {SchemaSql.Q(c,"auth","user")} u ON u.id=d.decided_by
            """);
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
        {
            var snap=reader["membership_snapshot_json"] is string s&&s!=""?JsonNode.Parse(s):new JsonObject();
            rows.Add(new()
            {
                ["id"]=reader["id"].ToString(),
                ["requestId"]=reader["request_id"].ToString(),
                ["decidedBy"]=reader["decided_by_name"] is DBNull?"":reader["decided_by_name"].ToString(),
                ["decidedAt"]=reader["decided_at"].ToString(),
                ["outcome"]=reader["outcome"].ToString(),
                ["membershipSnapshot"]=snap,
                ["bypassGrantId"]=reader["bypass_grant_id"] is DBNull?"":reader["bypass_grant_id"].ToString()
            });
        }
        return rows;
    }

    public static void SaveDecision(DbConnection c,DbTransaction tx,JsonObject row)
    {
        if(!SchemaSql.TableExists(c,tx,"approvals","decision"))return;
        string requestId=row.S("requestId");
        J.Need(Guid.TryParse(requestId,out var reqGuid),"معرف طلب الاعتماد غير صالح.",400);
        // Ensure parent request exists (document-era ids may need a stub).
        if(GetRequest(c,tx,SchemaSql.GuidText(reqGuid))==null)
            SaveRequest(c,tx,new()
            {
                ["id"]=SchemaSql.GuidText(reqGuid),["kind"]="decision_anchor",["titleAr"]="قرار",
                ["targetDepartment"]="FIN",["requiredRankLevel"]=1,["referenceId"]="",["status"]="pending",
                ["requestedBy"]=row.S("decidedBy","system"),["payload"]=new JsonObject()
            });
        string userId=EnsureAuthUserId(c,tx,row.S("decidedBy","system"));
        string sqlId=Guid.TryParse(row.S("id"),out var did)?SchemaSql.GuidText(did):SchemaSql.GuidText(SchemaSql.NewId());
        string outcome=row.S("outcome") switch{"approved"=>"approved","rejected"=>"rejected","suspended" or "held"=>"held","escalated"=>"escalated",_=>row.S("outcome","approved")};
        J.Need(outcome is "approved" or "rejected" or "held" or "escalated","نتيجة القرار غير صالحة.",400);
        string snapshot=(row["membershipSnapshot"] as JsonObject)?.ToJsonString()??"{}";
        object bypass=DBNull.Value;
        if(Guid.TryParse(row.S("bypassGrantId"),out var bg))bypass=SchemaSql.GuidText(bg);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"approvals","decision")}
            (id,request_id,decided_by,decided_at,outcome,membership_snapshot_json,bypass_grant_id,created_at)
            VALUES(@id,@req,@by,@at,@out,@snap,@grant,{SchemaSql.NowLiteral(c)})
            """,
            ("@id",sqlId),("@req",SchemaSql.GuidText(reqGuid)),("@by",userId),
            ("@at",row.S("decidedAt",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture))),
            ("@out",outcome),("@snap",snapshot),("@grant",bypass));
        row["id"]=sqlId;
        var request=GetRequest(c,tx,SchemaSql.GuidText(reqGuid));
        if(request!=null&&string.Equals(request.S("kind"),ArchiveApi.ApprovalKind,StringComparison.OrdinalIgnoreCase))
            ArchiveApi.ApplyApproval(c,tx,SchemaSql.GuidText(reqGuid),outcome,row.S("decidedBy","system"));
    }

    public static List<JsonObject> ListBypassGrants(DbConnection c,DbTransaction? tx)
    {
        if(!SchemaSql.TableExists(c,tx,"auth","bypass_grant"))return [];
        var rows=new List<JsonObject>();
        using var cmd=Store.Command(c,tx,$"""
            SELECT g.id,g.user_id,g.scope,g.valid_from,g.valid_to,g.reason,g.granted_by,g.revoked_at,g.created_at,
                   u.username AS username, gb.username AS granted_by_name
            FROM {SchemaSql.Q(c,"auth","bypass_grant")} g
            LEFT JOIN {SchemaSql.Q(c,"auth","user")} u ON u.id=g.user_id
            LEFT JOIN {SchemaSql.Q(c,"auth","user")} gb ON gb.id=g.granted_by
            ORDER BY g.created_at DESC
            """);
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
        {
            string from=DateOnlyText(reader["valid_from"]),to=DateOnlyText(reader["valid_to"]);
            string scope=reader["scope"].ToString()!;
            // API still uses pay_approvals; W1 seed uses pay.approvals.
            if(string.Equals(scope,"pay.approvals",StringComparison.OrdinalIgnoreCase))scope=AccessPolicy.ScopePayApprovals;
            rows.Add(new()
            {
                ["id"]=reader["id"].ToString(),
                ["userId"]=reader["user_id"].ToString(),
                ["username"]=reader["username"] is DBNull?"":reader["username"].ToString(),
                ["scope"]=scope,
                ["from"]=from,["to"]=to,
                ["reason"]=reader["reason"].ToString(),
                ["grantedBy"]=reader["granted_by_name"] is DBNull?"":reader["granted_by_name"].ToString(),
                ["grantedAt"]=reader["created_at"].ToString(),
                ["revokedAt"]=reader["revoked_at"] is DBNull?"":reader["revoked_at"].ToString(),
                ["revokedBy"]="",
                ["permission"]=AccessPolicy.BypassPermission
            });
        }
        return rows;
    }

    public static JsonObject? GetBypassGrant(DbConnection c,DbTransaction? tx,string id)
        =>ListBypassGrants(c,tx).FirstOrDefault(g=>string.Equals(g.S("id"),id,StringComparison.OrdinalIgnoreCase));

    public static void SaveBypassGrant(DbConnection c,DbTransaction tx,JsonObject grant)
    {
        if(!SchemaSql.TableExists(c,tx,"auth","bypass_grant"))return;
        string userId=EnsureAuthUserId(c,tx,grant.S("username"));
        string granter=EnsureAuthUserId(c,tx,grant.S("grantedBy","system"));
        string id=Guid.TryParse(grant.S("id"),out var gid)?SchemaSql.GuidText(gid):SchemaSql.GuidText(SchemaSql.NewId());
        grant["id"]=id;grant["userId"]=userId;
        string from=grant.S("from")+"T00:00:00",to=grant.S("to")+"T23:59:59";
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"auth","bypass_grant")} WHERE id=@id",("@id",id));
        object revoked=string.IsNullOrWhiteSpace(grant.S("revokedAt"))?(object)DBNull.Value:grant.S("revokedAt");
        string scope=grant.S("scope");
        if(string.Equals(scope,AccessPolicy.ScopePayApprovals,StringComparison.OrdinalIgnoreCase))scope="pay.approvals";
        if(existing==null)
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"auth","bypass_grant")}
                (id,user_id,scope,valid_from,valid_to,reason,granted_by,revoked_at,created_at)
                VALUES(@id,@u,@scope,@from,@to,@reason,@by,@rev,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",id),("@u",userId),("@scope",scope),("@from",from),("@to",to),
                ("@reason",grant.S("reason")),("@by",granter),("@rev",revoked));
        else
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"auth","bypass_grant")}
                SET scope=@scope, valid_from=@from, valid_to=@to, reason=@reason, revoked_at=@rev,
                    updated_at={SchemaSql.NowLiteral(c)}
                WHERE id=@id
                """,
                ("@id",id),("@scope",scope),("@from",from),("@to",to),
                ("@reason",grant.S("reason")),("@rev",revoked));
    }

    static string DateOnlyText(object value)
    {
        if(value is DateTime dt)return DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        if(value is DateTimeOffset dto)return DateOnly.FromDateTime(dto.UtcDateTime).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        string s=Convert.ToString(value,CultureInfo.InvariantCulture)??"";
        return s.Length>=10?s[..10]:s;
    }
}
