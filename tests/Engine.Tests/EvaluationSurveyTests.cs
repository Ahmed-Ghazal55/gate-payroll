using System.Security.Claims;
using System.Text;
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
        input["appAnswers"]![0]!["factorRatings"] = new JsonObject { ["orderVolume"] = 5, ["appProblems"] = 2, ["ghost"] = 3 };
        Assert.ThrowsExactly<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input)));
        input["appAnswers"]![0]!["factorRatings"] = new JsonObject { ["orderVolume"] = 5, ["appProblems"] = 2 };
        input["staffAnswers"]![0] = new JsonObject { ["staffId"] = r["staff"]![0]!.S("id"), ["reason"] = "not_dealt" };
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input));
        var stored = f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)).Single();
        Assert.AreEqual(5m, stored["appAnswers"]![0]!["factorRatings"]!.N("orderVolume"));
        Assert.IsNull(stored["appAnswers"]![0]!["factors"]);
        var stats = f.Store.Read(c => EvaluationSurvey.Statistics(c, Admin, r.S("id")));
        Assert.IsNull(stats["bySupervisor"]![0]!["average"]);
        Assert.AreEqual(1m, stats["bySupervisor"]![0]!.N("skippedCount"));
        Assert.AreEqual(4m, stats["byApp"]!.AsArray().OfType<JsonObject>().Single(a => a.N("ratedCount") == 1).N("average"));
        var factors = stats["factorAverages"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.AreEqual(2, factors.Count);
        Assert.AreEqual(5m, factors.Single(x => x.S("key") == "orderVolume").N("average"));
        Assert.AreEqual(2m, factors.Single(x => x.S("key") == "appProblems").N("average"));
        Assert.AreEqual(1m, factors.Single(x => x.S("key") == "orderVolume").N("count"));
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
        input["staffAnswers"]![0]!["attachments"] = new JsonArray(new JsonObject { ["kind"] = "image", ["fileName"] = "x.exe", ["contentType"] = "application/octet-stream", ["dataBase64"] = Convert.ToBase64String(new byte[] { 1, 2, 3 }) });
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input, store: f.Store)));
        input = Reply(r); input["appAnswers"]![0] = new JsonObject { ["app"] = r["apps"]![0]!.ToString(), ["overall"] = 4, ["factorRatings"] = new JsonObject { ["unapprovedMetric"] = 3 } };
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input, store: f.Store)));
        input = Reply(r); input["staffAnswers"]![0]!["ratings"]!["support"] = "8";
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input, store: f.Store)));
    }
    [TestMethod]
    public void UnifiedRiderCatalogIncludesOpenEvaluationsOnlyWhenClientDeclaresEvaluationWebCapability()
    {
        using var f = new PayFixture(); var r = Setup(f);
        var without = f.Store.Read(c => RiderAsks.FormSchemasCatalog(c, "2000000091"));
        Assert.IsFalse(without["activeCampaigns"]!.AsArray().OfType<JsonObject>().Any(a => a.S("schema") == "evaluation-v1"));
        var catalog = f.Store.Read(c => RiderAsks.FormSchemasCatalog(c, "2000000091", "evaluation-web"));
        Assert.IsTrue(catalog["activeCampaigns"]!.AsArray().OfType<JsonObject>().Any(a => a.S("schema") == "evaluation-v1" && !a.B("alreadySubmitted")));
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), Reply(r)));
        catalog = f.Store.Read(c => RiderAsks.FormSchemasCatalog(c, "2000000091", "evaluation-web, other-cap"));
        Assert.IsTrue(catalog["activeCampaigns"]!.AsArray().OfType<JsonObject>().Single(a => a.S("schema") == "evaluation-v1").B("alreadySubmitted"));
        f.Store.Write((c, tx) => EvaluationSurvey.Transition(c, tx, Admin, r.S("id"), "paused"));
        Assert.IsFalse(f.Store.Read(c => RiderAsks.FormSchemasCatalog(c, "2000000091", "evaluation-web"))["activeCampaigns"]!.AsArray().OfType<JsonObject>().Any(a => a.S("schema") == "evaluation-v1"));
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
    [TestMethod]
    public void AttachmentsAcceptAgreedLimitsAndPersistOnDisk()
    {
        using var f = new PayFixture(); var r = Setup(f); var input = Reply(r);
        string tinyPng = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D });
        var voiceBytes = new byte[200]; voiceBytes[0] = 0x1A; voiceBytes[1] = 0x45; voiceBytes[2] = 0xDF; voiceBytes[3] = 0xA3;
        string voice = Convert.ToBase64String(voiceBytes);
        input["staffAnswers"]![0]!["attachments"] = new JsonArray(
            new JsonObject { ["kind"] = "image", ["fileName"] = "a.png", ["contentType"] = "image/png", ["dataBase64"] = tinyPng },
            new JsonObject { ["kind"] = "image", ["fileName"] = "b.png", ["contentType"] = "image/png", ["dataBase64"] = tinyPng },
            new JsonObject { ["kind"] = "voice", ["fileName"] = "note.webm", ["contentType"] = "audio/webm", ["dataBase64"] = voice, ["durationSeconds"] = 12 });
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input, store: f.Store));
        var stored = f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)).Single();
        var atts = stored["staffAnswers"]![0]!["attachments"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.HasCount(3, atts);
        Assert.IsTrue(atts.All(a => a.S("playbackUrl") != "" && a.S("dataBase64") == ""));
        var files = f.Store.Read(c => Store.List(c, EvaluationSurvey.AttachmentKind));
        Assert.HasCount(3, files);
        Assert.IsTrue(files.All(a => File.Exists(Path.Combine(f.Store.DirectoryPath, a.S("relativePath")))));
    }
    [TestMethod]
    public void AttachmentsRejectFourthFileOrVoiceOverSixtySeconds()
    {
        using var f = new PayFixture(); var r = Setup(f); var input = Reply(r);
        string tinyPng = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var voiceBytes = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0, 0, 0, 0, 0, 0, 0, 0 };
        string voice = Convert.ToBase64String(voiceBytes);
        input["staffAnswers"]![0]!["attachments"] = new JsonArray(
            new JsonObject { ["kind"] = "image", ["fileName"] = "a.png", ["contentType"] = "image/png", ["dataBase64"] = tinyPng },
            new JsonObject { ["kind"] = "image", ["fileName"] = "b.png", ["contentType"] = "image/png", ["dataBase64"] = tinyPng },
            new JsonObject { ["kind"] = "voice", ["fileName"] = "n.webm", ["contentType"] = "audio/webm", ["dataBase64"] = voice, ["durationSeconds"] = 10 },
            new JsonObject { ["kind"] = "image", ["fileName"] = "c.png", ["contentType"] = "image/png", ["dataBase64"] = tinyPng });
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input, store: f.Store)));
        input = Reply(r);
        input["staffAnswers"]![0]!["attachments"] = new JsonArray(new JsonObject { ["kind"] = "voice", ["fileName"] = "n.webm", ["contentType"] = "audio/webm", ["dataBase64"] = voice, ["durationSeconds"] = 61 });
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input, store: f.Store)));
    }
    [TestMethod]
    public void RiderMayEditRatingsNotesComplaintUpToThreeTimesWhileRoundOpen()
    {
        using var f = new PayFixture(); var r = Setup(f);
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), Reply(r)));
        var edit = new JsonObject
        {
            ["staffAnswers"] = new JsonArray(r["staff"]!.AsArray().OfType<JsonObject>().Select(s => (JsonNode?)new JsonObject
            {
                ["staffId"] = s.S("id"),
                ["ratings"] = new JsonObject(EvaluationSurvey.StaffDimensions.Select(k => KeyValuePair.Create<string, JsonNode?>(k, JsonValue.Create(3)))),
                ["notes"] = "Synthetic edit note",
                ["complaint"] = true,
                ["complaintDetails"] = "Synthetic edited complaint"
            }).ToArray()),
            ["appAnswers"] = new JsonArray(r["apps"]!.AsArray().Select(a => (JsonNode?)new JsonObject { ["app"] = a!.ToString(), ["overall"] = 2, ["factorRatings"] = new JsonObject { ["orderVolume"] = 3 } }).ToArray())
        };
        for (int i = 0; i < 3; i++)
            f.Store.Write((c, tx) => EvaluationSurvey.Edit(c, tx, r.S("sharingToken"), edit, "2000000091"));
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Edit(c, tx, r.S("sharingToken"), edit, "2000000091")));
        var stored = f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)).Single();
        Assert.AreEqual(3m, stored.N("editCount"));
        Assert.AreEqual("Synthetic city", stored.S("city"));
        Assert.AreEqual(3m, stored["staffAnswers"]![0]!["ratings"]!.N("support"));
        f.Store.Write((c, tx) => EvaluationSurvey.Transition(c, tx, Admin, r.S("id"), "closed"));
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Edit(c, tx, r.S("sharingToken"), edit, "2000000091")));
    }
    [TestMethod]
    public void EditRejectsIdentityCityNationalityChanges()
    {
        using var f = new PayFixture(); var r = Setup(f);
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), Reply(r)));
        var edit = new JsonObject { ["city"] = "Other city", ["staffAnswers"] = Reply(r)["staffAnswers"]!.DeepClone(), ["appAnswers"] = Reply(r)["appAnswers"]!.DeepClone() };
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Edit(c, tx, r.S("sharingToken"), edit, "2000000091")));
    }
    [TestMethod]
    public void NationalityIsStoredAsCanonicalIdAcrossLanguages()
    {
        using var f = new PayFixture(); var r = Setup(f);
        var input = Reply(r); input["nationality"] = "مصري";
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input));
        var stored = f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)).Single();
        Assert.AreEqual("egyptian", stored.S("nationalityId"));
        var stats = f.Store.Read(c => EvaluationSurvey.Statistics(c, Admin, r.S("id")));
        Assert.AreEqual("egyptian", stats["byNationality"]![0]!.S("key"));
    }
    [TestMethod]
    public void StatisticsFilterByCityRecalculatesSupervisorAverages()
    {
        using var f = new PayFixture();
        f.Store.Write((c, tx) =>
        {
            var a = Staff("Synthetic A"); a["cities"] = new JsonArray("CityA", "CityB");
            return EvaluationSurvey.ImportStaff(c, tx, Admin, new() { ["staff"] = new JsonArray(a) });
        });
        var round = f.Store.Write((c, tx) =>
        {
            var created = EvaluationSurvey.CreateRound(c, tx, Admin, new() { ["title"] = "Synthetic multi-city" });
            return EvaluationSurvey.Transition(c, tx, Admin, created.S("id"), "open");
        });
        JsonObject Make(string city, string iqama, int rating) => new()
        {
            ["iqama"] = iqama, ["name"] = "Synthetic rider", ["city"] = city, ["nationality"] = "Saudi",
            ["workApps"] = new JsonArray("Ninja"),
            ["staffAnswers"] = new JsonArray(round["staff"]!.AsArray().OfType<JsonObject>()
                .Where(s => s["cities"]!.AsArray().Any(v => v!.ToString() == city))
                .Select(s => (JsonNode?)new JsonObject
                {
                    ["staffId"] = s.S("id"),
                    ["ratings"] = new JsonObject(EvaluationSurvey.StaffDimensions.Select(k => KeyValuePair.Create<string, JsonNode?>(k, JsonValue.Create(rating))))
                }).ToArray()),
            ["appAnswers"] = new JsonArray(round["apps"]!.AsArray().Select(a => (JsonNode?)new JsonObject { ["app"] = a!.ToString(), ["reason"] = "not_worked" }).ToArray())
        };
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, round.S("sharingToken"), Make("CityA", "2000000091", 2)));
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, round.S("sharingToken"), Make("CityB", "2000000092", 10)));
        var all = f.Store.Read(c => EvaluationSurvey.Statistics(c, Admin, round.S("id")));
        Assert.AreEqual(6m, all["bySupervisor"]![0]!.N("average"));
        var filtered = f.Store.Read(c => EvaluationSurvey.Statistics(c, Admin, round.S("id"), city: "CityA"));
        Assert.AreEqual(2m, filtered["bySupervisor"]![0]!.N("average"));
        Assert.AreEqual(1m, filtered.N("totalResponses"));
    }
    [TestMethod]
    public void DuplicateSubmitUsesStableErrorCode()
    {
        using var f = new PayFixture(); var r = Setup(f);
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), Reply(r)));
        var ex = Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), Reply(r))));
        Assert.AreEqual("already_submitted", ex.Code);
    }
    [TestMethod]
    public void RetentionPurgeDeletesAttachmentsOlderThanSixMonths()
    {
        using var f = new PayFixture(); var r = Setup(f); var input = Reply(r);
        string tinyPng = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        input["staffAnswers"]![0]!["attachments"] = new JsonArray(new JsonObject { ["kind"] = "image", ["fileName"] = "a.png", ["contentType"] = "image/png", ["dataBase64"] = tinyPng });
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, r.S("sharingToken"), input, store: f.Store));
        var att = f.Store.Read(c => Store.List(c, EvaluationSurvey.AttachmentKind)).Single();
        att["createdAt"] = DateTimeOffset.UtcNow.AddMonths(-7).ToString("O");
        f.Store.Write((c, tx) => { Store.Save(c, EvaluationSurvey.AttachmentKind, att, tx); return true; });
        int removed = EvaluationSurvey.PurgeExpiredAttachments(f.Store, DateTimeOffset.UtcNow);
        Assert.AreEqual(1, removed);
        Assert.HasCount(0, f.Store.Read(c => Store.List(c, EvaluationSurvey.AttachmentKind)));
    }
}
