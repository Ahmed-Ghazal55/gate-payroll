using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class EvaluationEv6Tests
{
    static readonly ClaimsPrincipal Admin = PayFixture.Maker;

    static JsonObject Person(string name, string dept, string city = "Synthetic city", string[]? apps = null, bool allApps = false, bool allCities = false, JsonObject? appCities = null) => new()
    {
        ["name"] = name,
        ["cities"] = new JsonArray(city),
        ["departments"] = new JsonArray(dept),
        ["roles"] = new JsonArray("Synthetic role"),
        ["apps"] = new JsonArray((apps ?? ["Ninja"]).Select(a => (JsonNode?)a).ToArray()),
        ["allApps"] = allApps,
        ["allCities"] = allCities,
        ["active"] = true,
        ["appCities"] = appCities ?? new JsonObject()
    };

    static JsonObject OpenSectionedRound(PayFixture f, params JsonObject[] people) => f.Store.Write((c, tx) =>
    {
        EvaluationSurvey.ImportStaff(c, tx, Admin, new()
        {
            ["staff"] = new JsonArray(people.Select(p => (JsonNode?)p).ToArray())
        });
        var round = EvaluationSurvey.CreateRound(c, tx, Admin, new() { ["title"] = "Synthetic EV6 round" });
        return EvaluationSurvey.Transition(c, tx, Admin, round.S("id"), "open");
    });

    static JsonObject Section(JsonObject round, string key) =>
        round["sections"]!.AsArray().OfType<JsonObject>().Single(s => s.S("key") == key);

    [TestMethod]
    public void R1_SchemaNoticeHasThreeConfidentialityLinesAndComplaintReminder()
    {
        using var f = new PayFixture();
        var round = OpenSectionedRound(f,
            Person("Synthetic ops", EvaluationSurvey.DeptOperations, apps: ["Ninja"]));
        var schema = f.Store.Read(c => EvaluationSurvey.Schema(EvaluationSurvey.ByToken(c, Section(round, "app:Ninja").S("sharingToken"))));

        Assert.IsTrue(schema.S("noticeAr").Contains("تقييمك وشكواك سرية تمامًا"));
        Assert.IsTrue(schema.S("noticeAr").Contains("لا يرى المشرف أو الموظف الذي تقيّمه اسمك ولا إجابتك"));
        Assert.IsTrue(schema.S("noticeAr").Contains("لن يُستخدم أي تقييم أو شكوى ضدك بأي شكل"));
        Assert.IsTrue(schema.S("noticeEn").Contains("completely confidential", StringComparison.OrdinalIgnoreCase)
            || schema.S("noticeEn").Contains("confidential", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(schema.S("noticeUr").Length > 20);
        Assert.AreEqual("شكواك سرية ولن تُستخدم ضدك.", schema.S("complaintConfidentialityReminderAr"));
        Assert.IsTrue(schema.S("complaintConfidentialityReminderEn").Length > 5);
        Assert.IsTrue(schema.S("complaintConfidentialityReminderUr").Length > 5);
        var lines = schema["noticeLines"]!.AsArray().OfType<JsonObject>().ToArray();
        Assert.HasCount(3, lines);
        Assert.AreEqual("تقييمك وشكواك سرية تمامًا.", lines[0].S("ar"));
    }
}
