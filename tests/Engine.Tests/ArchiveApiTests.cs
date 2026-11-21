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
                var decision=new JsonObject
                {
                    ["id"]=SchemaSql.GuidText(SchemaSql.NewId()),
                    ["requestId"]=approvalId,
                    ["outcome"]="approved",
                    ["decidedBy"]="approver2",
                    ["decidedAt"]=DateTimeOffset.UtcNow.ToString("O")
                };
                Store.Save(c,"approval_decision",decision,tx);
                var ar=Store.Get(c,approvalId,"approval_request",tx)!;
                ar["status"]="approved";ar["decidedBy"]="approver2";
                Store.Save(c,"approval_request",ar,tx);
                ArchiveApi.ApplyApproval(c,tx,approvalId,"approved","approver2");
                return true;
            });

            var after=store.Read(c=>ArchiveApi.ListPolicies(c,approver));
            var restores=(after["restoreRequests"] as JsonArray??[]).OfType<JsonObject>().ToList();
            Assert.IsTrue(restores.Any(r=>r.S("status")=="active"&&r.S("expiresAt")!=""));
            Assert.IsTrue(restores.All(r=>r.B("readOnly")));
            Assert.IsFalse(AccessPolicy.Allowed(UserAccess.Principal(new(){["username"]="x",["role"]="Reviewer"}),"GET","/api/admin/archive/policies"));
        });
    }
}
