using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Payroll;

public static partial class HungerStationInvoices
{
    public sealed record RowIssue(int File,string Sheet,int Row,string Code);
    static readonly string[] Components=["completedOrders","basicPayment","stackingDeduction","declinedPenalty","latePenalty","noShowPenalty","noShowSpecialPenalty","acceptanceRatePenalty","distancePayment","missedDaysPenalty","segmentPayment","courierBasicPayment","riderBalance"];
    static readonly string[] MoneyFields=["basicPayment","stackingDeduction","declinedPenalty","latePenalty","noShowPenalty","noShowSpecialPenalty","acceptanceRatePenalty","distancePayment","missedDaysPenalty","segmentPayment","courierBasicPayment","riderBalance","driverCardSupport","growthProgram","periodAdjustment","carDeduction","carAdjustment","tgaDeduction","amountExclVat","vat","amountInclVat","totalDeduction","netFtr","riderNet","companyCommission","platformAmountExclVat"];
    static readonly Dictionary<string,string> Aliases=BuildAliases();
    static string Key(string value)=>Regex.Replace(value.ToLowerInvariant(),@"[^a-z0-9]","");
    static Dictionary<string,string> BuildAliases()
    {
        string[] fields=MoneyFields.Concat(new[]{"completedOrders","platformId","contractName","companyCr","scheme","riderId","currency","cityName","vehicleType","contractStart","contractEnd","totalMonthlyHours","workingHours","fullyWorkingDays","workingDays","cityMonthlyFlatSalary","cityPayment","basicRate","segment","segmentRate","excusedAbsenceDays","missedWeekends","missedEom","totalMissedDays","missedDaysAfterAllowance","totalManhattanDistance","manhattanDistancePayable","invoiceDate","loadingPeriod"}).ToArray();
        var map=fields.ToDictionary(Key,x=>x);
        foreach(var (name,field) in new[]{("rider_id","platformId"),("platform_id","platformId"),("commercial_registration","companyCr"),("commercial_registration_number","companyCr"),("cr_number","companyCr"),("cr","companyCr"),("accounting_scheme","scheme"),("work_system","scheme"),("declined_penalties_day_logic","declinedPenalty"),("no_show_penalty_special_cities","noShowSpecialPenalty"),("daily_acceptance_rate_penalty","acceptanceRatePenalty"),("missed_days_penalty","missedDaysPenalty"),("amount_excluding_vat","amountExclVat"),("amount_including_vat","amountInclVat"),("vat_amount","vat"),("net_ftr","netFtr"),("actual_rider_iqama","riderId")})map[Key(name)]=field;
        return map;
    }
    static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    static decimal Amount(string value,bool optional=true)
    {
        if(string.IsNullOrWhiteSpace(value)&&optional)return 0;
        if(!decimal.TryParse(value.Trim(),NumberStyles.Number|NumberStyles.AllowParentheses,CultureInfo.InvariantCulture,out decimal amount)||amount!=J.Money(amount))throw new ApiError("قيمة مالية غير صالحة أو لا تمثل هللات كاملة.");return amount;
    }
    static string Scheme(string value)=>value.Trim().ToLowerInvariant() switch{"" or "unknown"=>"unknown","commission" or "عمولة"=>"commission","salary" or "راتب"=>"salary","target" or "تارجت"=>"target",_=>throw new ApiError("نظام أجر غير معروف.")};
    static List<JsonObject> Table(Imports.Sheet sheet,int file,string period,List<RowIssue> errors,string label)
    {
        if(sheet.Rows.Count==0){errors.Add(new(file,label,1,"missing_header"));return [];}
        var map=new Dictionary<int,string>();var seen=new HashSet<string>();
        string[] months=["january","february","march","april","may","june","july","august","september","october","november","december"];
        for(int i=0;i<sheet.Rows[0].Count;i++)
        {
            string header=sheet.Rows[0][i].Trim();int dash=header.IndexOf(" - ",StringComparison.Ordinal);
            if(dash>0&&Regex.IsMatch(header[..dash],@"[\u0600-\u06ff]"))header=header[(dash+3)..].Trim();
            var tagged=Regex.Match(header,@"(?<month>january|february|march|april|may|june|july|august|september|october|november|december|jan|feb|mar|apr|jun|jul|aug|sep|oct|nov|dec|[0-9]{1,2})[ _-]+(?<year>20[0-9]{2})",RegexOptions.IgnoreCase);
            if(tagged.Success)
            {
                string monthText=tagged.Groups["month"].Value.ToLowerInvariant();int month=int.TryParse(monthText,out int number)?number:Array.FindIndex(months,m=>m.StartsWith(monthText,StringComparison.Ordinal))+1;
                string taggedPeriod=tagged.Groups["year"].Value+"-"+month.ToString("D2");if(taggedPeriod!=period)errors.Add(new(file,label,1,"header_period_mismatch"));header=header.Remove(tagged.Index,tagged.Length);
            }
            if(Aliases.TryGetValue(Key(header),out string? field)){if(!seen.Add(field))errors.Add(new(file,label,1,"duplicate_column"));else map[i]=field;}
        }
        var result=new List<JsonObject>();
        for(int index=1;index<sheet.Rows.Count;index++)
        {
            var cells=sheet.Rows[index];if(cells.All(string.IsNullOrWhiteSpace))continue;
            var row=new JsonObject{["sourceRow"]=index+1};
            try
            {
                foreach(var (column,field) in map)
                {
                    string text=column<cells.Count?cells[column].Trim():"";
                    if(MoneyFields.Contains(field))row[field]=Amount(text);
                    else if(field=="completedOrders"){decimal value=Amount(text,false);J.Need(value>=0&&value==decimal.Truncate(value),"عدد الطلبات غير صالح.");row[field]=value;}
                    else row[field]=text;
                }
                if(row.S("contractName").Equals("total",StringComparison.OrdinalIgnoreCase)||row.S("contractName").Equals("grand total",StringComparison.OrdinalIgnoreCase)||row.S("contractName")=="الإجمالي")continue;
                result.Add(row);
            }
            catch(ApiError){errors.Add(new(file,label,index+1,"invalid_cell"));}
        }
        return result;
    }
    static string Company(DbConnection c,ClaimsPrincipal actor,string cr)
    {
        var ids=Store.List(c,"commercial_registration").Where(r=>r.S("status")!="archived"&&(r.S("number")==cr||r.S("crNumber")==cr)).Select(r=>r.S("companyId")).ToHashSet();
        var matches=Store.List(c,"company").Where(r=>r.S("status")!="archived"&&(r.S("crNumber")==cr||r.S("commercialReg")==cr||ids.Contains(r.S("id")))).ToList();
        J.Need(matches.Count==1,"السجل التجاري في الملف غير مربوط بشركة واحدة.");FinancialScopes.DemandSponsor(actor,matches[0].S("id"));return matches[0].S("id");
    }
    static string ActualRider(DbConnection c,JsonObject facts,bool confirmed,string period,string platformId)
    {
        if(facts.S("riderId")!="")
        {string id=facts.S("riderId");J.Need(confirmed&&Regex.IsMatch(id,@"^[12][0-9]{9}$")&&Store.Get(c,id,"rider") is {} rider&&ErpRiderClassification.ListedAsRider(rider),"أكد هوية المندوب الفعلي الموجود في الدليل.");return id;}
        var first=DateOnly.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);string start=first.ToString("yyyy-MM-dd"),end=first.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd");
        var accounts=Store.List(c,"platform_account").Where(a=>a.S("platformCode").Equals("HungerStation",StringComparison.OrdinalIgnoreCase)||a.S("platformCode").Equals("hs",StringComparison.OrdinalIgnoreCase)).Where(a=>a.S("externalAccountId")==platformId).ToList();
        if(accounts.Count!=1)return "";
        var all=(accounts[0]["assignments"] as JsonArray??[]).OfType<JsonObject>().Where(a=>!a.B("IsDeleted")).ToList();
        if(all.Any(a=>a.S("StartDate").Length<10||a.S("EndDate")!=""&&a.S("EndDate").Length<10))return "";
        var assignments=all.Where(a=>a.S("riderId")!=""&&string.CompareOrdinal(a.S("StartDate")[..10],end)<=0&&(a.S("EndDate")==""||string.CompareOrdinal(a.S("EndDate")[..10],start)>=0)).ToList();
        string mapped=assignments.Count==1&&string.CompareOrdinal(assignments[0].S("StartDate")[..10],start)<=0&&(assignments[0].S("EndDate")==""||string.CompareOrdinal(assignments[0].S("EndDate")[..10],end)>=0)?assignments[0].S("riderId"):"";
        return mapped!=""&&Store.Get(c,mapped,"rider") is {} candidate&&ErpRiderClassification.ListedAsRider(candidate)?mapped:""; // never substitute the registered account owner
    }
    static JsonObject? Parse(DbConnection c,ClaimsPrincipal actor,List<Imports.Sheet> sheets,JsonObject input,string hash,int file,List<RowIssue> errors)
    {
        string period=J.Period(input.Required("period"));
        Imports.Sheet? Find(params string[] names)
        {var matches=sheets.Where(s=>names.Any(n=>s.Name.Trim().Equals(n,StringComparison.OrdinalIgnoreCase))).ToArray();if(matches.Length>1)errors.Add(new(file,"workbook",0,"ambiguous_sheet"));return matches.Length==1?matches[0]:null;}
        var coSheet=Find("Co LVL","Company","الشركة");var riderSheet=Find("Rider LVL","Riders","المناديب");var detailSheet=Find("Breakdown","التفصيل");
        if(coSheet==null||riderSheet==null||detailSheet==null){errors.Add(new(file,"workbook",0,"missing_required_sheet"));return null;}
        var companyRows=Table(coSheet,file,period,errors,"Co LVL");var riderRows=Table(riderSheet,file,period,errors,"Rider LVL");var details=Table(detailSheet,file,period,errors,"Breakdown");
        if(companyRows.Count==0){errors.Add(new(file,"Co LVL",2,"missing_company_row"));return null;}
        var crs=companyRows.Select(r=>
        {if(r.S("companyCr") is {Length:>0} cr)return cr;var values=Regex.Matches(r.S("contractName"),@"(?<![0-9])[0-9]{10}(?![0-9])").Select(m=>m.Value).Distinct().ToArray();return values.Length==1?values[0]:"";}).Distinct().ToArray();
        string company;
        try {J.Need(crs.Length==1&&Regex.IsMatch(crs[0],@"^[0-9]{10}$"),"رقم السجل مطلوب في الملف.");company=Company(c,actor,crs[0]);}
        catch(ApiError e) when(e.Status!=403){errors.Add(new(file,"Co LVL",2,"company_cr_not_mapped"));return null;}
        string invoiceId="hs-invoice:"+Hash("HungerStation\n"+company+"\n"+crs[0]+"\n"+period);var lines=new JsonArray();var flags=new JsonArray();var knownContracts=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var knownRiders=new HashSet<string>();
        var overrides=(input["lineFacts"] as JsonArray??[]).OfType<JsonObject>().ToList();
        foreach(var co in companyRows)
        {
            int number=(int)co.N("sourceRow");string contract=co.S("contractName");
            if(contract==""||!knownContracts.Add(contract)){errors.Add(new(file,"Co LVL",number,"missing_or_duplicate_contract"));continue;}
            if(!new[]{"amountExclVat","vat","amountInclVat","completedOrders"}.All(co.ContainsKey))errors.Add(new(file,"Co LVL",number,"missing_required_column"));
            if(co.N("amountInclVat")!=co.N("amountExclVat")+co.N("vat"))errors.Add(new(file,"Co LVL",number,"vat_reconciliation_failed"));
            if(co.S("currency","SAR")!="SAR")errors.Add(new(file,"Co LVL",number,"unsupported_currency"));
            if(co.N("riderBalance")!=co.N("totalDeduction"))flags.Add(new JsonObject{["sourceRow"]=number,["code"]="rider_balance_deduction_mismatch"});
            if(co.N("amountInclVat")-co.N("totalDeduction")!=co.N("netFtr"))flags.Add(new JsonObject{["sourceRow"]=number,["code"]="net_ftr_equation_mismatch"});
            var riders=riderRows.Where(r=>r.S("contractName").Equals(contract,StringComparison.OrdinalIgnoreCase)).ToList();
            if(riders.Count==0)errors.Add(new(file,"Rider LVL",0,"contract_has_no_riders"));
            foreach(string component in Components)if(riders.Sum(r=>r.N(component))!=co.N(component))errors.Add(new(file,"Co LVL",number,"rider_sum_mismatch:"+component));
            bool riderTga=riders.Any(r=>r.ContainsKey("tgaDeduction"));
            if(riderTga&&riders.Sum(r=>r.N("tgaDeduction"))!=co.N("tgaDeduction"))errors.Add(new(file,"Co LVL",number,"rider_tga_sum_mismatch"));
            var companyLine=co.Clone();companyLine["id"]="hs-line:"+Hash(invoiceId+"\ncompany\n"+contract);companyLine["lineType"]="company";companyLine["tgaAmount"]=riderTga?0:co.N("tgaDeduction");lines.Add(companyLine);
            foreach(var row in riders)
            {
                int riderRow=(int)row.N("sourceRow");string platformId=row.S("platformId");
                if(!row.ContainsKey("completedOrders"))errors.Add(new(file,"Rider LVL",riderRow,"missing_completed_orders"));
                if(platformId==""||!knownRiders.Add(platformId)){errors.Add(new(file,"Rider LVL",riderRow,"missing_or_duplicate_platform_id"));continue;}
                var breakdown=details.Where(d=>d.S("platformId")==platformId).ToList();
                if(breakdown.Count==0||breakdown.Any(d=>!d.ContainsKey("completedOrders"))||breakdown.Sum(d=>d.N("completedOrders"))!=row.N("completedOrders"))errors.Add(new(file,"Breakdown",0,"completed_orders_mismatch"));
                var facts=overrides.Where(f=>f.S("platformId")==platformId&&(f.S("companyCr")==""||f.S("companyCr")==crs[0])).ToList();
                if(facts.Count>1){errors.Add(new(file,"Rider LVL",riderRow,"duplicate_line_facts"));continue;}
                var merged=row.Clone();if(facts.Count==1)foreach(string key in new[]{"scheme","riderId","riderNet","companyCommission","platformAmountExclVat"})if(facts[0].ContainsKey(key))merged[key]=facts[0][key]!.DeepClone();
                try
                {
                    merged["scheme"]=Scheme(merged.S("scheme"));merged["riderId"]=ActualRider(c,merged,input.B("confirmActualRider"),period,platformId);
                    foreach(string key in new[]{"riderNet","companyCommission","platformAmountExclVat"})if(merged.ContainsKey(key))merged[key]=Amount(merged.S(key),false);
                }
                catch(ApiError){errors.Add(new(file,"Rider LVL",riderRow,"invalid_approved_facts"));continue;}
                merged["id"]="hs-line:"+Hash(invoiceId+"\nrider\n"+platformId);merged["lineType"]="rider";merged["tgaAmount"]=riderTga?row.N("tgaDeduction"):0;
                merged["breakdown"]=new JsonArray(breakdown.Select(d=>(JsonNode)d.Clone()).ToArray());lines.Add(merged);
            }
        }
        foreach(var row in riderRows.Where(r=>!knownContracts.Contains(r.S("contractName"))))errors.Add(new(file,"Rider LVL",(int)row.N("sourceRow"),"unknown_contract"));
        foreach(var row in details.Where(r=>!knownRiders.Contains(r.S("platformId"))))errors.Add(new(file,"Breakdown",(int)row.N("sourceRow"),"unknown_platform_id"));
        var damage=Find("Car Damage","تلفيات السيارات");
        if(damage!=null)foreach(var row in Table(damage,file,period,errors,"Car Damage"))
        {
            if(!new[]{"amountExclVat","vat","amountInclVat"}.All(row.ContainsKey)||row.N("amountInclVat")!=row.N("amountExclVat")+row.N("vat"))errors.Add(new(file,"Car Damage",(int)row.N("sourceRow"),"car_damage_vat_mismatch"));
            row["id"]="hs-line:"+Hash(invoiceId+"\ncar-damage\n"+row.N("sourceRow"));row["lineType"]="car_damage";row["periodReview"]="pending";lines.Add(row);
        }
        var invoice=new JsonObject{["id"]=invoiceId,["companyId"]=company,["companyCr"]=crs[0],["platform"]="HungerStation",["period"]=period,["sourceHash"]=hash,["lines"]=lines,["flags"]=flags};
        foreach(string key in new[]{"amountExclVat","vat","amountInclVat"})invoice[key]=companyRows.Sum(r=>r.N(key));return invoice;
    }
}
