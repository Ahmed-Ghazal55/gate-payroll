using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class NinjaInvoiceImportTests
{
    static System.Security.Claims.ClaimsPrincipal Actor(string name="synthetic-maker")=>HungerStationInvoiceTests.Actor(name);
    static string Company=>FinancialCore.Sponsors[0].Id;
    sealed class Fixture:IDisposable
    {
        readonly HungerStationInvoiceTests.Fixture inner=new();
        public Store Store=>inner.Store;
        public Fixture()=>Store.Write((c,tx)=>{Store.Save(c,"platform_account",new(){["id"]="synthetic-ninja-platform",["platformCode"]="ninja",["companyId"]=Company},tx);Store.Save(c,"ninja_account",new(){["id"]="synthetic-ninja-account",["platformAccountId"]="synthetic-ninja-platform",["CaptainId"]="SYN-A"},tx);return true;});
        public JsonObject Input()=>new(){["period"]="2026-10",["companyId"]=Company,["reference"]="SYNTHETIC-NINJA-INVOICE"};
        public JsonObject Preview(byte[] bytes)=>Store.Read(c=>NinjaInvoices.Preview(c,Actor(),Input(),bytes));
        public JsonObject Commit(byte[] bytes,JsonObject? p=null,string actor="synthetic-maker")
        {p??=Preview(bytes);var i=Input();i["rows"]=p["rows"]!.DeepClone();i["fingerprint"]=p.S("fingerprint");i["confirm"]=true;return Store.Write((c,tx)=>NinjaInvoices.Commit(c,tx,Actor(actor),i,bytes));}
        public void Dispose()=>inner.Dispose();
    }
    public static byte[] Workbook(bool editedBike=false,decimal penalty=0,decimal blocked=0,bool badNumeric=false)
    {
        var header=NinjaBillingTests.Header.Concat(Enumerable.Range(1,7).Select(n=>"Synthetic optional "+n)).ToList();Assert.AreEqual(43,header.Count);
        var row=NinjaBillingTests.Row("SYN-A","capability_car",31,460,31);row.AddRange(Enumerable.Repeat("0",7));
        void Set(string key,object value)=>row[header.IndexOf(key)]=Convert.ToString(value,CultureInfo.InvariantCulture)!;
        if(editedBike)Set("capabilty","BIKE");
        decimal final=6500-penalty-blocked;Set("Penalty",-penalty);Set("NAQIL BLOCKED",-blocked);Set("Final Amount",final);Set("VAT",final*0.15m);Set("TOTAL COST",final*1.15m);
        if(badNumeric)Set("Penalty","not-a-number");
        using var memory=new MemoryStream();using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))
        {
            void Entry(string name,string xml){using var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(xml);}
            XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main",rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            Entry("xl/workbook.xml",new XElement(ns+"workbook",new XAttribute(XNamespace.Xmlns+"r",rel),new XElement(ns+"sheets",new XElement(ns+"sheet",new XAttribute("name","Synthetic"),new XAttribute(rel+"id","r1")))).ToString());
            Entry("xl/_rels/workbook.xml.rels","<Relationships><Relationship Id=\"r1\" Target=\"worksheets/sheet1.xml\" /></Relationships>");
            string Col(int index){string result="";for(int n=index+1;n>0;n=(n-1)/26)result=(char)('A'+(n-1)%26)+result;return result;}
            XElement Cells(List<string> values,int number)=>new(ns+"row",new XAttribute("r",number),values.Select((v,n)=>new XElement(ns+"c",new XAttribute("r",Col(n)+number),new XAttribute("t","inlineStr"),new XElement(ns+"is",new XElement(ns+"t",v)))));
            Entry("xl/worksheets/sheet1.xml",new XElement(ns+"worksheet",new XElement(ns+"sheetData",Cells(header,1),Cells(row,2))).ToString());
        }
        return memory.ToArray();
    }
    [TestMethod]
    public void Signed43ColumnInvoiceStoresInvoicedEntitlementAndRejectsDuplicatesOrTampering()
    {
        using var f=new Fixture();byte[] bytes=Workbook(true);var preview=f.Preview(bytes);var forged=preview.Clone();forged["rows"]![0]!["lines"]![0]!["invoiced"]=1;
        Assert.ThrowsExactly<ApiError>(()=>f.Commit(bytes,forged));Assert.ThrowsExactly<ApiError>(()=>f.Commit(bytes,preview,"synthetic-other"));
        var invoice=f.Commit(bytes,preview);var line=f.Store.Read(c=>NinjaInvoices.Lines(c,null,invoice.S("id")).Single());
        Assert.AreEqual(6500m,line.N("invoiced"));Assert.AreNotEqual(line.N("expected"),line.N("invoiced"));Assert.AreEqual("vehicle type edited by the platform",line.S("reason"));Assert.AreEqual("invoiced",line.S("status"));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"platform_dispute").Count));Assert.ThrowsExactly<ApiError>(()=>f.Preview(bytes));Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"platform_invoice").Count));
    }
    [TestMethod]
    public void DifferenceNeverBlocksIndependentApprovalOrGatedPostingAndVatIsBalanced()
    {
        using var f=new Fixture();var invoice=f.Commit(Workbook(true));string id=invoice.S("id");Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>NinjaInvoices.Approve(c,tx,Actor(),id)));
        var final=f.Store.Write((c,tx)=>NinjaInvoices.Approve(c,tx,Actor("synthetic-checker"),id));Assert.AreEqual("final",final.S("status"));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>NinjaInvoices.Post(c,tx,Actor("synthetic-checker"),id)));
        f.Store.Write((c,tx)=>PlatformPosting.Set(c,tx,Actor(),"ninja",new(){["enabled"]=true}));
        var posted=f.Store.Write((c,tx)=>NinjaInvoices.Post(c,tx,Actor("synthetic-checker"),id));var journal=f.Store.Read(c=>Store.Get(c,posted.S("journalEntryId"),"journal"))!;
        Assert.AreEqual(7475m,journal.N("totalDebit"));Assert.AreEqual(journal.N("totalDebit"),journal.N("totalCredit"));var lines=journal["lines"]!.AsArray().OfType<JsonObject>().ToList();Assert.AreEqual(6500m,lines.Single(l=>l.S("accountCode")=="411104").N("credit"));Assert.AreEqual(975m,lines.Single(l=>l.S("accountCode")=="213201").N("credit"));
        f.Store.Write((c,tx)=>NinjaInvoices.Post(c,tx,Actor("synthetic-checker"),id));Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"journal").Count));
    }
    [TestMethod]
    public void ClassificationsAreIndependentAndFormalDisputesAreNotEngineDifferences()
    {
        using var f=new Fixture();var invoice=f.Commit(Workbook(penalty:10,blocked:20));string id=invoice.S("id");var line=f.Store.Read(c=>NinjaInvoices.Lines(c,null,id).Single());
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>NinjaInvoices.Approve(c,tx,Actor("synthetic-checker"),id)));
        foreach(string category in new[]{"penalty","naqilBlocked"})f.Store.Write((c,tx)=>NinjaInvoices.Classify(c,tx,Actor(),id,new(){["lineId"]=line.S("id"),["category"]=category,["level"]="company_level",["expenseAccount"]="526402"}));
        f.Store.Write((c,tx)=>NinjaInvoices.Approve(c,tx,Actor("synthetic-checker"),id));Assert.AreEqual(2,f.Store.Read(c=>Store.List(c,PlatformDeductions.Kind).Count));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));
        var d=f.Store.Write((c,tx)=>NinjaInvoices.Dispute(c,tx,Actor(),id,new(){["reason"]="Synthetic formal dispute"}));f.Store.Write((c,tx)=>PlatformPosting.Set(c,tx,Actor(),"ninja",new(){["enabled"]=true}));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>NinjaInvoices.Post(c,tx,Actor("synthetic-checker"),id)));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>NinjaInvoices.Dispute(c,tx,Actor(),id,new(){["disputeId"]=d.S("id"),["reason"]="Synthetic resolution"},true)));
        f.Store.Write((c,tx)=>NinjaInvoices.Dispute(c,tx,Actor("synthetic-checker"),id,new(){["disputeId"]=d.S("id"),["reason"]="Synthetic resolution"},true));f.Store.Write((c,tx)=>NinjaInvoices.Post(c,tx,Actor("synthetic-checker"),id));
        Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"journal").Count));
    }
    [TestMethod]
    public void ChangedAccountScopeAndMalformedNumericFailClosed()
    {
        using var f=new Fixture();Assert.ThrowsExactly<ApiError>(()=>f.Preview(Workbook(badNumeric:true)));byte[] bytes=Workbook();var p=f.Preview(bytes);
        f.Store.Write((c,tx)=>{var account=Store.Get(c,"synthetic-ninja-platform","platform_account",tx)!;account["companyId"]=FinancialCore.Sponsors[1].Id;Store.Save(c,"platform_account",account,tx);return true;});Assert.ThrowsExactly<ApiError>(()=>f.Commit(bytes,p));Assert.ThrowsExactly<ApiError>(()=>f.Preview(bytes));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"platform_invoice").Count));
    }
    [TestMethod]
    public void ClosedPeriodRefusesInvoiceApprovalWithoutChangingImportedFacts()
    {
        using var f=new Fixture();var invoice=f.Commit(Workbook());string id=invoice.S("id");
        f.Store.Write((c,tx)=>{Store.Save(c,"accounting_period",new(){["id"]="synthetic-period",["companyId"]=Company,["period"]="2026-10",["status"]="closed"},tx);return true;});
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>NinjaInvoices.Approve(c,tx,Actor("synthetic-checker"),id)));Assert.AreEqual("invoiced",f.Store.Read(c=>Store.Get(c,id,"platform_invoice"))!.S("status"));
    }
    [TestMethod]
    public void NinjaDeductionSuppressionNeverBleedsIntoHungerStationOrUntaggedDebts()
    {
        var tags=new[]{new JsonObject{["id"]="synthetic-tag",["platform"]="Ninja",["status"]="confirmed",["platformDeducted"]=new JsonObject{["rider"]="synthetic-rider",["month"]="2026-10",["category"]="penalty"}}};
        var categories=PlatformDeductions.Categories(tags,"synthetic-rider","2026-10");Assert.IsTrue(PlatformDeductions.Skip(categories,new(){["platform"]="Ninja",["category"]="penalty"}));Assert.IsFalse(PlatformDeductions.Skip(categories,new(){["platform"]="HungerStation",["category"]="penalty"}));Assert.IsFalse(PlatformDeductions.Skip(categories,new(){["category"]="penalty"}));
    }
}
