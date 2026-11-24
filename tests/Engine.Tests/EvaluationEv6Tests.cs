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

    static JsonObject AppReply(JsonObject section, string iqama = "2000000301", bool withSystems = true)
    {
        var staffCards = section["staff"]!.AsArray().OfType<JsonObject>()
            .Where(s => EvaluationSurvey.ServesCity(s, "Synthetic city"))
            .Select(s => (JsonNode?)new JsonObject
            {
                ["staffId"] = s.S("id"),
                ["ratings"] = new JsonObject(EvaluationSurvey.StaffDimensions.Select(k => KeyValuePair.Create<string, JsonNode?>(k, JsonValue.Create(7))))
            }).ToArray();
        string platform = section.S("key")["app:".Length..];
        var input = new JsonObject
        {
            ["iqama"] = iqama,
            ["name"] = "Synthetic EV6 rider",
            ["city"] = "Synthetic city",
            ["nationality"] = "egyptian",
            ["workApps"] = new JsonArray(platform),
            ["staffAnswers"] = new JsonArray(staffCards),
            ["appAnswers"] = new JsonArray(new JsonObject
            {
                ["app"] = platform,
                ["overall"] = 4,
                ["factorRatings"] = new JsonObject { ["orderVolume"] = 3 }
            }),
            ["financeAnswer"] = new JsonObject
            {
                ["overall"] = 3,
                ["factorRatings"] = new JsonObject { ["responsiveness"] = 2 },
                ["notes"] = "Synthetic finance"
            }
        };
        if (withSystems)
        {
            input["systemsAnswer"] = new JsonObject
            {
                ["overall"] = 5,
                ["factorRatings"] = new JsonObject
                {
                    ["formsEase"] = 4,
                    ["dataClarity"] = 5,
                    ["helpfulness"] = 3,
                    ["responsiveness"] = 4
                },
                ["notes"] = "Synthetic systems note"
            };
        }
        return input;
    }

    [TestMethod]
    public void R2_SystemsCardSubmitEditExcelScorecardAndComplaintVisibility()
    {
        using var f = new PayFixture();
        var round = OpenSectionedRound(f,
            Person("Synthetic ops", EvaluationSurvey.DeptOperations, apps: ["Ninja"]));
        var ninja = Section(round, "app:Ninja");
        var schema = f.Store.Read(c => EvaluationSurvey.Schema(EvaluationSurvey.ByToken(c, ninja.S("sharingToken"))));
        Assert.IsTrue(schema.B("includeFinanceCard"));
        Assert.IsTrue(schema.B("includeSystemsCard"));
        Assert.AreEqual("تقييم الأنظمة والبيانات", schema.S("systemsTitleAr"));
        CollectionAssert.AreEquivalent(EvaluationSurvey.SystemsFactors, schema["systemsFactors"]!.AsArray().OfType<JsonObject>().Select(x => x.S("id")).ToArray());

        var missing = AppReply(ninja, "2000000301", withSystems: false);
        Assert.ThrowsExactly<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, ninja.S("sharingToken"), missing)));

        var input = AppReply(ninja, "2000000301");
        input["systemsAnswer"]!["complaint"] = true;
        input["systemsAnswer"]!["complaintDetails"] = "Synthetic systems complaint details";
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, ninja.S("sharingToken"), input));
        var stored = f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)).Single(r => r.S("iqama") == "2000000301");
        Assert.AreEqual(5m, stored["systemsAnswer"]!.N("overall"));
        Assert.AreEqual(4m, stored["systemsAnswer"]!["factorRatings"]!.N("formsEase"));

        var edit = AppReply(ninja, "2000000301");
        edit["systemsAnswer"] = new JsonObject
        {
            ["reason"] = "not_used_forms_data",
            ["notes"] = "skip systems"
        };
        var edited = f.Store.Write((c, tx) => EvaluationSurvey.Edit(c, tx, ninja.S("sharingToken"), edit, "2000000301"));
        Assert.AreEqual(1m, edited.N("editCount"));
        stored = f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)).Single(r => r.S("iqama") == "2000000301");
        Assert.AreEqual("not_used_forms_data", stored["systemsAnswer"]!.S("reason"));

        var score = f.Store.Read(c => EvaluationSurvey.SystemsScorecard(c, Admin, round.S("id")));
        Assert.IsTrue(score["items"]!.AsArray().OfType<JsonObject>().Any(i => i.S("app") == "Ninja"));

        var bytes = f.Store.Read(c => EvaluationSurvey.ExportReplies(c, Admin, round.S("id")));
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        using var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = new StreamReader(sheet).ReadToEnd();
        Assert.Contains("أنظمة: العام", xml);
        Assert.Contains("أنظمة: سهولة النماذج", xml);

        // Old finance-only reply still exports (no systemsAnswer).
        f.Store.Write((c, tx) =>
        {
            var legacy = new JsonObject
            {
                ["id"] = "evreply:synthetic-legacy-finance-only",
                ["roundId"] = round.S("id"),
                ["sectionKey"] = "app:Ninja",
                ["sectionTitle"] = "تطبيق نينجا",
                ["iqama"] = "2000000399",
                ["name"] = "Synthetic legacy rider",
                ["city"] = "Synthetic city",
                ["nationality"] = "egyptian",
                ["phone"] = "",
                ["workApps"] = new JsonArray("Ninja"),
                ["companyId"] = "",
                ["staffAnswers"] = new JsonArray(),
                ["appAnswers"] = new JsonArray(new JsonObject { ["app"] = "Ninja", ["overall"] = 3 }),
                ["financeAnswer"] = new JsonObject { ["overall"] = 2, ["factorRatings"] = new JsonObject() },
                ["createdAt"] = "2026-11-01T00:00:00Z"
            };
            Store.Save(c, EvaluationSurvey.ResponseKind, legacy, tx);
            return legacy;
        });
        var legacyBytes = f.Store.Read(c => EvaluationSurvey.ExportReplies(c, Admin, round.S("id")));
        Assert.IsTrue(legacyBytes.Length > 0);

        var complaints = f.Store.Read(c => Store.List(c, RiderAsks.ServiceKind)
            .Where(r => r.S("source") == "evaluation" && r["payload"].S("target") == EvaluationSurvey.DeptSystems).ToList());
        Assert.HasCount(1, complaints);
        Assert.AreEqual("department", complaints[0]["payload"].S("targetKind"));
        Assert.AreEqual("Ninja", complaints[0]["payload"].S("app"));
        var ninjaReviewer = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "synthetic-systems-reviewer"),
            new Claim(ClaimTypes.Role, "Reviewer"),
            new Claim("permission", EvaluationSurvey.ViewPermission),
            new Claim("permission", SubmissionChoices.PermAllCompanies),
            new Claim("finance.platforms", "Ninja")
        }, "test"));
        Assert.IsTrue(f.Store.Read(c => EvaluationSurvey.CanSeeComplaint(c, ninjaReviewer, complaints[0])));
    }

    [TestMethod]
    public void R3_HrSectionIncludesManagementDepartmentAndUpdatedTitle()
    {
        using var f = new PayFixture();
        var round = OpenSectionedRound(f,
            Person("Synthetic hr", EvaluationSurvey.DeptHr, apps: []),
            Person("Synthetic management", EvaluationSurvey.DeptManagement, apps: []),
            Person("Synthetic finance", EvaluationSurvey.DeptFinance, apps: []));
        var hr = Section(round, "dept:hr");
        Assert.AreEqual("الإدارة والموارد البشرية", hr.S("title"));
        Assert.AreEqual("Management and HR", hr.S("titleEn"));
        Assert.IsTrue(hr.S("titleUr").Length > 0);
        var titles = EvaluationSurvey.SectionTitles("dept:hr");
        Assert.AreEqual("الإدارة والموارد البشرية", titles.Ar);
        var names = hr["staff"]!.AsArray().OfType<JsonObject>()
            .Select(s => round["staff"]!.AsArray().OfType<JsonObject>().Single(x => x.S("id") == s.S("id")).S("name"))
            .ToArray();
        CollectionAssert.Contains(names, "Synthetic hr");
        CollectionAssert.Contains(names, "Synthetic management");
        CollectionAssert.DoesNotContain(names, "Synthetic finance");
        CollectionAssert.Contains(EvaluationSurvey.StaffDepartmentOptions, EvaluationSurvey.DeptManagement);
    }
}
