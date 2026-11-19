using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaFormAppTests
{
    static SqliteConnection Open()
    {
        var c=new SqliteConnection("Data Source=:memory:");c.Open();
        using(var fk=c.CreateCommand()){fk.CommandText="PRAGMA foreign_keys=ON;";fk.ExecuteNonQuery();}
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
        Assert.AreEqual(0L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM form_campaign")!));
    }

    [TestMethod]
    public void SubmissionUniqueAndFkRejectBadRows()
    {
        using var c=Open();
        SchemaFormApp.Ensure(c);
        var campaignId=SchemaSql.NewId();
        SchemaSql.Exec(c,null,"INSERT INTO form_campaign(id,title,kind,status,created_at) VALUES(@id,'t','iban','open',datetime('now'))",
            ("@id",SchemaSql.GuidText(campaignId)));
        SchemaSql.Exec(c,null,"INSERT INTO form_submission(id,campaign_id,person_key,status,created_at) VALUES(@id,@cid,'2000000001','pending',datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",SchemaSql.GuidText(campaignId)));

        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO form_submission(id,campaign_id,person_key,status,created_at) VALUES(@id,@cid,'2000000001','pending',datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",SchemaSql.GuidText(campaignId))));
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO form_submission(id,campaign_id,person_key,status,created_at) VALUES(@id,@cid,'2000000002','pending',datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",SchemaSql.GuidText(SchemaSql.NewId()))));
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
                Assert.AreEqual(0L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM form_campaign")!));
                return true;
            });
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
