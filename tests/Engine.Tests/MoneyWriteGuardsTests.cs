using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class MoneyWriteGuardsTests
{
    [TestMethod]
    public void SimulationRejectsEveryMoneyEffect()
    {
        foreach(MoneyEffectKind effect in Enum.GetValues<MoneyEffectKind>())
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>MoneyWriteGuards.Demand(runMode:1,payoutBlocked:false,effect)).Status);
    }

    [TestMethod]
    public void PayoutBlockedAllowsAccountingAndDebtButRejectsBankAndPayout()
    {
        MoneyWriteGuards.Demand(0,true,MoneyEffectKind.Accounting);
        MoneyWriteGuards.Demand(0,true,MoneyEffectKind.Debt);
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>MoneyWriteGuards.Demand(0,true,MoneyEffectKind.BankSent)).Status);
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>MoneyWriteGuards.Demand(0,true,MoneyEffectKind.Payout)).Status);
    }

    [TestMethod]
    public void LiveUnblockedAllowsAllEffects()
    {
        foreach(MoneyEffectKind effect in Enum.GetValues<MoneyEffectKind>())
            MoneyWriteGuards.Demand(0,false,effect);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DualProvider_SimulationCannotPostJournal(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        SchemaOrgCostCentre.Ensure(c);SchemaFinAccount.Ensure(c);SchemaFinPeriod.Ensure(c);SchemaFinJournal.Ensure(c);SchemaFinPeriodBalance.Ensure(c);
        string company=MoneySchemaFixture.Company(c);
        string period=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-11-01',1,datetime('now'))",("@id",period),("@c",company));
        string debit=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=1 AND nature=1 ORDER BY code LIMIT 1")!;
        string credit=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=1 AND nature=2 ORDER BY code LIMIT 1")!;
        var draft=new JournalPosting.Draft(
            CompanyId:Guid.Parse(company),PeriodId:Guid.Parse(period),PeriodMonth:new DateOnly(2026,11,1),
            JournalDate:new DateOnly(2026,11,15),SourceType:"manual",SourceId:Guid.NewGuid(),Purpose:"manual",
            RunMode:1,PayoutBlocked:false,Actor:"synthetic",
            Lines:[
                new(Guid.Parse(debit),SchemaOrgCostCentre.NoneId,"none",10m,0m,null),
                new(Guid.Parse(credit),SchemaOrgCostCentre.NoneId,"none",0m,10m,null)
            ]);
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,draft)).Status);
        Assert.AreEqual(0L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_journal")!));
    }

    [TestMethod]
    public void LegacyPostJournalUsesRunModeZeroAndRejectsSimulationContext()
    {
        using var f=new PayFixture();string company=FinancialCore.Sponsors[0].Id;
        JsonArray Lines()=>new(
            FinancialCore.CreateJournalLine("1210",company,10,0),
            FinancialCore.CreateJournalLine("3101",company,0,10));
        // Legacy tables are always real runs (run_mode=0) — normal post is allowed.
        f.Store.Write((c,tx)=>FinancialCore.PostJournal(c,tx,"opening","Synthetic legacy guard","2026-10","legacy-guard-ok",Lines(),"synthetic"));
        Assert.IsTrue(f.Store.Read(c=>Store.List(c,"journal").Any(j=>j.S("sourceRef")=="legacy-guard-ok")));
        // Ambient simulation context must never reach PostJournal.
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>
        {
            using(MoneyWriteGuards.BeginSimulationContext())
                f.Store.Write((c,tx)=>FinancialCore.PostJournal(c,tx,"opening","Synthetic sim context","2026-10","legacy-guard-sim",Lines(),"synthetic"));
        }).Status);
        Assert.IsFalse(f.Store.Read(c=>Store.List(c,"journal").Any(j=>j.S("sourceRef")=="legacy-guard-sim")));
    }

    [TestMethod]
    public void LegacyPostJournalReadsPayoutBlockedWhenPresentElseFalse()
    {
        using var f=new PayFixture();string company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>{
            Store.Save(c,PayCycles.Kind,new(){["id"]="cycle-legacy-blocked",["companyId"]=company,["period"]="2026-10",["status"]="approved",["payoutBlocked"]=true,["runMode"]=0},tx);
            return true;
        });
        JsonArray Lines()=>new(
            FinancialCore.CreateJournalLine("212103",company,10,0,targetChart:true,payChart:true),
            FinancialCore.CreateJournalLine("111101",company,0,10,targetChart:true,payChart:true));
        // payout_blocked from the stored cycle rejects payout-type journals.
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
            FinancialCore.PostJournal(c,tx,"bank_payroll_payout","Synthetic blocked payout","2026-10","legacy-payout-blocked",Lines(),"synthetic",originSponsorId:company,creditorSponsorId:company,targetChart:true,payChart:true))).Status);
        // Accounting still allowed while payout is blocked; when no stored flag, blocked is false.
        f.Store.Write((c,tx)=>FinancialCore.PostJournal(c,tx,"opening","Synthetic accounting while blocked","2026-10","legacy-acct-ok",new JsonArray(
            FinancialCore.CreateJournalLine("1210",company,5,0),
            FinancialCore.CreateJournalLine("3101",company,0,5)),"synthetic",originSponsorId:company,creditorSponsorId:company));
        Assert.IsTrue(f.Store.Read(c=>Store.List(c,"journal").Any(j=>j.S("sourceRef")=="legacy-acct-ok")));
    }

    [TestMethod]
    public void LegacyPostJournalRejectsStoredDocumentCycleRunModeOne()
    {
        using var f=new PayFixture();string company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>{
            Store.Save(c,PayCycles.Kind,new(){["id"]="cycle-legacy-sim",["companyId"]=company,["period"]="2026-10",["status"]="approved",["payoutBlocked"]=false,["runMode"]=1},tx);
            return true;
        });
        JsonArray Lines()=>new(
            FinancialCore.CreateJournalLine("1210",company,10,0),
            FinancialCore.CreateJournalLine("3101",company,0,10));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
            FinancialCore.PostJournal(c,tx,"opening","Synthetic cycle sim","2026-10","legacy-cycle-sim",Lines(),"synthetic",originSponsorId:company,creditorSponsorId:company))).Status);
        Assert.IsFalse(f.Store.Read(c=>Store.List(c,"journal").Any(j=>j.S("sourceRef")=="legacy-cycle-sim")));
    }

    [TestMethod]
    public void PayoutBlockedRejectsBankCashCreditUnlessAccountingWhitelist()
    {
        Assert.IsTrue(MoneyWriteGuards.AccountingOnlyPurposes.Contains("payroll_accrual"));
        Assert.IsTrue(MoneyWriteGuards.AccountingOnlyPurposes.Contains("debt_accrual"));
        Assert.IsTrue(MoneyWriteGuards.AccountingOnlyPurposes.Contains("accrual_reversal"));
        Assert.IsTrue(MoneyWriteGuards.IsBankOrCashAccountCode("111101"));
        Assert.IsTrue(MoneyWriteGuards.IsBankOrCashAccountCode("111201"));
        Assert.IsFalse(MoneyWriteGuards.IsBankOrCashAccountCode("212103"));

        using var f=new PayFixture();string company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>{
            Store.Save(c,PayCycles.Kind,new(){["id"]="cycle-payout-acct",["companyId"]=company,["period"]="2026-10",["status"]="approved",["payoutBlocked"]=true,["runMode"]=0},tx);
            return true;
        });
        // Non-payout purpose that credits a bank must still be rejected while payout is blocked.
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
            FinancialCore.PostJournal(c,tx,"internet_advance","Synthetic bank credit","2026-10","legacy-bank-credit",new JsonArray(
                FinancialCore.CreateJournalLine("112503",company,10,0,targetChart:true,payChart:true),
                FinancialCore.CreateJournalLine("111201",company,0,10,targetChart:true,payChart:true)),"synthetic",originSponsorId:company,creditorSponsorId:company,targetChart:true,payChart:true))).Status);
        // Accrual whitelist may post even when a line credits cash/bank (accounting-only).
        f.Store.Write((c,tx)=>FinancialCore.PostJournal(c,tx,"payroll_accrual","Synthetic accrual while blocked","2026-10","legacy-accrual-ok",new JsonArray(
            FinancialCore.CreateJournalLine("5301",company,10,0),
            FinancialCore.CreateJournalLine("2110",company,0,10)),"synthetic",originSponsorId:company,creditorSponsorId:company));
        Assert.IsTrue(f.Store.Read(c=>Store.List(c,"journal").Any(j=>j.S("sourceRef")=="legacy-accrual-ok")));
    }
}
