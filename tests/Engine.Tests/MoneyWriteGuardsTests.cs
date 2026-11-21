using Microsoft.VisualStudio.TestTools.UnitTesting;
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
}
