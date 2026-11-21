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

    [TestMethod]
    public void CopyPermissions_AppliesManagerGuard_AndMatchesSourceExactly()
    {
        WithStore(store=>
        {
            store.Write((c,tx)=>
            {
                Store.Save(c,"user",new(){["id"]="mgr",["username"]="mgr",["role"]="Accountant",["customPermissions"]=new JsonArray("users.manage","accounting.chart.view"),["status"]="active"},tx);
                Store.Save(c,"user",new(){["id"]="src",["username"]="src",["role"]="Reviewer",["customPermissions"]=new JsonArray("accounting.chart.view"),["deniedPermissions"]=new JsonArray("operations.view"),["status"]="active"},tx);
                Store.Save(c,"user",new(){["id"]="dst",["username"]="dst",["role"]="Accountant",["customPermissions"]=new JsonArray("treasury.journals"),["status"]="active"},tx);
                Store.Save(c,"user",new(){["id"]="admin-tgt",["username"]="admin-tgt",["role"]="Admin",["status"]="active"},tx);
                return true;
            });
            var manager=UserAccess.Principal(store.Read(c=>Store.Get(c,"mgr","user")!));
            var admin=UserAccess.Principal(new(){["username"]="admin",["role"]="Admin"});
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>UserAccess.CopyPermissions(c,tx,manager,"admin-tgt",new(){["sourceUserId"]="src"}))).Status);
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>UserAccess.CopyPermissions(c,tx,manager,"mgr",new(){["sourceUserId"]="src"}))).Status);

            var copied=store.Write((c,tx)=>UserAccess.CopyPermissions(c,tx,admin,"dst",new(){["sourceUserId"]="src"}));
            var after=copied["after"]!.AsArray().Select(p=>p?.ToString()??"").OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            var sourceEffective=store.Read(c=>UserAccess.EffectivePermissions(UserAccess.WithRole(c,null,Store.Get(c,"src","user")!))).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(sourceEffective,after);
            var dst=store.Read(c=>Store.Get(c,"dst","user")!);
            Assert.IsTrue((dst["deniedPermissions"] as JsonArray??[]).Select(p=>p?.ToString()??"").Contains("operations.view")
                ||!(dst["customPermissions"] as JsonArray??[]).Select(p=>p?.ToString()??"").Contains("treasury.journals"));
            Assert.IsFalse(after.Contains("treasury.journals"));
            Assert.IsTrue(store.Read(c=>Store.List(c,"audit").Any(a=>a.S("action")=="admin.copy_permissions")));
        });
    }
}
