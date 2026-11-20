using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaReviewAmountFreezeTests
{
    [TestMethod,DataRow(false),DataRow(true)]
    public void ApprovedRiderAmountsItemsAndPayableDueAreFrozen(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);SchemaSalPayRun.Ensure(c);SchemaFinAccount.Ensure(c);
        string company=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company),due=MoneySchemaFixture.Payable(c,company,100);
        string rider=MoneySchemaFixture.Rider(c,run,company,due,100);
        string account=(string)MoneySchemaFixture.Scalar(c,null,"SELECT code FROM fin_account WHERE is_postable=1 AND is_active=1 ORDER BY code LIMIT 1")!;
        string item=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_pay_run_item(id,period_month,pay_run_rider_id,line_no,stage_code,item_code,amount,affects_net,account_code) VALUES(@id,'2026-11-01',@r,1,'net','salary_net',100,1,@a)",("@id",item),("@r",rider),("@a",account));
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run_rider SET debt_confirm_status='confirmed',salary_approved_at=datetime('now') WHERE id=@id",("@id",rider));
        foreach(string set in new[]{"salary_net=99","gross=90","commission=5","old_debt_instalment=1"})
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run_rider SET "+set+" WHERE id=@id",("@id",rider)),"TR_rider_amounts_frozen");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run_item SET amount=90 WHERE id=@id",("@id",item)),"TR_item_amounts_frozen");
        string extra=MoneySchemaFixture.Id();
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_pay_run_item(id,period_month,pay_run_rider_id,line_no,stage_code,item_code,amount,affects_net,account_code) VALUES(@id,'2026-11-01',@r,2,'adjustment','adj',1,1,@a)",("@id",extra),("@r",rider),("@a",account)),"TR_item_amounts_frozen");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payable SET amount_due=90 WHERE id=@id",("@id",due)),"TR_payable_due_frozen");
    }
}
