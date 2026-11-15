using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class Debts
{
    /// <summary>UI buttons from the API only (project rule 8). Uses §35 BypassForView so dated grants appear without auditing a read.</summary>
    public static JsonObject View(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject debt)
    {
        FinancialScopes.DemandDebt(actor,debt);
        var result=debt.Clone();var actions=new JsonArray();var blockers=new JsonArray();
        bool bypass=AccessPolicy.BypassForView(c,tx,actor);
        string status=debt.S("status");
        decimal remaining=debt.N("remainingBalance");
        bool open=(status is "active" or "paused")&&remaining>0;

        if(open&&AccessPolicy.Has(actor,"debts.reschedule"))actions.Add("edit");
        else if(open&&!AccessPolicy.Has(actor,"debts.reschedule"))blockers.Add("صلاحية إعادة جدولة الدين مطلوبة.");

        if(open&&AccessPolicy.Has(actor,"debts.collect"))actions.Add("collect");
        else if(open&&!AccessPolicy.Has(actor,"debts.collect"))blockers.Add("صلاحية تحصيل الدين مطلوبة.");

        if(open&&debt.S("category")!="PlatformTransferredDebt"&&AccessPolicy.Has(actor,"debts.transfer_sponsor"))
            actions.Add("transfer_sponsor");

        if(DebtOpeningApproval.IsPendingOpening(debt))
        {
            if(!AccessPolicy.Administrator(actor))blockers.Add("اعتماد الأرصدة الافتتاحية لمسؤول النظام فقط.");
            else if(!AccessPolicy.IndependentForView(actor,debt.S("createdBy"),bypass))blockers.Add("لا يعتمد المنشئ رصيده الافتتاحي بنفسه.");
            else actions.Add("decide");
        }
        else if(status=="pending_approval"&&debt.S("approvalRequestId")!="")
        {
            if(AccessPolicy.Has(actor,"debts.view")&&AccessPolicy.IndependentForView(actor,debt.S("createdBy"),bypass))actions.Add("decide");
            else if(!AccessPolicy.IndependentForView(actor,debt.S("createdBy"),bypass))blockers.Add("اعتماد الدين لمستخدم مستقل عن المنشئ.");
        }

        result["allowedActions"]=actions;result["approvalBlockers"]=blockers;return result;
    }
}
