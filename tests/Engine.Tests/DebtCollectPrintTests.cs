using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>U-W2-20b: collect + print + promissory link-debt + disburse stub until U-W2-07.</summary>
[TestClass]
public sealed class DebtCollectPrintTests
{
    [TestMethod,DataRow(false),DataRow(true)]
    public void CollectCreatesMovementAndJournalInOneTransaction(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',80,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            Guid receivable=DebtLedgerFixture.Acc(c,"112501"),cash=DebtLedgerFixture.Acc(c,"111101");
            using var tx=c.BeginTransaction();
            var result=DebtLedger.Collect(c,tx,Guid.Parse(debt),companyG,periodG,new DateOnly(2026,11,1),DebtLedgerFixture.Admin,25m,"تحصيل نقدي",receivable,cash,runMode:0,payoutBlocked:false);
            tx.Commit();
            Assert.AreEqual(55m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM debt_collection WHERE status=3 AND amount=25"));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM debt_debt_movement WHERE source_type='collection' AND amount=-25"));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM fin_journal WHERE purpose='debt_collection' AND status=2"));
            Assert.AreEqual(55m,result.N("balance"));
        }
    }

    [TestMethod,DataRow(false),DataRow(true)]
    public void DisburseEndpointContractIsBlockedUntilPaymentEngine(bool sqlServer)
    {
        var (c,_,_,_,_)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            var err=Assert.ThrowsExactly<ApiError>(()=>DebtLedger.DemandDisburseAvailable());
            Assert.AreEqual(409,err.Status);
            Assert.IsTrue(err.Message.Contains("غير متاح",StringComparison.Ordinal));
        }
    }
}
