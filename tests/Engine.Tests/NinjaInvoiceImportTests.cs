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
        return WorkbookRows(header,row);
    }
    [TestMethod]
    public void DiscardRetainsRowsAllowsSameReferenceAndCannotApproveDiscardedInvoice()
    {
        using var f=new Fixture();var bytes=Workbook();var first=f.Commit(bytes);string id=first.S("id");
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>InvoiceDiscard.Discard(c,tx,new System.Security.Claims.ClaimsPrincipal(),"ninja",id,new(){["reason"]="No permission"})));
        Assert.IsTrue(f.Store.Read(c=>NinjaInvoices.View(c,null,Actor(),first))["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="discard"));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>InvoiceDiscard.Discard(c,tx,Actor(),"ninja",id,new(){["reason"]=" "})));
        f.Store.Write((c,tx)=>InvoiceDiscard.Discard(c,tx,Actor(),"ninja",id,new(){["reason"]="Replace incorrect supplier rows"}));
        Assert.AreEqual(1,f.Store.Read(c=>NinjaInvoices.Lines(c,null,id).Count));
        Assert.AreEqual(0,f.Store.Read(c=>NinjaInvoices.View(c,null,Actor(),NinjaInvoices.Get(c,null,Actor(),id)))["allowedActions"]!.AsArray().Count);
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>NinjaInvoices.Approve(c,tx,Actor("checker"),id)));
        var replacement=f.Commit(bytes);Assert.AreNotEqual(id,replacement.S("id"));Assert.AreEqual(first.S("reference"),replacement.S("reference"));
        Assert.AreEqual(2,f.Store.Read(c=>Store.List(c,"platform_invoice_line").Count));
        f.Store.Write((c,tx)=>NinjaInvoices.Approve(c,tx,Actor("checker"),replacement.S("id")));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>InvoiceDiscard.Discard(c,tx,Actor(),"ninja",replacement.S("id"),new(){["reason"]="Approved invoice cannot discard"})));
        Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"audit").Count(a=>a.S("action")=="platform.ninja.invoice_discarded")));
    }
    public static byte[] WorkbookRows(List<string> header,params List<string>[] dataRows)
    {
        using var memory=new MemoryStream();using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))
        {
            void Entry(string name,string xml){using var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(xml);}
            XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main",rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            Entry("xl/workbook.xml",new XElement(ns+"workbook",new XAttribute(XNamespace.Xmlns+"r",rel),new XElement(ns+"sheets",new XElement(ns+"sheet",new XAttribute("name","Synthetic"),new XAttribute(rel+"id","r1")))).ToString());
            Entry("xl/_rels/workbook.xml.rels","<Relationships><Relationship Id=\"r1\" Target=\"worksheets/sheet1.xml\" /></Relationships>");
            string Col(int index){string result="";for(int n=index+1;n>0;n=(n-1)/26)result=(char)('A'+(n-1)%26)+result;return result;}
            XElement Cells(List<string> values,int number)=>new(ns+"row",new XAttribute("r",number),values.Select((v,n)=>new XElement(ns+"c",new XAttribute("r",Col(n)+number),new XAttribute("t","inlineStr"),new XElement(ns+"is",new XElement(ns+"t",v)))));
            Entry("xl/worksheets/sheet1.xml",new XElement(ns+"worksheet",new XElement(ns+"sheetData",new[]{Cells(header,1)}.Concat(dataRows.Select((r,i)=>Cells(r,i+2))))).ToString());
        }
        return memory.ToArray();
    }
    [TestMethod]
    public void LiveShapedAccountMatchesWithoutPullAndApprovalExplainsItself()
    {
        using var f=new Fixture();
        // Live db77738: code NINJA, captain id on the account, no ninja_account rows.
        f.Store.Write((c,tx)=>{using(var d=c.CreateCommand()){d.Transaction=tx;d.CommandText="DELETE FROM records WHERE kind='ninja_account'";d.ExecuteNonQuery();}Store.Save(c,"platform_account",new(){["id"]="synthetic-ninja-platform",["platformCode"]="NINJA",["companyId"]=Company,["externalAccountId"]="SYN-A"},tx);return true;});
        var invoice=f.Commit(Workbook(penalty:300));
        Assert.AreEqual(1,f.Store.Read(c=>NinjaInvoices.Lines(c,null,invoice.S("id")).Count));
        var checker=f.Store.Read(c=>NinjaInvoices.View(c,null,Actor("synthetic-checker"),invoice));
        Assert.IsFalse(checker["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"));
        StringAssert.Contains(string.Join("|",checker["approvalBlockers"]!.AsArray()),"بلا تصنيف");
        var line=checker["lines"]!.AsArray()[0]!.AsObject();
        var batch=new JsonObject{["items"]=new JsonArray(new JsonObject{["lineId"]=line.S("id"),["category"]="penalty",["level"]="account_level"})};
        f.Store.Write((c,tx)=>NinjaInvoices.ClassifyBatch(c,tx,Actor(),invoice.S("id"),batch));
        var maker=f.Store.Read(c=>NinjaInvoices.View(c,null,Actor(),invoice));
        StringAssert.Contains(string.Join("|",maker["approvalBlockers"]!.AsArray()),"مستخدم آخر");Assert.IsFalse(maker["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"));
        checker=f.Store.Read(c=>NinjaInvoices.View(c,null,Actor("synthetic-checker"),invoice));
        Assert.IsTrue(checker["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"));Assert.AreEqual(0,checker["approvalBlockers"]!.AsArray().Count);
    }
    [TestMethod]
    public void NegativeCaptainFromTheInvoiceIsKept()
    {
        using var f=new Fixture();
        var header=NinjaBillingTests.Header.ToList();var row=NinjaBillingTests.Row("SYN-A","capability_car",31,1,1);
        void Set(string key,string value)=>row[header.IndexOf(key)]=value;
        Set("Target Total Amount","10");Set("Attendance Deduction","1.5");Set("8 Hour Deductions","31.45");Set("Final Amount","-22.95");Set("VAT","-3.4425");Set("TOTAL COST","-26.3925");
        var invoice=f.Commit(WorkbookRows(header,row));
        var line=f.Store.Read(c=>NinjaInvoices.Lines(c,null,invoice.S("id"))).Single();
        Assert.AreEqual(-22.95m,line.N("invoiced"));Assert.AreEqual(-22.95m,line.N("baseGross"));
    }
    [TestMethod]
    public void DeductionsRowWithDashesIsACompanyLine()
    {
        using var f=new Fixture();
        var header=NinjaBillingTests.Header.ToList();var captain=NinjaBillingTests.Row("SYN-A","capability_car",31,460,31);
        var adjustment=header.Select(h=>h switch{"captain_id"=>"SYN-A","supplier_id"=>"9001","Target Level Type"=>"DEDUCTIONS ","Final Amount"=>"-33.12","VAT"=>"-4.968","TOTAL COST"=>"-38.088",_=>"-"}).ToList();
        var invoice=f.Commit(WorkbookRows(header,captain,adjustment));
        var lines=f.Store.Read(c=>NinjaInvoices.Lines(c,null,invoice.S("id")));
        Assert.AreEqual(2,lines.Count);Assert.AreEqual("company",lines.Single(l=>l.N("sourceRow")==3).S("lineType"));
    }
    [TestMethod]
    public void UnknownOrDeletedCaptainRejectsWithRowNumbers()
    {
        using var f=new Fixture();
        f.Store.Write((c,tx)=>{using(var d=c.CreateCommand()){d.Transaction=tx;d.CommandText="DELETE FROM records WHERE kind='ninja_account'";d.ExecuteNonQuery();}Store.Save(c,"platform_account",new(){["id"]="synthetic-ninja-platform",["platformCode"]="NINJA",["companyId"]=Company,["externalAccountId"]="SYN-A",["sourceDeleted"]=true},tx);return true;});
        var error=Assert.ThrowsExactly<ApiError>(()=>f.Preview(Workbook()));StringAssert.Contains(error.Message,"rows 2");
    }
    [TestMethod]
    public void PerAccountUploadRejectsOffendingRowsAndReplayIsANoop()
    {
        using var f=new Fixture();byte[] bytes=Workbook();
        f.Store.Write((c,tx)=>{Store.Save(c,"platform_account",new(){["id"]="synthetic-other-account",["companyId"]=Company,["platformCode"]="ninja"},tx);return true;});
        var input=f.Input();input["accountIds"]=new JsonArray("synthetic-other-account");
        var mismatch=Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>NinjaInvoices.Preview(c,Actor(),input,bytes)));StringAssert.Contains(mismatch.Message,"rows 2");
        input["accountIds"]=new JsonArray("synthetic-ninja-platform");
        var preview=f.Store.Read(c=>NinjaInvoices.Preview(c,Actor(),input,bytes));input["rows"]=preview["rows"]!.DeepClone();input["fingerprint"]=preview.S("fingerprint");input["confirm"]=true;
        var invoice=f.Store.Write((c,tx)=>NinjaInvoices.Commit(c,tx,Actor(),input,bytes));
        int audits=f.Store.Read(c=>Store.List(c,"audit").Count);
        var replay=f.Store.Write((c,tx)=>NinjaInvoices.Commit(c,tx,Actor(),input,bytes));
        Assert.AreEqual(invoice.S("id"),replay.S("id"));Assert.AreEqual(audits,f.Store.Read(c=>Store.List(c,"audit").Count));Assert.AreEqual(1,f.Store.Read(c=>NinjaInvoices.Lines(c,null,invoice.S("id")).Count));
        var maker=f.Store.Read(c=>NinjaInvoices.View(c,null,Actor(),invoice));var checker=f.Store.Read(c=>NinjaInvoices.View(c,null,Actor("synthetic-checker"),invoice));
        Assert.IsFalse(maker["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"));Assert.IsTrue(checker["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"));
    }
    [TestMethod]
    public void Signed43ColumnInvoiceStoresInvoicedEntitlementAndRejectsDuplicatesOrTampering()
    {
        using var f=new Fixture();byte[] bytes=Workbook(true);var preview=f.Preview(bytes);var forged=preview.Clone();forged["rows"]![0]!["lines"]![0]!["invoiced"]=1;
        Assert.ThrowsExactly<ApiError>(()=>f.Commit(bytes,forged));Assert.ThrowsExactly<ApiError>(()=>f.Commit(bytes,preview,"synthetic-other"));
        var invoice=f.Commit(bytes,preview);var line=f.Store.Read(c=>NinjaInvoices.Lines(c,null,invoice.S("id")).Single());
        Assert.AreEqual(6500m,line.N("invoiced"));Assert.AreNotEqual(line.N("expected"),line.N("invoiced"));Assert.AreEqual("vehicle type edited by the platform",line.S("reason"));Assert.AreEqual("invoiced",line.S("status"));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"platform_dispute").Count));Assert.AreEqual(invoice.S("id"),f.Commit(bytes).S("id"));Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"platform_invoice").Count));
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
