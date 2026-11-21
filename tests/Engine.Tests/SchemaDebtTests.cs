using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaDebtTests
{
    internal static DbConnection Open(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);SchemaSalPayRun.Ensure(c);SchemaDebt.Ensure(c);return c;
    }
    internal static string Debt(DbConnection c,string company,decimal balance=0,string? rider=null)
    {
        string id=MoneySchemaFixture.Id();MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt(id,erp_rider_id,company_id,category,principal,balance,instalment) VALUES(@id,@r,@c,'synthetic',100,@b,20)",("@id",id),("@r",rider??id),("@c",company),("@b",balance));return id;
    }
    internal static string Move(DbConnection c,string company,string debt,decimal amount,string? source=null)
    {
        string id=MoneySchemaFixture.Id();MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-20',@a,'synthetic',@s)",("@id",id),("@d",debt),("@c",company),("@a",amount),("@s",source??id));return id;
    }
    [TestMethod,DataRow(false),DataRow(true)]
    public void LedgerOwnsBalanceNoDoubleApplyNoOverRecoveryAndVersionChanges(bool sqlServer)
    {
        using var c=Open(sqlServer);SchemaDebt.Ensure(c);
        string company=MoneySchemaFixture.Company(c),debt=Debt(c,company);
        MoneySchemaFixture.Reject(()=>Debt(c,company,12),"TR_debt_balance");
        byte[] version=(byte[])MoneySchemaFixture.Scalar(c,null,"SELECT row_version FROM debt_debt WHERE id=@id",("@id",debt))!;
        string first=Move(c,company,debt,100);
        Assert.AreEqual(100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
        CollectionAssert.AreNotEqual(version,(byte[])MoneySchemaFixture.Scalar(c,null,"SELECT row_version FROM debt_debt WHERE id=@id",("@id",debt))!);
        MoneySchemaFixture.Reject(()=>Move(c,company,debt,100,first),"Money row replacement is forbidden");
        MoneySchemaFixture.Reject(()=>Move(c,company,debt,-101),"CK",sqliteToken:"balance>=0");
        Move(c,company,debt,-20);
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET balance=0 WHERE id=@id",("@id",debt)),"TR_debt_balance");
        foreach(var (sql,name) in new[]{("UPDATE debt_debt_movement SET amount=50 WHERE id=@id","TR_debt_debt_movement_immutable"),("DELETE FROM debt_debt_movement WHERE id=@id","Money rows cannot be deleted")})
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,sql,("@id",first)),name);
        Assert.AreEqual(80m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM ref_business_key_guard WHERE row_id=@id",("@id",first)));
        MoneySchemaFixture.Reject(()=>Move(c,MoneySchemaFixture.Company(c),debt,5),"FK",sqliteToken:"FOREIGN KEY");
        MoneySchemaFixture.Reject(()=>Move(c,company,debt,0),"CK",sqliteToken:"amount<>0");
    }
    [TestMethod,DataRow(false),DataRow(true)]
    public void DocumentsFreezeAtApprovalAndDeferredInstalmentCannotMove(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c),debt=Debt(c,company),waiver=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_waiver(id,company_id,period_month,debt_id,amount,reason,expense_account_code) VALUES(@id,@c,'2026-11-01',@d,20,'Synthetic reason','526601')",("@id",waiver),("@c",company),("@d",debt));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE debt_waiver SET status=3 WHERE id=@id",("@id",waiver)),"TR_debt_waiver_state");
        MoneySchemaFixture.Exec(c,null,"UPDATE debt_waiver SET status=2 WHERE id=@id",("@id",waiver));
        MoneySchemaFixture.Exec(c,null,"UPDATE debt_waiver SET status=3,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",waiver),("@u",MoneySchemaFixture.Id()));
        foreach(string set in new[]{"amount=21","status=1","run_mode=1","payout_blocked=1"})
            MoneySchemaFixture.RejectAny(()=>MoneySchemaFixture.Exec(c,null,$"UPDATE debt_waiver SET {set} WHERE id=@id",("@id",waiver)),
                "TR_debt_waiver_state","Frozen money operation cannot change run mode or remove payout block");
        string run=MoneySchemaFixture.Run(c,company),rider=MoneySchemaFixture.Rider(c,run,company,net:100);
        debt=Debt(c,company,rider:rider);
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run_rider SET debt_confirm_status='deferred' WHERE id=@id",("@id",rider));
        Move(c,company,debt,100);
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id,pay_run_rider_id,pay_run_period_month) VALUES(@id,@d,@c,'2026-11-01','2026-11-20',-10,'salary_deduction',@id,@r,'2026-11-01')",("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company),("@r",rider)),"TR_debt_movement_source");
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM debt_debt_movement"));
    }
    [TestMethod,DataRow(false),DataRow(true)]
    public void ApprovedImportAndCollectionApplyOnceAndConfirmedInstalmentCapsAllDebts(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company),rider=MoneySchemaFixture.Rider(c,run,company,net:100),debt=Debt(c,company,rider:rider),batch=MoneySchemaFixture.Id(),line=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_import_batch(id,company_id,period_month,amount,reason,file_hash,import_kind) VALUES(@id,@c,'2026-11-01',100,'Synthetic import',randomblob(32),'opening')",("@id",batch),("@c",company));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_import_line(id,import_batch_id,period_month,line_no,source_row,debt_id,company_id,amount) VALUES(@id,@b,'2026-11-01',1,1,@d,@c,100)",("@id",line),("@b",batch),("@d",debt),("@c",company));
        void Apply(string kind,string source,decimal amount,string? salaryRider=null)=>MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id,pay_run_rider_id,pay_run_period_month) VALUES(@id,@d,@c,'2026-11-01','2026-11-20',@a,@kind,@s,@r,@rp)",("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company),("@a",amount),("@kind",kind),("@s",source),("@r",(object?)salaryRider??DBNull.Value),("@rp",salaryRider is null?DBNull.Value:"2026-11-01"));
        MoneySchemaFixture.Reject(()=>Apply("import",line,100),"TR_debt_source_import");
        void Approve(string table,string id)
        {
            MoneySchemaFixture.Exec(c,null,$"UPDATE debt_{table} SET status=2 WHERE id=@id",("@id",id));
            MoneySchemaFixture.Exec(c,null,$"UPDATE debt_{table} SET status=3,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",id),("@u",MoneySchemaFixture.Id()));
        }
        Approve("import_batch",batch);Apply("import",line,100);
        MoneySchemaFixture.Reject(()=>Apply("import",line,100),"Money row replacement is forbidden");
        string receipt=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_collection(id,company_id,period_month,debt_id,amount,reason) VALUES(@id,@c,'2026-11-01',@d,20,'Synthetic receipt')",("@id",receipt),("@c",company),("@d",debt));
        Approve("collection",receipt);Apply("collection",receipt,-20);
        MoneySchemaFixture.Reject(()=>Apply("collection",receipt,-20),"Money row replacement is forbidden");
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run_rider SET debt_confirm_status='confirmed',old_debt_instalment=20 WHERE id=@id",("@id",rider));
        Apply("salary_deduction",MoneySchemaFixture.Id(),-10,rider);Apply("salary_deduction",MoneySchemaFixture.Id(),-10,rider);
        MoneySchemaFixture.Reject(()=>Apply("salary_deduction",MoneySchemaFixture.Id(),-1,rider),"Debt deductions exceed net or instalment");
        Assert.AreEqual(60m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
    }
    [TestMethod,DataRow(false),DataRow(true)]
    public void DuplicateImportFileHashIsRejectedPermanentlyWithStableSourceRow(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c),debt=Debt(c,company),batch=MoneySchemaFixture.Id(),hash=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        byte[] bytes=Convert.FromHexString(hash);
        MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_import_batch(id,company_id,period_month,amount,reason,file_hash,import_kind) VALUES(@id,@c,'2026-11-01',100,'Synthetic import',@h,'opening')",("@id",batch),("@c",company),("@h",bytes));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_import_line(id,import_batch_id,period_month,line_no,source_row,debt_id,company_id,amount) VALUES(@id,@b,'2026-11-01',1,10,@d,@c,100)",("@id",MoneySchemaFixture.Id()),("@b",batch),("@d",debt),("@c",company));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_import_line(id,import_batch_id,period_month,line_no,source_row,debt_id,company_id,amount) VALUES(@id,@b,'2026-11-01',2,10,@d,@c,50)",("@id",MoneySchemaFixture.Id()),("@b",batch),("@d",debt),("@c",company)),"Money row replacement is forbidden");
        string again=MoneySchemaFixture.Id();
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_import_batch(id,company_id,period_month,amount,reason,file_hash,import_kind) VALUES(@id,@c,'2026-11-01',100,'Repeat import',@h,'opening')",("@id",again),("@c",company),("@h",bytes)),"Money row replacement is forbidden");
    }
    [TestMethod,DataRow(false),DataRow(true)]
    public void SalaryDeductionsCannotExceedPositiveNetEvenWhenInstalmentIsHigher(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company),rider=MoneySchemaFixture.Rider(c,run,company,net:15),debt=Debt(c,company,rider:rider);
        Move(c,company,debt,100);
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run_rider SET debt_confirm_status='confirmed',old_debt_instalment=20 WHERE id=@id",("@id",rider));
        void Apply(decimal amount)=>MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id,pay_run_rider_id,pay_run_period_month) VALUES(@id,@d,@c,'2026-11-01','2026-11-20',@a,'salary_deduction',@id,@r,'2026-11-01')",("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company),("@a",amount),("@r",rider));
        Apply(-10);
        MoneySchemaFixture.Reject(()=>Apply(-6),"Debt deductions exceed net or instalment");
    }
}
