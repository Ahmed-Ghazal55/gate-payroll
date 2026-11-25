using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace ErpImport;

/// <summary>
/// T12/T13: read-only live rates from Omar's <c>app.PlatformDailyCommissionRates</c>
/// (Jahez = Program 1, Chefz FullTime = Program 2). Never writes to ERP.
/// </summary>
public static class PlatformCommissionLiveQuery
{
    public const int CommandTimeoutSeconds = 15;
    // Omar LogisticsERP.Domain.Entities.Platform.DailyCommissionProgram
    public const int ProgramJahez = 1, ProgramChefzFullTime = 2;
    // Omar DailyCommissionRateKind
    public const int KindFixed = 1, KindPercentage = 2;

    public static int ProgramForApp(string app) => app.Trim().ToLowerInvariant() switch
    {
        "jahez" => ProgramJahez,
        "chefz" or "thechefz" or "the-chefz" or "shiftz" => ProgramChefzFullTime,
        _ => throw new ImportFailure("unsupported_commission_app")
    };

    public static string AppLabel(int program) => program switch
    {
        ProgramJahez => "jahez",
        ProgramChefzFullTime => "chefz",
        _ => "unknown"
    };

    public static string KindLabel(int kind) => kind switch
    {
        KindFixed => "fixed",
        KindPercentage => "percentage",
        _ => "unknown"
    };

    public static (string Sql, IReadOnlyList<string> ParameterNames) PreviewRatesSql(int program)
    {
        using var cmd = new SqlCommand();
        Add(cmd, "@program", program);
        return (RatesSql(), cmd.Parameters.Cast<SqlParameter>().Select(p => p.ParameterName).ToList());
    }

    public static JsonObject Read(DbConnection source, string app, DateOnly? asOf = null, CancellationToken ct = default)
    {
        EnsureSql(source);
        int program = ProgramForApp(app);
        DateOnly on = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        string onText = on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var readAt = DateTimeOffset.UtcNow;
        var rates = new List<JsonObject>();

        using (var cmd = source.CreateCommand())
        {
            cmd.CommandTimeout = CommandTimeoutSeconds;
            using var cancel = SyncCancellation.Register(cmd, ct);
            Add(cmd, "@program", program);
            cmd.CommandText = RatesSql();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                rates.Add(MapRate(reader, program));
            }
        }

        var missing = new List<JsonObject>();
        var byCity = new List<JsonObject>();
        foreach (var (code, nameAr) in new[] { ("riyadh", "الرياض"), ("jeddah", "جدة") })
        {
            var cityRates = rates
                .Where(r =>
                {
                    string cityCode = Text(r, "cityCode");
                    string cityName = Text(r, "cityNameAr");
                    return string.Equals(cityCode, code, StringComparison.OrdinalIgnoreCase)
                        || (code == "riyadh" && cityName.Contains("رياض", StringComparison.Ordinal))
                        || (code == "jeddah" && cityName.Contains("جدة", StringComparison.Ordinal));
                })
                .Concat(rates.Where(r => Text(r, "operatingCityId") == "" && Text(r, "cityCode") == ""))
                .OrderBy(r => Text(r, "effectiveFrom"))
                .ThenBy(r => Num(r, "id"))
                .ToList();

            var effective = cityRates.LastOrDefault(r => string.CompareOrdinal(Text(r, "effectiveFrom"), onText) <= 0)
                ?? rates.Where(r => Text(r, "operatingCityId") == "")
                    .OrderBy(r => Text(r, "effectiveFrom"))
                    .LastOrDefault(r => string.CompareOrdinal(Text(r, "effectiveFrom"), onText) <= 0);

            if (effective == null)
            {
                missing.Add(new JsonObject
                {
                    ["cityCode"] = code,
                    ["cityNameAr"] = nameAr,
                    ["message"] = "غير متوفر في نظام عمر",
                    ["omarFile"] = "PlatformDailyCommissionRates / admin platforms commission-rates"
                });
                byCity.Add(new JsonObject
                {
                    ["cityCode"] = code,
                    ["cityNameAr"] = nameAr,
                    ["available"] = false,
                    ["message"] = "غير متوفر في نظام عمر"
                });
            }
            else
            {
                var row = (JsonObject)effective.DeepClone();
                row["cityCode"] = code;
                row["cityNameAr"] = nameAr;
                row["available"] = true;
                byCity.Add(row);
            }
        }

        var schemeRows = new JsonArray();
        if (program == ProgramChefzFullTime)
        {
            foreach (var city in byCity)
            {
                bool available = city["available"]?.GetValue<bool>() == true;
                schemeRows.Add(new JsonObject
                {
                    ["identifierType"] = "FullTime",
                    ["cityCode"] = Text(city, "cityCode"),
                    ["cityNameAr"] = Text(city, "cityNameAr"),
                    ["kind"] = available ? Text(city, "kind") : "",
                    ["rate"] = available ? city["dailyRate"]?.DeepClone() : null,
                    ["available"] = available,
                    ["message"] = available ? "" : "غير متوفر في نظام عمر",
                    ["accountOverride"] = (JsonNode?)null
                });
            }
            // T13 / Grok TRIAL2: FreelancerRate is a code constant, not a SELECT — never mark as from Omar.
            missing.Add(new JsonObject
            {
                ["identifierType"] = "Freelancer",
                ["cityCode"] = "",
                ["cityNameAr"] = "كل المدن",
                ["message"] = "غير متوفر في نظام عمر",
                ["omarFile"] = "PlatformDailyCommissionRates / ChefzRules.FreelancerRate (list for Omar)"
            });
            schemeRows.Add(new JsonObject
            {
                ["identifierType"] = "Freelancer",
                ["cityCode"] = "",
                ["cityNameAr"] = "كل المدن",
                ["kind"] = "",
                ["rate"] = null,
                ["available"] = false,
                ["message"] = "غير متوفر في نظام عمر",
                ["sourceNote"] = "Omar ChefzRules.FreelancerRate is a code constant — not a live SELECT",
                ["accountOverride"] = (JsonNode?)null
            });
        }
        else
        {
            foreach (var city in byCity)
            {
                bool available = city["available"]?.GetValue<bool>() == true;
                schemeRows.Add(new JsonObject
                {
                    ["identifierType"] = "daily_commission",
                    ["cityCode"] = Text(city, "cityCode"),
                    ["cityNameAr"] = Text(city, "cityNameAr"),
                    ["kind"] = available ? Text(city, "kind") : "",
                    ["rate"] = available ? city["dailyRate"]?.DeepClone() : null,
                    ["available"] = available,
                    ["message"] = available ? "" : "غير متوفر في نظام عمر",
                    ["accountOverride"] = (JsonNode?)null
                });
            }
        }

        return new JsonObject
        {
            ["app"] = AppLabel(program),
            ["program"] = program,
            ["asOf"] = onText,
            ["readAt"] = readAt.ToString("O"),
            ["source"] = "omar_erp.app.PlatformDailyCommissionRates",
            ["rates"] = new JsonArray(rates.Select(r => (JsonNode)r.DeepClone()).ToArray()),
            ["byCity"] = new JsonArray(byCity.Select(r => (JsonNode)r.DeepClone()).ToArray()),
            ["schemeRows"] = schemeRows,
            ["missing"] = new JsonArray(missing.Select(m => (JsonNode)m.DeepClone()).ToArray()),
            ["writable"] = false
        };
    }

    /// <summary>Unit-test helper: build the envelope from already-mapped rate rows (no SQL).</summary>
    public static JsonObject FromMappedRates(string app, IEnumerable<JsonObject> mappedRates, DateOnly asOf)
    {
        int program = ProgramForApp(app);
        // Re-run city resolution by temporarily using an in-memory path: call Read's logic via cloning.
        // For tests we only need ProgramForApp / KindLabel / PreviewRatesSql — FromMappedRates builds byCity.
        string onText = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var rates = mappedRates.Select(r => (JsonObject)r.DeepClone()).ToList();
        var byCity = new List<JsonObject>();
        var missing = new List<JsonObject>();
        foreach (var (code, nameAr) in new[] { ("riyadh", "الرياض"), ("jeddah", "جدة") })
        {
            var cityRates = rates
                .Where(r => string.Equals(Text(r, "cityCode"), code, StringComparison.OrdinalIgnoreCase))
                .Concat(rates.Where(r => Text(r, "operatingCityId") == "" && Text(r, "cityCode") == ""))
                .OrderBy(r => Text(r, "effectiveFrom"))
                .ThenBy(r => Num(r, "id"))
                .ToList();
            var effective = cityRates.LastOrDefault(r => string.CompareOrdinal(Text(r, "effectiveFrom"), onText) <= 0);
            if (effective == null)
            {
                missing.Add(new JsonObject { ["cityCode"] = code, ["cityNameAr"] = nameAr, ["message"] = "غير متوفر في نظام عمر" });
                byCity.Add(new JsonObject { ["cityCode"] = code, ["cityNameAr"] = nameAr, ["available"] = false, ["message"] = "غير متوفر في نظام عمر" });
            }
            else
            {
                var row = (JsonObject)effective.DeepClone();
                row["cityCode"] = code;
                row["cityNameAr"] = nameAr;
                row["available"] = true;
                byCity.Add(row);
            }
        }
        var schemeRows = new JsonArray();
        if (program == ProgramChefzFullTime)
        {
            foreach (var city in byCity)
            {
                bool available = city["available"]?.GetValue<bool>() == true;
                schemeRows.Add(new JsonObject
                {
                    ["identifierType"] = "FullTime",
                    ["cityCode"] = Text(city, "cityCode"),
                    ["cityNameAr"] = Text(city, "cityNameAr"),
                    ["kind"] = available ? Text(city, "kind") : "",
                    ["rate"] = available ? city["dailyRate"]?.DeepClone() : null,
                    ["available"] = available,
                    ["message"] = available ? "" : "غير متوفر في نظام عمر"
                });
            }
            missing.Add(new JsonObject
            {
                ["identifierType"] = "Freelancer",
                ["cityNameAr"] = "كل المدن",
                ["message"] = "غير متوفر في نظام عمر",
                ["omarFile"] = "PlatformDailyCommissionRates / ChefzRules.FreelancerRate (list for Omar)"
            });
            schemeRows.Add(new JsonObject
            {
                ["identifierType"] = "Freelancer",
                ["cityCode"] = "",
                ["cityNameAr"] = "كل المدن",
                ["kind"] = "",
                ["rate"] = null,
                ["available"] = false,
                ["message"] = "غير متوفر في نظام عمر",
                ["sourceNote"] = "Omar ChefzRules.FreelancerRate is a code constant — not a live SELECT"
            });
        }
        else
        {
            foreach (var city in byCity)
            {
                bool available = city["available"]?.GetValue<bool>() == true;
                schemeRows.Add(new JsonObject
                {
                    ["identifierType"] = "daily_commission",
                    ["cityCode"] = Text(city, "cityCode"),
                    ["cityNameAr"] = Text(city, "cityNameAr"),
                    ["kind"] = available ? Text(city, "kind") : "",
                    ["rate"] = available ? city["dailyRate"]?.DeepClone() : null,
                    ["available"] = available,
                    ["message"] = available ? "" : "غير متوفر في نظام عمر"
                });
            }
        }
        return new JsonObject
        {
            ["app"] = AppLabel(program),
            ["program"] = program,
            ["asOf"] = onText,
            ["source"] = "omar_erp.app.PlatformDailyCommissionRates",
            ["rates"] = new JsonArray(rates.Select(r => (JsonNode)r.DeepClone()).ToArray()),
            ["byCity"] = new JsonArray(byCity.Select(r => (JsonNode)r.DeepClone()).ToArray()),
            ["schemeRows"] = schemeRows,
            ["missing"] = new JsonArray(missing.Select(m => (JsonNode)m.DeepClone()).ToArray()),
            ["writable"] = false
        };
    }

    static string RatesSql() => """
        SELECT
            r.[Id] AS Id,
            r.[Program] AS Program,
            r.[OperatingCityId] AS OperatingCityId,
            CONVERT(varchar(10), r.[EffectiveFrom], 23) AS EffectiveFrom,
            r.[DailyRate] AS DailyRate,
            r.[Kind] AS Kind,
            CONVERT(varchar(33), r.[RecordedAtUtc], 127) AS RecordedAtUtc,
            oc.[GlobalCityId] AS GlobalCityId,
            gc.[Code] AS CityCode,
            gc.[NameAr] AS CityNameAr
        FROM [app].[PlatformDailyCommissionRates] r
        LEFT JOIN [app].[OperatingCities] oc ON oc.[Id] = r.[OperatingCityId] AND oc.[IsDeleted] = 0
        LEFT JOIN [platform].[GlobalCities] gc ON gc.[Id] = oc.[GlobalCityId] AND gc.[IsDeleted] = 0
        WHERE r.[Program] = @program
        ORDER BY r.[EffectiveFrom], r.[Id]
        """;

    static JsonObject MapRate(IDataRecord r, int program) => new()
    {
        ["id"] = Long(r, "Id"),
        ["program"] = program,
        ["app"] = AppLabel(program),
        ["operatingCityId"] = Str(r, "OperatingCityId"),
        ["cityCode"] = Str(r, "CityCode").ToLowerInvariant(),
        ["cityNameAr"] = Str(r, "CityNameAr"),
        ["effectiveFrom"] = Str(r, "EffectiveFrom"),
        ["dailyRate"] = Decimal(r, "DailyRate"),
        ["kind"] = KindLabel((int)Long(r, "Kind")),
        ["kindCode"] = Long(r, "Kind"),
        ["recordedAtUtc"] = Str(r, "RecordedAtUtc")
    };

    static string Text(JsonObject o, string key) => o[key]?.ToString()?.Trim() ?? "";
    static decimal Num(JsonObject o, string key) =>
        decimal.TryParse(Text(o, key), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : 0;

    static void EnsureSql(DbConnection source)
    {
        if (source is not SqlConnection) throw new ImportFailure("erp_sql_required");
    }

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
}
