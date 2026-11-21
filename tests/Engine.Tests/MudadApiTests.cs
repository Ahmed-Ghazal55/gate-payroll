using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

[TestClass]
public sealed class MudadApiTests
{
    static ClaimsPrincipal Actor(params string[] permissions)
    {
        var id=new ClaimsIdentity("synthetic");
        id.AddClaim(new Claim(ClaimTypes.Name,MoneySchemaFixture.Id()));
        id.AddClaim(new Claim(ClaimTypes.Role,"FinancialAccountant"));
        foreach(string p in permissions)id.AddClaim(new Claim("permission",p));
        return new ClaimsPrincipal(id);
    }

    static System.Data.Common.DbConnection Open(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,null,"IF OBJECT_ID(N'dbo.records',N'U') IS NULL CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL);");
        else
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
        SchemaHrMudad.Ensure(c);
        SchemaSalPayRun.Ensure(c);
        SchemaSalMudad.Ensure(c);
        return c;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UploadMatchAndLineCrud_OneAuditPerFile(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Actor("people.mudad.manage","salaries.mudad.edit","pay.manage");
        using var tx=c.BeginTransaction();
        var upload=MudadApi.Upload(c,tx,actor,new JsonObject
        {
            ["companyId"]=company,
            ["periodMonth"]="2026-10-01",
            ["fileKind"]="salaries",
            ["fileName"]="mudad-oct.xlsx",
            ["fileHashHex"]=new string('a',64),
            ["lines"]=new JsonArray(
                new JsonObject{["iqama"]="1000000001",["officialName"]="Rider A",["basicSalary"]=3000,["grossSalary"]=3500},
                new JsonObject{["iqama"]="1000000002",["officialName"]="Rider B",["basicSalary"]=2800,["grossSalary"]=3200}
            )
        });
        Assert.AreEqual(2,(int)upload.N("rowCount"));
        Assert.AreEqual(1,Store.List(c,"audit",tx).Count(a=>a.S("action")=="mudad.upload"&&a.S("entityId")==upload.S("id")));

        var matched=MudadApi.Match(c,tx,actor,upload.S("id"),new JsonObject
        {
            ["matches"]=new JsonArray(
                new JsonObject{["lineId"]=upload["lines"]![0]!["id"]!.GetValue<string>(),["erpEmployeeId"]=MoneySchemaFixture.Id()},
                new JsonObject{["lineId"]=upload["lines"]![1]!["id"]!.GetValue<string>(),["erpEmployeeId"]=MoneySchemaFixture.Id()}
            )
        });
        Assert.AreEqual(2,(int)matched.N("matchedCount"));

        string lineId=upload["lines"]![0]!["id"]!.GetValue<string>();
        var edited=MudadApi.UpdateLine(c,tx,actor,upload.S("id"),lineId,new JsonObject{["basicSalary"]=3100,["grossSalary"]=3600});
        Assert.AreEqual(3100m,edited.N("basicSalary"));

        var created=MudadApi.AddLine(c,tx,actor,upload.S("id"),new JsonObject
        {
            ["iqama"]="1000000003",["officialName"]="Rider C",["basicSalary"]=2500,["grossSalary"]=2500
        });
        Assert.AreEqual("1000000003",created.S("iqama"));

        MudadApi.DeleteLine(c,tx,actor,upload.S("id"),created.S("id"));
        Assert.AreEqual(2L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM hr_mudad_line WHERE upload_id=@id AND match_status<>4",("@id",upload.S("id"))));
        Assert.AreEqual(3L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM hr_mudad_line WHERE upload_id=@id",("@id",upload.S("id"))));
        tx.Commit();
    }

    [TestMethod]
    public void DuplicateFileHash_IsRejected()
    {
        using var c=Open(false);
        string company=MoneySchemaFixture.Company(c);
        var actor=Actor("people.mudad.manage","salaries.mudad.edit");
        string hash=new string('b',64);
        using(var tx=c.BeginTransaction())
        {
            MudadApi.Upload(c,tx,actor,new JsonObject
            {
                ["companyId"]=company,["periodMonth"]="2026-10-01",["fileKind"]="salaries",
                ["fileName"]="a.xlsx",["fileHashHex"]=hash,
                ["lines"]=new JsonArray(new JsonObject{["iqama"]="1000000001",["officialName"]="A",["basicSalary"]=1,["grossSalary"]=1})
            });
            tx.Commit();
        }
        using var tx2=c.BeginTransaction();
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>MudadApi.Upload(c,tx2,actor,new JsonObject
        {
            ["companyId"]=company,["periodMonth"]="2026-10-01",["fileKind"]="salaries",
            ["fileName"]="b.xlsx",["fileHashHex"]=hash,
            ["lines"]=new JsonArray(new JsonObject{["iqama"]="1000000002",["officialName"]="B",["basicSalary"]=1,["grossSalary"]=1})
        })).Status);
    }
}
