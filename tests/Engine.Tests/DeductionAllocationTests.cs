using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.Json.Nodes;

[TestClass]
public sealed class DeductionAllocationTests
{
    [TestMethod]
    public void SumMustEqualRowAmount()
    {
        var d=new JsonObject{["amount"]=10,["allocations"]=new JsonArray(
            new JsonObject{["bearer"]="rider",["riderId"]="2000000001",["amount"]=6},
            new JsonObject{["bearer"]="rider",["riderId"]="2000000002",["amount"]=3})};
        var err=Assert.ThrowsExactly<ApiError>(()=>InvoiceDeductionClassification.DemandSum(d));
        Assert.AreEqual(400,err.Status);
    }

    [TestMethod]
    public void ProposeSingleOperatorIsThatRider()
    {
        var alloc=InvoiceDeductionClassification.Propose([("2000000001",10m)],12.5m);
        Assert.AreEqual(1,alloc.Count);
        Assert.AreEqual("rider",alloc[0]!.AsObject().S("bearer"));
        Assert.AreEqual("2000000001",alloc[0]!.AsObject().S("riderId"));
        Assert.AreEqual(12.5m,alloc[0]!.AsObject().N("amount"));
    }

    [TestMethod]
    public void ProposeDatedRowUsesAssignee()
    {
        var alloc=InvoiceDeductionClassification.Propose([("2000000001",5m),("2000000002",5m)],20m,date:"2026-10-05",assigneeOnDate:"2000000002");
        Assert.AreEqual(1,alloc.Count);
        Assert.AreEqual("2000000002",alloc[0]!.AsObject().S("riderId"));
        Assert.AreEqual(20m,alloc[0]!.AsObject().N("amount"));
    }

    [TestMethod]
    public void ProposeOrderShareRoundsToHalala()
    {
        var alloc=InvoiceDeductionClassification.Propose([("2000000001",1m),("2000000002",2m)],10m);
        Assert.AreEqual(2,alloc.Count);
        Assert.AreEqual(10m,alloc.OfType<JsonObject>().Sum(a=>a.N("amount")));
        Assert.AreEqual(3.33m,alloc.OfType<JsonObject>().Single(a=>a.S("riderId")=="2000000001").N("amount"));
        Assert.AreEqual(6.67m,alloc.OfType<JsonObject>().Single(a=>a.S("riderId")=="2000000002").N("amount"));
    }

    [TestMethod]
    public void MigrateCompanyRiderAndMultiAccount()
    {
        var company=new JsonObject{["amount"]=5,["level"]="company_level",["expenseAccount"]="526402"};
        InvoiceDeductionClassification.MigrateLevel(company);
        Assert.AreEqual(1,InvoiceDeductionClassification.AllocationsOf(company).Count);
        Assert.AreEqual("company",company["allocations"]![0]!.AsObject().S("bearer"));

        var rider=new JsonObject{["amount"]=7,["level"]="rider_level",["riderId"]="2000000001"};
        InvoiceDeductionClassification.MigrateLevel(rider);
        Assert.AreEqual("2000000001",rider["allocations"]![0]!.AsObject().S("riderId"));

        var multi=new JsonObject{["amount"]=9,["level"]="account_level"};
        InvoiceDeductionClassification.MigrateLevel(multi,multiRiderAccount:true);
        Assert.AreEqual(InvoiceDeductionClassification.NeedsReview,multi.S("migrationStatus"));
        Assert.AreEqual(0,InvoiceDeductionClassification.AllocationsOf(multi).Count);
    }

    [TestMethod]
    public void MultiRiderReportCountsOnly()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id,invoice="syn-alloc-inv",line="syn-alloc-line";
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"platform_account",new(){["id"]="syn-alloc-acc",["companyId"]=company,["platformCode"]="ninja",["assignments"]=new JsonArray(
                new JsonObject{["riderId"]="2000000001",["RawEffectiveFrom"]="2026-10-01T00:00:00Z"},
                new JsonObject{["riderId"]="2000000002",["RawEffectiveFrom"]="2026-10-01T00:00:00Z"})},tx);
            Store.Save(c,"platform_invoice",new(){["id"]=invoice,["companyId"]=company,["platform"]="Ninja",["period"]="2026-10"},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]=line,["invoiceId"]=invoice,["platformAccountId"]="syn-alloc-acc",["deductions"]=new JsonArray(
                new JsonObject{["category"]="penalty",["amount"]=30,["level"]="account_level"})},tx);
            return true;
        });
        var report=f.Store.Read(c=>InvoiceDeductionClassification.MultiRiderReport(c,null,invoice));
        Assert.AreEqual(1m,report.N("multiRiderRows"));
        Assert.AreEqual(30m,report.N("multiRiderAmount"));
        Assert.IsFalse(report.ToJsonString().Contains("2000000001",StringComparison.Ordinal));
    }
}
