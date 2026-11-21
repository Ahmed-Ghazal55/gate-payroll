using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-28: notify-settings, notify send, operational tasks. Chat must never create notify rows (G4).
/// Reply SLA default = 3 working days then reminder (X8). WMRG: storage is notify.* / task.* via Store redirects.</summary>
public static class NotifyTasksApi
{
    public const string SettingsKind="notify_settings",TaskKind="task_item",SettingsPerm="admin.notify.manage";
    public const string SendGeneral="notify.send.general",SendCustom="notify.send.custom";
    public const string AssignPerm="tasks.assign",ManagePerm="tasks.manage";
    public const int DefaultReplyWorkingDays=3;

    public static int ReplyWorkingDays(DbConnection c,DbTransaction? tx=null)
    {
        // Prefer admin setting reply_working_days when present; else notify-settings; else default 3.
        try
        {
            using var cmd=Store.Command(c,tx,SchemaSql.IsSql(c)
                ?$"SELECT value_json FROM {SchemaSql.Q(c,"ref","setting")} WHERE [key]=@k"
                :$"SELECT value_json FROM {SchemaSql.Q(c,"ref","setting")} WHERE key=@k",
                ("@k","reply_working_days"));
            if(cmd.ExecuteScalar() is string json)
            {
                var node=JsonNode.Parse(json);
                int fromAdmin=node switch
                {
                    JsonObject o when o.ContainsKey("days")=>(int)o.N("days"),
                    JsonObject o when o.ContainsKey("value")=>(int)o.N("value"),
                    JsonValue v when v.TryGetValue<int>(out var n)=>n,
                    _=>0
                };
                if(fromAdmin is >=1 and <=30)return fromAdmin;
            }
        }
        catch{/* ref.setting may be absent in early tests */}
        var row=Store.Get(c,"notify-settings:global",SettingsKind,tx);
        int days=(int)(row?.N("replyWorkingDays")??DefaultReplyWorkingDays);
        return days is >=1 and <=30?days:DefaultReplyWorkingDays;
    }

    public static DateTimeOffset AddWorkingDays(DateTimeOffset start,int workingDays)
    {
        var cursor=start.ToOffset(TimeSpan.FromHours(3));
        int left=Math.Max(0,workingDays);
        while(left>0)
        {
            cursor=cursor.AddDays(1);
            if(cursor.DayOfWeek is not (DayOfWeek.Friday or DayOfWeek.Saturday))left--;
        }
        return cursor;
    }

    static bool InCompanyScope(ClaimsPrincipal actor,string? companyId)
    {
        if(AccessPolicy.Administrator(actor)||FinancialScopes.AllCompanies(actor))return true;
        if(string.IsNullOrWhiteSpace(companyId))return false;
        return FinancialScopes.Sponsor(actor,companyId);
    }

    static bool UserInSenderScope(ClaimsPrincipal actor,JsonObject user)
    {
        if(AccessPolicy.Administrator(actor)||FinancialScopes.AllCompanies(actor))return true;
        var scope=FinancialScopes.Effective(user);
        var sponsors=(scope["sponsorIds"] as JsonArray??[]).Select(n=>n?.ToString()??"").Where(s=>s!="");
        return sponsors.Any(s=>FinancialScopes.Sponsor(actor,s));
    }

    static bool CanSeeTask(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject task)
    {
        if(!InCompanyScope(actor,task.S("companyId")))return false;
        string me=actor.Identity?.Name??"";
        if(string.Equals(task.S("createdBy"),me,StringComparison.OrdinalIgnoreCase))return true;
        if(string.Equals(task.S("assignee"),me,StringComparison.OrdinalIgnoreCase))return true;
        if(AccessPolicy.Has(actor,ManagePerm))return true;
        if(task.S("entityType") is "rider" or "iqama" && task.S("entityId")!=""
            && OrgDirectory.Supervises(c,tx,me,task.S("entityId")))return true;
        return false;
    }

    static bool CanManageTask(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject task)
    {
        if(!CanSeeTask(c,tx,actor,task))return false;
        if(AccessPolicy.Has(actor,ManagePerm))return true;
        string me=actor.Identity?.Name??"";
        return AccessPolicy.Has(actor,AssignPerm)
            &&(string.Equals(task.S("createdBy"),me,StringComparison.OrdinalIgnoreCase)
               ||string.Equals(task.S("assignee"),me,StringComparison.OrdinalIgnoreCase));
    }

    public static JsonObject GetSettings(DbConnection c,ClaimsPrincipal actor)
    {
        AccessPolicy.Demand(actor,SettingsPerm);
        var row=Store.Get(c,"notify-settings:global",SettingsKind)??new JsonObject
        {
            ["id"]="notify-settings:global",
            ["replyWorkingDays"]=DefaultReplyWorkingDays,
            ["matrix"]=new JsonArray(),
            ["channels"]=new JsonArray("in_app","push")
        };
        var view=row.DeepClone().AsObject();
        view["replyWorkingDays"]=ReplyWorkingDays(c);
        view["allowedActions"]=new JsonArray("save_notify_settings");
        view["chatCreatesNotify"]=false;
        return view;
    }

    public static JsonObject PutSettings(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,SettingsPerm);
        int days=input.ContainsKey("replyWorkingDays")?(int)input.N("replyWorkingDays"):DefaultReplyWorkingDays;
        J.Need(days is >=1 and <=30,"مهلة الرد من 1 إلى 30 يوم عمل.",400);
        var channels=input["channels"] as JsonArray??new JsonArray("in_app","push");
        var matrix=input["matrix"] as JsonArray??new JsonArray();
        var row=new JsonObject
        {
            ["id"]="notify-settings:global",
            ["replyWorkingDays"]=days,
            ["matrix"]=matrix.DeepClone(),
            ["channels"]=channels.DeepClone(),
            ["updatedBy"]=actor.Identity!.Name,
            ["updatedAt"]=DateTimeOffset.UtcNow.ToString("O")
        };
        Store.Save(c,SettingsKind,row,tx);
        Store.Audit(c,tx,"admin.notify_settings_save","notify-settings:global",actor.Identity!.Name!);
        var view=row.DeepClone().AsObject();
        view["allowedActions"]=new JsonArray("save_notify_settings");
        view["chatCreatesNotify"]=false;
        return view;
    }

    public static JsonObject Send(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        bool custom=input.B("custom")||(input["userIds"] as JsonArray)?.Count>0;
        AccessPolicy.Demand(actor,custom?SendCustom:SendGeneral);
        string title=input.Required("titleAr");
        string body=input.S("bodyAr",input.S("body"));
        J.Need(title.Length is >=1 and <=200,"عنوان الإشعار مطلوب.",400);
        J.Need(!string.Equals(input.S("source"),"chat",StringComparison.OrdinalIgnoreCase),"رسائل المحادثة لا تُنشئ إشعارات.",400);
        string? companyId=input.S("companyId")==""?null:input.S("companyId");
        if(companyId!=null)FinancialScopes.DemandSponsor(actor,companyId);
        else J.Need(AccessPolicy.Administrator(actor)||FinancialScopes.AllCompanies(actor),"حدد شركة ضمن نطاقك.",403);

        var settings=Store.Get(c,"notify-settings:global",SettingsKind,tx);
        var allowedChannels=(settings?["channels"] as JsonArray??new JsonArray("in_app","push"))
            .Select(n=>n?.ToString()??"").Where(s=>s!="").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var audience=(settings?["matrix"] as JsonArray??[]).Select(n=>n?.ToString()??"").Where(s=>s!="").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requestedChannels=(input["channels"] as JsonArray??[])
            .Select(n=>n?.ToString()??"").Where(s=>s!="").ToList();
        if(requestedChannels.Count==0)requestedChannels=allowedChannels.ToList();
        foreach(string ch in requestedChannels)
            J.Need(allowedChannels.Contains(ch),"قناة إشعار غير مفعّلة في الإعدادات: "+ch,400);

        int replyDays=ReplyWorkingDays(c,tx);
        DateTimeOffset due=AddWorkingDays(DateTimeOffset.UtcNow,replyDays);
        List<string> targets;
        if(custom)
        {
            targets=[];
            foreach(string idOrName in (input["userIds"] as JsonArray??[]).Select(u=>u?.ToString()??"").Where(u=>u!="").Distinct())
            {
                var user=Store.Get(c,idOrName,"user",tx)
                    ??Store.List(c,"user",tx).FirstOrDefault(u=>string.Equals(u.S("username"),idOrName,StringComparison.OrdinalIgnoreCase))
                    ??throw new ApiError("المستلم غير موجود: "+idOrName,400);
                J.Need(UserAccess.Status(user)=="active","المستلم غير نشط.",400);
                J.Need(UserInSenderScope(actor,user),"المستلم خارج نطاق الشركة.",403);
                if(audience.Count>0)
                {
                    string role=UserAccess.Role(user);
                    J.Need(audience.Contains(role)||audience.Contains(user.S("username"))||audience.Contains("role:"+role),
                        "المستلم خارج جمهور الإشعار المحفوظ.",403);
                }
                targets.Add(user.S("username"));
            }
        }
        else
        {
            targets=Store.List(c,"user",tx)
                .Where(u=>UserAccess.Status(u)=="active"&&UserInSenderScope(actor,u))
                .Where(u=>audience.Count==0||audience.Contains(UserAccess.Role(u))||audience.Contains(u.S("username"))||audience.Contains("role:"+UserAccess.Role(u)))
                .Select(u=>u.S("username")).Distinct().ToList();
        }
        J.Need(targets.Count>0,"لا مستلمين للإشعار.",400);
        string eventId="notify-send:"+Guid.NewGuid().ToString("N");
        Notifications.RaiseFor(c,tx,targets,eventId,title,body,actor.Identity!.Name!,"notify.send",eventId,requiresAction:input.B("requiresAction"),companyId:companyId);
        foreach(var n in Store.List(c,"sys_notification",tx).Where(n=>n.S("eventKey")==eventId||n.S("id").Contains(eventId,StringComparison.Ordinal)))
        {
            n["replyDueAt"]=due.ToString("O");
            n["replyWorkingDays"]=replyDays;
            n["channels"]=new JsonArray(requestedChannels.Select(ch=>(JsonNode?)ch).ToArray());
            Store.Save(c,"sys_notification",n,tx);
        }
        Store.Audit(c,tx,custom?"notify.send.custom":"notify.send.general",eventId,actor.Identity!.Name!);
        return new(){["id"]=eventId,["recipients"]=targets.Count,["replyDueAt"]=due.ToString("O"),["replyWorkingDays"]=replyDays,["channels"]=new JsonArray(requestedChannels.Select(ch=>(JsonNode?)ch).ToArray()),["allowedActions"]=new JsonArray()};
    }

    static JsonObject TaskView(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject t)
    {
        var actions=new JsonArray();
        if(CanManageTask(c,tx,actor,t))
        {
            if(t.S("status") is "open" or "escalated"){actions.Add((JsonNode)"edit");actions.Add((JsonNode)"complete");}
            if(t.S("status")=="open")actions.Add((JsonNode)"escalate");
        }
        var copy=t.DeepClone().AsObject();copy["allowedActions"]=actions;return copy;
    }

    public static JsonObject ListTasks(DbConnection c,ClaimsPrincipal actor,string? status)
    {
        if(!AccessPolicy.Has(actor,AssignPerm)&&!AccessPolicy.Has(actor,ManagePerm))AccessPolicy.Demand(actor,AssignPerm);
        IEnumerable<JsonObject> rows=Store.List(c,TaskKind)
            .Where(t=>CanSeeTask(c,null,actor,t))
            .Select(t=>TaskView(c,null,actor,t));
        if(!string.IsNullOrWhiteSpace(status))rows=rows.Where(t=>t.S("status")==status);
        return new(){["items"]=new JsonArray(rows.OrderByDescending(t=>t.S("createdAt")).Select(t=>(JsonNode?)t).ToArray()),["allowedActions"]=AccessPolicy.Has(actor,AssignPerm)?new JsonArray("create"):new JsonArray()};
    }

    public static JsonObject CreateTask(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,AssignPerm);
        string title=input.Required("titleAr");
        J.Need(title.Length is >=1 and <=200,"عنوان المهمة مطلوب.",400);
        string companyId=input.S("companyId");
        J.Need(companyId!="","حدد شركة المهمة.",400);
        FinancialScopes.DemandSponsor(actor,companyId);
        string assignee=input.S("assignee",input.S("assignedTo"));
        if(assignee!="")
        {
            var user=Store.List(c,"user",tx).FirstOrDefault(u=>string.Equals(u.S("username"),assignee,StringComparison.OrdinalIgnoreCase))
                ??throw new ApiError("المكلّف غير موجود؛ لا يُنشأ مستخدم تلقائيًا.",400);
            J.Need(UserAccess.Status(user)=="active","المكلّف غير نشط.",400);
            assignee=user.S("username");
        }
        int replyDays=ReplyWorkingDays(c,tx);
        DateTimeOffset due=input.S("dueAt")!=""&&DateTimeOffset.TryParse(input.S("dueAt"),out var parsed)
            ?parsed:AddWorkingDays(DateTimeOffset.UtcNow,replyDays);
        var task=new JsonObject
        {
            ["id"]=SchemaSql.GuidText(SchemaSql.NewId()),
            ["titleAr"]=title,
            ["bodyAr"]=input.S("bodyAr"),
            ["assignee"]=assignee,
            ["rank"]=input.S("rank"),
            ["status"]="open",
            ["companyId"]=companyId,
            ["entityType"]=input.S("entityType"),
            ["entityId"]=input.S("entityId"),
            ["dueAt"]=due.ToString("O"),
            ["replyWorkingDays"]=replyDays,
            ["createdBy"]=actor.Identity!.Name,
            ["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),
            ["comments"]=input["comments"]?.DeepClone()??new JsonArray(),
            ["attachments"]=input["attachments"]?.DeepClone()??new JsonArray()
        };
        Store.Save(c,TaskKind,task,tx);
        Store.Audit(c,tx,"task.create",task.S("id"),actor.Identity!.Name!);
        if(task.S("assignee")!="")
            Notifications.RaiseFor(c,tx,[task.S("assignee")],task.S("id"),"مهمة جديدة: "+title,task.S("bodyAr"),actor.Identity!.Name!,"task",task.S("id"),true,companyId);
        return TaskView(c,tx,actor,task);
    }

    public static JsonObject UpdateTask(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        var task=Store.Get(c,id,TaskKind,tx)??throw new ApiError("المهمة غير موجودة.",404);
        J.Need(CanManageTask(c,tx,actor,task),"لا صلاحية لتعديل هذه المهمة.",403);
        if(input.ContainsKey("titleAr"))task["titleAr"]=input.Required("titleAr");
        if(input.ContainsKey("bodyAr"))task["bodyAr"]=input.S("bodyAr");
        if(input.ContainsKey("assignee")||input.ContainsKey("assignedTo"))
        {
            string assignee=input.S("assignee",input.S("assignedTo"));
            if(assignee!="")
            {
                var user=Store.List(c,"user",tx).FirstOrDefault(u=>string.Equals(u.S("username"),assignee,StringComparison.OrdinalIgnoreCase))
                    ??throw new ApiError("المكلّف غير موجود؛ لا يُنشأ مستخدم تلقائيًا.",400);
                assignee=user.S("username");
            }
            task["assignee"]=assignee;
        }
        if(input.ContainsKey("status"))
        {
            string status=input.S("status");
            J.Need(status is "open" or "completed" or "escalated" or "cancelled","حالة مهمة غير صالحة.",400);
            task["status"]=status;
            if(status=="completed")
            {
                task["completedBy"]=actor.Identity!.Name;
                task["completedAt"]=DateTimeOffset.UtcNow.ToString("O");
                Notifications.CloseTasks(c,tx,"task",id,actor.Identity!.Name!);
            }
        }
        if(input["comments"] is JsonArray comments)task["comments"]=comments.DeepClone();
        if(input["attachments"] is JsonArray attachments)task["attachments"]=attachments.DeepClone();
        task["updatedBy"]=actor.Identity!.Name;
        task["updatedAt"]=DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c,TaskKind,task,tx);
        Store.Audit(c,tx,"task.update",id,actor.Identity!.Name!);
        return TaskView(c,tx,actor,Store.Get(c,id,TaskKind,tx)!);
    }

    public static void EscalateOverdue(DbConnection c,DbTransaction tx)
    {
        var now=DateTimeOffset.UtcNow;
        foreach(var task in Store.List(c,TaskKind,tx).Where(x=>x.S("status")=="open"))
        {
            if(!DateTimeOffset.TryParse(task.S("dueAt"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var due))continue;
            if(due>now)continue;
            task["status"]="escalated";
            task["escalatedAt"]=now.ToString("O");
            Store.Save(c,TaskKind,task,tx);
            string reminderKey=task.S("id")+":reminder:"+now.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            if(task.S("assignee")!=""&&Store.Get(c,"notice:"+reminderKey+":"+task.S("assignee"),"sys_notification",tx)==null)
                Notifications.RaiseFor(c,tx,[task.S("assignee")],reminderKey,"تذكير مهمة متأخرة",task.S("titleAr"),"system","task.reminder",task.S("id"),true,task.S("companyId")==""?null:task.S("companyId"));
        }
    }

    public static void RemindUnanswered(DbConnection c,DbTransaction tx)
    {
        var now=DateTimeOffset.UtcNow;
        foreach(var n in Store.List(c,"sys_notification",tx).Where(x=>x.B("requiresAction")&&x.S("actionStatus")=="open"))
        {
            if(!DateTimeOffset.TryParse(n.S("replyDueAt"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var due)||due>now)continue;
            string user=n.S("assignedTo");if(user=="")continue;
            string key=n.S("id")+":sla-reminder:"+due.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            if(Store.Get(c,"notice:"+key+":"+user,"sys_notification",tx)!=null)continue;
            Notifications.RaiseFor(c,tx,[user],key,"تذكير رد على إشعار",n.S("titleAr"),"system","notify.reminder",n.S("id"),true,n.S("companyId")==""?null:n.S("companyId"));
        }
    }

    /// <summary>Idempotent tick used by the hosted timer (and tests).</summary>
    public static void ProcessReminders(DbConnection c,DbTransaction tx)
    {
        EscalateOverdue(c,tx);
        RemindUnanswered(c,tx);
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/admin/notify-settings",(HttpContext ctx)=>store.Read(c=>GetSettings(c,ctx.User)))
            .WithTags("Administration").WithSummary("Read notification matrix and reply-time defaults");
        api.MapPut("/admin/notify-settings",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>PutSettings(c,tx,ctx.User,input)))
            .WithTags("Administration").WithSummary("Save notification settings; chat never creates notify rows");
        api.MapPost("/notify/send",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Send(c,tx,ctx.User,input)))
            .WithTags("Administration").WithSummary("Send general or custom notification with reply SLA");
        api.MapGet("/tasks",(string? status,HttpContext ctx)=>store.Read(c=>ListTasks(c,ctx.User,status)))
            .WithTags("Administration").WithSummary("List operational tasks in company/assignment scope");
        api.MapPost("/tasks",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>CreateTask(c,tx,ctx.User,input)))
            .WithTags("Administration").WithSummary("Create an operational task with default reply working days");
        api.MapPut("/tasks/{id}",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>UpdateTask(c,tx,ctx.User,id,input)))
            .WithTags("Administration").WithSummary("Update or complete an operational task");
    }
}
