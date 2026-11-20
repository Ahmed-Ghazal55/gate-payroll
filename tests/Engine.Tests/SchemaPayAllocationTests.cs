using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaPayAllocationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PostedClaimsAndFinalStatesCannotReopenOrChangeTheirIdentity(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);
        string company=MoneySchemaFixture.Company(c),payable=MoneySchemaFixture.Payable(c,company),payment=MoneySchemaFixture.Payment(c,company);
        string claim=MoneySchemaFixture.Claim(c,company,payment,payable);
        void Change(string set)=>MoneySchemaFixture.Exec(c,null,$"UPDATE pay_payment_allocation SET {set} WHERE id=@id",("@id",claim));
        Change("claim_status=2");
        foreach(int status in new[]{1,3,4})MoneySchemaFixture.Reject(()=>Change($"claim_status={status}"));
        foreach(string set in new[]{"amount=20",$"payment_id='{MoneySchemaFixture.Payment(c,company)}'",$"payable_id='{MoneySchemaFixture.Payable(c,company)}'",$"payment_line_id='{MoneySchemaFixture.Id()}'"})
            MoneySchemaFixture.Reject(()=>Change(set));
        Assert.AreEqual(40m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT amount_paid FROM pay_payable WHERE id=@id",("@id",payable))));
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET status=2 WHERE id=@id",("@id",payment));
        // status 3 on the payment is reversed; status 4 is cancelled.
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET status=3 WHERE id=@id",("@id",payment));
        Change("claim_status=4");
        MoneySchemaFixture.Reject(()=>Change("claim_status=1"));
        string released=MoneySchemaFixture.Claim(c,company,MoneySchemaFixture.Payment(c,company),payable);
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=3 WHERE id=@id",("@id",released));
        foreach(int status in new[]{1,2,4})MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,$"UPDATE pay_payment_allocation SET claim_status={status} WHERE id=@id",("@id",released)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PartialClaimsReservePostReleaseAndCancelWithoutOverpaying(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);string company=MoneySchemaFixture.Company(c);
        string payable=MoneySchemaFixture.Payable(c,company),payment=MoneySchemaFixture.Payment(c,company);
        string a=MoneySchemaFixture.Claim(c,company,payment,payable,40),b=MoneySchemaFixture.Claim(c,company,payment,payable,60);
        decimal Available()=>Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT available_balance FROM pay_payable WHERE id=@id",("@id",payable)));
        Assert.AreEqual(0m,Available());
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Claim(c,company,payment,payable,0.01m),"CK_payable_amounts");
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=2 WHERE id=@id",("@id",a));
        Assert.AreEqual(40m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT amount_paid FROM pay_payable WHERE id=@id",("@id",payable))));
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=4 WHERE id=@id",("@id",b));
        Assert.AreEqual(60m,Available());
        string replacement=MoneySchemaFixture.Claim(c,company,payment,payable,60);
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=3 WHERE id=@id",("@id",a)));
        Assert.AreEqual(0m,Available());
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET amount=101 WHERE id=@id",("@id",replacement)));
        Assert.AreEqual(0m,Available()); // failed trigger rolls the whole statement back
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payable SET amount_due=59 WHERE id=@id",("@id",payable)),"CK_payable_amounts");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payable SET amount_reserved=0 WHERE id=@id",("@id",payable)));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"DELETE FROM pay_payment_allocation WHERE id=@id",("@id",replacement)));
        SchemaPayAllocation.Ensure(c);Assert.AreEqual(0m,Available());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ClaimUniqueCompanyMonthsChecksAndStaleVersionAreEnforced(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);string company=MoneySchemaFixture.Company(c),other=MoneySchemaFixture.Company(c);
        string payable=MoneySchemaFixture.Payable(c,company),payment=MoneySchemaFixture.Payment(c,company,"2026-12-01"),line=MoneySchemaFixture.Id();
        string claim=MoneySchemaFixture.Claim(c,company,payment,payable,40,month:"2026-12-01",line:line);
        // Payment can settle an older entitlement; its own month must still match its header.
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Claim(c,company,payment,payable,10,month:"2026-12-01",line:line),"UQ_pay_alloc_payable_line");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Claim(c,company,payment,payable,10),"FK_pay_alloc_payment",sqliteToken:"FOREIGN KEY");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Claim(c,other,payment,payable,10,month:"2026-12-01"),"FK_pay_alloc_payable",sqliteToken:"FOREIGN KEY");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Claim(c,company,payment,payable,0,month:"2026-12-01"),"CK_pay_alloc_amount");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Claim(c,company,payment,payable,10,status:9,month:"2026-12-01"),"CK_pay_alloc_status");
        byte[] old=(byte[])MoneySchemaFixture.Scalar(c,null,"SELECT row_version FROM pay_payment_allocation WHERE id=@id",("@id",claim))!;
        int Change(){using var cmd=MoneySchemaFixture.Command(c,null,"UPDATE pay_payment_allocation SET claim_status=4 WHERE id=@id AND row_version=@v",("@id",claim),("@v",old));return cmd.ExecuteNonQuery();}
        Assert.AreEqual(1,Change());Assert.AreEqual(0,Change());
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"DELETE FROM pay_payment WHERE id=@id",("@id",payment)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FailedMultiClaimTransactionRollsBackItsEarlierReservation(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);string company=MoneySchemaFixture.Company(c),payable=MoneySchemaFixture.Payable(c,company),payment=MoneySchemaFixture.Payment(c,company);
        using(var tx=c.BeginTransaction())
        {
            void Claim(decimal amount)=>MoneySchemaFixture.Exec(c,tx,"INSERT INTO pay_payment_allocation(id,payment_id,payment_line_id,payable_id,company_id,payable_period_month,period_month,amount,claim_status) VALUES(@id,@p,@id,@due,@c,'2026-11-01','2026-11-01',@amount,1)",("@id",MoneySchemaFixture.Id()),("@p",payment),("@due",payable),("@c",company),("@amount",amount));
            Claim(70);MoneySchemaFixture.Reject(()=>Claim(40));tx.Rollback();
        }
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM pay_payment_allocation"));
        Assert.AreEqual(100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT available_balance FROM pay_payable WHERE id=@id",("@id",payable))));
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ClaimsStayWithinAPositiveLivePayment(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);string company=MoneySchemaFixture.Company(c);
        string first=MoneySchemaFixture.Payable(c,company),second=MoneySchemaFixture.Payable(c,company),payment=MoneySchemaFixture.Payment(c,company);
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET amount=0 WHERE id=@id",("@id",payment)),"CK_payment_amount");
        MoneySchemaFixture.Claim(c,company,payment,first,60);
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Claim(c,company,payment,second,50)); // each payable has room; the payment (100) does not
        string b=MoneySchemaFixture.Claim(c,company,payment,second,40);
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET amount=90 WHERE id=@id",("@id",payment)));
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=3 WHERE id=@id",("@id",b));
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET amount=90 WHERE id=@id",("@id",payment));
        foreach(int status in new[]{3,4})
        {
            string closed=MoneySchemaFixture.Payment(c,company),held=MoneySchemaFixture.Claim(c,company,closed,second,10);
            if(status==3)MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET status=2 WHERE id=@id",("@id",closed));
            MoneySchemaFixture.Exec(c,null,$"UPDATE pay_payment SET status={status} WHERE id=@id",("@id",closed));
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Claim(c,company,closed,second,10));
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=2 WHERE id=@id",("@id",held)));
            MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=3 WHERE id=@id",("@id",held));
        }
        Assert.AreEqual(60m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT amount_reserved FROM pay_payable WHERE id=@id",("@id",first))));
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT amount_reserved FROM pay_payable WHERE id=@id",("@id",second))));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PayingCompanyMaySettleAnotherCompanysPayable(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);string owner=MoneySchemaFixture.Company(c),payer=MoneySchemaFixture.Company(c);
        string payable=MoneySchemaFixture.Payable(c,owner),payment=MoneySchemaFixture.Payment(c,payer);
        // Combined sheet: the payer's payment claims the owner's entitlement; the claim keeps the owner's company.
        string claim=MoneySchemaFixture.Claim(c,owner,payment,payable,40);
        Assert.AreEqual(40m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT amount_reserved FROM pay_payable WHERE id=@id",("@id",payable))));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM pay_payment_allocation a JOIN pay_payment p ON p.id=a.payment_id WHERE a.id=@id AND a.company_id<>p.payer_company_id",("@id",claim)));
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Claim(c,payer,payment,payable,10),"FK_pay_alloc_payable",sqliteToken:"FOREIGN KEY");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Payment(c,owner,payer:MoneySchemaFixture.Id()),"FK_payment_payer",sqliteToken:"FOREIGN KEY");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void WritersLockThePayableBeforeClaimingAndRetryOnlyLockErrors(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);string company=MoneySchemaFixture.Company(c),payable=MoneySchemaFixture.Payable(c,company),payment=MoneySchemaFixture.Payment(c,company);
        using(var tx=c.BeginTransaction())
        {
            Assert.AreEqual(100m,SchemaPayAllocation.LockPayable(c,tx,payable));
            MoneySchemaFixture.Exec(c,tx,"INSERT INTO pay_payment_allocation(id,payment_id,payment_line_id,payable_id,company_id,payable_period_month,period_month,amount,claim_status) VALUES(@id,@p,@id,@due,@c,'2026-11-01','2026-11-01',30,1)",("@id",MoneySchemaFixture.Id()),("@p",payment),("@due",payable),("@c",company));
            Assert.AreEqual(70m,SchemaPayAllocation.LockPayable(c,tx,payable));
            Assert.ThrowsExactly<InvalidOperationException>(()=>SchemaPayAllocation.LockPayable(c,tx,MoneySchemaFixture.Id()));
            tx.Commit();
        }
        Assert.IsTrue(SchemaPayAllocation.IsRetryable(new SqliteException("busy",5)));
        Assert.IsFalse(SchemaPayAllocation.IsRetryable(new SqliteException("Claims exceed payment amount",19)));
    }

    [TestMethod]
    public void SqliteReplaceCannotSwapAPostedClaim()
    {
        using var c=MoneySchemaFixture.Payments();string company=MoneySchemaFixture.Company(c),payable=MoneySchemaFixture.Payable(c,company),payment=MoneySchemaFixture.Payment(c,company),line=MoneySchemaFixture.Id();
        string claim=MoneySchemaFixture.Claim(c,company,payment,payable,40,line:line);
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=2 WHERE id=@id",("@id",claim));
        foreach(string id in new[]{claim,MoneySchemaFixture.Id()})
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT OR REPLACE INTO pay_payment_allocation(id,payment_id,payment_line_id,payable_id,company_id,payable_period_month,period_month,amount,claim_status) VALUES(@id,@p,@line,@due,@c,'2026-11-01','2026-11-01',10,1)",("@id",id),("@p",payment),("@line",line),("@due",payable),("@c",company)));
        Assert.AreEqual(2L,MoneySchemaFixture.Scalar(c,null,"SELECT claim_status FROM pay_payment_allocation WHERE id=@id",("@id",claim)));
        Assert.AreEqual(40m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT amount_paid FROM pay_payable WHERE id=@id",("@id",payable))));
    }

    [TestMethod]
    public void SqliteStartupUpgradesEveryMoneyTrigger()
    {
        using var c=MoneySchemaFixture.Payments();SchemaSalInvoice.Ensure(c);SchemaSalPayRun.Ensure(c);SchemaOperationCapture.Ensure(c);
        Dictionary<string,(string Table,string Sql)> Triggers()
        {
            var found=new Dictionary<string,(string,string)>();
            using var cmd=MoneySchemaFixture.Command(c,null,"SELECT name,tbl_name,sql FROM sqlite_master WHERE type='trigger' AND (tbl_name LIKE 'pay%' OR tbl_name LIKE 'sal%' OR tbl_name LIKE 'fin_operation%')");
            using var reader=cmd.ExecuteReader();while(reader.Read())found[reader.GetString(0)]=(reader.GetString(1),reader.GetString(2));
            return found;
        }
        var expected=Triggers();Assert.IsTrue(expected.Count>30,expected.Count.ToString());
        // An older release left a different body under every name.
        foreach(var (name,(table,_)) in expected)
        {
            MoneySchemaFixture.Exec(c,null,$"DROP TRIGGER {name}");
            MoneySchemaFixture.Exec(c,null,$"CREATE TRIGGER {name} AFTER INSERT ON {table} BEGIN SELECT 1; END;");
        }
        SchemaPayPayable.Ensure(c);SchemaPayAllocation.Ensure(c);SchemaSalInvoice.Ensure(c);SchemaSalPayRun.Ensure(c);SchemaOperationCapture.Ensure(c);
        var upgraded=Triggers();
        foreach(var (name,(_,sql)) in expected)Assert.AreEqual(sql,upgraded[name].Sql,name);
    }

    [TestMethod]
    public void StartupCreatesPaymentAndAllocationParents()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-alloc-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            store.Read(c=>{foreach(string table in new[]{"payment_request","payment","payment_allocation"})Assert.IsTrue(SchemaSql.TableExists(c,null,"pay",table),table);return true;});
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
