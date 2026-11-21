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
        var rows=new List<JsonObject>();
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(SchemaSql.TableExists(c,tx,"audit","change_log"))
        {
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
                string label=r["display_name_ar"]?.ToString()??"";
                JsonObject? bag=null;
                if(diff.StartsWith('{'))
                {
                    try{bag=JsonNode.Parse(diff) as JsonObject;}
                    catch{/* truncated or legacy plain text */}
                }
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
                if(label!="")row["actionAr"]=row.S("actionAr",label);
                if(row.B("selfApprovedException")==false&&action.Contains("self_",StringComparison.OrdinalIgnoreCase))
                    row["selfApprovedException"]=true;
                seen.Add(row.S("id"));
                rows.Add(row);
            }
        }
        // Union legacy document audits so history never disappears when reading new tables.
        // When dual-written (change_log stub + full records body), prefer the full body so evidence stays whole.
        using(var legacy=Store.Command(c,tx,c is Microsoft.Data.Sqlite.SqliteConnection
            ?"SELECT body FROM records WHERE kind=@kind ORDER BY rowid DESC"
            :"SELECT body FROM records WHERE kind=@kind ORDER BY COALESCE(JSON_VALUE(body,'$.occurredAt'),JSON_VALUE(body,'$.at'),'') DESC,id DESC",
            ("@kind","audit")))
        using(var lr=legacy.ExecuteReader())
        {
            while(lr.Read())
            {
                var row=JsonNode.Parse(lr.GetString(0))!.AsObject();
                string id=row.S("id");
                if(id!=""&&seen.Contains(id))
                {
                    int idx=rows.FindIndex(a=>string.Equals(a.S("id"),id,StringComparison.OrdinalIgnoreCase));
                    if(idx>=0)rows[idx]=row;
                    continue;
                }
                if(id!="")seen.Add(id);
                rows.Add(row);
            }
        }
        return rows.OrderByDescending(a=>a.S("occurredAt",a.S("at"))).ToList();
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
        // Evidence fields stay whole; short label is stored separately in display_name_ar / diff_short.
        string shortLabel=row.S("actionAr");
        if(shortLabel.Length==0)shortLabel=AuditApi.LabelAr(action);
        if(shortLabel.Length>400)shortLabel=shortLabel[..400];
        var evidence=new JsonObject();
        foreach(string key in new[]{"reason","selfApprovedException","grantId","recordId","entityRef","entityId","entity","companyId","badge","operation","maker","approvalBypassUntil","approvalExceptionUntil","note","added","removed","counts","from","to","change","rowCount","editedRowCount","total","doNotSend","doNotSendMark"})
            if(row[key]!=null)evidence[key]=row[key]!.DeepClone();
        var stored=new JsonObject
        {
            ["action"]=action,
            ["actor"]=row.S("actor"),
            ["at"]=at.ToString("O"),
            ["occurredAt"]=at.ToString("O"),
            ["entityId"]=row.S("entityId",entityRef),
            ["entityRef"]=entityRef,
            ["actionAr"]=shortLabel,
            ["entityTypeAr"]=entityType,
            ["source"]=row.S("source","نظام")
        };
        foreach(var kv in evidence)stored[kv.Key]=kv.Value!.DeepClone();
        if(row["change"]!=null)stored["change"]=row["change"]!.DeepClone();
        string diff=stored.ToJsonString();
        // Drop descriptive payload only — never reason / grant / self-approval / record refs.
        while(diff.Length>400)
        {
            if(stored.ContainsKey("change"))stored.Remove("change");
            else if(stored.ContainsKey("added"))stored.Remove("added");
            else if(stored.ContainsKey("removed"))stored.Remove("removed");
            else if(stored.ContainsKey("note")&&stored.S("note").Length>80)stored["note"]=stored.S("note")[..80];
            else break;
            diff=stored.ToJsonString();
        }
        string sqlId=Guid.TryParse(row.S("id"),out var gid)?SchemaSql.GuidText(gid):SchemaSql.GuidText(SchemaSql.NewId());
        row["id"]=sqlId;
        // Always keep the caller document whole in records (evidence + export metadata).
        // change_log.diff_short stays a short label/bag; ListChanges prefers the full body when both exist.
        using(var full=Store.Command(c,tx,"UPDATE records SET body=@body WHERE id=@id AND kind=@kind",
            ("@id",sqlId),("@kind","audit"),("@body",row.ToJsonString())))
        {
            if(full.ExecuteNonQuery()==0)
            {
                using var insert=Store.Command(c,tx,"INSERT INTO records(id,kind,body) VALUES(@id,@kind,@body)",
                    ("@id",sqlId),("@kind","audit"),("@body",row.ToJsonString()));
                insert.ExecuteNonQuery();
            }
        }
        if(diff.Length>400)diff=shortLabel;
        string source=MapSource(row.S("source"));
        string actor=row.S("actor");
        object userId=DBNull.Value;
        if(actor!="")
        {
            try{userId=SchemaApprovals.EnsureAuthUserId(c,tx,actor);}
            catch{/* username bridge optional for legacy audit writers */}
        }
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
            ("@disp",shortLabel),
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
        // Materialize task rows first — comments/attachments need a second query (no MARS dependency).
        var heads=new List<(string Id,string Due,string Assignee,string Rank,string Status,string Title,string EntityType,string EntityId,string CreatedBy,string CreatedAt,string UpdatedBy)>();
        using(var cmd=Store.Command(c,tx,$"""
            SELECT t.id,t.due_on,t.assignee_user_id,t.assignee_rank,t.status,t.title_ar,t.entity_type,t.entity_id,t.created_by,t.created_at,t.updated_by,t.updated_at,u.username
            FROM {SchemaSql.Q(c,"task","item")} t
            LEFT JOIN {SchemaSql.Q(c,"auth","user")} u ON u.id=t.assignee_user_id
            ORDER BY t.created_at DESC
            """))
        using(var r=cmd.ExecuteReader())
        {
            while(r.Read())
            {
                string due=Convert.ToDateTime(r["due_on"],CultureInfo.InvariantCulture).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                string created=r["created_at"] is DBNull or null?"":Convert.ToDateTime(r["created_at"],CultureInfo.InvariantCulture).ToString("O");
                heads.Add((
                    r["id"].ToString()??"",
                    due,
                    r["username"]?.ToString()??"",
                    r["assignee_rank"]?.ToString()??"",
                    StatusFromSql(r["status"].ToString()??"open"),
                    r["title_ar"].ToString()??"",
                    r["entity_type"]?.ToString()??"",
                    r["entity_id"] is DBNull or null?"":r["entity_id"].ToString()!,
                    r["created_by"]?.ToString()??"",
                    created,
                    r["updated_by"]?.ToString()??""));
            }
        }
        var rows=new List<JsonObject>();
        foreach(var h in heads)
        {
            var comments=ListComments(c,tx,h.Id);
            string companyId="",bodyAr="";
            var visible=new JsonArray();
            foreach(var node in comments.OfType<JsonObject>())
            {
                string body=node.S("bodyAr");
                if(body.StartsWith("__company__:",StringComparison.Ordinal)){companyId=body["__company__:".Length..];continue;}
                if(body.StartsWith("__body__:",StringComparison.Ordinal)){bodyAr=body["__body__:".Length..];continue;}
                visible.Add(node.DeepClone());
            }
            // Pre-marker rows stored the body as the first plain comment.
            if(bodyAr==""&&visible.Count>0)bodyAr=visible[0]!.AsObject().S("bodyAr");
            rows.Add(new()
            {
                ["id"]=h.Id,
                ["titleAr"]=h.Title,
                ["bodyAr"]=bodyAr,
                ["assignee"]=h.Assignee,
                ["rank"]=h.Rank,
                ["status"]=h.Status,
                ["companyId"]=companyId,
                ["entityType"]=h.EntityType,
                ["entityId"]=h.EntityId,
                ["dueAt"]=h.Due+"T00:00:00+03:00",
                ["createdBy"]=h.CreatedBy,
                ["createdAt"]=h.CreatedAt,
                ["updatedBy"]=h.UpdatedBy,
                ["comments"]=visible,
                ["attachments"]=ListAttachments(c,tx,h.Id)
            });
        }
        return rows;
    }

    static JsonArray ListAttachments(DbConnection c,DbTransaction? tx,string taskId)
    {
        var arr=new JsonArray();
        if(!SchemaSql.TableExists(c,tx,"task","attachment"))return arr;
        using var cmd=Store.Command(c,tx,$"SELECT file_name,file_hash,created_at FROM {SchemaSql.Q(c,"task","attachment")} WHERE task_id=@t ORDER BY created_at",("@t",taskId));
        using var r=cmd.ExecuteReader();
        while(r.Read())
            arr.Add(new JsonObject
            {
                ["fileName"]=r["file_name"]?.ToString()??"",
                ["fileHash"]=r["file_hash"] is byte[] b?Convert.ToHexString(b):r["file_hash"]?.ToString()??"",
                ["at"]=r["created_at"]?.ToString()??""
            });
        return arr;
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
        if(SchemaSql.TableExists(c,tx,"task","comment"))
        {
            var comments=row["comments"] as JsonArray??new JsonArray();
            SchemaSql.Exec(c,tx,$"DELETE FROM {SchemaSql.Q(c,"task","comment")} WHERE task_id=@t",("@t",rawId));
            void InsertComment(string body,string author)
            {
                if(body=="")return;
                string authorId;
                try{authorId=SchemaApprovals.EnsureAuthUserId(c,tx,author);}
                catch{return;}
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"task","comment")}(id,task_id,author_user_id,body_ar,created_by,created_at)
                    VALUES(@id,@t,@a,@b,@by,{SchemaSql.NowLiteral(c)})
                    """,
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@t",rawId),("@a",authorId),("@b",body.Length<=400?body:body[..400]),
                    ("@by",author));
            }
            string author=row.S("updatedBy",row.S("createdBy","system"));
            // Persist company scope and body without schema columns (FINAL task.item has none).
            if(row.S("companyId")!="")InsertComment("__company__:"+row.S("companyId"),row.S("createdBy",author));
            // Body must save even when comments already exist (marker keeps it off the visible list).
            if(row.S("bodyAr")!="")InsertComment("__body__:"+row.S("bodyAr"),author);
            else if(comments.Count==0){/* no body, no comments */}
            foreach(var node in comments.OfType<JsonObject>())
            {
                string body=node.S("bodyAr",node.S("body"));
                if(body.StartsWith("__company__:",StringComparison.Ordinal)||body.StartsWith("__body__:",StringComparison.Ordinal))continue;
                InsertComment(body,node.S("by",author));
            }
        }
        if(SchemaSql.TableExists(c,tx,"task","attachment"))
        {
            SchemaSql.Exec(c,tx,$"DELETE FROM {SchemaSql.Q(c,"task","attachment")} WHERE task_id=@t",("@t",rawId));
            foreach(var node in (row["attachments"] as JsonArray??[]).OfType<JsonObject>())
            {
                string name=node.S("fileName",node.S("name"));
                if(name=="")continue;
                byte[] hash;
                string hex=node.S("fileHash",node.S("hash"));
                try{hash=hex.Length>=64?Convert.FromHexString(hex[..64]):System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name+rawId));}
                catch{hash=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name+rawId));}
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"task","attachment")}(id,task_id,file_hash,file_name,created_at)
                    VALUES(@id,@t,@h,@n,{SchemaSql.NowLiteral(c)})
                    """,
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@t",rawId),("@h",hash),("@n",name.Length<=200?name:name[..200]));
            }
        }
    }
}
