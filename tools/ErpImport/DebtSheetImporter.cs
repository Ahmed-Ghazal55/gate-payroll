using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Payroll;
namespace ErpImport;

public sealed class DebtSheetPreview(JsonObject summary,List<PlannedDocument> documents,string fingerprint,List<JsonObject>? newRiders=null)
{
    [System.Text.Json.Serialization.JsonIgnore] public List<JsonObject> NewRiders {get;}=newRiders??[];
    public JsonObject Summary {get;}=summary;
    [System.Text.Json.Serialization.JsonIgnore] public List<PlannedDocument> Documents {get;}=documents;
    [System.Text.Json.Serialization.JsonIgnore] public string Fingerprint {get;}=fingerprint;
}

// Workbook contents stay in memory. The only report is aggregate totals and Excel row numbers.
public static partial class DebtSheetImporter
{
    static string Text(string value)=>System.Text.RegularExpressions.Regex.Replace(value.Trim(),@"\s+"," ");
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    static string Cell(List<string> row,int column)=>column<row.Count?row[column].Trim():"";
    public static int Column(string value)
    {int n=0;foreach(char c in value){if(c is <'A' or >'Z')throw new ImportFailure("invalid_mapping_column");n=checked(n*26+c-'A'+1);}if(n is <1 or >500)throw new ImportFailure("invalid_mapping_column");return n-1;}
    static decimal Money(string value)
    {
        if(value=="")return 0;
        if(!decimal.TryParse(value,NumberStyles.Number,CultureInfo.InvariantCulture,out var n))throw new ImportFailure("invalid_numeric_cell");
        return J.Money(n);
    }
    static string Identity(string value)
    {
        // Excel may serialize a ten-digit ID as an exact integral decimal (including .00).
        // Never round a fractional identifier or truncate significant digits.
        string text=new(value.Select(c=>c is >= '\u0660' and <= '\u0669'?(char)('0'+c-'\u0660'):c is >= '\u06f0' and <= '\u06f9'?(char)('0'+c-'\u06f0'):c).ToArray());
        if(decimal.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&n==decimal.Truncate(n))return n.ToString("0",CultureInfo.InvariantCulture);
        return text;
    }
    static bool Close(decimal a,decimal b)=>Math.Abs(J.Money(a)-J.Money(b))<=0.01m;
    static Imports.Sheet Sheet(List<Imports.Sheet> sheets,string name)=>sheets.SingleOrDefault(s=>s.Name==name)??throw new ImportFailure("required_sheet_absent");
    static HashSet<int> MissingFormulaCache(byte[] bytes,string sheetName,HashSet<int>? selectedColumns=null)
    {
        using var zip=new ZipArchive(new MemoryStream(bytes));XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main",rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XElement Xml(string path){using var stream=(zip.GetEntry(path)??throw new ImportFailure("invalid_workbook_structure")).Open();return XElement.Load(stream);}
        var sheet=Xml("xl/workbook.xml").Descendants(ns+"sheet").Single(s=>(string?)s.Attribute("name")==sheetName);
        string id=(string)sheet.Attribute(rel+"id")!;var link=Xml("xl/_rels/workbook.xml.rels").Elements().Single(x=>(string?)x.Attribute("Id")==id);
        string target=(string)link.Attribute("Target")!;target=target.StartsWith('/')?target[1..]:"xl/"+target;
        return Xml(target).Descendants(ns+"row").Where(r=>r.Elements(ns+"c").Any(c=>c.Element(ns+"f")!=null&&string.IsNullOrEmpty(c.Element(ns+"v")?.Value)&&(selectedColumns==null||selectedColumns.Contains(Column(new string(((string?)c.Attribute("r")??"A1").TakeWhile(char.IsLetter).ToArray()))))))
            .Select(r=>int.Parse((string)r.Attribute("r")!,CultureInfo.InvariantCulture)).ToHashSet();
    }
    static void Headers(Imports.Sheet sheet,JsonArray columns)
    {
        if(sheet.Rows.Count==0)throw new ImportFailure("missing_header_row");
        foreach(var c in columns.OfType<JsonObject>())
            if(Text(Cell(sheet.Rows[0],Column(c.S("column"))))!=Text(c.S("header")))throw new ImportFailure("sheet_header_changed:"+c.S("column"));
    }
    static string Company(JsonObject rider)=>FinancialCore.FindSponsor(rider.S("financeSponsorId"))?.Id??FinancialCore.FindSponsor(rider.S("sponsorId"))?.Id??"";
    static bool Configured(JsonObject c)=>FinancialCore.CategoriesCatalog.Any(x=>x.Code==c.S("category"))&&FinanceViews.Accounts.Any(a=>a.S("code")==c.S("account")&&a.B("posting")&&a.S("status")=="active"&&a.S("code").StartsWith("1125",StringComparison.Ordinal));
    public static void EnsureCommittable(DebtSheetPreview preview)
    {if(!preview.Summary.B("canCommit"))throw new ImportFailure("debt_sheet_preview_not_approved_or_reconciled");}

    public static DebtSheetPreview Preview(byte[] debtBytes,byte[] deductionBytes,JsonObject mapping,List<JsonObject> riders)
    {
        if(mapping.N("version")!=1||mapping["paymentAllocation"].S("method")!="FIFO")throw new ImportFailure("unsupported_debt_mapping");
        var debtBook=Imports.Read(debtBytes,"debts.xlsx");var deductionBook=Imports.Read(deductionBytes,"deductions.xlsx");
        var sheet=Sheet(debtBook,mapping.Required("sheet"));var detail=Sheet(deductionBook,mapping.Required("deductionsSheet"));
        var columns=mapping["columns"] as JsonArray??throw new ImportFailure("missing_column_mapping");var deductionColumns=mapping["deductionsColumns"] as JsonArray??throw new ImportFailure("missing_deduction_mapping");
        Headers(sheet,columns);Headers(detail,deductionColumns);
        var buckets=columns.OfType<JsonObject>().Where(c=>c.S("role")=="debt").OrderBy(c=>Column(c.S("column"))).ToList();
        var payments=columns.OfType<JsonObject>().Where(c=>c.S("role")=="payment").ToList();
        if(buckets.Count==0||payments.Count==0||buckets.Any(c=>c.N("sign")!=1)||payments.Any(c=>c.N("sign")!=-1))throw new ImportFailure("invalid_fifo_column_signs");
        int Role(string role)=>Column(columns.OfType<JsonObject>().Single(c=>c.S("role")==role).S("column"));
        int identity=Role("identity"),grossColumn=Role("gross"),paymentsColumn=Role("paymentsTotal"),netColumn=Role("net");
        var riderIndex=riders.GroupBy(r=>r.S("iqama",r.S("id"))).ToDictionary(g=>g.Key,g=>g.ToList());
        var selected=columns.OfType<JsonObject>().Where(c=>c.S("role") is "debt" or "payment" or "gross" or "paymentsTotal" or "net").Select(c=>Column(c.S("column"))).ToHashSet();
        var missingCache=MissingFormulaCache(debtBytes,sheet.Name,selected);var detailCache=MissingFormulaCache(deductionBytes,detail.Name,[2,3,4,5,6,7]);
        if(buckets.Where(Configured).GroupBy(c=>c.S("category")).Any(g=>g.Select(c=>c.S("account")).Distinct().Count()>1))throw new ImportFailure("category_has_conflicting_accounts");
        var duplicates=sheet.Rows.Skip(1).Select((row,i)=>(Row:row,Number:i+2,Id:Identity(Cell(row,identity)))).Where(x=>x.Id!="").GroupBy(x=>x.Id).Where(g=>g.Count()>1).SelectMany(g=>g.Select(x=>x.Number)).ToHashSet();
        var documents=new List<PlannedDocument>();var newRiders=new List<JsonObject>();var mismatch=new SortedSet<int>();var errors=new Dictionary<string,int>();
        var resolvedCompanies=new Dictionary<string,string>();var byCompany=new Dictionary<string,decimal>();var byCategory=new Dictionary<string,decimal>();var categoryByCompany=new Dictionary<string,Dictionary<string,decimal>>();
        var unresolved=buckets.Concat(deductionColumns.OfType<JsonObject>()).Where(c=>!Configured(c)||c.S("status")=="needs_decision").Select(c=>c.S("column")).Distinct().Order().ToArray();
        decimal debtTotal=0,paymentTotal=0,sheetNetTotal=0,openingTotal=0,creditTotal=0;int rows=0,valid=0;var companyFromBranch=new SortedSet<int>();
        string fingerprint=Hash(Encoding.UTF8.GetBytes(Hash(debtBytes)+Hash(deductionBytes)+mapping.ToJsonString()));
        void Error(int number,string code){mismatch.Add(number);errors[code]=errors.GetValueOrDefault(code)+1;}
        for(int i=1;i<sheet.Rows.Count;i++)
        {
            var row=sheet.Rows[i];string iqama=Identity(Cell(row,identity));if(iqama=="")continue;int number=i+1;rows++;
            if(!System.Text.RegularExpressions.Regex.IsMatch(iqama,"^[12][0-9]{9}$")){Error(number,"invalid_identity");continue;}
            if(duplicates.Contains(number)){Error(number,"duplicate_identity");continue;}
            if(missingCache.Contains(number)){Error(number,"missing_formula_cache");continue;}
            riderIndex.TryGetValue(iqama,out var matches);
            if(matches?.Count>1){Error(number,"ambiguous_rider");continue;}
            bool newRider=matches==null;
            var rider=matches?.Single()??new JsonObject();string company=Company(rider);
            var branch=columns.OfType<JsonObject>().SingleOrDefault(c=>c.S("role")=="branch");
            string branchText=branch==null?"":Text(Cell(row,Column(branch.S("column"))));
            string alias=(mapping["companyAliases"] as JsonObject)?.S(branchText)??"";
            string branchCompany=alias==""?"":FinancialCore.FindSponsor(alias)?.Id??"";
            if(company!=""&&branchCompany!=""&&company!=branchCompany){Error(number,"company_branch_conflict");continue;}
            bool fromBranch=company==""&&branchCompany!="";
            if(fromBranch){company=branchCompany;companyFromBranch.Add(number);}
            if(company==""){Error(number,"unmatched_company");continue;}
            if(newRider)
            {
                var nameColumn=columns.OfType<JsonObject>().SingleOrDefault(c=>c.S("role")=="name");
                string name=nameColumn==null?"":Text(Cell(row,Column(nameColumn.S("column"))));
                if(name.Length is <2 or >200||name.Any(char.IsControl)){Error(number,"missing_or_invalid_debt_rider_name");continue;}
                rider=new(){["id"]=iqama,["iqama"]=iqama,["name"]=name,["financeSponsorId"]=company,["sponsorId"]=company,["status"]="Inactive",["personRole"]="debt_only",["debtOnly"]=true,["isDeliveryRider"]=false,["openingSource"]="debt_sheet",["sourceFingerprint"]=fingerprint};
            }
            try
            {
                var amounts=buckets.Select(c=>Money(Cell(row,Column(c.S("column"))))).ToArray();
                var paid=payments.Select(c=>Money(Cell(row,Column(c.S("column"))))).ToArray();
                if(amounts.Any(x=>x<0)||paid.Any(x=>x<0)){Error(number,"negative_bucket_needs_decision");continue;}
                decimal gross=J.Money(amounts.Sum()),payment=J.Money(paid.Sum()),sheetNet=Money(Cell(row,netColumn));
                debtTotal+=gross;paymentTotal+=payment;sheetNetTotal+=sheetNet;
                if(!Close(gross,Money(Cell(row,grossColumn))))Error(number,"gross_mismatch");
                if(!Close(payment,Money(Cell(row,paymentsColumn))))Error(number,"payments_mismatch");
                decimal left=payment,credit=Math.Max(0,J.Money(payment-gross));var remainder=new Dictionary<(string Category,string Account),decimal>();
                for(int b=0;b<buckets.Count;b++)
                {
                    decimal allocation=Math.Min(left,amounts[b]);left=J.Money(left-allocation);decimal balance=J.Money(amounts[b]-allocation);
                    if(balance==0)continue;
                    var c=buckets[b];string category=Configured(c)?c.S("category"):"needs_decision",account=c.S("account");
                    remainder[(category,account)]=remainder.GetValueOrDefault((category,account))+balance;
                }
                decimal opening=J.Money(remainder.Values.Sum());
                if(!Close(opening-credit,sheetNet))Error(number,"net_mismatch");
                if(mismatch.Contains(number))continue;
                if(newRider)newRiders.Add(rider);
                resolvedCompanies[iqama]=company;valid++;openingTotal+=opening;creditTotal+=credit;byCompany[company]=byCompany.GetValueOrDefault(company)+sheetNet;
                if(!categoryByCompany.TryGetValue(company,out var categoryTotals))categoryByCompany[company]=categoryTotals=[];
                foreach(var group in remainder)
                {
                    byCategory[group.Key.Category]=byCategory.GetValueOrDefault(group.Key.Category)+group.Value;
                    categoryTotals[group.Key.Category]=categoryTotals.GetValueOrDefault(group.Key.Category)+group.Value;
                    if(group.Key.Category=="needs_decision")continue;
                    string reference=$"DEBT-SHEET:{number}:{group.Key.Category}";
                    var meta=FinancialCore.ResolveCategory(group.Key.Category);
                    documents.Add(new("debt",new(){["id"]=reference,["sourceRef"]=reference,["sourceRow"]=number,["sourceFingerprint"]=fingerprint,["companyFromBranch"]=fromBranch,["iqama"]=iqama,["riderName"]=rider.S("name"),["debtorType"]="Rider",
                        ["category"]=meta.Code,["categoryAr"]=meta.NameAr,["sector"]=meta.Sector,["entryMode"]="OpeningBalance",["allocationMode"]="RiderFull",["receivableAccountCode"]=group.Key.Account,["targetAccountCode"]=group.Key.Account,
                        ["creditorSponsorId"]=company,["originSponsorId"]=company,["sponsorshipSponsorId"]=company,["totalAmount"]=group.Value,["totalCostAmount"]=group.Value,["remainingBalance"]=group.Value,["paidAmount"]=0,["waivedAmount"]=0,["monthlyInstallment"]=0,
                        ["status"]="pending_approval",["openingSource"]="debt_sheet",["openingReviewRequired"]=true,["effectiveFrom"]=mapping.S("effectiveFrom"),["createdBy"]="debt-sheet-import"}));
                }
                if(credit>0)
                {
                    string reference=$"DEBT-SHEET:{number}:CREDIT";
                    documents.Add(new("rider_credit",new(){["id"]=reference,["sourceRef"]=reference,["sourceRow"]=number,["sourceFingerprint"]=fingerprint,["companyFromBranch"]=fromBranch,["iqama"]=iqama,["companyId"]=company,["amount"]=credit,["status"]="pending_approval",["openingSource"]="debt_sheet",["openingReviewRequired"]=true}));
                }
            }
            catch(ImportFailure){Error(number,"invalid_numeric_cell");}
        }
        // Read the final receipt sheet as independent source evidence. It is never converted
        // into a second payment or matched to an unrelated Jahez request.
        var receiptCompanies=new Dictionary<string,HashSet<string>>();var receiptMismatches=new SortedSet<int>();
        var receiptCompanyTotals=new Dictionary<string,decimal>();var receiptReferences=new HashSet<string>();int receiptRows=0;decimal receiptTotal=0;
        bool receiptAvailable=mapping.S("receiptSheet")!="";
        if(receiptAvailable)
        {
            var receipts=Sheet(deductionBook,mapping.S("receiptSheet"));if(mapping["receiptHeaders"] is JsonArray receiptHeaders)Headers(receipts,receiptHeaders);var rc=mapping["receiptColumns"] as JsonObject??throw new ImportFailure("missing_receipt_mapping");
            int idCol=Column(rc.Required("identity")),amountCol=Column(rc.Required("amount")),companyCol=Column(rc.Required("company")),referenceCol=Column(rc.Required("reference"));
            var cache=MissingFormulaCache(deductionBytes,receipts.Name,[amountCol]);
            for(int i=1;i<receipts.Rows.Count;i++)
            {
                var row=receipts.Rows[i];string iqama=Identity(Cell(row,idCol));if(iqama=="")continue;int number=i+1;
                if(!System.Text.RegularExpressions.Regex.IsMatch(iqama,"^[12][0-9]{9}$")||cache.Contains(number)){receiptMismatches.Add(number);continue;}
                try
                {
                    decimal amount=Money(Cell(row,amountCol));receiptRows++;receiptTotal+=amount;
                    string reference=Cell(row,referenceCol);if(reference!="")receiptReferences.Add(reference);
                    string companyText=Text(Cell(row,companyCol));
                    string approvedAlias=(mapping["companyAliases"] as JsonObject)?.S(companyText)??"";
                    var sponsor=FinancialCore.FindSponsor(approvedAlias!=""?approvedAlias:companyText);if(sponsor==null){receiptMismatches.Add(number);continue;}
                    receiptCompanyTotals[sponsor.Id]=receiptCompanyTotals.GetValueOrDefault(sponsor.Id)+amount;
                    if(!receiptCompanies.TryGetValue(iqama,out var set))receiptCompanies[iqama]=set=[];set.Add(sponsor.Id);
                }
                catch(ImportFailure){receiptMismatches.Add(number);}
            }
        }
        // Monthly deduction flows reconcile independently from the historical opening stock.
        var detailErrors=new SortedSet<int>();var detailTotals=deductionColumns.OfType<JsonObject>().ToDictionary(c=>c.S("column"),_=>0m);
        var detailCompany=new Dictionary<string,Dictionary<string,decimal>>();int detailRows=0;
        for(int i=1;i<detail.Rows.Count;i++)
        {
            var row=detail.Rows[i];string iqama=Identity(Cell(row,0));if(iqama=="")continue;int number=i+1;
            if(!System.Text.RegularExpressions.Regex.IsMatch(iqama,"^[12][0-9]{9}$")){detailErrors.Add(number);continue;}detailRows++;
            try
            {
                if(detailCache.Contains(number)){detailErrors.Add(number);continue;}
                var amounts=deductionColumns.OfType<JsonObject>().ToDictionary(c=>c.S("column"),c=>Money(Cell(row,Column(c.S("column")))));
                if(!Close(amounts.Values.Sum(),Money(Cell(row,7))))detailErrors.Add(number);
                foreach(var a in amounts)detailTotals[a.Key]+=a.Value;
                string company=receiptAvailable?(receiptCompanies.TryGetValue(iqama,out var sourceCompanies)&&sourceCompanies.Count==1?sourceCompanies.Single():""):
                    resolvedCompanies.GetValueOrDefault(iqama)??(riderIndex.TryGetValue(iqama,out var rr)&&rr.Count==1?Company(rr[0]):"");
                if(company==""){detailErrors.Add(number);continue;}
                if(!detailCompany.TryGetValue(company,out var totals))detailCompany[company]=totals=amounts.Keys.ToDictionary(k=>k,_=>0m);
                foreach(var a in amounts)totals[a.Key]+=a.Value;
            }
            catch(ImportFailure){detailErrors.Add(number);}
        }
        var controlRows=new SortedSet<int>();var controlComparisons=new List<JsonObject>();
        var controls=mapping["controlSections"] as JsonArray??[];bool controlAvailable=controls.Count>0;
        if(controlAvailable)
        {
            var control=Sheet(deductionBook,mapping.Required("controlSheet"));var controlCache=MissingFormulaCache(deductionBytes,control.Name);
            foreach(var section in controls.OfType<JsonObject>())
            {
                string column=section.Required("deductionColumn"),cell=section.Required("totalCell");int n=int.Parse(new string(cell.Where(char.IsDigit).ToArray()),CultureInfo.InvariantCulture);int c=Column(new string(cell.Where(char.IsLetter).ToArray()));
                decimal expected=Money(Cell(control.Rows.ElementAtOrDefault(n-1)??[],c)),actual=detailTotals.GetValueOrDefault(column);
                bool match=Close(actual,expected)&&!controlCache.Contains(n);if(!match)controlRows.Add(n);
                controlComparisons.Add(new(){["deductionColumn"]=column,["scope"]="ALL",["actual"]=actual,["expected"]=expected,["matched"]=match});
                foreach(var group in (section["companies"] as JsonArray??[]).OfType<JsonObject>())
                {
                    string company=FinancialCore.ResolveSponsor(group.Required("company")).Id;int col=Column(group.Required("valueColumn")),from=(int)group.N("fromRow"),to=(int)group.N("toRow");
                    decimal amount=0;bool cached=true;for(int row=from;row<=to;row++){amount+=Money(Cell(control.Rows.ElementAtOrDefault(row-1)??[],col));cached&=!controlCache.Contains(row);}
                    decimal found=detailCompany.GetValueOrDefault(company)?.GetValueOrDefault(column)??0;bool same=Close(amount,found)&&cached;
                    if(!same)for(int row=from;row<=to;row++)controlRows.Add(row);
                    controlComparisons.Add(new(){["deductionColumn"]=column,["companyId"]=company,["actual"]=found,["expected"]=amount,["matched"]=same});
                }
            }
        }
        bool approved=mapping.S("status")=="approved"&&mapping.S("approvedBy")=="Ghazal";
        bool period=System.Text.RegularExpressions.Regex.IsMatch(mapping.S("effectiveFrom"),"^[0-9]{4}-(0[1-9]|1[0-2])$");
        var summary=new JsonObject{["company_from_branch"]=new JsonArray(companyFromBranch.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()),["sourceRows"]=rows,["validRows"]=valid,["debtBucketTotal"]=J.Money(debtTotal),["paymentsTotal"]=J.Money(paymentTotal),["sheetNetTotal"]=J.Money(sheetNetTotal),["openingDebtTotal"]=J.Money(openingTotal),["riderCreditTotal"]=J.Money(creditTotal),["aggregateNetRoundingDelta"]=J.Money(openingTotal-creditTotal-byCompany.Values.Sum()),
            ["inactiveDebtOnlyRiders"]=newRiders.Count,["openingDebts"]=documents.Count(d=>d.Kind=="debt"),["riderCredits"]=documents.Count(d=>d.Kind=="rider_credit"),["mismatchedRows"]=new JsonArray(mismatch.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()),["errorCounts"]=System.Text.Json.JsonSerializer.SerializeToNode(errors),
            ["companyNetTotals"]=System.Text.Json.JsonSerializer.SerializeToNode(byCompany),["categoryOpeningTotals"]=System.Text.Json.JsonSerializer.SerializeToNode(byCategory),["companyCategoryOpeningTotals"]=System.Text.Json.JsonSerializer.SerializeToNode(categoryByCompany),
            ["receiptAvailable"]=receiptAvailable,["receiptRows"]=receiptRows,["receiptTotal"]=J.Money(receiptTotal),["receiptReferenceCount"]=receiptReferences.Count,["receiptCompanyTotals"]=System.Text.Json.JsonSerializer.SerializeToNode(receiptCompanyTotals),["receiptMismatchedRows"]=new JsonArray(receiptMismatches.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()),["receiptGrandMatched"]=!receiptAvailable||Close(receiptTotal,detailTotals.Values.Sum()),["erpAdvanceReferenceComparisonAvailable"]=false,
            ["deductionRows"]=detailRows,["deductionTotalsByColumn"]=System.Text.Json.JsonSerializer.SerializeToNode(detailTotals),["deductionMismatchedRows"]=new JsonArray(detailErrors.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()),["controlAvailable"]=controlAvailable,["controlComparisons"]=new JsonArray(controlComparisons.Select(x=>(JsonNode)x).ToArray()),["controlMismatchedRows"]=new JsonArray(controlRows.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()),
            ["unresolvedColumns"]=new JsonArray(unresolved.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()),["mappingApproved"]=approved,["effectivePeriodConfigured"]=period,["committed"]=false,
            ["octoberDecisionRequired"]=mapping.S("octoberLoadMode")=="pending_decision",
            ["canCommit"]=mapping.S("octoberLoadMode")!="pending_decision"&&approved&&period&&unresolved.Length==0&&rows>0&&mismatch.Count==0&&detailErrors.Count==0&&controlAvailable&&controlRows.Count==0&&receiptMismatches.Count==0&&(!receiptAvailable||Close(receiptTotal,detailTotals.Values.Sum()))};
        return new(summary,documents,fingerprint,newRiders);
    }
    public static List<JsonObject> Riders(DbConnection c)=>Store.List(c,"rider");
    public static int Commit(Store target,DebtSheetPreview preview)
    {
        EnsureCommittable(preview);
        return target.Write((c,tx)=>
        {
            var existing=Store.List(c,"debt",tx).Concat(Store.List(c,"rider_credit",tx)).Where(d=>d.S("sourceRef").StartsWith("DEBT-SHEET:",StringComparison.Ordinal)).ToDictionary(d=>d.S("sourceRef"));
            int added=0;
            foreach(var planned in preview.NewRiders)
            {
                var current=Store.Get(c,planned.S("id"),"rider",tx);
                if(current!=null)
                {
                    if(current.S("sourceFingerprint")!=preview.Fingerprint||Company(current)!=Company(planned))throw new ImportFailure("debt_sheet_target_rider_changed");
                    continue;
                }
                // Store's identity collision guard and this transaction make rider + debts atomic.
                Store.Save(c,"rider",planned.Clone(),tx);Store.Audit(c,tx,"debt.sheet.inactive_rider_staged",planned.S("id"),"debt-sheet-import");added++;
            }
            foreach(var document in preview.Documents)
            {
                var rider=Store.Get(c,document.Body.S("iqama"),"rider",tx);string company=document.Kind=="debt"?document.Body.S("creditorSponsorId"):document.Body.S("companyId");
                if(rider==null||(Company(rider)!=company&&!(Company(rider)==""&&document.Body.B("companyFromBranch"))))throw new ImportFailure("debt_sheet_target_rider_changed");
                if(existing.TryGetValue(document.Body.S("sourceRef"),out var previous))
                {
                    string oldCompany=document.Kind=="debt"?previous.S("creditorSponsorId"):previous.S("companyId");
                    if(previous.S("sourceFingerprint")!=preview.Fingerprint||previous.S("iqama")!=document.Body.S("iqama")||oldCompany!=company)throw new ImportFailure("debt_sheet_row_changed_requires_review");continue;
                }
                if(Store.Get(c,document.Body.S("id"),document.Kind,tx)!=null)throw new ImportFailure("debt_sheet_identity_collision");
                var body=document.Body.Clone();body["createdAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,document.Kind,body,tx);Store.Audit(c,tx,"debt.sheet.opening_staged",body.S("id"),"debt-sheet-import");added++;
            }
            return added;
        });
    }
}
