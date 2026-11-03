using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class ApiPaginationTests
{
    static IQueryCollection Query(params (string Key,string Value)[] values)=>new QueryCollection(values.ToDictionary(x=>x.Key,x=>(StringValues)x.Value));
    [TestMethod]
    public void LegacyListsIgnoreNewOptionsWithoutPage()
    {
        var rows=new[]{new JsonObject{["name"]="Zeta"},new JsonObject{["name"]="Alpha"}};
        Assert.AreEqual(JsonSerializer.Serialize(rows),JsonSerializer.Serialize(ApiPagination.Apply(rows,Query(("q","missing"),("sort","name"),("pageSize","invalid")))));
    }
    [TestMethod]
    public void FilteringSortingAndTotalPrecedeThePageSlice()
    {
        var rows=Enumerable.Range(1,205).Select(i=>new JsonObject{["name"]="Synthetic match",["number"]=i}).ToList();
        var result=JsonSerializer.SerializeToNode(ApiPagination.Apply(rows,Query(("page","1"),("pageSize","999"),("q","match"),("sort","-number"))))!;
        Assert.AreEqual(205,result["total"]!.GetValue<int>());Assert.AreEqual(200,result["pageSize"]!.GetValue<int>());
        Assert.AreEqual(205,result["items"]![0]!["number"]!.GetValue<int>());
        Assert.AreEqual(200,result["items"]!.AsArray().Count);
        result=JsonSerializer.SerializeToNode(ApiPagination.Apply(rows,Query(("page",int.MaxValue.ToString()))))!;
        Assert.AreEqual(0,result["items"]!.AsArray().Count);
        Assert.ThrowsExactly<ApiError>(()=>ApiPagination.Apply(rows,Query(("page","0"))));
        Assert.ThrowsExactly<ApiError>(()=>ApiPagination.Apply(rows,Query(("page","1"),("pageSize","0"))));
    }
}
