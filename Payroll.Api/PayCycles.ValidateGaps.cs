using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PayCycles
{
    /// <summary>BR §38.5 / R11 T7: counts-only gaps that block ValidateRiders. Never returns identities or amounts.</summary>
    public static JsonObject DiagnoseValidateGaps(DbConnection c,ClaimsPrincipal actor,string companyId,string period,string app,Func<string,IEnumerable<JsonObject>>? liveOps=null)
    {
        AccessPolicy.Demand(actor,"pay.view");period=J.Period(period);app=PayPlatforms.App(app);string company=FinancialCore.ResolveSponsor(companyId).Id;FinancialScopes.DemandSponsor(actor,company);
        var counts=new Dictionary<string,int>(StringComparer.Ordinal);
        // Captured cycle → freeze. Pre-cycle: liveOps when live mode, else store (B3).
        var cycle=Store.List(c,Kind).FirstOrDefault(p=>p.S("companyId")==company&&p.S("period")==period&&p.S("status") is not ("cancelled" or "reversed")&&(p.S("app")==app||(p["invoices"] as JsonArray??[]).OfType<JsonObject>().Any(i=>i.S("app")==app))&&PayRunInputs.Captured(p));
        List<JsonObject> Ops(string kind)=>cycle!=null?PayRunInputs.List(c,null,cycle.S("id"),kind):(liveOps!=null?liveOps(kind).ToList():Store.List(c,kind));
        List<JsonObject> Accounts()=>Ops("platform_account").Where(a=>a.S("companyId")==company&&PayPlatforms.For(a.S("platformCode")).App==app).ToList();
        List<JsonObject> Daily()=>Ops("ninja_daily").Where(d=>d.S("companyId")==company&&d.S("date").StartsWith(period+"-",StringComparison.Ordinal)).ToList();
        JsonObject? AccountGet(string id)=>cycle!=null?PayRunInputs.Get(c,null,cycle.S("id"),"platform_account",id):Ops("platform_account").FirstOrDefault(a=>a.S("id")==id)??Store.Get(c,id,"platform_account");
        var accounts=Accounts();
        int withAssignment=0,withoutAssignment=0,multiOperator=0;
        var missingCategory=new HashSet<string>(StringComparer.Ordinal);var invalidCategory=new HashSet<string>(StringComparer.Ordinal);var missingRecord=new HashSet<string>(StringComparer.Ordinal);
        foreach(var account in accounts)
        {
            var assigned=(account["assignments"] as JsonArray??[]).OfType<JsonObject>().Where(a=>a.S("riderId")!=""&&AssignmentSpan.Overlaps(a,period)).Select(a=>a.S("riderId")).Distinct(StringComparer.Ordinal).ToList();
            if(assigned.Count==0)withoutAssignment++;
            else if(assigned.Count==1)withAssignment++;
            else multiOperator++;
            foreach(string rider in assigned)
            {
                var person=Store.Get(c,rider,"rider");
                if(person==null){missingRecord.Add(rider);continue;}
                string category=person.S("payCategory");
                if(category=="")missingCategory.Add(rider);
                else if(!PaySettings.Categories.Contains(category))invalidCategory.Add(rider);
            }
        }
        var daily=Daily();
        int dailyUnmatchedAccount=daily.Count(d=>AccountGet(d.S("platformAccountId"))==null);
        int dailyNoAssignment=0;
        foreach(var day in daily)
        {
            var account=AccountGet(day.S("platformAccountId"));
            if(account==null)continue;
            bool ok=(account["assignments"] as JsonArray??[]).OfType<JsonObject>().Any(a=>a.S("riderId")==day.S("riderId")&&AssignmentSpan.Covers(a,day.S("date")));
            if(!ok)dailyNoAssignment++;
        }
        counts["accounts_total"]=accounts.Count;
        counts["accounts_with_one_operator"]=withAssignment;
        counts["accounts_no_assignment_overlap"]=withoutAssignment;
        counts["accounts_multi_operator"]=multiOperator;
        counts["riders_missing_record"]=missingRecord.Count;
        counts["riders_missing_category"]=missingCategory.Count;
        counts["riders_invalid_category"]=invalidCategory.Count;
        counts["ninja_daily_rows"]=daily.Count;
        counts["ninja_daily_unmatched_account"]=dailyUnmatchedAccount;
        counts["ninja_daily_no_assignment_on_date"]=dailyNoAssignment;
        counts["inputs_captured"]=cycle!=null?1:0;
        return new JsonObject
        {
            ["companyId"]=company,["period"]=period,["app"]=app,
            ["counts"]=new JsonObject(counts.OrderBy(kv=>kv.Key).Select(kv=>KeyValuePair.Create(kv.Key,(JsonNode?)JsonValue.Create(kv.Value)))),
            ["note"]="counts only; no identities or amounts"
        };
    }

    /// <summary>Pre-cycle validate-gaps live reader. Unknown kind = error (never silent empty).</summary>
    public static Func<string,IEnumerable<JsonObject>> ValidateGapsLiveOps(IReadOnlyList<JsonObject> accounts,IReadOnlyList<JsonObject> daily)
        =>kind=>kind switch
        {
            "platform_account"=>accounts,
            "ninja_daily"=>daily,
            _=>throw new ApiError("مصدر تشغيل غير متوقع: "+kind,503)
        };
}
