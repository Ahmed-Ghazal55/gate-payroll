using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class ImportTemplateAccessTests
{
    static ClaimsPrincipal Principal(string user, string role, params string[] perms)
    {
        var body = new JsonObject
        {
            ["username"] = user,
            ["role"] = role,
            ["customPermissions"] = new JsonArray(perms.Select(p => (JsonNode?)p).ToArray())
        };
        return UserAccess.Principal(body);
    }

    [TestMethod]
    public void DownloadTemplateActionFollowsEndpointPermission()
    {
        var payOnly = Principal("syn-pay", "Reviewer", "pay.manage");
        var withImports = Principal("syn-imp", "Reviewer", "pay.manage", "imports.view");
        var ridersView = Principal("syn-rv", "Reviewer", "riders.view");

        Assert.IsFalse(AccessPolicy.Allowed(payOnly, "GET", "/api/imports/templates/operations-ninja"));
        Assert.IsTrue(AccessPolicy.Allowed(withImports, "GET", "/api/imports/templates/operations-ninja"));
        Assert.IsTrue(AccessPolicy.Allowed(ridersView, "GET", "/api/excel/riders/template"));

        var payActions = new JsonArray();
        ImportTemplateAccess.AddIfAllowed(payActions, payOnly, "/api/imports/templates/operations-ninja");
        Assert.AreEqual(0, payActions.Count);

        var importActions = new JsonArray();
        ImportTemplateAccess.AddIfAllowed(importActions, withImports, "/api/imports/templates/operations-ninja");
        Assert.AreEqual(1, importActions.Count);
        Assert.AreEqual(ImportTemplateAccess.DownloadAction, importActions[0]!.ToString());

        var riderActions = new JsonArray();
        ImportTemplateAccess.AddIfAllowed(riderActions, ridersView, "/api/excel/riders/template");
        Assert.AreEqual(ImportTemplateAccess.DownloadAction, riderActions[0]!.ToString());
    }
}
