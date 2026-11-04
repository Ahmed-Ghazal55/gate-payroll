using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class PayPlatformTests
{
    [TestMethod]
    public void EveryPlatformHasAnActiveIndependentReceivableAndUnknownPlatformsUseOther()
    {
        var expected=new[]{"112801","112804","112805","112806","112807","112808"};CollectionAssert.AreEqual(expected,PayPlatforms.Table.Select(a=>PayPlatforms.Active(a.Receivable)).ToArray());Assert.AreEqual("112802",PayPlatforms.Active(PayPlatforms.For("synthetic-platform").Receivable));Assert.AreEqual("112805",PayPlatforms.For("HungerStation").Receivable);Assert.AreEqual("411501",PayPlatforms.Revenue("Keeta","agent"));
        // Jahez principal411106 is stopped in the binding chart; preserve the catalog reference and refuse posting.
        Assert.AreEqual("411106",PayPlatforms.For("Jahez").Principal);Assert.ThrowsExactly<ApiError>(()=>PayPlatforms.Revenue("jahez","principal"));
    }
    [TestMethod]
    public void NewHsInvoicesUseTheNewReceivableButOldTargetDocumentsStayByteIdentical()
    {
        using var f=new HungerStationInvoiceTests.Fixture();string company=FinancialCore.Sponsors[0].Id;
        var old=new JsonObject{["id"]="synthetic-old-journal",["chart"]="target",["sourceRef"]="synthetic-existing",["period"]="2026-09",["status"]="posted",["lines"]=new JsonArray(FinancialCore.CreateJournalLine("112802",company,100,0,targetChart:true),FinancialCore.CreateJournalLine("411102",company,0,100,targetChart:true))};
        f.Store.Write((c,tx)=>{Store.Save(c,"journal",old,tx);return true;});string raw=f.Store.Read(c=>Store.Get(c,old.S("id"),"journal")!.ToJsonString());
        var input=f.Input(HungerStationInvoiceTests.Workbook(f.Cr()));f.Commit(input,f.Preview(input));string id=f.Store.Read(c=>Store.List(c,"platform_invoice").Single().S("id"));var checker=HungerStationInvoiceTests.Actor("synthetic-checker");
        f.Store.Write((c,tx)=>PlatformPosting.Set(c,tx,checker,"hs",new(){["enabled"]=true}));var preview=f.Store.Read(c=>HungerStationInvoices.Proposal(c,checker,id));var posted=f.Store.Write((c,tx)=>HungerStationInvoices.ApproveInvoice(c,tx,checker,id,new(){["rows"]=preview["rows"]!.DeepClone(),["fingerprint"]=preview.S("fingerprint"),["confirm"]=true}));
        var lines=f.Store.Read(c=>Store.Get(c,posted.S("journalEntryId"),"journal")!["lines"]!.AsArray().OfType<JsonObject>().ToArray());Assert.IsTrue(lines.Any(l=>l.S("accountCode")=="112805"));Assert.IsFalse(lines.Any(l=>l.S("accountCode")=="112802"));
        Assert.AreEqual(raw,f.Store.Read(c=>Store.Get(c,old.S("id"),"journal")!.ToJsonString()));Assert.AreEqual("112802",FinanceViews.MapAccount(old["lines"]![0]!.AsObject(),old,null,null,null));
        Assert.AreEqual("112805",FinanceViews.MapAccount(new(){["accountCode"]="1241",["platform"]="HungerStation"},new(),null,null,null));
    }
}
