using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.RegularExpressions;

[TestClass]
public sealed class SchemaFinPeriodBalanceTests
{
    static SqliteConnection Open()
    {
        var c=new SqliteConnection("Data Source=:memory:");c.Open();
        using(var fk=c.CreateCommand()){fk.CommandText="PRAGMA foreign_keys=ON;";fk.ExecuteNonQuery();}
        SchemaRef.Ensure(c);SchemaHrCompany.Ensure(c);SchemaOrgCostCentre.Ensure(c);
        SchemaFinAccount.Ensure(c);SchemaFinPeriod.Ensure(c);SchemaFinJournal.Ensure(c);SchemaFinPeriodBalance.Ensure(c);
        return c;
    }

    static (string Company,string Period,string Account) SeedKeys(SqliteConnection c)
    {
        string company=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,null,"INSERT INTO hr_company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'B1','UB1','n',1,datetime('now'))",("@id",company));
        string period=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@cid,'2026-11-01',1,datetime('now'))",("@id",period),("@cid",company));
        string account=(string)SchemaSql.Scalar(c,null,"SELECT id FROM fin_account WHERE is_posting=1 ORDER BY code LIMIT 1")!;
        return (company,period,account);
    }

    [TestMethod]
    public void EnsureTwiceIsIdempotentEmpty()
    {
        using var c=Open();
        SchemaFinPeriodBalance.Ensure(c);
        Assert.AreEqual(0L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM fin_period_balance")!));
        Assert.IsTrue(SchemaSql.TableExists(c,null,"fin","period_balance"));
    }

    [TestMethod]
    public void DimensionKeyRequiredAndGrainUnique()
    {
        using var c=Open();
        var k=SeedKeys(c);
        byte[] dim=Enumerable.Range(1,32).Select(i=>(byte)i).ToArray();
        string cc=SchemaSql.GuidText(SchemaOrgCostCentre.NoneId);
        SchemaSql.Exec(c,null,"""
            INSERT INTO fin_period_balance(id,period_id,period_month,company_id,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
            VALUES(@id,@pid,'2026-11-01',@cid,@aid,@cc,'none',@dim,0,0,0,0,datetime('now'))
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pid",k.Period),("@cid",k.Company),("@aid",k.Account),("@cc",cc),("@dim",dim));
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,"""
            INSERT INTO fin_period_balance(id,period_id,period_month,company_id,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
            VALUES(@id,@pid,'2026-11-01',@cid,@aid,@cc,'none',@dim,0,1,0,1,datetime('now'))
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pid",k.Period),("@cid",k.Company),("@aid",k.Account),("@cc",cc),("@dim",dim)));
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,"""
            INSERT INTO fin_period_balance(id,period_id,period_month,company_id,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
            VALUES(@id,@pid,'2026-11-01',@cid,@aid,@cc,'',@dim2,0,0,0,0,datetime('now'))
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pid",k.Period),("@cid",k.Company),("@aid",k.Account),("@cc",cc),
            ("@dim2",Enumerable.Range(2,32).Select(i=>(byte)i).ToArray())));
    }

    [TestMethod]
    public void StoreStartupCreatesEmptyBalanceTable()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-fin-pb-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            store.Read(c=>{
                Assert.IsTrue(SchemaSql.TableExists(c,null,"fin","period_balance"));
                Assert.AreEqual(0L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM fin_period_balance")!));
                return true;
            });
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    [TestMethod]
    public void SqlServerFreshRepeatAndGrainUnique()
    {
        if(!TrySql(out var builder,out var admin))return;
        string db="GateFinPb_"+Guid.NewGuid().ToString("N");
        string dir=Path.Combine(Path.GetTempPath(),db);Directory.CreateDirectory(dir);
        using(var q=admin.CreateCommand()){q.CommandText="CREATE DATABASE ["+db+"]";q.ExecuteNonQuery();}
        builder.InitialCatalog=db;
        try
        {
            var a=new Store(dir,builder.ToString());var b=new Store(dir,builder.ToString());
            Assert.IsTrue(a.DataPreservationVerified);Assert.IsTrue(b.DataPreservationVerified);
            a.Read(c=>{
                string company=SchemaSql.GuidText(SchemaSql.NewId());
                SchemaSql.Exec(c,null,"INSERT INTO hr.company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'B1','UB1',N'n',1,SYSUTCDATETIME())",("@id",company));
                string period=SchemaSql.GuidText(SchemaSql.NewId());
                SchemaSql.Exec(c,null,"INSERT INTO fin.accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@cid,'2026-11-01',1,SYSUTCDATETIME())",("@id",period),("@cid",company));
                string account=(string)SchemaSql.Scalar(c,null,"SELECT TOP 1 CONVERT(varchar(36),id) FROM fin.account WHERE is_posting=1 ORDER BY code")!;
                byte[] dim=Enumerable.Range(1,32).Select(i=>(byte)i).ToArray();
                string cc=SchemaSql.GuidText(SchemaOrgCostCentre.NoneId);
                SchemaSql.Exec(c,null,"""
                    INSERT INTO fin.period_balance(id,period_id,period_month,company_id,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
                    VALUES(@id,@pid,'2026-11-01',@cid,@aid,@cc,'none',@dim,0,0,0,0,SYSUTCDATETIME())
                    """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pid",period),("@cid",company),("@aid",account),("@cc",cc),("@dim",dim));
                Assert.ThrowsExactly<SqlException>(()=>SchemaSql.Exec(c,null,"""
                    INSERT INTO fin.period_balance(id,period_id,period_month,company_id,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
                    VALUES(@id,@pid,'2026-11-01',@cid,@aid,@cc,'none',@dim,0,1,0,1,SYSUTCDATETIME())
                    """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pid",period),("@cid",company),("@aid",account),("@cc",cc),("@dim",dim)));
                return true;
            });
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using(var q=admin.CreateCommand()){q.CommandText="ALTER DATABASE ["+db+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+db+"]";q.ExecuteNonQuery();}
            if(Directory.Exists(dir))Directory.Delete(dir,true);admin.Dispose();
        }
    }

    static bool TrySql(out SqlConnectionStringBuilder builder,out SqlConnection admin)
    {
        builder=new SqlConnectionStringBuilder();admin=null!;
        string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if(string.IsNullOrWhiteSpace(configured)){Assert.Inconclusive("Explicit isolated loopback SQL required.");return false;}
        builder=new SqlConnectionStringBuilder(configured);
        Assert.IsTrue(Regex.IsMatch(builder.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",RegexOptions.IgnoreCase));
        builder.InitialCatalog="master";admin=new SqlConnection(builder.ToString());admin.Open();return true;
    }
}
