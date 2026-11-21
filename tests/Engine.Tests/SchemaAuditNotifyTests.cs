using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaAuditNotifyTests
{
    [TestMethod,DataRow(false),DataRow(true)]
    public void EnsureTwiceCreatesAuditNotifyTaskChat(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        SchemaAuthCatalog.Ensure(c);SchemaAuditNotify.Ensure(c);SchemaAuditNotify.Ensure(c);
        foreach(var (schema,table) in new[]{
            ("audit","change_log"),("audit","action_label"),("audit","read_summary_daily"),
            ("notify","notification"),("notify","subscription"),("notify","reply"),
            ("task","item"),("task","comment"),("task","attachment"),
            ("chat","channel"),("chat","message"),("chat","receipt"),("chat","moderation"),("chat","block")})
            Assert.IsTrue(SchemaSql.TableExists(c,null,schema,table),schema+"."+table);
        Assert.IsTrue(Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM audit_action_label")!)>=1);
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM audit_change_log"));
    }

    [TestMethod,DataRow(false),DataRow(true)]
    public void ChangeLogRejectsMissingActionAndAllowsBulkImportRow(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        SchemaAuthCatalog.Ensure(c);SchemaAuditNotify.Ensure(c);
        string id=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO audit_change_log(id,occurred_on,occurred_at,action_code,entity_type,entity_ref,diff_short,source,bulk_count,file_hash,created_at) VALUES(@id,'2026-11-01','2026-11-01T12:00:00','create','debt','SYN-1','+1','import',10,randomblob(32),datetime('now'))",("@id",id));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO audit_change_log(id,occurred_on,occurred_at,action_code,entity_type,entity_ref,diff_short,source,created_at) VALUES(@id,'2026-11-01','2026-11-01T12:00:00','nope','debt','x','x','ui',datetime('now'))",("@id",MoneySchemaFixture.Id())),"FK",sqliteToken:"FOREIGN KEY");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO audit_change_log(id,occurred_on,occurred_at,action_code,entity_type,entity_ref,diff_short,source,created_at) VALUES(@id,'2026-11-01','2026-11-01T12:00:00','create','debt','x','x','read',datetime('now'))",("@id",MoneySchemaFixture.Id())),"CK",sqliteToken:"CHECK");
        MoneySchemaFixture.Exec(c,null,"INSERT INTO audit_read_summary_daily(id,summary_date,user_id,area,read_count,export_count,sensitive_count,created_at) VALUES(@id,'2026-11-01',@u,'iban',1,0,1,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@u",MoneySchemaFixture.Id()));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO audit_read_summary_daily(id,summary_date,user_id,area,read_count,export_count,sensitive_count,created_at) VALUES(@id,'2026-11-01',@u,'iban',2,0,0,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@u",(string)MoneySchemaFixture.Scalar(c,null,"SELECT user_id FROM audit_read_summary_daily LIMIT 1")!)),"UQ",sqliteToken:"UNIQUE");
    }

    [TestMethod,DataRow(false),DataRow(true)]
    public void NotifyTaskChatChildFkAndUniques(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        SchemaAuthCatalog.Ensure(c);SchemaAuditNotify.Ensure(c);
        string role=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM auth_role WHERE code='Admin'")!;
        string user=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_user(id,username,role_id,security_version,status,created_at) VALUES(@id,'syn-notify',@role,1,1,datetime('now'))",("@id",user),("@role",role));
        string note=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO notify_notification(id,user_id,kind,title_ar,status,created_at) VALUES(@id,@u,'general','t','unread',datetime('now'))",("@id",note),("@u",user));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO notify_reply(id,notification_id,author_user_id,body_ar,created_at) VALUES(@id,@n,@u,'ok',datetime('now'))",("@id",MoneySchemaFixture.Id()),("@n",note),("@u",user));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO notify_subscription(id,event_type,audience_key,channel,enabled,created_at) VALUES(@id,'approval.pending','role:Admin','in_app',1,datetime('now'))",("@id",MoneySchemaFixture.Id()));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO notify_subscription(id,event_type,audience_key,channel,enabled,created_at) VALUES(@id,'approval.pending','role:Admin','in_app',0,datetime('now'))",("@id",MoneySchemaFixture.Id())),"UQ",sqliteToken:"UNIQUE");
        string task=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO task_item(id,due_on,status,title_ar,created_at) VALUES(@id,'2026-11-21','open','t',datetime('now'))",("@id",task));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO task_comment(id,task_id,author_user_id,body_ar,created_at) VALUES(@id,@t,@u,'c',datetime('now'))",("@id",MoneySchemaFixture.Id()),("@t",task),("@u",user));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO task_attachment(id,task_id,file_hash,file_name,created_at) VALUES(@id,@t,randomblob(32),'a.bin',datetime('now'))",("@id",MoneySchemaFixture.Id()),("@t",task));
        string channel=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO chat_channel(id,code,name_ar,created_at) VALUES(@id,'admin','Admin',datetime('now'))",("@id",channel));
        string msg=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO chat_message(id,channel_id,author_user_id,body_ar,created_at) VALUES(@id,@c,@u,'hi',datetime('now'))",("@id",msg),("@c",channel),("@u",user));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO chat_receipt(id,message_id,user_id,read_at,created_at) VALUES(@id,@m,@u,datetime('now'),datetime('now'))",("@id",MoneySchemaFixture.Id()),("@m",msg),("@u",user));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO chat_moderation(id,message_id,moderator_user_id,action,created_at) VALUES(@id,@m,@u,'hide',datetime('now'))",("@id",MoneySchemaFixture.Id()),("@m",msg),("@u",user));
        string other=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_user(id,username,role_id,security_version,status,created_at) VALUES(@id,'syn-blocked',@role,1,1,datetime('now'))",("@id",other),("@role",role));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO chat_block(id,blocker_user_id,blocked_user_id,created_at) VALUES(@id,@u,@u2,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@u",user),("@u2",other));
        // chat never requires a notify row
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM chat_message"));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM notify_notification"));
    }

    [TestMethod]
    public void StoreStartupCreatesAuditNotifySchemas()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-audit-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            Assert.IsTrue(store.DataPreservationVerified);
            store.Read(c=>{
                Assert.IsTrue(SchemaSql.TableExists(c,null,"audit","change_log"));
                Assert.IsTrue(SchemaSql.TableExists(c,null,"chat","channel"));
                return true;
            });
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
