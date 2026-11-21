using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaApprovalsTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ApprovalLimitSlaAndDecisionTablesAreAdditive(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Open(sqlServer);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,null,"IF OBJECT_ID(N'dbo.records',N'U') IS NULL CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL);");
        else
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
        SchemaApprovals.Ensure(c);
        SchemaApprovals.Ensure(c);
        Assert.IsTrue(SchemaSql.TableExists(c,null,"auth","approval_limit"));
        Assert.IsTrue(SchemaSql.TableExists(c,null,"org","sla_policy"));
        Assert.IsTrue(SchemaSql.TableExists(c,null,"approvals","decision"));
        var limit=new System.Text.Json.Nodes.JsonObject{["id"]=MoneySchemaFixture.Id(),["rank"]="B",["department"]="OPS",["limitSar"]=1000,["unlimited"]=false,["updatedBy"]="synthetic"};
        using(var tx=c.BeginTransaction())
        {
            SchemaApprovals.UpsertLimit(c,tx,limit);
            SchemaApprovals.UpsertSla(c,tx,new(){["id"]=MoneySchemaFixture.Id(),["requestType"]="default",["workingDays"]=3,["updatedBy"]="synthetic"});
            SchemaApprovals.SaveDecision(c,tx,new()
            {
                ["id"]=MoneySchemaFixture.Id(),["requestId"]="req-1",["outcome"]="approved",
                ["decidedBy"]="synthetic",["decidedAt"]=DateTimeOffset.UtcNow.ToString("O"),
                ["membershipSnapshot"]=new System.Text.Json.Nodes.JsonObject{["department"]="FIN"},
                ["bypassGrantId"]="",["note"]="ok",["dualPermission"]=""
            });
            tx.Commit();
        }
        Assert.AreEqual(1000m,SchemaApprovals.FindLimit(c,null,"B","OPS")!.N("limitSar"));
        Assert.AreEqual(1,SchemaApprovals.ListSla(c,null).Count(s=>s.S("requestType")=="default"));
    }
}
