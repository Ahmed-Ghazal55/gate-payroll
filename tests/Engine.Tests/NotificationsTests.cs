using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class NotificationsTests
{
    [TestMethod]
    public void AnswerNotificationsAreThrottledAndTransactional()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-notifications-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            store.Write((c,tx)=>
            {
                foreach(string id in new[]{"synthetic-answer-one","synthetic-answer-two"})
                {
                    Store.Save(c,"submission",new(){["id"]=id,["campaignId"]="synthetic-campaign"},tx);
                    Notifications.OnAudit(c,tx,"submission.received",id,"synthetic",DateTimeOffset.Parse("2026-11-03T12:05:00Z"));
                }
                return true;
            });
            Assert.AreEqual(1,store.Read(c=>Store.List(c,"sys_notification").Count));
            Assert.AreEqual(1,store.Read(c=>Store.List(c,"audit").Count(a=>a.S("action")=="notification.system_created")));
            Assert.ThrowsExactly<InvalidOperationException>(()=>store.Write<int>((c,tx)=>{Notifications.Raise(c,tx,"rolled-back","system","اختبار","forms.view","synthetic");throw new InvalidOperationException();}));
            Assert.IsNull(store.Read(c=>Store.Get(c,"rolled-back","sys_notification")));
            store.Write((c,tx)=>{Notifications.OnAudit(c,tx,"submission.received","synthetic-answer-one","synthetic",DateTimeOffset.Parse("2026-11-03T13:00:00Z"));return true;});
            Assert.AreEqual(1,store.Read(c=>Store.List(c,"sys_notification").Count)); // Fixed-hour boundaries must not bypass the rolling hour.
            store.Write((c,tx)=>{Notifications.Answers(c,tx,"synthetic-campaign","synthetic",DateTimeOffset.Parse("2026-11-03T13:06:00Z"));return true;});
            Assert.AreEqual(2,store.Read(c=>Store.List(c,"sys_notification").Count));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
    }
    [TestMethod]
    public void DataQualityIsDailyAndSystemVisibilityRequiresMatchingPermission()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-notification-quality-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);var admin=UserAccess.Principal(new JsonObject{["username"]="synthetic-admin",["role"]="Admin"});
            var reader=UserAccess.Principal(new JsonObject{["username"]="synthetic-reader",["role"]="Reviewer",["deniedPermissions"]=new JsonArray("riders.view")});
            store.Write((c,tx)=>{Store.Save(c,"rider",new(){["id"]="2000000001",["name"]="Synthetic Rider"},tx);Store.Audit(c,tx,"import.commit","synthetic-import","synthetic");return true;});
            Notifications.EnsureDataQuality(store,admin);Notifications.EnsureDataQuality(store,admin);
            Assert.AreEqual(2,store.Read(c=>Store.List(c,"sys_notification").Count));
            Assert.AreEqual(0,store.Read(c=>Notifications.Inbox(c,reader).Count));
            Assert.AreEqual(2,store.Read(c=>Notifications.Inbox(c,admin).Count));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
    }
}
