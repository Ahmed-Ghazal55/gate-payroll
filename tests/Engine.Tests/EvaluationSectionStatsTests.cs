using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>
/// EV5 S2 hand-computed fixtures:
///   Movement section: one rider rates movement staff all-8 → staff avg 8.00
///   Ninja section: same rider rates ops staff all-6; finance overall 4; responsiveness 5; accuracy 3
///   Combined ops scorecard: one person, bySection has app:Ninja avg 6.00 only
///   Finance stats for Ninja: overall 4.00, responsiveness 5.00, accuracy 3.00, ratedCount 1
/// </summary>
[TestClass]
public sealed class EvaluationSectionStatsTests
{
    static readonly ClaimsPrincipal Admin = PayFixture.Maker;

    static JsonObject Person(string name, string dept, string[]? apps = null, bool allApps = false) => new()
    {
        ["name"] = name,
        ["cities"] = new JsonArray("Synthetic city"),
        ["departments"] = new JsonArray(dept),
        ["roles"] = new JsonArray("Synthetic role"),
        ["apps"] = new JsonArray((apps ?? []).Select(a => (JsonNode?)a).ToArray()),
        ["allApps"] = allApps,
        ["active"] = true
    };

    static JsonObject Dims(decimal v) => new(EvaluationSurvey.StaffDimensions.Select(k =>
        KeyValuePair.Create<string, JsonNode?>(k, JsonValue.Create(v))));

    static JsonObject Section(JsonObject round, string key) =>
        round["sections"]!.AsArray().OfType<JsonObject>().Single(s => s.S("key") == key);

    static (JsonObject Round, string MovementId, string OpsId) Open(PayFixture f) => f.Store.Write((c, tx) =>
    {
        EvaluationSurvey.ImportStaff(c, tx, Admin, new()
        {
            ["staff"] = new JsonArray(
                Person("Synthetic movement", EvaluationSurvey.DeptMovement),
                Person("Synthetic ops", EvaluationSurvey.DeptOperations, apps: ["Ninja"]))
        });
        var round = EvaluationSurvey.Transition(c, tx, Admin,
            EvaluationSurvey.CreateRound(c, tx, Admin, new() { ["title"] = "Synthetic stats round" }).S("id"), "open");
        string movementId = round["staff"]!.AsArray().OfType<JsonObject>().Single(s => s.S("name") == "Synthetic movement").S("id");
        string opsId = round["staff"]!.AsArray().OfType<JsonObject>().Single(s => s.S("name") == "Synthetic ops").S("id");
        var movement = Section(round, "dept:movement");
        EvaluationSurvey.Submit(c, tx, movement.S("sharingToken"), new()
        {
            ["iqama"] = "2000000201",
            ["name"] = "Synthetic rider",
            ["city"] = "Synthetic city",
            ["nationality"] = "egyptian",
            ["workApps"] = new JsonArray("Ninja"),
            ["staffAnswers"] = new JsonArray(new JsonObject { ["staffId"] = movementId, ["ratings"] = Dims(8) }),
            ["appAnswers"] = new JsonArray()
        });
        var ninja = Section(round, "app:Ninja");
        EvaluationSurvey.Submit(c, tx, ninja.S("sharingToken"), new()
        {
            ["iqama"] = "2000000201",
            ["name"] = "Synthetic rider",
            ["city"] = "Synthetic city",
            ["nationality"] = "egyptian",
            ["workApps"] = new JsonArray("Ninja"),
            ["staffAnswers"] = new JsonArray(new JsonObject { ["staffId"] = opsId, ["ratings"] = Dims(6) }),
            ["appAnswers"] = new JsonArray(new JsonObject { ["app"] = "Ninja", ["overall"] = 4, ["factorRatings"] = new JsonObject { ["orderVolume"] = 2 } }),
            ["financeAnswer"] = new JsonObject
            {
                ["overall"] = 4,
                ["factorRatings"] = new JsonObject { ["responsiveness"] = 5, ["accuracy"] = 3 }
            },
            ["systemsAnswer"] = new JsonObject
            {
                ["overall"] = 3,
                ["factorRatings"] = new JsonObject { ["formsEase"] = 2, ["dataClarity"] = 4 }
            }
        });
        return (EvaluationSurvey.Round(c, round.S("id"), tx), movementId, opsId);
    });

    [TestMethod]
    public void SectionFilterOnResponsesAndStatistics()
    {
        using var f = new PayFixture();
        var (round, _, _) = Open(f);
        var all = f.Store.Read(c => EvaluationSurvey.Responses(c, Admin, round.S("id")));
        Assert.HasCount(2, all);
        var movementOnly = f.Store.Read(c => EvaluationSurvey.Responses(c, Admin, round.S("id"), section: "dept:movement"));
        Assert.HasCount(1, movementOnly);
        Assert.AreEqual("dept:movement", movementOnly[0].S("sectionKey"));
        var stats = f.Store.Read(c => EvaluationSurvey.Statistics(c, Admin, round.S("id"), section: "app:Ninja"));
        Assert.AreEqual(1m, stats.N("totalResponses"));
        Assert.AreEqual(6m, stats["bySupervisor"]![0]!.N("average"));
        Assert.AreEqual(4m, stats["byApp"]!.AsArray().OfType<JsonObject>().Single(a => a.N("ratedCount") == 1).N("average"));
    }

    [TestMethod]
    public void CombinedPersonScorecardHasBySectionBreakdown()
    {
        using var f = new PayFixture();
        var (round, movementId, opsId) = Open(f);
        var card = f.Store.Read(c => EvaluationSurvey.SupervisorScorecard(c, Admin, round.S("id")));
        var ops = card["items"]!.AsArray().OfType<JsonObject>().Single(i => i.S("staffId") == opsId);
        Assert.AreEqual(6m, ops.N("average"));
        var bySection = ops["bySection"]!.AsArray().OfType<JsonObject>().ToArray();
        Assert.HasCount(1, bySection);
        Assert.AreEqual("app:Ninja", bySection[0].S("sectionKey"));
        Assert.AreEqual(6m, bySection[0].N("average"));
        var movement = card["items"]!.AsArray().OfType<JsonObject>().Single(i => i.S("staffId") == movementId);
        Assert.AreEqual(8m, movement.N("average"));
        Assert.AreEqual("dept:movement", movement["bySection"]![0]!.S("sectionKey"));
    }

    [TestMethod]
    public void FinanceCardStatsPerAppUseExactAverages()
    {
        using var f = new PayFixture();
        var (round, _, _) = Open(f);
        var finance = f.Store.Read(c => EvaluationSurvey.FinanceScorecard(c, Admin, round.S("id")));
        var ninja = finance["items"]!.AsArray().OfType<JsonObject>().Single(i => i.S("app") == "Ninja");
        Assert.AreEqual(1m, ninja.N("ratedCount"));
        Assert.AreEqual(4m, ninja.N("average"));
        var factors = ninja["factors"]!.AsArray().OfType<JsonObject>().ToDictionary(x => x.S("key"), x => x);
        Assert.AreEqual(5m, factors["responsiveness"].N("average"));
        Assert.AreEqual(3m, factors["accuracy"].N("average"));
        Assert.AreEqual(1m, factors["responsiveness"].N("count"));
        // A finance card answered in the Ninja form must not appear under any other app.
        CollectionAssert.AreEqual(new[] { "Ninja" }, finance["items"]!.AsArray().OfType<JsonObject>().Select(i => i.S("app")).ToArray());
    }

    [TestMethod]
    public void ExcelExportAddsSectionAndFinanceColumnsWithoutMergedCells()
    {
        using var f = new PayFixture();
        var (round, _, _) = Open(f);
        var bytes = f.Store.Read(c => EvaluationSurvey.ExportReplies(c, Admin, round.S("id")));
        Assert.IsTrue(bytes.Length > 0);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        using var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = new StreamReader(sheet).ReadToEnd();
        Assert.Contains("القسم", xml);
        Assert.Contains("مالية: العام", xml);
        Assert.Contains("مالية: التجاوب", xml);
        Assert.DoesNotContain("mergeCells", xml, StringComparison.OrdinalIgnoreCase);
        // Section column shows Arabic titles, never raw keys.
        Assert.Contains("تطبيق نينجا", xml);
        Assert.Contains("الحركة والمركبات", xml);
        Assert.DoesNotContain("app:Ninja", xml);
        Assert.DoesNotContain("dept:movement", xml);
        // Finance overall=4 is written once per reply (first card row only), not on every staff/app row.
        // Ninja reply has 2 card rows; before the fix the value appeared twice and inflated column sums.
        var doc = XDocument.Parse(xml);
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var header = doc.Root!.Element(s + "sheetData")!.Elements(s + "row").First().Elements(s + "c").ToList();
        int financeOverallCol = header.FindIndex(c => c.Value.Contains("مالية: العام", StringComparison.Ordinal));
        Assert.IsTrue(financeOverallCol >= 0);
        string ColRef(int index)
        {
            string value = "";
            int i = index;
            do { value = (char)('A' + i % 26) + value; i = i / 26 - 1; } while (i >= 0);
            return value;
        }
        string financeCol = ColRef(financeOverallCol);
        int financeFours = doc.Root.Element(s + "sheetData")!.Elements(s + "row").Skip(1)
            .SelectMany(r => r.Elements(s + "c"))
            .Count(c => ((string?)c.Attribute("r"))?.StartsWith(financeCol, StringComparison.Ordinal) == true
                && !char.IsLetter(((string?)c.Attribute("r"))![financeCol.Length..][0])
                && c.Element(s + "v")?.Value == "4");
        Assert.AreEqual(1, financeFours, "finance overall must appear once per reply (first card row)");
    }
}
