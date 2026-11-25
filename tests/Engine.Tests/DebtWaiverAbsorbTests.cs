using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>U-W2-20c: waiver D11 + absorb 526602/526603 + maker≠checker.</summary>
[TestClass]
public sealed class DebtWaiverAbsorbTests
{
    [TestMethod,DataRow(false),DataRow(true)]
    public void WaiverAndAbsorbPostExpenseAccountsWithMakerChecker(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',50,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            Guid receivable=DebtLedgerFixture.Acc(c,"112501"),waiveExp=DebtLedgerFixture.Acc(c,"526601"),absorbExp=DebtLedgerFixture.Acc(c,"526602");
            using(var tx=c.BeginTransaction())
            {
                DebtLedger.Waive(c,tx,Guid.Parse(debt),companyG,periodG,new DateOnly(2026,11,1),DebtLedgerFixture.Admin,DebtLedgerFixture.Checker,15m,"إعفاء عام معتمد","526601",receivable,waiveExp);
                tx.Commit();
            }
            Assert.AreEqual(35m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM debt_waiver WHERE expense_account_code='526601' AND status=3"));

            using(var tx=c.BeginTransaction())
            {
                DebtLedger.Absorb(c,tx,Guid.Parse(debt),companyG,periodG,new DateOnly(2026,11,1),DebtLedgerFixture.Admin,DebtLedgerFixture.Checker,10m,"تحمل الشركة","526602",receivable,absorbExp);
                tx.Commit();
            }
            Assert.AreEqual(25m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM fin_journal WHERE purpose='debt_absorb' AND status=2"));

            using var bad=c.BeginTransaction();
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>
                DebtLedger.Waive(c,bad,Guid.Parse(debt),companyG,periodG,new DateOnly(2026,11,1),DebtLedgerFixture.Admin,DebtLedgerFixture.Admin,5m,"نفس الشخص","526601",receivable,waiveExp)).Status);
        }
    }
}
