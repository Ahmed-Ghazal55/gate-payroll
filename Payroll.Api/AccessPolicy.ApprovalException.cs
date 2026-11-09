using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class AccessPolicy
{
    static DateOnly Today()=>DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).Date);
    public static bool ApprovalException(ClaimsPrincipal actor)=>actor.Identity?.IsAuthenticated==true&&
        string.Equals(actor.Identity.Name,"ghazal",StringComparison.OrdinalIgnoreCase)&&
        DateOnly.TryParseExact(actor.FindFirstValue("approvalExceptionUntil"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var until)&&until>=Today()&&
        !string.IsNullOrWhiteSpace(actor.FindFirstValue("approvalExceptionReason"));
    // Pure when tx is null (allowedActions). Every committed exception use is audited in
    // the same transaction and stamps the operation's actual stored document.
    public static bool Independent(ClaimsPrincipal actor,string maker,string operation,DbConnection? c=null,DbTransaction? tx=null,JsonObject? record=null,DateTimeOffset? evidenceAt=null)
    {
        if(!string.Equals(actor.Identity?.Name,maker,StringComparison.OrdinalIgnoreCase))return true;
        if(evidenceAt is {} approved)
            return c!=null&&record?.B("selfApprovedException")==true&&string.Equals(actor.Identity?.Name,"ghazal",StringComparison.OrdinalIgnoreCase)&&Store.List(c,"audit",tx).Any(a=>a.S("action")=="approval.self_exception"&&a.S("recordId")==record.S("id")&&a.S("actor").Equals(actor.Identity!.Name,StringComparison.OrdinalIgnoreCase)&&DateTimeOffset.TryParse(a.S("at"),out var used)&&used<=approved&&used>=approved.AddMinutes(-5)&&string.CompareOrdinal(a.S("approvalExceptionUntil"),used.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd"))>=0);
        if(!ApprovalException(actor))return false;
        if(tx!=null)
        {
            J.Need(c!=null&&record!=null&&record.S("id")!="","استثناء الاعتماد يحتاج مستندًا وسجل مراجعة.",409);
            record!["selfApprovedException"]=true;
            Store.Save(c!,"audit",new(){["id"]=Guid.NewGuid().ToString("N"),["action"]="approval.self_exception",["operation"]=operation,["entity"]=record.S("id"),["recordId"]=record.S("id"),["maker"]=maker,["actor"]=actor.Identity!.Name,["at"]=DateTimeOffset.UtcNow.ToString("O"),["approvalExceptionUntil"]=actor.FindFirstValue("approvalExceptionUntil"),["selfApprovedException"]=true},tx);
        }
        return true;
    }
    public static void ConfigureApprovalException(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject user,JsonObject input)
    {
        if(!input.ContainsKey("approvalExceptionUntil")&&!input.ContainsKey("approvalExceptionReason"))return;
        J.Need(Administrator(actor),"استثناء الاعتماد لمسؤول النظام فقط.",403);
        J.Need(UserAccess.Normalize(user.S("username"))=="GHAZAL","الاستثناء المؤقت لحساب ghazal فقط.");
        string raw=input.S("approvalExceptionUntil"),reason=input.S("approvalExceptionReason").Trim();
        if(raw!="")
        {
            J.Need(DateOnly.TryParseExact(raw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var until)&&until>=Today()&&until<=Today().AddDays(120),"حدد نهاية الاستثناء خلال 120 يومًا.");
            J.Need(reason.Length is >=3 and <=500&&!reason.Any(char.IsControl),"سبب الاستثناء مطلوب من 3 إلى 500 حرف.");
        }
        else J.Need(reason=="","تاريخ نهاية الاستثناء مطلوب مع السبب.");
        user["approvalExceptionUntil"]=raw;user["approvalExceptionReason"]=reason;
        Store.Audit(c,tx,"admin.approval_exception_configured",user.S("id"),actor.Identity!.Name!);
    }
}
