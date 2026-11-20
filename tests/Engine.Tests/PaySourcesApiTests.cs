using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class PaySourcesApiTests
{
    [TestMethod]
    public void StoreModeFuelUsageKindIsAvailableForPaySources()
    {
        using var f = new PayFixture();
        string company = FinancialCore.Sponsors[0].Id;
        f.Store.Write((c, tx) =>
        {
            Store.Save(c, "rider", new() { ["id"] = "2123456789", ["sponsorId"] = company }, tx);
            Store.Save(c, "fuel_usage", new()
            {
                ["id"] = "syn-fuel-1",
                ["riderId"] = "2123456789",
                ["companyId"] = company,
                ["month"] = "2026-10",
                ["period"] = "2026-10",
                ["ReportMonth"] = "2026-10-01",
                ["amount"] = 80,
                ["totalAmount"] = 80,
                ["liters"] = 40,
                ["fuelCardId"] = "card-1"
            }, tx);
            return true;
        });

        var listed = f.Store.Read(c => Operations.List(c, PayFixture.Maker, "fuel_usage").ToList());
        Assert.AreEqual(1, listed.Count);
        Assert.AreEqual(80m, listed[0].N("totalAmount", listed[0].N("amount")));
        Assert.AreEqual(40m, listed[0].N("liters"));
        Assert.IsTrue(AccessPolicy.Allowed(PayFixture.Maker, "GET", "/api/pay/sources/fuel-consumption"));
    }

    [TestMethod]
    public void StoreModeVehicleAndHousingKindsAreAvailableForPaySources()
    {
        using var f = new PayFixture();
        string company = FinancialCore.Sponsors[0].Id;
        f.Store.Write((c, tx) =>
        {
            Store.Save(c, "rider", new() { ["id"] = "2123456789", ["sponsorId"] = company }, tx);
            Store.Save(c, "vehicle", new() { ["id"] = "syn-vehicle", ["companyId"] = company, ["assetNumber"] = "Synthetic" }, tx);
            Store.Save(c, "vehicle_assignment", new()
            {
                ["id"] = "syn-assign",
                ["vehicleId"] = "syn-vehicle",
                ["riderId"] = "2123456789",
                ["startedAt"] = "2026-10-01",
                ["endedAt"] = "2026-10-31",
                ["isRealRider"] = true
            }, tx);
            Store.Save(c, "housing", new() { ["id"] = "syn-housing", ["companyId"] = company }, tx);
            Store.Save(c, "housing_residence", new()
            {
                ["id"] = "syn-res",
                ["housingId"] = "syn-housing",
                ["riderId"] = "2123456789",
                ["effectiveFrom"] = "2026-10-01",
                ["effectiveTo"] = "2026-10-15"
            }, tx);
            return true;
        });

        Assert.AreEqual(1, f.Store.Read(c => Operations.List(c, PayFixture.Maker, "vehicle_assignment").Count()));
        Assert.AreEqual(1, f.Store.Read(c => Operations.List(c, PayFixture.Maker, "housing_residence").Count()));
        Assert.IsTrue(AccessPolicy.Allowed(PayFixture.Maker, "GET", "/api/pay/sources/vehicle-assignment-periods"));
        Assert.IsTrue(AccessPolicy.Allowed(PayFixture.Maker, "GET", "/api/pay/sources/housing-periods"));
        Assert.IsTrue(AccessPolicy.Allowed(PayFixture.Maker, "GET", "/api/pay/sources/keeta-eligibility"));
    }
}
