using System.Data.Common;
using System.Security.Claims;
using Payroll;

internal static class DebtLedgerFixture
{
    public static ClaimsPrincipal Admin=>UserAccess.Principal(new(){["role"]="Admin",["username"]="debt-admin"});
    public static ClaimsPrincipal Checker=>UserAccess.Principal(new(){["role"]="Admin",["username"]="debt-checker"});

    public static (DbConnection c,string company,string period,Guid companyG,Guid periodG) Open(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);
        SchemaFinPeriodBalance.Ensure(c);SchemaSalPayRun.Ensure(c);SchemaDebt.Ensure(c);
        string company=MoneySchemaFixture.Company(c),period=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-11-01',1,datetime('now'))",("@id",period),("@c",company));
        return (c,company,period,Guid.Parse(company),Guid.Parse(period));
    }

    public static Guid Acc(DbConnection c,string code)=>
        Guid.Parse((string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE code=@c",("@c",code))!);
}
