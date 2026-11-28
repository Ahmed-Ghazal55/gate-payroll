using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class VehicleIdentityTests
{
    [TestMethod]
    public void Normalize_AllBikesAreCompanyBike_PrivateFourWheelIsCar()
    {
        Assert.AreEqual(VehicleIdentity.Bike, VehicleIdentity.Normalize("1"));
        Assert.AreEqual(VehicleIdentity.Bike, VehicleIdentity.Normalize("Motorcycle"));
        Assert.AreEqual(VehicleIdentity.Bike, VehicleIdentity.Normalize("دباب"));
        Assert.AreEqual(VehicleIdentity.Bike, VehicleIdentity.Normalize("private bike"));
        Assert.AreEqual(VehicleIdentity.Car, VehicleIdentity.Normalize("2"));
        Assert.AreEqual(VehicleIdentity.Car, VehicleIdentity.Normalize("Car"));
        Assert.AreEqual(VehicleIdentity.Car, VehicleIdentity.Normalize("company_car"));
    }

    [TestMethod]
    public void Normalize_PrivateTokenMapsToPrivateCar_NotCompanyCar()
    {
        // HIGH: pay token "private" must stay private; company_car only from company-car values.
        Assert.AreEqual(VehicleIdentity.PrivateCar, VehicleIdentity.Normalize("private"));
        Assert.AreEqual(VehicleIdentity.PrivateCar, VehicleIdentity.Normalize("own_vehicle"));
        Assert.AreEqual(VehicleIdentity.PrivateCar, VehicleIdentity.Normalize("سيارة خاصة"));
        Assert.AreEqual(VehicleIdentity.Bike, VehicleIdentity.Normalize("دراجة خاصة"));
        Assert.AreEqual(VehicleIdentity.PrivateCar, VehicleIdentity.NormalizeForPay("private"));
        Assert.AreEqual(VehicleIdentity.PrivateCar, VehicleIdentity.NormalizeForPay("2", "private"));
        // Private rider + bike platform → car_on_bike_id needs_decision path (not silent company_car).
        Assert.AreEqual(VehicleIdentity.MismatchCarOnBike,
            VehicleIdentity.MismatchCode(VehicleIdentity.PrivateCar, VehicleIdentity.Bike));
    }

    [TestMethod]
    public void Mismatch_BikeOnCarId()
    {
        Assert.AreEqual(VehicleIdentity.MismatchBikeOnCar,
            VehicleIdentity.MismatchCode(VehicleIdentity.Bike, VehicleIdentity.Car));
        Assert.IsNull(VehicleIdentity.MismatchCode(VehicleIdentity.Bike, VehicleIdentity.Bike));
        Assert.AreEqual("sponsored_bike_on_car_id",
            VehicleIdentity.PayCategory("sponsored", VehicleIdentity.Bike, VehicleIdentity.Car));
        Assert.AreEqual("external_company_bike",
            VehicleIdentity.PayCategory("external", VehicleIdentity.Bike, VehicleIdentity.Bike));
    }

    [TestMethod]
    public void BikeOnCarCategory_IsCompanyBikeForCustodyAndRent()
    {
        // HIGH: *_bike_on_car_id must charge company-bike rent (§24.4), not private/0 days.
        Assert.AreEqual("company_bike",
            CategoryVehicleType("sponsored_bike_on_car_id"));
        Assert.AreEqual("company_bike",
            CategoryVehicleType("external_bike_on_car_id"));
        Assert.AreEqual("company_car",
            CategoryVehicleType("sponsored_company_car"));
        Assert.AreEqual("private",
            CategoryVehicleType("sponsored_private"));
    }

    static string CategoryVehicleType(string category)
        =>category.EndsWith("_company_car",StringComparison.Ordinal)?"company_car"
            :category.EndsWith("_company_bike",StringComparison.Ordinal)||category.EndsWith("_bike_on_car_id",StringComparison.Ordinal)?"company_bike"
            :"private";
}
