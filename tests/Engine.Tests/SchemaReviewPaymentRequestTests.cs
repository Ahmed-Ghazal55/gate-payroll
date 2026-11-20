using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaReviewPaymentRequestTests
{
    [TestMethod,DataRow(false),DataRow(true)]
    public void PaymentMustMatchLinkedRequestFlags(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);
        string company=MoneySchemaFixture.Company(c),payable=MoneySchemaFixture.Payable(c,company),request=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO pay_payment_request(id,company_id,payable_id,payable_period_month,period_month,amount,run_mode,payout_blocked) VALUES(@id,@c,@p,'2026-11-01','2026-11-01',100,1,1)",("@id",request),("@c",company),("@p",payable));
        string payment=MoneySchemaFixture.Id();
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO pay_payment(id,payment_no,company_id,payer_company_id,request_id,request_period_month,period_month,paid_on,amount,run_mode,payout_blocked) VALUES(@id,@id,@c,@c,@r,'2026-11-01','2026-11-01','2026-11-01',100,0,0)",("@id",payment),("@c",company),("@r",request)));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO pay_payment(id,payment_no,company_id,payer_company_id,request_id,request_period_month,period_month,paid_on,amount,run_mode,payout_blocked) VALUES(@id,@id,@c,@c,@r,'2026-11-01','2026-11-01','2026-11-01',100,1,1)",("@id",payment),("@c",company),("@r",request));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET run_mode=0 WHERE id=@id",("@id",payment)));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET payout_blocked=0 WHERE id=@id",("@id",payment)));
    }
}