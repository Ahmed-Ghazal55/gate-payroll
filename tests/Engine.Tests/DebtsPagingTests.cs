using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class DebtsPagingTests
{
    static ClaimsPrincipal Admin => UserAccess.Principal(new() { ["username"] = "synthetic-debts-admin", ["role"] = "Admin" });
    static string Bw => FinancialCore.ResolveSponsor("BW").Id;
    static string Ex => FinancialCore.ResolveSponsor("EX").Id;

    static IQueryCollection Q(params (string Key, string Value)[] values) =>
        new QueryCollection(values.ToDictionary(x => x.Key, x => (StringValues)x.Value));

    static JsonObject Debt(string id, string status, string category, decimal remaining, string sponsor, string iqama = "2000000101", string? name = null, string entryMode = "Accrual") => new()
    {
        ["id"] = id,
        ["iqama"] = iqama,
        ["riderName"] = name ?? "Synthetic Rider",
        ["category"] = category,
        ["status"] = status,
        ["totalAmount"] = remaining + 10m,
        ["remainingBalance"] = remaining,
        ["monthlyInstallment"] = 10m,
        ["creditorSponsorId"] = sponsor,
        ["originSponsorId"] = sponsor,
        ["entryMode"] = entryMode,
        ["platform"] = "",
        ["createdAt"] = "2026-10-01T00:00:00Z"
    };

    static void Seed(Store store, params JsonObject[] debts) =>
        store.Write((c, tx) => { foreach (var d in debts) Store.Save(c, "debt", d, tx); return true; });

    static void WithSqlite(Action<Store> action)
    {
        string path = Path.Combine(Path.GetTempPath(), "gate-debts-page-" + Guid.NewGuid().ToString("N"));
        try { action(new Store(path)); }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    static void WithSqlServer(Action<Store> action)
    {
        string? configured = Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Inconclusive("SQL Server debts paging requires PAYROLL_TEST_SQL_CONNECTION (gp-d1-sql).");
            return;
        }
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(master.DataSource, @"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        string name = "GateDebtsPage_" + Guid.NewGuid().ToString("N");
        string dir = Path.Combine(Path.GetTempPath(), name);
        using var admin = new SqlConnection(master.ConnectionString);
        admin.Open();
        using (var create = admin.CreateCommand()) { create.CommandText = "CREATE DATABASE [" + name + "]"; create.ExecuteNonQuery(); }
        try
        {
            var target = new SqlConnectionStringBuilder(configured) { InitialCatalog = name };
            action(new Store(dir, target.ConnectionString));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var drop = admin.CreateCommand();
            drop.CommandText = "ALTER DATABASE [" + name + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + name + "]";
            drop.ExecuteNonQuery();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    static void OnProvider(bool sqlServer, Action<Store> action)
    {
        if (sqlServer) WithSqlServer(action);
        else WithSqlite(action);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void WithoutPage_ReturnsLegacyArray_BackwardCompatible(bool sqlServer)
    {
        OnProvider(sqlServer, store =>
        {
            Seed(store, Debt("d1", "active", "CashAdvance", 100m, Bw), Debt("d2", "settled", "TrafficFine", 0m, Bw));
            var result = store.Read(c => Debts.ListPaged(c, Admin, Q(("status", "active"))));
            Assert.IsTrue(result is List<JsonObject> or IEnumerable<JsonObject>);
            var rows = ((IEnumerable<JsonObject>)result).ToList();
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("d1", rows[0].S("id"));
            Assert.IsTrue(rows[0]["allowedActions"] is JsonArray);
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void WithPage_ReturnsEnvelope_FiltersAndTotalsInSql(bool sqlServer)
    {
        OnProvider(sqlServer, store =>
        {
            var debts = Enumerable.Range(1, 55).Select(i => Debt("p" + i, i % 2 == 0 ? "active" : "settled", "CashAdvance", i, Bw, iqama: "2000000" + (100 + i).ToString("D3"), name: i == 3 ? "أحمد تجريبي" : "Synthetic " + i)).ToArray();
            Seed(store, debts);
            var page1 = JsonSerializer.SerializeToNode(store.Read(c => Debts.ListPaged(c, Admin, Q(("page", "1"), ("pageSize", "10"), ("status", "active")))))!.AsObject();
            Assert.AreEqual(10, page1["items"]!.AsArray().Count);
            Assert.AreEqual(27, page1["total"]!.GetValue<int>()); // 2,4,...,54
            Assert.AreEqual(1, page1["page"]!.GetValue<int>());
            Assert.AreEqual(10, page1["pageSize"]!.GetValue<int>());
            Assert.IsTrue(page1["items"]![0]!["allowedActions"] is JsonArray);

            var page2 = JsonSerializer.SerializeToNode(store.Read(c => Debts.ListPaged(c, Admin, Q(("page", "2"), ("pageSize", "10"), ("status", "active")))))!.AsObject();
            Assert.AreEqual(10, page2["items"]!.AsArray().Count);

            var search = JsonSerializer.SerializeToNode(store.Read(c => Debts.ListPaged(c, Admin, Q(("page", "1"), ("q", "احمد")))))!.AsObject();
            Assert.AreEqual(1, search["total"]!.GetValue<int>());
            Assert.AreEqual("p3", search["items"]![0]!.S("id"));

            var capped = JsonSerializer.SerializeToNode(store.Read(c => Debts.ListPaged(c, Admin, Q(("page", "1"), ("pageSize", "999")))))!.AsObject();
            Assert.AreEqual(200, capped["pageSize"]!.GetValue<int>());

            Assert.ThrowsExactly<ApiError>(() => store.Read(c => Debts.ListPaged(c, Admin, Q(("page", "0")))));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompanyFilterAndSummaryTotals(bool sqlServer)
    {
        OnProvider(sqlServer, store =>
        {
            Seed(store,
                Debt("bw1", "active", "CashAdvance", 40m, Bw),
                Debt("bw2", "active", "TrafficFine", 60m, Bw),
                Debt("ex1", "active", "CashAdvance", 25m, Ex),
                Debt("open1", "pending_approval", "CashAdvance", 15m, Bw, entryMode: "OpeningBalance"));
            var page = JsonSerializer.SerializeToNode(store.Read(c => Debts.ListPaged(c, Admin, Q(("page", "1"), ("sponsorId", "BW"), ("status", "active")))))!.AsObject();
            Assert.AreEqual(2, page["total"]!.GetValue<int>());

            var summary = store.Read(c => Debts.Summary(c, Admin, Q(("sponsorId", "BW"))));
            Assert.AreEqual(100m, summary.N("activeRemainingTotal")); // 40+60
            Assert.AreEqual(55m, summary.N("advanceRemainingTotal")); // 40+15 (opening still remaining)
            Assert.AreEqual(1, summary.N("pendingOpeningCount"));
            Assert.AreEqual(15m, summary.N("pendingOpeningTotal"));
            Assert.AreEqual(3, summary.N("totalCount")); // bw rows only
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ScopedUserSeesOnlyAssignedCompany(bool sqlServer)
    {
        OnProvider(sqlServer, store =>
        {
            Seed(store, Debt("bw1", "active", "CashAdvance", 10m, Bw), Debt("ex1", "active", "CashAdvance", 20m, Ex));
            var scoped = UserAccess.Principal(new JsonObject
            {
                ["username"] = "synthetic-scoped-debts",
                ["role"] = "FinancialAccountant",
                ["financialScope"] = new JsonObject
                {
                    ["sponsorIds"] = new JsonArray(Bw),
                    ["accountCodes"] = new JsonArray(),
                    ["platforms"] = new JsonArray("Jahez", "Keeta", "HungerStation", "Ninja", "Chefz", "Amazon", "Other"),
                    ["categoryCodes"] = new JsonArray("CashAdvance", "TrafficFine", "Advance", "BankAdvance")
                }
            });
            var page = JsonSerializer.SerializeToNode(store.Read(c => Debts.ListPaged(c, scoped, Q(("page", "1")))))!.AsObject();
            Assert.AreEqual(1, page["total"]!.GetValue<int>());
            Assert.AreEqual("bw1", page["items"]![0]!.S("id"));
        });
    }

    static JsonObject DebtFull(string id, string status, string category, decimal remaining, string sponsor, string platform, string iqama = "2000000101", string? name = null)
    {
        var d = Debt(id, status, category, remaining, sponsor, iqama, name);
        d["platform"] = platform;
        return d;
    }

    static ClaimsPrincipal ScopedBwAccountant() => UserAccess.Principal(new JsonObject
    {
        ["username"] = "synthetic-scoped-parity",
        ["role"] = "FinancialAccountant",
        ["financialScope"] = new JsonObject
        {
            ["sponsorIds"] = new JsonArray(Bw),
            ["accountCodes"] = new JsonArray(),
            ["platforms"] = new JsonArray("Jahez", "Ninja"),
            ["categoryCodes"] = new JsonArray("CashAdvance", "TrafficFine", "Advance", "BankAdvance", "InternetAdvance")
        }
    });

    static List<string> SqlIds(Store store, ClaimsPrincipal actor, params (string Key, string Value)[] filters)
    {
        var result = store.Read(c => Debts.ListPaged(c, actor, Q(filters)));
        return ((IEnumerable<JsonObject>)result).Select(d => d.S("id")).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    static List<string> LegacyIds(Store store, ClaimsPrincipal actor, Func<JsonObject, bool>? extra = null)
    {
        return store.Read(c => Store.List(c, "debt")
            .Where(d => FinancialScopes.Debt(actor, d) && (extra?.Invoke(d) ?? true))
            .Select(d => d.S("id"))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SqlList_MatchesStoreListPlusFinancialScopes_OnMixedData(bool sqlServer)
    {
        OnProvider(sqlServer, store =>
        {
            // Mixed: canonical id, code, name; free-text platform; legacy category; empty platform; out-of-scope.
            Seed(store,
                DebtFull("a-id", "active", "CashAdvance", 10m, Bw, ""),
                DebtFull("a-code", "active", "TrafficFine", 11m, "BW", "Jahez"),
                DebtFull("a-name", "settled", "CashAdvance", 0m, "سجل البوابا", ""),
                DebtFull("a-free-plat", "active", "CashAdvance", 12m, Bw, "CustomExcelPlatform"),
                DebtFull("a-legacy-cat", "active", "hr_cost", 13m, Bw, ""),
                DebtFull("a-internet", "active", "InternetAdvance", 14m, Bw, "Ninja"),
                DebtFull("ex-only", "active", "CashAdvance", 15m, Ex, ""),
                DebtFull("unlinked", "active", "CashAdvance", 16m, "unlinked-sponsor-id", ""));

            var admin = Admin;
            var scoped = ScopedBwAccountant();

            CollectionAssert.AreEqual(LegacyIds(store, admin), SqlIds(store, admin));
            CollectionAssert.AreEqual(LegacyIds(store, scoped), SqlIds(store, scoped));

            // Status tab
            CollectionAssert.AreEqual(
                LegacyIds(store, admin, d => d.S("status") == "active"),
                SqlIds(store, admin, ("status", "active")));
            CollectionAssert.AreEqual(
                LegacyIds(store, scoped, d => d.S("status") == "active"),
                SqlIds(store, scoped, ("status", "active")));

            // Exact category filter (CashAdvance does not expand to the whole advance group)
            CollectionAssert.AreEqual(
                LegacyIds(store, admin, d => d.S("category") == "CashAdvance"),
                SqlIds(store, admin, ("category", "CashAdvance")));
            CollectionAssert.AreEqual(
                LegacyIds(store, admin, d => AdvanceCategories.Contains(d.S("category")) || string.Equals(d.S("category"), "advance", StringComparison.OrdinalIgnoreCase)),
                SqlIds(store, admin, ("category", "advance")));

            // Admin keeps free-text platform + legacy category; scoped user does not.
            Assert.IsTrue(SqlIds(store, admin).Contains("a-free-plat"));
            Assert.IsTrue(SqlIds(store, admin).Contains("a-legacy-cat"));
            Assert.IsTrue(SqlIds(store, admin).Contains("unlinked"));
            Assert.IsFalse(SqlIds(store, scoped).Contains("a-free-plat"));
            Assert.IsFalse(SqlIds(store, scoped).Contains("a-legacy-cat"));
            Assert.IsFalse(SqlIds(store, scoped).Contains("unlinked"));
            Assert.IsTrue(SqlIds(store, scoped).Contains("a-code")); // sponsor stored as code
            Assert.IsTrue(SqlIds(store, scoped).Contains("a-name")); // sponsor stored as short name
            Assert.IsTrue(SqlIds(store, scoped).Contains("a-internet")); // catalog category in scope
        });
    }

    static readonly HashSet<string> AdvanceCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Advance", "CashAdvance", "BankAdvance"
    };

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PlatformScope_EmptyAllowed_FreeTextOnlyForAdmin(bool sqlServer)
    {
        OnProvider(sqlServer, store =>
        {
            Seed(store,
                DebtFull("empty-plat", "active", "CashAdvance", 10m, Bw, ""),
                DebtFull("jahez", "active", "CashAdvance", 11m, Bw, "Jahez"),
                DebtFull("free", "active", "CashAdvance", 12m, Bw, "SheetPlatformX"));
            var scoped = ScopedBwAccountant(); // platforms: Jahez, Ninja
            var adminIds = SqlIds(store, Admin);
            var scopedIds = SqlIds(store, scoped);
            Assert.IsTrue(adminIds.Contains("empty-plat") && adminIds.Contains("jahez") && adminIds.Contains("free"));
            Assert.IsTrue(scopedIds.Contains("empty-plat") && scopedIds.Contains("jahez"));
            Assert.IsFalse(scopedIds.Contains("free"));
            CollectionAssert.AreEqual(LegacyIds(store, Admin), adminIds);
            CollectionAssert.AreEqual(LegacyIds(store, scoped), scopedIds);
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EmptySponsor_AllowedForFinanceWithAllCompanies(bool sqlServer)
    {
        OnProvider(sqlServer, store =>
        {
            Seed(store,
                DebtFull("no-sp", "active", "CashAdvance", 10m, "", ""),
                DebtFull("bw", "active", "CashAdvance", 11m, Bw, ""));
            // Admin / all-companies finance role sees empty creditorSponsorId (FinancialScopes.Debt empty-sponsor rule).
            Assert.IsTrue(SqlIds(store, Admin).Contains("no-sp"));
            CollectionAssert.AreEqual(LegacyIds(store, Admin), SqlIds(store, Admin));

            var scoped = ScopedBwAccountant();
            Assert.IsFalse(SqlIds(store, scoped).Contains("no-sp"));
            CollectionAssert.AreEqual(LegacyIds(store, scoped), SqlIds(store, scoped));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AdminBypass_SeesLegacyCategoryAndUnlinkedSponsor(bool sqlServer)
    {
        OnProvider(sqlServer, store =>
        {
            Seed(store,
                DebtFull("legacy", "active", "hr_cost", 10m, Bw, ""),
                DebtFull("orphan", "active", "CashAdvance", 11m, "not-a-real-sponsor", ""));
            var ids = SqlIds(store, Admin);
            Assert.IsTrue(ids.Contains("legacy"));
            Assert.IsTrue(ids.Contains("orphan"));
            CollectionAssert.AreEqual(LegacyIds(store, Admin), ids);
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CategoryScope_ScopedUserLimitedToAssignedCodes(bool sqlServer)
    {
        OnProvider(sqlServer, store =>
        {
            Seed(store,
                DebtFull("cash", "active", "CashAdvance", 10m, Bw, ""),
                DebtFull("internet", "active", "InternetAdvance", 11m, Bw, ""),
                DebtFull("admin-ded", "active", "AdministrativeDeduction", 12m, Bw, ""));
            var scoped = ScopedBwAccountant(); // CashAdvance + InternetAdvance, not AdministrativeDeduction
            var ids = SqlIds(store, scoped);
            Assert.IsTrue(ids.Contains("cash") && ids.Contains("internet"));
            Assert.IsFalse(ids.Contains("admin-ded"));
            CollectionAssert.AreEqual(LegacyIds(store, scoped), ids);
            Assert.IsTrue(SqlIds(store, Admin).Contains("admin-ded"));
        });
    }
}
