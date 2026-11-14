using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.Json.Nodes;

[TestClass]
public sealed class AllocationWeightTests
{
    static PayReviewCategory.OperatorMetrics[] Two()=>[
        new("2000000001",Orders:10,Days:2,Periods:1,Compliance:80),
        new("2000000002",Orders:10,Days:8,Periods:3,Compliance:20)];

    [TestMethod]
    public void OrdersWeightSplitsEvenlyWhenEqual()
    {
        var shares=PayReviewCategory.Shares(Two(),"orders","days");
        Assert.AreEqual(0.5m,shares[0]);
        Assert.AreEqual(0.5m,shares[1]);
    }

    [TestMethod]
    public void DaysWeightFavoursMoreDays()
    {
        var shares=PayReviewCategory.Shares(Two(),"days","orders");
        Assert.AreEqual(0.2m,shares[0]);
        Assert.AreEqual(0.8m,shares[1]);
    }

    [TestMethod]
    public void PeriodsWeightFavoursMorePeriods()
    {
        var shares=PayReviewCategory.Shares(Two(),"periods","orders");
        Assert.AreEqual(0.25m,shares[0]);
        Assert.AreEqual(0.75m,shares[1]);
    }

    [TestMethod]
    public void ComplianceWeightFavoursHigherScore()
    {
        var shares=PayReviewCategory.Shares(Two(),"compliance","orders");
        Assert.AreEqual(0.8m,shares[0]);
        Assert.AreEqual(0.2m,shares[1]);
    }

    [TestMethod]
    public void TieBreakUsesDaysWhenOrderFractionsEqual()
    {
        // Equal order weights → equal floor; leftover halala goes to higher days (tie-break).
        var ops=new PayReviewCategory.OperatorMetrics[]{new("2000000001",1,Days:1),new("2000000002",1,Days:9)};
        var units=PayReviewCategory.SplitUnits(ops,units:3,weight:"orders",tieBreak:"days");
        Assert.AreEqual(3L,units.Sum());
        Assert.AreEqual(1L,units[0]);
        Assert.AreEqual(2L,units[1]);
    }

    [TestMethod]
    public void ProposeNeverMarksAccepted()
    {
        var result=PayReviewCategory.Propose(Two(),20,100m,10m,"days","orders");
        Assert.AreEqual(2,result.Count);
        Assert.IsFalse(result.ToJsonString().Contains("accepted",StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(100m,result.OfType<JsonObject>().Sum(r=>r.N("baseGross")));
        Assert.AreEqual(10m,result.OfType<JsonObject>().Sum(r=>r.N("deduction")));
    }

    [TestMethod]
    public void DeductionProposeUsesWeight()
    {
        var alloc=InvoiceDeductionClassification.Propose(Two(),10m,weight:"days",tieBreak:"orders");
        Assert.AreEqual(10m,alloc.OfType<JsonObject>().Sum(a=>a.N("amount")));
        Assert.AreEqual(2m,alloc.OfType<JsonObject>().Single(a=>a.S("riderId")=="2000000001").N("amount"));
        Assert.AreEqual(8m,alloc.OfType<JsonObject>().Single(a=>a.S("riderId")=="2000000002").N("amount"));
    }

    [TestMethod]
    public void DatedRowStillWinsOverWeight()
    {
        var alloc=InvoiceDeductionClassification.Propose(Two(),20m,date:"2026-10-05",assigneeOnDate:"2000000001",weight:"days");
        Assert.AreEqual(1,alloc.Count);
        Assert.AreEqual("2000000001",alloc[0]!.AsObject().S("riderId"));
    }
}
