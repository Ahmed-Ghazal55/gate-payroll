using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.RegularExpressions;

[TestClass]
public sealed class SchemaFinAccountTests
{
    static SqliteConnection Open()
    {
        var c=new SqliteConnection("Data Source=:memory:");c.Open();
        using(var fk=c.CreateCommand()){fk.CommandText="PRAGMA foreign_keys=ON;";fk.ExecuteNonQuery();}
        SchemaRef.Ensure(c);SchemaHrCompany.Ensure(c);SchemaOrgCostCentre.Ensure(c);SchemaFinAccount.Ensure(c);
        return c;
    }

    [TestMethod]
    public void EnsureTwiceSeedsTargetChartIdempotently()
    {
        using var c=Open();
        long first=Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM fin_account")!);
        SchemaFinAccount.Ensure(c);
        long second=Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM fin_account")!);
        Assert.AreEqual(first,second);
        Assert.AreEqual(SchemaFinAccount.ExpectedSeedCount(),(int)first);
        Assert.IsTrue(first>200);
        Assert.AreEqual(SchemaFinAccount.StableId("1").ToString("D").ToLowerInvariant(),
            ((string)SchemaSql.Scalar(c,null,"SELECT lower(id) FROM fin_account WHERE code='1'")!).ToLowerInvariant());
        Assert.AreEqual(0L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM fin_account WHERE is_posting=1 AND posting_id IS NULL")!));
        Assert.AreEqual(0L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM fin_account WHERE is_posting=0 AND posting_id IS NOT NULL")!));
        Assert.IsTrue(Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM fin_account WHERE is_posting=1")!)>0);
    }

    [TestMethod]
    public void CodeUniqueNatureAndPostingChecksRejectBadRows()
    {
        using var c=Open();
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO fin_account(id,code,name_ar,account_type,nature,is_posting,posting_id,status,is_active,company_scope,level_no,created_at) VALUES(@id,'1','dup',1,1,0,NULL,1,1,0,1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId()))));
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO fin_account(id,code,name_ar,account_type,nature,is_posting,posting_id,status,is_active,company_scope,level_no,created_at) VALUES(@id,'9',N'x',1,9,0,NULL,1,1,0,1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId()))));
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO fin_account(id,code,name_ar,account_type,nature,is_posting,posting_id,status,is_active,company_scope,level_no,created_at) VALUES(@id,'999901',N'bad',1,1,1,NULL,1,1,0,1,datetime('now'))",
            ("@id",id)));
    }

    [TestMethod]
    public void StoreStartupSeedsTargetChartWithoutTouchingLegacyRecords()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-fin-acc-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            Assert.IsTrue(store.DataPreservationVerified);
            store.Read(c=>{
                Assert.AreEqual(SchemaFinAccount.ExpectedSeedCount(),Convert.ToInt32(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM fin_account")!));
                Assert.AreEqual(0L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM records WHERE kind IN ('journal','account')")!));
                return true;
            });
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    [TestMethod]
    public void SqlServerFreshRepeatAndUniqueCode()
    {
        if(!TrySql(out var builder,out var admin))return;
        string db="GateFinAcc_"+Guid.NewGuid().ToString("N");
        string dir=Path.Combine(Path.GetTempPath(),db);Directory.CreateDirectory(dir);
        using(var q=admin.CreateCommand()){q.CommandText="CREATE DATABASE ["+db+"]";q.ExecuteNonQuery();}
        builder.InitialCatalog=db;
        try
        {
            var first=new Store(dir,builder.ToString());
            var second=new Store(dir,builder.ToString());
            Assert.IsTrue(first.DataPreservationVerified);Assert.IsTrue(second.DataPreservationVerified);
            first.Read(c=>{
                Assert.AreEqual(SchemaFinAccount.ExpectedSeedCount(),Convert.ToInt32(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM fin.account")!));
                Assert.ThrowsExactly<SqlException>(()=>SchemaSql.Exec(c,null,
                    "INSERT INTO fin.account(id,code,name_ar,account_type,nature,is_posting,posting_id,status,is_active,company_scope,level_no,created_at) VALUES(@id,'1',N'dup',1,1,0,NULL,1,1,0,1,SYSUTCDATETIME())",
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId()))));
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
