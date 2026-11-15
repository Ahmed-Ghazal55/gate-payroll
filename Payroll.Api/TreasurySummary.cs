using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>Cash / bank / other balances per company from target CoA posting accounts only (no parent double-count).</summary>
public static class TreasurySummary
{
    public static JsonObject Build(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string? companyId)
    {
        AccessPolicy.Demand(actor,"treasury.view");
        var posting=FinanceViews.Accounts.Where(a=>a.B("posting")&&a.S("status")=="active").ToList();
        var lines=Store.List(c,"journal",tx).Where(FinanceViews.Posted).Where(j=>j.S("chart")=="target"||j.S("chart")=="")
            .SelectMany(j=>(j["lines"] as JsonArray??[]).OfType<JsonObject>())
            .Where(l=>FinancialScopes.Line(actor,l)).ToList();
        var companies=string.IsNullOrWhiteSpace(companyId)
            ?FinancialCore.Sponsors.Where(s=>FinancialScopes.Sponsor(actor,s.Id)).ToList()
            :[FinancialCore.ResolveSponsor(companyId!)];
        if(!string.IsNullOrWhiteSpace(companyId))FinancialScopes.DemandSponsor(actor,companies[0].Id);
        var rows=new JsonArray();
        foreach(var company in companies)
        {
            var sp=lines.Where(l=>l.S("sponsorId")==company.Id).ToList();
            decimal Balance(Func<JsonObject,bool> accountFilter)
            {
                var codes=posting.Where(accountFilter).Select(a=>a.S("code")).ToHashSet(StringComparer.Ordinal);
                return J.Money(sp.Where(l=>codes.Contains(l.S("accountCode"))).Sum(l=>l.N("debit")-l.N("credit")));
            }
            // Posting accounts only: CoA type cash/bank; remaining debit asset posting accounts → other.
            decimal cash=Balance(a=>a.S("type")=="cash");
            decimal bank=Balance(a=>a.S("type")=="bank");
            decimal assets=Balance(a=>a.S("nature")=="debit"&&a.S("code").StartsWith('1'));
            decimal other=J.Money(Math.Max(0,assets-cash-bank));
            rows.Add(new JsonObject
            {
                ["companyId"]=company.Id,["code"]=company.Code,["nameAr"]=company.ShortNameAr,
                ["cash"]=cash,["bank"]=bank,["other"]=other,["total"]=J.Money(cash+bank+other)
            });
        }
        return new(){["companies"]=rows,["totals"]=new JsonObject{
            ["cash"]=J.Money(rows.OfType<JsonObject>().Sum(r=>r.N("cash"))),
            ["bank"]=J.Money(rows.OfType<JsonObject>().Sum(r=>r.N("bank"))),
            ["other"]=J.Money(rows.OfType<JsonObject>().Sum(r=>r.N("other"))),
            ["total"]=J.Money(rows.OfType<JsonObject>().Sum(r=>r.N("total")))
        }};
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/finance/treasury/summary",(string? companyId,HttpContext ctx)=>store.Read(c=>Build(c,null,ctx.User,companyId)))
            .WithTags("Banks and treasury").WithSummary("Cash, bank and other balances per company from posting CoA accounts only");
    }
}
