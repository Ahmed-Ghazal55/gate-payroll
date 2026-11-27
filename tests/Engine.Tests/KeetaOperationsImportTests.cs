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

    public TestContext TestContext { get; set; } = null!;
}
