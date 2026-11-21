using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

[TestClass]
public sealed class SalDistributionTests
{
    static ClaimsPrincipal Actor()=>UserAccess.Principal(new JsonObject{["username"]=MoneySchemaFixture.Id(),["role"]="Admin"});
    static ClaimsPrincipal PlatformScoped(string company,params string[] platforms)
    {
        // Manual claims avoid FinancialCore.Normalize requiring a catalog sponsor id.
        var id=new ClaimsIdentity("synthetic");
        id.AddClaim(new Claim(ClaimTypes.Name,MoneySchemaFixture.Id()));
        id.AddClaim(new Claim(ClaimTypes.Role,"FinancialAccountant"));
        id.AddClaim(new Claim("permission","pay.manage"));
        id.AddClaim(new Claim("permission","pay.view"));
        id.AddClaim(new Claim("finance.sponsorIds",company));
        foreach(string p in platforms)id.AddClaim(new Claim("finance.platforms",p));
        return new ClaimsPrincipal(id);
    }

    static (System.Data.Common.DbConnection c,string company,string invoice,string line,string tgaItem) SeedInvoice(bool sqlServer,decimal lineNet=900,decimal tga=100)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);
        SchemaSalInvoice.Ensure(c);SchemaSalDistribution.Ensure(c);SchemaSalPayRun.Ensure(c);SchemaOperationCapture.Ensure(c);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,null,"IF OBJECT_ID(N'dbo.records',N'U') IS NULL CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL);");
        else
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
        string company=MoneySchemaFixture.Company(c);
        string invoice=MoneySchemaFixture.Invoice(c,company,month:"2026-10-01");
        string line=MoneySchemaFixture.Id(),account=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_line(id,invoice_id,period_month,source_row,account_key,erp_platform_account_id,gross,deductions,net,orders) VALUES(@id,@inv,'2026-10-01',1,'acc',@acc,@gross,@ded,@net,90)",
            ("@id",line),("@inv",invoice),("@acc",account),("@gross",lineNet+tga),("@ded",tga),("@net",lineNet));
        string tgaId=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_item(id,line_id,period_month,item_code,amount,bearer_status,platform_deducted,due_before_tga) VALUES(@id,@line,'2026-10-01','tga',@tga,1,1,@due)",
            ("@id",tgaId),("@line",line),("@tga",tga),("@due",lineNet+tga));
        return (c,company,invoice,line,tgaId);
    }

    static void EnsureRecords(System.Data.Common.DbConnection c)
    {
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,null,"IF OBJECT_ID(N'dbo.records',N'U') IS NULL CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL);");
        else
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
    }

    static string FreezeAssignments(System.Data.Common.DbConnection c,ClaimsPrincipal actor,string company,string accountA,string riderA,string? accountB=null,string? riderB=null,int ordersA=30,int ordersB=60)
    {
        EnsureRecords(c);
        string run=MoneySchemaFixture.Run(c,company,"ninja","2026-10-01");
        var facts=new FakeOpsLiveFactsReader();
        facts.Rows["ninja_daily"]=[new(){["id"]=MoneySchemaFixture.Id(),["reportDate"]="2026-10-01",["platformRiderAccountId"]=accountA,["riderProfileId"]=riderA,["orders"]=ordersA,["hours"]=8}];
        facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=accountA,["actualRiderProfileId"]=riderA,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-15"}];
        if(accountB!=null&&riderB!=null)
        {
            facts.Rows["ninja_daily"].Add(new(){["id"]=MoneySchemaFixture.Id(),["reportDate"]="2026-10-16",["platformRiderAccountId"]=accountB,["riderProfileId"]=riderB,["orders"]=ordersB,["hours"]=8});
            facts.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=accountB,["actualRiderProfileId"]=riderB,["effectiveFrom"]="2026-10-16",["effectiveTo"]="2026-10-31"});
        }
        facts.Rows["ninja_daily_deduction"]=[];
        facts.Rows["platform_account"]=[new(){["id"]=accountA,["externalAccountId"]="A"}];
        if(accountB!=null)facts.Rows["platform_account"].Add(new(){["id"]=accountB,["externalAccountId"]="B"});
        facts.Rows["housing"]=[];facts.Rows["fuel"]=[];facts.Rows["vehicle"]=[];facts.Rows["scheme"]=[];facts.Rows["eligibility"]=[];
        var reasons=new Dictionary<string,string>(StringComparer.Ordinal)
        {
            ["ninja_daily_deduction"]="none",["housing"]="none",["fuel"]="none",["vehicle"]="none",["scheme"]="none",["eligibility"]="none"
        };
        using(var tx=c.BeginTransaction())
        {
            OpsSnapshotCapture.Capture(c,tx,actor,run,facts,reasons);
            OpsSnapshotCapture.Freeze(c,tx,actor,run);
            tx.Commit();
        }
        return run;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TgaAllocate_SumsToItem_ResidualLast_ActualRiderByDates(bool sqlServer)
    {
        var (c,_,invoice,_,tga)=SeedInvoice(sqlServer);
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
    public void PlatformScope_BlocksAllocateProposeAndCombine(bool sqlServer)
    {
        var (c,company,invoice,_,tga)=SeedInvoice(sqlServer);
        using(c)
        {
            // Admin actor creates freeze; Keeta-only scoped user cannot load Ninja invoice.
            var admin=Actor();
            string account=(string)MoneySchemaFixture.Scalar(c,null,"SELECT erp_platform_account_id FROM sal_platform_invoice_line WHERE invoice_id=@id LIMIT 1",("@id",invoice))!;
            string rider=MoneySchemaFixture.Id();
            string run=FreezeAssignments(c,admin,company,account,rider,ordersA:90);
            var keetaOnly=PlatformScoped(company,"Keeta");
            using(var tx=c.BeginTransaction())
            {
                Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>SalDistribution.AllocateTga(c,tx,keetaOnly,new JsonObject
                {
                    ["invoiceId"]=invoice,["itemId"]=tga,
                    ["allocations"]=new JsonArray(new JsonObject{["bearer"]="company",["amount"]=100})
                })).Status);
                tx.Rollback();
            }
            using(var tx=c.BeginTransaction())
            {
                Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>SalDistribution.Propose(c,tx,keetaOnly,new JsonObject
                {
                    ["invoiceId"]=invoice,["app"]="ninja",["payRunId"]=run
                })).Status);
                tx.Rollback();
            }
            string invoice2=MoneySchemaFixture.Invoice(c,company,month:"2026-10-01");
            using(var tx=c.BeginTransaction())
            {
                Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>SalDistribution.CombineSettlements(c,tx,keetaOnly,new JsonObject
                {
                    ["app"]="ninja",["periodFrom"]="2026-10-01",["periodTo"]="2026-10-31",
                    ["invoiceIds"]=new JsonArray(invoice,invoice2)
                })).Status);
                tx.Rollback();
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Propose_PerAccountEntitlement_equalOrders_100And900StaySeparate(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);
        SchemaSalInvoice.Ensure(c);SchemaSalDistribution.Ensure(c);SchemaSalPayRun.Ensure(c);SchemaOperationCapture.Ensure(c);
        EnsureRecords(c);
        using(c)
        {
            string company=MoneySchemaFixture.Company(c);
            string invoice=MoneySchemaFixture.Invoice(c,company,month:"2026-10-01");
            string accountA=MoneySchemaFixture.Id(),accountB=MoneySchemaFixture.Id();
            string riderA=MoneySchemaFixture.Id(),riderB=MoneySchemaFixture.Id();
            string lineA=MoneySchemaFixture.Id(),lineB=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_line(id,invoice_id,period_month,source_row,account_key,erp_platform_account_id,gross,deductions,net,orders) VALUES(@id,@inv,'2026-10-01',1,'a',@acc,100,0,100,10)",
                ("@id",lineA),("@inv",invoice),("@acc",accountA));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_line(id,invoice_id,period_month,source_row,account_key,erp_platform_account_id,gross,deductions,net,orders) VALUES(@id,@inv,'2026-10-01',2,'b',@acc,900,0,900,10)",
                ("@id",lineB),("@inv",invoice),("@acc",accountB));
            var actor=Actor();
            string run=FreezeAssignments(c,actor,company,accountA,riderA,accountB,riderB,ordersA:10,ordersB:10);
            using var tx=c.BeginTransaction();
            var result=SalDistribution.Propose(c,tx,actor,new JsonObject
            {
                ["invoiceId"]=invoice,["app"]="ninja",["payRunId"]=run
            });
            var byAccount=result["rows"]!.AsArray().OfType<JsonObject>()
                .ToDictionary(r=>r.S("erpPlatformAccountId"),r=>r.N("shareAmount"));
            Assert.AreEqual(100m,byAccount[accountA]);
            Assert.AreEqual(900m,byAccount[accountB]);
            Assert.AreEqual(1000m,result.N("distributedTotal"));
            tx.Commit();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Propose_NeverUsesRegistrationFallback_SharesStayOnFrozenAccount(bool sqlServer)
    {
        var (c,company,invoice,_,tga)=SeedInvoice(sqlServer);
        using(c)
        {
            string account=(string)MoneySchemaFixture.Scalar(c,null,"SELECT erp_platform_account_id FROM sal_platform_invoice_line WHERE invoice_id=@id LIMIT 1",("@id",invoice))!;
            string registration=(string)MoneySchemaFixture.Scalar(c,null,"SELECT erp_cr_id FROM sal_platform_invoice WHERE id=@id",("@id",invoice))!;
            string rider=MoneySchemaFixture.Id();
            var actor=Actor();
            string run=FreezeAssignments(c,actor,company,account,rider,ordersA:90);
            using var tx=c.BeginTransaction();
            SalDistribution.AllocateTga(c,tx,actor,new JsonObject
            {
                ["invoiceId"]=invoice,["itemId"]=tga,
                ["allocations"]=new JsonArray(new JsonObject{["bearer"]="company",["amount"]=100})
            });
            // Old bug: worker without account fell back to erp_cr_id (registration). Caller may still send it — must not win.
            var result=SalDistribution.Propose(c,tx,actor,new JsonObject
            {
                ["invoiceId"]=invoice,["app"]="ninja",["payRunId"]=run,
                ["workers"]=new JsonArray(new JsonObject{["erpRiderId"]=rider,["erpPlatformAccountId"]=registration,["orders"]=90})
            });
            var row=result["rows"]!.AsArray().OfType<JsonObject>().Single();
            Assert.AreEqual(account,row.S("erpPlatformAccountId"));
            Assert.AreNotEqual(registration,row.S("erpPlatformAccountId"));
            Assert.AreEqual(1000m,result.N("distributedTotal"));
            tx.Commit();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Propose_Entitlement1000_CompanyTga100_RidersShare1000(bool sqlServer)
    {
        var (c,company,invoice,_,tga)=SeedInvoice(sqlServer,lineNet:900,tga:100);
        using(c)
        {
            var actor=Actor();
            string account=(string)MoneySchemaFixture.Scalar(c,null,"SELECT erp_platform_account_id FROM sal_platform_invoice_line WHERE invoice_id=@id LIMIT 1",("@id",invoice))!;
            string riderA=MoneySchemaFixture.Id(),riderB=MoneySchemaFixture.Id();
            // Two riders equal orders on the same account → 500 each of 1000 entitlement.
            string run=MoneySchemaFixture.Run(c,company,"ninja","2026-10-01");
            var facts=new FakeOpsLiveFactsReader();
            facts.Rows["ninja_daily"]=
            [
                new(){["id"]=MoneySchemaFixture.Id(),["reportDate"]="2026-10-01",["platformRiderAccountId"]=account,["riderProfileId"]=riderA,["orders"]=45},
                new(){["id"]=MoneySchemaFixture.Id(),["reportDate"]="2026-10-16",["platformRiderAccountId"]=account,["riderProfileId"]=riderB,["orders"]=45}
            ];
            facts.Rows["assignment"]=
            [
                new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=riderA,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-15"},
                new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=riderB,["effectiveFrom"]="2026-10-16",["effectiveTo"]="2026-10-31"}
            ];
            facts.Rows["ninja_daily_deduction"]=[];facts.Rows["platform_account"]=[new(){["id"]=account}];
            facts.Rows["housing"]=[];facts.Rows["fuel"]=[];facts.Rows["vehicle"]=[];facts.Rows["scheme"]=[];facts.Rows["eligibility"]=[];
            var reasons=new Dictionary<string,string>{["ninja_daily_deduction"]="n",["housing"]="n",["fuel"]="n",["vehicle"]="n",["scheme"]="n",["eligibility"]="n"};
            using(var tx=c.BeginTransaction())
            {
                OpsSnapshotCapture.Capture(c,tx,actor,run,facts,reasons);
                OpsSnapshotCapture.Freeze(c,tx,actor,run);
                SalDistribution.AllocateTga(c,tx,actor,new JsonObject
                {
                    ["invoiceId"]=invoice,["itemId"]=tga,
                    ["allocations"]=new JsonArray(new JsonObject{["bearer"]="company",["amount"]=100})
                });
                var proposed=SalDistribution.Propose(c,tx,actor,new JsonObject{["invoiceId"]=invoice,["app"]="ninja",["payRunId"]=run});
                Assert.AreEqual(1000m,proposed.N("entitlementBeforeTga"));
                Assert.AreEqual(1000m,proposed.N("distributedTotal"));
                var shares=proposed["rows"]!.AsArray().OfType<JsonObject>().Select(r=>r.N("shareAmount")).OrderBy(x=>x).ToList();
                Assert.AreEqual(500m,shares[0]);
                Assert.AreEqual(500m,shares[1]);
                tx.Commit();
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ProposeDistribution_NinjaUsesOrderShare_OtherAppsNeedExplicitBearer(bool sqlServer)
    {
        var (c,company,invoice,_,tga)=SeedInvoice(sqlServer);
        using(c)
        {
            MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice SET app_code='ninja' WHERE id=@id",("@id",invoice));
            string account=(string)MoneySchemaFixture.Scalar(c,null,"SELECT erp_platform_account_id FROM sal_platform_invoice_line WHERE invoice_id=@id LIMIT 1",("@id",invoice))!;
            string riderA=MoneySchemaFixture.Id(),riderB=MoneySchemaFixture.Id();
            var actor=Actor();
            string run=MoneySchemaFixture.Run(c,company,"ninja","2026-10-01");
            var facts=new FakeOpsLiveFactsReader();
            facts.Rows["ninja_daily"]=
            [
                new(){["id"]=MoneySchemaFixture.Id(),["reportDate"]="2026-10-01",["platformRiderAccountId"]=account,["riderProfileId"]=riderA,["orders"]=30},
                new(){["id"]=MoneySchemaFixture.Id(),["reportDate"]="2026-10-16",["platformRiderAccountId"]=account,["riderProfileId"]=riderB,["orders"]=60}
            ];
            facts.Rows["assignment"]=
            [
                new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=riderA,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-15"},
                new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=riderB,["effectiveFrom"]="2026-10-16",["effectiveTo"]="2026-10-31"}
            ];
            facts.Rows["ninja_daily_deduction"]=[];facts.Rows["platform_account"]=[new(){["id"]=account}];
            facts.Rows["housing"]=[];facts.Rows["fuel"]=[];facts.Rows["vehicle"]=[];facts.Rows["scheme"]=[];facts.Rows["eligibility"]=[];
            var reasons=new Dictionary<string,string>{["ninja_daily_deduction"]="n",["housing"]="n",["fuel"]="n",["vehicle"]="n",["scheme"]="n",["eligibility"]="n"};
            using(var tx=c.BeginTransaction())
            {
                OpsSnapshotCapture.Capture(c,tx,actor,run,facts,reasons);
                OpsSnapshotCapture.Freeze(c,tx,actor,run);
                SalDistribution.AllocateTga(c,tx,actor,new JsonObject
                {
                    ["invoiceId"]=invoice,["itemId"]=tga,
                    ["allocations"]=new JsonArray(new JsonObject{["bearer"]="company",["amount"]=100})
                });
                var ninja=SalDistribution.Propose(c,tx,actor,new JsonObject{["invoiceId"]=invoice,["app"]="ninja",["payRunId"]=run});
                Assert.AreEqual("order_share",ninja.S("method"));
                Assert.AreEqual(1000m,ninja.N("distributedTotal"));
                var shares=ninja["rows"]!.AsArray().OfType<JsonObject>().OrderBy(r=>r.N("shareAmount")).ToList();
                Assert.AreEqual(333.33m,shares[0].N("shareAmount")); // 30/90 of 1000
                Assert.AreEqual(666.67m,shares[1].N("shareAmount"));
                tx.Commit();
            }
            MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice SET app_code='keeta' WHERE id=@id",("@id",invoice));
            using(var tx=c.BeginTransaction())
            {
                Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalDistribution.Propose(c,tx,actor,new JsonObject
                {
                    ["invoiceId"]=invoice,["app"]="keeta",["payRunId"]=run
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
            Assert.AreEqual(150m,split.N("chargeTotal"));
            Assert.AreEqual(2,split["bearers"]!.AsArray().Count);
            Assert.AreEqual(150m,split["bearers"]!.AsArray().OfType<JsonObject>().Sum(b=>b.N("amount")));
            Assert.IsTrue(split["bearers"]!.AsArray().OfType<JsonObject>().All(b=>b.N("amount")==75m));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AssignShared_LoadsItem_Persists_Invented404_BlocksProposeWhileNeedsDecision(bool sqlServer)
    {
        var (c,company,invoice,line,tga)=SeedInvoice(sqlServer);
        using(c)
        {
            var actor=Actor();
            string shared=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_item(id,line_id,period_month,item_code,amount,bearer_status,platform_deducted) VALUES(@id,@line,'2026-10-01','damage',50,1,0)",
                ("@id",shared),("@line",line));
            using(var tx=c.BeginTransaction())
            {
                Assert.AreEqual(404,Assert.ThrowsExactly<ApiError>(()=>SalDistribution.AssignSharedBearer(c,tx,actor,new JsonObject
                {
                    ["itemId"]=MoneySchemaFixture.Id(),
                    ["itemDate"]="",
                    ["allocations"]=new JsonArray(new JsonObject{["bearer"]="rider",["erpRiderId"]=MoneySchemaFixture.Id(),["amount"]=50})
                })).Status);
                tx.Rollback();
            }
            using(var tx=c.BeginTransaction())
            {
                var needs=SalDistribution.AssignSharedBearer(c,tx,actor,new JsonObject
                {
                    ["itemId"]=shared,["itemDate"]="",
                    ["allocations"]=new JsonArray(new JsonObject{["bearer"]="company",["amount"]=50})
                });
                Assert.AreEqual("needs_decision",needs.S("status"));
                Assert.AreEqual((long)SalDistribution.BearerNeedsDecision,Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,"SELECT bearer_status FROM sal_platform_invoice_item WHERE id=@id",("@id",shared))!));
                tx.Commit();
            }
            string account=(string)MoneySchemaFixture.Scalar(c,null,"SELECT erp_platform_account_id FROM sal_platform_invoice_line WHERE id=@id",("@id",line))!;
            string rider=MoneySchemaFixture.Id();
            string run=FreezeAssignments(c,actor,company,account,rider,ordersA:90);
            using(var tx=c.BeginTransaction())
            {
                Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalDistribution.Propose(c,tx,actor,new JsonObject
                {
                    ["invoiceId"]=invoice,["app"]="ninja",["payRunId"]=run
                })).Status);
                tx.Rollback();
            }
            using(var tx=c.BeginTransaction())
            {
                var ready=SalDistribution.AssignSharedBearer(c,tx,actor,new JsonObject
                {
                    ["itemId"]=shared,["itemDate"]="",
                    ["allocations"]=new JsonArray(new JsonObject{["bearer"]="rider",["erpRiderId"]=rider,["amount"]=50})
                });
                Assert.AreEqual("ready",ready.S("status"));
                Assert.AreEqual(1L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(1) FROM sal_item_bearer WHERE item_id=@id",("@id",shared))!));
                tx.Commit();
            }
            using(var tx=c.BeginTransaction())
            {
                var dated=SalDistribution.AssignSharedBearer(c,tx,actor,new JsonObject
                {
                    ["itemId"]=tga,["itemDate"]="2026-10-15",["actualRiderId"]=rider,
                    ["allocations"]=new JsonArray(new JsonObject{["bearer"]="rider",["erpRiderId"]=MoneySchemaFixture.Id(),["amount"]=100})
                });
                Assert.AreEqual("ready",dated.S("status"));
                Assert.AreEqual(rider,dated["allocations"]!.AsArray().OfType<JsonObject>().First().S("erpRiderId"));
                tx.Commit();
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CombineSettlements_RequiresPlatformScope(bool sqlServer)
    {
        var (c,company,invoice,_,_)=SeedInvoice(sqlServer);
        using(c)
        {
            string invoice2=MoneySchemaFixture.Invoice(c,company,month:"2026-10-01");
            var actor=Actor();
            using var tx=c.BeginTransaction();
            var combined=SalDistribution.CombineSettlements(c,tx,actor,new JsonObject
            {
                ["app"]="ninja",["periodFrom"]="2026-10-01",["periodTo"]="2026-10-31",
                ["invoiceIds"]=new JsonArray(invoice,invoice2)
            });
            Assert.AreEqual("combine_settlement",combined["allowedActions"]!.AsArray().First()?.ToString());
            Assert.AreEqual(2m,combined.N("invoiceCount"));
            tx.Commit();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AssignShared_ForgedRider_Rejects_AndOutOfPeriodOrdersExcluded(bool sqlServer)
    {
        var (c,company,invoice,line,tga)=SeedInvoice(sqlServer);
        using(c)
        {
            var actor=Actor();
            string account=(string)MoneySchemaFixture.Scalar(c,null,"SELECT erp_platform_account_id FROM sal_platform_invoice_line WHERE id=@id",("@id",line))!;
            string actual=MoneySchemaFixture.Id(),forged=MoneySchemaFixture.Id();
            string run=FreezeAssignments(c,actor,company,account,actual,ordersA:90);
            using(var tx=c.BeginTransaction())
            {
                Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalDistribution.AssignSharedBearer(c,tx,actor,new JsonObject
                {
                    ["itemId"]=tga,["itemDate"]="2026-10-10",["payRunId"]=run,["actualRiderId"]=forged,
                    ["allocations"]=new JsonArray(new JsonObject{["bearer"]="rider",["erpRiderId"]=actual,["amount"]=100})
                })).Status);
                tx.Rollback();
            }
        }
        // Fresh seed: orders on Oct 1 with assignment only Oct 16–31 → no in-window worker.
        var (c2,company2,invoice2,_,tga2)=SeedInvoice(sqlServer);
        using(c2)
        {
            var actor=Actor();
            string account=(string)MoneySchemaFixture.Scalar(c2,null,"SELECT erp_platform_account_id FROM sal_platform_invoice_line WHERE invoice_id=@id LIMIT 1",("@id",invoice2))!;
            string riderB=MoneySchemaFixture.Id();
            string run2=MoneySchemaFixture.Run(c2,company2,"ninja","2026-10-01");
            var facts=new FakeOpsLiveFactsReader();
            facts.Rows["ninja_daily"]=[new(){["id"]=MoneySchemaFixture.Id(),["reportDate"]="2026-10-01",["platformRiderAccountId"]=account,["riderProfileId"]=riderB,["orders"]=90}];
            facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=riderB,["effectiveFrom"]="2026-10-16",["effectiveTo"]="2026-10-31"}];
            facts.Rows["ninja_daily_deduction"]=[];facts.Rows["platform_account"]=[new(){["id"]=account}];
            facts.Rows["housing"]=[];facts.Rows["fuel"]=[];facts.Rows["vehicle"]=[];facts.Rows["scheme"]=[];facts.Rows["eligibility"]=[];
            var reasons=new Dictionary<string,string>{["ninja_daily_deduction"]="n",["housing"]="n",["fuel"]="n",["vehicle"]="n",["scheme"]="n",["eligibility"]="n"};
            using(var tx=c2.BeginTransaction())
            {
                OpsSnapshotCapture.Capture(c2,tx,actor,run2,facts,reasons);
                OpsSnapshotCapture.Freeze(c2,tx,actor,run2);
                SalDistribution.AllocateTga(c2,tx,actor,new JsonObject
                {
                    ["invoiceId"]=invoice2,["itemId"]=tga2,
                    ["allocations"]=new JsonArray(new JsonObject{["bearer"]="company",["amount"]=100})
                });
                Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalDistribution.Propose(c2,tx,actor,new JsonObject
                {
                    ["invoiceId"]=invoice2,["app"]="ninja",["payRunId"]=run2
                })).Status);
                tx.Rollback();
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Propose_PendingDamage50_BlocksDistributionOf1000(bool sqlServer)
    {
        var (c,company,invoice,line,_)=SeedInvoice(sqlServer,lineNet:900,tga:100);
        using(c)
        {
            var actor=Actor();
            string account=(string)MoneySchemaFixture.Scalar(c,null,"SELECT erp_platform_account_id FROM sal_platform_invoice_line WHERE id=@id",("@id",line))!;
            string rider=MoneySchemaFixture.Id();
            string run=FreezeAssignments(c,actor,company,account,rider,ordersA:90);
            // Resolve TGA so the blocker is the untouched pending damage (status=1), not TGA.
            MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice_item SET bearer_status=2 WHERE line_id=@line AND item_code='tga'",("@line",line));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_item(id,line_id,period_month,item_code,amount,bearer_status,platform_deducted) VALUES(@id,@line,'2026-10-01','damage',50,1,0)",
                ("@id",MoneySchemaFixture.Id()),("@line",line));
            using var tx=c.BeginTransaction();
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalDistribution.Propose(c,tx,actor,new JsonObject
            {
                ["invoiceId"]=invoice,["app"]="ninja",["payRunId"]=run
            })).Status);
            tx.Rollback();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Propose_Entitlement1000_Penalty100OnRiderA_EqualOrders_A400_B500(bool sqlServer)
    {
        var (c,company,invoice,line,_)=SeedInvoice(sqlServer,lineNet:900,tga:0);
        using(c)
        {
            var actor=Actor();
            string account=(string)MoneySchemaFixture.Scalar(c,null,"SELECT erp_platform_account_id FROM sal_platform_invoice_line WHERE id=@id",("@id",line))!;
            MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice_line SET gross=1000,deductions=100,net=900 WHERE id=@id",("@id",line));
            string riderA=MoneySchemaFixture.Id(),riderB=MoneySchemaFixture.Id();
            // Seed with tga:0 still inserts a zero TGA row; keep it ready and add a separate penalty item.
            MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice_item SET bearer_status=2 WHERE line_id=@line AND item_code='tga'",("@line",line));
            string damage=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_item(id,line_id,period_month,item_code,amount,bearer_status,platform_deducted) VALUES(@id,@line,'2026-10-01','damage',100,1,1)",
                ("@id",damage),("@line",line));
            string run=MoneySchemaFixture.Run(c,company,"ninja","2026-10-01");
            var facts=new FakeOpsLiveFactsReader();
            facts.Rows["ninja_daily"]=
            [
                new(){["id"]=MoneySchemaFixture.Id(),["reportDate"]="2026-10-01",["platformRiderAccountId"]=account,["riderProfileId"]=riderA,["orders"]=45},
                new(){["id"]=MoneySchemaFixture.Id(),["reportDate"]="2026-10-16",["platformRiderAccountId"]=account,["riderProfileId"]=riderB,["orders"]=45}
            ];
            facts.Rows["assignment"]=
            [
                new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=riderA,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-15"},
                new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=riderB,["effectiveFrom"]="2026-10-16",["effectiveTo"]="2026-10-31"}
            ];
            facts.Rows["ninja_daily_deduction"]=[];facts.Rows["platform_account"]=[new(){["id"]=account}];
            facts.Rows["housing"]=[];facts.Rows["fuel"]=[];facts.Rows["vehicle"]=[];facts.Rows["scheme"]=[];facts.Rows["eligibility"]=[];
            var reasons=new Dictionary<string,string>{["ninja_daily_deduction"]="n",["housing"]="n",["fuel"]="n",["vehicle"]="n",["scheme"]="n",["eligibility"]="n"};
            using var tx=c.BeginTransaction();
            OpsSnapshotCapture.Capture(c,tx,actor,run,facts,reasons);
            OpsSnapshotCapture.Freeze(c,tx,actor,run);
            SalDistribution.AssignSharedBearer(c,tx,actor,new JsonObject
            {
                ["itemId"]=damage,["payRunId"]=run,
                ["allocations"]=new JsonArray(new JsonObject{["bearer"]="rider",["erpRiderId"]=riderA,["amount"]=100})
            });
            var proposed=SalDistribution.Propose(c,tx,actor,new JsonObject{["invoiceId"]=invoice,["app"]="ninja",["payRunId"]=run});
            Assert.AreEqual(1000m,proposed.N("entitlementBeforeTga"));
            var byRider=proposed["rows"]!.AsArray().OfType<JsonObject>().ToDictionary(r=>r.S("erpRiderId"),r=>r.N("shareAmount"));
            Assert.AreEqual(400m,byRider[riderA]);
            Assert.AreEqual(500m,byRider[riderB]);
            tx.Commit();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Propose_SameAccountLines100And900_Aggregates_AndPeriodMismatchRejected(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);
        SchemaSalInvoice.Ensure(c);SchemaSalDistribution.Ensure(c);SchemaSalPayRun.Ensure(c);SchemaOperationCapture.Ensure(c);
        EnsureRecords(c);
        using(c)
        {
            string company=MoneySchemaFixture.Company(c);
            string invoice=MoneySchemaFixture.Invoice(c,company,month:"2026-11-01");
            string account=MoneySchemaFixture.Id(),rider=MoneySchemaFixture.Id();
            string line1=MoneySchemaFixture.Id(),line2=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_line(id,invoice_id,period_month,source_row,account_key,erp_platform_account_id,gross,deductions,net,orders) VALUES(@id,@inv,'2026-11-01',1,'a',@acc,100,0,100,10)",
                ("@id",line1),("@inv",invoice),("@acc",account));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_line(id,invoice_id,period_month,source_row,account_key,erp_platform_account_id,gross,deductions,net,orders) VALUES(@id,@inv,'2026-11-01',2,'a',@acc,900,0,900,10)",
                ("@id",line2),("@inv",invoice),("@acc",account));
            var actor=Actor();
            // October freeze must not serve a November invoice.
            string octRun=FreezeAssignments(c,actor,company,account,rider,ordersA:20);
            using(var tx=c.BeginTransaction())
            {
                Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalDistribution.Propose(c,tx,actor,new JsonObject
                {
                    ["invoiceId"]=invoice,["app"]="ninja",["payRunId"]=octRun
                })).Status);
                tx.Rollback();
            }
            string novRun=MoneySchemaFixture.Run(c,company,"ninja","2026-11-01");
            var facts=new FakeOpsLiveFactsReader();
            facts.Rows["ninja_daily"]=[new(){["id"]=MoneySchemaFixture.Id(),["reportDate"]="2026-11-01",["platformRiderAccountId"]=account,["riderProfileId"]=rider,["orders"]=20}];
            facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=rider,["effectiveFrom"]="2026-11-01",["effectiveTo"]="2026-11-30"}];
            facts.Rows["ninja_daily_deduction"]=[];facts.Rows["platform_account"]=[new(){["id"]=account}];
            facts.Rows["housing"]=[];facts.Rows["fuel"]=[];facts.Rows["vehicle"]=[];facts.Rows["scheme"]=[];facts.Rows["eligibility"]=[];
            var reasons=new Dictionary<string,string>{["ninja_daily_deduction"]="n",["housing"]="n",["fuel"]="n",["vehicle"]="n",["scheme"]="n",["eligibility"]="n"};
            using var tx2=c.BeginTransaction();
            OpsSnapshotCapture.Capture(c,tx2,actor,novRun,facts,reasons);
            OpsSnapshotCapture.Freeze(c,tx2,actor,novRun);
            var proposed=SalDistribution.Propose(c,tx2,actor,new JsonObject{["invoiceId"]=invoice,["app"]="ninja",["payRunId"]=novRun});
            Assert.AreEqual(1000m,proposed.N("distributedTotal"));
            Assert.AreEqual(1,proposed["rows"]!.AsArray().Count);
            Assert.AreEqual(1000m,proposed["rows"]!.AsArray().OfType<JsonObject>().Single().N("shareAmount"));
            tx2.Commit();
        }
    }
}
