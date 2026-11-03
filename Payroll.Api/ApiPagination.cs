using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Payroll;

public static class ApiPagination
{
    static readonly HashSet<string> Lists = ["/api/riders", "/api/debts", "/api/finance/journals", "/api/submissions", "/api/audit", "/api/ledger", "/api/approvals", "/api/campaigns", "/api/forms/builder"];
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
    public static object Apply(IEnumerable<JsonObject> source, IQueryCollection query, bool always = false)
    {
        var rows=source.ToList();
        if(!always && !query.ContainsKey("page"))return rows;
        int Positive(string key,int fallback)
        {
            if(!query.ContainsKey(key))return fallback;
            if(!int.TryParse(query[key],NumberStyles.None,CultureInfo.InvariantCulture,out int value)||value<1)throw new ApiError("رقم الصفحة وحجمها يجب أن يكونا عددين صحيحين موجبين.");
            return value;
        }
        int page=Positive("page",1),pageSize=Math.Min(Positive("pageSize",50),200);
        string q=query["q"].ToString().Trim(),sort=query["sort"].ToString();
        if(q.Length>0)rows=rows.Where(r=>Text(r).Contains(q,StringComparison.OrdinalIgnoreCase)).ToList();
        if(sort.Length>0)
        {
            string field=sort.TrimStart('-');
            if(!Regex.IsMatch(field,"^[A-Za-z][A-Za-z0-9_]*$"))throw new ApiError("حقل الترتيب غير صالح.");
            var comparer=Comparer<JsonNode?>.Create((a,b)=>a?.GetValueKind()==JsonValueKind.Number&&b?.GetValueKind()==JsonValueKind.Number&&decimal.TryParse(a.ToJsonString(),NumberStyles.Number,CultureInfo.InvariantCulture,out var an)&&decimal.TryParse(b.ToJsonString(),NumberStyles.Number,CultureInfo.InvariantCulture,out var bn)?an.CompareTo(bn):StringComparer.OrdinalIgnoreCase.Compare(a?.ToString(),b?.ToString()));
            rows=(sort.StartsWith('-')?rows.OrderByDescending(r=>r[field],comparer):rows.OrderBy(r=>r[field],comparer)).ToList();
        }
        long offset=((long)page-1)*pageSize;
        return new { items=offset>=rows.Count?new List<JsonObject>():rows.Skip((int)offset).Take(pageSize).ToList(), total=rows.Count, page, pageSize };
    }
    static string Text(JsonNode? node)=>node switch
    {
        JsonObject obj=>string.Join(' ',obj.Select(p=>Text(p.Value))),
        JsonArray array=>string.Join(' ',array.Select(Text)), _=>node?.ToString()??""
    };
}
