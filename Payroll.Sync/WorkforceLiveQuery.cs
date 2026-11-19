using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace ErpImport;

/// <summary>
/// Live paged workforce list from Omar's ERP (one SQL with joins). Identifiers allowlisted; values are parameters.
/// </summary>
public static class WorkforceLiveQuery
{
    public const int MaxPageSize = 200, CommandTimeoutSeconds = 15;
    public const string NoSourceYet = "لا يوجد مصدر بعد";
    public const string ImagePending = "بانتظار صلاحية القراءة من نظام عمر";

    public sealed record ListRequest(
        int Page, int PageSize, string Sort, string Query, string QueryAlt,
        string Sponsorship, string WorkType, string Status, string SponsorUnified,
        string Platform, string CityCode, bool MissingIban, int Skip, int Take);

    public sealed record ListResult(IReadOnlyList<JsonObject> Items, long Total, ListRequest Request, DateTimeOffset AsOf, JsonObject Summary);

    static readonly HashSet<string> Sorts = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "-name", "status", "-status", "iqama", "-iqama", "id", "-id"
    };

    public static ListRequest ParseRequest(IReadOnlyDictionary<string, string> query)
    {
        int Positive(string key, int fallback)
        {
            if (!query.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return fallback;
            if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 1)
                throw new ImportFailure("invalid_page");
            return value;
        }
        int page = Positive("page", 1), pageSize = Math.Min(Positive("pageSize", 50), MaxPageSize);
        string sort = (query.TryGetValue("sort", out var s) ? s : "").Trim();
        if (sort.Length == 0) sort = "name";
        if (!Sorts.Contains(sort)) throw new ImportFailure("invalid_live_sort");
        string q = "";
        if (query.TryGetValue("q", out var q1) && !string.IsNullOrWhiteSpace(q1)) q = q1.Trim();
        else if (query.TryGetValue("search", out var q2) && !string.IsNullOrWhiteSpace(q2)) q = q2.Trim();
        string sponsorship = Get(query, "sponsorship");
        string workType = Get(query, "workType");
        string status = Get(query, "status");
        string sponsor = Get(query, "sponsorUnified", Get(query, "companyId"));
        string platform = Get(query, "platform");
        string city = Get(query, "city");
        bool missingIban = query.TryGetValue("missingIban", out var mi) && (mi == "1" || mi.Equals("true", StringComparison.OrdinalIgnoreCase));
        return new(page, pageSize, sort, q, "", sponsorship, workType, status, sponsor, platform, city, missingIban, checked((page - 1) * pageSize), pageSize);
    }

    static string Get(IReadOnlyDictionary<string, string> query, string key, string fallback = "")
        => query.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

    /// <summary>SQL text for tests: identifiers from allowlist; values are parameters.</summary>
    public static (string CountSql, string PageSql, string SummarySql, IReadOnlyList<string> ParameterNames) PreviewSql(ListRequest request)
    {
        using var cmd = new SqlCommand();
        string where = BuildWhere(request, cmd);
        string from = FromSql();
        string order = OrderSql(request.Sort);
        string count = $"SELECT COUNT_BIG(*) FROM {from}{where}";
        string page = $"SELECT {SelectSql()} FROM {from}{where} {order} OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        string summary = $"SELECT {SummarySelectSql()} FROM {from}{where}";
        var names = cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).Append("@skip").Append("@take").ToList();
        return (count, page, summary, names);
    }

    public static ListResult Page(DbConnection source, ListRequest request, CancellationToken cancellationToken = default)
    {
        if (source is not SqlConnection) throw new ImportFailure("live_ops_requires_sql");
        DateTimeOffset asOf = DateTimeOffset.UtcNow;
        using var countCmd = source.CreateCommand();
        countCmd.CommandTimeout = CommandTimeoutSeconds;
        using var countCancel = SyncCancellation.Register(countCmd, cancellationToken);
        countCmd.CommandText = $"SELECT COUNT_BIG(*) FROM {FromSql()}{BuildWhere(request, countCmd)}";
        long total = Convert.ToInt64(countCmd.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);

        using var summaryCmd = source.CreateCommand();
        summaryCmd.CommandTimeout = CommandTimeoutSeconds;
        using var summaryCancel = SyncCancellation.Register(summaryCmd, cancellationToken);
        summaryCmd.CommandText = $"SELECT {SummarySelectSql()} FROM {FromSql()}{BuildWhere(request, summaryCmd)}";
        var summary = ReadSummary(summaryCmd);

        using var pageCmd = source.CreateCommand();
        pageCmd.CommandTimeout = CommandTimeoutSeconds;
        using var pageCancel = SyncCancellation.Register(pageCmd, cancellationToken);
        Add(pageCmd, "@skip", request.Skip);
        Add(pageCmd, "@take", request.Take);
        pageCmd.CommandText = $"SELECT {SelectSql()} FROM {FromSql()}{BuildWhere(request, pageCmd)} {OrderSql(request.Sort)} OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        var items = ReadRows(pageCmd, cancellationToken);
        return new(items, total, request, asOf, summary);
    }

    public static IReadOnlyList<JsonObject> Catalog(DbConnection source, string kind, CancellationToken cancellationToken = default)
    {
        if (source is not SqlConnection) throw new ImportFailure("live_ops_requires_sql");
        string sql = kind switch
        {
            "job-titles" => """
                SELECT j.[Id], j.[Code], j.[NameAr], j.[NameEn], j.[Status],
                       STUFF((SELECT ',' + owt.[Code] FROM [app].[JobTitleOperationalWorkTypes] link
                              INNER JOIN [app].[OperationalWorkTypes] owt ON owt.[Id]=link.[OperationalWorkTypeId] AND owt.[IsDeleted]=0
                              WHERE link.[JobTitleId]=j.[Id] FOR XML PATH(''), TYPE).value('.','nvarchar(max)'),1,1,'') AS WorkTypeCodes
                FROM [app].[JobTitles] j WHERE j.[IsDeleted]=0 ORDER BY j.[Code]
                """,
            "work-types" or "operational-work-types" => """
                SELECT [Id], [Code], [NameAr], [NameEn], [Status]
                FROM [app].[OperationalWorkTypes] WHERE [IsDeleted]=0 ORDER BY [Code]
                """,
            "iqama-professions" or "residency-professions" => """
                SELECT [Id], [Code], [NameAr], [NameEn], [Status]
                FROM [app].[ResidencyProfessions] WHERE [IsDeleted]=0 ORDER BY [Code]
                """,
            "licence-categories" or "driver-license-categories" => """
                SELECT [Id], [Code], [NameAr], [NameEn], [Status]
                FROM [app].[DriverLicenseCategories] WHERE [IsDeleted]=0 ORDER BY [Code]
                """,
            "sponsors" => """
                SELECT s.[Id], s.[RegistryNameAr], s.[RegistryNameEn], s.[UnifiedNationalNumber], s.[EmployerIdentityNumber],
                       s.[CommercialRegistrationNumber], s.[SponsorType], s.[Status]
                FROM [app].[Sponsors] s WHERE s.[IsDeleted]=0 ORDER BY s.[RegistryNameAr]
                """,
            "city-settings" or "operating-cities" => """
                SELECT oc.[Id], oc.[Status], oc.[EnabledFrom], oc.[DisabledAt],
                       gc.[Code], gc.[NameAr], gc.[NameEn], gc.[RegionAr], gc.[RegionEn], gc.[CountryCode]
                FROM [app].[OperatingCities] oc
                INNER JOIN [platform].[GlobalCities] gc ON gc.[Id]=oc.[GlobalCityId]
                WHERE oc.[IsDeleted]=0 AND gc.[IsDeleted]=0
                ORDER BY gc.[NameAr]
                """,
            _ => throw new ImportFailure("hr_catalog_kind_unsupported:" + kind)
        };
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = sql;
        using var cancel = SyncCancellation.Register(cmd, cancellationToken);
        return ReadGeneric(cmd, cancellationToken);
    }

    public static IReadOnlyList<JsonObject> SaudiPayrollEmployees(DbConnection source, CancellationToken cancellationToken = default)
    {
        if (source is not SqlConnection) throw new ImportFailure("live_ops_requires_sql");
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = """
            SELECT p.[Id], p.[Number], p.[Name], p.[NationalId], p.[Country], p.[JoiningDate], p.[PersonalIban], p.[Salary], p.[Status],
                   s.[RegistryNameAr] AS SponsorNameAr, s.[UnifiedNationalNumber]
            FROM [app].[PayrollEmployees] p
            INNER JOIN [app].[Sponsors] s ON s.[Id]=p.[SponsorId] AND s.[IsDeleted]=0
            WHERE p.[IsDeleted]=0
            ORDER BY p.[Number]
            """;
        using var cancel = SyncCancellation.Register(cmd, cancellationToken);
        return ReadGeneric(cmd, cancellationToken).Select(MapSaudi).ToList();
    }

    public static IReadOnlyList<JsonObject> TerminatedAndFleeing(DbConnection source, CancellationToken cancellationToken = default)
    {
        if (source is not SqlConnection) throw new ImportFailure("live_ops_requires_sql");
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = """
            SELECT e.[Id], e.[IqamaNo], e.[FullNameAr], e.[Nationality], e.[PrimaryPhone], e.[Status], e.[TerminationDate], e.[StatusReason],
                   owt.[NameAr] AS WorkTypeNameAr, gc.[NameAr] AS CityNameAr
            FROM [app].[Employees] e
            LEFT JOIN [app].[OperationalWorkTypes] owt ON owt.[Id]=e.[OperationalWorkTypeId] AND owt.[IsDeleted]=0
            LEFT JOIN [app].[OperatingCities] oc ON oc.[Id]=e.[OperatingCityId] AND oc.[IsDeleted]=0
            LEFT JOIN [platform].[GlobalCities] gc ON gc.[Id]=oc.[GlobalCityId] AND gc.[IsDeleted]=0
            WHERE e.[IsDeleted]=0 AND e.[Status] IN (6,8)
            ORDER BY e.[FullNameAr]
            """;
        using var cancel = SyncCancellation.Register(cmd, cancellationToken);
        return ReadGeneric(cmd, cancellationToken).Select(MapTerminated).ToList();
    }

    public static IReadOnlyList<JsonObject> AbsenceCases(DbConnection source, CancellationToken cancellationToken = default)
    {
        if (source is not SqlConnection) throw new ImportFailure("live_ops_requires_sql");
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = """
            SELECT c.[Id], c.[CaseNumber], c.[AbsenceDate], c.[CurrentPath], c.[Status], e.[FullNameAr] AS EmployeeNameAr, e.[IqamaNo]
            FROM [app].[EmployeeAbsenceComplianceCases] c
            INNER JOIN [app].[Employees] e ON e.[Id]=c.[EmployeeId] AND e.[IsDeleted]=0
            WHERE c.[IsDeleted]=0
            ORDER BY c.[CaseNumber]
            """;
        using var cancel = SyncCancellation.Register(cmd, cancellationToken);
        return ReadGeneric(cmd, cancellationToken);
    }

    public static JsonObject Person360(DbConnection source, string employeeId, CancellationToken cancellationToken = default)
    {
        if (source is not SqlConnection) throw new ImportFailure("live_ops_requires_sql");
        if (!Guid.TryParse(employeeId, out _)) throw new ImportFailure("invalid_employee_id");
        using var personCmd = source.CreateCommand();
        personCmd.CommandTimeout = CommandTimeoutSeconds;
        Add(personCmd, "@id", employeeId);
        personCmd.CommandText = $"SELECT TOP 1 {SelectSql()} FROM {FromSql()} WHERE e.[IsDeleted]=0 AND e.[Id]=@id";
        using var cancel = SyncCancellation.Register(personCmd, cancellationToken);
        var people = ReadRows(personCmd, cancellationToken);
        if (people.Count == 0) throw new ImportFailure("employee_not_found");
        var person = people[0];

        var work = ReadWorkRecord(source, employeeId, cancellationToken);
        var platforms = ReadOwnedPlatforms(source, employeeId, cancellationToken);
        var notes = ReadPromissoryMeta(source, employeeId, person.S("riderProfileId"), cancellationToken);

        return new JsonObject
        {
            ["person"] = person.DeepClone(),
            ["workRecord3Months"] = new JsonArray(work.Select(r => (JsonNode?)r.DeepClone()).ToArray()),
            ["financialEvaluation"] = new JsonObject
            {
                ["status"] = "unavailable",
                ["message"] = NoSourceYet
            },
            ["iqamaImage"] = new JsonObject
            {
                ["status"] = "pending_permission",
                ["message"] = ImagePending
            },
            ["ibanImage"] = new JsonObject
            {
                ["status"] = "pending_permission",
                ["message"] = ImagePending
            },
            ["promissoryNotes"] = new JsonArray(notes.Select(r => (JsonNode?)r.DeepClone()).ToArray()),
            ["platformAccounts"] = new JsonArray(platforms.Select(r => (JsonNode?)r.DeepClone()).ToArray()),
            ["financialPosition"] = new JsonObject
            {
                ["statementPath"] = "/app/riders/statement/?iqama=" + Uri.EscapeDataString(person.S("iqama")),
                ["message"] = "افتح كشف الحساب التفصيلي"
            }
        };
    }

    static IReadOnlyList<JsonObject> ReadWorkRecord(DbConnection source, string employeeId, CancellationToken ct)
    {
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        Add(cmd, "@id", employeeId);
        cmd.CommandText = """
            SELECT TOP 200 a.[EffectiveFrom], a.[EffectiveTo], a.[PaymentModel], a.[Status],
                   cp.[Code] AS PlatformCode, cp.[NameAr] AS PlatformNameAr,
                   pra.[ExternalAccountId]
            FROM [app].[RiderProfiles] rp
            INNER JOIN [app].[RiderClientAssignments] a ON a.[RiderProfileId]=rp.[Id] AND a.[IsDeleted]=0
            LEFT JOIN [app].[PlatformRiderAccounts] pra ON pra.[Id]=a.[PlatformRiderAccountId] AND pra.[IsDeleted]=0
            LEFT JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=pra.[ClientPlatformId]
            WHERE rp.[EmployeeId]=@id AND rp.[IsDeleted]=0
              AND a.[EffectiveFrom] >= DATEADD(month,-3,SYSUTCDATETIME())
            ORDER BY a.[EffectiveFrom] DESC
            """;
        using var cancel = SyncCancellation.Register(cmd, ct);
        return ReadGeneric(cmd, ct).Select(r => new JsonObject
        {
            ["month"] = r.S("EffectiveFrom").Length >= 7 ? r.S("EffectiveFrom")[..7] : r.S("EffectiveFrom"),
            ["platform"] = r.S("PlatformCode", r.S("PlatformNameAr")),
            ["workDays"] = NoSourceYet,
            ["orders"] = NoSourceYet,
            ["netDue"] = NoSourceYet,
            ["status"] = r.S("Status"),
            ["paymentModel"] = PaymentModel(r.S("PaymentModel")),
            ["externalAccountId"] = r.S("ExternalAccountId"),
            ["effectiveFrom"] = r.S("EffectiveFrom"),
            ["effectiveTo"] = r.S("EffectiveTo")
        }).ToList();
    }

    static IReadOnlyList<JsonObject> ReadOwnedPlatforms(DbConnection source, string employeeId, CancellationToken ct)
    {
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        Add(cmd, "@id", employeeId);
        cmd.CommandText = """
            SELECT pra.[Id], pra.[ExternalAccountId], pra.[CreatedAtUtc], pra.[StartDate], pra.[AcquisitionDate],
                   cp.[Code] AS PlatformCode, cp.[NameAr] AS PlatformNameAr,
                   gc.[NameAr] AS CityNameAr
            FROM [app].[PlatformRiderAccounts] pra
            LEFT JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=pra.[ClientPlatformId]
            LEFT JOIN [app].[OperatingCities] oc ON oc.[Id]=pra.[OperatingCityId] AND oc.[IsDeleted]=0
            LEFT JOIN [platform].[GlobalCities] gc ON gc.[Id]=oc.[GlobalCityId] AND gc.[IsDeleted]=0
            WHERE pra.[RegisteredEmployeeId]=@id AND pra.[IsDeleted]=0
            ORDER BY pra.[CreatedAtUtc] DESC
            """;
        using var cancel = SyncCancellation.Register(cmd, ct);
        return ReadGeneric(cmd, ct).Select(r => new JsonObject
        {
            ["id"] = r.S("Id"),
            ["platform"] = r.S("PlatformCode", r.S("PlatformNameAr")),
            ["externalAccountId"] = r.S("ExternalAccountId"),
            ["city"] = r.S("CityNameAr"),
            ["createdAt"] = r.S("CreatedAtUtc", r.S("StartDate", r.S("AcquisitionDate"))),
            ["owner"] = "registered"
        }).ToList();
    }

    static IReadOnlyList<JsonObject> ReadPromissoryMeta(DbConnection source, string employeeId, string riderProfileId, CancellationToken ct)
    {
        var rows = new List<JsonObject>();
        if (riderProfileId != "")
        {
            using var cmd = source.CreateCommand();
            cmd.CommandTimeout = CommandTimeoutSeconds;
            Add(cmd, "@rp", riderProfileId);
            cmd.CommandText = """
                SELECT f.[Id], f.[CreatedAtUtc]
                FROM [app].[RiderPromissoryFiles] f
                WHERE f.[RiderProfileId]=@rp AND f.[IsDeleted]=0
                """;
            try
            {
                using var cancel = SyncCancellation.Register(cmd, ct);
                rows.AddRange(ReadGeneric(cmd, ct).Select(r => new JsonObject
                {
                    ["id"] = r.S("Id"),
                    ["date"] = r.S("CreatedAtUtc"),
                    ["amount"] = NoSourceYet,
                    ["status"] = "erp_file",
                    ["noteNumber"] = r.S("Id")
                }));
            }
            catch (SqlException) { /* table may be absent in synthetic envs */ }
        }
        _ = employeeId;
        return rows;
    }

    static string FromSql() => """
        [app].[Employees] e
        LEFT JOIN [app].[Sponsors] s ON s.[Id]=e.[SponsorId] AND s.[IsDeleted]=0
        LEFT JOIN [app].[OperationalWorkTypes] owt ON owt.[Id]=e.[OperationalWorkTypeId] AND owt.[IsDeleted]=0
        LEFT JOIN [app].[OperatingCities] oc ON oc.[Id]=e.[OperatingCityId] AND oc.[IsDeleted]=0
        LEFT JOIN [platform].[GlobalCities] gc ON gc.[Id]=oc.[GlobalCityId] AND gc.[IsDeleted]=0
        LEFT JOIN [app].[RiderProfiles] rp ON rp.[EmployeeId]=e.[Id] AND rp.[IsDeleted]=0
        OUTER APPLY (
            SELECT TOP 1 h.[NameAr] AS HousingNameAr
            FROM [app].[HousingResidencePeriods] hr
            INNER JOIN [app].[HousingRooms] room ON room.[Id]=hr.[RoomId] AND room.[IsDeleted]=0
            INNER JOIN [app].[Housing] h ON h.[Id]=room.[HousingId] AND h.[IsDeleted]=0
            WHERE hr.[EmployeeId]=e.[Id] AND hr.[EffectiveTo] IS NULL
        ) housing
        OUTER APPLY (
            SELECT TOP 1 cp.[Code] AS PlatformCode, cp.[NameAr] AS PlatformNameAr,
                   pra.[ExternalAccountId], a.[PaymentModel]
            FROM [app].[RiderClientAssignments] a
            INNER JOIN [app].[PlatformRiderAccounts] pra ON pra.[Id]=a.[PlatformRiderAccountId] AND pra.[IsDeleted]=0
            INNER JOIN [platform].[ClientPlatforms] cp ON cp.[Id]=pra.[ClientPlatformId]
            WHERE a.[RiderProfileId]=rp.[Id] AND a.[IsDeleted]=0 AND a.[EffectiveTo] IS NULL
            ORDER BY a.[EffectiveFrom] DESC
        ) plat
        OUTER APPLY (
            SELECT TOP 1 v.[VehicleType]
            FROM [app].[RiderVehicleAssignments] va
            INNER JOIN [app].[Vehicles] v ON v.[Id]=va.[VehicleId] AND v.[IsDeleted]=0
            WHERE va.[RiderProfileId]=rp.[Id] AND va.[IsDeleted]=0 AND va.[EndedAtUtc] IS NULL
            ORDER BY va.[StartedAtUtc] DESC
        ) veh
        OUTER APPLY (
            SELECT STUFF((
                SELECT ',' + dlc.[NameAr]
                FROM [app].[EmployeeDriverLicenses] lic
                INNER JOIN [app].[DriverLicenseCategories] dlc ON dlc.[Id]=lic.[DriverLicenseCategoryId] AND dlc.[IsDeleted]=0
                WHERE lic.[EmployeeId]=e.[Id] AND lic.[IsDeleted]=0 AND lic.[IsCurrent]=1
                FOR XML PATH(''), TYPE).value('.','nvarchar(max)'),1,1,'') AS LicenseNamesAr
        ) lic
        """;

    static string SelectSql() => """
        e.[Id], e.[IqamaNo], e.[FullNameAr], e.[FullNameEn], e.[PrimaryPhone], e.[Nationality], e.[Iban],
        e.[IsEmployee], e.[EngagementType], e.[Status], e.[ResidencyProfession],
        s.[RegistryNameAr] AS SponsorNameAr, s.[UnifiedNationalNumber] AS SponsorUnified,
        owt.[Code] AS WorkTypeCode, owt.[NameAr] AS WorkTypeNameAr,
        gc.[Code] AS CityCode, gc.[NameAr] AS CityNameAr,
        housing.[HousingNameAr],
        plat.[PlatformCode], plat.[PlatformNameAr], plat.[ExternalAccountId], plat.[PaymentModel],
        veh.[VehicleType],
        lic.[LicenseNamesAr],
        rp.[Id] AS RiderProfileId
        """;

    static string SummarySelectSql() => """
        COUNT_BIG(*) AS TotalCount,
        SUM(CASE WHEN e.[EngagementType]=1 THEN 1 ELSE 0 END) AS SponsoredCount,
        SUM(CASE WHEN e.[EngagementType]=1 AND e.[IsEmployee]=0 THEN 1 ELSE 0 END) AS SponsoredRiders,
        SUM(CASE WHEN e.[EngagementType]=1 AND e.[IsEmployee]=1 THEN 1 ELSE 0 END) AS SponsoredAdmins,
        SUM(CASE WHEN e.[EngagementType]=1 AND (e.[Iban] IS NULL OR e.[Iban]='') THEN 1 ELSE 0 END) AS SponsoredMissingIban
        """;

    static string OrderSql(string sort) => sort.ToLowerInvariant() switch
    {
        "status" => "ORDER BY e.[Status] ASC, e.[FullNameAr] ASC",
        "-status" => "ORDER BY e.[Status] DESC, e.[FullNameAr] ASC",
        "iqama" or "id" => "ORDER BY e.[IqamaNo] ASC",
        "-iqama" or "-id" => "ORDER BY e.[IqamaNo] DESC",
        "-name" => "ORDER BY e.[FullNameAr] DESC",
        _ => "ORDER BY e.[FullNameAr] ASC"
    };

    static string BuildWhere(ListRequest request, DbCommand cmd)
    {
        var parts = new List<string> { "e.[IsDeleted]=0" };
        if (request.Query.Length > 0)
        {
            Add(cmd, "@q", "%" + EscapeLike(request.Query) + "%");
            parts.Add("(e.[IqamaNo] LIKE @q OR e.[FullNameAr] LIKE @q OR e.[FullNameEn] LIKE @q OR e.[PrimaryPhone] LIKE @q OR plat.[ExternalAccountId] LIKE @q)");
            if (request.QueryAlt.Length > 0 && !string.Equals(request.QueryAlt, request.Query, StringComparison.Ordinal))
            {
                Add(cmd, "@qAlt", "%" + EscapeLike(request.QueryAlt) + "%");
                parts[^1] = parts[^1][..^1] + " OR e.[IqamaNo] LIKE @qAlt OR e.[FullNameAr] LIKE @qAlt)";
            }
        }
        if (request.Sponsorship is "sponsored" or "SponsoredInternal")
            parts.Add("e.[EngagementType]=1");
        else if (request.Sponsorship is "external" or "OutsideRider" or "External")
            parts.Add("e.[EngagementType]=2");
        if (request.WorkType is "rider" or "مندوب" or "مناديب")
            parts.Add("e.[IsEmployee]=0");
        else if (request.WorkType is "admin" or "إداري" or "administrative")
            parts.Add("e.[IsEmployee]=1");
        if (request.Status.Length > 0 && int.TryParse(StatusCode(request.Status), out int statusCode))
        {
            Add(cmd, "@status", statusCode);
            parts.Add("e.[Status]=@status");
        }
        if (request.SponsorUnified.Length > 0)
        {
            Add(cmd, "@sponsor", request.SponsorUnified);
            parts.Add("s.[UnifiedNationalNumber]=@sponsor");
        }
        if (request.Platform.Length > 0)
        {
            Add(cmd, "@platform", request.Platform);
            parts.Add("plat.[PlatformCode]=@platform");
        }
        if (request.CityCode.Length > 0)
        {
            Add(cmd, "@city", request.CityCode);
            parts.Add("gc.[Code]=@city");
        }
        if (request.MissingIban)
            parts.Add("e.[EngagementType]=1 AND (e.[Iban] IS NULL OR e.[Iban]='')");
        return parts.Count == 0 ? "" : " WHERE " + string.Join(" AND ", parts);
    }

    static string StatusCode(string status) => status.Trim().ToLowerInvariant() switch
    {
        "1" or "draft" or "مسودة" => "1",
        "2" or "onboarding" or "قيد التهيئة" => "2",
        "3" or "active" or "نشط" => "3",
        "4" or "suspended" or "موقوف" => "4",
        "5" or "onleave" or "في إجازة" => "5",
        "6" or "terminated" or "منتهي الخدمة" => "6",
        "7" or "archived" or "مؤرشف" => "7",
        "8" or "fleeing" or "هروب" or "هروب / انقطاع" => "8",
        "9" or "accident" or "حادث" => "9",
        "10" or "sick" or "إجازة مرضية" => "10",
        _ => status
    };

    static string EscapeLike(string value) => value.Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal).Replace("_", "[_]", StringComparison.Ordinal);

    static void Add(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    static JsonObject ReadSummary(DbCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new JsonObject();
        return new JsonObject
        {
            ["totalCount"] = Convert.ToInt64(r["TotalCount"], CultureInfo.InvariantCulture),
            ["sponsoredCount"] = Convert.ToInt64(r["SponsoredCount"], CultureInfo.InvariantCulture),
            ["sponsoredRiders"] = Convert.ToInt64(r["SponsoredRiders"], CultureInfo.InvariantCulture),
            ["sponsoredAdmins"] = Convert.ToInt64(r["SponsoredAdmins"], CultureInfo.InvariantCulture),
            ["sponsoredMissingIban"] = Convert.ToInt64(r["SponsoredMissingIban"], CultureInfo.InvariantCulture)
        };
    }

    static List<JsonObject> ReadRows(DbCommand cmd, CancellationToken ct)
    {
        using var reader = cmd.ExecuteReader();
        var rows = new List<JsonObject>();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            string engagement = IntStr(reader, "EngagementType") switch { "1" => "SponsoredInternal", "2" => "External", _ => "" };
            bool isEmployee = IntStr(reader, "IsEmployee") == "1";
            string vehicleType = IntStr(reader, "VehicleType");
            string status = IntStr(reader, "Status");
            rows.Add(new JsonObject
            {
                ["id"] = Str(reader, "Id"),
                ["erpEmployeeId"] = Str(reader, "Id"),
                ["riderProfileId"] = Str(reader, "RiderProfileId"),
                ["iqama"] = Str(reader, "IqamaNo"),
                ["name"] = Str(reader, "FullNameAr"),
                ["nameEn"] = Str(reader, "FullNameEn"),
                ["phone"] = Str(reader, "PrimaryPhone"),
                ["nationality"] = Str(reader, "Nationality"),
                ["iban"] = Str(reader, "Iban"),
                ["engagementType"] = engagement,
                ["isEmployee"] = isEmployee,
                ["workType"] = isEmployee ? "admin" : "rider",
                ["status"] = StatusName(status),
                ["statusCode"] = status,
                ["sponsorNameAr"] = Str(reader, "SponsorNameAr"),
                ["sponsorUnified"] = Str(reader, "SponsorUnified"),
                ["jobTitle"] = Str(reader, "WorkTypeNameAr").Length > 0 ? Str(reader, "WorkTypeNameAr") : NoSourceYet,
                ["workTypeCode"] = Str(reader, "WorkTypeCode"),
                ["workTypeNameAr"] = Str(reader, "WorkTypeNameAr"),
                ["city"] = Str(reader, "CityNameAr"),
                ["cityCode"] = Str(reader, "CityCode"),
                ["housing"] = Str(reader, "HousingNameAr"),
                ["platform"] = Str(reader, "PlatformCode", Str(reader, "PlatformNameAr")),
                ["platformWorkId"] = Str(reader, "ExternalAccountId"),
                ["paymentModel"] = PaymentModel(Str(reader, "PaymentModel")),
                ["hasCompanyVehicle"] = vehicleType.Length > 0,
                ["companyVehicleType"] = VehicleTypeName(vehicleType),
                ["drivingLicense"] = Str(reader, "LicenseNamesAr"),
                ["mudadSalary"] = NoSourceYet,
                ["residencyProfession"] = Str(reader, "ResidencyProfession")
            });
        }
        return rows;
    }

    static List<JsonObject> ReadGeneric(DbCommand cmd, CancellationToken ct)
    {
        using var reader = cmd.ExecuteReader();
        var rows = new List<JsonObject>();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            var row = new JsonObject();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                string name = reader.GetName(i);
                if (reader.IsDBNull(i)) { row[name] = ""; continue; }
                object value = reader.GetValue(i);
                row[name] = value switch
                {
                    DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
                    DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
                    DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    byte[] bytes => Convert.ToHexString(bytes),
                    bool b => b,
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "",
                    _ => value.ToString() ?? ""
                };
            }
            rows.Add(row);
        }
        return rows;
    }

    static JsonObject MapSaudi(JsonObject r) => new()
    {
        ["id"] = r.S("Id"),
        ["number"] = r.S("Number"),
        ["name"] = r.S("Name"),
        ["nationalId"] = r.S("NationalId"),
        ["country"] = r.S("Country"),
        ["joiningDate"] = r.S("JoiningDate"),
        ["personalIban"] = r.S("PersonalIban"),
        ["salary"] = r.S("Salary"),
        ["status"] = string.IsNullOrWhiteSpace(r.S("Status")) ? "" : r.S("Status"),
        ["sponsorNameAr"] = r.S("SponsorNameAr"),
        ["unifiedNationalNumber"] = r.S("UnifiedNationalNumber")
    };

    static JsonObject MapTerminated(JsonObject r) => new()
    {
        ["id"] = r.S("Id"),
        ["iqama"] = r.S("IqamaNo"),
        ["name"] = r.S("FullNameAr"),
        ["nationality"] = r.S("Nationality"),
        ["phone"] = r.S("PrimaryPhone"),
        ["city"] = r.S("CityNameAr"),
        ["role"] = r.S("WorkTypeNameAr"),
        ["status"] = StatusName(r.S("Status")),
        ["terminationDate"] = r.S("TerminationDate"),
        ["statusReason"] = r.S("StatusReason")
    };

    static string StatusName(string code) => code switch
    {
        "1" => "Draft",
        "2" => "Onboarding",
        "3" => "Active",
        "4" => "Suspended",
        "5" => "OnLeave",
        "6" => "Terminated",
        "7" => "Archived",
        "8" => "Fleeing",
        "9" => "Accident",
        "10" => "Sick",
        _ => code
    };

    static string PaymentModel(string code) => code switch
    {
        "1" => "PayPerOrder",
        "2" => "Salary",
        _ => code
    };

    static string VehicleTypeName(string code) => code switch
    {
        "1" => "Motorcycle",
        "2" => "Car",
        _ => code
    };

    static string Str(IDataRecord r, string name, string fallback = "")
    {
        try
        {
            int ord = r.GetOrdinal(name);
            if (r.IsDBNull(ord)) return fallback;
            return Convert.ToString(r.GetValue(ord), CultureInfo.InvariantCulture)?.Trim() ?? fallback;
        }
        catch (IndexOutOfRangeException) { return fallback; }
    }

    static string IntStr(IDataRecord r, string name)
    {
        string raw = Str(r, name);
        return raw;
    }
}
