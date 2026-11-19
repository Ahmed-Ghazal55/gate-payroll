using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaSalPayRunTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RunFlagsAreIndependentAndActiveBusinessKeyIsFiltered(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);SchemaSalPayRun.Ensure(c);
        string company=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company);
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET run_mode=0,payout_blocked=1 WHERE id=@id",("@id",run));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,null,"SELECT run_mode FROM sal_pay_run WHERE id=@id",("@id",run)));
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Run(c,company),"UQ_sal_run_active",sqliteToken:"sal_pay_run.app_code, sal_pay_run.company_id, sal_pay_run.period_month");
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET status=4 WHERE id=@id",("@id",run));
        string simulation=MoneySchemaFixture.Run(c,company);
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET run_mode=1,payout_blocked=0 WHERE id=@id",("@id",simulation));
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET run_mode=2 WHERE id=@id",("@id",simulation)),"CK_sal_run_flags");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET stage=2 WHERE id=@id",("@id",simulation)),"CK_sal_run_capture");
        SchemaSalPayRun.Ensure(c);Assert.AreEqual(2L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM sal_pay_run"));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SignedNetDebtBeforeApprovalCompositeFksAndVersionAreEnforced(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);SchemaSalPayRun.Ensure(c);
        string company=MoneySchemaFixture.Company(c),other=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company);
        string rider=MoneySchemaFixture.Rider(c,run,company); // negative net remains a signed value until approval
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Rider(c,run,other),"FK_sal_rider_run",sqliteToken:"FOREIGN KEY");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Rider(c,run,company,month:"2026-12-01"),"FK_sal_rider_run",sqliteToken:"FOREIGN KEY");
        string wrong=MoneySchemaFixture.Payable(c,other);
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run_rider SET payable_id=@p WHERE id=@id",("@id",rider),("@p",wrong)),"FK_sal_rider_payable",sqliteToken:"FOREIGN KEY");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run_rider SET salary_approved_at=datetime('now') WHERE id=@id",("@id",rider)),"CK_sal_rider_approval");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run_rider SET debt_confirm_status='deferred',old_debt_instalment=1 WHERE id=@id",("@id",rider)),"CK_sal_rider_debt");
        byte[] version=(byte[])MoneySchemaFixture.Scalar(c,null,"SELECT row_version FROM sal_pay_run_rider WHERE id=@id",("@id",rider))!;
        int Approve(){using var cmd=MoneySchemaFixture.Command(c,null,"UPDATE sal_pay_run_rider SET debt_confirm_status='deferred',salary_approved_at=datetime('now') WHERE id=@id AND row_version=@v",("@id",rider),("@v",version));return cmd.ExecuteNonQuery();}
        Assert.AreEqual(1,Approve());Assert.AreEqual(0,Approve());
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run_rider SET bank_eligible=1 WHERE id=@id",("@id",rider)),"CK_sal_rider_bank");
        void Item(string month,string stage,int line)=>MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_pay_run_item(id,period_month,pay_run_rider_id,line_no,stage_code,item_code,amount,affects_net,account_code) VALUES(@id,@month,@r,@line,@stage,'synthetic',1,1,'112507')",("@id",MoneySchemaFixture.Id()),("@r",rider),("@month",month),("@stage",stage),("@line",line));
        SchemaConstraintAssert.ThrowsNamed(()=>Item("2026-12-01","level1",1),"FK_sal_item_rider",sqliteToken:"FOREIGN KEY");
        Item("2026-11-01","level1",1);Item("2026-11-01","net",2);Item("2026-11-01","level2",3);Item("2026-11-01","old_debt",4);
        Assert.AreEqual(4L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM sal_pay_run_item"));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"DELETE FROM sal_pay_run WHERE id=@id",("@id",run)));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HoldsDefaultOpenAndOnlyOneActiveHoldOrRequestCanOwnItsKey(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);SchemaSalPayRun.Ensure(c);
        string company=MoneySchemaFixture.Company(c),rider=MoneySchemaFixture.Id();
        void Hold()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_shared_rider_hold(id,erp_rider_id,period_month) VALUES(@id,@r,'2026-11-01')",("@id",MoneySchemaFixture.Id()),("@r",rider));
        Hold();Assert.AreEqual("open",MoneySchemaFixture.Scalar(c,null,"SELECT status FROM sal_shared_rider_hold"));
        SchemaConstraintAssert.ThrowsNamed(Hold,"UQ_sal_shared_hold_open",sqliteToken:"sal_shared_rider_hold.erp_rider_id, sal_shared_rider_hold.period_month");
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_shared_rider_hold SET status='cleared'");Hold();
        void Request()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_rider_request(id,erp_rider_id,company_id,period_month,app_code,kind) VALUES(@id,@r,@c,'2026-11-01','ninja','hold')",("@id",MoneySchemaFixture.Id()),("@r",rider),("@c",company));
        Request();SchemaConstraintAssert.ThrowsNamed(Request,"UQ_sal_request_hold",sqliteToken:"sal_rider_request.erp_rider_id, sal_rider_request.period_month, sal_rider_request.app_code");
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_rider_request SET status=4");Request();
    }
    [TestMethod]
    public void StartupCreatesEmptyRunRidersItemsHoldsAndRequests()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-sal-run-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            store.Read(c=>{foreach(string t in new[]{"pay_run","pay_run_rider","pay_run_item","shared_rider_hold","shared_rider_hold_app","rider_request"})Assert.IsTrue(SchemaSql.TableExists(c,null,"sal",t),t);return true;});
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
