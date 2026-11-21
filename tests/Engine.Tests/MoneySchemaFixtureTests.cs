using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;
using Payroll;

[TestClass]
public sealed class MoneySchemaFixtureTests
{
    [TestMethod]
    public void RejectRequiresTheSpecificConstraintName()
    {
        using var c=MoneySchemaFixture.Payments();
        string company=MoneySchemaFixture.Company(c),payment=MoneySchemaFixture.Payment(c,company);
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"DELETE FROM pay_payment WHERE id=@id",("@id",payment)),"Money rows cannot be deleted");
        // Assert only around the helper call so a later Assert.Fail is not swallowed.
        bool rejectedWrongName=false;
        try{MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"DELETE FROM pay_payment WHERE id=@id",("@id",payment)),"wrong-constraint");}
        catch(AssertFailedException){rejectedWrongName=true;}
        Assert.IsTrue(rejectedWrongName,"Reject must fail when the constraint name does not match.");
    }

    [TestMethod]
    public void TableRewriteLeavesColumnsAndLiteralsAlone()
    {
        var tables=new HashSet<string>{"pay_payment","pay_payment_allocation","sal_pay_run","sal_pay_run_rider","fin_operation_capture_manifest"};
        string sql=MoneySchemaFixture.RewriteTables(
            "INSERT INTO fin_operation_capture_manifest(operation_type,pay_run_id) SELECT 'pay_run','sal.pay_run_rider' FROM sal_pay_run_rider r JOIN pay_payment_allocation a ON a.payable_id=r.payable_id",
            tables.Contains,(schema,table)=>$"[{schema}].[{table}]");
        Assert.AreEqual("INSERT INTO [fin].[operation_capture_manifest](operation_type,pay_run_id) SELECT 'pay_run','sal.pay_run_rider' FROM [sal].[pay_run_rider] r JOIN [pay].[payment_allocation] a ON a.payable_id=r.payable_id",sql);
    }

    [TestMethod]
    public void SqlServerRewriteTurnsLimitIntoTop()
    {
        string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if(string.IsNullOrWhiteSpace(configured))Assert.Inconclusive("PAYROLL_TEST_SQL_CONNECTION not set");
        using var c=MoneySchemaFixture.Open(true);
        string sql=MoneySchemaFixture.Sql(c,"SELECT id FROM fin_account WHERE is_postable=1 ORDER BY code LIMIT 1");
        StringAssert.Contains(sql,"SELECT TOP 1 ",StringComparison.OrdinalIgnoreCase);
        Assert.IsFalse(sql.Contains("LIMIT",StringComparison.OrdinalIgnoreCase),sql);
    }

    [TestMethod]
    public void SyntheticCompanyFitsSqlServerColumns()
    {
        using var c=MoneySchemaFixture.Open();string id=MoneySchemaFixture.Company(c),other=MoneySchemaFixture.Company(c);
        string unified=(string)MoneySchemaFixture.Scalar(c,null,"SELECT unified_number FROM hr_company WHERE id=@id",("@id",id))!;
        string code=(string)MoneySchemaFixture.Scalar(c,null,"SELECT code FROM hr_company WHERE id=@id",("@id",id))!;
        Assert.IsTrue(Regex.IsMatch(unified,"^7[0-9]{9}$"),unified);
        Assert.IsTrue(code.Length<=20,code);
        Assert.AreNotEqual(unified,MoneySchemaFixture.Scalar(c,null,"SELECT unified_number FROM hr_company WHERE id=@id",("@id",other)));
    }
}
