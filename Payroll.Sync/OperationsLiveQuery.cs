using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace ErpImport;

/// <summary>
/// Live operations/fleet reads from Omar's ERP (cities, housing, vehicles period, fuel).
/// Identifiers allowlisted; values are parameters. Money posting is out of scope.
/// </summary>
public static partial class OperationsLiveQuery
{
    public const int MaxPageSize = 200, CommandTimeoutSeconds = 15, ExportCommandTimeoutSeconds = 120;
    public const string NoSourceYet = "لا يوجد مصدر بعد";
    /// <summary>Omar VehicleRiderPeriodReportService monthly rates (SAR).</summary>
    public const decimal MonthlyCostCarSar = 1800m, MonthlyCostMotorcycleSar = 800m;
    static readonly TimeSpan RiyadhOffset = TimeSpan.FromHours(3);
    static readonly TimeSpan BillingCutoff = new(14, 0, 0);

    public sealed record PageResult(IReadOnlyList<JsonObject> Items, long Total, JsonObject Summary, DateTimeOffset AsOf);
    public sealed record DetailResult(JsonObject Body, DateTimeOffset AsOf);

    public static (string Sql, IReadOnlyList<string> ParameterNames) PreviewCitiesSql(LiveOperationsQuery.Scope? scope = null)
    {
        using var cmd = new SqlCommand();
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        string platformSql = ScopePlatform(scope, cmd, "cp");
        string sql = CitiesSummarySql(scopeSql, platformSql);
        return (sql, cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).ToList());
    }

    public static (string Sql, IReadOnlyList<string> ParameterNames) PreviewHousingSql(
        string city = "", string status = "", string search = "", LiveOperationsQuery.Scope? scope = null)
    {
        using var cmd = new SqlCommand();
        string where = HousingWhere(cmd, city, status, search, scope);
        string sql = $"""
            WITH filtered AS (
                SELECT {HousingSelect()}
                FROM {HousingFrom()}
                {where}
            ),
            agg AS (
                SELECT COUNT_BIG(*) AS TotalCount,
                       SUM(RoomsCount) AS RoomsCount,
                       SUM(ResidentsTotal) AS ResidentsTotal,
                       SUM(CapacityTotal) AS CapacityTotal
                FROM filtered
            )
            SELECT f.*, a.TotalCount, a.RoomsCount, a.ResidentsTotal, a.CapacityTotal
            FROM filtered f CROSS JOIN agg a
            ORDER BY f.NameAr
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
            """;
        Add(cmd, "@skip", 0);
        Add(cmd, "@take", 50);
        return (sql, cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).ToList());
    }

    public static (string Sql, IReadOnlyList<string> ParameterNames) PreviewPeriodReportSql(
        string mode, LiveOperationsQuery.Scope? scope = null)
    {
        using var cmd = new SqlCommand();
        Add(cmd, "@from", "2026-11-01");
        Add(cmd, "@to", "2026-11-30");
        Add(cmd, "@asOf", DateTimeOffset.UtcNow);
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        string sql = (mode.Equals("rider", StringComparison.OrdinalIgnoreCase)
            ? PeriodReportRiderSql(scopeSql)
            : PeriodReportVehicleSql(scopeSql))
            + "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        Add(cmd, "@skip", 0);
        Add(cmd, "@take", 50);
        return (sql, cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).ToList());
    }

    public static (string Sql, IReadOnlyList<string> ParameterNames) PreviewFuelCardsSql(LiveOperationsQuery.Scope? scope = null)
    {
        using var cmd = new SqlCommand();
        Add(cmd, "@month", "2026-11-01");
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        string sql = FuelCardsSql(scopeSql, providerFilter: false, search: false);
        Add(cmd, "@skip", 0);
        Add(cmd, "@take", 50);
        return (sql, cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).ToList());
    }

    public static PageResult CitiesSummary(DbConnection source, LiveOperationsQuery.Scope scope, CancellationToken ct = default)
    {
        EnsureSql(source);
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        string platformSql = ScopePlatform(scope, cmd, "cp");
        cmd.CommandText = CitiesSummarySql(scopeSql, platformSql);
        var items = ReadMapped(cmd, MapCityRow, ct);
        var summary = new JsonObject
        {
            ["activeCities"] = items.Count,
            ["totalVehicles"] = items.Sum(r => NumLong(r, "vehiclesTotal")),
            ["totalRiders"] = items.Sum(r => NumLong(r, "ridersTotal")),
            ["platformAccountsActive"] = items.Sum(r => NumLong(r, "accountsActive")),
            ["platformAccountsAssigned"] = items.Sum(r => NumLong(r, "accountsAssigned"))
        };
        return new(items, items.Count, summary, DateTimeOffset.UtcNow);
    }

    public static DetailResult CityDetail(DbConnection source, string id, LiveOperationsQuery.Scope scope, CancellationToken ct = default)
    {
        EnsureSql(source);
        if (!Guid.TryParse(id, out _)) throw new ImportFailure("city_not_found");
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        string platformSql = ScopePlatform(scope, cmd, "cp");
        Add(cmd, "@cityId", id);
        cmd.CommandText = CitiesSummarySql(scopeSql, platformSql, cityFilter: true);
        var rows = ReadMapped(cmd, MapCityRow, ct);
        if (rows.Count == 0) throw new ImportFailure("city_not_found");
        var city = rows[0];
        city["byRegistration"] = CitySplitByRegistration(source, id, scope, ct);
        city["byApp"] = CitySplitByApp(source, id, scope, ct);
        return new(city, DateTimeOffset.UtcNow);
    }

    public static PageResult HousingPage(DbConnection source, int page, int pageSize, string city, string status, string search,
        LiveOperationsQuery.Scope scope, CancellationToken ct = default)
    {
        EnsureSql(source);
        pageSize = Math.Min(Math.Max(1, pageSize), MaxPageSize);
        page = Math.Max(1, page);
        int skip = checked((page - 1) * pageSize);
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        string where = HousingWhere(cmd, city, status, search, scope);
        Add(cmd, "@skip", skip);
        Add(cmd, "@take", pageSize);
        cmd.CommandText = $"""
            WITH filtered AS (
                SELECT {HousingSelect()}
                FROM {HousingFrom()}
                {where}
            ),
            agg AS (
                SELECT COUNT_BIG(*) AS TotalCount,
                       ISNULL(SUM(RoomsCount),0) AS RoomsCount,
                       ISNULL(SUM(ResidentsTotal),0) AS ResidentsTotal,
                       ISNULL(SUM(CapacityTotal),0) AS CapacityTotal
                FROM filtered
            )
            SELECT f.*, a.TotalCount, a.RoomsCount AS SumRooms, a.ResidentsTotal AS SumResidents, a.CapacityTotal AS SumCapacity
            FROM filtered f CROSS JOIN agg a
            ORDER BY f.NameAr
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
            """;
        long total = 0;
        var summary = EmptyHousingSummary();
        var items = new List<JsonObject>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (items.Count == 0)
                {
                    total = Long(reader, "TotalCount");
                    long rooms = Long(reader, "SumRooms");
                    long residents = Long(reader, "SumResidents");
                    long capacity = Long(reader, "SumCapacity");
                    summary = new JsonObject
                    {
                        ["housingsTotal"] = total,
                        ["roomsTotal"] = rooms,
                        ["residentsTotal"] = residents,
                        ["occupancyPercent"] = capacity <= 0 ? 0 : Math.Round(100.0 * residents / capacity, 1)
                    };
                }
                items.Add(MapHousingRow(reader));
            }
        }
        if (items.Count == 0 && total == 0)
            summary = HousingSummaryOnly(source, city, status, search, scope, ct);
        return new(items, total, summary, DateTimeOffset.UtcNow);
    }

    public static PageResult HousingExport(DbConnection source, string city, string status, string search,
        LiveOperationsQuery.Scope scope, int timeoutSeconds, CancellationToken ct = default)
    {
        EnsureSql(source);
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = timeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        string where = HousingWhere(cmd, city, status, search, scope);
        cmd.CommandText = $"""
            WITH filtered AS (
                SELECT {HousingSelect()}
                FROM {HousingFrom()}
                {where}
            ),
            agg AS (
                SELECT COUNT_BIG(*) AS TotalCount,
                       ISNULL(SUM(RoomsCount),0) AS RoomsCount,
                       ISNULL(SUM(ResidentsTotal),0) AS ResidentsTotal,
                       ISNULL(SUM(CapacityTotal),0) AS CapacityTotal
                FROM filtered
            )
            SELECT f.*, a.TotalCount, a.RoomsCount AS SumRooms, a.ResidentsTotal AS SumResidents, a.CapacityTotal AS SumCapacity
            FROM filtered f CROSS JOIN agg a
            ORDER BY f.NameAr
            """;
        long total = 0;
        var summary = EmptyHousingSummary();
        var items = new List<JsonObject>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (items.Count == 0)
                {
                    total = Long(reader, "TotalCount");
                    long rooms = Long(reader, "SumRooms");
                    long residents = Long(reader, "SumResidents");
                    long capacity = Long(reader, "SumCapacity");
                    summary = new JsonObject
                    {
                        ["housingsTotal"] = total,
                        ["roomsTotal"] = rooms,
                        ["residentsTotal"] = residents,
                        ["occupancyPercent"] = capacity <= 0 ? 0 : Math.Round(100.0 * residents / capacity, 1),
                        ["residentsTotalNote"] = "إجمالي المقيمين من نظام الموارد (قد يشمل شركات أخرى)"
                    };
                }
                items.Add(MapHousingRow(reader));
            }
        }
        return new(items, total, summary, DateTimeOffset.UtcNow);
    }

    static JsonObject HousingSummaryOnly(DbConnection source, string city, string status, string search,
        LiveOperationsQuery.Scope scope, CancellationToken ct)
    {
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        string where = HousingWhere(cmd, city, status, search, scope);
        cmd.CommandText = $"""
            SELECT COUNT_BIG(*) AS TotalCount,
                   ISNULL(SUM(RoomsCount),0) AS RoomsCount,
                   ISNULL(SUM(ResidentsTotal),0) AS ResidentsTotal,
                   ISNULL(SUM(CapacityTotal),0) AS CapacityTotal
            FROM (
                SELECT {HousingSelect()}
                FROM {HousingFrom()}
                {where}
            ) x
            """;
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return EmptyHousingSummary();
        long total = Long(r, "TotalCount");
        long rooms = Long(r, "RoomsCount");
        long residents = Long(r, "ResidentsTotal");
        long capacity = Long(r, "CapacityTotal");
        return new JsonObject
        {
            ["housingsTotal"] = total,
            ["roomsTotal"] = rooms,
            ["residentsTotal"] = residents,
            ["occupancyPercent"] = capacity <= 0 ? 0 : Math.Round(100.0 * residents / capacity, 1)
        };
    }

    public static DetailResult HousingRooms(DbConnection source, string housingId, LiveOperationsQuery.Scope scope, CancellationToken ct = default)
    {
        EnsureSql(source);
        if (!Guid.TryParse(housingId, out _)) throw new ImportFailure("housing_not_found");
        using var head = source.CreateCommand();
        head.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(head, ct);
        string scopeSql = ScopeHousing(scope, head);
        Add(head, "@id", housingId);
        head.CommandText = $"""
            SELECT TOP 1 h.[Id], h.[NameAr], h.[Code], h.[Status], gc.[NameAr] AS CityNameAr
            FROM [app].[Housing] h
            LEFT JOIN [platform].[GlobalCities] gc ON gc.[Id]=h.[CityId] AND gc.[IsDeleted]=0
            WHERE h.[IsDeleted]=0 AND h.[Id]=@id{scopeSql}
            """;
        using var hr = head.ExecuteReader();
        if (!hr.Read()) throw new ImportFailure("housing_not_found");
        var body = new JsonObject
        {
            ["id"] = Str(hr, "Id"),
            ["nameAr"] = Str(hr, "NameAr"),
            ["code"] = Str(hr, "Code"),
            ["status"] = Str(hr, "Status"),
            ["city"] = Str(hr, "CityNameAr"),
            ["supervisor"] = null,
            ["supervisorSource"] = NoSourceYet
        };
        hr.Close();

        using var roomsCmd = source.CreateCommand();
        roomsCmd.CommandTimeout = CommandTimeoutSeconds;
        using var roomsCancel = SyncCancellation.Register(roomsCmd, ct);
        Add(roomsCmd, "@housingId", housingId);
        string empPred = "";
        if (!scope.AllCompanies)
        {
            if (scope.EmployeeIds.Count == 0) empPred = " AND 1=0";
            else
            {
                string ids = InList(roomsCmd, "@resEmp", scope.EmployeeIds);
                empPred = $" AND p.[EmployeeId] IN ({ids})";
            }
        }
        roomsCmd.CommandText = $"""
            SELECT r.[Id], r.[Name], r.[Capacity], r.[CurrentOccupancy],
                   (SELECT e.[Id] AS employeeId, e.[FullNameAr] AS nameAr, e.[IqamaNo] AS iqama,
                           CONVERT(varchar(10), p.[EffectiveFrom], 23) AS checkInDate
                    FROM [app].[HousingResidencePeriods] p
                    INNER JOIN [app].[Employees] e ON e.[Id]=p.[EmployeeId] AND e.[IsDeleted]=0
                    WHERE p.[RoomId]=r.[Id] AND p.[EffectiveTo] IS NULL{empPred}
                    FOR JSON PATH) AS ResidentsJson
            FROM [app].[HousingRooms] r
            WHERE r.[IsDeleted]=0 AND r.[HousingId]=@housingId
            ORDER BY r.[Name]
            """;
        // Residents list is company-scoped when !AllCompanies; room occupancy counts stay ERP totals.

        var rooms = new JsonArray();
        using (var rr = roomsCmd.ExecuteReader())
        {
            while (rr.Read())
            {
                ct.ThrowIfCancellationRequested();
                var room = new JsonObject
                {
                    ["id"] = Str(rr, "Id"),
                    ["name"] = Str(rr, "Name"),
                    ["capacity"] = Long(rr, "Capacity"),
                    ["currentOccupancy"] = Long(rr, "CurrentOccupancy"),
                    ["residents"] = ParseJsonArray(rr, "ResidentsJson")
                };
                rooms.Add(room);
            }
        }
        body["rooms"] = rooms;
        return new(body, DateTimeOffset.UtcNow);
    }

    static string CitiesSummarySql(string scopeSql, string platformSql = "", bool cityFilter = false)
    {
        string cityPred = cityFilter ? " AND oc.[Id]=@cityId" : "";
        // Sponsor scope applied to vehicle/account/employee counts via LEFT JOIN sponsors when restricted.
        string sponsorJoinFilter = scopeSql.Length == 0 ? "" : scopeSql;
        return $"""
            SELECT oc.[Id], gc.[Code] AS CityCode, gc.[NameAr] AS CityNameAr, gc.[NameEn] AS CityNameEn, oc.[Status],
                   (SELECT COUNT_BIG(DISTINCT s.[Id])
                    FROM [app].[Sponsors] s
                    WHERE s.[IsDeleted]=0 AND s.[Status]=1{sponsorJoinFilter}
                      AND (
                        EXISTS(SELECT 1 FROM [app].[Vehicles] v WHERE v.[IsDeleted]=0 AND v.[OperatingCityId]=oc.[Id] AND v.[SponsorId]=s.[Id])
                        OR EXISTS(SELECT 1 FROM [app].[Employees] e WHERE e.[IsDeleted]=0 AND e.[OperatingCityId]=oc.[Id] AND e.[SponsorId]=s.[Id])
                        OR EXISTS(SELECT 1 FROM [app].[PlatformRiderAccounts] a WHERE a.[IsDeleted]=0 AND a.[OperatingCityId]=oc.[Id] AND a.[SponsorId]=s.[Id])
                      )
                   ) AS ActiveRegistrations,
                   (SELECT STUFF((
                        SELECT N'، ' + s2.[RegistryNameAr]
                        FROM [app].[Sponsors] s2
                        WHERE s2.[IsDeleted]=0 AND s2.[Status]=1{sponsorJoinFilter.Replace("s.[", "s2.[", StringComparison.Ordinal)}
                          AND (
                            EXISTS(SELECT 1 FROM [app].[Vehicles] v WHERE v.[IsDeleted]=0 AND v.[OperatingCityId]=oc.[Id] AND v.[SponsorId]=s2.[Id])
                            OR EXISTS(SELECT 1 FROM [app].[Employees] e WHERE e.[IsDeleted]=0 AND e.[OperatingCityId]=oc.[Id] AND e.[SponsorId]=s2.[Id])
                            OR EXISTS(SELECT 1 FROM [app].[PlatformRiderAccounts] a WHERE a.[IsDeleted]=0 AND a.[OperatingCityId]=oc.[Id] AND a.[SponsorId]=s2.[Id])
                          )
                        ORDER BY s2.[RegistryNameAr]
                        FOR XML PATH(''), TYPE).value('.','nvarchar(max)'),1,2,'')) AS RegistrationNames,
                   (SELECT COUNT_BIG(*) FROM [app].[Vehicles] v
                    LEFT JOIN [app].[Sponsors] s ON s.[Id]=v.[SponsorId] AND s.[IsDeleted]=0
                    WHERE v.[IsDeleted]=0 AND v.[OperatingCityId]=oc.[Id] AND v.[VehicleType]=2{sponsorJoinFilter}) AS CarsCount,
                   (SELECT COUNT_BIG(*) FROM [app].[Vehicles] v
                    LEFT JOIN [app].[Sponsors] s ON s.[Id]=v.[SponsorId] AND s.[IsDeleted]=0
                    WHERE v.[IsDeleted]=0 AND v.[OperatingCityId]=oc.[Id] AND v.[VehicleType]=1{sponsorJoinFilter}) AS MotorbikesCount,
                   (SELECT COUNT_BIG(*) FROM [app].[Vehicles] v
                    LEFT JOIN [app].[Sponsors] s ON s.[Id]=v.[SponsorId] AND s.[IsDeleted]=0
                    WHERE v.[IsDeleted]=0 AND v.[OperatingCityId]=oc.[Id]{sponsorJoinFilter}) AS VehiclesTotal,
                   (SELECT COUNT_BIG(*) FROM [app].[Employees] e
                    LEFT JOIN [app].[Sponsors] s ON s.[Id]=e.[SponsorId] AND s.[IsDeleted]=0
                    WHERE e.[IsDeleted]=0 AND e.[OperatingCityId]=oc.[Id] AND e.[Status]=3 AND e.[IsEmployee]=0{sponsorJoinFilter}) AS RidersTotal,
                   (SELECT STUFF((
                        SELECT N'، ' + cp.[Code]
                        FROM [app].[PlatformRiderAccounts] a
                        INNER JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=a.[ClientPlatformId] AND cp.[IsDeleted]=0
                        LEFT JOIN [app].[Sponsors] s ON s.[Id]=a.[SponsorId] AND s.[IsDeleted]=0
                        WHERE a.[IsDeleted]=0 AND a.[OperatingCityId]=oc.[Id]{sponsorJoinFilter}{platformSql}
                        GROUP BY cp.[Code]
                        ORDER BY cp.[Code]
                        FOR XML PATH(''), TYPE).value('.','nvarchar(max)'),1,2,'')) AS AppsWorking,
                   (SELECT COUNT_BIG(*) FROM [app].[PlatformRiderAccounts] a
                    LEFT JOIN [app].[Sponsors] s ON s.[Id]=a.[SponsorId] AND s.[IsDeleted]=0
                    LEFT JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=a.[ClientPlatformId] AND cp.[IsDeleted]=0
                    WHERE a.[IsDeleted]=0 AND a.[OperatingCityId]=oc.[Id]{sponsorJoinFilter}{platformSql}) AS AccountsTotal,
                   (SELECT COUNT_BIG(*) FROM [app].[PlatformRiderAccounts] a
                    LEFT JOIN [app].[Sponsors] s ON s.[Id]=a.[SponsorId] AND s.[IsDeleted]=0
                    LEFT JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=a.[ClientPlatformId] AND cp.[IsDeleted]=0
                    WHERE a.[IsDeleted]=0 AND a.[OperatingCityId]=oc.[Id] AND a.[Status] IN (1,2){sponsorJoinFilter}{platformSql}) AS AccountsActive,
                   (SELECT COUNT_BIG(*) FROM [app].[PlatformRiderAccounts] a
                    LEFT JOIN [app].[Sponsors] s ON s.[Id]=a.[SponsorId] AND s.[IsDeleted]=0
                    LEFT JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=a.[ClientPlatformId] AND cp.[IsDeleted]=0
                    WHERE a.[IsDeleted]=0 AND a.[OperatingCityId]=oc.[Id]
                      AND EXISTS(
                        SELECT 1 FROM [app].[RiderClientAssignments] rca
                        WHERE rca.[PlatformRiderAccountId]=a.[Id] AND rca.[IsDeleted]=0 AND rca.[EffectiveTo] IS NULL
                      ){sponsorJoinFilter}{platformSql}) AS AccountsAssigned
            FROM [app].[OperatingCities] oc
            INNER JOIN [platform].[GlobalCities] gc ON gc.[Id]=oc.[GlobalCityId] AND gc.[IsDeleted]=0
            WHERE oc.[IsDeleted]=0{cityPred}
            ORDER BY gc.[NameAr]
            """;
    }

    static JsonArray CitySplitByRegistration(DbConnection source, string cityId, LiveOperationsQuery.Scope scope, CancellationToken ct)
    {
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        Add(cmd, "@cityId", cityId);
        cmd.CommandText = $"""
            SELECT s.[Id], s.[RegistryNameAr] AS nameAr, s.[UnifiedNationalNumber] AS unifiedNumber,
                   (SELECT COUNT_BIG(*) FROM [app].[Vehicles] v WHERE v.[IsDeleted]=0 AND v.[OperatingCityId]=@cityId AND v.[SponsorId]=s.[Id]) AS vehicles,
                   (SELECT COUNT_BIG(*) FROM [app].[Employees] e WHERE e.[IsDeleted]=0 AND e.[OperatingCityId]=@cityId AND e.[SponsorId]=s.[Id] AND e.[Status]=3 AND e.[IsEmployee]=0) AS riders,
                   (SELECT COUNT_BIG(*) FROM [app].[PlatformRiderAccounts] a WHERE a.[IsDeleted]=0 AND a.[OperatingCityId]=@cityId AND a.[SponsorId]=s.[Id]) AS accounts
            FROM [app].[Sponsors] s
            WHERE s.[IsDeleted]=0 AND s.[Status]=1{scopeSql}
              AND (
                EXISTS(SELECT 1 FROM [app].[Vehicles] v WHERE v.[IsDeleted]=0 AND v.[OperatingCityId]=@cityId AND v.[SponsorId]=s.[Id])
                OR EXISTS(SELECT 1 FROM [app].[Employees] e WHERE e.[IsDeleted]=0 AND e.[OperatingCityId]=@cityId AND e.[SponsorId]=s.[Id])
                OR EXISTS(SELECT 1 FROM [app].[PlatformRiderAccounts] a WHERE a.[IsDeleted]=0 AND a.[OperatingCityId]=@cityId AND a.[SponsorId]=s.[Id])
              )
            ORDER BY s.[RegistryNameAr]
            """;
        return new JsonArray(ReadMapped(cmd, r => new JsonObject
        {
            ["id"] = Str(r, "Id"),
            ["nameAr"] = Str(r, "nameAr"),
            ["unifiedNumber"] = Str(r, "unifiedNumber"),
            ["vehicles"] = Long(r, "vehicles"),
            ["riders"] = Long(r, "riders"),
            ["accounts"] = Long(r, "accounts")
        }, ct).Select(x => (JsonNode?)x).ToArray());
    }

    static JsonArray CitySplitByApp(DbConnection source, string cityId, LiveOperationsQuery.Scope scope, CancellationToken ct)
    {
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        string platformSql = ScopePlatform(scope, cmd, "cp");
        Add(cmd, "@cityId", cityId);
        cmd.CommandText = $"""
            SELECT cp.[Code] AS platformCode, cp.[NameAr] AS nameAr,
                   COUNT_BIG(*) AS accountsTotal,
                   SUM(CASE WHEN a.[Status] IN (1,2) THEN 1 ELSE 0 END) AS accountsActive,
                   SUM(CASE WHEN EXISTS(
                        SELECT 1 FROM [app].[RiderClientAssignments] rca
                        WHERE rca.[PlatformRiderAccountId]=a.[Id] AND rca.[IsDeleted]=0 AND rca.[EffectiveTo] IS NULL
                   ) THEN 1 ELSE 0 END) AS accountsAssigned
            FROM [app].[PlatformRiderAccounts] a
            INNER JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=a.[ClientPlatformId] AND cp.[IsDeleted]=0
            LEFT JOIN [app].[Sponsors] s ON s.[Id]=a.[SponsorId] AND s.[IsDeleted]=0
            WHERE a.[IsDeleted]=0 AND a.[OperatingCityId]=@cityId{scopeSql}{platformSql}
            GROUP BY cp.[Code], cp.[NameAr]
            ORDER BY cp.[Code]
            """;
        return new JsonArray(ReadMapped(cmd, r => new JsonObject
        {
            ["platformCode"] = Str(r, "platformCode"),
            ["nameAr"] = Str(r, "nameAr"),
            ["accountsTotal"] = Long(r, "accountsTotal"),
            ["accountsActive"] = Long(r, "accountsActive"),
            ["accountsAssigned"] = Long(r, "accountsAssigned")
        }, ct).Select(x => (JsonNode?)x).ToArray());
    }

    static string HousingSelect() => """
        h.[Id], h.[Code], h.[NameAr], h.[Status],
        gc.[NameAr] AS CityNameAr, gc.[Code] AS CityCode,
        (SELECT COUNT_BIG(*) FROM [app].[HousingRooms] r WHERE r.[IsDeleted]=0 AND r.[HousingId]=h.[Id]) AS RoomsCount,
        (SELECT ISNULL(SUM(r.[Capacity]),0) FROM [app].[HousingRooms] r WHERE r.[IsDeleted]=0 AND r.[HousingId]=h.[Id]) AS CapacityTotal,
        (SELECT COUNT_BIG(*) FROM [app].[HousingResidencePeriods] p
         INNER JOIN [app].[HousingRooms] r ON r.[Id]=p.[RoomId] AND r.[IsDeleted]=0
         WHERE r.[HousingId]=h.[Id] AND p.[EffectiveTo] IS NULL) AS ResidentsTotal
        """;

    static string HousingFrom() => """
        [app].[Housing] h
        LEFT JOIN [platform].[GlobalCities] gc ON gc.[Id]=h.[CityId] AND gc.[IsDeleted]=0
        """;

    static string HousingWhere(DbCommand cmd, string city, string status, string search, LiveOperationsQuery.Scope? scope)
    {
        var parts = new List<string> { "h.[IsDeleted]=0" };
        if (scope is { AllCompanies: false })
        {
            // Housing visible if any open residence rider is in scope (LiveOperationsQuery HousingByResidence).
            if (scope.EmployeeIds.Count == 0) parts.Add("1=0");
            else
            {
                string ids = InList(cmd, "@he", scope.EmployeeIds);
                parts.Add($"""
                    EXISTS(
                      SELECT 1 FROM [app].[HousingResidencePeriods] p
                      INNER JOIN [app].[HousingRooms] r ON r.[Id]=p.[RoomId] AND r.[IsDeleted]=0
                      WHERE r.[HousingId]=h.[Id] AND p.[EffectiveTo] IS NULL AND p.[EmployeeId] IN ({ids})
                    )
                    """);
            }
        }
        if (city.Length > 0)
        {
            Add(cmd, "@city", city);
            parts.Add("(gc.[Code]=@city OR gc.[NameAr]=@city OR CAST(h.[CityId] AS nvarchar(36))=@city)");
        }
        if (status.Length > 0)
        {
            Add(cmd, "@status", status);
            parts.Add("CAST(h.[Status] AS nvarchar(20))=@status");
        }
        if (search.Length > 0)
        {
            Add(cmd, "@q", "%" + EscapeLike(search) + "%");
            parts.Add("(h.[NameAr] LIKE @q OR h.[Code] LIKE @q OR gc.[NameAr] LIKE @q)");
        }
        return " WHERE " + string.Join(" AND ", parts);
    }

    static string ScopeHousing(LiveOperationsQuery.Scope? scope, DbCommand cmd)
    {
        if (scope is null or { AllCompanies: true }) return "";
        if (scope.EmployeeIds.Count == 0) return " AND 1=0";
        string ids = InList(cmd, "@hscope", scope.EmployeeIds);
        return $"""
             AND EXISTS(
              SELECT 1 FROM [app].[HousingResidencePeriods] p
              INNER JOIN [app].[HousingRooms] r ON r.[Id]=p.[RoomId] AND r.[IsDeleted]=0
              WHERE r.[HousingId]=h.[Id] AND p.[EffectiveTo] IS NULL AND p.[EmployeeId] IN ({ids})
            )
            """;
    }

    static JsonObject MapCityRow(IDataRecord r) => new()
    {
        ["id"] = Str(r, "Id"),
        ["code"] = Str(r, "CityCode"),
        ["nameAr"] = Str(r, "CityNameAr"),
        ["nameEn"] = Str(r, "CityNameEn"),
        ["status"] = CityStatusLabel(Str(r, "Status")),
        ["activeRegistrations"] = Long(r, "ActiveRegistrations"),
        ["registrationNames"] = Str(r, "RegistrationNames"),
        ["vehiclesCars"] = Long(r, "CarsCount"),
        ["vehiclesMotorbikes"] = Long(r, "MotorbikesCount"),
        ["vehiclesTotal"] = Long(r, "VehiclesTotal"),
        ["ridersTotal"] = Long(r, "RidersTotal"),
        ["appsWorking"] = Str(r, "AppsWorking"),
        ["accountsTotal"] = Long(r, "AccountsTotal"),
        ["accountsActive"] = Long(r, "AccountsActive"),
        ["accountsAssigned"] = Long(r, "AccountsAssigned")
    };

    static JsonObject MapHousingRow(IDataRecord r)
    {
        long rooms = Long(r, "RoomsCount");
        long residents = Long(r, "ResidentsTotal");
        return new JsonObject
        {
            ["id"] = Str(r, "Id"),
            ["code"] = Str(r, "Code"),
            ["nameAr"] = Str(r, "NameAr"),
            ["city"] = Str(r, "CityNameAr"),
            ["cityCode"] = Str(r, "CityCode"),
            ["roomsCount"] = rooms,
            ["capacity"] = Long(r, "CapacityTotal"),
            ["residentsTotal"] = residents,
            ["ridersPerRoom"] = rooms <= 0 ? 0 : Math.Round((double)residents / rooms, 2),
            ["status"] = HousingStatusLabel(Str(r, "Status")),
            ["supervisor"] = null,
            ["supervisorSource"] = NoSourceYet
        };
    }

    static JsonObject EmptyHousingSummary() => new()
    {
        ["housingsTotal"] = 0L,
        ["roomsTotal"] = 0L,
        ["residentsTotal"] = 0L,
        ["occupancyPercent"] = 0d
    };
}
