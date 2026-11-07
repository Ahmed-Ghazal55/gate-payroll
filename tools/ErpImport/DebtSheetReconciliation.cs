using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Payroll;
namespace ErpImport;

public static partial class DebtSheetImporter
{
    public sealed record DeductionEvidence(JsonObject Summary,JsonArray Issues,JsonArray Lineage,JsonArray Carried);
    sealed record Receipt(string Sheet,int Row,string Identity,string Reference,string Date,string Type,string App,string Company,decimal Amount,string Status)
    {
        public string Key=>Reference!=""?Reference:Identity+"|"+Date+"|"+Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)+"|"+Type;
        public string Canonical=>Identity+"|"+Date+"|"+Type+"|"+App+"|"+Amount+"|"+Company;
    }
    static string Operation(string text)=>Regex.Replace(Text(text),@"^\d+\s*","");
    public static DeductionEvidence ReconcileDeductions(ReviewInput input,JsonObject mapping)
    {
        var issues=new JsonArray();var lineage=new JsonArray();var carried=new JsonArray();
        string App(string value){string normalized=Text(value);return (mapping["deductionAppAliases"] as JsonObject)?.S(normalized,normalized)??normalized;}
        void Issue(string sheet,int row,string reason)=>issues.Add(new JsonObject{["sheet"]=sheet,["row"]=row,["reason"]=reason});
        List<Receipt> Read(string name,string company="")
        {
            var s=Sheet(input.Sheets,name);var missing=MissingFormulaCache(input.Bytes,name);
            int id=ReviewHeader(s,"رقم الهوية"),date=ReviewHeader(s,"التاريخ"),type=ReviewHeader(s,"نوع العملية"),app=name=="المرفوض"?-1:ReviewHeader(s,"التطبيق"),reference=ReviewHeader(s,"رقم طلب الدفع"),amount=ReviewHeader(s,"القيمة"),status=ReviewHeader(s,"الحالة");
            var result=new List<Receipt>();
            for(int i=1;i<s.Rows.Count;i++)
            {
                var row=s.Rows[i];if(row.All(string.IsNullOrWhiteSpace))continue;
                if(missing.Contains(i+1)){Issue(name,i+1,"formula_without_cached_value");continue;}
                try{result.Add(new(name,i+1,NormalizeReviewIdentity(Cell(row,id)),Cell(row,reference),ReviewDate(Cell(row,date))?.ToString("yyyy-MM-dd")??Cell(row,date),Operation(Cell(row,type)),app<0?"":App(Cell(row,app)),company,Money(Cell(row,amount)),Cell(row,status)));}
                catch(ImportFailure){Issue(name,i+1,"invalid_numeric_cell");}
            }
            return result;
        }
        var companyRows=new[]{("اكسبريس","EX"),("البوابا","BW"),("التجارية","TR")}.SelectMany(x=>Read(x.Item1,x.Item2)).ToList();
        var approved=companyRows.Where(r=>r.Status is "مدير الحسابات" or "تم الإغلاق").ToList();
        var unique=new List<Receipt>();
        foreach(var group in approved.GroupBy(r=>r.Key))
        {
            if(group.Select(r=>r.Canonical).Distinct().Count()>1){foreach(var row in group)Issue(row.Sheet,row.Row,"request_reference_conflict");continue;}
            unique.Add(group.First());
            foreach(var row in group.Skip(1))Issue(row.Sheet,row.Row,"duplicate_request_counted_once");
        }
        var totals=Read("الاجماليات");var totalKeys=totals.GroupBy(r=>r.Key).ToDictionary(g=>g.Key,g=>g.ToList());
        foreach(var r in unique)
        {
            if(!totalKeys.TryGetValue(r.Key,out var found))Issue(r.Sheet,r.Row,"approved_detail_missing_from_totals");
            else if(found.Count!=1||found[0].Identity!=r.Identity||found[0].Type!=r.Type||found[0].App!=r.App||!Close(found[0].Amount,r.Amount))Issue(r.Sheet,r.Row,"totals_request_mismatch");
            lineage.Add(new JsonObject{["sheet"]=r.Sheet,["row"]=r.Row,["odooVoucherNo"]=r.Reference,["creditor"]=r.Company,["purpose"]="reconciliation_only_october_included_in_net"});
        }
        foreach(var r in totals.Where(r=>!unique.Any(x=>x.Key==r.Key)))Issue(r.Sheet,r.Row,"totals_row_missing_from_approved_detail");
        var rejected=Read("المرفوض");int missingRejected=0,drafts=0;
        foreach(var r in companyRows)
        {
            if(r.Status=="مرفوض"&&!rejected.Any(x=>x.Key==r.Key)){Issue(r.Sheet,r.Row,"rejected_not_in_rejected_sheet");missingRejected++;}
            if(r.Status=="مسودة"){Issue(r.Sheet,r.Row,"draft_excluded");drafts++;}
        }
        var detail=Sheet(input.Sheets,mapping.Required("deductionsSheet"));var detailCache=MissingFormulaCache(input.Bytes,detail.Name);
        var detailSeen=new HashSet<(string,string)>();
        var types=(mapping["deductionsColumns"] as JsonArray??[]).OfType<JsonObject>().ToList();
        for(int i=1;i<detail.Rows.Count;i++)
        {
            var row=detail.Rows[i];if(Cell(row,0)=="")continue;string identity=NormalizeReviewIdentity(Cell(row,0));
            if(identity==""&&(Regex.IsMatch(Cell(row,0),@"^(الاجمال|الإجمال|المجموع|Grand Total)",RegexOptions.IgnoreCase)||Cell(row,0)=="Row Labels"))continue;
            if(detailCache.Contains(i+1)){Issue(detail.Name,i+1,"formula_without_cached_value");continue;}
            decimal sum=0;
            try
            {
                foreach(var col in types)
                {
                    string type=Operation(col.S("header"));decimal actual=Money(Cell(row,Column(col.S("column")))),expected=unique.Where(r=>r.Identity==identity&&r.Type==type).Sum(r=>r.Amount);
                    if(!detailSeen.Add((identity,type)))Issue(detail.Name,i+1,"duplicate_detail_identity_type");
                    if(!Close(actual,expected))Issue(detail.Name,i+1,"detail_identity_type_mismatch");sum+=actual;
                }
                if(!Close(sum,Money(Cell(row,7))))Issue(detail.Name,i+1,"detail_total_mismatch");
            }
            catch(ImportFailure){Issue(detail.Name,i+1,"invalid_numeric_cell");}
        }
        foreach(var r in unique.Where(r=>!detailSeen.Contains((r.Identity,r.Type))))Issue(r.Sheet,r.Row,"approved_type_missing_from_detail");
        var control=Sheet(input.Sheets,mapping.Required("controlSheet"));var controlCache=MissingFormulaCache(input.Bytes,control.Name);
        foreach(var section in (mapping["controlSections"] as JsonArray??[]).OfType<JsonObject>())
        {
            var typeMap=types.Single(c=>c.S("column")==section.S("deductionColumn"));string type=Operation(typeMap.S("header"));
            string totalCell=section.Required("totalCell");int totalRow=int.Parse(new string(totalCell.Where(char.IsDigit).ToArray()));int totalCol=Column(new string(totalCell.Where(char.IsLetter).ToArray()));
            if(controlCache.Contains(totalRow)||!Close(Money(Cell(control.Rows[totalRow-1],totalCol)),unique.Where(r=>r.Type==type).Sum(r=>r.Amount)))Issue(control.Name,totalRow,"summary_type_total_mismatch");
            foreach(var company in section["companies"]!.AsArray().OfType<JsonObject>())
            {
                string code=company.Required("company");int value=Column(company.Required("valueColumn"));var seenLabels=new HashSet<string>();var appTotals=new Dictionary<string,decimal>();decimal total=0;
                for(int n=(int)company.N("fromRow");n<=(int)company.N("toRow");n++)
                {
                    var row=control.Rows[n-1];string app=App(Cell(row,value-2));decimal actual=Money(Cell(row,value));
                    if(controlCache.Contains(n)){Issue(control.Name,n,"formula_without_cached_value");continue;}
                    if(typeMap.S("category")=="HsWalletTopUp")app="هنقرستيشن";
                    if(app==""&&actual==0)continue;
                    if(!seenLabels.Add(Text(Cell(row,value-2))))Issue(control.Name,n,"summary_app_duplicate");
                    appTotals[app]=appTotals.GetValueOrDefault(app)+actual;total+=actual;
                }
                foreach(var app in appTotals)
                    if(!Close(app.Value,unique.Where(r=>r.Company==code&&r.Type==type&&r.App==app.Key).Sum(r=>r.Amount)))Issue(control.Name,(int)company.N("fromRow"),"summary_company_type_app_mismatch");
                if(!Close(total,unique.Where(r=>r.Company==code&&r.Type==type).Sum(r=>r.Amount)))Issue(control.Name,(int)company.N("fromRow"),"summary_company_type_total_mismatch");
            }
        }
        var carry=Sheet(input.Sheets,"ما لم يخصم سابقا");int duplicates=0;decimal carriedTotal=0;var carryKeys=new HashSet<string>();
        var carryCache=MissingFormulaCache(input.Bytes,carry.Name);
        for(int i=0;i<carry.Rows.Count;i++)
        {
            var row=carry.Rows[i];int referenceIndex=row.FindIndex(c=>Regex.IsMatch(c,@"^[A-Z]{2,}/\d{4}/"));string reference=referenceIndex<0?"":Cell(row,referenceIndex);
            var fields=mapping["carriedColumns"] as JsonObject;
            if(referenceIndex<0)
            {
                if(fields==null||!ValidReviewIdentity(NormalizeReviewIdentity(Cell(row,Column(fields.S("identity"))))))continue;
                referenceIndex=Column(fields.S("reference"));
            }
            if(carryCache.Contains(i+1)){Issue(carry.Name,i+1,"formula_without_cached_value");continue;}
            decimal amount=Money(Cell(row,referenceIndex-2));
            string key=reference!=""?reference:NormalizeReviewIdentity(Cell(row,referenceIndex-1))+"|"+(ReviewDate(Cell(row,referenceIndex-5))?.ToString("yyyy-MM-dd")??Cell(row,referenceIndex-5))+"|"+amount.ToString(System.Globalization.CultureInfo.InvariantCulture)+"|"+Operation(Cell(row,referenceIndex+1));
            bool already=totalKeys.ContainsKey(key)||!carryKeys.Add(key);if(already)duplicates++;
            carriedTotal+=amount;
            carried.Add(new JsonObject{["sheet"]=carry.Name,["row"]=i+1,["odooVoucherNo"]=reference,["amount"]=amount,["reason"]=already?"duplicate_counted_once":"carried_from_previous_period",["loaded"]=false});
        }
        var policy=mapping["deductionReconciliationPolicy"];
        bool summaryWarningsOnly=policy.S("authority")=="company_sheets_totals_detail"&&policy.S("formattedSummary")=="warning_only"&&policy.S("approvedBy")=="Ghazal"&&policy.S("approvedAt")=="2026-11-07";
        bool blocking=issues.OfType<JsonObject>().Any(i=>i.S("reason") is not ("rejected_not_in_rejected_sheet" or "draft_excluded" or "duplicate_request_counted_once")&&!(summaryWarningsOnly&&i.S("reason").StartsWith("summary_",StringComparison.Ordinal)));
        var summary=new JsonObject{["matched"]=!blocking,["approvedRows"]=approved.Count,["uniqueApprovedRequests"]=unique.Count,["approvedTotal"]=unique.Sum(r=>r.Amount),["totalRows"]=totals.Count,["totalsAmount"]=totals.Sum(r=>r.Amount),
            ["rejectedMissingFromRejectedSheet"]=missingRejected,["draftsExcluded"]=drafts,["carriedRows"]=carried.Count,["carriedTotal"]=carriedTotal,["carriedAlreadyInTotals"]=duplicates,["carriedPreviousPeriod"]=carried.Count-duplicates,
            ["octoberDocumentsToLoad"]=0,["formattedSummaryWarningsOnly"]=summaryWarningsOnly,["reasonCounts"]=JsonSerializer.SerializeToNode(issues.OfType<JsonObject>().GroupBy(i=>i.S("reason")).ToDictionary(g=>g.Key,g=>g.Count()))};
        return new(summary,issues,lineage,carried);
    }
}
