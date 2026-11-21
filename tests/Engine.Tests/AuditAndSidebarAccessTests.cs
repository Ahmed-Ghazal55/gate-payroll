using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class AuditAndSidebarAccessTests
{
    static ClaimsPrincipal Principal(string user, string role, params string[] perms) =>
        UserAccess.Principal(new JsonObject
        {
            ["username"] = user,
            ["role"] = role,
            ["customPermissions"] = new JsonArray(perms.Select(p => (JsonNode?)p).ToArray())
        });

    [TestMethod]
    public void AuditEndpointRequiresAuditViewNotUsersManageAlone()
    {
        Assert.IsTrue(UserAccess.PermissionsCatalog.Any(p => p.Code == "audit.view"));
        var manager = Principal("syn-users", "Reviewer", "users.manage");
        var auditor = Principal("syn-audit", "Reviewer", "audit.view");
        Assert.IsFalse(AccessPolicy.Allowed(manager, "GET", "/api/audit"));
        Assert.IsTrue(AccessPolicy.Allowed(auditor, "GET", "/api/audit"));
    }

    [TestMethod]
    public void FormsDeleteIsDeprecatedAndDoesNotOpenCampaignDelete()
    {
        Assert.IsTrue(UserAccess.PermissionsCatalog.First(p => p.Code == "forms.delete").Deprecated);
        var deleter = Principal("syn-del", "Reviewer", "forms.delete", "forms.manage");
        Assert.IsFalse(AccessPolicy.Allowed(deleter, "DELETE", "/api/campaigns/abc"));
        Assert.IsTrue(AccessPolicy.Allowed(UserAccess.Principal(new JsonObject { ["username"] = "admin", ["role"] = "Admin" }), "DELETE", "/api/campaigns/abc"));
    }

    [TestMethod]
    public void BankFileApiStillNeedsExportAndPayrollView()
    {
        Assert.IsFalse(AccessPolicy.Allowed(Principal("a", "Reviewer", "treasury.view"), "POST", "/api/finance/bank-file/preview"));
        Assert.IsFalse(AccessPolicy.Allowed(Principal("b", "Reviewer", "finance.bankfile.export"), "POST", "/api/finance/bank-file/preview"));
        Assert.IsTrue(AccessPolicy.Allowed(
            Principal("c", "Reviewer", "finance.bankfile.export", "payroll.view"),
            "POST", "/api/finance/bank-file/preview"));
    }
}
