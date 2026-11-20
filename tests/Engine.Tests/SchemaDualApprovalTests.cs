using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaDualApprovalTests
{
    [TestMethod,DataRow(false),DataRow(true)]
    public void DedicatedPermissionsSeedIdempotentlyAndAreGrantableToAnyRole(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);SchemaAuthCatalog.Ensure(c);
        string role=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_role(id,code,name_ar,created_at) VALUES(@id,'SyntheticDualRole','Synthetic role',datetime('now'))",("@id",role));
        var ids=new List<string>();
        foreach(string code in new[]{"approvals.dual.bank_file","approvals.dual.payment_out","approvals.dual.shared_rider_release"})
        {
            string permission=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM auth_permission WHERE code=@code AND deprecated=0",("@code",code))!;
            Assert.IsNotNull(permission);ids.Add(permission);
            MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_role_permission(id,role_id,permission_id,created_at) VALUES(@id,@r,@p,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@r",role),("@p",permission));
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT INTO auth_role_permission(id,role_id,permission_id,created_at) VALUES(@id,@r,@p,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@r",role),("@p",permission)));
        }
        SchemaAuthCatalog.Ensure(c);
        Assert.AreEqual(3L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM auth_role_permission WHERE role_id=@r",("@r",role)));
        Assert.AreEqual(3L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM auth_role_permission rp JOIN auth_role r ON r.id=rp.role_id JOIN auth_permission p ON p.id=rp.permission_id WHERE r.code='Admin' AND p.code LIKE 'approvals.dual.%'"));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM auth_user"));
        Assert.IsTrue(UserAccess.EditorPermissionsCatalog.Any(p=>p.Code=="approvals.dual.bank_file"));
        Assert.IsFalse(UserAccess.RolesCatalog.Single(r=>r.Code=="Accountant").DefaultPermissions.Contains("approvals.dual.payment_out"));
    }
}
