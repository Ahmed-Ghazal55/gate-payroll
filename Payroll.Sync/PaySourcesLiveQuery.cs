using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace ErpImport;

/// <summary>
/// Live read-only L1 pay-run input sources from Omar's ERP (fuel, vehicle periods, housing periods,
/// Keeta eligibility expected values, HS stamp/WalletPending). Facts only — no pay calculation.
/// Company/platform scope and paging live in SQL. Identifiers allowlisted; values are parameters.
/// </summary>
public static class PaySourcesLiveQuery
{
    public const int MaxPageSize = 200, CommandTimeoutSeconds = 15;

    public sealed record PageResult(IReadOnlyList<JsonObject> Items, long Total, JsonObject Summary, DateTimeOffset AsOf);

    public static (string Sql, IReadOnlyList<string> ParameterNames) PreviewFuelConsumptionSql(
        LiveOperationsQuery.Scope? scope = null)
        => OperationsLiveQuery.PreviewFuelCardsSql(scope);

    /// <summary>
    /// F13b: one row per fuel card in the pay-cycle range (same daily/monthly + coverage rules as operations fuel).
    /// </summary>
    public static PageResult FuelConsumption(DbConnection source, DateOnly from, DateOnly to, int page, int pageSize,
        string search, LiveOperationsQuery.Scope scope, CancellationToken ct = default)
    {
        var result = OperationsLiveQuery.FuelCards(source, from, to, "", page, pageSize, search, scope, ct);
        string period = from.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var items = result.Items.Select(r => new JsonObject
        {
            ["id"] = r["id"]?.DeepClone(),
            ["fuelCardId"] = r["id"]?.DeepClone(),
            ["cardNumber"] = r["cardNumber"]?.DeepClone(),
            ["provider"] = r["provider"]?.DeepClone(),
            ["plateNumber"] = r["plateNumber"]?.DeepClone(),
            ["riderProfileId"] = "",
            ["employeeId"] = "",
            ["riderNameAr"] = r["riderNameAr"]?.DeepClone(),
            ["iqama"] = r["iqama"]?.DeepClone(),
            ["period"] = period,
            ["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["liters"] = r["liters"]?.DeepClone(),
            ["amountBeforeTax"] = r["amountBeforeTax"]?.DeepClone(),
            ["vat"] = r["vat"]?.DeepClone(),
            ["totalInclVat"] = r["totalInclVat"]?.DeepClone(),
            ["transactionCount"] = r["transactionCount"]?.DeepClone(),
            ["fuelType"] = r["fuelType"]?.DeepClone(),
            ["usageSource"] = r["usageSource"]?.DeepClone(),
            ["coveredFrom"] = r["coveredFrom"]?.DeepClone(),
            ["coveredTo"] = r["coveredTo"]?.DeepClone(),
            ["sponsorUnifiedNumber"] = ""
        }).ToList();
        var summary = new JsonObject
        {
            ["rows"] = result.Total,
            ["riders"] = result.Summary["consumingRiders"]?.DeepClone() ?? JsonValue.Create(0L),
            ["totalLiters"] = result.Summary["totalLiters"]?.DeepClone() ?? JsonValue.Create(0m),
            ["totalInclVat"] = result.Summary["totalInclVat"]?.DeepClone() ?? JsonValue.Create(0m),
            ["coveredFrom"] = result.Summary["coveredFrom"]?.DeepClone() ?? "",
            ["coveredTo"] = result.Summary["coveredTo"]?.DeepClone() ?? "",
            ["uncoveredDays"] = result.Summary["uncoveredDays"]?.DeepClone() ?? JsonValue.Create(0L)
        };
        return new(items, result.Total, summary, result.AsOf);
    }

    public static (string Sql, IReadOnlyList<string> ParameterNames) PreviewVehicleAssignmentPeriodsSql(
        LiveOperationsQuery.Scope? scope = null)
    {
        using var cmd = new SqlCommand();
        Add(cmd, "@from", "2026-10-01");
        Add(cmd, "@to", "2026-10-31");
        Add(cmd, "@skip", 0);
        Add(cmd, "@take", 50);
        string sql = VehicleAssignmentPeriodsSql(ScopeSponsor(scope, cmd, "s"))
            + "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        return (sql, cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).ToList());
    }

    public static PageResult VehicleAssignmentPeriods(DbConnection source, DateOnly from, DateOnly to,
        int page, int pageSize, string search, LiveOperationsQuery.Scope scope, CancellationToken ct = default)
    {
        EnsureSql(source);
        if (to < from) throw new ImportFailure("invalid_period");
        pageSize = Math.Min(Math.Max(1, pageSize), MaxPageSize);
        page = Math.Max(1, page);
        int skip = checked((page - 1) * pageSize);
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        Add(cmd, "@from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        if (search.Length > 0) Add(cmd, "@q", "%" + EscapeLike(search) + "%");
        Add(cmd, "@skip", skip);
        Add(cmd, "@take", pageSize);
        cmd.CommandText = VehicleAssignmentPeriodsSql(scopeSql, search.Length > 0)
            + "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        long total = 0;
        var summary = new JsonObject { ["rows"] = 0L, ["openAssignments"] = 0L };
        var items = new List<JsonObject>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (items.Count == 0)
                {
                    total = Long(reader, "TotalCount");
                    summary = new JsonObject
                    {
                        ["rows"] = total,
                        ["openAssignments"] = Long(reader, "OpenCount")
                    };
                }
                items.Add(MapVehicleAssignmentRow(reader));
            }
        }
        return new(items, total, summary, DateTimeOffset.UtcNow);
    }

    static string VehicleAssignmentPeriodsSql(string scopeSql, bool search = false)
    {
        string searchPred = search
            ? " AND (v.[PlateNumberAr] LIKE @q OR v.[PlateNumberEn] LIKE @q OR v.[AssetNumber] LIKE @q OR e.[FullNameAr] LIKE @q OR e.[IqamaNo] LIKE @q OR rr.[Name] LIKE @q OR rr.[IqamaNo] LIKE @q)"
            : "";
        return $"""
            WITH filtered AS (
                SELECT a.[Id], a.[RiderProfileId], a.[VehicleId], a.[IsRealRider],
                       a.[StartedAtUtc], a.[EndedAtUtc], a.[StartOdometer], a.[EndOdometer],
                       a.[PermissionStartsOn], a.[PermissionEndsOn], a.[Status],
                       v.[AssetNumber], v.[PlateNumberAr], v.[PlateNumberEn], v.[VehicleType],
                       s.[UnifiedNationalNumber] AS SponsorUnifiedNumber,
                       CASE WHEN a.[IsRealRider]=1 THEN e.[FullNameAr] ELSE rr.[Name] END AS RiderNameAr,
                       CASE WHEN a.[IsRealRider]=1 THEN e.[IqamaNo] ELSE rr.[IqamaNo] END AS IqamaNo,
                       CASE WHEN a.[EndedAtUtc] IS NULL THEN 1 ELSE 0 END AS IsOpen
                FROM [app].[RiderVehicleAssignments] a
                INNER JOIN [app].[Vehicles] v ON v.[Id]=a.[VehicleId] AND v.[IsDeleted]=0
                LEFT JOIN [app].[Sponsors] s ON s.[Id]=v.[SponsorId] AND s.[IsDeleted]=0
                LEFT JOIN [app].[RiderProfiles] rp ON rp.[Id]=a.[RiderProfileId] AND rp.[IsDeleted]=0
                LEFT JOIN [app].[Employees] e ON e.[Id]=rp.[EmployeeId] AND e.[IsDeleted]=0
                LEFT JOIN [app].[RealRiders] rr ON rr.[RiderVehicleAssignmentId]=a.[Id]
                WHERE a.[IsDeleted]=0
                  AND CONVERT(date, SWITCHOFFSET(a.[StartedAtUtc], '+03:00')) <= CONVERT(date, @to)
                  AND (a.[EndedAtUtc] IS NULL OR CONVERT(date, SWITCHOFFSET(a.[EndedAtUtc], '+03:00')) >= CONVERT(date, @from))
                  {scopeSql}{searchPred}
            ),
            agg AS (
                SELECT COUNT_BIG(*) AS TotalCount,
                       SUM(IsOpen) AS OpenCount
                FROM filtered
            )
            SELECT f.*, a.TotalCount, a.OpenCount
            FROM filtered f CROSS JOIN agg a
            ORDER BY f.StartedAtUtc, f.Id
            """;
    }

    static JsonObject MapVehicleAssignmentRow(IDataRecord r) => new()
    {
        ["id"] = Str(r, "Id"),
        ["riderProfileId"] = Str(r, "RiderProfileId"),
        ["vehicleId"] = Str(r, "VehicleId"),
        ["isRealRider"] = Bool(r, "IsRealRider"),
        ["riderNameAr"] = Str(r, "RiderNameAr"),
        ["iqama"] = Str(r, "IqamaNo"),
        ["assetNumber"] = Str(r, "AssetNumber"),
        ["plateNumberAr"] = Str(r, "PlateNumberAr"),
        ["plateNumberEn"] = Str(r, "PlateNumberEn"),
        ["vehicleType"] = Str(r, "VehicleType"),
        ["startedAt"] = Str(r, "StartedAtUtc"),
        ["endedAt"] = Str(r, "EndedAtUtc"),
        ["startOdometer"] = NullLong(r, "StartOdometer"),
        ["endOdometer"] = NullLong(r, "EndOdometer"),
        ["permissionStartsOn"] = Str(r, "PermissionStartsOn"),
        ["permissionEndsOn"] = Str(r, "PermissionEndsOn"),
        ["status"] = Str(r, "Status"),
        ["sponsorUnifiedNumber"] = Str(r, "SponsorUnifiedNumber"),
        ["isOpen"] = Bool(r, "IsOpen")
    };

    public static (string Sql, IReadOnlyList<string> ParameterNames) PreviewHousingPeriodsSql(
        LiveOperationsQuery.Scope? scope = null)
    {
        using var cmd = new SqlCommand();
        Add(cmd, "@from", "2026-10-01");
        Add(cmd, "@to", "2026-10-31");
        Add(cmd, "@skip", 0);
        Add(cmd, "@take", 50);
        string sql = HousingPeriodsSql(ScopeSponsor(scope, cmd, "s"))
            + "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        return (sql, cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).ToList());
    }

    public static PageResult HousingPeriods(DbConnection source, DateOnly from, DateOnly to,
        int page, int pageSize, string search, LiveOperationsQuery.Scope scope, CancellationToken ct = default)
    {
        EnsureSql(source);
        if (to < from) throw new ImportFailure("invalid_period");
        pageSize = Math.Min(Math.Max(1, pageSize), MaxPageSize);
        page = Math.Max(1, page);
        int skip = checked((page - 1) * pageSize);
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        Add(cmd, "@from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        string scopeSql = ScopeHousing(scope, cmd);
        if (search.Length > 0) Add(cmd, "@q", "%" + EscapeLike(search) + "%");
        Add(cmd, "@skip", skip);
        Add(cmd, "@take", pageSize);
        cmd.CommandText = HousingPeriodsSql(scopeSql, search.Length > 0)
            + "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        long total = 0;
        var summary = new JsonObject { ["rows"] = 0L, ["openPeriods"] = 0L, ["daysInPeriod"] = 0L };
        var items = new List<JsonObject>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (items.Count == 0)
                {
                    total = Long(reader, "TotalCount");
                    summary = new JsonObject
                    {
                        ["rows"] = total,
                        ["openPeriods"] = Long(reader, "OpenCount"),
                        ["daysInPeriod"] = Long(reader, "DaysSum")
                    };
                }
                items.Add(MapHousingPeriodRow(reader));
            }
        }
        return new(items, total, summary, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Housing has no SponsorId; scope by resident employee's sponsor UnifiedNationalNumber when restricted.
    /// </summary>
    static string ScopeHousing(LiveOperationsQuery.Scope? scope, DbCommand cmd)
    {
        if (scope is null or { AllCompanies: true }) return "";
        if (scope.SponsorUnifiedNumbers.Count == 0) return " AND 1=0";
        string regs = InList(cmd, "@cr", scope.SponsorUnifiedNumbers);
        return $" AND s.[UnifiedNationalNumber] IN ({regs})";
    }

    static string HousingPeriodsSql(string scopeSql, bool search = false)
    {
        string searchPred = search
            ? " AND (h.[Code] LIKE @q OR h.[NameAr] LIKE @q OR room.[Name] LIKE @q OR e.[FullNameAr] LIKE @q OR e.[IqamaNo] LIKE @q)"
            : "";
        return $"""
            WITH filtered AS (
                SELECT p.[Id], p.[EmployeeId], p.[RoomId], p.[EffectiveFrom], p.[EffectiveTo],
                       p.[MoveInReason], p.[MoveOutReason], p.[SourceReference],
                       room.[Name] AS RoomName, room.[HousingId],
                       h.[Code] AS HousingCode, h.[NameAr] AS HousingNameAr,
                       e.[FullNameAr] AS RiderNameAr, e.[IqamaNo],
                       rp.[Id] AS RiderProfileId,
                       s.[UnifiedNationalNumber] AS SponsorUnifiedNumber,
                       CASE WHEN p.[EffectiveTo] IS NULL THEN 1 ELSE 0 END AS IsOpen,
                       CASE
                         WHEN p.[EffectiveFrom] > CONVERT(date, @to) THEN 0
                         WHEN p.[EffectiveTo] IS NOT NULL AND p.[EffectiveTo] < CONVERT(date, @from) THEN 0
                         ELSE DATEDIFF(DAY,
                              CASE WHEN p.[EffectiveFrom] > CONVERT(date, @from) THEN p.[EffectiveFrom] ELSE CONVERT(date, @from) END,
                              CASE WHEN p.[EffectiveTo] IS NULL OR p.[EffectiveTo] > CONVERT(date, @to) THEN CONVERT(date, @to) ELSE p.[EffectiveTo] END
                         ) + 1
                       END AS DaysInSelectedPeriod
                FROM [app].[HousingResidencePeriods] p
                INNER JOIN [app].[HousingRooms] room ON room.[Id]=p.[RoomId] AND room.[IsDeleted]=0
                INNER JOIN [app].[Housing] h ON h.[Id]=room.[HousingId] AND h.[IsDeleted]=0
                LEFT JOIN [app].[Employees] e ON e.[Id]=p.[EmployeeId] AND e.[IsDeleted]=0
                LEFT JOIN [app].[Sponsors] s ON s.[Id]=e.[SponsorId] AND s.[IsDeleted]=0
                LEFT JOIN [app].[RiderProfiles] rp ON rp.[EmployeeId]=e.[Id] AND rp.[IsDeleted]=0
                WHERE p.[EffectiveFrom] <= CONVERT(date, @to)
                  AND (p.[EffectiveTo] IS NULL OR p.[EffectiveTo] >= CONVERT(date, @from))
                  {scopeSql}{searchPred}
            ),
            agg AS (
                SELECT COUNT_BIG(*) AS TotalCount,
                       SUM(IsOpen) AS OpenCount,
                       ISNULL(SUM(DaysInSelectedPeriod),0) AS DaysSum
                FROM filtered
            )
            SELECT f.*, a.TotalCount, a.OpenCount, a.DaysSum
            FROM filtered f CROSS JOIN agg a
            ORDER BY f.EffectiveFrom, f.Id
            """;
    }

    static JsonObject MapHousingPeriodRow(IDataRecord r) => new()
    {
        ["id"] = Str(r, "Id"),
        ["employeeId"] = Str(r, "EmployeeId"),
        ["riderProfileId"] = Str(r, "RiderProfileId"),
        ["riderNameAr"] = Str(r, "RiderNameAr"),
        ["iqama"] = Str(r, "IqamaNo"),
        ["housingId"] = Str(r, "HousingId"),
        ["housingCode"] = Str(r, "HousingCode"),
        ["housingNameAr"] = Str(r, "HousingNameAr"),
        ["roomId"] = Str(r, "RoomId"),
        ["roomName"] = Str(r, "RoomName"),
        ["effectiveFrom"] = Str(r, "EffectiveFrom"),
        ["effectiveTo"] = Str(r, "EffectiveTo"),
        ["daysInSelectedPeriod"] = Long(r, "DaysInSelectedPeriod"),
        ["isOpen"] = Bool(r, "IsOpen"),
        ["moveInReason"] = Str(r, "MoveInReason"),
        ["moveOutReason"] = Str(r, "MoveOutReason"),
        ["sponsorUnifiedNumber"] = Str(r, "SponsorUnifiedNumber")
    };

    public static (string Sql, IReadOnlyList<string> ParameterNames) PreviewKeetaEligibilitySql(
        LiveOperationsQuery.Scope? scope = null)
    {
        using var cmd = new SqlCommand();
        Add(cmd, "@month", new DateTime(2026, 11, 1));
        Add(cmd, "@skip", 0);
        Add(cmd, "@take", 50);
        string sql = KeetaEligibilitySql(ScopeSponsor(scope, cmd, "s"), ScopePlatform(scope, cmd, "cp"))
            + "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        return (sql, cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).ToList());
    }

    public static PageResult KeetaEligibility(DbConnection source, DateOnly month, int page, int pageSize,
        string search, LiveOperationsQuery.Scope scope, CancellationToken ct = default)
    {
        EnsureSql(source);
        pageSize = Math.Min(Math.Max(1, pageSize), MaxPageSize);
        page = Math.Max(1, page);
        int skip = checked((page - 1) * pageSize);
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        Add(cmd, "@month", new DateTime(month.Year, month.Month, 1));
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        string platformSql = ScopePlatform(scope, cmd, "cp");
        if (search.Length > 0) Add(cmd, "@q", "%" + EscapeLike(search) + "%");
        Add(cmd, "@skip", skip);
        Add(cmd, "@take", pageSize);
        cmd.CommandText = KeetaEligibilitySql(scopeSql, platformSql, search.Length > 0)
            + "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        long total = 0;
        var summary = new JsonObject { ["rows"] = 0L, ["eligible"] = 0L, ["notEligible"] = 0L, ["pending"] = 0L };
        var items = new List<JsonObject>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (items.Count == 0)
                {
                    total = Long(reader, "TotalCount");
                    summary = new JsonObject
                    {
                        ["rows"] = total,
                        ["eligible"] = Long(reader, "EligibleCount"),
                        ["notEligible"] = Long(reader, "NotEligibleCount"),
                        ["pending"] = Long(reader, "PendingCount")
                    };
                }
                items.Add(MapKeetaEligibilityRow(reader, month));
            }
        }
        return new(items, total, summary, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Newest expected eligibility row per account+rider+month+source from keeta.vw_keeta_eligibility_v1.
    /// Settlement scheme from PlatformAccountBusinessDetails (raw ERP value). Days lost / orders required from ResultJson.
    /// </summary>
    static string KeetaEligibilitySql(string scopeSql, string platformSql, bool search = false)
    {
        string searchPred = search
            ? " AND (pa.[ExternalAccountId] LIKE @q OR pa.[Code] LIKE @q OR CONVERT(nvarchar(36), e.[PlatformRiderAccountId]) LIKE @q)"
            : "";
        return $"""
            WITH ranked AS (
                SELECT e.[SnapshotId], e.[PlatformRiderAccountId], e.[RiderProfileId], e.[SponsorId],
                       e.[OperatingCityId], e.[Month], e.[DataThrough], e.[Source], e.[RuleSetId], e.[RuleVersion],
                       e.[Status], e.[ValidDays], e.[OrdersDelivered], e.[ExpectedExperienceTier],
                       e.[ExpectedCapacityTier], e.[ResultJson], e.[CreatedAtUtc],
                       pa.[ExternalAccountId] AS KeetaId, pa.[Code] AS AccountCode,
                       bd.[SettlementModel] AS SettlementScheme,
                       s.[UnifiedNationalNumber] AS SponsorUnifiedNumber,
                       ROW_NUMBER() OVER (
                         PARTITION BY e.[PlatformRiderAccountId], e.[RiderProfileId], e.[Month], e.[Source]
                         ORDER BY e.[CreatedAtUtc] DESC, e.[SnapshotId] DESC
                       ) AS rn
                FROM [keeta].[vw_keeta_eligibility_v1] e
                INNER JOIN [app].[PlatformRiderAccounts] pa ON pa.[Id]=e.[PlatformRiderAccountId] AND pa.[IsDeleted]=0
                INNER JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=pa.[ClientPlatformId] AND cp.[IsDeleted]=0
                LEFT JOIN [app].[Sponsors] s ON s.[Id]=e.[SponsorId] AND s.[IsDeleted]=0
                LEFT JOIN [app].[PlatformAccountBusinessDetails] bd ON bd.[PlatformRiderAccountId]=pa.[Id] AND bd.[IsDeleted]=0
                WHERE e.[Month]=@month{scopeSql}{platformSql}{searchPred}
            ),
            filtered AS (
                SELECT * FROM ranked WHERE rn=1
            ),
            agg AS (
                SELECT COUNT_BIG(*) AS TotalCount,
                       SUM(CASE WHEN Status=N'Valid' THEN 1 ELSE 0 END) AS EligibleCount,
                       SUM(CASE WHEN Status=N'NotValid' THEN 1 ELSE 0 END) AS NotEligibleCount,
                       SUM(CASE WHEN Status=N'Pending' OR Status IS NULL OR Status=N'' THEN 1 ELSE 0 END) AS PendingCount
                FROM filtered
            )
            SELECT f.*, a.TotalCount, a.EligibleCount, a.NotEligibleCount, a.PendingCount
            FROM filtered f CROSS JOIN agg a
            ORDER BY f.CreatedAtUtc DESC, f.SnapshotId
            """;
    }

    static JsonObject MapKeetaEligibilityRow(IDataRecord r, DateOnly month)
    {
        string resultJson = Str(r, "ResultJson");
        var fromJson = ReadEligibilityJson(resultJson);
        return new()
        {
            ["id"] = Str(r, "SnapshotId"),
            ["platformRiderAccountId"] = Str(r, "PlatformRiderAccountId"),
            ["keetaId"] = Str(r, "KeetaId"),
            ["accountCode"] = Str(r, "AccountCode"),
            ["riderProfileId"] = Str(r, "RiderProfileId"),
            ["sponsorId"] = Str(r, "SponsorId"),
            ["sponsorUnifiedNumber"] = Str(r, "SponsorUnifiedNumber"),
            ["operatingCityId"] = Str(r, "OperatingCityId"),
            ["period"] = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            ["month"] = month.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["dataThrough"] = Str(r, "DataThrough"),
            ["source"] = Str(r, "Source"),
            ["ruleSetId"] = Str(r, "RuleSetId"),
            ["ruleVersion"] = NullLong(r, "RuleVersion"),
            ["expectedStatus"] = Str(r, "Status"),
            ["validDays"] = NullLong(r, "ValidDays") ?? fromJson.ValidDays,
            ["invalidDays"] = fromJson.InvalidDays,
            ["allowedLossDays"] = fromJson.AllowedLossDays,
            ["requiredValidDays"] = fromJson.RequiredValidDays,
            ["ordersDelivered"] = NullLong(r, "OrdersDelivered") ?? fromJson.OrdersDelivered,
            ["monthlyOrdersRequired"] = fromJson.MonthlyOrdersRequired,
            ["ordersRequiredSoFar"] = fromJson.OrdersRequiredSoFar,
            ["settlementScheme"] = Str(r, "SettlementScheme"),
            ["expectedExperienceTier"] = Str(r, "ExpectedExperienceTier"),
            ["expectedCapacityTier"] = Str(r, "ExpectedCapacityTier"),
            ["createdAt"] = Str(r, "CreatedAtUtc"),
            ["approval"] = "expected_only"
        };
    }

    sealed record EligibilityJson(
        long? ValidDays, long? InvalidDays, long? AllowedLossDays, long? RequiredValidDays,
        long? OrdersDelivered, long? MonthlyOrdersRequired, long? OrdersRequiredSoFar);

    static EligibilityJson ReadEligibilityJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(null, null, null, null, null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            long? N(string name)
            {
                if (!root.TryGetProperty(name, out var p) || p.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    return null;
                if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out long v)) return v;
                if (p.ValueKind == JsonValueKind.String && long.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long s))
                    return s;
                return null;
            }
            return new(N("validDays"), N("invalidDays"), N("allowedLossDays"), N("requiredValidDays"),
                N("ordersDelivered"), N("monthlyOrdersRequired"), N("ordersRequiredSoFar"));
        }
        catch (JsonException) { return new(null, null, null, null, null, null, null); }
    }

    public static (string Sql, IReadOnlyList<string> ParameterNames) PreviewHsStampQueueSql(
        LiveOperationsQuery.Scope? scope = null)
    {
        using var cmd = new SqlCommand();
        Add(cmd, "@skip", 0);
        Add(cmd, "@take", 50);
        string sql = HsStampQueueSql(ScopeSponsor(scope, cmd, "s"), ScopePlatform(scope, cmd, "cp"))
            + "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        return (sql, cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).ToList());
    }

    public static PageResult HsStampQueue(DbConnection source, int page, int pageSize, string search,
        string disposition, LiveOperationsQuery.Scope scope, CancellationToken ct = default)
    {
        EnsureSql(source);
        pageSize = Math.Min(Math.Max(1, pageSize), MaxPageSize);
        page = Math.Max(1, page);
        int skip = checked((page - 1) * pageSize);
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        string platformSql = ScopePlatform(scope, cmd, "cp");
        bool dispositionFilter = disposition.Length > 0;
        if (dispositionFilter) Add(cmd, "@disposition", disposition);
        if (search.Length > 0) Add(cmd, "@q", "%" + EscapeLike(search) + "%");
        Add(cmd, "@skip", skip);
        Add(cmd, "@take", pageSize);
        cmd.CommandText = HsStampQueueSql(scopeSql, platformSql, search.Length > 0, dispositionFilter)
            + "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        long total = 0;
        var summary = new JsonObject
        {
            ["rows"] = 0L,
            ["walletPending"] = 0L,
            ["deferredNextMonth"] = 0L,
            ["stampedEvents"] = 0L,
            ["pendingSealEvents"] = 0L
        };
        var items = new List<JsonObject>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (items.Count == 0)
                {
                    total = Long(reader, "TotalCount");
                    summary = new JsonObject
                    {
                        ["rows"] = total,
                        ["walletPending"] = Long(reader, "WalletPendingCount"),
                        ["deferredNextMonth"] = Long(reader, "DeferredCount"),
                        ["stampedEvents"] = Long(reader, "SealedEventCount"),
                        ["pendingSealEvents"] = Long(reader, "PendingSealCount")
                    };
                }
                items.Add(MapHsStampRow(reader));
            }
        }
        return new(items, total, summary, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Union of (1) HS accounts with WalletPending/DeferredNextMonth disposition and
    /// (2) finance_read stamped/pending-seal financial events. Read side only.
    /// </summary>
    static string HsStampQueueSql(string scopeSql, string platformSql, bool search = false, bool dispositionFilter = false)
    {
        string searchPredAccount = search
            ? " AND (pa.[ExternalAccountId] LIKE @q OR pa.[Code] LIKE @q OR bd.[OperationalDisposition] LIKE @q)"
            : "";
        string searchPredEvent = search
            ? " AND (pa.[ExternalAccountId] LIKE @q OR pa.[Code] LIKE @q OR ev.[Kind] LIKE @q OR ev.[ApprovalStatus] LIKE @q)"
            : "";
        string dispositionPred = dispositionFilter ? " AND bd.[OperationalDisposition]=@disposition" : "";
        return $"""
            WITH account_rows AS (
                SELECT CONVERT(nvarchar(36), pa.[Id]) AS RowId,
                       N'wallet_status' AS RowKind,
                       pa.[Id] AS PlatformRiderAccountId,
                       pa.[ExternalAccountId], pa.[Code] AS AccountCode,
                       bd.[OperationalDisposition] AS Disposition,
                       bd.[DispositionReason], bd.[DeferredUntil],
                       CAST(NULL AS uniqueidentifier) AS EventId,
                       CAST(NULL AS nvarchar(100)) AS EventKind,
                       CAST(NULL AS nvarchar(100)) AS EventStatus,
                       CAST(NULL AS nvarchar(100)) AS ApprovalStatus,
                       CAST(NULL AS decimal(18,2)) AS Amount,
                       CAST(NULL AS date) AS BusinessDate,
                       CAST(NULL AS datetimeoffset) AS ApprovedAtUtc,
                       bd.[SourceActualRiderProfileId] AS RiderProfileId,
                       s.[UnifiedNationalNumber] AS SponsorUnifiedNumber,
                       bd.[UpdatedAtUtc] AS SortAt
                FROM [app].[PlatformRiderAccounts] pa
                INNER JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=pa.[ClientPlatformId] AND cp.[IsDeleted]=0
                INNER JOIN [app].[PlatformAccountBusinessDetails] bd ON bd.[PlatformRiderAccountId]=pa.[Id] AND bd.[IsDeleted]=0
                LEFT JOIN [app].[Sponsors] s ON s.[Id]=pa.[SponsorId] AND s.[IsDeleted]=0
                WHERE pa.[IsDeleted]=0
                  AND bd.[OperationalDisposition] IN (N'WalletPending', N'DeferredNextMonth')
                  {scopeSql}{platformSql}{dispositionPred}{searchPredAccount}
            ),
            event_rows AS (
                SELECT CONVERT(nvarchar(36), ev.[EventId]) AS RowId,
                       N'stamp_event' AS RowKind,
                       ev.[PlatformRiderAccountId],
                       pa.[ExternalAccountId], pa.[Code] AS AccountCode,
                       bd.[OperationalDisposition] AS Disposition,
                       CAST(NULL AS nvarchar(400)) AS DispositionReason,
                       CAST(NULL AS date) AS DeferredUntil,
                       ev.[EventId], ev.[Kind] AS EventKind, ev.[Status] AS EventStatus,
                       ev.[ApprovalStatus], ev.[Amount], ev.[BusinessDate], ev.[ApprovedAtUtc],
                       ev.[RiderProfileId],
                       s.[UnifiedNationalNumber] AS SponsorUnifiedNumber,
                       COALESCE(ev.[ApprovedAtUtc], ev.[CreatedAtUtc]) AS SortAt
                FROM [finance_read].[vw_hunger_financial_events_v1] ev
                LEFT JOIN [app].[PlatformRiderAccounts] pa ON pa.[Id]=ev.[PlatformRiderAccountId] AND pa.[IsDeleted]=0
                LEFT JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=pa.[ClientPlatformId] AND cp.[IsDeleted]=0
                LEFT JOIN [app].[PlatformAccountBusinessDetails] bd ON bd.[PlatformRiderAccountId]=pa.[Id] AND bd.[IsDeleted]=0
                LEFT JOIN [app].[Sponsors] s ON s.[Id]=pa.[SponsorId] AND s.[IsDeleted]=0
                WHERE ev.[ApprovalStatus] IN (N'Sealed', N'PendingSeal')
                  {scopeSql}{platformSql}{searchPredEvent}
            ),
            filtered AS (
                SELECT * FROM account_rows
                UNION ALL
                SELECT * FROM event_rows
            ),
            agg AS (
                SELECT COUNT_BIG(*) AS TotalCount,
                       SUM(CASE WHEN RowKind=N'wallet_status' AND Disposition=N'WalletPending' THEN 1 ELSE 0 END) AS WalletPendingCount,
                       SUM(CASE WHEN RowKind=N'wallet_status' AND Disposition=N'DeferredNextMonth' THEN 1 ELSE 0 END) AS DeferredCount,
                       SUM(CASE WHEN RowKind=N'stamp_event' AND ApprovalStatus=N'Sealed' THEN 1 ELSE 0 END) AS SealedEventCount,
                       SUM(CASE WHEN RowKind=N'stamp_event' AND ApprovalStatus=N'PendingSeal' THEN 1 ELSE 0 END) AS PendingSealCount
                FROM filtered
            )
            SELECT f.*, a.TotalCount, a.WalletPendingCount, a.DeferredCount, a.SealedEventCount, a.PendingSealCount
            FROM filtered f CROSS JOIN agg a
            ORDER BY f.SortAt DESC, f.RowId
            """;
    }

    static JsonObject MapHsStampRow(IDataRecord r) => new()
    {
        ["id"] = Str(r, "RowId"),
        ["rowKind"] = Str(r, "RowKind"),
        ["platformRiderAccountId"] = Str(r, "PlatformRiderAccountId"),
        ["externalAccountId"] = Str(r, "ExternalAccountId"),
        ["accountCode"] = Str(r, "AccountCode"),
        ["disposition"] = Str(r, "Disposition"),
        ["dispositionReason"] = Str(r, "DispositionReason"),
        ["deferredUntil"] = Str(r, "DeferredUntil"),
        ["eventId"] = Str(r, "EventId"),
        ["eventKind"] = Str(r, "EventKind"),
        ["eventStatus"] = Str(r, "EventStatus"),
        ["approvalStatus"] = Str(r, "ApprovalStatus"),
        ["amount"] = NullDecimal(r, "Amount"),
        ["businessDate"] = Str(r, "BusinessDate"),
        ["approvedAt"] = Str(r, "ApprovedAtUtc"),
        ["riderProfileId"] = Str(r, "RiderProfileId"),
        ["sponsorUnifiedNumber"] = Str(r, "SponsorUnifiedNumber")
    };

    static void EnsureSql(DbConnection source)
    {
        if (source is not SqlConnection) throw new ImportFailure("live_ops_requires_sql");
    }

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
        if (values.Count == 0) { Add(cmd, prefix + "0", Guid.Empty.ToString("D")); return prefix + "0"; }
        if (values.Count > LiveOperationsQuery.MaxInlineParameters)
        {
            Add(cmd, prefix + "json", JsonSerializer.Serialize(values));
            return $"SELECT [value] FROM OPENJSON({prefix}json)";
        }
        var names = new List<string>();
        for (int i = 0; i < values.Count; i++)
        {
            string name = prefix + i;
            Add(cmd, name, values[i]);
            names.Add(name);
        }
        return string.Join(',', names);
    }

    static string EscapeLike(string value) =>
        value.Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal).Replace("_", "[_]", StringComparison.Ordinal);

    static void Add(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    static string Str(IDataRecord r, string col)
    {
        try
        {
            int ord = r.GetOrdinal(col);
            if (r.IsDBNull(ord)) return "";
            return Convert.ToString(r.GetValue(ord), CultureInfo.InvariantCulture) ?? "";
        }
        catch (IndexOutOfRangeException) { return ""; }
    }

    static long Long(IDataRecord r, string col)
    {
        try
        {
            int ord = r.GetOrdinal(col);
            if (r.IsDBNull(ord)) return 0;
            return Convert.ToInt64(r.GetValue(ord), CultureInfo.InvariantCulture);
        }
        catch (IndexOutOfRangeException) { return 0; }
    }

    static long? NullLong(IDataRecord r, string col)
    {
        try
        {
            int ord = r.GetOrdinal(col);
            if (r.IsDBNull(ord)) return null;
            return Convert.ToInt64(r.GetValue(ord), CultureInfo.InvariantCulture);
        }
        catch (IndexOutOfRangeException) { return null; }
    }

    static decimal Decimal(IDataRecord r, string col)
    {
        try
        {
            int ord = r.GetOrdinal(col);
            if (r.IsDBNull(ord)) return 0;
            return Convert.ToDecimal(r.GetValue(ord), CultureInfo.InvariantCulture);
        }
        catch (IndexOutOfRangeException) { return 0; }
    }

    static decimal? NullDecimal(IDataRecord r, string col)
    {
        try
        {
            int ord = r.GetOrdinal(col);
            if (r.IsDBNull(ord)) return null;
            return Convert.ToDecimal(r.GetValue(ord), CultureInfo.InvariantCulture);
        }
        catch (IndexOutOfRangeException) { return null; }
    }

    static bool Bool(IDataRecord r, string col)
    {
        try
        {
            int ord = r.GetOrdinal(col);
            if (r.IsDBNull(ord)) return false;
            object v = r.GetValue(ord);
            if (v is bool b) return b;
            return Convert.ToInt32(v, CultureInfo.InvariantCulture) != 0;
        }
        catch (IndexOutOfRangeException) { return false; }
    }

    static string FuelProviderLabel(string code) => code switch
    {
        "1" => "PetroApp",
        "2" => "SayaraApp",
        "PetroApp" => "PetroApp",
        "SayaraApp" => "SayaraApp",
        _ => code
    };
}
