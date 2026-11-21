using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Payroll;

public static class ApiPagination
{
    // /api/debts pages in Debts.ListPaged (SQL); keep it out of the in-memory filter.
    static readonly Dictionary<string, HashSet<string>> RouteWhitelists = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/api/riders"] = new(StringComparer.OrdinalIgnoreCase) { "id", "iqama", "name", "phone", "nationality", "status", "sponsorId", "sponsor", "platforms", "city", "housing", "jobTitle", "engagementType", "workType" },
        ["/api/finance/journals"] = new(StringComparer.OrdinalIgnoreCase) { "id", "entryNumber", "companyId", "company", "date", "entryDate", "period", "description", "source", "status", "totalDebit", "totalCredit", "createdAt", "createdBy", "reference" },
        ["/api/submissions"] = new(StringComparer.OrdinalIgnoreCase) { "id", "campaignId", "formId", "riderId", "iqama", "riderName", "status", "submittedAt", "createdAt", "reviewedAt", "reviewedBy", "companyId" },
        ["/api/audit"] = new(StringComparer.OrdinalIgnoreCase) { "id", "at", "createdAt", "actor", "action", "entityType", "entityId", "target", "summary", "status", "companyId" },
        ["/api/ledger"] = new(StringComparer.OrdinalIgnoreCase) { "id", "riderId", "iqama", "riderName", "companyId", "date", "at", "createdAt", "type", "kind", "category", "amount", "debit", "credit", "balance", "description", "reference", "status" },
        ["/api/approvals"] = new(StringComparer.OrdinalIgnoreCase) { "id", "type", "kind", "entityType", "entityId", "status", "requestedBy", "requestedAt", "createdAt", "decidedBy", "decidedAt", "companyId", "amount", "description", "reason" },
        ["/api/campaigns"] = new(StringComparer.OrdinalIgnoreCase) { "id", "title", "titleAr", "name", "status", "kind", "type", "period", "app", "platform", "companyId", "createdAt", "expiresAt", "startsAt", "endsAt" },
        ["/api/forms/builder"] = new(StringComparer.OrdinalIgnoreCase) { "id", "title", "titleAr", "name", "slug", "status", "kind", "category", "createdAt", "updatedAt", "createdBy" }
    };

    static readonly HashSet<string> DeniedSortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "passwordHash", "password", "secret", "token", "refreshToken", "accessToken", "iban", "personalIban", "mudadIban", "pin", "vin", "Vin"
    };

    public static async ValueTask<object?> Filter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        var request = context.HttpContext.Request;
        string path = request.Path.Value ?? "";
        if(request.Method != "GET" || !RouteWhitelists.TryGetValue(path, out var whitelist) || !request.Query.ContainsKey("page")) return result;
        var value = result is IValueHttpResult wrapped ? wrapped.Value : result;
        var rows = JsonSerializer.SerializeToNode(value, JsonSerializerOptions.Web) as JsonArray
            ?? throw new InvalidOperationException("A paginated list must return an array.");
        return Apply(rows.OfType<JsonObject>(), request.Query, whitelist: whitelist);
    }
    public static object Apply(IEnumerable<JsonObject> source, IQueryCollection query, bool always = false, IReadOnlySet<string>? whitelist = null)
    {
        var rows=source.ToList();
        if(!always && !query.ContainsKey("page"))
        {
            if(whitelist != null && TableFilterContract.HasJsonContractParams(query))
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
        if(whitelist != null)
        {
            if(query.ContainsKey("filters")||query.ContainsKey("sort"))
                rows=TableFilterContract.Apply(rows,query,whitelist);
        }
        else if(query.ContainsKey("filters") && !string.IsNullOrWhiteSpace(query["filters"].ToString()))
        {
            throw new ApiError("تصفية الأعمدة غير مفعّلة لهذا المسار.", 400);
        }
        else if(query.ContainsKey("sort") && !string.IsNullOrWhiteSpace(query["sort"].ToString()))
        {
            string rawSort = query["sort"].ToString().Trim();
            if(rawSort.StartsWith('['))
                throw new ApiError("ترتيب الأعمدة المركب غير مفعّل لهذا المسار.", 400);
            string field = rawSort.TrimStart('-').Trim();
            if(DeniedSortColumns.Contains(field))
                throw new ApiError($"حقل الترتيب غير صالح: {field}", 400);
            rows=TableFilterContract.Apply(rows,query,whitelist: null);
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
