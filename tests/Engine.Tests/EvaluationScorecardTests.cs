using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>
/// KPI1 scorecards: fixture of known replies with hand-computed expected numbers.
/// Fixture math (current round):
///   Alpha rated n=5 scores (10,8,6,10,6) → avg 8.00; complaints 2 → rate 0.40; prev avg 7.00 → Δ +1.00; lowSample=false; Ops rank 1
///   Beta  rated n=3 scores (5,7,9) → avg 7.00; complaints 0 → rate 0.00; no prev → Δ null; lowSample=true; Ops rank 2
///   Ninja stars (5,5,4,3,1,2) → avg 3.33; dist 1:1,2:1,3:1,4:1,5:2; net = %4-5 − %1-2 = 50 − 33.33 = 16.67
///   Ninja factors: orderVolume avg 4.75 (top); fairPay avg 1.25 (bottom)
/// </summary>
[TestClass]
public sealed class EvaluationScorecardTests
{
    static readonly ClaimsPrincipal Admin = PayFixture.Maker;
    static ClaimsPrincipal Viewer(params string[] permissions) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "synthetic-viewer"), new Claim(ClaimTypes.Role, "Reviewer") }
            .Concat(permissions.Select(p => new Claim("permission", p))), "test"));

    static JsonObject Staff(string name, string dept) => new()
    {
        ["name"] = name,
        ["cities"] = new JsonArray("CityA"),
        ["departments"] = new JsonArray(dept),
        ["roles"] = new JsonArray("Synthetic role"),
        ["apps"] = new JsonArray("Ninja"),
        ["active"] = true
    };

    static JsonObject Dims(decimal v) => new(EvaluationSurvey.StaffDimensions.Select(k =>
        KeyValuePair.Create<string, JsonNode?>(k, JsonValue.Create(v))));

    static JsonObject AppSkip(string app) => new() { ["app"] = app, ["reason"] = "not_worked" };

    static JsonObject AppRated(string app, int stars, JsonObject? factors = null)
    {
        var o = new JsonObject { ["app"] = app, ["overall"] = stars };
        if (factors != null) o["factorRatings"] = factors;
        return o;
    }

    static JsonArray AllApps(JsonObject round, JsonObject ninja)
    {
        var apps = Strings(round);
        return new(apps.Select(a => (JsonNode?)(a == "Ninja" ? ninja.Clone() : AppSkip(a))).ToArray());
    }

    static string[] Strings(JsonObject round) =>
        round["apps"]!.AsArray().Select(a => a!.ToString()).ToArray();

    sealed record Fixture(string CurrentId, string AlphaId, string BetaId, string PrevId);

    static Fixture Build(PayFixture f)
    {
        return f.Store.Write((c, tx) =>
        {
            EvaluationSurvey.ImportStaff(c, tx, Admin, new()
            {
                ["staff"] = new JsonArray(Staff("Synthetic Alpha", "Ops"), Staff("Synthetic Beta", "Ops"))
            });
            var prev = EvaluationSurvey.CreateRound(c, tx, Admin, new() { ["title"] = "Previous synthetic round" });
            prev = EvaluationSurvey.Transition(c, tx, Admin, prev.S("id"), "open");
            prev["createdAt"] = "2026-01-01T00:00:00.0000000+00:00";
            prev["updatedAt"] = "2026-01-01T00:00:00.0000000+00:00";
            Store.Save(c, EvaluationSurvey.RoundKind, prev, tx);

            string alphaId = prev["staff"]!.AsArray().OfType<JsonObject>().Single(s => s.S("name") == "Synthetic Alpha").S("id");
            string betaId = prev["staff"]!.AsArray().OfType<JsonObject>().Single(s => s.S("name") == "Synthetic Beta").S("id");

            // Previous: Alpha all-7 once; Beta skipped; Ninja = 4
            EvaluationSurvey.Submit(c, tx, prev.S("sharingToken"), new()
            {
                ["iqama"] = "2000000190",
                ["name"] = "Synthetic prev rider",
                ["city"] = "CityA",
                ["nationality"] = "egyptian",
                ["workApps"] = new JsonArray("Ninja"),
                ["staffAnswers"] = new JsonArray(
                    new JsonObject { ["staffId"] = alphaId, ["ratings"] = Dims(7) },
                    new JsonObject { ["staffId"] = betaId, ["reason"] = "not_dealt" }),
                ["appAnswers"] = AllApps(prev, AppRated("Ninja", 4))
            });
            EvaluationSurvey.Transition(c, tx, Admin, prev.S("id"), "closed");

            var curr = EvaluationSurvey.CreateRound(c, tx, Admin, new() { ["title"] = "Current synthetic round" });
            curr = EvaluationSurvey.Transition(c, tx, Admin, curr.S("id"), "open");
            curr["createdAt"] = "2026-02-01T00:00:00.0000000+00:00";
            curr["updatedAt"] = "2026-02-01T00:00:00.0000000+00:00";
            Store.Save(c, EvaluationSurvey.RoundKind, curr, tx);
            alphaId = curr["staff"]!.AsArray().OfType<JsonObject>().Single(s => s.S("name") == "Synthetic Alpha").S("id");
            betaId = curr["staff"]!.AsArray().OfType<JsonObject>().Single(s => s.S("name") == "Synthetic Beta").S("id");

            // Hand-picked star / dimension values — see class summary for expected aggregates.
            var plans = new (string Iqama, decimal? Alpha, bool AlphaComplaint, decimal? Beta, int Ninja, JsonObject? Factors)[]
            {
                ("2000000101", 10m, true,  5m, 5, new JsonObject { ["orderVolume"] = 5, ["fairPay"] = 1 }),
                ("2000000102", 8m,  false, 7m, 5, new JsonObject { ["orderVolume"] = 5, ["fairPay"] = 1 }),
                ("2000000103", 6m,  true,  9m, 4, new JsonObject { ["orderVolume"] = 4, ["fairPay"] = 2 }),
                ("2000000104", 10m, false, null, 3, new JsonObject { ["orderVolume"] = 5, ["fairPay"] = 1 }),
                ("2000000105", 6m,  false, null, 1, null),
                ("2000000106", null, false, null, 2, null),
            };
            foreach (var p in plans)
            {
                JsonObject AlphaCard() => p.Alpha is null
                    ? new() { ["staffId"] = alphaId, ["reason"] = "not_dealt" }
                    : new()
                    {
                        ["staffId"] = alphaId,
                        ["ratings"] = Dims(p.Alpha.Value),
                        ["complaint"] = p.AlphaComplaint,
                        ["complaintDetails"] = p.AlphaComplaint ? "Synthetic complaint" : ""
                    };
                JsonObject BetaCard() => p.Beta is null
                    ? new() { ["staffId"] = betaId, ["reason"] = "not_dealt" }
                    : new() { ["staffId"] = betaId, ["ratings"] = Dims(p.Beta.Value) };
                EvaluationSurvey.Submit(c, tx, curr.S("sharingToken"), new()
                {
                    ["iqama"] = p.Iqama,
                    ["name"] = "Synthetic rider " + p.Iqama[^2..],
                    ["city"] = "CityA",
                    ["nationality"] = "egyptian",
                    ["workApps"] = new JsonArray("Ninja"),
                    ["staffAnswers"] = new JsonArray(AlphaCard(), BetaCard()),
                    ["appAnswers"] = AllApps(curr, AppRated("Ninja", p.Ninja, p.Factors))
                });
            }
            return new Fixture(curr.S("id"), alphaId, betaId, prev.S("id"));
        });
    }

    [TestMethod]
    public void SupervisorScorecard_MatchesHandComputedFixture()
    {
        using var f = new PayFixture();
        var fx = Build(f);
        var viewer = Viewer(EvaluationSurvey.ViewPermission, SubmissionChoices.PermAllCompanies);
        var card = f.Store.Read(c => EvaluationSurvey.SupervisorScorecard(c, viewer, fx.CurrentId));
        Assert.AreEqual(fx.CurrentId, card.S("roundId"));
        Assert.AreEqual(6m, card.N("totalResponses"));
        Assert.IsTrue(card.S("confidenceNoteAr").Length > 0, "small-sample confidence note required");
        Assert.IsFalse(card.ToJsonString().Contains("2000000101"), "no rider identity in aggregates");
        Assert.IsFalse(card.ToJsonString().Contains("Synthetic rider"), "no rider names in aggregates");

        var items = card["items"]!.AsArray().OfType<JsonObject>().ToDictionary(i => i.S("staffId"));
        var alpha = items[fx.AlphaId];
        Assert.AreEqual(5m, alpha.N("replyCount"));
        Assert.AreEqual(8.00m, alpha.N("average"));
        Assert.AreEqual(0.40m, alpha.N("complaintRate"));
        Assert.AreEqual(2m, alpha.N("complaintCount"));
        Assert.AreEqual(1m, alpha.N("rankInDepartment"));
        Assert.AreEqual("Ops", alpha.S("department"));
        Assert.AreEqual(1.00m, alpha.N("changeVsPrevious"));
        Assert.IsFalse(alpha.B("lowSample"));
        Assert.IsTrue(alpha.B("identityVisible")); // n>=3
        foreach (var d in EvaluationSurvey.StaffDimensions)
            Assert.AreEqual(8.00m, alpha["dimensions"]!.AsArray().OfType<JsonObject>().Single(x => x.S("key") == d).N("average"));

        var beta = items[fx.BetaId];
        Assert.AreEqual(3m, beta.N("replyCount"));
        Assert.AreEqual(7.00m, beta.N("average"));
        Assert.AreEqual(0.00m, beta.N("complaintRate"));
        Assert.AreEqual(2m, beta.N("rankInDepartment"));
        Assert.IsNull(beta["changeVsPrevious"]);
        Assert.IsTrue(beta.B("lowSample"));
        Assert.IsTrue(beta.B("identityVisible")); // n==3 allowed
    }

    [TestMethod]
    public void AppScorecard_NetScoreAndFactorExtremes_MatchFixture()
    {
        using var f = new PayFixture();
        var fx = Build(f);
        var viewer = Viewer(EvaluationSurvey.ViewPermission, SubmissionChoices.PermAllCompanies);
        var card = f.Store.Read(c => EvaluationSurvey.AppScorecard(c, viewer, fx.CurrentId));
        var ninja = card["items"]!.AsArray().OfType<JsonObject>().Single(i => i.S("app") == "Ninja");
        Assert.AreEqual(6m, ninja.N("replyCount"));
        Assert.AreEqual(3.33m, ninja.N("average"));
        Assert.AreEqual(16.67m, ninja.N("netScore"));
        var dist = ninja["distribution"]!.AsArray().OfType<JsonObject>().ToDictionary(d => (int)d.N("stars"), d => (int)d.N("count"));
        Assert.AreEqual(1, dist[1]);
        Assert.AreEqual(1, dist[2]);
        Assert.AreEqual(1, dist[3]);
        Assert.AreEqual(1, dist[4]);
        Assert.AreEqual(2, dist[5]);
        Assert.AreEqual("orderVolume", ninja["topFactors"]![0]!.S("key"));
        Assert.AreEqual(4.75m, ninja["topFactors"]![0]!.N("average"));
        Assert.AreEqual("fairPay", ninja["bottomFactors"]![0]!.S("key"));
        Assert.AreEqual(1.25m, ninja["bottomFactors"]![0]!.N("average"));
        Assert.IsFalse(card.ToJsonString().Contains("2000000101"));
    }

    [TestMethod]
    public void HeatmapAndCityAppMatrix_MatchFixture()
    {
        using var f = new PayFixture();
        var fx = Build(f);
        var viewer = Viewer(EvaluationSurvey.ViewPermission, SubmissionChoices.PermAllCompanies);
        var heat = f.Store.Read(c => EvaluationSurvey.SupervisorDimensionHeatmap(c, viewer, fx.CurrentId));
        Assert.IsTrue(heat.S("confidenceNoteAr").Length > 0, "heatmap includes Beta n=3 low-sample note");
        var alphaProf = heat["cells"]!.AsArray().OfType<JsonObject>()
            .Single(c => c.S("staffId") == fx.AlphaId && c.S("dimension") == "professional");
        Assert.AreEqual(8.00m, alphaProf.N("average"));
        Assert.AreEqual(5m, alphaProf.N("replyCount"));
        var betaTreat = heat["cells"]!.AsArray().OfType<JsonObject>()
            .Single(c => c.S("staffId") == fx.BetaId && c.S("dimension") == "treatment");
        Assert.AreEqual(7.00m, betaTreat.N("average"));
        Assert.IsTrue(betaTreat.B("lowSample"));

        var matrix = f.Store.Read(c => EvaluationSurvey.CityAppMatrix(c, viewer, fx.CurrentId));
        var cell = matrix["cells"]!.AsArray().OfType<JsonObject>()
            .Single(c => c.S("city") == "CityA" && c.S("app") == "Ninja");
        Assert.AreEqual(3.33m, cell.N("average"));
        Assert.AreEqual(6m, cell.N("replyCount"));
        Assert.IsFalse(cell.B("lowSample"));
        Assert.AreEqual("", matrix.S("confidenceNoteAr"), "CityA×Ninja n=6 is not a small sample");
    }

    [TestMethod]
    public void Scorecards_ApplyFiltersBeforeAggregation_AndHideIdentityBelowThree()
    {
        using var f = new PayFixture();
        var fx = Build(f);
        var viewer = Viewer(EvaluationSurvey.ViewPermission, SubmissionChoices.PermAllCompanies);
        var filtered = f.Store.Read(c => EvaluationSurvey.SupervisorScorecard(c, viewer, fx.CurrentId,
            city: null, app: null, department: "Ops", supervisorId: fx.BetaId, nationalityId: null));
        Assert.HasCount(1, filtered["items"]!.AsArray());
        Assert.AreEqual(fx.BetaId, filtered["items"]![0]!.S("staffId"));
        Assert.AreEqual(3m, filtered["items"]![0]!.N("replyCount"));

        // Force a 2-reply filter (city that only two of the six used — none); use supervisor+nationality on a tiny set via department noop.
        // Build a second mini-round with n=2 for the privacy flag.
        var tiny = f.Store.Write((c, tx) =>
        {
            var r = EvaluationSurvey.CreateRound(c, tx, Admin, new() { ["title"] = "Tiny privacy round" });
            r = EvaluationSurvey.Transition(c, tx, Admin, r.S("id"), "open");
            string sid = r["staff"]!.AsArray().OfType<JsonObject>().First().S("id");
            for (int i = 0; i < 2; i++)
            {
                EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), new()
                {
                    ["iqama"] = "200000020" + i,
                    ["name"] = "Tiny rider",
                    ["city"] = "CityA",
                    ["nationality"] = "saudi",
                    ["workApps"] = new JsonArray("Ninja"),
                    ["staffAnswers"] = new JsonArray(r["staff"]!.AsArray().OfType<JsonObject>().Select(s => (JsonNode?)new JsonObject
                    {
                        ["staffId"] = s.S("id"),
                        ["ratings"] = Dims(9)
                    }).ToArray()),
                    ["appAnswers"] = AllApps(r, AppRated("Ninja", 5))
                });
            }
            return (Id: r.S("id"), StaffId: sid);
        });
        var tinyCard = f.Store.Read(c => EvaluationSurvey.SupervisorScorecard(c, viewer, tiny.Id, supervisorId: tiny.StaffId));
        var row = tinyCard["items"]!.AsArray().OfType<JsonObject>().Single(i => i.S("staffId") == tiny.StaffId);
        Assert.AreEqual(2m, row.N("replyCount"));
        Assert.IsTrue(row.B("lowSample"));
        Assert.IsFalse(row.B("identityVisible"), "n<3 must hide rider identity for drill-down");
        Assert.IsFalse(tinyCard.ToJsonString().Contains("200000020"));
    }

    [TestMethod]
    public void ScorecardExports_RequireExportPermission_AndRespectFilters()
    {
        using var f = new PayFixture();
        var fx = Build(f);
        var viewOnly = Viewer(EvaluationSurvey.ViewPermission, SubmissionChoices.PermAllCompanies);
        Assert.Throws<ApiError>(() => f.Store.Read(c => EvaluationSurvey.ExportSupervisorScorecard(c, viewOnly, fx.CurrentId)));
        var exporter = Viewer(EvaluationSurvey.ViewPermission, EvaluationSurvey.ExportPermission, SubmissionChoices.PermAllCompanies);
        var bytes = f.Store.Read(c => EvaluationSurvey.ExportSupervisorScorecard(c, exporter, fx.CurrentId, department: "Ops"));
        Assert.IsTrue(bytes.Length > 100);
        foreach (var kind in new[] { "apps", "heatmap", "city-app" })
        {
            var sheet = f.Store.Read(c => EvaluationSurvey.ExportScorecard(c, exporter, fx.CurrentId, kind));
            Assert.IsTrue(sheet.Length > 100, kind);
        }
    }

    [TestMethod]
    public void DrillDownHints_PointAtFilteredRepliesTable()
    {
        using var f = new PayFixture();
        var fx = Build(f);
        var viewer = Viewer(EvaluationSurvey.ViewPermission, SubmissionChoices.PermAllCompanies);
        var card = f.Store.Read(c => EvaluationSurvey.SupervisorScorecard(c, viewer, fx.CurrentId));
        var alpha = card["items"]!.AsArray().OfType<JsonObject>().Single(i => i.S("staffId") == fx.AlphaId);
        Assert.IsTrue(alpha["drillDown"] is JsonObject);
        Assert.AreEqual(fx.AlphaId, alpha["drillDown"]!.S("supervisorId"));
        Assert.AreEqual("replies", alpha["drillDown"]!.S("target"));
    }
}
