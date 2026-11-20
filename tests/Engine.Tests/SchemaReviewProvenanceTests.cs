using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaReviewProvenanceTests
{
    [TestMethod, DataRow(false), DataRow(true)]
    public void BlockedRiderCannotDetachDeleteMoveOrAttachAnAlreadyPaidPayable(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);SchemaSalPayRun.Ensure(c);
        string company=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company),due=MoneySchemaFixture.Payable(c,company);
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET payout_blocked=1 WHERE id=@id",("@id",run));
        string rider=MoneySchemaFixture.Rider(c,run,company,due,100);
        foreach(string set in new[]{"payable_id=NULL",$"pay_run_id='{MoneySchemaFixture.Run(c,company,"other")}'",$"id='{MoneySchemaFixture.Id()}'"})
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,$"UPDATE sal_pay_run_rider SET {set} WHERE id=@id",("@id",rider)));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"DELETE FROM sal_pay_run_rider WHERE id=@id",("@id",rider)));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Claim(c,company,MoneySchemaFixture.Payment(c,company),due,status:2));
        string paid=MoneySchemaFixture.Payable(c,company);MoneySchemaFixture.Claim(c,company,MoneySchemaFixture.Payment(c,company),paid,status:2);
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Rider(c,run,company,paid,100));
    }

    [TestMethod, DataRow(false), DataRow(true)]
    public void SourceClaimsAndOrphanSourcesCannotPayBlockedOrSimulationRuns(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);SchemaSalPayRun.Ensure(c);
        string company=MoneySchemaFixture.Company(c);
        foreach(int mode in new[]{0,1})
        {
            string run=MoneySchemaFixture.Run(c,company,"source"+mode);
            MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET payout_blocked=1,run_mode=@mode WHERE id=@id",("@id",run),("@mode",mode));
            string rider=MoneySchemaFixture.Rider(c,run,company,mode:mode);
            string due=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO pay_payable(id,kind,source_table,source_id,company_id,amount_due,period_month) VALUES(@id,'pay_run_rider','sal.pay_run_rider',@r,@c,100,'2026-11-01')",("@id",due),("@r",rider),("@c",company));
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Claim(c,company,MoneySchemaFixture.Payment(c,company),due,status:2));
            if(mode==1)MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Claim(c,company,MoneySchemaFixture.Payment(c,company),due));
            foreach(string set in new[]{"source_table='other'",$"source_id='{MoneySchemaFixture.Id()}'","kind='other'"})
                MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,$"UPDATE pay_payable SET {set} WHERE id=@id",("@id",due)));
            string spoof=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO pay_payable(id,kind,source_table,source_id,company_id,amount_due,period_month) VALUES(@id,'document_line','spoofed.source',@r,@c,100,'2026-11-01')",("@id",spoof),("@r",rider),("@c",company));
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Claim(c,company,MoneySchemaFixture.Payment(c,company),spoof,status:2));
            string orphan=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO pay_payable(id,kind,source_table,source_id,company_id,amount_due,period_month) VALUES(@id,'pay_run_rider','sal.pay_run_rider',@id,@c,100,'2026-11-01')",("@id",orphan),("@c",company));
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Claim(c,company,MoneySchemaFixture.Payment(c,company),orphan,status:2));
        }
    }
}
