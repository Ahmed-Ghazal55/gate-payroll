using System.Globalization;
using ErpImport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
}
