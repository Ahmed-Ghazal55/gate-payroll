using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class PlatformDeductionTests
{
    static string Rider=>HungerStationInvoiceTests.Rider;
    static JsonObject Policy()=>new(){["id"]="synthetic-policy",["name"]="Synthetic",["kind"]="approved_pay",["platform"]="all",["effectiveFrom"]="2026-01",["status"]="approved"};
    static WorkLine Work()=>new("synthetic-work",Rider,"Synthetic Rider","HungerStation","synthetic-account","2026-10","2026-10-01","2026-10-31",10,null,1000,true,"Synthetic","Synthetic",true);
    static JsonObject Tag(string category,string? rider=null,string month="2026-10",string status="confirmed")=>new(){["id"]="synthetic-tag-"+category,["platform"]="HungerStation",["status"]=status,["platformDeducted"]=new JsonObject{["category"]=category,["rider"]=rider??Rider,["month"]=month}};
    static List<JsonObject> Debts()=>[new(){["id"]="synthetic-tga",["iqama"]=Rider,["status"]="active",["category"]="TrafficFine",["remainingBalance"]=100,["monthlyInstallment"]=100},new(){["id"]="synthetic-advance",["iqama"]=Rider,["status"]="active",["category"]="Advance",["remainingBalance"]=50,["monthlyInstallment"]=50}];
    static List<JsonObject> Adjustments()=>[new(){["id"]="synthetic-penalty",["category"]="penalty",["amount"]=-20},new(){["id"]="synthetic-balance",["category"]="RiderBalance",["amount"]=-30},new(){["id"]="synthetic-bonus",["category"]="penalty",["amount"]=10}];
    [TestMethod]
    public void StageTwoSkipsOnlyMatchingConfirmedRiderMonthCategories()
    {
        var baseline=Engine.Calculate(Rider,"2026-10",Policy(),[Work()],Adjustments(),0,Debts());Assert.AreEqual(810m,baseline.Payable);
        var tags=new List<JsonObject>{Tag("tga"),Tag("penalty"),Tag("rider_balance")};
        var filtered=Engine.Calculate(Rider,"2026-10",Policy(),[Work()],Adjustments(),0,Debts(),platformDeductions:tags);Assert.AreEqual(960m,filtered.Payable);Assert.AreEqual(1,filtered.Lines.Count(l=>l.Category=="debt_installment"));Assert.IsTrue(filtered.Lines.Any(l=>l.Source=="synthetic-bonus"));
        foreach(var unrelated in new[]{Tag("tga",rider:"2"+2.ToString("D9")),Tag("tga",month:"2026-09"),Tag("tga",status:"pending")})
            Assert.AreEqual(810m,Engine.Calculate(Rider,"2026-10",Policy(),[Work()],Adjustments(),0,Debts(),platformDeductions:[unrelated]).Payable);
        var otherPlatform=Debts();otherPlatform[0]["platform"]="Jahez";
        Assert.AreEqual(810m,Engine.Calculate(Rider,"2026-10",Policy(),[Work()],Adjustments(),0,otherPlatform,platformDeductions:[Tag("tga")]).Payable);
    }
    [TestMethod]
    public void ApprovedFactsCreateIdempotentTagsWithoutRewritingSourceLines()
    {
        using var f=new HungerStationInvoiceTests.Fixture();var input=f.Input(HungerStationInvoiceTests.Workbook(f.Cr(),tga:100,balance:40,latePenalty:-20));
        input["confirmActualRider"]=true;input["lineFacts"]=new JsonArray(new JsonObject{["platformId"]="SYN-ACCOUNT-1",["riderId"]=Rider,["scheme"]="commission",["riderNet"]=800,["companyCommission"]=200});
        f.Commit(input,f.Preview(input));string invoice=f.Store.Read(c=>Store.List(c,"platform_invoice").Single().S("id"));var checker=HungerStationInvoiceTests.Actor("synthetic-checker");
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PlatformDeductions.ConfirmInvoice(c,tx,checker,invoice)));
        string line=f.Store.Read(c=>Store.List(c,"platform_invoice_line").Single(l=>l.N("tgaAmount")!=0).S("id"));var split=f.Store.Write((c,tx)=>HungerStationInvoices.Split(c,tx,HungerStationInvoiceTests.Actor(),line,new(){["riderPercent"]=30,["riderId"]=Rider}));
        f.Store.Write((c,tx)=>HungerStationInvoices.ApproveAllocation(c,tx,checker,split.S("id")));
        f.Store.Write((c,tx)=>{var doc=Store.Get(c,invoice,"platform_invoice",tx)!;doc["approvalStatus"]="approved";doc["approvedBy"]="synthetic-checker";Store.Save(c,"platform_invoice",doc,tx);return true;});
        string Before()=>f.Store.Read(c=>string.Join('\n',Store.List(c,"platform_invoice_line").OrderBy(l=>l.S("id")).Select(l=>l.ToJsonString())));string before=Before();
        Assert.AreEqual(3,f.Store.Write((c,tx)=>PlatformDeductions.ConfirmInvoice(c,tx,checker,invoice)));Assert.AreEqual(before,Before());
        int audits=f.Store.Read(c=>Store.List(c,"audit").Count);Assert.AreEqual(0,f.Store.Write((c,tx)=>PlatformDeductions.ConfirmInvoice(c,tx,checker,invoice)));Assert.AreEqual(audits,f.Store.Read(c=>Store.List(c,"audit").Count));
        var tags=f.Store.Read(c=>Store.List(c,PlatformDeductions.Kind));CollectionAssert.AreEquivalent(new[]{"tga","penalty","rider_balance"},tags.Select(t=>t["platformDeducted"].S("category")).ToArray());Assert.IsTrue(tags.All(t=>t["platformDeducted"].S("rider")==Rider&&t["platformDeducted"].S("month")=="2026-10"));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));
    }
    [TestMethod]
    public void RawNegativeBalanceNeverBecomesADeductionTagOrDebt()
    {
        using var f=new HungerStationInvoiceTests.Fixture();var input=f.Input(HungerStationInvoiceTests.Workbook(f.Cr(),balance:-40));input["confirmActualRider"]=true;input["lineFacts"]=new JsonArray(new JsonObject{["platformId"]="SYN-ACCOUNT-1",["riderId"]=Rider});f.Commit(input,f.Preview(input));
        string invoice=f.Store.Read(c=>Store.List(c,"platform_invoice").Single().S("id"));f.Store.Write((c,tx)=>{var doc=Store.Get(c,invoice,"platform_invoice",tx)!;doc["approvalStatus"]="approved";Store.Save(c,"platform_invoice",doc,tx);return true;});
        Assert.AreEqual(0,f.Store.Write((c,tx)=>PlatformDeductions.ConfirmInvoice(c,tx,HungerStationInvoiceTests.Actor("synthetic-checker"),invoice)));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));
    }
}
