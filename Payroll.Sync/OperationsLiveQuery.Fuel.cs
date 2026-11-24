using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
namespace ErpImport;

public static partial class OperationsLiveQuery
{
    public static PageResult FuelCards(DbConnection source, DateOnly month, string provider, int page, int pageSize,
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
        bool providerFilter = provider.Length > 0;
        if (providerFilter) Add(cmd, "@provider", provider);
        if (search.Length > 0) Add(cmd, "@q", "%" + EscapeLike(search) + "%");
        Add(cmd, "@skip", skip);
        Add(cmd, "@take", pageSize);
        bool preferDaily = HasFuelUsageDateColumn(source, ct);
        cmd.CommandText = FuelCardsSql(scopeSql, providerFilter, search.Length > 0, preferDaily)
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
                        ["activeCards"] = Long(reader, "ActiveCards"),
                        ["consumingRiders"] = Long(reader, "ConsumingRiders"),
                        ["totalLiters"] = Decimal(reader, "SumLiters"),
                        ["totalInclVat"] = Decimal(reader, "TotalInclVat"),
                        ["capExceeded"] = null,
                        ["capExceededSource"] = NoSourceYet
                    };
                }
                items.Add(MapFuelCardRow(reader));
            }
        }
        if (items.Count == 0)
            summary = FuelSummaryOnly(source, month, provider, search, scope, preferDaily, ct);
        return new(items, total, summary, DateTimeOffset.UtcNow);
    }

    static JsonObject FuelSummaryOnly(DbConnection source, DateOnly month, string provider, string search,
        LiveOperationsQuery.Scope scope, bool preferDaily, CancellationToken ct)
    {
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        Add(cmd, "@month", new DateTime(month.Year, month.Month, 1));
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        bool providerFilter = provider.Length > 0;
        if (providerFilter) Add(cmd, "@provider", provider);
        if (search.Length > 0) Add(cmd, "@q", "%" + EscapeLike(search) + "%");
        Add(cmd, "@skip", 0);
        Add(cmd, "@take", 1);
        cmd.CommandText = FuelCardsSql(scopeSql, providerFilter, search.Length > 0, preferDaily)
            + "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return EmptyFuelSummary();
        return new JsonObject
        {
            ["activeCards"] = Long(reader, "ActiveCards"),
            ["consumingRiders"] = Long(reader, "ConsumingRiders"),
            ["totalLiters"] = Decimal(reader, "SumLiters"),
            ["totalInclVat"] = Decimal(reader, "TotalInclVat"),
            ["capExceeded"] = null,
            ["capExceededSource"] = NoSourceYet
        };
    }

    public static PageResult FuelAssignments(DbConnection source, DateOnly from, DateOnly to, int page, int pageSize,
        LiveOperationsQuery.Scope scope, CancellationToken ct = default, bool unpaged = false, string search = "")
    {
        EnsureSql(source);
        if (to < from) throw new ImportFailure("invalid_period");
        pageSize = Math.Min(Math.Max(1, pageSize), MaxPageSize);
        page = Math.Max(1, page);
        int skip = checked((page - 1) * pageSize);
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = unpaged ? ExportCommandTimeoutSeconds : CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        Add(cmd, "@from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        string searchPred = "";
        if (!string.IsNullOrWhiteSpace(search))
        {
            Add(cmd, "@q", "%" + EscapeLike(search.Trim()) + "%");
            searchPred = " AND (fc.[CardNumber] LIKE @q OR fc.[PlateNumberText] LIKE @q OR fc.[Notes] LIKE @q OR e.[FullNameAr] LIKE @q OR e.[IqamaNo] LIKE @q)";
        }
        string pageSql = "";
        if (!unpaged)
        {
            Add(cmd, "@skip", skip);
            Add(cmd, "@take", pageSize);
            pageSql = "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        }
        cmd.CommandText = $"""
            WITH filtered AS (
                SELECT a.[Id], a.[EffectiveFrom], a.[EffectiveTo], a.[AssignmentReason], a.[EndReason],
                       fc.[CardNumber], fc.[Provider], fc.[PlateNumberText], fc.[Notes],
                       e.[FullNameAr] AS RiderNameAr, e.[IqamaNo]
                FROM [app].[FuelCardRiderAssignments] a
                INNER JOIN [app].[FuelCards] fc ON fc.[Id]=a.[FuelCardId] AND fc.[IsDeleted]=0
                LEFT JOIN [app].[Sponsors] s ON s.[Id]=fc.[SponsorId] AND s.[IsDeleted]=0
                LEFT JOIN [app].[Employees] e ON e.[Id]=a.[EmployeeId] AND e.[IsDeleted]=0
                WHERE CONVERT(date, a.[EffectiveFrom]) <= CONVERT(date, @to)
                  AND (a.[EffectiveTo] IS NULL OR CONVERT(date, a.[EffectiveTo]) >= CONVERT(date, @from))
                  {scopeSql}{searchPred}
            ),
            agg AS (SELECT COUNT_BIG(*) AS TotalCount FROM filtered)
            SELECT f.*, a.TotalCount
            FROM filtered f CROSS JOIN agg a
            ORDER BY f.EffectiveFrom DESC{pageSql}
            """;
        long total = 0;
        var items = new List<JsonObject>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (items.Count == 0) total = Long(reader, "TotalCount");
                string rawProvider = Str(reader, "Provider");
                string providerAr = FuelProviderNameAr(rawProvider);
                string cardNotes = Str(reader, "Notes");
                items.Add(new JsonObject
                {
                    ["id"] = Str(reader, "Id"),
                    ["cardNumber"] = Str(reader, "CardNumber"),
                    ["provider"] = FuelProviderLabel(rawProvider),
                    ["providerNameAr"] = providerAr,
                    ["cardName"] = !string.IsNullOrWhiteSpace(cardNotes) ? cardNotes : providerAr,
                    ["cardNotes"] = cardNotes,
                    ["plateNumber"] = Str(reader, "PlateNumberText"),
                    ["riderNameAr"] = Str(reader, "RiderNameAr"),
                    ["iqama"] = Str(reader, "IqamaNo"),
                    ["effectiveFrom"] = Str(reader, "EffectiveFrom"),
                    ["effectiveTo"] = Str(reader, "EffectiveTo"),
                    ["assignmentReason"] = Str(reader, "AssignmentReason"),
                    ["endReason"] = Str(reader, "EndReason")
                });
            }
        }
        return new(items, total, new JsonObject { ["assignmentsTotal"] = total }, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// M5: one row per card (SUM usages in month). Rider = usage employee; fallback = assignment open in that month.
    /// consumingRiders = distinct usage EmployeeIds with liters &gt; 0.
    /// F13: when UsageDate exists, per card-month prefer daily rows (UsageDate not null) over the monthly summary (null); never both.
    /// Snapshot/live schemas without UsageDate keep the legacy SUM of all month rows.
    /// </summary>
    static string FuelCardsSql(string scopeSql, bool providerFilter, bool search = false, bool preferDailyUsage = false)
    {
        string providerPred = providerFilter ? " AND fc.[Provider]=@provider" : "";
        string searchPred = search ? " AND (fc.[CardNumber] LIKE @q OR fc.[PlateNumberText] LIKE @q OR fc.[Notes] LIKE @q OR ue.[FullNameAr] LIKE @q OR ue.[IqamaNo] LIKE @q OR ae.[FullNameAr] LIKE @q OR ae.[IqamaNo] LIKE @q)" : "";
        string usageSource = preferDailyUsage
            ? """
                usage_base AS (
                    SELECT u.[FuelCardId], u.[TotalLiters], u.[AmountBeforeTax], u.[VatAmount], u.[TotalAmount],
                           u.[TransactionCount], u.[FuelType], u.[EmployeeId], u.[UsageDate],
                           MAX(CASE WHEN u.[UsageDate] IS NOT NULL THEN 1 ELSE 0 END)
                             OVER (PARTITION BY u.[FuelCardId]) AS HasDaily
                    FROM [app].[FuelCardMonthlyUsages] u
                    WHERE u.[IsDeleted]=0 AND u.[ReportMonth]=@month
                ),
                usage_rows AS (
                    SELECT b.[FuelCardId], b.[TotalLiters], b.[AmountBeforeTax], b.[VatAmount], b.[TotalAmount],
                           b.[TransactionCount], b.[FuelType], b.[EmployeeId]
                    FROM usage_base b
                    WHERE (b.[HasDaily]=1 AND b.[UsageDate] IS NOT NULL)
                       OR (b.[HasDaily]=0)
                )
                """
            : """
                usage_rows AS (
                    SELECT u.[FuelCardId], u.[TotalLiters], u.[AmountBeforeTax], u.[VatAmount], u.[TotalAmount],
                           u.[TransactionCount], u.[FuelType], u.[EmployeeId]
                    FROM [app].[FuelCardMonthlyUsages] u
                    WHERE u.[IsDeleted]=0 AND u.[ReportMonth]=@month
                )
                """;
        string consumingFrom = preferDailyUsage
            ? """
                        FROM usage_rows u2
                        INNER JOIN [app].[FuelCards] fc2 ON fc2.[Id]=u2.[FuelCardId] AND fc2.[IsDeleted]=0
                        LEFT JOIN [app].[Sponsors] s2 ON s2.[Id]=fc2.[SponsorId] AND s2.[IsDeleted]=0
                        WHERE u2.[EmployeeId] IS NOT NULL AND u2.[TotalLiters] IS NOT NULL AND u2.[TotalLiters]>0
                """
            : """
                        FROM [app].[FuelCardMonthlyUsages] u2
                        INNER JOIN [app].[FuelCards] fc2 ON fc2.[Id]=u2.[FuelCardId] AND fc2.[IsDeleted]=0
                        LEFT JOIN [app].[Sponsors] s2 ON s2.[Id]=fc2.[SponsorId] AND s2.[IsDeleted]=0
                        WHERE u2.[IsDeleted]=0 AND u2.[ReportMonth]=@month
                          AND u2.[EmployeeId] IS NOT NULL AND u2.[TotalLiters] IS NOT NULL AND u2.[TotalLiters]>0
                """;
        return $"""
            WITH {usageSource},
            usage_agg AS (
                SELECT u.[FuelCardId],
                       SUM(u.[TotalLiters]) AS TotalLiters,
                       SUM(u.[AmountBeforeTax]) AS AmountBeforeTax,
                       SUM(u.[VatAmount]) AS VatAmount,
                       SUM(u.[TotalAmount]) AS TotalAmount,
                       SUM(u.[TransactionCount]) AS TransactionCount,
                       MAX(u.[FuelType]) AS FuelType,
                       MAX(u.[EmployeeId]) AS UsageEmployeeId
                FROM usage_rows u
                GROUP BY u.[FuelCardId]
            ),
            filtered AS (
                SELECT fc.[Id], fc.[CardNumber], fc.[Provider], fc.[PlateNumberText], fc.[Notes],
                       COALESCE(ue.[FullNameAr], ae.[FullNameAr]) AS RiderNameAr,
                       COALESCE(ue.[IqamaNo], ae.[IqamaNo]) AS IqamaNo,
                       ua.[TotalLiters], ua.[AmountBeforeTax], ua.[VatAmount], ua.[TotalAmount], ua.[TransactionCount], ua.[FuelType],
                       ua.[UsageEmployeeId],
                       CASE WHEN fa.[Id] IS NOT NULL THEN 1 ELSE 0 END AS HasOpenAssignment
                FROM [app].[FuelCards] fc
                LEFT JOIN [app].[Sponsors] s ON s.[Id]=fc.[SponsorId] AND s.[IsDeleted]=0
                LEFT JOIN usage_agg ua ON ua.[FuelCardId]=fc.[Id]
                LEFT JOIN [app].[Employees] ue ON ue.[Id]=ua.[UsageEmployeeId] AND ue.[IsDeleted]=0
                OUTER APPLY (
                    SELECT TOP 1 a.[Id], a.[EmployeeId]
                    FROM [app].[FuelCardRiderAssignments] a
                    WHERE a.[FuelCardId]=fc.[Id]
                      AND a.[EffectiveFrom] <= EOMONTH(@month)
                      AND (a.[EffectiveTo] IS NULL OR a.[EffectiveTo] >= @month)
                    ORDER BY a.[EffectiveFrom] DESC
                ) fa
                LEFT JOIN [app].[Employees] ae ON ae.[Id]=fa.[EmployeeId] AND ae.[IsDeleted]=0
                     AND ua.[UsageEmployeeId] IS NULL
                WHERE fc.[IsDeleted]=0{scopeSql}{providerPred}{searchPred}
            ),
            agg AS (
                SELECT COUNT_BIG(*) AS TotalCount,
                       SUM(CASE WHEN HasOpenAssignment=1 THEN 1 ELSE 0 END) AS ActiveCards,
                       (SELECT COUNT_BIG(DISTINCT u2.[EmployeeId])
                        {consumingFrom}
                          {scopeSql.Replace("s.[", "s2.[", StringComparison.Ordinal)}) AS ConsumingRiders,
                       ISNULL(SUM(TotalLiters),0) AS TotalLiters,
                       ISNULL(SUM(TotalAmount),0) AS TotalInclVat
                FROM filtered
            )
            SELECT f.*, a.TotalCount, a.ActiveCards, a.ConsumingRiders, a.TotalLiters AS SumLiters, a.TotalInclVat
            FROM filtered f CROSS JOIN agg a
            ORDER BY f.Provider, f.CardNumber
            """;
    }

    /// <summary>erp-schema.snapshot.json has no UsageDate yet; detect at runtime so live SELECTs keep working.</summary>
    static bool HasFuelUsageDateColumn(DbConnection source, CancellationToken ct = default)
    {
        using var cmd = source.CreateCommand();
        using var cancel = SyncCancellation.Register(cmd, ct);
        cmd.CommandText = "SELECT CASE WHEN COL_LENGTH('app.FuelCardMonthlyUsages','UsageDate') IS NULL THEN 0 ELSE 1 END";
        object? value = cmd.ExecuteScalar();
        return value is not null and not DBNull && Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
    }

    public static PageResult FuelCardsExport(DbConnection source, DateOnly month, string provider, string search,
        LiveOperationsQuery.Scope scope, int timeoutSeconds, CancellationToken ct = default)
    {
        EnsureSql(source);
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = timeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        Add(cmd, "@month", new DateTime(month.Year, month.Month, 1));
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        bool providerFilter = provider.Length > 0;
        if (providerFilter) Add(cmd, "@provider", provider);
        if (search.Length > 0) Add(cmd, "@q", "%" + EscapeLike(search) + "%");
        cmd.CommandText = FuelCardsSql(scopeSql, providerFilter, search.Length > 0, HasFuelUsageDateColumn(source, ct));
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
                        ["activeCards"] = Long(reader, "ActiveCards"),
                        ["consumingRiders"] = Long(reader, "ConsumingRiders"),
                        ["totalLiters"] = Decimal(reader, "SumLiters"),
                        ["totalInclVat"] = Decimal(reader, "TotalInclVat"),
                        ["capExceeded"] = null,
                        ["capExceededSource"] = NoSourceYet
                    };
                }
                items.Add(MapFuelCardRow(reader));
            }
        }
        return new(items, total, summary, DateTimeOffset.UtcNow);
    }

    static JsonObject MapFuelCardRow(IDataRecord r)
    {
        string rawProvider = Str(r, "Provider");
        string providerAr = FuelProviderNameAr(rawProvider);
        string cardNotes = Str(r, "Notes");
        return new()
        {
            ["id"] = Str(r, "Id"),
            ["cardNumber"] = Str(r, "CardNumber"),
            ["provider"] = FuelProviderLabel(rawProvider),
            ["providerNameAr"] = providerAr,
            ["cardName"] = !string.IsNullOrWhiteSpace(cardNotes) ? cardNotes : providerAr,
            ["cardNotes"] = cardNotes,
            ["plateNumber"] = Str(r, "PlateNumberText"),
            ["riderNameAr"] = Str(r, "RiderNameAr"),
            ["iqama"] = Str(r, "IqamaNo"),
            ["liters"] = NullDecimal(r, "TotalLiters"),
            ["amountBeforeTax"] = NullDecimal(r, "AmountBeforeTax"),
            ["vat"] = NullDecimal(r, "VatAmount"),
            ["totalInclVat"] = NullDecimal(r, "TotalAmount"),
            ["transactionCount"] = NullLong(r, "TransactionCount"),
            ["fuelType"] = Str(r, "FuelType"),
            ["cap"] = null,
            ["capSource"] = NoSourceYet,
            ["status"] = FuelStatusLabel(Long(r, "HasOpenAssignment") == 1 ? "active" : "inactive")
        };
    }

    static JsonObject EmptyFuelSummary() => new()
    {
        ["activeCards"] = 0L,
        ["consumingRiders"] = 0L,
        ["totalLiters"] = 0m,
        ["totalInclVat"] = 0m,
        ["capExceeded"] = null,
        ["capExceededSource"] = NoSourceYet
    };
}
