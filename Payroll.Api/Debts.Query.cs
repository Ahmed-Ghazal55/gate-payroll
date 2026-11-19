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

    /// <summary>
    /// List debts with optional SQL filters. Without <c>page</c>, returns the legacy JSON array
    /// (backward compatible). With <c>page</c>, returns <c>{items,total,page,pageSize}</c>.
    /// </summary>
    public static object ListPaged(DbConnection c, ClaimsPrincipal actor, IQueryCollection query)
    {
        var filter = ParseFilter(query);
        bool paged = query.ContainsKey("page");
        int page = 1, pageSize = 50;
        if (paged)
        {
            page = Positive(query, "page", 1);
            pageSize = Math.Min(Positive(query, "pageSize", 50), 200);
        }
        string sort = query["sort"].ToString();
        var (where, parms) = BuildWhere(c, actor, filter);
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

    /// <summary>Lower + Arabic letter/digit fold for LIKE search (mirrors TextSearch.Normalize).</summary>
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
        string folded = $"{lower}({e})";
        foreach (var pair in folds)
            folded = $"REPLACE({folded},{Lit(pair[0])},{Lit(pair[1])})";
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

    static string OrderBy(DbConnection c, string sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return c is SqliteConnection
                ? " ORDER BY rowid DESC"
                : " ORDER BY COALESCE(JSON_VALUE(body,'$.createdAt'),JSON_VALUE(body,'$.at'),'') DESC, id DESC";
        }
        bool desc = sort.StartsWith('-');
        string field = sort.TrimStart('-');
        if (!Regex.IsMatch(field, "^[A-Za-z][A-Za-z0-9_]*$")) throw new ApiError("حقل الترتيب غير صالح.");
        string expr = RiderDataIndexes.FieldExpr(c, field);
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
