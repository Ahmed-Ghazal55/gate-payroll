using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.RegularExpressions;

[TestClass]
public sealed class SchemaFinPeriodTests
{
    static SqliteConnection Open()
    {
        var c=new SqliteConnection("Data Source=:memory:");c.Open();
        using(var fk=c.CreateCommand()){fk.CommandText="PRAGMA foreign_keys=ON;";fk.ExecuteNonQuery();}
        SchemaRef.Ensure(c);SchemaHrCompany.Ensure(c);SchemaOrgCostCentre.Ensure(c);
        SchemaFinAccount.Ensure(c);SchemaFinPeriod.Ensure(c);
        return c;
    }

    static string InsertCompany(SqliteConnection c)
    {
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,null,"INSERT INTO hr_company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'P1','UP1','n',1,datetime('now'))",("@id",id));
        return id;
    }

    [TestMethod]
    public void EnsureTwiceIsIdempotentEmpty()
    {
        using var c=Open();
        SchemaFinPeriod.Ensure(c);
        Assert.AreEqual(0L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM fin_accounting_period")!));
        Assert.IsTrue(SchemaSql.TableExists(c,null,"fin","accounting_period"));
    }

    [TestMethod]
    public void StateCheckUniqueFirstOfMonthAndCompanyFkRejectBadRows()
    {
        using var c=Open();
        string company=InsertCompany(c);
        SchemaSql.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@cid,'2026-11-01',1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",company));
        SchemaConstraintAssert.ThrowsNamed(()=>SchemaSql.Exec(c,null,
            "INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@cid,@m,1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",company),("@m","2026-11-01")),
            "UQ_fin_accounting_period",sqliteToken:"fin_accounting_period.company_id, fin_accounting_period.period_month");
        // Mid-month is not a valid period_start (must be first day) — closes the same-month duplicate loophole.
        SchemaConstraintAssert.ThrowsNamed(()=>SchemaSql.Exec(c,null,
            "INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@cid,@m,1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",company),("@m","2026-11-15")),
            "CK_fin_accounting_period_start");
        // Same calendar day in a non-canonical textual form is rejected (cannot defeat unique via variant text).
        SchemaConstraintAssert.ThrowsNamed(()=>SchemaSql.Exec(c,null,
            "INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@cid,@m,1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",company),("@m","2026-11-01 00:00:00")),
            "CK_fin_accounting_period_start");
        SchemaConstraintAssert.ThrowsNamed(()=>SchemaSql.Exec(c,null,
            "INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@cid,@m,@state,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",company),("@m","2026-12-01"),("@state",9)),
            "CK_fin_accounting_period_state");
        SchemaConstraintAssert.ThrowsNamed(()=>SchemaSql.Exec(c,null,
            "INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@cid,@m,1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",SchemaSql.GuidText(SchemaSql.NewId())),("@m","2026-12-01")),
            "FK_fin_period_company",sqliteToken:"FOREIGN KEY");
        SchemaSql.Exec(c,null,"UPDATE fin_accounting_period SET state=2 WHERE company_id=@cid",("@cid",company));
        Assert.AreEqual(2L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT state FROM fin_accounting_period WHERE company_id=@cid",("@cid",company))!));
    }

    [TestMethod]
    public void StoreStartupCreatesPeriodTable()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-fin-per-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            store.Read(c=>{Assert.IsTrue(SchemaSql.TableExists(c,null,"fin","accounting_period"));return true;});
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    [TestMethod]
    public void SqlServerFreshRepeatAndStateCheck()
    {
        if(!TrySql(out var builder,out var admin))return;
        string db="GateFinPer_"+Guid.NewGuid().ToString("N");
        string dir=Path.Combine(Path.GetTempPath(),db);Directory.CreateDirectory(dir);
        using(var q=admin.CreateCommand()){q.CommandText="CREATE DATABASE ["+db+"]";q.ExecuteNonQuery();}
        builder.InitialCatalog=db;
        try
        {
            var a=new Store(dir,builder.ToString());var b=new Store(dir,builder.ToString());
            Assert.IsTrue(a.DataPreservationVerified);Assert.IsTrue(b.DataPreservationVerified);
            a.Read(c=>{
                string company=SchemaSql.GuidText(SchemaSql.NewId());
                SchemaSql.Exec(c,null,"INSERT INTO hr.company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'P1','UP1',N'n',1,SYSUTCDATETIME())",("@id",company));
                SchemaSql.Exec(c,null,"INSERT INTO fin.accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@cid,'2026-11-01',1,SYSUTCDATETIME())",
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",company));
                Assert.ThrowsExactly<SqlException>(()=>SchemaSql.Exec(c,null,
                    "INSERT INTO fin.accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@cid,'2026-12-01',3,SYSUTCDATETIME())",
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",company)));
                Assert.ThrowsExactly<SqlException>(()=>SchemaSql.Exec(c,null,
                    "INSERT INTO fin.accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@cid,@m,1,SYSUTCDATETIME())",
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",company),("@m","2026-11-15")));
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
