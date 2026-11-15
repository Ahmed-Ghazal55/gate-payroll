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
        foreach(var summary in Rows(cycle["riders"]).OfType<JsonObject>())
        {
            string rider=summary.S("rider");
            var person=Store.Get(c,rider,"rider",tx);if(person!=null)FinancialScopes.DemandRider(actor,person);
            var lines=Lines(c,tx,id).Where(l=>l.S("rider")==rider).ToList();
            var deductions=Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("rider")==rider).ToList();
            var stage4=new JsonArray(deductions.Where(d=>d.S("type")=="stage4").Select(d=>(JsonNode)new JsonObject
            {
                ["key"]=DeductionKey(d),["category"]=d.S("category",d.S("debtCategory")),["source"]=d.S("source"),
                ["labelAr"]=d.S("labelAr"),["amount"]=d.N("amount"),["requestedAmount"]=d.N("requestedAmount"),
                ["phase"]=d.N("phase"),["accountCode"]=d.S("accountCode")
            }).ToArray());
            var expenses=new JsonArray(deductions.Where(d=>d.S("type")=="company_expense"||d.B("affectsNet")==false&&d.S("type")!="debt").Select(d=>(JsonNode)new JsonObject
            {
                ["key"]=DeductionKey(d),["category"]=d.S("category"),["source"]=d.S("source"),
                ["labelAr"]=d.S("labelAr"),["companyCost"]=d.N("companyCost",-d.N("amount")),["accountCode"]=d.S("accountCode"),["affectsNet"]=false
            }).ToArray());
            decimal accountShare=J.Money(lines.Sum(l=>l.N("accountDeduction")));
            decimal penalties=accountShare;
            decimal oldDebt=J.Money(deductions.Where(d=>d.S("type")=="debt").Sum(d=>-d.N("requestedAmount",-d.N("amount"))));
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
                    ["6_net"]=summary.N("net"),
                    ["8_oldDebtInstallment"]=oldDebt,
                    ["8_remainder"]=summary.N("closing",summary.N("net"))
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
