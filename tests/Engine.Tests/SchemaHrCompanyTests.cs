using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaHrCompanyTests
{
    static SqliteConnection Open()
    {
        var c=new SqliteConnection("Data Source=:memory:");c.Open();
        using(var fk=c.CreateCommand()){fk.CommandText="PRAGMA foreign_keys=ON;";fk.ExecuteNonQuery();}
        return c;
    }

    [TestMethod]
    public void EnsureIdempotentCreatesEmptyCompanyTables()
    {
        using var c=Open();
        SchemaRef.Ensure(c);
        SchemaHrCompany.Ensure(c);
        SchemaHrCompany.Ensure(c);
        Assert.IsTrue(SchemaSql.TableExists(c,null,"hr","company"));
        Assert.IsTrue(SchemaSql.TableExists(c,null,"hr","commercial_registration"));
        Assert.AreEqual(0L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM hr_company")!));
    }

    [TestMethod]
    public void FkAndUniqueAndCheckRejectBadRows()
    {
        using var c=Open();
        SchemaRef.Ensure(c);SchemaHrCompany.Ensure(c);
        string companyId=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,null,"INSERT INTO hr_company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'X1','U1','n',1,datetime('now'))",("@id",companyId));

        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO hr_company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'X1','U2','n',1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId()))));

        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO hr_company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'X2','U3','n',9,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId()))));

        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO hr_commercial_registration(id,company_id,cr_number,status,created_at) VALUES(@id,@cid,'999',1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",SchemaSql.GuidText(SchemaSql.NewId()))));
    }

    [TestMethod]
    public void FilteredUniqueAllowsTwoNullErpSponsorIds()
    {
        using var c=Open();
        SchemaRef.Ensure(c);SchemaHrCompany.Ensure(c);
        SchemaSql.Exec(c,null,"INSERT INTO hr_company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'A1','UA1','n',1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())));
        SchemaSql.Exec(c,null,"INSERT INTO hr_company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'A2','UA2','n',1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())));
        Assert.AreEqual(2L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM hr_company WHERE erp_sponsor_id IS NULL")!));
        string erp=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,null,"UPDATE hr_company SET erp_sponsor_id=@erp WHERE code='A1'",("@erp",erp));
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,"UPDATE hr_company SET erp_sponsor_id=@erp WHERE code='A2'",("@erp",erp)));
    }

    [TestMethod]
    public void StoreStartupCreatesEmptyCompanyMasters()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-hr-company-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            Companies.Seed(store);
            Assert.IsTrue(store.DataPreservationVerified);
            store.Read(c=>{
                Assert.IsTrue(SchemaSql.TableExists(c,null,"hr","company"));
                Assert.AreEqual(0L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM hr_company")!));
                Assert.IsTrue(Store.List(c,"company").Count>=3);
                return true;
            });
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
