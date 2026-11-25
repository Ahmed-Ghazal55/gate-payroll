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
    /// <summary>TRIAL7 X2 / grok-TRIAL6: money-out by posting lines (credit to bank/cash/vault) or recon payment — not only by operation-name substrings.</summary>
    public static bool IsMoneyOutDocument(JsonObject? record,string operation="",string? scope=null)
    {
        if(MoneyOutBlocksApproveOwn(operation,scope))return true;
        if(record==null)return false;
        if(string.Equals(record.S("reconKind"),"payment",StringComparison.OrdinalIgnoreCase))return true;
        var lines=record["lines"] as JsonArray;
        if(lines==null)return false;
        return lines.OfType<JsonObject>().Any(l=>l.N("credit")>0&&MoneyWriteGuards.IsBankOrCashAccountCode(l.S("accountCode")));
    }
    /// <summary>View-time: claim is present (not AccessPolicy.Has — Admin must not inherit approve-own).</summary>
    public static bool HasApproveOwnClaim(ClaimsPrincipal actor)=>actor.HasClaim("permission",ApproveOwnPermission);
    /// <summary>TRIAL9: one shared self-approval gate for every money path — check every maker field
    /// (createdBy / updatedBy / calculatedBy / …). Same person needs approvals.approve_own; never money-out.</summary>
    public static void DemandIndependentOfMakers(ClaimsPrincipal actor,string operation,DbConnection? c,DbTransaction? tx,JsonObject record,string? scope,params string[] makerFields)
    {
        var makers=makerFields
            .Select(f=>record.S(f))
            .Where(m=>!string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        string? self=makers.FirstOrDefault(m=>SamePerson(actor,m));
        if(self==null)return;
        J.Need(Independent(actor,self,operation,c,tx,record,scope:scope),"لا يجوز اعتماد طلبك بنفسك؛ يلزم مراجع مستقل أو صلاحية اعتماد مستنداته بنفسه.",403);
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
        // TRIAL8/9 shared rule (every role, dual and non-dual):
        // 1) approvals.approve_own — never money-out
        // 2) dated approval.bypass_maker_checker grant — money-out only with an explicit Dual* scope grant
        // 3) never automatic SystemAdmin self-path
        bool moneyOut=IsMoneyOutDocument(record,operation,scope);
        if(HasApproveOwnClaim(actor)&&!moneyOut)
        {
            if(tx!=null)
            {
                J.Need(c!=null&&record!=null&&record.S("id")!="","اعتماد المستند الذاتي يحتاج مستندًا وسجل مراجعة.",409);
                record!["selfApprovedException"]=true;record["selfApproved"]=true;
                Store.Save(c!,"audit",new(){["id"]=Guid.NewGuid().ToString("N"),["action"]="approval.approve_own",["operation"]=operation,["entity"]=record.S("id"),["recordId"]=record.S("id"),["maker"]=maker,["actor"]=actor.Identity!.Name,["at"]=DateTimeOffset.UtcNow.ToString("O"),["scope"]=scope??"",["selfApprovedException"]=true,["selfApproved"]=true,["badge"]="approve_own"},tx);
            }
            return true;
        }
        if(moneyOut)
        {
            if(scope is not (ApprovalsArea.DualBankFile or ApprovalsArea.DualPaymentOut or ApprovalsArea.DualSharedRiderRelease))
                return false;
            if(c==null||string.IsNullOrWhiteSpace(scope))return false;
            var dualGrant=ActiveBypassGrant(c,tx,actor.Identity!.Name!,scope);
            if(dualGrant==null)return false;
            if(tx!=null)
            {
                J.Need(record!=null&&record.S("id")!="","استثناء الاعتماد يحتاج مستندًا وسجل مراجعة.",409);
                record!["selfApprovedException"]=true;
                Store.Save(c,"audit",new(){["id"]=Guid.NewGuid().ToString("N"),["action"]="approval.bypass_maker_checker",["operation"]=operation,["entity"]=record.S("id"),["recordId"]=record.S("id"),["maker"]=maker,["actor"]=actor.Identity!.Name,["at"]=DateTimeOffset.UtcNow.ToString("O"),["grantId"]=dualGrant.S("id"),["scope"]=dualGrant.S("scope",scope??""),["reason"]=dualGrant.S("reason"),["approvalBypassUntil"]=dualGrant.S("to"),["selfApprovedException"]=true,["badge"]="bypass_maker_checker"},tx);
            }
            return true;
        }
        // Non-money-out: dated bypass grant (no SystemAdmin default). Null scope = any active grant
        // so ValidateApproval self-checks still honour §35 grants that callers pass via Independent(...,scope:…).
        if(c==null)return false;
        var grant=ActiveBypassGrant(c,tx,actor.Identity!.Name!,scope);
        if(grant==null)return false;
        if(tx!=null)
        {
            J.Need(record!=null&&record.S("id")!="","استثناء الاعتماد يحتاج مستندًا وسجل مراجعة.",409);
            record!["selfApprovedException"]=true;
            Store.Save(c,"audit",new(){["id"]=Guid.NewGuid().ToString("N"),["action"]="approval.bypass_maker_checker",["operation"]=operation,["entity"]=record.S("id"),["recordId"]=record.S("id"),["maker"]=maker,["actor"]=actor.Identity!.Name,["at"]=DateTimeOffset.UtcNow.ToString("O"),["grantId"]=grant.S("id"),["scope"]=grant.S("scope",scope??""),["reason"]=grant.S("reason"),["approvalBypassUntil"]=grant.S("to"),["selfApprovedException"]=true,["badge"]="bypass_maker_checker"},tx);
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
