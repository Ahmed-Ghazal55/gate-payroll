using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaApprovalsTests
{
    [TestMethod,DataRow(false),DataRow(true)]
    public void EnsureTwiceCreatesApprovalsBypassAndLimits(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        SchemaAuthCatalog.Ensure(c);SchemaOrgStructure.Ensure(c);SchemaApprovals.Ensure(c);SchemaApprovals.Ensure(c);
        Assert.IsTrue(SchemaSql.TableExists(c,null,"approvals","request"));
        Assert.IsTrue(SchemaSql.TableExists(c,null,"approvals","decision"));
        Assert.IsTrue(SchemaSql.TableExists(c,null,"auth","bypass_grant"));
        Assert.IsTrue(SchemaSql.TableExists(c,null,"auth","approval_limit"));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM auth_bypass_grant"));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM approvals_request"));
    }

    [TestMethod,DataRow(false),DataRow(true)]
    public void BypassGrantDatesAndScopeChecks(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        SchemaAuthCatalog.Ensure(c);SchemaApprovals.Ensure(c);
        string role=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM auth_role WHERE code='Admin'")!;
        string user=MoneySchemaFixture.Id(),granter=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_user(id,username,role_id,security_version,status,created_at) VALUES(@id,'syn-bypass',@role,1,1,datetime('now'))",("@id",user),("@role",role));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_user(id,username,role_id,security_version,status,created_at) VALUES(@id,'syn-granter',@role,1,1,datetime('now'))",("@id",granter),("@role",role));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_bypass_grant(id,user_id,scope,valid_from,valid_to,reason,granted_by,created_at) VALUES(@id,@u,'pay.approvals','2026-01-01T00:00:00','2026-12-31T23:59:59','parallel',@g,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@u",user),("@g",granter));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_bypass_grant(id,user_id,scope,valid_from,valid_to,reason,granted_by,created_at) VALUES(@id,@u,'other.scope','2026-12-31T00:00:00','2026-01-01T00:00:00','bad',@g,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@u",user),("@g",granter)),"CK_bypass_dates",sqliteToken:"CHECK");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_bypass_grant(id,user_id,scope,valid_from,valid_to,reason,granted_by,created_at) VALUES(@id,@u,'pay.approvals','2026-01-01T00:00:00','2026-12-31T23:59:59','x',@g,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@u",MoneySchemaFixture.Id()),("@g",granter)),"FK_auth_bypass_user",sqliteToken:"FOREIGN KEY");
    }

    [TestMethod,DataRow(false),DataRow(true)]
    public void DecisionRequiresMembershipSnapshotAndLinksBypass(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        SchemaAuthCatalog.Ensure(c);SchemaOrgStructure.Ensure(c);SchemaApprovals.Ensure(c);
        string role=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM auth_role WHERE code='Admin'")!;
        string user=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_user(id,username,role_id,security_version,status,created_at) VALUES(@id,'syn-decide',@role,1,1,datetime('now'))",("@id",user),("@role",role));
        string request=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO approvals_request(id,kind,title_ar,status,target_department,required_rank_level,requested_by,created_at) VALUES(@id,'pay.approve','t','pending','FIN',1,@u,datetime('now'))",("@id",request),("@u",user));
        string bypass=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_bypass_grant(id,user_id,scope,valid_from,valid_to,reason,granted_by,created_at) VALUES(@id,@u,'pay.approvals','2026-01-01T00:00:00','2026-12-31T23:59:59','parallel',@u,datetime('now'))",("@id",bypass),("@u",user));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO approvals_decision(id,request_id,decided_by,decided_at,outcome,membership_snapshot_json,bypass_grant_id,created_at) VALUES(@id,@r,@u,datetime('now'),'approved','{}',@b,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@r",request),("@u",user),("@b",bypass));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO approvals_decision(id,request_id,decided_by,decided_at,outcome,membership_snapshot_json,created_at) VALUES(@id,@r,@u,datetime('now'),'approved',NULL,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@r",request),("@u",user)),"membership_snapshot_json",sqliteToken:"NOT NULL");
        MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_approval_limit(id,rank,department,limit_amount,created_at) VALUES(@id,'A','FIN',20000,datetime('now'))",("@id",MoneySchemaFixture.Id()));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_approval_limit(id,rank,department,limit_amount,created_at) VALUES(@id,'A','FIN',1,datetime('now'))",("@id",MoneySchemaFixture.Id())),"UQ",sqliteToken:"UNIQUE");
    }

    [TestMethod]
    public void StoreStartupCreatesApprovalsSchema()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-appr-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            Assert.IsTrue(store.DataPreservationVerified);
            store.Read(c=>{
                Assert.IsTrue(SchemaSql.TableExists(c,null,"approvals","decision"));
                Assert.IsTrue(SchemaSql.TableExists(c,null,"auth","bypass_grant"));
                return true;
            });
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
