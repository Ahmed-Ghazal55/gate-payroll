using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

[TestClass]
public sealed class KeetaViewsCaptureTests
{
    static ClaimsPrincipal Actor()
    {
        var id=new ClaimsIdentity("synthetic");
        id.AddClaim(new Claim(ClaimTypes.Name,MoneySchemaFixture.Id()));
        id.AddClaim(new Claim(ClaimTypes.Role,"Admin"));
        id.AddClaim(new Claim("permission","pay.manage"));
        id.AddClaim(new Claim("permission","pay.view"));
        id.AddClaim(new Claim("permission","pay.approve"));
        return new ClaimsPrincipal(id);
    }

    static void EnsureRecords(System.Data.Common.DbConnection c)
    {
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,null,"IF OBJECT_ID(N'dbo.records',N'U') IS NULL CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL);");
        else
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HwmAdvancesPerView_NeverGoesBackwards(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);
        EnsureRecords(c);SchemaKeetaHwm.Ensure(c);
        var actor=Actor();
        using var tx=c.BeginTransaction();
        string view="keeta.vw_keeta_eligibility_v1";
        var first=KeetaViewsCapture.Advance(c,tx,actor,new JsonObject{["viewName"]=view,["revision"]=5,["snapshotId"]=MoneySchemaFixture.Id()});
        Assert.AreEqual(5m,first.N("lastRevision"));
        Assert.ThrowsExactly<ApiError>(()=>KeetaViewsCapture.Advance(c,tx,actor,new JsonObject{["viewName"]=view,["revision"]=4}));
        var second=KeetaViewsCapture.Advance(c,tx,actor,new JsonObject{["viewName"]=view,["revision"]=7});
        Assert.AreEqual(7m,second.N("lastRevision"));
        var status=KeetaViewsCapture.Status(c,tx,actor,view);
        Assert.AreEqual(1,status["views"]!.AsArray().Count);
        Assert.AreEqual(7m,status["views"]![0]!.N("lastRevision"));
        tx.Commit();
    }

    [TestMethod]
    public void NotValidEligibility_IsNeedsReview_NotAutoIneligible()
    {
        var row=KeetaViewsCapture.EligibilityCaptureRow(new JsonObject
        {
            ["id"]=MoneySchemaFixture.Id(),["riderProfileId"]=MoneySchemaFixture.Id(),["expectedStatus"]="NotValid"
        });
        Assert.IsTrue(row.B("needsReview"));
        Assert.IsTrue(row.B("eligible"));
        Assert.AreEqual("NotValid",row.S("expectedStatus"));
        Assert.IsTrue(row.S("reason").Contains("needs review",StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Capture_NotValid_DoesNotSetKeetaIneligible(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);
        EnsureRecords(c);
        SchemaSalPayRun.Ensure(c);SchemaOperationCapture.Ensure(c);SchemaSalInvoice.Ensure(c);SchemaSalDistribution.Ensure(c);
        string company=MoneySchemaFixture.Company(c);
        var actor=Actor();
        using var tx=c.BeginTransaction();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) SELECT @id,@c,'2026-10-01',1,datetime('now') WHERE NOT EXISTS(SELECT 1 FROM fin_accounting_period WHERE company_id=@c AND period_month='2026-10-01')",
            ("@id",MoneySchemaFixture.Id()),("@c",company));
        string rider=MoneySchemaFixture.Id(),account=MoneySchemaFixture.Id(),run=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month) VALUES(@id,@c,'keeta','2026-10-01')",
            ("@id",run),("@c",company));
        var facts=new FakeOpsLiveFactsReader();
        foreach(string kind in OpsSnapshotCapture.UsedFields.Keys)facts.Rows[kind]=[];
        facts.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=account,["gross"]=100,["deductions"]=0,["orders"]=1}];
        facts.Rows["platform_account"]=[new(){["id"]=account,["externalAccountId"]="A1",["code"]="A1"}];
        facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=rider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31"}];
        facts.Rows["scheme"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["schemeCode"]="salary",["source"]="pay_settings"}];
        facts.Rows["eligibility"]=[KeetaViewsCapture.EligibilityCaptureRow(new JsonObject{["id"]=MoneySchemaFixture.Id(),["riderProfileId"]=rider,["expectedStatus"]="NotValid"})];
        var reasons=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var kv in facts.Rows)if(kv.Value.Count==0)reasons[kv.Key]="none";
        OpsSnapshotCapture.Capture(c,tx,actor,run,facts,reasons);
        OpsSnapshotCapture.Freeze(c,tx,actor,run);
        var calc=SalPayCalc.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject{["erpRiderId"]=rider,["gross"]=100,["commission"]=0,["schemeType"]="salary"})
        });
        Assert.IsFalse(calc["riders"]![0]!.B("keetaIneligible"));
        Assert.IsTrue(calc["riders"]![0]!.B("eligibilityNeedsReview"));
        tx.Commit();
    }
}
