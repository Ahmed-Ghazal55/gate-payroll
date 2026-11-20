using ErpImport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class PaySourcesLiveQueryTests
{
    static readonly LiveOperationsQuery.Scope OneCompany = new(false, ["7000000001"], [], [], []);
    static readonly LiveOperationsQuery.Scope All = new(true, [], [], [], []);

    [TestMethod]
    public void PreviewFuelConsumptionSqlPagesAndScopesInSql()
    {
        var (sql, names) = PaySourcesLiveQuery.PreviewFuelConsumptionSql(OneCompany);
        Assert.Contains("[app].[FuelCardMonthlyUsages]", sql, StringComparison.Ordinal);
        Assert.Contains("[app].[FuelCards]", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET @skip", sql, StringComparison.Ordinal);
        Assert.Contains("FETCH NEXT @take", sql, StringComparison.Ordinal);
        Assert.Contains("@cr", string.Join(',', names), StringComparison.Ordinal);
        Assert.DoesNotContain("7000000001", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", sql, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void PreviewVehicleAssignmentPeriodsSqlUsesRiyadhDayAndRealRider()
    {
        var (sql, names) = PaySourcesLiveQuery.PreviewVehicleAssignmentPeriodsSql(OneCompany);
        Assert.Contains("[app].[RiderVehicleAssignments]", sql, StringComparison.Ordinal);
        Assert.Contains("IsRealRider", sql, StringComparison.Ordinal);
        Assert.Contains("RealRiders", sql, StringComparison.Ordinal);
        Assert.Contains("SWITCHOFFSET", sql, StringComparison.Ordinal);
        Assert.Contains("@from", string.Join(',', names), StringComparison.Ordinal);
        Assert.Contains("@to", string.Join(',', names), StringComparison.Ordinal);
        Assert.DoesNotContain("].[Vin]", sql, StringComparison.Ordinal);
    }

    [TestMethod]
    public void PreviewHousingPeriodsSqlScopesByEmployeeSponsor()
    {
        var (sql, names) = PaySourcesLiveQuery.PreviewHousingPeriodsSql(OneCompany);
        Assert.Contains("[app].[HousingResidencePeriods]", sql, StringComparison.Ordinal);
        Assert.Contains("[app].[HousingRooms]", sql, StringComparison.Ordinal);
        Assert.Contains("DaysInSelectedPeriod", sql, StringComparison.Ordinal);
        Assert.Contains("@cr", string.Join(',', names), StringComparison.Ordinal);
        Assert.DoesNotContain("7000000001", sql, StringComparison.Ordinal);
    }

    [TestMethod]
    public void EmptyCompanyScopeBecomesFalsePredicate()
    {
        var empty = new LiveOperationsQuery.Scope(false, [], [], [], []);
        var (fuel, _) = PaySourcesLiveQuery.PreviewFuelConsumptionSql(empty);
        Assert.Contains("1=0", fuel, StringComparison.Ordinal);
        var (vehicle, _) = PaySourcesLiveQuery.PreviewVehicleAssignmentPeriodsSql(empty);
        Assert.Contains("1=0", vehicle, StringComparison.Ordinal);
        var (housing, _) = PaySourcesLiveQuery.PreviewHousingPeriodsSql(empty);
        Assert.Contains("1=0", housing, StringComparison.Ordinal);
    }
}
