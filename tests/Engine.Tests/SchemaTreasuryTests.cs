using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaTreasuryTests
{
    internal static DbConnection Open(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);SchemaTreasury.Ensure(c);return c;
    }
    internal static string Registration(DbConnection c,string company)
    {
        string id=MoneySchemaFixture.Id();MoneySchemaFixture.Exec(c,null,"INSERT INTO hr_commercial_registration(id,company_id,cr_number,status,created_at) VALUES(@id,@c,@code,1,datetime('now'))",("@id",id),("@c",company),("@code",id.Replace("-","")[..32]));return id;
    }
    internal static string Account(DbConnection c,string company,string registration,string table="bank_account")
    {
        string id=MoneySchemaFixture.Id(),chart=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=1 AND account_type=1")!;
        MoneySchemaFixture.Exec(c,null,$"INSERT INTO treasury_{table}(id,company_id,registration_id,code,name_ar,account_id) VALUES(@id,@c,@r,@id,'Synthetic treasury account',@a)",("@id",id),("@c",company),("@r",registration),("@a",chart));return id;
    }
    internal static string Statement(DbConnection c,string company,string registration,string bank)
    {
        string id=MoneySchemaFixture.Id();MoneySchemaFixture.Exec(c,null,"INSERT INTO treasury_bank_statement(id,company_id,registration_id,bank_account_id,period_month,file_hash,opening_balance,closing_balance) VALUES(@id,@c,@r,@b,'2026-11-01',randomblob(32),0,100)",("@id",id),("@c",company),("@r",registration),("@b",bank));return id;
    }
    internal static string Line(DbConnection c,string company,string statement,int number=1,decimal amount=100)
    {
        string id=MoneySchemaFixture.Id();MoneySchemaFixture.Exec(c,null,"INSERT INTO treasury_bank_statement_line(id,statement_id,company_id,period_month,line_no,occurred_on,amount) VALUES(@id,@s,@c,'2026-11-01',@n,'2026-11-20',@a)",("@id",id),("@s",statement),("@c",company),("@n",number),("@a",amount));return id;
    }
    internal static string JournalLine(DbConnection c,string company,string bank,decimal amount=100)
    {
        string period=MoneySchemaFixture.Id(),journal=MoneySchemaFixture.Id(),line=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) SELECT @id,@c,'2026-11-01',1,datetime('now') WHERE NOT EXISTS(SELECT 1 FROM fin_accounting_period WHERE company_id=@c AND period_month='2026-11-01')",("@id",period),("@c",company));
        period=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_accounting_period WHERE company_id=@c AND period_month='2026-11-01'",("@c",company))!;
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_journal(id,voucher_no,period_id,company_id,period_month,journal_date,source_type,source_id,purpose,status,created_at) VALUES(@id,@id,@p,@c,'2026-11-01','2026-11-20','synthetic',@id,'manual',1,datetime('now'))",("@id",journal),("@p",period),("@c",company));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_journal_line(id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at) SELECT @id,@p,@c,'2026-11-01',@j,1,b.account_id,@cc,@a,0,datetime('now') FROM treasury_bank_account b WHERE b.id=@b",("@id",line),("@p",period),("@c",company),("@j",journal),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)),("@a",amount),("@b",bank));
        MoneySchemaFixture.Exec(c,null,"UPDATE fin_journal SET status=2,posted_at=datetime('now'),posted_by='tester' WHERE id=@id",("@id",journal));
        return line;
    }
    static string Match(DbConnection c,string company,string statementLine,string journalLine,decimal amount)
    {
        string id=MoneySchemaFixture.Id();MoneySchemaFixture.Exec(c,null,"INSERT INTO treasury_bank_match(id,statement_line_id,journal_line_id,company_id,period_month,amount) VALUES(@id,@s,@j,@c,'2026-11-01',@a)",("@id",id),("@s",statementLine),("@j",journalLine),("@c",company),("@a",amount));return id;
    }
    [TestMethod,DataRow(false),DataRow(true)]
    public void StatementFactsScopeTotalsAndApprovalAreProtected(bool sqlServer)
    {
        using var c=Open(sqlServer);SchemaTreasury.Ensure(c);
        string company=MoneySchemaFixture.Company(c),registration=Registration(c,company),bank=Account(c,company,registration),statement=Statement(c,company,registration,bank),line=Line(c,company,statement);
        MoneySchemaFixture.Reject(()=>Account(c,MoneySchemaFixture.Company(c),registration),"TR_treasury_bank_account_scope");
        MoneySchemaFixture.Reject(()=>Line(c,company,statement),"Money row replacement is forbidden");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE treasury_bank_statement_line SET amount=99 WHERE id=@id",("@id",line)),"TR_treasury_bank_statement_line_immutable");
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_bank_statement SET status=2 WHERE id=@id",("@id",statement));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE treasury_bank_statement SET status=3,closing_balance=99,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",statement),("@u",MoneySchemaFixture.Id())),"TR_statement_total");
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_bank_statement SET status=3,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",statement),("@u",MoneySchemaFixture.Id()));
        MoneySchemaFixture.Reject(()=>Line(c,company,statement,2,1),"TR_statement_line_open");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE treasury_bank_statement SET status=1 WHERE id=@id",("@id",statement)),"TR_treasury_bank_statement_state");
    }
    [TestMethod,DataRow(false),DataRow(true)]
    public void BankMatchingCannotDoubleUseEitherSourceAndCancelledMatchesAreFinal(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c),registration=Registration(c,company),bank=Account(c,company,registration),statement=Statement(c,company,registration,bank),line=Line(c,company,statement),journal=JournalLine(c,company,bank),match=Match(c,company,line,journal,60);
        MoneySchemaFixture.Reject(()=>Match(c,company,line,journal,40),"Money row replacement is forbidden");
        MoneySchemaFixture.Reject(()=>Match(c,company,line,JournalLine(c,company,bank),41),"Bank match exceeds source amount");
        MoneySchemaFixture.Reject(()=>Match(c,company,Line(c,company,statement,2),journal,41),"Bank match exceeds source amount");
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_bank_match SET status=5 WHERE id=@id",("@id",match));
        Match(c,company,line,journal,100);
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE treasury_bank_match SET status=1 WHERE id=@id",("@id",match)),"TR_treasury_match_source");
    }
    [TestMethod,DataRow(false),DataRow(true)]
    public void VaultVarianceNeedsPostedJournalAndApprovedCountsAreImmutable(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c),registration=Registration(c,company),box=Account(c,company,registration,"cash_box"),id=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO treasury_vault_count(id,cash_box_id,company_id,registration_id,period_month,counted_on,book_balance,counted_amount) VALUES(@id,@b,@c,@r,'2026-11-01','2026-11-20',100,99)",("@id",id),("@b",box),("@c",company),("@r",registration));
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET status=2 WHERE id=@id",("@id",id));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET status=3,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",id),("@u",MoneySchemaFixture.Id())),"TR_vault_count_journal");
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET status=3,counted_amount=100,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",id),("@u",MoneySchemaFixture.Id()));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET counted_amount=101 WHERE id=@id",("@id",id)),"TR_vault_count_journal");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"DELETE FROM treasury_vault_count WHERE id=@id",("@id",id)),"Money rows cannot be deleted");
    }
}
