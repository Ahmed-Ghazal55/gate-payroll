using System.Data.Common;
using System.Text.Json.Nodes;

namespace Payroll;

// Opening balances loaded from the reviewed debt sheet arrive as pending_approval without an
// approval_request (they were not created through POST /debts). This is their decision path:
// admin only, never the loader account, one journal per debt through PostCreationJournalIfNeeded.
public static class DebtOpeningApproval
{
    public const int MaxBatch = 100;

    public static bool IsPendingOpening(JsonObject d) =>
        d.S("entryMode") == "OpeningBalance" && d.S("status") == "pending_approval" && string.IsNullOrWhiteSpace(d.S("approvalRequestId"));

    public static JsonObject Decide(DbConnection c, DbTransaction tx, IReadOnlyCollection<string> ids, string decision, string note, string actor)
    {
        J.Need(decision is "approved" or "rejected", "قرار غير صالح: approved أو rejected.");
        J.Need(ids.Count is > 0 and <= MaxBatch, $"حدد من 1 إلى {MaxBatch} رصيد افتتاحي في الدفعة الواحدة.");
        if (decision == "rejected") J.Need(!string.IsNullOrWhiteSpace(note), "يلزم تدوين سبب الرفض.");
        int done = 0, skipped = 0; decimal total = 0m;
        foreach (var id in ids.Distinct())
        {
            var debt = Store.Get(c, id, "debt", tx);
            if (debt == null || !IsPendingOpening(debt)) { skipped++; continue; }
            var principal=UserAccess.Principal(UserAccess.Find(c,actor,tx)??new JsonObject{["username"]=actor,["role"]="Reviewer"});
            J.Need(AccessPolicy.Independent(principal,debt.S("createdBy"),"debt.opening_decide",c,tx,debt), "لا يعتمد المنشئ رصيده الافتتاحي بنفسه.");
            string stamp = DateTimeOffset.UtcNow.ToString("O");
            if (decision == "approved")
            {
                // The balance is as of the sheet date, so it posts in that month, not the approval month.
                string asOf = debt.S("balanceAsOf");
                if (string.IsNullOrWhiteSpace(debt.S("postingPeriod")) && asOf.Length >= 7) debt["postingPeriod"] = asOf[..7];
                debt["status"] = debt.N("remainingBalance") > 0 ? "active" : "settled";
                debt["approvedBy"] = actor; debt["approvedAt"] = stamp; debt["approvalNote"] = note;
                Store.Save(c, "debt", debt, tx);
                Debts.PostCreationJournalIfNeeded(c, tx, debt, actor);
            }
            else
            {
                debt["status"] = "cancelled"; debt["rejectedBy"] = actor; debt["rejectedAt"] = stamp; debt["rejectionNote"] = note;
                Store.Save(c, "debt", debt, tx);
            }
            Store.Audit(c, tx, decision == "approved" ? "debt.opening_approved" : "debt.opening_rejected", id, actor);
            done++; total = J.Money(total + debt.N("totalAmount"));
        }
        return new JsonObject { ["decision"] = decision, ["processed"] = done, ["skipped"] = skipped, ["total"] = total };
    }

    public static void Map(RouteGroupBuilder api, Store store)
    {
        api.MapPost("/debts/opening/decide", (JsonObject input, HttpContext ctx) =>
        {
            if (!AccessPolicy.Administrator(ctx.User)) throw new ApiError("اعتماد الأرصدة الافتتاحية لمسؤول النظام فقط.", 403);
            var ids = (input["ids"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").Where(s => s != "").ToList() ?? [];
            return store.Write((c, tx) => Decide(c, tx, ids, input.Required("decision"), input.S("note"), ctx.User.Identity!.Name!));
        }).WithTags("Debts and advances").WithSummary("Approve or reject a batch of opening-balance debts");
    }
}
