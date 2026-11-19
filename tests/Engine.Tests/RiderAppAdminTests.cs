using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

// Section 9 (R3, R7, U-W3-17): staff queue of rider service requests, its buttons and counts.
[TestClass]
public sealed class RiderAppAdminTests
{
    static void WithStore(Action<Store> action)
    {
        string? configured = Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        string dbName = "GateRiderAppAdmin_" + Guid.NewGuid().ToString("N");
        string tempDir = Path.Combine(Path.GetTempPath(), dbName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            try { action(new Store(tempDir)); }
            finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
            return;
        }
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        using (var m = new SqlConnection(master.ConnectionString)) { m.Open(); using var cmd = m.CreateCommand(); cmd.CommandText = $"CREATE DATABASE [{dbName}]"; cmd.ExecuteNonQuery(); }
        try
        {
            var store = new Store(tempDir, new SqlConnectionStringBuilder(configured) { InitialCatalog = dbName }.ConnectionString);
            Assert.AreEqual("mssql", store.Provider);
            action(store);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using (var m = new SqlConnection(master.ConnectionString)) { m.Open(); using var cmd = m.CreateCommand(); cmd.CommandText = $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]"; cmd.ExecuteNonQuery(); }
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    // HousingSupervisor has no forms/mobile permission of its own, so each test grants exactly what it needs.
    static ClaimsPrincipal Staff(string id, string sponsorId, params string[] permissions) => UserAccess.Principal(new()
    {
        ["id"] = id,
        ["username"] = id,
        ["role"] = "HousingSupervisor",
        ["customPermissions"] = new JsonArray(permissions.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
        ["financialScope"] = new JsonObject { ["sponsorIds"] = new JsonArray(sponsorId) }
    });

    static JsonObject Request(string id, string type, string iqama, string status, string createdAt, bool twoStep = false, bool confidential = false, string decidedAt = "") => new()
    {
        ["id"] = id,
        ["requestType"] = type,
        ["direction"] = "rider_to_admin",
        ["riderIqama"] = iqama,
        ["titleAr"] = "طلب تجريبي",
        ["status"] = status,
        ["requiresSecondApprover"] = twoStep,
        ["confidential"] = confidential,
        ["createdAt"] = createdAt,
        ["decidedAt"] = decidedAt
    };

    static void Seed(Store store, out string bwId, out string otherId)
    {
        string bw = FinancialCore.ResolveSponsor("BW").Id;
        string other = FinancialCore.Sponsors.First(s => s.Id != bw).Id;
        string now = DateTimeOffset.UtcNow.ToString("O");
        store.Write((c, tx) =>
        {
            Store.Save(c, "rider", new() { ["id"] = "2000000991", ["iqama"] = "2000000991", ["name"] = "مندوب تجريبي أ", ["status"] = "Active", ["financeSponsorId"] = bw }, tx);
            Store.Save(c, "rider", new() { ["id"] = "2000000992", ["iqama"] = "2000000992", ["name"] = "مندوب تجريبي ب", ["status"] = "Active", ["financeSponsorId"] = other }, tx);
            Store.Save(c, RiderAsks.ServiceKind, Request("sreq:leave-bw", "annual_leave", "2000000991", "open", "2026-11-10T08:00:00Z"), tx);
            Store.Save(c, RiderAsks.ServiceKind, Request("sreq:appt-bw", "appointment", "2000000991", "open", "2026-11-11T08:00:00Z"), tx);
            var iban = Request("sreq:iban-bw", "iban_update", "2000000991", "open", "2026-11-12T08:00:00Z", twoStep: true);
            iban["payload"] = new JsonObject { ["iban"] = "SA0380000000608010167519", ["ibanLast4"] = "7519" };
            Store.Save(c, RiderAsks.ServiceKind, iban, tx);
            Store.Save(c, RiderAsks.ServiceKind, Request("sreq:complaint-bw", "complaint", "2000000991", "open", "2026-11-13T08:00:00Z", confidential: true), tx);
            Store.Save(c, RiderAsks.ServiceKind, Request("sreq:done-bw", "annual_leave", "2000000991", "approved", "2026-11-01T08:00:00Z", decidedAt: now), tx);
            Store.Save(c, RiderAsks.ServiceKind, Request("sreq:leave-other", "annual_leave", "2000000992", "open", "2026-11-14T08:00:00Z"), tx);
            return true;
        });
        bwId = bw; otherId = other;
    }

    [TestMethod]
    public void StaffQueueKeepsCompanyScopeHidesConfidentialAndFiltersInSql()
    {
        WithStore(store =>
        {
            Seed(store, out string bw, out _);
            var reviewer = Staff("syn-forms-rev", bw, "forms.review", "mobile.broadcast");
            var rows = store.Read(c => RiderAsks.StaffServiceRequests(c, reviewer, null, null, null));
            CollectionAssert.AreEquivalent(new[] { "sreq:leave-bw", "sreq:appt-bw", "sreq:iban-bw", "sreq:done-bw" }, rows.Select(r => r.S("id")).ToArray(),
                "other company's request and the confidential complaint stay hidden");
            Assert.AreEqual("sreq:iban-bw", rows[0].S("id"), "newest first");
            Assert.AreEqual("••••7519", rows[0]["payload"]!.AsObject().S("iban"), "full new IBAN hidden without IBAN permission");
            var ibanViewer = Staff("syn-iban-view", bw, "forms.view", "riders.iban.view");
            Assert.AreEqual("SA0380000000608010167519",
                store.Read(c => RiderAsks.StaffServiceRequests(c, ibanViewer, "iban_update", null, null)).Single()["payload"]!.AsObject().S("iban"));
            // The stored request is unchanged (masking is on the read copy only).
            Assert.AreEqual("SA0380000000608010167519", store.Read(c => Store.Get(c, "sreq:iban-bw", RiderAsks.ServiceKind))!["payload"]!.AsObject().S("iban"));

            var leaves = store.Read(c => RiderAsks.StaffServiceRequests(c, reviewer, "annual_leave", "open", null));
            CollectionAssert.AreEqual(new[] { "sreq:leave-bw" }, leaves.Select(r => r.S("id")).ToArray());

            var confidential = Staff("syn-conf", bw, "forms.review", "mobile.complaints.confidential");
            Assert.IsTrue(store.Read(c => RiderAsks.StaffServiceRequests(c, confidential, "complaint", null, null)).Any(r => r.S("id") == "sreq:complaint-bw"));

            // Out-of-scope request: reply and decide answer 404, as before.
            Assert.AreEqual(404, Assert.ThrowsExactly<ApiError>(() =>
                store.Write((c, tx) => RiderAsks.StaffReplyServiceRequest(c, tx, reviewer, "sreq:leave-other", new() { ["message"] = "رد تجريبي" }))).Status);
            Assert.AreEqual(404, Assert.ThrowsExactly<ApiError>(() =>
                store.Write((c, tx) => RiderAsks.DecideServiceRequest(c, tx, reviewer, "sreq:leave-other", new() { ["action"] = "approve" }))).Status);
        });
    }

    [TestMethod]
    public void AllowedActionsFollowTheServerChecks()
    {
        WithStore(store =>
        {
            Seed(store, out string bw, out _);
            var reviewer = Staff("syn-forms-rev", bw, "forms.review", "mobile.broadcast");
            var rows = store.Read(c => RiderAsks.StaffServiceRequests(c, reviewer, null, null, null)).ToDictionary(r => r.S("id"));
            string[] Actions(string id) => rows[id]["allowedActions"]!.AsArray().Select(a => a!.ToString()).ToArray();
            CollectionAssert.AreEquivalent(new[] { "approve", "reject", "need_info", "reply" }, Actions("sreq:leave-bw"));
            CollectionAssert.AreEquivalent(new[] { "reply" }, Actions("sreq:iban-bw"), "IBAN change needs the dedicated first/second approver permissions");
            CollectionAssert.AreEquivalent(Array.Empty<string>(), Actions("sreq:done-bw"), "approved request has no buttons");

            var first = Staff("syn-first", bw, "forms.review", RiderAsks.IbanDataApproveFirstPermission);
            var iban = store.Read(c => RiderAsks.StaffServiceRequests(c, first, "iban_update", null, null)).Single();
            CollectionAssert.AreEquivalent(new[] { "first_approve", "reject", "need_info", "reply" }, iban["allowedActions"]!.AsArray().Select(a => a!.ToString()).ToArray());

            var viewer = Staff("syn-view", bw, "forms.view");
            var viewRows = store.Read(c => RiderAsks.StaffServiceRequests(c, viewer, null, null, null));
            Assert.IsTrue(viewRows.All(r => r["allowedActions"]!.AsArray().Count == 0), "view-only user gets no buttons");
        });
    }

    [TestMethod]
    public void SummaryCountsTheWholeScopeNotOnePage()
    {
        WithStore(store =>
        {
            Seed(store, out string bw, out _);
            var reviewer = Staff("syn-forms-rev", bw, "forms.review", "mobile.broadcast");
            var s = store.Read(c => RiderAsks.StaffServiceSummary(c, reviewer));
            Assert.AreEqual(3, (int)s.N("open"));
            Assert.AreEqual(1, (int)s.N("annualLeaveOpen"));
            Assert.AreEqual(1, (int)s.N("appointmentsOpen"));
            Assert.AreEqual(1, (int)s.N("doneThisMonth"));
            Assert.AreEqual(4, (int)s.N("total"));
        });
    }

    [TestMethod]
    public void RiderAppAdminPathsUseTheSamePermissionsAsTheSidebar()
    {
        string bw = FinancialCore.ResolveSponsor("BW").Id;
        var forms = Staff("syn-f", bw, "forms.view");
        var broadcaster = Staff("syn-b", bw, "mobile.broadcast");
        var sessions = Staff("syn-s", bw, "mobile.sessions", "inquiry.issue");
        var ibanApprover = Staff("syn-ia", bw, RiderAsks.IbanDataApproveFirstPermission);
        Assert.IsTrue(AccessPolicy.Allowed(forms, "GET", "/api/rider-mobile/service-requests"));
        Assert.IsTrue(AccessPolicy.Allowed(forms, "GET", "/api/rider-mobile/service-requests/summary"));
        Assert.IsFalse(AccessPolicy.Allowed(forms, "POST", "/api/rider-mobile/service-requests/sreq:x/decide"));
        Assert.IsTrue(AccessPolicy.Allowed(forms, "GET", "/api/forms/builder"), "builder list is forms.view");
        Assert.IsFalse(AccessPolicy.Allowed(forms, "POST", "/api/forms/builder"), "builder writes stay forms.manage");
        Assert.IsTrue(AccessPolicy.Allowed(forms, "POST", "/api/excel/export"));
        Assert.IsTrue(AccessPolicy.Allowed(broadcaster, "GET", "/api/rider-mobile/broadcasts"));
        Assert.IsTrue(AccessPolicy.Allowed(broadcaster, "POST", "/api/rider-mobile/broadcast"));
        Assert.IsTrue(AccessPolicy.Allowed(broadcaster, "POST", "/api/rider-mobile/statements/notify-period"));
        Assert.IsTrue(AccessPolicy.Allowed(broadcaster, "GET", "/api/finance/statement/rider/2000000991/monthly"));
        Assert.IsTrue(AccessPolicy.CanAccessRiderMonthlyStatement(broadcaster));
        Assert.IsTrue(AccessPolicy.Allowed(broadcaster, "POST", "/api/excel/export"));
        Assert.IsTrue(RiderLookup.Allowed(broadcaster));
        Assert.IsTrue(RiderLookup.Allowed(sessions));
        Assert.IsTrue(AccessPolicy.Allowed(ibanApprover, "GET", "/api/rider-mobile/service-requests"));
        Assert.IsFalse(AccessPolicy.Allowed(broadcaster, "GET", "/api/rider-mobile/firebase-config"), "settings stay Admin only");
        Assert.IsFalse(AccessPolicy.Allowed(forms, "POST", "/api/inquiry-access"));
        Assert.IsTrue(AccessPolicy.Allowed(sessions, "POST", "/api/inquiry-access"));
    }

    [TestMethod]
    public void BroadcastOnlyMayReadApprovedMonthlyPreviewButNotDraft()
    {
        string bw = FinancialCore.ResolveSponsor("BW").Id;
        var broadcaster = Staff("syn-b-stmt", bw, "mobile.broadcast");
        AccessPolicy.DemandRiderMonthlyStatement(broadcaster, approvedOnly: true);
        Assert.AreEqual(403, Assert.ThrowsExactly<ApiError>(() =>
            AccessPolicy.DemandRiderMonthlyStatement(broadcaster, approvedOnly: false)).Status);
        var finance = Staff("syn-fin-stmt", bw, "pay.view", "payroll.view", "debts.view");
        AccessPolicy.DemandRiderMonthlyStatement(finance, approvedOnly: false);
    }

    [TestMethod]
    public void ReplyMasksIbanAndRejectsClosedRequestsAndDecideIsTypeScoped()
    {
        WithStore(store =>
        {
            Seed(store, out string bw, out _);
            var reviewer = Staff("syn-forms-rev", bw, "forms.review", "mobile.broadcast");
            var replied = store.Write((c, tx) => RiderAsks.StaffReplyServiceRequest(c, tx, reviewer, "sreq:iban-bw", new() { ["message"] = "نحتاج صورة الآيبان" }));
            Assert.AreEqual("••••7519", replied["payload"]!.AsObject().S("iban"), "reply must not leak full IBAN without IBAN permission");
            Assert.AreEqual("SA0380000000608010167519", store.Read(c => Store.Get(c, "sreq:iban-bw", RiderAsks.ServiceKind))!["payload"]!.AsObject().S("iban"));

            Assert.AreEqual(409, Assert.ThrowsExactly<ApiError>(() =>
                store.Write((c, tx) => RiderAsks.StaffReplyServiceRequest(c, tx, reviewer, "sreq:done-bw", new() { ["message"] = "رد متأخر" }))).Status);

            var confidentialOnly = Staff("syn-conf-only", bw, "mobile.complaints.confidential");
            Assert.AreEqual(403, Assert.ThrowsExactly<ApiError>(() =>
                store.Write((c, tx) => RiderAsks.DecideServiceRequest(c, tx, confidentialOnly, "sreq:leave-bw", new() { ["action"] = "approve" }))).Status,
                "confidential alone must not approve annual leave");
            Assert.IsFalse(RiderAsks.CanDecideType(confidentialOnly, new() { ["requestType"] = "annual_leave" }));
            Assert.IsTrue(RiderAsks.CanDecideType(reviewer, new() { ["requestType"] = "annual_leave" }));
        });
    }

    [TestMethod]
    public void ExportBankSplitsSaudiIbanOnly()
    {
        Assert.AreEqual(("RJHI", "مصرف الراجحي"), BankPayrollFiles.ExportBank("SA03 8000 0000 6080 1016 7519"));
        Assert.AreEqual(("", ""), BankPayrollFiles.ExportBank(""));
        Assert.AreEqual(("", ""), BankPayrollFiles.ExportBank("GB00X"));
    }
}
