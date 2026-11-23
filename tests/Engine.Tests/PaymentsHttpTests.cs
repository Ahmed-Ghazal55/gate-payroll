using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class PaymentsHttpTests
{
    sealed class BodyDetection:IHttpRequestBodyDetectionFeature{public bool CanHaveBody=>true;}
    [TestMethod][DataRow(false)][DataRow(true)]
    public async Task RealEndpointDelegatesEnforcePermissionScopeDualAndContracts(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions{ContentRootPath=f.Store.DirectoryPath,EnvironmentName="Development"});
        await using var app=builder.Build();var api=app.MapGroup("/api");
        api.AddEndpointFilter(async (context,next)=>AccessPolicy.Allowed(context.HttpContext.User,context.HttpContext.Request.Method,context.HttpContext.Request.Path)?await next(context):Results.StatusCode(403));
        PaymentsApi.Map(api,f.Store);
        async Task<(int Status,JsonObject Body)> Request(string method,string path,ClaimsPrincipal actor,JsonObject? body=null)
        {
            var matches=((IEndpointRouteBuilder)app).DataSources.SelectMany(s=>s.Endpoints).OfType<RouteEndpoint>().Where(e=>e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method)).Select(e=>{
                var values=new RouteValueDictionary();bool match=new TemplateMatcher(TemplateParser.Parse(e.RoutePattern.RawText!),new()).TryMatch(path,values);return(Endpoint:e,Values:values,Match:match);
            }).Where(x=>x.Match).ToList();Assert.AreEqual(1,matches.Count,"Exactly one real endpoint must match.");
            using var scope=app.Services.CreateScope();var ctx=new DefaultHttpContext{RequestServices=scope.ServiceProvider,User=actor};ctx.Request.Method=method;ctx.Request.Path=path;ctx.Request.RouteValues=matches[0].Values;ctx.SetEndpoint(matches[0].Endpoint);
            var bytes=Encoding.UTF8.GetBytes(body?.ToJsonString()??"{}");ctx.Request.Body=new MemoryStream(bytes);ctx.Request.ContentLength=bytes.Length;ctx.Request.ContentType="application/json";ctx.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection());ctx.Response.Body=new MemoryStream();
            try{await matches[0].Endpoint.RequestDelegate!(ctx);}catch(ApiError error){ctx.Response.StatusCode=error.Status;}
            ctx.Response.Body.Position=0;string text=await new StreamReader(ctx.Response.Body).ReadToEndAsync();return(ctx.Response.StatusCode,text==""?new():JsonNode.Parse(text)!.AsObject());
        }
        var nobody=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"unprivileged")},"test"));Assert.AreEqual(403,(await Request("POST","/api/payments/quick",nobody,f.Input())).Status);
        var created=await Request("POST","/api/payments/quick",f.Maker,f.Input());Assert.AreEqual(200,created.Status);Assert.AreEqual("quick",created.Body.S("type"));Assert.IsTrue(created.Body["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="submit"));
        string path="/api/payments/documents/"+created.Body.S("id");
        var outsider=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"outsider"),new Claim("permission","payments.view")},"test"));Assert.AreEqual(404,(await Request("GET",path,outsider)).Status);
        Assert.AreEqual(200,(await Request("POST",path+"/submit",f.Maker)).Status);Assert.AreEqual(200,(await Request("POST",path+"/approve",f.Checker,new())).Status);
        var reserved=await Request("POST",path+"/payment-requests",f.Maker,new(){["idempotencyKey"]="http-pay",["amount"]=40,["bankAccountCode"]="111101",["paidOn"]="2026-11-20",["reference"]="SYN-HTTP"});Assert.AreEqual(200,reserved.Status);
        string pay="/api/payments/vouchers/"+reserved.Body.S("id")+"/pay";Assert.AreEqual(403,(await Request("POST",pay,f.Maker)).Status);
        f.Dual(reserved.Body);var paid=await Request("POST",pay,f.Maker);Assert.AreEqual(200,paid.Status);Assert.AreEqual(2m,paid.Body.N("status"));Assert.AreEqual(40m,Convert.ToDecimal(f.Scalar("SELECT amount_paid FROM pay_payable")));
    }
}
