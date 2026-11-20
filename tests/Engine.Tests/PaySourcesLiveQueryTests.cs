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
    public void EmptyCompanyScopeBecomesFalsePredicate()
    {
        var empty = new LiveOperationsQuery.Scope(false, [], [], [], []);
        var (fuel, _) = PaySourcesLiveQuery.PreviewFuelConsumptionSql(empty);
        Assert.Contains("1=0", fuel, StringComparison.Ordinal);
    }
}
