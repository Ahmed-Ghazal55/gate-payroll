using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class ArchiveApiTests
{
    static void WithStore(Action<Store> body)
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-arch-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try{body(new Store(dir));}
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    [TestMethod,DataRow(false),DataRow(true)]
    public void ArchiveTablesExist(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        SchemaAuthCatalog.Ensure(c);SchemaArchive.Ensure(c);SchemaArchive.Ensure(c);
        Assert.IsTrue(SchemaSql.TableExists(c,null,"archive","policy"));
        Assert.IsTrue(SchemaSql.TableExists(c,null,"archive","restore_request"));
    }

    [TestMethod]
    public void PoliciesAndRestoreRequest_NoHardDelete_TemporaryViewWithApproval()
    {
        WithStore(store=>
        {
            store.Write((c,tx)=>{Store.Save(c,"user",UserAccess.Create("archadmin","Password!23456","Admin"),tx);return true;});
            var admin=UserAccess.Principal(store.Read(c=>Store.List(c,"user").First(u=>u.S("username")=="archadmin")));
            Assert.IsTrue(AccessPolicy.Has(admin,ArchiveApi.Permission)||AccessPolicy.Administrator(admin));

            var policies=store.Read(c=>ArchiveApi.ListPolicies(c,admin));
            Assert.IsTrue(policies["items"] is JsonArray items&&items.Count>=1);
            Assert.IsFalse(policies.B("hardDeleteAllowed"));
            Assert.AreEqual(3,(int)policies.N("retentionYears"));

            Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>ArchiveApi.RequestRestore(c,tx,admin,new()
            {
                ["entityType"]="audit.change_log",
                ["periodFrom"]="2025-01-01",
                ["periodTo"]="2025-12-01",
                ["reasonAr"]="اختبار"
            }))).Status);

            var req=store.Write((c,tx)=>ArchiveApi.RequestRestore(c,tx,admin,new()
            {
                ["entityType"]="audit.change_log",
                ["periodFrom"]="2020-01-01",
                ["periodTo"]="2020-06-01",
                ["reasonAr"]="مراجعة قديمة"
            }));
            Assert.AreEqual("pending",req.S("status"));
            Assert.AreEqual("request_restore",((JsonArray)req["allowedActions"]!)[0]!.GetValue<string>());
            Assert.IsTrue(store.Read(c=>Store.List(c,"approval_request").Any(a=>a.S("kind")==ArchiveApi.ApprovalKind)));
            Assert.IsTrue(store.Read(c=>Store.List(c,"audit").Any(a=>a.S("action")=="archive.restore_request")));

            store.Write((c,tx)=>{Store.Save(c,"user",UserAccess.Create("approver2","Password!23456","Admin"),tx);return true;});
            var approver=UserAccess.Principal(store.Read(c=>Store.List(c,"user").First(u=>u.S("username")=="approver2")));
            string approvalId=store.Read(c=>Store.List(c,"approval_request").First(a=>a.S("kind")==ArchiveApi.ApprovalKind).S("id"));
            store.Write((c,tx)=>
            {
                var ar=Store.Get(c,approvalId,"approval_request",tx)!;
                ar["status"]="approved";ar["decidedBy"]="approver2";ar["decidedAt"]=DateTimeOffset.UtcNow.ToString("O");
                Store.Save(c,"approval_request",ar,tx);
                SchemaApprovals.SaveDecision(c,tx,new()
                {
                    ["requestId"]=approvalId,
                    ["outcome"]="approved",
                    ["decidedBy"]="approver2",
                    ["decidedAt"]=ar.S("decidedAt")
                });
                return true;
            });

            var after=store.Read(c=>ArchiveApi.ListPolicies(c,approver));
            var restores=(after["restoreRequests"] as JsonArray??[]).OfType<JsonObject>().ToList();
            Assert.IsTrue(restores.Any(r=>r.S("status")=="active"&&r.S("expiresAt")!=""));
            Assert.IsTrue(restores.All(r=>r.B("readOnly")));
            Assert.IsFalse(AccessPolicy.Allowed(UserAccess.Principal(new(){["username"]="x",["role"]="Reviewer"}),"GET","/api/admin/archive/policies"));
        });
    }

    [TestMethod]
    public void DecidePath_SaveDecision_ActivatesRestore_WithoutManualApplyApproval()
    {
        WithStore(store=>
        {
            store.Write((c,tx)=>
            {
                Store.Save(c,"user",UserAccess.Create("archmaker","Password!23456","Admin"),tx);
                Store.Save(c,"user",UserAccess.Create("archcheck","Password!23456","Admin"),tx);
                return true;
            });
            var maker=UserAccess.Principal(store.Read(c=>Store.List(c,"user").First(u=>u.S("username")=="archmaker")));
            var req=store.Write((c,tx)=>ArchiveApi.RequestRestore(c,tx,maker,new()
            {
                ["entityType"]="audit.change_log",
                ["periodFrom"]="2019-01-01",
                ["periodTo"]="2019-12-31",
                ["reasonAr"]="اعتماد عبر قرار"
            }));
            string approvalId=req.S("approvalRequestId");
            Assert.AreEqual(36,approvalId.Length);
            store.Write((c,tx)=>
            {
                var ar=Store.Get(c,approvalId,"approval_request",tx)!;
                ar["status"]="approved";ar["decidedBy"]="archcheck";ar["decidedAt"]="2026-11-23T03:00:00.0000000Z";
                Store.Save(c,"approval_request",ar,tx);
                // Real decide path: SaveDecision (not ApplyApproval alone).
                SchemaApprovals.SaveDecision(c,tx,new()
                {
                    ["requestId"]=approvalId,
                    ["outcome"]="approved",
                    ["decidedBy"]="archcheck",
                    ["decidedAt"]="2026-11-23T03:00:00.0000000Z"
                });
                return true;
            });
            string status=store.Read(c=>
            {
                object? s=SchemaSql.Scalar(c,null,$"SELECT status FROM {SchemaSql.Q(c,"archive","restore_request")} WHERE approval_request_id=@a",("@a",approvalId));
                return s?.ToString()??"";
            });
            Assert.AreEqual("active",status);
        });
    }

    [TestMethod]
    public void ExpiredRestore_SameCalendarDay_MarkedExpired_ExactHours()
    {
        WithStore(store=>
        {
            store.Write((c,tx)=>{Store.Save(c,"user",UserAccess.Create("archx","Password!23456","Admin"),tx);return true;});
            var admin=UserAccess.Principal(store.Read(c=>Store.List(c,"user").First(u=>u.S("username")=="archx")));
            var req=store.Write((c,tx)=>ArchiveApi.RequestRestore(c,tx,admin,new()
            {
                ["entityType"]="audit.change_log",
                ["periodFrom"]="2018-01-01",
                ["periodTo"]="2018-06-01",
                ["reasonAr"]="انتهاء نفس اليوم"
            }));
            string approvalId=req.S("approvalRequestId");
            string restoreId=req.S("id");
            // Expiry 1 hour ago, ISO with T (the bug format) — must expire on same calendar day.
            string expiredAt=DateTimeOffset.UtcNow.AddHours(-1).UtcDateTime.ToString("O",System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains('T',expiredAt);
            store.Write((c,tx)=>
            {
                SchemaSql.Exec(c,tx,$"""
                    UPDATE {SchemaSql.Q(c,"archive","restore_request")}
                    SET status='active', expires_at=@ex, updated_at={SchemaSql.NowLiteral(c)}
                    WHERE id=@id
                    """,("@ex",expiredAt),("@id",restoreId));
                return true;
            });
            var policies=store.Read(c=>ArchiveApi.ListPolicies(c,admin));
            var row=((policies["restoreRequests"] as JsonArray)??[]).OfType<JsonObject>().First(r=>r.S("id")==restoreId);
            Assert.AreEqual("expired",row.S("status"));
        });
    }

    [TestMethod]
    public void RestoreView_RequiresActiveApproval_OrdinaryReadExcludesArchivedChat()
    {
        WithStore(store=>
        {
            string company=FinancialCore.Sponsors[0].Id;
            store.Write((c,tx)=>
            {
                Store.Save(c,"user",UserAccess.Create("archview","Password!23456","Admin"),tx);
                return true;
            });
            var admin=UserAccess.Principal(store.Read(c=>Store.List(c,"user").First(u=>u.S("username")=="archview")));
            var channel=store.Write((c,tx)=>StaffChatApi.CreateChannel(c,tx,admin,new()
            {
                ["code"]="arch-room",
                ["nameAr"]="غرفة الأرشيف",
                ["companyId"]=company,
                ["memberUsernames"]=new JsonArray()
            }));
            string channelId=channel.S("id");
            string oldId=store.Write((c,tx)=>
            {
                string userId=SchemaApprovals.EnsureAuthUserId(c,tx,"archview");
                string id=SchemaSql.GuidText(SchemaSql.NewId());
                // 4 years old → archived under 3y retention.
                string created=DateTime.UtcNow.AddYears(-4).ToString("yyyy-MM-dd HH:mm:ss.fffffff",System.Globalization.CultureInfo.InvariantCulture);
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"chat","message")}
                    (id,channel_id,author_user_id,body_ar,created_at,created_by)
                    VALUES(@id,@ch,@au,@body,@at,@by)
                    """,
                    ("@id",id),("@ch",channelId),("@au",userId),("@body","رسالة قديمة"),
                    ("@at",created),("@by","archview"));
                return id;
            });
            string freshId=store.Write((c,tx)=>StaffChatApi.PostMessage(c,tx,admin,channelId,new(){["bodyAr"]="رسالة حديثة"})).S("id");

            var ordinary=store.Read(c=>StaffChatApi.ListMessages(c,admin,channelId,null));
            var ordinaryIds=((ordinary["items"] as JsonArray)??[]).OfType<JsonObject>().Select(x=>x.S("id")).ToList();
            Assert.IsTrue(ordinaryIds.Contains(freshId));
            Assert.IsFalse(ordinaryIds.Contains(oldId));
            Assert.AreEqual(1,ordinaryIds.Count);

            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>ArchiveApi.RestoreView(c,admin,new()
            {
                ["entityType"]="chat.message",
                ["periodFrom"]=DateTime.UtcNow.AddYears(-5).ToString("yyyy-MM-dd"),
                ["periodTo"]=DateTime.UtcNow.AddYears(-3).ToString("yyyy-MM-dd")
            }))).Status);

            var restore=store.Write((c,tx)=>ArchiveApi.RequestRestore(c,tx,admin,new()
            {
                ["entityType"]="chat.message",
                ["periodFrom"]="2020-01-01",
                ["periodTo"]="2022-01-01",
                ["reasonAr"]="عرض قديم"
            }));
            // Force-active with future expiry for the view test (approval path covered elsewhere).
            store.Write((c,tx)=>
            {
                SchemaSql.Exec(c,tx,$"""
                    UPDATE {SchemaSql.Q(c,"archive","restore_request")}
                    SET status='active', expires_at=@ex WHERE id=@id
                    """,
                    ("@ex",DateTimeOffset.UtcNow.AddHours(72).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff",System.Globalization.CultureInfo.InvariantCulture)),
                    ("@id",restore.S("id")));
                return true;
            });
            var view=store.Read(c=>ArchiveApi.RestoreView(c,admin,new()
            {
                ["entityType"]="chat.message",
                ["periodFrom"]="2020-01-01",
                ["periodTo"]=DateTime.UtcNow.AddYears(-3).ToString("yyyy-MM-dd"),
                ["restoreRequestId"]=restore.S("id")
            }));
            Assert.IsTrue(view.B("readOnly"));
            var ids=((view["items"] as JsonArray)??[]).OfType<JsonObject>().Select(x=>x.S("id")).ToList();
            Assert.IsTrue(ids.Contains(oldId));
            Assert.IsFalse(ids.Contains(freshId));
            Assert.AreEqual(1,ids.Count);
        });
    }
}
