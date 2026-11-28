using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>HR1 staff HR — synthetic fixtures only (no real names/IDs/amounts from production).</summary>
[TestClass]
public sealed class StaffHrTests
{
    const string SynIqama = "2099000101";
    static readonly string Bw = FinancialCore.ResolveSponsor("BW").Id;
    // Distinct Admin actors so company scope passes while SamePerson still blocks self-approve.
    static readonly ClaimsPrincipal Preparer = UserAccess.Principal(new() { ["id"] = "syn-hr-prep", ["username"] = "syn-hr-prep", ["role"] = "Admin" });
    static readonly ClaimsPrincipal Approver = UserAccess.Principal(new() { ["id"] = "syn-hr-appr", ["username"] = "syn-hr-appr", ["role"] = "Admin" });
    static readonly ClaimsPrincipal ManagerOnly = UserAccess.Principal(new()
    {
        ["id"] = "syn-hr-mgr",
        ["username"] = "syn-hr-mgr",
        ["role"] = "Reviewer",
        ["customPermissions"] = new JsonArray("staff.hr.view", "staff.hr.manage", "staff.contract.upload"),
        ["financialScope"] = new JsonObject { ["sponsorIds"] = new JsonArray(Bw) },
    });

    static void Run(Action<Store> body)
    {
        string dir = Path.Combine(Path.GetTempPath(), "gate-staff-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(dir);
            body(store);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    static JsonObject TinyPdf() => new()
    {
        ["fileName"] = "qiwa.pdf",
        ["dataBase64"] = Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4 synthetic")),
    };

    static JsonObject CreateEmp(Store s, ClaimsPrincipal who, string iqama = SynIqama) => s.Write((c, tx) => StaffHr.CreateEmployee(c, tx, who, new()
    {
        ["displayName"] = "Synthetic Staff",
        ["iqama"] = iqama,
        ["companyId"] = Bw,
        ["department"] = "HR",
        ["jobTitle"] = "محاسب",
        ["city"] = "جدة",
        ["nationality"] = "مصري",
        ["employmentType"] = "office",
        ["erpEmployeeId"] = "00000000-0000-4000-8000-000000000099",
    }));

    static void AddContract(Store s, string empId, string issue, string end, decimal basic = 4000m) =>
        s.Write((c, tx) => StaffHr.AddContract(c, tx, Preparer, empId, new()
        {
            ["issueDate"] = issue,
            ["endDate"] = end,
            ["basicSalary"] = basic,
            ["attachment"] = TinyPdf(),
        }));

    static JsonObject DraftAdvance(Store s, string empId, string key, decimal amount = 600m, decimal instalment = 200m) =>
        s.Write((c, tx) => StaffHr.CreateAdvance(c, tx, Preparer, empId, new()
        {
            ["category"] = "CashAdvance",
            ["amount"] = amount,
            ["monthlyInstallment"] = instalment,
            ["uniqueKey"] = key,
            ["paymentVaultCode"] = "MAIN.BW.DEBT",
        }));

    static JsonObject ApproveAdv(Store s, string empId, string debtId) =>
        s.Write((c, tx) => StaffHr.ApproveAdvance(c, tx, Approver, empId, debtId));

    [TestMethod]
    public void EmployeeCardContractMonthAndUniqueAdvance()
    {
        Run(s =>
        {
            AccountingPostingTests.SeedCash(s, "MAIN.BW.DEBT", 20000m);
            var emp = CreateEmp(s, Preparer);
            Assert.AreEqual("active", emp.S("status"));
            Assert.AreEqual("00000000-0000-4000-8000-000000000099", emp.S("erpEmployeeId"));
            AddContract(s, emp.S("id"), "2026-01-01", "2026-12-31", 4500m);
            var card = s.Read(c => StaffHr.GetEmployee(c, Preparer, emp.S("id")));
            Assert.AreEqual(4500m, card.N("basicSalary"));
            Assert.AreEqual(1, (card["contracts"] as JsonArray)!.Count);

            var month = s.Write((c, tx) => StaffHr.UpsertMonth(c, tx, Preparer, emp.S("id"), new()
            {
                ["period"] = "2026-11",
                ["attendanceDays"] = 22,
                ["overtimeHours"] = 4,
                ["latenessHours"] = 1,
                ["bonusAmount"] = 200,
                ["deductionAmount"] = 50,
            }));
            Assert.AreEqual(22m, month.N("attendanceDays"));

            var adv = DraftAdvance(s, emp.S("id"), "SYN-ADV-1");
            Assert.AreEqual("draft", adv.S("status"));
            Assert.AreEqual("", adv.S("journalEntryId"));
            Assert.AreEqual("STAFF-ADV:" + emp.S("id") + ":SYN-ADV-1", adv.S("sourceRef"));
            adv = ApproveAdv(s, emp.S("id"), adv.S("id"));
            Assert.AreEqual("active", adv.S("status"));
            Assert.AreNotEqual("", adv.S("journalEntryId"));
            var dup = Assert.ThrowsExactly<ApiError>(() => DraftAdvance(s, emp.S("id"), "SYN-ADV-1"));
            StringAssert.Contains(dup.Message, "مرجع");
        });
    }

    [TestMethod]
    public void ContractExpiryAlertsOnDashboard()
    {
        Run(s =>
        {
            var emp = CreateEmp(s, Preparer);
            var soon = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(25)).ToString("yyyy-MM-dd");
            AddContract(s, emp.S("id"), "2026-01-01", soon, 3000m);
            var dash = s.Read(c => StaffHr.Dashboard(c, Preparer));
            var alerts = dash["contractAlerts"]!.AsArray();
            Assert.IsTrue(alerts.Count >= 1);
            Assert.AreEqual("30", alerts[0]!.AsObject().S("level"));
        });
    }

    [TestMethod]
    public void StaffRunDraftReviewApproveMudadJournalAndPreparerCannotApprove()
    {
        Run(s =>
        {
            AccountingPostingTests.SeedCash(s, "MAIN.BW.DEBT", 20000m);
            var emp = CreateEmp(s, Preparer);
            AddContract(s, emp.S("id"), "2026-01-01", "2027-01-01", 5000m);
            s.Write((c, tx) => StaffHr.UpsertMonth(c, tx, Preparer, emp.S("id"), new()
            {
                ["period"] = "2026-11",
                ["attendanceDays"] = 20,
                ["overtimeHours"] = 0,
                ["latenessHours"] = 0,
                ["bonusAmount"] = 100,
                ["deductionAmount"] = 0,
            }));
            var adv = DraftAdvance(s, emp.S("id"), "SYN-RUN-ADV", 300m, 100m);
            ApproveAdv(s, emp.S("id"), adv.S("id"));

            var run = s.Write((c, tx) => StaffHr.CreateRun(c, tx, Preparer, new() { ["period"] = "2026-11", ["companyId"] = Bw }));
            Assert.AreEqual("draft", run.S("status"));
            Assert.AreEqual(1m, run.N("lineCount"));
            Assert.AreEqual(5100m, run.N("grossTotal")); // 5000 + 100
            Assert.AreEqual(5000m, run.N("netTotal")); // minus 100 instalment

            run = s.Write((c, tx) => StaffHr.SubmitReview(c, tx, Preparer, run.S("id")));
            Assert.AreEqual("review", run.S("status"));

            var self = Assert.ThrowsExactly<ApiError>(() => s.Write((c, tx) => StaffHr.Approve(c, tx, Preparer, run.S("id"))));
            StringAssert.Contains(self.Message, "مجهّز");

            run = s.Write((c, tx) => StaffHr.Approve(c, tx, Approver, run.S("id")));
            Assert.AreEqual("approved", run.S("status"));

            run = s.Write((c, tx) => StaffHr.GenerateMudad(c, tx, Preparer, run.S("id")));
            Assert.AreNotEqual("", run.S("mudadFileId"));

            run = s.Write((c, tx) => StaffHr.PostJournal(c, tx, Preparer, run.S("id")));
            Assert.AreEqual("posted", run.S("status"));
            Assert.AreNotEqual("", run.S("journalEntryId"));
            var journal = s.Read(c => Store.Get(c, run.S("journalEntryId"), "journal")!);
            Assert.AreEqual("STAFF-PAY:" + run.S("id"), journal.S("sourceRef"));
            decimal debit = journal["lines"]!.AsArray().OfType<JsonObject>().Sum(l => l.N("debit"));
            decimal credit = journal["lines"]!.AsArray().OfType<JsonObject>().Sum(l => l.N("credit"));
            Assert.AreEqual(debit, credit);

            var debt = s.Read(c => Store.List(c, "debt").Single(d => d.S("sourceRef").Contains("SYN-RUN-ADV")));
            Assert.AreEqual(200m, debt.N("remainingBalance"));

            run = s.Write((c, tx) => StaffHr.Reverse(c, tx, Approver, run.S("id")));
            Assert.AreEqual("reversed", run.S("status"));
            debt = s.Read(c => Store.List(c, "debt").Single(d => d.S("sourceRef").Contains("SYN-RUN-ADV")));
            Assert.AreEqual(300m, debt.N("remainingBalance"));
        });
    }

    [TestMethod]
    public void LinkUserEmployeeIdReadOnlyFieldOnCard()
    {
        Run(s =>
        {
            s.Write((c, tx) =>
            {
                Store.Save(c, "user", new() { ["id"] = "syn-user-1", ["username"] = "syn-user-1", ["role"] = "Reviewer" }, tx);
                return true;
            });
            var emp = s.Write((c, tx) => StaffHr.CreateEmployee(c, tx, Preparer, new()
            {
                ["displayName"] = "Synthetic Linked",
                ["iqama"] = "2099000102",
                ["companyId"] = Bw,
                ["department"] = "FIN",
                ["jobTitle"] = "محاسب",
                ["city"] = "الرياض",
                ["nationality"] = "سعودي",
                ["employmentType"] = "management",
                ["linkedUserId"] = "syn-user-1",
            }));
            Assert.AreEqual("syn-user-1", emp.S("linkedUserId"));
            var user = s.Read(c => Store.Get(c, "syn-user-1", "user")!);
            Assert.AreEqual(emp.S("id"), user.S("employeeId"));
        });
    }

    // --- HIGH rows from grok-HR1 ---

    [TestMethod]
    public void High_ManualDeductionDoesNotInflatePayable_CreditsNetOnlyOn2110()
    {
        Run(s =>
        {
            AccountingPostingTests.SeedCash(s, "MAIN.BW.DEBT", 20000m);
            var emp = CreateEmp(s, Preparer);
            AddContract(s, emp.S("id"), "2026-01-01", "2027-01-01", 5000m);
            s.Write((c, tx) => StaffHr.UpsertMonth(c, tx, Preparer, emp.S("id"), new()
            {
                ["period"] = "2026-11",
                ["attendanceDays"] = 20,
                ["bonusAmount"] = 0,
                ["deductionAmount"] = 50,
            }));
            var run = s.Write((c, tx) => StaffHr.CreateRun(c, tx, Preparer, new() { ["period"] = "2026-11", ["companyId"] = Bw }));
            Assert.AreEqual(4950m, run.N("netTotal"));
            run = s.Write((c, tx) => StaffHr.SubmitReview(c, tx, Preparer, run.S("id")));
            run = s.Write((c, tx) => StaffHr.Approve(c, tx, Approver, run.S("id")));
            run = s.Write((c, tx) => StaffHr.GenerateMudad(c, tx, Preparer, run.S("id")));
            run = s.Write((c, tx) => StaffHr.PostJournal(c, tx, Preparer, run.S("id")));
            var journal = s.Read(c => Store.Get(c, run.S("journalEntryId"), "journal")!);
            var jl = journal["lines"]!.AsArray().OfType<JsonObject>().ToList();
            decimal payableCredit = jl.Where(l => l.S("accountCode") == "2110").Sum(l => l.N("credit"));
            decimal expenseDebit = jl.Where(l => l.S("accountCode") == "5111").Sum(l => l.N("debit"));
            Assert.AreEqual(4950m, payableCredit); // net only — 50 manual must not stick on 2110
            Assert.AreEqual(4950m, expenseDebit); // expense = net + instalments (= gross − manual)
            Assert.AreEqual(0, jl.Count(l => l.S("accountCode") == "5301"));
            Assert.AreEqual(0, jl.Count(l => l.S("accountCode") == "521101")); // target code must not be current-chart
            var expLine = jl.First(l => l.S("accountCode") == "5111");
            Assert.AreEqual(SynIqama, expLine.S("iqama"));
            Assert.AreEqual("Employee", expLine.S("partyType"));
            Assert.AreEqual("5100", FinancialCore.ChartOfAccounts.First(a => a.Code == "5111").ParentCode);
            Assert.AreEqual(jl.Sum(l => l.N("debit")), jl.Sum(l => l.N("credit")));
        });
    }

    [TestMethod]
    public void High_StaffAdvanceIsDraftUntilSecondPersonApproves()
    {
        Run(s =>
        {
            AccountingPostingTests.SeedCash(s, "MAIN.BW.DEBT", 20000m);
            var emp = CreateEmp(s, Preparer);
            var draft = DraftAdvance(s, emp.S("id"), "SYN-MC-1", 400m, 100m);
            Assert.AreEqual("draft", draft.S("status"));
            Assert.AreEqual("", draft.S("journalEntryId"));
            var stored = s.Read(c => Store.Get(c, draft.S("id"), "debt")!);
            Assert.IsFalse(stored.B("agreementReviewed"));
            Assert.AreEqual(0, s.Read(c => Store.List(c, "journal").Count(j => j.S("sourceRef") == draft.S("sourceRef"))));

            var self = Assert.ThrowsExactly<ApiError>(() =>
                s.Write((c, tx) => StaffHr.ApproveAdvance(c, tx, Preparer, emp.S("id"), draft.S("id"))));
            StringAssert.Contains(self.Message, "منشئ");

            var approved = ApproveAdv(s, emp.S("id"), draft.S("id"));
            Assert.AreEqual("active", approved.S("status"));
            Assert.AreNotEqual("", approved.S("journalEntryId"));
            stored = s.Read(c => Store.Get(c, draft.S("id"), "debt")!);
            Assert.IsTrue(stored.B("agreementReviewed"));
            var journal = s.Read(c => Store.Get(c, approved.S("journalEntryId"), "journal")!);
            Assert.AreEqual(draft.S("sourceRef"), journal.S("sourceRef"));
        });
    }

    [TestMethod]
    public void High_PersonStubDoesNotOverwriteExistingNonStaffProfile()
    {
        Run(s =>
        {
            s.Write((c, tx) =>
            {
                Store.Save(c, "rider", new()
                {
                    ["id"] = "2099000199",
                    ["iqama"] = "2099000199",
                    ["name"] = "Synthetic Admin Profile",
                    ["personRole"] = "administrative",
                    ["erpSource"] = "import",
                    ["debtOnly"] = true,
                    ["status"] = "Active",
                    ["sponsorId"] = Bw,
                }, tx);
                return true;
            });
            var err = Assert.ThrowsExactly<ApiError>(() => CreateEmp(s, Preparer, "2099000199"));
            StringAssert.Contains(err.Message, "موجود");
            var kept = s.Read(c => Store.Get(c, "2099000199", "rider")!);
            Assert.AreEqual("Synthetic Admin Profile", kept.S("name"));
            Assert.IsFalse(kept.B("staffHrStub"));
        });
    }

    [TestMethod]
    public void High_KeetaPositiveCompanyLineStillAcceptedAfterHrMerge()
    {
        // Verifies release/0.34.1 company-line exception remains on this branch (Grok HR1 row).
        new KeetaOperationsImportTests().KeetaPositiveCompanyLineCountsInCycleBaseGrossWithoutPlatformAccountOrRider();
    }

    [TestMethod]
    public void Medium_ExpiredContractBlockedOnRun_AndCreateAdvanceNeedsBothPerms()
    {
        Run(s =>
        {
            var emp = CreateEmp(s, Preparer);
            AddContract(s, emp.S("id"), "2025-01-01", "2025-12-31", 4000m); // expired before 2026-11
            var blocked = Assert.ThrowsExactly<ApiError>(() =>
                s.Write((c, tx) => StaffHr.CreateRun(c, tx, Preparer, new() { ["period"] = "2026-11", ["companyId"] = Bw })));
            StringAssert.Contains(blocked.Message, "لا موظفون");

            var actions = emp["allowedActions"] as JsonArray ?? [];
            // Manager-only principal: no debts.create.advance → no create_advance action.
            var mgrEmp = s.Read(c => StaffHr.GetEmployee(c, ManagerOnly, emp.S("id")));
            var mgrActions = (mgrEmp["allowedActions"] as JsonArray)!.Select(a => a!.ToString()).ToHashSet();
            Assert.IsFalse(mgrActions.Contains("create_advance"));
            Assert.IsTrue(mgrActions.Contains("edit"));
        });
    }

    // --- MEDIUM rows from grok-HR1b ---

    [TestMethod]
    public void Medium_StaffExpense5111MapsToTarget521101OutsideOperatingPayroll()
    {
        Run(s =>
        {
            AccountingPostingTests.SeedCash(s, "MAIN.BW.DEBT", 20000m);
            var emp = CreateEmp(s, Preparer);
            AddContract(s, emp.S("id"), "2026-01-01", "2027-01-01", 4000m);
            var run = s.Write((c, tx) => StaffHr.CreateRun(c, tx, Preparer, new() { ["period"] = "2026-11", ["companyId"] = Bw }));
            run = s.Write((c, tx) => StaffHr.SubmitReview(c, tx, Preparer, run.S("id")));
            run = s.Write((c, tx) => StaffHr.Approve(c, tx, Approver, run.S("id")));
            run = s.Write((c, tx) => StaffHr.GenerateMudad(c, tx, Preparer, run.S("id")));
            run = s.Write((c, tx) => StaffHr.PostJournal(c, tx, Preparer, run.S("id")));
            var journal = s.Read(c => Store.Get(c, run.S("journalEntryId"), "journal")!);
            var exp = journal["lines"]!.AsArray().OfType<JsonObject>().First(l => l.S("accountCode") == "5111");
            Assert.AreEqual("Employee", exp.S("partyType"));
            var mapped = FinanceViews.MapAccount(exp, journal, null, new JsonObject { ["personRole"] = "administrative" }, null);
            Assert.AreEqual("521101", mapped);
            Assert.AreNotEqual("5300", FinancialCore.ChartOfAccounts.First(a => a.Code == "5111").ParentCode);
        });
    }

    [TestMethod]
    public void Medium_InstalmentCredits1220PerDebtWithCategoryAndDebtId()
    {
        Run(s =>
        {
            AccountingPostingTests.SeedCash(s, "MAIN.BW.DEBT", 20000m);
            var emp = CreateEmp(s, Preparer);
            AddContract(s, emp.S("id"), "2026-01-01", "2027-01-01", 5000m);
            var a1 = DraftAdvance(s, emp.S("id"), "SYN-D1", 300m, 100m);
            var a2 = DraftAdvance(s, emp.S("id"), "SYN-D2", 400m, 150m);
            ApproveAdv(s, emp.S("id"), a1.S("id"));
            ApproveAdv(s, emp.S("id"), a2.S("id"));
            var run = s.Write((c, tx) => StaffHr.CreateRun(c, tx, Preparer, new() { ["period"] = "2026-11", ["companyId"] = Bw }));
            run = s.Write((c, tx) => StaffHr.SubmitReview(c, tx, Preparer, run.S("id")));
            run = s.Write((c, tx) => StaffHr.Approve(c, tx, Approver, run.S("id")));
            run = s.Write((c, tx) => StaffHr.GenerateMudad(c, tx, Preparer, run.S("id")));
            run = s.Write((c, tx) => StaffHr.PostJournal(c, tx, Preparer, run.S("id")));
            var journal = s.Read(c => Store.Get(c, run.S("journalEntryId"), "journal")!);
            var credits1220 = journal["lines"]!.AsArray().OfType<JsonObject>()
                .Where(l => l.S("accountCode") == "1220" && l.N("credit") > 0).ToList();
            Assert.AreEqual(2, credits1220.Count);
            Assert.IsTrue(credits1220.All(l => l.S("debtId") != "" && l.S("category") == "CashAdvance"));
            Assert.AreEqual(250m, credits1220.Sum(l => l.N("credit")));
            foreach (var line in credits1220)
            {
                var debt = s.Read(c => Store.Get(c, line.S("debtId"), "debt")!);
                Assert.AreEqual("CashAdvance", debt.S("category"));
                Assert.AreEqual("112301", FinanceViews.MapAccount(line, journal, debt, null, null));
            }
        });
    }

    [TestMethod]
    public void Medium_OmittedEmployeeRecordedBlocked_SubmitRefusedUntilCleared()
    {
        Run(s =>
        {
            var paid = CreateEmp(s, Preparer, "2099000103");
            AddContract(s, paid.S("id"), "2026-01-01", "2027-01-01", 4000m);
            var omitted = CreateEmp(s, Preparer, "2099000104");
            // omitted has no covering contract for 2026-11
            var run = s.Write((c, tx) => StaffHr.CreateRun(c, tx, Preparer, new() { ["period"] = "2026-11", ["companyId"] = Bw }));
            Assert.AreEqual(1m, run.N("lineCount"));
            Assert.AreEqual(1m, run.N("blockedCount"));
            var blocked = run["blocked"]!.AsArray().OfType<JsonObject>().Single();
            Assert.AreEqual(omitted.S("id"), blocked.S("employeeId"));
            Assert.AreEqual("no_valid_contract", blocked.S("reason"));
            var err = Assert.ThrowsExactly<ApiError>(() =>
                s.Write((c, tx) => StaffHr.SubmitReview(c, tx, Preparer, run.S("id"))));
            StringAssert.Contains(err.Message, "محجوبون");
            AddContract(s, omitted.S("id"), "2026-01-01", "2027-01-01", 3500m);
            run = s.Write((c, tx) => StaffHr.Recalculate(c, tx, Preparer, run.S("id")));
            Assert.AreEqual(2m, run.N("lineCount"));
            Assert.AreEqual(0m, run.N("blockedCount"));
            run = s.Write((c, tx) => StaffHr.SubmitReview(c, tx, Preparer, run.S("id")));
            Assert.AreEqual("review", run.S("status"));
        });
    }

    [TestMethod]
    public void Medium_ZeroExpenseRunRefusedAtSubmit_ApprovedReturnNeedsRefuseOrApprover()
    {
        Run(s =>
        {
            AccountingPostingTests.SeedCash(s, "MAIN.BW.DEBT", 20000m);
            var emp = CreateEmp(s, Preparer);
            AddContract(s, emp.S("id"), "2026-01-01", "2027-01-01", 1000m);
            // Manual deduction equals gross → net 0, no instalment → zero journal lines.
            s.Write((c, tx) => StaffHr.UpsertMonth(c, tx, Preparer, emp.S("id"), new()
            {
                ["period"] = "2026-11",
                ["attendanceDays"] = 20,
                ["bonusAmount"] = 0,
                ["deductionAmount"] = 1000,
            }));
            var run = s.Write((c, tx) => StaffHr.CreateRun(c, tx, Preparer, new() { ["period"] = "2026-11", ["companyId"] = Bw }));
            Assert.AreEqual(0m, run.N("netTotal"));
            var zero = Assert.ThrowsExactly<ApiError>(() =>
                s.Write((c, tx) => StaffHr.SubmitReview(c, tx, Preparer, run.S("id"))));
            StringAssert.Contains(zero.Message, "صفر");

            s.Write((c, tx) => StaffHr.UpsertMonth(c, tx, Preparer, emp.S("id"), new()
            {
                ["period"] = "2026-11",
                ["attendanceDays"] = 20,
                ["bonusAmount"] = 0,
                ["deductionAmount"] = 0,
            }));
            run = s.Write((c, tx) => StaffHr.Recalculate(c, tx, Preparer, run.S("id")));
            run = s.Write((c, tx) => StaffHr.SubmitReview(c, tx, Preparer, run.S("id")));
            run = s.Write((c, tx) => StaffHr.Approve(c, tx, Approver, run.S("id")));
            Assert.AreEqual("approved", run.S("status"));

            // Manage-only actor must NOT undo a binding approval before post refuse.
            var manageView = s.Read(c => StaffHr.GetRun(c, ManagerOnly, run.S("id")));
            var manageActs = (manageView["allowedActions"] as JsonArray)!.Select(a => a!.ToString()).ToHashSet();
            Assert.IsFalse(manageActs.Contains("back_to_draft"));
            var blocked = Assert.ThrowsExactly<ApiError>(() =>
                s.Write((c, tx) => StaffHr.BackToDraft(c, tx, ManagerOnly, run.S("id"))));
            Assert.AreEqual(403, blocked.Status);
            StringAssert.Contains(blocked.Message, "صلاحية");

            // Approver may return without a refuse flag (approval is binding for manage alone).
            var approveView = s.Read(c => StaffHr.GetRun(c, Approver, run.S("id")));
            Assert.IsTrue((approveView["allowedActions"] as JsonArray)!.Select(a => a!.ToString()).Contains("back_to_draft"));
            run = s.Write((c, tx) => StaffHr.BackToDraft(c, tx, Approver, run.S("id")));
            Assert.AreEqual("draft", run.S("status"));
            Assert.AreEqual("", run.S("approvedBy"));
        });
    }

    [TestMethod]
    public void Medium_PostRefuseUnlocksBackToDraft_AndVoidsMudad()
    {
        Run(s =>
        {
            AccountingPostingTests.SeedCash(s, "MAIN.BW.DEBT", 20000m);
            var emp = CreateEmp(s, Preparer);
            AddContract(s, emp.S("id"), "2026-01-01", "2027-01-01", 4000m);
            var adv = DraftAdvance(s, emp.S("id"), "SYN-POST-REFUSE", 500m, 200m);
            ApproveAdv(s, emp.S("id"), adv.S("id"));

            var run = s.Write((c, tx) => StaffHr.CreateRun(c, tx, Preparer, new() { ["period"] = "2026-11", ["companyId"] = Bw }));
            run = s.Write((c, tx) => StaffHr.SubmitReview(c, tx, Preparer, run.S("id")));
            run = s.Write((c, tx) => StaffHr.Approve(c, tx, Approver, run.S("id")));
            run = s.Write((c, tx) => StaffHr.GenerateMudad(c, tx, Preparer, run.S("id")));
            string mudadId = run.S("mudadFileId");
            Assert.AreNotEqual("", mudadId);

            // Drift the advance balance below the snapshot so post refuses.
            s.Write((c, tx) =>
            {
                var debt = Store.Get(c, adv.S("id"), "debt", tx)!;
                debt["remainingBalance"] = 50m;
                Store.Save(c, "debt", debt, tx);
                return true;
            });
            var refuse = Assert.ThrowsExactly<ApiError>(() =>
                s.Write((c, tx) => StaffHr.PostJournal(c, tx, Preparer, run.S("id"))));
            StringAssert.Contains(refuse.Message, "رصيد السلفة");
            // HTTP layer would call MarkPostRefused after the rolled-back Write; invoke it here.
            run = s.Write((c, tx) => StaffHr.MarkPostRefused(c, tx, Preparer, run.S("id"), refuse.Message));
            Assert.IsTrue(run.B("postRefused"));

            var actions = (run["allowedActions"] as JsonArray)!.Select(a => a!.ToString()).ToHashSet();
            Assert.IsTrue(actions.Contains("back_to_draft"));
            run = s.Write((c, tx) => StaffHr.BackToDraft(c, tx, Preparer, run.S("id")));
            Assert.AreEqual("draft", run.S("status"));
            Assert.AreEqual("", run.S("mudadFileId"));
            Assert.IsTrue(run.B("mudadVoided"));
            Assert.IsFalse(run.B("postRefused"));
            var file = s.Read(c => Store.Get(c, mudadId, "financial_attachment")!);
            Assert.IsTrue(file.B("voided"));
            Assert.AreEqual("staff_run_back_to_draft", file.S("voidReason"));
        });
    }

    [TestMethod]
    public void Medium_ListRunsExposesBlockedEmployeesAndCount()
    {
        Run(s =>
        {
            var paid = CreateEmp(s, Preparer, "2099000105");
            AddContract(s, paid.S("id"), "2026-01-01", "2027-01-01", 4000m);
            var omitted = CreateEmp(s, Preparer, "2099000106");
            var run = s.Write((c, tx) => StaffHr.CreateRun(c, tx, Preparer, new() { ["period"] = "2026-11", ["companyId"] = Bw }));
            var list = s.Read(c => StaffHr.ListRuns(c, Preparer, Bw));
            var row = list.OfType<JsonObject>().Single(r => r.S("id") == run.S("id"));
            Assert.AreEqual(1m, row.N("blockedCount"));
            var blocked = row["blocked"]!.AsArray().OfType<JsonObject>().Single();
            Assert.AreEqual(omitted.S("id"), blocked.S("employeeId"));
            Assert.AreEqual("no_valid_contract", blocked.S("reason"));
            Assert.AreEqual("لا عقد سارٍ يغطي شهر المسير", blocked.S("reasonAr"));
        });
    }
}
