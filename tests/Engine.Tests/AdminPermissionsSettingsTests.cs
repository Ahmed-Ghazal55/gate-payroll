using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class AdminPermissionsSettingsTests
{
    static void WithStore(Action<Store> body)
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-admin-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try{body(new Store(dir));}
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    [TestMethod]
    public void AliasMapAndEffectiveCopyAndSettingsRoundTrip()
    {
        WithStore(store=>
        {
            Assert.AreEqual("accounting.chart.view",UserAccess.CanonicalPermission("finance.coa.view"));
            Assert.IsTrue(UserAccess.PermissionsCatalog.Any(p=>p.Code=="forms.responses.allCompanies"));
            Assert.IsTrue(AccessPolicy.Allowed(UserAccess.Principal(new(){["username"]="u",["role"]="Admin"}),"GET","/api/admin/settings"));
            Assert.IsTrue(AccessPolicy.Allowed(UserAccess.Principal(new(){["username"]="u",["role"]="Admin"}),"PUT","/api/admin/settings"));

            var admin=UserAccess.Principal(new(){["username"]="admin",["role"]="Admin"});
            store.Write((c,tx)=>
            {
                Store.Save(c,"user",new(){["id"]="src",["username"]="src",["role"]="Accountant",["customPermissions"]=new JsonArray("accounting.chart.view","forms.responses.allCompanies")},tx);
                Store.Save(c,"user",new(){["id"]="dst",["username"]="dst",["role"]="Reviewer",["customPermissions"]=new JsonArray()},tx);
                return true;
            });
            var effective=store.Read(c=>UserAccess.EffectivePermissionsView(c,null,Store.Get(c,"src","user")!));
            Assert.IsTrue(effective["effective"]!.AsArray().Any(p=>p?.ToString()=="accounting.chart.view"));
            var copied=store.Write((c,tx)=>UserAccess.CopyPermissions(c,tx,admin,"dst",new(){["sourceUserId"]="src"}));
            Assert.IsTrue(copied["after"]!.AsArray().Any(p=>p?.ToString()=="forms.responses.allCompanies"));
            Assert.AreNotEqual("",store.Read(c=>Store.Get(c,"dst","user")!.S("securityVersion")));

            var saved=store.Write((c,tx)=>AdminSettings.Put(c,tx,admin,new(){["items"]=new JsonArray(new JsonObject{["key"]="current_period",["value"]=new JsonObject{["period"]="2026-11"}})}));
            Assert.IsTrue(saved["items"]!.AsArray().OfType<JsonObject>().Any(i=>i.S("key")=="current_period"));
            Assert.IsTrue(store.Read(c=>Store.List(c,"audit").Any(a=>a.S("action")=="admin.settings_save")));
        });
    }
}
