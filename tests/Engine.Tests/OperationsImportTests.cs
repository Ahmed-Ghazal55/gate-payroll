using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class OperationsImportTests
{
    internal const string Rider="2000000091",AccountId="synthetic-ops-account",InvoiceId="synthetic-ops-invoice";
    internal static string Company=>FinancialCore.Sponsors[0].Id;
    internal static void Seed(Store store,string app,bool deductions=true)
    {
        store.Write((c,tx)=>{
            Store.Save(c,"rider",new(){["id"]=Rider,["iqama"]=Rider,["sponsorId"]=Company,["isDeliveryRider"]=true},tx);
            Store.Save(c,"platform_account",new(){["id"]=AccountId,["companyId"]=Company,["platformCode"]=app,["registeredRiderId"]="2000000092",["assignments"]=new JsonArray(new JsonObject{["riderId"]=Rider,["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00",["RawEffectiveTo"]="2026-11-01T00:00:00+03:00"})},tx);
            Store.Save(c,"platform_invoice",new(){["id"]=InvoiceId,["companyId"]=Company,["platform"]=PayPlatforms.For(app).Platform,["period"]="2026-10",["approvalStatus"]="approved",["status"]="final",["amountExclVat"]=110,["vat"]=16.5m,["deductionsTotal"]=deductions?8:0},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]="synthetic-ops-line",["invoiceId"]=InvoiceId,["platformAccountId"]=AccountId,["baseGross"]=100,["incentives"]=10,["orders"]=5,["deductionAmount"]=deductions?8:0,["deductions"]=deductions?new JsonArray(new JsonObject{["category"]="penalty",["amount"]=3,["level"]="account_level"},new JsonObject{["category"]="company_shortfall",["amount"]=5,["level"]="company_level",["expenseAccount"]="512101"}):new JsonArray()},tx);return true;
        });
    }
    internal static byte[] Workbook(string app,Action<Dictionary<string,object?>>? edit=null,bool twice=false)
    {
        var step=ImportCatalog.Steps().Single(s=>s.Key==(app=="hs"?"hungerstation":app)+"_operations");
        var row=step.Columns.ToDictionary(c=>c.Header,c=>(object?)"");row["period (YYYY-MM)"]="2026-10";row["platformAccountId"]=AccountId;row["iqama"]=Rider;row["days"]=1;row["orders"]=5;row["baseGross"]=100;row["deduction"]=3;row["category"]="sponsored_private";
        row[step.Columns.Last(c=>c.Numeric).Header]=2;edit?.Invoke(row);object?[] cells=step.Columns.Select(c=>row[c.Header]).ToArray();
        return ExcelFiles.Write(step.Columns,twice?[cells,cells]:[cells]);
    }
    static JsonObject Input()=>new(){["invoiceId"]=InvoiceId};
    static JsonObject Confirm(JsonObject preview)=>new(){["invoiceId"]=InvoiceId,["confirm"]=true,["rows"]=preview["rows"]!.DeepClone(),["fingerprint"]=preview.S("fingerprint")};
    [TestMethod][DataRow("ninja")][DataRow("keeta")][DataRow("hs")]
    public void PerAppReviewFeedsActualCycleRiderStageWithoutCreatingDebtOrJournal(string app)
    {
        using var f=new PayFixture();Seed(f.Store,app);f.Approve("pay_terms",app,f.Draft("pay_terms",app,PayFixture.Terms()));
        var bytes=Workbook(app);var preview=f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,app,Input(),bytes));var review=preview["rows"]![0]!.AsObject();
        Assert.AreEqual(100m,review["accounts"]![0]!.N("baseGross"));Assert.AreEqual(10m,review["accounts"]![0]!.N("incentives"));
        Assert.AreEqual(3m,review["accounts"]![0]!["riders"]![0]!.N("deduction"));Assert.AreEqual(2,review["deductions"]!.AsArray().Count);
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,OperationsImports.Kind).Count),"preview never writes");
        var saved=f.Store.Write((c,tx)=>OperationsImports.Commit(c,tx,PayFixture.Maker,app,Confirm(preview),bytes));
        Assert.IsTrue(JsonNode.DeepEquals(review,saved["review"]));
        var cycle=f.Store.Write((c,tx)=>PayCycles.Create(c,tx,PayFixture.Maker,new(){["companyId"]=Company,["period"]="2026-10",["invoices"]=new JsonArray(saved["review"]!.DeepClone())}));
        for(int stage=1;stage<=3;stage++)f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,PayFixture.Maker,cycle.S("id"),new(){["stage"]=stage}));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count));
        Assert.AreEqual("ready",ImportCatalog.Steps().Single(s=>s.Key==(app=="hs"?"hungerstation":app)+"_operations").Status);
    }
    [TestMethod]
    public void MismatchesUnknownRidersOwnerOnlyDatesAndUnreviewedNativeHsFailClosed()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store,"hs");
        foreach(var change in new Action<Dictionary<string,object?>>[]{r=>r["distanceKm"]=0.12345m,r=>r["orders"]=4,r=>r["baseGross"]=99,r=>r["deduction"]=8,r=>r["iqama"]="2000000092",r=>r["category"]="bike",r=>r["days"]=32,r=>r["from (YYYY-MM-DD)"]="2026-11-01",r=>{r["from (YYYY-MM-DD)"]="2026-10-01";r["to (YYYY-MM-DD)"]="2026-11-01";}})
            Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>OperationsImports.Parse(c,null,PayFixture.Maker,"hs",Input(),Workbook("hs",change))));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>OperationsImports.Parse(c,null,PayFixture.Maker,"hs",Input(),Workbook("hs",twice:true))));
        var missing=DebtSheetReviewTests.Book([("Synthetic",[["period (YYYY-MM)","platformAccountId","iqama","days","orders","baseGross","deduction","category"],["2026-10",AccountId,Rider,"1","5","=NOCACHE","3","sponsored_private"]])]);
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>OperationsImports.Parse(c,null,PayFixture.Maker,"hs",Input(),missing)));
        f.Store.Write((c,tx)=>{var line=Store.Get(c,"synthetic-ops-line","platform_invoice_line",tx)!;line.Remove("baseGross");line["riderNet"]=100;Store.Save(c,"platform_invoice_line",line,tx);return true;});
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>OperationsImports.Parse(c,null,PayFixture.Maker,"hs",Input(),Workbook("hs"))),"never replace platform gross with rider net");
    }
    [TestMethod]
    public void SignedConfirmationBindsActorBytesRowsInvoiceAssignmentsAndScope()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store,"ninja");var bytes=Workbook("ninja");var preview=f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,"ninja",Input(),bytes));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OperationsImports.Commit(c,tx,PayFixture.Checker,"ninja",Confirm(preview),bytes)));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OperationsImports.Commit(c,tx,PayFixture.Maker,"ninja",Confirm(preview),Workbook("ninja",r=>r["riderName"]="SYNTHETIC_CHANGED"))));
        var forged=Confirm(preview);forged["rows"]![0]!["accounts"]![0]!["baseGross"]=1;Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OperationsImports.Commit(c,tx,PayFixture.Maker,"ninja",forged,bytes)));
        f.Store.Write((c,tx)=>{var a=Store.Get(c,AccountId,"platform_account",tx)!;a["assignments"]=new JsonArray();Store.Save(c,"platform_account",a,tx);return true;});
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OperationsImports.Commit(c,tx,PayFixture.Maker,"ninja",Confirm(preview),bytes)));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,"ninja",Input(),bytes)),"registered holder cannot replace assignment");
        var scoped=UserAccess.Principal(new JsonObject{["username"]="synthetic-scoped",["role"]="FinancialAccountant",["customPermissions"]=new JsonArray("pay.manage"),["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(FinancialCore.Sponsors[1].Id),["platforms"]=new JsonArray("Ninja")}});
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>OperationsImports.Preview(c,scoped,"ninja",Input(),bytes)));
    }
}
