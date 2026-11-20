using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaReviewJournalTests
{
    [TestMethod,DataRow(false),DataRow(true)]
    public void PostedJournalAndLinesCannotBeEditedOrDeleted(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        string period=MoneySchemaFixture.Id();
        string journal=MoneySchemaFixture.Id();
        string line=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,
            "INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-11-01',1,datetime('now'))",
            ("@id",period),("@c",company));
        MoneySchemaFixture.Exec(c,null,
            "INSERT INTO fin_journal(id,voucher_no,period_id,period_month,journal_date,source_type,source_id,company_id,purpose,status,created_at) VALUES(@id,@id,@p,'2026-11-01','2026-11-20','synthetic',@id,@c,'manual',1,datetime('now'))",
            ("@id",journal),("@p",period),("@c",company));
        string account=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=1 AND is_active=1 ORDER BY code LIMIT 1")!;
        string cc=SchemaSql.GuidText(SchemaOrgCostCentre.NoneId);
        MoneySchemaFixture.Exec(c,null,
            "INSERT INTO fin_journal_line(id,period_id,period_month,journal_id,line_no,account_id,is_postable,company_id,cost_centre_id,debit,credit,created_at) VALUES(@id,@p,'2026-11-01',@j,1,@a,1,@c,@cc,10,0,datetime('now'))",
            ("@id",line),("@p",period),("@j",journal),("@a",account),("@c",company),("@cc",cc));
        MoneySchemaFixture.Exec(c,null,"UPDATE fin_journal SET narration_ar='draft edit' WHERE id=@id",("@id",journal));
        MoneySchemaFixture.Exec(c,null,"UPDATE fin_journal SET status=2,posted_at=datetime('now'),posted_by='tester' WHERE id=@id",("@id",journal));
        foreach(string set in new[]{"voucher_no='hacked'","narration_ar='posted edit'","purpose='other'","status=1","run_mode=1","payout_blocked=1"})
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE fin_journal SET "+set+" WHERE id=@id",("@id",journal)));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"DELETE FROM fin_journal WHERE id=@id",("@id",journal)));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE fin_journal_line SET debit=11,credit=0 WHERE id=@id",("@id",line)));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"DELETE FROM fin_journal_line WHERE id=@id",("@id",line)));
        string extra=MoneySchemaFixture.Id();
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,
            "INSERT INTO fin_journal_line(id,period_id,period_month,journal_id,line_no,account_id,is_postable,company_id,cost_centre_id,debit,credit,created_at) VALUES(@id,@p,'2026-11-01',@j,2,@a,1,@c,@cc,0,10,datetime('now'))",
            ("@id",extra),("@p",period),("@j",journal),("@a",account),("@c",company),("@cc",cc)));
        MoneySchemaFixture.Exec(c,null,"UPDATE fin_journal SET status=3 WHERE id=@id",("@id",journal));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE fin_journal SET status=2 WHERE id=@id",("@id",journal)));
    }
}
