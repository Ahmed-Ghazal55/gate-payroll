using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class ApiConventionsTests
{
    static System.Security.Claims.ClaimsPrincipal User(params string[] permissions) => UserAccess.Principal(new JsonObject
    { ["username"]="synthetic-policy-user", ["role"]="Reviewer", ["customPermissions"]=new JsonArray(permissions.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()) });
    [TestMethod]
    public void DeletedShadowBranchesKeepTheirEffectivePermissions()
    {
        var user=User("treasury.opening","riders.view","forms.view");
        Assert.IsTrue(AccessPolicy.Allowed(user,"POST","/api/debts/reconcile-opening/preview"));
        Assert.IsTrue(AccessPolicy.Allowed(user,"GET","/api/excel/riders/template"));
        Assert.IsTrue(AccessPolicy.Allowed(user,"GET","/api/excel/choices/template"));
        Assert.IsFalse(AccessPolicy.Allowed(user,"GET","/api/excel/unknown/template"));
    }
    [TestMethod]
    public void CampaignEditsAndDeletesMatchAdministratorHandlers()
    {
        var user=User("forms.manage","forms.delete");
        foreach(var (method,path) in new[]{("PUT","/api/campaigns/example"),("DELETE","/api/campaigns/example"),("DELETE","/api/submissions/example"),("POST","/api/submissions/bulk-delete"),("GET","/api/forms/builder/example/delete-preview")})
        { Assert.IsFalse(AccessPolicy.Allowed(user,method,path),path); Assert.IsTrue(UserAccess.Allowed("Admin",method,path),path); Assert.IsTrue(UserAccess.Allowed("SuperAdmin",method,path),path); }
        Assert.IsTrue(AccessPolicy.Allowed(user,"POST","/api/campaigns"));
    }
}
