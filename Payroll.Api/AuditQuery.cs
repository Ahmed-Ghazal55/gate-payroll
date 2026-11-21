using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Payroll;

/// <summary>
/// Always-paged audit list with date/search filters applied in SQL (M-0607 / FU1-d).
/// Legacy-free: response is always <c>{items,total,page,pageSize}</c>.
/// </summary>
public static class AuditQuery
{
    public static object ListPaged(DbConnection c, IQueryCollection query)
    {
        int page = Positive(query, "page", 1);
        int pageSize = Math.Min(Positive(query, "pageSize", 50), 200);
        var (where, parms) = BuildWhere(c, query);
        long total = Count(c, where, parms);
        long offset = ((long)page - 1) * pageSize;
        var items = offset >= total
            ? new List<JsonObject>()
            : ReadBodies(c, where, parms, OrderBy(c), pageSize, offset);
        return new { items, total, page, pageSize };
    }

    static (string Where, List<(string Name, object Value)> Parms) BuildWhere(DbConnection c, IQueryCollection query)
    {
        var parms = new List<(string Name, object Value)>();
        var sb = new StringBuilder("kind='audit'");
        string atExpr = RiderDataIndexes.FieldExpr(c, "at");
        string actionExpr = RiderDataIndexes.FieldExpr(c, "action");
        string actorExpr = RiderDataIndexes.FieldExpr(c, "actor");
        string idExpr = c is SqliteConnection ? "id" : "id";
        string entityExpr = RiderDataIndexes.FieldExpr(c, "entityId");
        string noteExpr = RiderDataIndexes.FieldExpr(c, "note");

        string from = query["from"].ToString().Trim();
        string to = query["to"].ToString().Trim();
        if (from.Length > 0)
        {
            if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsedFrom))
                throw new ApiError("تاريخ البداية غير صالح.");
            sb.Append(" AND ").Append(atExpr).Append(" >= @fromAt");
            parms.Add(("@fromAt", parsedFrom.ToString("O", CultureInfo.InvariantCulture)));
        }
        if (to.Length > 0)
        {
            if (!DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsedTo))
                throw new ApiError("تاريخ النهاية غير صالح.");
            sb.Append(" AND ").Append(atExpr).Append(" <= @toAt");
            parms.Add(("@toAt", parsedTo.ToString("O", CultureInfo.InvariantCulture)));
        }

        string action = query["action"].ToString().Trim();
        if (action.Length > 0 && !string.Equals(action, "all", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(" AND ").Append(actionExpr).Append(" LIKE @action ESCAPE '\\'");
            parms.Add(("@action", "%" + EscapeLike(action) + "%"));
        }

        string q = (query.ContainsKey("q") ? query["q"] : query["search"]).ToString().Trim();
        if (q.Length > 0)
        {
            string like = "%" + EscapeLike(TextSearch.Normalize(q)) + "%";
            string actionFold = SqlFold(c, actionExpr);
            string actorFold = SqlFold(c, actorExpr);
            string idFold = SqlFold(c, idExpr);
            string entityFold = SqlFold(c, entityExpr);
            string noteFold = SqlFold(c, noteExpr);
            sb.Append(" AND (").Append(actionFold).Append(" LIKE @q ESCAPE '\\' OR ")
                .Append(actorFold).Append(" LIKE @q ESCAPE '\\' OR ")
                .Append(idFold).Append(" LIKE @q ESCAPE '\\' OR ")
                .Append(entityFold).Append(" LIKE @q ESCAPE '\\' OR ")
                .Append(noteFold).Append(" LIKE @q ESCAPE '\\')");
            parms.Add(("@q", like));
        }
        return (sb.ToString(), parms);
    }

    static string OrderBy(DbConnection c) =>
        c is SqliteConnection
            ? " ORDER BY COALESCE(json_extract(body,'$.at'),'') DESC, id DESC"
            : " ORDER BY COALESCE(JSON_VALUE(body,'$.at'),'') DESC, id DESC";

    static long Count(DbConnection c, string where, List<(string Name, object Value)> parms)
    {
        using var cmd = Store.Command(c, null, "SELECT COUNT(*) FROM records WHERE " + where, parms.ToArray());
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    static List<JsonObject> ReadBodies(DbConnection c, string where, List<(string Name, object Value)> parms, string orderBy, int take, long offset)
    {
        var sql = new StringBuilder("SELECT body FROM records WHERE ").Append(where).Append(orderBy);
        var all = parms.ToList();
        if (c is SqliteConnection)
        {
            sql.Append(" LIMIT @take OFFSET @offset");
            all.Add(("@take", take));
            all.Add(("@offset", offset));
        }
        else
        {
            sql.Append(" OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY");
            all.Add(("@offset", offset));
            all.Add(("@take", take));
        }
        using var cmd = Store.Command(c, null, sql.ToString(), all.ToArray());
        using var r = cmd.ExecuteReader();
        var rows = new List<JsonObject>();
        while (r.Read()) rows.Add(JsonNode.Parse(r.GetString(0))!.AsObject());
        return rows;
    }

    static int Positive(IQueryCollection query, string key, int fallback)
    {
        if (!query.ContainsKey(key)) return fallback;
        if (!int.TryParse(query[key], NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 1)
            throw new ApiError("رقم الصفحة وحجمها يجب أن يكونا عددين صحيحين موجبين.");
        return value;
    }

    static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>Lower + Arabic fold for LIKE (same idea as Debts.Query.SqlFold).</summary>
    static string SqlFold(DbConnection c, string expr)
    {
        bool sqlite = c is SqliteConnection;
        string lower = sqlite ? "lower" : "LOWER";
        string e = $"COALESCE({expr},'')";
        string Lit(string s) => sqlite ? "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'" : "N'" + s.Replace("'", "''", StringComparison.Ordinal) + "'";
        string[][] folds =
        [
            ["أ", "ا"], ["إ", "ا"], ["آ", "ا"], ["ٱ", "ا"],
            ["ة", "ه"], ["ى", "ي"], ["ؤ", "و"], ["ئ", "ي"],
            ["٠", "0"], ["١", "1"], ["٢", "2"], ["٣", "3"], ["٤", "4"],
            ["٥", "5"], ["٦", "6"], ["٧", "7"], ["٨", "8"], ["٩", "9"]
        ];
        string folded = $"{lower}({e})";
        foreach (var pair in folds)
            folded = $"REPLACE({folded},{Lit(pair[0])},{Lit(pair[1])})";
        return folded;
    }
}
