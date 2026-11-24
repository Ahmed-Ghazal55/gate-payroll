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
        // TRIAL6/8: approvals.approve_own stays OFF for SystemAdmin unless granted in customPermissions.
        var effective=UserAccess.EffectivePermissions(user);
        Assert.AreEqual(UserAccess.PermissionsCatalog.Length-1,effective.Length);
        Assert.IsFalse(effective.Contains(AccessPolicy.ApproveOwnPermission));
        Assert.IsTrue(effective.Contains("users.manage"));
        Assert.IsTrue(effective.Contains("approval.bypass_maker_checker"));
        UserAccess.SeedSystemAdmin(f.Store);
        Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"audit").Count(a=>a.S("action")=="admin.system_admin_permissions_seeded")));

        var actor=UserAccess.Principal(user);
        // TRIAL8: SystemAdmin without approve_own cannot self-approve (dual or non-dual).
        var request=new JsonObject{["id"]="synthetic-sysadmin-request",["kind"]="generic",["status"]="pending",["requestedBy"]="synthetic-sysadmin",["targetDepartment"]="MGT",["requiredRankLevel"]=1,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>{AccessPolicy.ValidateApproval(actor,request,new(),c:c,tx:tx);return true;})).Status);
        f.Store.Write((c,tx)=>
        {
            var dual=new JsonObject{["id"]="synthetic-sysadmin-dual",["kind"]="payment_out",["status"]="pending",["requestedBy"]="synthetic-sysadmin",["targetDepartment"]="MGT",["requiredRankLevel"]=1,["dualPermission"]=ApprovalsArea.DualPaymentOut,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>AccessPolicy.ValidateApproval(actor,dual,new(),"approvals.decide",c,tx,"approval.decide",dual,ApprovalsArea.DualPaymentOut)).Status);
            return true;
        });
        f.Store.Write((c,tx)=>
        {
            var u=Store.Get(c,"synthetic-sysadmin","user",tx)!;
            u["customPermissions"]=new JsonArray(AccessPolicy.ApproveOwnPermission);
            Store.Save(c,"user",u,tx);
            return true;
        });
        var withOwn=UserAccess.Principal(f.Store.Read(c=>Store.Get(c,"synthetic-sysadmin","user")!)!);
        var ok=new JsonObject{["id"]="synthetic-sysadmin-own",["kind"]="generic",["status"]="pending",["requestedBy"]="synthetic-sysadmin",["targetDepartment"]="MGT",["requiredRankLevel"]=1,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        f.Store.Write((c,tx)=>{AccessPolicy.ValidateApproval(withOwn,ok,new(),c:c,tx:tx,record:ok);Assert.IsTrue(ok.B("selfApproved")||ok.B("selfApprovedException"));return true;});
        Assert.IsTrue(f.Store.Read(c=>Store.List(c,"audit").Any(a=>a.S("recordId")=="synthetic-sysadmin-own"&&a.S("action")=="approval.approve_own")));
        Assert.IsTrue(f.Store.Read(c=>AccessPolicy.BypassForView(c,null,actor)));
    }
}
