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
    {
        using var cmd = new SqlCommand();
        Add(cmd, "@month", new DateTime(2026, 10, 1));
        Add(cmd, "@skip", 0);
        Add(cmd, "@take", 50);
        string sql = FuelConsumptionSql(ScopeSponsor(scope, cmd, "s")) + "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        return (sql, cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).ToList());
    }

    public static PageResult FuelConsumption(DbConnection source, DateOnly month, int page, int pageSize,
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
        if (search.Length > 0) Add(cmd, "@q", "%" + EscapeLike(search) + "%");
        Add(cmd, "@skip", skip);
        Add(cmd, "@take", pageSize);
        cmd.CommandText = FuelConsumptionSql(scopeSql, search.Length > 0)
            + "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        long total = 0;
        var summary = EmptyFuelSummary();
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
                        ["riders"] = Long(reader, "RiderCount"),
                        ["totalLiters"] = Decimal(reader, "SumLiters"),
                        ["totalInclVat"] = Decimal(reader, "SumAmount")
                    };
                }
                items.Add(MapFuelRow(reader, month));
            }
        }
        if (items.Count == 0)
            summary = FuelSummaryOnly(source, month, search, scope, ct);
        return new(items, total, summary, DateTimeOffset.UtcNow);
    }

    static JsonObject FuelSummaryOnly(DbConnection source, DateOnly month, string search,
        LiveOperationsQuery.Scope scope, CancellationToken ct)
    {
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        Add(cmd, "@month", new DateTime(month.Year, month.Month, 1));
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        if (search.Length > 0) Add(cmd, "@q", "%" + EscapeLike(search) + "%");
        Add(cmd, "@skip", 0);
        Add(cmd, "@take", 1);
        cmd.CommandText = FuelConsumptionSql(scopeSql, search.Length > 0)
            + "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return EmptyFuelSummary();
        return new JsonObject
        {
            ["rows"] = Long(reader, "TotalCount"),
            ["riders"] = Long(reader, "RiderCount"),
            ["totalLiters"] = Decimal(reader, "SumLiters"),
            ["totalInclVat"] = Decimal(reader, "SumAmount")
        };
    }

    static JsonObject EmptyFuelSummary() => new()
    {
        ["rows"] = 0L,
        ["riders"] = 0L,
        ["totalLiters"] = 0m,
        ["totalInclVat"] = 0m
    };

    /// <summary>
    /// One row per FuelCardMonthlyUsages in the report month: rider + card/plate + liters/amount facts.
    /// Vehicle is the card plate text (ERP has no vehicle FK on usages).
    /// </summary>
    static string FuelConsumptionSql(string scopeSql, bool search = false)
    {
        string searchPred = search
            ? " AND (fc.[CardNumber] LIKE @q OR fc.[PlateNumberText] LIKE @q OR e.[FullNameAr] LIKE @q OR e.[IqamaNo] LIKE @q OR CONVERT(nvarchar(36), u.[RiderProfileId]) LIKE @q)"
            : "";
        return $"""
            WITH filtered AS (
                SELECT u.[Id], u.[FuelCardId], u.[RiderProfileId], u.[EmployeeId], u.[ReportMonth],
                       u.[TotalLiters], u.[AmountBeforeTax], u.[VatAmount], u.[TotalAmount],
                       u.[TransactionCount], u.[FuelType],
                       fc.[CardNumber], fc.[Provider], fc.[PlateNumberText],
                       e.[FullNameAr] AS RiderNameAr, e.[IqamaNo],
                       s.[UnifiedNationalNumber] AS SponsorUnifiedNumber
                FROM [app].[FuelCardMonthlyUsages] u
                INNER JOIN [app].[FuelCards] fc ON fc.[Id]=u.[FuelCardId] AND fc.[IsDeleted]=0
                LEFT JOIN [app].[Sponsors] s ON s.[Id]=fc.[SponsorId] AND s.[IsDeleted]=0
                LEFT JOIN [app].[Employees] e ON e.[Id]=u.[EmployeeId] AND e.[IsDeleted]=0
                WHERE u.[IsDeleted]=0 AND u.[ReportMonth]=@month{scopeSql}{searchPred}
            ),
            agg AS (
                SELECT COUNT_BIG(*) AS TotalCount,
                       COUNT_BIG(DISTINCT RiderProfileId) AS RiderCount,
                       ISNULL(SUM(TotalLiters),0) AS SumLiters,
                       ISNULL(SUM(TotalAmount),0) AS SumAmount
                FROM filtered
            )
            SELECT f.*, a.TotalCount, a.RiderCount, a.SumLiters, a.SumAmount
            FROM filtered f CROSS JOIN agg a
            ORDER BY f.ReportMonth, f.Id
            """;
    }

    static JsonObject MapFuelRow(IDataRecord r, DateOnly month) => new()
    {
        ["id"] = Str(r, "Id"),
        ["fuelCardId"] = Str(r, "FuelCardId"),
        ["cardNumber"] = Str(r, "CardNumber"),
        ["provider"] = FuelProviderLabel(Str(r, "Provider")),
        ["plateNumber"] = Str(r, "PlateNumberText"),
        ["riderProfileId"] = Str(r, "RiderProfileId"),
        ["employeeId"] = Str(r, "EmployeeId"),
        ["riderNameAr"] = Str(r, "RiderNameAr"),
        ["iqama"] = Str(r, "IqamaNo"),
        ["period"] = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        ["reportMonth"] = month.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["liters"] = NullDecimal(r, "TotalLiters"),
        ["amountBeforeTax"] = NullDecimal(r, "AmountBeforeTax"),
        ["vat"] = NullDecimal(r, "VatAmount"),
        ["totalInclVat"] = NullDecimal(r, "TotalAmount"),
        ["transactionCount"] = NullLong(r, "TransactionCount"),
        ["fuelType"] = Str(r, "FuelType"),
        ["sponsorUnifiedNumber"] = Str(r, "SponsorUnifiedNumber")
    };

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
