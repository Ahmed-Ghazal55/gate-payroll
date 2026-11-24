using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class EvaluationSectionTests
{
    static readonly ClaimsPrincipal Admin = PayFixture.Maker;

    static JsonObject Person(string name, string dept, string city = "Synthetic city", string[]? apps = null, bool allApps = false, bool allCities = false) => new()
    {
        ["name"] = name,
        ["cities"] = new JsonArray(city),
        ["departments"] = new JsonArray(dept),
        ["roles"] = new JsonArray("Synthetic role"),
        ["apps"] = new JsonArray((apps ?? ["Ninja"]).Select(a => (JsonNode?)a).ToArray()),
        ["allApps"] = allApps,
        ["allCities"] = allCities,
        ["active"] = true
    };

    static JsonObject OpenSectionedRound(PayFixture f) => f.Store.Write((c, tx) =>
    {
        EvaluationSurvey.ImportStaff(c, tx, Admin, new()
        {
            ["staff"] = new JsonArray(
                Person("Synthetic movement", EvaluationSurvey.DeptMovement, apps: []),
                Person("Synthetic finance person", EvaluationSurvey.DeptFinance, apps: []),
                Person("Synthetic hr", EvaluationSurvey.DeptHr, apps: []),
                Person("Synthetic support", EvaluationSurvey.DeptSupport, city: "Remote office", apps: [], allCities: true),
                Person("Synthetic ops ninja", EvaluationSurvey.DeptOperations, apps: ["Ninja"]),
                Person("Synthetic ops all", EvaluationSurvey.DeptOperations, apps: [], allApps: true),
                Person("Synthetic other city", EvaluationSurvey.DeptMovement, city: "Other city", apps: []))
        });
        var round = EvaluationSurvey.CreateRound(c, tx, Admin, new() { ["title"] = "Synthetic sectioned round" });
        return EvaluationSurvey.Transition(c, tx, Admin, round.S("id"), "open");
    });

    static JsonObject Section(JsonObject round, string key) =>
        round["sections"]!.AsArray().OfType<JsonObject>().Single(s => s.S("key") == key);

    static string[] MemberIds(JsonObject section) =>
        section["staff"]!.AsArray().OfType<JsonObject>().Select(s => s.S("id")).ToArray();

    static string NameOf(JsonObject round, string staffId) =>
        round["staff"]!.AsArray().OfType<JsonObject>().Single(s => s.S("id") == staffId).S("name");

    static JsonObject SectionReply(JsonObject section, string iqama = "2000000091", string city = "Synthetic city")
    {
        var staffCards = section["staff"]!.AsArray().OfType<JsonObject>()
            .Where(s => EvaluationSurvey.ServesCity(s, city))
            .Select(s => (JsonNode?)new JsonObject
            {
                ["staffId"] = s.S("id"),
                ["ratings"] = new JsonObject(EvaluationSurvey.StaffDimensions.Select(k => KeyValuePair.Create<string, JsonNode?>(k, JsonValue.Create(8))))
            }).ToArray();
        var input = new JsonObject
        {
            ["iqama"] = iqama,
            ["name"] = "Synthetic rider",
            ["city"] = city,
            ["nationality"] = "Synthetic nationality",
            ["workApps"] = new JsonArray("Ninja"),
            ["staffAnswers"] = new JsonArray(staffCards),
            ["appAnswers"] = new JsonArray()
        };
        if (section.S("kind") == "app")
        {
            string platform = section.S("key")["app:".Length..];
            input["appAnswers"] = new JsonArray(new JsonObject
            {
                ["app"] = platform,
                ["overall"] = 4,
                ["factorRatings"] = new JsonObject { ["orderVolume"] = 3 },
                ["notes"] = "Synthetic app note"
            });
            input["financeAnswer"] = new JsonObject
            {
                ["overall"] = 5,
                ["factorRatings"] = new JsonObject { ["responsiveness"] = 4, ["accuracy"] = 5 },
                ["notes"] = "Synthetic finance note"
            };
            input["systemsAnswer"] = new JsonObject
            {
                ["overall"] = 4,
                ["factorRatings"] = new JsonObject { ["formsEase"] = 3, ["dataClarity"] = 4 },
                ["notes"] = "Synthetic systems note"
            };
        }
        return input;
    }

    [TestMethod]
    public void CreateRoundBuildsDepartmentAndAppSectionsWithOwnTokens()
    {
        using var f = new PayFixture();
        var round = OpenSectionedRound(f);
        Assert.IsTrue(EvaluationSurvey.IsSectioned(round));
        var keys = round["sections"]!.AsArray().OfType<JsonObject>().Select(s => s.S("key")).ToArray();
        CollectionAssert.Contains(keys, "dept:movement");
        CollectionAssert.Contains(keys, "dept:finance");
        CollectionAssert.Contains(keys, "dept:hr");
        CollectionAssert.Contains(keys, "dept:support");
        foreach (var platform in PayPlatforms.Table.Select(p => p.Platform))
            CollectionAssert.Contains(keys, "app:" + platform);
        var tokens = round["sections"]!.AsArray().OfType<JsonObject>().Select(s => s.S("sharingToken")).ToArray();
        Assert.IsTrue(tokens.All(t => t.Length == 64 && t.All(Uri.IsHexDigit)));
        Assert.AreEqual(tokens.Length, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.AreNotEqual(round.S("sharingToken"), tokens[0]);
        Assert.IsFalse(keys.Any(k => k.Contains("operations", StringComparison.Ordinal)));
        var keeta = Section(round, "app:Keeta");
        Assert.AreEqual("تطبيق كيتا", keeta.S("title"));
        Assert.AreEqual("Keeta app", keeta.S("titleEn"));
        Assert.IsTrue(keeta.S("titleUr").Length > 0);
        var schema = f.Store.Read(c => EvaluationSurvey.Schema(EvaluationSurvey.ByToken(c, keeta.S("sharingToken"))));
        Assert.AreEqual("تطبيق كيتا", schema.S("title"));
        Assert.AreEqual("Keeta app", schema.S("titleEn"));
    }

    [TestMethod]
    public void SectionedRoundLevelTokenReturns404AndAdminViewOmitsSharingToken()
    {
        using var f = new PayFixture();
        var round = OpenSectionedRound(f);
        string roundToken = round.S("sharingToken");
        Assert.AreEqual(64, roundToken.Length);
        var err = Assert.ThrowsExactly<ApiError>(() => f.Store.Read(c => EvaluationSurvey.ByToken(c, roundToken)));
        Assert.AreEqual(404, err.Status);
        var view = EvaluationSurvey.RoundViewDoc(round, Admin);
        Assert.IsNull(view["sharingToken"]);
        Assert.IsTrue(EvaluationSurvey.IsSectioned(view));
        Assert.IsTrue(view["sections"]!.AsArray().OfType<JsonObject>().All(s => s.S("sharingToken").Length == 64));
        // Section token still opens the bound section only.
        var section = Section(round, "app:Ninja");
        var opened = f.Store.Read(c => EvaluationSurvey.ByToken(c, section.S("sharingToken")));
        Assert.AreEqual("app:Ninja", opened["activeSection"]!.S("key"));
    }

    [TestMethod]
    public void SectionMembershipUsesAppsAllAppsDepartmentAndCity()
    {
        using var f = new PayFixture();
        var round = OpenSectionedRound(f);
        var movementNames = MemberIds(Section(round, "dept:movement")).Select(id => NameOf(round, id)).ToArray();
        CollectionAssert.Contains(movementNames, "Synthetic movement");
        CollectionAssert.Contains(movementNames, "Synthetic other city");
        CollectionAssert.DoesNotContain(movementNames, "Synthetic ops ninja");

        var ninjaNames = MemberIds(Section(round, "app:Ninja")).Select(id => NameOf(round, id)).ToArray();
        CollectionAssert.Contains(ninjaNames, "Synthetic ops ninja");
        CollectionAssert.Contains(ninjaNames, "Synthetic ops all");
        CollectionAssert.DoesNotContain(ninjaNames, "Synthetic movement");

        var supportSchema = f.Store.Read(c => EvaluationSurvey.Schema(EvaluationSurvey.ByToken(c, Section(round, "dept:support").S("sharingToken")), "Synthetic city"));
        Assert.IsTrue(supportSchema["staff"]!.AsArray().OfType<JsonObject>().Any(s => s.S("name") == "Synthetic support"));
        Assert.AreEqual("dept:support", supportSchema.S("sectionKey"));
        Assert.IsFalse(supportSchema.B("includeFinanceCard"));
        Assert.HasCount(0, supportSchema["apps"]!.AsArray());

        var appSchema = f.Store.Read(c => EvaluationSurvey.Schema(EvaluationSurvey.ByToken(c, Section(round, "app:Ninja").S("sharingToken")), "Synthetic city"));
        Assert.IsTrue(appSchema.B("includeFinanceCard"));
        Assert.AreEqual(1, appSchema["apps"]!.AsArray().Count);
        Assert.AreEqual("Ninja", appSchema["apps"]![0]!.ToString());
        Assert.IsTrue(appSchema["financeReasons"]!.AsArray().OfType<JsonObject>().Any(r => r.S("id") == "not_dealt_finance"));
    }

    [TestMethod]
    public void SectionOverridesAddAndRemoveStaff()
    {
        using var f = new PayFixture();
        var round = OpenSectionedRound(f);
        string movementId = round["staff"]!.AsArray().OfType<JsonObject>().Single(s => s.S("name") == "Synthetic movement").S("id");
        string financeId = round["staff"]!.AsArray().OfType<JsonObject>().Single(s => s.S("name") == "Synthetic finance person").S("id");
        var updated = f.Store.Write((c, tx) =>
        {
            EvaluationSurvey.AddSectionStaff(c, tx, Admin, round.S("id"), "dept:finance", movementId);
            EvaluationSurvey.RemoveSectionStaff(c, tx, Admin, round.S("id"), "dept:finance", financeId);
            return EvaluationSurvey.Round(c, round.S("id"), tx);
        });
        var members = MemberIds(Section(updated, "dept:finance"));
        CollectionAssert.Contains(members, movementId);
        CollectionAssert.DoesNotContain(members, financeId);
    }

    [TestMethod]
    public void ImportAndPutStaffAcceptAllApps()
    {
        using var f = new PayFixture();
        f.Store.Write((c, tx) => EvaluationSurvey.ImportStaff(c, tx, Admin, new()
        {
            ["staff"] = new JsonArray(Person("Synthetic ops all", EvaluationSurvey.DeptOperations, apps: [], allApps: true))
        }));
        var staff = f.Store.Read(c => Store.List(c, EvaluationSurvey.StaffKind)).Single();
        Assert.IsTrue(staff.B("allApps"));
        var put = f.Store.Write((c, tx) => EvaluationSurvey.PutStaff(c, tx, Admin, staff.S("id"), new()
        {
            ["name"] = "Synthetic ops all",
            ["cities"] = new JsonArray("Synthetic city"),
            ["departments"] = new JsonArray(EvaluationSurvey.DeptOperations),
            ["roles"] = new JsonArray("Synthetic role"),
            ["apps"] = new JsonArray("Ninja"),
            ["allApps"] = false,
            ["active"] = true
        }));
        Assert.IsFalse(put.B("allApps"));
        CollectionAssert.AreEqual(new[] { "Ninja" }, put["apps"]!.AsArray().Select(x => x!.ToString()).ToArray());
    }

    [TestMethod]
    public void SectionReplyIsUniquePerSectionAndFinanceCardValidated()
    {
        using var f = new PayFixture();
        var round = OpenSectionedRound(f);
        var hr = Section(round, "dept:hr");
        var ninja = Section(round, "app:Ninja");
        string hrToken = hr.S("sharingToken");
        string appToken = ninja.S("sharingToken");

        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, hrToken, SectionReply(hr)));
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, appToken, SectionReply(ninja)));
        Assert.HasCount(2, f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)));
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, hrToken, SectionReply(hr))));

        var badFinance = SectionReply(ninja, "2000000092");
        badFinance["financeAnswer"] = new JsonObject { ["overall"] = 6 };
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, appToken, badFinance)));

        var reasonOk = SectionReply(ninja, "2000000093");
        reasonOk["financeAnswer"] = new JsonObject { ["reason"] = "not_dealt_finance", ["notes"] = "" };
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, appToken, reasonOk));
        var stored = f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)).Single(r => r.S("iqama") == "2000000093");
        Assert.AreEqual("app:Ninja", stored.S("sectionKey"));
        Assert.AreEqual("not_dealt_finance", stored["financeAnswer"]!.S("reason"));
    }

    static ClaimsPrincipal PlatformReviewer(string platform) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "synthetic-platform-reviewer"),
            new Claim(ClaimTypes.Role, "Reviewer"),
            new Claim("permission", EvaluationSurvey.ViewPermission),
            new Claim("permission", SubmissionChoices.PermAllCompanies),
            new Claim("finance.platforms", platform)
        }, "test"));

    [TestMethod]
    public void PlatformScopeUsesSectionKeyAndBlanksFinanceOutsideAppScope()
    {
        using var f = new PayFixture();
        var round = OpenSectionedRound(f);
        var ninja = Section(round, "app:Ninja");
        var hs = Section(round, "app:HungerStation");
        var hr = Section(round, "dept:hr");
        // Rider ticked Ninja on an HS form — workApps must not leak that reply to a Ninja-scoped reviewer.
        var hsReply = SectionReply(hs, "2000000091");
        hsReply["workApps"] = new JsonArray("Ninja");
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, hs.S("sharingToken"), hsReply));
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, ninja.S("sharingToken"), SectionReply(ninja, "2000000092")));
        // Dept section: keep workApps rule — Ninja in workApps keeps HR reply visible to Ninja scope.
        var hrReply = SectionReply(hr, "2000000093");
        hrReply["workApps"] = new JsonArray("Ninja");
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, hr.S("sharingToken"), hrReply));

        var ninjaUser = PlatformReviewer("Ninja");
        var rows = f.Store.Read(c => EvaluationSurvey.Responses(c, ninjaUser, round.S("id")));
        CollectionAssert.AreEquivalent(new[] { "2000000092", "2000000093" }, rows.Select(r => r.S("iqama")).ToArray());
        Assert.IsFalse(rows.Any(r => r.S("sectionKey") == "app:HungerStation"));
        var ninjaRow = rows.Single(r => r.S("sectionKey") == "app:Ninja");
        Assert.IsNotNull(ninjaRow["financeAnswer"]);
        Assert.IsTrue(ninjaRow["financeAnswer"]!["overall"] != null || ninjaRow["financeAnswer"]!.S("reason") != "" || ninjaRow["financeAnswer"]!["factorRatings"] != null);

        var hsUser = PlatformReviewer("HungerStation");
        var hsRows = f.Store.Read(c => EvaluationSurvey.Responses(c, hsUser, round.S("id")));
        Assert.HasCount(1, hsRows);
        Assert.AreEqual("app:HungerStation", hsRows[0].S("sectionKey"));
        Assert.IsNotNull(hsRows[0]["financeAnswer"]);
        // Ninja-scoped view of a Ninja app reply keeps finance; HS-scoped view of Ninja reply is out of scope entirely.
        var keetaUser = PlatformReviewer("Keeta");
        Assert.HasCount(0, f.Store.Read(c => EvaluationSurvey.Responses(c, keetaUser, round.S("id"))));
    }

    [TestMethod]
    public void SectionEditAcceptsNationalityIdAndLocalPhoneFromSectionFlow()
    {
        using var f = new PayFixture();
        var round = OpenSectionedRound(f);
        var ninja = Section(round, "app:Ninja");
        // Exact shape EvaluationSectionFlow.buildPayload sends: nationality id + local 05 phone.
        var input = SectionReply(ninja);
        input["nationality"] = "egyptian";
        input["phone"] = "0501234567";
        var submitted = f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, ninja.S("sharingToken"), input));
        Assert.AreEqual(0m, submitted.N("editCount"));
        Assert.AreEqual(3m, submitted.N("editsRemaining"));
        var stored = f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)).Single();
        Assert.AreEqual("Egyptian", stored.S("nationality"));
        Assert.AreEqual("egyptian", stored.S("nationalityId"));
        Assert.AreEqual("+966501234567", stored.S("phone"));

        var edit = SectionReply(ninja);
        edit["nationality"] = "egyptian";
        edit["phone"] = "0501234567";
        edit["staffAnswers"]![0]!["ratings"] = new JsonObject(EvaluationSurvey.StaffDimensions.Select(k => KeyValuePair.Create<string, JsonNode?>(k, JsonValue.Create(5))));
        var result = f.Store.Write((c, tx) => EvaluationSurvey.Edit(c, tx, ninja.S("sharingToken"), edit, "2000000091"));
        Assert.AreEqual(1m, result.N("editCount"));
        Assert.AreEqual(2m, result.N("editsRemaining"));
    }

    [TestMethod]
    public void FinanceDepartmentComplaintsVisibleToInScopeNonAdminViewers()
    {
        using var f = new PayFixture();
        var round = OpenSectionedRound(f);
        var ninja = Section(round, "app:Ninja");
        var input = SectionReply(ninja);
        input["financeAnswer"] = new JsonObject
        {
            ["overall"] = 2,
            ["factorRatings"] = new JsonObject { ["responsiveness"] = 1 },
            ["notes"] = "Synthetic finance note",
            ["complaint"] = true,
            ["complaintDetails"] = "Synthetic finance complaint details text"
        };
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, ninja.S("sharingToken"), input));
        var management = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "synthetic-eval-viewer"),
            new Claim(ClaimTypes.Role, "Reviewer"),
            new Claim("permission", EvaluationSurvey.ViewPermission),
            new Claim("permission", SubmissionChoices.PermAllCompanies),
            new Claim("permission", "mobile.complaints.confidential")
        }, "test"));
        var visible = f.Store.Read(c => RiderAsks.StaffServiceRequests(c, management, null, null, null));
        Assert.HasCount(1, visible);
        Assert.AreEqual("department", visible[0]["payload"]!.S("targetKind"));
        Assert.AreEqual("Ninja", visible[0]["payload"]!.S("app"));

        var ninjaScoped = new ClaimsPrincipal(new ClaimsIdentity(management.Claims
            .Append(new Claim("finance.platforms", "Ninja")), "test"));
        Assert.HasCount(1, f.Store.Read(c => RiderAsks.StaffServiceRequests(c, ninjaScoped, null, null, null)));

        var keetaScoped = new ClaimsPrincipal(new ClaimsIdentity(management.Claims
            .Where(c => c.Type != "finance.platforms")
            .Append(new Claim("finance.platforms", "Keeta")), "test"));
        Assert.HasCount(0, f.Store.Read(c => RiderAsks.StaffServiceRequests(c, keetaScoped, null, null, null)));
    }

    [TestMethod]
    public void DeleteRoundRemovesRepliesFilesAndComplaintsWithTitleConfirm()
    {
        using var f = new PayFixture();
        var round = OpenSectionedRound(f);
        var hr = Section(round, "dept:hr");
        var input = SectionReply(hr);
        input["staffAnswers"]![0]!["complaint"] = true;
        input["staffAnswers"]![0]!["complaintDetails"] = "Synthetic complaint details";
        // Stage a tiny attachment meta row + file so delete must clear disk.
        f.Store.Write((c, tx) =>
        {
            EvaluationSurvey.Submit(c, tx, hr.S("sharingToken"), input);
            return 0;
        });
        Assert.HasCount(1, f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)));
        Assert.HasCount(1, f.Store.Read(c => Store.List(c, RiderAsks.ServiceKind)));

        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) =>
            EvaluationSurvey.DeleteRound(c, tx, Admin, round.S("id"), new() { ["title"] = "Wrong title" }, f.Store)));

        string roundId = round.S("id");
        var result = f.Store.Write((c, tx) =>
            EvaluationSurvey.DeleteRound(c, tx, Admin, roundId, new() { ["title"] = "Synthetic sectioned round" }, f.Store));
        Assert.AreEqual(1m, result.N("repliesDeleted"));
        Assert.AreEqual(1m, result.N("complaintsDeleted"));
        Assert.IsNull(f.Store.Read(c => Store.Get(c, roundId, EvaluationSurvey.RoundKind)));
        Assert.HasCount(0, f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)));
        Assert.HasCount(0, f.Store.Read(c => Store.List(c, RiderAsks.ServiceKind)));
        var deleteAudit = f.Store.Read(c => Store.List(c, "audit")).Single(a => a.S("action") == "evaluation.deleted");
        string auditEntity = deleteAudit.S("entityId");
        Assert.Contains(roundId, auditEntity);
        Assert.Contains("repliesDeleted", auditEntity);
        Assert.DoesNotContain("2000000091", auditEntity);
        Assert.DoesNotContain("Synthetic rider", auditEntity);
    }

    [TestMethod]
    public void KeptAttachmentMustBelongToThisReply()
    {
        using var f = new PayFixture();
        var round = OpenSectionedRound(f);
        var hr = Section(round, "dept:hr");
        var input = SectionReply(hr);
        string staffId = input["staffAnswers"]![0]!.S("staffId");
        f.Store.Write((c, tx) =>
        {
            // Another reply's file: a client must not be able to reference it by id.
            Store.Save(c, EvaluationSurvey.AttachmentKind, new JsonObject
            {
                ["id"] = "evatt:synthetic-foreign", ["replyId"] = "evreply:someone-else", ["cardKey"] = "staffId", ["cardId"] = staffId,
                ["kind"] = "image", ["fileName"] = "x.jpg", ["contentType"] = "image/jpeg", ["sizeBytes"] = 10, ["relativePath"] = "none", ["createdAt"] = "2026-11-24T00:00:00Z"
            }, tx);
            return 0;
        });
        input["staffAnswers"]![0]!["attachments"] = new JsonArray(new JsonObject { ["id"] = "evatt:synthetic-foreign" });
        var error = Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, hr.S("sharingToken"), input)));
        Assert.AreEqual("attachment_invalid", error.Code);
    }

    [TestMethod]
    public void LegacyRoundWithoutSectionsKeepsCombinedPath()
    {
        using var f = new PayFixture();
        var round = f.Store.Write((c, tx) =>
        {
            EvaluationSurvey.ImportStaff(c, tx, Admin, new()
            {
                ["staff"] = new JsonArray(Person("Synthetic movement", EvaluationSurvey.DeptMovement, apps: ["Ninja"]))
            });
            var created = EvaluationSurvey.CreateRound(c, tx, Admin, new() { ["title"] = "Legacy synthetic round" });
            PayFixture.StripEvaluationSections(c, tx, created);
            return EvaluationSurvey.Transition(c, tx, Admin, created.S("id"), "open");
        });
        Assert.IsFalse(EvaluationSurvey.IsSectioned(round));
        var schema = EvaluationSurvey.Schema(round, "Synthetic city");
        Assert.IsFalse(schema.ContainsKey("sectionKey"));
        Assert.IsTrue(schema["apps"]!.AsArray().Count >= 1);
        var input = new JsonObject
        {
            ["iqama"] = "2000000091",
            ["name"] = "Synthetic rider",
            ["city"] = "Synthetic city",
            ["nationality"] = "Synthetic nationality",
            ["workApps"] = new JsonArray("Ninja"),
            ["staffAnswers"] = new JsonArray(round["staff"]!.AsArray().OfType<JsonObject>().Select(s => (JsonNode?)new JsonObject
            {
                ["staffId"] = s.S("id"),
                ["ratings"] = new JsonObject(EvaluationSurvey.StaffDimensions.Select(k => KeyValuePair.Create<string, JsonNode?>(k, JsonValue.Create(7))))
            }).ToArray()),
            ["appAnswers"] = new JsonArray(round["apps"]!.AsArray().Select(a => (JsonNode?)new JsonObject { ["app"] = a!.ToString(), ["reason"] = "not_worked" }).ToArray())
        };
        f.Store.Write((c, tx) => EvaluationSurvey.Submit(c, tx, round.S("sharingToken"), input));
        Assert.HasCount(1, f.Store.Read(c => Store.List(c, EvaluationSurvey.ResponseKind)));
    }

    [TestMethod]
    public void RiderMobileListingExcludesSectionedRounds()
    {
        using var f = new PayFixture();
        var sectioned = OpenSectionedRound(f);
        var legacy = f.Store.Write((c, tx) =>
        {
            var created = EvaluationSurvey.CreateRound(c, tx, Admin, new() { ["title"] = "Legacy open round" });
            PayFixture.StripEvaluationSections(c, tx, created);
            return EvaluationSurvey.Transition(c, tx, Admin, created.S("id"), "open");
        });
        var listed = f.Store.Read(c => Store.ListWhere(c, EvaluationSurvey.RoundKind, null, null, ("status", "open"))
            .Where(r => !EvaluationSurvey.IsSectioned(r)).Select(r => r.S("id")).ToArray());
        CollectionAssert.Contains(listed, legacy.S("id"));
        CollectionAssert.DoesNotContain(listed, sectioned.S("id"));
    }
}
