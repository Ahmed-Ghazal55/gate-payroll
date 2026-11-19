using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaRefTests
{
    static SqliteConnection Open()
    {
        var c=new SqliteConnection("Data Source=:memory:");c.Open();
        using(var fk=c.CreateCommand()){fk.CommandText="PRAGMA foreign_keys=ON;";fk.ExecuteNonQuery();}
        return c;
    }

    [TestMethod]
    public void EnsureIsIdempotentAndCreatesRefCatalogues()
    {
        using var c=Open();
        SchemaRef.Ensure(c);
        SchemaRef.Ensure(c);
        Assert.IsTrue(SchemaSql.TableExists(c,null,"ref","business_key_guard"));
        Assert.IsTrue(SchemaSql.TableExists(c,null,"ref","saudi_bank"));
        Assert.IsTrue(SchemaSql.TableExists(c,null,"ref","erp_event_type"));
        Assert.IsTrue(SchemaSql.TableExists(c,null,"ref","setting"));
        Assert.IsTrue(SchemaSql.TableExists(c,null,"ref","legacy_account_map"));
        Assert.IsTrue(Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM ref_saudi_bank")!)>=12);
        Assert.IsTrue(Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM ref_erp_event_type")!)>=4);
    }

    [TestMethod]
    public void BusinessKeyGuardRejectsDuplicateNaturalKey()
    {
        using var c=Open();SchemaRef.Ensure(c);
        var a=SchemaSql.NewId();var b=SchemaSql.NewId();
        SchemaSql.Exec(c,null,"INSERT INTO ref_business_key_guard(id,object_type,business_key,row_id) VALUES(@id,'sal.platform_invoice','app|cr|2026-10',@row)",
            ("@id",a),("@row",a));
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO ref_business_key_guard(id,object_type,business_key,row_id) VALUES(@id,'sal.platform_invoice','app|cr|2026-10',@row)",
            ("@id",b),("@row",b)));
    }

    [TestMethod]
    public void SaudiBankCheckRejectsShortCode()
    {
        using var c=Open();SchemaRef.Ensure(c);
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO ref_saudi_bank(id,code,short_code,name_ar,active,created_at) VALUES(@id,'1','X','x',1,datetime('now'))",
            ("@id",SchemaSql.NewId())));
    }

    [TestMethod]
    public void StoreStartupStillCreatesRecordsAndRef()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-schema-ref-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            Assert.IsTrue(store.DataPreservationVerified);
            store.Read(c=>{
                SchemaRef.Ensure(c);
                Assert.IsTrue(SchemaSql.TableExists(c,null,"ref","business_key_guard"));
                Assert.IsNotNull(Store.List(c,"company"));
                return true;
            });
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
