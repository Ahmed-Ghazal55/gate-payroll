using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ErpImport;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class DebtSheetImportTests
{
    const string Iqama="2000000901";
    static string Sponsor=>FinancialCore.ResolveSponsor("BW").Id;
    static List<JsonObject> Riders()=>[new(){["id"]=Iqama,["iqama"]=Iqama,["name"]="SYNTHETIC_PRIVATE_NAME",["financeSponsorId"]=Sponsor}];
    static JsonObject Mapping()=>new()
    {
        ["version"]=1,["status"]="approved",["approvedBy"]="Ghazal",["sheet"]="Debts",["deductionsSheet"]="Detail",["controlSheet"]="Control",["effectiveFrom"]="2026-11",["paymentAllocation"]=new JsonObject{["method"]="FIFO"},
        ["columns"]=new JsonArray(C("A","ID","identity"),C("B","Old","debt","CashAdvance","112501"),C("C","New","debt","DriverLicense","112506"),C("D","Gross","gross"),C("E","Paid","payment"),C("F","Payments","paymentsTotal"),C("G","Net","net")),
        ["deductionsColumns"]=new JsonArray(C("C","Deduction","deduction","AdministrativeDeduction","112505")),
        ["controlSections"]=new JsonArray(new JsonObject{["deductionColumn"]="C",["totalCell"]="B2",["companies"]=new JsonArray(new JsonObject{["company"]="BW",["valueColumn"]="B",["fromRow"] =2,["toRow"]=2})})
    };
    static JsonObject C(string col,string header,string role,string category="",string account="")=>new(){["column"]=col,["header"]=header,["role"]=role,["category"]=category,["account"]=account,["sign"]=role=="payment"?-1:1,["status"]="proposed"};
    static byte[] Book(params (string Name,string[][] Rows)[] sheets)
    {
        using var buffer=new MemoryStream();using(var zip=new ZipArchive(buffer,ZipArchiveMode.Create,true))
        {
            XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main",rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            void Add(string path,XElement xml){using var w=new StreamWriter(zip.CreateEntry(path).Open(),new UTF8Encoding(false));w.Write(xml);}
            Add("xl/workbook.xml",new(ns+"workbook",new XAttribute(XNamespace.Xmlns+"r",rel),new XElement(ns+"sheets",sheets.Select((s,i)=>new XElement(ns+"sheet",new XAttribute("name",s.Name),new XAttribute(rel+"id","r"+i))))));
            Add("xl/_rels/workbook.xml.rels",new("Relationships",sheets.Select((s,i)=>new XElement("Relationship",new XAttribute("Id","r"+i),new XAttribute("Target","worksheets/s"+i+".xml")))));
            for(int i=0;i<sheets.Length;i++)Add("xl/worksheets/s"+i+".xml",new(ns+"worksheet",new XElement(ns+"sheetData",sheets[i].Rows.Select((row,n)=>new XElement(ns+"row",new XAttribute("r",n+1),row.Select((v,c)=>new XElement(ns+"c",new XAttribute("r",((char)('A'+c)).ToString()+(n+1)),new XAttribute("t","inlineStr"),new XElement(ns+"is",new XElement(ns+"t",v)))))))));
        }
        return buffer.ToArray();
    }
    static byte[] Debt(string old="100",string newer="200",string paid="150",string net="150",string identity=Iqama)=>Book(("Debts",[["ID","Old","New","Gross","Paid","Payments","Net"],[identity,old,newer,"300",paid,paid,net]]));
    static byte[] Deductions(string total="10")=>Book(("Detail",[["ID","Name","Deduction","Cash","Internet","Wallet","Fine","Total"],[Iqama,"SYNTHETIC_PRIVATE_NAME","10","0","0","0","0","10"]]),("Control",[["Label","Amount"],["BW",total]]));

    [TestMethod]
    public void FifoConsumesOldBucketsAndCreatesOnlyTheRemainingCategory()
    {
        var preview=DebtSheetImporter.Preview(Debt(),Deductions(),Mapping(),Riders());Assert.IsTrue(preview.Summary.B("canCommit"));
        Assert.AreEqual(1,preview.Documents.Count);var debt=preview.Documents[0].Body;
        Assert.AreEqual("DriverLicense",debt.S("category"));Assert.AreEqual(150m,debt.N("remainingBalance"));Assert.AreEqual("DEBT-SHEET:2:DriverLicense",debt.S("sourceRef"));
        Assert.AreEqual(150m,preview.Summary.N("openingDebtTotal"));
        string report=JsonSerializer.Serialize(preview);Assert.DoesNotContain(Iqama,report);Assert.DoesNotContain("SYNTHETIC_PRIVATE_NAME",report);Assert.DoesNotContain("sourceFingerprint",report);
    }
    [TestMethod]
    public void OverpaymentCreatesSeparateCreditAndNeverANegativeOrZeroDebt()
    {
        var p=DebtSheetImporter.Preview(Debt(paid:"350",net:"-50"),Deductions(),Mapping(),Riders());
        Assert.IsTrue(p.Summary.B("canCommit"));Assert.AreEqual(0m,p.Summary.N("openingDebtTotal"));Assert.AreEqual(50m,p.Summary.N("riderCreditTotal"));
        Assert.AreEqual(1,p.Documents.Count);Assert.AreEqual("rider_credit",p.Documents[0].Kind);Assert.AreEqual(50m,p.Documents[0].Body.N("amount"));Assert.AreEqual("",p.Documents[0].Body.S("category"));
        Assert.AreEqual(0,DebtSheetImporter.Preview(Debt(paid:"300",net:"0"),Deductions(),Mapping(),Riders()).Documents.Count);
    }
    [TestMethod]
    public void DraftMappingUnresolvedAccountsAndRowOrControlMismatchesRefuseWholeCommit()
    {
        var m=Mapping();m["status"]="draft";Assert.IsFalse(DebtSheetImporter.Preview(Debt(),Deductions(),m,Riders()).Summary.B("canCommit"));
        m=Mapping();m["columns"]![2]!["status"]="needs_decision";
        Assert.IsFalse(DebtSheetImporter.Preview(Debt(),Deductions(),m,Riders()).Summary.B("canCommit"));
        var wrong=DebtSheetImporter.Preview(Debt(net:"151"),Deductions(),Mapping(),Riders());Assert.AreEqual(2,wrong.Summary["mismatchedRows"]![0]!.GetValue<int>());Assert.AreEqual(0,wrong.Documents.Count);
        Assert.IsFalse(DebtSheetImporter.Preview(Debt(),Deductions("11"),Mapping(),Riders()).Summary.B("canCommit"));
        Assert.ThrowsExactly<ImportFailure>(()=>DebtSheetImporter.EnsureCommittable(wrong));
        var cent=DebtSheetImporter.Preview(Debt(net:"150.01"),Deductions(),Mapping(),Riders());Assert.IsTrue(cent.Summary.B("canCommit"));
    }
    [TestMethod]
    public void StagingCommitIsAtomicIdempotentAndPreservesOpeningReviewAndFingerprints()
    {
        string path=Path.Combine(Path.GetTempPath(),"gate-debt-sheet-"+Guid.NewGuid().ToString("N"));
        try
        {
            var target=new Store(path);target.Write((c,tx)=>{Store.Save(c,"rider",Riders()[0],tx);return true;});
            var preview=DebtSheetImporter.Preview(Debt(),Deductions(),Mapping(),Riders());Assert.AreEqual(1,DebtSheetImporter.Commit(target,preview));Assert.AreEqual(0,DebtSheetImporter.Commit(target,preview));
            Assert.AreEqual(1,target.Read(c=>Store.List(c,"debt").Count));Assert.AreEqual("pending_approval",target.Read(c=>Store.List(c,"debt").Single().S("status")));Assert.AreEqual(0,target.Read(c=>Store.List(c,"journal").Count));
            var changed=DebtSheetImporter.Preview(Debt(paid:"200",net:"100"),Deductions(),Mapping(),Riders());Assert.ThrowsExactly<ImportFailure>(()=>DebtSheetImporter.Commit(target,changed));
            Assert.AreEqual(150m,target.Read(c=>Store.List(c,"debt").Single().N("remainingBalance")));
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(path))Directory.Delete(path,true);}
    }
    [TestMethod]
    public void ExcelNumericIdentifiersNormalizeWithoutRoundingOrLosingDigits()
    {
        Assert.IsTrue(DebtSheetImporter.Preview(Debt(identity:Iqama+".00"),Deductions(),Mapping(),Riders()).Summary.B("canCommit"));
        var fractional=DebtSheetImporter.Preview(Debt(identity:Iqama+".5"),Deductions(),Mapping(),Riders());
        Assert.IsFalse(fractional.Summary.B("canCommit"));Assert.AreEqual(1m,fractional.Summary["errorCounts"].N("invalid_identity"));
    }
    [TestMethod]
    public void FinalReceiptSheetReconcilesReferencesAndUsesItsExplicitCompanyEvidence()
    {
        var m=Mapping();m["receiptSheet"]="Receipts";m["companyAliases"]=new JsonObject{["SYN-SOURCE-COMPANY"]="BW"};m["receiptColumns"]=new JsonObject{["identity"]="A",["amount"]="B",["company"]="C",["reference"]="D"};
        var bytes=Book(("Detail",[["ID","Name","Deduction","Cash","Internet","Wallet","Fine","Total"],[Iqama,"SYNTHETIC_PRIVATE_NAME","10","0","0","0","0","10"]]),("Control",[["Label","Amount"],["BW","10"]]),("Receipts",[["ID","Amount","Company","Reference"],[Iqama,"10","SYN-SOURCE-COMPANY","SYN-ADV-1"]]));
        var p=DebtSheetImporter.Preview(Debt(),bytes,m,Riders());Assert.IsTrue(p.Summary.B("canCommit"));Assert.AreEqual(1m,p.Summary.N("receiptReferenceCount"));Assert.IsTrue(p.Summary.B("receiptGrandMatched"));
        Assert.DoesNotContain("SYN-ADV-1",p.Summary.ToJsonString());
    }
    [TestMethod]
    public void AChangedTargetRiderRollsBackTheWholeOpeningBatch()
    {
        const string other="2000000902";var riders=Riders();var second=Riders()[0].Clone();second["id"]=other;second["iqama"]=other;riders.Add(second);
        var bytes=Book(("Debts",[["ID","Old","New","Gross","Paid","Payments","Net"],[Iqama,"100","200","300","150","150","150"],[other,"100","200","300","150","150","150"]]));
        var preview=DebtSheetImporter.Preview(bytes,Deductions(),Mapping(),riders);Assert.IsTrue(preview.Summary.B("canCommit"));
        string path=Path.Combine(Path.GetTempPath(),"gate-debt-rollback-"+Guid.NewGuid().ToString("N"));
        try{var store=new Store(path);store.Write((c,tx)=>{Store.Save(c,"rider",riders[0],tx);return true;});Assert.ThrowsExactly<ImportFailure>(()=>DebtSheetImporter.Commit(store,preview));Assert.AreEqual(0,store.Read(c=>Store.List(c,"debt").Count));Assert.AreEqual(0,store.Read(c=>Store.List(c,"audit").Count));}
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(path))Directory.Delete(path,true);}
    }
}
