using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
namespace ErpImport;

public static partial class OperationsLiveQuery
{
    public static PageResult FuelCards(DbConnection source, DateOnly from, DateOnly to, string provider, int page, int pageSize,
        string search, LiveOperationsQuery.Scope scope, CancellationToken ct = default)
    {
        EnsureSql(source);
        if (to < from) throw new ImportFailure("invalid_period");
        pageSize = Math.Min(Math.Max(1, pageSize), MaxPageSize);
        page = Math.Max(1, page);
        int skip = checked((page - 1) * pageSize);
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        AddFuelRange(cmd, from, to);
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        bool providerFilter = provider.Length > 0;
        if (providerFilter) Add(cmd, "@provider", provider);
        if (search.Length > 0) Add(cmd, "@q", "%" + EscapeLike(search) + "%");
        Add(cmd, "@skip", skip);
        Add(cmd, "@take", pageSize);
        bool preferDaily = HasFuelUsageDateColumn(source, ct);
        bool importCoverage = HasFuelImportCoverageColumns(source, ct);
        cmd.CommandText = FuelCardsSql(scopeSql, providerFilter, search.Length > 0, preferDaily, importCoverage)
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
                    summary = MapFuelSummary(reader);
                }
                items.Add(MapFuelCardRow(reader));
            }
        }
        if (items.Count == 0)
            summary = FuelSummaryOnly(source, from, to, provider, search, scope, preferDaily, importCoverage, ct);
        return new(items, total, summary, DateTimeOffset.UtcNow);
    }

    static JsonObject FuelSummaryOnly(DbConnection source, DateOnly from, DateOnly to, string provider, string search,
        LiveOperationsQuery.Scope scope, bool preferDaily, bool importCoverage, CancellationToken ct)
    {
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        AddFuelRange(cmd, from, to);
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        bool providerFilter = provider.Length > 0;
        if (providerFilter) Add(cmd, "@provider", provider);
        if (search.Length > 0) Add(cmd, "@q", "%" + EscapeLike(search) + "%");
        Add(cmd, "@skip", 0);
        Add(cmd, "@take", 1);
        cmd.CommandText = FuelCardsSql(scopeSql, providerFilter, search.Length > 0, preferDaily, importCoverage)
            + "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return EmptyFuelSummary();
        return MapFuelSummary(reader);
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
    /// F13b: pay-cycle / picked range. Daily rows (UsageDate in range) win per card-month;
    /// legacy monthly (UsageDate null) only when that card-month has no daily rows and the range
    /// covers the whole ReportMonth. Coverage from FuelCardImports (UsageFrom/UsageTo, InvalidRows=0)
    /// or a complete legacy month — uncovered days are a warning, never zero liters.
    /// </summary>
    static string FuelCardsSql(string scopeSql, bool providerFilter, bool search = false,
        bool preferDailyUsage = false, bool importCoverage = false)
    {
        string providerPred = providerFilter ? " AND fc.[Provider]=@provider" : "";
        string searchPred = search ? " AND (fc.[CardNumber] LIKE @q OR fc.[PlateNumberText] LIKE @q OR fc.[Notes] LIKE @q OR ue.[FullNameAr] LIKE @q OR ue.[IqamaNo] LIKE @q OR ae.[FullNameAr] LIKE @q OR ae.[IqamaNo] LIKE @q)" : "";
        string usageSource = preferDailyUsage
            ? """
                month_daily AS (
                    SELECT u.[FuelCardId], u.[ReportMonth],
                           MAX(CASE WHEN u.[UsageDate] IS NOT NULL THEN 1 ELSE 0 END) AS HasDaily
                    FROM [app].[FuelCardMonthlyUsages] u
                    WHERE u.[IsDeleted]=0
                      AND u.[ReportMonth] >= DATEFROMPARTS(YEAR(CONVERT(date,@from)), MONTH(CONVERT(date,@from)), 1)
                      AND u.[ReportMonth] <= DATEFROMPARTS(YEAR(CONVERT(date,@to)), MONTH(CONVERT(date,@to)), 1)
                    GROUP BY u.[FuelCardId], u.[ReportMonth]
                ),
                usage_rows AS (
                    SELECT u.[FuelCardId], u.[TotalLiters], u.[AmountBeforeTax], u.[VatAmount], u.[TotalAmount],
                           u.[TransactionCount], u.[FuelType], u.[EmployeeId],
                           CAST('daily' AS nvarchar(16)) AS UsageSource
                    FROM [app].[FuelCardMonthlyUsages] u
                    INNER JOIN month_daily md ON md.[FuelCardId]=u.[FuelCardId] AND md.[ReportMonth]=u.[ReportMonth]
                    WHERE u.[IsDeleted]=0 AND md.[HasDaily]=1
                      AND u.[UsageDate] IS NOT NULL
                      AND u.[UsageDate] >= CONVERT(date,@from) AND u.[UsageDate] <= CONVERT(date,@to)
                    UNION ALL
                    SELECT u.[FuelCardId], u.[TotalLiters], u.[AmountBeforeTax], u.[VatAmount], u.[TotalAmount],
                           u.[TransactionCount], u.[FuelType], u.[EmployeeId],
                           CAST('monthly' AS nvarchar(16)) AS UsageSource
                    FROM [app].[FuelCardMonthlyUsages] u
                    INNER JOIN month_daily md ON md.[FuelCardId]=u.[FuelCardId] AND md.[ReportMonth]=u.[ReportMonth]
                    WHERE u.[IsDeleted]=0 AND md.[HasDaily]=0 AND u.[UsageDate] IS NULL
                      AND CONVERT(date,@from) <= u.[ReportMonth]
                      AND CONVERT(date,@to) >= EOMONTH(u.[ReportMonth])
                )
                """
            : """
                usage_rows AS (
                    SELECT u.[FuelCardId], u.[TotalLiters], u.[AmountBeforeTax], u.[VatAmount], u.[TotalAmount],
                           u.[TransactionCount], u.[FuelType], u.[EmployeeId],
                           CAST('monthly' AS nvarchar(16)) AS UsageSource
                    FROM [app].[FuelCardMonthlyUsages] u
                    WHERE u.[IsDeleted]=0
                      AND u.[ReportMonth] >= DATEFROMPARTS(YEAR(CONVERT(date,@from)), MONTH(CONVERT(date,@from)), 1)
                      AND u.[ReportMonth] <= DATEFROMPARTS(YEAR(CONVERT(date,@to)), MONTH(CONVERT(date,@to)), 1)
                      AND CONVERT(date,@from) <= u.[ReportMonth]
                      AND CONVERT(date,@to) >= EOMONTH(u.[ReportMonth])
                )
                """;
        string coverageCte = importCoverage
            ? """
                ,
                covered_days AS (
                    SELECT DISTINCT d.[Day]
                    FROM (
                        SELECT DATEADD(day, v.number, CONVERT(date,@from)) AS [Day]
                        FROM (SELECT TOP (DATEDIFF(day, CONVERT(date,@from), CONVERT(date,@to)) + 1)
                                     ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS number
                              FROM sys.all_objects) v
                    ) d
                    WHERE EXISTS (
                        SELECT 1 FROM [app].[FuelCardImports] i
                        WHERE i.[InvalidRows]=0 AND i.[Provider]=1
                          AND i.[UsageFrom] IS NOT NULL AND i.[UsageTo] IS NOT NULL
                          AND d.[Day] >= i.[UsageFrom] AND d.[Day] <= i.[UsageTo]
                    )
                    OR EXISTS (
                        SELECT 1 FROM [app].[FuelCardImports] i
                        WHERE i.[InvalidRows]=0 AND i.[Provider]=1
                          AND i.[UsageFrom] IS NULL AND i.[UsageTo] IS NULL
                          AND i.[ReportMonth]=DATEFROMPARTS(YEAR(d.[Day]), MONTH(d.[Day]), 1)
                          AND CONVERT(date,@from) <= i.[ReportMonth]
                          AND CONVERT(date,@to) >= EOMONTH(i.[ReportMonth])
                          AND (i.[ReportThroughAtUtc] IS NULL
                               OR CONVERT(date, DATEADD(hour, 3, i.[ReportThroughAtUtc]))
                                  = EOMONTH(i.[ReportMonth]))
                    )
                ),
                coverage AS (
                    SELECT COUNT_BIG(*) AS CoveredDays,
                           MIN([Day]) AS CoveredFrom,
                           MAX([Day]) AS CoveredTo
                    FROM covered_days
                )
                """
            : """
                ,
                coverage AS (
                    SELECT CAST(0 AS bigint) AS CoveredDays,
                           CAST(NULL AS date) AS CoveredFrom,
                           CAST(NULL AS date) AS CoveredTo
                )
                """;
        string rangeDaysExpr = "DATEDIFF(day, CONVERT(date,@from), CONVERT(date,@to)) + 1";
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
                       MAX(u.[EmployeeId]) AS UsageEmployeeId,
                       CASE WHEN MAX(CASE WHEN u.[UsageSource]=N'daily' THEN 1 ELSE 0 END)=1
                            THEN N'daily' ELSE N'monthly' END AS UsageSource
                FROM usage_rows u
                GROUP BY u.[FuelCardId]
            ),
            filtered AS (
                SELECT fc.[Id], fc.[CardNumber], fc.[Provider], fc.[PlateNumberText], fc.[Notes],
                       COALESCE(ue.[FullNameAr], ae.[FullNameAr]) AS RiderNameAr,
                       COALESCE(ue.[IqamaNo], ae.[IqamaNo]) AS IqamaNo,
                       ua.[TotalLiters], ua.[AmountBeforeTax], ua.[VatAmount], ua.[TotalAmount], ua.[TransactionCount], ua.[FuelType],
                       ua.[UsageEmployeeId], ua.[UsageSource],
                       CASE WHEN fa.[Id] IS NOT NULL THEN 1 ELSE 0 END AS HasOpenAssignment
                FROM [app].[FuelCards] fc
                LEFT JOIN [app].[Sponsors] s ON s.[Id]=fc.[SponsorId] AND s.[IsDeleted]=0
                INNER JOIN usage_agg ua ON ua.[FuelCardId]=fc.[Id]
                LEFT JOIN [app].[Employees] ue ON ue.[Id]=ua.[UsageEmployeeId] AND ue.[IsDeleted]=0
                OUTER APPLY (
                    SELECT TOP 1 a.[Id], a.[EmployeeId]
                    FROM [app].[FuelCardRiderAssignments] a
                    WHERE a.[FuelCardId]=fc.[Id]
                      AND a.[EffectiveFrom] <= CONVERT(date,@to)
                      AND (a.[EffectiveTo] IS NULL OR a.[EffectiveTo] >= CONVERT(date,@from))
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
                        FROM usage_rows u2
                        INNER JOIN [app].[FuelCards] fc2 ON fc2.[Id]=u2.[FuelCardId] AND fc2.[IsDeleted]=0
                        LEFT JOIN [app].[Sponsors] s2 ON s2.[Id]=fc2.[SponsorId] AND s2.[IsDeleted]=0
                        WHERE u2.[EmployeeId] IS NOT NULL AND u2.[TotalLiters] IS NOT NULL AND u2.[TotalLiters]>0
                          {scopeSql.Replace("s.[", "s2.[", StringComparison.Ordinal)}) AS ConsumingRiders,
                       ISNULL(SUM(TotalLiters),0) AS TotalLiters,
                       ISNULL(SUM(TotalAmount),0) AS TotalInclVat
                FROM filtered
            ){coverageCte}
            SELECT f.*, a.TotalCount, a.ActiveCards, a.ConsumingRiders, a.TotalLiters AS SumLiters, a.TotalInclVat,
                   c.CoveredFrom, c.CoveredTo, c.CoveredDays,
                   CAST({rangeDaysExpr} AS bigint) - ISNULL(c.CoveredDays,0) AS UncoveredDays
            FROM filtered f CROSS JOIN agg a CROSS JOIN coverage c
            ORDER BY f.Provider, f.CardNumber
            """;
    }

    static void AddFuelRange(DbCommand cmd, DateOnly from, DateOnly to)
    {
        Add(cmd, "@from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
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

    /// <summary>Omar dc4c756: FuelCardImports.UsageFrom/UsageTo. Absent → coverage fields stay empty (legacy).</summary>
    static bool HasFuelImportCoverageColumns(DbConnection source, CancellationToken ct = default)
    {
        using var cmd = source.CreateCommand();
        using var cancel = SyncCancellation.Register(cmd, ct);
        cmd.CommandText = """
            SELECT CASE WHEN COL_LENGTH('app.FuelCardImports','UsageFrom') IS NULL
                          OR COL_LENGTH('app.FuelCardImports','UsageTo') IS NULL
                        THEN 0 ELSE 1 END
            """;
        object? value = cmd.ExecuteScalar();
        return value is not null and not DBNull && Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
    }

    public static PageResult FuelCardsExport(DbConnection source, DateOnly from, DateOnly to, string provider, string search,
        LiveOperationsQuery.Scope scope, int timeoutSeconds, CancellationToken ct = default)
    {
        EnsureSql(source);
        if (to < from) throw new ImportFailure("invalid_period");
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = timeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        AddFuelRange(cmd, from, to);
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        bool providerFilter = provider.Length > 0;
        if (providerFilter) Add(cmd, "@provider", provider);
        if (search.Length > 0) Add(cmd, "@q", "%" + EscapeLike(search) + "%");
        bool preferDaily = HasFuelUsageDateColumn(source, ct);
        bool importCoverage = HasFuelImportCoverageColumns(source, ct);
        cmd.CommandText = FuelCardsSql(scopeSql, providerFilter, search.Length > 0, preferDaily, importCoverage);
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
                    summary = MapFuelSummary(reader);
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
        string usageSource = Str(r, "UsageSource");
        if (string.IsNullOrWhiteSpace(usageSource)) usageSource = "monthly";
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
            ["usageSource"] = usageSource,
            ["coveredFrom"] = DateStr(r, "CoveredFrom"),
            ["coveredTo"] = DateStr(r, "CoveredTo"),
            ["cap"] = null,
            ["capSource"] = NoSourceYet,
            ["status"] = FuelStatusLabel(Long(r, "HasOpenAssignment") == 1 ? "active" : "inactive")
        };
    }

    static JsonObject MapFuelSummary(IDataRecord reader) => new()
    {
        ["activeCards"] = Long(reader, "ActiveCards"),
        ["consumingRiders"] = Long(reader, "ConsumingRiders"),
        ["totalLiters"] = Decimal(reader, "SumLiters"),
        ["totalInclVat"] = Decimal(reader, "TotalInclVat"),
        ["capExceeded"] = null,
        ["capExceededSource"] = NoSourceYet,
        ["coveredFrom"] = DateStr(reader, "CoveredFrom"),
        ["coveredTo"] = DateStr(reader, "CoveredTo"),
        ["coveredDays"] = Long(reader, "CoveredDays"),
        ["uncoveredDays"] = Long(reader, "UncoveredDays")
    };

    static string DateStr(IDataRecord r, string col)
    {
        try
        {
            int i = r.GetOrdinal(col);
            if (r.IsDBNull(i)) return "";
            object v = r.GetValue(i);
            return v switch
            {
                DateTime dt => DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
            };
        }
        catch (IndexOutOfRangeException) { return ""; }
        catch (ArgumentException) { return ""; }
    }

    static JsonObject EmptyFuelSummary() => new()
    {
        ["activeCards"] = 0L,
        ["consumingRiders"] = 0L,
        ["totalLiters"] = 0m,
        ["totalInclVat"] = 0m,
        ["capExceeded"] = null,
        ["capExceededSource"] = NoSourceYet,
        ["coveredFrom"] = "",
        ["coveredTo"] = "",
        ["coveredDays"] = 0L,
        ["uncoveredDays"] = 0L
    };
}
