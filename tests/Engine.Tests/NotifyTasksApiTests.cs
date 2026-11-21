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
            store.Write((c,tx)=>{Store.Save(c,"user",new(){["id"]="u1",["username"]="assignee",["role"]="Accountant",["status"]="active"},tx);return true;});
            var task=store.Write((c,tx)=>NotifyTasksApi.CreateTask(c,tx,admin,new(){["titleAr"]="مهمة",["assignee"]="assignee",["dueAt"]=DateTimeOffset.UtcNow.AddDays(-1).ToString("O")}));
            Assert.AreEqual("open",task.S("status"));
            store.Write((c,tx)=>{NotifyTasksApi.EscalateOverdue(c,tx);return true;});
            Assert.AreEqual("escalated",store.Read(c=>Store.Get(c,task.S("id"),NotifyTasksApi.TaskKind)!.S("status")));
            Assert.AreNotEqual("completed",store.Read(c=>Store.Get(c,task.S("id"),NotifyTasksApi.TaskKind)!.S("status")));
            Assert.IsTrue(AccessPolicy.Allowed(admin,"GET","/api/admin/notify-settings"));
            Assert.IsTrue(AccessPolicy.Allowed(admin,"POST","/api/tasks"));
        });
    }
}
