using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetReconcile;

[TestClass]
public sealed class SheetReconcileTests
{
    [TestMethod]
    public void HashIqamaIsStablePrefixWithoutRawId()
    {
        string h=Reconcile.HashIqama("2000000001");
        Assert.AreEqual(16,h.Length);
        Assert.AreEqual(h,Reconcile.HashIqama("2000000001"));
        Assert.IsFalse(h.Contains("2000000001",StringComparison.Ordinal));
    }

    [TestMethod]
    public void ExcludesSubtotalAndRefLabels()
    {
        Assert.IsTrue(Reconcile.IsExcludedLabel("SUBTOTAL"));
        Assert.IsTrue(Reconcile.IsExcludedLabel("#REF!"));
        Assert.IsFalse(Reconcile.IsExcludedLabel("2000000001"));
    }

    [TestMethod]
    public void NormalizeIqamaFromScientificNotation()
    {
        Assert.AreEqual("2640335853",Reconcile.NormalizeIqamaDigits("2.640335853E9"));
        Assert.AreEqual("2000000001",Reconcile.NormalizeIqamaDigits("2000000001"));
    }

    [TestMethod]
    public void ExactColumnPrefersDeliverySplitNotTheSimilarHeader()
    {
        string[] headers=["تسليم عدد الطلبات","تسليم عدد طلبات"];
        Assert.AreEqual(1,Reconcile.ExactColumn(headers,"تسليم عدد طلبات"));
        Assert.AreEqual(0,Reconcile.ExactColumn(headers,"تسليم عدد الطلبات"));
        Assert.AreEqual(-1,Reconcile.ExactColumn(headers,"طلبات"));
    }

    [TestMethod]
    public void ExactColumnCollapsesLineBreaks()
    {
        string[] headers=["تسليم\nعدد طلبات"];
        Assert.AreEqual(0,Reconcile.ExactColumn(headers,"تسليم عدد طلبات"));
    }

    [TestMethod]
    public void SalaryFormulaIncludesDayOrderBonusAndAllowance()
    {
        Assert.IsTrue(Reconcile.TrySalaryEarned(100,2,50,100,10,out var low));
        Assert.AreEqual(1250m,low);
        Assert.IsTrue(Reconcile.TrySalaryEarned(100,2,50,500,10,out var high));
        Assert.AreEqual(2450m,high);
        Assert.IsFalse(Reconcile.TrySalaryEarned(100,2,-1,10,10,out _));
    }

    [TestMethod]
    public void CompareReportsStageAggregates()
    {
        string h=Reconcile.HashIqama("2000000001");
        var sheet=new List<SheetRow>{new(h,100,20,10,70,5,65)};
        var ours=new Dictionary<string,SheetRow>{[h]=new(h,100,20,10,70,0,70)};
        var report=Reconcile.Compare(sheet,ours,"synthetic");
        Assert.AreEqual(0,report.Stages.Single(s=>s.Stage=="due").Mismatched);
        Assert.AreEqual(1,report.Stages.Single(s=>s.Stage=="instalment").Mismatched);
        Assert.AreEqual(5m,report.Stages.Single(s=>s.Stage=="instalment").DiffSum);
        Assert.IsTrue(Reconcile.RenderMarkdown(report).Contains("instalment",StringComparison.Ordinal));
    }
}
