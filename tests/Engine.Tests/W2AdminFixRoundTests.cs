using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class W2AdminFixRoundTests
{
    static void WithStore(Action<Store> body)
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-w2f-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try{body(new Store(dir));}
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    [TestMethod]
    public void CoA_IsUsedReadsDocumentJournals_AndDeactivatedNotPostable()
    {
        WithStore(store=>
        {
            var actor=UserAccess.Principal(new(){["username"]="coa",["role"]="Admin"});
            store.Write((c,tx)=>ChartOfAccountsApi.Create(c,tx,actor,new(){["parentCode"]="5",["code"]="599920",["nameAr"]="مستخدم قيود",["isPostable"]=true}));
            store.Write((c,tx)=>
            {
                Store.Save(c,"journal",new()
                {
                    ["id"]="j1",["period"]="2026-11",["status"]="posted",
                    ["lines"]=new JsonArray(new JsonObject{["accountCode"]="599920",["debit"]=1,["credit"]=0})
                },tx);
                return true;
            });
            Assert.IsTrue(store.Read(c=>ChartOfAccountsApi.IsUsed(c,null,"599920")));
            store.Write((c,tx)=>ChartOfAccountsApi.Deactivate(c,tx,actor,"599920"));
            // Second person confirm for used account.
            var other=UserAccess.Principal(new(){["username"]="coa2",["role"]="Admin"});
            store.Write((c,tx)=>ChartOfAccountsApi.Deactivate(c,tx,other,"599920"));
            Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>{ChartOfAccountsApi.DemandPostable(c,null,"599920");return true;})).Status);
        });
    }

    [TestMethod]
    public void Audit_UnionsLegacyDocuments_KeepsEvidence_AndScopesCompanies()
    {
        WithStore(store=>
        {
            string company=FinancialCore.Sponsors[0].Id,other=FinancialCore.Sponsors[1].Id;
            store.Write((c,tx)=>
            {
                Store.Save(c,"user",new(){["id"]=SchemaSql.GuidText(SchemaSql.NewId()),["username"]="auditor",["role"]="Accountant",["status"]="active",["customPermissions"]=new JsonArray("admin.audit.view"),["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(company),["accountCodes"]=new JsonArray(),["platforms"]=new JsonArray("Jahez"),["categoryCodes"]=new JsonArray()}},tx);
                // Legacy document audit (pre-change_log).
                using var insert=Store.Command(c,tx,"INSERT INTO records(id,kind,body) VALUES(@id,@kind,@body)",
                    ("@id","legacy-audit-1"),("@kind","audit"),("@body",new JsonObject{["id"]="legacy-audit-1",["action"]="approval.bypass_maker_checker",["actor"]="old",["at"]="2026-01-01T00:00:00Z",["companyId"]=company,["grantId"]="grant-keep",["reason"]=new string('ر',120),["selfApprovedException"]=true}.ToJsonString()));
                insert.ExecuteNonQuery();
                Store.Save(c,"audit",new(){["id"]=Guid.NewGuid().ToString("N"),["action"]="approval.self_decision",["actor"]="auditor",["at"]=DateTimeOffset.UtcNow.ToString("O"),["companyId"]=other,["reason"]="other-co",["selfApprovedException"]=true},tx);
                Store.Save(c,"audit",new(){["id"]=Guid.NewGuid().ToString("N"),["action"]="operations.rider_accounts_read",["actor"]="auditor",["at"]=DateTimeOffset.UtcNow.ToString("O"),["companyId"]=company},tx);
                return true;
            });
            var scoped=store.Read(c=>UserAccess.Principal(Store.List(c,"user").First(u=>u.S("username")=="auditor")));
            var list=store.Read(c=>AuditApi.Query(c,scoped,null,null,null,null,null));
            Assert.IsTrue(list.Any(a=>a.S("id")=="legacy-audit-1"));
            Assert.IsFalse(list.Any(a=>a.S("action")=="operations.rider_accounts_read"));
            Assert.IsFalse(list.Any(a=>a.S("companyId")==other));
            var legacy=list.First(a=>a.S("id")=="legacy-audit-1");
            Assert.AreEqual("grant-keep",legacy.S("grantId"));
            Assert.IsTrue(legacy.B("selfApprovedException"));
        });
    }

    [TestMethod]
    public void TaskProjection_LoadsCommentsAfterReader_AndPersistsAttachments()
    {
        WithStore(store=>
        {
            var admin=UserAccess.Principal(new(){["username"]="admin",["role"]="Admin"});
            store.Write((c,tx)=>
            {
                Store.Save(c,"user",new(){["id"]=SchemaSql.GuidText(SchemaSql.NewId()),["username"]="admin",["role"]="Admin",["status"]="active"},tx);
                return true;
            });
            string company=FinancialCore.Sponsors[0].Id;
            var created=store.Write((c,tx)=>NotifyTasksApi.CreateTask(c,tx,admin,new()
            {
                ["titleAr"]="مع تعليقات",
                ["bodyAr"]="النص الأصلي",
                ["companyId"]=company,
                ["comments"]=new JsonArray(new JsonObject{["bodyAr"]="تعليق1",["by"]="admin"}),
                ["attachments"]=new JsonArray(new JsonObject{["fileName"]="a.pdf",["fileHash"]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("x"u8.ToArray()))})
            }));
            var loaded=store.Read(c=>Store.Get(c,created.S("id"),NotifyTasksApi.TaskKind)!);
            Assert.IsTrue(loaded["comments"]!.AsArray().Count>=1);
            Assert.IsTrue(loaded["attachments"]!.AsArray().Count>=1);
            store.Write((c,tx)=>NotifyTasksApi.UpdateTask(c,tx,admin,created.S("id"),new()
            {
                ["bodyAr"]="بعد التعديل",
                ["comments"]=new JsonArray(new JsonObject{["bodyAr"]="تعليق2",["by"]="admin"}),
                ["attachments"]=new JsonArray(new JsonObject{["fileName"]="b.pdf",["fileHash"]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("y"u8.ToArray()))})
            }));
            var again=store.Read(c=>Store.Get(c,created.S("id"),NotifyTasksApi.TaskKind)!);
            Assert.AreEqual("تعليق2",again["comments"]![0]!.AsObject().S("bodyAr"));
            Assert.AreEqual("b.pdf",again["attachments"]![0]!.AsObject().S("fileName"));
        });
    }

    [TestMethod]
    public void TaskBodyEdit_PersistsEvenWhenCommentsAlreadyExist()
    {
        WithStore(store=>
        {
            var admin=UserAccess.Principal(new(){["username"]="admin",["role"]="Admin"});
            store.Write((c,tx)=>
            {
                Store.Save(c,"user",new(){["id"]=SchemaSql.GuidText(SchemaSql.NewId()),["username"]="admin",["role"]="Admin",["status"]="active"},tx);
                return true;
            });
            string company=FinancialCore.Sponsors[0].Id;
            var created=store.Write((c,tx)=>NotifyTasksApi.CreateTask(c,tx,admin,new()
            {
                ["titleAr"]="نص وتعليق",
                ["bodyAr"]="النص الأصلي",
                ["companyId"]=company,
                ["comments"]=new JsonArray(new JsonObject{["bodyAr"]="تعليق قائم",["by"]="admin"})
            }));
            store.Write((c,tx)=>NotifyTasksApi.UpdateTask(c,tx,admin,created.S("id"),new()
            {
                ["bodyAr"]="النص بعد التعديل"
            }));
            var again=store.Read(c=>Store.Get(c,created.S("id"),NotifyTasksApi.TaskKind)!);
            Assert.AreEqual("النص بعد التعديل",again.S("bodyAr"));
            Assert.IsTrue(again["comments"]!.AsArray().OfType<JsonObject>().Any(c=>c.S("bodyAr")=="تعليق قائم"));
        });
    }
}
