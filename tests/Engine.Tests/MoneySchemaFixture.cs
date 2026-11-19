using Microsoft.Data.Sqlite;
using Payroll;

internal static class MoneySchemaFixture
{
    public static string Id()=>SchemaSql.GuidText(SchemaSql.NewId());
    public static SqliteConnection Open()
    {
        var c=new SqliteConnection("Data Source=:memory:");c.Open();
        SchemaSql.Exec(c,null,"PRAGMA foreign_keys=ON; PRAGMA recursive_triggers=ON;");
        SchemaRef.Ensure(c);SchemaHrCompany.Ensure(c);SchemaPayPayable.Ensure(c);
        return c;
    }
    public static string Company(SqliteConnection c)
    {
        string id=Id();
        SchemaSql.Exec(c,null,"INSERT INTO hr_company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,@id,@id,'Synthetic company',1,datetime('now'))",("@id",id));
        return id;
    }
    public static string Payable(SqliteConnection c,string company,decimal amount=100,string month="2026-11-01")
    {
        string id=Id();
        SchemaSql.Exec(c,null,"INSERT INTO pay_payable(id,kind,source_table,source_id,company_id,amount_due,period_month) VALUES(@id,'pay_run_rider','sal.pay_run_rider',@id,@company,@amount,@month)",("@id",id),("@company",company),("@amount",amount),("@month",month));
        return id;
    }
}
