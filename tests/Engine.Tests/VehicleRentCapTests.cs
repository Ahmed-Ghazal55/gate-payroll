using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class VehicleRentCapTests
{
    static JsonObject Values()=>PaySettings.Defaults()["values"]!.AsObject();
    [TestMethod]
    [DataRow("company_car",40,1800)]
    [DataRow("company_bike",99,800)]
    public void VehicleChargeNeverExceedsTypeMonthlyEvenWhenDaysExceedMonth(string type,int days,int expected)
        =>Assert.AreEqual((decimal)expected,PayEvaluator.VehicleCharge(type,days,"2026-10",Values()));
    [TestMethod]
    public void CapVehicleRentTotalClampsSharedVehicleChargesToMonthlyTypeValue()
    {
        var v=Values();Assert.AreEqual(800m,PayEvaluator.CapVehicleRentTotal(1600m,"company_bike",v));
        Assert.AreEqual(400m,PayEvaluator.CapVehicleRentTotal(400m,"company_bike",v));
        Assert.AreEqual(0m,PayEvaluator.CapVehicleRentTotal(-1m,"company_car",v));
    }
    [TestMethod]
    public void TwoRidersSharingOneBikeCannotTogetherExceedMonthlyRent()
    {
        var (f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            foreach(var invoice in input["invoices"]!.AsArray())foreach(var share in invoice!["accounts"]![0]!["riders"]!.AsArray())share!["category"]="sponsored_company_bike";
            foreach(string app in new[]{"keeta","hs"})
            {
                var data=PayFixture.Scheme("percent_monthly_with_vehicle");
                var scheme=f.Approve("pay_scheme",app,f.Draft("pay_scheme",app,data,"vehicle-cap"));
                f.Store.Write((c,tx)=>{foreach(int i in new[]{1,2}){var choice=Store.Get(c,"synthetic-choice-"+app+i,"pay_assignment",tx)!;choice["schemeId"]=scheme.S("id");Store.Save(c,"pay_assignment",choice,tx);}return true;});
            }
            // Same bike, full-month custody for both riders → without §38.4 each would owe 800.
            f.Store.Write((c,tx)=>{
                Store.Save(c,"vehicle",new(){["id"]="synthetic-shared-bike",["vehicleType"]=1,["ownershipType"]=1},tx);
                foreach(int i in new[]{1,2})Store.Save(c,"vehicle_assignment",new(){["id"]="synthetic-shared-bike-"+i,["vehicleId"]="synthetic-shared-bike",["riderId"]="2"+i.ToString("D9"),["startedAt"]="2026-10-01T00:00:00+03:00",["endedAt"]="2026-11-01T00:00:00+03:00"},tx);
                return true;
            });
            var cycle=PayCycleTests.Through(f,input);
            var lines=f.Store.Read(c=>PayCycles.Lines(c,null,cycle.S("id")));
            decimal total=lines.Sum(l=>l.N("allocatedVehicleCharge"));
            Assert.AreEqual(800m,total,"§38.4 shared vehicle total rent ≤ type monthly");
            Assert.AreEqual(400m,lines.Where(l=>l.S("rider")=="2000000001").Sum(l=>l.N("allocatedVehicleCharge")));
            Assert.AreEqual(400m,lines.Where(l=>l.S("rider")=="2000000002").Sum(l=>l.N("allocatedVehicleCharge")));
        }
    }
}
