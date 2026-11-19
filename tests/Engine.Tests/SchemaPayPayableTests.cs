using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaPayPayableTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CountersCannotBeInventedWithoutClaimsOnInsertOrUpdate(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);string company=MoneySchemaFixture.Company(c),id=MoneySchemaFixture.Payable(c,company);
        foreach(string column in new[]{"amount_paid","amount_reserved"})
        {
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,$"UPDATE pay_payable SET {column}=1 WHERE id=@id",("@id",id)));
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,$"INSERT INTO pay_payable(id,kind,source_table,source_id,company_id,period_month,amount_due,{column}) VALUES(@id,'advance','test',@id,@c,'2026-11-01',100,1)",("@id",MoneySchemaFixture.Id()),("@c",company)));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SourceUniquenessMoneyMonthCurrencyAndCompanyAreEnforced(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);string company=MoneySchemaFixture.Company(c);
        string id=MoneySchemaFixture.Payable(c,company);
        void Change(string set)=>MoneySchemaFixture.Exec(c,null,$"UPDATE pay_payable SET {set} WHERE id=@id",("@id",id));
        SchemaConstraintAssert.ThrowsNamed(()=>Change("amount_due=-1"),"CK_payable_nonnegative");
        SchemaConstraintAssert.ThrowsNamed(()=>Change("amount_reserved=101"),"CK_payable_amounts");
        SchemaConstraintAssert.ThrowsNamed(()=>Change("amount_paid=-1"),"CK_payable_nonnegative");
        SchemaConstraintAssert.ThrowsNamed(()=>Change("currency_code='USD'"),"CK_payable_currency");
        foreach(string month in new[]{"2026-11-02"})
            SchemaConstraintAssert.ThrowsNamed(()=>Change($"period_month='{month}'"),"CK_payable_month");
        SchemaConstraintAssert.ThrowsNamed(()=>Change($"company_id='{MoneySchemaFixture.Id()}'"),"FK_payable_company",sqliteToken:"FOREIGN KEY");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO pay_payable(id,kind,source_table,source_id,company_id,amount_due,period_month) SELECT @new,kind,source_table,source_id,company_id,amount_due,period_month FROM pay_payable WHERE id=@id",("@new",MoneySchemaFixture.Id()),("@id",id)),"UQ_payable_source",sqliteToken:"pay_payable.source_table, pay_payable.source_id");
        MoneySchemaFixture.Reject(()=>Change("amount_reserved=25,amount_paid=30"));
        Assert.AreEqual(100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT available_balance FROM pay_payable WHERE id=@id",("@id",id))));
        SchemaPayPayable.Ensure(c);
        Assert.AreEqual("new",MoneySchemaFixture.Scalar(c,null,"SELECT generation FROM pay_payable WHERE id=@id",("@id",id)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompareAndSwapRejectsStaleRowVersion(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);string id=MoneySchemaFixture.Payable(c,MoneySchemaFixture.Company(c));
        byte[] version=(byte[])MoneySchemaFixture.Scalar(c,null,"SELECT row_version FROM pay_payable WHERE id=@id",("@id",id))!;
        Assert.AreEqual(8,version.Length);
        int Update(){using var cmd=MoneySchemaFixture.Command(c,null,"UPDATE pay_payable SET due_on='2026-11-20' WHERE id=@id AND row_version=@version",("@id",id),("@version",version));return cmd.ExecuteNonQuery();}
        Assert.AreEqual(1,Update());Assert.AreEqual(0,Update());
        Assert.IsFalse(version.SequenceEqual((byte[])MoneySchemaFixture.Scalar(c,null,"SELECT row_version FROM pay_payable WHERE id=@id",("@id",id))!));
    }
    [TestMethod]
    public void StartupCreatesEmptyCanonicalPayableAndReopensWithoutBackfill()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-payable-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            store.Read(c=>{Assert.IsTrue(SchemaSql.TableExists(c,null,"pay","payable"));return true;});
            store=new Store(dir);
            store.Read(c=>{Assert.AreEqual(0L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM pay_payable")));return true;});
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
