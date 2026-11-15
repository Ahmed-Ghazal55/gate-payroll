using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PayCycles
{
    /// <summary>§40 UI pipeline: per rider stages 1–6 with stage-4 items and company expense lines that do not hit net.</summary>
    public static JsonObject Pipeline(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        var cycle=Store.Get(c,id,Kind,tx)??throw new ApiError("الدورة غير موجودة.",404);Scope(actor,cycle,"pay.view");
        var riders=new JsonArray();
        var allLines=Lines(c,tx,id); // one read for the whole cycle, not one per rider
        foreach(var summary in Rows(cycle["riders"]).OfType<JsonObject>())
        {
            string rider=summary.S("rider");
            var person=Store.Get(c,rider,"rider",tx);if(person!=null)FinancialScopes.DemandRider(actor,person);
            var lines=allLines.Where(l=>l.S("rider")==rider).ToList();
            var deductions=Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("rider")==rider).ToList();
            var stage4=new JsonArray(deductions.Where(d=>d.S("type")=="stage4").Select(d=>(JsonNode)new JsonObject
            {
                ["key"]=DeductionKey(d),["category"]=d.S("category",d.S("debtCategory")),["source"]=d.S("source"),
                ["labelAr"]=d.S("labelAr"),["amount"]=d.N("amount"),["requestedAmount"]=d.N("requestedAmount"),
                ["phase"]=d.N("phase"),["accountCode"]=d.S("accountCode")
            }).ToArray());
            var expenses=new JsonArray(deductions.Where(d=>d.S("type")=="company_expense"||d.ContainsKey("affectsNet")&&!d.B("affectsNet")).Select(d=>(JsonNode)new JsonObject
            {
                ["key"]=DeductionKey(d),["category"]=d.S("category"),["source"]=d.S("source"),
                ["labelAr"]=d.S("labelAr"),["companyCost"]=d.N("companyCost",-d.N("amount")),["accountCode"]=d.S("accountCode"),["affectsNet"]=false
            }).ToArray());
            // Stage 2: the rider's share of the accounts' earnings (base + incentives), and the platform penalties allocated to them.
            decimal accountShare=J.Money(lines.SelectMany(l=>Rows(l["sources"]).OfType<JsonObject>()).Sum(src=>src.N("baseGross")+src.N("incentive")));
            decimal penalties=J.Money(lines.Sum(l=>l.N("platformPenalties")));
            // Stage 6: net before the old-debt instalment. After stage-8 confirmation the row's "net" holds the remainder, so read salaryNet.
            bool confirmed=summary.S("debtDeductionStatus")=="confirmed";
            decimal net=summary.ContainsKey("salaryNet")?summary.N("salaryNet"):summary.N("net");
            decimal oldDebt=J.Money(deductions.Where(d=>d.S("type")=="debt").Sum(d=>Math.Abs(confirmed?d.N("amount"):d.N("requestedAmount",d.N("amount")))));
            // Remainder to transfer: what stage 8 left when confirmed; otherwise positive net minus the requested instalment (never below zero).
            decimal remainder=confirmed?summary.N("net"):J.Money(Math.Max(0,Math.Max(0,net)-oldDebt));
            riders.Add(new JsonObject
            {
                ["rider"]=rider,
                ["name"]=person?.S("name")??"",
                ["stages"]=new JsonObject
                {
                    ["1_gross"]=summary.N("gross"),
                    ["2_accountShare"]=accountShare,
                    ["2_penaltiesAllocated"]=penalties,
                    ["3_commission"]=summary.N("commission"),
                    ["3_afterCommission"]=summary.N("afterCommission"),
                    ["4_items"]=stage4,
                    ["4_expenseLines"]=expenses,
                    ["5_phase1"]=summary.N("phase1"),
                    ["5_preliminaryDue"]=summary.N("preliminaryDue"),
                    ["5_phase2"]=summary.N("phase2"),
                    ["5_phase3"]=summary.N("phase3"),
                    ["6_net"]=net,
                    ["8_oldDebtInstallment"]=oldDebt,
                    ["8_remainder"]=remainder,
                    ["8_confirmed"]=confirmed
                },
                ["lines"]=new JsonArray(lines.Select(l=>(JsonNode)l.Clone()).ToArray())
            });
        }
        return new()
        {
            ["cycleId"]=id,["period"]=cycle.S("period"),["companyId"]=cycle.S("companyId"),
            ["stage"]=cycle.N("stage"),["status"]=cycle.S("status"),["riders"]=riders
        };
    }
}
