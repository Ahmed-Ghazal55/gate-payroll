using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PaySettings
{
    // Base evaluator metrics plus the explicit metrics supported by the Ninja operations adapter.
    public static readonly string[] NinjaMetrics=["orders","days","gross","incentives","attendanceDays","hours8Days","weekendDays","workingHours","activeWorkingHours","committedWorkingHours","shiftCompliancePercent","onTimeAttendancePercent"];
    public static JsonObject Discard(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string kind,string app,string id,JsonObject? input=null,bool delete=false)
    {
        J.Need(Kinds.Contains(kind),"نوع إعداد غير معروف.");bool global=kind=="pay_setting";app=global?"global":PayPlatforms.App(app);Scope(actor,"pay.manage",global?null:app,true);
        var doc=Store.Get(c,id,kind,tx)??throw new ApiError("الإعداد غير موجود.",404);J.Need(doc.S("app")==app,"الإعداد يخص تطبيقًا آخر.");
        J.Need(doc.S("status")=="draft","الرفض والحذف للمسودة فقط؛ النسخة المعتمدة محفوظة.");
        if(!AccessPolicy.Administrator(actor)&&!string.Equals(doc.S("createdBy"),actor.Identity!.Name,StringComparison.OrdinalIgnoreCase))throw new ApiError("المسودة لصاحبها أو مسؤول النظام فقط.",403);
        if(delete)
        {
            J.Need(!Store.List(c,"pay_scheme",tx).Any(s=>s["data"].S("eligibilitySetId")==id),"المسودة مرتبطة بنظام؛ احذف مسودة النظام أولًا.");
            using var command=Store.Command(c,tx,"DELETE FROM records WHERE id=@id AND kind=@kind",("@id",id),("@kind",kind));command.ExecuteNonQuery();
            Store.Audit(c,tx,"pay.config_deleted",id,actor.Identity!.Name!);return new(){["id"]=id,["deleted"]=true};
        }
        PayValues.Fields(input??new(),"reason");string reason=(input??new()).Required("reason");J.Need(reason.Length<=1000,"سبب الرفض أطول من المسموح.");
        doc["status"]="rejected";doc["rejectionReason"]=reason;doc["rejectedBy"]=actor.Identity!.Name;doc["rejectedAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,kind,doc,tx);Store.Audit(c,tx,"pay.config_rejected",id,actor.Identity!.Name!);return doc;
    }
    static void MapLifecycle(RouteGroupBuilder api,Store store)
    {
        foreach(string section in new[]{"terms","schemes","eligibility"})
        {
            string kind=Kind(section);
            api.MapPost("/pay/apps/{app}/"+section+"/{id}/reject",(string app,string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Discard(c,tx,ctx.User,kind,app,id,input))).WithTags("Payroll");
            api.MapDelete("/pay/apps/{app}/"+section+"/{id}",(string app,string id,HttpContext ctx)=>store.Write((c,tx)=>Discard(c,tx,ctx.User,kind,app,id,delete:true))).WithTags("Payroll");
        }
        api.MapPost("/pay/settings/{id}/reject",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Discard(c,tx,ctx.User,"pay_setting","global",id,input))).WithTags("Payroll");
        api.MapDelete("/pay/settings/{id}",(string id,HttpContext ctx)=>store.Write((c,tx)=>Discard(c,tx,ctx.User,"pay_setting","global",id,delete:true))).WithTags("Payroll");
    }
}
