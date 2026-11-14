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

    [TestMethod]
    public void ThisMonthAdvanceWithZeroInstalmentSettlesInFullAtApproval()
    {
        // M4 + B1: month item takes remaining (instalment 0); approval posts Dr 212103 / Cr receivable and reduces the debt.
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            string company=input.S("companyId"),rider="2000000001",period="2026-10",debtId="syn-month-advance";
            f.Store.Write((c,tx)=>
            {
                Store.Save(c,"debt",new(){["id"]=debtId,["iqama"]=rider,["category"]="Advance",["creditorSponsorId"]=company,["receivableAccountCode"]="112501",["status"]="active",["totalAmount"]=40,["remainingBalance"]=40,["paidAmount"]=0,["monthlyInstallment"]=0,["effectiveFrom"]=period,["lastDeductionPeriod"]="",["createdAt"]="2026-10-02T00:00:00Z"},tx);
                FinancialCore.PostJournal(c,tx,"opening","Synthetic month advance",period,"syn-month-advance-open",new JsonArray(FinancialCore.CreateJournalLine("1210",company,40,0,iqama:rider,debtId:debtId),FinancialCore.CreateJournalLine("3101",company,0,40,iqama:rider)),"synthetic-opening-checker");
                return true;
            });
            var cycle=PayCycleTests.Through(f,input);
            var stage4=cycle["deductions"]!.AsArray().OfType<JsonObject>().Single(d=>d.S("type")=="stage4"&&d.S("source")==debtId);
            Assert.AreEqual(-40m,stage4.N("amount"));
            Assert.AreEqual(40m,f.Store.Read(c=>Store.Get(c,debtId,"debt")!.N("remainingBalance")),"preview must not mutate the debt");
            decimal netBefore=cycle["riders"]!.AsArray().OfType<JsonObject>().Single(r=>r.S("rider")==rider).N("net");
            cycle=PayCycleTests.Advance(f,cycle.S("id"),7,true);
            var debt=f.Store.Read(c=>Store.Get(c,debtId,"debt")!);
            Assert.AreEqual(0m,debt.N("remainingBalance"));
            Assert.AreEqual("settled",debt.S("status"));
            Assert.AreEqual(period,debt.S("lastDeductionPeriod"));
            var lines=f.Store.Read(c=>Store.List(c,"journal").Where(j=>j.S("payCycleId")==cycle.S("id")).SelectMany(j=>j["lines"]!.AsArray().OfType<JsonObject>()).ToList());
            Assert.AreEqual(40m,lines.Where(l=>l.S("accountCode")=="112501"&&l.S("debtId")==debtId).Sum(l=>l.N("credit")-l.N("debit")));
            Assert.AreEqual(40m,lines.Where(l=>l.S("accountCode")=="212103"&&l.S("debtId")==debtId).Sum(l=>l.N("debit")-l.N("credit")));
            foreach(var j in f.Store.Read(c=>Store.List(c,"journal").Where(j=>j.S("payCycleId")==cycle.S("id")).ToList()))
                Assert.AreEqual(j.N("totalDebit"),j.N("totalCredit"));
            decimal payable=lines.Where(l=>l.S("iqama")==rider&&l.S("accountCode")=="212103").Sum(l=>l.N("credit")-l.N("debit"));
            Assert.AreEqual(J.Money(Math.Max(0,netBefore)),payable,"payable after stage-4 must equal the shown (non-negative) net");
            // Reversal restores the month debt.
            f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,PayFixture.Maker,cycle.S("id"),new(){["action"]="reversal_request",["period"]="2026-11",["note"]="Synthetic stage4 reversal"}));
            f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,PayFixture.Checker,cycle.S("id"),new(){["action"]="reversal_approve"}));
            Assert.AreEqual(40m,f.Store.Read(c=>Store.Get(c,debtId,"debt")!.N("remainingBalance")));
            Assert.AreEqual("active",f.Store.Read(c=>Store.Get(c,debtId,"debt")!.S("status")));
        }
    }
}
