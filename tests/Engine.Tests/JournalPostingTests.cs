using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class JournalPostingTests
{
    static (System.Data.Common.DbConnection c,string company,string period,string debit,string credit) Open(bool sqlServer=false)
    {
        var c=MoneySchemaFixture.Open(sqlServer);
        SchemaOrgCostCentre.Ensure(c);SchemaFinAccount.Ensure(c);SchemaFinPeriod.Ensure(c);SchemaFinJournal.Ensure(c);SchemaFinPeriodBalance.Ensure(c);
        string company=MoneySchemaFixture.Company(c),period=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-11-01',1,datetime('now'))",("@id",period),("@c",company));
        string debit=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=1 AND nature=1 ORDER BY code LIMIT 1")!;
        string credit=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=1 AND nature=2 ORDER BY code LIMIT 1")!;
        return (c,company,period,debit,credit);
    }

    static JournalPosting.Draft Draft(string company,string period,string debit,string credit,Guid? source=null,string purpose="manual",byte runMode=0,bool blocked=false,decimal amount=10m)=>
        new(Guid.Parse(company),Guid.Parse(period),new DateOnly(2026,11,1),new DateOnly(2026,11,15),
            "test_source",source??Guid.NewGuid(),purpose,runMode,blocked,"synthetic",
            [
                new(Guid.Parse(debit),SchemaOrgCostCentre.NoneId,"none",amount,0m,null),
                new(Guid.Parse(credit),SchemaOrgCostCentre.NoneId,"none",0m,amount,null)
            ]);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PostsBalancedJournalAndUpsertsPeriodBalance(bool sqlServer)
    {
        var (c,company,period,debit,credit)=Open(sqlServer);
        using(c)
        {
            var result=JournalPosting.Post(c,null,Draft(company,period,debit,credit));
            Assert.IsFalse(result.AlreadyPosted);
            Assert.AreEqual(1L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_journal WHERE status=2")!));
            Assert.AreEqual(2L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_journal_line")!));
            Assert.AreEqual(2L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_period_balance")!));
            Assert.AreEqual(10m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT debit FROM fin_period_balance WHERE account_id=@a",("@a",debit))!));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void IdempotentRetryReturnsExistingWithoutDoubleBalance(bool sqlServer)
    {
        var (c,company,period,debit,credit)=Open(sqlServer);
        using(c)
        {
            var source=Guid.NewGuid();
            var first=JournalPosting.Post(c,null,Draft(company,period,debit,credit,source));
            var second=JournalPosting.Post(c,null,Draft(company,period,debit,credit,source));
            Assert.IsTrue(second.AlreadyPosted);
            Assert.AreEqual(first.JournalId,second.JournalId);
            Assert.AreEqual(1L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_journal")!));
            Assert.AreEqual(10m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT debit FROM fin_period_balance WHERE account_id=@a",("@a",debit))!));
        }
    }

    [TestMethod]
    public void UnbalancedAndClosedPeriodAndNonLeafAreRejected()
    {
        var (c,company,period,debit,credit)=Open();
        using(c)
        {
            var bad=Draft(company,period,debit,credit) with
            {
                Lines=[
                    new(Guid.Parse(debit),SchemaOrgCostCentre.NoneId,"none",10m,0m,null),
                    new(Guid.Parse(credit),SchemaOrgCostCentre.NoneId,"none",0m,9m,null)
                ]
            };
            Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,bad)).Status);

            MoneySchemaFixture.Exec(c,null,"UPDATE fin_accounting_period SET state=2 WHERE id=@id",("@id",period));
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,Draft(company,period,debit,credit))).Status);
            MoneySchemaFixture.Exec(c,null,"UPDATE fin_accounting_period SET state=1 WHERE id=@id",("@id",period));

            string parent=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=0 ORDER BY code LIMIT 1")!;
            var nonLeaf=Draft(company,period,debit,credit) with
            {
                Lines=[
                    new(Guid.Parse(parent),SchemaOrgCostCentre.NoneId,"none",10m,0m,null),
                    new(Guid.Parse(credit),SchemaOrgCostCentre.NoneId,"none",0m,10m,null)
                ]
            };
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,nonLeaf)).Status);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CorrectionIsReversalOnlyAndPayoutPurposeRespectsBlock(bool sqlServer)
    {
        var (c,company,period,debit,credit)=Open(sqlServer);
        using(c)
        {
            var posted=JournalPosting.Post(c,null,Draft(company,period,debit,credit));
            var reverse=JournalPosting.Reverse(c,null,posted.JournalId,"synthetic",Guid.NewGuid(),"عكس قيد اختبار");
            Assert.IsFalse(reverse.AlreadyPosted);
            Assert.AreEqual(3L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT status FROM fin_journal WHERE id=@id",("@id",SchemaSql.GuidText(posted.JournalId)))!));
            Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT debit-credit FROM fin_period_balance WHERE account_id=@a",("@a",debit))!));

            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,
                Draft(company,period,debit,credit,purpose:"bank_claim",blocked:true))).Status);
        }
    }

    [TestMethod]
    public void AmountsRoundAwayFromZeroToTwoDecimals()
    {
        var (c,company,period,debit,credit)=Open();
        using(c)
        {
            var draft=Draft(company,period,debit,credit,amount:10.005m);
            JournalPosting.Post(c,null,draft);
            Assert.AreEqual(10.01m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT debit FROM fin_journal_line WHERE debit>0")!));
        }
    }
}
