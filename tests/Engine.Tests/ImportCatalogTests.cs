using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class ImportCatalogTests
{
    static System.Security.Claims.ClaimsPrincipal User(params string[] perms)=>UserAccess.Principal(new JsonObject{["username"]="synthetic-importer",["role"]="Reviewer",
        ["customPermissions"]=new JsonArray(perms.Select(p=>(JsonNode?)JsonValue.Create(p)).ToArray())});

    [TestMethod]
    public void StepsFollowGhazalOrderAndDependOnlyOnEarlierSteps()
    {
        var steps=ImportCatalog.Steps();
        CollectionAssert.AreEqual(steps.Select(s=>s.Order).ToArray(),Enumerable.Range(1,steps.Count).ToArray(),"orders are 1..n with no gaps");
        Assert.AreEqual(steps.Count,steps.Select(s=>s.Key).Distinct().Count());
        // Companies → riders → opening balances → debts → platform invoices/operations.
        int Rank(string g)=>g switch{"setup"=>0,"opening"=>1,_=>2};
        for(int i=1;i<steps.Count;i++)Assert.IsTrue(Rank(steps[i-1].Group)<=Rank(steps[i].Group),$"{steps[i].Key} is out of order");
        Assert.AreEqual("companies",steps[0].Key);Assert.AreEqual("riders",steps[1].Key);
        var seen=new HashSet<string>();
        foreach(var s in steps)
        {
            foreach(var d in s.DependsOn)Assert.IsTrue(seen.Contains(d),$"{s.Key} depends on {d}, which must come earlier");
            var names=s.Columns.Select(c=>c.Header).ToList();
            Assert.AreEqual(names.Count,names.Distinct().Count(),$"{s.Key} has duplicate columns");
            foreach(var r in s.Required)CollectionAssert.Contains(names,r,$"{s.Key}: required column {r} is in the template");
            if(s.Status is "ready" or "template_only"&&s.Upload is "xlsx" or "json-rows")Assert.IsNotNull(s.TemplateUrl,$"{s.Key} offers a template");
            seen.Add(s.Key);
        }
        // Per-app operations come in Ghazal's order: Ninja, Keeta, HungerStation, then Chefz from ERP.
        CollectionAssert.AreEqual(new[]{"ninja_operations","keeta_operations","hungerstation_operations","chefz_operations"},steps.Where(s=>s.Group=="operations").Select(s=>s.Key).ToArray());
    }

    [TestMethod]
    public void CatalogTemplatesDownloadWithTheExactCatalogHeaders()
    {
        foreach(var s in ImportCatalog.Steps().Where(s=>s.TemplateUrl?.StartsWith("/api/imports/templates/",StringComparison.Ordinal)==true))
        {
            string name=s.TemplateUrl!["/api/imports/templates/".Length..];
            var file=(FileContentHttpResult)ImportCatalog.Template(name);
            var rows=Imports.Read(file.FileContents.ToArray(),"template.xlsx")[0].Rows;
            CollectionAssert.AreEqual(s.Columns.Select(c=>c.Header).ToArray(),rows[0].Take(s.Columns.Count).ToArray(),$"{name} header = catalog columns");
            StringAssert.StartsWith(rows[1][0],"مثال","row 2 is a marked example");
        }
        Assert.ThrowsExactly<ApiError>(()=>ImportCatalog.Template("../secret"));
    }

    [TestMethod]
    public void SharedTemplatesUseTheSameColumnSourceAsTheirDownloadEndpoints()
    {
        var steps=ImportCatalog.Steps().ToDictionary(s=>s.Key);
        CollectionAssert.AreEqual(TableExcelApi.Columns("riders").Select(c=>c.Header).ToArray(),steps["riders"].Columns.Select(c=>c.Header).ToArray());
        CollectionAssert.AreEqual(TreasuryEntries.OpeningColumns.Select(c=>c.Header).ToArray(),steps["treasury_opening"].Columns.Select(c=>c.Header).ToArray());
        CollectionAssert.AreEqual(Debts.OpeningTemplateColumns().Select(c=>c.Header).ToArray(),steps["opening_debts"].Columns.Select(c=>c.Header).ToArray());
        Assert.AreEqual(16,steps["opening_debts"].Columns.Count,"opening-debt template keeps its 16 columns");
        Assert.AreEqual(7,steps["bulk_collect"].Columns.Count,"bulk-collect template keeps its 7 columns");
    }

    [TestMethod]
    public void CatalogAndTemplatesNeedImportsViewAndStayReadOnly()
    {
        Assert.IsTrue(AccessPolicy.Allowed(User("imports.view"),"GET","/api/imports/catalog"));
        Assert.IsTrue(AccessPolicy.Allowed(User("imports.view"),"GET","/api/imports/templates/operations-ninja"));
        Assert.IsFalse(AccessPolicy.Allowed(User("advances.request"),"GET","/api/imports/catalog"));
        Assert.IsFalse(AccessPolicy.Allowed(User("imports.view"),"POST","/api/imports/catalog/commit"),"no write path under the catalog");
        var view=ImportCatalog.View();Assert.AreEqual(ImportCatalog.Steps().Count,view.Count);
        var first=view[0]!.AsObject();Assert.AreEqual("companies",first["key"]!.GetValue<string>());
        Assert.IsTrue(first["columns"]!.AsArray().OfType<JsonObject>().Any(c=>c["required"]!.GetValue<bool>()));
    }
}
