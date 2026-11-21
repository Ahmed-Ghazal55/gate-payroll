using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-29: staff chat polling APIs (G4/G8/X25). Chat never creates notifications. Voice ≤ 2 min; image ≤ 5 MB; voice ≤ 2 MB.</summary>
public static class StaffChatApi
{
    public const int MaxImageBytes=5*1024*1024,MaxVoiceBytes=2*1024*1024,MaxVoiceSeconds=120;
    public const string ViewPerm="chat.view",PostPerm="chat.post",ModeratePerm="chat.moderate",AdminPerm="chat.admin";

    static bool CanView(ClaimsPrincipal a)=>AccessPolicy.Has(a,ViewPerm)||AccessPolicy.Has(a,PostPerm)||AccessPolicy.Has(a,ModeratePerm)||AccessPolicy.Has(a,AdminPerm);
    static bool CanPost(ClaimsPrincipal a)=>AccessPolicy.Has(a,PostPerm)||AccessPolicy.Has(a,ModeratePerm)||AccessPolicy.Has(a,AdminPerm);
    static bool CanAdmin(ClaimsPrincipal a)=>AccessPolicy.Has(a,AdminPerm);

    static bool InCompanyScope(ClaimsPrincipal actor,string? companyId)
    {
        if(AccessPolicy.Administrator(actor)||FinancialScopes.AllCompanies(actor))return true;
        if(string.IsNullOrWhiteSpace(companyId))return AccessPolicy.Administrator(actor);
        return FinancialScopes.Sponsor(actor,companyId);
    }

    static bool IsMember(DbConnection c,DbTransaction? tx,string channelId,string userId)
        =>Convert.ToInt64(SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"chat","membership")} WHERE channel_id=@c AND user_id=@u",("@c",channelId),("@u",userId))!)>0;

    static string? MemberCompany(DbConnection c,DbTransaction? tx,string channelId,string userId)
    {
        object? v=SchemaSql.Scalar(c,tx,$"SELECT company_id FROM {SchemaSql.Q(c,"chat","membership")} WHERE channel_id=@c AND user_id=@u",("@c",channelId),("@u",userId));
        return v is null or DBNull?null:v switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(v,CultureInfo.InvariantCulture)};
    }

    public static JsonObject ListChannels(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor)
    {
        J.Need(CanView(actor),"ليست لديك صلاحية لعرض المحادثة.",403);
        // Resolve membership without inventing users on pure GET when possible.
        object? uid=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"auth","user")} WHERE username=@u OR username=@n",
            ("@u",actor.Identity!.Name!),("@n",UserAccess.Normalize(actor.Identity!.Name!)));
        var items=new JsonArray();
        string sql=uid==null&&!AccessPolicy.Administrator(actor)
            ?"SELECT id,code,name_ar FROM "+SchemaSql.Q(c,"chat","channel")+" WHERE 1=0"
            :AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,AdminPerm)
                ?$"SELECT id,code,name_ar FROM {SchemaSql.Q(c,"chat","channel")} ORDER BY code"
                :$"""
                SELECT ch.id,ch.code,ch.name_ar FROM {SchemaSql.Q(c,"chat","channel")} ch
                INNER JOIN {SchemaSql.Q(c,"chat","membership")} m ON m.channel_id=ch.id
                WHERE m.user_id=@u ORDER BY ch.code
                """;
        using var cmd=uid==null
            ?Store.Command(c,tx,sql)
            :Store.Command(c,tx,sql,("@u",uid switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(uid,CultureInfo.InvariantCulture)!}));
        using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            string id=r.GetValue(0).ToString()!;
            string? company=uid==null?null:MemberCompany(c,tx,id,uid switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(uid,CultureInfo.InvariantCulture)!});
            if(company!=null&&!InCompanyScope(actor,company))continue;
            var actions=new JsonArray();
            if(CanView(actor))actions.Add("poll_messages");
            if(CanPost(actor))actions.Add("post_message");
            if(CanAdmin(actor))actions.Add("manage_channel");
            items.Add(new JsonObject{["id"]=id,["code"]=r.GetString(1),["nameAr"]=r.GetString(2),["companyId"]=company??"",["allowedActions"]=actions});
        }
        return new(){["items"]=items,["chatCreatesNotify"]=false,["allowedActions"]=new JsonArray(CanAdmin(actor)?"create_channel":"poll_channels"),["mediaLimits"]=new JsonObject{["imageMaxBytes"]=MaxImageBytes,["voiceMaxBytes"]=MaxVoiceBytes,["voiceMaxSeconds"]=MaxVoiceSeconds,["videoAllowed"]=false}};
    }

    public static JsonObject CreateChannel(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,AdminPerm);
        string code=input.Required("code").Trim().ToLowerInvariant();
        J.Need(System.Text.RegularExpressions.Regex.IsMatch(code,@"^[a-z0-9_-]{2,40}$"),"رمز القناة غير صالح.",400);
        string name=input.Required("nameAr").Trim();
        J.Need(name.Length is >=2 and <=200,"اسم القناة مطلوب.",400);
        string companyId=input.S("companyId");
        if(companyId!="")J.Need(InCompanyScope(actor,companyId),"القناة خارج نطاق شركتك.",403);
        object? exists=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"chat","channel")} WHERE code=@c",("@c",code));
        J.Need(Convert.ToInt64(exists!)==0,"رمز القناة مستخدم.",409);
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"chat","channel")}(id,code,name_ar,created_by,created_at)
            VALUES(@id,@c,@n,@by,{SchemaSql.NowLiteral(c)})
            """,("@id",id),("@c",code),("@n",name),("@by",actor.Identity!.Name!));
        string userId=SchemaApprovals.EnsureAuthUserId(c,tx,actor.Identity!.Name!);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"chat","membership")}(id,channel_id,user_id,company_id,created_by,created_at)
            VALUES(@id,@ch,@u,@co,@by,{SchemaSql.NowLiteral(c)})
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@ch",id),("@u",userId),
            ("@co",(object?)(companyId==""?null:companyId)??DBNull.Value),("@by",actor.Identity!.Name!));
        var members=input["memberUsernames"] as JsonArray??[];
        foreach(var node in members)
        {
            string nameUser=node?.ToString()?.Trim()??"";
            if(nameUser==""||string.Equals(nameUser,actor.Identity!.Name,StringComparison.OrdinalIgnoreCase))continue;
            var doc=UserAccess.Find(c,nameUser,tx);if(doc==null)continue;
            string mid=SchemaApprovals.EnsureAuthUserId(c,tx,nameUser);
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"chat","membership")}(id,channel_id,user_id,company_id,created_by,created_at)
                VALUES(@id,@ch,@u,@co,@by,{SchemaSql.NowLiteral(c)})
                """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@ch",id),("@u",mid),
                ("@co",(object?)(companyId==""?null:companyId)??DBNull.Value),("@by",actor.Identity!.Name!));
        }
        Store.Audit(c,tx,"chat.channel_create",id,actor.Identity!.Name!);
        return new(){["id"]=id,["code"]=code,["nameAr"]=name,["companyId"]=companyId,["allowedActions"]=new JsonArray("manage_channel","post_message"),["chatCreatesNotify"]=false};
    }

    public static JsonObject ListMessages(DbConnection c,ClaimsPrincipal actor,string channelId,string? after)
    {
        J.Need(CanView(actor),"ليست لديك صلاحية لعرض المحادثة.",403);
        object? uid=SchemaSql.Scalar(c,null,$"SELECT id FROM {SchemaSql.Q(c,"auth","user")} WHERE username=@u OR username=@n",
            ("@u",actor.Identity!.Name!),("@n",UserAccess.Normalize(actor.Identity!.Name!)));
        J.Need(uid!=null||AccessPolicy.Administrator(actor),"انضم للقناة أولاً.",403);
        string userId=uid==null?"":uid switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(uid,CultureInfo.InvariantCulture)!};
        if(!AccessPolicy.Administrator(actor)&&!AccessPolicy.Has(actor,AdminPerm))
            J.Need(IsMember(c,null,channelId,userId),"لست عضواً في هذه القناة.",403);
        string? company=userId==""?null:MemberCompany(c,null,channelId,userId);
        if(company!=null)J.Need(InCompanyScope(actor,company),"القناة خارج نطاق شركتك.",403);

        var items=new JsonArray();
        string sql=$"""
            SELECT id,author_user_id,body_ar,media_kind,media_duration_seconds,media_bytes,created_at
            FROM {SchemaSql.Q(c,"chat","message")}
            WHERE channel_id=@ch {(string.IsNullOrWhiteSpace(after)?"":"AND created_at > @after")}
            ORDER BY created_at ASC
            """;
        var args=new List<(string,object)>{("@ch",channelId)};
        if(!string.IsNullOrWhiteSpace(after))args.Add(("@after",after));
        using var cmd=Store.Command(c,null,sql,args.ToArray());
        using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            items.Add(new JsonObject
            {
                ["id"]=r.GetValue(0).ToString(),
                ["authorUserId"]=r.GetValue(1).ToString(),
                ["bodyAr"]=r.IsDBNull(2)?"":r.GetString(2),
                ["mediaKind"]=r.IsDBNull(3)?"":r.GetString(3),
                ["mediaDurationSeconds"]=r.IsDBNull(4)?null:JsonValue.Create(Convert.ToInt32(r.GetValue(4),CultureInfo.InvariantCulture)),
                ["mediaBytes"]=r.IsDBNull(5)?null:JsonValue.Create(Convert.ToInt32(r.GetValue(5),CultureInfo.InvariantCulture)),
                ["createdAt"]=Convert.ToDateTime(r.GetValue(6),CultureInfo.InvariantCulture).ToString("O")
            });
        }
        return new(){["items"]=items,["chatCreatesNotify"]=false,["allowedActions"]=new JsonArray(CanPost(actor)?"post_message":"poll_messages")};
    }

    public static JsonObject PostMessage(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string channelId,JsonObject input)
    {
        J.Need(CanPost(actor),"ليست لديك صلاحية للنشر.",403);
        string userId=SchemaApprovals.EnsureAuthUserId(c,tx,actor.Identity!.Name!);
        if(!AccessPolicy.Administrator(actor)&&!AccessPolicy.Has(actor,AdminPerm))
            J.Need(IsMember(c,tx,channelId,userId),"لست عضواً في هذه القناة.",403);
        string? company=MemberCompany(c,tx,channelId,userId);
        if(company!=null)J.Need(InCompanyScope(actor,company),"القناة خارج نطاق شركتك.",403);
        // Chat must never create notify rows (G4).
        J.Need(!input.B("createNotify")&&input.S("source")!="notify","رسالة المحادثة لا تُنشئ إشعاراً.",400);

        string body=input.S("bodyAr").Trim();
        string mediaKind=input.S("mediaKind").Trim().ToLowerInvariant();
        int? duration=input.ContainsKey("mediaDurationSeconds")?(int?)input.N("mediaDurationSeconds"):null;
        int? bytes=input.ContainsKey("mediaBytes")?(int?)input.N("mediaBytes"):null;
        byte[]? mediaHash=null;
        if(mediaKind!="")
        {
            J.Need(mediaKind is "image" or "voice","يُسمح بالصورة أو الصوت فقط؛ بدون فيديو.",400);
            J.Need(bytes!=null&&bytes>0,"حجم المرفق مطلوب.",400);
            if(mediaKind=="image")J.Need(bytes<=MaxImageBytes,"حجم الصورة أكبر من ٥ ميغابايت.",400);
            if(mediaKind=="voice")
            {
                J.Need(bytes<=MaxVoiceBytes,"حجم الصوت أكبر من ٢ ميغابايت.",400);
                J.Need(duration!=null&&duration>0&&duration<=MaxVoiceSeconds,"مدة الصوت يجب ألا تتجاوز دقيقتين.",400);
            }
            string raw=input.S("mediaHash");
            mediaHash=raw!=""&&raw.Length>=64?Convert.FromHexString(raw[..64]):SHA256.HashData(Encoding.UTF8.GetBytes(input.S("mediaName",mediaKind)+bytes));
        }
        else J.Need(body.Length is >=1 and <=4000,"نص الرسالة مطلوب.",400);

        string id=SchemaSql.GuidText(SchemaSql.NewId());
        bool hasDuration=ColumnReady(c,tx);
        if(hasDuration)
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"chat","message")}
                (id,channel_id,author_user_id,body_ar,media_hash,media_kind,media_duration_seconds,media_bytes,created_at,created_by)
                VALUES(@id,@ch,@au,@body,@hash,@kind,@dur,@bytes,{SchemaSql.NowLiteral(c)},@by)
                """,
                ("@id",id),("@ch",channelId),("@au",userId),
                ("@body",(object?)(body==""?null:body)??DBNull.Value),
                ("@hash",(object?)mediaHash??DBNull.Value),
                ("@kind",(object?)(mediaKind==""?null:mediaKind)??DBNull.Value),
                ("@dur",(object?)duration??DBNull.Value),("@bytes",(object?)bytes??DBNull.Value),
                ("@by",actor.Identity!.Name!));
        else
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"chat","message")}
                (id,channel_id,author_user_id,body_ar,media_hash,media_kind,created_at,created_by)
                VALUES(@id,@ch,@au,@body,@hash,@kind,{SchemaSql.NowLiteral(c)},@by)
                """,
                ("@id",id),("@ch",channelId),("@au",userId),
                ("@body",(object?)(body==""?null:body)??DBNull.Value),
                ("@hash",(object?)mediaHash??DBNull.Value),
                ("@kind",(object?)(mediaKind==""?null:mediaKind)??DBNull.Value),
                ("@by",actor.Identity!.Name!));
        Store.Audit(c,tx,"chat.message_post",id,actor.Identity!.Name!);
        return new(){["id"]=id,["channelId"]=channelId,["chatCreatesNotify"]=false,["allowedActions"]=new JsonArray("post_message"),["messageAr"]="تم إرسال الرسالة."};
    }

    public static JsonObject PostAttachment(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string channelId,JsonObject input)
    {
        // Attachment is a message with media only (polling API surface).
        J.Need(input.S("mediaKind")!="","نوع المرفق مطلوب.",400);
        return PostMessage(c,tx,actor,channelId,input);
    }

    static bool ColumnReady(DbConnection c,DbTransaction? tx)
    {
        if(SchemaSql.IsSql(c))
            return Convert.ToInt32(SchemaSql.Scalar(c,tx,"SELECT CASE WHEN COL_LENGTH(N'chat.message',N'media_duration_seconds') IS NULL THEN 0 ELSE 1 END")!)==1;
        using var cmd=Store.Command(c,tx,"PRAGMA table_info(chat_message)");
        using var r=cmd.ExecuteReader();
        while(r.Read())if(string.Equals(r.GetString(1),"media_duration_seconds",StringComparison.OrdinalIgnoreCase))return true;
        return false;
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/chat/channels",(HttpContext ctx)=>store.Write((c,tx)=>
        {
            // Bridge membership identity when listing; no notify side effects.
            _=SchemaApprovals.EnsureAuthUserId(c,tx,ctx.User.Identity!.Name!);
            return ListChannels(c,tx,ctx.User);
        })).WithTags("Administration").WithSummary("Poll staff chat channels in scope");
        api.MapPost("/chat/channels",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>CreateChannel(c,tx,ctx.User,input)))
            .WithTags("Administration").WithSummary("Create staff chat channel with membership");
        api.MapGet("/chat/channels/{id}/messages",(string id,string? after,HttpContext ctx)=>store.Read(c=>ListMessages(c,ctx.User,id,after)))
            .WithTags("Administration").WithSummary("Poll channel messages");
        api.MapPost("/chat/channels/{id}/messages",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>PostMessage(c,tx,ctx.User,id,input)))
            .WithTags("Administration").WithSummary("Post staff chat message (never notifies)");
        api.MapPost("/chat/channels/{id}/attachments",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>PostAttachment(c,tx,ctx.User,id,input)))
            .WithTags("Administration").WithSummary("Post chat attachment with media limits");
    }
}
