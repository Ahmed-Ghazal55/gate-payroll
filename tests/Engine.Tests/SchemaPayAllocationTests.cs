using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaPayAllocationTests
{
    [TestMethod]
    public void PartialClaimsReservePostReleaseAndCancelWithoutOverpaying()
    {
        using var c=MoneySchemaFixture.Payments();string company=MoneySchemaFixture.Company(c);
        string payable=MoneySchemaFixture.Payable(c,company),payment=MoneySchemaFixture.Payment(c,company);
        string a=MoneySchemaFixture.Claim(c,company,payment,payable,40),b=MoneySchemaFixture.Claim(c,company,payment,payable,60);
        decimal Available()=>Convert.ToDecimal(SchemaSql.Scalar(c,null,"SELECT available_balance FROM pay_payable WHERE id=@id",("@id",payable)));
        Assert.AreEqual(0m,Available());
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Claim(c,company,payment,payable,0.01m),"CK_payable_amounts");
        SchemaSql.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=2 WHERE id=@id",("@id",a));
        Assert.AreEqual(40m,Convert.ToDecimal(SchemaSql.Scalar(c,null,"SELECT amount_paid FROM pay_payable WHERE id=@id",("@id",payable))));
        SchemaSql.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=4 WHERE id=@id",("@id",b));
        Assert.AreEqual(60m,Available());
        string replacement=MoneySchemaFixture.Claim(c,company,payment,payable,60);
        SchemaSql.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=3 WHERE id=@id",("@id",a));
        Assert.AreEqual(40m,Available());
        SchemaConstraintAssert.ThrowsNamed(()=>SchemaSql.Exec(c,null,"UPDATE pay_payment_allocation SET amount=101 WHERE id=@id",("@id",replacement)),"CK_payable_amounts");
        Assert.AreEqual(40m,Available()); // failed trigger rolls the whole statement back
        SchemaConstraintAssert.ThrowsNamed(()=>SchemaSql.Exec(c,null,"UPDATE pay_payable SET amount_due=59 WHERE id=@id",("@id",payable)),"CK_payable_amounts");
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,"UPDATE pay_payable SET amount_reserved=0 WHERE id=@id",("@id",payable)));
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,"DELETE FROM pay_payment_allocation WHERE id=@id",("@id",replacement)));
        SchemaPayAllocation.Ensure(c);Assert.AreEqual(40m,Available());
    }

    [TestMethod]
    public void ClaimUniqueCompanyMonthsChecksAndStaleVersionAreEnforced()
    {
        using var c=MoneySchemaFixture.Payments();string company=MoneySchemaFixture.Company(c),other=MoneySchemaFixture.Company(c);
        string payable=MoneySchemaFixture.Payable(c,company),payment=MoneySchemaFixture.Payment(c,company,"2026-12-01"),line=MoneySchemaFixture.Id();
        string claim=MoneySchemaFixture.Claim(c,company,payment,payable,40,month:"2026-12-01",line:line);
        // Payment can settle an older entitlement; its own month must still match its header.
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Claim(c,company,payment,payable,10,month:"2026-12-01",line:line),"UQ_pay_alloc_payable_line",sqliteToken:"pay_payment_allocation.payable_id, pay_payment_allocation.payment_line_id");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Claim(c,company,payment,payable,10),"FK_pay_alloc_payment",sqliteToken:"FOREIGN KEY");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Claim(c,other,payment,payable,10,month:"2026-12-01"),"FK_pay_alloc_payable",sqliteToken:"FOREIGN KEY");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Claim(c,company,payment,payable,0,month:"2026-12-01"),"CK_pay_alloc_amount");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Claim(c,company,payment,payable,10,status:9,month:"2026-12-01"),"CK_pay_alloc_status");
        byte[] old=(byte[])SchemaSql.Scalar(c,null,"SELECT row_version FROM pay_payment_allocation WHERE id=@id",("@id",claim))!;
        int Change(){using var cmd=Store.Command(c,null,"UPDATE pay_payment_allocation SET claim_status=4 WHERE id=@id AND row_version=@v",("@id",claim),("@v",old));return cmd.ExecuteNonQuery();}
        Assert.AreEqual(1,Change());Assert.AreEqual(0,Change());
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,"DELETE FROM pay_payment WHERE id=@id",("@id",payment)));
    }

    [TestMethod]
    public void FailedMultiClaimTransactionRollsBackItsEarlierReservation()
    {
        using var c=MoneySchemaFixture.Payments();string company=MoneySchemaFixture.Company(c),payable=MoneySchemaFixture.Payable(c,company),payment=MoneySchemaFixture.Payment(c,company);
        using(var tx=c.BeginTransaction())
        {
            void Claim(decimal amount)=>SchemaSql.Exec(c,tx,"INSERT INTO pay_payment_allocation(id,payment_id,payment_line_id,payable_id,company_id,payable_period_month,period_month,amount,claim_status) VALUES(@id,@p,@id,@due,@c,'2026-11-01','2026-11-01',@amount,1)",("@id",MoneySchemaFixture.Id()),("@p",payment),("@due",payable),("@c",company),("@amount",amount));
            Claim(70);Assert.ThrowsExactly<SqliteException>(()=>Claim(40));tx.Rollback();
        }
        Assert.AreEqual(0L,SchemaSql.Scalar(c,null,"SELECT COUNT(*) FROM pay_payment_allocation"));
        Assert.AreEqual(100m,Convert.ToDecimal(SchemaSql.Scalar(c,null,"SELECT available_balance FROM pay_payable WHERE id=@id",("@id",payable))));
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
