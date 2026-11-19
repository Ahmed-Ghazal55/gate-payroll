using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class OperationsLiveQueryTests
{
    [TestMethod]
    public void PreviewCitiesSqlUsesParametersAndAllowlistedTables()
    {
        var scope = new LiveOperationsQuery.Scope(false, ["7000000001"], [], [], [], PlatformRestricted: false);
        var (sql, names) = OperationsLiveQuery.PreviewCitiesSql(scope);
        Assert.Contains("[app].[OperatingCities]", sql, StringComparison.Ordinal);
        Assert.Contains("[platform].[GlobalCities]", sql, StringComparison.Ordinal);
        Assert.Contains("@cr", string.Join(',', names), StringComparison.Ordinal);
        Assert.DoesNotContain("DROP ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("7000000001", sql, StringComparison.Ordinal);
    }

    [TestMethod]
    public void PreviewHousingSqlPagesWithOffsetFetch()
    {
        var (sql, names) = OperationsLiveQuery.PreviewHousingSql(city: "JED", search: "سكن");
        Assert.Contains("OFFSET @skip", sql, StringComparison.Ordinal);
        Assert.Contains("FETCH NEXT @take", sql, StringComparison.Ordinal);
        Assert.Contains("[app].[Housing]", sql, StringComparison.Ordinal);
        CollectionAssert.Contains(names.ToList(), "@skip");
        CollectionAssert.Contains(names.ToList(), "@take");
        Assert.DoesNotContain("سكن", sql, StringComparison.Ordinal);
    }

    [TestMethod]
    public void PreviewPeriodReportSqlHasNoVinAndUsesCostConstants()
    {
        var (sql, _) = OperationsLiveQuery.PreviewPeriodReportSql("vehicle");
        Assert.DoesNotContain("].[Vin]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" v.[Vin]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("period_bounds", sql, StringComparison.Ordinal);
        Assert.Contains("RiderVehicleAssignments", sql, StringComparison.Ordinal);
        Assert.Contains("MaintenanceMaterialUsage", sql, StringComparison.Ordinal);
        Assert.Contains("RealRiders", sql, StringComparison.Ordinal);
        Assert.Contains(OperationsLiveQuery.MonthlyCostCarSar.ToString(CultureInfo.InvariantCulture), sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET @skip", sql, StringComparison.Ordinal);
        Assert.Contains("DATEDIFF(DAY, '0001-01-01', CONVERT(date, @from))", sql, StringComparison.Ordinal);
    }

    [TestMethod]
    public void PreviewPeriodReportRiderModeGroupsByRider()
    {
        var (sql, _) = OperationsLiveQuery.PreviewPeriodReportSql("rider");
        Assert.Contains("RiderKey", sql, StringComparison.Ordinal);
        Assert.Contains("IsRealRider", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("].[Vin]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CAST(0 AS decimal(18,2)) AS SparePartsCost", sql, StringComparison.Ordinal);
    }

    [TestMethod]
    public void PreviewCitiesSqlUsesActiveStatusesOneAndTwo()
    {
        var (sql, _) = OperationsLiveQuery.PreviewCitiesSql();
        Assert.Contains("a.[Status] IN (1,2)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("a.[OperatingCityId]=oc.[Id] AND a.[Status]=1", sql, StringComparison.Ordinal);
    }

    [TestMethod]
    public void PreviewFuelCardsSqlMarksCapAbsent()
    {
        var (sql, names) = OperationsLiveQuery.PreviewFuelCardsSql();
        Assert.Contains("[app].[FuelCards]", sql, StringComparison.Ordinal);
        Assert.Contains("FuelCardMonthlyUsages", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CapLimit", sql, StringComparison.OrdinalIgnoreCase);
        CollectionAssert.Contains(names.ToList(), "@month");
    }

    [TestMethod]
    public void DaysInPeriodMatchesOmarFourteenHundredCutoff()
    {
        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 30);
        // Start 15:00 Riyadh on Nov 1 → billing starts Nov 2
        var start = new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.FromHours(3));
        var end = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.FromHours(3));
        decimal days = OperationsLiveQuery.DaysInPeriod(start, end, from, to);
        Assert.AreEqual(1m, days);
        Assert.AreEqual(60m, OperationsLiveQuery.CostInPeriodSar(1m, "Car"));
        Assert.AreEqual(Math.Round(800m / 30m, 2, MidpointRounding.AwayFromZero), OperationsLiveQuery.CostInPeriodSar(1m, "Motorcycle"));
        Assert.IsNull(OperationsLiveQuery.CostInPeriodSar(1m, "Van"));
    }

    [TestMethod]
    public void OutOfScopeSponsorClauseIsFalseWhenEmpty()
    {
        var scope = new LiveOperationsQuery.Scope(false, [], [], [], []);
        var (sql, _) = OperationsLiveQuery.PreviewCitiesSql(scope);
        Assert.Contains("1=0", sql, StringComparison.Ordinal);
    }

    sealed class TestHostEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Gate Payroll";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [TestMethod]
    public void PreviewPurchaseInvoicesSqlMatchesInvoiceDateOrRiyadhReceivedAtWithoutStatusFilter()
    {
        var (sql, names) = OperationsLiveQuery.PreviewPurchaseInvoicesSql();
        Assert.Contains("[maintenance].[PurchaseReceipts]", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN [maintenance].[Suppliers]", sql, StringComparison.Ordinal);
        Assert.Contains("pr.[InvoiceDate]", sql, StringComparison.Ordinal);
        Assert.Contains("SWITCHOFFSET(pr.[ReceivedAtUtc], '+03:00')", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("pr.[Status]=", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pr.[Status] =", sql, StringComparison.OrdinalIgnoreCase);
        CollectionAssert.Contains(names.ToList(), "@from");
        CollectionAssert.Contains(names.ToList(), "@to");

        var (detailSql, detailNames) = OperationsLiveQuery.PreviewPurchaseInvoicesSql(receiptId: "18181818-1818-1818-1818-181818181818");
        Assert.Contains("pr.[Id]=@receiptId", detailSql, StringComparison.Ordinal);
        CollectionAssert.DoesNotContain(detailNames.ToList(), "@from");
    }

    [TestMethod]
    public void PurchaseInvoicesApiReturnsAllSyntheticInvoicesMatchingPeriodAcrossDatesAndStatuses()
    {
        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 30);
        Assert.IsTrue(OperationsLiveQuery.PurchaseInvoiceInPeriod(new DateOnly(2026, 11, 15), null, from, to));
        Assert.IsTrue(OperationsLiveQuery.PurchaseInvoiceInPeriod(new DateOnly(2026, 10, 28), new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(3)), from, to));
        Assert.IsTrue(OperationsLiveQuery.PurchaseInvoiceInPeriod(new DateOnly(2026, 10, 31), new DateTimeOffset(2026, 10, 31, 22, 0, 0, TimeSpan.Zero), from, to));
        Assert.IsFalse(OperationsLiveQuery.PurchaseInvoiceInPeriod(new DateOnly(2026, 10, 15), new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.FromHours(3)), from, to));
        Assert.AreEqual("مرحّل", OperationsLiveQuery.PurchaseStatusLabel("1"));
        Assert.AreEqual("معكوس", OperationsLiveQuery.PurchaseStatusLabel("2"));

        using var f = new PayFixture();
        f.Store.Write((c, tx) =>
        {
            Store.Save(c, "purchase_invoice", new JsonObject
            {
                ["id"] = "inv-1", ["receiptNumber"] = "BILL-001", ["supplierInvoiceNumber"] = "SUP-101",
                ["invoiceDate"] = "2026-11-15", ["receivedAt"] = "2026-11-15T10:00:00+03:00",
                ["subtotal"] = 100m, ["taxAmount"] = 15m, ["totalAmount"] = 115m, ["status"] = "1", ["supplierNameAr"] = "مورد تجريبي أ"
            }, tx);
            Store.Save(c, "purchase_invoice", new JsonObject
            {
                ["id"] = "inv-2", ["receiptNumber"] = "BILL-002", ["supplierInvoiceNumber"] = "SUP-102",
                ["invoiceDate"] = "2026-10-28", ["receivedAt"] = "2026-11-02T09:00:00+03:00",
                ["subtotal"] = 200m, ["taxAmount"] = 30m, ["totalAmount"] = 230m, ["status"] = "Posted", ["supplierNameAr"] = "مورد تجريبي ب"
            }, tx);
            Store.Save(c, "purchase_invoice", new JsonObject
            {
                ["id"] = "inv-3", ["receiptNumber"] = "BILL-003", ["supplierInvoiceNumber"] = "SUP-103",
                ["invoiceDate"] = "2026-11-10", ["receivedAt"] = "2026-12-02T09:00:00+03:00",
                ["subtotal"] = 300m, ["taxAmount"] = 45m, ["totalAmount"] = 345m, ["status"] = "2", ["supplierNameAr"] = "مورد تجريبي ج"
            }, tx);
            Store.Save(c, "purchase_invoice", new JsonObject
            {
                ["id"] = "inv-4", ["receiptNumber"] = "BILL-004", ["supplierInvoiceNumber"] = "SUP-104",
                ["invoiceDate"] = "2026-10-31", ["receivedAt"] = "2026-10-31T22:00:00Z",
                ["subtotal"] = 400m, ["taxAmount"] = 60m, ["totalAmount"] = 460m, ["status"] = "1", ["supplierNameAr"] = "مورد تجريبي د"
            }, tx);
            Store.Save(c, "purchase_invoice", new JsonObject
            {
                ["id"] = "inv-out", ["receiptNumber"] = "BILL-005", ["supplierInvoiceNumber"] = "SUP-105",
                ["invoiceDate"] = "2026-10-10", ["receivedAt"] = "2026-10-12T09:00:00+03:00",
                ["subtotal"] = 500m, ["taxAmount"] = 75m, ["totalAmount"] = 575m, ["status"] = "1", ["supplierNameAr"] = "مورد خارج الفترة"
            }, tx);
            Store.Save(c, "purchase_invoice", new JsonObject
            {
                ["id"] = "inv-del", ["receiptNumber"] = "BILL-006", ["supplierInvoiceNumber"] = "SUP-106",
                ["invoiceDate"] = "2026-11-20", ["receivedAt"] = "2026-11-20T09:00:00+03:00",
                ["subtotal"] = 600m, ["taxAmount"] = 90m, ["totalAmount"] = 690m, ["status"] = "1", ["supplierNameAr"] = "مورد محذوف", ["isDeleted"] = true
            }, tx);
            return true;
        });

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Operations:Source"] = "store" }).Build();
        var env = new TestHostEnv("Development");
        var ctx = new DefaultHttpContext { User = PayFixture.Maker };
        ctx.Request.QueryString = new QueryString("?from=2026-11-01&to=2026-11-30&page=1&pageSize=25");
        var method = typeof(OperationsLiveApi).GetMethod("Purchases", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new AssertFailedException("missing Purchases handler");
        var result = (JsonObject)method.Invoke(null, [f.Store, cfg, env, ctx])!;
        var items = result["items"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.AreEqual(4L, result["total"]!.GetValue<long>());
        Assert.AreEqual(4, items.Count);
        Assert.AreEqual(1150m, result["summary"]!["totalAmount"]!.GetValue<decimal>());
        CollectionAssert.AreEquivalent(new[] { "inv-1", "inv-2", "inv-3", "inv-4" }, items.Select(i => i["id"]!.GetValue<string>()).ToList());
        Assert.IsTrue(items.Any(i => i["status"]!.GetValue<string>() == "مرحّل"));
        Assert.IsTrue(items.Any(i => i["status"]!.GetValue<string>() == "معكوس"));
    }
}
