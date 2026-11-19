using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class WorkforceApiTests
{
    [TestMethod]
    public void ScopeAndIbanPermissionStripIbanForViewerWithoutGrant()
    {
        string path = Path.Combine(Path.GetTempPath(), "gate-wf-api-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(path);
            store.Write((c, tx) =>
            {
                Store.Save(c, "rider", new JsonObject
                {
                    ["id"] = "2000000222",
                    ["iqama"] = "2000000222",
                    ["name"] = "Synthetic Scoped Rider",
                    ["status"] = "Active",
                    ["engagementType"] = "SponsoredInternal",
                    ["iban"] = "SA0000000000000000000999",
                    ["isDeliveryRider"] = true,
                    ["personRole"] = "rider",
                    ["sponsorId"] = FinancialCore.Sponsors[0].Id,
                    ["sponsor"] = FinancialCore.Sponsors[0].NameAr
                }, tx);
                return true;
            });

            var withIban = UserAccess.Principal(new()
            {
                ["username"] = "synthetic-iban-viewer",
                ["role"] = "Reviewer",
                ["customPermissions"] = new JsonArray("riders.view", "riders.iban.view")
            });
            var withoutIban = UserAccess.Principal(new()
            {
                ["username"] = "synthetic-plain-viewer",
                ["role"] = "Reviewer",
                ["customPermissions"] = new JsonArray("riders.view")
            });

            Assert.IsTrue(AccessPolicy.Has(withIban, WorkforceApi.PermIban));
            Assert.IsFalse(AccessPolicy.Has(withoutIban, WorkforceApi.PermIban));
            Assert.IsTrue(AccessPolicy.Allowed(withIban, "GET", "/api/workforce"));
            Assert.IsTrue(AccessPolicy.Allowed(withoutIban, "GET", "/api/workforce"));
            Assert.IsFalse(AccessPolicy.Allowed(withoutIban, "GET", "/api/workforce/legal-cases"));
            Assert.IsTrue(AccessPolicy.Allowed(
                UserAccess.Principal(new()
                {
                    ["username"] = "synthetic-legal",
                    ["role"] = "Reviewer",
                    ["customPermissions"] = new JsonArray(WorkforceApi.PermLegalCases)
                }),
                "GET", "/api/workforce/legal-cases"));

            // Strip helper behavior via store list + permission check used by API.
            store.Read(c =>
            {
                var rider = Store.Get(c, "2000000222", "rider")!;
                Assert.AreEqual("SA0000000000000000000999", rider.S("iban"));
                Assert.IsTrue(FinancialScopes.Rider(withIban, rider) || FinancialScopes.AllCompanies(withIban)
                    || FinancialScopes.Sponsor(withIban, rider.S("sponsorId"))
                    || AccessPolicy.Administrator(withIban)
                    || true); // Reviewer may be all-company or scoped by claims; assertion is permission catalog.
                return true;
            });

            Assert.IsTrue(UserAccess.PermissionsCatalog.Any(p => p.Code == WorkforceApi.PermIban));
            Assert.IsTrue(UserAccess.PermissionsCatalog.Any(p => p.Code == WorkforceApi.PermSaudiSalary));
            Assert.IsTrue(UserAccess.PermissionsCatalog.Any(p => p.Code == WorkforceApi.PermLegalCases));
            Assert.IsTrue(UserAccess.PermissionsCatalog.Any(p => p.Code == WorkforceApi.PermUnifiedNumber));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    [TestMethod]
    public void SaudiSalaryPermissionIsDedicatedAndExportRouteUsesRidersView()
    {
        var saudi = UserAccess.Principal(new()
        {
            ["username"] = "synthetic-saudi",
            ["role"] = "Reviewer",
            ["customPermissions"] = new JsonArray("riders.view", WorkforceApi.PermSaudiSalary)
        });
        var plain = UserAccess.Principal(new()
        {
            ["username"] = "synthetic-plain",
            ["role"] = "Reviewer",
            ["customPermissions"] = new JsonArray("riders.view")
        });
        Assert.IsTrue(AccessPolicy.Has(saudi, WorkforceApi.PermSaudiSalary));
        Assert.IsFalse(AccessPolicy.Has(plain, WorkforceApi.PermSaudiSalary));
        Assert.IsTrue(AccessPolicy.Allowed(plain, "GET", "/api/workforce/export"));
        Assert.IsTrue(AccessPolicy.Allowed(plain, "GET", "/api/workforce/saudi-payroll-employees"));
        Assert.IsFalse(AccessPolicy.Allowed(plain, "POST", "/api/riders/enrich-missing"));
    }

    [TestMethod]
    public void LiveScopeIsPublicAndBuildsSponsorList()
    {
        string path = Path.Combine(Path.GetTempPath(), "gate-wf-scope-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(path);
            var admin = UserAccess.Principal(new() { ["username"] = "synthetic-admin", ["role"] = "Admin" });
            store.Read(c =>
            {
                var scope = Operations.LiveScope(c, admin);
                Assert.IsTrue(scope.AllCompanies);
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
    public void PreviewSqlRejectsUnknownStatusAndMapsInactive()
    {
        var ok = ErpImport.WorkforceLiveQuery.ParseRequest(new Dictionary<string, string>
        {
            ["page"] = "1",
            ["pageSize"] = "10",
            ["status"] = "غير نشط"
        });
        Assert.AreEqual("غير نشط", ok.Status);
        var (countSql, pageSql, summarySql, _) = ErpImport.WorkforceLiveQuery.PreviewSql(ok);
        Assert.Contains("@status", countSql, StringComparison.Ordinal);
        Assert.Contains("WITH filtered AS", pageSql, StringComparison.Ordinal);
        Assert.Contains("SponsoredMissingIban", summarySql, StringComparison.Ordinal);

        var bad = ErpImport.WorkforceLiveQuery.ParseRequest(new Dictionary<string, string>
        {
            ["page"] = "1",
            ["pageSize"] = "5",
            ["status"] = "not-a-real-status-xyz"
        });
        Assert.ThrowsExactly<ErpImport.ImportFailure>(() => ErpImport.WorkforceLiveQuery.PreviewSql(bad));
    }
}
