using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class PaymentAdvancesTests
{
    const string Rider="2000000601";

    [TestMethod][DataRow(false)][DataRow(true)]
    public async Task HsTopupUsesLiveAccountAndDualKeepsLegacyAdvanceRoutes(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        string employee=Guid.NewGuid().ToString("D"),platform=Guid.NewGuid().ToString("D");
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"rider",new(){["id"]=Rider,["iqama"]=Rider,["name"]="Synthetic Advance Rider",["sponsorId"]=f.Company,["erpId"]=employee},tx);
            OrgDirectory.AddSupervision(c,tx,f.Maker,new(){["supervisorUserId"]="wp-maker",["riderIqama"]=Rider,["validFrom"]="2026-01-01"});
            return true;
        });
        var created=f.Store.Write((c,tx)=>PaymentAdvances.Create(c,tx,f.Maker,new()
        {
            ["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",
            ["reference"]="SYN-HS",["riderIqama"]=Rider,["erpEmployeeId"]=employee,["erpPlatformAccountId"]=platform,["advanceType"]="hs_topup",["amount"]=120m
        }));
        Assert.AreEqual("hs_topup",created.S("type"));Assert.AreEqual(120m,created.N("amount"));
        Assert.AreEqual(PaymentAdvances.HsTopupAccount,f.Store.Read(c=>MoneySchemaFixture.Scalar(c,null,"SELECT a.code FROM pay_document_line l JOIN fin_account a ON a.id=l.account_id WHERE l.document_id=@id AND l.active=1",("@id",created.S("id")))!.ToString()));
        Assert.AreEqual(PaymentAdvances.HsTopupAccount,PipelineSettings.HsWalletAccount());
        Assert.AreEqual(1L,f.Scalar("SELECT COUNT(*) FROM pay_advance_request"));
        var preview=PaymentAdvances.MonthClosePreview(new(){["C"]=100,["A"]=-40,["M"]=-80});
        Assert.AreEqual(PaymentAdvances.HsTopupAccount,preview.S("currentTopupAccount"));Assert.AreEqual(false,preview.B("postingAllowed"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(preview.S("blocker")));
        var builder=WebApplication.CreateSlimBuilder();
        await using var app=builder.Build();var api=app.MapGroup("/api");
        AdvanceRequests.Map(api,f.Store);PaymentsApi.Map(api,f.Store);
        var routes=((IEndpointRouteBuilder)app).DataSources.SelectMany(s=>s.Endpoints).OfType<RouteEndpoint>().Select(e=>e.RoutePattern.RawText!).ToArray();
        Assert.IsTrue(routes.Any(r=>r.Contains("advance-requests",StringComparison.Ordinal)));
        Assert.IsTrue(routes.Any(r=>r.Contains("payments/advances",StringComparison.Ordinal)));
    }
}
