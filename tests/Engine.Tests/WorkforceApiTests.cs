using System.Reflection;
using System.Security.Claims;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class WorkforceApiTests
{
    static ClaimsPrincipal Principal(string user, string role, string[] perms, string[]? sponsors = null, string[]? platforms = null)
    {
        var body = new JsonObject
        {
            ["username"] = user,
            ["role"] = role,
            ["customPermissions"] = new JsonArray(perms.Select(p => (JsonNode?)p).ToArray())
        };
        if (sponsors != null || platforms != null)
        {
            body["financialScope"] = new JsonObject
            {
                ["sponsorIds"] = new JsonArray((sponsors ?? []).Select(s => (JsonNode?)s).ToArray()),
                ["platforms"] = new JsonArray((platforms ?? ["Jahez", "Keeta", "HungerStation", "Ninja", "Chefz", "Amazon", "Other"]).Select(p => (JsonNode?)p).ToArray()),
                ["accountCodes"] = new JsonArray(),
                ["categoryCodes"] = new JsonArray()
            };
        }
        return UserAccess.Principal(body);
    }

    static JsonObject InvokeStrip(string name, JsonObject row, ClaimsPrincipal user)
    {
        var method = typeof(WorkforceApi).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new AssertFailedException("missing " + name);
        return (JsonObject)method.Invoke(null, [row, user])!;
    }

    [TestMethod]
    public void CatalogPermissionsAndAccessPolicyRoutes()
    {
        Assert.IsTrue(UserAccess.PermissionsCatalog.Any(p => p.Code == WorkforceApi.PermIban));
        Assert.IsTrue(UserAccess.PermissionsCatalog.Any(p => p.Code == WorkforceApi.PermSaudiSalary));
        Assert.IsTrue(UserAccess.PermissionsCatalog.Any(p => p.Code == WorkforceApi.PermLegalCases));
        Assert.IsTrue(UserAccess.PermissionsCatalog.Any(p => p.Code == WorkforceApi.PermUnifiedNumber));

        var withIban = Principal("synthetic-iban", "Reviewer", ["riders.view", WorkforceApi.PermIban]);
        var plain = Principal("synthetic-plain", "Reviewer", ["riders.view"]);
        Assert.IsTrue(AccessPolicy.Has(withIban, WorkforceApi.PermIban));
        Assert.IsFalse(AccessPolicy.Has(plain, WorkforceApi.PermIban));
        Assert.IsTrue(AccessPolicy.Allowed(plain, "GET", "/api/workforce"));
        Assert.IsTrue(AccessPolicy.Allowed(plain, "GET", "/api/workforce/export"));
        Assert.IsTrue(AccessPolicy.Allowed(plain, "GET", "/api/workforce/saudi-payroll-employees"));
        Assert.IsFalse(AccessPolicy.Allowed(plain, "GET", "/api/workforce/legal-cases"));
        Assert.IsTrue(AccessPolicy.Allowed(
            Principal("synthetic-legal", "Reviewer", [WorkforceApi.PermLegalCases]),
            "GET", "/api/workforce/legal-cases"));
        Assert.IsFalse(AccessPolicy.Allowed(plain, "POST", "/api/riders/enrich-missing"));
    }

    [TestMethod]
    public void StripSensitiveAndSaudiHonorFourFieldPermissions()
    {
        var person = new JsonObject
        {
            ["iban"] = "SA0000000000000000000444",
            ["personalIban"] = "SA0000000000000000000555",
            ["formIban"] = "SA0000000000000000000666",
            ["mudadIban"] = "SA0000000000000000000777",
            ["salary"] = "5000",
            ["unifiedNationalNumber"] = "7015658094",
            ["legalCases"] = new JsonArray(new JsonObject { ["id"] = "c1" })
        };
        var plain = Principal("strip-plain", "Reviewer", ["riders.view"]);
        var stripped = InvokeStrip("StripSensitive", person, plain);
        Assert.AreEqual("", stripped.S("iban"));
        Assert.IsFalse(stripped.ContainsKey("personalIban"));
        Assert.IsFalse(stripped.ContainsKey("formIban"));
        Assert.IsFalse(stripped.ContainsKey("mudadIban"));

        var withIban = Principal("strip-iban", "Reviewer", ["riders.view", WorkforceApi.PermIban]);
        Assert.AreEqual("SA0000000000000000000444", InvokeStrip("StripSensitive", person, withIban).S("iban"));

        var saudiPlain = InvokeStrip("StripSaudi", person, plain);
        Assert.AreEqual("", saudiPlain.S("salary"));
        Assert.AreEqual("", saudiPlain.S("personalIban"));
        Assert.AreEqual("", saudiPlain.S("unifiedNationalNumber"));

        var saudiFull = Principal("strip-saudi", "Reviewer", [
            "riders.view", WorkforceApi.PermSaudiSalary, WorkforceApi.PermIban, WorkforceApi.PermUnifiedNumber
        ]);
        var kept = InvokeStrip("StripSaudi", person, saudiFull);
        Assert.AreEqual("5000", kept.S("salary"));
        Assert.AreEqual("SA0000000000000000000555", kept.S("personalIban"));
        Assert.AreEqual("7015658094", kept.S("unifiedNationalNumber"));

        var body = new JsonObject { ["person"] = person.DeepClone(), ["legalCases"] = person["legalCases"]!.DeepClone() };
        var strip360 = typeof(WorkforceApi).GetMethod("Strip360", BindingFlags.NonPublic | BindingFlags.Static)!;
        var withoutLegal = (JsonObject)strip360.Invoke(null, [body, plain])!;
        Assert.IsFalse(withoutLegal.ContainsKey("legalCases"));
        var withLegal = (JsonObject)strip360.Invoke(null, [body, Principal("strip-legal", "Reviewer", [WorkforceApi.PermLegalCases])])!;
        Assert.IsTrue(withLegal["legalCases"] is JsonArray);
    }

    [TestMethod]
    public void StoreScopeTotalsMatchScopedRiderSet_CompanyAndPlatform()
    {
        string path = Path.Combine(Path.GetTempPath(), "gate-wf-scope-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(path);
            string bw = FinancialCore.ResolveSponsor("BW").Id;
            string ex = FinancialCore.ResolveSponsor("EX").Id;
            store.Write((c, tx) =>
            {
                Store.Save(c, "rider", new JsonObject
                {
                    ["id"] = "2000000411", ["iqama"] = "2000000411", ["name"] = "Synthetic BW Jahez",
                    ["status"] = "Active", ["engagementType"] = "SponsoredInternal", ["iban"] = "SA0000000000000000000411",
                    ["isDeliveryRider"] = true, ["personRole"] = "rider", ["sponsorId"] = bw, ["sponsor"] = "BW",
                    ["financeSponsorId"] = bw, ["platforms"] = "Jahez"
                }, tx);
                Store.Save(c, "rider", new JsonObject
                {
                    ["id"] = "2000000422", ["iqama"] = "2000000422", ["name"] = "Synthetic EX Jahez",
                    ["status"] = "Active", ["engagementType"] = "SponsoredInternal", ["iban"] = "SA0000000000000000000422",
                    ["isDeliveryRider"] = true, ["personRole"] = "rider", ["sponsorId"] = ex, ["sponsor"] = "EX",
                    ["financeSponsorId"] = ex, ["platforms"] = "Jahez"
                }, tx);
                Store.Save(c, "rider", new JsonObject
                {
                    ["id"] = "2000000433", ["iqama"] = "2000000433", ["name"] = "Synthetic BW Keeta",
                    ["status"] = "Active", ["engagementType"] = "SponsoredInternal", ["iban"] = "SA0000000000000000000433",
                    ["isDeliveryRider"] = true, ["personRole"] = "rider", ["sponsorId"] = bw, ["sponsor"] = "BW",
                    ["financeSponsorId"] = bw, ["platforms"] = "Keeta"
                }, tx);
                return true;
            });

            var company = Principal("wf-company", "Reviewer", ["riders.view"], sponsors: ["BW"]);
            var platform = Principal("wf-platform", "Reviewer", ["riders.view"], sponsors: ["BW", "EX", "TR"], platforms: ["Jahez"]);
            var admin = Principal("wf-admin", "Admin", []);

            store.Read(c =>
            {
                var companyScope = Operations.LiveScope(c, company);
                Assert.IsFalse(companyScope.AllCompanies);
                Assert.IsTrue(companyScope.SponsorUnifiedNumbers.Contains(FinancialCore.ResolveSponsor("BW").UnifiedNumber));
                Assert.AreEqual(2, Store.List(c, "rider").Count(r => FinancialScopes.Rider(company, r)));

                var platformScope = Operations.LiveScope(c, platform);
                Assert.IsTrue(platformScope.AllCompanies);
                Assert.IsTrue(platformScope.PlatformRestricted);
                Assert.IsTrue(platformScope.PlatformCodes.Count > 0);

                Assert.IsTrue(FinancialScopes.Rider(company, Store.Get(c, "2000000411", "rider")!));
                Assert.IsFalse(FinancialScopes.Rider(company, Store.Get(c, "2000000422", "rider")!));
                Assert.IsTrue(FinancialScopes.AllCompanies(admin));
                return true;
            });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    [TestMethod]
    public void PreviewSqlEmbedsCompanyAndPlatformScopePredicates()
    {
        var req = WorkforceLiveQuery.ParseRequest(new Dictionary<string, string> { ["page"] = "1", ["pageSize"] = "10" });
        var companyOnly = new LiveOperationsQuery.Scope(
            false,
            new[] { "7015658094" },
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            PlatformRestricted: false);
        var (countSql, _, _, names) = WorkforceLiveQuery.PreviewSql(req, companyOnly);
        Assert.Contains("UnifiedNationalNumber", countSql, StringComparison.Ordinal);
        Assert.Contains("@cr", countSql, StringComparison.Ordinal);
        CollectionAssert.Contains(names.ToList(), "@cr0");

        var platformScope = new LiveOperationsQuery.Scope(
            false,
            new[] { "7015658094" },
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[] { "jahez", "HungerStation" },
            PlatformRestricted: true);
        var (scopedSql, _, _, scopedNames) = WorkforceLiveQuery.PreviewSql(req, platformScope);
        Assert.Contains("UnifiedNationalNumber", scopedSql, StringComparison.Ordinal);
        Assert.Contains("PlatformCode", scopedSql, StringComparison.Ordinal);
        Assert.IsTrue(scopedNames.Any(n => n.StartsWith("@pc", StringComparison.Ordinal)), "platform parameters in SQL");

        var emptySponsors = new LiveOperationsQuery.Scope(false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        var (blocked, _, _, _) = WorkforceLiveQuery.PreviewSql(req, emptySponsors);
        Assert.Contains("1=0", blocked, StringComparison.Ordinal);
    }

    [TestMethod]
    public void PreviewSqlRejectsUnknownStatusAndMapsInactive()
    {
        var ok = WorkforceLiveQuery.ParseRequest(new Dictionary<string, string>
        {
            ["page"] = "1",
            ["pageSize"] = "10",
            ["status"] = "غير نشط"
        });
        Assert.AreEqual("غير نشط", ok.Status);
        var (countSql, pageSql, summarySql, _) = WorkforceLiveQuery.PreviewSql(ok);
        Assert.Contains("@status", countSql, StringComparison.Ordinal);
        Assert.Contains("WITH filtered AS", pageSql, StringComparison.Ordinal);
        Assert.Contains("SponsoredMissingIban", summarySql, StringComparison.Ordinal);

        var bad = WorkforceLiveQuery.ParseRequest(new Dictionary<string, string>
        {
            ["page"] = "1",
            ["pageSize"] = "5",
            ["status"] = "not-a-real-status-xyz"
        });
        Assert.ThrowsExactly<ImportFailure>(() => WorkforceLiveQuery.PreviewSql(bad));
    }

    [TestMethod]
    public void ExportTimeoutDefaultAndPagedPreviewStillUseOffset()
    {
        var req = WorkforceLiveQuery.ParseRequest(new Dictionary<string, string> { ["page"] = "1", ["pageSize"] = "50" });
        // List PreviewSql keeps OFFSET; ExportAll uses a separate single-query path (ExportCommandTimeoutSeconds).
        var (_, pageSql, _, _) = WorkforceLiveQuery.PreviewSql(req);
        StringAssert.Contains(pageSql, "OFFSET @skip");
        StringAssert.Contains(pageSql, "FETCH NEXT @take");
        Assert.IsTrue(typeof(WorkforceLiveQuery).GetMethod("ExportAll") != null);
    }

    [TestMethod]
    public void LiveScopeIsPublicAndBuildsSponsorList()
    {
        string path = Path.Combine(Path.GetTempPath(), "gate-wf-livescope-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(path);
            var admin = Principal("synthetic-admin", "Admin", []);
            store.Read(c =>
            {
                var scope = Operations.LiveScope(c, admin);
                Assert.IsTrue(scope.AllCompanies);
                Assert.IsFalse(scope.PlatformRestricted);
                return true;
            });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }
}
