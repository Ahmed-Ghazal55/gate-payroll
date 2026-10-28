using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Claims;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class RiderSimpleStatementTests
{
    private const string Iqama = "2000000999";
    private static string Bw => FinancialCore.Sponsors[0].Id;
    private static string Ex => FinancialCore.Sponsors[1].Id;
    private static ClaimsPrincipal Admin => UserAccess.Principal(new() { ["role"] = "Admin", ["username"] = "synthetic-admin" });
    private static void InStore(Action<Store> test)
    {
        string path = Path.Combine(Path.GetTempPath(), "gate-simple-statement-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(path);
            store.Write((c, tx) => { Store.Save(c, "rider", new() { ["id"] = Iqama, ["iqama"] = Iqama, ["name"] = "Synthetic Rider", ["financeSponsorId"] = Bw, ["phone"] = "not-in-statement", ["iban"] = "not-in-statement" }, tx); return true; });
            test(store);
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    private static JsonObject Line(string code, string sponsor, decimal debit, decimal credit, string debt = "") => FinancialCore.CreateJournalLine(code, sponsor, debit, credit, "Synthetic line", Iqama, debt);
    private static void Post(Store store, string type, string period, string reference, params JsonObject[] lines) =>
        store.Write((c, tx) => FinancialCore.PostJournal(c, tx, type, "Synthetic " + type, period, reference, new JsonArray(lines.Select(l => (JsonNode?)l).ToArray()), "synthetic"));
    private static void Debt(Store store, string id, string owner, string category = "Advance", string platform = "Jahez", bool verified = true) => store.Write((c, tx) =>
    {
        Store.Save(c, "debt", new() { ["id"] = id, ["iqama"] = Iqama, ["category"] = category, ["platform"] = platform,
            ["status"] = "active", ["totalAmount"] = 75m, ["remainingBalance"] = 75m, ["creditorSponsorId"] = owner,
            ["originalCreditorSponsorId"] = owner, ["journalEntryId"] = verified ? "synthetic-opening" : "", ["createdAt"] = "2026-01-01", ["effectiveFrom"] = "2026-01" }, tx);
        return true;
    });
    private static JsonObject Simple(Store store, string from = "2026-10", string to = "2026-10", string? sponsor = null, ClaimsPrincipal? user = null) =>
        store.Read(c => RiderStatements.BuildSimple(c, Iqama, from, to, sponsor, user ?? Admin));
    private static decimal Subledger(Store store, string to, string? sponsor = null) => store.Read(c => J.Money(Store.List(c, "journal")
        .Where(j => string.CompareOrdinal(j.S("period"), to) <= 0 && j.S("status", "posted") == "posted")
        .SelectMany(j => j["lines"]!.AsArray().OfType<JsonObject>())
        .Where(l => l.S("iqama") == Iqama && (sponsor == null || l.S("sponsorId") == sponsor) && l.S("accountCode") is "2110" or "1210" or "1211" or "1230")
        .Sum(l => l.N("credit") - l.N("debit"))));

    [TestMethod]
    public void MonthlyStatementReconcilesPostedPayDeductionsPaymentDebtAndSeparateJahezCash()
    {
        InStore(store =>
        {
            Post(store, "reviewed_opening", "2026-09", "prior-payable", Line("3101", Bw, 200, 0), Line("2110", Bw, 0, 200));
            AccountingPostingTests.SeedCash(store, "BANK.BW", 5000);
            store.Write((c, tx) =>
            {
                var result = new PayrollResult(Iqama, "2026-10", "Synthetic", 200, 1650, 1850, 1850, 0, 0,
                    [new("base_salary", "salary", 2000, "salary"), new("vehicle_rent", "vehicle", -300, "vehicle"), new("fine", "fine", -50, "fine")], []);
                var run = new JsonObject { ["id"] = "october-run", ["iqama"] = Iqama };
                AccountingEvents.PostPayroll(c, tx, result, run, Bw, "synthetic"); Store.Save(c, "run", run, tx);
                AccountingEvents.PostPayment(c, tx, new() { ["amount"] = -400, ["iqama"] = Iqama, ["period"] = "2026-10", ["sourceRef"] = "payment" }, Bw, "BANK.BW", "synthetic");
                return true;
            });
            Debt(store, "advance", Bw); Debt(store, "jahez-cash", Bw, "PlatformTransferredDebt");
            Post(store, "debt_accrual", "2026-10", "advance", Line("1210", Bw, 100, 0, "advance"), Line("2130", Bw, 0, 100, "advance"));
            Post(store, "debt_accrual", "2026-10", "jahez-cash", Line("1230", Bw, 200, 0, "jahez-cash"), Line("2140", Bw, 0, 200, "jahez-cash"));
            Post(store, "payroll_deduction", "2026-10", "installment", Line("2110", Bw, 50, 0, "advance"), Line("1210", Bw, 0, 50, "advance"));
            Post(store, "payroll_accrual", "2026-11", "future", Line("5301", Bw, 900, 0), Line("2110", Bw, 0, 900));
            string before = store.Read(c => JsonSerializer.Serialize(ReleaseSafety.Capture(c))); long revision = store.Revision;
            var statement = Simple(store, sponsor: "BW");
            Assert.AreEqual(200m, statement.N("openingBalance")); Assert.AreEqual(1150m, statement.N("closingNet"));
            Assert.AreEqual(Subledger(store, "2026-10", Bw), statement.N("closingNet"));
            Assert.AreEqual(1400m, statement.N("closingSalaryNet")); Assert.AreEqual(-200m, statement.N("closingJahezCashNet")); Assert.AreEqual(-50m, statement.N("closingOtherDebtNet"));
            var totals = statement["periodTotals"]!; Assert.AreEqual(2000m, totals.N("earned")); Assert.AreEqual(-400m, totals.N("deducted")); Assert.AreEqual(-400m, totals.N("received")); Assert.AreEqual(-200m, totals.N("jahez_cash"));
            Assert.AreEqual(statement.N("closingNet"), statement.N("openingBalance") + statement.N("netMovement"));
            Assert.AreEqual(statement.N("netMovement"), totals.AsObject().Sum(p => decimal.Parse(p.Value!.ToString())));
            int sourceCount = store.Read(c => Store.List(c, "journal").Where(j => j.S("period") == "2026-10").SelectMany(j => j["lines"]!.AsArray()).OfType<JsonObject>().Count(l => l.S("iqama") == Iqama));
            var details = statement["details"]!.AsArray(); Assert.AreEqual(sourceCount, details.Count);
            Assert.IsTrue(details.OfType<JsonObject>().All(r => r.S("journalId") != "" && r.N("lineNumber") > 0));
            Assert.IsTrue(details.OfType<JsonObject>().Any(r => !r.B("affectsNet") && r.N("amount") != 0));
            string json = statement.ToJsonString(); foreach (string field in new[] { "debit", "credit", "accountCode", "phone", "iban" }) Assert.IsFalse(json.Contains('"' + field + '"'));
            Assert.AreEqual(before, store.Read(c => JsonSerializer.Serialize(ReleaseSafety.Capture(c)))); Assert.AreEqual(revision, store.Revision);
        });
    }

    [TestMethod]
    public void DeficitAndLaterOffsetAreNotEarnedTwiceAndReconcile1211()
    {
        InStore(store =>
        {
            store.Write((c, tx) => AccountingEvents.PostPayroll(c, tx,
                new(Iqama, "2026-10", "Synthetic", 0, -150, -150, 0, 150, 0, [new("base_salary", "salary", 100, "salary"), new("company_fee", "fee", -250, "fee")], []), new() { ["id"] = "deficit" }, Bw, "synthetic"));
            var october = Simple(store); Assert.AreEqual(-150m, october.N("closingNet")); Assert.AreEqual(Subledger(store, "2026-10"), october.N("closingNet"));
            Assert.AreEqual(100m, october["periodTotals"].N("earned")); Assert.AreEqual(0m, october["periodTotals"].N("debt_added"));
            store.Write((c, tx) => AccountingEvents.PostPayroll(c, tx,
                new(Iqama, "2026-11", "Synthetic", -150, 230, 80, 80, 0, 0, [new("base_salary", "salary", 230, "salary")], []), new() { ["id"] = "recovery" }, Bw, "synthetic"));
            var november = Simple(store, "2026-11", "2026-11"); Assert.AreEqual(-150m, november.N("openingBalance")); Assert.AreEqual(80m, november.N("closingNet"));
            Assert.AreEqual(230m, november["periodTotals"].N("earned")); Assert.AreEqual(0m, november["periodTotals"].N("debt_collected"));
            Assert.AreEqual(Subledger(store, "2026-11"), november.N("closingNet"));
        });
    }

    private static ClaimsPrincipal Scoped(string category = "Advance", string platform = "Jahez") => UserAccess.Principal(new()
    {
        ["role"] = "FinancialAccountant", ["username"] = "synthetic-bw",
        ["financialScope"] = new JsonObject { ["sponsorIds"] = new JsonArray("BW"), ["accountCodes"] = new JsonArray("BANK.BW"), ["categoryCodes"] = new JsonArray(category), ["platforms"] = new JsonArray(platform) }
    });

    [TestMethod]
    public void CompanyCategoryPlatformAndHistoricalLineOwnershipAreEnforcedBeforeAggregation()
    {
        InStore(store =>
        {
            Debt(store, "moving", Ex); Debt(store, "hidden-category", Bw, "WorkPermit"); Debt(store, "hidden-platform", Bw, platform: "Keeta");
            Post(store, "intercompany_debt_transfer", "2026-10", "transfer", Line("1230", Bw, 100, 0, "moving"), Line("1310", Bw, 0, 100, "moving"), Line("1310", Ex, 100, 0, "moving"), Line("1230", Ex, 0, 100, "moving"));
            Post(store, "debt_accrual", "2026-10", "secret-company", Line("1210", Ex, 900, 0), Line("2130", Ex, 0, 900));
            Post(store, "debt_accrual", "2026-10", "secret-category", Line("1210", Bw, 70, 0, "hidden-category"), Line("2130", Bw, 0, 70, "hidden-category"));
            Post(store, "debt_accrual", "2026-10", "secret-platform", Line("1210", Bw, 80, 0, "hidden-platform"), Line("2130", Bw, 0, 80, "hidden-platform"));
            var user = Scoped(); var statement = Simple(store, user: user); var rows = statement["details"]!.AsArray().OfType<JsonObject>().ToList();
            Assert.AreEqual(2, rows.Count); Assert.IsTrue(rows.All(r => r.S("sponsorId") == Bw)); Assert.AreEqual(-100m, statement.N("closingNet"));
            Assert.AreEqual(-100m, statement["periodTotals"].N("transferred")); Assert.AreEqual(1, statement["transactionTypes"]!.AsArray().Count);
            Assert.IsTrue(statement.B("scopeLimited")); Assert.AreEqual(8m, statement.N("excludedLineCount")); Assert.IsFalse(statement.B("closingNetIsComplete"));
            Assert.AreEqual("صافي جزئي ضمن نطاقك — لا يمثل الرصيد الكامل", statement.S("closingLabelAr"));
            var selected = Simple(store, sponsor: "BW", user: user); Assert.AreEqual(4m, selected.N("excludedLineCount")); Assert.IsTrue(selected.B("scopeLimited"));
            var admin = Simple(store); Assert.IsFalse(admin.B("scopeLimited")); Assert.AreEqual(0m, admin.N("excludedLineCount")); Assert.IsTrue(admin.B("closingNetIsComplete"));
            var selectedAdmin = Simple(store, sponsor: "BW"); Assert.IsFalse(selectedAdmin.B("scopeLimited")); Assert.IsTrue(selectedAdmin.B("closingNetIsComplete"));

            Assert.AreEqual(403, Assert.ThrowsExactly<ApiError>(() => Simple(store, sponsor: "EX", user: user)).Status);
            Assert.IsTrue(Simple(store)["details"]!.AsArray().Count > rows.Count); // Administrative override.
            Assert.AreEqual(Subledger(store, "2026-10"), Simple(store).N("closingNet"));
        });
    }

    [TestMethod]
    [DataRow("company")]
    [DataRow("category")]
    [DataRow("platform")]
    [DataRow("payroll.view")]
    public void ScopeExclusionsFlagOpeningHistoryButNotFuturePeriods(string reason)
    {
        InStore(store =>
        {
            string owner = reason == "company" ? Ex : Bw;
            string category = reason == "category" ? "WorkPermit" : "Advance";
            string platform = reason == "platform" ? "Keeta" : "Jahez";
            Debt(store, "restricted", owner, category, platform);
            bool payroll = reason == "payroll.view";
            string type = payroll ? "payroll_accrual" : "debt_accrual";
            JsonObject[] Lines() => payroll
                ? [Line("5301", owner, 100, 0), Line("2110", owner, 0, 100)]
                : [Line("1210", owner, 100, 0, "restricted"), Line("2130", owner, 0, 100, "restricted")];
            Post(store, type, "2026-09", "restricted-opening", Lines());
            Post(store, type, "2026-11", "future-excluded", Lines());
            var user = Scoped();
            if (payroll) user = new ClaimsPrincipal(new ClaimsIdentity(user.Claims.Where(c => c.Type != "permission" || c.Value != "payroll.view"), "synthetic"));
            var result = Simple(store, user: user);
            Assert.IsTrue(result.B("scopeLimited")); Assert.AreEqual(2m, result.N("excludedLineCount")); Assert.IsFalse(result.B("closingNetIsComplete"));
            Assert.AreEqual(0m, result.N("openingBalance")); Assert.AreEqual(0m, result.N("closingNet"));
            Assert.AreEqual(0, result["details"]!.AsArray().Count); Assert.AreEqual(0, result["transactionTypes"]!.AsArray().Count);
            Assert.IsFalse(result.ContainsKey("excludedLines")); Assert.IsFalse(result.ContainsKey("excludedAmount"));
            var unrestricted = Simple(store); Assert.IsFalse(unrestricted.B("scopeLimited")); Assert.AreEqual(0m, unrestricted.N("excludedLineCount")); Assert.IsTrue(unrestricted.B("closingNetIsComplete"));
        });
    }

    [TestMethod]
    public void MissingPlatformEvidenceAndMissingDebtFailClosedForLimitedUsers()
    {
        InStore(store =>
        {
            Post(store, "payroll_accrual", "2026-10", "unattributed-pay", Line("5301", Bw, 400, 0), Line("2110", Bw, 0, 400));
            Post(store, "debt_accrual", "2026-10", "orphan", Line("1210", Bw, 50, 0, "deleted-debt"), Line("2130", Bw, 0, 50, "deleted-debt"));
            Assert.AreEqual(0, Simple(store, user: Scoped())["details"]!.AsArray().Count);
            Assert.AreEqual(4, Simple(store)["details"]!.AsArray().Count);
        });
    }

    [TestMethod]
    public void UnknownTypesAndEveryCounterpartLineSurviveWithoutChangingBalanceTwice()
    {
        InStore(store =>
        {
            Post(store, "future_adjustment", "2026-10", "future-type", Line("2110", Bw, 0, 0.03m), Line("5301", Bw, 0.03m, 0));
            var statement = Simple(store); var rows = statement["details"]!.AsArray().OfType<JsonObject>().ToList();
            Assert.AreEqual(2, rows.Count); Assert.IsTrue(rows.All(r => r.S("bucket") == "other" && r.S("labelAr") == "Synthetic line"));
            Assert.AreEqual(0.03m, statement.N("closingNet")); Assert.AreEqual(0.03m, statement["periodTotals"].N("other"));
            Assert.AreEqual(Subledger(store, "2026-10"), statement.N("closingNet"));
        });
    }

    [TestMethod]
    public void LegacyUnverifiedFlagSurvivesButDoesNotInventAPostedOpening()
    {
        InStore(store =>
        {
            Debt(store, "old", Bw, verified: false);
            var old = store.Read(c => RiderStatements.Build(c, Iqama, Bw, Admin)); Assert.IsTrue(old["statementLines"]!.AsArray().OfType<JsonObject>().Any(r => r.B("legacyEvidenceIncomplete")));
            string snapshot = store.Read(c => JsonSerializer.Serialize(ReleaseSafety.Capture(c)));
            var result = Simple(store); Assert.IsTrue(result.B("legacyEvidenceIncomplete")); Assert.AreEqual("legacy_requires_review", result.S("historyCoverage"));
            Assert.IsTrue(result["legacyUnverifiedDebts"]![0]!.B("legacyEvidenceIncomplete")); Assert.AreEqual(75m, result["legacyUnverifiedDebts"]![0]!.N("remainingBalance"));
            Assert.AreEqual(0m, result.N("closingNet")); Assert.AreEqual(Subledger(store, "2026-10"), result.N("closingNet")); Assert.AreEqual(0, result["details"]!.AsArray().Count);
            Assert.AreEqual(snapshot, store.Read(c => JsonSerializer.Serialize(ReleaseSafety.Capture(c))));
            Assert.AreEqual(0, Simple(store, user: Scoped("WorkPermit"))["legacyUnverifiedDebts"]!.AsArray().Count);
            Debt(store, "other-legacy", Ex, verified: false);
            store.Write((c, tx) => { var other = Store.Get(c, "other-legacy", "debt", tx)!; other["originalCreditorSponsorId"] = Bw; Store.Save(c, "debt", other, tx); return true; });
            var scoped = Simple(store, user: Scoped()); Assert.AreEqual(1, scoped["legacyUnverifiedDebts"]!.AsArray().Count);
            Assert.AreEqual("old", scoped["legacyUnverifiedDebts"]![0]!.S("debtId"));

        });
    }

    [TestMethod]
    public void EmptyMonthsDateBoundariesNormalizedIdentityAndDraftsAreHandled()
    {
        InStore(store =>
        {
            Post(store, "reviewed_opening", "2026-09", "opening", Line("3101", Bw, 1.01m, 0), Line("2110", Bw, 0, 1.01m));
            store.Write((c, tx) => { Store.Save(c, "journal", new() { ["id"] = "draft", ["status"] = "draft", ["period"] = "2026-10", ["lines"] = new JsonArray(Line("2110", Bw, 0, 100)) }, tx); return true; });
            var statement = store.Read(c => RiderStatements.BuildSimple(c, "٢٠٠٠٠٠٠٩٩٩", "2026-10", "2026-12", "BW", Admin));
            Assert.AreEqual(Iqama, statement["rider"].S("iqama")); Assert.AreEqual(3, statement["months"]!.AsArray().Count);
            Assert.AreEqual(1.01m, statement.N("openingBalance")); Assert.AreEqual(1.01m, statement.N("closingNet")); Assert.AreEqual(0, statement["details"]!.AsArray().Count);
            foreach (var range in new[] { ("2026-13", "2026-12"), ("2026-1", "2026-12"), ("2026-12", "2026-10") }) Assert.ThrowsExactly<ApiError>(() => Simple(store, range.Item1, range.Item2));
            Assert.AreEqual(1, Simple(store, "9999-12", "9999-12")["months"]!.AsArray().Count);
        });
    }
}
