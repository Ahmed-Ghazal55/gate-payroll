using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;

namespace Payroll;

// Track M (BR §25.8): a rider asks for an advance from the app. It goes only to the rider's current supervisor,
// who either converts it into a formal advance request (the §25 cycle, unchanged) or declines it with a reason.
public static class RiderAsks
{
    public const string Kind="rider_ask";
    static readonly string[] Open=["open"];

    static JsonObject RiderView(DbConnection c,JsonObject a)
    {
        var v=new JsonObject{["id"]=a.S("id"),["type"]=a.S("type"),["amount"]=a.N("amount"),["reason"]=a.S("reason"),["status"]=a.S("status"),["createdAt"]=a.S("createdAt"),["decidedAt"]=a.S("decidedAt"),["note"]=a.S("status")=="declined"?a.S("note"):""};
        // After conversion the rider sees the formal request's stage only, never reviewer notes or vault balances.
        if(a.S("advanceRequestId")!=""&&Store.Get(c,a.S("advanceRequestId"),AdvanceRequests.Kind) is {} r){v["advanceStatus"]=r.S("status");v["amount"]=r.N("amount");v["type"]=r.S("type");}
        return v;
    }

    public static JsonObject Ask(DbConnection c,DbTransaction tx,HttpContext ctx,JsonObject input)
    {
        string iqama=MobileSessions.Validate(c,ctx,null,tx).S("iqama");var rider=Store.Get(c,iqama,"rider",tx)!;
        string type=input.Required("type");J.Need(type is "cash" or "bank","نوع السلفة نقدية أو بنكية.");
        decimal amount=Debts.Amount(input,"amount");J.Need(amount>0&&amount<=100000,"مبلغ السلفة غير صالح.");
        string reason=input.S("reason").Trim();J.Need(reason.Length is >2 and <=500,"اكتب سبب الطلب.");
        var supervisors=OrgDirectory.SupervisorsOf(c,tx,iqama);J.Need(supervisors.Count>0,"لا يوجد مشرف مسجل لك حاليًا؛ تواصل مع الإدارة.",409);
        J.Need(!Store.List(c,Kind,tx).Any(a=>a.S("riderIqama")==iqama&&Open.Contains(a.S("status"))),"لديك طلب مفتوح بالفعل.",409);
        J.Need(!Store.List(c,AdvanceRequests.Kind,tx).Any(r=>r.S("riderIqama")==iqama&&r.S("status") is not ("rejected" or "cancelled" or "paid")),"لديك طلب سلفة قيد المراجعة بالفعل.",409);
        var a=new JsonObject{["id"]="ask:"+Guid.NewGuid().ToString("N"),["riderIqama"]=iqama,["riderName"]=rider.S("name"),["type"]=type,["amount"]=amount,["reason"]=reason,["status"]="open",["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["supervisors"]=new JsonArray(supervisors.Select(u=>(JsonNode)u).ToArray())};
        Store.Save(c,Kind,a,tx);Store.Audit(c,tx,"rider_ask.created",a.S("id"),"rider:"+iqama);
        Notifications.RaiseFor(c,tx,supervisors,"ask:"+a.S("id"),"طلب سلفة من مندوب",$"{rider.S("name")} يطلب {amount:0.##} ({(type=="cash"?"نقدي":"بنكي")})","rider:"+iqama,Kind,a.S("id"),true);
        return RiderView(c,a);
    }

    public static JsonObject Cancel(DbConnection c,DbTransaction tx,HttpContext ctx,string id)
    {
        string iqama=MobileSessions.Validate(c,ctx,null,tx).S("iqama");
        var a=Store.Get(c,id,Kind,tx)?.Clone();J.Need(a!=null&&a.S("riderIqama")==iqama,"الطلب غير موجود.",404);
        J.Need(a!.S("status")=="open","لا يمكن إلغاء طلب تم البت فيه.",409);
        a!["status"]="cancelled";a["decidedAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,Kind,a,tx);Store.Audit(c,tx,"rider_ask.cancelled",id,"rider:"+iqama);
        Notifications.CloseTasks(c,tx,Kind,id,"rider:"+iqama);return RiderView(c,a);
    }

    static JsonObject ForSupervisor(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        AccessPolicy.Demand(actor,"advances.request");
        var a=Store.Get(c,id,Kind,tx)?.Clone();
        // Not found and not yours look the same, so ids cannot be probed.
        J.Need(a!=null&&OrgDirectory.Supervises(c,tx,actor.Identity!.Name!,a.S("riderIqama")),"الطلب غير موجود.",404);
        return a!;
    }

    public static JsonObject Convert(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        var a=ForSupervisor(c,tx,actor,id);J.Need(a.S("status")=="open","تم البت في الطلب بالفعل.",409);
        var request=AdvanceRequests.Create(c,tx,actor,new(){["riderIqama"]=a.S("riderIqama"),["type"]=input.S("type",a.S("type")),["amount"]=input["amount"]?.DeepClone()??a.N("amount"),
            ["reason"]=input.S("reason","").Trim() is {Length:>2} r?r:"طلب المندوب من التطبيق: "+a.S("reason")});
        a["status"]="converted";a["advanceRequestId"]=request.S("id");a["decidedBy"]=actor.Identity!.Name;a["decidedAt"]=DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c,Kind,a,tx);Store.Audit(c,tx,"rider_ask.converted",id,actor.Identity!.Name!);Notifications.CloseTasks(c,tx,Kind,id,actor.Identity!.Name!);
        return new(){["ask"]=a,["advanceRequest"]=request};
    }

    public static JsonObject Decline(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        var a=ForSupervisor(c,tx,actor,id);J.Need(a.S("status")=="open","تم البت في الطلب بالفعل.",409);
        string note=input.S("note").Trim();J.Need(note.Length is >2 and <=500,"اكتب سبب الرفض ليظهر للمندوب.");
        a["status"]="declined";a["note"]=note;a["decidedBy"]=actor.Identity!.Name;a["decidedAt"]=DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c,Kind,a,tx);Store.Audit(c,tx,"rider_ask.declined",id,actor.Identity!.Name!);Notifications.CloseTasks(c,tx,Kind,id,actor.Identity!.Name!);
        return a;
    }

    public static void Map(WebApplication app,RouteGroupBuilder api,Store store)
    {
        app.MapGet("/api/rider-mobile/requests",(HttpContext ctx)=>{MobileSessions.Authenticate(store,ctx);return store.Read(c=>{string iqama=MobileSessions.Validate(c,ctx).S("iqama");
            return Store.List(c,Kind).Where(a=>a.S("riderIqama")==iqama).OrderByDescending(a=>a.S("createdAt")).Select(a=>RiderView(c,a)).ToList();});})
            .WithTags("Riders").WithSummary("Read own advance asks and their stage using the rider bearer session");
        app.MapPost("/api/rider-mobile/requests",(JsonObject input,HttpContext ctx)=>{MobileSessions.Authenticate(store,ctx);return store.Write((c,tx)=>Ask(c,tx,ctx,input));})
            .WithTags("Riders").WithSummary("Rider asks the current supervisor for a cash or bank advance");
        app.MapPost("/api/rider-mobile/requests/{id}/cancel",(string id,HttpContext ctx)=>{MobileSessions.Authenticate(store,ctx);return store.Write((c,tx)=>Cancel(c,tx,ctx,id));})
            .WithTags("Riders").WithSummary("Rider cancels an open advance ask");
        api.MapGet("/advance-requests/rider-asks",(string? status,HttpContext ctx)=>store.Read(c=>{AccessPolicy.Demand(ctx.User,"advances.request");string me=ctx.User.Identity!.Name!;
            return Store.List(c,Kind).Where(a=>OrgDirectory.Supervises(c,null,me,a.S("riderIqama"))&&(string.IsNullOrWhiteSpace(status)||a.S("status")==status)).OrderByDescending(a=>a.S("createdAt")).ToList();}))
            .WithTags("Debts and advances").WithSummary("Supervisor lists advance asks from currently supervised riders");
        api.MapPost("/advance-requests/rider-asks/{id}/convert",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Convert(c,tx,ctx.User,id,input)))
            .WithTags("Debts and advances").WithSummary("Supervisor converts a rider ask into a formal advance request");
        api.MapPost("/advance-requests/rider-asks/{id}/decline",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Decline(c,tx,ctx.User,id,input)))
            .WithTags("Debts and advances").WithSummary("Supervisor declines a rider ask with a reason shown to the rider");
    }
}
