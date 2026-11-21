using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class SalSimulationsTests
{
    static (System.Data.Common.DbConnection c,string company,string runId) SeedSim(bool sqlServer=false)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);
        SchemaSalPayRun.Ensure(c);
        // Store.Audit writes into records; money-schema fixtures do not create that table by default.
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,null,"IF OBJECT_ID(N'dbo.records',N'U') IS NULL CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL);");
        else
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
        string company=MoneySchemaFixture.Company(c);
        string run=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month,run_mode,status,stage,capture_state,created_at) VALUES(@id,@c,'ninja','2026-11-01',1,1,0,0,datetime('now'))",("@id",run),("@c",company));
        MoneySchemaFixture.Rider(c,run,company,net:100,mode:1);
        return (c,company,run);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void JournalPreviewDoesNotCreateJournals(bool sqlServer)
    {
        var (c,_,run)=SeedSim(sqlServer);
        using(c)
        {
            SchemaFinJournal.Ensure(c);
            var admin=UserAccess.Principal(new JsonObject{["username"]="sim-user",["role"]="Admin"});
            var preview=SalSimulations.JournalPreview(c,admin,run);
            Assert.IsFalse(preview.B("posted"));
            Assert.IsTrue(preview["lines"]!.AsArray().Count>=2);
            Assert.AreEqual(0L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_journal")!));
            Assert.IsTrue(preview["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="discard_simulation"));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DiscardAndPromoteReal(bool sqlServer)
    {
        var (c,company,run)=SeedSim(sqlServer);
        using(c)
        {
            var admin=UserAccess.Principal(new JsonObject{["username"]="sim-admin",["role"]="Admin"});
            using(var tx=c.BeginTransaction())
            {
                var discarded=SalSimulations.Discard(c,tx,admin,run);
                Assert.AreEqual(4m,discarded.N("status"));
                tx.Commit();
            }

            string run2=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month,run_mode,status,stage,capture_state,created_at) VALUES(@id,@c,'ninja','2026-11-01',1,1,0,0,datetime('now'))",("@id",run2),("@c",company));
            MoneySchemaFixture.Rider(c,run2,company,net:80,mode:1);
            using(var tx=c.BeginTransaction())
            {
                var promoted=SalSimulations.PromoteReal(c,tx,admin,run2);
                Assert.AreEqual(0m,promoted.N("runMode"));
                Assert.AreEqual(1L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(1) FROM sal_pay_run WHERE id=@id AND run_mode=0",("@id",promoted.S("id")))!));
                tx.Commit();
            }
            var diff=SalSimulations.PromoteDiff(c,admin,run2);
            Assert.IsTrue(diff["rows"]!.AsArray().Count>=1);
        }
    }
}
