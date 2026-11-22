using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class EvaluationSurveyTests
{
    static readonly ClaimsPrincipal Admin = PayFixture.Maker;
    static ClaimsPrincipal Reader(string role = "Reviewer", params string[] permissions) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "synthetic-reader"), new Claim(ClaimTypes.Role, role) }
            .Concat(permissions.Select(p => new Claim("permission", p))), "test"));
    static JsonObject Staff(string name = "Synthetic staff") => new()
    {
        ["name"] = name, ["cities"] = new JsonArray("Synthetic city"),
        ["departments"] = new JsonArray("Synthetic department"), ["roles"] = new JsonArray("Synthetic role"),
        ["apps"] = new JsonArray("Ninja"), ["active"] = true
    };
    static JsonObject Setup(PayFixture f) => f.Store.Write((c, tx) =>
    {
        EvaluationSurvey.ImportStaff(c, tx, Admin, new() { ["staff"] = new JsonArray(Staff(), Staff()) });
        var round = EvaluationSurvey.CreateRound(c, tx, Admin, new() { ["title"] = "Synthetic round" });
        return EvaluationSurvey.Transition(c, tx, Admin, round.S("id"), "open");
    });
    static JsonObject Reply(JsonObject round, string iqama = "2000000091") => new()
    {
        ["iqama"] = iqama, ["name"] = "Synthetic rider", ["city"] = "Synthetic city", ["nationality"] = "Synthetic nationality",
        ["workApps"] = new JsonArray("Ninja"),
        ["staffAnswers"] = new JsonArray(round["staff"]!.AsArray().OfType<JsonObject>().Select(s => (JsonNode?)new JsonObject
        {
            ["staffId"] = s.S("id"), ["ratings"] = new JsonObject(EvaluationSurvey.StaffDimensions.Select(k => KeyValuePair.Create<string, JsonNode?>(k, JsonValue.Create(8))))
        }).ToArray()),
        ["appAnswers"] = new JsonArray(round["apps"]!.AsArray().Select(a => (JsonNode?)new JsonObject { ["app"] = a!.ToString(), ["reason"] = "not_worked" }).ToArray())
    };
    [TestMethod]
    public void ImportDeduplicatesPeopleAndSnapshotsOneCardPerPerson()
    {
        using var f = new PayFixture(); var r = Setup(f);
        Assert.HasCount(1, r["staff"]!.AsArray());
        f.Store.Write((c, tx) => EvaluationSurvey.ImportStaff(c, tx, Admin, new() { ["staff"] = new JsonArray(Staff("Another synthetic staff")) }));
        Assert.HasCount(1, f.Store.Read(c => EvaluationSurvey.Round(c, r.S("id")))["staff"]!.AsArray());
    }
    [TestMethod]
    public void ReplyIsPersistedOnceAndEveryCardAndNationalityAreRequired()
    {
        using var f = new PayFixture(); var r = Setup(f); var input = Reply(r);
        input["nationality"] = "";
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input)));
        input["nationality"] = "Synthetic nationality"; input["staffAnswers"] = new JsonArray();
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input)));
        input = Reply(r); var receipt = f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input));
        Assert.IsFalse(receipt.ContainsKey("iqama"));
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input)));
        Assert.HasCount(1, f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)));
    }
    [TestMethod]
    public void BoundsReasonsAndRoundLifecycleAreEnforced()
    {
        using var f = new PayFixture(); var r = Setup(f); var input = Reply(r);
        input["staffAnswers"]![0]!["ratings"]!["support"] = 11;
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input)));
        input = Reply(r); input["appAnswers"]![0]!["reason"] = "invented";
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input)));
        f.Store.Write((c, tx) => EvaluationSurvey.Transition(c, tx, Admin, r.S("id"), "paused"));
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), Reply(r))));
        f.Store.Write((c, tx) => EvaluationSurvey.Transition(c, tx, Admin, r.S("id"), "closed"));
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Transition(c, tx, Admin, r.S("id"), "open")));
    }
    [TestMethod]
    public void SupervisorsAndOrdinaryFormViewersCannotReadEvaluationResults()
    {
        foreach (var user in new[] { Reader("Reviewer", "forms.view"), Reader("PlatformsSupervisor", EvaluationSurvey.ViewPermission), Reader() })
        {
            Assert.IsFalse(EvaluationSurvey.CanView(user));
            Assert.IsFalse(AccessPolicy.Allowed(user, "GET", "/api/forms/evaluations/rounds/x/statistics"));
        }
        Assert.IsTrue(EvaluationSurvey.CanView(Reader("Reviewer", EvaluationSurvey.ViewPermission)));
    }
    [TestMethod]
    public void StatisticsUseTheSameScopeAndDoNotExposeContactData()
    {
        using var f = new PayFixture(); var r = Setup(f);
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), Reply(r)));
        var denied = Reader("Reviewer", EvaluationSurvey.ViewPermission);
        Assert.HasCount(0, f.Store.Read(c => EvaluationSurvey.Responses(c, denied, r.S("id"))));
        var all = Reader("Reviewer", EvaluationSurvey.ViewPermission, SubmissionChoices.PermAllCompanies);
        var stats = f.Store.Read(c => EvaluationSurvey.Statistics(c, all, r.S("id")));
        Assert.AreEqual(1m, stats.N("totalResponses"));
        Assert.AreEqual(8m, stats["bySupervisor"]![0]!.N("average"));
        Assert.AreEqual(0m, stats["byApp"]![0]!.N("ratedCount"));
        Assert.IsFalse(stats.ToJsonString().Contains("2000000091"));
        Assert.IsFalse(stats.ToJsonString().Contains("Synthetic rider"));
    }
    [TestMethod]
    public void ComplaintIsCreatedInTheExistingConfidentialQueueAtomically()
    {
        using var f = new PayFixture(); var r = Setup(f); var input = Reply(r);
        input["staffAnswers"]![0]!["complaint"] = true;
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input)));
        Assert.HasCount(0, f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)));
        input["staffAnswers"]![0]!["complaintDetails"] = "Synthetic complaint details";
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input));
        var complaint = f.Store.Read(c => Store.List(c, RiderAsks.ServiceKind)).Single();
        Assert.IsTrue(complaint.B("confidential"));
        Assert.AreEqual("supervisor", complaint["payload"].S("targetKind"));
    }
    [TestMethod]
    public void NormalizedIqamaPreventsArabicDigitDuplicatesAndMobileSpoofing()
    {
        using var f = new PayFixture(); var r = Setup(f);
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), Reply(r, "٢٠٠٠٠٠٠٠٩١")));
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), Reply(r))));
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), Reply(r), "2000000092")));
    }
    [TestMethod]
    public void ExcludedAndOtherCityStaffNeverBecomeRequiredCards()
    {
        using var f = new PayFixture();
        f.Store.Write((c, tx) =>
        {
            var excluded = Staff("Synthetic excluded"); excluded["excluded"] = true;
            var other = Staff("Synthetic other city"); other["cities"] = new JsonArray("Other city");
            return EvaluationSurvey.ImportStaff(c, tx, Admin, new() { ["staff"] = new JsonArray(Staff(), excluded, other) });
        });
        var r = f.Store.Write((c, tx) => EvaluationSurvey.CreateRound(c, tx, Admin, new() { ["title"] = "Synthetic round" }));
        var schema = EvaluationSurvey.Schema(r, "Synthetic city");
        Assert.HasCount(1, schema["staff"]!.AsArray());
        Assert.HasCount(2, r["staff"]!.AsArray());
        Assert.Throws<ApiError>(() => EvaluationSurvey.Schema(r, "Invented city"));
    }
    [TestMethod]
    public void StarsFactorsAndSkipReasonsAreCountedSeparately()
    {
        using var f = new PayFixture(); var r = Setup(f); var input = Reply(r);
        input["appAnswers"]![0] = new JsonObject { ["app"] = r["apps"]![0]!.ToString(), ["overall"] = 6 };
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input)));
        input["appAnswers"]![0]!["overall"] = 4;
        input["appAnswers"]![0]!["factors"] = new JsonArray("orderVolume", "orderVolume", "appProblems");
        input["staffAnswers"]![0] = new JsonObject { ["staffId"] = r["staff"]![0]!.S("id"), ["reason"] = "not_dealt" };
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input));
        var stats = f.Store.Read(c => EvaluationSurvey.Statistics(c, Admin, r.S("id")));
        Assert.IsNull(stats["bySupervisor"]![0]!["average"]);
        Assert.AreEqual(1m, stats["bySupervisor"]![0]!.N("skippedCount"));
        Assert.AreEqual(4m, stats["byApp"]!.AsArray().OfType<JsonObject>().Single(a => a.N("ratedCount") == 1).N("average"));
        Assert.AreEqual(2, stats["commonFactors"]!.AsArray().Count);
        Assert.IsTrue(stats["commonFactors"]!.AsArray().All(factor => factor.N("count") == 1));
    }
    [TestMethod]
    public void ExplicitPlatformScopeRedactsOtherPlatformCardsFromRepliesAndStatistics()
    {
        using var f = new PayFixture(); var r = Setup(f); var input = Reply(r);
        input["workApps"] = new JsonArray("Ninja");
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input));
        var claims = Reader("Reviewer", EvaluationSurvey.ViewPermission, SubmissionChoices.PermAllCompanies).Claims.Append(new Claim("finance.platforms", "Ninja"));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var replies = f.Store.Read(c => EvaluationSurvey.Responses(c, user, r.S("id")));
        Assert.HasCount(1, replies); Assert.HasCount(1, replies[0]["appAnswers"]!.AsArray());
        Assert.AreEqual("Ninja", replies[0]["appAnswers"]![0]!.S("app"));
        var hidden = new ClaimsPrincipal(new ClaimsIdentity(claims.Where(c => c.Type != "finance.platforms").Append(new Claim("finance.platforms", "Keeta")), "test"));
        Assert.HasCount(0, f.Store.Read(c => EvaluationSurvey.Responses(c, hidden, r.S("id"))));
    }
    [TestMethod]
    public void SupervisorCannotSeeEvaluationComplaintEvenWithConfidentialPermission()
    {
        using var f = new PayFixture(); var r = Setup(f); var input = Reply(r);
        input["staffAnswers"]![0]!["complaint"] = true; input["staffAnswers"]![0]!["complaintDetails"] = "Synthetic complaint";
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input));
        var supervisor = Reader("PlatformsSupervisor", EvaluationSurvey.ViewPermission, "mobile.complaints.confidential", SubmissionChoices.PermAllCompanies);
        Assert.HasCount(0, f.Store.Read(c => RiderAsks.StaffServiceRequests(c, supervisor, null, null, null)));
        Assert.HasCount(1, f.Store.Read(c => RiderAsks.StaffServiceRequests(c, Admin, null, null, null)));
    }
    [TestMethod]
    public void ConcurrentRepliesCreateOneResponseAndOneComplaint()
    {
        using var f = new PayFixture(); var r = Setup(f); var input = Reply(r);
        input["staffAnswers"]![0]!["complaint"] = true; input["staffAnswers"]![0]!["complaintDetails"] = "Synthetic complaint";
        int accepted = 0;
        Parallel.For(0, 4, _ =>
        {
            try { f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input.Clone())); Interlocked.Increment(ref accepted); }
            catch (ApiError e) when (e.Status == 409) { }
        });
        Assert.AreEqual(1, accepted);
        Assert.HasCount(1, f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)));
        Assert.HasCount(1, f.Store.Read(c => Store.List(c, RiderAsks.ServiceKind)));
    }
    [TestMethod]
    public void LargeExcelExportKeepsEveryRowInSeparateCellsAcrossSheets()
    {
        using var stream = new MemoryStream(EvaluationSurvey.ExportWorkbook([new("Synthetic column")], Enumerable.Range(1, 25000).Select(i => new object?[] { i }).ToList()));
        using var zip = new System.IO.Compression.ZipArchive(stream);
        var sheets = zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)).ToList();
        Assert.HasCount(2, sheets);
        var docs = sheets.Select(e => { using var s = e.Open(); return System.Xml.Linq.XDocument.Load(s); }).ToList();
        System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Assert.AreEqual(25002, docs.Sum(d => d.Descendants(ns + "row").Count()));
        Assert.AreEqual("25000", docs[1].Descendants(ns + "v").Single().Value);
        Assert.IsFalse(docs.Any(d => d.Descendants(ns + "mergeCell").Any()));
    }
    [TestMethod]
    public void CompanyScopeUsesTrustedRiderCompanyAndNeverThePublicInput()
    {
        using var f = new PayFixture(); var r = Setup(f);
        string company = FinancialCore.Sponsors[0].Id, other = FinancialCore.Sponsors[1].Id;
        f.Store.Write((c, tx) =>
        {
            Store.Save(c, "rider", new() { ["id"] = "2000000091", ["name"] = "Synthetic directory rider", ["financeSponsorId"] = "", ["sponsorId"] = company }, tx);
            return true;
        });
        var input = Reply(r); input["companyId"] = other;
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input));
        ClaimsPrincipal Scoped(string id) => new(new ClaimsIdentity(Reader("Reviewer", EvaluationSurvey.ViewPermission, SubmissionChoices.PermAllCompanies).Claims.Append(new Claim("finance.sponsorIds", id)), "test"));
        Assert.HasCount(1, f.Store.Read(c => EvaluationSurvey.Responses(c, Scoped(company), r.S("id"))));
        Assert.HasCount(0, f.Store.Read(c => EvaluationSurvey.Responses(c, Scoped(other), r.S("id"))));
    }
    [TestMethod]
    public void UnsupportedUploadsAndUnapprovedFactorsAreRejected()
    {
        using var f = new PayFixture(); var r = Setup(f); var input = Reply(r);
        input["staffAnswers"]![0]!["attachments"] = new JsonArray(new JsonObject { ["name"] = "synthetic.png", ["size"] = 100 });
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input)));
        input = Reply(r); input["appAnswers"]![0] = new JsonObject { ["app"] = r["apps"]![0]!.ToString(), ["overall"] = 4, ["factors"] = new JsonArray("unapprovedMetric") };
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input)));
        input = Reply(r); input["staffAnswers"]![0]!["ratings"]!["support"] = "8";
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input)));
    }
    [TestMethod]
    public void UnifiedRiderCatalogIncludesOpenEvaluationsAndMarksSubmittedOnes()
    {
        using var f = new PayFixture(); var r = Setup(f);
        var catalog = f.Store.Read(c => RiderAsks.FormSchemasCatalog(c, "2000000091"));
        Assert.IsTrue(catalog["activeCampaigns"]!.AsArray().OfType<JsonObject>().Any(a => a.S("schema") == "evaluation-v1" && !a.B("alreadySubmitted")));
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), Reply(r)));
        catalog = f.Store.Read(c => RiderAsks.FormSchemasCatalog(c, "2000000091"));
        Assert.IsTrue(catalog["activeCampaigns"]!.AsArray().OfType<JsonObject>().Single(a => a.S("schema") == "evaluation-v1").B("alreadySubmitted"));
        f.Store.Write((c, tx) => EvaluationSurvey.Transition(c, tx, Admin, r.S("id"), "paused"));
        Assert.IsFalse(f.Store.Read(c => RiderAsks.FormSchemasCatalog(c, "2000000091"))["activeCampaigns"]!.AsArray().OfType<JsonObject>().Any(a => a.S("schema") == "evaluation-v1"));
    }
    [TestMethod]
    public void AuthenticatedReplyUsesTheTrustedDirectoryName()
    {
        using var f = new PayFixture(); var r = Setup(f);
        f.Store.Write((c, tx) => { Store.Save(c, "rider", new() { ["id"] = "2000000091", ["name"] = "Synthetic trusted name" }, tx); return true; });
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), Reply(r), "2000000091"));
        var stored = f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)).Single();
        Assert.AreEqual("Synthetic trusted name", stored.S("name")); Assert.IsTrue(stored.B("identityVerified"));
    }
}
