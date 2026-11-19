using System.Security.Claims;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class CompanyStatsTests
{
    static ClaimsPrincipal Principal(string username, string role, string[] perms, string[]? sponsors = null, string[]? denied = null)
    {
        var body = new JsonObject
        {
            ["username"] = username,
            ["role"] = role,
            ["customPermissions"] = new JsonArray(perms.Select(p => (JsonNode?)p).ToArray())
        };
        if (denied != null)
            body["deniedPermissions"] = new JsonArray(denied.Select(p => (JsonNode?)p).ToArray());
        if (sponsors != null)
        {
            body["financialScope"] = new JsonObject
            {
                ["sponsorIds"] = new JsonArray(sponsors.Select(s => (JsonNode?)s).ToArray()),
                ["platforms"] = new JsonArray("Jahez", "Keeta", "HungerStation", "Ninja", "Chefz", "Amazon", "Other"),
                ["accountCodes"] = new JsonArray(),
                ["categoryCodes"] = new JsonArray()
            };
        }
        return UserAccess.Principal(body);
    }

    [TestMethod]
    public void PermissionGateEnforcesCompaniesViewOnStatsRoute()
    {
        var viewer = Principal("syn-co-viewer", "Reviewer", ["companies.view"]);
        var noPerm = Principal("syn-co-none", "Reviewer", ["riders.view"]);
        var admin = Principal("syn-co-admin", "Admin", []);

        Assert.IsTrue(AccessPolicy.Allowed(viewer, "GET", "/api/companies/stats"));
        Assert.IsTrue(AccessPolicy.Allowed(admin, "GET", "/api/companies/stats"));
        Assert.IsFalse(AccessPolicy.Allowed(noPerm, "GET", "/api/companies/stats"));
        Assert.IsFalse(AccessPolicy.Allowed(viewer, "POST", "/api/companies/stats"));

        string dir = Path.Combine(Path.GetTempPath(), "gate-co-stats-perm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(dir);
            Companies.Seed(store);
            var ctx = new DefaultHttpContext { User = noPerm };
            var ex = Assert.ThrowsExactly<ApiError>(() => Companies.Stats(store, null, null, ctx));
            Assert.AreEqual(403, ex.Status);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void PreviewSqlAppliesCompanyScopeAndUsesSingleQuery()
    {
        var allScope = new LiveOperationsQuery.Scope(true, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        var (allSql, allParams) = CompanyStatsLiveQuery.PreviewSql(allScope);
        StringAssert.Contains(allSql, "[app].[Sponsors]");
        StringAssert.Contains(allSql, "[app].[Employees]");
        StringAssert.Contains(allSql, "[app].[PlatformRiderAccounts]");
        StringAssert.Contains(allSql, "[platform].[ClientPlatforms]");
        StringAssert.Contains(allSql, "[app].[RiderClientAssignments]");
        Assert.IsFalse(allSql.Contains(';', StringComparison.Ordinal), "must be one SQL statement");
        Assert.IsFalse(allSql.Contains("1=0", StringComparison.Ordinal));
        Assert.AreEqual(0, allParams.Count);
        Assert.DoesNotContain("INSERT ", allSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", allSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", allSql, StringComparison.OrdinalIgnoreCase);

        var scoped = new LiveOperationsQuery.Scope(
            false,
            new[] { "7000000001", "7000000002" },
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());
        var (scopedSql, scopedParams) = CompanyStatsLiveQuery.PreviewSql(scoped);
        StringAssert.Contains(scopedSql, "s.[UnifiedNationalNumber] IN (@cr0,@cr1)");
        CollectionAssert.Contains(scopedParams.ToList(), "@cr0");
        CollectionAssert.Contains(scopedParams.ToList(), "@cr1");
        Assert.DoesNotContain("7000000001", scopedSql, StringComparison.Ordinal);

        var emptyScope = new LiveOperationsQuery.Scope(false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        var (blockedSql, _) = CompanyStatsLiveQuery.PreviewSql(emptyScope);
        StringAssert.Contains(blockedSql, "1=0");

        using var sqlite = new SqliteConnection("Data Source=:memory:");
        sqlite.Open();
        var fail = Assert.ThrowsExactly<ImportFailure>(() => CompanyStatsLiveQuery.Read(sqlite, allScope));
        Assert.AreEqual("live_ops_requires_sql", fail.Code);
    }

    [TestMethod]
    public void StoreStatsCountsAndScopeWithSyntheticData()
    {
        string dir = Path.Combine(Path.GetTempPath(), "gate-co-stats-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(dir);
            Companies.Seed(store);
            string bw = FinancialCore.ResolveSponsor("BW").Id;
            string ex = FinancialCore.ResolveSponsor("EX").Id;

            store.Write((c, tx) =>
            {
                // BW: 2 sponsored riders (1 Active, 1 OnLeave), 1 sponsored admin (Active), 1 external rider (excluded)
                Store.Save(c, "rider", new JsonObject
                {
                    ["id"] = "2000000801", ["iqama"] = "2000000801", ["name"] = "Synthetic Rider One",
                    ["status"] = "Active", ["engagementType"] = "SponsoredInternal", ["isEmployee"] = false,
                    ["sponsorId"] = bw, ["financeSponsorId"] = bw
                }, tx);
                Store.Save(c, "rider", new JsonObject
                {
                    ["id"] = "2000000802", ["iqama"] = "2000000802", ["name"] = "Synthetic Rider Two",
                    ["status"] = "OnLeave", ["engagementType"] = "SponsoredInternal", ["isEmployee"] = false,
                    ["sponsorId"] = bw, ["financeSponsorId"] = bw
                }, tx);
                Store.Save(c, "rider", new JsonObject
                {
                    ["id"] = "2000000803", ["iqama"] = "2000000803", ["name"] = "Synthetic Admin One",
                    ["status"] = "Active", ["engagementType"] = "SponsoredInternal", ["isEmployee"] = true,
                    ["personRole"] = "administrative", ["sponsorId"] = bw, ["financeSponsorId"] = bw
                }, tx);
                Store.Save(c, "rider", new JsonObject
                {
                    ["id"] = "2000000804", ["iqama"] = "2000000804", ["name"] = "Synthetic External",
                    ["status"] = "Active", ["engagementType"] = "External", ["isEmployee"] = false,
                    ["sponsorId"] = bw, ["financeSponsorId"] = bw
                }, tx);

                // EX: 1 sponsored rider (Active)
                Store.Save(c, "rider", new JsonObject
                {
                    ["id"] = "2000000805", ["iqama"] = "2000000805", ["name"] = "Synthetic EX Rider",
                    ["status"] = "Active", ["engagementType"] = "SponsoredInternal", ["isEmployee"] = false,
                    ["sponsorId"] = ex, ["financeSponsorId"] = ex
                }, tx);

                // Platform accounts: 2 for BW (JAHEZ assigned, KEETA available), 2 for EX (HUNGER assigned, NINJA suspended)
                Store.Save(c, "platform_account", new JsonObject
                {
                    ["id"] = "syn-pa-1", ["companyId"] = bw, ["platformCode"] = "JAHEZ", ["status"] = "Assigned",
                    ["assignments"] = new JsonArray(new JsonObject { ["riderId"] = "2000000801", ["EffectiveTo"] = "" })
                }, tx);
                Store.Save(c, "platform_account", new JsonObject
                {
                    ["id"] = "syn-pa-2", ["companyId"] = bw, ["platformCode"] = "KEETA", ["status"] = "Available",
                    ["assignments"] = new JsonArray()
                }, tx);
                Store.Save(c, "platform_account", new JsonObject
                {
                    ["id"] = "syn-pa-3", ["companyId"] = ex, ["platformCode"] = "HUNGER", ["status"] = "2",
                    ["assignments"] = new JsonArray(new JsonObject { ["riderId"] = "2000000805", ["EffectiveTo"] = "" })
                }, tx);
                Store.Save(c, "platform_account", new JsonObject
                {
                    ["id"] = "syn-pa-4", ["companyId"] = ex, ["platformCode"] = "NINJA", ["status"] = "Suspended",
                    ["assignments"] = new JsonArray(new JsonObject { ["riderId"] = "2000000805", ["EffectiveTo"] = "2026-10-01" })
                }, tx);
                return true;
            });

            var admin = Principal("syn-admin", "Admin", []);
            var adminStats = store.Read(c => Companies.StoreStats(c, admin));
            Assert.AreEqual(Operations.SourceStore, adminStats.S("source"));
            Assert.IsFalse(adminStats.B("balancesAvailable"));
            Assert.AreEqual("الأرصدة بعد ربط المحاسبة", adminStats.S("balancesNote"));

            var adminItems = adminStats["items"]!.AsArray().OfType<JsonObject>().ToList();
            Assert.AreEqual(3, adminItems.Count);
            var bwRow = adminItems.Single(x => x.S("code") == "BW");
            Assert.AreEqual(3m, bwRow["sponsorships"]!.N("total"));
            Assert.AreEqual(2m, bwRow["sponsorships"]!.N("riders"));
            Assert.AreEqual(1m, bwRow["sponsorships"]!.N("admins"));
            Assert.AreEqual(2m, bwRow["sponsorships"]!.N("active"));
            Assert.AreEqual(1m, bwRow["sponsorships"]!.N("onLeave"));
            Assert.AreEqual(2m, bwRow["platformAccounts"]!.N("total"));
            Assert.AreEqual(2m, bwRow["platformAccounts"]!.N("active"));
            Assert.AreEqual(1m, bwRow["platformAccounts"]!.N("assigned"));

            var exRow = adminItems.Single(x => x.S("code") == "EX");
            Assert.AreEqual(1m, exRow["sponsorships"]!.N("total"));
            Assert.AreEqual(2m, exRow["platformAccounts"]!.N("total"));
            Assert.AreEqual(1m, exRow["platformAccounts"]!.N("active"));
            Assert.AreEqual(1m, exRow["platformAccounts"]!.N("assigned"));

            var totals = adminStats["totals"]!.AsObject();
            Assert.AreEqual(3m, totals.N("companiesCount"));
            Assert.AreEqual(4m, totals.N("sponsoredTotal"));
            Assert.AreEqual(3m, totals.N("sponsoredRiders"));
            Assert.AreEqual(1m, totals.N("sponsoredAdmins"));
            Assert.AreEqual(4m, totals.N("platformAccountsTotal"));
            Assert.AreEqual(3m, totals.N("platformAccountsActive"));
            Assert.AreEqual(2m, totals.N("platformAccountsAssigned"));

            var adminActions = adminStats["allowedActions"]!.AsArray().Select(n => n!.ToString()).ToList();
            CollectionAssert.Contains(adminActions, "view");
            CollectionAssert.Contains(adminActions, "manage");
            CollectionAssert.Contains(adminActions, "filter_workforce");
            CollectionAssert.Contains(adminActions, "export");

            // Scoped user (BW only, companies.view only — riders.view denied, no companies.manage)
            var bwOnly = Principal("syn-bw-only", "Reviewer", ["companies.view"], sponsors: [bw], denied: ["riders.view"]);
            var scopedStats = store.Read(c => Companies.StoreStats(c, bwOnly));
            var scopedItems = scopedStats["items"]!.AsArray().OfType<JsonObject>().ToList();
            Assert.AreEqual(1, scopedItems.Count);
            Assert.AreEqual("BW", scopedItems[0].S("code"));
            Assert.AreEqual(3m, scopedStats["totals"]!.N("sponsoredTotal"));
            Assert.AreEqual(2m, scopedStats["totals"]!.N("platformAccountsTotal"));
            var scopedActions = scopedStats["allowedActions"]!.AsArray().Select(n => n!.ToString()).ToList();
            CollectionAssert.Contains(scopedActions, "view");
            CollectionAssert.DoesNotContain(scopedActions, "manage");
            CollectionAssert.DoesNotContain(scopedActions, "filter_workforce");
            CollectionAssert.DoesNotContain(scopedActions, "export");

            // Empty sponsor scope -> 0 companies, 0 totals
            var emptyUser = Principal("syn-empty", "Reviewer", ["companies.view"], sponsors: []);
            var emptyStats = store.Read(c => Companies.StoreStats(c, emptyUser));
            Assert.AreEqual(0, emptyStats["items"]!.AsArray().Count);
            Assert.AreEqual(0m, emptyStats["totals"]!.N("sponsoredTotal"));
            Assert.AreEqual(0m, emptyStats["totals"]!.N("platformAccountsTotal"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void BuildStatsPayloadMergesLiveErpRowsByUnifiedNumber()
    {
        var companies = new List<JsonObject>
        {
            new() { ["id"] = "syn-c1", ["code"] = "C1", ["nameAr"] = "شركة تجريبية أولى", ["shortNameAr"] = "الأولى", ["unifiedNumber"] = "7000000011", ["commercialReg"] = "4000000011", ["status"] = "active" },
            new() { ["id"] = "syn-c2", ["code"] = "C2", ["nameAr"] = "شركة تجريبية ثانية", ["shortNameAr"] = "الثانية", ["unifiedNumber"] = "7000000022", ["commercialReg"] = "4000000022", ["status"] = "active" }
        };
        var erpRows = new List<JsonObject>
        {
            new()
            {
                ["unifiedNumber"] = "7000000011",
                ["sponsoredTotal"] = 12L, ["sponsoredRiders"] = 10L, ["sponsoredAdmins"] = 2L,
                ["sponsoredActive"] = 11L, ["sponsoredOnLeave"] = 1L,
                ["platformAccountsTotal"] = 8L, ["platformAccountsActive"] = 7L, ["platformAccountsAssigned"] = 6L,
                ["byPlatform"] = new JsonArray(
                    new JsonObject { ["code"] = "JAHEZ", ["nameAr"] = "Jahez", ["total"] = 5L, ["active"] = 5L, ["assigned"] = 4L },
                    new JsonObject { ["code"] = "CHEFZ", ["nameAr"] = "Chefz", ["total"] = 3L, ["active"] = 2L, ["assigned"] = 2L }
                )
            }
        };

        var admin = Principal("syn-merge-admin", "Admin", []);
        var payload = Companies.BuildStatsPayload(companies, erpRows, Operations.SourceLive, DateTimeOffset.UtcNow, admin);
        Assert.AreEqual(Operations.SourceLive, payload.S("source"));
        var items = payload["items"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.AreEqual(2, items.Count);
        Assert.IsTrue(items[0].B("erpLinked"));
        Assert.IsFalse(items[1].B("erpLinked"));
        Assert.AreEqual(12m, items[0]["sponsorships"]!.N("total"));
        Assert.AreEqual(0m, items[1]["sponsorships"]!.N("total"));

        var platforms = items[0]["platformAccounts"]!["byPlatform"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.AreEqual("JAHEZ", platforms[0].S("code"));
        Assert.AreEqual("جاهز", platforms[0].S("nameAr"));
        Assert.AreEqual("SHIFTZ", platforms[1].S("code"));
        Assert.AreEqual("ذا شفز", platforms[1].S("nameAr"));
    }

    [TestMethod]
    public void WorkforceParseRequestAcceptsCompanyAlias()
    {
        var req = WorkforceLiveQuery.ParseRequest(new Dictionary<string, string>
        {
            ["company"] = "7000000011",
            ["platform"] = "JAHEZ"
        });
        Assert.AreEqual("7000000011", req.SponsorUnified);
        Assert.AreEqual("JAHEZ", req.Platform);
    }
}
