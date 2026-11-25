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
                Assert.IsTrue(promoted.B("payoutBlocked"));
                Assert.IsTrue(promoted["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="submit_for_approval"));
                Assert.AreEqual(1L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(1) FROM sal_pay_run WHERE id=@id AND run_mode=0 AND payout_blocked=1",("@id",promoted.S("id")))!));
                tx.Commit();
            }
            var diff=SalSimulations.PromoteDiff(c,admin,run2);
            Assert.IsTrue(diff["rows"]!.AsArray().Count>=1);
            Assert.IsTrue(diff["rows"]!.AsArray().OfType<JsonObject>().All(r=>r.S("changeAr")!=""));
            // Screen binds Arabic from change; English code stays under changeCode.
            Assert.IsTrue(diff["rows"]!.AsArray().OfType<JsonObject>().All(r=>
                r.S("change") is "في المحاكاة فقط" or "في الحي فقط" or "تغيّر" or "متطابق"
                &&r.S("changeCode") is "sim_only" or "live_only" or "changed" or "same"));
            Assert.IsFalse(diff["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="promote_simulation"),
                "live run exists after promote-real — promote_simulation must not be offered");
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CreateSimulationReturnsServerId(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);
        using(c)
        {
            SchemaSalPayRun.Ensure(c);
            if(SchemaSql.IsSql(c))
                SchemaSql.Exec(c,null,"IF OBJECT_ID(N'dbo.records',N'U') IS NULL CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL);");
            else
                MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
            string company=MoneySchemaFixture.Company(c);
            var admin=UserAccess.Principal(new JsonObject{["username"]="sim-create",["role"]="Admin"});
            using var tx=c.BeginTransaction();
            var created=SalSimulations.Create(c,tx,admin,new JsonObject
            {
                ["companyId"]=company,["app"]="ninja",["periodMonth"]="2026-11"
            });
            Assert.AreEqual(1m,created.N("runMode"));
            Assert.IsFalse(string.IsNullOrWhiteSpace(created.S("id")));
            Assert.IsTrue(created["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="discard_simulation"));
            Assert.AreEqual("2026-11-01",created.S("periodMonth"));
            Assert.AreEqual(1L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(1) FROM sal_pay_run WHERE id=@id AND run_mode=1",("@id",created.S("id")))!));
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalSimulations.Create(c,tx,admin,new JsonObject
            {
                ["companyId"]=company,["app"]="ninja",["periodMonth"]="2026-11"
            })).Status);
            tx.Commit();
        }
    }

    [TestMethod]
    public void CreateRouteRequiresSimulationPermission()
    {
        // HRSpecialist has no pay.manage — isolate salaries.simulation.run vs promote routes.
        var runner=UserAccess.Principal(new JsonObject
        {
            ["username"]="sim-runner",["role"]="HRSpecialist",
            ["customPermissions"]=new JsonArray(SalSimulations.RunPermission)
        });
        var viewer=UserAccess.Principal(new JsonObject
        {
            ["username"]="sim-viewer",["role"]="HRSpecialist",
            ["customPermissions"]=new JsonArray("payroll.view")
        });
        var promoter=UserAccess.Principal(new JsonObject
        {
            ["username"]="sim-promoter",["role"]="HRSpecialist",
            ["customPermissions"]=new JsonArray(SalSimulations.PromotePermission)
        });
        Assert.IsTrue(AccessPolicy.Allowed(runner,"POST","/api/sal/simulations"));
        Assert.IsFalse(AccessPolicy.Allowed(runner,"GET","/api/sal/simulations"));
        Assert.IsFalse(AccessPolicy.Allowed(viewer,"POST","/api/sal/simulations"));
        Assert.IsTrue(AccessPolicy.Allowed(runner,"POST","/api/sal/simulations/x/discard"));
        Assert.IsFalse(AccessPolicy.Allowed(runner,"GET","/api/sal/simulations/x/promote-diff"));
        Assert.IsTrue(AccessPolicy.Allowed(promoter,"GET","/api/sal/simulations/x/promote-diff"));
        Assert.IsFalse(AccessPolicy.Allowed(promoter,"POST","/api/sal/simulations"));
    }

    [TestMethod]
    public void PlatformScopeIsRequiredOnLoad()
    {
        var (c,company,run)=SeedSim();
        using(c)
        {
            var limited=UserAccess.Principal(new JsonObject
            {
                ["username"]="ninja-only",["role"]="FinancialAccountant",
                ["customPermissions"]=new JsonArray(SalSimulations.RunPermission)
            });
            var identity=(System.Security.Claims.ClaimsIdentity)limited.Identity!;
            foreach(var claim in limited.FindAll("finance.platforms").ToList())identity.RemoveClaim(claim);
            foreach(var claim in limited.FindAll("finance.sponsorIds").ToList())identity.RemoveClaim(claim);
            identity.AddClaim(new System.Security.Claims.Claim("finance.platforms","Ninja"));
            identity.AddClaim(new System.Security.Claims.Claim("finance.sponsorIds",company));
            Assert.IsNotNull(SalSimulations.JournalPreview(c,limited,run));
            MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET app_code='keeta' WHERE id=@id",("@id",run));
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>SalSimulations.JournalPreview(c,limited,run)).Status);
        }
    }
}
