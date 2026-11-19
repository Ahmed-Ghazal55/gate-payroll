using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace ErpImport;

/// <summary>
/// Live read-only company statistics from Omar's ERP in ONE SQL statement:
/// sponsorships per sponsor and platform accounts registered under each sponsor.
/// Identifiers are allowlisted; company scope is applied in SQL with parameters.
/// </summary>
public static class CompanyStatsLiveQuery
{
    public const int CommandTimeoutSeconds = 15;

    public sealed record Result(IReadOnlyList<JsonObject> Rows, DateTimeOffset AsOf);

    /// <summary>SQL text and parameter names for tests (company scope embedded in SQL).</summary>
    public static (string Sql, IReadOnlyList<string> ParameterNames) PreviewSql(LiveOperationsQuery.Scope? scope = null)
    {
        using var cmd = new SqlCommand();
        string sql = BuildSql(cmd, scope);
        var names = cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).ToList();
        return (sql, names);
    }

    public static Result Read(DbConnection source, LiveOperationsQuery.Scope scope, CancellationToken cancellationToken = default)
    {
        if (source is not SqlConnection) throw new ImportFailure("live_ops_requires_sql");
        DateTimeOffset asOf = DateTimeOffset.UtcNow;
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, cancellationToken);
        cmd.CommandText = BuildSql(cmd, scope);
        var rows = new List<JsonObject>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(MapRow(reader));
        }
        return new(rows, asOf);
    }

    static string BuildSql(DbCommand cmd, LiveOperationsQuery.Scope? scope)
    {
        string sponsorScope = BuildSponsorScopeSql(scope, cmd);
        return $"""
            WITH scoped_sponsors AS (
                SELECT s.[Id], s.[RegistryNameAr], s.[UnifiedNationalNumber], s.[CommercialRegistrationNumber]
                FROM [app].[Sponsors] s
                WHERE s.[IsDeleted]=0{sponsorScope}
            ),
            emp_agg AS (
                SELECT e.[SponsorId],
                       COUNT_BIG(*) AS SponsoredTotal,
                       SUM(CASE WHEN e.[IsEmployee]=0 THEN 1 ELSE 0 END) AS SponsoredRiders,
                       SUM(CASE WHEN e.[IsEmployee]=1 THEN 1 ELSE 0 END) AS SponsoredAdmins,
                       SUM(CASE WHEN e.[Status]=3 THEN 1 ELSE 0 END) AS SponsoredActive,
                       SUM(CASE WHEN e.[Status]=5 THEN 1 ELSE 0 END) AS SponsoredOnLeave
                FROM [app].[Employees] e
                INNER JOIN scoped_sponsors ss ON ss.[Id]=e.[SponsorId]
                WHERE e.[IsDeleted]=0 AND e.[EngagementType]=1
                GROUP BY e.[SponsorId]
            ),
            acct_base AS (
                SELECT pa.[Id], pa.[SponsorId], pa.[Status],
                       cp.[Code] AS PlatformCode, cp.[NameAr] AS PlatformNameAr,
                       CASE WHEN EXISTS (
                           SELECT 1 FROM [app].[RiderClientAssignments] rca
                           WHERE rca.[PlatformRiderAccountId]=pa.[Id] AND rca.[IsDeleted]=0 AND rca.[EffectiveTo] IS NULL
                       ) THEN 1 ELSE 0 END AS HasOpenAssignment
                FROM [app].[PlatformRiderAccounts] pa
                INNER JOIN scoped_sponsors ss ON ss.[Id]=pa.[SponsorId]
                INNER JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=pa.[ClientPlatformId] AND cp.[IsDeleted]=0
                WHERE pa.[IsDeleted]=0
            ),
            acct_agg AS (
                SELECT a.[SponsorId],
                       COUNT_BIG(*) AS PlatformAccountsTotal,
                       SUM(CASE WHEN a.[Status] IN (1, 2) THEN 1 ELSE 0 END) AS PlatformAccountsActive,
                       SUM(CASE WHEN a.[Status]=2 OR a.[HasOpenAssignment]=1 THEN 1 ELSE 0 END) AS PlatformAccountsAssigned,
                       (SELECT a2.[PlatformCode] AS code,
                               MAX(a2.[PlatformNameAr]) AS nameAr,
                               COUNT_BIG(*) AS total,
                               SUM(CASE WHEN a2.[Status] IN (1, 2) THEN 1 ELSE 0 END) AS active,
                               SUM(CASE WHEN a2.[Status]=2 OR a2.[HasOpenAssignment]=1 THEN 1 ELSE 0 END) AS assigned
                        FROM acct_base a2
                        WHERE a2.[SponsorId]=a.[SponsorId]
                        GROUP BY a2.[PlatformCode]
                        ORDER BY a2.[PlatformCode]
                        FOR JSON PATH) AS ByPlatformJson
                FROM acct_base a
                GROUP BY a.[SponsorId]
            )
            SELECT ss.[Id] AS ErpSponsorId,
                   ss.[RegistryNameAr] AS SponsorNameAr,
                   ss.[UnifiedNationalNumber] AS UnifiedNumber,
                   ss.[CommercialRegistrationNumber] AS CommercialReg,
                   COALESCE(ea.[SponsoredTotal], 0) AS SponsoredTotal,
                   COALESCE(ea.[SponsoredRiders], 0) AS SponsoredRiders,
                   COALESCE(ea.[SponsoredAdmins], 0) AS SponsoredAdmins,
                   COALESCE(ea.[SponsoredActive], 0) AS SponsoredActive,
                   COALESCE(ea.[SponsoredOnLeave], 0) AS SponsoredOnLeave,
                   COALESCE(aa.[PlatformAccountsTotal], 0) AS PlatformAccountsTotal,
                   COALESCE(aa.[PlatformAccountsActive], 0) AS PlatformAccountsActive,
                   COALESCE(aa.[PlatformAccountsAssigned], 0) AS PlatformAccountsAssigned,
                   aa.[ByPlatformJson]
            FROM scoped_sponsors ss
            LEFT JOIN emp_agg ea ON ea.[SponsorId]=ss.[Id]
            LEFT JOIN acct_agg aa ON aa.[SponsorId]=ss.[Id]
            ORDER BY ss.[RegistryNameAr]
            """;
    }

    static string BuildSponsorScopeSql(LiveOperationsQuery.Scope? scope, DbCommand cmd)
    {
        if (scope == null || scope.AllCompanies) return "";
        if (scope.SponsorUnifiedNumbers.Count == 0) return " AND 1=0";
        string regs = InList(cmd, "@cr", scope.SponsorUnifiedNumbers);
        return $" AND s.[UnifiedNationalNumber] IN ({regs})";
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

    static void Add(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    static JsonObject MapRow(IDataRecord r)
    {
        return new JsonObject
        {
            ["erpSponsorId"] = Str(r, "ErpSponsorId"),
            ["sponsorNameAr"] = Str(r, "SponsorNameAr"),
            ["unifiedNumber"] = Str(r, "UnifiedNumber"),
            ["commercialReg"] = Str(r, "CommercialReg"),
            ["sponsoredTotal"] = Num(r, "SponsoredTotal"),
            ["sponsoredRiders"] = Num(r, "SponsoredRiders"),
            ["sponsoredAdmins"] = Num(r, "SponsoredAdmins"),
            ["sponsoredActive"] = Num(r, "SponsoredActive"),
            ["sponsoredOnLeave"] = Num(r, "SponsoredOnLeave"),
            ["platformAccountsTotal"] = Num(r, "PlatformAccountsTotal"),
            ["platformAccountsActive"] = Num(r, "PlatformAccountsActive"),
            ["platformAccountsAssigned"] = Num(r, "PlatformAccountsAssigned"),
            ["byPlatform"] = ParseByPlatform(r)
        };
    }

    static JsonArray ParseByPlatform(IDataRecord r)
    {
        try
        {
            int ord = r.GetOrdinal("ByPlatformJson");
            if (r.IsDBNull(ord)) return new JsonArray();
            string json = Convert.ToString(r.GetValue(ord), CultureInfo.InvariantCulture) ?? "";
            if (string.IsNullOrWhiteSpace(json)) return new JsonArray();
            return JsonNode.Parse(json) as JsonArray ?? new JsonArray();
        }
        catch (IndexOutOfRangeException) { return new JsonArray(); }
        catch (JsonException) { return new JsonArray(); }
    }

    static string Str(IDataRecord r, string name)
    {
        int ord = r.GetOrdinal(name);
        if (r.IsDBNull(ord)) return "";
        return Convert.ToString(r.GetValue(ord), CultureInfo.InvariantCulture)?.Trim() ?? "";
    }

    static long Num(IDataRecord r, string name)
    {
        int ord = r.GetOrdinal(name);
        if (r.IsDBNull(ord)) return 0L;
        return Convert.ToInt64(r.GetValue(ord), CultureInfo.InvariantCulture);
    }
}
