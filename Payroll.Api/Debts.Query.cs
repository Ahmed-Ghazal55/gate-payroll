using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Payroll;

public static partial class Debts
{
    static readonly HashSet<string> AdvanceCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Advance", "CashAdvance", "BankAdvance"
    };

    static readonly HashSet<string> AllowedDebtColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "riderName", "iqama", "creditorSponsorId", "originSponsorId",
        "companyId", "company", "category", "categoryAr", "platform",
        "originalAmount", "totalAmount", "paidAmount", "remainingBalance", "monthlyInstallment",
        "status", "source", "entryMode", "createdAt", "approvedAt", "at", "notes", "reference", "description"
    };

    static readonly Dictionary<string, string> DebtColumnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["companyId"] = "creditorSponsorId",
        ["company"] = "creditorSponsorId"
    };

    /// <summary>
    /// List debts with optional SQL filters and generic Excel-style filter/sort contract (FL2).
    /// Without <c>page</c>, returns the legacy JSON array (backward compatible).
    /// With <c>page</c>, returns <c>{items,total,page,pageSize}</c>.
    /// </summary>
    public static object ListPaged(DbConnection c, ClaimsPrincipal actor, IQueryCollection query)
    {
        // Validate generic filter/sort contract against whitelist when contract params (filters / JSON sort) are present
        if (TableFilterContract.HasContractParams(query))
        {
            _ = TableFilterContract.ParseFilters(query, AllowedDebtColumns, DebtColumnAliases);
            _ = TableFilterContract.ParseSort(query, AllowedDebtColumns, DebtColumnAliases);
        }

        var filter = ParseFilter(query);
        bool paged = query.ContainsKey("page");
        int page = 1, pageSize = 50;
        if (paged)
        {
            page = Positive(query, "page", 1);
            pageSize = Math.Min(Positive(query, "pageSize", 50), 200);
        }
        var (where, parms) = BuildWhere(c, actor, filter);

        if (TableFilterContract.HasContractParams(query))
        {
            var allViews = ReadBodies(c, where, parms, OrderBy(c, ""), null, null)
                .Select(d => View(c, null, actor, d))
                .ToList();
            var filtered = TableFilterContract.Apply(allViews, query, AllowedDebtColumns, DebtColumnAliases);
            if (!paged) return filtered;
            long filteredTotal = filtered.Count;
            long filteredOffset = ((long)page - 1) * pageSize;
            var pageItems = filteredOffset >= filteredTotal
                ? new List<JsonObject>()
                : filtered.Skip((int)filteredOffset).Take(pageSize).ToList();
            return new { items = pageItems, total = filteredTotal, page, pageSize };
        }

        string sort = query["sort"].ToString();
        long total = Count(c, where, parms);
        if (!paged)
        {
            var all = ReadBodies(c, where, parms, OrderBy(c, sort), null, null);
            return all.Select(d => View(c, null, actor, d)).ToList();
        }
        long offset = ((long)page - 1) * pageSize;
        var items = offset >= total
            ? new List<JsonObject>()
            : ReadBodies(c, where, parms, OrderBy(c, sort), pageSize, offset).Select(d => View(c, null, actor, d)).ToList();
        return new { items, total, page, pageSize };
    }

    static readonly Dictionary<string, string> DistinctColumnMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["status"] = "status",
        ["category"] = "category",
        ["categoryAr"] = "categoryAr",
        ["creditorSponsorId"] = "creditorSponsorId",
        ["originSponsorId"] = "originSponsorId",
        ["companyId"] = "creditorSponsorId",
        ["company"] = "creditorSponsorId",
        ["platform"] = "platform",
        ["source"] = "source",
        ["entryMode"] = "entryMode",
        ["iqama"] = "iqama",
        ["riderName"] = "riderName"
    };

    /// <summary>Light scoped distinct values (with counts) for DataTable column filters.</summary>
    public static JsonObject Distinct(DbConnection c, ClaimsPrincipal actor, IQueryCollection query)
    {
        _ = TableFilterContract.ParseFilters(query, AllowedDebtColumns, DebtColumnAliases);
        string rawCol = (query["column"].ToString() ?? "").Trim();
        if (!AllowedDebtColumns.Contains(rawCol))
            return new JsonObject { ["column"] = rawCol, ["values"] = new JsonArray(), ["counts"] = new JsonObject() };

        var filter = ParseFilter(query) with { Status = null, Category = null, Search = null };
        var (where, parms) = BuildWhere(c, actor, filter);

        if (TableFilterContract.HasContractParams(query) || !DistinctColumnMap.TryGetValue(rawCol, out string? field))
        {
            var allViews = ReadBodies(c, where, parms, OrderBy(c, ""), null, null)
                .Select(d => View(c, null, actor, d));
            return TableFilterContract.BuildDistinct(allViews, rawCol, query, AllowedDebtColumns, DebtColumnAliases);
        }

        string expr = RiderDataIndexes.FieldExpr(c, field);
        bool sqlite = c is SqliteConnection;
        string sql = sqlite
            ? $"SELECT {expr} AS v, COUNT(*) AS cnt FROM records WHERE {where} AND {expr} IS NOT NULL AND {expr} <> '' GROUP BY {expr} ORDER BY v LIMIT 500"
            : $"SELECT TOP 500 {expr} AS v, COUNT(*) AS cnt FROM records WHERE {where} AND {expr} IS NOT NULL AND {expr} <> '' GROUP BY {expr} ORDER BY v";
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in parms)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = k;
            p.Value = v ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
        var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0)) continue;
            string v = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)?.Trim() ?? "";
            if (v.Length == 0) continue;
            int cnt = reader.IsDBNull(1) ? 1 : Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
            counts[v] = counts.TryGetValue(v, out int prev) ? prev + cnt : cnt;
        }
        var countsObj = new JsonObject();
        foreach (var (k, cnt) in counts) countsObj[k] = cnt;
        return new JsonObject
        {
            ["column"] = rawCol,
            ["values"] = new JsonArray(counts.Keys.Select(v => (JsonNode?)v).ToArray()),
            ["counts"] = countsObj
        };
    }

    /// <summary>Light SQL aggregates for debts stat cards (scoped like the list).</summary>
    public static JsonObject Summary(DbConnection c, ClaimsPrincipal actor, IQueryCollection query)
    {
        // Cards are global (old page behavior): ignore status/category/search page filters; company scope still applies.
        var filter = ParseFilter(query) with { Status = null, Category = null, Search = null };
        var (where, parms) = BuildWhere(c, actor, filter);
        bool sqlite = c is SqliteConnection;
        string rem = sqlite
            ? "CAST(COALESCE(json_extract(body,'$.remainingBalance'),0) AS REAL)"
            : "TRY_CAST(COALESCE(JSON_VALUE(body,'$.remainingBalance'),'0') AS decimal(18,2))";
        string status = RiderDataIndexes.FieldExpr(c, "status");
        string category = RiderDataIndexes.FieldExpr(c, "category");
        string entryMode = RiderDataIndexes.FieldExpr(c, "entryMode");
        string approvalRequestId = RiderDataIndexes.FieldExpr(c, "approvalRequestId");
        string sql = $"""
            SELECT
              COUNT(*) AS totalCount,
              COALESCE(SUM(CASE WHEN {status}='active' THEN {rem} ELSE 0 END),0) AS activeRemaining,
              COALESCE(SUM(CASE WHEN {category} IN ('Advance','CashAdvance','BankAdvance','advance') THEN {rem} ELSE 0 END),0) AS advanceRemaining,
              COALESCE(SUM(CASE WHEN {entryMode}='OpeningBalance' AND {status}='pending_approval' AND COALESCE({approvalRequestId},'')='' THEN 1 ELSE 0 END),0) AS pendingOpeningCount,
              COALESCE(SUM(CASE WHEN {entryMode}='OpeningBalance' AND {status}='pending_approval' AND COALESCE({approvalRequestId},'')='' THEN {rem} ELSE 0 END),0) AS pendingOpeningTotal
            FROM records WHERE {where}
            """;
        using var cmd = Store.Command(c, null, sql, parms.ToArray());
        using var r = cmd.ExecuteReader();
        r.Read();
        return new JsonObject
        {
            ["totalCount"] = Convert.ToInt64(r["totalCount"], CultureInfo.InvariantCulture),
            ["activeRemainingTotal"] = J.Money(Convert.ToDecimal(r["activeRemaining"], CultureInfo.InvariantCulture)),
            ["advanceRemainingTotal"] = J.Money(Convert.ToDecimal(r["advanceRemaining"], CultureInfo.InvariantCulture)),
            ["pendingOpeningCount"] = Convert.ToInt64(r["pendingOpeningCount"], CultureInfo.InvariantCulture),
            ["pendingOpeningTotal"] = J.Money(Convert.ToDecimal(r["pendingOpeningTotal"], CultureInfo.InvariantCulture))
        };
    }

    record DebtFilter(string? SponsorId, string? Category, string? Status, string? Iqama, string? Search);

    static DebtFilter ParseFilter(IQueryCollection query)
    {
        string search = (query.ContainsKey("q") ? query["q"] : query["search"]).ToString().Trim();
        return new(
            NullIfEmpty(query["sponsorId"].ToString()),
            NullIfEmpty(query["category"].ToString()),
            NullIfEmpty(query["status"].ToString()),
            NullIfEmpty(query["iqama"].ToString())?.Trim(),
            search.Length == 0 ? null : search
        );
    }

    static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) || value == "all" ? null : value.Trim();

    static int Positive(IQueryCollection query, string key, int fallback)
    {
        if (!query.ContainsKey(key)) return fallback;
        if (!int.TryParse(query[key], NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 1)
            throw new ApiError("رقم الصفحة وحجمها يجب أن يكونا عددين صحيحين موجبين.");
        return value;
    }

    static (string Where, List<(string Name, object Value)> Parms) BuildWhere(DbConnection c, ClaimsPrincipal actor, DebtFilter filter)
    {
        var parms = new List<(string Name, object Value)>();
        var sb = new StringBuilder("kind='debt'");
        string statusExpr = RiderDataIndexes.FieldExpr(c, "status");
        string categoryExpr = RiderDataIndexes.FieldExpr(c, "category");
        string sponsorExpr = RiderDataIndexes.FieldExpr(c, "creditorSponsorId");
        string platformExpr = RiderDataIndexes.FieldExpr(c, "platform");
        string iqamaExpr = RiderDataIndexes.FieldExpr(c, "iqama");
        string nameExpr = RiderDataIndexes.FieldExpr(c, "riderName");

        // Financial scope in SQL — same rows as FinancialScopes.Debt (admin bypasses the three IN checks).
        if (!AccessPolicy.Administrator(actor))
        {
            var categories = FinancialCore.CategoriesCatalog.Where(cat => FinancialScopes.Category(actor, cat.Code)).Select(cat => cat.Code).ToList();
            if (categories.Count == 0) { sb.Append(" AND 1=0"); return (sb.ToString(), parms); }
            AppendIn(sb, parms, categoryExpr, "cat", categories);

            // Sponsor aliases (id/code/name/…) like FindSponsor, so scoped users match stored codes/names.
            var sponsorKeys = SponsorScopeKeys(actor);
            bool allowEmptySponsor = FinancialScopes.AllCompanies(actor) && IsFinanceRole(actor);
            sb.Append(" AND (");
            if (sponsorKeys.Count > 0)
            {
                AppendIn(sb, parms, sponsorExpr, "sp", sponsorKeys, bare: true);
                if (allowEmptySponsor) sb.Append(" OR COALESCE(").Append(sponsorExpr).Append(",'')=''");
            }
            else if (allowEmptySponsor) sb.Append("COALESCE(").Append(sponsorExpr).Append(",'')=''");
            else sb.Append("1=0");
            sb.Append(')');

            var platforms = new[] { "Jahez", "Keeta", "HungerStation", "Ninja", "Chefz", "Amazon", "Other" }
                .Where(p => FinancialScopes.Platform(actor, p)).ToList();
            sb.Append(" AND (COALESCE(").Append(platformExpr).Append(",'')=''");
            if (platforms.Count > 0)
            {
                sb.Append(" OR ");
                AppendIn(sb, parms, platformExpr, "pf", platforms, bare: true);
            }
            sb.Append(')');
        }

        if (filter.SponsorId != null)
        {
            // Match canonical id plus FindSponsor aliases (stored rows may keep code/name).
            var aliases = SponsorAliases(FinancialCore.ResolveSponsor(filter.SponsorId));
            AppendIn(sb, parms, sponsorExpr, "filterSp", aliases);
        }
        if (filter.Status != null)
        {
            sb.Append(" AND ").Append(statusExpr).Append("=@filterSt");
            parms.Add(("@filterSt", filter.Status));
        }
        if (filter.Category != null)
        {
            // Only the "advance" tab expands the advance group; CashAdvance/BankAdvance/Advance stay exact.
            if (string.Equals(filter.Category, "advance", StringComparison.OrdinalIgnoreCase))
                AppendIn(sb, parms, categoryExpr, "adv", AdvanceCategories.Concat(["advance"]).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            else
            {
                sb.Append(" AND ").Append(categoryExpr).Append("=@filterCat");
                parms.Add(("@filterCat", filter.Category));
            }
        }
        if (filter.Iqama != null)
        {
            sb.Append(" AND ").Append(iqamaExpr).Append("=@filterIq");
            parms.Add(("@filterIq", filter.Iqama));
        }
        if (filter.Search != null)
        {
            string like = "%" + EscapeLike(TextSearch.Normalize(filter.Search)) + "%";
            // Fold Arabic letters / digits in SQL so Typed أ and ا match (same rules as TextSearch.Normalize).
            string iqamaFold = SqlFold(c, iqamaExpr);
            string nameFold = SqlFold(c, nameExpr);
            sb.Append(" AND (").Append(iqamaFold).Append(" LIKE @q ESCAPE '\\' OR ").Append(nameFold).Append(" LIKE @q ESCAPE '\\')");
            parms.Add(("@q", like));
        }
        return (sb.ToString(), parms);
    }

    /// <summary>All string keys FindSponsor would resolve to an in-scope sponsor (id, code, names, regs).</summary>
    static List<string> SponsorScopeKeys(ClaimsPrincipal actor)
    {
        var keys = new List<string>();
        foreach (var sp in FinancialCore.Sponsors.Where(s => FinancialScopes.Sponsor(actor, s.Id)))
            keys.AddRange(SponsorAliases(sp));
        return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static List<string> SponsorAliases(FinancialCore.SponsorDef sp) =>
    [
        sp.Id, sp.Code, sp.UnifiedNumber, sp.CommercialReg, sp.NameAr, sp.ShortNameAr
    ];

    /// <summary>Lower + Arabic letter/digit/harakat/tatweel/space fold for LIKE search (mirrors TextSearch.Normalize).</summary>
    static string SqlFold(DbConnection c, string expr)
    {
        bool sqlite = c is SqliteConnection;
        string lower = sqlite ? "lower" : "LOWER";
        string e = $"COALESCE({expr},'')";
        // Nested REPLACE keeps the predicate portable on SQLite and SQL Server.
        // SQL Server needs N'…' literals so Arabic letters are not corrupted as varchar.
        string Lit(string s) => sqlite ? "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'" : "N'" + s.Replace("'", "''", StringComparison.Ordinal) + "'";
        string[][] folds =
        [
            ["أ", "ا"], ["إ", "ا"], ["آ", "ا"], ["ٱ", "ا"],
            ["ة", "ه"], ["ى", "ي"], ["ؤ", "و"], ["ئ", "ي"],
            ["٠", "0"], ["١", "1"], ["٢", "2"], ["٣", "3"], ["٤", "4"],
            ["٥", "5"], ["٦", "6"], ["٧", "7"], ["٨", "8"], ["٩", "9"],
            ["۰", "0"], ["۱", "1"], ["۲", "2"], ["۳", "3"], ["۴", "4"],
            ["۵", "5"], ["۶", "6"], ["۷", "7"], ["۸", "8"], ["۹", "9"]
        ];
        // Strip tatweel + harakat (same removals as TextSearch.Normalize) from stored names.
        string[] strip =
        [
            "ـ",
            "ً", "ٌ", "ٍ", "َ", "ُ", "ِ", "ّ", "ْ", "ٓ", "ٔ", "ٕ", "ٖ", "ٗ", "٘", "ٙ", "ٚ", "ٛ", "ٜ", "ٝ", "ٞ", "ٟ", "ٰ"
        ];
        string folded = $"{lower}({e})";
        foreach (var pair in folds)
            folded = $"REPLACE({folded},{Lit(pair[0])},{Lit(pair[1])})";
        foreach (string ch in strip)
            folded = $"REPLACE({folded},{Lit(ch)},{Lit("")})";
        // Collapse runs of spaces (a few passes cover typical stored names).
        for (int i = 0; i < 4; i++)
            folded = $"REPLACE({folded},{Lit("  ")},{Lit(" ")})";
        return folded;
    }

    static void AppendIn(StringBuilder sb, List<(string Name, object Value)> parms, string expr, string prefix, IReadOnlyList<string> values, bool bare = false)
    {
        if (!bare) sb.Append(" AND ");
        sb.Append(expr).Append(" IN (");
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0) sb.Append(',');
            string name = $"@{prefix}{i}";
            sb.Append(name);
            parms.Add((name, values[i]));
        }
        sb.Append(')');
    }

    static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    static bool IsFinanceRole(ClaimsPrincipal actor)
    {
        string role = actor.FindFirstValue(ClaimTypes.Role) ?? "";
        return role is "Admin" or "SuperAdmin" or "Accountant" or "FinancialAccountant" or "FinancialManager";
    }

    static readonly HashSet<string> NumericSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "remainingBalance", "totalAmount", "monthlyInstallment", "originalAmount", "paidAmount"
    };

    static string OrderBy(DbConnection c, string sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            // Match Store.List default: createdAt / at / approvedAt, then id (Store.cs:111).
            return c is SqliteConnection
                ? " ORDER BY rowid DESC"
                : " ORDER BY COALESCE(JSON_VALUE(body,'$.createdAt'),JSON_VALUE(body,'$.at'),JSON_VALUE(body,'$.approvedAt'),'') DESC, id DESC";
        }
        bool desc = sort.StartsWith('-');
        string field = sort.TrimStart('-');
        if (!Regex.IsMatch(field, "^[A-Za-z][A-Za-z0-9_]*$")) throw new ApiError("حقل الترتيب غير صالح.");
        string expr = RiderDataIndexes.FieldExpr(c, field);
        if (NumericSortFields.Contains(field))
            expr = c is SqliteConnection
                ? $"CAST({expr} AS REAL)"
                : $"TRY_CAST({expr} AS decimal(18,2))";
        string dir = desc ? " DESC" : " ASC";
        return c is SqliteConnection
            ? $" ORDER BY {expr}{dir}, rowid DESC"
            : $" ORDER BY {expr}{dir}, id DESC";
    }

    static long Count(DbConnection c, string where, List<(string Name, object Value)> parms)
    {
        using var cmd = Store.Command(c, null, "SELECT COUNT(*) FROM records WHERE " + where, parms.ToArray());
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    static List<JsonObject> ReadBodies(DbConnection c, string where, List<(string Name, object Value)> parms, string orderBy, int? take, long? offset)
    {
        var sql = new StringBuilder("SELECT body FROM records WHERE ").Append(where).Append(orderBy);
        var all = parms.ToList();
        if (take.HasValue)
        {
            if (c is SqliteConnection)
            {
                sql.Append(" LIMIT @take OFFSET @offset");
                all.Add(("@take", take.Value));
                all.Add(("@offset", offset ?? 0L));
            }
            else
            {
                sql.Append(" OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY");
                all.Add(("@offset", offset ?? 0L));
                all.Add(("@take", take.Value));
            }
        }
        using var cmd = Store.Command(c, null, sql.ToString(), all.ToArray());
        using var r = cmd.ExecuteReader();
        var rows = new List<JsonObject>();
        while (r.Read()) rows.Add(JsonNode.Parse(r.GetString(0))!.AsObject());
        return rows;
    }
}
