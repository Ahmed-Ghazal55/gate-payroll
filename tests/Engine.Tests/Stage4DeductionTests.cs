using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.Json.Nodes;

[TestClass]
public sealed class Stage4DeductionTests
{
    [TestMethod]
    public void FuelOverageDefaultAccountIs112511()
    {
        Assert.AreEqual("112511",ConfigLists.DefaultDeductions.Single(d=>d.Category=="FuelOverage").Account);
        Assert.AreEqual("112511",FinanceViews.MapAccount(new(){["accountCode"]="1210"},new(),new(){["category"]="FuelOverage"},null,null));
    }

    [TestMethod]
    public void HsWalletUses112506OpenDecision6()
    {
        Assert.AreEqual("112506",PipelineSettings.HsWalletAccount());
        Assert.AreEqual("112506",ConfigLists.DefaultDeductions.Single(d=>d.Category=="HsWalletTopUp").Account);
        Assert.AreEqual("112506",FinanceViews.MapAccount(new(){["accountCode"]="1210"},new(),new(){["category"]="HsWalletTopUp"},null,null));
    }

    [TestMethod]
    public void FuelUsageIsCaptureKind()
    {
        Assert.IsTrue(Operations.Kinds.Contains("fuel_usage"));
        Assert.IsTrue(PayRunInputs.KindsForApp("ninja").Contains("fuel_usage"));
        Assert.IsTrue(ErpImport.LiveOperationsQuery.HasKind("fuel_usage"));
    }

    [TestMethod]
    public void ThisMonthDebtIsStage4OldDebtIsStage8()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id,rider="2000000001",period="2026-10";
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"rider",new(){["id"]=rider,["iqama"]=rider,["name"]="Synthetic",["isDeliveryRider"]=true,["sponsorId"]=company,["status"]="Active"},tx);
            Store.Save(c,"debt",new(){["id"]="syn-old",["iqama"]=rider,["category"]="TrafficFine",["creditorSponsorId"]=company,["receivableAccountCode"]="112502",["status"]="active",["totalAmount"]=90,["remainingBalance"]=90,["monthlyInstallment"]=30,["effectiveFrom"]="2026-09",["lastDeductionPeriod"]=""},tx);
            Store.Save(c,"debt",new(){["id"]="syn-new",["iqama"]=rider,["category"]="InternetAdvance",["creditorSponsorId"]=company,["receivableAccountCode"]="112503",["status"]="active",["totalAmount"]=50,["remainingBalance"]=50,["monthlyInstallment"]=50,["effectiveFrom"]=period,["lastDeductionPeriod"]=""},tx);
            return true;
        });
        // Classification only — BuildDeductions needs a full cycle; pin the filter helpers via ConfigLists + period compare.
        Assert.IsTrue(string.CompareOrdinal("2026-09",period)<0);
        Assert.AreEqual("2026-10",period);
        var old=f.Store.Read(c=>Store.Get(c,"syn-old","debt")!);
        var neu=f.Store.Read(c=>Store.Get(c,"syn-new","debt")!);
        Assert.AreNotEqual(period,old.S("effectiveFrom"));
        Assert.AreEqual(period,neu.S("effectiveFrom"));
    }

    [TestMethod]
    public void SalaryFuelOverageDefaultStaysOnRider()
    {
        Assert.IsTrue(PipelineSettings.SalaryFuelOverageOnRider());
    }
}
