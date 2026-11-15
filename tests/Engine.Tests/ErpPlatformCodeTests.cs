using ErpImport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class ErpPlatformCodeTests
{
    [TestMethod]
    public void MapsPayrollAppsToExactErpCodes()
    {
        Assert.AreEqual("NINJA",ErpPlatformCode.Primary("ninja"));
        Assert.AreEqual("KEETA",ErpPlatformCode.Primary("keeta"));
        Assert.AreEqual("HUNGER",ErpPlatformCode.Primary("hs"));
        Assert.AreEqual("SHIFTZ",ErpPlatformCode.Primary("chefz"));
        Assert.AreEqual("JAHEZ",ErpPlatformCode.Primary("jahez"));
        Assert.AreEqual("AMAZON",ErpPlatformCode.Primary("amazon"));
        CollectionAssert.AreEquivalent(new[]{"SHIFTZ","CHEFZ","THECHEFZ","CHEFSZ"},ErpPlatformCode.Accept("chefz").ToArray());
    }

    [TestMethod]
    public void PayPlatformsAppAcceptsErpCodes()
    {
        Assert.AreEqual("hs",PayPlatforms.App("HUNGER"));
        Assert.AreEqual("chefz",PayPlatforms.App("SHIFTZ"));
        Assert.AreEqual("chefz",PayPlatforms.App("CHEFZ"));
        Assert.AreEqual("chefz",PayPlatforms.App("THECHEFZ"));
        Assert.AreEqual("chefz",PayPlatforms.App("CHEFSZ"));
        Assert.AreEqual("ninja",PayPlatforms.App("NINJA"));
    }

    [TestMethod]
    public void UnknownAppIsErrorNotPassThrough()
    {
        Assert.ThrowsExactly<ImportFailure>(()=>ErpPlatformCode.Primary("unknown-app"));
        Assert.ThrowsExactly<ApiError>(()=>PayPlatforms.App("unknown-app"));
    }

    [TestMethod]
    public void CapturePeriodSqlUsesPerKindDateColumns()
    {
        var page=new LiveOperationsQuery.PageRequest(1,25,"id","",0,25,"","2026-10-01","2026-10-31","ninja");
        var scope=new LiveOperationsQuery.Scope(true,[],[],[],[]);
        StringAssert.Contains(LiveOperationsQuery.PreviewSql("ninja_daily",scope,page).CountSql,"[ReportDate]");
        StringAssert.Contains(LiveOperationsQuery.PreviewSql("vehicle_assignment",scope,page).CountSql,"[StartedAtUtc]");
        StringAssert.Contains(LiveOperationsQuery.PreviewSql("vehicle_assignment",scope,page).CountSql,"DATEADD(hour,3");
        StringAssert.Contains(LiveOperationsQuery.PreviewSql("jahez_daily_order",scope,page).CountSql,"[Date]");
        StringAssert.Contains(LiveOperationsQuery.PreviewSql("platform_day_usage",scope,page).CountSql,"[BusinessDate]");
        Assert.IsFalse(LiveOperationsQuery.PreviewSql("ninja_daily",scope,page).CountSql.Contains("[Date]",StringComparison.Ordinal));
    }

    [TestMethod]
    public void PlatformAppFilterUsesErpCodesIncludingChefzAliases()
    {
        var page=new LiveOperationsQuery.PageRequest(1,25,"id","",0,25,"","","","chefz");
        var scope=new LiveOperationsQuery.Scope(true,[],[],[],[]);
        string sql=LiveOperationsQuery.PreviewSql("platform_account",scope,page).CountSql;
        StringAssert.Contains(sql,"cp.[Code] IN");
        StringAssert.Contains(sql,"@pcap");
    }
}
