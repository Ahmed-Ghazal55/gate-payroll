using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Data.Common;
using System.Text.RegularExpressions;
using Payroll;

internal static class MoneySchemaFixture
{
    public static string Sql(DbConnection c,string sql,DbTransaction? tx=null)
    {
        if(!SchemaSql.IsSql(c))return sql;
        var tables=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx; // SQL Server rejects a command outside the connection's open transaction
            cmd.CommandText="SELECT s.name+'_'+t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id UNION ALL SELECT s.name+'_'+v.name FROM sys.views v JOIN sys.schemas s ON s.schema_id=v.schema_id";
            using var reader=cmd.ExecuteReader();while(reader.Read())tables.Add(reader.GetString(0));
        }
        sql=sql.Replace("datetime('now')","SYSUTCDATETIME()").Replace("randomblob(32)","CRYPT_GEN_RANDOM(32)");
        return RewriteTables(sql,tables.Contains,(schema,table)=>SchemaSql.Q(c,schema,table));
    }
    /// <summary>Rewrite SQLite-style schema_table names to SQL Server names. Only names of existing tables are
    /// rewritten; string literals and columns such as pay_run_id are left alone.</summary>
    public static string RewriteTables(string sql,Func<string,bool> isTable,Func<string,string,string> quote)=>
        Regex.Replace(sql,@"'(?:[^']|'')*'|\b(pay|sal|fin|hr|org|ref|debt|treasury|auth)_([a-z][a-z0-9_]*)\b",m=>
            m.Value.StartsWith('\'')||!isTable(m.Value)?m.Value:quote(m.Groups[1].Value,m.Groups[2].Value));
    public static void Exec(DbConnection c,DbTransaction? tx,string sql,params (string Name,object Value)[] values)=>SchemaSql.Exec(c,tx,Sql(c,sql,tx),values);
    public static object? Scalar(DbConnection c,DbTransaction? tx,string sql,params (string Name,object Value)[] values)
    {
        object? value=SchemaSql.Scalar(c,tx,Sql(c,sql,tx),values);
        return value switch { int n=>(long)n, byte n=>(long)n, bool b=>b?1L:0L, Guid id=>id.ToString("D"), _=>value };
    }
    public static DbCommand Command(DbConnection c,DbTransaction? tx,string sql,params (string Name,object Value)[] values)=>Store.Command(c,tx,Sql(c,sql,tx),values);
    /// <summary>Reject must name the constraint or trigger message (finding L: Reject accepted any DbException).</summary>
    public static void Reject(Action action,string constraintName,string? sqliteToken=null)=>
        SchemaConstraintAssert.ThrowsNamed(action,constraintName,sqliteToken);
    /// <summary>When several BEFORE triggers can abort the same statement, accept any of the named messages.</summary>
    public static void RejectAny(Action action,params string[] constraintNames)=>
        SchemaConstraintAssert.ThrowsAny(action,constraintNames);
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
        // hr.company code and unified_number are varchar(20); a unified number is 10 digits starting with 7.
        string id=Id(),unified="7"+System.Security.Cryptography.RandomNumberGenerator.GetInt32(0,1_000_000_000).ToString("D9");
        Exec(c,null,"INSERT INTO hr_company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,@code,@unified,'Synthetic company',1,datetime('now'))",("@id",id),("@code","SYN"+unified),("@unified",unified));
        return id;
    }
    public static DbConnection Payments(bool sqlServer=false)
    {
        var c=Open(sqlServer);SchemaOrgCostCentre.Ensure(c);SchemaFinAccount.Ensure(c);SchemaFinPeriod.Ensure(c);SchemaFinJournal.Ensure(c);SchemaPayAllocation.Ensure(c);
        return c;
    }
    public static string Payment(DbConnection c,string company,string month="2026-11-01",string? payer=null)
    {
        string id=Id();
        Exec(c,null,"INSERT INTO pay_payment(id,payment_no,company_id,payer_company_id,period_month,paid_on,amount) VALUES(@id,@id,@company,@payer,@month,@month,100)",("@id",id),("@company",company),("@payer",payer??company),("@month",month));
        return id;
    }
    // Build actual linked reversal evidence before the state change that releases posted claims.
    public static void PreparePaymentReversal(DbConnection c,string payment)
    {
        string company=(string)Scalar(c,null,"SELECT company_id FROM pay_payment WHERE id=@id",("@id",payment))!;
        string period=Id(),original=Id(),reverse=Id();
        Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) SELECT @id,@c,'2026-11-01',1,datetime('now') WHERE NOT EXISTS(SELECT 1 FROM fin_accounting_period WHERE company_id=@c AND period_month='2026-11-01')",("@id",period),("@c",company));
        period=(string)Scalar(c,null,"SELECT id FROM fin_accounting_period WHERE company_id=@c AND period_month='2026-11-01'",("@c",company))!;
        void Journal(string id,string purpose,object reverses,int status)=>Exec(c,null,"INSERT INTO fin_journal(id,voucher_no,period_id,period_month,journal_date,source_type,source_id,company_id,purpose,status,reverses_journal_id,created_at) VALUES(@id,@id,@period,'2026-11-01','2026-11-01','payment',@payment,@c,@purpose,@status,@reverses,datetime('now'))",("@id",id),("@period",period),("@payment",payment),("@c",company),("@purpose",purpose),("@status",status),("@reverses",reverses));
        Journal(original,"payment",DBNull.Value,3);
        Exec(c,null,"UPDATE pay_payment SET journal_id=@j,status=2 WHERE id=@id",("@j",original),("@id",payment));
        Journal(reverse,"reversal",original,2);
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
        Exec(c,null,"INSERT INTO pay_payable(id,kind,source_table,source_id,company_id,amount_due,period_month) VALUES(@id,'document_line','synthetic.document_line',@id,@company,@amount,@month)",("@id",id),("@company",company),("@amount",amount),("@month",month));
        return id;
    }
}
