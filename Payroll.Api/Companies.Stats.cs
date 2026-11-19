using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using ErpImport;
namespace Payroll;

public static partial class Companies
{
    public const string BalancesNoteAr = "الأرصدة بعد ربط المحاسبة";

    static readonly string[] CanonicalPlatformOrder = ["KEETA", "HUNGER", "NINJA", "JAHEZ", "SHIFTZ", "AMAZON"];

    public static JsonObject Stats(Store store, IConfiguration? config, IHostEnvironment? env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "companies.view");
        if (config == null || env == null || Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c => StoreStats(c, ctx.User));

        var (companies, scope) = store.Read(c =>
        {
            var list = ActiveCompanies(c, ctx.User);
            return (list, list.Count == 0 ? null : LiveCompanyScope(c, ctx.User, list));
        });
        if (companies.Count == 0 || scope == null)
            return BuildStatsPayload(companies, Array.Empty<JsonObject>(), Operations.SourceLive, DateTimeOffset.UtcNow, ctx.User);

        return Operations.LiveRead(ctx, token =>
        {
            using var erp = Operations.OpenErp(config);
            var live = CompanyStatsLiveQuery.Read(erp, scope, token);
            return BuildStatsPayload(companies, live.Rows, Operations.SourceLive, live.AsOf, ctx.User);
        }, "company-stats");
    }

    public static JsonObject StoreStats(DbConnection c, ClaimsPrincipal user)
    {
        var companies = ActiveCompanies(c, user);
        var byId = companies.ToDictionary(x => x.S("id"), StringComparer.Ordinal);
        var byCode = companies.Where(x => x.S("code") != "").GroupBy(x => x.S("code"), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        string ResolveCompanyId(string sponsorId, string sponsorCode = "")
        {
            if (sponsorId != "" && byId.ContainsKey(sponsorId)) return sponsorId;
            if (sponsorCode != "" && byCode.TryGetValue(sponsorCode, out var byC)) return byC.S("id");
            var def = FinancialCore.FindSponsor(sponsorId != "" ? sponsorId : sponsorCode);
            if (def != null && byId.ContainsKey(def.Id)) return def.Id;
            return "";
        }

        var riders = Store.List(c, "rider");
        var accounts = Store.List(c, "platform_account");
        var syntheticErpRows = new List<JsonObject>(companies.Count);

        foreach (var comp in companies)
        {
            string cid = comp.S("id");
            var compRiders = riders.Where(r =>
                r.S("engagementType") == "SponsoredInternal"
                && ResolveCompanyId(r.S("financeSponsorId", r.S("sponsorId")), r.S("sponsor")) == cid).ToList();

            long sponsoredTotal = compRiders.Count;
            long sponsoredAdmins = compRiders.Count(r => r.B("isEmployee") || r.S("personRole") == "administrative");
            long sponsoredRiders = sponsoredTotal - sponsoredAdmins;
            long sponsoredActive = compRiders.Count(r => string.Equals(r.S("status"), "Active", StringComparison.OrdinalIgnoreCase));
            long sponsoredOnLeave = compRiders.Count(r => string.Equals(r.S("status"), "OnLeave", StringComparison.OrdinalIgnoreCase));

            var compAccounts = accounts.Where(a =>
                ResolveCompanyId(a.S("companyId", a.S("sponsorId"))) == cid
                && FinancialScopes.Platform(user, PayPlatforms.For(a.S("platformCode", a.S("platform"))).Platform)).ToList();

            var platformGroups = new Dictionary<string, (long Total, long Active, long Assigned)>(StringComparer.OrdinalIgnoreCase);
            long accTotal = 0, accActive = 0, accAssigned = 0;

            foreach (var acc in compAccounts)
            {
                string rawCode = NormalizePlatformCode(acc.S("platformCode", acc.S("platform")));
                if (rawCode == "") rawCode = "OTHER";
                string st = acc.S("status", acc.S("statusCode"));
                bool hasOpen = (acc["assignments"] as JsonArray ?? []).OfType<JsonObject>().Any(a => string.IsNullOrWhiteSpace(a.S("EffectiveTo", a.S("effectiveTo"))));
                bool isAssigned = st is "2" or "Assigned" or "assigned" || hasOpen;
                bool isActive = st is "" or "1" or "2" or "Available" or "Assigned" or "active" or "available" or "assigned";

                accTotal++;
                if (isActive) accActive++;
                if (isAssigned) accAssigned++;

                var cur = platformGroups.GetValueOrDefault(rawCode);
                platformGroups[rawCode] = (cur.Total + 1, cur.Active + (isActive ? 1 : 0), cur.Assigned + (isAssigned ? 1 : 0));
            }

            var byPlat = new JsonArray(platformGroups
                .OrderBy(kv => PlatformSortOrder(kv.Key))
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => (JsonNode?)new JsonObject
                {
                    ["code"] = kv.Key,
                    ["nameAr"] = PlatformLabelAr(kv.Key, ""),
                    ["total"] = kv.Value.Total,
                    ["active"] = kv.Value.Active,
                    ["assigned"] = kv.Value.Assigned
                }).ToArray());

            syntheticErpRows.Add(new JsonObject
            {
                ["erpSponsorId"] = cid,
                ["sponsorNameAr"] = comp.S("nameAr"),
                ["unifiedNumber"] = comp.S("unifiedNumber"),
                ["commercialReg"] = comp.S("crNumber", comp.S("commercialReg")),
                ["sponsoredTotal"] = sponsoredTotal,
                ["sponsoredRiders"] = sponsoredRiders,
                ["sponsoredAdmins"] = sponsoredAdmins,
                ["sponsoredActive"] = sponsoredActive,
                ["sponsoredOnLeave"] = sponsoredOnLeave,
                ["platformAccountsTotal"] = accTotal,
                ["platformAccountsActive"] = accActive,
                ["platformAccountsAssigned"] = accAssigned,
                ["byPlatform"] = byPlat
            });
        }

        return BuildStatsPayload(companies, syntheticErpRows, Operations.SourceStore, DateTimeOffset.UtcNow, user);
    }

    public static JsonObject BuildStatsPayload(
        IReadOnlyList<JsonObject> companies,
        IReadOnlyList<JsonObject> erpRows,
        string source,
        DateTimeOffset asOf,
        ClaimsPrincipal user)
    {
        var erpByUnified = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var erpBySponsorId = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in erpRows)
        {
            string u = row.S("unifiedNumber");
            if (u != "" && !erpByUnified.ContainsKey(u)) erpByUnified[u] = row;
            string sid = row.S("erpSponsorId");
            if (sid != "" && !erpBySponsorId.ContainsKey(sid)) erpBySponsorId[sid] = row;
        }

        var items = new JsonArray();
        long totalSponsored = 0, totalRiders = 0, totalAdmins = 0, totalActive = 0, totalOnLeave = 0;
        long totalAccounts = 0, totalAccountsActive = 0, totalAccountsAssigned = 0;
        var globalPlatforms = new Dictionary<string, (string NameAr, long Total, long Active, long Assigned)>(StringComparer.OrdinalIgnoreCase);

        foreach (var comp in companies)
        {
            string unified = comp.S("unifiedNumber");
            string fallbackSponsorId = comp.S("erpSponsorId", comp.S("sponsorId", comp.S("id")));
            JsonObject? erp = null;
            bool linked = (unified != "" && erpByUnified.TryGetValue(unified, out erp))
                || (fallbackSponsorId != "" && erpBySponsorId.TryGetValue(fallbackSponsorId, out erp));

            long sTotal = erp == null ? 0L : Int64(erp, "sponsoredTotal");
            long sRiders = erp == null ? 0L : Int64(erp, "sponsoredRiders");
            long sAdmins = erp == null ? 0L : Int64(erp, "sponsoredAdmins");
            long sActive = erp == null ? 0L : Int64(erp, "sponsoredActive");
            long sOnLeave = erp == null ? 0L : Int64(erp, "sponsoredOnLeave");

            long aTotal = erp == null ? 0L : Int64(erp, "platformAccountsTotal");
            long aActive = erp == null ? 0L : Int64(erp, "platformAccountsActive");
            long aAssigned = erp == null ? 0L : Int64(erp, "platformAccountsAssigned");

            var mergedPlatforms = new Dictionary<string, (string NameAr, long Total, long Active, long Assigned)>(StringComparer.OrdinalIgnoreCase);
            if (erp?["byPlatform"] is JsonArray arr)
            {
                foreach (var node in arr.OfType<JsonObject>())
                {
                    string code = NormalizePlatformCode(node.S("code", node.S("platformCode")));
                    if (code == "") code = "OTHER";
                    string nameAr = PlatformLabelAr(code, node.S("nameAr", node.S("platformNameAr")));
                    long pt = Int64(node, "total");
                    long pa = Int64(node, "active");
                    long pas = Int64(node, "assigned");
                    var cur = mergedPlatforms.GetValueOrDefault(code);
                    mergedPlatforms[code] = (nameAr, cur.Total + pt, cur.Active + pa, cur.Assigned + pas);
                    var gcur = globalPlatforms.GetValueOrDefault(code);
                    globalPlatforms[code] = (nameAr, gcur.Total + pt, gcur.Active + pa, gcur.Assigned + pas);
                }
            }

            var byPlatformOut = new JsonArray(mergedPlatforms
                .OrderBy(kv => PlatformSortOrder(kv.Key))
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => (JsonNode?)new JsonObject
                {
                    ["code"] = kv.Key,
                    ["nameAr"] = kv.Value.NameAr,
                    ["total"] = kv.Value.Total,
                    ["active"] = kv.Value.Active,
                    ["assigned"] = kv.Value.Assigned
                }).ToArray());

            totalSponsored += sTotal;
            totalRiders += sRiders;
            totalAdmins += sAdmins;
            totalActive += sActive;
            totalOnLeave += sOnLeave;
            totalAccounts += aTotal;
            totalAccountsActive += aActive;
            totalAccountsAssigned += aAssigned;

            items.Add(new JsonObject
            {
                ["companyId"] = comp.S("id"),
                ["code"] = comp.S("code"),
                ["nameAr"] = comp.S("nameAr"),
                ["shortNameAr"] = comp.S("shortNameAr", comp.S("nameAr")),
                ["unifiedNumber"] = unified,
                ["commercialReg"] = comp.S("crNumber", comp.S("commercialReg")),
                ["status"] = comp.S("status", "active"),
                ["erpLinked"] = source == Operations.SourceLive && linked,
                ["sponsorships"] = new JsonObject
                {
                    ["total"] = sTotal,
                    ["riders"] = sRiders,
                    ["admins"] = sAdmins,
                    ["active"] = sActive,
                    ["onLeave"] = sOnLeave
                },
                ["platformAccounts"] = new JsonObject
                {
                    ["total"] = aTotal,
                    ["active"] = aActive,
                    ["assigned"] = aAssigned,
                    ["byPlatform"] = byPlatformOut
                }
            });
        }

        var globalByPlatform = new JsonArray(globalPlatforms
            .OrderBy(kv => PlatformSortOrder(kv.Key))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (JsonNode?)new JsonObject
            {
                ["code"] = kv.Key,
                ["nameAr"] = kv.Value.NameAr,
                ["total"] = kv.Value.Total,
                ["active"] = kv.Value.Active,
                ["assigned"] = kv.Value.Assigned
            }).ToArray());

        return new JsonObject
        {
            ["items"] = items,
            ["totals"] = new JsonObject
            {
                ["companiesCount"] = items.Count,
                ["sponsoredTotal"] = totalSponsored,
                ["sponsoredRiders"] = totalRiders,
                ["sponsoredAdmins"] = totalAdmins,
                ["sponsoredActive"] = totalActive,
                ["sponsoredOnLeave"] = totalOnLeave,
                ["platformAccountsTotal"] = totalAccounts,
                ["platformAccountsActive"] = totalAccountsActive,
                ["platformAccountsAssigned"] = totalAccountsAssigned,
                ["byPlatform"] = globalByPlatform
            },
            ["source"] = source,
            ["asOf"] = asOf.ToString("O"),
            ["balancesAvailable"] = false,
            ["balancesNote"] = BalancesNoteAr,
            ["allowedActions"] = StatsActions(user)
        };
    }

    static List<JsonObject> ActiveCompanies(DbConnection c, ClaimsPrincipal user)
        => List(c, user)
            .Where(x => x.S("status") != "archived")
            .OrderBy(x => x.S("code"), StringComparer.Ordinal)
            .ThenBy(x => x.S("nameAr"), StringComparer.Ordinal)
            .ToList();

    static LiveOperationsQuery.Scope LiveCompanyScope(DbConnection c, ClaimsPrincipal user, IReadOnlyList<JsonObject> scopedCompanies)
    {
        var baseScope = Operations.LiveScope(c, user);
        if (baseScope.AllCompanies) return baseScope;
        var unifieds = baseScope.SponsorUnifiedNumbers
            .Concat(scopedCompanies.Select(x => x.S("unifiedNumber")))
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return baseScope with { SponsorUnifiedNumbers = unifieds };
    }

    static JsonArray StatsActions(ClaimsPrincipal user)
    {
        var actions = new JsonArray();
        if (AccessPolicy.Has(user, "companies.view"))
        {
            actions.Add("view");
            actions.Add("export");
        }
        if (AccessPolicy.Has(user, "companies.manage")) actions.Add("manage");
        if (AccessPolicy.Has(user, "riders.view")) actions.Add("filter_workforce");
        return actions;
    }

    static string NormalizePlatformCode(string raw)
    {
        string trimmed = (raw ?? "").Trim().ToUpperInvariant();
        if (trimmed == "") return "";
        return trimmed switch
        {
            "HUNGERSTATION" or "HUNGER_STATION" or "HUNGER-STATION" or "HS" => "HUNGER",
            "CHEFZ" or "THECHEFZ" or "THE_CHEFZ" or "THE-CHEFZ" or "CHEFSZ" => "SHIFTZ",
            _ => trimmed
        };
    }

    static string PlatformLabelAr(string code, string erpNameAr)
    {
        string normalized = NormalizePlatformCode(code);
        return normalized switch
        {
            "KEETA" => "كيتا",
            "HUNGER" => "هنقرستيشن",
            "NINJA" => "نينجا",
            "JAHEZ" => "جاهز",
            "SHIFTZ" => "ذا شفز",
            "AMAZON" => "أمازون",
            "OTHER" => "أخرى",
            _ => HasArabic(erpNameAr) ? erpNameAr.Trim() : "منصة أخرى"
        };
    }

    static bool HasArabic(string text)
        => !string.IsNullOrWhiteSpace(text) && text.Any(ch => ch is >= '\u0600' and <= '\u06FF');

    static int PlatformSortOrder(string code)
    {
        int idx = Array.IndexOf(CanonicalPlatformOrder, NormalizePlatformCode(code));
        return idx >= 0 ? idx : CanonicalPlatformOrder.Length;
    }

    static long Int64(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node == null) return 0L;
        try { return Convert.ToInt64(node.GetValue<decimal>(), CultureInfo.InvariantCulture); }
        catch
        {
            return long.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0L;
        }
    }
}
