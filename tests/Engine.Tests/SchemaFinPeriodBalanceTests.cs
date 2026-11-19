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

    static (string Company,string Period,string Account,Guid CompanyG,Guid AccountG) SeedKeys(SqliteConnection c)
    {
        Guid companyG=SchemaSql.NewId();
        string company=SchemaSql.GuidText(companyG);
        SchemaSql.Exec(c,null,"INSERT INTO hr_company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'B1','UB1','n',1,datetime('now'))",("@id",company));
        string period=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@cid,'2026-11-01',1,datetime('now'))",("@id",period),("@cid",company));
        string account=(string)SchemaSql.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=1 ORDER BY code LIMIT 1")!;
        Guid accountG=Guid.Parse(account);
        return (company,period,account,companyG,accountG);
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
    public void DimensionKeyLengthGrainAndRawDimsUnique()
    {
        using var c=Open();
        var k=SeedKeys(c);
        Guid ccG=SchemaOrgCostCentre.NoneId;
        string cc=SchemaSql.GuidText(ccG);
        byte[] dim=SchemaFinPeriodBalance.ComputeDimensionKey(k.CompanyG,k.AccountG,ccG,"none");
        Assert.AreEqual(32,dim.Length);
        SchemaSql.Exec(c,null,"""
            INSERT INTO fin_period_balance(id,period_id,period_month,company_id,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
            VALUES(@id,@pid,'2026-11-01',@cid,@aid,@cc,'none',@dim,0,0,0,0,datetime('now'))
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pid",k.Period),("@cid",k.Company),("@aid",k.Account),("@cc",cc),("@dim",dim));
        // Same hash → grain unique
        SchemaConstraintAssert.ThrowsNamed(()=>SchemaSql.Exec(c,null,"""
            INSERT INTO fin_period_balance(id,period_id,period_month,company_id,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
            VALUES(@id,@pid,@pm,@cid,@aid,@cc,'none',@dim,0,1,0,1,datetime('now'))
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pid",k.Period),("@pm","2026-11-01"),("@cid",k.Company),("@aid",k.Account),("@cc",cc),("@dim",dim)),
            "UQ_fin_period_balance_grain",sqliteToken:"fin_period_balance.period_id, fin_period_balance.dimension_key");
        // Same raw dims with a different 32-byte hash still rejected by UQ_fin_period_balance_dims
        byte[] otherHash=Enumerable.Range(1,32).Select(i=>(byte)i).ToArray();
        SchemaConstraintAssert.ThrowsNamed(()=>SchemaSql.Exec(c,null,"""
            INSERT INTO fin_period_balance(id,period_id,period_month,company_id,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
            VALUES(@id,@pid,@pm,@cid,@aid,@cc,'none',@dim2,0,2,0,2,datetime('now'))
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pid",k.Period),("@pm","2026-11-01"),("@cid",k.Company),("@aid",k.Account),("@cc",cc),("@dim2",otherHash)),
            "UQ_fin_period_balance_dims",sqliteToken:"fin_period_balance.period_id, fin_period_balance.company_id, fin_period_balance.account_id, fin_period_balance.cost_centre_id, fin_period_balance.app_code");
        // Short hash rejected by length CHECK
        SchemaConstraintAssert.ThrowsNamed(()=>SchemaSql.Exec(c,null,"""
            INSERT INTO fin_period_balance(id,period_id,period_month,company_id,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
            VALUES(@id,@pid,@pm,@cid,@aid,@cc,'ninja',@short,0,0,0,0,datetime('now'))
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pid",k.Period),("@pm","2026-11-01"),("@cid",k.Company),("@aid",k.Account),("@cc",cc),
            ("@short",new byte[]{1,2,3})),
            "CK_fin_period_balance_dim_len");
        SchemaConstraintAssert.ThrowsNamed(()=>SchemaSql.Exec(c,null,"""
            INSERT INTO fin_period_balance(id,period_id,period_month,company_id,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
            VALUES(@id,@pid,@pm,@cid,@aid,@cc,'',@dim2,0,0,0,0,datetime('now'))
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pid",k.Period),("@pm","2026-11-01"),("@cid",k.Company),("@aid",k.Account),("@cc",cc),
            ("@dim2",SchemaFinPeriodBalance.ComputeDimensionKey(k.CompanyG,k.AccountG,ccG,""))),
            "CK_fin_period_balance_app");
    }

    [TestMethod]
    public void DimensionKeyHashIsDeterministicAcrossProviders()
    {
        // Fixed inputs — same SHA-256 bytes whether the row lands in SQLite or SQL Server.
        Guid company=Guid.Parse("11111111-1111-7111-8111-111111111111");
        Guid account=Guid.Parse("22222222-2222-7222-8222-222222222222");
        Guid cc=SchemaOrgCostCentre.NoneId;
        byte[] expected=SchemaFinPeriodBalance.ComputeDimensionKey(company,account,cc,"none");
        Assert.AreEqual(32,expected.Length);
        Assert.AreEqual(
            $"11111111-1111-7111-8111-111111111111|22222222-2222-7222-8222-222222222222|{cc:D}|none",
            SchemaFinPeriodBalance.CanonicalDimensionString(company,account,cc,"none"));

        using var sqlite=Open();
        PersistAndReadHash(sqlite,false,company,account,cc,expected);

        // SQL Server path when PAYROLL_TEST_SQL_CONNECTION is set; SQLite proof above always runs.
        string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if(string.IsNullOrWhiteSpace(configured))return;
        var builder=new SqlConnectionStringBuilder(configured);
        Assert.IsTrue(Regex.IsMatch(builder.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",RegexOptions.IgnoreCase));
        builder.InitialCatalog="master";
        using var admin=new SqlConnection(builder.ToString());admin.Open();
        string db="GateFinPbHash_"+Guid.NewGuid().ToString("N");
        string dir=Path.Combine(Path.GetTempPath(),db);Directory.CreateDirectory(dir);
        using(var q=admin.CreateCommand()){q.CommandText="CREATE DATABASE ["+db+"]";q.ExecuteNonQuery();}
        builder.InitialCatalog=db;
        try
        {
            var store=new Store(dir,builder.ToString());
            store.Read(c=>{PersistAndReadHash(c,true,company,account,cc,expected);return true;});
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using(var q=admin.CreateCommand()){q.CommandText="ALTER DATABASE ["+db+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+db+"]";q.ExecuteNonQuery();}
            if(Directory.Exists(dir))Directory.Delete(dir,true);
        }
    }

    static void PersistAndReadHash(System.Data.Common.DbConnection c,bool sql,Guid company,Guid account,Guid cc,byte[] expected)
    {
        string coTable=sql?"hr.company":"hr_company";
        string perTable=sql?"fin.accounting_period":"fin_accounting_period";
        string accTable=sql?"fin.account":"fin_account";
        string balTable=sql?"fin.period_balance":"fin_period_balance";
        string now=sql?"SYSUTCDATETIME()":"datetime('now')";
        // Ensure FKs: company, period, and a synthetic postable account matching the fixed GUID when possible.
        SchemaSql.Exec(c,null,$"INSERT INTO {coTable}(id,code,unified_number,name_ar,status,created_at) VALUES(@id,@code,@un,'n',1,{now})",
            ("@id",SchemaSql.GuidText(company)),("@code","H"+Guid.NewGuid().ToString("N")[..6]),("@un","U"+Guid.NewGuid().ToString("N")[..6]));
        string period=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,null,$"INSERT INTO {perTable}(id,company_id,period_month,state,created_at) VALUES(@id,@cid,'2026-11-01',1,{now})",
            ("@id",period),("@cid",SchemaSql.GuidText(company)));
        // Use an existing postable account id from seed for FK; hash still uses the fixed account GUID only when inserting with that id.
        // For the cross-provider proof we insert with the fixed GUIDs as the dimension inputs via ComputeDimensionKey,
        // and store a row whose raw dims match those GUIDs — so create a dedicated postable account with that id.
        SchemaSql.Exec(c,null,$"""
            INSERT INTO {accTable}(id,code,name_ar,account_type,nature,is_postable,status,is_active,company_scope,level_no,created_at)
            VALUES(@id,@code,'hash-probe',5,1,1,1,1,0,1,{now})
            """,("@id",SchemaSql.GuidText(account)),("@code","9"+Guid.NewGuid().ToString("N")[..7]));
        byte[] dim=SchemaFinPeriodBalance.ComputeDimensionKey(company,account,cc,"none");
        CollectionAssert.AreEqual(expected,dim);
        SchemaSql.Exec(c,null,$"""
            INSERT INTO {balTable}(id,period_id,period_month,company_id,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
            VALUES(@id,@pid,'2026-11-01',@cid,@aid,@cc,'none',@dim,0,0,0,0,{now})
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pid",period),("@cid",SchemaSql.GuidText(company)),
            ("@aid",SchemaSql.GuidText(account)),("@cc",SchemaSql.GuidText(cc)),("@dim",dim));
        object? raw=SchemaSql.Scalar(c,null,$"SELECT dimension_key FROM {balTable} WHERE company_id=@cid AND account_id=@aid",
            ("@cid",SchemaSql.GuidText(company)),("@aid",SchemaSql.GuidText(account)));
        byte[] stored=raw switch
        {
            byte[] b=>b,
            string s=>Convert.FromHexString(s.Replace("0x","",StringComparison.OrdinalIgnoreCase)),
            _=>throw new AssertFailedException("unexpected dimension_key type: "+(raw?.GetType().FullName??"null"))
        };
        CollectionAssert.AreEqual(expected,stored);
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
                Guid companyG=SchemaSql.NewId();
                string company=SchemaSql.GuidText(companyG);
                SchemaSql.Exec(c,null,"INSERT INTO hr.company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'B1','UB1',N'n',1,SYSUTCDATETIME())",("@id",company));
                string period=SchemaSql.GuidText(SchemaSql.NewId());
                SchemaSql.Exec(c,null,"INSERT INTO fin.accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@cid,'2026-11-01',1,SYSUTCDATETIME())",("@id",period),("@cid",company));
                string account=(string)SchemaSql.Scalar(c,null,"SELECT TOP 1 CAST(varchar(36),id) FROM fin.account WHERE is_postable=1 ORDER BY code")!;
                Guid accountG=Guid.Parse(account);
                Guid ccG=SchemaOrgCostCentre.NoneId;
                string cc=SchemaSql.GuidText(ccG);
                byte[] dim=SchemaFinPeriodBalance.ComputeDimensionKey(companyG,accountG,ccG,"none");
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
