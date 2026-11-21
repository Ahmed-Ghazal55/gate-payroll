using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class TableFilterContractTests
{
    sealed class TestHostEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Gate Payroll";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [TestMethod]
    public void EvaluatesAllFilterOperatorsAndSortByColorOnServerContract()
    {
        var refDate = new DateOnly(2026, 11, 19);
        var rows = new List<JsonObject>
        {
            new() { ["id"] = "1", ["name"] = "شركة بترو اب", ["city"] = "الرياض", ["status"] = "active", ["amount"] = 1500m, ["date"] = "2026-11-10" },
            new() { ["id"] = "2", ["name"] = "شركة سيارة اب", ["city"] = "الرياض", ["status"] = "pending_approval", ["amount"] = 2400m, ["date"] = "2026-11-15" },
            new() { ["id"] = "3", ["name"] = "مؤسسة الوقود", ["city"] = "جدة", ["status"] = "cancelled", ["amount"] = 800m, ["date"] = "2026-10-20" },
            new() { ["id"] = "4", ["name"] = "شركة بترو الخليج", ["city"] = "الدمام", ["status"] = "active", ["amount"] = 3200m, ["date"] = "2026-09-05" }
        };

        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("name", "contains", "بترو")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("name", "notContains", "بترو")], refDate).Count);
        Assert.AreEqual(3, TableFilterContract.ApplyFilters(rows, [new("name", "beginsWith", "شركه")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("name", "endsWith", "اب")], refDate).Count);
        Assert.AreEqual(1, TableFilterContract.ApplyFilters(rows, [new("city", "eq", "جده")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("city", "neq", "الرياض")], refDate).Count);

        var activeRows = TableFilterContract.ApplyFilters(rows, [new("status", "eq", "نشط")], refDate);
        Assert.AreEqual(2, activeRows.Count);

        var inRows = TableFilterContract.ApplyFilters(rows, [new("city", "in", Values: ["جدة", "الدمام"])], refDate);
        CollectionAssert.AreEquivalent(new[] { "3", "4" }, inRows.Select(r => r.S("id")).ToList());

        var warnRows = TableFilterContract.ApplyFilters(rows, [new("status", "color", "warning")], refDate);
        Assert.AreEqual(1, warnRows.Count);
        Assert.AreEqual("2", warnRows[0].S("id"));

        Assert.AreEqual(1, TableFilterContract.ApplyFilters(rows, [new("amount", "eq", "1500")], refDate).Count);
        Assert.AreEqual(3, TableFilterContract.ApplyFilters(rows, [new("amount", "neq", "1500")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("amount", "gt", "1500")], refDate).Count);
        Assert.AreEqual(3, TableFilterContract.ApplyFilters(rows, [new("amount", "gte", "1500")], refDate).Count);
        Assert.AreEqual(1, TableFilterContract.ApplyFilters(rows, [new("amount", "lt", "1500")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("amount", "lte", "1500")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("amount", "between", "1000", "2500")], refDate).Count);
        var top2 = TableFilterContract.ApplyFilters(rows, [new("amount", "topN", "2")], refDate);
        CollectionAssert.AreEquivalent(new[] { "2", "4" }, top2.Select(r => r.S("id")).ToList());

        Assert.AreEqual(1, TableFilterContract.ApplyFilters(rows, [new("date", "eq", "2026-11-10")], refDate).Count);
        Assert.AreEqual(3, TableFilterContract.ApplyFilters(rows, [new("date", "neq", "2026-11-10")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("date", "before", "2026-11-01")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("date", "after", "2026-11-01")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("date", "between", "2026-11-01", "2026-11-30")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("date", "thisMonth")], refDate).Count);
        Assert.AreEqual(1, TableFilterContract.ApplyFilters(rows, [new("date", "lastMonth")], refDate).Count);

        var sortedByDanger = TableFilterContract.ApplySort(rows, [new("status", "asc", "danger")]);
        Assert.AreEqual("3", sortedByDanger[0].S("id"));

        var sortedByAmountDesc = TableFilterContract.ApplySort(rows, [new("amount", "desc")]);
        Assert.AreEqual("4", sortedByAmountDesc[0].S("id"));
        Assert.AreEqual("3", sortedByAmountDesc[^1].S("id"));
    }

    [TestMethod]
    public void OperationsRejectsNonWhitelistedFilterColumn()
    {
        using var f = new PayFixture();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Operations:Source"] = "store" })
            .Build();
        var env = new TestHostEnv("Development");
        var opsPurchases = typeof(OperationsLiveApi).GetMethod("Purchases", BindingFlags.NonPublic | BindingFlags.Static)!;
        var badOpsCtx = new DefaultHttpContext { User = PayFixture.Maker };
        badOpsCtx.Request.QueryString = new QueryString("?from=2026-11-01&to=2026-11-30&filters=[{\"col\":\"vin\",\"op\":\"contains\",\"value\":\"123\"}]");
        var ex = Assert.ThrowsExactly<TargetInvocationException>(() =>
            opsPurchases.Invoke(null, [f.Store, cfg, env, badOpsCtx]));
        Assert.IsInstanceOfType<ApiError>(ex.InnerException);
        Assert.AreEqual(400, ((ApiError)ex.InnerException!).Status);
    }

    [TestMethod]
    public void ParseFiltersAndSortRejectOverLimits()
    {
        var overFilterCtx = new DefaultHttpContext();
        overFilterCtx.Request.QueryString = new QueryString("?filters=" + Uri.EscapeDataString(
            "[" + string.Join(",", Enumerable.Range(0, TableFilterContract.MaxFilterClauses + 1).Select(_ => "{\"col\":\"name\",\"op\":\"eq\",\"value\":\"x\"}")) + "]"));
        Assert.AreEqual(400, Assert.ThrowsExactly<ApiError>(() => TableFilterContract.ParseFilters(overFilterCtx.Request.Query)).Status);

        var overSortCtx = new DefaultHttpContext();
        overSortCtx.Request.QueryString = new QueryString("?sort=" + Uri.EscapeDataString(
            "[" + string.Join(",", Enumerable.Range(0, TableFilterContract.MaxSortClauses + 1).Select(_ => "{\"col\":\"name\",\"dir\":\"asc\"}")) + "]"));
        Assert.AreEqual(400, Assert.ThrowsExactly<ApiError>(() => TableFilterContract.ParseSort(overSortCtx.Request.Query)).Status);

        var overValsCtx = new DefaultHttpContext();
        overValsCtx.Request.QueryString = new QueryString("?filters=" + Uri.EscapeDataString(
            "[{\"col\":\"city\",\"op\":\"in\",\"values\":[" + string.Join(",", Enumerable.Range(0, TableFilterContract.MaxValuesPerClause + 1).Select(i => $"\"c{i}\"")) + "]}]"));
        Assert.AreEqual(400, Assert.ThrowsExactly<ApiError>(() => TableFilterContract.ParseFilters(overValsCtx.Request.Query)).Status);
    }
}
