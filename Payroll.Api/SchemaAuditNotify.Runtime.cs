using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>WMRG: JSON↔SQL adapters for W1 audit/notify/task tables used by temporary W2 document kinds.</summary>
public static partial class SchemaAuditNotify
{
    public static bool IsAuditNotifyKind(string kind)=>kind is "audit" or "audit_read_summary_daily" or "notify_settings" or "task_item";

    static string MapSource(string raw)=>raw switch
    {
        "واجهة" or "ui"=>"ui",
        "نظام" or "system"=>"system",
        "job"=>"job",
        "import"=>"import",
        _=>"api"
    };

    static string ActionCode(string actionOrAr)
    {
        if(string.IsNullOrWhiteSpace(actionOrAr))return "update";
        foreach(var (code,name) in ActionLabels)
            if(string.Equals(actionOrAr,code,StringComparison.OrdinalIgnoreCase)||string.Equals(actionOrAr,name,StringComparison.Ordinal))
                return code;
        // Free-form legacy actions (admin.login, approval.self_decision, …) — keep as code when they fit.
        string codeCandidate=actionOrAr.Trim();
        if(codeCandidate.Length<=40&&codeCandidate.All(ch=>char.IsLetterOrDigit(ch)||ch is '.' or '_' or '-'))return codeCandidate;
        return "update";
    }

    static void EnsureLabel(DbConnection c,DbTransaction tx,string code,string? nameAr=null)
    {
        object? exists=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"audit","action_label")} WHERE action_code=@c",("@c",code));
        if(Convert.ToInt64(exists!)>0)return;
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"audit","action_label")}(id,action_code,name_ar,created_at)
            VALUES(@id,@c,@n,{SchemaSql.NowLiteral(c)})
            """,
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@c",code),
            ("@n",nameAr??AuditApi.LabelAr(code)));
    }

    public static List<JsonObject> ListChanges(DbConnection c,DbTransaction? tx)
    {
        if(!SchemaSql.TableExists(c,tx,"audit","change_log"))return [];
        var rows=new List<JsonObject>();
        using var cmd=Store.Command(c,tx,$"""
            SELECT id,occurred_on,occurred_at,user_id,username,display_name_ar,action_code,entity_type,entity_ref,diff_short,source
            FROM {SchemaSql.Q(c,"audit","change_log")}
            ORDER BY occurred_at DESC
            """);
        using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            string action=r["action_code"].ToString()??"update";
            string at=Convert.ToDateTime(r["occurred_at"],CultureInfo.InvariantCulture).ToString("O");
            string entity=r["entity_ref"].ToString()??"";
            if(entity=="-")entity="";
            string actor=r["username"]?.ToString()??"";
            string diff=r["diff_short"].ToString()??"";
            JsonObject? bag=null;
            if(diff.StartsWith('{'))
            {
                try{bag=JsonNode.Parse(diff) as JsonObject;}
                catch{/* truncated or legacy plain text */}
            }
            // Prefer the original document shape stored in diff_short (WMRG: no behaviour change beyond storage).
            JsonObject row;
            if(bag!=null)
            {
                row=(JsonObject)bag.DeepClone();
                row["id"]=r["id"].ToString();
                if(row.S("action")=="")row["action"]=action;
                if(row.S("actor")=="")row["actor"]=actor;
                if(row.S("at")=="")row["at"]=at;
                if(row.S("entityId")==""&&entity!="")row["entityId"]=entity;
            }
            else
            {
                row=new()
                {
                    ["id"]=r["id"].ToString(),
                    ["action"]=action,
                    ["entityId"]=entity,
                    ["actor"]=actor,
                    ["at"]=at
                };
                if(diff!=""&&diff!=action)row["change"]=new JsonObject{["diff"]=diff};
            }
            if(row.B("selfApprovedException")==false&&action.Contains("self_",StringComparison.OrdinalIgnoreCase))
                row["selfApprovedException"]=true;
            rows.Add(row);
        }
        return rows;
    }

    public static JsonObject? GetChange(DbConnection c,DbTransaction? tx,string id)
        =>ListChanges(c,tx).FirstOrDefault(a=>a.S("id")==id);

    public static void SaveChange(DbConnection c,DbTransaction tx,JsonObject row)
    {
        if(!SchemaSql.TableExists(c,tx,"audit","change_log"))return;
        string action=ActionCode(row.S("action",row.S("actionAr")));
        EnsureLabel(c,tx,action,row.S("actionAr")==""?null:row.S("actionAr"));
        string atRaw=row.S("occurredAt",row.S("at"));
        if(!DateTimeOffset.TryParse(atRaw,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var at))
            at=DateTimeOffset.UtcNow;
        string entityType=row.S("entityTypeAr",row.S("entityType"));
        if(entityType.Length is 0 or >40)entityType=entityType.Length==0?"record":entityType[..40];
        string entityRef=row.S("entityRef",row.S("entityId",row.S("entity",row.S("recordId"))));
        if(entityRef.Length>200)entityRef=entityRef[..200];
        // Persist original document fields under nvarchar(400). Never truncate mid-JSON — drop keys until it fits.
        var stored=new JsonObject();
        foreach(string key in new[]{
            "action","actor","at","occurredAt","entityId","entity","entityRef","recordId","badge","reason",
            "selfApprovedException","grantId","operation","maker","approvalBypassUntil","approvalExceptionUntil",
            "rowCount","editedRowCount","total","source","actionAr","entityTypeAr","entityType","change"})
            if(row[key]!=null)stored[key]=row[key]!.DeepClone();
        foreach(var kv in row)
            if(stored[kv.Key]==null&&kv.Key!="id"&&kv.Value!=null)stored[kv.Key]=kv.Value.DeepClone();
        string diff=stored.ToJsonString();
        while(diff.Length>400&&stored.Count>0)
        {
            // Drop lowest-priority trailing keys first (change, then long free-form fields).
            string drop=stored.ContainsKey("change")?"change"
                :stored.Select(kv=>kv.Key).LastOrDefault(k=>k is not ("action" or "actor" or "at" or "badge" or "reason" or "selfApprovedException" or "rowCount" or "editedRowCount" or "total"))
                ??stored.Select(kv=>kv.Key).Last();
            stored.Remove(drop);
            diff=stored.ToJsonString();
        }
        if(diff.Length==0||diff.Length>400)diff=action;
        string source=MapSource(row.S("source"));
        string actor=row.S("actor");
        object userId=DBNull.Value;
        if(actor!="")
        {
            try{userId=SchemaApprovals.EnsureAuthUserId(c,tx,actor);}
            catch{/* username bridge optional for legacy audit writers */}
        }
        string sqlId=Guid.TryParse(row.S("id"),out var gid)?SchemaSql.GuidText(gid):SchemaSql.GuidText(SchemaSql.NewId());
        row["id"]=sqlId;
        string onText=at.UtcDateTime.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        string atText=at.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff",CultureInfo.InvariantCulture);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"audit","change_log")}
            (id,occurred_on,occurred_at,user_id,username,display_name_ar,action_code,entity_type,entity_ref,diff_short,source,created_at)
            VALUES(@id,@on,@at,@uid,@user,@disp,@act,@etype,@eref,@diff,@src,{SchemaSql.NowLiteral(c)})
            """,
            ("@id",sqlId),
            ("@on",onText),
            ("@at",atText),
            ("@uid",userId),
            ("@user",(object?)(actor==""?null:actor)??DBNull.Value),
            ("@disp",DBNull.Value),
            ("@act",action),
            ("@etype",entityType),
            ("@eref",entityRef==""?"-":entityRef),
            ("@diff",diff),
            ("@src",source));
    }

    public static List<JsonObject> ListReadSummaries(DbConnection c,DbTransaction? tx)
    {
        if(!SchemaSql.TableExists(c,tx,"audit","read_summary_daily"))return [];
        var rows=new List<JsonObject>();
        using var cmd=Store.Command(c,tx,$"""
            SELECT s.id,s.summary_date,s.user_id,s.area,s.read_count,s.export_count,s.sensitive_count,u.username
            FROM {SchemaSql.Q(c,"audit","read_summary_daily")} s
            LEFT JOIN {SchemaSql.Q(c,"auth","user")} u ON u.id=s.user_id
            """);
        using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            string userKey=r["username"]?.ToString()??r["user_id"].ToString()??"";
            string day=Convert.ToDateTime(r["summary_date"],CultureInfo.InvariantCulture).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            string area=r["area"].ToString()??"";
            rows.Add(new()
            {
                ["id"]=$"read-summary:{day}:{userKey}:{area}",
                ["sqlId"]=r["id"].ToString(),
                ["summaryDate"]=day,
                ["userId"]=userKey,
                ["area"]=area,
                ["readCount"]=Convert.ToInt32(r["read_count"]),
                ["exportCount"]=Convert.ToInt32(r["export_count"]),
                ["sensitiveViewCount"]=Convert.ToInt32(r["sensitive_count"])
            });
        }
        return rows;
    }

    public static JsonObject? GetReadSummary(DbConnection c,DbTransaction? tx,string id)
        =>ListReadSummaries(c,tx).FirstOrDefault(r=>r.S("id")==id||r.S("sqlId")==id);

    public static void SaveReadSummary(DbConnection c,DbTransaction tx,JsonObject row)
    {
        if(!SchemaSql.TableExists(c,tx,"audit","read_summary_daily"))return;
        string day=row.S("summaryDate");
        string userKey=row.S("userId");
        string area=row.S("area");
        J.Need(day!=""&&userKey!=""&&area!="","ملخص القراءة غير مكتمل.",400);
        string userId=SchemaApprovals.EnsureAuthUserId(c,tx,userKey);
        object? existing=SchemaSql.Scalar(c,tx,$"""
            SELECT id FROM {SchemaSql.Q(c,"audit","read_summary_daily")}
            WHERE summary_date=@d AND user_id=@u AND area=@a
            """,("@d",day),("@u",userId),("@a",area));
        string sqlId=existing?.ToString()??(Guid.TryParse(row.S("sqlId"),out var gid)?SchemaSql.GuidText(gid):SchemaSql.GuidText(SchemaSql.NewId()));
        if(existing==null)
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"audit","read_summary_daily")}
                (id,summary_date,user_id,area,read_count,export_count,sensitive_count,created_at)
                VALUES(@id,@d,@u,@a,@r,@e,@s,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",sqlId),("@d",day),("@u",userId),("@a",area),
                ("@r",(int)row.N("readCount")),("@e",(int)row.N("exportCount")),("@s",(int)row.N("sensitiveViewCount")));
        else
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"audit","read_summary_daily")}
                SET read_count=@r, export_count=@e, sensitive_count=@s
                WHERE id=@id
                """,
                ("@id",sqlId),("@r",(int)row.N("readCount")),("@e",(int)row.N("exportCount")),("@s",(int)row.N("sensitiveViewCount")));
        row["sqlId"]=sqlId;
        row["id"]=$"read-summary:{day}:{userKey}:{area}";
    }

    const string SettingsEvent="settings.reply_working_days",SettingsAudience="global",ChannelEvent="settings.channel",MatrixEvent="settings.matrix";

    public static JsonObject GetNotifySettings(DbConnection c,DbTransaction? tx)
    {
        var row=new JsonObject
        {
            ["id"]="notify-settings:global",
            ["replyWorkingDays"]=NotifyTasksApi.DefaultReplyWorkingDays,
            ["matrix"]=new JsonArray(),
            ["channels"]=new JsonArray()
        };
        if(!SchemaSql.TableExists(c,tx,"notify","subscription"))return row;
        using var cmd=Store.Command(c,tx,$"""
            SELECT event_type,audience_key,channel,enabled FROM {SchemaSql.Q(c,"notify","subscription")}
            WHERE event_type IN (@sla,@ch,@mx)
            """,("@sla",SettingsEvent),("@ch",ChannelEvent),("@mx",MatrixEvent));
        using var r=cmd.ExecuteReader();
        var channels=new JsonArray();
        var matrix=new JsonArray();
        while(r.Read())
        {
            string ev=r["event_type"].ToString()??"";
            string audience=r["audience_key"].ToString()??"";
            string channel=r["channel"].ToString()??"";
            bool enabled=Convert.ToInt32(r["enabled"])!=0;
            if(ev==SettingsEvent&&int.TryParse(audience,out var days)&&days is >=1 and <=30)
                row["replyWorkingDays"]=days;
            else if(ev==ChannelEvent&&enabled)channels.Add(audience!=""?audience:channel);
            else if(ev==MatrixEvent&&enabled)matrix.Add(audience);
        }
        if(channels.Count==0){channels.Add("in_app");channels.Add("push");}
        row["channels"]=channels;row["matrix"]=matrix;
        return row;
    }

    public static void SaveNotifySettings(DbConnection c,DbTransaction tx,JsonObject row)
    {
        if(!SchemaSql.TableExists(c,tx,"notify","subscription"))return;
        int days=(int)(row.N("replyWorkingDays") is 0?NotifyTasksApi.DefaultReplyWorkingDays:row.N("replyWorkingDays"));
        SchemaSql.Exec(c,tx,$"DELETE FROM {SchemaSql.Q(c,"notify","subscription")} WHERE event_type IN (@sla,@ch,@mx)",
            ("@sla",SettingsEvent),("@ch",ChannelEvent),("@mx",MatrixEvent));
        void Insert(string ev,string audience,string channel)
        {
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"notify","subscription")}
                (id,event_type,audience_key,channel,enabled,created_at)
                VALUES(@id,@ev,@aud,@ch,1,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@ev",ev),("@aud",audience),("@ch",channel));
        }
        Insert(SettingsEvent,days.ToString(CultureInfo.InvariantCulture),"in_app");
        foreach(var ch in (row["channels"] as JsonArray??[]).Select(n=>n?.ToString()??"").Where(s=>s!=""))
            Insert(ChannelEvent,ch,ch is "in_app" or "push" or "email"?ch:"in_app");
        foreach(var m in (row["matrix"] as JsonArray??[]).Select(n=>n?.ToString()??"").Where(s=>s!=""))
            Insert(MatrixEvent,m.Length<=80?m:m[..80],"in_app");
        row["id"]="notify-settings:global";
    }

    static string StatusToSql(string status)=>status switch{"completed"=>"done","in_progress"=>"in_progress","cancelled"=>"cancelled","escalated"=>"escalated",_=>"open"};
    static string StatusFromSql(string status)=>status switch{"done"=>"completed",_=>status};

    public static List<JsonObject> ListTasks(DbConnection c,DbTransaction? tx)
    {
        if(!SchemaSql.TableExists(c,tx,"task","item"))return [];
        var rows=new List<JsonObject>();
        using var cmd=Store.Command(c,tx,$"""
            SELECT t.id,t.due_on,t.assignee_user_id,t.assignee_rank,t.status,t.title_ar,t.entity_type,t.entity_id,t.created_by,t.created_at,t.updated_by,t.updated_at,u.username
            FROM {SchemaSql.Q(c,"task","item")} t
            LEFT JOIN {SchemaSql.Q(c,"auth","user")} u ON u.id=t.assignee_user_id
            ORDER BY t.created_at DESC
            """);
        using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            string id=r["id"].ToString()??"";
            string due=Convert.ToDateTime(r["due_on"],CultureInfo.InvariantCulture).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            string created=r["created_at"] is DBNull or null?"":Convert.ToDateTime(r["created_at"],CultureInfo.InvariantCulture).ToString("O");
            rows.Add(new()
            {
                ["id"]=id,
                ["titleAr"]=r["title_ar"].ToString()??"",
                ["bodyAr"]=FirstCommentBody(c,tx,id),
                ["assignee"]=r["username"]?.ToString()??"",
                ["rank"]=r["assignee_rank"]?.ToString()??"",
                ["status"]=StatusFromSql(r["status"].ToString()??"open"),
                ["entityType"]=r["entity_type"]?.ToString()??"",
                ["entityId"]=r["entity_id"] is DBNull or null?"":r["entity_id"].ToString()!,
                ["dueAt"]=due+"T00:00:00+03:00",
                ["createdBy"]=r["created_by"]?.ToString()??"",
                ["createdAt"]=created,
                ["updatedBy"]=r["updated_by"]?.ToString()??"",
                ["comments"]=ListComments(c,tx,id),
                ["attachments"]=new JsonArray()
            });
        }
        return rows;
    }

    static string FirstCommentBody(DbConnection c,DbTransaction? tx,string taskId)
    {
        if(!SchemaSql.TableExists(c,tx,"task","comment"))return "";
        object? body=SchemaSql.Scalar(c,tx,$"""
            SELECT body_ar FROM {SchemaSql.Q(c,"task","comment")} WHERE task_id=@t ORDER BY created_at
            """,("@t",taskId));
        return body is null or DBNull?"":Convert.ToString(body,CultureInfo.InvariantCulture)!;
    }

    static JsonArray ListComments(DbConnection c,DbTransaction? tx,string taskId)
    {
        var arr=new JsonArray();
        if(!SchemaSql.TableExists(c,tx,"task","comment"))return arr;
        using var cmd=Store.Command(c,tx,$"SELECT body_ar,created_by,created_at FROM {SchemaSql.Q(c,"task","comment")} WHERE task_id=@t ORDER BY created_at",("@t",taskId));
        using var r=cmd.ExecuteReader();
        while(r.Read())
            arr.Add(new JsonObject{["bodyAr"]=r["body_ar"].ToString(),["by"]=r["created_by"]?.ToString()??"",["at"]=r["created_at"]?.ToString()??""});
        return arr;
    }

    public static JsonObject? GetTask(DbConnection c,DbTransaction? tx,string id)
        =>ListTasks(c,tx).FirstOrDefault(t=>t.S("id")==id);

    public static void SaveTask(DbConnection c,DbTransaction tx,JsonObject row)
    {
        if(!SchemaSql.TableExists(c,tx,"task","item"))return;
        string rawId=row.S("id");
        if(rawId.StartsWith("task:",StringComparison.Ordinal)&&Guid.TryParse(rawId[5..],out var prefixed))
            rawId=SchemaSql.GuidText(prefixed);
        else if(Guid.TryParse(rawId,out var gid))rawId=SchemaSql.GuidText(gid);
        else rawId=SchemaSql.GuidText(SchemaSql.NewId());
        row["id"]=rawId;
        string status=StatusToSql(row.S("status","open"));
        DateTimeOffset due=DateTimeOffset.UtcNow.AddDays(3);
        if(DateTimeOffset.TryParse(row.S("dueAt"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var parsed))due=parsed;
        object assignee=DBNull.Value;
        if(row.S("assignee")!="")
            try{assignee=SchemaApprovals.EnsureAuthUserId(c,tx,row.S("assignee"));}catch{/* optional */}
        object entityId=DBNull.Value;
        if(Guid.TryParse(row.S("entityId"),out var eid))entityId=SchemaSql.GuidText(eid);
        object? exists=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"task","item")} WHERE id=@id",("@id",rawId));
        if(exists is null or DBNull)
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"task","item")}
                (id,due_on,assignee_user_id,assignee_rank,status,title_ar,entity_type,entity_id,created_by,created_at)
                VALUES(@id,@due,@asg,@rank,@st,@title,@etype,@eid,@by,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",rawId),("@due",due.UtcDateTime.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),("@asg",assignee),
                ("@rank",(object?)(row.S("rank")==""?null:row.S("rank"))??DBNull.Value),
                ("@st",status),("@title",row.S("titleAr","مهمة")),
                ("@etype",(object?)(row.S("entityType")==""?null:row.S("entityType"))??DBNull.Value),
                ("@eid",entityId),("@by",(object?)(row.S("createdBy")==""?null:row.S("createdBy"))??DBNull.Value));
        else
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"task","item")}
                SET due_on=@due, assignee_user_id=@asg, assignee_rank=@rank, status=@st, title_ar=@title,
                    entity_type=@etype, entity_id=@eid, updated_by=@by, updated_at={SchemaSql.NowLiteral(c)}
                WHERE id=@id
                """,
                ("@id",rawId),("@due",due.UtcDateTime.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),("@asg",assignee),
                ("@rank",(object?)(row.S("rank")==""?null:row.S("rank"))??DBNull.Value),
                ("@st",status),("@title",row.S("titleAr","مهمة")),
                ("@etype",(object?)(row.S("entityType")==""?null:row.S("entityType"))??DBNull.Value),
                ("@eid",entityId),("@by",(object?)(row.S("updatedBy")==""?row.S("createdBy"):row.S("updatedBy"))??DBNull.Value));
        string body=row.S("bodyAr");
        if(body!=""&&SchemaSql.TableExists(c,tx,"task","comment"))
        {
            object? hasBody=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"task","comment")} WHERE task_id=@t",("@t",rawId));
            if(Convert.ToInt64(hasBody!)==0)
            {
                string author=row.S("createdBy",row.S("updatedBy","system"));
                string authorId;
                try{authorId=SchemaApprovals.EnsureAuthUserId(c,tx,author);}
                catch{authorId=SchemaSql.GuidText(SchemaSql.NewId());}
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"task","comment")}(id,task_id,author_user_id,body_ar,created_by,created_at)
                    VALUES(@id,@t,@a,@b,@by,{SchemaSql.NowLiteral(c)})
                    """,
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@t",rawId),("@a",authorId),("@b",body.Length<=400?body:body[..400]),
                    ("@by",author));
            }
        }
    }
}
