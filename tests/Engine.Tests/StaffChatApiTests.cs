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
                ["mediaKind"]="voice",["mediaBytes"]=1000,["mediaDurationSeconds"]=180,["mediaName"]="long.ogg",
                ["mediaBase64"]=Convert.ToBase64String(TinyWav(180))
            }))).Status);
            Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>StaffChatApi.PostAttachment(c,tx,poster,channel.S("id"),new()
            {
                ["mediaKind"]="video",["mediaBytes"]=1000,["mediaName"]="x.mp4",
                ["mediaBase64"]=Convert.ToBase64String(new byte[]{0,0,0,0})
            }))).Status);

            byte[] wav=TinyWav(90);
            Assert.AreEqual(44+90,wav.Length); // 90s mono 8-bit 1Hz + 44-byte header
            var voice=store.Write((c,tx)=>StaffChatApi.PostAttachment(c,tx,poster,channel.S("id"),new()
            {
                ["mediaKind"]="voice",["mediaName"]="ok.wav",
                ["mediaBase64"]=Convert.ToBase64String(wav)
            }));
            Assert.AreNotEqual("",voice.S("id"));
            Assert.AreEqual(wav.Length,(int)voice.N("mediaBytes"));
            Assert.AreEqual(90,(int)voice.N("mediaDurationSeconds"));

            var downloaded=store.Read(c=>StaffChatApi.DownloadMedia(c,poster,channel.S("id"),voice.S("id")));
            Assert.AreEqual(wav.Length,downloaded.Bytes.Length);
            CollectionAssert.AreEqual(wav,downloaded.Bytes);

            var polled=store.Read(c=>StaffChatApi.ListMessages(c,poster,channel.S("id"),null));
            Assert.IsTrue((polled["items"] as JsonArray)!.Count>=2);
            Assert.IsTrue(AccessPolicy.Allowed(poster,"GET","/api/chat/channels"));
            Assert.IsTrue(AccessPolicy.Allowed(poster,"POST","/api/chat/channels/"+channel.S("id")+"/messages"));
        });
    }

    [TestMethod]
    public void ChatAdmin_WithoutMembership_CannotCrossCompany_ChannelCompanyEnforced()
    {
        WithStore(store=>
        {
            string companyA=FinancialCore.Sponsors[0].Id;
            string companyB=FinancialCore.Sponsors[1].Id;
            Assert.AreNotEqual(companyA,companyB);
            store.Write((c,tx)=>
            {
                Store.Save(c,"user",UserAccess.Create("sysadmin","Password!23456","Admin"),tx);
                var scoped=UserAccess.Create("chatadm","Password!23456","Accountant");
                scoped["customPermissions"]=new JsonArray("chat.view","chat.post","chat.admin");
                scoped["financialScope"]=new JsonObject
                {
                    ["sponsorIds"]=new JsonArray(companyA),
                    ["accountCodes"]=new JsonArray(),
                    ["platforms"]=new JsonArray(),
                    ["categoryCodes"]=new JsonArray()
                };
                Store.Save(c,"user",scoped,tx);
                return true;
            });
            var sys=UserAccess.Principal(store.Read(c=>Store.List(c,"user").First(u=>u.S("username")=="sysadmin")));
            var chatAdm=UserAccess.Principal(store.Read(c=>Store.List(c,"user").First(u=>u.S("username")=="chatadm")));
            var other=store.Write((c,tx)=>StaffChatApi.CreateChannel(c,tx,sys,new()
            {
                ["code"]="other-co",
                ["nameAr"]="شركة أخرى",
                ["companyId"]=companyB,
                ["memberUsernames"]=new JsonArray()
            }));
            Assert.AreEqual(companyB,other.S("companyId"));
            // chat.admin without membership and scoped to company A must not read/post company B.
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>StaffChatApi.ListMessages(c,chatAdm,other.S("id"),null))).Status);
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>StaffChatApi.PostMessage(c,tx,chatAdm,other.S("id"),new(){["bodyAr"]="اختراق"}))).Status);
            var listed=store.Write((c,tx)=>{_=SchemaApprovals.EnsureAuthUserId(c,tx,"chatadm");return StaffChatApi.ListChannels(c,tx,chatAdm);});
            Assert.AreEqual(0,(listed["items"] as JsonArray)!.Count);
        });
    }

    [TestMethod]
    public void Attachment_RejectsClientSizeLie_StoresExactServerBytes()
    {
        WithStore(store=>
        {
            string company=FinancialCore.Sponsors[0].Id;
            store.Write((c,tx)=>
            {
                Store.Save(c,"user",UserAccess.Create("media1","Password!23456","Admin"),tx);
                return true;
            });
            var admin=UserAccess.Principal(store.Read(c=>Store.List(c,"user").First(u=>u.S("username")=="media1")));
            var channel=store.Write((c,tx)=>StaffChatApi.CreateChannel(c,tx,admin,new()
            {
                ["code"]="media-room",["nameAr"]="وسائط",["companyId"]=company
            }));
            byte[] png=Png1x1();
            Assert.AreEqual(70,png.Length);
            var att=store.Write((c,tx)=>StaffChatApi.PostAttachment(c,tx,admin,channel.S("id"),new()
            {
                ["mediaKind"]="image",
                ["mediaName"]="dot.png",
                ["mediaBytes"]=1, // client lie
                ["mediaBase64"]=Convert.ToBase64String(png)
            }));
            Assert.AreEqual(70,(int)att.N("mediaBytes"));
            Assert.AreEqual("image/png",att.S("contentType"));
            var file=store.Read(c=>StaffChatApi.DownloadMedia(c,admin,channel.S("id"),att.S("id")));
            Assert.AreEqual(70,file.Bytes.Length);
            Assert.AreEqual("image/png",file.ContentType);
        });
    }

    // 1Hz 8-bit mono PCM WAV: durationSeconds samples + 44-byte header.
    static byte[] TinyWav(int durationSeconds)
    {
        int dataLen=durationSeconds; // 1 sample/sec
        var buf=new byte[44+dataLen];
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("RIFF"),0,buf,0,4);
        BitConverter.TryWriteBytes(buf.AsSpan(4),36+dataLen);
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "),0,buf,8,8);
        BitConverter.TryWriteBytes(buf.AsSpan(16),16);
        BitConverter.TryWriteBytes(buf.AsSpan(20),(short)1);
        BitConverter.TryWriteBytes(buf.AsSpan(22),(short)1);
        BitConverter.TryWriteBytes(buf.AsSpan(24),1); // sample rate 1 Hz
        BitConverter.TryWriteBytes(buf.AsSpan(28),1);
        BitConverter.TryWriteBytes(buf.AsSpan(32),(short)1);
        BitConverter.TryWriteBytes(buf.AsSpan(34),(short)8);
        Buffer.BlockCopy(System.Text.Encoding.ASCII.GetBytes("data"),0,buf,36,4);
        BitConverter.TryWriteBytes(buf.AsSpan(40),dataLen);
        return buf;
    }

    static byte[] Png1x1()
    {
        // Minimal valid 1×1 PNG (68 bytes).
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    }
}
