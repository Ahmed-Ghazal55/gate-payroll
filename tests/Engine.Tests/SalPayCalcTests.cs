using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>U-W2-15a worked examples: gross → L1 → NET → L2 → final (exact SAR). Both providers.</summary>
[TestClass]
public sealed class SalPayCalcTests
{
    static ClaimsPrincipal Actor()
    {
        var id=new ClaimsIdentity("synthetic");
        id.AddClaim(new Claim(ClaimTypes.Name,MoneySchemaFixture.Id()));
        id.AddClaim(new Claim(ClaimTypes.Role,"Admin"));
        id.AddClaim(new Claim("permission","pay.manage"));
        id.AddClaim(new Claim("permission","pay.approve"));
        id.AddClaim(new Claim("permission","people.mudad.manage"));
        id.AddClaim(new Claim("permission","salaries.mudad.edit"));
        return new ClaimsPrincipal(id);
    }

    static System.Data.Common.DbConnection Open(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,null,"IF OBJECT_ID(N'dbo.records',N'U') IS NULL CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL);");
        else
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
        SchemaSalInvoice.Ensure(c);SchemaSalDistribution.Ensure(c);
        SchemaSalPayRun.Ensure(c);SchemaHrMudad.Ensure(c);SchemaSalMudad.Ensure(c);SchemaDebt.Ensure(c);
        SchemaOperationCapture.Ensure(c);
        return c;
    }

    static string FrozenRun(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,string company,string app)
    {
        // Jahez/Chefz pay_run kinds are not seeded yet (settlement paths); add minimal catalogue so freeze can pass.
        if(app is "jahez" or "chefz")
            foreach(string kind in new[]{"platform_account","assignment","housing","fuel","vehicle","scheme","eligibility"})
                MoneySchemaFixture.Exec(c,tx,"""
                    INSERT INTO fin_operation_required_kind(id,operation_type,app_code,required_kind)
                    SELECT @id,'pay_run',@app,@kind WHERE NOT EXISTS(
                      SELECT 1 FROM fin_operation_required_kind WHERE operation_type='pay_run' AND app_code=@app AND required_kind=@kind)
                    """,("@id",MoneySchemaFixture.Id()),("@app",app),("@kind",kind));
        string run=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month) VALUES(@id,@c,@app,'2026-10-01')",("@id",run),("@c",company),("@app",app));
        var facts=new FakeOpsLiveFactsReader();
        foreach(string kind in new[]{"ninja_daily","ninja_daily_deduction","platform_account","assignment","housing","fuel","vehicle","scheme","eligibility",
            "keeta_invoice_row","hs_ops","hs_wallet","platform_ops"})
            facts.Rows[kind]=[];
        string primary=app switch{"ninja"=>"ninja_daily","keeta"=>"keeta_invoice_row","hs"=>"hs_ops","amazon"=>"platform_ops",_=>"assignment"};
        facts.Rows[primary]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["riderProfileId"]=MoneySchemaFixture.Id(),["orders"]=1,["reportDate"]="2026-10-01"}];
        if(app=="ninja")facts.Rows["ninja_daily_deduction"]=[];
        facts.Rows["platform_account"]=[new(){["id"]=MoneySchemaFixture.Id(),["externalAccountId"]="A"}];
        facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=MoneySchemaFixture.Id(),["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31"}];
        var reasons=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var kv in facts.Rows)if(kv.Value.Count==0)reasons[kv.Key]="none";
        OpsSnapshotCapture.Capture(c,tx,actor,run,facts,reasons);
        OpsSnapshotCapture.Freeze(c,tx,actor,run);
        return run;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Ninja_WorkedExample_Gross1000_L1_230_Net770_L2_70_Final700(bool sqlServer)
    {
        // Exact: gross 1000; L1 fuel 50 + vehicle 100 + housing 80 = 230; NET 770; L2 TGA 40 + HR 30 = 70; final 700.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        string rider=MoneySchemaFixture.Id();
        var actor=Actor();
        using var tx=c.BeginTransaction();
        string run=FrozenRun(c,tx,actor,company,"ninja");
        var result=SalPayCalc.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=1000,["commission"]=0,["schemeType"]="commission",
                ["level1"]=new JsonArray(
                    new JsonObject{["itemCode"]="fuel",["amount"]=50,["accountCode"]="112511"},
                    new JsonObject{["itemCode"]="vehicle",["amount"]=100,["accountCode"]="112505"},
                    new JsonObject{["itemCode"]="housing",["amount"]=80,["accountCode"]="112505"}
                ),
                ["level2"]=new JsonArray(
                    new JsonObject{["itemCode"]="tga_rider",["amount"]=40,["accountCode"]="112502"},
                    new JsonObject{["itemCode"]="hr_request",["amount"]=30,["accountCode"]="112506"}
                )
            })
        });
        var row=result["riders"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual(1000m,row.N("gross"));
        Assert.AreEqual(230m,row.N("level1Total"));
        Assert.AreEqual(770m,row.N("netAfterLevel1"));
        Assert.AreEqual(70m,row.N("level2Total"));
        Assert.AreEqual(700m,row.N("salaryNet"));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Keeta_WorkedExample_AndIneligible_NoNegativeDebt(bool sqlServer)
    {
        // Exact: gross 500; L1 fuel 80; NET 420; L2 TGA 20; final 400. Ineligible: net -50 → no carryover.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Actor();
        using var tx=c.BeginTransaction();
        string run=FrozenRun(c,tx,actor,company,"keeta");
        var ok=SalPayCalc.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=MoneySchemaFixture.Id(),["gross"]=500,["commission"]=0,["schemeType"]="commission",
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="fuel",["amount"]=80,["accountCode"]="112511"}),
                ["level2"]=new JsonArray(new JsonObject{["itemCode"]="tga_rider",["amount"]=20,["accountCode"]="112502"})
            })
        });
        Assert.AreEqual(400m,ok["riders"]![0]!["salaryNet"]!.GetValue<decimal>());

        string inelig=MoneySchemaFixture.Id();
        var neg=SalPayCalc.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=inelig,["gross"]=100,["commission"]=0,["schemeType"]="salary",
                ["keetaIneligible"]=true,
                // Personal L1 (advance) can push net negative; §22.9.2 blocks 112507 carryover.
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="cash_advance",["amount"]=150,["accountCode"]="112501",["operating"]=false}),
                ["level2"]=new JsonArray()
            })
        });
        var row=neg["riders"]!.AsArray().OfType<JsonObject>().Single(r=>r.S("erpRiderId")==inelig);
        Assert.AreEqual(-50m,row.N("salaryNet"));
        Assert.IsTrue(row.B("keetaIneligible"));
        Assert.IsFalse(row.B("createsNegativeDebt"));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HungerStation_WorkedExample_WalletL1_TgaL2(bool sqlServer)
    {
        // Exact: gross 1000; L1 wallet top-up 100 (112513); NET 900; L2 TGA 50; final 850.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Actor();
        using var tx=c.BeginTransaction();
        string run=FrozenRun(c,tx,actor,company,"hs");
        var result=SalPayCalc.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=MoneySchemaFixture.Id(),["gross"]=1000,["commission"]=0,["schemeType"]="commission",
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="hs_wallet_topup",["amount"]=100,["accountCode"]="112513"}),
                ["level2"]=new JsonArray(new JsonObject{["itemCode"]="tga_rider",["amount"]=50,["accountCode"]="112502"})
            })
        });
        var row=result["riders"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual(100m,row.N("level1Total"));
        Assert.AreEqual(900m,row.N("netAfterLevel1"));
        Assert.AreEqual(850m,row.N("salaryNet"));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Chefz_ConsumesRiderAmount_NeverRecomputesGross(bool sqlServer)
    {
        // Exact: Omar RiderAmount 800 as gross; reject recompute when gross≠riderAmount; L1 fuel 40 → net 760.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Actor();
        using var tx=c.BeginTransaction();
        string run=FrozenRun(c,tx,actor,company,"chefz");
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalPayCalc.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=MoneySchemaFixture.Id(),["gross"]=900,["riderAmount"]=800,["commission"]=50,
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        })).Status);

        var ok=SalPayCalc.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=MoneySchemaFixture.Id(),["gross"]=800,["riderAmount"]=800,["commission"]=0,
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="fuel",["amount"]=40,["accountCode"]="112511"}),
                ["level2"]=new JsonArray()
            })
        });
        Assert.AreEqual(800m,ok["riders"]![0]!["gross"]!.GetValue<decimal>());
        Assert.AreEqual(760m,ok["riders"]![0]!["salaryNet"]!.GetValue<decimal>());
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Jahez_AndAmazon_SharedL1L2Path_WhenGrossProvided(bool sqlServer)
    {
        // Exact Jahez: gross 600; L1 advance 50; NET 550; L2 HR 25; final 525.
        // Exact Amazon: gross 400; L1 vehicle 40; NET 360; L2 0; final 360.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Actor();
        using var tx=c.BeginTransaction();
        string jahez=FrozenRun(c,tx,actor,company,"jahez");
        var j=SalPayCalc.Calculate(c,tx,actor,jahez,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=MoneySchemaFixture.Id(),["gross"]=600,["commission"]=0,
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="cash_advance",["amount"]=50,["accountCode"]="112501"}),
                ["level2"]=new JsonArray(new JsonObject{["itemCode"]="hr_request",["amount"]=25,["accountCode"]="112506"})
            })
        });
        Assert.AreEqual(525m,j["riders"]![0]!["salaryNet"]!.GetValue<decimal>());

        string amazon=FrozenRun(c,tx,actor,company,"amazon");
        var a=SalPayCalc.Calculate(c,tx,actor,amazon,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=MoneySchemaFixture.Id(),["gross"]=400,["commission"]=0,
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="vehicle",["amount"]=40,["accountCode"]="112505"}),
                ["level2"]=new JsonArray()
            })
        });
        Assert.AreEqual(360m,a["riders"]![0]!["salaryNet"]!.GetValue<decimal>());
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CrossAppDebt_IsLevel1_AndNegativeNetCreatesDebtOnlyAtApproval(bool sqlServer)
    {
        // X2: cross-app debt 60 in L1. Then net -20 → createsNegativeDebt true only when not keeta-ineligible.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        string rider=MoneySchemaFixture.Id();
        var actor=Actor();
        using var tx=c.BeginTransaction();
        string run=FrozenRun(c,tx,actor,company,"ninja");
        // X2: cross-app debt amount is an L1 input (no ledger insert required for the calc itself).
        var result=SalPayCalc.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=100,["commission"]=0,
                ["level1"]=new JsonArray(
                    new JsonObject{["itemCode"]="fuel",["amount"]=80,["accountCode"]="112511"},
                    new JsonObject{["itemCode"]="cross_app_debt",["amount"]=60,["accountCode"]="112507",["sourceApp"]="keeta"}
                ),
                ["level2"]=new JsonArray()
            })
        });
        var row=result["riders"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual(140m,row.N("level1Total"));
        Assert.AreEqual(-40m,row.N("salaryNet"));
        Assert.IsTrue(row.B("createsNegativeDebt"));

        var preview=SalPayCalc.BulkPreviewDebts(c,tx,actor,run,new JsonObject());
        Assert.AreEqual(1,preview["rows"]!.AsArray().Count);
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MudadApply_LinksBasicSalaryIntoLevel1(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Actor();
        using var tx=c.BeginTransaction();
        string run=FrozenRun(c,tx,actor,company,"ninja");
        var upload=MudadApi.Upload(c,tx,actor,new JsonObject
        {
            ["companyId"]=company,["periodMonth"]="2026-10-01",["fileKind"]="salaries",
            ["fileName"]="m.xlsx",["fileHashHex"]=new string('c',64),
            ["lines"]=new JsonArray(new JsonObject{["iqama"]="1000000099",["officialName"]="X",["basicSalary"]=3000,["grossSalary"]=3000})
        });
        string lineId=upload["lines"]![0]!["id"]!.GetValue<string>();
        string erp=MoneySchemaFixture.Id();
        MudadApi.Match(c,tx,actor,upload.S("id"),new JsonObject
        {
            ["matches"]=new JsonArray(new JsonObject{["lineId"]=lineId,["erpEmployeeId"]=erp})
        });
        SalPayCalc.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=erp,["gross"]=5000,["commission"]=0,
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        });
        var applied=SalPayCalc.ApplyMudad(c,tx,actor,new JsonObject
        {
            ["payRunId"]=run,["erpRiderId"]=erp,["mudadLineId"]=lineId,["amount"]=3000
        });
        Assert.AreEqual(3000m,applied.N("amount"));
        Assert.AreEqual(2000m,applied.N("salaryNet"));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SalaryScheme_OperatingCostsDoNotReduceSalary(bool sqlServer)
    {
        // BR §40.3: fixed salary — operating L1 shown as company cost, not deducted; personal L1 still deducted.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Actor();
        using var tx=c.BeginTransaction();
        string run=FrozenRun(c,tx,actor,company,"ninja");
        var result=SalPayCalc.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=MoneySchemaFixture.Id(),["gross"]=3000,["commission"]=0,["schemeType"]="salary",
                ["level1"]=new JsonArray(
                    new JsonObject{["itemCode"]="fuel",["amount"]=100,["accountCode"]="112511",["operating"]=true},
                    new JsonObject{["itemCode"]="cash_advance",["amount"]=50,["accountCode"]="112501",["operating"]=false}
                ),
                ["level2"]=new JsonArray()
            })
        });
        var row=result["riders"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual(50m,row.N("level1Total"));
        Assert.AreEqual(100m,row.N("companyOperatingTotal"));
        Assert.AreEqual(2950m,row.N("salaryNet"));
        tx.Commit();
    }
}
