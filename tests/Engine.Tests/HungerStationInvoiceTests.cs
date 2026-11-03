using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class HungerStationInvoiceTests
{
    public static System.Security.Claims.ClaimsPrincipal Actor(string name="synthetic-maker")=>UserAccess.Principal(new JsonObject{["username"]=name,["role"]="Admin"});
    public static string Rider=>"2"+1.ToString("D9");
    public sealed class Fixture:IDisposable
    {
        public string DirectoryPath {get;}=Path.Combine(Path.GetTempPath(),"gate-hs-invoice-"+Guid.NewGuid().ToString("N"));
        public Store Store {get;}
        public string Cr(int index=0)=>"4"+(index+1).ToString("D9");
        public Fixture()
        {
            Store=new Store(DirectoryPath);Store.Write((c,tx)=>{for(int i=0;i<3;i++)Store.Save(c,"company",new(){["id"]=FinancialCore.Sponsors[i].Id,["code"]=FinancialCore.Sponsors[i].Code,["crNumber"]=Cr(i),["nameAr"]="Synthetic Company"},tx);Store.Save(c,"rider",new(){["id"]=Rider,["iqama"]=Rider,["name"]="Synthetic Rider"},tx);return true;});
        }
        public JsonObject Input(byte[] bytes)=>new(){["period"]="2026-10",["files"]=new JsonArray(new JsonObject{["fileName"]="synthetic.xlsx",["dataBase64"]=Convert.ToBase64String(bytes)})};
        public JsonObject Preview(JsonObject input)=>Store.Read(c=>HungerStationInvoices.Preview(c,Actor(),input));
        public JsonObject Commit(JsonObject input,JsonObject preview,string actor="synthetic-maker")
        {var confirmed=input.Clone();confirmed["rows"]=preview["rows"]!.DeepClone();confirmed["fingerprint"]=preview.S("fingerprint");confirmed["confirm"]=true;return Store.Write((c,tx)=>HungerStationInvoices.Commit(Store,c,tx,Actor(actor),confirmed));}
        public void Dispose(){Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(DirectoryPath,true);}
    }
    public static byte[] Workbook(string cr,decimal car=0,decimal tga=0,decimal riderBasic=1000,decimal incl=1150,decimal balance=0,decimal net=1150,int detailOrders=10,string taggedMonth="October",bool riderTga=false,bool damage=false,decimal carAdjustment=0)
    {
        Dictionary<string,string> Row(params (string Key,object Value)[] values)=>values.ToDictionary(v=>v.Key,v=>Convert.ToString(v.Value,CultureInfo.InvariantCulture)!);
        var company=Row(("Company CR",cr),("Contract Name","Synthetic Contract"),("completed_orders",10),("basic_payment",1000),("COURIER_BASIC_PAYMENT",1000),("Rider Balance",balance),("Amount Excl VAT",1000),("VAT",150),("Amount Incl VAT",incl),("Total Deduction",0),("Net FTR",net),("CAR Deduction October 2026",car),("TGA Deduction October 2026",tga),("Driver Card Support "+taggedMonth+" 2026",0),("Car Adjustment - October 2026",carAdjustment));
        // Different column order is intentional: the adapter must use headers.
        var rider=Row(("basic_payment",riderBasic),("Contract Name","Synthetic Contract"),("rider_id","SYN-ACCOUNT-1"),("completed_orders",10),("COURIER_BASIC_PAYMENT",1000),("Rider Balance",balance),("scheme","salary"));if(riderTga)rider["TGA Deduction October 2026"]=tga.ToString(CultureInfo.InvariantCulture);
        var detail=Row(("rider_id","SYN-ACCOUNT-1"),("completed_orders",detailOrders),("working_hours",100));
        var sheets=new List<(string Name,Dictionary<string,string> Row)>{("Co LVL",company),("Rider LVL",rider),("Breakdown",detail)};
        if(damage)sheets.Add(("Car Damage",Row(("rider_id","SYN-ACCOUNT-1"),("Amount Excl VAT",25),("VAT",3.75m),("Amount Incl VAT",28.75m),("loading_period","2026-09"),("invoice_date","2026-10-05"))));
        using var memory=new MemoryStream();
        using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))
        {
            void Entry(string name,string xml){using var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(xml);}
            XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main",rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            Entry("xl/workbook.xml",new XElement(ns+"workbook",new XAttribute(XNamespace.Xmlns+"r",rel),new XElement(ns+"sheets",sheets.Select((s,i)=>new XElement(ns+"sheet",new XAttribute("name",s.Name),new XAttribute(rel+"id","rId"+i))))).ToString());
            Entry("xl/_rels/workbook.xml.rels",new XElement("Relationships",sheets.Select((s,i)=>new XElement("Relationship",new XAttribute("Id","rId"+i),new XAttribute("Target","worksheets/sheet"+i+".xml")))).ToString());
            for(int i=0;i<sheets.Count;i++)
            {
                string Column(int index){string value="";for(int n=index+1;n>0;n=(n-1)/26)value=(char)('A'+(n-1)%26)+value;return value;}
                XElement Cells(IEnumerable<string> values,int row)=>new(ns+"row",new XAttribute("r",row),values.Select((v,n)=>new XElement(ns+"c",new XAttribute("r",Column(n)+row),new XAttribute("t","inlineStr"),new XElement(ns+"is",new XElement(ns+"t",v)))));
                Entry("xl/worksheets/sheet"+i+".xml",new XElement(ns+"worksheet",new XElement(ns+"sheetData",Cells(sheets[i].Row.Keys,1),Cells(sheets[i].Row.Values,2))).ToString());
            }
        }
        return memory.ToArray();
    }
    [TestMethod]
    public void WorkbookHeadersAndFlagsAreIndependentOfColumnPositions()
    {
        using var f=new Fixture();var preview=f.Preview(f.Input(Workbook(f.Cr(),balance:10,net:1111,carAdjustment:25)));
        Assert.IsTrue(preview.B("canCommit"));Assert.AreEqual(0,preview["errors"]!.AsArray().Count);Assert.AreEqual(2m,preview["counts"].N("flags"));
        var rows=preview["rows"]![0]!["lines"]!.AsArray().OfType<JsonObject>().ToArray();Assert.AreEqual(2,rows.Length);Assert.AreEqual(1000m,rows.Single(r=>r.S("lineType")=="rider").N("basicPayment"));Assert.AreEqual(25m,rows.Single(r=>r.S("lineType")=="company").N("carAdjustment"));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"platform_invoice").Count));
    }
    [TestMethod]
    public void VatComponentOrdersAndTaggedMonthsBlockTheWholeCommit()
    {
        using var f=new Fixture();foreach(byte[] bytes in new[]{Workbook(f.Cr(),incl:1151),Workbook(f.Cr(),riderBasic:999),Workbook(f.Cr(),detailOrders:11),Workbook(f.Cr(),taggedMonth:"September")})
        {var input=f.Input(bytes);var preview=f.Preview(input);Assert.IsFalse(preview.B("canCommit"));Assert.AreEqual("",preview.S("fingerprint"));Assert.ThrowsExactly<ApiError>(()=>f.Commit(input,preview));}
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"platform_invoice").Count));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count));
    }
    [TestMethod]
    public void SignedImportKeepsEvidenceAndIsAuditedAndIdempotent()
    {
        using var f=new Fixture();byte[] bytes=Workbook(f.Cr());var input=f.Input(bytes);var preview=f.Preview(input);
        var tampered=preview.Clone();tampered["rows"]![0]!["amountInclVat"]=999;Assert.ThrowsExactly<ApiError>(()=>f.Commit(input,tampered));Assert.ThrowsExactly<ApiError>(()=>f.Commit(input,preview,"different-maker"));
        var imported=f.Commit(input,preview);Assert.AreEqual(1m,imported.N("importedInvoices"));int audits=f.Store.Read(c=>Store.List(c,"audit").Count);
        var repeat=f.Preview(input);Assert.IsTrue(repeat.B("canCommit"));Assert.AreEqual(1m,f.Commit(input,repeat).N("unchangedInvoices"));Assert.AreEqual(audits,f.Store.Read(c=>Store.List(c,"audit").Count));
        string evidence=Directory.GetFiles(Path.Combine(f.DirectoryPath,"platform-imports/hs")).Single();CollectionAssert.AreEqual(bytes,File.ReadAllBytes(evidence));if(!OperatingSystem.IsWindows())Assert.AreEqual(UnixFileMode.UserRead|UnixFileMode.UserWrite,File.GetUnixFileMode(evidence));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));
    }
    [TestMethod]
    public void CarDisputeAndTgaSplitRequireExplicitIndependentDecisions()
    {
        using var f=new Fixture();var input=f.Input(Workbook(f.Cr(),car:50,tga:100));f.Commit(input,f.Preview(input));
        Assert.AreEqual("disputed",f.Store.Read(c=>Store.List(c,"platform_invoice").Single().S("status")));Assert.AreEqual(50m,f.Store.Read(c=>Store.List(c,"platform_dispute").Single().N("amount")));Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"sys_notification").Count));
        string line=f.Store.Read(c=>Store.List(c,"platform_invoice_line").Single(l=>l.N("tgaAmount")!=0).S("id"));Assert.AreEqual("pending_allocation",f.Store.Read(c=>Store.Get(c,line,"platform_invoice_line")!.S("status")));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>HungerStationInvoices.Split(c,tx,Actor(),line,new())));
        var allocation=f.Store.Write((c,tx)=>HungerStationInvoices.Split(c,tx,Actor(),line,new(){["riderPercent"]=30,["riderId"]=Rider}));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>HungerStationInvoices.ApproveAllocation(c,tx,Actor(),allocation.S("id"))));
        var approved=f.Store.Write((c,tx)=>HungerStationInvoices.ApproveAllocation(c,tx,Actor("synthetic-checker"),allocation.S("id")));Assert.AreEqual(70m,approved.N("companyAmount"));Assert.AreEqual(30m,approved["riderShares"]![0].N("amount"));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>HungerStationInvoices.Split(c,tx,Actor(),line,new(){["riderPercent"]=0})));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));
    }
    [TestMethod]
    public void CompanyAndRiderTgaTotalsDoNotCreateTwoAllocations()
    {
        using var f=new Fixture();var input=f.Input(Workbook(f.Cr(),tga:100,riderTga:true));f.Commit(input,f.Preview(input));
        Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"platform_invoice_line").Count(l=>l.N("tgaAmount")!=0)));Assert.AreEqual("rider",f.Store.Read(c=>Store.List(c,"platform_invoice_line").Single(l=>l.N("tgaAmount")!=0).S("lineType")));
    }
    [TestMethod]
    public void ThreeCompanyBooksStaySeparateAndCarDamageIsNotCarFinance()
    {
        using var f=new Fixture();var input=f.Input(Workbook(f.Cr(),damage:true));for(int i=1;i<3;i++)input["files"]!.AsArray().Add(f.Input(Workbook(f.Cr(i)))["files"]![0]!.DeepClone());
        var preview=f.Preview(input);Assert.IsTrue(preview.B("canCommit"));Assert.AreEqual(3m,f.Commit(input,preview).N("importedInvoices"));
        Assert.AreEqual(3,f.Store.Read(c=>Store.List(c,"platform_invoice").Select(i=>i.S("companyId")).Distinct().Count()));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"platform_dispute").Count));
        Assert.AreEqual("2026-09",f.Store.Read(c=>Store.List(c,"platform_invoice_line").Single(l=>l.S("lineType")=="car_damage").S("loadingPeriod")));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));
    }
    [TestMethod]
    public void ImportRollbackRetainsPrivateEvidenceButNoFinancialDocuments()
    {
        using var f=new Fixture();var input=f.Input(Workbook(f.Cr()));var preview=f.Preview(input);var confirmed=input.Clone();confirmed["rows"]=preview["rows"]!.DeepClone();confirmed["fingerprint"]=preview.S("fingerprint");confirmed["confirm"]=true;
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write<int>((c,tx)=>{HungerStationInvoices.Commit(f.Store,c,tx,Actor(),confirmed);throw new ApiError("Synthetic rollback");}));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"platform_invoice").Count));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"audit").Count));Assert.AreEqual(1,Directory.GetFiles(Path.Combine(f.DirectoryPath,"platform-imports/hs")).Length);
        var changed=input.Clone();changed["files"]![0]!["dataBase64"]=Convert.ToBase64String(Workbook(f.Cr(),car:1));Assert.ThrowsExactly<ApiError>(()=>f.Commit(changed,preview));
    }
    [TestMethod]
    public void AccountOwnershipDoesNotReplaceHistoricalOperatorProof()
    {
        using var f=new Fixture();var input=f.Input(Workbook(f.Cr()));string owner="2"+2.ToString("D9");
        f.Store.Write((c,tx)=>{Store.Save(c,"platform_account",new(){["id"]="synthetic-hs-account",["externalAccountId"]="SYN-ACCOUNT-1",["platformCode"]="HS",["registeredRiderId"]=owner,["assignments"]=new JsonArray()},tx);return true;});
        string Mapped()=>f.Preview(input)["rows"]![0]!["lines"]!.AsArray().OfType<JsonObject>().Single(l=>l.S("lineType")=="rider").S("riderId");Assert.AreEqual("",Mapped());
        f.Store.Write((c,tx)=>{var account=Store.Get(c,"synthetic-hs-account","platform_account",tx)!;account["assignments"]=new JsonArray(new JsonObject{["riderId"]=Rider,["StartDate"]="2026-10-01",["EndDate"]="2026-10-31"});Store.Save(c,"platform_account",account,tx);return true;});Assert.AreEqual(Rider,Mapped());
        f.Store.Write((c,tx)=>{var account=Store.Get(c,"synthetic-hs-account","platform_account",tx)!;account["assignments"]!.AsArray().Add(new JsonObject{["riderId"]=owner,["StartDate"]="2026-10-15",["EndDate"]="2026-10-20"});Store.Save(c,"platform_account",account,tx);return true;});Assert.AreEqual("",Mapped());
    }
    [TestMethod]
    public void DisputeNotificationsRespectCompanyAndPlatformScopes()
    {
        using var f=new Fixture();var input=f.Input(Workbook(f.Cr(),car:5));f.Commit(input,f.Preview(input));var notice=f.Store.Read(c=>Store.List(c,"sys_notification").Single());
        System.Security.Claims.ClaimsPrincipal Scoped(string company,string platform)=>new(new System.Security.Claims.ClaimsIdentity(new[]{new System.Security.Claims.Claim("permission","platforms.hs.view"),new System.Security.Claims.Claim("finance.sponsorIds",company),new System.Security.Claims.Claim("finance.platforms",platform)},"synthetic"));
        Assert.IsTrue(f.Store.Read(c=>Notifications.Visible(c,Scoped(FinancialCore.Sponsors[0].Id,"HungerStation"),notice)));
        Assert.IsFalse(f.Store.Read(c=>Notifications.Visible(c,Scoped(FinancialCore.Sponsors[1].Id,"HungerStation"),notice)));
        Assert.IsFalse(f.Store.Read(c=>Notifications.Visible(c,Scoped(FinancialCore.Sponsors[0].Id,"Jahez"),notice)));
    }
}
