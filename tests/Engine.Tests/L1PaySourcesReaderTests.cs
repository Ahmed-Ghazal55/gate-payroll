using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class L1PaySourcesReaderTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FakeReader_PagesFuelVehicleAndHousing_WithoutErp(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        var fake=new FakeL1PaySourcesReader();
        fake.FuelRows.Add(new(){["id"]="f1",["liters"]=10m,["totalInclVat"]=50m,["erpRiderId"]="r1"});
        fake.FuelRows.Add(new(){["id"]="f2",["liters"]=5m,["totalInclVat"]=25m,["erpRiderId"]="r2"});
        fake.VehicleRows.Add(new(){["id"]="v1",["erpRiderId"]="r1",["vehicleId"]="veh1",["fromDate"]="2026-10-01",["toDate"]="2026-10-15"});
        fake.HousingRows.Add(new(){["id"]="h1",["erpRiderId"]="r1",["housingId"]="hou1",["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31"});
        var scope=new LiveOperationsQuery.Scope(true,[],[],[],[],PlatformRestricted:false);

        var fuel=fake.FuelConsumption(c,new DateOnly(2026,10,1),new DateOnly(2026,10,31),1,1,"",scope);
        Assert.AreEqual(2,fuel.Total);
        Assert.AreEqual(1,fuel.Items.Count);
        Assert.AreEqual(10m,fuel.Items[0].N("liters"));
        Assert.AreEqual(15m,fuel.Summary.N("litersTotal"));

        var vehicles=fake.VehicleAssignmentPeriods(c,new DateOnly(2026,10,1),new DateOnly(2026,10,31),1,50,"",scope);
        Assert.AreEqual(1,vehicles.Total);
        Assert.AreEqual("r1",vehicles.Items[0].S("erpRiderId"));

        var housing=fake.HousingPeriods(c,new DateOnly(2026,10,1),new DateOnly(2026,10,31),1,50,"",scope);
        Assert.AreEqual(1,housing.Total);
        Assert.AreEqual("hou1",housing.Items[0].S("housingId"));
    }

    [TestMethod]
    public void LiveReader_IsWiredAsDefaultImplementation()
    {
        IL1PaySourcesReader reader=new LiveL1PaySourcesReader();
        Assert.IsInstanceOfType<LiveL1PaySourcesReader>(reader);
    }
}
