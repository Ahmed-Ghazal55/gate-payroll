using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SalDistributionTests
{
    static ClaimsPrincipal Actor()=>UserAccess.Principal(new JsonObject{["username"]=MoneySchemaFixture.Id(),["role"]="Admin"});

    static (System.Data.Common.DbConnection c,string company,string invoice,string line,string tgaItem) SeedInvoice(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);
        SchemaSalInvoice.Ensure(c);SchemaSalDistribution.Ensure(c);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,null,"IF OBJECT_ID(N'dbo.records',N'U') IS NULL CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL);");
        else
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
        string company=MoneySchemaFixture.Company(c);
        string invoice=MoneySchemaFixture.Invoice(c,company,month:"2026-10-01");
        string line=MoneySchemaFixture.Id(),account=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_line(id,invoice_id,period_month,source_row,account_key,erp_platform_account_id,gross,deductions,net,orders) VALUES(@id,@inv,'2026-10-01',1,'acc',@acc,1000,100,900,90)",
            ("@id",line),("@inv",invoice),("@acc",account));
        string tga=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_item(id,line_id,period_month,item_code,amount,bearer_status,platform_deducted,due_before_tga) VALUES(@id,@line,'2026-10-01','tga',100,1,1,1000)",
            ("@id",tga),("@line",line));
        return (c,company,invoice,line,tga);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TgaAllocate_SumsToItem_ResidualLast_ActualRiderByDates(bool sqlServer)
    {
        var (c,company,invoice,_,tga)=SeedInvoice(sqlServer);
        using(c)
        {
            string riderA=MoneySchemaFixture.Id(),riderB=MoneySchemaFixture.Id();
            var actor=Actor();
            using var tx=c.BeginTransaction();
            var result=SalDistribution.AllocateTga(c,tx,actor,new JsonObject
            {
                ["invoiceId"]=invoice,
                ["itemId"]=tga,
                ["allocations"]=new JsonArray(
                    new JsonObject{["bearer"]="rider",["erpRiderId"]=riderA,["amount"]=40,["fromDate"]="2026-10-01",["toDate"]="2026-10-10"},
                    new JsonObject{["bearer"]="rider",["erpRiderId"]=riderB,["amount"]=0,["fromDate"]="2026-10-11",["toDate"]="2026-10-31"},
                    new JsonObject{["bearer"]="company",["amount"]=0}
                )
            });
            // Residual 60 goes to last line (company) so sum=100.
            Assert.AreEqual(100m,result.N("allocatedTotal"));
            Assert.AreEqual(3,result["bearers"]!.AsArray().Count);
            Assert.AreEqual(60m,result["bearers"]!.AsArray().OfType<JsonObject>().Last().N("amount"));
            Assert.IsTrue(result["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="allocate_tga"));
            Assert.AreEqual(3L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(1) FROM sal_item_bearer WHERE item_id=@id",("@id",tga))!));
            tx.Commit();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ProposeDistribution_NinjaUsesOrderShare_OtherAppsNeedExplicitBearer(bool sqlServer)
    {
        var (c,company,invoice,line,_)=SeedInvoice(sqlServer);
        using(c)
        {
            MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice SET app_code='ninja' WHERE id=@id",("@id",invoice));
            string riderA=MoneySchemaFixture.Id(),riderB=MoneySchemaFixture.Id();
            var actor=Actor();
            using(var tx=c.BeginTransaction())
            {
                var ninja=SalDistribution.Propose(c,tx,actor,new JsonObject
                {
                    ["invoiceId"]=invoice,
                    ["app"]="ninja",
                    ["workers"]=new JsonArray(
                        new JsonObject{["erpRiderId"]=riderA,["orders"]=30,["fromDate"]="2026-10-01",["toDate"]="2026-10-15"},
                        new JsonObject{["erpRiderId"]=riderB,["orders"]=60,["fromDate"]="2026-10-16",["toDate"]="2026-10-31"}
                    )
                });
                Assert.AreEqual("order_share",ninja.S("method"));
                Assert.AreEqual(900m,ninja.N("distributedTotal"));
                var shares=ninja["rows"]!.AsArray().OfType<JsonObject>().OrderBy(r=>r.N("shareAmount")).ToList();
                Assert.AreEqual(300m,shares[0].N("shareAmount"));
                Assert.AreEqual(600m,shares[1].N("shareAmount"));
                tx.Commit();
            }
            MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice SET app_code='keeta' WHERE id=@id",("@id",invoice));
            using(var tx=c.BeginTransaction())
            {
                Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalDistribution.Propose(c,tx,actor,new JsonObject
                {
                    ["invoiceId"]=invoice,
                    ["app"]="keeta",
                    ["workers"]=new JsonArray(new JsonObject{["erpRiderId"]=riderA,["orders"]=10})
                })).Status);
                tx.Rollback();
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void KeetaRejectionSplit_AfterFreeQuota_OnAccountId(bool sqlServer)
    {
        var (c,_,_,_,_)=SeedInvoice(sqlServer);
        using(c)
        {
            string riderA=MoneySchemaFixture.Id(),riderB=MoneySchemaFixture.Id(),account=MoneySchemaFixture.Id();
            var split=SalDistribution.SplitKeetaRejections(new JsonObject
            {
                ["erpPlatformAccountId"]=account,
                ["days"]=new JsonArray(
                    new JsonObject{["date"]="2026-10-01",["rejected"]=5},
                    new JsonObject{["date"]="2026-10-02",["rejected"]=1}
                ),
                ["workers"]=new JsonArray(
                    new JsonObject{["erpRiderId"]=riderA,["fromDate"]="2026-10-01",["toDate"]="2026-10-01"},
                    new JsonObject{["erpRiderId"]=riderB,["fromDate"]="2026-10-01",["toDate"]="2026-10-02"}
                )
            });
            // Day1: 5-2 free = 3 * 50 = 150; Day2: 1-2 free = 0. Split day1 across riders who worked that day.
            Assert.AreEqual(150m,split.N("chargeTotal"));
            Assert.AreEqual(2,split["bearers"]!.AsArray().Count);
            Assert.AreEqual(150m,split["bearers"]!.AsArray().OfType<JsonObject>().Sum(b=>b.N("amount")));
            Assert.IsTrue(split["bearers"]!.AsArray().OfType<JsonObject>().All(b=>b.N("amount")==75m));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CombineSettlements_AndUndatedSharedNeedsSupervisorAssign(bool sqlServer)
    {
        var (c,company,invoice,_,_)=SeedInvoice(sqlServer);
        using(c)
        {
            string invoice2=MoneySchemaFixture.Invoice(c,company,month:"2026-10-01");
            var actor=Actor();
            using(var tx=c.BeginTransaction())
            {
                var combined=SalDistribution.CombineSettlements(c,tx,actor,new JsonObject
                {
                    ["app"]="ninja",
                    ["periodFrom"]="2026-10-01",
                    ["periodTo"]="2026-10-31",
                    ["invoiceIds"]=new JsonArray(invoice,invoice2)
                });
                Assert.AreEqual("combine_settlement",combined["allowedActions"]!.AsArray().First()?.ToString());
                Assert.AreEqual(2m,combined.N("invoiceCount"));
                tx.Commit();
            }
            using(var tx=c.BeginTransaction())
            {
                var needs=SalDistribution.AssignSharedBearer(c,tx,actor,new JsonObject
                {
                    ["itemId"]=MoneySchemaFixture.Id(),
                    ["erpPlatformAccountId"]=MoneySchemaFixture.Id(),
                    ["itemDate"]="",
                    ["allocations"]=new JsonArray(new JsonObject{["bearer"]="rider",["erpRiderId"]=MoneySchemaFixture.Id(),["amount"]=50})
                });
                Assert.AreEqual("needs_decision",needs.S("status"));
                Assert.IsTrue(needs["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="assign_shared_bearer"));
                tx.Commit();
            }
            using(var tx=c.BeginTransaction())
            {
                var dated=SalDistribution.AssignSharedBearer(c,tx,actor,new JsonObject
                {
                    ["itemId"]=MoneySchemaFixture.Id(),
                    ["erpPlatformAccountId"]=MoneySchemaFixture.Id(),
                    ["itemDate"]="2026-10-15",
                    ["actualRiderId"]=MoneySchemaFixture.Id(),
                    ["allocations"]=new JsonArray(new JsonObject{["bearer"]="rider",["erpRiderId"]=MoneySchemaFixture.Id(),["amount"]=50})
                });
                Assert.AreEqual("ready",dated.S("status"));
                Assert.AreEqual(dated.S("actualRiderId"),dated["allocations"]!.AsArray().OfType<JsonObject>().First().S("erpRiderId"));
                tx.Commit();
            }
        }
    }
}
