using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class Auth2faApiTests
{
    static void WithStore(Action<Store> body)
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-2fa-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try{body(new Store(dir));}
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    static JsonObject SeedUser(Store store,string username,string role,params string[] perms)
    {
        var u=UserAccess.Create(username,"Password!23456",role);
        if(perms.Length>0)u["customPermissions"]=new JsonArray(perms.Select(p=>(JsonNode?)p).ToArray());
        store.Write((c,tx)=>{Store.Save(c,"user",u,tx);return true;});
        return u;
    }

    [TestMethod,DataRow(false),DataRow(true)]
    public void TotpTablesExistOnBothProviders(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        SchemaAuthCatalog.Ensure(c);SchemaAuthCatalog.Ensure(c);
        Assert.IsTrue(SchemaSql.TableExists(c,null,"auth","totp_secret"));
        Assert.IsTrue(SchemaSql.TableExists(c,null,"auth","totp_backup_code"));
    }

    [TestMethod]
    public void SetupVerifyBackupReset_AndLoginGateForFinance()
    {
        WithStore(store=>
        {
            var accountant=SeedUser(store,"acct1","Accountant");
            var actor=UserAccess.Principal(accountant);
            var setup=store.Write((c,tx)=>Auth2faApi.Setup(c,tx,store.DirectoryPath,actor,new()));
            Assert.AreEqual("setup_2fa",setup["allowedActions"]![0]!.GetValue<string>());
            string secret=setup.S("secret");
            Assert.IsFalse(string.IsNullOrWhiteSpace(secret));
            Assert.IsTrue(setup["backupCodes"] is JsonArray codes&&codes.Count>=8);

            // Secret must never be returned after setup completes (second call before verify still may show once).
            string otp=Auth2faApi.ComputeTotp(secret,DateTimeOffset.UtcNow);
            var verified=store.Write((c,tx)=>Auth2faApi.Verify(c,tx,store.DirectoryPath,actor,new(){["code"]=otp}));
            Assert.AreEqual("verify_2fa",((JsonArray)verified["allowedActions"]!)[0]!.GetValue<string>());
            Assert.AreEqual("",verified.S("secret"));
            Assert.IsTrue(verified.B("verified"));

            var again=store.Write((c,tx)=>Auth2faApi.Setup(c,tx,store.DirectoryPath,actor,new()));
            Assert.AreEqual("",again.S("secret"));
            Assert.IsNull(again["backupCodes"]);

            // Encrypted at rest — plaintext secret must not appear in SQL row.
            store.Read(c=>
            {
                using var cmd=Store.Command(c,null,$"SELECT secret_encrypted FROM {SchemaSql.Q(c,"auth","totp_secret")}");
                using var r=cmd.ExecuteReader();
                Assert.IsTrue(r.Read());
                var blob=(byte[])r.GetValue(0);
                Assert.IsFalse(Encoding.UTF8.GetString(blob).Contains(secret,StringComparison.Ordinal));
                return true;
            });

            string backup=setup["backupCodes"]![0]!.GetValue<string>();
            var used=store.Write((c,tx)=>Auth2faApi.Verify(c,tx,store.DirectoryPath,actor,new(){["code"]=backup}));
            Assert.IsTrue(used.B("verified"));
            Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>Auth2faApi.Verify(c,tx,store.DirectoryPath,actor,new(){["code"]=backup}))).Status);

            var regen=store.Write((c,tx)=>Auth2faApi.RegenBackupCodes(c,tx,store.DirectoryPath,actor,new(){["code"]=Auth2faApi.ComputeTotp(secret,DateTimeOffset.UtcNow)}));
            Assert.AreEqual("regen_backup",((JsonArray)regen["allowedActions"]!)[0]!.GetValue<string>());
            Assert.IsTrue(regen["backupCodes"] is JsonArray nc&&nc.Count>=8);

            var admin=SeedUser(store,"boss","Admin");
            var adminActor=UserAccess.Principal(admin);
            var reset=store.Write((c,tx)=>Auth2faApi.AdminReset(c,tx,store.DirectoryPath,adminActor,new(){["username"]="acct1"}));
            Assert.AreEqual("reset_2fa",((JsonArray)reset["allowedActions"]!)[0]!.GetValue<string>());
            Assert.IsTrue(store.Read(c=>Store.List(c,"audit").Any(a=>a.S("action")=="auth.2fa_reset")));

            // Finance role without verified 2FA after deadline → login blocked.
            store.Write((c,tx)=>
            {
                Auth2faApi.EnsureDeadline(c,tx,"acct1",DateTimeOffset.UtcNow.AddDays(-1));
                return true;
            });
            Assert.IsTrue(store.Read(c=>Auth2faApi.LoginBlocked(c,store.DirectoryPath,accountant)));
            Assert.IsTrue(AccessPolicy.Allowed(actor,"POST","/api/auth/2fa/setup"));
            Assert.IsTrue(AccessPolicy.Allowed(actor,"POST","/api/auth/2fa/verify"));
        });
    }

    [TestMethod]
    public void FourteenDayWindowAllowsLoginUntilDeadline()
    {
        WithStore(store=>
        {
            var u=SeedUser(store,"fin2","FinancialManager");
            store.Write((c,tx)=>{Auth2faApi.EnsureDeadline(c,tx,"fin2",DateTimeOffset.UtcNow.AddDays(10));return true;});
            Assert.IsFalse(store.Read(c=>Auth2faApi.LoginBlocked(c,store.DirectoryPath,u)));
            store.Write((c,tx)=>{Auth2faApi.EnsureDeadline(c,tx,"fin2",DateTimeOffset.UtcNow.AddDays(-1));return true;});
            Assert.IsTrue(store.Read(c=>Auth2faApi.LoginBlocked(c,store.DirectoryPath,u)));
        });
    }
}
