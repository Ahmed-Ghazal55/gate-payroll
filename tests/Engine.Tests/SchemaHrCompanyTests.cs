using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.Json.Nodes;

[TestClass]
public sealed class SchemaHrCompanyTests
{
    static SqliteConnection Open()
    {
        var c=new SqliteConnection("Data Source=:memory:");c.Open();
        using(var fk=c.CreateCommand()){fk.CommandText="PRAGMA foreign_keys=ON;";fk.ExecuteNonQuery();}
        using(var records=c.CreateCommand()){records.CommandText="CREATE TABLE records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL);";records.ExecuteNonQuery();}
        return c;
    }

    [TestMethod]
    public void EnsureIdempotentConstraintsAndBackfill()
    {
        using var c=Open();
        using(var tx=c.BeginTransaction())
        {
            var companyId=Guid.Parse("019c18d5-62e1-7000-8000-000000000041");
            Store.Save(c,"company",new JsonObject{
                ["id"]=companyId.ToString("D"),["sponsorId"]=companyId.ToString("D"),["code"]="BW",
                ["unifiedNumber"]="7015658094",["nameAr"]="شركة اختبار",["status"]="active",
                ["activities"]=new JsonArray(new JsonObject{["isicCode"]="4923",["nameAr"]="نقل"})
            },tx);
            Store.Save(c,"commercial_registration",new JsonObject{
                ["id"]="cr:"+Guid.NewGuid().ToString("N"),["companyId"]=companyId.ToString("D"),
                ["number"]="4030362130",["status"]="active"
            },tx);
            tx.Commit();
        }

        SchemaRef.Ensure(c);
        SchemaHrCompany.Ensure(c);
        SchemaHrCompany.Ensure(c);

        Assert.AreEqual(1L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM hr_company WHERE code='BW'")!));
        Assert.AreEqual(1L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM hr_commercial_registration WHERE cr_number='4030362130'")!));
        Assert.AreEqual(1L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM hr_company_activity WHERE isic_code='4923'")!));
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
    public void StoreStartupCreatesCompanyMasters()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-hr-company-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            Companies.Seed(store);
            Assert.IsTrue(store.DataPreservationVerified);
            // Seed runs after Store ctor Ensure; re-run Ensure to copy seeded kinds.
            store.Write((c,tx)=>{SchemaHrCompany.Ensure(c,tx);return true;});
            store.Read(c=>{
                Assert.IsTrue(SchemaSql.TableExists(c,null,"hr","company"));
                Assert.IsTrue(Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM hr_company")!)>=3);
                return true;
            });
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
