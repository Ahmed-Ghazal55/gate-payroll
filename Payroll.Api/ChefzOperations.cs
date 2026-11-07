using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

// Read-only projection of already-pulled ERP facts. No pull, seeding, invoice creation or posting.
public static class ChefzOperations
{
    static bool Date(string value,out DateOnly date)=>DateOnly.TryParseExact(value.Length>=10?value[..10]:value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date);
    public static JsonObject View(DbConnection c,ClaimsPrincipal actor,JsonObject input)
    {
        string company=FinancialCore.ResolveSponsor(input.Required("companyId")).Id,period=J.Period(input.Required("period"));OperationsImports.Scope(actor,"chefz",company,"pay.view");
        PayValues.Fields(input,"companyId","period","invoiceId","operators");
        var first=DateOnly.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);var next=first.AddMonths(1);int monthDays=next.DayNumber-first.DayNumber;
        var errors=new JsonArray();var notes=new JsonArray();var lineage=new JsonArray();int deleted=0,dailyCount=0,settlementCount=0;
        void Error(string code)=>errors.Add(new JsonObject{["code"]=code});
        var accounts=Store.List(c,"platform_account").Where(a=>a.S("companyId")==company&&PayPlatforms.For(a.S("platformCode")).App=="chefz").ToDictionary(a=>a.S("id"));
        var assignmentGroups=Store.List(c,"rider_account_assignment").Where(a=>a.S("companyId")==company&&a.S("erpSourceId")!="").GroupBy(a=>a.S("erpSourceId")).ToList();
        var assignments=assignmentGroups.Where(g=>g.Count()==1).ToDictionary(g=>g.Key,g=>g.Single(),StringComparer.Ordinal);
        bool Relation(JsonObject row)
        {
            if(!accounts.TryGetValue(row.S("platformAccountId"),out var account)){Error("account_relation_missing");return false;}
            var person=Store.Get(c,row.S("riderId"),"rider");if(person==null||!ErpRiderClassification.ListedAsRider(person)){Error("actual_rider_missing");return false;}
            if(!FinancialScopes.Rider(actor,person)&&!FinancialScopes.AllCompanies(actor)){Error("actual_rider_out_of_scope");return false;}
            if(!assignments.TryGetValue(row.S("assignmentSourceId"),out var assignment)||assignment.B("sourceDeleted")||assignment.S("companyId")!=company||assignment.S("platformAccountId")!=account.S("id")||assignment.S("riderId")!=row.S("riderId")||!Date(assignment.S("effectiveFrom"),out var start)||start>=next||assignment.S("effectiveTo")!=""&&(!Date(assignment.S("effectiveTo"),out var end)||end<first))
            {Error("actual_assignment_unmatched");return false;}return true;
        }
        var settlements=new List<JsonObject>();var dates=new Dictionary<string,HashSet<DateOnly>>();var seen=new HashSet<string>();var intervals=new Dictionary<string,List<(DateOnly From,DateOnly To)>>();
        foreach(var row in Store.List(c,"chefz_settlement").Where(r=>r.S("companyId")==company))
        {
            if(row.B("sourceDeleted")){deleted++;continue;}
            if(!Date(row.S("periodFrom"),out var from)||!Date(row.S("periodTo"),out var to)||to<from){Error("settlement_period_invalid");continue;}
            if(from>=next||to<first)continue;
            if(from<first||to>=next){Error("settlement_crosses_month");continue;}
            if(!seen.Add(row.S("erpSourceId"))||row.S("erpSourceId")==""){Error("settlement_source_duplicate_or_missing");continue;}
            if(!Relation(row))continue;
            if(!Date(row.S("chargedFrom"),out var chargedFrom)||!Date(row.S("chargedTo"),out var chargedTo)||chargedFrom<from||chargedTo>to||chargedTo<chargedFrom||row.N("assignedDays")!=chargedTo.DayNumber-chargedFrom.DayNumber+1)
            {Error("settlement_charged_days_invalid");continue;}
            string key=row.S("platformAccountId")+":"+row.S("riderId");if(!intervals.TryGetValue(key,out var ranges))intervals[key]=ranges=[];
            if(ranges.Any(x=>chargedFrom<=x.To&&chargedTo>=x.From)){Error("settlement_intervals_overlap");continue;}ranges.Add((chargedFrom,chargedTo));
            bool money=true;foreach(string field in new[]{"gross","deductions","vat","companyAmount","riderAmount"})
                try{decimal value=PayValues.Number(row,field,-100000000);if(value!=J.Money(value))money=false;}catch(ApiError){money=false;}
            if(!money||row.N("deductions")<0||row.N("vat")<0||row.N("gross")<0||row.N("gross")!=row.N("companyAmount")+row.N("riderAmount")){Error("settlement_financial_facts_invalid");continue;}
            var assignment=assignments[row.S("assignmentSourceId")];Date(assignment.S("effectiveFrom"),out var assignmentStart);
            if(chargedFrom<assignmentStart||assignment.S("effectiveTo")!=""&&Date(assignment.S("effectiveTo"),out var assignmentEnd)&&chargedTo>assignmentEnd){Error("settlement_outside_assignment");continue;}
            if(!dates.TryGetValue(row.S("riderId"),out var days))dates[row.S("riderId")]=days=[];
            for(var day=chargedFrom;day<=chargedTo;day=day.AddDays(1))days.Add(day);
            settlements.Add(row);settlementCount++;
            lineage.Add(new JsonObject{["kind"]="chefz_settlement",["sourceId"]=row.S("id"),["assignmentSourceId"]=row.S("assignmentSourceId"),["gross"]=row["gross"]!.DeepClone(),["vat"]=row["vat"]!.DeepClone(),["companyAmount"]=row["companyAmount"]!.DeepClone(),["riderAmount"]=row["riderAmount"]!.DeepClone()});
        }
        var metrics=new Dictionary<string,JsonObject>();seen.Clear();
        foreach(var row in Store.List(c,"chefz_daily").Where(r=>r.S("companyId")==company))
        {
            if(row.B("sourceDeleted")){deleted++;continue;}if(!Date(row.S("date"),out var date)){Error("daily_date_invalid");continue;}if(date<first||date>=next)continue;
            if(!Relation(row))continue;
            var assignment=assignments[row.S("assignmentSourceId")];Date(assignment.S("effectiveFrom"),out var assignmentStart);
            if(date<assignmentStart||assignment.S("effectiveTo")!=""&&Date(assignment.S("effectiveTo"),out var assignmentEnd)&&date>assignmentEnd){Error("daily_outside_assignment");continue;}
            string key=row.S("platformAccountId")+":"+row.S("riderId")+":"+date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            if(!seen.Add(key)){Error("daily_account_rider_date_duplicate");continue;}
            var values=new JsonObject();bool valid=true;foreach(string field in new[]{"totalValidOffers","acceptedOffers","declinedOffers","missedOffers"})
                try{decimal n=PayValues.Number(row,field);if(n!=decimal.Truncate(n))valid=false;values[field]=n;}catch(ApiError){valid=false;}
            if(!valid||values.N("acceptedOffers")+values.N("declinedOffers")+values.N("missedOffers")>values.N("totalValidOffers")){Error("daily_offers_invalid");continue;}
            if(!metrics.TryGetValue(row.S("riderId"),out var total))metrics[row.S("riderId")]=total=new JsonObject();
            foreach(var value in values)total[value.Key]=total.N(value.Key)+values.N(value.Key);dailyCount++;
        }
        var operators=PayValues.Array(input["operators"]??new JsonArray(),25000).OfType<JsonObject>().ToList();var operatorKeys=new HashSet<string>();
        foreach(var op in operators){PayValues.Fields(op,"accountId","rider","category","orders");J.Iqama(op.Required("rider"));J.Need(operatorKeys.Add(op.Required("accountId")+":"+op.S("rider")),"توزيع مشغل مكرر.");}
        var invoiceId=input.S("invoiceId");JsonObject? invoice=null;var sourceLines=new List<JsonObject>();
        if(invoiceId!="")
        {invoice=OperationsImports.Invoice(c,null,actor,"chefz",invoiceId,"pay.view");J.Need(invoice.S("companyId")==company&&invoice.S("period")==period,"الفاتورة لا تخص السجل والشهر.");sourceLines=OperationsImports.SourceLines(c,null,"chefz",invoiceId);}
        else Error("approved_invoice_required");
        var reviewAccounts=new JsonArray();var usedOperators=new HashSet<string>();
        foreach(var group in settlements.GroupBy(r=>r.S("platformAccountId")))
        {
            var source=sourceLines.Where(l=>l.S("platformAccountId")==group.Key||l.S("accountId")==group.Key).ToList();
            if(invoice!=null&&source.Count!=1){Error("invoice_account_line_unmatched");continue;}
            var riders=new JsonArray();decimal gross=0;int orders=0;
            foreach(var riderGroup in group.GroupBy(r=>r.S("riderId")))
            {
                var op=operators.SingleOrDefault(o=>o.S("accountId")==group.Key&&o.S("rider")==riderGroup.Key);decimal actualOrders=0;string category="";
                if(op==null)Error("actual_delivered_orders_and_category_required");
                else
                {
                    usedOperators.Add(group.Key+":"+riderGroup.Key);category=op.S("category");if(!PaySettings.Categories.Contains(category))Error("pay_category_required");
                    actualOrders=PayValues.Number(op,"orders",0,int.MaxValue);J.Need(actualOrders==decimal.Truncate(actualOrders),"عدد طلبات فعلي صحيح مطلوب.");
                }
                decimal baseGross=riderGroup.Sum(r=>r.N("gross")+r.N("deductions")),deduction=riderGroup.Sum(r=>r.N("deductions"));gross+=baseGross;orders=checked(orders+(int)actualOrders);
                riders.Add(new JsonObject{["rider"]=riderGroup.Key,["orders"]=actualOrders,["baseGross"]=baseGross,["deduction"]=deduction,["category"]=category,["days"]=Math.Min(monthDays,dates[riderGroup.Key].Count),["metrics"]=(metrics.GetValueOrDefault(riderGroup.Key)??new JsonObject()).DeepClone()});
            }
            // ERP GrossEarnings is after source deductions. Add those once to reconstruct pre-deduction gross;
            // bonuses already live inside GrossEarnings, never add them again as incentives.
            reviewAccounts.Add(new JsonObject{["accountId"]=group.Key,["sourceLineId"]=source.Count==1?source[0].S("id"):"",["baseGross"]=gross,["incentives"]=0,["orders"]=orders,["riders"]=riders});
            if(source.Count==1&&(source[0].N("baseGross")!=gross||source[0].N("incentives")!=0||source[0].N("orders")!=orders))
            {notes.Add(new JsonObject{["code"]="erp_expected_differs_from_final_invoice"});Error("final_invoice_allocation_required");}
        }
        foreach(var rider in reviewAccounts.OfType<JsonObject>().SelectMany(a=>a["riders"]!.AsArray().OfType<JsonObject>()).GroupBy(r=>r.S("rider")))
            if(rider.Select(r=>r.S("category")).Distinct().Count()!=1)Error("rider_month_category_conflict");
        if(operators.Any(o=>!usedOperators.Contains(o.S("accountId")+":"+o.S("rider"))))Error("operator_without_settlement");
        if(reviewAccounts.Count==0)Error("no_settlement_facts");
        var deductions=invoice==null?new JsonArray():OperationsImports.Deductions(sourceLines);
        var review=new JsonObject{["invoiceId"]=invoiceId,["app"]="chefz",["accounts"]=reviewAccounts,["deductions"]=deductions};
        if(invoice!=null&&errors.Count==0)
        {try{PayCycles.ValidateImportedReview(c,null,actor,company,period,review,"pay.view");}catch(ApiError){Error("cycle_review_validation_required");}}

        return new(){["readOnly"]=true,["canUseInCycle"]=errors.Count==0,["review"]=review,["errors"]=errors,["notes"]=notes,["lineage"]=lineage,["counts"]=new JsonObject{["daily"]=dailyCount,["settlements"]=settlementCount,["deletedIgnored"]=deleted},["firstLoadRequiresApproval"]=true};
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/pay/apps/chefz/operations/review",(JsonObject input,HttpContext ctx)=>store.Read(c=>View(c,ctx.User,input))).WithTags("Payroll").WithSummary("Read already-pulled Chefz performance and wallet settlements as a cycle review candidate without writing");
    }
}
