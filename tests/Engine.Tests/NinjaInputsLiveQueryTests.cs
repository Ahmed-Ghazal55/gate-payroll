using System.Security.Claims;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class NinjaInputsLiveQueryTests
{
    static LiveOperationsQuery.Scope All => new(true, [], [], [], []);
    static NinjaInputsLiveQuery.PeriodRequest Oct => new(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 1, 50);

    static ClaimsPrincipal Principal(string username, string[] perms, string[]? platforms = null)
    {
        var body = new JsonObject
        {
            ["username"] = username,
            ["role"] = "Reviewer",
            ["customPermissions"] = new JsonArray(perms.Select(p => (JsonNode?)p).ToArray()),
            ["financialScope"] = new JsonObject
            {
                ["sponsorIds"] = new JsonArray(FinancialCore.Sponsors.Select(s => (JsonNode?)s.Id).ToArray()),
                ["platforms"] = new JsonArray((platforms ?? ["Ninja"]).Select(p => (JsonNode?)p).ToArray()),
                ["accountCodes"] = new JsonArray(),
                ["categoryCodes"] = new JsonArray()
            }
        };
        return UserAccess.Principal(body);
    }

    [TestMethod]
    public void PreviewDailyPerformanceSql_IsSingleScopedSelect()
    {
        var (sql, names) = NinjaInputsLiveQuery.PreviewDailyPerformanceSql(All, Oct);
        StringAssert.Contains(sql, "[ninja].[NinjaDailyPerformance]");
        StringAssert.Contains(sql, "[app].[PlatformRiderAccounts]");
        StringAssert.Contains(sql, "cp.[Code]=@ninja");
        StringAssert.Contains(sql, "CoverageFirstDay");
        StringAssert.Contains(sql, "CoverageLastDay");
        Assert.IsFalse(sql.Contains(';', StringComparison.Ordinal), "one SQL statement");
        Assert.DoesNotContain("INSERT ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", sql, StringComparison.OrdinalIgnoreCase);
        CollectionAssert.Contains(names.ToList(), "@from");
        CollectionAssert.Contains(names.ToList(), "@to");
        CollectionAssert.Contains(names.ToList(), "@ninja");
        Assert.DoesNotContain("2026-10-01", sql, StringComparison.Ordinal);

        var scoped = new LiveOperationsQuery.Scope(false, ["7000000001"], [], [], []);
        var (scopedSql, scopedNames) = NinjaInputsLiveQuery.PreviewDailyPerformanceSql(scoped, Oct);
        StringAssert.Contains(scopedSql, "s.[UnifiedNationalNumber] IN (@cr0)");
        CollectionAssert.Contains(scopedNames.ToList(), "@cr0");

        var blocked = new LiveOperationsQuery.Scope(false, [], [], [], []);
        var (blockedSql, _) = NinjaInputsLiveQuery.PreviewDailyPerformanceSql(blocked, Oct);
        StringAssert.Contains(blockedSql, "1=0");
    }

    [TestMethod]
    public void DailyPerformance_RequiresSqlServer()
    {
        using var sqlite = new SqliteConnection("Data Source=:memory:");
        sqlite.Open();
        var fail = Assert.ThrowsExactly<ImportFailure>(() => NinjaInputsLiveQuery.DailyPerformance(sqlite, All, Oct));
        Assert.AreEqual("live_ops_requires_sql", fail.Code);
    }

    [TestMethod]
    public void PreviewDailyDeductionsSql_KeysActualRiderAndScopes()
    {
        var (sql, names) = NinjaInputsLiveQuery.PreviewDailyDeductionsSql(All, Oct);
        StringAssert.Contains(sql, "[ninja].[NinjaDailyDeductions]");
        StringAssert.Contains(sql, "d.[RiderProfileId]");
        StringAssert.Contains(sql, "cp.[Code]=@ninja");
        Assert.IsFalse(sql.Contains(';', StringComparison.Ordinal));
        CollectionAssert.Contains(names.ToList(), "@from");
        Assert.IsTrue(AccessPolicy.Allowed(Principal("syn-ded", ["pay.view"]), "GET", "/api/pay/apps/ninja/live/daily-deductions"));
    }

    [TestMethod]
    public void Coverage_FlagsMissingDaysAtPeriodEnd()
    {
        var coverage = NinjaInputsLiveQuery.Coverage(Oct, "2026-10-01", "2026-10-29", 100);
        Assert.AreEqual("2026-10-01", coverage.S("firstDay"));
        Assert.AreEqual("2026-10-29", coverage.S("lastDay"));
        Assert.IsTrue(coverage.B("hasGapAtPeriodEnd"));
        var full = NinjaInputsLiveQuery.Coverage(Oct, "2026-10-01", "2026-10-31", 100);
        Assert.IsFalse(full.B("hasGapAtPeriodEnd"));
    }

    [TestMethod]
    public void PermissionGate_RequiresPayViewAndNinjaPlatform()
    {
        var ok = Principal("syn-ninja-ok", ["pay.view"], ["Ninja"]);
        var noPay = Principal("syn-ninja-no-pay", ["operations.view"], ["Ninja"]);
        var noPlat = Principal("syn-ninja-no-plat", ["pay.view"], ["Keeta"]);
        Assert.IsTrue(AccessPolicy.Allowed(ok, "GET", "/api/pay/apps/ninja/live/daily-performance"));
        Assert.IsFalse(AccessPolicy.Allowed(noPay, "GET", "/api/pay/apps/ninja/live/daily-performance"));
        var ex = Assert.ThrowsExactly<ApiError>(() => NinjaInputsLiveApi.DemandNinjaPlatform(noPlat));
        Assert.AreEqual(403, ex.Status);
        NinjaInputsLiveApi.DemandNinjaPlatform(ok);
    }
}
