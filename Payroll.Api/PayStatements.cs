using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static class PayStatements
{
    static JsonArray Array(IEnumerable<JsonObject> rows)=>new(rows.Select(r=>(JsonNode?)r.Clone()).ToArray());
    static JsonObject Pick(JsonObject source,params string[] keys){var result=new JsonObject();foreach(string key in keys)if(source.ContainsKey(key))result[key]=source[key]?.DeepClone();return result;}
    static bool RunVisible(ClaimsPrincipal actor,JsonObject run)
    {
        if(!FinancialScopes.Ledger(actor,run))return false;var platforms=(run["sourceRows"] as JsonArray??[]).OfType<JsonObject>().Select(r=>r.S("platform")).Where(p=>p!="").Distinct().ToList();return platforms.Count>0?platforms.All(p=>FinancialScopes.Platform(actor,p)):PayPlatforms.Table.All(p=>FinancialScopes.Platform(actor,p.Platform))&&FinancialScopes.Platform(actor,"Other");
    }
    public static JsonObject Build(DbConnection c,string rider,string period,ClaimsPrincipal? actor,bool approvedOnly=false,DbTransaction? tx=null)
    {
        rider=J.Iqama(rider);period=J.Period(period);var person=Store.Get(c,rider,"rider",tx)??throw new ApiError("المندوب غير موجود.",404);
        if(actor!=null){AccessPolicy.Demand(actor,"pay.view");AccessPolicy.Demand(actor,"payroll.view");AccessPolicy.Demand(actor,"debts.view");}
        var allLines=Store.List(c,PayCycles.LineKind,tx).Where(l=>l.S("rider")==rider&&l.S("period")==period).ToList();
        var candidates=Store.List(c,PayCycles.Kind,tx).Where(d=>d.S("period")==period&&d.S("status") is not ("cancelled" or "reversed")&&(!approvedOnly||d.S("status")=="approved")&&allLines.Any(l=>l.S("cycleId")==d.S("id"))).ToList();
        var cycles=candidates.Where(d=>actor==null||PayCycles.Visible(actor,d)).ToList();if(candidates.Count>0&&cycles.Count==0)throw new ApiError("الكشف الشهري خارج نطاق الشركة أو التطبيقات.",403);
        var cards=new List<JsonObject>();var deductions=new List<JsonObject>();var refs=new List<JsonObject>();decimal net=0,carried=0,closing=0;string source="none",status="no_approved_cycle";
        if(cycles.Count>0)
        {
            source="pay_cycle";status=cycles.All(d=>d.S("status")=="approved")?"approved":"preview";
            foreach(var cycle in cycles.OrderBy(d=>d.S("companyId"),StringComparer.Ordinal))
            {
                refs.Add(Pick(cycle,"id","companyId","period","postingPeriod","status","stage","approvedAt"));var lines=allLines.Where(l=>l.S("cycleId")==cycle.S("id")).ToList();
                foreach(var line in lines)
                {
                    var card=Pick(line,"app","schemeId","schemeName","schemeType","accountingRole","category","companyId","cycleId","orders","days","incentives","companyCommission","band","operatingCost","accountDeduction","fuelCap","status","blockers");
                    card["platform"]=PayPlatforms.For(line.S("app")).Platform;card["gross"]=line.N("platformGross")+line.N("incentives");card["earnedGross"]=line.N("gross");card["vehicleCharge"]=line.N("allocatedVehicleCharge",line.N("vehicleCharge"));card["netBeforePersonal"]=line.N("entitlement")-card.N("vehicleCharge")-line.N("accountDeduction");
                    var explain=(line["lines"] as JsonArray??[]).OfType<JsonObject>().Select(l=>l.Clone()).ToList();foreach(var e in explain.Where(e=>e.S("category")=="vehicle_charge")){e["amount"]=-card.N("vehicleCharge");e["formula"]="monthly rent allocated once per rider across apps";}
                    if(line.N("accountDeduction")!=0)explain.Add(new(){["label"]="خصم اليوزر المراجع",["formula"]="reviewed actual operator deduction; excludes company-level costs",["inputs"]=new JsonObject{["reviewedAmount"]=line.N("accountDeduction")},["amount"]=-line.N("accountDeduction"),["category"]="account_deduction",["affectsNet"]=true});card["explain"]=Array(explain);cards.Add(card);
                }
                foreach(var d in (cycle["deductions"] as JsonArray??[]).OfType<JsonObject>().Where(d=>d.S("rider")==rider))
                {
                    if(actor!=null&&d.S("type")=="debt"){var debt=Store.Get(c,d.S("source"),"debt",tx)??throw new ApiError("دليل الدين غير موجود.",409);FinancialScopes.DemandDebt(actor,debt);}
                    deductions.Add(Pick(d,"category","amount","source","type","accountCode"));
                }
                var summary=(cycle["riders"] as JsonArray??[]).OfType<JsonObject>().SingleOrDefault(r=>r.S("rider")==rider);decimal movement=summary?.N("net")??lines.Sum(l=>l.N("entitlement")-l.N("accountDeduction")-l.N("allocatedVehicleCharge",l.N("vehicleCharge")))+(cycle["deductions"] as JsonArray??[]).OfType<JsonObject>().Where(d=>d.S("rider")==rider).Sum(d=>d.N("amount"));
                net+=movement;carried+=summary?.N("carried")??0;closing+=summary?.N("closing")??movement;
            }
        }
        else
        {
            var allRuns=Store.List(c,"run",tx).Where(r=>r.S("iqama")==rider&&r.S("period")==period&&r.S("approvedAt")!="").ToList();var runs=allRuns.Where(r=>actor==null||RunVisible(actor,r)).ToList();if(allRuns.Count>0&&runs.Count==0)throw new ApiError("المسير القديم خارج النطاق المالي.",403);
            if(runs.Count>0){source="legacy_run";status="approved";}
            foreach(var run in runs)
            {
                refs.Add(Pick(run,"id","sponsorId","period","approvedAt"));var result=run["result"] as JsonObject??new();var work=(run["sourceRows"] as JsonArray??[]).OfType<JsonObject>().ToList();var pay=(result["lines"] as JsonArray??[]).OfType<JsonObject>().ToList();var earnings=pay.Where(l=>l.S("category") is not ("debt_installment" or "already_paid")&&(l.N("amount")>=0||l.S("category") is "company_fee" or "platform_settlement")).ToList();
                string App(JsonObject line){string platform=line.S("platform");if(platform=="")platform=work.FirstOrDefault(w=>w.S("sourceRef")==line.S("source")).S("platform");return platform==""?"legacy_unattributed":PayPlatforms.For(platform).App;}
                foreach(var group in earnings.GroupBy(App))
                {
                    var explain=group.Select(l=>new JsonObject{["label"]=l.S("description"),["formula"]="immutable approved legacy run line",["inputs"]=new JsonObject{["category"]=l.S("category"),["source"]=l.S("source")},["amount"]=l.N("amount"),["category"]=l.S("category"),["affectsNet"]=true}).ToList();
                    cards.Add(new(){["app"]=group.Key,["platform"]=group.Key=="legacy_unattributed"?"Legacy":PayPlatforms.For(group.Key).Platform,["companyId"]=run.S("sponsorId"),["schemeName"]=result.S("policyName"),["schemeType"]="legacy",["gross"]=group.Where(l=>l.N("amount")>0).Sum(l=>l.N("amount")),["earnedGross"]=group.Sum(l=>l.N("amount")),["companyCommission"]=-group.Where(l=>l.S("category")=="company_fee").Sum(l=>l.N("amount")),["vehicleCharge"]=0,["incentives"]=0,["band"]=null,["explain"]=Array(explain),["status"]="approved"});
                }
                foreach(var d in pay.Except(earnings)){if(actor!=null&&d.S("category")=="debt_installment"&&Store.Get(c,d.S("source"),"debt",tx) is {} debt)FinancialScopes.DemandDebt(actor,debt);deductions.Add(Pick(d,"category","description","amount","source"));}
                net+=result.N("periodMovement");carried+=result.N("openingBalance");closing+=result.N("closingBalance");
            }
        }
        if(actor!=null&&source=="none")FinancialScopes.DemandRider(actor,person);
        string iban=person.S("iban");var header=new JsonObject{["rider"]=rider,["name"]=person.S("name"),["period"]=period,["status"]=person.S("status"),["ibanMasked"]=iban.Length>=4?"••••"+iban[^4..]:"",["cycles"]=Array(refs)};
        return new(){["header"]=header,["source"]=source,["status"]=status,["cards"]=Array(cards),["deductions"]=Array(deductions),["net"]=J.Money(net),["carried"]=J.Money(carried),["closing"]=J.Money(closing),["balanceBasis"]=source=="pay_cycle"?"cycle_snapshot":"approved_legacy_snapshot",["approvedOnly"]=approvedOnly};
    }
    public static JsonObject Mobile(DbConnection c,HttpContext ctx,string period)
    {if(ctx.Request.Query.Any(q=>q.Key!="period"))throw new ApiError("هوية المندوب تؤخذ من الجلسة فقط.");return Build(c,FinancialCore.AuthenticatedMobileRider(c,ctx),period,null,true);}
    public static void Map(WebApplication app,RouteGroupBuilder api,Store store)
    {
        api.MapGet("/finance/statement/rider/{iqama}/monthly",(string iqama,string period,HttpContext ctx)=>store.Read(c=>Build(c,iqama,period,ctx.User))).WithTags("Payroll").WithSummary("Read scoped monthly rider app cards and explainable net with legacy fallback");
        app.MapGet("/api/rider-mobile/statement",(string period,HttpContext ctx)=>{MobileSessions.Authenticate(store,ctx);return store.Read(c=>Mobile(c,ctx,period));}).WithTags("Riders").WithSummary("Read own approved monthly statement using rider bearer session only");
    }
}
