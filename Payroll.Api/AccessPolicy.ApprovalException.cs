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
    /// <summary>TRIAL6 / Ghazal 11-24: explicit «اعتماد مستنداته بنفسه» — OFF by default, never money-out.</summary>
    public const string ApproveOwnPermission="approvals.approve_own";
    static bool MoneyOutBlocksApproveOwn(string operation,string? scope)
    {
        if(scope is ApprovalsArea.DualBankFile or ApprovalsArea.DualPaymentOut or ApprovalsArea.DualSharedRiderRelease)
            return true;
        string op=operation??"";
        return op.Contains("payout",StringComparison.OrdinalIgnoreCase)
            ||op.Contains("payment_out",StringComparison.OrdinalIgnoreCase)
            ||op.Contains("payment.out",StringComparison.OrdinalIgnoreCase)
            ||op.Contains("bank_file",StringComparison.OrdinalIgnoreCase)
            ||op.Contains("disburse",StringComparison.OrdinalIgnoreCase)
            ||op.Contains("advance.payout",StringComparison.OrdinalIgnoreCase)
            ||op.Contains("debt.payout",StringComparison.OrdinalIgnoreCase)
            ||op.Contains("payment.document.approve",StringComparison.OrdinalIgnoreCase);
    }
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
                   ||a.S("action")=="approval.approve_own"
                   ||(a.S("action")=="approval.self_exception"&&string.Equals(actor.Identity.Name,"ghazal",StringComparison.OrdinalIgnoreCase)
                      &&string.CompareOrdinal(a.S("approvalExceptionUntil"),used.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd"))>=0)));
        }
        // TRIAL6: explicit approve-own — claim only (Has() treats Admin as all-perms; this stays OFF by default).
        if(actor.HasClaim("permission",ApproveOwnPermission)&&!MoneyOutBlocksApproveOwn(operation,scope))
        {
            if(tx!=null)
            {
                J.Need(c!=null&&record!=null&&record.S("id")!="","اعتماد المستند الذاتي يحتاج مستندًا وسجل مراجعة.",409);
                record!["selfApprovedException"]=true;record["selfApproved"]=true;
                Store.Save(c!,"audit",new(){["id"]=Guid.NewGuid().ToString("N"),["action"]="approval.approve_own",["operation"]=operation,["entity"]=record.S("id"),["recordId"]=record.S("id"),["maker"]=maker,["actor"]=actor.Identity!.Name,["at"]=DateTimeOffset.UtcNow.ToString("O"),["scope"]=scope??"",["selfApprovedException"]=true,["selfApproved"]=true,["badge"]="approve_own"},tx);
            }
            return true;
        }
        // BR §35 / WC1H: self-approvals need a dated bypass grant whose scope explicitly matches,
        // or SystemAdmin (M-0667) — every use is audited. Omitting scope is never a wildcard.
        // Money-out duals never accept approve-own or pay_approvals bypass (M-0677).
        if(MoneyOutBlocksApproveOwn(operation,scope)&&scope is ApprovalsArea.DualBankFile or ApprovalsArea.DualPaymentOut or ApprovalsArea.DualSharedRiderRelease)
        {
            // Only an explicit dual-scoped bypass grant (not approve-own) can pass — handled below.
        }
        bool systemAdmin=actor.IsInRole("SystemAdmin");
        if(!systemAdmin&&(string.IsNullOrWhiteSpace(scope)||c==null))return false;
        var grant=c!=null?ActiveBypassGrant(c,tx,actor.Identity!.Name!,scope):null;
        if(!systemAdmin&&grant==null)return false;
        if(tx!=null)
        {
            J.Need(c!=null&&record!=null&&record.S("id")!="","استثناء الاعتماد يحتاج مستندًا وسجل مراجعة.",409);
            record!["selfApprovedException"]=true;
            grant??=scope!=null?ActiveBypassGrant(c!,tx,actor.Identity!.Name!,scope):ActiveBypassGrant(c!,tx,actor.Identity!.Name!);
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
