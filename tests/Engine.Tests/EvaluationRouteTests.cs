using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class EvaluationRouteTests
{
    [TestMethod]
    public async Task MappedHandlersEnforceConfidentialityAndExportPermissionWithoutListeningSocket()
    {
        using var f = new PayFixture();
        var admin = PayFixture.Maker;
        var round = f.Store.Write((c, tx) =>
        {
            EvaluationSurvey.ImportStaff(c, tx, admin, new() { ["staff"] = new JsonArray(new JsonObject { ["name"] = "Synthetic route person", ["cities"] = new JsonArray("Synthetic city") }) });
            var draft = EvaluationSurvey.CreateRound(c, tx, admin, new() { ["title"] = "Synthetic route round" });
            return EvaluationSurvey.Transition(c, tx, admin, draft.S("id"), "open");
        });
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", ContentRootPath = f.DirectoryPath });
        await using var app = builder.Build();
        EvaluationSurvey.Map(app, app.MapGroup("/api"), f.Store);
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.HasCount(29, endpoints);
        async Task<DefaultHttpContext> Invoke(string pattern, ClaimsPrincipal user)
        {
            var endpoint = endpoints.Single(e => e.RoutePattern.RawText == pattern && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));
            var ctx = new DefaultHttpContext { RequestServices = app.Services, User = user };
            ctx.Request.Method = "GET"; ctx.Request.Scheme = "https"; ctx.Request.Host = new HostString("synthetic.invalid");
            ctx.Request.RouteValues["id"] = round.S("id"); ctx.Request.RouteValues["token"] = round.S("sharingToken");
            ctx.Response.Body = new MemoryStream(); ctx.SetEndpoint(endpoint);
            await endpoint.RequestDelegate!(ctx); return ctx;
        }
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var schema = await Invoke("/api/public/evaluations/{token}", anonymous);
        schema.Response.Body.Position = 0;
        var publicJson = JsonNode.Parse(await new StreamReader(schema.Response.Body).ReadToEndAsync())!;
        Assert.AreEqual("evaluation-v1", publicJson.S("schema"));
        Assert.IsNull(publicJson["sharingToken"]);
        var statistics = await Invoke("/api/forms/evaluations/rounds/{id}/statistics", admin);
        Assert.AreEqual(200, statistics.Response.StatusCode);
        await Assert.ThrowsAsync<ApiError>(() => Invoke("/api/forms/evaluations/rounds/{id}/statistics", anonymous));
        var reader = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "synthetic-reader"), new Claim("permission", EvaluationSurvey.ViewPermission) }, "test"));
        await Assert.ThrowsAsync<ApiError>(() => Invoke("/api/forms/evaluations/rounds/{id}/export", reader));
        var export = await Invoke("/api/forms/evaluations/rounds/{id}/export", admin);
        Assert.AreEqual(ExcelFiles.Mime, export.Response.ContentType);
        Assert.IsTrue(export.Response.Body.Length > 0);
        f.Store.Write((c, tx) => EvaluationSurvey.Transition(c, tx, admin, round.S("id"), "paused"));
        await Assert.ThrowsAsync<ApiError>(() => Invoke("/api/public/evaluations/{token}", anonymous));
    }
}
