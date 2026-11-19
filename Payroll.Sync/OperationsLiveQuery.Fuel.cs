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
        cmd.CommandText = FuelCardsSql(scopeSql, providerFilter, search.Length > 0)
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
            summary = FuelSummaryOnly(source, month, provider, search, scope, ct);
        return new(items, total, summary, DateTimeOffset.UtcNow);
    }

    static JsonObject FuelSummaryOnly(DbConnection source, DateOnly month, string provider, string search,
        LiveOperationsQuery.Scope scope, CancellationToken ct)
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
        cmd.CommandText = FuelCardsSql(scopeSql, providerFilter, search.Length > 0)
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
        LiveOperationsQuery.Scope scope, CancellationToken ct = default)
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
        Add(cmd, "@skip", skip);
        Add(cmd, "@take", pageSize);
        cmd.CommandText = $"""
            WITH filtered AS (
                SELECT a.[Id], a.[EffectiveFrom], a.[EffectiveTo], a.[AssignmentReason], a.[EndReason],
                       fc.[CardNumber], fc.[Provider], fc.[PlateNumberText],
                       e.[FullNameAr] AS RiderNameAr, e.[IqamaNo]
                FROM [app].[FuelCardRiderAssignments] a
                INNER JOIN [app].[FuelCards] fc ON fc.[Id]=a.[FuelCardId] AND fc.[IsDeleted]=0
                LEFT JOIN [app].[Sponsors] s ON s.[Id]=fc.[SponsorId] AND s.[IsDeleted]=0
                LEFT JOIN [app].[Employees] e ON e.[Id]=a.[EmployeeId] AND e.[IsDeleted]=0
                WHERE CONVERT(date, a.[EffectiveFrom]) <= CONVERT(date, @to)
                  AND (a.[EffectiveTo] IS NULL OR CONVERT(date, a.[EffectiveTo]) >= CONVERT(date, @from))
                  {scopeSql}
            ),
            agg AS (SELECT COUNT_BIG(*) AS TotalCount FROM filtered)
            SELECT f.*, a.TotalCount
            FROM filtered f CROSS JOIN agg a
            ORDER BY f.EffectiveFrom DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
            """;
        long total = 0;
        var items = new List<JsonObject>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (items.Count == 0) total = Long(reader, "TotalCount");
                items.Add(new JsonObject
                {
                    ["id"] = Str(reader, "Id"),
                    ["cardNumber"] = Str(reader, "CardNumber"),
                    ["provider"] = Str(reader, "Provider"),
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

    static string FuelCardsSql(string scopeSql, bool providerFilter, bool search = false)
    {
        string providerPred = providerFilter ? " AND fc.[Provider]=@provider" : "";
        string searchPred = search ? " AND (fc.[CardNumber] LIKE @q OR fc.[PlateNumberText] LIKE @q OR e.[FullNameAr] LIKE @q OR e.[IqamaNo] LIKE @q)" : "";
        return $"""
            WITH filtered AS (
                SELECT fc.[Id], fc.[CardNumber], fc.[Provider], fc.[PlateNumberText],
                       e.[FullNameAr] AS RiderNameAr, e.[IqamaNo],
                       u.[TotalLiters], u.[AmountBeforeTax], u.[VatAmount], u.[TotalAmount], u.[TransactionCount], u.[FuelType],
                       CASE WHEN fa.[Id] IS NOT NULL THEN 1 ELSE 0 END AS HasOpenAssignment
                FROM [app].[FuelCards] fc
                LEFT JOIN [app].[Sponsors] s ON s.[Id]=fc.[SponsorId] AND s.[IsDeleted]=0
                LEFT JOIN [app].[FuelCardRiderAssignments] fa ON fa.[FuelCardId]=fc.[Id] AND fa.[EffectiveTo] IS NULL
                LEFT JOIN [app].[Employees] e ON e.[Id]=fa.[EmployeeId] AND e.[IsDeleted]=0
                LEFT JOIN [app].[FuelCardMonthlyUsages] u ON u.[FuelCardId]=fc.[Id] AND u.[IsDeleted]=0
                     AND u.[ReportMonth]=@month
                WHERE fc.[IsDeleted]=0{scopeSql}{providerPred}{searchPred}
            ),
            agg AS (
                SELECT COUNT_BIG(*) AS TotalCount,
                       SUM(CASE WHEN HasOpenAssignment=1 THEN 1 ELSE 0 END) AS ActiveCards,
                       COUNT_BIG(DISTINCT CASE WHEN TotalLiters IS NOT NULL AND TotalLiters>0 THEN IqamaNo END) AS ConsumingRiders,
                       ISNULL(SUM(TotalLiters),0) AS TotalLiters,
                       ISNULL(SUM(TotalAmount),0) AS TotalInclVat
                FROM filtered
            )
            SELECT f.*, a.TotalCount, a.ActiveCards, a.ConsumingRiders, a.TotalLiters AS SumLiters, a.TotalInclVat
            FROM filtered f CROSS JOIN agg a
            ORDER BY f.Provider, f.CardNumber
            """;
    }

    static JsonObject MapFuelCardRow(IDataRecord r) => new()
    {
        ["id"] = Str(r, "Id"),
        ["cardNumber"] = Str(r, "CardNumber"),
        ["provider"] = Str(r, "Provider"),
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
        ["status"] = Long(r, "HasOpenAssignment") == 1 ? "active" : "inactive"
    };

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
