using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class ChartOfAccountsApiTests
{
    static ClaimsPrincipal Actor(string name,params string[] perms)=>
        UserAccess.Principal(new JsonObject
        {
            ["username"]=name,["role"]="Reviewer",
            ["customPermissions"]=new JsonArray(perms.Select(p=>(JsonNode?)p).ToArray())
        });

    static void WithStore(Action<Store> body,bool sqlServer=false)
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-coa-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            if(!sqlServer){body(new Store(dir));return;}
            string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
            if(string.IsNullOrWhiteSpace(configured))Assert.Inconclusive("PAYROLL_TEST_SQL_CONNECTION not set; isolated SQL Server required.");
            var builder=new SqlConnectionStringBuilder(configured){Pooling=false};
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(builder.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            builder.InitialCatalog="master";
            string db="GateCoa_"+Guid.NewGuid().ToString("N");
            using(var admin=new SqlConnection(builder.ToString())){admin.Open();SchemaSql.Exec(admin,null,$"CREATE DATABASE [{db}]");}
            builder.InitialCatalog=db;
            try{body(new Store(dir,builder.ToString()));}
            finally
            {
                using var admin=new SqlConnection(new SqlConnectionStringBuilder(configured){InitialCatalog="master",Pooling=false}.ToString());
                admin.Open();
                SchemaSql.Exec(admin,null,$"ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]");
            }
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CreateChildLocksNatureToParent_AndSplitsPermissions(bool sqlServer)
    {
        WithStore(store=>
        {
            var viewer=Actor("coa-viewer","accounting.chart.view");
            var creator=Actor("coa-creator","accounting.chart.view","accounting.chart.create");
            var editor=Actor("coa-editor","accounting.chart.view","accounting.chart.edit");
            Assert.IsFalse(AccessPolicy.Allowed(viewer,"POST","/api/finance/coa"));
            Assert.IsTrue(AccessPolicy.Allowed(creator,"POST","/api/finance/coa"));
            Assert.IsTrue(AccessPolicy.Allowed(editor,"PUT","/api/finance/coa/1"));
            Assert.IsFalse(AccessPolicy.Allowed(creator,"DELETE","/api/finance/coa/599901"));

            var created=store.Write((c,tx)=>ChartOfAccountsApi.Create(c,tx,creator,new()
            {
                ["parentCode"]="5",["code"]="599901",["nameAr"]="حساب تجريبي",["isPostable"]=true
            }));
            Assert.AreEqual("599901",created.S("code"));
            Assert.AreEqual(store.Read(c=>Convert.ToInt32(SchemaSql.Scalar(c,null,"SELECT nature FROM "+SchemaSql.Q(c,"fin","account")+" WHERE code='5'")!)),created["nature"]!.GetValue<int>());
            Assert.IsTrue(created["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="create"));
            Assert.IsTrue(store.Read(c=>Store.List(c,"audit").Any(a=>a.S("action")=="accounting.chart.create"&&a.S("entityId")=="599901")));
        },sqlServer);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EditBlocksCodeChangeWhenPosted_AndDeactivateNeverDeletes(bool sqlServer)
    {
        WithStore(store=>
        {
            var actor=Actor("coa-c","accounting.chart.view","accounting.chart.create","accounting.chart.edit","accounting.chart.deactivate");
            store.Write((c,tx)=>ChartOfAccountsApi.Create(c,tx,actor,new(){["parentCode"]="5",["code"]="599902",["nameAr"]="قبل الترحيل",["isPostable"]=true}));
            store.Write((c,tx)=>{Store.Save(c,"coa_posted_use",new(){["id"]="coa-use:599902",["code"]="599902"},tx);return true;});
            Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>ChartOfAccountsApi.Update(c,tx,actor,"599902",new(){["code"]="599903",["nameAr"]="محاولة"}))).Status);
            var updated=store.Write((c,tx)=>ChartOfAccountsApi.Update(c,tx,actor,"599902",new(){["nameAr"]="بعد التعديل"}));
            Assert.AreEqual("بعد التعديل",updated.S("nameAr"));
            Assert.AreEqual(1L,store.Read(c=>Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM "+SchemaSql.Q(c,"fin","account")+" WHERE code='599902'")!)));
        },sqlServer);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DeactivateUsedNeedsSecondPerson_UnusedIsImmediate_ThenReactivate(bool sqlServer)
    {
        WithStore(store=>
        {
            var a=Actor("coa-a","accounting.chart.view","accounting.chart.create","accounting.chart.deactivate");
            var b=Actor("coa-b","accounting.chart.view","accounting.chart.deactivate");
            store.Write((c,tx)=>ChartOfAccountsApi.Create(c,tx,a,new(){["parentCode"]="5",["code"]="599910",["nameAr"]="غير مستخدم",["isPostable"]=true}));
            var stopped=store.Write((c,tx)=>ChartOfAccountsApi.Deactivate(c,tx,a,"599910"));
            Assert.AreEqual(2,stopped["status"]!.GetValue<int>());
            Assert.IsFalse(stopped.B("isActive"));

            store.Write((c,tx)=>ChartOfAccountsApi.Create(c,tx,a,new(){["parentCode"]="5",["code"]="599911",["nameAr"]="مستخدم",["isPostable"]=true}));
            store.Write((c,tx)=>{Store.Save(c,"coa_posted_use",new(){["id"]="coa-use:599911",["code"]="599911"},tx);return true;});
            var pending=store.Write((c,tx)=>ChartOfAccountsApi.Deactivate(c,tx,a,"599911"));
            Assert.IsTrue(pending.B("deactivatePending"));
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>ChartOfAccountsApi.Deactivate(c,tx,a,"599911"))).Status);
            var done=store.Write((c,tx)=>ChartOfAccountsApi.Deactivate(c,tx,b,"599911"));
            Assert.AreEqual(2,done["status"]!.GetValue<int>());
            Assert.IsFalse(done.B("isActive"));
            var again=store.Write((c,tx)=>ChartOfAccountsApi.Reactivate(c,tx,a,"599911"));
            Assert.AreEqual(1,again["status"]!.GetValue<int>());
            Assert.IsTrue(again.B("isActive"));
        },sqlServer);
    }
}
