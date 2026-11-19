using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.RegularExpressions;

[TestClass]
public sealed class SchemaSqlServerTests
{
    static bool TryOpenAdmin(out SqlConnectionStringBuilder builder,out SqlConnection admin)
    {
        builder=new SqlConnectionStringBuilder();
        admin=null!;
        string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if(string.IsNullOrWhiteSpace(configured)){Assert.Inconclusive("Explicit isolated loopback SQL required.");return false;}
        builder=new SqlConnectionStringBuilder(configured);
        Assert.IsTrue(Regex.IsMatch(builder.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",RegexOptions.IgnoreCase));
        builder.InitialCatalog="master";
        admin=new SqlConnection(builder.ToString());
        admin.Open();
        return true;
    }

    static string CreateDb(SqlConnection admin,SqlConnectionStringBuilder builder,out string dir)
    {
        string db="GateSchemaM1F_"+Guid.NewGuid().ToString("N");
        dir=Path.Combine(Path.GetTempPath(),db);
        Directory.CreateDirectory(dir);
        using(var q=admin.CreateCommand()){q.CommandText="CREATE DATABASE ["+db+"]";q.ExecuteNonQuery();}
        builder.InitialCatalog=db;
        return db;
    }

    static void DropDb(SqlConnection admin,string db,string dir)
    {
        SqlConnection.ClearAllPools();
        using var q=admin.CreateCommand();
        q.CommandText="ALTER DATABASE ["+db+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+db+"]";
        q.ExecuteNonQuery();
        if(Directory.Exists(dir))Directory.Delete(dir,true);
    }

    [TestMethod]
    public void SqlServerFreshAndRepeatStartAreIdempotent()
    {
        if(!TryOpenAdmin(out var builder,out var admin))return;
        string db=CreateDb(admin,builder,out string dir);
        try
        {
            var first=new Store(dir,builder.ToString());
            Assert.IsTrue(first.DataPreservationVerified);
            var second=new Store(dir,builder.ToString());
            Assert.IsTrue(second.DataPreservationVerified);
            first.Read(c=>{
                Assert.IsTrue(SchemaSql.TableExists(c,null,"hr","company"));
                Assert.IsTrue(SchemaSql.TableExists(c,null,"ref","legacy_key_map"));
                Assert.IsTrue(SchemaSql.TableExists(c,null,"ref","legacy_account_map"));
                Assert.IsTrue(SchemaSql.TableExists(c,null,"form","campaign"));
                Assert.AreEqual(0,Convert.ToInt32(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM ref.legacy_key_map")!));
                Assert.AreEqual(0,Convert.ToInt32(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM hr.company")!));
                return true;
            });
        }
        finally{DropDb(admin,db,dir);admin.Dispose();}
    }

    [TestMethod]
    public void SqlServerConstraintsAndFilteredUnique()
    {
        if(!TryOpenAdmin(out var builder,out var admin))return;
        string db=CreateDb(admin,builder,out string dir);
        try
        {
            var store=new Store(dir,builder.ToString());
            store.Read(c=>{
                Assert.ThrowsExactly<SqlException>(()=>SchemaSql.Exec(c,null,
                    "INSERT INTO hr.company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'X1','U1','n',9,SYSUTCDATETIME())",
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId()))));
                Assert.ThrowsExactly<SqlException>(()=>SchemaSql.Exec(c,null,
                    "INSERT INTO ref.legacy_account_map(id,legacy_code,target_code,company_id,created_at) VALUES(@id,'L1','T1',@cid,SYSUTCDATETIME())",
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",SchemaSql.GuidText(SchemaSql.NewId()))));
                SchemaSql.Exec(c,null,"INSERT INTO hr.company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'A1','UA1','n',1,SYSUTCDATETIME())",
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())));
                SchemaSql.Exec(c,null,"INSERT INTO hr.company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'A2','UA2','n',1,SYSUTCDATETIME())",
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())));
                Assert.AreEqual(2,Convert.ToInt32(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM hr.company WHERE erp_sponsor_id IS NULL")!));
                SchemaSql.Exec(c,null,"INSERT INTO form.campaign(id,title,kind,status,created_at) VALUES(@id,'d1','iban','draft',SYSUTCDATETIME())",
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())));
                SchemaSql.Exec(c,null,"INSERT INTO form.campaign(id,title,kind,status,created_at) VALUES(@id,'d2','iban','draft',SYSUTCDATETIME())",
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())));
                Assert.AreEqual(2,Convert.ToInt32(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM form.campaign WHERE sharing_token IS NULL")!));
                string erp=SchemaSql.GuidText(SchemaSql.NewId());
                SchemaSql.Exec(c,null,"UPDATE hr.company SET erp_sponsor_id=@erp WHERE code='A1'",("@erp",erp));
                Assert.ThrowsExactly<SqlException>(()=>SchemaSql.Exec(c,null,"UPDATE hr.company SET erp_sponsor_id=@erp WHERE code='A2'",("@erp",erp)));
                return true;
            });
        }
        finally{DropDb(admin,db,dir);admin.Dispose();}
    }

    [TestMethod]
    public void SqlServerConcurrentStartsDoNotFail()
    {
        if(!TryOpenAdmin(out var builder,out var admin))return;
        string db=CreateDb(admin,builder,out string dir);
        try
        {
            string cs=builder.ToString();
            Exception?[] errors=new Exception?[4];
            var threads=Enumerable.Range(0,4).Select(i=>new Thread(()=>
            {
                try{var store=new Store(dir,cs);Assert.IsTrue(store.DataPreservationVerified);}
                catch(Exception ex){errors[i]=ex;}
            })).ToArray();
            foreach(var t in threads)t.Start();
            foreach(var t in threads)t.Join();
            Assert.IsTrue(errors.All(e=>e is null),string.Join("; ",errors.Where(e=>e!=null).Select(e=>e!.GetType().Name+": "+e.Message)));
        }
        finally{DropDb(admin,db,dir);admin.Dispose();}
    }
}
