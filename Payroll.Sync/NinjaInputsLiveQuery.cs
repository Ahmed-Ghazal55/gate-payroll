using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace ErpImport;

/// <summary>
/// Live read-only Ninja pay-run input facts from Omar's ERP (L1).
/// Facts only — no pay calculation. Company/platform scope and paging in SQL.
/// </summary>
public static class NinjaInputsLiveQuery
{
    public const int CommandTimeoutSeconds = 15;
    public const int MaxPageSize = LiveOperationsQuery.MaxPageSize;
    public const string PlatformCode = "NINJA";

    public sealed record PageResult(
        IReadOnlyList<JsonObject> Items,
        long Total,
        JsonObject Coverage,
        DateTimeOffset AsOf);

    public sealed record PeriodRequest(DateOnly From, DateOnly To, int Page, int PageSize);

    public static PeriodRequest ParsePeriod(IReadOnlyDictionary<string, string> query)
    {
        string fromRaw = query.TryGetValue("from", out var f) ? f.Trim() : "";
        string toRaw = query.TryGetValue("to", out var t) ? t.Trim() : "";
        if (fromRaw.Length == 0 || toRaw.Length == 0)
            throw new ImportFailure("period_required");
        if (!DateOnly.TryParse(fromRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
            !DateOnly.TryParse(toRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to) ||
            to < from)
            throw new ImportFailure("invalid_period");
        int page = Positive(query, "page", 1);
        int pageSize = Positive(query, "pageSize", 50);
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return new(from, to, page, pageSize);
    }

    public static (int Page, int PageSize) ParsePage(IReadOnlyDictionary<string, string> query)
    {
        int page = Positive(query, "page", 1);
        int pageSize = Positive(query, "pageSize", 50);
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize);
    }

    static int Positive(IReadOnlyDictionary<string, string> query, string key, int fallback)
    {
        if (!query.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return fallback;
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 1)
            throw new ImportFailure("invalid_page");
        return value;
    }

    /// <summary>Reader 1 — daily performance per Ninja account and day, plus period coverage.</summary>
    public static (string Sql, IReadOnlyList<string> ParameterNames) PreviewDailyPerformanceSql(
        LiveOperationsQuery.Scope? scope, PeriodRequest request)
    {
        using var cmd = new SqlCommand();
        string sql = BuildDailyPerformanceSql(cmd, scope, request);
        return (sql, cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).ToList());
    }

    public static PageResult DailyPerformance(
        DbConnection source,
        LiveOperationsQuery.Scope scope,
        PeriodRequest request,
        CancellationToken cancellationToken = default)
    {
        if (source is not SqlConnection) throw new ImportFailure("live_ops_requires_sql");
        DateTimeOffset asOf = DateTimeOffset.UtcNow;
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, cancellationToken);
        cmd.CommandText = BuildDailyPerformanceSql(cmd, scope, request);
        var items = new List<JsonObject>();
        long total = 0;
        string? firstDay = null, lastDay = null;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (total == 0)
            {
                total = Num(reader, "CoverageTotal");
                firstDay = DateStr(reader, "CoverageFirstDay");
                lastDay = DateStr(reader, "CoverageLastDay");
            }
            items.Add(MapDailyPerformanceRow(reader));
        }
        return new(items, total, Coverage(request, firstDay, lastDay, total), asOf);
    }

    static string BuildDailyPerformanceSql(DbCommand cmd, LiveOperationsQuery.Scope? scope, PeriodRequest request)
    {
        Add(cmd, "@from", request.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@to", request.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@skip", (request.Page - 1) * request.PageSize);
        Add(cmd, "@take", request.PageSize);
        Add(cmd, "@ninja", PlatformCode);
        string sponsorScope = BuildSponsorScopeSql(scope, cmd, "s");
        return $"""
            WITH scoped AS (
                SELECT d.[Id], d.[ReportDate], d.[CaptainId], d.[PlatformRiderAccountId],
                       d.[RiderClientAssignmentId], d.[RiderProfileId], d.[SourceId], d.[SourceRowNumber],
                       d.[TotalOrders], d.[TotalWorkingHours],
                       pa.[ExternalAccountId], pa.[Code] AS AccountCode,
                       s.[UnifiedNationalNumber] AS OwnerUnifiedNumber
                FROM [ninja].[NinjaDailyPerformance] d
                INNER JOIN [app].[PlatformRiderAccounts] pa ON pa.[Id]=d.[PlatformRiderAccountId] AND pa.[IsDeleted]=0
                INNER JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=pa.[ClientPlatformId] AND cp.[IsDeleted]=0 AND cp.[Code]=@ninja
                INNER JOIN [app].[Sponsors] s ON s.[Id]=pa.[SponsorId] AND s.[IsDeleted]=0
                WHERE d.[ReportDate]>=@from AND d.[ReportDate]<=@to{sponsorScope}
            ),
            coverage AS (
                SELECT COUNT_BIG(*) AS CoverageTotal,
                       MIN([ReportDate]) AS CoverageFirstDay,
                       MAX([ReportDate]) AS CoverageLastDay
                FROM scoped
            ),
            page AS (
                SELECT * FROM scoped
                ORDER BY [ReportDate], [CaptainId], [Id]
                OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
            )
            SELECT p.*, c.[CoverageTotal], c.[CoverageFirstDay], c.[CoverageLastDay]
            FROM page p
            CROSS JOIN coverage c
            ORDER BY p.[ReportDate], p.[CaptainId], p.[Id]
            """;
    }

    static JsonObject MapDailyPerformanceRow(IDataRecord r) => new()
    {
        ["id"] = Str(r, "Id"),
        ["reportDate"] = DateStr(r, "ReportDate"),
        ["captainId"] = Str(r, "CaptainId"),
        ["platformRiderAccountId"] = Str(r, "PlatformRiderAccountId"),
        ["externalAccountId"] = Str(r, "ExternalAccountId"),
        ["accountCode"] = Str(r, "AccountCode"),
        ["riderProfileId"] = Str(r, "RiderProfileId"),
        ["riderClientAssignmentId"] = Str(r, "RiderClientAssignmentId"),
        ["sourceId"] = Str(r, "SourceId"),
        ["sourceRowNumber"] = Num(r, "SourceRowNumber"),
        ["orders"] = Dec(r, "TotalOrders"),
        ["hours"] = Dec(r, "TotalWorkingHours"),
        ["ownerUnifiedNumber"] = Str(r, "OwnerUnifiedNumber")
    };

    /// <summary>Reader 2 — daily deductions keyed by actual rider of that day.</summary>
    public static (string Sql, IReadOnlyList<string> ParameterNames) PreviewDailyDeductionsSql(
        LiveOperationsQuery.Scope? scope, PeriodRequest request)
    {
        using var cmd = new SqlCommand();
        string sql = BuildDailyDeductionsSql(cmd, scope, request);
        return (sql, cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).ToList());
    }

    public static PageResult DailyDeductions(
        DbConnection source,
        LiveOperationsQuery.Scope scope,
        PeriodRequest request,
        CancellationToken cancellationToken = default)
    {
        if (source is not SqlConnection) throw new ImportFailure("live_ops_requires_sql");
        DateTimeOffset asOf = DateTimeOffset.UtcNow;
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, cancellationToken);
        cmd.CommandText = BuildDailyDeductionsSql(cmd, scope, request);
        var items = new List<JsonObject>();
        long total = 0;
        string? firstDay = null, lastDay = null;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (total == 0)
            {
                total = Num(reader, "CoverageTotal");
                firstDay = DateStr(reader, "CoverageFirstDay");
                lastDay = DateStr(reader, "CoverageLastDay");
            }
            items.Add(MapDailyDeductionRow(reader));
        }
        return new(items, total, Coverage(request, firstDay, lastDay, total), asOf);
    }

    static string BuildDailyDeductionsSql(DbCommand cmd, LiveOperationsQuery.Scope? scope, PeriodRequest request)
    {
        Add(cmd, "@from", request.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@to", request.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@skip", (request.Page - 1) * request.PageSize);
        Add(cmd, "@take", request.PageSize);
        Add(cmd, "@ninja", PlatformCode);
        string sponsorScope = BuildSponsorScopeSql(scope, cmd, "s");
        return $"""
            WITH scoped AS (
                SELECT d.[Id], d.[ReportDate], d.[CaptainId], d.[PlatformRiderAccountId],
                       d.[RiderClientAssignmentId], d.[RiderProfileId], d.[SourceId], d.[SourceRowNumber],
                       d.[TotalCollectedAmountAtDelivery], d.[TotalDeductions],
                       pa.[ExternalAccountId], pa.[Code] AS AccountCode,
                       s.[UnifiedNationalNumber] AS OwnerUnifiedNumber
                FROM [ninja].[NinjaDailyDeductions] d
                INNER JOIN [app].[PlatformRiderAccounts] pa ON pa.[Id]=d.[PlatformRiderAccountId] AND pa.[IsDeleted]=0
                INNER JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=pa.[ClientPlatformId] AND cp.[IsDeleted]=0 AND cp.[Code]=@ninja
                INNER JOIN [app].[Sponsors] s ON s.[Id]=pa.[SponsorId] AND s.[IsDeleted]=0
                WHERE d.[ReportDate]>=@from AND d.[ReportDate]<=@to{sponsorScope}
            ),
            coverage AS (
                SELECT COUNT_BIG(*) AS CoverageTotal,
                       MIN([ReportDate]) AS CoverageFirstDay,
                       MAX([ReportDate]) AS CoverageLastDay
                FROM scoped
            ),
            page AS (
                SELECT * FROM scoped
                ORDER BY [ReportDate], [CaptainId], [Id]
                OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
            )
            SELECT p.*, c.[CoverageTotal], c.[CoverageFirstDay], c.[CoverageLastDay]
            FROM page p
            CROSS JOIN coverage c
            ORDER BY p.[ReportDate], p.[CaptainId], p.[Id]
            """;
    }

    static JsonObject MapDailyDeductionRow(IDataRecord r) => new()
    {
        ["id"] = Str(r, "Id"),
        ["reportDate"] = DateStr(r, "ReportDate"),
        ["captainId"] = Str(r, "CaptainId"),
        ["platformRiderAccountId"] = Str(r, "PlatformRiderAccountId"),
        ["externalAccountId"] = Str(r, "ExternalAccountId"),
        ["accountCode"] = Str(r, "AccountCode"),
        ["actualRiderProfileId"] = Str(r, "RiderProfileId"),
        ["riderClientAssignmentId"] = Str(r, "RiderClientAssignmentId"),
        ["sourceId"] = Str(r, "SourceId"),
        ["sourceRowNumber"] = Num(r, "SourceRowNumber"),
        ["collectedAmount"] = Dec(r, "TotalCollectedAmountAtDelivery"),
        ["deductionsAmount"] = Dec(r, "TotalDeductions"),
        ["ownerUnifiedNumber"] = Str(r, "OwnerUnifiedNumber")
    };

    /// <summary>Reader 3 — payment method / pay scheme per Ninja account (Omar PaymentModel).</summary>
    public static (string Sql, IReadOnlyList<string> ParameterNames) PreviewPaymentMethodsSql(
        LiveOperationsQuery.Scope? scope, int page, int pageSize)
    {
        using var cmd = new SqlCommand();
        string sql = BuildPaymentMethodsSql(cmd, scope, page, pageSize);
        return (sql, cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).ToList());
    }

    public static PageResult PaymentMethods(
        DbConnection source,
        LiveOperationsQuery.Scope scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (source is not SqlConnection) throw new ImportFailure("live_ops_requires_sql");
        DateTimeOffset asOf = DateTimeOffset.UtcNow;
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, cancellationToken);
        cmd.CommandText = BuildPaymentMethodsSql(cmd, scope, page, pageSize);
        var items = new List<JsonObject>();
        long total = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (total == 0) total = Num(reader, "CoverageTotal");
            items.Add(MapPaymentMethodRow(reader));
        }
        return new(items, total, new JsonObject { ["total"] = total }, asOf);
    }

    static string BuildPaymentMethodsSql(DbCommand cmd, LiveOperationsQuery.Scope? scope, int page, int pageSize)
    {
        Add(cmd, "@skip", (page - 1) * pageSize);
        Add(cmd, "@take", pageSize);
        Add(cmd, "@ninja", PlatformCode);
        string sponsorScope = BuildSponsorScopeSql(scope, cmd, "s");
        return $"""
            WITH scoped AS (
                SELECT pa.[Id], pa.[ExternalAccountId], pa.[Code] AS AccountCode, pa.[Status],
                       pa.[PaymentModel] AS AccountPaymentModel,
                       pa.[RegisteredEmployeeId] AS OwnerEmployeeId,
                       s.[UnifiedNationalNumber] AS OwnerUnifiedNumber,
                       open_a.[Id] AS OpenAssignmentId,
                       open_a.[RiderProfileId] AS OpenActualRiderProfileId,
                       open_a.[PaymentModel] AS AssignmentPaymentModel,
                       open_a.[NinjaRiderType],
                       open_a.[EffectiveFrom] AS OpenEffectiveFrom,
                       open_a.[EffectiveTo] AS OpenEffectiveTo
                FROM [app].[PlatformRiderAccounts] pa
                INNER JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=pa.[ClientPlatformId] AND cp.[IsDeleted]=0 AND cp.[Code]=@ninja
                INNER JOIN [app].[Sponsors] s ON s.[Id]=pa.[SponsorId] AND s.[IsDeleted]=0
                OUTER APPLY (
                    SELECT TOP 1 a.[Id], a.[RiderProfileId], a.[PaymentModel], a.[NinjaRiderType], a.[EffectiveFrom], a.[EffectiveTo]
                    FROM [app].[RiderClientAssignments] a
                    WHERE a.[PlatformRiderAccountId]=pa.[Id] AND a.[IsDeleted]=0 AND a.[EffectiveTo] IS NULL
                    ORDER BY a.[EffectiveFrom] DESC
                ) open_a
                WHERE pa.[IsDeleted]=0{sponsorScope}
            ),
            coverage AS (SELECT COUNT_BIG(*) AS CoverageTotal FROM scoped),
            page AS (
                SELECT * FROM scoped
                ORDER BY [ExternalAccountId], [Id]
                OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
            )
            SELECT p.*, c.[CoverageTotal]
            FROM page p
            CROSS JOIN coverage c
            ORDER BY p.[ExternalAccountId], p.[Id]
            """;
    }

    static JsonObject MapPaymentMethodRow(IDataRecord r)
    {
        string accountModel = PaymentModelName(Str(r, "AccountPaymentModel"));
        string assignmentModel = PaymentModelName(Str(r, "AssignmentPaymentModel"));
        string scheme = assignmentModel.Length > 0 ? assignmentModel : accountModel;
        return new()
        {
            ["platformRiderAccountId"] = Str(r, "Id"),
            ["externalAccountId"] = Str(r, "ExternalAccountId"),
            ["accountCode"] = Str(r, "AccountCode"),
            ["accountStatus"] = Str(r, "Status"),
            ["accountPaymentModel"] = accountModel,
            ["assignmentPaymentModel"] = assignmentModel,
            ["payScheme"] = scheme,
            ["ownerEmployeeId"] = Str(r, "OwnerEmployeeId"),
            ["ownerUnifiedNumber"] = Str(r, "OwnerUnifiedNumber"),
            ["openAssignmentId"] = Str(r, "OpenAssignmentId"),
            ["openActualRiderProfileId"] = Str(r, "OpenActualRiderProfileId"),
            ["ninjaRiderType"] = Str(r, "NinjaRiderType"),
            ["openEffectiveFrom"] = DateStr(r, "OpenEffectiveFrom"),
            ["openEffectiveTo"] = DateStr(r, "OpenEffectiveTo")
        };
    }

    public static JsonObject Coverage(PeriodRequest request, string? firstDay, string? lastDay, long total) => new()
    {
        ["requestedFrom"] = request.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["requestedTo"] = request.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["firstDay"] = firstDay ?? "",
        ["lastDay"] = lastDay ?? "",
        ["total"] = total,
        ["hasGapAtPeriodEnd"] = !string.IsNullOrEmpty(lastDay) &&
            string.CompareOrdinal(lastDay, request.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) < 0
    };

    internal static string BuildSponsorScopeSql(LiveOperationsQuery.Scope? scope, DbCommand cmd, string sponsorAlias)
    {
        if (scope == null || scope.AllCompanies) return "";
        if (scope.SponsorUnifiedNumbers.Count == 0) return " AND 1=0";
        string regs = InList(cmd, "@cr", scope.SponsorUnifiedNumbers);
        return $" AND {sponsorAlias}.[UnifiedNationalNumber] IN ({regs})";
    }

    internal static string InList(DbCommand cmd, string prefix, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            Add(cmd, prefix + "0", Guid.Empty.ToString("D"));
            return prefix + "0";
        }
        if (values.Count > LiveOperationsQuery.MaxInlineParameters)
        {
            Add(cmd, prefix + "json", JsonSerializer.Serialize(values));
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

    internal static void Add(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    internal static string PaymentModelName(string code) => code switch
    {
        "1" => "PayPerOrder",
        "2" => "Salary",
        "PayPerOrder" => "PayPerOrder",
        "Salary" => "Salary",
        _ => code
    };

    internal static string Str(IDataRecord r, string name)
    {
        try
        {
            int ord = r.GetOrdinal(name);
            if (r.IsDBNull(ord)) return "";
            return Convert.ToString(r.GetValue(ord), CultureInfo.InvariantCulture)?.Trim() ?? "";
        }
        catch (IndexOutOfRangeException) { return ""; }
    }

    internal static string DateStr(IDataRecord r, string name)
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

    internal static long Num(IDataRecord r, string name)
    {
        try
        {
            int ord = r.GetOrdinal(name);
            if (r.IsDBNull(ord)) return 0L;
            return Convert.ToInt64(r.GetValue(ord), CultureInfo.InvariantCulture);
        }
        catch (IndexOutOfRangeException) { return 0L; }
    }

    internal static decimal? Dec(IDataRecord r, string name)
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
