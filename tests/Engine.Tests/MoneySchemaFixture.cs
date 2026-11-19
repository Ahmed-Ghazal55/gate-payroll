using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Data.Common;
using System.Text.RegularExpressions;
using Payroll;

internal static class MoneySchemaFixture
{
    public static string Sql(DbConnection c,string sql)
    {
        if(!SchemaSql.IsSql(c))return sql;
        sql=sql.Replace("datetime('now')","SYSUTCDATETIME()").Replace("randomblob(32)","CRYPT_GEN_RANDOM(32)");
        return Regex.Replace(sql,@"\b(pay|sal|fin|hr|org|ref)_([a-z][a-z0-9_]*)\b",m=>SchemaSql.Q(c,m.Groups[1].Value,m.Groups[2].Value));
    }
    public static void Exec(DbConnection c,DbTransaction? tx,string sql,params (string Name,object Value)[] values)=>SchemaSql.Exec(c,tx,Sql(c,sql),values);
    public static object? Scalar(DbConnection c,DbTransaction? tx,string sql,params (string Name,object Value)[] values)
    {
        object? value=SchemaSql.Scalar(c,tx,Sql(c,sql),values);
        return value switch { int n=>(long)n, byte n=>(long)n, bool b=>b?1L:0L, Guid id=>id.ToString("D"), _=>value };
    }
    public static DbCommand Command(DbConnection c,DbTransaction? tx,string sql,params (string Name,object Value)[] values)=>Store.Command(c,tx,Sql(c,sql),values);
    public static void Reject(Action action)
    {
        try{action();Assert.Fail("Expected a database rejection.");}catch(DbException){}
    }
    public static string Id()=>SchemaSql.GuidText(SchemaSql.NewId());
    public static DbConnection Open(bool sqlServer=false)
    {
        DbConnection c;
        if(sqlServer)
        {
            string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
            if(string.IsNullOrWhiteSpace(configured))Assert.Inconclusive("PAYROLL_TEST_SQL_CONNECTION not set; isolated SQL Server required.");
            var builder=new SqlConnectionStringBuilder(configured);
            Assert.IsTrue(Regex.IsMatch(builder.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",RegexOptions.IgnoreCase),"Only isolated loopback SQL Server is allowed.");
            builder.InitialCatalog="master";builder.Pooling=false;
            string adminConnection=builder.ToString(),database="GateMoney_"+Guid.NewGuid().ToString("N");
            using(var admin=new SqlConnection(adminConnection))
            {admin.Open();SchemaSql.Exec(admin,null,$"CREATE DATABASE [{database}]");}
            builder.InitialCatalog=database;c=new SqlConnection(builder.ToString());c.Open();
            c.StateChange+=(_,e)=>
            {
                if(e.CurrentState!=System.Data.ConnectionState.Closed)return;
                using var admin=new SqlConnection(adminConnection);admin.Open();
                SchemaSql.Exec(admin,null,$"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
            };
        }
        else
        {
            c=new SqliteConnection("Data Source=:memory:");c.Open();
            // Store.Open sets only foreign_keys; do not enable recursive_triggers.
            Exec(c,null,"PRAGMA foreign_keys=ON;");
        }
        SchemaRef.Ensure(c);SchemaHrCompany.Ensure(c);SchemaPayPayable.Ensure(c);
        return c;
    }
    public static string Company(DbConnection c)
    {
        string id=Id();
        Exec(c,null,"INSERT INTO hr_company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,@id,@id,'Synthetic company',1,datetime('now'))",("@id",id));
        return id;
    }
    public static DbConnection Payments(bool sqlServer=false)
    {
        var c=Open(sqlServer);SchemaOrgCostCentre.Ensure(c);SchemaFinAccount.Ensure(c);SchemaFinPeriod.Ensure(c);SchemaFinJournal.Ensure(c);SchemaPayAllocation.Ensure(c);
        return c;
    }
    public static string Payment(DbConnection c,string company,string month="2026-11-01")
    {
        string id=Id();
        Exec(c,null,"INSERT INTO pay_payment(id,payment_no,company_id,period_month,paid_on,amount) VALUES(@id,@id,@company,@month,@month,100)",("@id",id),("@company",company),("@month",month));
        return id;
    }
    public static string Invoice(DbConnection c,string company,string? cr=null,string month="2026-11-01")
    {
        string id=Id();
        Exec(c,null,"INSERT INTO sal_platform_invoice(id,app_code,company_id,erp_cr_id,city_code,period_from,period_to,period_month,file_hash,gross,deductions,net,posting_owner) VALUES(@id,'ninja',@c,@cr,'synthetic',@month,@month,@month,randomblob(32),100,10,90,'invoice')",("@id",id),("@c",company),("@cr",cr??Id()),("@month",month));
        return id;
    }
    public static string Run(DbConnection c,string company,string app="ninja",string month="2026-11-01")
    {
        string id=Id();Exec(c,null,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month) VALUES(@id,@c,@app,@month)",("@id",id),("@c",company),("@app",app),("@month",month));return id;
    }
    public static string Rider(DbConnection c,string run,string company,string? payable=null,decimal net=-10,string month="2026-11-01",int mode=0)
    {
        string id=Id();Exec(c,null,"INSERT INTO sal_pay_run_rider(id,pay_run_id,company_id,period_month,erp_rider_id,gross,commission,salary_net,payable_id,run_mode) VALUES(@id,@run,@c,@month,@id,100,10,@net,@payable,@mode)",("@id",id),("@run",run),("@c",company),("@month",month),("@net",net),("@payable",(object?)payable??DBNull.Value),("@mode",mode));return id;
    }
    public static string Claim(DbConnection c,string company,string payment,string payable,decimal amount=40,int status=1,string month="2026-11-01",string? line=null)
    {
        string id=Id();
        Exec(c,null,"INSERT INTO pay_payment_allocation(id,payment_id,payment_line_id,payable_id,company_id,payable_period_month,period_month,amount,claim_status) VALUES(@id,@payment,@line,@payable,@company,'2026-11-01',@month,@amount,@status)",("@id",id),("@payment",payment),("@line",line??Id()),("@payable",payable),("@company",company),("@month",month),("@amount",amount),("@status",status));
        return id;
    }
    public static string Payable(DbConnection c,string company,decimal amount=100,string month="2026-11-01")
    {
        string id=Id();
        Exec(c,null,"INSERT INTO pay_payable(id,kind,source_table,source_id,company_id,amount_due,period_month) VALUES(@id,'pay_run_rider','sal.pay_run_rider',@id,@company,@amount,@month)",("@id",id),("@company",company),("@amount",amount),("@month",month));
        return id;
    }
}
