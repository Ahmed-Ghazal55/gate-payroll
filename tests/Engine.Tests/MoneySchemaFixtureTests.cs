using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

[TestClass]
public sealed class MoneySchemaFixtureTests
{
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
