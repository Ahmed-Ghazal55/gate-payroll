using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
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
        foreach(string index in new[]{"IX_auth_bypass_granted_by","IX_approvals_request_requested_by","IX_approvals_decision_decided_by","IX_approvals_decision_delegation_id"})
            Assert.IsTrue(IndexExists(c,index),index);
    }

    static bool IndexExists(System.Data.Common.DbConnection c,string name)
    {
        if(SchemaSql.IsSql(c))
            return Convert.ToInt32(MoneySchemaFixture.Scalar(c,null,"SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.indexes WHERE name=@n) THEN 1 ELSE 0 END",("@n",name))!)==1;
        return Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM sqlite_master WHERE type='index' AND name=@n",("@n",name))!)>0;
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
        MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_bypass_grant(id,user_id,scope,valid_from,valid_to,reason,granted_by,created_at) VALUES(@id,@u,'pay.approvals','2026-01-01T00:00:00','2026-06-30T23:59:59','parallel',@g,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@u",user),("@g",granter));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_bypass_grant(id,user_id,scope,valid_from,valid_to,reason,granted_by,created_at) VALUES(@id,@u,'pay.approvals','2026-03-01T00:00:00','2026-09-30T23:59:59','overlap',@g,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@u",user),("@g",granter)),"Bypass grant date ranges for one user and scope cannot overlap");
        MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_bypass_grant(id,user_id,scope,valid_from,valid_to,reason,granted_by,created_at) VALUES(@id,@u,'pay.approvals','2026-07-01T00:00:00','2026-12-31T23:59:59','renewed',@g,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@u",user),("@g",granter));
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

    [TestMethod,DataRow(false),DataRow(true)]
    public void SeedsConfirmedBypassOnlyWhenGhazalUserExists(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        SchemaAuthCatalog.Ensure(c);SchemaApprovals.Ensure(c);
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM auth_user WHERE username='ghazal'"));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM auth_bypass_grant"));
        string role=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM auth_role WHERE code='Admin'")!;
        string user=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_user(id,username,role_id,security_version,status,created_at) VALUES(@id,'ghazal',@role,1,1,datetime('now'))",("@id",user),("@role",role));
        SchemaApprovals.Ensure(c);SchemaApprovals.Ensure(c);
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM auth_bypass_grant WHERE user_id=@u AND scope='pay.approvals' AND revoked_at IS NULL",("@u",user)));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM auth_user WHERE username='ghazal'"));
        Assert.AreEqual("Ninja October parallel (payout_blocked=1); revoke before first real payment",
            MoneySchemaFixture.Scalar(c,null,"SELECT reason FROM auth_bypass_grant WHERE user_id=@u",("@u",user)));
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
                Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM auth_bypass_grant"));
                return true;
            });
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    [TestMethod]
    public void RuntimeWritesLandInRealApprovalTables()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{
            var maker=UserAccess.Find(c,"synthetic-pay-maker",tx)??new JsonObject{["id"]=SchemaSql.GuidText(SchemaSql.NewId()),["username"]="synthetic-pay-maker",["role"]="Admin"};
            Store.Save(c,"user",maker,tx);
            ApprovalsArea.PutLimits(c,tx,PayFixture.Maker,new(){["items"]=new JsonArray(new JsonObject{["rank"]="A",["department"]="FIN",["limitSar"]=1234})});
            ApprovalsArea.PutSla(c,tx,PayFixture.Maker,new(){["items"]=new JsonArray(new JsonObject{["requestType"]="default",["workingDays"]=2})});
            AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="synthetic-pay-maker",["from"]="2026-01-01",["to"]="2026-12-31",["reason"]="table read",["scope"]=AccessPolicy.ScopePayApprovals});
            Store.Save(c,"approval_request",new()
            {
                ["kind"]="synthetic.table_read",["titleAr"]="طلب",["targetDepartment"]="FIN",["requiredRankLevel"]=1,
                ["referenceId"]="",["status"]="pending",["requestedBy"]="synthetic-pay-maker",["payload"]=new JsonObject()
            },tx);
            Store.Save(c,"approval_decision",new()
            {
                ["requestId"]=Store.List(c,"approval_request",tx).First(r=>r.S("kind")=="synthetic.table_read").S("id"),
                ["decidedBy"]="synthetic-pay-maker",["decidedAt"]=DateTimeOffset.UtcNow.ToString("O"),
                ["outcome"]="approved",["membershipSnapshot"]=new JsonObject()
            },tx);
            return true;
        });
        Assert.AreEqual(1234m,f.Store.Read(c=>Convert.ToDecimal(SchemaSql.Scalar(c,null,$"SELECT limit_amount FROM {SchemaSql.Q(c,"auth","approval_limit")} WHERE rank=@r AND department=@d",("@r","A"),("@d","FIN"))!)));
        Assert.AreEqual(2,f.Store.Read(c=>Convert.ToInt32(SchemaSql.Scalar(c,null,$"SELECT working_days FROM {SchemaSql.Q(c,"org","sla_policy")} WHERE request_type=@t",("@t","default"))!)));
        Assert.IsTrue(f.Store.Read(c=>Convert.ToInt64(SchemaSql.Scalar(c,null,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"auth","bypass_grant")}")!)>0));
        Assert.IsTrue(f.Store.Read(c=>Convert.ToInt64(SchemaSql.Scalar(c,null,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"approvals","request")}")!)>0));
        Assert.IsTrue(f.Store.Read(c=>Convert.ToInt64(SchemaSql.Scalar(c,null,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"approvals","decision")}")!)>0));
    }
}
