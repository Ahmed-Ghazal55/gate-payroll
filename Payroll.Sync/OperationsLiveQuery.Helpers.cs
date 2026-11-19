using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace ErpImport;

public static partial class OperationsLiveQuery
{
    static void EnsureSql(DbConnection source)
    {
        if (source is not SqlConnection) throw new ImportFailure("live_ops_requires_sql");
    }

    static bool IsMissingTable(SqlException ex) =>
        ex.Number is 208 or 207 || ex.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase);

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
            Add(cmd, prefix + "json", System.Text.Json.JsonSerializer.Serialize(values));
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

    static List<JsonObject> ReadMapped(DbCommand cmd, Func<IDataRecord, JsonObject> map, CancellationToken ct)
    {
        using var reader = cmd.ExecuteReader();
        var rows = new List<JsonObject>();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(map(reader));
        }
        return rows;
    }

    static JsonArray ParseJsonArray(IDataRecord r, string column)
    {
        try
        {
            int ord = r.GetOrdinal(column);
            if (r.IsDBNull(ord)) return new JsonArray();
            string json = Convert.ToString(r.GetValue(ord), CultureInfo.InvariantCulture) ?? "";
            if (string.IsNullOrWhiteSpace(json)) return new JsonArray();
            return JsonNode.Parse(json) as JsonArray ?? new JsonArray();
        }
        catch (IndexOutOfRangeException) { return new JsonArray(); }
        catch (System.Text.Json.JsonException) { return new JsonArray(); }
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

    static string VehicleTypeName(string code) => code switch
    {
        "1" => "دراجة",
        "2" => "سيارة",
        "3" => "فان",
        "4" => "شاحنة",
        "5" => "أخرى",
        "Motorcycle" => "دراجة",
        "Car" => "سيارة",
        _ => code
    };

    static string FuelProviderLabel(string code) => code switch
    {
        "1" => "PetroApp",
        "2" => "SayaraApp",
        "PetroApp" => "PetroApp",
        "SayaraApp" => "SayaraApp",
        _ => code
    };

    static string HousingStatusLabel(string code) => code switch
    {
        "1" => "مسودة",
        "2" => "نشط",
        "3" => "غير نشط",
        "4" => "مغلق",
        "5" => "مؤرشف",
        _ => code
    };

    static string CityStatusLabel(string code) => code switch
    {
        "1" => "نشط",
        "2" => "موقوف",
        "3" => "مغلق",
        _ => code
    };

    /// <summary>C# port of Omar BillingDayBoundary + Days for unit tests (no SQL).</summary>
    public static decimal DaysInPeriod(DateTimeOffset start, DateTimeOffset end, DateOnly from, DateOnly to)
    {
        int firstDay = BillingDayBoundary(start);
        int endDayExclusive = BillingDayBoundary(end);
        firstDay = Math.Max(firstDay, from.DayNumber);
        endDayExclusive = Math.Min(endDayExclusive, to.DayNumber + 1);
        return Math.Max(0, endDayExclusive - firstDay);
    }

    public static int BillingDayBoundary(DateTimeOffset timestamp)
    {
        var local = timestamp.ToOffset(RiyadhOffset);
        return DateOnly.FromDateTime(local.DateTime).DayNumber + (local.TimeOfDay > BillingCutoff ? 1 : 0);
    }

    public static decimal? CostInPeriodSar(decimal days, string vehicleType) => vehicleType switch
    {
        "2" or "Car" => Math.Round(days * (MonthlyCostCarSar / 30m), 2, MidpointRounding.AwayFromZero),
        "1" or "Motorcycle" => Math.Round(days * (MonthlyCostMotorcycleSar / 30m), 2, MidpointRounding.AwayFromZero),
        _ => null
    };

    static long NumLong(JsonObject row, string key)
    {
        if (row[key] is null) return 0;
        return long.TryParse(row[key]!.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    static decimal NumDec(JsonObject row, string key)
    {
        if (row[key] is null) return 0;
        return decimal.TryParse(row[key]!.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }
}
