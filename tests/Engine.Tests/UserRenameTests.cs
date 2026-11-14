using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>Admin login rename: old names stay reserved and still count as the same person for maker-checker.</summary>
[TestClass]
public sealed class UserRenameTests
{
    static JsonObject User(string id,string name,string role="FinancialAccountant")=>UserAccess.Create(name,"synthetic-password-0001",role,id);
    static JsonObject Rename(PayFixture f,string id,string name,string reason="synthetic rename",System.Security.Claims.ClaimsPrincipal? actor=null)
        =>f.Store.Write((c,tx)=>UserAccess.Rename(c,tx,actor??PayFixture.Maker,id,new(){["username"]=name,["reason"]=reason}));

    [TestMethod]
    public void AdminRenamesLoginKeepsIdReservesOldNameAndEndsSessions()
    {
        using var f=new PayFixture();
        var before=User("synthetic-u1","synthetic-old");
        f.Store.Write((c,tx)=>{Store.Save(c,"user",before,tx);return true;});
        var view=Rename(f,"synthetic-u1","synthetic-new");
        Assert.AreEqual("synthetic-new",view.S("username"));
        CollectionAssert.AreEqual(new[]{"synthetic-old"},view["formerUsernames"]!.AsArray().Select(n=>n!.ToString()).ToArray());
        var stored=f.Store.Read(c=>Store.Get(c,"synthetic-u1","user"))!;
        Assert.AreNotEqual(before.S("securityVersion"),stored.S("securityVersion"),"old sessions end");
        Assert.AreEqual(before.S("hash"),stored.S("hash"),"password unchanged");
        Assert.IsNotNull(f.Store.Read(c=>UserAccess.Find(c,"synthetic-new")));
        Assert.IsNull(f.Store.Read(c=>UserAccess.Find(c,"synthetic-old")),"old name no longer logs in");
        var audit=f.Store.Read(c=>Store.List(c,"audit").Single(a=>a.S("action")=="admin.username_changed"));
        Assert.AreEqual("synthetic-old",audit.S("from"));Assert.AreEqual("synthetic-new",audit.S("to"));Assert.AreEqual("synthetic rename",audit.S("reason"));
    }

    [TestMethod]
    public void OldNameIsReservedAndNonAdminCannotRename()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{Store.Save(c,"user",User("synthetic-u1","synthetic-old"),tx);Store.Save(c,"user",User("synthetic-u2","synthetic-other"),tx);return true;});
        Rename(f,"synthetic-u1","synthetic-new");
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>Rename(f,"synthetic-u2","SYNTHETIC-OLD")).Status,"former name reserved (case-insensitive)");
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>Rename(f,"synthetic-u2","synthetic-new")).Status,"current name taken");
        Assert.IsTrue(f.Store.Read(c=>UserAccess.NameTaken(c,"synthetic-old")),"create refuses a former name too");
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>Rename(f,"synthetic-u2","bad name")).Status,"no spaces");
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>Rename(f,"synthetic-u2","synthetic-x","")).Status,"reason required");
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>Rename(f,"synthetic-u2","synthetic-x",actor:PayFixture.Checker)).Status,"admin only");
    }

    [TestMethod]
    public void RenamedUserCannotApproveWorkDoneUnderOldName()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{Store.Save(c,"user",User("synthetic-u1","synthetic-old"),tx);return true;});
        Rename(f,"synthetic-u1","synthetic-new");
        var renamed=UserAccess.Principal(f.Store.Read(c=>Store.Get(c,"synthetic-u1","user"))!);
        Assert.IsFalse(AccessPolicy.Independent(renamed,"synthetic-old","synthetic.op"),"old name = same person");
        Assert.IsFalse(AccessPolicy.Independent(renamed,"synthetic-new","synthetic.op"));
        Assert.IsTrue(AccessPolicy.Independent(renamed,"synthetic-someone-else","synthetic.op"));
        Assert.IsFalse(AccessPolicy.IndependentForView(renamed,"SYNTHETIC-OLD",false));
        Assert.IsTrue(AccessPolicy.IndependentForView(renamed,"synthetic-old",true),"an active dated grant still lifts the rule");
    }

    [TestMethod]
    public void BypassGrantFollowsTheRenamedUser()
    {
        using var f=new PayFixture();
        string today=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd");
        f.Store.Write((c,tx)=>{Store.Save(c,"user",User("synthetic-u1","synthetic-old"),tx);AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="synthetic-old",["from"]=today,["to"]=today,["reason"]="synthetic grant"});return true;});
        Rename(f,"synthetic-u1","synthetic-new");
        Assert.IsNotNull(f.Store.Read(c=>AccessPolicy.ActiveBypassGrant(c,null,"synthetic-new")));
        Assert.IsNull(f.Store.Read(c=>AccessPolicy.ActiveBypassGrant(c,null,"synthetic-old")));
    }

    [TestMethod]
    public void StalePermissionCodeIsHiddenSoTheDialogCanSave()
    {
        var u=User("synthetic-u3","synthetic-stale");
        u["customPermissions"]=new JsonArray("riders.sync_baseline","pay.view");u["deniedPermissions"]=new JsonArray("riders.sync_baseline");
        var view=UserAccess.Public(u);
        CollectionAssert.AreEqual(new[]{"pay.view"},view["customPermissions"]!.AsArray().Select(n=>n!.ToString()).ToArray());
        Assert.AreEqual(0,view["deniedPermissions"]!.AsArray().Count);
        UserAccess.ValidatePermissionList(view["customPermissions"]!.AsArray());
    }
}
