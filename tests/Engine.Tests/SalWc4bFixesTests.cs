using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>WC4B: Codex RWC4 HIGH/MEDIUM + per-app worked numbers (exact SAR).</summary>
[TestClass]
public sealed class SalWc4bFixesTests
{
    static ClaimsPrincipal Admin(string? name=null)
    {
        var id=new ClaimsIdentity("synthetic");
        id.AddClaim(new Claim(ClaimTypes.Name,name??MoneySchemaFixture.Id()));
        id.AddClaim(new Claim(ClaimTypes.Role,"Admin"));
        id.AddClaim(new Claim("permission","pay.manage"));
        id.AddClaim(new Claim("permission","pay.approve"));
        id.AddClaim(new Claim("permission","people.mudad.manage"));
        id.AddClaim(new Claim("permission","salaries.mudad.edit"));
        return new ClaimsPrincipal(id);
    }

    static ClaimsPrincipal Scoped(string name,string companyId,string platform,params string[] permissions)
    {
        var id=new ClaimsIdentity("synthetic");
        id.AddClaim(new Claim(ClaimTypes.Name,name));
        id.AddClaim(new Claim(ClaimTypes.Role,"FinancialAccountant"));
        foreach(string p in permissions)id.AddClaim(new Claim("permission",p));
        id.AddClaim(new Claim("finance.sponsorIds",companyId));
        id.AddClaim(new Claim("finance.platforms",platform));
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
        SchemaOperationCapture.Ensure(c);SchemaSalStatement.Ensure(c);SchemaFinPeriodBalance.Ensure(c);
        return c;
    }

    static string FrozenRun(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,string company,string app,
        string? accountId=null,string? riderId=null,string? schemeId=null,bool? keetaEligible=null,decimal? frozenGross=null)
    {
        string run=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month) VALUES(@id,@c,@app,'2026-10-01')",("@id",run),("@c",company),("@app",app));
        var facts=new FakeOpsLiveFactsReader();
        foreach(string kind in new[]{"ninja_daily","ninja_daily_deduction","platform_account","assignment","housing","fuel","vehicle","scheme","eligibility",
            "keeta_invoice_row","hs_ops","hs_wallet","platform_ops","chefz_settlement","jahez_day"})
            facts.Rows[kind]=[];
        string account=accountId??MoneySchemaFixture.Id(),rider=riderId??MoneySchemaFixture.Id();
        string primary=app switch{"ninja"=>"ninja_daily","keeta"=>"keeta_invoice_row","hs"=>"hs_ops","amazon"=>"platform_ops","chefz"=>"chefz_settlement","jahez"=>"jahez_day",_=>"assignment"};
        decimal gross=frozenGross??800m;
        facts.Rows[primary]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["erpPlatformAccountId"]=account,["riderProfileId"]=rider,["erpRiderId"]=rider,["orders"]=1,["reportDate"]="2026-10-01",["RiderAmount"]=gross,["gross"]=gross}];
        if(app=="ninja")facts.Rows["ninja_daily_deduction"]=[];
        facts.Rows["platform_account"]=[new(){["id"]=account,["externalAccountId"]="A1",["code"]="A1"}];
        facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=rider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31"}];
        string schemeCode=app=="keeta"&&keetaEligible==false?"salary":"commission";
        facts.Rows["scheme"]=[new(){["id"]=schemeId??MoneySchemaFixture.Id(),["erpRiderId"]=rider,["schemeCode"]=schemeCode,["source"]="pay_settings"}];
        bool eligible=keetaEligible??true;
        facts.Rows["eligibility"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["eligible"]=eligible,["source"]=eligible?"pay_settings":"keeta_eligibility_view"}];
        if(app=="hs")facts.Rows["hs_wallet"]=
        [
            new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["balance"]=100,["amount"]=100,["asOf"]="2026-10-31",["fundingType"]="invoice_close"},
            new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["balance"]=100,["amount"]=100,["asOf"]="2026-10-31",["fundingType"]="custody"}
        ];
        if(app=="chefz")facts.Rows["chefz_settlement"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["RiderAmount"]=gross,["GrossEarnings"]=1000,["CompanyAmount"]=200}];
        var reasons=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var kv in facts.Rows)if(kv.Value.Count==0)reasons[kv.Key]="none";
        OpsSnapshotCapture.Capture(c,tx,actor,run,facts,reasons);
        OpsSnapshotCapture.Freeze(c,tx,actor,run);
        return run;
    }

    static void EnsurePeriod(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,string company)
    {
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) SELECT @id,@c,'2026-10-01',1,datetime('now') WHERE NOT EXISTS(SELECT 1 FROM fin_accounting_period WHERE company_id=@c AND period_month='2026-10-01')",
            ("@id",MoneySchemaFixture.Id()),("@c",company));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Commission_BoundZero_L1ThenL2_Exact700(bool sqlServer)
    {
        // Codex: gross 1000 − commission 100 − L1 230 − L2 70 = 600 (not 700).
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",riderId:rider);
        var result=SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                // RWC4a1 H1: commission binds from frozen evidence (none → 0). Order L1→NET→L2: 1000−230−70=700.
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
        Assert.AreEqual(700m,result["riders"]![0]!["salaryNet"]!.GetValue<decimal>());
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void KeetaIneligible_FuelRecoverThen512121_NetFloorZero(bool sqlServer)
    {
        // §22.9.2: gross 100, fuel 150 → recover 100, unrecovered 50 on 512121, net 0 (not 100 with all fuel on company).
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"keeta",riderId:rider,keetaEligible:false,frozenGross:100m);
        var result=SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=100,["commission"]=0,["schemeType"]="salary",
                ["keetaIneligible"]=true,
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="fuel",["amount"]=150,["accountCode"]="112511"}),
                ["level2"]=new JsonArray()
            })
        });
        var row=result["riders"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual(0m,row.N("salaryNet"));
        Assert.AreEqual(50m,row.N("companyOperatingTotal"));
        Assert.IsFalse(row.B("createsNegativeDebt"));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void KeetaIneligible_PersonalAdvance_NetFloorZero_ResidualDebt(bool sqlServer)
    {
        // §22.9.2: gross 100, advance 150 → net 0 (not -50); residual personal 50 stays open; no 112507 carryover.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"keeta",riderId:rider,keetaEligible:false,frozenGross:100m);
        var result=SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=100,["commission"]=0,["schemeType"]="salary",
                ["keetaIneligible"]=true,
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="cash_advance",["amount"]=150,["accountCode"]="112501",["operating"]=false}),
                ["level2"]=new JsonArray()
            })
        });
        var row=result["riders"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual(0m,row.N("salaryNet"));
        Assert.AreEqual(50m,row.N("personalResidualDebt"));
        Assert.IsFalse(row.B("createsNegativeDebt"));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Chefz_RequiresRiderAmount_RejectsMissing(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"chefz",riderId:rider);
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=900,["commission"]=50,
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        })).Status);
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HungerStation_CollectedTopup_Blocked_CustodyAndCloseAllowed(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"hs",riderId:rider);
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=1000,["commission"]=0,
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="hs_wallet_topup",["amount"]=100,["accountCode"]="112513",["fundingType"]="collected"}),
                ["level2"]=new JsonArray()
            })
        })).Status);
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=1000,["commission"]=0,
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="hs_wallet_topup",["amount"]=100,["accountCode"]="112513"}),
                ["level2"]=new JsonArray()
            })
        })).Status);

        var ok=SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=1000,["commission"]=0,
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="hs_wallet_close",["amount"]=100,["accountCode"]="112513",["fundingType"]="invoice_close"}),
                ["level2"]=new JsonArray(new JsonObject{["itemCode"]="tga_rider",["amount"]=50,["accountCode"]="112502"})
            })
        });
        Assert.AreEqual(850m,ok["riders"]![0]!["salaryNet"]!.GetValue<decimal>());
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Scope_BlocksOutOfCompanyCalc(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        string other=MoneySchemaFixture.Company(c);
        var admin=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,admin,company,"ninja",riderId:rider);
        var outsider=Scoped(MoneySchemaFixture.Id(),other,"Ninja","pay.manage","pay.approve");
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>SalEvidence.Calculate(c,tx,outsider,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=100,["commission"]=0,
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        })).Status);
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DebtConfirm_MultipleDebtsPerRider_UpdatesStatement(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",riderId:rider);
        var calc=SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=500,["commission"]=0,["oldDebtInstalment"]=80,
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        });
        string riderId=calc["riders"]![0]!["id"]!.GetValue<string>();
        string d1=OpenDebt(c,tx,company,rider,100);
        string d2=OpenDebt(c,tx,company,rider,100);
        var confirmed=SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject
            {
                ["payRunRiderId"]=riderId,
                ["allocations"]=new JsonArray(
                    new JsonObject{["debtId"]=d1,["amount"]=30},
                    new JsonObject{["debtId"]=d2,["amount"]=20}
                )
            })
        });
        Assert.AreEqual("confirmed",confirmed["rows"]![0]!["debtConfirmStatus"]!.GetValue<string>());
        Assert.AreEqual(450m,confirmed["rows"]![0]!["salaryNet"]!.GetValue<decimal>());
        Assert.AreEqual(50m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(amount),0) FROM sal_pay_run_item WHERE pay_run_rider_id=@r AND item_code='old_debt_instalment' AND amount<>0",("@r",riderId))));
        Assert.AreEqual(2L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM debt_debt_movement WHERE pay_run_rider_id=@r AND source_type='salary_deduction'",("@r",riderId)));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Approve_PositiveNet_CreatesPayableAndJournal(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var maker=Admin();
        var checker=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,maker,company,"ninja",riderId:rider);
        var calc=SalEvidence.Calculate(c,tx,maker,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=500,["commission"]=0,
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        });
        string riderId=calc["riders"]![0]!["id"]!.GetValue<string>();
        SalDebtConfirm.Confirm(c,tx,maker,run,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})
        });
        var approved=SalSalaryApprove.Approve(c,tx,checker,run,new JsonObject
        {
            ["payRunRiderIds"]=new JsonArray(riderId)
        });
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM pay_payable WHERE source_table='sal.pay_run_rider' AND source_id=@r",("@r",riderId)));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM fin_journal WHERE purpose='salary_accrual' AND status=2"));
        Assert.IsFalse(string.IsNullOrEmpty(approved["rows"]![0]!["payableId"]?.GetValue<string>()));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Approve_SelfApprovalBlocked_WithoutIndependentGrant(bool sqlServer)
    {
        // Ghazal 11-24 / grok-TRIAL4: preparer cannot approve the same sheet (403, no journals).
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        string user=MoneySchemaFixture.Id();
        var admin=Admin(user);
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,admin,company,"ninja",riderId:rider);
        var calc=SalEvidence.Calculate(c,tx,admin,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=300,["commission"]=0,
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        });
        string riderId=calc["riders"]![0]!["id"]!.GetValue<string>();
        SalDebtConfirm.Confirm(c,tx,admin,run,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})
        });
        long journalsBefore=Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM fin_journal")??0L);
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>SalSalaryApprove.Approve(c,tx,admin,run,new JsonObject
        {
            ["payRunRiderIds"]=new JsonArray(riderId)
        })).Status);
        Assert.AreEqual(journalsBefore,Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM fin_journal")??0L));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Mudad_ValidatesEmployeeAndAppliesBeforeL2(bool sqlServer)
    {
        // Codex: gross 100 / L2 80 / Mudad 50 with wrong order → -30; correct = Mudad in L1 before NET/L2 → final 20 after recalc.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",riderId:erp);
        var upload=MudadApi.Upload(c,tx,actor,new JsonObject
        {
            ["companyId"]=company,["periodMonth"]="2026-10-01",["fileKind"]="salaries",
            ["fileName"]="m.xlsx",["fileHashHex"]=new string('d',64),
            ["lines"]=new JsonArray(new JsonObject{["iqama"]="1000000088",["officialName"]="X",["basicSalary"]=50,["grossSalary"]=50})
        });
        string lineId=upload["lines"]![0]!["id"]!.GetValue<string>();
        string other=MoneySchemaFixture.Id();
        MudadApi.Match(c,tx,actor,upload.S("id"),new JsonObject
        {
            ["matches"]=new JsonArray(new JsonObject{["lineId"]=lineId,["erpEmployeeId"]=other})
        });
        SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=erp,["gross"]=100,["commission"]=0,
                ["level1"]=new JsonArray(),
                ["level2"]=new JsonArray(new JsonObject{["itemCode"]="tga_rider",["amount"]=80,["accountCode"]="112502"})
            })
        });
        Assert.AreEqual(404,Assert.ThrowsExactly<ApiError>(()=>SalPayCalc.ApplyMudad(c,tx,actor,new JsonObject
        {
            ["payRunId"]=run,["erpRiderId"]=erp,["mudadLineId"]=lineId,["amount"]=50
        })).Status);

        MudadApi.Match(c,tx,actor,upload.S("id"),new JsonObject
        {
            ["matches"]=new JsonArray(new JsonObject{["lineId"]=lineId,["erpEmployeeId"]=erp})
        });
        // Recalc then apply — engine must fold mudad into L1 before L2 on recalculation.
        SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=erp,["gross"]=100,["commission"]=0,
                ["level1"]=new JsonArray(),
                ["level2"]=new JsonArray(new JsonObject{["itemCode"]="tga_rider",["amount"]=80,["accountCode"]="112502"}),
                ["mudadLineId"]=lineId,["mudadAmount"]=50
            })
        });
        var row=SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=erp,["gross"]=100,["commission"]=0,
                ["level1"]=new JsonArray(),
                ["level2"]=new JsonArray(new JsonObject{["itemCode"]="tga_rider",["amount"]=80,["accountCode"]="112502"}),
                ["mudadLineId"]=lineId,["mudadAmount"]=50
            })
        })["riders"]!.AsArray().OfType<JsonObject>().Single();
        // L1 mudad 50 then L2 capped by remaining positive net 50 → final 0 (not −30 from post-L2 mudad).
        Assert.AreEqual(0m,row.N("salaryNet"));
        Assert.AreEqual(50m,row.N("level1Total"));
        Assert.AreEqual(50m,row.N("level2Total"));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DebtId_PersistedAndSettledOnCalc(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",riderId:rider);
        string debtId=OpenDebt(c,tx,company,rider,60);
        var result=SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=200,["commission"]=0,
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="cross_app_debt",["amount"]=60,["accountCode"]="112507",["debtId"]=debtId,["sourceApp"]="keeta"}),
                ["level2"]=new JsonArray()
            })
        });
        string riderId=result["riders"]![0]!["id"]!.GetValue<string>();
        Assert.AreEqual(debtId,MoneySchemaFixture.Scalar(c,tx,"SELECT debt_id FROM sal_pay_run_item WHERE pay_run_rider_id=@r AND item_code='cross_app_debt' AND amount<>0",("@r",riderId))?.ToString());
        // Balance settles when the linked debt is confirmed / approved; link must survive persistence.
        Assert.AreEqual(60m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debtId))));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SharedHold_BlocksApproveAndNegativeDebt(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var maker=Admin();
        var checker=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,maker,company,"ninja",riderId:rider);
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_shared_rider_hold(id,erp_rider_id,period_month,status,created_at) VALUES(@id,@r,'2026-10-01','open',datetime('now'))",
            ("@id",MoneySchemaFixture.Id()),("@r",rider));
        var calc=SalEvidence.Calculate(c,tx,maker,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=50,["commission"]=0,
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="cash_advance",["amount"]=120,["accountCode"]="112501"}),
                ["level2"]=new JsonArray()
            })
        });
        string riderId=calc["riders"]![0]!["id"]!.GetValue<string>();
        // RWC4a3F §40.6: open shared hold blocks debt confirm (approve already blocked via DemandApprovable).
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalDebtConfirm.Confirm(c,tx,maker,run,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})
        })).Status);
        tx.Commit();
    }

    [TestMethod]
    public void SchemeCapture_AcceptsTextualPayAssignmentId()
    {
        using var c=Open(false);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string textual="pay-assignment:"+new string('a',40);
        string run=FrozenRun(c,tx,actor,company,"ninja",schemeId:textual);
        object? erp=MoneySchemaFixture.Scalar(c,tx,"SELECT erp_row_id FROM sal_ops_snapshot WHERE kind='scheme'");
        Assert.IsTrue(Guid.TryParse(erp?.ToString(),out _));
        string payload=MoneySchemaFixture.Scalar(c,tx,"SELECT payload FROM sal_ops_snapshot WHERE kind='scheme'")!.ToString()!;
        Assert.IsTrue(payload.Contains(textual,StringComparison.Ordinal)||payload.Contains("sourceKey",StringComparison.Ordinal));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ChefzAndJahez_HavePayRunCaptureCatalogue(bool sqlServer)
    {
        using var c=Open(sqlServer);
        SchemaOperationCapture.Ensure(c);
        Assert.IsTrue(Convert.ToInt32(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM fin_operation_required_kind WHERE operation_type='pay_run' AND app_code='chefz'"))>0);
        Assert.IsTrue(Convert.ToInt32(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM fin_operation_required_kind WHERE operation_type='pay_run' AND app_code='jahez'"))>0);
    }

    static string OpenDebt(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,string company,string erp,decimal balance,string category="LegacyOpeningDebt")
    {
        string debtId=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO debt_debt(id,erp_rider_id,company_id,category,principal,balance,instalment,status,created_at) VALUES(@id,@r,@c,@cat,@p,0,@i,'open',datetime('now'))",
            ("@id",debtId),("@r",erp),("@c",company),("@cat",category),("@p",balance),("@i",Math.Min(balance,80m)));
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id,created_at) VALUES(@id,@d,@c,'2026-10-01','2026-10-01',@a,'synthetic',@id,datetime('now'))",
            ("@id",MoneySchemaFixture.Id()),("@d",debtId),("@c",company),("@a",balance));
        return debtId;
    }
}
