using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaReviewFreezeTests
{
    [TestMethod, DataRow(false), DataRow(true)]
    public void ApprovedInvoiceCannotClearApprovalReopenOrChangeEvidence(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);SchemaSalInvoice.Ensure(c);SchemaSalPayRun.Ensure(c);SchemaOperationCapture.Ensure(c);
        string company=MoneySchemaFixture.Company(c),invoice=MoneySchemaFixture.Invoice(c,company);
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice SET status=2,approved_at=datetime('now'),payout_blocked=1 WHERE id=@id",("@id",invoice));
        foreach(string set in new[]{"approved_at=NULL,status=1","status=1","approved_at=NULL","approved_at='2026-11-02'"})
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,$"UPDATE sal_platform_invoice SET {set} WHERE id=@id",("@id",invoice)));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_operation_capture_manifest(id,operation_type,operation_id,required_kind,status,empty_reason,period_month) VALUES(@id,'platform_invoice',@invoice,'invoice_file',2,'late edit','2026-11-01')",("@id",MoneySchemaFixture.Id()),("@invoice",invoice)));
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice SET status=3 WHERE id=@id",("@id",invoice));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice SET status=2 WHERE id=@id",("@id",invoice)));
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice SET status=5 WHERE id=@id",("@id",invoice));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice SET status=4 WHERE id=@id",("@id",invoice)));
    }

    [TestMethod, DataRow(false), DataRow(true)]
    public void ApprovedRunRequiresCaptureAndFinalStatesCannotReopen(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);SchemaSalPayRun.Ensure(c);
        string company=MoneySchemaFixture.Company(c);
        foreach(int status in new[]{2,3,5})
        {
            string run=MoneySchemaFixture.Run(c,company,"app"+status);
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,$"UPDATE sal_pay_run SET status={status},payout_blocked=1 WHERE id=@id",("@id",run)));
        }
        string frozen=MoneySchemaFixture.Run(c,company);
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET status=3,capture_state=1,calc_started_at=datetime('now') WHERE id=@id",("@id",frozen));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET status=1 WHERE id=@id",("@id",frozen)));
        string cancelled=MoneySchemaFixture.Run(c,company,"cancelled");
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET status=4 WHERE id=@id",("@id",cancelled));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET status=1 WHERE id=@id",("@id",cancelled)));
    }
}
