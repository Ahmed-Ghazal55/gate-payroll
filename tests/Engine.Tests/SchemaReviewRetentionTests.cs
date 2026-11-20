using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaReviewRetentionTests
{
    [TestMethod, DataRow(false), DataRow(true)]
    public void FrozenParentsCannotRenameDeleteOrReplaceByAnyUniqueKey(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);SchemaSalInvoice.Ensure(c);SchemaSalPayRun.Ensure(c);
        string company=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company),payment=MoneySchemaFixture.Payment(c,company),invoice=MoneySchemaFixture.Invoice(c,company);
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET capture_state=1,calc_started_at=datetime('now'),payout_blocked=1 WHERE id=@id",("@id",run));
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET status=2 WHERE id=@id",("@id",payment));
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice SET status=2,approved_at=datetime('now'),payout_blocked=1 WHERE id=@id",("@id",invoice));
        foreach(var (table,id,columns) in new[]{
            ("sal_pay_run",run,"id,company_id,app_code,period_month"),
            ("pay_payment",payment,"id,payment_no,company_id,payer_company_id,period_month,paid_on,amount"),
            ("sal_platform_invoice",invoice,"id,app_code,company_id,erp_cr_id,city_code,period_from,period_to,period_month,file_hash,gross,deductions,net,posting_owner")})
        {
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,$"UPDATE {table} SET id=@new WHERE id=@id",("@new",MoneySchemaFixture.Id()),("@id",id)));
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,$"DELETE FROM {table} WHERE id=@id",("@id",id)));
            if(!sqlServer)
            foreach(bool sameId in new[]{true,false})
            {
                // Replace only the leading id expression; alternate business keys stay equal.
                string selected=sameId?columns:"@new"+columns[2..];
                MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,$"INSERT OR REPLACE INTO {table}({columns}) SELECT {selected} FROM {table} WHERE id=@id",("@id",id),("@new",MoneySchemaFixture.Id())));
            }
        }
    }
}
