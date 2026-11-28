using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public class NinjaDefaultsNset1Tests
{
    static string Rider(int n) => n.ToString("D10");

    static JsonObject NonZeroNinjaSalary()
    {
        var cells = new JsonObject();
        foreach (string cat in PaySettings.Categories)
        {
            cells[cat] = new JsonObject
            {
                ["enabled"] = true,
                ["chargeCompanyVehicle"] = false,
                ["fuelCap"] = 0,
                ["internet"] = 100,
                ["bands"] = new JsonArray(new JsonObject
                {
                    ["min"] = 0, ["base"] = 2500, ["extraRate"] = 5, ["fuelCap"] = 0, ["internet"] = 100
                })
            };
        }
        return new JsonObject
        {
            ["name"] = "نينجا — راتب ثابت (اختبار)",
            ["type"] = "target_salary",
            ["accountingRole"] = "principal",
            ["eligibilitySetId"] = "",
            ["parameters"] = new JsonObject { ["categories"] = cells }
        };
    }

    [TestMethod]
    public void SeedDefaults_NinjaOnly_CreatesEligibilityTermsAndSchemesAsDrafts()
    {
        using var f = new PayFixture();
        Assert.ThrowsExactly<ApiError>(() => f.Store.Write((c, tx) => PaySettings.SeedDefaults(c, tx, PayFixture.Maker, "keeta", "2026-10")));

        var ninjaFirst = f.Store.Write((c, tx) => PaySettings.SeedDefaults(c, tx, PayFixture.Maker, "ninja", "2026-10"));
        Assert.AreEqual("ninja", ninjaFirst.S("app"));
        Assert.AreEqual("2026-10", ninjaFirst.S("period"));
        Assert.AreEqual(4m, ninjaFirst.N("created"));
        var drafts = ninjaFirst["drafts"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.IsTrue(drafts.All(d => d.S("status") == "draft"));
        Assert.AreEqual(1, drafts.Count(d => d.S("kind") == "pay_eligibility"));
        Assert.AreEqual(1, drafts.Count(d => d.S("kind") == "pay_terms"));
        Assert.AreEqual(2, drafts.Count(d => d.S("kind") == "pay_scheme"));

        var second = f.Store.Write((c, tx) => PaySettings.SeedDefaults(c, tx, PayFixture.Maker, "ninja", "2026-10"));
        Assert.AreEqual(0m, second.N("created"));
        Assert.AreEqual(4m, second.N("existing"));

        var ninjaSalary = drafts.Single(d => d.S("code") == "ninja-salary");
        Assert.ThrowsExactly<ApiError>(() => f.Approve("pay_scheme", "ninja", ninjaSalary));
        Assert.AreEqual("draft", f.Store.Read(c => Store.Get(c, ninjaSalary.S("id"), "pay_scheme")!.S("status")));

        var ninjaComm = drafts.Single(d => d.S("code") == "ninja-commission");
        Assert.ThrowsExactly<ApiError>(() => f.Store.Write((c, tx) => PaySettings.Approve(c, tx, PayFixture.Maker, "pay_scheme", "ninja", ninjaComm.S("id"))));
        Assert.AreEqual("approved", f.Approve("pay_scheme", "ninja", ninjaComm).S("status"));
    }

    [TestMethod]
    public void ParentTermsDisplay_NinjaStoreMode_ReadsLocalSnapshots()
    {
        using var f = new PayFixture();
        f.Store.Write((c, tx) =>
        {
            Store.Save(c, "ninja_evaluation_snapshot", new()
            {
                ["id"] = "snap-1",
                ["period"] = "2026-10",
                ["frozen"] = true,
                ["policyVersion"] = "p1",
                ["createdAt"] = "2026-10-15T00:00:00Z",
                ["accountIdsJson"] = "[\"acc-a\"]",
                ["reason"] = "test"
            }, tx);
            return true;
        });
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Operations:Source"] = "store" })
            .Build();
        var env = new DevEnv();
        var doc = f.Store.Read(c => ParentTermsDisplay.Build(c, PayFixture.Maker, "ninja", "2026-10", cfg, env));
        Assert.AreEqual("store", doc.S("source"));
        Assert.IsTrue(doc["erpExpected"]!.AsArray().Count >= 1);
    }

    [TestMethod]
    public void ParentTermsDisplay_MatchesRequestedMonthViaQueryJson_NotCreatedAtPrefix()
    {
        using var f = new PayFixture();
        f.Store.Write((c, tx) =>
        {
            // createdAt is November but QueryJson says October — must match 2026-10 only via Year/Month.
            Store.Save(c, "ninja_evaluation_snapshot", new()
            {
                ["id"] = "snap-q",
                ["frozen"] = true,
                ["policyVersion"] = "p1",
                ["createdAt"] = "2026-11-02T00:00:00Z",
                ["queryJson"] = """{"Year":2026,"Month":10,"View":"riders","Page":1,"PageSize":50}""",
                ["accountIdsJson"] = "[\"acc-q\"]",
                ["reason"] = "test"
            }, tx);
            // createdAt starts with 2026-10 but QueryJson is September — must NOT match October.
            Store.Save(c, "ninja_evaluation_snapshot", new()
            {
                ["id"] = "snap-wrong",
                ["frozen"] = true,
                ["policyVersion"] = "p1",
                ["createdAt"] = "2026-10-31T00:00:00Z",
                ["queryJson"] = """{"Year":2026,"Month":9,"View":"riders"}""",
                ["accountIdsJson"] = "[\"acc-wrong\"]",
                ["reason"] = "test"
            }, tx);
            return true;
        });
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Operations:Source"] = "store" })
            .Build();
        var env = new DevEnv();
        var doc = f.Store.Read(c => ParentTermsDisplay.Build(c, PayFixture.Maker, "ninja", "2026-10", cfg, env));
        // Ninja erpExpected lists matching snapshots (month via QueryJson Year/Month, never createdAt).
        var snaps = doc["erpExpected"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.IsTrue(snaps.Any(r => r.S("id") == "snap-q"), doc.ToJsonString());
        Assert.IsFalse(snaps.Any(r => r.S("id") == "snap-wrong"), doc.ToJsonString());
    }

    [TestMethod]
    public void DefaultSchemesCatalog_HasNoKeetaGates()
    {
        var catalog = PaySettings.DefaultSchemesCatalog();
        Assert.IsNull(catalog["keeta"]);
        Assert.IsNotNull(catalog["ninja"]);
        Assert.IsFalse(catalog.ToJsonString().Contains("keeta-salary-eligibility", StringComparison.Ordinal));
        Assert.IsFalse(catalog.ToJsonString().Contains("shiftCompliancePercent", StringComparison.Ordinal));
    }

    [TestMethod]
    public void NinjaLiveCategory_UsesLiveVehicleOps_NotEmptyLocalPrivateFallback()
    {
        using var f = new PayFixture();
        string company = FinancialCore.Sponsors[0].Id;
        string rid = Rider(401);
        f.Store.Write((c, tx) =>
        {
            Store.Save(c, "rider", new()
            {
                ["id"] = rid, ["iqama"] = rid, ["isDeliveryRider"] = true,
                ["sponsorId"] = company, ["engagementType"] = "SponsoredInternal"
            }, tx);
            return true;
        });
        // Empty local vehicle store would invent private; liveOps supplies company bike.
        var assignments = new List<JsonObject>
        {
            new() { ["id"] = "va-live", ["riderId"] = rid, ["vehicleId"] = "v-live", ["startedAt"] = "2026-10-01T00:00:00Z", ["endedAt"] = "" }
        };
        var vehicles = new List<JsonObject>
        {
            new() { ["id"] = "v-live", ["vehicleType"] = 1, ["ownershipType"] = 1 }
        };
        var live = PayReviewDraft.KindAwareLiveOps([], assignments, vehicles);
        string withLive = f.Store.Read(c =>
        {
            var person = Store.Get(c, rid, "rider")!;
            return PayReviewCategory.Derive(c, null, person, "2026-10", live);
        });
        Assert.AreEqual("sponsored_company_bike", withLive);
        string withoutLive = f.Store.Read(c =>
        {
            var person = Store.Get(c, rid, "rider")!;
            return PayReviewCategory.Derive(c, null, person, "2026-10");
        });
        Assert.AreEqual("sponsored_private", withoutLive);
    }

    sealed class DevEnv : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Engine.Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    [TestMethod]
    public void NinjaDefaultAssignmentPreviewSavesDraftsAndDetectsConflicts()
    {
        using var f = new PayFixture();
        var seeded = f.Store.Write((c, tx) => PaySettings.SeedDefaults(c, tx, PayFixture.Maker, "ninja", "2026-10"));
        var nComm = f.Approve("pay_scheme", "ninja", seeded["drafts"]!.AsArray().OfType<JsonObject>().Single(d => d.S("code") == "ninja-commission"));
        var nSal = f.Approve("pay_scheme", "ninja", f.Draft("pay_scheme", "ninja", NonZeroNinjaSalary(), "ninja-salary-active"));

        string company = FinancialCore.Sponsors[0].Id;
        f.Store.Write((c, tx) =>
        {
            foreach (int i in new[] { 1, 2, 3 })
            {
                string r = Rider(300 + i);
                Store.Save(c, "rider", new() { ["id"] = r, ["iqama"] = r, ["isDeliveryRider"] = true, ["sponsorId"] = company }, tx);
            }
            Store.Save(c, "platform_account", new() { ["id"] = "n-acc-1", ["platformCode"] = "NINJA", ["paymentModelCode"] = "1" }, tx);
            Store.Save(c, "platform_account", new() { ["id"] = "n-acc-2", ["platformCode"] = "NINJA", ["paymentModelCode"] = "2" }, tx);
            Store.Save(c, "rider_account_assignment", new() { ["id"] = "na-1", ["platformAccountId"] = "n-acc-1", ["riderId"] = Rider(301), ["paymentModelCode"] = "PayPerOrder", ["effectiveFrom"] = "2026-09-15" }, tx);
            Store.Save(c, "rider_account_assignment", new() { ["id"] = "na-2", ["platformAccountId"] = "n-acc-2", ["riderId"] = Rider(302), ["paymentModelCode"] = "Salary", ["effectiveFrom"] = "2026-10-01" }, tx);
            Store.Save(c, "rider_account_assignment", new() { ["id"] = "na-3", ["platformAccountId"] = "n-acc-1", ["riderId"] = Rider(303), ["effectiveFrom"] = "2026-10-01" }, tx);
            return true;
        });

        var schemeMap = new JsonObject { ["commission"] = nComm.S("id"), ["salary"] = nSal.S("id") };
        var preview = f.Store.Read(c => PayAssignments.PreviewDefaults(c, PayFixture.Maker, "ninja", new JsonObject
        {
            ["period"] = "2026-10",
            ["schemeMap"] = schemeMap.DeepClone()
        }));
        var counts = preview["counts"]!.AsObject();
        Assert.AreEqual(3m, counts.N("eligibleRiders"));
        Assert.AreEqual(2m, counts.N("commission"));
        Assert.AreEqual(1m, counts.N("salary"));
        Assert.IsFalse(preview.B("canCommit"));
        Assert.AreEqual("", preview.S("fingerprint"));
        Assert.AreEqual(0, preview["errors"]!.AsArray().Count);

        var saved = f.Store.Write((c, tx) => PayAssignments.SaveDefaultDrafts(c, tx, PayFixture.Maker, "ninja", new JsonObject
        {
            ["period"] = "2026-10",
            ["schemeMap"] = schemeMap.DeepClone()
        }));
        Assert.AreEqual(3m, saved.N("drafted"));
        Assert.ThrowsExactly<ApiError>(() => f.Store.Read(c => PayAssignments.Resolve(c, null, Rider(301), "ninja", "2026-10")));
        foreach (var r in saved["rows"]!.AsArray().OfType<JsonObject>())
        {
            Assert.ThrowsExactly<ApiError>(() => f.Store.Write((c, tx) => PayAssignments.Approve(c, tx, PayFixture.Maker, "ninja", r.S("id"))));
            f.Store.Write((c, tx) => PayAssignments.Approve(c, tx, PayFixture.Checker, "ninja", r.S("id")));
        }
        Assert.AreEqual(nComm.S("id"), f.Store.Read(c => PayAssignments.Resolve(c, null, Rider(301), "ninja", "2026-10").S("schemeId")));
        Assert.AreEqual(nSal.S("id"), f.Store.Read(c => PayAssignments.Resolve(c, null, Rider(302), "ninja", "2026-10").S("schemeId")));

        // Conflict: same rider on commission + salary accounts overlapping the month.
        f.Store.Write((c, tx) =>
        {
            Store.Save(c, "rider_account_assignment", new()
            {
                ["id"] = "na-conflict",
                ["platformAccountId"] = "n-acc-2",
                ["riderId"] = Rider(301),
                ["paymentModelCode"] = "Salary",
                ["effectiveFrom"] = "2026-10-10"
            }, tx);
            return true;
        });
        var conflict = f.Store.Read(c => PayAssignments.PreviewDefaults(c, PayFixture.Maker, "ninja", new JsonObject
        {
            ["period"] = "2026-10",
            ["schemeMap"] = schemeMap.DeepClone()
        }));
        Assert.IsTrue(conflict["errors"]!.AsArray().OfType<JsonObject>().Any(e => e.S("code") == "conflicting_ninja_payment_model"));
    }
}
