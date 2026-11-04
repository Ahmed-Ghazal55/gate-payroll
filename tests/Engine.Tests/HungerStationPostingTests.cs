using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class HungerStationPostingTests
{
    static System.Security.Claims.ClaimsPrincipal Maker=>HungerStationInvoiceTests.Actor();
    static System.Security.Claims.ClaimsPrincipal Checker=>HungerStationInvoiceTests.Actor("synthetic-checker");
    static JsonObject Confirm(JsonObject preview)=>new(){["rows"]=preview["rows"]!.DeepClone(),["fingerprint"]=preview.S("fingerprint"),["confirm"]=true};
    static string Import(HungerStationInvoiceTests.Fixture f,JsonObject? input=null)
    {input??=f.Input(HungerStationInvoiceTests.Workbook(f.Cr()));f.Commit(input,f.Preview(input));return f.Store.Read(c=>Store.List(c,"platform_invoice").OrderByDescending(i=>i.N("revision",1)).First().S("id"));}
    static JsonObject Approve(HungerStationInvoiceTests.Fixture f,string id)
    {var proposal=f.Store.Read(c=>HungerStationInvoices.Proposal(c,Checker,id));return f.Store.Write((c,tx)=>HungerStationInvoices.ApproveInvoice(c,tx,Checker,id,Confirm(proposal)));}
    static void Gate(HungerStationInvoiceTests.Fixture f,bool enabled)=>f.Store.Write((c,tx)=>PlatformPosting.Set(c,tx,Maker,"hs",new(){["enabled"]=enabled}));
    [TestMethod]
    public void ApprovalIsIndependentAndTheGatePreventsPostingUntilEnabled()
    {
        using var f=new HungerStationInvoiceTests.Fixture();string id=Import(f);var self=f.Store.Read(c=>HungerStationInvoices.Proposal(c,Maker,id));Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>HungerStationInvoices.ApproveInvoice(c,tx,Maker,id,Confirm(self))));
        var approved=Approve(f,id);Assert.AreEqual("approved",approved.S("approvalStatus"));Assert.AreEqual("blocked_by_gate",approved.S("accountingStatus"));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count));
        Gate(f,true);var posted=Approve(f,id);Assert.AreEqual("posted",posted.S("accountingStatus"));var original=f.Store.Read(c=>Store.Get(c,posted.S("journalEntryId"),"journal"))!;
        Assert.AreEqual(1150m,original["lines"]!.AsArray().OfType<JsonObject>().Sum(l=>l.N("debit")));Assert.IsTrue(original["lines"]!.AsArray().OfType<JsonObject>().Any(l=>l.S("accountCode")=="411102"));
        Approve(f,id);Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"journal").Count));
    }
    [TestMethod]
    public void CommissionVatDisputeAndAllocatedTgaUseOnlyApprovedAccounts()
    {
        using var f=new HungerStationInvoiceTests.Fixture();var input=f.Input(HungerStationInvoiceTests.Workbook(f.Cr(),car:50,tga:100));input["confirmActualRider"]=true;input["lineFacts"]=new JsonArray(new JsonObject{["platformId"]="SYN-ACCOUNT-1",["scheme"]="commission",["riderId"]=HungerStationInvoiceTests.Rider,["riderNet"]=800,["companyCommission"]=200});string id=Import(f,input);
        Gate(f,true);Assert.ThrowsExactly<ApiError>(()=>Approve(f,id));string line=f.Store.Read(c=>Store.List(c,"platform_invoice_line").Single(l=>l.N("tgaAmount")!=0).S("id"));var split=f.Store.Write((c,tx)=>HungerStationInvoices.Split(c,tx,Maker,line,new(){["riderPercent"]=30,["riderId"]=HungerStationInvoiceTests.Rider}));f.Store.Write((c,tx)=>HungerStationInvoices.ApproveAllocation(c,tx,Checker,split.S("id")));
        var posted=Approve(f,id);Assert.AreEqual("disputed",posted.S("status"));Assert.IsTrue(posted.B("settlementHold"));var lines=f.Store.Read(c=>Store.Get(c,posted.S("journalEntryId"),"journal")!["lines"]!.AsArray().OfType<JsonObject>().ToList());
        Assert.AreEqual(lines.Sum(l=>l.N("debit")),lines.Sum(l=>l.N("credit")));Assert.AreEqual(50m,lines.Where(l=>l.S("accountCode")=="112803").Sum(l=>l.N("debit")));Assert.AreEqual(30m,lines.Where(l=>l.S("accountCode")=="112502").Sum(l=>l.N("debit")));Assert.AreEqual(70m,lines.Where(l=>l.S("accountCode")=="526402").Sum(l=>l.N("debit")));Assert.AreEqual(150m,lines.Where(l=>l.S("accountCode")=="213201").Sum(l=>l.N("credit")));
        Assert.IsFalse(lines.Any(l=>l.S("accountCode") is "1241" or "2141"));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,PlatformDeductions.Kind).Count));
    }
    [TestMethod]
    public void TargetAndMissingFactsBlockUntilIndependentFactRevision()
    {
        using var f=new HungerStationInvoiceTests.Fixture();var input=f.Input(HungerStationInvoiceTests.Workbook(f.Cr()));input["lineFacts"]=new JsonArray(new JsonObject{["platformId"]="SYN-ACCOUNT-1",["scheme"]="target"});string id=Import(f,input);var error=Assert.ThrowsExactly<ApiError>(()=>Approve(f,id));StringAssert.Contains(error.Message,"التارجت");
        string line=f.Store.Read(c=>Store.List(c,"platform_invoice_line").Single(l=>l.S("lineType")=="rider").S("id"));string raw=f.Store.Read(c=>Store.Get(c,line,"platform_invoice_line")!.ToJsonString());
        f.Store.Write((c,tx)=>HungerStationInvoices.ReviseFacts(c,tx,HungerStationInvoiceTests.Actor("synthetic-preparer"),id,new(){["reason"]="Synthetic reviewed scheme",["lineFacts"]=new JsonArray(new JsonObject{["lineId"]=line,["scheme"]="salary"})}));
        Approve(f,id);Assert.AreEqual(raw,f.Store.Read(c=>Store.Get(c,line,"platform_invoice_line")!.ToJsonString()));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count));
    }
    [TestMethod]
    public void ReversalUsesAnOpenPeriodAndNeverRewritesTheOriginalJournal()
    {
        using var f=new HungerStationInvoiceTests.Fixture();string id=Import(f);Gate(f,true);var posted=Approve(f,id);string journalId=posted.S("journalEntryId"),original=f.Store.Read(c=>Store.Get(c,journalId,"journal")!.ToJsonString());
        f.Store.Write((c,tx)=>{Store.Save(c,"accounting_period",new(){["id"]="synthetic-closed",["companyId"]=FinancialCore.Sponsors[0].Id,["period"]="2026-10",["status"]="closed"},tx);return true;});
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>HungerStationInvoices.RequestReversal(c,tx,Maker,id,new(){["period"]="2026-10",["reason"]="Synthetic correction"})));
        var request=f.Store.Write((c,tx)=>HungerStationInvoices.RequestReversal(c,tx,Maker,id,new(){["period"]="2026-11",["reason"]="Synthetic correction"}));Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>HungerStationInvoices.ApproveReversal(c,tx,Maker,id,request.S("id"))));
        var approved=f.Store.Write((c,tx)=>HungerStationInvoices.ApproveReversal(c,tx,Checker,id,request.S("id")));var reverse=f.Store.Read(c=>Store.Get(c,approved.S("journalEntryId"),"journal"))!;Assert.AreEqual(journalId,reverse.S("reversesJournalId"));Assert.AreEqual("2026-11",reverse.S("period"));Assert.AreEqual(original,f.Store.Read(c=>Store.Get(c,journalId,"journal")!.ToJsonString()));
        var next=f.Input(HungerStationInvoiceTests.Workbook(f.Cr()));next["replacesInvoiceId"]=id;string replacement=Import(f,next);Assert.AreNotEqual(id,replacement);var corrected=Approve(f,replacement);Assert.AreEqual("2026-11",f.Store.Read(c=>Store.Get(c,corrected.S("journalEntryId"),"journal")!.S("period")));Assert.AreEqual(3,f.Store.Read(c=>Store.List(c,"journal").Count));
    }
}
