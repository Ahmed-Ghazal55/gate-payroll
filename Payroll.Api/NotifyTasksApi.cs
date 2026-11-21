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
        view["allowedActions"]=new JsonArray("save_notify_settings");
        view["chatCreatesNotify"]=false;
        return view;
    }

    public static JsonObject PutSettings(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,SettingsPerm);
        int days=input.ContainsKey("replyWorkingDays")?(int)input.N("replyWorkingDays"):DefaultReplyWorkingDays;
        J.Need(days is >=1 and <=30,"مهلة الرد من 1 إلى 30 يوم عمل.",400);
        var row=new JsonObject
        {
            ["id"]="notify-settings:global",
            ["replyWorkingDays"]=days,
            ["matrix"]=input["matrix"]?.DeepClone()??new JsonArray(),
            ["channels"]=input["channels"]?.DeepClone()??new JsonArray("in_app","push"),
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
        // Chat posts never call this path (G4) — explicit guard if a client marks chat source.
        J.Need(!string.Equals(input.S("source"),"chat",StringComparison.OrdinalIgnoreCase),"رسائل المحادثة لا تُنشئ إشعارات.",400);
        int replyDays=ReplyWorkingDays(c,tx);
        DateTimeOffset due=AddWorkingDays(DateTimeOffset.UtcNow,replyDays);
        var targets=custom
            ?(input["userIds"] as JsonArray??[]).Select(u=>u?.ToString()??"").Where(u=>u!="").Distinct().ToList()
            :Store.List(c,"user",tx).Where(u=>UserAccess.Status(u)=="active").Select(u=>u.S("username")).ToList();
        J.Need(targets.Count>0,"لا مستلمين للإشعار.",400);
        string eventId="notify-send:"+Guid.NewGuid().ToString("N");
        Notifications.RaiseFor(c,tx,targets,eventId,title,body,actor.Identity!.Name!,"notify.send",eventId,requiresAction:input.B("requiresAction"),companyId:input.S("companyId")==""?null:input.S("companyId"));
        foreach(var n in Store.List(c,"sys_notification",tx).Where(n=>n.S("eventKey")==eventId||n.S("id").Contains(eventId,StringComparison.Ordinal)))
        {
            n["replyDueAt"]=due.ToString("O");
            n["replyWorkingDays"]=replyDays;
            Store.Save(c,"sys_notification",n,tx);
        }
        Store.Audit(c,tx,custom?"notify.send.custom":"notify.send.general",eventId,actor.Identity!.Name!);
        return new(){["id"]=eventId,["recipients"]=targets.Count,["replyDueAt"]=due.ToString("O"),["replyWorkingDays"]=replyDays,["allowedActions"]=new JsonArray()};
    }

    static JsonObject TaskView(ClaimsPrincipal actor,JsonObject t)
    {
        var actions=new JsonArray();
        if(AccessPolicy.Has(actor,ManagePerm)||AccessPolicy.Has(actor,AssignPerm))
        {
            if(t.S("status") is "open" or "escalated"){actions.Add((JsonNode)"edit");actions.Add((JsonNode)"complete");}
            if(t.S("status")=="open")actions.Add((JsonNode)"escalate");
        }
        var copy=t.DeepClone().AsObject();copy["allowedActions"]=actions;return copy;
    }

    public static JsonObject ListTasks(DbConnection c,ClaimsPrincipal actor,string? status)
    {
        if(!AccessPolicy.Has(actor,AssignPerm)&&!AccessPolicy.Has(actor,ManagePerm))AccessPolicy.Demand(actor,AssignPerm);
        IEnumerable<JsonObject> rows=Store.List(c,TaskKind).Select(t=>TaskView(actor,t));
        if(!string.IsNullOrWhiteSpace(status))rows=rows.Where(t=>t.S("status")==status);
        return new(){["items"]=new JsonArray(rows.OrderByDescending(t=>t.S("createdAt")).Select(t=>(JsonNode?)t).ToArray()),["allowedActions"]=AccessPolicy.Has(actor,AssignPerm)?new JsonArray("create"):new JsonArray()};
    }

    public static JsonObject CreateTask(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,AssignPerm);
        EscalateOverdue(c,tx);
        string title=input.Required("titleAr");
        J.Need(title.Length is >=1 and <=200,"عنوان المهمة مطلوب.",400);
        int replyDays=ReplyWorkingDays(c,tx);
        DateTimeOffset due=input.S("dueAt")!=""&&DateTimeOffset.TryParse(input.S("dueAt"),out var parsed)
            ?parsed:AddWorkingDays(DateTimeOffset.UtcNow,replyDays);
        var task=new JsonObject
        {
            ["id"]=SchemaSql.GuidText(SchemaSql.NewId()),
            ["titleAr"]=title,
            ["bodyAr"]=input.S("bodyAr"),
            ["assignee"]=input.S("assignee",input.S("assignedTo")),
            ["rank"]=input.S("rank"),
            ["status"]="open",
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
            Notifications.RaiseFor(c,tx,[task.S("assignee")],task.S("id"),"مهمة جديدة: "+title,task.S("bodyAr"),actor.Identity!.Name!,"task",task.S("id"),true);
        return TaskView(actor,task);
    }

    public static JsonObject UpdateTask(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        if(!AccessPolicy.Has(actor,ManagePerm)&&!AccessPolicy.Has(actor,AssignPerm))AccessPolicy.Demand(actor,ManagePerm);
        var task=Store.Get(c,id,TaskKind,tx)??throw new ApiError("المهمة غير موجودة.",404);
        if(input.ContainsKey("titleAr"))task["titleAr"]=input.Required("titleAr");
        if(input.ContainsKey("bodyAr"))task["bodyAr"]=input.S("bodyAr");
        if(input.ContainsKey("assignee")||input.ContainsKey("assignedTo"))task["assignee"]=input.S("assignee",input.S("assignedTo"));
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
        task["updatedBy"]=actor.Identity!.Name;
        task["updatedAt"]=DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c,TaskKind,task,tx);
        Store.Audit(c,tx,"task.update",id,actor.Identity!.Name!);
        return TaskView(actor,task);
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
            // Escalation reminds — never auto-completes.
            Store.Save(c,TaskKind,task,tx);
            if(task.S("assignee")!="")
                Notifications.RaiseFor(c,tx,[task.S("assignee")],task.S("id")+":reminder","تذكير مهمة متأخرة",task.S("titleAr"),"system","task.reminder",task.S("id"),true);
        }
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
            .WithTags("Administration").WithSummary("List operational tasks");
        api.MapPost("/tasks",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>CreateTask(c,tx,ctx.User,input)))
            .WithTags("Administration").WithSummary("Create an operational task with default reply working days");
        api.MapPut("/tasks/{id}",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>UpdateTask(c,tx,ctx.User,id,input)))
            .WithTags("Administration").WithSummary("Update or complete an operational task");
    }
}
