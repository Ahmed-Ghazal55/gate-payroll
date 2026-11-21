using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaOrgStructureTests
{
    [TestMethod,DataRow(false),DataRow(true)]
    public void EnsureTwiceCreatesRemainingOrgTables(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        SchemaAuthCatalog.Ensure(c);SchemaOrgStructure.Ensure(c);SchemaOrgStructure.Ensure(c);
        foreach(string table in new[]{"membership","delegation","supervision","routing","sla_policy"})
            Assert.IsTrue(SchemaSql.TableExists(c,null,"org",table),"org."+table);
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM org_membership"));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM org_sla_policy"));
    }

    [TestMethod,DataRow(false),DataRow(true)]
    public void ActiveMembershipUniquePerUserDepartmentAndFk(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        SchemaAuthCatalog.Ensure(c);SchemaOrgStructure.Ensure(c);
        string role=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM auth_role WHERE code='Admin'")!;
        string user=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_user(id,username,role_id,security_version,status,created_at) VALUES(@id,'synthetic-org',@role,1,1,datetime('now'))",("@id",user),("@role",role));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO org_membership(id,user_id,department,rank,rank_level,valid_from,created_at) VALUES(@id,@u,'FIN','A',3,'2026-01-01',datetime('now'))",("@id",MoneySchemaFixture.Id()),("@u",user));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO org_membership(id,user_id,department,rank,rank_level,valid_from,created_at) VALUES(@id,@u,'FIN','B',2,'2026-02-01',datetime('now'))",("@id",MoneySchemaFixture.Id()),("@u",user)),"UQ_org_membership_active",sqliteToken:"org_membership");
        MoneySchemaFixture.Exec(c,null,"UPDATE org_membership SET valid_to='2026-06-30' WHERE user_id=@u",("@u",user));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO org_membership(id,user_id,department,rank,rank_level,valid_from,valid_to,created_at) VALUES(@id,@u,'FIN','B',2,'2026-03-01','2026-09-30',datetime('now'))",("@id",MoneySchemaFixture.Id()),("@u",user)),"Membership date ranges for one user and department cannot overlap");
        MoneySchemaFixture.Exec(c,null,"INSERT INTO org_membership(id,user_id,department,rank,rank_level,valid_from,created_at) VALUES(@id,@u,'FIN','A+',4,'2026-07-01',datetime('now'))",("@id",MoneySchemaFixture.Id()),("@u",user));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO org_membership(id,user_id,department,rank,rank_level,valid_from,created_at) VALUES(@id,@u,'FIN','C',1,'2026-08-01',datetime('now'))",("@id",MoneySchemaFixture.Id()),("@u",MoneySchemaFixture.Id())),"FK_org_membership_user",sqliteToken:"FOREIGN KEY");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO org_membership(id,user_id,department,rank,rank_level,valid_from,created_at) VALUES(@id,@u,'OPS','Z',9,'2026-01-01',datetime('now'))",("@id",MoneySchemaFixture.Id()),("@u",user)),"CK_org_membership_rank",sqliteToken:"CHECK");
    }

    [TestMethod,DataRow(false),DataRow(true)]
    public void DelegationSupervisionRoutingAndSlaChecks(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        SchemaAuthCatalog.Ensure(c);SchemaOrgStructure.Ensure(c);
        string role=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM auth_role WHERE code='Admin'")!;
        string a=MoneySchemaFixture.Id(),b=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_user(id,username,role_id,security_version,status,created_at) VALUES(@id,'syn-a',@role,1,1,datetime('now'))",("@id",a),("@role",role));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_user(id,username,role_id,security_version,status,created_at) VALUES(@id,'syn-b',@role,1,1,datetime('now'))",("@id",b),("@role",role));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO org_delegation(id,from_user_id,to_user_id,operation,valid_from,until_date,created_at) VALUES(@id,@a,@b,'advance.review','2026-01-01','2026-12-31',datetime('now'))",("@id",MoneySchemaFixture.Id()),("@a",a),("@b",b));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO org_delegation(id,from_user_id,to_user_id,operation,valid_from,until_date,created_at) VALUES(@id,@a,@a,'advance.review','2026-01-01','2026-12-31',datetime('now'))",("@id",MoneySchemaFixture.Id()),("@a",a)),"CK",sqliteToken:"CHECK");
        MoneySchemaFixture.Exec(c,null,"INSERT INTO org_supervision(id,supervisor_user_id,rider_iqama,valid_from,created_at) VALUES(@id,@a,'2000000001','2026-01-01',datetime('now'))",("@id",MoneySchemaFixture.Id()),("@a",a));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO org_supervision(id,supervisor_user_id,rider_iqama,valid_from,created_at) VALUES(@id,@a,'2000000001','2026-02-01',datetime('now'))",("@id",MoneySchemaFixture.Id()),("@a",a)),"UQ",sqliteToken:"org_supervision");
        MoneySchemaFixture.Exec(c,null,"INSERT INTO org_routing(id,operation,department,min_rank,min_rank_level,users_json,valid_from,created_at) VALUES(@id,'advance.review','FIN','B',2,'[]','2026-01-01',datetime('now'))",("@id",MoneySchemaFixture.Id()));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO org_sla_policy(id,request_type,working_days,created_at) VALUES(@id,'advance.review',3,datetime('now'))",("@id",MoneySchemaFixture.Id()));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO org_sla_policy(id,request_type,working_days,created_at) VALUES(@id,'advance.review',5,datetime('now'))",("@id",MoneySchemaFixture.Id())),"UQ",sqliteToken:"UNIQUE");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO org_sla_policy(id,request_type,working_days,created_at) VALUES(@id,'other',0,datetime('now'))",("@id",MoneySchemaFixture.Id())),"CK",sqliteToken:"CHECK");
    }

    [TestMethod]
    public void StoreStartupCreatesOrgStructure()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-org-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            Assert.IsTrue(store.DataPreservationVerified);
            store.Read(c=>{
                Assert.IsTrue(SchemaSql.TableExists(c,null,"org","membership"));
                Assert.IsTrue(SchemaSql.TableExists(c,null,"org","sla_policy"));
                return true;
            });
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
