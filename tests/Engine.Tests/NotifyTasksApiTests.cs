using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class NotifyTasksApiTests
{
    static void WithStore(Action<Store> body)
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-notify-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try{body(new Store(dir));}
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    [TestMethod]
    public void ReplyDefaultIsThreeWorkingDays_ChatCannotNotify_TasksEscalateWithoutAutoComplete()
    {
        WithStore(store=>
        {
            var admin=UserAccess.Principal(new(){["username"]="admin",["role"]="Admin"});
            Assert.AreEqual(3,store.Read(c=>NotifyTasksApi.ReplyWorkingDays(c)));
            // Thursday 2026-11-19 + 3 working days (skip Fri/Sat) → Tuesday 2026-11-24
            var due=NotifyTasksApi.AddWorkingDays(DateTimeOffset.Parse("2026-11-19T10:00:00+03:00"),3);
            Assert.AreEqual(new DateTimeOffset(2026,11,24,10,0,0,TimeSpan.FromHours(3)),due);

            Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>NotifyTasksApi.Send(c,tx,admin,new(){["titleAr"]="x",["source"]="chat"}))).Status);

            store.Write((c,tx)=>NotifyTasksApi.PutSettings(c,tx,admin,new(){["replyWorkingDays"]=3,["matrix"]=new JsonArray()}));
            store.Write((c,tx)=>{Store.Save(c,"user",new(){["id"]=SchemaSql.GuidText(SchemaSql.NewId()),["username"]="assignee",["role"]="Accountant",["status"]="active"},tx);return true;});
            string company=FinancialCore.Sponsors[0].Id;
            var task=store.Write((c,tx)=>NotifyTasksApi.CreateTask(c,tx,admin,new(){["titleAr"]="مهمة",["assignee"]="assignee",["companyId"]=company,["dueAt"]=DateTimeOffset.UtcNow.AddDays(-1).ToString("O")}));
            Assert.AreEqual("open",task.S("status"));
            store.Write((c,tx)=>{NotifyTasksApi.ProcessReminders(c,tx);return true;});
            Assert.AreEqual("escalated",store.Read(c=>Store.Get(c,task.S("id"),NotifyTasksApi.TaskKind)!.S("status")));
            Assert.AreNotEqual("completed",store.Read(c=>Store.Get(c,task.S("id"),NotifyTasksApi.TaskKind)!.S("status")));
            // WMRG: real notify.subscription + task.item rows.
            Assert.IsTrue(store.Read(c=>Convert.ToInt64(SchemaSql.Scalar(c,null,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"notify","subscription")} WHERE event_type=@e",("@e","settings.reply_working_days"))!)>=1));
            Assert.IsTrue(store.Read(c=>Convert.ToInt64(SchemaSql.Scalar(c,null,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"task","item")} WHERE id=@id",("@id",task.S("id")))!)==1));
            Assert.AreEqual("escalated",store.Read(c=>Convert.ToString(SchemaSql.Scalar(c,null,$"SELECT status FROM {SchemaSql.Q(c,"task","item")} WHERE id=@id",("@id",task.S("id")))!)));
            Assert.IsTrue(AccessPolicy.Allowed(admin,"GET","/api/admin/notify-settings"));
            Assert.IsTrue(AccessPolicy.Allowed(admin,"POST","/api/tasks"));
        });
    }

    [TestMethod]
    public void TasksStayInCompanyAndOwnership_SendChecksRecipients()
    {
        WithStore(store=>
        {
            string company=FinancialCore.Sponsors[0].Id,other=FinancialCore.Sponsors[1].Id;
            store.Write((c,tx)=>
            {
                Store.Save(c,"user",new(){["id"]=SchemaSql.GuidText(SchemaSql.NewId()),["username"]="owner",["role"]="Accountant",["status"]="active",["customPermissions"]=new JsonArray("tasks.assign","tasks.manage","notify.send.custom","notify.send.general"),["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(company),["accountCodes"]=new JsonArray(),["platforms"]=new JsonArray(),["categoryCodes"]=new JsonArray()}},tx);
                Store.Save(c,"user",new(){["id"]=SchemaSql.GuidText(SchemaSql.NewId()),["username"]="stranger",["role"]="Reviewer",["status"]="active",["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(other),["accountCodes"]=new JsonArray(),["platforms"]=new JsonArray(),["categoryCodes"]=new JsonArray()}},tx);
                return true;
            });
            var owner=store.Read(c=>UserAccess.Principal(Store.List(c,"user").First(u=>u.S("username")=="owner")));
            var task=store.Write((c,tx)=>NotifyTasksApi.CreateTask(c,tx,owner,new(){["titleAr"]="ملكي",["companyId"]=company}));
            var listed=store.Read(c=>NotifyTasksApi.ListTasks(c,owner,null));
            Assert.AreEqual(1,listed["items"]!.AsArray().Count);
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>NotifyTasksApi.CreateTask(c,tx,owner,new(){["titleAr"]="خارج",["companyId"]=other}))).Status);
            Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>NotifyTasksApi.CreateTask(c,tx,owner,new(){["titleAr"]="وهمي",["companyId"]=company,["assignee"]="ghost"}))).Status);
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>NotifyTasksApi.Send(c,tx,owner,new(){["titleAr"]="x",["custom"]=true,["companyId"]=company,["userIds"]=new JsonArray("stranger")}))).Status);
        });
    }
}
