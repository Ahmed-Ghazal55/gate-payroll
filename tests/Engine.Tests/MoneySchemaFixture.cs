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
    public static SqliteConnection Payments()
    {
        var c=Open();SchemaOrgCostCentre.Ensure(c);SchemaFinAccount.Ensure(c);SchemaFinPeriod.Ensure(c);SchemaFinJournal.Ensure(c);SchemaPayAllocation.Ensure(c);
        return c;
    }
    public static string Payment(SqliteConnection c,string company,string month="2026-11-01")
    {
        string id=Id();
        SchemaSql.Exec(c,null,"INSERT INTO pay_payment(id,payment_no,company_id,period_month,paid_on,amount) VALUES(@id,@id,@company,@month,@month,100)",("@id",id),("@company",company),("@month",month));
        return id;
    }
    public static string Invoice(SqliteConnection c,string company,string? cr=null,string month="2026-11-01")
    {
        string id=Id();
        SchemaSql.Exec(c,null,"INSERT INTO sal_platform_invoice(id,app_code,company_id,erp_cr_id,city_code,period_from,period_to,period_month,file_hash,gross,deductions,net,posting_owner) VALUES(@id,'ninja',@c,@cr,'synthetic',@month,@month,@month,randomblob(32),100,10,90,'invoice')",("@id",id),("@c",company),("@cr",cr??Id()),("@month",month));
        return id;
    }
    public static string Run(SqliteConnection c,string company,string app="ninja",string month="2026-11-01")
    {
        string id=Id();SchemaSql.Exec(c,null,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month) VALUES(@id,@c,@app,@month)",("@id",id),("@c",company),("@app",app),("@month",month));return id;
    }
    public static string Rider(SqliteConnection c,string run,string company,string? payable=null,decimal net=-10,string month="2026-11-01")
    {
        string id=Id();SchemaSql.Exec(c,null,"INSERT INTO sal_pay_run_rider(id,pay_run_id,company_id,period_month,erp_rider_id,gross,commission,salary_net,payable_id) VALUES(@id,@run,@c,@month,@id,100,10,@net,@payable)",("@id",id),("@run",run),("@c",company),("@month",month),("@net",net),("@payable",(object?)payable??DBNull.Value));return id;
    }
    public static string Claim(SqliteConnection c,string company,string payment,string payable,decimal amount=40,int status=1,string month="2026-11-01",string? line=null)
    {
        string id=Id();
        SchemaSql.Exec(c,null,"INSERT INTO pay_payment_allocation(id,payment_id,payment_line_id,payable_id,company_id,payable_period_month,period_month,amount,claim_status) VALUES(@id,@payment,@line,@payable,@company,'2026-11-01',@month,@amount,@status)",("@id",id),("@payment",payment),("@line",line??Id()),("@payable",payable),("@company",company),("@month",month),("@amount",amount),("@status",status));
        return id;
    }
    public static string Payable(SqliteConnection c,string company,decimal amount=100,string month="2026-11-01")
    {
        string id=Id();
        SchemaSql.Exec(c,null,"INSERT INTO pay_payable(id,kind,source_table,source_id,company_id,amount_due,period_month) VALUES(@id,'pay_run_rider','sal.pay_run_rider',@id,@company,@amount,@month)",("@id",id),("@company",company),("@amount",amount),("@month",month));
        return id;
    }
}
