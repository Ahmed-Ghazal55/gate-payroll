using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static class GlobalSearch
{
    public sealed record Hit(string Type,string Id,string Title,string Subtitle,string Url);
    public static IReadOnlyList<Hit> Search(DbConnection c,ClaimsPrincipal user,string query)
    {
        string q=query.Trim();J.Need(q.Length is >=2 and <=200,"اكتب حرفين على الأقل وبحد أقصى 200 حرف للبحث.");
        bool Match(params string[] values)=>values.Any(value=>value.Contains(q,StringComparison.OrdinalIgnoreCase));
        var hits=new List<Hit>();
        // Use the same policy gates, projections and record scope as each collection.
        if(AccessPolicy.Allowed(user,"GET","/api/riders"))
            hits.AddRange(Store.List(c,"rider").Where(ErpRiderClassification.ListedAsRider).Where(r=>Match(r.S("iqama"),r.S("name"))).Select(r=>new Hit("rider",r.S("iqama",r.S("id")),r.S("name"),r.S("iqama"),"/app/riders?iqama="+Uri.EscapeDataString(r.S("iqama",r.S("id"))))));
        if(AccessPolicy.Allowed(user,"GET","/api/companies"))
            hits.AddRange(Companies.List(c,user).Where(r=>r.S("status")!="archived"&&Match(r.S("nameAr"),r.S("shortNameAr"),r.S("code"))).Select(r=>new Hit("company",r.S("id"),r.S("nameAr"),r.S("code"),"/app/companies?id="+Uri.EscapeDataString(r.S("id")))));
        if(AccessPolicy.Allowed(user,"GET","/api/debts"))
            hits.AddRange(Store.List(c,"debt").Where(r=>FinancialScopes.Debt(user,r)&&Match(r.S("id"),r.S("iqama"),r.S("riderName"),r.S("description"),r.S("sourceRef"))).Select(r=>new Hit("debt",r.S("id"),r.S("description",r.S("sourceRef",r.S("id"))),r.S("iqama"),"/app/debts?id="+Uri.EscapeDataString(r.S("id")))));
        if(AccessPolicy.Allowed(user,"GET","/api/campaigns"))
            hits.AddRange(Store.List(c,"campaign").Where(r=>Match(r.S("title"))).Select(r=>new Hit("campaign",r.S("id"),r.S("title"),r.S("status"),"/app/forms?campaignId="+Uri.EscapeDataString(r.S("id")))));
        hits.AddRange(Vouchers.Search(c,user,q));
        return hits.DistinctBy(h=>(h.Type,h.Id)).Take(20).ToList();
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/search",(string q,HttpContext ctx)=>store.Read(c=>Search(c,ctx.User,q))).WithTags("Home").WithSummary("Search up to twenty permitted riders, companies, debts and campaigns");
    }
}
