using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaAuthCatalogTests
{
    static SqliteConnection Open()
    {
        var c=new SqliteConnection("Data Source=:memory:");c.Open();
        using(var fk=c.CreateCommand()){fk.CommandText="PRAGMA foreign_keys=ON;";fk.ExecuteNonQuery();}
        SchemaRef.Ensure(c);SchemaAuthCatalog.Ensure(c);
        return c;
    }

    [TestMethod]
    public void EnsureTwiceSeedsPermissionsAndRolesWithoutBypass()
    {
        using var c=Open();
        SchemaAuthCatalog.Ensure(c);
        Assert.IsTrue(Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM auth_permission")!)>=UserAccess.PermissionsCatalog.Length);
        Assert.IsTrue(Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM auth_role")!)>=UserAccess.RolesCatalog.Length);
        Assert.IsTrue(Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM auth_role_permission")!)>0);
        Assert.AreEqual(0L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM sqlite_master WHERE name LIKE '%bypass%'")!));
        Assert.AreEqual(0L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM auth_user")!));
    }

    [TestMethod]
    public void UniqueUsernameAndFkAndEffectChecks()
    {
        using var c=Open();
        string roleId=(string)SchemaSql.Scalar(c,null,"SELECT id FROM auth_role WHERE code='Admin'")!;
        string userId=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,null,"INSERT INTO auth_user(id,username,role_id,security_version,status,created_at) VALUES(@id,'synthetic-op',@role,1,1,datetime('now'))",
            ("@id",userId),("@role",roleId));
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO auth_user(id,username,role_id,security_version,status,created_at) VALUES(@id,'synthetic-op',@role,1,1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@role",roleId)));
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO auth_user(id,username,role_id,security_version,status,created_at) VALUES(@id,'other',@role,1,1,datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@role",SchemaSql.GuidText(SchemaSql.NewId()))));
        string permId=(string)SchemaSql.Scalar(c,null,"SELECT id FROM auth_permission LIMIT 1")!;
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,
            "INSERT INTO auth_user_permission(id,user_id,permission_id,effect,created_at) VALUES(@id,@u,@p,'allow',datetime('now'))",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@u",userId),("@p",permId)));
    }

    [TestMethod]
    public void StoreStartupSeedsAuthCatalog()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-auth-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            Assert.IsTrue(store.DataPreservationVerified);
            store.Read(c=>{
                Assert.IsTrue(Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM auth_permission")!)>=10);
                Assert.IsTrue(Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM auth_role WHERE code='Admin'")!)==1);
                return true;
            });
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
