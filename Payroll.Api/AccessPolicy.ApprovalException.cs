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
    public static bool Independent(ClaimsPrincipal actor,string maker,string operation,DbConnection? c=null,DbTransaction? tx=null,JsonObject? record=null,DateTimeOffset? evidenceAt=null,string? scope=null)
    {
        if(!SamePerson(actor,maker))return true;
        if(evidenceAt is {} approved)
        {
            // Historical evidence: keep dated ghazal rows and new bypass audits valid.
            if(c==null||record?.B("selfApprovedException")!=true)return false;
            return Store.List(c,"audit",tx).Any(a=>
                a.S("recordId")==record.S("id")&&a.S("actor").Equals(actor.Identity!.Name,StringComparison.OrdinalIgnoreCase)
                &&DateTimeOffset.TryParse(a.S("at"),out var used)&&used<=approved&&used>=approved.AddMinutes(-5)
                &&(a.S("action")=="approval.bypass_maker_checker"
                   ||(a.S("action")=="approval.self_exception"&&string.Equals(actor.Identity.Name,"ghazal",StringComparison.OrdinalIgnoreCase)
                      &&string.CompareOrdinal(a.S("approvalExceptionUntil"),used.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd"))>=0)));
        }
// BR §35 / WC1H + M-0667: SystemAdmin default OR a dated grant whose scope explicitly matches.
        // Omitting scope is never a wildcard for non-admin — Jahez and other callers must pass the dual/operation scope.
        bool systemAdmin=actor.IsInRole("SystemAdmin");
        JsonObject? grant=null;
        if(!systemAdmin)
        {
            if(string.IsNullOrWhiteSpace(scope)||c==null)return false;
            grant=ActiveBypassGrant(c,tx,actor.Identity!.Name!,scope);
            if(grant==null)return false;
        }
        if(tx!=null)
        {
            J.Need(c!=null&&record!=null&&record.S("id")!="","استثناء الاعتماد يحتاج مستندًا وسجل مراجعة.",409);
            record!["selfApprovedException"]=true;
            grant??=string.IsNullOrWhiteSpace(scope)?ActiveBypassGrant(c!,tx,actor.Identity!.Name!):ActiveBypassGrant(c!,tx,actor.Identity!.Name!,scope);
            string reason=systemAdmin?"system-admin-default":(grant?.S("reason")??actor.FindFirstValue("approvalBypassReason")??"");
            string badge=systemAdmin?"system_admin":"bypass_maker_checker";
            Store.Save(c!,"audit",new(){["id"]=Guid.NewGuid().ToString("N"),["action"]="approval.bypass_maker_checker",["operation"]=operation,["entity"]=record.S("id"),["recordId"]=record.S("id"),["maker"]=maker,["actor"]=actor.Identity!.Name,["at"]=DateTimeOffset.UtcNow.ToString("O"),["grantId"]=grant?.S("id")??"",["scope"]=grant?.S("scope",scope??"")??(scope??""),["reason"]=reason,["approvalBypassUntil"]=grant?.S("to")??actor.FindFirstValue("approvalBypassUntil")??"",["selfApprovedException"]=true,["badge"]=badge},tx);
        }
        return true;
    }
    public static void ConfigureApprovalException(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject user,JsonObject input)
    {
        if(!input.ContainsKey("approvalExceptionUntil")&&!input.ContainsKey("approvalExceptionReason"))return;
        // BR §35: dated exception was replaced by approval.bypass_maker_checker grants; clearing leftover fields is still allowed.
        J.Need(Administrator(actor),"استثناء الاعتماد لمسؤول النظام فقط.",403);
        J.Need(UserAccess.Normalize(user.S("username"))=="GHAZAL","الاستثناء المؤقت لحساب ghazal فقط.");
        string raw=input.S("approvalExceptionUntil"),reason=input.S("approvalExceptionReason").Trim();
        if(raw!="")throw new ApiError("استثناء الاعتماد المؤرخ أُلغي؛ استخدم منح تجاوز المراجعة الثنائية (§35).",409);
        J.Need(reason=="","تاريخ نهاية الاستثناء مطلوب مع السبب.");
        user["approvalExceptionUntil"]="";user["approvalExceptionReason"]="";
        Store.Audit(c,tx,"admin.approval_exception_cleared",user.S("id"),actor.Identity!.Name!);
    }
}
