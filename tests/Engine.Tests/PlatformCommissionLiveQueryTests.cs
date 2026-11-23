using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ErpImport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class PlatformCommissionLiveQueryTests
{
    [TestMethod]
    public void ProgramForApp_MapsJahezAndChefz()
    {
        Assert.AreEqual(PlatformCommissionLiveQuery.ProgramJahez, PlatformCommissionLiveQuery.ProgramForApp("jahez"));
        Assert.AreEqual(PlatformCommissionLiveQuery.ProgramChefzFullTime, PlatformCommissionLiveQuery.ProgramForApp("chefz"));
        Assert.AreEqual(PlatformCommissionLiveQuery.ProgramChefzFullTime, PlatformCommissionLiveQuery.ProgramForApp("thechefz"));
        Assert.ThrowsExactly<ImportFailure>(() => PlatformCommissionLiveQuery.ProgramForApp("ninja"));
    }

    [TestMethod]
    public void PreviewRatesSql_IsParameterizedAndTargetsOmarTable()
    {
        var (sql, names) = PlatformCommissionLiveQuery.PreviewRatesSql(PlatformCommissionLiveQuery.ProgramJahez);
        Assert.IsTrue(sql.Contains("[app].[PlatformDailyCommissionRates]", StringComparison.Ordinal));
        Assert.IsTrue(sql.Contains("@program", StringComparison.Ordinal));
        Assert.IsTrue(sql.Contains("[platform].[GlobalCities]", StringComparison.Ordinal));
        CollectionAssert.Contains(names.ToList(), "@program");
        Assert.IsFalse(Regex.IsMatch(sql, @"\bINSERT\b", RegexOptions.IgnoreCase));
        Assert.IsFalse(Regex.IsMatch(sql, @"\bUPDATE\b", RegexOptions.IgnoreCase));
        Assert.IsFalse(Regex.IsMatch(sql, @"\bDELETE\b", RegexOptions.IgnoreCase)); // IsDeleted column is fine
    }

    [TestMethod]
    public void FromMappedRates_Jahez_ShowsBothCities_AndMissingWhenAbsent()
    {
        // PII-free fixture: Riyadh only → Jeddah missing.
        var rates = new[]
        {
            new JsonObject
            {
                ["id"] = 1,
                ["operatingCityId"] = "city-riyadh",
                ["cityCode"] = "riyadh",
                ["cityNameAr"] = "الرياض",
                ["effectiveFrom"] = "2026-11-01",
                ["dailyRate"] = 20m,
                ["kind"] = "fixed",
                ["kindCode"] = 1
            }
        };
        var result = PlatformCommissionLiveQuery.FromMappedRates("jahez", rates, new DateOnly(2026, 11, 23));
        Assert.AreEqual("jahez", result["app"]!.GetValue<string>());
        Assert.AreEqual(false, result["writable"]!.GetValue<bool>());
        var byCity = result["byCity"]!.AsArray().OfType<JsonObject>().ToArray();
        Assert.AreEqual(2, byCity.Length);
        Assert.IsTrue(byCity.Single(c => c["cityCode"]!.GetValue<string>() == "riyadh")["available"]!.GetValue<bool>());
        Assert.IsFalse(byCity.Single(c => c["cityCode"]!.GetValue<string>() == "jeddah")["available"]!.GetValue<bool>());
        Assert.AreEqual("غير متوفر في نظام عمر", byCity.Single(c => c["cityCode"]!.GetValue<string>() == "jeddah")["message"]!.GetValue<string>());
        Assert.IsTrue(result["schemeRows"]!.AsArray().Count >= 2);
    }

    [TestMethod]
    public void FromMappedRates_Chefz_IncludesFreelancerPercentageRow()
    {
        var rates = new[]
        {
            new JsonObject
            {
                ["id"] = 2,
                ["operatingCityId"] = "city-jeddah",
                ["cityCode"] = "jeddah",
                ["cityNameAr"] = "جدة",
                ["effectiveFrom"] = "2026-11-01",
                ["dailyRate"] = 10m,
                ["kind"] = "percentage",
                ["kindCode"] = 2
            }
        };
        var result = PlatformCommissionLiveQuery.FromMappedRates("chefz", rates, new DateOnly(2026, 11, 23));
        var rows = result["schemeRows"]!.AsArray().OfType<JsonObject>().ToArray();
        Assert.IsTrue(rows.Any(r => r["identifierType"]!.GetValue<string>() == "FullTime"));
        var freelancer = rows.Single(r => r["identifierType"]!.GetValue<string>() == "Freelancer");
        Assert.AreEqual("percentage", freelancer["kind"]!.GetValue<string>());
        Assert.AreEqual(15m, freelancer["rate"]!.GetValue<decimal>());
        Assert.IsTrue(freelancer["available"]!.GetValue<bool>());
    }
}
