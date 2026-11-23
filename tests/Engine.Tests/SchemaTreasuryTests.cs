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
        // One active master per company+chart account (UQ_treasury_*_account) — pick an unused type-1 account.
        string id=MoneySchemaFixture.Id();
        object? chartObj=MoneySchemaFixture.Scalar(c,null,$"""
            SELECT a.id FROM fin_account a
            WHERE a.is_postable=1 AND a.account_type=1
              AND NOT EXISTS(SELECT 1 FROM treasury_{table} t WHERE t.company_id=@c AND t.account_id=a.id AND t.status=1)
            ORDER BY a.code LIMIT 1
            """,("@c",company));
        Assert.IsNotNull(chartObj,"no free postable type-1 account for treasury master");
        string chart=chartObj switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(chartObj,System.Globalization.CultureInfo.InvariantCulture)!};
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
    static string VaultJournal(DbConnection c,string company,string countId,string box,decimal shortAmount)=>
        VaultJournalLines(c,company,countId,box,(shortAcc,boxAcc,cc,period,journal)=>
        {
            MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_journal_line(id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at) VALUES(@id,@p,@c,'2026-11-01',@j,1,@a,@cc,@amt,0,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@p",period),("@c",company),("@j",journal),("@a",shortAcc),("@cc",cc),("@amt",shortAmount));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_journal_line(id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at) VALUES(@id,@p,@c,'2026-11-01',@j,2,@a,@cc,0,@amt,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@p",period),("@c",company),("@j",journal),("@a",boxAcc),("@cc",cc),("@amt",shortAmount));
        });
    static string VaultJournalLines(DbConnection c,string company,string countId,string box,Action<string,string,string,string,string> lines)
    {
        string period=MoneySchemaFixture.Id(),journal=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) SELECT @id,@c,'2026-11-01',1,datetime('now') WHERE NOT EXISTS(SELECT 1 FROM fin_accounting_period WHERE company_id=@c AND period_month='2026-11-01')",("@id",period),("@c",company));
        period=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_accounting_period WHERE company_id=@c AND period_month='2026-11-01'",("@c",company))!;
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_journal(id,voucher_no,period_id,company_id,period_month,journal_date,source_type,source_id,purpose,status,created_at) VALUES(@id,@id,@p,@c,'2026-11-01','2026-11-20','vault_count',@s,'vault_over_short',1,datetime('now'))",("@id",journal),("@p",period),("@c",company),("@s",countId));
        string shortAcc=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE code='542102'")!;
        string boxAcc=(string)MoneySchemaFixture.Scalar(c,null,"SELECT account_id FROM treasury_cash_box WHERE id=@id",("@id",box))!;
        string cc=SchemaSql.GuidText(SchemaOrgCostCentre.NoneId);
        lines(shortAcc,boxAcc,cc,period,journal);
        MoneySchemaFixture.Exec(c,null,"UPDATE fin_journal SET status=2,posted_at=datetime('now'),posted_by='tester' WHERE id=@id",("@id",journal));
        return journal;
    }
    static void InsertVaultLine(DbConnection c,string company,string period,string journal,int lineNo,string account,string cc,decimal debit,decimal credit)=>
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_journal_line(id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at) VALUES(@id,@p,@c,'2026-11-01',@j,@n,@a,@cc,@d,@cr,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@p",period),("@c",company),("@j",journal),("@n",lineNo),("@a",account),("@cc",cc),("@d",debit),("@cr",credit));
    [TestMethod,DataRow(false),DataRow(true)]
    public void VaultVarianceJournalMustMatchCountBoxAndAmountAndCannotBeReused(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c),registration=Registration(c,company),box=Account(c,company,registration,"cash_box"),id=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO treasury_vault_count(id,cash_box_id,company_id,registration_id,period_month,counted_on,book_balance,counted_amount) VALUES(@id,@b,@c,@r,'2026-11-01','2026-11-20',100,99)",("@id",id),("@b",box),("@c",company),("@r",registration));
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET status=2 WHERE id=@id",("@id",id));
        string wrong=VaultJournal(c,company,MoneySchemaFixture.Id(),box,1),right=VaultJournal(c,company,id,box,1);
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET journal_id=@j WHERE id=@id",("@id",id),("@j",wrong));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET status=3,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",id),("@u",MoneySchemaFixture.Id())),"TR_vault_count_journal");
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET journal_id=@j,status=3,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",id),("@j",right),("@u",MoneySchemaFixture.Id()));
        string other=MoneySchemaFixture.Id(),otherBox=Account(c,company,registration,"cash_box");
        MoneySchemaFixture.Exec(c,null,"INSERT INTO treasury_vault_count(id,cash_box_id,company_id,registration_id,period_month,counted_on,book_balance,counted_amount) VALUES(@id,@b,@c,@r,'2026-11-01','2026-11-21',50,49)",("@id",other),("@b",otherBox),("@c",company),("@r",registration));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET journal_id=@j WHERE id=@id",("@id",other),("@j",right)),"UQ_treasury_vault_journal",sqliteToken:"journal_id");
    }
    [TestMethod,DataRow(false),DataRow(true)]
    public void VaultVarianceJournalMustUseNetAccountTotalsAndOnlyBoxAndVarianceAccounts(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c),registration=Registration(c,company),box=Account(c,company,registration,"cash_box");
        // Fixed non-cash CoA row so later cash_box masters (unique per account) cannot steal this id.
        string otherAcc=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE code='212103'")!;
        Assert.IsNotNull(otherAcc,"missing CoA 212103 for foreign-line rejection case");
        string zeroNetCount=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO treasury_vault_count(id,cash_box_id,company_id,registration_id,period_month,counted_on,book_balance,counted_amount) VALUES(@id,@b,@c,@r,'2026-11-01','2026-11-20',100,99)",("@id",zeroNetCount),("@b",box),("@c",company),("@r",registration));
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET status=2 WHERE id=@id",("@id",zeroNetCount));
        string zeroNet=VaultJournalLines(c,company,zeroNetCount,box,(shortAcc,boxAcc,cc,period,journal)=>
        {
            InsertVaultLine(c,company,period,journal,1,boxAcc,cc,1,0);
            InsertVaultLine(c,company,period,journal,2,boxAcc,cc,0,1);
            InsertVaultLine(c,company,period,journal,3,shortAcc,cc,1,0);
            InsertVaultLine(c,company,period,journal,4,otherAcc,cc,0,1);
        });
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET journal_id=@j WHERE id=@id",("@id",zeroNetCount),("@j",zeroNet));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET status=3,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",zeroNetCount),("@u",MoneySchemaFixture.Id())),"TR_vault_count_journal");

        string thirdCount=MoneySchemaFixture.Id(),thirdBox=Account(c,company,registration,"cash_box");
        MoneySchemaFixture.Exec(c,null,"INSERT INTO treasury_vault_count(id,cash_box_id,company_id,registration_id,period_month,counted_on,book_balance,counted_amount) VALUES(@id,@b,@c,@r,'2026-11-01','2026-11-21',100,99)",("@id",thirdCount),("@b",thirdBox),("@c",company),("@r",registration));
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET status=2 WHERE id=@id",("@id",thirdCount));
        string third=VaultJournalLines(c,company,thirdCount,thirdBox,(shortAcc,boxAcc,cc,period,journal)=>
        {
            InsertVaultLine(c,company,period,journal,1,shortAcc,cc,1,0);
            InsertVaultLine(c,company,period,journal,2,boxAcc,cc,0,1);
            InsertVaultLine(c,company,period,journal,3,otherAcc,cc,1,0);
            InsertVaultLine(c,company,period,journal,4,otherAcc,cc,0,1);
        });
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET journal_id=@j WHERE id=@id",("@id",thirdCount),("@j",third));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET status=3,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",thirdCount),("@u",MoneySchemaFixture.Id())),"TR_vault_count_journal");

        string splitCount=MoneySchemaFixture.Id(),splitBox=Account(c,company,registration,"cash_box");
        MoneySchemaFixture.Exec(c,null,"INSERT INTO treasury_vault_count(id,cash_box_id,company_id,registration_id,period_month,counted_on,book_balance,counted_amount) VALUES(@id,@b,@c,@r,'2026-11-01','2026-11-22',100,99)",("@id",splitCount),("@b",splitBox),("@c",company),("@r",registration));
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET status=2 WHERE id=@id",("@id",splitCount));
        string split=VaultJournalLines(c,company,splitCount,splitBox,(shortAcc,boxAcc,cc,period,journal)=>
        {
            InsertVaultLine(c,company,period,journal,1,shortAcc,cc,0.40m,0);
            InsertVaultLine(c,company,period,journal,2,shortAcc,cc,0.60m,0);
            InsertVaultLine(c,company,period,journal,3,boxAcc,cc,0,0.25m);
            InsertVaultLine(c,company,period,journal,4,boxAcc,cc,0,0.75m);
        });
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET journal_id=@j,status=3,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",splitCount),("@j",split),("@u",MoneySchemaFixture.Id()));

        string plainCount=MoneySchemaFixture.Id(),plainBox=Account(c,company,registration,"cash_box");
        MoneySchemaFixture.Exec(c,null,"INSERT INTO treasury_vault_count(id,cash_box_id,company_id,registration_id,period_month,counted_on,book_balance,counted_amount) VALUES(@id,@b,@c,@r,'2026-11-01','2026-11-23',100,99)",("@id",plainCount),("@b",plainBox),("@c",company),("@r",registration));
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET status=2 WHERE id=@id",("@id",plainCount));
        string plain=VaultJournal(c,company,plainCount,plainBox,1);
        MoneySchemaFixture.Exec(c,null,"UPDATE treasury_vault_count SET journal_id=@j,status=3,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",plainCount),("@j",plain),("@u",MoneySchemaFixture.Id()));
    }
}
