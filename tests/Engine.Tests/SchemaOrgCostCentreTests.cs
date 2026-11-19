using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaOrgCostCentreTests
{
    static SqliteConnection OpenMasters()
    {
        var c=new SqliteConnection("Data Source=:memory:");c.Open();
        using(var fk=c.CreateCommand()){fk.CommandText="PRAGMA foreign_keys=ON;";fk.ExecuteNonQuery();}
        SchemaRef.Ensure(c);SchemaHrCompany.Ensure(c);SchemaOrgCostCentre.Ensure(c);
        return c;
    }

    [TestMethod]
    public void EnsureTwiceSeedsNoneSentinelOnly()
    {
        using var c=OpenMasters();
        SchemaOrgCostCentre.Ensure(c);
        Assert.AreEqual(1L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM org_cost_centre WHERE code='none'")!));
        Assert.AreEqual(SchemaOrgCostCentre.NoneId.ToString("D").ToLowerInvariant(),
            ((string)SchemaSql.Scalar(c,null,"SELECT lower(id) FROM org_cost_centre WHERE code='none'")!).ToLowerInvariant());
        Assert.AreEqual(0L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM sqlite_master WHERE name LIKE '%period_balance%'")!));
    }

    [TestMethod]
    public void KindCheckAndCompanyFkRejectBadRows()
    {
        using var c=OpenMasters();
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO org_cost_centre(id,code,name_ar,kind,status,created_at) VALUES(@id,'bad','x','warehouse',1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId()))));
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO org_cost_centre(id,code,name_ar,kind,company_id,status,created_at) VALUES(@id,'c1','x','city',@cid,1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",SchemaSql.GuidText(SchemaSql.NewId()))));
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO org_cost_centre(id,code,name_ar,kind,status,created_at) VALUES(@id,'none','dup','none',1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId()))));
    }

    [TestMethod]
    public void StoreStartupHasNoneCostCentre()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-org-cc-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            Assert.IsTrue(store.DataPreservationVerified);
            store.Read(c=>{
                Assert.AreEqual(1L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM org_cost_centre WHERE code='none'")!));
                return true;
            });
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
