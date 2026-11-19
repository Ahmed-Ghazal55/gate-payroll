using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Payroll;

public static class ApiPagination
{
    // /api/debts pages in Debts.ListPaged (SQL); keep it out of the in-memory filter.
    static readonly HashSet<string> Lists = ["/api/riders", "/api/finance/journals", "/api/submissions", "/api/audit", "/api/ledger", "/api/approvals", "/api/campaigns", "/api/forms/builder"];
    public static async ValueTask<object?> Filter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        var request = context.HttpContext.Request;
        if(request.Method != "GET" || !Lists.Contains(request.Path.Value ?? "") || !request.Query.ContainsKey("page")) return result;
        var value = result is IValueHttpResult wrapped ? wrapped.Value : result;
        var rows = JsonSerializer.SerializeToNode(value, JsonSerializerOptions.Web) as JsonArray
            ?? throw new InvalidOperationException("A paginated list must return an array.");
        return Apply(rows.OfType<JsonObject>(), request.Query);
    }
    public static object Apply(IEnumerable<JsonObject> source, IQueryCollection query, bool always = false, IReadOnlySet<string>? whitelist = null)
    {
        var rows=source.ToList();
        if(!always && !query.ContainsKey("page"))
        {
            if(TableFilterContract.HasContractParams(query))
                rows=TableFilterContract.Apply(rows,query,whitelist);
            return rows;
        }
        int Positive(string key,int fallback)
        {
            if(!query.ContainsKey(key))return fallback;
            if(!int.TryParse(query[key],NumberStyles.None,CultureInfo.InvariantCulture,out int value)||value<1)throw new ApiError("رقم الصفحة وحجمها يجب أن يكونا عددين صحيحين موجبين.");
            return value;
        }
        int page=Positive("page",1),pageSize=Math.Min(Positive("pageSize",50),200);
        // `search` is the older UI name for `q`; both use the shared Arabic/digit normaliser.
        string q=(query.ContainsKey("q")?query["q"]:query["search"]).ToString().Trim();
        if(q.Length>0)rows=rows.Where(r=>TextSearch.Matches(q,Text(r))).ToList();
        if(query.ContainsKey("filters")||query.ContainsKey("sort"))
            rows=TableFilterContract.Apply(rows,query,whitelist);
        long offset=((long)page-1)*pageSize;
        return new { items=offset>=rows.Count?new List<JsonObject>():rows.Skip((int)offset).Take(pageSize).ToList(), total=rows.Count, page, pageSize };
    }
    static string Text(JsonNode? node)=>node switch
    {
        JsonObject obj=>string.Join(' ',obj.Select(p=>Text(p.Value))),
        JsonArray array=>string.Join(' ',array.Select(Text)), _=>node?.ToString()??""
    };
}
