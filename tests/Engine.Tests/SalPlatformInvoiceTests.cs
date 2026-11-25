using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

[TestClass]
public sealed class SalPlatformInvoiceTests
{
    // Money-schema audit columns are uniqueidentifier on SQL Server — actor name must be a GUID.
    static ClaimsPrincipal Actor(string? name=null)
    {
        var id=new ClaimsIdentity("synthetic");
        id.AddClaim(new Claim(ClaimTypes.Name,name??MoneySchemaFixture.Id()));
        id.AddClaim(new Claim(ClaimTypes.Role,"Admin"));
        id.AddClaim(new Claim("permission","pay.manage"));
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

    static void SeedOwnership(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,string company,string account,string from="2026-10-01")
        =>PlatformAccountOwnership.RecordOwnership(c,tx,account,company,from,null,PlatformAccountOwnership.SourceManual,"synthetic-maker");

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Approve_FeedsDistribution_PartnerTotalsStayOnCompany(bool sqlServer)
    {
        // Exact SAR: line gross 1100 − 100 = net 1000; partner VAT 150 + penalty 2000 on company → header 3250 / 2250 / 1000.
        using var c=MoneySchemaFixture.Payments(sqlServer);
        EnsureRecords(c);
        SchemaSalInvoice.Ensure(c);SchemaSalDistribution.Ensure(c);SchemaOpsPlatformAccount.Ensure(c);
        string company=MoneySchemaFixture.Company(c),account=MoneySchemaFixture.Id(),rider=MoneySchemaFixture.Id();
        var actor=Actor();
        using var tx=c.BeginTransaction();
        SeedOwnership(c,tx,company,account);
        var uploaded=SalPlatformInvoice.Upload(c,tx,actor,new JsonObject
        {
            ["app"]="keeta",["companyId"]=company,["cityCode"]="RUH",
            ["periodFrom"]="2026-10-01",["periodTo"]="2026-10-31",
            ["lines"]=new JsonArray(new JsonObject
            {
                ["erpPlatformAccountId"]=account,["accountKey"]="K-1",["gross"]=1100,["deductions"]=100,["net"]=1000,["orders"]=10
            }),
            ["partnerTotals"]=new JsonObject{["vat"]=150,["partnerPenalty"]=2000}
        });
        Assert.AreEqual(3250m,uploaded.N("gross"));
        Assert.AreEqual(2250m,uploaded.N("deductions"));
        Assert.AreEqual(1000m,uploaded.N("net"));
        Assert.AreEqual(150m,uploaded.N("partnerVat"));
        Assert.AreEqual(2000m,uploaded.N("partnerPenalty"));
        var approved=SalPlatformInvoice.Approve(c,tx,Actor(),uploaded.S("id"),new JsonObject
        {
            ["workers"]=new JsonArray(new JsonObject{["erpPlatformAccountId"]=account,["erpRiderId"]=rider,["orders"]=10,["fromDate"]="2026-10-01",["toDate"]="2026-10-31"})
        });
        Assert.AreEqual(2m,approved.N("status"));
        Assert.AreEqual(1,approved["distribution"]!.AsArray().Count);
        Assert.AreEqual(1000m,approved["distribution"]![0]!.N("shareAmount"));
        Assert.AreEqual(rider,approved["distribution"]![0]!.S("erpRiderId"));
        long companyItems=Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,
            "SELECT COUNT(*) FROM sal_platform_invoice_item i JOIN sal_platform_invoice_line l ON l.id=i.line_id WHERE l.account_key='partner_total' AND i.item_code IN ('partner_vat','partner_penalty') AND i.platform_deducted=1"));
        Assert.AreEqual(2L,companyItems);
        long companyBearers=Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,
            "SELECT COUNT(*) FROM sal_item_bearer b JOIN sal_platform_invoice_item i ON i.id=b.item_id WHERE b.bearer='company' AND i.item_code IN ('partner_vat','partner_penalty')"));
        Assert.AreEqual(2L,companyBearers);
        var rows=SalPlatformInvoice.ApprovedKeetaRows(c,tx,company,"2026-10-01").ToList();
        Assert.AreEqual(1,rows.Count);
        Assert.AreEqual(1100m,rows[0].N("gross"));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Upload_WrongOwnerBlocked(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);
        EnsureRecords(c);
        SchemaSalInvoice.Ensure(c);SchemaOpsPlatformAccount.Ensure(c);
        string company=MoneySchemaFixture.Company(c),other=MoneySchemaFixture.Company(c),account=MoneySchemaFixture.Id();
        using var tx=c.BeginTransaction();
        SeedOwnership(c,tx,other,account);
        var err=Assert.ThrowsExactly<ApiError>(()=>SalPlatformInvoice.Upload(c,tx,Actor(),new JsonObject
        {
            ["app"]="keeta",["companyId"]=company,["cityCode"]="RUH",
            ["periodFrom"]="2026-10-01",["periodTo"]="2026-10-31",
            ["lines"]=new JsonArray(new JsonObject{["erpPlatformAccountId"]=account,["gross"]=100,["deductions"]=0,["net"]=100,["orders"]=1})
        }));
        Assert.AreEqual(409,err.Status);
        tx.Rollback();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Upload_RejectsNinja_LiveOpsOnly(bool sqlServer)
    {
        // M-0692: Ninja October reads Omar live ops — no our Ninja upload on money-schema path.
        using var c=MoneySchemaFixture.Payments(sqlServer);
        EnsureRecords(c);
        SchemaSalInvoice.Ensure(c);SchemaOpsPlatformAccount.Ensure(c);
        string company=MoneySchemaFixture.Company(c),account=MoneySchemaFixture.Id();
        using var tx=c.BeginTransaction();
        SeedOwnership(c,tx,company,account);
        var err=Assert.ThrowsExactly<ApiError>(()=>SalPlatformInvoice.Upload(c,tx,Actor(),new JsonObject
        {
            ["app"]="ninja",["companyId"]=company,["cityCode"]="RUH",
            ["periodFrom"]="2026-10-01",["periodTo"]="2026-10-31",
            ["lines"]=new JsonArray(new JsonObject{["erpPlatformAccountId"]=account,["gross"]=100,["deductions"]=0,["net"]=100,["orders"]=1})
        }));
        Assert.AreEqual(400,err.Status);
        tx.Rollback();
    }
}
