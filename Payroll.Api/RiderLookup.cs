using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

// Combobox source for every screen where a person types a rider (advance, debt, statement,
// treasury). Covers all riders: sponsored/external, active/inactive, debt-only. Read-only.
public static class RiderLookup
{
    public static bool Allowed(ClaimsPrincipal user) =>
        new[] { "riders.view", "advances.request", "advances.review", "debts.view", "treasury.journals", "treasury.disburse" }.Any(p => AccessPolicy.Has(user, p))
        || AccessPolicy.Administrator(user);

    static readonly Dictionary<string, string> StatusAr = new()
    {
        ["Active"] = "نشط", ["Draft"] = "مسودة", ["Onboarding"] = "تحت التعيين", ["Suspended"] = "موقوف", ["OnLeave"] = "إجازة",
        ["Terminated"] = "منتهي", ["Archived"] = "مؤرشف", ["Fleeing"] = "متغيب", ["Accident"] = "حادث", ["Sick"] = "مرضي",
        ["Inactive"] = "غير نشط", ["NeedsReview"] = "يحتاج مراجعة"
    };

    public static List<JsonObject> Search(IEnumerable<JsonObject> riders, IEnumerable<JsonObject> accounts, ClaimsPrincipal user, string q, int limit)
    {
        string query = q.Trim();
        J.Need(query.Length is >= 2 and <= 100, "اكتب حرفين على الأقل للبحث عن مندوب.");
        bool phones = AccessPolicy.Has(user, "riders.view") || AccessPolicy.Administrator(user);
        // Platform account codes / external ids point back to the rider through the ERP profile id or the iqama.
        var accountKeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in accounts)
        {
            string owner = a.S("registeredRiderId", a.S("riderIqama", a.S("riderId")));
            if (owner == "") continue;
            if (!accountKeys.TryGetValue(owner, out var list)) accountKeys[owner] = list = [];
            list.Add(a.S("code")); list.Add(a.S("externalAccountId"));
        }
        IEnumerable<string> Accounts(JsonObject r)
        {
            var keys = new List<string> { r.S("iqama"), r.S("id") };
            if (r["erpRiderProfileIds"] is JsonArray ids) keys.AddRange(ids.Select(i => i?.ToString() ?? ""));
            return keys.Where(k => k != "" && accountKeys.ContainsKey(k)).SelectMany(k => accountKeys[k]);
        }
        var hits = new List<(int Rank, JsonObject Row)>();
        foreach (var r in riders)
        {
            if (!FinancialScopes.Rider(user, r) && !FinancialScopes.AllCompanies(user)) continue;
            string iqama = r.S("iqama", r.S("id"));
            var acc = Accounts(r).ToArray();
            if (!TextSearch.Matches(query, [iqama, r.S("name"), r.S("nameEn"), phones ? r.S("phone") : "", .. acc])) continue;
            // Exact iqama first, then prefix, then the rest by name.
            int rank = iqama == query ? 0 : iqama.StartsWith(query, StringComparison.Ordinal) ? 1 : 2;
            string status = r.S("status");
            bool sponsored = r.S("engagementType") == "SponsoredInternal";
            var chips = new JsonArray(
                (JsonNode)(sponsored ? "كفالة" : r.S("engagementType") == "External" ? "خارجي" : "علاقة غير محسومة"),
                (JsonNode)StatusAr.GetValueOrDefault(status, status == "" ? "غير محدد" : status));
            if (r.B("debtOnly") || r.S("personRole") == "debt_only") chips.Add((JsonNode)"مديونية فقط");
            var row = new JsonObject
            {
                ["id"] = r.S("id", iqama), ["iqama"] = iqama, ["name"] = r.S("name"), ["status"] = status,
                ["active"] = status == "Active", ["engagementType"] = r.S("engagementType"),
                ["companyId"] = r.S("financeSponsorId", r.S("sponsorId")), ["sponsor"] = r.S("sponsor"), ["city"] = r.S("city"),
                ["chips"] = chips
            };
            if (phones) row["phone"] = r.S("phone");
            hits.Add((rank, row));
        }
        return hits.OrderBy(h => h.Rank).ThenBy(h => h.Row.S("name"), StringComparer.Ordinal).Take(Math.Clamp(limit, 1, 50)).Select(h => h.Row).ToList();
    }

    public static void Map(RouteGroupBuilder api, Store store)
    {
        api.MapGet("/riders/lookup", (string q, int? limit, HttpContext ctx) =>
        {
            if (!Allowed(ctx.User)) throw new ApiError("البحث عن المناديب غير متاح لهذا المستخدم.", 403);
            return store.Read(c => Search(Store.List(c, "rider"), Store.List(c, "platform_account"), ctx.User, q, limit ?? 20));
        }).WithTags("Riders").WithSummary("Find riders by iqama, name, phone or platform account for pickers");
    }
}
