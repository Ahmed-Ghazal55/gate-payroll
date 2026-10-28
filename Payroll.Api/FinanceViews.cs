using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

// Projections only: the resource describes the proposed tree, not a replacement posting chart.
public static class FinanceViews
{
    private static readonly JsonObject Resource = JsonNode.Parse(typeof(FinanceViews).Assembly.GetManifestResourceStream("Payroll.TargetCoa.v1.json")!)!.AsObject();
    public static JsonObject TargetChart()=>Resource.Clone();
    public static IEnumerable<JsonObject> Accounts=>Resource["accounts"]!.AsArray().OfType<JsonObject>();
    private static readonly Dictionary<string,JsonObject> Mappings=Resource["mapping"]!.AsArray().OfType<JsonObject>().ToDictionary(x=>x.S("currentCode"));
    public static bool Posted(JsonObject journal)=>journal.S("status","posted")=="posted";
    public static (string From,string To) Periods(string? from,string? to)
    {
        string end=J.Period(string.IsNullOrWhiteSpace(to)?DateTime.UtcNow.ToString("yyyy-MM"):to);
        string start=J.Period(string.IsNullOrWhiteSpace(from)?end:from);
        J.Need(string.CompareOrdinal(start,end)<=0,"بداية الفترة بعد نهايتها.");return(start,end);
    }
    public static IEnumerable<FinancialCore.SponsorDef> Companies(ClaimsPrincipal user,string? sponsorId)
    {
        if(!string.IsNullOrWhiteSpace(sponsorId)){var sponsor=FinancialCore.ResolveSponsor(sponsorId);FinancialScopes.DemandSponsor(user,sponsor.Id);return [sponsor];}
        return FinancialCore.Sponsors.Where(s=>FinancialScopes.Sponsor(user,s.Id));
    }
    public static bool Visible(ClaimsPrincipal user,JsonObject line)
    {
        if(!FinancialScopes.Sponsor(user,line.S("sponsorId")))return false;
        var account=FinancialCore.ChartOfAccounts.FirstOrDefault(a=>a.Code==line.S("accountCode"));
        return account==null?AccessPolicy.Has(user,"treasury.journals"):FinancialScopes.Line(user,line);
    }
    private sealed class Movement
    {
        public decimal Opening,Debit,Credit;
        public JsonObject Json()=>new(){["openingDebit"]=Math.Max(Opening,0),["openingCredit"]=Math.Max(-Opening,0),["periodDebit"]=Debit,["periodCredit"]=Credit,["closingDebit"]=Math.Max(Opening+Debit-Credit,0),["closingCredit"]=Math.Max(-Opening-Debit+Credit,0)};
    }
    // Contextual splits are deliberately conservative. Unproven historical classification remains unmapped.
    private static string First(params string[] values)=>values.FirstOrDefault(v=>v!="")??"";
    public static string? MapAccount(JsonObject line,JsonObject journal,JsonObject? debt,JsonObject? rider,JsonObject? run)
    {
        string code=line.S("accountCode");if(!Mappings.TryGetValue(code,out var rule))return null;
        string[] targets=rule["targetCodes"]!.AsArray().Select(v=>v!.ToString()).Distinct().ToArray();
        if(targets.Length==1)return targets[0];
        string category=First(line.S("category"),debt.S("category"),journal.S("category"));
        bool employee=line.S("partyType",debt.S("debtorType"))=="Employee"||rider.B("isEmployee")||rider.S("personRole") is "Employee" or "employee" or "administrative"||code=="1220";
        if(code=="1210")return category switch {
            "Advance" or "CashAdvance" or "BankAdvance"=>"112501", "TrafficFine"=>"112502",
            "WorkPermit" or "IqamaRenewal" or "SponsorshipTransfer" or "ExitReEntryVisa" or "MedicalInsurance" or "ProfessionChange" or "DriverLicense" or "DriverCard" or "OtherHrService"=>"112506",
            "HousingDeduction" or "AdministrativeDeduction" or "EquipmentLoan" or "CashCustody"=>"112505",
            "AccidentDamage"=>"112509", "FuelOverage"=>"112510", _=>null};
        if(code=="1220")return category switch {"Advance" or "CashAdvance" or "BankAdvance"=>"112301","CashCustody"=>"112302",""=>null,_=>"112303"};
        if(code=="2110")return line.S("iqama")!=""||line.S("partyType")!=""?employee?"212101":"212103":null;
        if(code=="1310")
        {
            string own=line.S("sponsorId");
            string counterpart=line.S("counterpartySponsorId");
            if(counterpart=="")counterpart=new[]{journal.S("originSponsorId"),journal.S("creditorSponsorId")}.FirstOrDefault(s=>s!=""&&s!=own)??"";
            if(counterpart==""){var others=journal["lines"]!.AsArray().OfType<JsonObject>().Select(l=>l.S("sponsorId")).Where(s=>s!=""&&s!=own).Distinct().ToArray();if(others.Length==1)counterpart=others[0];}
            if(counterpart==own)return null;
            return FinancialCore.FindSponsor(counterpart)?.Code switch {"BW"=>"112701","EX"=>"112702","TR"=>"112703",_=>null};
        }
        if(code=="2130")return debt.S("supplierId")!=""&&debt.S("invoiceNumber")!=""?"211101":"212102";
        if(code=="4102")
        {
            string platform=First(line.S("platform"),debt.S("platform"),journal.S("platform"));
            if(platform==""&&run?["sourceRows"] is JsonArray sources){var platforms=sources.OfType<JsonObject>().Select(s=>s.S("platform")).Where(p=>p!="").Distinct().ToArray();if(platforms.Length==1)platform=platforms[0];}
            return platform switch {"Jahez"=>"411401","Keeta"=>"411501",_=>null};
        }
        if(code is "5101" or "5102" or "5103" or "5104" or "5105" or "5106" or "5109" or "5301")
        {
            string target=employee?code switch {"5104"=>"521201","5105"=>"521202","5301"=>"521101",_=>"521204"}:code switch {"5101"=>"512201","5102"=>"512202","5103"=>"512203","5104"=>"512204","5105"=>"512107","5106"=>"512205","5109"=>"512208",_=>"512101"};
            return targets.Contains(target)?target:null;
        }
        if(code=="5203")return category=="FuelOverage"?"512104":"526402";
        if(code=="5201")return category=="AccidentDamage"?"512111":category=="EquipmentLoan"?"512112":line.S("vehicleType",debt.S("vehicleType")) switch {"Car"=>"512105","Motorcycle"=>"512106",_=>null};
        return null;
    }
    public static JsonObject TrialBalance(DbConnection c,ClaimsPrincipal user,string? sponsorId,string? from,string? to,string view="current")
    {
        AccessPolicy.Demand(user,"treasury.journals");J.Need(view is "current" or "target","العرض current أو target فقط.");
        var period=Periods(from,to);var companies=Companies(user,sponsorId).ToArray();
        var journals=Store.List(c,"journal").Where(Posted).Where(j=>string.CompareOrdinal(j.S("period"),period.To)<=0).ToArray();
        var debts=Store.List(c,"debt").ToDictionary(d=>d.S("id"));var riders=Store.List(c,"rider").GroupBy(r=>r.S("iqama",r.S("id"))).ToDictionary(g=>g.Key,g=>g.First());var runList=Store.List(c,"run");
        JsonObject[] chart=view=="target"?Accounts.ToArray():FinancialCore.ChartOfAccounts.Select(a=>new JsonObject{["code"]=a.Code,["nameAr"]=a.NameAr,["parent"]=a.ParentCode,["posting"]=!FinancialCore.ChartOfAccounts.Any(x=>x.ParentCode==a.Code)}).ToArray();
        var chartByCode=chart.ToDictionary(a=>a.S("code"));var output=new JsonArray();
        foreach(var company in companies)
        {
            var direct=new Dictionary<string,Movement>();var unknown=new Dictionary<string,Movement>();int excluded=0;
            foreach(var journal in journals)foreach(var line in (journal["lines"] as JsonArray??[]).OfType<JsonObject>().Where(l=>l.S("sponsorId")==company.Id))
            {
                if(!Visible(user,line)){excluded++;continue;}
                J.Period(journal.S("period"));
                string current=line.S("accountCode"),code=current;
                if(view=="target"){
                    debts.TryGetValue(First(line.S("debtId"),journal.S("debtId")),out var debt);riders.TryGetValue(line.S("iqama",journal.S("iqama")),out var rider);var run=runList.FirstOrDefault(r=>r.S("journalEntryId")==journal.S("id")||journal.S("sourceRef")=="PAYROLL:"+r.S("id")||journal.S("sourceRef")==r.S("id"));
                    code=MapAccount(line,journal,debt,rider,run)??"";
                }
                var destination=code!=""&&chartByCode.ContainsKey(code)?direct:unknown;string key=ReferenceEquals(destination,direct)?code:current;
                if(!destination.TryGetValue(key,out var movement))destination[key]=movement=new();
                if(string.CompareOrdinal(journal.S("period"),period.From)<0)movement.Opening+=line.N("debit")-line.N("credit");
                else {movement.Debit+=line.N("debit");movement.Credit+=line.N("credit");}
            }
            var rolled=chart.ToDictionary(a=>a.S("code"),_=>new Movement().Json());
            foreach(var pair in direct){string? code=pair.Key;var visited=new HashSet<string>();while(code!=null&&rolled.TryGetValue(code,out var total)&&visited.Add(code)){foreach(var amount in pair.Value.Json())total[amount.Key]=total.N(amount.Key)+amount.Value!.GetValue<decimal>();code=chartByCode[code]["parent"]?.ToString();}}
            var rows=new JsonArray();foreach(var account in chart){var row=account.Clone();row["name"]=account.S("nameAr");foreach(var amount in rolled[account.S("code")])row[amount.Key]=amount.Value?.DeepClone();rows.Add(row);}
            var unmapped=new JsonArray();foreach(var pair in unknown.OrderBy(p=>p.Key,StringComparer.Ordinal)){var row=pair.Value.Json();row["code"]=pair.Key;row["nameAr"]=FinancialCore.ChartOfAccounts.FirstOrDefault(a=>a.Code==pair.Key)?.NameAr??"حساب غير معروف";row["name"]=row.S("nameAr");row["reason"]="insufficient_mapping_context";row["candidateCodes"]=Mappings.GetValueOrDefault(pair.Key)?["targetCodes"]?.DeepClone()??new JsonArray();unmapped.Add(row);}
            JsonObject Totals(IEnumerable<Movement> movements){var total=new JsonObject();foreach(var m in movements)foreach(var v in m.Json())total[v.Key]=total.N(v.Key)+v.Value!.GetValue<decimal>();return total.Count==0?new Movement().Json():total;}
            var totals=Totals(direct.Values.Concat(unknown.Values));
            output.Add(new JsonObject{["sponsorId"]=company.Id,["companyCode"]=company.Code,["rows"]=rows,["unmapped"]=unmapped,["totals"]=totals,["mappedTotals"]=Totals(direct.Values),["unmappedTotals"]=Totals(unknown.Values),["mappingComplete"]=unknown.Count==0,["scopeLimited"]=excluded>0,["excludedLineCount"]=excluded,["isComplete"]=excluded==0,["isBalanced"]=new[]{"opening","period","closing"}.All(k=>totals.N(k+"Debit")==totals.N(k+"Credit"))});
        }
        return new(){["version"]=Resource.S("version"),["view"]=view,["from"]=period.From,["to"]=period.To,["companies"]=output};
    }
    public static JsonObject IncomeStatement(DbConnection c,ClaimsPrincipal user,string? sponsorId,string? from,string? to,string view="target")
    {
        var trial=TrialBalance(c,user,sponsorId,from,to,view);var companies=new JsonArray();
        foreach(var company in trial["companies"]!.AsArray().OfType<JsonObject>())
        {
            var rows=company["rows"]!.AsArray().OfType<JsonObject>().ToArray();var index=rows.ToDictionary(r=>r.S("code"));
            bool Under(JsonObject r,string root){string code=r.S("code");var seen=new HashSet<string>();while(seen.Add(code)){if(code==root)return true;if(!index.TryGetValue(code,out var a)||a["parent"]==null)return false;code=a.S("parent");}return false;}
            string revenueRoot=view=="target"?"4":"4100",expenseRoot=view=="target"?"5":"5100";
            // Current expenses have three independent roots; target has one root.
            var revenueRows=rows.Where(r=>Under(r,revenueRoot)).ToArray();var expenseRows=rows.Where(r=>Under(r,expenseRoot)||view=="current"&&(Under(r,"5200")||Under(r,"5300"))).ToArray();
            JsonArray Groups(JsonObject[] source,bool revenue)=>new(source.Select(r=>(JsonNode?)new JsonObject{["code"]=r.S("code"),["nameAr"]=r.S("nameAr"),["parent"]=r["parent"]?.DeepClone(),["posting"]=r.B("posting"),["amount"]=revenue?r.N("periodCredit")-r.N("periodDebit"):r.N("periodDebit")-r.N("periodCredit")}).ToArray());
            decimal revenue=revenueRows.FirstOrDefault(r=>r.S("code")==revenueRoot)?.N("periodCredit")-revenueRows.FirstOrDefault(r=>r.S("code")==revenueRoot)?.N("periodDebit")??0;
            decimal expense=expenseRows.Where(r=>view=="target"?r.S("code")=="5":new[]{"5100","5200","5300"}.Contains(r.S("code"))).Sum(r=>r.N("periodDebit")-r.N("periodCredit"));
            companies.Add(new JsonObject{["sponsorId"]=company.S("sponsorId"),["companyCode"]=company.S("companyCode"),["revenueGroups"]=Groups(revenueRows,true),["expenseGroups"]=Groups(expenseRows,false),["revenue"]=revenue,["expense"]=expense,["net"]=revenue-expense,["netIsComplete"]=company.B("isComplete")&&company.B("mappingComplete"),["scopeLimited"]=company.B("scopeLimited"),["excludedLineCount"]=company.N("excludedLineCount"),["unmapped"]=company["unmapped"]!.DeepClone()});
        }
        trial["companies"]=companies;return trial;
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/finance/coa/target",(HttpContext ctx)=>{AccessPolicy.Demand(ctx.User,"treasury.view");return Results.Json(TargetChart());});
        api.MapGet("/finance/trial-balance",(HttpContext ctx,string? sponsorId,string? from,string? to,string? view)=>store.Read(c=>Results.Json(TrialBalance(c,ctx.User,sponsorId,from,to,view??"current"))));
        api.MapGet("/finance/income-statement",(HttpContext ctx,string? sponsorId,string? from,string? to,string? view)=>store.Read(c=>Results.Json(IncomeStatement(c,ctx.User,sponsorId,from,to,view??"target"))));
    }
}
