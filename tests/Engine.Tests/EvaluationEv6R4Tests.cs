using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class EvaluationEv6R4Tests
{
    static readonly ClaimsPrincipal Admin = PayFixture.Maker;

    static JsonObject Person(string name, string dept, string city = "Synthetic city", string[]? apps = null, JsonObject? appCities = null) => new()
    {
        ["name"] = name,
        ["cities"] = new JsonArray(city),
        ["departments"] = new JsonArray(dept),
        ["roles"] = new JsonArray("Synthetic role"),
        ["apps"] = new JsonArray((apps ?? ["Ninja"]).Select(a => (JsonNode?)a).ToArray()),
        ["allApps"] = false,
        ["allCities"] = false,
        ["active"] = true,
        ["appCities"] = appCities ?? new JsonObject()
    };

    static JsonObject OpenSectionedRound(PayFixture f, params JsonObject[] people) => f.Store.Write((c, tx) =>
    {
        EvaluationSurvey.ImportStaff(c, tx, Admin, new()
        {
            ["staff"] = new JsonArray(people.Select(p => (JsonNode?)p).ToArray())
        });
        var round = EvaluationSurvey.CreateRound(c, tx, Admin, new() { ["title"] = "Synthetic EV6 R4 round" });
        return EvaluationSurvey.Transition(c, tx, Admin, round.S("id"), "open");
    });

    static JsonObject Section(JsonObject round, string key) =>
        round["sections"]!.AsArray().OfType<JsonObject>().Single(s => s.S("key") == key);

    [TestMethod]
    public void R4_AppCitiesLimitsAppSectionAndLeavesDeptSectionsAlone()
    {
        using var f = new PayFixture();
        var round = OpenSectionedRound(f,
            Person("Synthetic mover", EvaluationSurvey.DeptOperations, city: "Synthetic city", apps: ["Ninja"],
                appCities: new JsonObject { ["Ninja"] = new JsonArray("Other city") }),
            Person("Synthetic movement", EvaluationSurvey.DeptMovement, city: "Synthetic city", apps: []));
        var ninja = Section(round, "app:Ninja");
        var schemaOther = f.Store.Read(c => EvaluationSurvey.Schema(EvaluationSurvey.ByToken(c, ninja.S("sharingToken")), "Other city"));
        var schemaSynth = f.Store.Read(c => EvaluationSurvey.Schema(EvaluationSurvey.ByToken(c, ninja.S("sharingToken")), "Synthetic city"));
        CollectionAssert.Contains(schemaOther["staff"]!.AsArray().OfType<JsonObject>().Select(s => s.S("name")).ToArray(), "Synthetic mover");
        CollectionAssert.DoesNotContain(schemaSynth["staff"]!.AsArray().OfType<JsonObject>().Select(s => s.S("name")).ToArray(), "Synthetic mover");
        // The server must expect exactly the cards the schema shows: a rider in the card's own city submits WITHOUT the limited card.
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, ninja.S("sharingToken"), EvaluationEv6Tests.AppReply(ninja, "2000000391", city: "Synthetic city")));
        var moverId = f.Store.Read(c => Store.List(c, EvaluationSurvey.StaffKind)).Single(s => s.S("name") == "Synthetic mover").S("id");
        var stored = f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)).Single();
        Assert.IsFalse(stored["staffAnswers"]!.AsArray().OfType<JsonObject>().Any(a => a.S("staffId") == moverId));

        var movement = Section(round, "dept:movement");
        var moveSchema = f.Store.Read(c => EvaluationSurvey.Schema(EvaluationSurvey.ByToken(c, movement.S("sharingToken")), "Synthetic city"));
        CollectionAssert.Contains(moveSchema["staff"]!.AsArray().OfType<JsonObject>().Select(s => s.S("name")).ToArray(), "Synthetic movement");

        var put = f.Store.Write((c, tx) =>
        {
            var staff = Store.List(c, EvaluationSurvey.StaffKind, tx).Single(s => s.S("name") == "Synthetic mover");
            return EvaluationSurvey.PutStaff(c, tx, Admin, staff.S("id"), new()
            {
                ["name"] = staff.S("name"),
                ["cities"] = new JsonArray("Synthetic city", "Other city"),
                ["departments"] = new JsonArray(EvaluationSurvey.DeptOperations),
                ["roles"] = new JsonArray("Synthetic role"),
                ["apps"] = new JsonArray("Ninja"),
                ["appCities"] = new JsonObject { ["Ninja"] = new JsonArray("Synthetic city") },
                ["active"] = true
            });
        });
        Assert.AreEqual(1, put["appCities"]!["Ninja"]!.AsArray().Count);
        Assert.AreEqual("Synthetic city", put["appCities"]!["Ninja"]![0]!.GetValue<string>());
    }
}
