using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

public static class Notifications
{
    public static readonly string[] Categories = ["general", "personal", "system", "approvals"];
    static string Identity(ClaimsPrincipal user)=>user.FindFirstValue(ClaimTypes.NameIdentifier)??user.Identity?.Name??"";
    static bool Own(ClaimsPrincipal user,string value)=>value!=""&&(value==Identity(user)||value==user.Identity?.Name);
    public static string Category(JsonObject item)=>item.S("category")!=""?item.S("category"):item.S("assignedTo")!=""?"personal":item.S("tab")=="approvals"?"approvals":"general";
    public static bool Visible(DbConnection c,ClaimsPrincipal user,JsonObject item,DbTransaction? tx=null)
    {
        if(item.S("companyId")!=""&&!FinancialScopes.Sponsor(user,item.S("companyId")))return false;
        if(item.S("platform")!=""&&!FinancialScopes.Platform(user,item.S("platform")))return false;
        string category=Category(item);
        if(category=="personal")return Own(user,item.S("assignedTo"));
        if(!AccessPolicy.Administrator(user)&&item.S("targetDepartment","ALL") is var department&&department!="ALL"&&department!=user.FindFirstValue("department"))return false;
        if(category=="approvals")return AccessPolicy.Has(user,"approvals.view")&&(item.S("approvalId")==""||Store.Get(c,item.S("approvalId"),"approval_request",tx) is {} approval&&FinancialScopes.Approval(user,approval,c,tx));
        if(category=="system")return item.S("requiredPermission")!=""&&AccessPolicy.Has(user,item.S("requiredPermission"));
        return category=="general";
    }
    static string ReadId(ClaimsPrincipal user,string id)=>"notification-read:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Identity(user)+"\n"+id)));
    public static List<JsonObject> Inbox(DbConnection c,ClaimsPrincipal user,DbTransaction? tx=null)
    {
        var read=Store.List(c,"notification_read",tx).Where(r=>r.S("userId")==Identity(user)).Select(r=>r.S("notificationId")).ToHashSet(StringComparer.Ordinal);
        return Store.List(c,"sys_notification",tx).Where(n=>Visible(c,user,n,tx)).Select(n=>
        {
            var view=n.Clone();view["category"]=Category(n);view["unread"]=!read.Contains(n.S("id"));return view;
        }).ToList();
    }
    public static void Mark(DbConnection c,DbTransaction tx,ClaimsPrincipal user,JsonObject item)
    {
        if(!Visible(c,user,item,tx))throw new ApiError("الإشعار غير موجود.",404);
        string id=ReadId(user,item.S("id"));if(Store.Get(c,id,"notification_read",tx)!=null)return;
        Store.Save(c,"notification_read",new(){["id"]=id,["userId"]=Identity(user),["notificationId"]=item.S("id"),["readAt"]=DateTimeOffset.UtcNow.ToString("O")},tx);
    }
    public static void Raise(DbConnection c,DbTransaction tx,string id,string category,string title,string permission,string actor,string? approvalId=null,DateTimeOffset? clock=null,string eventKey="",string? companyId=null,string? platform=null)
    {
        if(Store.Get(c,id,"sys_notification",tx)!=null)return;
        var notification=new JsonObject{["id"]=id,["category"]=category,["tab"]=category,["titleAr"]=title,["bodyAr"]="راجع قائمة العمليات للمزيد.",["requiredPermission"]=permission,["targetDepartment"]="ALL",["approvalId"]=approvalId??"",["status"]="unread",["createdAt"]=(clock??DateTimeOffset.UtcNow).ToString("O"),["eventKey"]=eventKey,["createdBy"]=actor};
        if(companyId!=null)notification["companyId"]=companyId;if(platform!=null)notification["platform"]=platform;
        Store.Save(c,"sys_notification",notification,tx);
        Store.Audit(c,tx,"notification.system_created",id,actor);
    }
    // Invoked inside the original write transaction; the notification cannot outlive a rollback.
    public static void OnAudit(DbConnection c,DbTransaction tx,string action,string entity,string actor,DateTimeOffset? clock=null)
    {
        var now=clock??DateTimeOffset.UtcNow;
        if(action=="submission.received"||action=="builder.response_received")
        {
            var answer=Store.Get(c,entity,action=="submission.received"?"submission":"custom_response",tx);
            if(answer==null)return;
            string campaign=answer.S(action=="submission.received"?"campaignId":"formId");
            Answers(c,tx,campaign,actor,now);
        }
        else if(action=="import.commit"||action.StartsWith("excel.import.",StringComparison.Ordinal)||action is "rider.baseline_imported" or "debt.opening_imported")
        {
            string permission=action=="debt.opening_imported"?"debts.view":action=="rider.baseline_imported"||action=="excel.import.riders"?"riders.view":action=="excel.import.choices"?"forms.view":"imports.view";
            // Audit entities may contain summaries, so use a fresh opaque key rather than embedding them.
            Raise(c,tx,"notice:import:"+Guid.NewGuid().ToString("N"),"system","اكتمل استيراد البيانات.",permission,actor);
        }
    }
    public static void Answers(DbConnection c,DbTransaction tx,string campaign,string actor,DateTimeOffset? clock=null)
    {
        var now=clock??DateTimeOffset.UtcNow;string key="answers:"+campaign;
        if(Store.List(c,"sys_notification",tx).Any(n=>n.S("eventKey")==key&&DateTimeOffset.TryParse(n.S("createdAt"),out var at)&&at>now.AddHours(-1)))return;
        Raise(c,tx,$"notice:answers:{campaign}:{now:yyyyMMddHHmmssfff}","system","وصلت ردود جديدة للنماذج.","forms.view",actor,clock:now,eventKey:key);
    }
    public static void EnsureDataQuality(Store store,ClaimsPrincipal user)
    {
        if(!AccessPolicy.Has(user,"riders.view"))return;
        string id=$"notice:data-quality:{DateTimeOffset.UtcNow:yyyyMMdd}";
        if(store.Read(c=>Store.Get(c,id,"sys_notification")!=null))return;
        store.Write((c,tx)=>
        {
            var riders=Store.List(c,"rider",tx).Where(ErpRiderClassification.ListedAsRider);
            if(riders.Any(r=>r.S("phone")==""||r.S("city")==""||r.S("iban")==""||r.S("sponsor")==""&&r.S("sponsorId")==""&&r.S("financeSponsorId")==""))
                Raise(c,tx,id,"system","بيانات المناديب تحتاج استكمال الجوال أو المدينة أو الشركة أو الآيبان.","riders.view","system");
            return true;
        });
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/notifications",(HttpContext ctx)=>
        {
            EnsureDataQuality(store,ctx.User);
            string category=ctx.Request.Query["category"].ToString();
            J.Need(category==""||Categories.Contains(category),"فئة الإشعار غير صالحة.");
            string unread=ctx.Request.Query["unread"].ToString();J.Need(unread is "" or "true" or "false","مرشح القراءة غير صالح.");
            return store.Read(c=>ApiPagination.Apply(Inbox(c,ctx.User).Where(n=>(category==""||Category(n)==category)&&(unread!="true"||n.B("unread"))),ctx.Request.Query));
        }).WithTags("Home").WithSummary("List visible notifications with per-user read state");
        api.MapGet("/notifications/summary",(HttpContext ctx)=>
        {
            EnsureDataQuality(store,ctx.User);
            return store.Read(c=>Categories.ToDictionary(category=>category,category=>Inbox(c,ctx.User).Count(n=>Category(n)==category&&n.B("unread"))));
        }).WithTags("Home").WithSummary("Unread notification counts per category for the current user");
        api.MapPost("/notifications/{id}/read",(string id,HttpContext ctx)=>store.Write((c,tx)=>
        {
            var item=Store.Get(c,id,"sys_notification",tx)??throw new ApiError("الإشعار غير موجود.",404);Mark(c,tx,ctx.User,item);return new{ok=true};
        })).WithTags("Home").WithSummary("Mark one visible notification read for the current user");
        api.MapPost("/notifications/read-all",(HttpContext ctx)=>store.Write((c,tx)=>
        {
            var items=Inbox(c,ctx.User,tx).Where(n=>n.B("unread")).ToList();foreach(var item in items)Mark(c,tx,ctx.User,item);return new{ok=true,count=items.Count};
        })).WithTags("Home").WithSummary("Mark all visible notifications read for the current user");
    }
}
