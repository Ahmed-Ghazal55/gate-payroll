using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaBankFileTests
{
    static DbConnection Open(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);SchemaSalPayRun.Ensure(c);SchemaTreasury.Ensure(c);SchemaBankFile.Ensure(c);return c;
    }
    static string File(DbConnection c,string payer,string registration,string bank,bool combined=true,int blocked=0)
    {
        string id=MoneySchemaFixture.Id();MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_bank_file(id,period_month,issuance_choice,scope_company_id,paying_company_id,paying_registration_id,paying_bank_id,payout_blocked,created_by) VALUES(@id,'2026-11-01',@choice,@scope,@c,@r,@b,@blocked,@maker)",("@id",id),("@choice",combined?"combined":"per_company"),("@scope",combined?DBNull.Value:payer),("@c",payer),("@r",registration),("@b",bank),("@blocked",blocked),("@maker",id));return id;
    }
    static (string Rider,string Payment,string Claim,string Line) Line(DbConnection c,string file,string company,string registration,string payer,string? run=null,int blocked=0)
    {
        run??=MoneySchemaFixture.Run(c,company);
        string due=MoneySchemaFixture.Payable(c,company),rider=MoneySchemaFixture.Rider(c,run,company,due,100);
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run_rider SET debt_confirm_status='deferred',salary_approved_at=datetime('now') WHERE id=@id",("@id",rider));
        string payment=MoneySchemaFixture.Payment(c,company,payer:payer),line=MoneySchemaFixture.Id();
        if(blocked==1)MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET payout_blocked=1 WHERE id=@id",("@id",payment));
        string claim=MoneySchemaFixture.Claim(c,company,payment,due,100,line:line);
        string bankCode=(string)MoneySchemaFixture.Scalar(c,null,"SELECT short_code FROM ref_saudi_bank WHERE active=1")!;
        MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_bank_file_line(id,bank_file_id,period_month,pay_run_rider_id,payable_id,company_id,registration_id,payable_period_month,payment_id,allocation_id,amount,bank_short_code) VALUES(@id,@f,'2026-11-01',@r,@due,@c,@cr,'2026-11-01',@p,@a,100,@bank)",("@id",line),("@f",file),("@r",rider),("@due",due),("@c",company),("@cr",registration),("@p",payment),("@a",claim),("@bank",bankCode));
        return (rider,payment,claim,line);
    }
    static void Approve(DbConnection c,string file)=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_bank_file SET status=2,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",file),("@u",MoneySchemaFixture.Id()));
    static void Sent(DbConnection c,string file)=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_bank_file SET status=3,sent_by=@u,sent_at=datetime('now') WHERE id=@id",("@id",file),("@u",MoneySchemaFixture.Id()));

    [TestMethod,DataRow(false),DataRow(true)]
    public void ComparisonExportsButCannotPostOrSendAndChoiceFreezes(bool sqlServer)
    {
        using var c=Open(sqlServer);SchemaBankFile.Ensure(c);
        string company=MoneySchemaFixture.Company(c),registration=SchemaTreasuryTests.Registration(c,company),bank=SchemaTreasuryTests.Account(c,company,registration),run=MoneySchemaFixture.Run(c,company);
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET payout_blocked=1 WHERE id=@id",("@id",run));
        string file=File(c,company,registration,bank,blocked:1);var l=Line(c,file,company,registration,company,run,1);
        Assert.AreEqual("لا يُرسل",MoneySchemaFixture.Scalar(c,null,"SELECT payout_label FROM sal_bank_file_export WHERE id=@id",("@id",file)));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_bank_file SET status=2,approved_by=@id,approved_at=datetime('now') WHERE id=@id",("@id",file)),"TR_bank_file_state");
        Approve(c,file);
        foreach(string set in new[]{"payout_blocked=0","issuance_choice='per_company',scope_company_id=paying_company_id","status=1"})
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,$"UPDATE sal_bank_file SET {set} WHERE id=@id",("@id",file)),"TR_bank_file_state");
        MoneySchemaFixture.Reject(()=>Sent(c,file),"TR_bank_file_state");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=2 WHERE id=@id",("@id",l.Claim)),"TR_bank_claim_state");
    }
    [TestMethod,DataRow(false),DataRow(true)]
    public void LiveFileLinksExactClaimAndNeedsConsumedPaymentBeforeSend(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c),registration=SchemaTreasuryTests.Registration(c,company),bank=SchemaTreasuryTests.Account(c,company,registration),file=File(c,company,registration,bank,false);
        var l=Line(c,file,company,registration,company);
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_bank_file_line SET amount=99 WHERE id=@id",("@id",l.Line)),"TR_sal_bank_file_line_immutable");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=2 WHERE id=@id",("@id",l.Claim)),"TR_bank_claim_state");
        Approve(c,file);
        MoneySchemaFixture.Reject(()=>Sent(c,file),"TR_bank_file_state");
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET status=2 WHERE id=@id",("@id",l.Payment));
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=2 WHERE id=@id",("@id",l.Claim));
        Sent(c,file);
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_bank_file SET status=2 WHERE id=@id",("@id",file)),"TR_bank_file_state");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=3 WHERE id=@id",("@id",l.Claim)),"Invalid allocation status transition; posted claims require reversed payment");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"DELETE FROM sal_bank_file_line WHERE id=@id",("@id",l.Line)),"Money rows cannot be deleted");
    }
    [TestMethod,DataRow(false),DataRow(true)]
    public void BlockedFileCannotBecomeSentEvenAfterClaimsArePosted(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c),registration=SchemaTreasuryTests.Registration(c,company),bank=SchemaTreasuryTests.Account(c,company,registration),file=File(c,company,registration,bank,false);
        var l=Line(c,file,company,registration,company);
        Approve(c,file);
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET status=2 WHERE id=@id",("@id",l.Payment));
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=2 WHERE id=@id",("@id",l.Claim));
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_bank_file SET payout_blocked=1 WHERE id=@id",("@id",file));
        MoneySchemaFixture.Reject(()=>Sent(c,file),"TR_bank_file_state");
    }
    [TestMethod,DataRow(false),DataRow(true)]
    public void CombinedIssuanceStoresCrossCompanySettlementAndPerCompanyRejectsOtherCompany(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string payer=MoneySchemaFixture.Company(c),registration=SchemaTreasuryTests.Registration(c,payer),bank=SchemaTreasuryTests.Account(c,payer,registration),other=MoneySchemaFixture.Company(c),otherRegistration=SchemaTreasuryTests.Registration(c,other);
        string file=File(c,payer,registration,bank);var l=Line(c,file,other,otherRegistration,payer);
        string settlement=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_inter_company_settlement(id,bank_file_line_id,period_month,paying_company_id,entitlement_company_id,amount) VALUES(@id,@l,'2026-11-01',@p,@c,100)",("@id",settlement),("@l",l.Line),("@p",payer),("@c",other));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE fin_inter_company_settlement SET status=2 WHERE id=@id",("@id",settlement)),"TR_inter_company_source");
        string separate=File(c,payer,registration,bank,false);
        MoneySchemaFixture.Reject(()=>Line(c,separate,other,otherRegistration,payer,MoneySchemaFixture.Run(c,other,"other")),"Bank line is not eligible or allocation does not match");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_bank_file_line SET allocation_id=@a WHERE id=@id",("@id",l.Line),("@a",MoneySchemaFixture.Id())),"TR_sal_bank_file_line_immutable");
    }
    static string SettlementJournal(DbConnection c,string company,string settlement,decimal amount)
    {
        string period=MoneySchemaFixture.Id(),journal=MoneySchemaFixture.Id(),debit=MoneySchemaFixture.Id(),credit=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) SELECT @id,@c,'2026-11-01',1,datetime('now') WHERE NOT EXISTS(SELECT 1 FROM fin_accounting_period WHERE company_id=@c AND period_month='2026-11-01')",("@id",period),("@c",company));
        period=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_accounting_period WHERE company_id=@c AND period_month='2026-11-01'",("@c",company))!;
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_journal(id,voucher_no,period_id,company_id,period_month,journal_date,source_type,source_id,purpose,status,created_at) VALUES(@id,@id,@p,@c,'2026-11-01','2026-11-20','inter_company_settlement',@s,'bank_inter_company',1,datetime('now'))",("@id",journal),("@p",period),("@c",company),("@s",settlement));
        string account=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=1 AND is_active=1 ORDER BY code LIMIT 1")!;
        string cc=SchemaSql.GuidText(SchemaOrgCostCentre.NoneId);
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_journal_line(id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at) VALUES(@id,@p,@c,'2026-11-01',@j,1,@a,@cc,@amt,0,datetime('now'))",("@id",debit),("@p",period),("@c",company),("@j",journal),("@a",account),("@cc",cc),("@amt",amount));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_journal_line(id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at) VALUES(@id,@p,@c,'2026-11-01',@j,2,@a,@cc,0,@amt,datetime('now'))",("@id",credit),("@p",period),("@c",company),("@j",journal),("@a",account),("@cc",cc),("@amt",amount));
        MoneySchemaFixture.Exec(c,null,"UPDATE fin_journal SET status=2,posted_at=datetime('now'),posted_by='tester' WHERE id=@id",("@id",journal));
        return journal;
    }
    [TestMethod,DataRow(false),DataRow(true)]
    public void SettlementJournalsMustMatchSourceAndAmountAndCannotBeReused(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string payer=MoneySchemaFixture.Company(c),registration=SchemaTreasuryTests.Registration(c,payer),bank=SchemaTreasuryTests.Account(c,payer,registration),other=MoneySchemaFixture.Company(c),otherRegistration=SchemaTreasuryTests.Registration(c,other);
        string file=File(c,payer,registration,bank);var l=Line(c,file,other,otherRegistration,payer);
        Approve(c,file);
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET status=2 WHERE id=@id",("@id",l.Payment));
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=2 WHERE id=@id",("@id",l.Claim));
        string settlement=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_inter_company_settlement(id,bank_file_line_id,period_month,paying_company_id,entitlement_company_id,amount) VALUES(@id,@l,'2026-11-01',@p,@c,100)",("@id",settlement),("@l",l.Line),("@p",payer),("@c",other));
        string wrong=SettlementJournal(c,payer,MoneySchemaFixture.Id(),100),rightPayer=SettlementJournal(c,payer,settlement,100),rightEnt=SettlementJournal(c,other,settlement,100);
        MoneySchemaFixture.Exec(c,null,"UPDATE fin_inter_company_settlement SET payer_journal_id=@p,entitlement_journal_id=@e WHERE id=@id",("@id",settlement),("@p",wrong),("@e",rightEnt));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE fin_inter_company_settlement SET status=2 WHERE id=@id",("@id",settlement)),"TR_inter_company_source");
        MoneySchemaFixture.Exec(c,null,"UPDATE fin_inter_company_settlement SET payer_journal_id=@p,entitlement_journal_id=@e,status=2 WHERE id=@id",("@id",settlement),("@p",rightPayer),("@e",rightEnt));
        string otherFile=File(c,payer,registration,bank);var otherLine=Line(c,otherFile,other,otherRegistration,payer,MoneySchemaFixture.Run(c,other,"keeta"));
        string reuse=MoneySchemaFixture.Id();
        MoneySchemaFixture.RejectAny(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_inter_company_settlement(id,bank_file_line_id,period_month,paying_company_id,entitlement_company_id,amount,payer_journal_id,entitlement_journal_id) VALUES(@id,@l,'2026-11-01',@p,@c,100,@pj,@ej)",("@id",reuse),("@l",otherLine.Line),("@p",payer),("@c",other),("@pj",rightPayer),("@ej",rightEnt)),
            "UQ_inter_company_payer_journal","UQ_inter_company_entitlement_journal","payer_journal_id","entitlement_journal_id");
    }
}
