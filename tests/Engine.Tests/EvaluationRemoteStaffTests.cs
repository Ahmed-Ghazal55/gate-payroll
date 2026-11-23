using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class EvaluationRemoteStaffTests
{
    static JsonObject Staff(string name, params string[] cities) => new()
    {
        ["name"] = name, ["cities"] = new JsonArray(cities.Select(c => (JsonNode?)c).ToArray()),
        ["departments"] = new JsonArray("Synthetic department"), ["roles"] = new JsonArray("Synthetic role"),
        ["apps"] = new JsonArray("Ninja"), ["active"] = true
    };
    static JsonObject Remote(string name, string office) { var s = Staff(name, office); s["allCities"] = true; return s; }
    static string[] Ids(JsonObject schema) => schema["staff"]!.AsArray().OfType<JsonObject>().Select(s => s.S("id")).ToArray();

    [TestMethod]
    public void RemoteOfficeIsNotAWorkCityAndItsStaffServeEveryCity()
    {
        using var f = new PayFixture();
        var round = f.Store.Write((c, tx) =>
        {
            // A multi-city lead also lists the remote office; the office must still not become a work city.
            EvaluationSurvey.ImportStaff(c, tx, PayFixture.Maker, new() { ["staff"] = new JsonArray(
                Staff("Synthetic A", "CityA"), Staff("Synthetic lead", "CityA", "CityB", "Remote office"), Remote("Synthetic support", "Remote office")) });
            return EvaluationSurvey.Transition(c, tx, PayFixture.Maker, EvaluationSurvey.CreateRound(c, tx, PayFixture.Maker, new() { ["title"] = "Synthetic" }).S("id"), "open");
        });
        CollectionAssert.AreEquivalent(new[] { "CityA", "CityB" }, round["cities"]!.AsArray().Select(x => x!.ToString()).ToArray());
        Assert.Throws<ApiError>(() => EvaluationSurvey.Schema(round, "Remote office"));
        var cityB = EvaluationSurvey.Schema(round, "CityB");
        Assert.HasCount(2, Ids(cityB));
        var support = cityB["staff"]!.AsArray().OfType<JsonObject>().Single(s => s.S("name") == "Synthetic support");
        CollectionAssert.AreEquivalent(new[] { "CityA", "CityB" }, support["cities"]!.AsArray().Select(x => x!.ToString()).ToArray());
        Assert.HasCount(3, Ids(EvaluationSurvey.Schema(round, "CityA")));
    }

    [TestMethod]
    public void RefreshStaffUpdatesAnOpenRoundFromTheCurrentList()
    {
        using var f = new PayFixture();
        var round = f.Store.Write((c, tx) =>
        {
            EvaluationSurvey.ImportStaff(c, tx, PayFixture.Maker, new() { ["staff"] = new JsonArray(Staff("Synthetic A", "CityA"), Staff("Synthetic support", "Remote office")) });
            return EvaluationSurvey.Transition(c, tx, PayFixture.Maker, EvaluationSurvey.CreateRound(c, tx, PayFixture.Maker, new() { ["title"] = "Synthetic" }).S("id"), "open");
        });
        Assert.HasCount(2, round["cities"]!.AsArray());
        var refreshed = f.Store.Write((c, tx) =>
        {
            EvaluationSurvey.ImportStaff(c, tx, PayFixture.Maker, new() { ["staff"] = new JsonArray(Remote("Synthetic support", "Remote office")) });
            return EvaluationSurvey.RefreshStaff(c, tx, PayFixture.Maker, round.S("id"));
        });
        CollectionAssert.AreEqual(new[] { "CityA" }, refreshed["cities"]!.AsArray().Select(x => x!.ToString()).ToArray());
        Assert.AreEqual("open", refreshed.S("status"));
        Assert.HasCount(2, Ids(EvaluationSurvey.Schema(refreshed, "CityA")));
        f.Store.Write((c, tx) => EvaluationSurvey.Transition(c, tx, PayFixture.Maker, round.S("id"), "closed"));
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.RefreshStaff(c, tx, PayFixture.Maker, round.S("id"))));
    }
}
