using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class AuditApiTests
{
    static ClaimsPrincipal Actor(string name,params string[] perms)=>
        UserAccess.Principal(new JsonObject
        {
            ["username"]=name,["role"]="Accountant",
            ["customPermissions"]=new JsonArray(perms.Select(p=>(JsonNode?)p).ToArray())
        });

    static void WithStore(Action<Store> body)
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-audit-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try{body(new Store(dir));}
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    [TestMethod]
    public void DataChangeAuditIsShort_AndReadsAreNotLogged()
    {
        WithStore(store=>
        {
            var admin=Actor("audit-admin","admin.audit.view");
            store.Write((c,tx)=>{AuditApi.WriteChange(c,tx,"إنشاء","حساب","599901","coa-a",new JsonObject{["from"]="",["to"]="حساب"});return true;});
            var page=store.Read(c=>AuditApi.List(c,admin,null,null,null,null,null,1,50));
            Assert.AreEqual(1,page["items"]!.AsArray().Count);
            var row=page["items"]![0]!.AsObject();
            Assert.AreEqual("إنشاء",row.S("actionAr"));
            Assert.IsFalse(row.ContainsKey("iban"));
            Assert.IsFalse(AccessPolicy.Allowed(Actor("x"),"GET","/api/audit"));
            Assert.IsTrue(AccessPolicy.Allowed(admin,"GET","/api/audit"));
            // Read events must not create change_log rows.
            store.Write((c,tx)=>{AuditApi.NoteRead(c,tx,"audit-admin","iban",false);return true;});
            Assert.AreEqual(1,store.Read(c=>AuditApi.List(c,admin,null,null,null,null,null,1,50)["items"]!.AsArray().Count));
        });
    }

    [TestMethod]
    public void SelfApprovalsAndReadSummaryRequireSeparatePermissions()
    {
        WithStore(store=>
        {
            var audit=Actor("a1","admin.audit.view");
            var summary=Actor("a2","admin.read_summary.view");
            Assert.IsTrue(AccessPolicy.Allowed(audit,"GET","/api/admin/audit/self-approvals"));
            Assert.IsFalse(AccessPolicy.Allowed(audit,"GET","/api/admin/read-summary"));
            Assert.IsTrue(AccessPolicy.Allowed(summary,"GET","/api/admin/read-summary"));
            store.Write((c,tx)=>
            {
                Store.Save(c,"audit",new(){["id"]=Guid.NewGuid().ToString("N"),["action"]="approval.self_decision",["entityId"]="req1",["actor"]="sysadmin",["at"]=DateTimeOffset.UtcNow.ToString("O"),["selfApprovedException"]=true},tx);
                AuditApi.NoteRead(c,tx,"reader","iban",true);
                return true;
            });
            var self=store.Read(c=>AuditApi.SelfApprovals(c,audit,null,null));
            Assert.IsTrue(self["items"]!.AsArray().Count>=1);
            var reads=store.Read(c=>AuditApi.ReadSummary(c,summary,null,null,null));
            Assert.IsTrue(reads["items"]!.AsArray().Count>=1);
        });
    }
}
