using System.Security.Claims;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class RegisterOpsLiveQueryTests
{
    static LiveOperationsQuery.Scope All => new(true, [], [], [], []);

    static ClaimsPrincipal Principal(string username, string[] perms, string[]? platforms = null)
    {
        var body = new JsonObject
        {
            ["username"] = username,
            ["role"] = "Reviewer",
            ["customPermissions"] = new JsonArray(perms.Select(p => (JsonNode?)p).ToArray()),
            ["financialScope"] = new JsonObject
            {
                ["sponsorIds"] = new JsonArray(FinancialCore.Sponsors.Select(s => (JsonNode?)s.Id).ToArray()),
                ["platforms"] = new JsonArray((platforms ?? ["Ninja", "Keeta"]).Select(p => (JsonNode?)p).ToArray()),
                ["accountCodes"] = new JsonArray(),
                ["categoryCodes"] = new JsonArray()
            }
        };
        return UserAccess.Principal(body);
    }

    [TestMethod]
    public void HeadersMatchKeetaOperationsImportAtoAH()
    {
        CollectionAssert.AreEqual(KeetaOperationsImport.Headers, RegisterOpsLiveQuery.Headers);
        Assert.AreEqual(34, RegisterOpsLiveQuery.Headers.Length);
    }

    [TestMethod]
    public void PreviewSql_IsReadOnlyScopedSelect_KeetaAndNinja()
    {
        var (keetaSql, keetaNames) = RegisterOpsLiveQuery.PreviewSql("keeta", All);
        StringAssert.Contains(keetaSql, "[keeta].[vw_keeta_daily_performance_v1]");
        StringAssert.Contains(keetaSql, "[keeta].[vw_keeta_eligibility_v1]");
        StringAssert.Contains(keetaSql, "[keeta].[KeetaAccountDetails]");
        StringAssert.Contains(keetaSql, "[app].[PlatformAccountBusinessDetails]");
        Assert.IsFalse(keetaSql.Contains(';', StringComparison.Ordinal));
        Assert.DoesNotContain("INSERT ", keetaSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", keetaSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", keetaSql, StringComparison.OrdinalIgnoreCase);
        CollectionAssert.Contains(keetaNames.ToList(), "@from");
        CollectionAssert.Contains(keetaNames.ToList(), "@registerCr");

        var (ninjaSql, ninjaNames) = RegisterOpsLiveQuery.PreviewSql("ninja", All);
        StringAssert.Contains(ninjaSql, "[ninja].[NinjaDailyPerformance]");
        StringAssert.Contains(ninjaSql, "cp.[Code]=@ninja");
        Assert.IsFalse(ninjaSql.Contains(';', StringComparison.Ordinal));
        CollectionAssert.Contains(ninjaNames.ToList(), "@to");

        var blocked = new LiveOperationsQuery.Scope(false, [], [], [], []);
        var (blockedSql, _) = RegisterOpsLiveQuery.PreviewSql("ninja", blocked);
        StringAssert.Contains(blockedSql, "1=0");
    }

    [TestMethod]
    public void Query_RequiresSqlServer()
    {
        using var sqlite = new SqliteConnection("Data Source=:memory:");
        sqlite.Open();
        var req = new RegisterOpsLiveQuery.Request("ninja", "BW", "7000000001", new(2026, 10, 1), new(2026, 10, 31), 1, 50);
        var fail = Assert.ThrowsExactly<ImportFailure>(() => RegisterOpsLiveQuery.Query(sqlite, All, req));
        Assert.AreEqual("live_ops_requires_sql", fail.Code);
    }

    [TestMethod]
    public void AccessPolicy_AllowsRegisterOpsGet()
    {
        Assert.IsTrue(AccessPolicy.Allowed(Principal("syn-reg", ["pay.view"]), "GET", "/api/pay/apps/keeta/live/register-ops"));
        Assert.IsTrue(AccessPolicy.Allowed(Principal("syn-reg", ["pay.view"]), "GET", "/api/pay/apps/ninja/live/register-ops"));
        Assert.IsFalse(AccessPolicy.Allowed(Principal("syn-reg", ["operations.view"]), "GET", "/api/pay/apps/ninja/live/register-ops"));
    }

    [TestMethod]
    public void High_KeetaSql_DoesNotCopyNullRiderAccountRowsOntoEveryRider()
    {
        var (keetaSql, _) = RegisterOpsLiveQuery.PreviewSql("keeta", All);
        // Match assignment or that rider only; never OR (AssignmentId IS NULL AND RiderProfileId IS NULL).
        Assert.IsFalse(keetaSql.Contains("p.AssignmentId IS NULL AND p.RiderProfileId IS NULL", StringComparison.Ordinal));
        Assert.IsFalse(keetaSql.Contains("v.RiderProfileId IS NULL", StringComparison.Ordinal));
        Assert.IsFalse(keetaSql.Contains("e.RiderProfileId IS NULL)", StringComparison.Ordinal));
        StringAssert.Contains(keetaSql, "p.AssignmentId IS NULL AND p.RiderProfileId=a.RiderProfileId");
        StringAssert.Contains(keetaSql, "v.RiderProfileId=a.RiderProfileId");
        // Null-assignment facts are cut to this assignment's EffectiveFrom/To window.
        StringAssert.Contains(keetaSql, "p.BusinessDate BETWEEN");
        // Account VDA: exactly one covering assignment per day (TOP 1 owner), not every perf match.
        StringAssert.Contains(keetaSql, "SELECT TOP 1 aPick.AssignmentId");
        StringAssert.Contains(keetaSql, "p.AssignmentId=aPick.AssignmentId) THEN 0");
        StringAssert.Contains(keetaSql, "p.AssignmentId IS NULL AND p.RiderProfileId=aPick.RiderProfileId) THEN 1");
        StringAssert.Contains(keetaSql, "ELSE 2");
        // Orphaned perf (null rider / outside assigns) must not gate ownership on "no perf at all".
        Assert.IsFalse(keetaSql.Contains("FROM perf p2", StringComparison.Ordinal));
        Assert.IsFalse(keetaSql.Contains("a2.AssignmentId<>a.AssignmentId", StringComparison.Ordinal));
    }

    [TestMethod]
    public void High_KeetaVda_AttributedToExactlyOneCoveringAssignmentPerDay()
    {
        // grok-ERPV1c #2 HIGH PARTLY + N2 PARTLY + new N1: handover must not double-count;
        // orphaned perf days must still land on one covering assignment.
        var (keetaSql, _) = RegisterOpsLiveQuery.PreviewSql("keeta", All);
        StringAssert.Contains(keetaSql, "SELECT TOP 1 aPick.AssignmentId");
        StringAssert.Contains(keetaSql, "ORDER BY");
        StringAssert.Contains(keetaSql, "p.AssignmentId=aPick.AssignmentId) THEN 0");
        StringAssert.Contains(keetaSql, "p.AssignmentId IS NULL AND p.RiderProfileId=aPick.RiderProfileId) THEN 1");
        StringAssert.Contains(keetaSql, "ELSE 2");
        StringAssert.Contains(keetaSql, "aPick.EffectiveFrom");
        Assert.IsFalse(keetaSql.Contains("FROM perf p2", StringComparison.Ordinal));
        Assert.IsFalse(keetaSql.Contains("a2.AssignmentId<>a.AssignmentId", StringComparison.Ordinal));
        // Both DistancePaid and ValidDays use the same single-owner predicate.
        int ownerHits = 0;
        int idx = 0;
        while ((idx = keetaSql.IndexOf("SELECT TOP 1 aPick.AssignmentId", idx, StringComparison.Ordinal)) >= 0)
        {
            ownerHits++;
            idx += 1;
        }
        Assert.AreEqual(2, ownerHits, "DistancePaid and ValidDays must each pick one VDA-day owner");
        StringAssert.Contains(keetaSql, "AS DistancePaid");
        StringAssert.Contains(keetaSql, "AS ValidDays");
    }

    [TestMethod]
    public void High_KeetaEligibility_UsesViewColumnsNotResultLabelAr()
    {
        var (keetaSql, _) = RegisterOpsLiveQuery.PreviewSql("keeta", All);
        Assert.IsFalse(keetaSql.Contains("ResultLabelAr", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(keetaSql, "ExpectedExperienceTier");
        StringAssert.Contains(keetaSql, "ExpectedCapacityTier");
        StringAssert.Contains(keetaSql, "e.[Status]");
        StringAssert.Contains(keetaSql, "COALESCE(NULLIF(e.[ExpectedExperienceTier],N''), NULLIF(e.[ExpectedCapacityTier],N''), NULLIF(e.[Status],N''))");
    }

    [TestMethod]
    public void High_Ninja_DropsUnusedEvaluationSnapshotsAndDoesNotLabelOrderDaysAsValidDays()
    {
        Assert.IsFalse(RegisterOpsLiveQuery.ErpSources["ninja"].Any(s =>
            s.Contains("EvaluationSnapshots", StringComparison.OrdinalIgnoreCase)));
        var (ninjaSql, _) = RegisterOpsLiveQuery.PreviewSql("ninja", All);
        Assert.IsFalse(ninjaSql.Contains("EvaluationSnapshots", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(ninjaSql, "CAST(0 AS bigint) AS ValidDays");
        StringAssert.Contains(ninjaSql, "CAST(N'' AS nvarchar(200)) AS ExpectedEligibility");
        // Work days still come from performance; valid days stay a separate (empty) fact.
        StringAssert.Contains(ninjaSql, "AS WorkDays");
        // User-day total uses work days (ValidDays is forced 0).
        StringAssert.Contains(ninjaSql, "WorkDays AS UserDays");
    }

    [TestMethod]
    public void High_MaskIbanUnlessPermitted_EmptiesIbanWithoutPermission()
    {
        var withIban = new JsonObject { ["الايبان 1"] = "SA0380000000608010167519", ["رقم التواصل"] = "0500000000" };
        var kept = new JsonObject { ["الايبان 1"] = "SA0080000000608010167519" };
        RegisterOpsLiveQuery.MaskIbanUnlessPermitted([withIban], canViewIban: false);
        Assert.AreEqual("", withIban.S("الايبان 1"));
        Assert.AreEqual("0500000000", withIban.S("رقم التواصل"));
        RegisterOpsLiveQuery.MaskIbanUnlessPermitted([kept], canViewIban: true);
        Assert.AreEqual("SA0080000000608010167519", kept.S("الايبان 1"));

        Assert.IsTrue(AccessPolicy.Has(Principal("syn-iban", ["pay.view", WorkforceApi.PermIban]), WorkforceApi.PermIban));
        Assert.IsFalse(AccessPolicy.Has(Principal("syn-no-iban", ["pay.view"]), WorkforceApi.PermIban));
    }

    [TestMethod]
    public void Medium_KeetaEligibility_RanksNewestPerAccountAndRider()
    {
        var (keetaSql, _) = RegisterOpsLiveQuery.PreviewSql("keeta", All);
        StringAssert.Contains(keetaSql, "ROW_NUMBER() OVER");
        StringAssert.Contains(keetaSql, "ORDER BY e.[CreatedAtUtc] DESC, e.[SnapshotId] DESC");
        StringAssert.Contains(keetaSql, "e.RiderProfileId=a.RiderProfileId AND e.rn=1");
        StringAssert.Contains(keetaSql, "e.[RiderProfileId] IS NOT NULL");
        Assert.IsFalse(keetaSql.Contains("ResultLabelAr", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Medium_SubstituteType_ComesFromRelationshipToAssignedRider()
    {
        var (keetaSql, _) = RegisterOpsLiveQuery.PreviewSql("keeta", All);
        StringAssert.Contains(keetaSql, "RelationshipToAssignedRider");
        StringAssert.Contains(keetaSql, "AS SubstituteType");
        var (ninjaSql, _) = RegisterOpsLiveQuery.PreviewSql("ninja", All);
        StringAssert.Contains(ninjaSql, "RelationshipToAssignedRider");
        StringAssert.Contains(ninjaSql, "AS SubstituteType");
        // Projection reads ERP relationship, not a hard-coded «بديل».
        Assert.IsFalse(keetaSql.Contains("N'بديل'", StringComparison.Ordinal));
        Assert.IsFalse(ninjaSql.Contains("N'بديل'", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Medium_NinjaUserDays_UsesWorkDaysNotForcedZeroValidDays()
    {
        var (ninjaSql, _) = RegisterOpsLiveQuery.PreviewSql("ninja", All);
        StringAssert.Contains(ninjaSql, "WorkDays AS UserDays");
        StringAssert.Contains(ninjaSql, "CAST(0 AS bigint) AS ValidDays");
        var (keetaSql, _) = RegisterOpsLiveQuery.PreviewSql("keeta", All);
        StringAssert.Contains(keetaSql, "ValidDays AS UserDays");
    }

    [TestMethod]
    public void Medium_PlatformOpsCapture_StaysPlatformDayUsageNotRegisterOpsMonthStamp()
    {
        Assert.AreEqual("platform_day_usage", LiveOpsLiveFactsReader.MapCaptureKind("platform_ops"));
        CollectionAssert.AreEqual(
            new[] { "id", "erpPlatformAccountId", "orders", "workDate" },
            OpsSnapshotCapture.UsedFields["platform_ops"]);
        Assert.IsFalse(OpsSnapshotCapture.UsedFields["platform_ops"].Contains("expectedEligibility"));
    }

    [TestMethod]
    public void Medium_VehicleColumnsComeFromBusinessDetailsAndFuelAssignment()
    {
        var (keetaSql, _) = RegisterOpsLiveQuery.PreviewSql("keeta", All);
        StringAssert.Contains(keetaSql, "[app].[PlatformAccountBusinessDetails]");
        StringAssert.Contains(keetaSql, "RegisteredVehiclePlate");
        StringAssert.Contains(keetaSql, "RegisteredVehicleType");
        StringAssert.Contains(keetaSql, "[app].[FuelCards]");
        StringAssert.Contains(keetaSql, "[app].[RealRiders]");
        StringAssert.Contains(keetaSql, "RelationshipToAssignedRider");
        var (ninjaSql, _) = RegisterOpsLiveQuery.PreviewSql("ninja", All);
        StringAssert.Contains(ninjaSql, "[app].[PlatformAccountBusinessDetails]");
        StringAssert.Contains(ninjaSql, "FuelCardNumber");
        StringAssert.Contains(ninjaSql, "RelationshipToAssignedRider");
    }

    [TestMethod]
    public void DisplayProjection_KeepsExpectedOnlyAndDismissedFlag()
    {
        var display = new JsonObject
        {
            ["assignmentId"] = "syn-assign-1",
            ["platformRiderAccountId"] = "syn-acct-1",
            ["riderProfileId"] = "syn-rider-1",
            ["الطلبات"] = 12,
            ["الأيام الصالحة"] = 18,
            ["الصلاحية المتوقعة"] = "شريحة تجريبية متوقعة",
            ["كفالة اليوزر"] = "سجل تجريبي",
            ["dismissed"] = true,
            ["expectedApproval"] = "expected_only"
        };
        Assert.AreEqual("expected_only", display.S("expectedApproval"));
        Assert.IsTrue(display.B("dismissed"));
        Assert.AreEqual(12, display.N("الطلبات"));
        Assert.AreEqual("platform_day_usage", LiveOpsLiveFactsReader.MapCaptureKind("platform_ops"));
    }
}
