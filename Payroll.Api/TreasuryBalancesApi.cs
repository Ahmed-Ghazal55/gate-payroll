using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-35: balances tree from fin.period_balance (+ Omar Jahez display nodes) and transfer contract.
/// Transfers need the payment engine (U-W2-07 on another branch) — return Arabic 409, never fake a transfer.</summary>
public static class TreasuryBalancesApi
{
    public const string TogaryMarker="togary";
    public static JsonObject Tree(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string? periodMonth,string? companyId)
    {
        AccessPolicy.Demand(actor,"treasury.view");
        DateOnly month=ParseMonth(periodMonth);
        EnsureSchemas(c,tx);
        var nodes=new JsonArray();
        var companies=string.IsNullOrWhiteSpace(companyId)
            ?FinancialCore.Sponsors.Where(s=>FinancialScopes.Sponsor(actor,s.Id)).ToList()
            :[FinancialCore.ResolveSponsor(companyId!)];
        if(!string.IsNullOrWhiteSpace(companyId))FinancialScopes.DemandSponsor(actor,companies[0].Id);

        foreach(var company in companies)
        {
            bool togary=IsTogary(company.Code,company.ShortNameAr,company.Id);
            var companyNode=new JsonObject
            {
                ["id"]="company:"+company.Id,["kind"]="company",["companyId"]=company.Id,
                ["code"]=company.Code,["nameAr"]=company.ShortNameAr,
                ["isAlTogary"]=togary,["children"]=new JsonArray(),
                ["cash"]=0m,["bank"]=0m,["custody"]=0m,["total"]=0m
            };
            var children=companyNode["children"]!.AsArray();

            // Book balances from period_balance; classify cash/bank via FinanceViews chart types.
            var chartType=FinanceViews.Accounts.Where(a=>a.B("posting")).ToDictionary(a=>a.S("code"),a=>a.S("type"),StringComparer.Ordinal);
            using(var cmd=Store.Command(c,tx,$"""
                SELECT a.code,a.name_ar,COALESCE(SUM(pb.closing),0) AS closing
                FROM {SchemaSql.Q(c,"fin","period_balance")} pb
                JOIN {SchemaSql.Q(c,"fin","account")} a ON a.id=pb.account_id
                WHERE pb.company_id=@c AND pb.period_month=@m
                GROUP BY a.code,a.name_ar
                ORDER BY a.code
                """,("@c",company.Id),("@m",MonthLiteral(c,month))))
            using(var r=cmd.ExecuteReader())
            while(r.Read())
            {
                string code=r.GetString(0);string name=r.IsDBNull(1)?code:r.GetString(1);
                decimal bal=J.Money(Convert.ToDecimal(r.GetValue(2),CultureInfo.InvariantCulture));
                string type=chartType.GetValueOrDefault(code,"");
                string kind=ClassifyTreasuryKind(code,type);
                if(kind=="")continue; // never "everything else is custody" — skip revenue/liability/etc.
                if(!FinancialScopes.TargetAccount(actor,company.Id,code))continue;
                children.Add(new JsonObject
                {
                    ["id"]="account:"+company.Id+":"+code,["kind"]=kind,["accountCode"]=code,
                    ["nameAr"]=name,["companyId"]=company.Id,["balance"]=bal,
                    ["isAlTogary"]=togary,["source"]="period_balance"
                });
                if(kind=="cash")companyNode["cash"]=J.Money(companyNode.N("cash")+bal);
                else if(kind=="bank")companyNode["bank"]=J.Money(companyNode.N("bank")+bal);
                else companyNode["custody"]=J.Money(companyNode.N("custody")+bal);
            }

            // Live Omar Jahez display nodes (read-only; never mixed into company cash totals).
            foreach(var row in Store.List(c,"erp_jahez_treasury_balance",tx).Where(b=>b.S("companyId")==company.Id||b.S("sponsorId")==company.Id))
            {
                children.Add(new JsonObject
                {
                    ["id"]="jahez:"+row.S("id",row.S("vaultCode")),["kind"]="jahez_live",
                    ["accountCode"]=row.S("vaultCode",row.S("code")),["nameAr"]=row.S("nameAr","خزينة جاهز (مباشر)"),
                    ["companyId"]=company.Id,["balance"]=J.Money(row.N("balance")),
                    ["isAlTogary"]=togary||IsTogary(row.S("vaultCode"),row.S("nameAr")),
                    ["source"]="omar_live",["displayOnly"]=true
                });
            }

            companyNode["total"]=J.Money(companyNode.N("cash")+companyNode.N("bank")+companyNode.N("custody"));
            nodes.Add(companyNode);
        }

        var actions=new JsonArray{"refresh"};
        if(AccessPolicy.Has(actor,"treasury.transfer")||AccessPolicy.Administrator(actor))actions.Add("request_transfer");
        if(AccessPolicy.Has(actor,"treasury.balances.by_registration"))actions.Add("by_registration");
        return new()
        {
            ["periodMonth"]=month.ToString("yyyy-MM",CultureInfo.InvariantCulture),
            ["items"]=nodes,
            ["totals"]=new JsonObject
            {
                ["cash"]=J.Money(nodes.OfType<JsonObject>().Sum(n=>n.N("cash"))),
                ["bank"]=J.Money(nodes.OfType<JsonObject>().Sum(n=>n.N("bank"))),
                ["custody"]=J.Money(nodes.OfType<JsonObject>().Sum(n=>n.N("custody"))),
                ["total"]=J.Money(nodes.OfType<JsonObject>().Sum(n=>n.N("total")))
            },
            ["allowedActions"]=actions,
            ["noteAr"]="أرصدة الدفتر من أرصدة الفترة؛ أرصدة جاهز المباشرة للعرض فقط ولا تُضاف لنقد الشركة."
        };
    }

    public static JsonObject RefuseTransfer(ClaimsPrincipal actor,JsonObject _)
    {
        AccessPolicy.Demand(actor,"treasury.transfer");
        // U-W2-07 payment engine is on another branch — contract exists, no fake transfer.
        throw new ApiError("التحويلات عبر محرك المدفوعات غير مدمجة بعد في هذا الفرع؛ انتظر دمج وحدة المدفوعات.",409);
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/treasury/balances/tree",(string? period,string? companyId,HttpContext ctx)=>
            store.Read(c=>Tree(c,null,ctx.User,period,companyId)))
            .WithTags("Banks and treasury").WithSummary("Treasury balances tree from period_balance + Omar Jahez display");
        api.MapPost("/treasury/transfers",(JsonObject input,HttpContext ctx)=>RefuseTransfer(ctx.User,input))
            .WithTags("Banks and treasury").WithSummary("Transfer contract — 409 until payment engine merge");
    }

    internal static bool IsTogary(params string?[] parts)=>
        parts.Any(p=>!string.IsNullOrWhiteSpace(p)&&(p!.Contains("togary",StringComparison.OrdinalIgnoreCase)||p.Contains("تجار",StringComparison.Ordinal)||p.Contains("التجارية",StringComparison.Ordinal)));

    /// <summary>Chart type/code ranges only: cash, bank, defined custody. Never dump residual accounts into custody.</summary>
    internal static string ClassifyTreasuryKind(string code,string type)
    {
        if(type is "cash" or "bank")return type;
        if(code.StartsWith("1111",StringComparison.Ordinal))return "cash";
        if(code.StartsWith("1112",StringComparison.Ordinal))return "bank";
        // Defined custody: employee/supervisor عهدة 112302–112306 and cash-transfer clearing 114101/114104/114106.
        if(code is "112302" or "112304" or "112305" or "112306" or "114101" or "114104" or "114106")return "custody";
        return "";
    }

    static DateOnly ParseMonth(string? periodMonth)
    {
        string raw=(periodMonth??DateTime.UtcNow.ToString("yyyy-MM",CultureInfo.InvariantCulture)).Trim();
        if(raw.Length==7)raw+="-01";
        J.Need(DateOnly.TryParseExact(raw.Length>=10?raw[..10]:raw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d),"شهر الأرصدة غير صالح.");
        return new DateOnly(d.Year,d.Month,1);
    }
    static object MonthLiteral(DbConnection c,DateOnly month)=>SchemaSql.IsSql(c)?month.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):month.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    static void EnsureSchemas(DbConnection c,DbTransaction? tx)
    {
        SchemaFinPeriodBalance.Ensure(c,tx);SchemaTreasury.Ensure(c,tx);
    }
}
