using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace ErpImport;

/// <summary>
/// Live read-only register-sheet ops display (A..AH Arabic headers) from Omar's ERP.
/// One row per rider × platform account for a chosen register/period. Expected eligibility only.
/// No writes. Facts only — no pay calculation.
/// </summary>
public static class RegisterOpsLiveQuery
{
    public const int CommandTimeoutSeconds = 45;
    public const int MaxPageSize = LiveOperationsQuery.MaxPageSize;
    public const int ShortCacheSeconds = 60;

    /// <summary>Same A..AH order as Keeta operations import register sheets.</summary>
    public static readonly string[] Headers =
    [
        "المعرف","الاسم بالكامل","رقم بطاقة الهوية","كفالة اليوزر","المركبة","الحالة",
        "رقم الهوية1","البديل 1","رقم التواصل","نوع البديل","نوع المركبة","رقم كرت البنزين1",
        "الأدوات1","الجنسية 1","الرقم المسجل","الايبان 1","تاريخ الاستلام1","تاريخ التسليم",
        "الأيام الصالحة","الطلبات","المخالفات","المسافة المدفوعة","عدد ايام عمل المندوب",
        "الصلاحية المتوقعة","اجمالي المسافه","مطابقة المسافة","الملاحظات",
        "اجمالي طلبات المعادلة","اجمالي طلبات اليوزر","مطابقة الطلبات","عدد البدلاء",
        "اجمالي ايام المعادلة","اجمالي ايام اليوزر","مطابقة الايام"
    ];

    public static readonly IReadOnlyDictionary<string, string[]> ErpSources = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["keeta"] =
        [
            "keeta.vw_keeta_daily_performance_v1",
            "keeta.vw_keeta_vda_daily_v1",
            "keeta.vw_keeta_eligibility_v1",
            "keeta.vw_keeta_violations_v1",
            "keeta.KeetaAccountDetails",
            "app.PlatformAccountBusinessDetails",
            "app.FuelCards",
            "app.FuelCardRiderAssignments",
            "app.RiderVehicleAssignments",
            "app.RealRiders",
            "app.PlatformRiderAccounts",
            "app.RiderClientAssignments",
            "app.Sponsors",
            "app.Employees",
            "app.RiderProfiles",
            "platform.ClientPlatforms"
        ],
        ["ninja"] =
        [
            // EvaluationSnapshots is batch JSON (not register/rider grain) — omit until mapped.
            "ninja.NinjaDailyPerformance",
            "app.PlatformAccountBusinessDetails",
            "app.FuelCards",
            "app.FuelCardRiderAssignments",
            "app.RiderVehicleAssignments",
            "app.RealRiders",
            "app.PlatformRiderAccounts",
            "app.RiderClientAssignments",
            "app.Sponsors",
            "app.Employees",
            "app.RiderProfiles",
            "platform.ClientPlatforms"
        ]
    };

    public sealed record Request(string App, string RegisterCode, string RegisterUnifiedNumber, DateOnly From, DateOnly To, int Page, int PageSize);

    public sealed record PageResult(
        IReadOnlyList<JsonObject> DisplayRows,
        long Total,
        JsonObject Coverage,
        JsonObject Counts,
        DateTimeOffset AsOf,
        string Register,
        string RegisterSheet,
        IReadOnlyList<string> Columns,
        IReadOnlyList<string> Sources);

    public static (string Sql, IReadOnlyList<string> ParameterNames) PreviewSql(string app, LiveOperationsQuery.Scope? scope = null)
    {
        using var cmd = new SqlCommand();
        string sql = app.Equals("ninja", StringComparison.OrdinalIgnoreCase)
            ? BuildNinjaSql(cmd, scope, new Request("ninja", "BW", "7000000001", new(2026, 10, 1), new(2026, 10, 31), 1, 50))
            : BuildKeetaSql(cmd, scope, new Request("keeta", "BW", "7000000001", new(2026, 11, 1), new(2026, 11, 27), 1, 50));
        return (sql, cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).ToList());
    }

    public static PageResult Query(
        DbConnection source,
        LiveOperationsQuery.Scope scope,
        Request request,
        CancellationToken cancellationToken = default)
    {
        if (source is not SqlConnection) throw new ImportFailure("live_ops_requires_sql");
        string app = request.App.Trim().ToLowerInvariant();
        if (app is not ("keeta" or "ninja")) throw new ImportFailure("unsupported_app");
        DateTimeOffset asOf = DateTimeOffset.UtcNow;
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, cancellationToken);
        cmd.CommandText = app == "ninja" ? BuildNinjaSql(cmd, scope, request) : BuildKeetaSql(cmd, scope, request);

        var items = new List<JsonObject>();
        long total = 0;
        long dismissed = 0;
        long duplicateIdRows = 0;
        long uniqueIds = 0;
        var missingDays = new JsonArray();
        string? firstDay = null, lastDay = null;
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (total == 0)
                {
                    total = Num(reader, "TotalCount");
                    dismissed = Num(reader, "DismissedCount");
                    duplicateIdRows = Num(reader, "DuplicateIdRows");
                    uniqueIds = Num(reader, "UniqueIds");
                    firstDay = DateStr(reader, "CoverageFirstDay");
                    lastDay = DateStr(reader, "CoverageLastDay");
                    missingDays = ParseMissingDays(Str(reader, "MissingDaysCsv"));
                }
                items.Add(MapRow(reader));
            }
        }

        string sheet = app == "ninja"
            ? request.RegisterCode switch { "EX" => "Express", "BW" => "Albwaba", "TR" => "Togary", _ => request.RegisterCode }
            : request.RegisterCode switch { "EX" => "Express", "BW" => "Albwaba", "TR" => "Togary", _ => request.RegisterCode };

        return new(
            items,
            total,
            new JsonObject
            {
                ["requestedFrom"] = request.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["requestedTo"] = request.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["firstDay"] = firstDay ?? "",
                ["lastDay"] = lastDay ?? "",
                ["missingDays"] = missingDays,
                ["missingDayCount"] = missingDays.Count,
                ["hasGapAtPeriodEnd"] = !string.IsNullOrEmpty(lastDay) &&
                    string.CompareOrdinal(lastDay, request.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) < 0
            },
            new JsonObject
            {
                ["rows"] = total,
                ["dismissed"] = dismissed,
                ["duplicateIdRows"] = duplicateIdRows,
                ["uniqueIds"] = uniqueIds
            },
            asOf,
            request.RegisterCode,
            sheet,
            Headers,
            ErpSources[app]);
    }

    static string BuildKeetaSql(DbCommand cmd, LiveOperationsQuery.Scope? scope, Request request)
    {
        Add(cmd, "@from", request.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@to", request.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@month", new DateTime(request.From.Year, request.From.Month, 1));
        Add(cmd, "@registerCr", request.RegisterUnifiedNumber);
        Add(cmd, "@keeta", "KEETA");
        Add(cmd, "@skip", (request.Page - 1) * request.PageSize);
        Add(cmd, "@take", request.PageSize);
        string sponsorScope = ScopeSponsor(scope, cmd, "s");
        string platformScope = ScopePlatform(scope, cmd, "cp");
        return $"""
            WITH calendar AS (
                SELECT CAST(@from AS date) AS d
                UNION ALL
                SELECT DATEADD(day, 1, d) FROM calendar WHERE d < @to
            ),
            scoped_accounts AS (
                SELECT pa.[Id] AS PlatformRiderAccountId, pa.[ExternalAccountId], pa.[Code] AS AccountCode,
                       pa.[RegisteredEmployeeId], pa.[SponsorId],
                       s.[UnifiedNationalNumber] AS RegisterUnifiedNumber,
                       s.[RegistryNameAr] AS RegisterNameAr,
                       owner_e.[IqamaNo] AS OwnerIqama, owner_e.[FullNameAr] AS OwnerNameAr,
                       owner_s.[RegistryNameAr] AS OwnerRegisterNameAr,
                       kd.[DismissedOn],
                       ISNULL(bd.[RegisteredVehiclePlate], N'') AS VehiclePlate,
                       ISNULL(bd.[RegisteredVehicleType], N'') AS VehicleTypeRaw,
                       ISNULL(bd.[RegistryIdentifier], N'') AS RegistryIdentifier
                FROM [app].[PlatformRiderAccounts] pa
                INNER JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=pa.[ClientPlatformId] AND cp.[IsDeleted]=0 AND cp.[Code]=@keeta
                INNER JOIN [app].[Sponsors] s ON s.[Id]=pa.[SponsorId] AND s.[IsDeleted]=0
                LEFT JOIN [app].[Employees] owner_e ON owner_e.[Id]=pa.[RegisteredEmployeeId] AND owner_e.[IsDeleted]=0
                LEFT JOIN [app].[Sponsors] owner_s ON owner_s.[Id]=owner_e.[SponsorId] AND owner_s.[IsDeleted]=0
                LEFT JOIN [keeta].[KeetaAccountDetails] kd ON kd.[PlatformRiderAccountId]=pa.[Id] AND kd.[IsDeleted]=0
                LEFT JOIN [app].[PlatformAccountBusinessDetails] bd ON bd.[PlatformRiderAccountId]=pa.[Id] AND bd.[IsDeleted]=0
                WHERE pa.[IsDeleted]=0 AND s.[UnifiedNationalNumber]=@registerCr{sponsorScope}{platformScope}
            ),
            assigns AS (
                SELECT a.[Id] AS AssignmentId, a.[PlatformRiderAccountId], a.[RiderProfileId],
                       a.[EffectiveFrom], a.[EffectiveTo], a.[Status] AS AssignmentStatus,
                       sa.[ExternalAccountId], sa.[AccountCode], sa.[OwnerIqama], sa.[OwnerNameAr],
                       sa.[OwnerRegisterNameAr], sa.[RegisterNameAr], sa.[DismissedOn],
                       sa.[VehiclePlate], sa.[VehicleTypeRaw], sa.[RegistryIdentifier],
                       rider_e.[IqamaNo] AS RiderIqama, rider_e.[FullNameAr] AS RiderNameAr,
                       rider_e.[Nationality], rider_e.[PrimaryPhone], rider_e.[Iban],
                       (SELECT TOP 1 fc.[CardNumber]
                        FROM [app].[FuelCardRiderAssignments] fca
                        INNER JOIN [app].[FuelCards] fc ON fc.[Id]=fca.[FuelCardId] AND fc.[IsDeleted]=0
                        WHERE fca.[RiderProfileId]=a.[RiderProfileId] AND fca.[IsDeleted]=0
                          AND fca.[EffectiveFrom]<=@to
                          AND (fca.[EffectiveTo] IS NULL OR fca.[EffectiveTo]>=@from)
                        ORDER BY fca.[EffectiveFrom] DESC) AS FuelCardNumber,
                       (SELECT TOP 1 rr.[Name]
                        FROM [app].[RiderVehicleAssignments] va
                        INNER JOIN [app].[RealRiders] rr ON rr.[RiderVehicleAssignmentId]=va.[Id]
                        WHERE va.[RiderProfileId]=a.[RiderProfileId] AND va.[IsDeleted]=0 AND va.[IsRealRider]=0
                          AND CAST(va.[StartedAtUtc] AS date)<=@to
                          AND (va.[EndedAtUtc] IS NULL OR CAST(va.[EndedAtUtc] AS date)>=@from)
                        ORDER BY va.[StartedAtUtc] DESC) AS SubstituteName,
                       (SELECT TOP 1 ISNULL(rr.[RelationshipToAssignedRider], N'')
                        FROM [app].[RiderVehicleAssignments] va
                        INNER JOIN [app].[RealRiders] rr ON rr.[RiderVehicleAssignmentId]=va.[Id]
                        WHERE va.[RiderProfileId]=a.[RiderProfileId] AND va.[IsDeleted]=0 AND va.[IsRealRider]=0
                          AND CAST(va.[StartedAtUtc] AS date)<=@to
                          AND (va.[EndedAtUtc] IS NULL OR CAST(va.[EndedAtUtc] AS date)>=@from)
                        ORDER BY va.[StartedAtUtc] DESC) AS SubstituteType
                FROM [app].[RiderClientAssignments] a
                INNER JOIN scoped_accounts sa ON sa.[PlatformRiderAccountId]=a.[PlatformRiderAccountId]
                INNER JOIN [app].[RiderProfiles] rp ON rp.[Id]=a.[RiderProfileId] AND rp.[IsDeleted]=0
                INNER JOIN [app].[Employees] rider_e ON rider_e.[Id]=rp.[EmployeeId] AND rider_e.[IsDeleted]=0
                WHERE a.[IsDeleted]=0
                  AND a.[EffectiveFrom]<=@to
                  AND (a.[EffectiveTo] IS NULL OR a.[EffectiveTo]>=@from)
            ),
            perf AS (
                SELECT p.[PlatformRiderAccountId], p.[AssignmentId], p.[RiderProfileId], p.[BusinessDate],
                       ISNULL(p.[DeliveredTasks],0) AS DeliveredTasks,
                       ISNULL(p.[ValidOnlineMinutes],0) AS ValidOnlineMinutes
                FROM [keeta].[vw_keeta_daily_performance_v1] p
                INNER JOIN scoped_accounts sa ON sa.[PlatformRiderAccountId]=p.[PlatformRiderAccountId]
                WHERE p.[BusinessDate] BETWEEN @from AND @to
            ),
            vda AS (
                SELECT v.[PlatformRiderAccountId], v.[BusinessDate],
                       ISNULL(v.[PayableDistanceKm],0) AS PayableDistanceKm,
                       CASE WHEN v.[Vda]=1 THEN 1 ELSE 0 END AS ValidDay
                FROM [keeta].[vw_keeta_vda_daily_v1] v
                INNER JOIN scoped_accounts sa ON sa.[PlatformRiderAccountId]=v.[PlatformRiderAccountId]
                WHERE v.[BusinessDate] BETWEEN @from AND @to
            ),
            viol AS (
                SELECT v.[PlatformRiderAccountId], v.[RiderProfileId], COUNT_BIG(*) AS Violations
                FROM [keeta].[vw_keeta_violations_v1] v
                INNER JOIN scoped_accounts sa ON sa.[PlatformRiderAccountId]=v.[PlatformRiderAccountId]
                WHERE v.[BusinessDate] BETWEEN @from AND @to
                  AND v.[RiderProfileId] IS NOT NULL
                GROUP BY v.[PlatformRiderAccountId], v.[RiderProfileId]
            ),
            elig AS (
                SELECT e.[PlatformRiderAccountId], e.[RiderProfileId],
                       COALESCE(NULLIF(e.[ExpectedExperienceTier],N''), NULLIF(e.[ExpectedCapacityTier],N''), NULLIF(e.[Status],N'')) AS ExpectedLabel,
                       e.[ValidDays] AS EligValidDays, e.[OrdersDelivered] AS EligOrders,
                       ROW_NUMBER() OVER (
                         PARTITION BY e.[PlatformRiderAccountId], e.[RiderProfileId]
                         ORDER BY e.[CreatedAtUtc] DESC, e.[SnapshotId] DESC
                       ) AS rn
                FROM [keeta].[vw_keeta_eligibility_v1] e
                INNER JOIN scoped_accounts sa ON sa.[PlatformRiderAccountId]=e.[PlatformRiderAccountId]
                WHERE e.[Month]=@month AND e.[RiderProfileId] IS NOT NULL
            ),
            agg AS (
                SELECT a.*,
                       -- Match assignment, or rider-only null-assignment cut to this assignment's dates. Never copy null-rider rows.
                       ISNULL((SELECT SUM(p.DeliveredTasks) FROM perf p
                         WHERE p.PlatformRiderAccountId=a.PlatformRiderAccountId
                           AND (p.AssignmentId=a.AssignmentId
                                OR (p.AssignmentId IS NULL AND p.RiderProfileId=a.RiderProfileId
                                    AND p.BusinessDate BETWEEN
                                      CASE WHEN a.EffectiveFrom>@from THEN a.EffectiveFrom ELSE @from END
                                      AND CASE WHEN a.EffectiveTo IS NULL OR a.EffectiveTo>@to THEN @to ELSE a.EffectiveTo END))), 0) AS Orders,
                       -- Account-level VDA: exactly one covering assignment per day (never both sides of a handover).
                       -- Prefer exact AssignmentId perf, then null-assignment+rider, else first covering (orphaned perf still lands).
                       ISNULL((SELECT SUM(v.PayableDistanceKm) FROM vda v
                         WHERE v.PlatformRiderAccountId=a.PlatformRiderAccountId
                           AND v.BusinessDate BETWEEN
                             CASE WHEN a.EffectiveFrom>@from THEN a.EffectiveFrom ELSE @from END
                             AND CASE WHEN a.EffectiveTo IS NULL OR a.EffectiveTo>@to THEN @to ELSE a.EffectiveTo END
                           AND {VdaDayOwnerSql("a")}), 0) AS DistancePaid,
                       ISNULL((SELECT SUM(v.ValidDay) FROM vda v
                         WHERE v.PlatformRiderAccountId=a.PlatformRiderAccountId
                           AND v.BusinessDate BETWEEN
                             CASE WHEN a.EffectiveFrom>@from THEN a.EffectiveFrom ELSE @from END
                             AND CASE WHEN a.EffectiveTo IS NULL OR a.EffectiveTo>@to THEN @to ELSE a.EffectiveTo END
                           AND {VdaDayOwnerSql("a")}), 0) AS ValidDays,
                       ISNULL((SELECT COUNT(DISTINCT p.BusinessDate) FROM perf p
                         WHERE p.PlatformRiderAccountId=a.PlatformRiderAccountId
                           AND (p.AssignmentId=a.AssignmentId
                                OR (p.AssignmentId IS NULL AND p.RiderProfileId=a.RiderProfileId
                                    AND p.BusinessDate BETWEEN
                                      CASE WHEN a.EffectiveFrom>@from THEN a.EffectiveFrom ELSE @from END
                                      AND CASE WHEN a.EffectiveTo IS NULL OR a.EffectiveTo>@to THEN @to ELSE a.EffectiveTo END))
                           AND (p.DeliveredTasks>0 OR p.ValidOnlineMinutes>0)), 0) AS WorkDays,
                       ISNULL((SELECT Violations FROM viol v WHERE v.PlatformRiderAccountId=a.PlatformRiderAccountId
                         AND v.RiderProfileId=a.RiderProfileId), 0) AS Violations,
                       (SELECT ExpectedLabel FROM elig e WHERE e.PlatformRiderAccountId=a.PlatformRiderAccountId
                         AND e.RiderProfileId=a.RiderProfileId AND e.rn=1) AS ExpectedEligibility,
                       CASE WHEN a.DismissedOn IS NOT NULL AND a.DismissedOn<=@to THEN 1 ELSE 0 END AS IsDismissed
                FROM assigns a
            ),
            with_perf AS (
                -- One row per rider×ID with performance in the period (dismissed with performance included).
                SELECT *, ValidDays AS UserDays FROM agg
                WHERE Orders>0 OR ValidDays>0 OR WorkDays>0 OR DistancePaid>0 OR Violations>0
            ),
            id_counts AS (
                SELECT ExternalAccountId, COUNT_BIG(*) AS IdRowCount FROM with_perf GROUP BY ExternalAccountId
            ),
            totals AS (
                SELECT COUNT_BIG(*) AS TotalCount,
                       SUM(CASE WHEN IsDismissed=1 THEN 1 ELSE 0 END) AS DismissedCount,
                       (SELECT COUNT(DISTINCT ExternalAccountId) FROM with_perf) AS UniqueIds,
                       ISNULL((SELECT SUM(CASE WHEN IdRowCount>1 THEN IdRowCount ELSE 0 END) FROM id_counts),0) AS DuplicateIdRows,
                       (SELECT MIN(BusinessDate) FROM (SELECT BusinessDate FROM perf UNION ALL SELECT BusinessDate FROM vda) x) AS CoverageFirstDay,
                       (SELECT MAX(BusinessDate) FROM (SELECT BusinessDate FROM perf UNION ALL SELECT BusinessDate FROM vda) x) AS CoverageLastDay,
                       (SELECT STRING_AGG(CONVERT(varchar(10), c.d, 23), ',') WITHIN GROUP (ORDER BY c.d)
                        FROM calendar c
                        WHERE NOT EXISTS (SELECT 1 FROM perf p WHERE p.BusinessDate=c.d)
                          AND NOT EXISTS (SELECT 1 FROM vda v WHERE v.BusinessDate=c.d)) AS MissingDaysCsv
                FROM with_perf
            ),
            page AS (
                SELECT w.*, ROW_NUMBER() OVER (ORDER BY w.ExternalAccountId, w.EffectiveFrom, w.AssignmentId) AS rn
                FROM with_perf w
            )
            SELECT p.*, t.TotalCount, t.DismissedCount, t.UniqueIds, t.DuplicateIdRows,
                   t.CoverageFirstDay, t.CoverageLastDay, t.MissingDaysCsv
            FROM page p CROSS JOIN totals t
            WHERE p.rn > @skip AND p.rn <= @skip + @take
            ORDER BY p.rn
            OPTION (MAXRECURSION 400)
            """;
    }

    static string BuildNinjaSql(DbCommand cmd, LiveOperationsQuery.Scope? scope, Request request)
    {
        Add(cmd, "@from", request.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@to", request.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@registerCr", request.RegisterUnifiedNumber);
        Add(cmd, "@ninja", "NINJA");
        Add(cmd, "@skip", (request.Page - 1) * request.PageSize);
        Add(cmd, "@take", request.PageSize);
        string sponsorScope = ScopeSponsor(scope, cmd, "s");
        string platformScope = ScopePlatform(scope, cmd, "cp");
        return $"""
            WITH calendar AS (
                SELECT CAST(@from AS date) AS d
                UNION ALL
                SELECT DATEADD(day, 1, d) FROM calendar WHERE d < @to
            ),
            scoped_accounts AS (
                SELECT pa.[Id] AS PlatformRiderAccountId, pa.[ExternalAccountId], pa.[Code] AS AccountCode,
                       pa.[RegisteredEmployeeId], pa.[SponsorId],
                       s.[UnifiedNationalNumber] AS RegisterUnifiedNumber,
                       s.[RegistryNameAr] AS RegisterNameAr,
                       owner_e.[IqamaNo] AS OwnerIqama, owner_e.[FullNameAr] AS OwnerNameAr,
                       owner_s.[RegistryNameAr] AS OwnerRegisterNameAr,
                       ISNULL(bd.[RegisteredVehiclePlate], N'') AS VehiclePlate,
                       ISNULL(bd.[RegisteredVehicleType], N'') AS VehicleTypeRaw,
                       ISNULL(bd.[RegistryIdentifier], N'') AS RegistryIdentifier
                FROM [app].[PlatformRiderAccounts] pa
                INNER JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=pa.[ClientPlatformId] AND cp.[IsDeleted]=0 AND cp.[Code]=@ninja
                INNER JOIN [app].[Sponsors] s ON s.[Id]=pa.[SponsorId] AND s.[IsDeleted]=0
                LEFT JOIN [app].[Employees] owner_e ON owner_e.[Id]=pa.[RegisteredEmployeeId] AND owner_e.[IsDeleted]=0
                LEFT JOIN [app].[Sponsors] owner_s ON owner_s.[Id]=owner_e.[SponsorId] AND owner_s.[IsDeleted]=0
                LEFT JOIN [app].[PlatformAccountBusinessDetails] bd ON bd.[PlatformRiderAccountId]=pa.[Id] AND bd.[IsDeleted]=0
                WHERE pa.[IsDeleted]=0 AND s.[UnifiedNationalNumber]=@registerCr{sponsorScope}{platformScope}
            ),
            assigns AS (
                SELECT a.[Id] AS AssignmentId, a.[PlatformRiderAccountId], a.[RiderProfileId],
                       a.[EffectiveFrom], a.[EffectiveTo], a.[Status] AS AssignmentStatus,
                       sa.[ExternalAccountId], sa.[AccountCode], sa.[OwnerIqama], sa.[OwnerNameAr],
                       sa.[OwnerRegisterNameAr], sa.[RegisterNameAr],
                       sa.[VehiclePlate], sa.[VehicleTypeRaw], sa.[RegistryIdentifier],
                       rider_e.[IqamaNo] AS RiderIqama, rider_e.[FullNameAr] AS RiderNameAr,
                       rider_e.[Nationality], rider_e.[PrimaryPhone], rider_e.[Iban],
                       CAST(NULL AS date) AS DismissedOn,
                       (SELECT TOP 1 fc.[CardNumber]
                        FROM [app].[FuelCardRiderAssignments] fca
                        INNER JOIN [app].[FuelCards] fc ON fc.[Id]=fca.[FuelCardId] AND fc.[IsDeleted]=0
                        WHERE fca.[RiderProfileId]=a.[RiderProfileId] AND fca.[IsDeleted]=0
                          AND fca.[EffectiveFrom]<=@to
                          AND (fca.[EffectiveTo] IS NULL OR fca.[EffectiveTo]>=@from)
                        ORDER BY fca.[EffectiveFrom] DESC) AS FuelCardNumber,
                       (SELECT TOP 1 rr.[Name]
                        FROM [app].[RiderVehicleAssignments] va
                        INNER JOIN [app].[RealRiders] rr ON rr.[RiderVehicleAssignmentId]=va.[Id]
                        WHERE va.[RiderProfileId]=a.[RiderProfileId] AND va.[IsDeleted]=0 AND va.[IsRealRider]=0
                          AND CAST(va.[StartedAtUtc] AS date)<=@to
                          AND (va.[EndedAtUtc] IS NULL OR CAST(va.[EndedAtUtc] AS date)>=@from)
                        ORDER BY va.[StartedAtUtc] DESC) AS SubstituteName,
                       (SELECT TOP 1 ISNULL(rr.[RelationshipToAssignedRider], N'')
                        FROM [app].[RiderVehicleAssignments] va
                        INNER JOIN [app].[RealRiders] rr ON rr.[RiderVehicleAssignmentId]=va.[Id]
                        WHERE va.[RiderProfileId]=a.[RiderProfileId] AND va.[IsDeleted]=0 AND va.[IsRealRider]=0
                          AND CAST(va.[StartedAtUtc] AS date)<=@to
                          AND (va.[EndedAtUtc] IS NULL OR CAST(va.[EndedAtUtc] AS date)>=@from)
                        ORDER BY va.[StartedAtUtc] DESC) AS SubstituteType
                FROM [app].[RiderClientAssignments] a
                INNER JOIN scoped_accounts sa ON sa.[PlatformRiderAccountId]=a.[PlatformRiderAccountId]
                INNER JOIN [app].[RiderProfiles] rp ON rp.[Id]=a.[RiderProfileId] AND rp.[IsDeleted]=0
                INNER JOIN [app].[Employees] rider_e ON rider_e.[Id]=rp.[EmployeeId] AND rider_e.[IsDeleted]=0
                WHERE a.[IsDeleted]=0
                  AND a.[EffectiveFrom]<=@to
                  AND (a.[EffectiveTo] IS NULL OR a.[EffectiveTo]>=@from)
            ),
            perf AS (
                SELECT d.[PlatformRiderAccountId], d.[RiderClientAssignmentId] AS AssignmentId, d.[RiderProfileId],
                       d.[ReportDate] AS BusinessDate, ISNULL(d.[TotalOrders],0) AS DeliveredTasks,
                       ISNULL(d.[TotalWorkingHours],0) AS WorkingHours
                FROM [ninja].[NinjaDailyPerformance] d
                INNER JOIN scoped_accounts sa ON sa.[PlatformRiderAccountId]=d.[PlatformRiderAccountId]
                WHERE d.[ReportDate] BETWEEN @from AND @to
            ),
            agg AS (
                SELECT a.*,
                       ISNULL((SELECT SUM(p.DeliveredTasks) FROM perf p
                         WHERE p.PlatformRiderAccountId=a.PlatformRiderAccountId
                           AND (p.AssignmentId=a.AssignmentId
                                OR (p.AssignmentId IS NULL AND p.RiderProfileId=a.RiderProfileId
                                    AND p.BusinessDate BETWEEN
                                      CASE WHEN a.EffectiveFrom>@from THEN a.EffectiveFrom ELSE @from END
                                      AND CASE WHEN a.EffectiveTo IS NULL OR a.EffectiveTo>@to THEN @to ELSE a.EffectiveTo END))), 0) AS Orders,
                       CAST(0 AS decimal(18,4)) AS DistancePaid,
                       -- Valid days stay a separate ERP fact (not order-days).
                       CAST(0 AS bigint) AS ValidDays,
                       ISNULL((SELECT COUNT(DISTINCT p.BusinessDate) FROM perf p
                         WHERE p.PlatformRiderAccountId=a.PlatformRiderAccountId
                           AND (p.AssignmentId=a.AssignmentId
                                OR (p.AssignmentId IS NULL AND p.RiderProfileId=a.RiderProfileId
                                    AND p.BusinessDate BETWEEN
                                      CASE WHEN a.EffectiveFrom>@from THEN a.EffectiveFrom ELSE @from END
                                      AND CASE WHEN a.EffectiveTo IS NULL OR a.EffectiveTo>@to THEN @to ELSE a.EffectiveTo END))
                           AND (p.DeliveredTasks>0 OR p.WorkingHours>0)), 0) AS WorkDays,
                       CAST(0 AS bigint) AS Violations,
                       -- Expected eligibility empty until evaluation snapshots map at register/rider grain.
                       CAST(N'' AS nvarchar(200)) AS ExpectedEligibility,
                       CASE WHEN a.EffectiveTo IS NOT NULL AND a.EffectiveTo < @to
                              AND NOT EXISTS (
                                SELECT 1 FROM assigns x
                                WHERE x.PlatformRiderAccountId=a.PlatformRiderAccountId
                                  AND x.AssignmentId<>a.AssignmentId
                                  AND (x.EffectiveTo IS NULL OR x.EffectiveTo>=@to))
                            THEN 1 ELSE 0 END AS IsDismissed
                FROM assigns a
            ),
            with_perf AS (
                SELECT *, WorkDays AS UserDays FROM agg
                WHERE Orders>0 OR ValidDays>0 OR WorkDays>0
            ),
            id_counts AS (
                SELECT ExternalAccountId, COUNT_BIG(*) AS IdRowCount FROM with_perf GROUP BY ExternalAccountId
            ),
            totals AS (
                SELECT COUNT_BIG(*) AS TotalCount,
                       SUM(CASE WHEN IsDismissed=1 THEN 1 ELSE 0 END) AS DismissedCount,
                       (SELECT COUNT(DISTINCT ExternalAccountId) FROM with_perf) AS UniqueIds,
                       ISNULL((SELECT SUM(CASE WHEN IdRowCount>1 THEN IdRowCount ELSE 0 END) FROM id_counts),0) AS DuplicateIdRows,
                       (SELECT MIN(BusinessDate) FROM perf) AS CoverageFirstDay,
                       (SELECT MAX(BusinessDate) FROM perf) AS CoverageLastDay,
                       (SELECT STRING_AGG(CONVERT(varchar(10), c.d, 23), ',') WITHIN GROUP (ORDER BY c.d)
                        FROM calendar c
                        WHERE NOT EXISTS (SELECT 1 FROM perf p WHERE p.BusinessDate=c.d)) AS MissingDaysCsv
                FROM with_perf
            ),
            page AS (
                SELECT w.*, ROW_NUMBER() OVER (ORDER BY w.ExternalAccountId, w.EffectiveFrom, w.AssignmentId) AS rn
                FROM with_perf w
            )
            SELECT p.*, t.TotalCount, t.DismissedCount, t.UniqueIds, t.DuplicateIdRows,
                   t.CoverageFirstDay, t.CoverageLastDay, t.MissingDaysCsv
            FROM page p CROSS JOIN totals t
            WHERE p.rn > @skip AND p.rn <= @skip + @take
            ORDER BY p.rn
            OPTION (MAXRECURSION 400)
            """;
    }

    static JsonObject MapRow(IDataRecord r)
    {
        bool dismissed = Num(r, "IsDismissed") == 1;
        bool active = !dismissed;
        string status = active ? "TRUE" : "FALSE";
        string expected = Str(r, "ExpectedEligibility");
        var row = new JsonObject
        {
            ["row"] = Num(r, "rn"),
            ["dismissed"] = dismissed,
            ["active"] = active,
            ["expectedApproval"] = "expected_only",
            ["expectedFinal"] = false,
            ["platformRiderAccountId"] = Str(r, "PlatformRiderAccountId"),
            ["riderProfileId"] = Str(r, "RiderProfileId"),
            ["assignmentId"] = Str(r, "AssignmentId"),
            ["المعرف"] = Str(r, "ExternalAccountId"),
            ["الاسم بالكامل"] = Str(r, "RiderNameAr"),
            ["رقم بطاقة الهوية"] = Str(r, "OwnerIqama"),
            ["كفالة اليوزر"] = Str(r, "OwnerRegisterNameAr").Length > 0 ? Str(r, "OwnerRegisterNameAr") : Str(r, "RegisterNameAr"),
            ["المركبة"] = Str(r, "VehiclePlate"),
            ["الحالة"] = status,
            ["رقم الهوية1"] = Str(r, "RiderIqama"),
            ["البديل 1"] = Str(r, "SubstituteName"),
            ["رقم التواصل"] = Str(r, "PrimaryPhone"),
            ["نوع البديل"] = Str(r, "SubstituteType"),
            ["نوع المركبة"] = VehicleTypeLabel(Str(r, "VehicleTypeRaw")),
            ["رقم كرت البنزين1"] = Str(r, "FuelCardNumber"),
            ["الأدوات1"] = "",
            ["الجنسية 1"] = Str(r, "Nationality"),
            ["الرقم المسجل"] = Str(r, "RegistryIdentifier").Length > 0 ? Str(r, "RegistryIdentifier") : Str(r, "AccountCode"),
            ["الايبان 1"] = Str(r, "Iban"),
            ["تاريخ الاستلام1"] = DateStr(r, "EffectiveFrom"),
            ["تاريخ التسليم"] = DateStr(r, "EffectiveTo"),
            ["الأيام الصالحة"] = Num(r, "ValidDays"),
            ["الطلبات"] = Num(r, "Orders"),
            ["المخالفات"] = Num(r, "Violations"),
            ["المسافة المدفوعة"] = Dec(r, "DistancePaid") ?? 0m,
            ["عدد ايام عمل المندوب"] = Num(r, "WorkDays"),
            ["الصلاحية المتوقعة"] = expected,
            ["اجمالي المسافه"] = Dec(r, "DistancePaid") ?? 0m,
            ["مطابقة المسافة"] = "",
            ["الملاحظات"] = dismissed ? "مفصول بأداء في الفترة (عرض فقط)" : "",
            ["اجمالي طلبات المعادلة"] = 0,
            ["اجمالي طلبات اليوزر"] = Num(r, "Orders"),
            ["مطابقة الطلبات"] = "",
            ["عدد البدلاء"] = 0,
            ["اجمالي ايام المعادلة"] = 0,
            ["اجمالي ايام اليوزر"] = Num(r, "UserDays"),
            ["مطابقة الايام"] = ""
        };
        return row;
    }

    static JsonArray ParseMissingDays(string csv)
    {
        var arr = new JsonArray();
        if (string.IsNullOrWhiteSpace(csv)) return arr;
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            arr.Add(part);
        return arr;
    }

    static string VehicleTypeLabel(string code) => code switch
    {
        "1" or "Motorcycle" or "Bike" => "دراجة",
        "2" or "Car" => "سيارة",
        "3" or "Van" => "فان",
        "4" or "Truck" => "شاحنة",
        "5" => "أخرى",
        _ => code
    };

    /// <summary>Workforce-style IBAN strip for callers without riders.iban.view (empty cell).</summary>
    public static void MaskIbanUnlessPermitted(IEnumerable<JsonObject> rows, bool canViewIban)
    {
        if (canViewIban) return;
        foreach (var row in rows)
            row["الايبان 1"] = "";
    }

    /// <summary>
    /// Exactly one covering assignment owns each account VDA day.
    /// Priority: AssignmentId perf match, then null-assignment+rider match, else earliest covering
    /// (so orphaned perf — null rider or assignment outside assigns — still attributes once).
    /// </summary>
    static string VdaDayOwnerSql(string assignmentAlias) => $"""
        {assignmentAlias}.AssignmentId = (
          SELECT TOP 1 aPick.AssignmentId
          FROM assigns aPick
          WHERE aPick.PlatformRiderAccountId={assignmentAlias}.PlatformRiderAccountId
            AND v.BusinessDate BETWEEN
              CASE WHEN aPick.EffectiveFrom>@from THEN aPick.EffectiveFrom ELSE @from END
              AND CASE WHEN aPick.EffectiveTo IS NULL OR aPick.EffectiveTo>@to THEN @to ELSE aPick.EffectiveTo END
          ORDER BY
            CASE
              WHEN EXISTS (
                SELECT 1 FROM perf p
                WHERE p.PlatformRiderAccountId=aPick.PlatformRiderAccountId
                  AND p.BusinessDate=v.BusinessDate
                  AND p.AssignmentId=aPick.AssignmentId) THEN 0
              WHEN EXISTS (
                SELECT 1 FROM perf p
                WHERE p.PlatformRiderAccountId=aPick.PlatformRiderAccountId
                  AND p.BusinessDate=v.BusinessDate
                  AND p.AssignmentId IS NULL AND p.RiderProfileId=aPick.RiderProfileId) THEN 1
              ELSE 2
            END,
            aPick.EffectiveFrom,
            aPick.AssignmentId)
        """;

    static string ScopeSponsor(LiveOperationsQuery.Scope? scope, DbCommand cmd, string alias)
    {
        if (scope is null or { AllCompanies: true }) return "";
        if (scope.SponsorUnifiedNumbers.Count == 0) return " AND 1=0";
        string regs = InList(cmd, "@cr", scope.SponsorUnifiedNumbers);
        return $" AND {alias}.[UnifiedNationalNumber] IN ({regs})";
    }

    static string ScopePlatform(LiveOperationsQuery.Scope? scope, DbCommand cmd, string alias)
    {
        if (scope is not { PlatformRestricted: true }) return "";
        if (scope.PlatformCodes.Count == 0) return " AND 1=0";
        string codes = InList(cmd, "@pc", scope.PlatformCodes);
        return $" AND {alias}.[Code] IN ({codes})";
    }

    static string InList(DbCommand cmd, string prefix, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            Add(cmd, prefix + "0", Guid.Empty.ToString("D"));
            return prefix + "0";
        }
        if (values.Count > LiveOperationsQuery.MaxInlineParameters)
        {
            Add(cmd, prefix + "json", System.Text.Json.JsonSerializer.Serialize(values));
            return $"SELECT [value] FROM OPENJSON({prefix}json)";
        }
        var names = new List<string>(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            string name = prefix + i;
            Add(cmd, name, values[i]);
            names.Add(name);
        }
        return string.Join(',', names);
    }

    static void Add(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    static string Str(IDataRecord r, string name)
    {
        try
        {
            int ord = r.GetOrdinal(name);
            if (r.IsDBNull(ord)) return "";
            return Convert.ToString(r.GetValue(ord), CultureInfo.InvariantCulture)?.Trim() ?? "";
        }
        catch (IndexOutOfRangeException) { return ""; }
    }

    static string DateStr(IDataRecord r, string name)
    {
        try
        {
            int ord = r.GetOrdinal(name);
            if (r.IsDBNull(ord)) return "";
            object v = r.GetValue(ord);
            return v switch
            {
                DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTimeOffset dto => dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? ""
            };
        }
        catch (IndexOutOfRangeException) { return ""; }
    }

    static long Num(IDataRecord r, string name)
    {
        try
        {
            int ord = r.GetOrdinal(name);
            if (r.IsDBNull(ord)) return 0L;
            return Convert.ToInt64(r.GetValue(ord), CultureInfo.InvariantCulture);
        }
        catch (IndexOutOfRangeException) { return 0L; }
    }

    static decimal? Dec(IDataRecord r, string name)
    {
        try
        {
            int ord = r.GetOrdinal(name);
            if (r.IsDBNull(ord)) return null;
            return Convert.ToDecimal(r.GetValue(ord), CultureInfo.InvariantCulture);
        }
        catch (IndexOutOfRangeException) { return null; }
    }
}
