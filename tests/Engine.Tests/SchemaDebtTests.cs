using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaDebtTests
{
    internal static DbConnection Open(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);SchemaSalPayRun.Ensure(c);SchemaDebt.Ensure(c);return c;
    }
    internal static string Debt(DbConnection c,string company,decimal balance=0)
    {
        string id=MoneySchemaFixture.Id();MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt(id,erp_rider_id,company_id,category,principal,balance,instalment) VALUES(@id,@id,@c,'synthetic',100,@b,20)",("@id",id),("@c",company),("@b",balance));return id;
    }
    internal static string Move(DbConnection c,string company,string debt,decimal amount,string? source=null)
    {
        string id=MoneySchemaFixture.Id();MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-20',@a,'synthetic',@s)",("@id",id),("@d",debt),("@c",company),("@a",amount),("@s",source??id));return id;
    }
    [TestMethod,DataRow(false),DataRow(true)]
    public void LedgerOwnsBalanceNoDoubleApplyNoOverRecoveryAndVersionChanges(bool sqlServer)
    {
        using var c=Open(sqlServer);SchemaDebt.Ensure(c);
        string company=MoneySchemaFixture.Company(c),debt=Debt(c,company);
        MoneySchemaFixture.Reject(()=>Debt(c,company,12));
        byte[] version=(byte[])MoneySchemaFixture.Scalar(c,null,"SELECT row_version FROM debt_debt WHERE id=@id",("@id",debt))!;
        string first=Move(c,company,debt,100);
        Assert.AreEqual(100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
        CollectionAssert.AreNotEqual(version,(byte[])MoneySchemaFixture.Scalar(c,null,"SELECT row_version FROM debt_debt WHERE id=@id",("@id",debt))!);
        MoneySchemaFixture.Reject(()=>Move(c,company,debt,100,first));
        MoneySchemaFixture.Reject(()=>Move(c,company,debt,-101));
        Move(c,company,debt,-20);
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET balance=0 WHERE id=@id",("@id",debt)));
        foreach(string sql in new[]{"UPDATE debt_debt_movement SET amount=50 WHERE id=@id","DELETE FROM debt_debt_movement WHERE id=@id"})
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,sql,("@id",first)));
        Assert.AreEqual(80m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM ref_business_key_guard WHERE row_id=@id",("@id",first)));
        MoneySchemaFixture.Reject(()=>Move(c,MoneySchemaFixture.Company(c),debt,5));
        MoneySchemaFixture.Reject(()=>Move(c,company,debt,0));
    }
    [TestMethod,DataRow(false),DataRow(true)]
    public void DocumentsFreezeAtApprovalAndDeferredInstalmentCannotMove(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c),debt=Debt(c,company),waiver=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_waiver(id,company_id,period_month,debt_id,amount,reason,expense_account_code) VALUES(@id,@c,'2026-11-01',@d,20,'Synthetic reason','526601')",("@id",waiver),("@c",company),("@d",debt));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE debt_waiver SET status=3 WHERE id=@id",("@id",waiver)));
        MoneySchemaFixture.Exec(c,null,"UPDATE debt_waiver SET status=2 WHERE id=@id",("@id",waiver));
        MoneySchemaFixture.Exec(c,null,"UPDATE debt_waiver SET status=3,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",waiver),("@u",MoneySchemaFixture.Id()));
        foreach(string set in new[]{"amount=21","status=1","run_mode=1","payout_blocked=1"})
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,$"UPDATE debt_waiver SET {set} WHERE id=@id",("@id",waiver)));
        string run=MoneySchemaFixture.Run(c,company),rider=MoneySchemaFixture.Rider(c,run,company,net:100);
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run_rider SET debt_confirm_status='deferred' WHERE id=@id",("@id",rider));
        Move(c,company,debt,100);
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id,pay_run_rider_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-20',-10,'salary_deduction',@id,@r)",("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company),("@r",rider)));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM debt_debt_movement"));
    }
}
