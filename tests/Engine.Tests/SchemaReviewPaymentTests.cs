using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaReviewPaymentTests
{
    [TestMethod, DataRow(false), DataRow(true)]
    public void PaymentCannotReopenCancelPostedClaimsOrChangePaidIdentity(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);
        string company=MoneySchemaFixture.Company(c),other=MoneySchemaFixture.Company(c);
        string p=MoneySchemaFixture.Payment(c,company),due=MoneySchemaFixture.Payable(c,company);
        string claim=MoneySchemaFixture.Claim(c,company,p,due,status:2);
        void Change(string set)=>MoneySchemaFixture.Exec(c,null,$"UPDATE pay_payment SET {set} WHERE id=@id",("@id",p));
        MoneySchemaFixture.Reject(()=>Change("status=4"),"Payout blocked or cancellation/reversal lacks evidence");
        MoneySchemaFixture.PreparePaymentReversal(c,p);
        foreach(string set in new[]{"status=1","status=4",$"id='{MoneySchemaFixture.Id()}'",$"company_id='{other}'",$"payer_company_id='{other}'","paid_on='2026-11-02'","amount=99"})
            MoneySchemaFixture.Reject(()=>Change(set),"Invalid payment transition or frozen identity/flags");
        Change("status=3");
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=4 WHERE id=@id",("@id",claim));
        foreach(int status in new[]{1,2,4})MoneySchemaFixture.Reject(()=>Change($"status={status}"),"Invalid payment transition or frozen identity/flags");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Claim(c,company,p,due,status:2),"Reversed or cancelled payment cannot receive claims");
        string cancelled=MoneySchemaFixture.Payment(c,company);
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET status=4 WHERE id=@id",("@id",cancelled));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET status=1 WHERE id=@id",("@id",cancelled)),"Invalid payment transition or frozen identity/flags");
    }

    [TestMethod, DataRow(false), DataRow(true)]
    public void AStatusFlipWithoutAReversalJournalCannotReleasePaidMoney(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);string company=MoneySchemaFixture.Company(c);
        string p=MoneySchemaFixture.Payment(c,company),due=MoneySchemaFixture.Payable(c,company);
        MoneySchemaFixture.Claim(c,company,p,due,status:2);
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET status=2 WHERE id=@id",("@id",p));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET status=3 WHERE id=@id",("@id",p)),"Payout blocked or cancellation/reversal lacks evidence");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET journal_id=@id WHERE id=@p",("@id",MoneySchemaFixture.Id()),("@p",p)),"Invalid payment transition or frozen identity/flags");
    }

    [TestMethod, DataRow(false), DataRow(true)]
    public void BlockedOrSimulationPaymentsCannotBecomePaidOrLoseTheirFlags(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);string company=MoneySchemaFixture.Company(c);
        foreach(string flags in new[]{"payout_blocked=1","run_mode=1"})
        {
            string p=MoneySchemaFixture.Payment(c,company);
            MoneySchemaFixture.Exec(c,null,$"UPDATE pay_payment SET {flags} WHERE id=@id",("@id",p));
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET status=2 WHERE id=@id",("@id",p)),"Payout blocked or cancellation/reversal lacks evidence");
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET payout_blocked=0,run_mode=0,status=2 WHERE id=@id",("@id",p)),"Invalid payment transition or frozen identity/flags");
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO pay_payment(id,payment_no,company_id,payer_company_id,period_month,paid_on,amount,status,run_mode,payout_blocked) VALUES(@id,@id,@c,@c,'2026-11-01','2026-11-01',100,2,1,1)",("@id",MoneySchemaFixture.Id()),("@c",company)),"Payout blocked or cancellation/reversal lacks evidence");
        }
    }
}
