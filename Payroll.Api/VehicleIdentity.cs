using System.Text.RegularExpressions;
namespace Payroll;

/// <summary>
/// Ghazal 2026-11-27: compare rider vehicle vs platform user/ID vehicle.
/// All bikes (company or private) count as company_bike; a private four-wheel vehicle is a car.
/// Bike-on-car-ID is its own scheme cell (different price from bike-on-bike-ID).
/// </summary>
public static class VehicleIdentity
{
    public const string Bike="company_bike",Car="company_car",PrivateCar="private",Unknown="";
    public const string MismatchBikeOnCar="bike_on_car_id",MismatchCarOnBike="car_on_bike_id";
    public static readonly string[] OptionalMismatchCategories=["sponsored_bike_on_car_id","external_bike_on_car_id"];

    public static string Normalize(string? raw)
    {
        if(string.IsNullOrWhiteSpace(raw))return Unknown;
        string v=raw.Trim().ToLowerInvariant();
        // Private bike before generic car/private — a private bike is still company_bike for pay.
        if(v is "private_bike" or "private-bike" or "bike_private" or "bike-private")return Bike;
        if(Regex.IsMatch(v,"private.?bike|bike.?private|دراجة خاصة|دباب خاص"))return Bike;
        if(v is "1" or "motorcycle" or "bike" or "scooter" or "motorbike" or "company_bike")return Bike;
        if(Regex.IsMatch(v,"دباب|دراجة|سكوتر|موتو"))return Bike;
        // Pay token "private" = private car (not company_car). Company car only from company-car values.
        if(v is "private" or "own_vehicle" or "own_car" or "private_car" or "private-car")return PrivateCar;
        if(v is "2" or "car" or "company_car")return Car;
        if(Regex.IsMatch(v,"مركبة خاصة|سيارة خاصة|خاص"))return PrivateCar;
        if(Regex.IsMatch(v,"سيارة|company.?car"))return Car;
        return Unknown;
    }

    /// <summary>
    /// Platform user/ID vehicle. Prefer نوع المركبة alone; when both bike and car words appear
    /// (concatenated rider+ID text), prefer car so bike-on-car-ID stays detectable.
    /// </summary>
    public static string NormalizePlatform(string? raw)
    {
        if(string.IsNullOrWhiteSpace(raw))return Unknown;
        string v=raw.Trim().ToLowerInvariant();
        bool bike=v is "1" or "motorcycle" or "bike" or "scooter" or "motorbike" or "company_bike" or "private_bike" or "private-bike"
            ||Regex.IsMatch(v,"دباب|دراجة|سكوتر|موتو|private.?bike|bike.?private");
        bool car=v is "2" or "car" or "company_car"||Regex.IsMatch(v,"سيارة|company.?car");
        if(bike&&car)return Car;
        return Normalize(raw);
    }

    /// <summary>Private four-wheel → car token private; any bike → company_bike (binding brief).</summary>
    public static string NormalizeForPay(string? riderVehicle,string? ownership="")
    {
        string n=Normalize(riderVehicle);
        if(n==Bike)return Bike;
        if(n==PrivateCar)return PrivateCar;
        if(n==Car)
        {
            string own=(ownership??"").Trim().ToLowerInvariant();
            if(own is "3" or "private" or "third_party" or "خاص")return PrivateCar;
            return Car;
        }
        return n;
    }

    public static string? MismatchCode(string riderNormalized,string platformNormalized)
    {
        if(riderNormalized==Unknown||platformNormalized==Unknown)return null;
        string riderSide=riderNormalized==PrivateCar?Car:riderNormalized;
        string platformSide=platformNormalized==PrivateCar?Car:platformNormalized;
        if(riderSide==platformSide)return null;
        if(riderSide==Bike&&platformSide==Car)return MismatchBikeOnCar;
        if(riderSide==Car&&platformSide==Bike)return MismatchCarOnBike;
        return null;
    }

    public static string PayCategory(string engagementPrefix,string riderNormalized,string? platformNormalized)
    {
        string prefix=engagementPrefix is "sponsored" or "external"?engagementPrefix:"";
        if(prefix=="")return "";
        string mismatch=MismatchCode(riderNormalized,platformNormalized??Unknown)??"";
        if(mismatch==MismatchBikeOnCar)return prefix+"_bike_on_car_id";
        string vehicle=riderNormalized switch{Bike=>"company_bike",Car=>"company_car",PrivateCar=>"private",_=>""};
        return vehicle==""?"":prefix+"_"+vehicle;
    }

    public static string ArabicMismatch(string? code)=>code switch
    {
        MismatchBikeOnCar=>"دباب على يوزر سيارة",
        MismatchCarOnBike=>"سيارة على يوزر دباب",
        _=>""
    };
}
