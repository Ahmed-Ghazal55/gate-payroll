using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class EvaluationLaunchTests
{
    const string Rider = "2000000091";
    const string MobToken = "rmob_" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static string MobId() => "mobile:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MobToken)));
    static DefaultHttpContext Ctx(string bearer)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Authorization = "Bearer " + bearer;
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("synthetic.invalid");
        return ctx;
    }
    static JsonObject OpenRound(PayFixture f)
    {
        var opened = f.Store.Write((c, tx) =>
        {
            EvaluationSurvey.ImportStaff(c, tx, PayFixture.Maker, new()
            {
                ["staff"] = new JsonArray(new JsonObject
                {
                    ["name"] = "Synthetic staff", ["cities"] = new JsonArray("Synthetic city"),
                    ["departments"] = new JsonArray("Synthetic department"), ["roles"] = new JsonArray("Synthetic role"),
                    ["apps"] = new JsonArray("Ninja"), ["active"] = true
                })
            });
            var draft = EvaluationSurvey.CreateRound(c, tx, PayFixture.Maker, new() { ["title"] = "Synthetic launch round" });
            return EvaluationSurvey.Transition(c, tx, PayFixture.Maker, draft.S("id"), "open");
        });
        // Rider-mobile launch lists only legacy (non-sectioned) rounds.
        return f.MakeLegacyEvaluationRound(opened);
    }
    static void SeedRiderSession(PayFixture f)
    {
        f.Store.Write((c, tx) =>
        {
            Store.Save(c, "rider", new() { ["id"] = Rider, ["iqama"] = Rider, ["name"] = "Synthetic rider", ["status"] = "Active" }, tx);
            Store.Save(c, MobileSessions.Kind, new()
            {
                ["id"] = MobId(), ["iqama"] = Rider, ["language"] = "ar",
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(1).ToString("O")
            }, tx);
            return true;
        });
    }

    static string LaunchCodeFromUrl(string url)
    {
        var uri = new Uri(url);
        Assert.IsFalse(uri.Query.Contains("launch=", StringComparison.Ordinal), "launch code must not be in the query string");
        string fragment = uri.Fragment.TrimStart('#');
        return fragment.Split('&').Select(p => p.Split('=')).Single(p => p[0] == "launch")[1];
    }

    [TestMethod]
    public void LaunchCodeIsSingleUseAndNeverPutsAppSessionInUrl()
    {
        using var f = new PayFixture();
        SeedRiderSession(f);
        var round = OpenRound(f);
        string token = round.S("sharingToken");
        var launched = f.Store.Write((c, tx) => EvaluationSurvey.IssueLaunch(c, tx, Rider, token, "https://synthetic.invalid"));
        string url = launched.S("url");
        Assert.IsFalse(url.Contains(MobToken, StringComparison.Ordinal));
        Assert.IsFalse(url.Contains("riderSession=", StringComparison.Ordinal));
        Assert.IsTrue(url.Contains("#launch=", StringComparison.Ordinal));
        Assert.IsTrue(url.Contains("evaluation=" + token, StringComparison.Ordinal));
        string code = LaunchCodeFromUrl(url);
        var session = f.Store.Write((c, tx) => EvaluationSurvey.ExchangeLaunch(c, tx, token, code));
        Assert.IsTrue(session.S("accessToken").StartsWith("eves_", StringComparison.Ordinal));
        var reuse = Assert.ThrowsExactly<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.ExchangeLaunch(c, tx, token, code)));
        Assert.AreEqual(401, reuse.Status);
    }

    [TestMethod]
    public void LaunchCodeExpiresAfterFiveMinutes()
    {
        using var f = new PayFixture();
        SeedRiderSession(f);
        var round = OpenRound(f);
        string token = round.S("sharingToken");
        var launched = f.Store.Write((c, tx) => EvaluationSurvey.IssueLaunch(c, tx, Rider, token, "https://synthetic.invalid"));
        string code = LaunchCodeFromUrl(launched.S("url"));
        f.Store.Write((c, tx) =>
        {
            var row = Store.Get(c, EvaluationSurvey.LaunchRecordId(code), EvaluationSurvey.LaunchKind, tx)!;
            row["expiresAt"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
            Store.Save(c, EvaluationSurvey.LaunchKind, row, tx);
            return true;
        });
        var ex = Assert.ThrowsExactly<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.ExchangeLaunch(c, tx, token, code)));
        Assert.AreEqual(401, ex.Status);
    }

    [TestMethod]
    public void LaunchCodeBoundToRoundRejectsWrongRound()
    {
        using var f = new PayFixture();
        SeedRiderSession(f);
        var a = OpenRound(f);
        var b = f.MakeLegacyEvaluationRound(f.Store.Write((c, tx) =>
        {
            var draft = EvaluationSurvey.CreateRound(c, tx, PayFixture.Maker, new() { ["title"] = "Other round" });
            return EvaluationSurvey.Transition(c, tx, PayFixture.Maker, draft.S("id"), "open");
        }));
        var launched = f.Store.Write((c, tx) => EvaluationSurvey.IssueLaunch(c, tx, Rider, a.S("sharingToken"), "https://synthetic.invalid"));
        string code = LaunchCodeFromUrl(launched.S("url"));
        var ex = Assert.ThrowsExactly<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.ExchangeLaunch(c, tx, b.S("sharingToken"), code)));
        Assert.AreEqual(401, ex.Status);
    }

    [TestMethod]
    public void SurveySessionCannotCallOtherRiderMobileEndpointsAndIsRoundScoped()
    {
        using var f = new PayFixture();
        SeedRiderSession(f);
        var a = OpenRound(f);
        var b = f.MakeLegacyEvaluationRound(f.Store.Write((c, tx) =>
        {
            var draft = EvaluationSurvey.CreateRound(c, tx, PayFixture.Maker, new() { ["title"] = "Other round" });
            return EvaluationSurvey.Transition(c, tx, PayFixture.Maker, draft.S("id"), "open");
        }));
        var launched = f.Store.Write((c, tx) => EvaluationSurvey.IssueLaunch(c, tx, Rider, a.S("sharingToken"), "https://synthetic.invalid"));
        string code = LaunchCodeFromUrl(launched.S("url"));
        string survey = f.Store.Write((c, tx) => EvaluationSurvey.ExchangeLaunch(c, tx, a.S("sharingToken"), code)).S("accessToken");
        Assert.AreEqual(401, Assert.ThrowsExactly<ApiError>(() => MobileSessions.Authenticate(f.Store, Ctx(survey))).Status);
        Assert.AreEqual(Rider, EvaluationSurvey.AuthenticateEvaluation(f.Store, Ctx(survey), a.S("sharingToken")).S("iqama"));
        Assert.AreEqual(401, Assert.ThrowsExactly<ApiError>(() => EvaluationSurvey.AuthenticateEvaluation(f.Store, Ctx(survey), b.S("sharingToken"))).Status);
        Assert.AreEqual(Rider, EvaluationSurvey.AuthenticateEvaluation(f.Store, Ctx(MobToken), a.S("sharingToken")).S("iqama"));
    }

    [TestMethod]
    public void SurveySessionAndUnusedLaunchRejectWhenRiderIsNotActive()
    {
        using var f = new PayFixture();
        SeedRiderSession(f);
        var round = OpenRound(f);
        string token = round.S("sharingToken");
        var launched = f.Store.Write((c, tx) => EvaluationSurvey.IssueLaunch(c, tx, Rider, token, "https://synthetic.invalid"));
        string code = LaunchCodeFromUrl(launched.S("url"));
        string survey = f.Store.Write((c, tx) => EvaluationSurvey.ExchangeLaunch(c, tx, token, code)).S("accessToken");
        f.Store.Write((c, tx) =>
        {
            var rider = Store.Get(c, Rider, "rider", tx)!;
            rider["status"] = "Suspended";
            Store.Save(c, "rider", rider, tx);
            return true;
        });
        Assert.AreEqual(401, Assert.ThrowsExactly<ApiError>(() => EvaluationSurvey.AuthenticateEvaluation(f.Store, Ctx(survey), token)).Status);
        var unused = f.Store.Write((c, tx) => EvaluationSurvey.IssueLaunch(c, tx, Rider, token, "https://synthetic.invalid"));
        string unusedCode = LaunchCodeFromUrl(unused.S("url"));
        var ex = Assert.ThrowsExactly<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.ExchangeLaunch(c, tx, token, unusedCode)));
        Assert.AreEqual(401, ex.Status);
        Assert.AreEqual("survey_session_inactive", ex.Code);
    }
}
