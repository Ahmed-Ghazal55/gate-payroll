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
        // Document-store table for ResolveMoneyFlags / change_log helpers (SQLite vs SQL Server DDL).
        if(SchemaSql.IsSql(c))
            MoneySchemaFixture.Exec(c,null,"IF OBJECT_ID(N'dbo.records',N'U') IS NULL CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL)");
        else
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
        SchemaFinPeriodBalance.Ensure(c);SchemaSalPayRun.Ensure(c);SchemaDebt.Ensure(c);
        SchemaAuthCatalog.Ensure(c);SchemaApprovals.Ensure(c);SchemaAuditNotify.Ensure(c);
        string company=MoneySchemaFixture.Company(c),period=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-11-01',1,datetime('now'))",("@id",period),("@c",company));
        return (c,company,period,Guid.Parse(company),Guid.Parse(period));
    }

    public static Guid Acc(DbConnection c,string code)=>
        Guid.Parse((string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE code=@c",("@c",code))!);
}
