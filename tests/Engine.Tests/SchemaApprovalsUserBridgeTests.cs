using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaApprovalsUserBridgeTests
{
    static void WithStore(Action<Store> body)
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-auth-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try{body(new Store(dir));}
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    [TestMethod]
    public void EnsureAuthUserId_BridgesExistingUserRole_AndRejectsUnknown()
    {
        WithStore(store=>
        {
            store.Write((c,tx)=>
            {
                Store.Save(c,"user",new(){["id"]=SchemaSql.GuidText(SchemaSql.NewId()),["username"]="acct1",["role"]="Accountant",["status"]="active",["department"]="FIN",["rankLevel"]=3},tx);
                return true;
            });
            string bridged=store.Write((c,tx)=>SchemaApprovals.EnsureAuthUserId(c,tx,"acct1"));
            Assert.AreNotEqual("",bridged);
            Assert.AreEqual("Accountant",store.Read(c=>Convert.ToString(SchemaSql.Scalar(c,null,$"SELECT r.code FROM {SchemaSql.Q(c,"auth","user")} u JOIN {SchemaSql.Q(c,"auth","role")} r ON r.id=u.role_id WHERE u.username=@u",("@u","acct1"))!)));
            Assert.AreEqual(bridged,store.Write((c,tx)=>SchemaApprovals.EnsureAuthUserId(c,tx,"acct1")));
            Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>SchemaApprovals.EnsureAuthUserId(c,tx,"nobody-here"))).Status);
            Assert.AreEqual(0L,store.Read(c=>Convert.ToInt64(SchemaSql.Scalar(c,null,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"auth","user")} u JOIN {SchemaSql.Q(c,"auth","role")} r ON r.id=u.role_id WHERE r.code='Admin' AND u.username=@u",("@u","nobody-here"))!)));
        });
    }
}
