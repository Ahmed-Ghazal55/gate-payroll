using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SystemAdminMigrationTests
{
    [TestMethod]
    public void SeedGrantsFullCatalogIdempotentlyAndSelfApprovalIsAuditedInReport()
    {
        using var f=new HungerStationInvoiceTests.Fixture();
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"user",new(){["id"]="synthetic-sysadmin",["username"]="synthetic-sysadmin",["role"]="SystemAdmin",["department"]="MGT"},tx);
            Store.Save(c,UserAccess.RolePermissionsKind,new(){["id"]="SystemAdmin",["permissions"]=new JsonArray(),["updatedBy"]="legacy"},tx);
            return true;
        });
        Assert.AreEqual(0,f.Store.Read(c=>Store.Get(c,"SystemAdmin",UserAccess.RolePermissionsKind)!["permissions"]!.AsArray().Count));
        UserAccess.SeedSystemAdmin(f.Store);
        var template=f.Store.Read(c=>Store.Get(c,"SystemAdmin",UserAccess.RolePermissionsKind))!;
        Assert.AreEqual(UserAccess.PermissionsCatalog.Length,template["permissions"]!.AsArray().Count);
        Assert.IsNotNull(f.Store.Read(c=>Store.Get(c,UserAccess.SystemAdminMigrationMarker,"setting")));
        var user=f.Store.Read(c=>Store.Get(c,"synthetic-sysadmin","user"))!;
        Assert.AreEqual(UserAccess.PermissionsCatalog.Length,UserAccess.EffectivePermissions(user).Length);
        Assert.IsTrue(UserAccess.EffectivePermissions(user).Contains("users.manage"));
        Assert.IsTrue(UserAccess.EffectivePermissions(user).Contains("approval.bypass_maker_checker"));
        UserAccess.SeedSystemAdmin(f.Store);
        Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"audit").Count(a=>a.S("action")=="admin.system_admin_permissions_seeded")));

        var actor=UserAccess.Principal(user);
        var request=new JsonObject{["id"]="synthetic-sysadmin-request",["requestedBy"]="synthetic-sysadmin",["targetDepartment"]="MGT",["requiredRankLevel"]=1};
        f.Store.Write((c,tx)=>{AccessPolicy.ValidateApproval(actor,request,new(),c:c,tx:tx);Store.Save(c,"approval_request",request,tx);return true;});
        Assert.IsTrue(request.B("selfApprovedException"));
        var report=f.Store.Read(c=>Store.List(c,"audit").Where(a=>a.B("selfApprovedException")||a.S("action") is "approval.self_exception" or "approval.bypass_maker_checker").ToList());
        Assert.IsTrue(report.Any(a=>a.S("recordId")=="synthetic-sysadmin-request"&&a.S("badge")=="system_admin"&&a.S("reason")=="system-admin-default"));
        Assert.IsTrue(f.Store.Read(c=>AccessPolicy.BypassForView(c,null,actor)));
    }
}
