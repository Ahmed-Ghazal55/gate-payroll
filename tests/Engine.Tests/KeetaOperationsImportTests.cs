using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class KeetaOperationsImportTests
{
    const string Rider="2000000091",AccountId="synthetic-keeta-ops-account",ExternalId="1000000000000001",InvoiceId="synthetic-keeta-ops-invoice";
    static string Company=>FinancialCore.Sponsors[0].Id; // BW
    static string Register=>"BW";

    static void Seed(Store store,decimal invoiceOrders=5,bool deductions=true)
    {
        store.Write((c,tx)=>{
            Store.Save(c,"rider",new(){["id"]=Rider,["iqama"]=Rider,["sponsorId"]=Company,["isDeliveryRider"]=true},tx);
            Store.Save(c,"platform_account",new(){["id"]=AccountId,["externalAccountId"]=ExternalId,["code"]="SYN-K-OPS",["companyId"]=Company,["platformCode"]="keeta",["registeredRiderId"]="2000000092",["assignments"]=new JsonArray(new JsonObject{["riderId"]=Rider,["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00",["RawEffectiveTo"]="2026-11-01T00:00:00+03:00"})},tx);
            Store.Save(c,"platform_invoice",new(){["id"]=InvoiceId,["companyId"]=Company,["platform"]=PayPlatforms.For("keeta").Platform,["period"]="2026-10",["approvalStatus"]="approved",["status"]="final",["amountExclVat"]=110,["vat"]=16.5m,["deductionsTotal"]=deductions?8:0},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]="synthetic-keeta-ops-line",["invoiceId"]=InvoiceId,["platformAccountId"]=AccountId,["baseGross"]=100,["incentives"]=10,["orders"]=invoiceOrders,["deductionAmount"]=deductions?8:0,["deductions"]=deductions?new JsonArray(new JsonObject{["category"]="penalty",["amount"]=3,["level"]="account_level"},new JsonObject{["category"]="company_shortfall",["amount"]=5,["level"]="company_level",["expenseAccount"]="512101"}):new JsonArray()},tx);
            return true;
        });
    }

    static byte[] Workbook(Action<Dictionary<string,object?>>? edit=null,bool dismissed=false,bool secondRider=false)
    {
        var row=KeetaOperationsImport.Headers.ToDictionary(h=>h,h=>(object?)"");
        row["المعرف"]=ExternalId;
        row["الاسم بالكامل"]="Synthetic Rider";
        row["رقم بطاقة الهوية"]=Rider;
        row["كفالة اليوزر"]="سجل البوابا";
        row["المركبة"]="خاصة";
        row["الحالة"]=dismissed?"FALSE":"TRUE";
        row["رقم الهوية1"]=Rider;
        row["البديل 1"]="";
        row["نوع البديل"]="";
        row["نوع المركبة"]="خاصة";
        row["تاريخ الاستلام1"]="2026-10-01";
        row["تاريخ التسليم"]="2026-10-31";
        row["الأيام الصالحة"]=20;
        row["الطلبات"]=5;
        row["المخالفات"]=0;
        row["المسافة المدفوعة"]=12.5m;
        row["عدد ايام عمل المندوب"]=18;
        row["الصلاحية المتوقعة"]="شريحة تجريبية متوقعة";
        row["اجمالي المسافه"]=12.5m;
        edit?.Invoke(row);
        var rows=new List<object?[]> { KeetaOperationsImport.Headers.Select(h=>row[h]).ToArray() };
        if(secondRider)
        {
            const string rider2="2000000093";
            var row2=new Dictionary<string,object?>(row)
            {
                ["رقم الهوية1"]=rider2,
                ["رقم بطاقة الهوية"]=Rider,
                ["الطلبات"]=2,
                ["الأيام الصالحة"]=5,
                ["الحالة"]="TRUE"
            };
            // first row keeps remaining orders so sum matches invoice when Seed uses 7
            rows.Add(KeetaOperationsImport.Headers.Select(h=>row2[h]).ToArray());
        }
        return ExcelFiles.Write(KeetaOperationsImport.Columns,rows);
    }

    static JsonObject Input()=>new(){["invoiceId"]=InvoiceId,["register"]=Register};
    static JsonObject Confirm(JsonObject preview)=>new(){["invoiceId"]=InvoiceId,["register"]=Register,["confirm"]=true,["rows"]=preview["rows"]!.DeepClone(),["fingerprint"]=preview.S("fingerprint")};

    [TestMethod]
    public void ArabicRegisterSheetPreviewAndCommitKeepsExpectedOnlyAndFlagsDismissed()
    {
        using var f=new PayFixture();Seed(f.Store);f.Approve("pay_terms","keeta",f.Draft("pay_terms","keeta",PayFixture.Terms()));
        var bytes=Workbook(dismissed:true);
        var preview=f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,"keeta",Input(),bytes));
        Assert.AreEqual("BW",preview.S("register"));
        Assert.AreEqual(KeetaOperationsImport.Headers.Length,preview["columns"]!.AsArray().Count);
        Assert.AreEqual(1,preview["displayRows"]!.AsArray().Count);
        Assert.IsTrue(preview["displayRows"]![0]!.AsObject().B("dismissed"));
        Assert.AreEqual(1,preview["counts"]!.AsObject().N("dismissed"));
        var review=preview["rows"]![0]!.AsObject();
        Assert.AreEqual("keeta",review.S("app"));
        Assert.IsFalse(review.ContainsKey("displayRows"),"cycle review stays closed");
        var share=review["accounts"]![0]!["riders"]![0]!.AsObject();
        Assert.AreEqual("expected_only",share["metrics"]!.AsObject().S("expectedApproval"));
        Assert.IsFalse(share["metrics"]!.AsObject().B("expectedFinal"));
        Assert.IsTrue(share["metrics"]!.AsObject().B("dismissed"));
        Assert.AreEqual("سجل البوابا",share["metrics"]!.AsObject().S("ownerRegister"));
        var saved=f.Store.Write((c,tx)=>OperationsImports.Commit(c,tx,PayFixture.Maker,"keeta",Confirm(preview),bytes));
        Assert.AreEqual("BW",saved.S("register"));
        Assert.AreEqual(1,saved["displayRows"]!.AsArray().Count);
        Assert.IsTrue(JsonNode.DeepEquals(review,saved["review"]));
    }

    [TestMethod]
    public void RegisterMustBeChosenAndMustMatchInvoiceCompany()
    {
        using var f=new PayFixture();Seed(f.Store);
        var bytes=Workbook();
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,"keeta",new(){["invoiceId"]=InvoiceId},bytes)));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,"keeta",new(){["invoiceId"]=InvoiceId,["register"]="EX"},bytes)));
    }

    [TestMethod]
    public void TemplateHeadersAreArabicAtoAH()
    {
        var step=ImportCatalog.Steps().Single(s=>s.Key=="keeta_operations");
        CollectionAssert.AreEqual(KeetaOperationsImport.Headers,step.Columns.Select(c=>c.Header).ToArray());
        var file=(Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult)ImportCatalog.Template("operations-keeta");
        var rows=Imports.Read(file.FileContents.ToArray(),"template.xlsx")[0].Rows;
        CollectionAssert.AreEqual(KeetaOperationsImport.Headers,rows[0].Take(KeetaOperationsImport.Headers.Length).ToArray());
    }

    [TestMethod]
    public void MultiRiderSameIdIsAllowedWhenOrdersMatchInvoice()
    {
        using var f=new PayFixture();
        Seed(f.Store,invoiceOrders:7,deductions:false);
        f.Store.Write((c,tx)=>{
            Store.Save(c,"rider",new(){["id"]="2000000093",["iqama"]="2000000093",["sponsorId"]=Company,["isDeliveryRider"]=true},tx);
            var account=Store.Get(c,AccountId,"platform_account",tx)!;
            account["assignments"]=new JsonArray(
                new JsonObject{["riderId"]=Rider,["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00",["RawEffectiveTo"]="2026-11-01T00:00:00+03:00"},
                new JsonObject{["riderId"]="2000000093",["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00",["RawEffectiveTo"]="2026-11-01T00:00:00+03:00"});
            Store.Save(c,"platform_account",account,tx);return true;
        });
        var bytes=Workbook(r=>r["الطلبات"]=5,secondRider:true);
        var preview=f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,"keeta",Input(),bytes));
        Assert.AreEqual(2,preview["displayRows"]!.AsArray().Count);
        Assert.AreEqual(2,preview["counts"]!.AsObject().N("duplicateIdRows"));
        Assert.AreEqual(2,preview["rows"]![0]!["accounts"]![0]!["riders"]!.AsArray().Count);
    }

    [TestMethod]
    public void SameRiderOnTwoIdsMayDifferInDaysAndOrders()
    {
        using var f=new PayFixture();
        const string account2="synthetic-keeta-ops-account-2",external2="1000000000000002",line2="synthetic-keeta-ops-line-2";
        Seed(f.Store,invoiceOrders:5,deductions:false);
        f.Store.Write((c,tx)=>{
            Store.Save(c,"platform_account",new(){["id"]=account2,["externalAccountId"]=external2,["code"]="SYN-K-OPS-2",["companyId"]=Company,["platformCode"]="keeta",["registeredRiderId"]="2000000092",["assignments"]=new JsonArray(new JsonObject{["riderId"]=Rider,["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00",["RawEffectiveTo"]="2026-11-01T00:00:00+03:00"})},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]=line2,["invoiceId"]=InvoiceId,["platformAccountId"]=account2,["baseGross"]=40,["incentives"]=0,["orders"]=3,["deductionAmount"]=0,["deductions"]=new JsonArray()},tx);
            // Seed line keeps baseGross 100 + incentives 10; plus this line 40 → 150.
            var inv=Store.Get(c,InvoiceId,"platform_invoice",tx)!;inv["amountExclVat"]=150;Store.Save(c,"platform_invoice",inv,tx);
            return true;
        });
        f.Approve("pay_terms","keeta",f.Draft("pay_terms","keeta",PayFixture.Terms()));
        // Same rider on two IDs with different days/orders (month-wide identity check must not run on Keeta).
        var baseRow=KeetaOperationsImport.Headers.ToDictionary(h=>h,h=>(object?)"");
        baseRow["المعرف"]=ExternalId;baseRow["الاسم بالكامل"]="Synthetic Rider";baseRow["رقم بطاقة الهوية"]=Rider;baseRow["كفالة اليوزر"]="سجل البوابا";
        baseRow["المركبة"]="خاصة";baseRow["الحالة"]="TRUE";baseRow["رقم الهوية1"]=Rider;baseRow["نوع المركبة"]="خاصة";
        baseRow["تاريخ الاستلام1"]="2026-10-01";baseRow["تاريخ التسليم"]="2026-10-31";baseRow["الأيام الصالحة"]=20;baseRow["الطلبات"]=5;
        baseRow["المخالفات"]=0;baseRow["المسافة المدفوعة"]=1;baseRow["عدد ايام عمل المندوب"]=18;baseRow["الصلاحية المتوقعة"]="شريحة";baseRow["اجمالي المسافه"]=1;
        var other=new Dictionary<string,object?>(baseRow){["المعرف"]=external2,["الأيام الصالحة"]=7,["الطلبات"]=3,["عدد ايام عمل المندوب"]=6};
        var bytes=ExcelFiles.Write(KeetaOperationsImport.Columns,new List<object?[]>{
            KeetaOperationsImport.Headers.Select(h=>baseRow[h]).ToArray(),
            KeetaOperationsImport.Headers.Select(h=>other[h]).ToArray()
        });
        var preview=f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,"keeta",Input(),bytes));
        Assert.AreEqual(2,preview["displayRows"]!.AsArray().Count);
        var riders=preview["rows"]![0]!["accounts"]!.AsArray().OfType<JsonObject>().SelectMany(a=>a["riders"]!.AsArray().OfType<JsonObject>()).ToList();
        Assert.AreEqual(2,riders.Count);
        Assert.AreEqual(1,riders.Select(r=>r.S("rider")).Distinct().Count());
        Assert.AreEqual(2,riders.Select(r=>r.N("days")).Distinct().Count());
    }

    [TestMethod]
    public void KeetaAcceptsSpanOverlappingInvoiceMonth_KeepsRealDates()
    {
        using var f=new PayFixture();Seed(f.Store,deductions:false);f.Approve("pay_terms","keeta",f.Draft("pay_terms","keeta",PayFixture.Terms()));
        var bytes=Workbook(r=>{r["تاريخ الاستلام1"]="2026-09-28";r["تاريخ التسليم"]="2026-10-15";});
        var preview=f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,"keeta",Input(),bytes));
        var metrics=preview["rows"]![0]!["accounts"]![0]!["riders"]![0]!["metrics"]!.AsObject();
        Assert.AreEqual("2026-09-28",metrics.S("idStart"));
        Assert.AreEqual("2026-10-15",metrics.S("idStop"));
        Assert.AreEqual("2026-09-28",metrics.S("riderStart"));
        Assert.AreEqual("2026-10-15",metrics.S("riderStop"));
    }

    [TestMethod]
    public void KeetaRejectsSpanWithNoOverlapOfInvoiceMonth()
    {
        using var f=new PayFixture();Seed(f.Store,deductions:false);
        var bytes=Workbook(r=>{r["تاريخ الاستلام1"]="2026-09-01";r["تاريخ التسليم"]="2026-09-15";});
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,"keeta",Input(),bytes)));
        StringAssert.Contains(err.Message,"تتقاطع");
    }

    [TestMethod]
    public void RealJeddahRegisterSheets_ReportCountsOnly_NoValues()
    {
        string path="/home/ghazal/Projects/pro win/02 بيانات وتشغيل المنصات/01 كيتا/01 فواتير وتسويات أكتوبر 2026/_بيانات فاتورة اكتوبر جدة.xlsx";
        if(!File.Exists(path)){Assert.Inconclusive("reference workbook not present");return;}
        var bytes=File.ReadAllBytes(path);
        var sheets=Imports.Read(bytes,"ref.xlsx");
        var report=new Dictionary<string,(int Rows,int DupIdRows,int Dismissed)>(StringComparer.Ordinal);
        foreach(string name in new[]{"Express","Albwaba","Togary"})
        {
            var sheet=sheets.Single(s=>s.Name==name);
            var header=sheet.Rows[0].Select(v=>v.Trim()).ToList();
            Assert.IsTrue(header.Count>=KeetaOperationsImport.Headers.Length);
            for(int i=0;i<KeetaOperationsImport.Headers.Length;i++)
                Assert.AreEqual(KeetaOperationsImport.Headers[i],header[i],name+" header order");
            int idCol=0,statusCol=Array.IndexOf(KeetaOperationsImport.Headers,"الحالة");
            int ordersCol=Array.IndexOf(KeetaOperationsImport.Headers,"الطلبات");
            int daysCol=Array.IndexOf(KeetaOperationsImport.Headers,"الأيام الصالحة");
            int workCol=Array.IndexOf(KeetaOperationsImport.Headers,"عدد ايام عمل المندوب");
            var idCounts=new Dictionary<string,int>(StringComparer.Ordinal);
            int rows=0,dismissed=0;
            for(int r=1;r<sheet.Rows.Count;r++)
            {
                var values=sheet.Rows[r];
                if(values.Take(KeetaOperationsImport.Headers.Length).All(string.IsNullOrWhiteSpace))continue;
                rows++;
                string id=idCol<values.Count?values[idCol].Trim():"";
                if(id!="")idCounts[id]=idCounts.GetValueOrDefault(id)+1;
                string status=statusCol<values.Count?values[statusCol].Trim():"";
                bool active=status.Equals("TRUE",StringComparison.OrdinalIgnoreCase)||status=="1"||status=="نعم"||status=="نشط";
                decimal Num(int col)
                {
                    if(col<0||col>=values.Count||string.IsNullOrWhiteSpace(values[col]))return 0;
                    return decimal.TryParse(J.Digits(values[col]),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var n)?n:0;
                }
                bool perf=Num(ordersCol)>0||Num(daysCol)>0||Num(workCol)>0;
                if(!active&&perf)dismissed++;
            }
            int dup=idCounts.Where(kv=>kv.Value>1).Sum(kv=>kv.Value);
            report[name]=(rows,dup,dismissed);
        }
        // Counts only — never assert on identities or amounts from the real file.
        Assert.IsTrue(report["Express"].Rows>0);
        Assert.IsTrue(report["Albwaba"].Rows>0);
        Assert.IsTrue(report["Togary"].Rows>0);
        TestContext.WriteLine($"Express rows={report["Express"].Rows} duplicateIdRows={report["Express"].DupIdRows} dismissed={report["Express"].Dismissed}");
        TestContext.WriteLine($"Albwaba rows={report["Albwaba"].Rows} duplicateIdRows={report["Albwaba"].DupIdRows} dismissed={report["Albwaba"].Dismissed}");
        TestContext.WriteLine($"Togary rows={report["Togary"].Rows} duplicateIdRows={report["Togary"].DupIdRows} dismissed={report["Togary"].Dismissed}");
        Directory.CreateDirectory("/home/ghazal/.cache/agents-runs");
        File.WriteAllText("/home/ghazal/.cache/agents-runs/cursor-KOPS1-real-sheet-counts.txt",
            string.Join('\n',report.Select(kv=>$"{kv.Key}\trows={kv.Value.Rows}\tduplicateIdRows={kv.Value.DupIdRows}\tdismissed={kv.Value.Dismissed}"))+"\n");
    }

    /// <summary>
    /// Real Keeta import PREVIEW (not parser-only) on Express/Albwaba/Togary against a temp store.
    /// Seeds synthetic platform accounts for sheet IDs. Report: accepted + rejected reason codes only (no personal values).
    /// </summary>
    [TestMethod]
    public void RealJeddahRegisterSheets_PreviewEndToEnd_ReasonCodesOnly()
    {
        string path="/home/ghazal/Projects/pro win/02 بيانات وتشغيل المنصات/01 كيتا/01 فواتير وتسويات أكتوبر 2026/_بيانات فاتورة اكتوبر جدة.xlsx";
        if(!File.Exists(path)){Assert.Inconclusive("reference workbook not present");return;}
        var bytes=File.ReadAllBytes(path); // read-only; never copy into the repo
        var sheets=Imports.Read(bytes,"ref.xlsx");
        var lines=new List<string>();
        foreach(var (sheetName,register) in new[]{("Express","EX"),("Albwaba","BW"),("Togary","TR")})
        {
            var sheet=sheets.Single(s=>s.Name==sheetName);
            var sponsor=FinancialCore.Sponsors.Single(s=>s.Code==register);
            string company=sponsor.Id,period="2026-10",invoiceId="e2e-keeta-inv-"+register;
            int idCol=Array.IndexOf(KeetaOperationsImport.Headers,"المعرف");
            int ownerCol=Array.IndexOf(KeetaOperationsImport.Headers,"رقم بطاقة الهوية");
            int riderCol=Array.IndexOf(KeetaOperationsImport.Headers,"رقم الهوية1");
            int ordersCol=Array.IndexOf(KeetaOperationsImport.Headers,"الطلبات");
            var dataRows=new List<(int ExcelRow,string AccountKey,string RiderRaw,decimal Orders)>();
            var orderById=new Dictionary<string,decimal>(StringComparer.Ordinal);
            var ridersById=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
            for(int r=1;r<sheet.Rows.Count;r++)
            {
                var values=sheet.Rows[r];
                if(values.Take(KeetaOperationsImport.Headers.Length).All(string.IsNullOrWhiteSpace))continue;
                string accountKey=idCol<values.Count?values[idCol].Trim():"";
                string owner=ownerCol<values.Count?values[ownerCol].Trim():"";
                string riderRaw=riderCol<values.Count?values[riderCol].Trim():"";
                if(riderRaw=="")riderRaw=owner;
                decimal orders=0;
                if(ordersCol>=0&&ordersCol<values.Count&&!string.IsNullOrWhiteSpace(values[ordersCol]))
                    decimal.TryParse(J.Digits(values[ordersCol]),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out orders);
                dataRows.Add((r+1,accountKey,riderRaw,orders));
                if(accountKey!="")orderById[accountKey]=orderById.GetValueOrDefault(accountKey)+orders;
                if(accountKey!=""&&riderRaw!="")
                {
                    if(!ridersById.TryGetValue(accountKey,out var set))ridersById[accountKey]=set=new HashSet<string>(StringComparer.Ordinal);
                    set.Add(riderRaw);
                }
            }

            using var f=new PayFixture();
            f.Store.Write((c,tx)=>{
                decimal gross=0;int n=0;
                foreach(var (accountKey,orders) in orderById)
                {
                    n++;
                    string accountId="e2e-acc-"+register+"-"+n;
                    decimal baseGross=Math.Max(orders,1);
                    gross+=baseGross;
                    var assignments=new JsonArray();
                    foreach(var riderRaw in ridersById.GetValueOrDefault(accountKey)??[])
                    {
                        string iqama;
                        try{iqama=J.Iqama(riderRaw);}catch{continue;}
                        Store.Save(c,"rider",new(){["id"]=iqama,["iqama"]=iqama,["sponsorId"]=company,["isDeliveryRider"]=true},tx);
                        assignments.Add(new JsonObject{["riderId"]=iqama,["RawEffectiveFrom"]="2026-09-01T00:00:00+03:00",["RawEffectiveTo"]="2026-11-01T00:00:00+03:00"});
                    }
                    Store.Save(c,"platform_account",new(){["id"]=accountId,["externalAccountId"]=accountKey,["code"]="E2E-"+register+"-"+n,["companyId"]=company,["platformCode"]="keeta",["registeredRiderId"]="2000000099",["assignments"]=assignments},tx);
                    Store.Save(c,"platform_invoice_line",new(){["id"]="e2e-line-"+register+"-"+n,["invoiceId"]=invoiceId,["platformAccountId"]=accountId,["baseGross"]=baseGross,["incentives"]=0,["orders"]=orders,["deductionAmount"]=0,["deductions"]=new JsonArray()},tx);
                }
                Store.Save(c,"platform_invoice",new(){["id"]=invoiceId,["companyId"]=company,["platform"]=PayPlatforms.For("keeta").Platform,["period"]=period,["approvalStatus"]="approved",["status"]="final",["amountExclVat"]=gross,["vat"]=0,["deductionsTotal"]=0},tx);
                return true;
            });
            f.Approve("pay_terms","keeta",f.Draft("pay_terms","keeta",PayFixture.Terms()));

            int accepted=0;
            var rejected=new Dictionary<string,int>(StringComparer.Ordinal);
            try
            {
                var preview=f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,"keeta",new(){["invoiceId"]=invoiceId,["register"]=register},bytes));
                accepted=(int)preview["counts"]!.AsObject().N("rows");
                Assert.AreEqual(sheetName,preview.S("registerSheet"));
            }
            catch(ApiError)
            {
                // Whole-sheet preview failed: isolate per row with a one-row workbook + matching one-account invoice.
                foreach(var (excelRow,accountKey,riderRaw,orders) in dataRows)
                {
                    string code=TryPreviewOneRow(sheet,excelRow,register,company,period,accountKey,riderRaw,orders);
                    if(code=="accepted")accepted++;
                    else rejected[code]=rejected.GetValueOrDefault(code)+1;
                }
            }
            string rejectSummary=rejected.Count==0?"(none)":string.Join(", ",rejected.OrderByDescending(kv=>kv.Value).Select(kv=>$"{kv.Key}={kv.Value}"));
            string line=$"{sheetName}\taccepted={accepted}\trejected={rejected.Values.Sum()}\treasons={rejectSummary}";
            lines.Add(line);
            TestContext.WriteLine(line);
            Assert.IsTrue(accepted+rejected.Values.Sum()==dataRows.Count||accepted==dataRows.Count,
                sheetName+": accepted+rejected must cover data rows (or full preview accepted all)");
        }
        Directory.CreateDirectory("/home/ghazal/.cache/agents-runs");
        File.WriteAllText("/home/ghazal/.cache/agents-runs/cursor-KOPS1b-preview-e2e.txt",string.Join('\n',lines)+"\n");
    }

    static string TryPreviewOneRow(Imports.Sheet sheet,int excelRow,string register,string company,string period,string accountKey,string riderRaw,decimal orders)
    {
        try
        {
            using var f=new PayFixture();
            string invoiceId="e2e-one-"+register+"-"+excelRow;
            string accountId="e2e-one-acc-"+excelRow;
            string iqama;
            try{iqama=J.Iqama(riderRaw);}
            catch{return "invalid_iqama";}
            if(string.IsNullOrWhiteSpace(accountKey))return "missing_account_id";
            f.Store.Write((c,tx)=>{
                Store.Save(c,"rider",new(){["id"]=iqama,["iqama"]=iqama,["sponsorId"]=company,["isDeliveryRider"]=true},tx);
                Store.Save(c,"platform_account",new(){["id"]=accountId,["externalAccountId"]=accountKey,["code"]="E2E-ONE-"+excelRow,["companyId"]=company,["platformCode"]="keeta",["registeredRiderId"]="2000000099",["assignments"]=new JsonArray(new JsonObject{["riderId"]=iqama,["RawEffectiveFrom"]="2026-09-01T00:00:00+03:00",["RawEffectiveTo"]="2026-11-01T00:00:00+03:00"})},tx);
                decimal baseGross=Math.Max(orders,1);
                Store.Save(c,"platform_invoice",new(){["id"]=invoiceId,["companyId"]=company,["platform"]=PayPlatforms.For("keeta").Platform,["period"]=period,["approvalStatus"]="approved",["status"]="final",["amountExclVat"]=baseGross,["vat"]=0,["deductionsTotal"]=0},tx);
                Store.Save(c,"platform_invoice_line",new(){["id"]="e2e-one-line-"+excelRow,["invoiceId"]=invoiceId,["platformAccountId"]=accountId,["baseGross"]=baseGross,["incentives"]=0,["orders"]=orders,["deductionAmount"]=0,["deductions"]=new JsonArray()},tx);
                return true;
            });
            f.Approve("pay_terms","keeta",f.Draft("pay_terms","keeta",PayFixture.Terms()));
            var values=sheet.Rows[excelRow-1];
            var rowObj=new object?[KeetaOperationsImport.Headers.Length];
            for(int i=0;i<KeetaOperationsImport.Headers.Length;i++)
            {
                string raw=i<values.Count?values[i]:"";
                // Identity / iqama columns: keep digit text so ExcelFiles writes inlineStr, not a lossy number.
                if(KeetaOperationsImport.Columns[i].Identity||KeetaOperationsImport.Headers[i] is "رقم بطاقة الهوية" or "رقم الهوية1")
                    rowObj[i]=string.Concat(J.Digits(raw).Where(char.IsDigit));
                else if(KeetaOperationsImport.Columns[i].Numeric&&decimal.TryParse(J.Digits(raw),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var n))
                    rowObj[i]=n;
                else rowObj[i]=raw;
            }
            // ExcelFiles.Write already emits the header row; pass data rows only.
            var one=ExcelFiles.Write(KeetaOperationsImport.Columns,new List<object?[]>{rowObj});
            f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,"keeta",new(){["invoiceId"]=invoiceId,["register"]=register},one));
            return "accepted";
        }
        catch(ApiError ex){return ReasonCode(ex.Message);}
        catch(Exception){return "unexpected_error";}
    }

    /// <summary>Map Arabic validation text to a stable reason code; strip row numbers and never echo personal values.</summary>
    static string ReasonCode(string message)
    {
        string m=System.Text.RegularExpressions.Regex.Replace(message,@"الصف\s+\d+\s*:?\s*","");
        m=System.Text.RegularExpressions.Regex.Replace(m,@"[0-9]{5,}","#");
        if(m.Contains("تتقاطع",StringComparison.Ordinal))return "date_no_month_overlap";
        if(m.Contains("تاريخ",StringComparison.Ordinal)&&m.Contains("شهر",StringComparison.Ordinal))return "date_outside_month";
        if(m.Contains("تكرار نفس المندوب",StringComparison.Ordinal))return "duplicate_account_rider";
        if(m.Contains("غير موجود في فاتورة",StringComparison.Ordinal))return "account_not_on_invoice";
        if(m.Contains("يوزر المنصة غير موجود",StringComparison.Ordinal))return "platform_account_missing";
        if(m.Contains("المندوب غير موجود",StringComparison.Ordinal))return "rider_missing";
        if(m.Contains("ليست لمندوب",StringComparison.Ordinal)||m.Contains("خارج إطار المناديب",StringComparison.Ordinal))return "not_delivery_rider";
        if(m.Contains("تكليف",StringComparison.Ordinal)||m.Contains("المشغل الفعلي",StringComparison.Ordinal))return "assignment_mismatch";
        if(m.Contains("مفصول بلا أداء",StringComparison.Ordinal))return "dismissed_without_performance";
        if(m.Contains("الصلاحية المتوقعة",StringComparison.Ordinal))return "expected_eligibility_required";
        if(m.Contains("فئة الكفالة",StringComparison.Ordinal)||m.Contains("تعذر استنتاج فئة",StringComparison.Ordinal))return "category_unresolved";
        if(m.Contains("مجموع طلبات",StringComparison.Ordinal)||m.Contains("لا يطابق",StringComparison.Ordinal)&&m.Contains("طلبات",StringComparison.Ordinal))return "orders_mismatch_invoice";
        if(m.Contains("الأيام الصالحة تتجاوز",StringComparison.Ordinal))return "days_exceed_month";
        if(m.Contains("10 أرقام",StringComparison.Ordinal)||m.Contains("الإقامة/الهوية",StringComparison.Ordinal))return "iqama_invalid";
        if(m.Contains("الحالة",StringComparison.Ordinal))return "status_invalid";
        if(m.Contains("رقم الهوية",StringComparison.Ordinal))return "iqama_required";
        if(m.Contains("المعرف مطلوب",StringComparison.Ordinal))return "account_id_required";
        if(m.Contains("قيمة رقمية",StringComparison.Ordinal))return "numeric_invalid";
        if(m.Contains("مقاييس وأيام وفئة",StringComparison.Ordinal))return "month_wide_metrics_conflict";
        if(m.Contains("استحقاقات اليوزرات لا يطابق",StringComparison.Ordinal))return "invoice_gross_mismatch";
        return "other_validation";
    }

    public TestContext TestContext { get; set; } = null!;
}
