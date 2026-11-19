using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.Json.Nodes;

[TestClass]
public sealed class SchemaFormAppTests
{
    static SqliteConnection Open()
    {
        var c=new SqliteConnection("Data Source=:memory:");c.Open();
        using(var fk=c.CreateCommand()){fk.CommandText="PRAGMA foreign_keys=ON;";fk.ExecuteNonQuery();}
        using(var records=c.CreateCommand()){records.CommandText="CREATE TABLE records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL);";records.ExecuteNonQuery();}
        return c;
    }

    [TestMethod]
    public void EnsureTwiceCreatesFormAndAppTables()
    {
        using var c=Open();
        SchemaFormApp.Ensure(c);
        SchemaFormApp.Ensure(c);
        foreach(var (schema,table) in new[]{("form","campaign"),("form","submission"),("form","submission_history"),("form","custom_form"),("form","custom_response"),
            ("app","mobile_session"),("app","mobile_broadcast"),("app","broadcast_ack"),("app","push_outbox"),("app","rider_ask")})
            Assert.IsTrue(SchemaSql.TableExists(c,null,schema,table),schema+"."+table);
    }

    [TestMethod]
    public void SubmissionUniqueAndFkAndBackfill()
    {
        using var c=Open();
        var campaignId=SchemaSql.NewId();
        using(var tx=c.BeginTransaction())
        {
            Store.Save(c,"campaign",new JsonObject{["id"]=campaignId.ToString("D"),["title"]="حملة اختبار",["kind"]="iban",["status"]="open",["sharingToken"]="tok"+Guid.NewGuid().ToString("N")},tx);
            Store.Save(c,"submission",new JsonObject{["id"]=SchemaSql.NewId().ToString("D"),["campaignId"]=campaignId.ToString("D"),["iqama"]="2000000001",["status"]="pending"},tx);
            tx.Commit();
        }
        SchemaFormApp.Ensure(c);
        Assert.AreEqual(1L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM form_campaign")!));
        Assert.AreEqual(1L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM form_submission WHERE person_key='2000000001'")!));

        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO form_submission(id,campaign_id,person_key,status,created_at) VALUES(@id,@cid,'2000000001','pending',datetime('now'))",
            ("@id",SchemaSql.NewId()),("@cid",campaignId)));
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO form_submission(id,campaign_id,person_key,status,created_at) VALUES(@id,@cid,'2000000002','pending',datetime('now'))",
            ("@id",SchemaSql.NewId()),("@cid",SchemaSql.NewId())));
    }

    [TestMethod]
    public void StoreStartupCreatesFormAppSchema()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-form-app-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            Assert.IsTrue(store.DataPreservationVerified);
            store.Read(c=>{
                Assert.IsTrue(SchemaSql.TableExists(c,null,"form","campaign"));
                Assert.IsTrue(SchemaSql.TableExists(c,null,"app","push_outbox"));
                return true;
            });
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
