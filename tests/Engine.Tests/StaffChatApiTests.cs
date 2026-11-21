using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class StaffChatApiTests
{
    static void WithStore(Action<Store> body)
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-chat-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try{body(new Store(dir));}
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    [TestMethod,DataRow(false),DataRow(true)]
    public void MembershipAndMediaColumnsExist(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        SchemaAuthCatalog.Ensure(c);SchemaAuditNotify.Ensure(c);SchemaAuditNotify.Ensure(c);
        Assert.IsTrue(SchemaSql.TableExists(c,null,"chat","membership"));
    }

    [TestMethod]
    public void ChannelsMessagesAttachments_ScopeMediaLimits_NoNotify()
    {
        WithStore(store=>
        {
            string company=FinancialCore.Sponsors[0].Id;
            store.Write((c,tx)=>
            {
                Store.Save(c,"user",UserAccess.Create("chadmin","Password!23456","Admin"),tx);
                var poster=UserAccess.Create("poster1","Password!23456","Accountant");
                poster["customPermissions"]=new JsonArray("chat.view","chat.post");
                poster["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(company),["accountCodes"]=new JsonArray(),["platforms"]=new JsonArray(),["categoryCodes"]=new JsonArray()};
                Store.Save(c,"user",poster,tx);
                return true;
            });
            var admin=UserAccess.Principal(store.Read(c=>Store.List(c,"user").First(u=>u.S("username")=="chadmin")));
            var poster=UserAccess.Principal(store.Read(c=>Store.List(c,"user").First(u=>u.S("username")=="poster1")));

            var channel=store.Write((c,tx)=>StaffChatApi.CreateChannel(c,tx,admin,new()
            {
                ["code"]="ops-room",
                ["nameAr"]="غرفة التشغيل",
                ["companyId"]=company,
                ["memberUsernames"]=new JsonArray("poster1")
            }));
            Assert.AreEqual("ops-room",channel.S("code"));
            Assert.IsFalse(channel.B("chatCreatesNotify"));

            var listed=store.Write((c,tx)=>{_=SchemaApprovals.EnsureAuthUserId(c,tx,"poster1");return StaffChatApi.ListChannels(c,tx,poster);});
            Assert.AreEqual(1,(listed["items"] as JsonArray)!.Count);

            var msg=store.Write((c,tx)=>StaffChatApi.PostMessage(c,tx,poster,channel.S("id"),new(){["bodyAr"]="مرحبا"}));
            Assert.IsFalse(msg.B("chatCreatesNotify"));
            Assert.AreEqual(0,store.Read(c=>Convert.ToInt64(SchemaSql.Scalar(c,null,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"notify","notification")}")!)));

            Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>StaffChatApi.PostAttachment(c,tx,poster,channel.S("id"),new()
            {
                ["mediaKind"]="voice",["mediaBytes"]=1000,["mediaDurationSeconds"]=180,["mediaName"]="long.ogg"
            }))).Status);
            Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>StaffChatApi.PostAttachment(c,tx,poster,channel.S("id"),new()
            {
                ["mediaKind"]="video",["mediaBytes"]=1000,["mediaName"]="x.mp4"
            }))).Status);

            var voice=store.Write((c,tx)=>StaffChatApi.PostAttachment(c,tx,poster,channel.S("id"),new()
            {
                ["mediaKind"]="voice",["mediaBytes"]=1500,["mediaDurationSeconds"]=90,["mediaName"]="ok.ogg"
            }));
            Assert.AreNotEqual("",voice.S("id"));

            var polled=store.Read(c=>StaffChatApi.ListMessages(c,poster,channel.S("id"),null));
            Assert.IsTrue((polled["items"] as JsonArray)!.Count>=2);
            Assert.IsTrue(AccessPolicy.Allowed(poster,"GET","/api/chat/channels"));
            Assert.IsTrue(AccessPolicy.Allowed(poster,"POST","/api/chat/channels/"+channel.S("id")+"/messages"));
        });
    }
}
