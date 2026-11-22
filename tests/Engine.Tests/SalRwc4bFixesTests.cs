using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>RWC4bF: Codex RWC4b remaining HIGH/MEDIUM — failing-test-first, exact SAR.</summary>
[TestClass]
public sealed class SalRwc4bFixesTests
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
        string? accountId=null,string? riderId=null,Action<FakeOpsLiveFactsReader,string,string>? customize=null)
    {
        string run=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month) VALUES(@id,@c,@app,'2026-10-01')",("@id",run),("@c",company),("@app",app));
        var facts=new FakeOpsLiveFactsReader();
        foreach(string kind in new[]{"ninja_daily","ninja_daily_deduction","platform_account","assignment","housing","fuel","vehicle","scheme","eligibility",
            "keeta_invoice_row","hs_ops","hs_wallet","platform_ops","chefz_settlement","jahez_day"})
            facts.Rows[kind]=[];
        string account=accountId??MoneySchemaFixture.Id(),rider=riderId??MoneySchemaFixture.Id();
        string primary=app switch{"ninja"=>"ninja_daily","keeta"=>"keeta_invoice_row","hs"=>"hs_ops","amazon"=>"platform_ops","chefz"=>"chefz_settlement","jahez"=>"jahez_day",_=>"assignment"};
        facts.Rows[primary]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["riderProfileId"]=rider,["erpRiderId"]=rider,["orders"]=1,["reportDate"]="2026-10-01",["RiderAmount"]=800,["gross"]=800,["net"]=600,["commission"]=50,["workDate"]="2026-10-01"}];
        if(app=="ninja")facts.Rows["ninja_daily_deduction"]=[];
        facts.Rows["platform_account"]=[new(){["id"]=account,["externalAccountId"]="A1",["code"]="A1"}];
        facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=rider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31"}];
        facts.Rows["scheme"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["schemeCode"]="commission",["source"]="pay_settings"}];
        facts.Rows["eligibility"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["eligible"]=true,["source"]="pay_settings"}];
        if(app=="hs")facts.Rows["hs_wallet"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["balance"]=100,["asOf"]="2026-10-31",["fundingType"]="custody",["amount"]=100,["eventType"]="topup"}];
        if(app=="chefz")facts.Rows["chefz_settlement"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["RiderAmount"]=800,["GrossEarnings"]=1000,["CompanyAmount"]=200}];
        if(app=="keeta")
        {
            facts.Rows["scheme"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["schemeCode"]="salary",["source"]="pay_settings"}];
            facts.Rows["eligibility"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["eligible"]=false,["source"]="keeta_eligibility_view"}];
            facts.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=account,["erpRiderId"]=rider,["gross"]=100,["deductions"]=0,["orders"]=10}];
        }
        customize?.Invoke(facts,account,rider);
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

    static string OpenDebt(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,string company,string erp,decimal balance,string category="LegacyOpeningDebt")
    {
        string debtId=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO debt_debt(id,erp_rider_id,company_id,category,principal,balance,instalment,status,created_at) VALUES(@id,@r,@c,@cat,@p,0,@i,'open',datetime('now'))",
            ("@id",debtId),("@r",erp),("@c",company),("@cat",category),("@p",balance),("@i",Math.Min(balance,80m)));
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id,created_at) VALUES(@id,@d,@c,'2026-10-01','2026-10-01',@a,'synthetic',@id,datetime('now'))",
            ("@id",MoneySchemaFixture.Id()),("@d",debtId),("@c",company),("@a",balance));
        return debtId;
    }

    // --- H1: calc binds scheme/eligibility/gross from frozen evidence ---
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H1_Calc_UsesFrozenSchemeEligibilityAndRejectsConflict(bool sqlServer)
    {
        // Frozen scheme=salary, eligibility=false → keeta gross 100 from frozen invoice; caller scheme commission / eligible true → 409.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"keeta",riderId:rider);
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=999,["commission"]=0,["schemeType"]="commission",["keetaIneligible"]=false,
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        })).Status);

        var ok=SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider, // gross/scheme/eligibility omitted → derived from frozen
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        });
        var row=ok["riders"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual(100m,row.N("gross"));
        Assert.AreEqual("salary",row.S("schemeType"));
        Assert.IsTrue(row.B("keetaIneligible"));
        Assert.AreEqual(100m,row.N("salaryNet"));
        tx.Commit();
    }

    // --- H3: accrual posts entitlement 500; debt recovery 50 already on payable → ledger liability 450 ---
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H3_Approve_PostsEntitlement500_LedgerPayable450_AfterDebt50(bool sqlServer)
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
                ["erpRiderId"]=rider,["gross"]=500,["commission"]=0,
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        });
        string riderId=calc["riders"]![0]!["id"]!.GetValue<string>();
        string debtId=OpenDebt(c,tx,company,rider,50);
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject
            {
                ["payRunRiderId"]=riderId,
                ["allocations"]=new JsonArray(new JsonObject{["debtId"]=debtId,["amount"]=50})
            })
        });
        SalSalaryApprove.Approve(c,tx,actor,run,new JsonObject{["payRunRiderIds"]=new JsonArray(riderId)});
        Assert.AreEqual(450m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount_due FROM pay_payable WHERE source_id=@r",("@r",riderId))));
        // Accrual credits payable for entitlement 500; debt confirm already debited 50 → net credit 450.
        Assert.AreEqual(500m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(credit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='212103') AND journal_id IN (SELECT id FROM fin_journal WHERE purpose='salary_accrual')")));
        Assert.AreEqual(50m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(debit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='212103') AND journal_id IN (SELECT id FROM fin_journal WHERE purpose='debt_confirm')")));
        decimal netLiability=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(credit-debit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='212103')"));
        Assert.AreEqual(450m,netLiability);
        tx.Commit();
    }

    // --- H4: Jahez live capture kind is mapped (not silent empty stub) ---
    [TestMethod]
    public void H4_LiveReader_MapsJahezDay_ToDailyOrder()
    {
        Assert.AreEqual("jahez_daily_order",LiveOpsLiveFactsReader.MapCaptureKind("jahez_day"));
        Assert.AreNotEqual("",LiveOpsLiveFactsReader.MapCaptureKind("jahez_day"));
    }

    // --- H8: operating recovered before personal regardless of caller order ---
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H8_Keeta_OperatingBeforePersonal_ExactNumbers(bool sqlServer)
    {
        // §22.9.2 journal order: D/fuel (operating) before personal. Gross 100; personal 60 then fuel 80 in request → recover fuel 100 first? wages 100:
        // operating fuel 80 recover 80; personal 60 recover 20 residual 40; net 0.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"keeta",riderId:rider);
        var result=SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=100,["commission"]=0,["schemeType"]="salary",["keetaIneligible"]=true,
                ["level1"]=new JsonArray(
                    new JsonObject{["itemCode"]="cash_advance",["amount"]=60,["accountCode"]="112501",["operating"]=false},
                    new JsonObject{["itemCode"]="fuel",["amount"]=80,["accountCode"]="112511",["operating"]=true}
                ),
                ["level2"]=new JsonArray()
            })
        });
        var row=result["riders"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual(0m,row.N("salaryNet"));
        Assert.AreEqual(100m,row.N("level1Total")); // 80 fuel + 20 personal
        Assert.AreEqual(40m,row.N("personalResidualDebt"));
        Assert.AreEqual(0m,row.N("companyOperatingTotal")); // fuel fully covered by wages
        tx.Commit();
    }

    // --- H9: Chefz requires frozen settlement provenance ---
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H9_Chefz_RequiresFrozenSettlement_NotCallerTwin(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        // Frozen settlement RiderAmount=800; caller twin 900/900 must still fail provenance.
        string run=FrozenRun(c,tx,actor,company,"chefz",riderId:rider);
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=900,["riderAmount"]=900,["commission"]=0,
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        })).Status);
        var ok=SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=800,["riderAmount"]=800,["commission"]=0,
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="fuel",["amount"]=40,["accountCode"]="112511"}),
                ["level2"]=new JsonArray()
            })
        });
        Assert.AreEqual(760m,ok["riders"]![0]!["salaryNet"]!.GetValue<decimal>());
        tx.Commit();
    }

    // --- H10: HS wallet needs stamped frozen evidence; close/refund offsets ---
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H10_Hs_WalletRequiresFrozenEvidence_AndNetsCloseRefund(bool sqlServer)
    {
        // Custody top-up 300 stamped; close refund/offset 250 same holder → net L1 wallet 50; gross 1000 − 50 = 950.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"hs",null,rider,(facts,_,r)=>
        {
            facts.Rows["hs_wallet"]=
            [
                new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=r,["balance"]=300,["asOf"]="2026-10-15",["fundingType"]="custody",["amount"]=300,["eventType"]="topup"},
                new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=r,["balance"]=0,["asOf"]="2026-10-20",["fundingType"]="invoice_close",["amount"]=250,["eventType"]="close_refund",["offsetsTopup"]=true}
            ];
        });
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=1000,["commission"]=0,
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="hs_wallet_topup",["amount"]=99,["accountCode"]="112501",["fundingType"]="custody"}),
                ["level2"]=new JsonArray()
            })
        })).Status);

        var ok=SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=1000,["commission"]=0,
                ["level1"]=new JsonArray(
                    new JsonObject{["itemCode"]="hs_wallet_topup",["amount"]=300,["accountCode"]="112501",["fundingType"]="custody"},
                    new JsonObject{["itemCode"]="hs_wallet_close",["amount"]=250,["accountCode"]="112513",["fundingType"]="invoice_close",["eventType"]="close_refund"}
                ),
                ["level2"]=new JsonArray()
            })
        });
        Assert.AreEqual(950m,ok["riders"]![0]!["salaryNet"]!.GetValue<decimal>());
        Assert.AreEqual(50m,ok["riders"]![0]!["level1Total"]!.GetValue<decimal>());
        tx.Commit();
    }

    // --- H11: detect multi-app and open shared hold ---
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_DetectSharedRider_CreatesHold_BlocksApprove(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string ninja=FrozenRun(c,tx,actor,company,"ninja",riderId:rider);
        string keeta=FrozenRun(c,tx,actor,company,"keeta",riderId:rider);
        SalEvidence.Calculate(c,tx,actor,keeta,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject{["erpRiderId"]=rider,["level1"]=new JsonArray(),["level2"]=new JsonArray()})
        });
        var calc=SalEvidence.Calculate(c,tx,actor,ninja,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=200,["commission"]=0,
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        });
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND period_month='2026-10-01' AND status='open'",("@r",rider)));
        Assert.IsTrue(Convert.ToInt32(MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_shared_rider_hold_app ha INNER JOIN sal_shared_rider_hold h ON h.id=ha.hold_id WHERE h.erp_rider_id=@r",("@r",rider)))>=2);
        string riderId=calc["riders"]![0]!["id"]!.GetValue<string>();
        SalDebtConfirm.Confirm(c,tx,actor,ninja,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})});
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalSalaryApprove.Approve(c,tx,actor,ninja,new JsonObject{["payRunRiderIds"]=new JsonArray(riderId)})).Status);
        tx.Commit();
    }

    // --- H12: approval settles linked debt_id items ---
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H12_Approve_SettlesLinkedDebtIds(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",riderId:rider);
        string debtId=OpenDebt(c,tx,company,rider,60);
        var calc=SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["gross"]=200,["commission"]=0,
                ["level1"]=new JsonArray(new JsonObject{["itemCode"]="cross_app_debt",["amount"]=60,["accountCode"]="112507",["debtId"]=debtId}),
                ["level2"]=new JsonArray()
            })
        });
        string riderId=calc["riders"]![0]!["id"]!.GetValue<string>();
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,actor,run,new JsonObject{["payRunRiderIds"]=new JsonArray(riderId)});
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debtId))));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM debt_debt_movement WHERE debt_id=@d AND source_type='pay_item_debt_settle'",("@d",debtId)));
        tx.Commit();
    }

    // --- H13: ApplyMudad endpoint reorders atomically → final 0 not -30 ---
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H13_ApplyMudad_AtomicReorder_FinalZeroNotMinus30(bool sqlServer)
    {
        // gross 100 / L2 80 → net 20; mudad 50 applied after must become L1 before L2 → final 0 (not 20-50=-30).
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",riderId:erp);
        var upload=MudadApi.Upload(c,tx,actor,new JsonObject
        {
            ["companyId"]=company,["periodMonth"]="2026-10-01",["fileKind"]="salaries",
            ["fileName"]="m.xlsx",["fileHashHex"]=new string('e',64),
            ["lines"]=new JsonArray(new JsonObject{["iqama"]="1000000099",["officialName"]="Y",["basicSalary"]=50,["grossSalary"]=50})
        });
        string lineId=upload["lines"]![0]!["id"]!.GetValue<string>();
        MudadApi.Match(c,tx,actor,upload.S("id"),new JsonObject
        {
            ["matches"]=new JsonArray(new JsonObject{["lineId"]=lineId,["erpEmployeeId"]=erp})
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
        var applied=SalPayCalc.ApplyMudad(c,tx,actor,new JsonObject
        {
            ["payRunId"]=run,["erpRiderId"]=erp,["mudadLineId"]=lineId,["amount"]=50
        });
        Assert.AreEqual(0m,applied.N("salaryNet"));
        Assert.AreEqual(50m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount FROM sal_pay_run_item WHERE pay_run_rider_id=(SELECT id FROM sal_pay_run_rider WHERE pay_run_id=@run AND erp_rider_id=@erp) AND item_code='mudad_salary' AND amount<>0",("@run",run),("@erp",erp))));
        Assert.AreEqual(50m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount FROM sal_pay_run_item WHERE pay_run_rider_id=(SELECT id FROM sal_pay_run_rider WHERE pay_run_id=@run AND erp_rider_id=@erp) AND item_code='tga_rider' AND amount<>0",("@run",run),("@erp",erp))));
        tx.Commit();
    }

    // --- M1: each allocation keeps its receivable account on statement ---
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void M1_DebtConfirm_RetainsAllocationAccountCodes(bool sqlServer)
    {
        // LegacyOpeningDebt → 112505 (not hardcoded 112507). Confirmed 50 replaces proposal.
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
        string debtId=OpenDebt(c,tx,company,rider,100,"LegacyOpeningDebt");
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject
            {
                ["payRunRiderId"]=riderId,
                ["allocations"]=new JsonArray(new JsonObject{["debtId"]=debtId,["amount"]=50})
            })
        });
        Assert.AreEqual("112505",MoneySchemaFixture.Scalar(c,tx,"SELECT account_code FROM sal_pay_run_item WHERE pay_run_rider_id=@r AND item_code='old_debt_instalment' AND amount<>0",("@r",riderId))?.ToString());
        Assert.AreEqual(50m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount FROM sal_pay_run_item WHERE pay_run_rider_id=@r AND item_code='old_debt_instalment' AND amount<>0",("@r",riderId))));
        tx.Commit();
    }

    // --- New HIGH: next month instalment uses allocation-specific journal source ---
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NewHigh_RecurringInstalment_UniqueJournalSourcePerAllocation(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) SELECT @id,@c,'2026-11-01',1,datetime('now') WHERE NOT EXISTS(SELECT 1 FROM fin_accounting_period WHERE company_id=@c AND period_month='2026-11-01')",
            ("@id",MoneySchemaFixture.Id()),("@c",company));
        string rider=MoneySchemaFixture.Id();
        string debtId=OpenDebt(c,tx,company,rider,200);
        string runOct=FrozenRun(c,tx,actor,company,"ninja",riderId:rider);
        var calcOct=SalEvidence.Calculate(c,tx,actor,runOct,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject{["erpRiderId"]=rider,["gross"]=300,["commission"]=0,["level1"]=new JsonArray(),["level2"]=new JsonArray()})
        });
        string riderOct=calcOct["riders"]![0]!["id"]!.GetValue<string>();
        SalDebtConfirm.Confirm(c,tx,actor,runOct,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderOct,["allocations"]=new JsonArray(new JsonObject{["debtId"]=debtId,["amount"]=40})})
        });

        // Second month: new pay-run rider + confirm same debt again; journal source must not reuse debt id.
        string runNov=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month) VALUES(@id,@c,'ninja','2026-11-01')",("@id",runNov),("@c",company));
        MoneySchemaFixture.Exec(c,tx,"UPDATE sal_pay_run SET status=4 WHERE id=@id",("@id",runOct)); // free active unique for same company/app if needed — Nov is different month
        var facts=new FakeOpsLiveFactsReader();
        foreach(string kind in new[]{"ninja_daily","ninja_daily_deduction","platform_account","assignment","housing","fuel","vehicle","scheme","eligibility",
            "keeta_invoice_row","hs_ops","hs_wallet","platform_ops","chefz_settlement","jahez_day"})
            facts.Rows[kind]=[];
        string account=MoneySchemaFixture.Id();
        facts.Rows["ninja_daily"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["riderProfileId"]=rider,["erpRiderId"]=rider,["orders"]=1,["reportDate"]="2026-11-01"}];
        facts.Rows["platform_account"]=[new(){["id"]=account,["externalAccountId"]="A1",["code"]="A1"}];
        facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=rider,["effectiveFrom"]="2026-11-01",["effectiveTo"]="2026-11-30"}];
        facts.Rows["scheme"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["schemeCode"]="commission",["source"]="pay_settings"}];
        facts.Rows["eligibility"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["eligible"]=true,["source"]="pay_settings"}];
        var reasons=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var kv in facts.Rows)if(kv.Value.Count==0)reasons[kv.Key]="none";
        OpsSnapshotCapture.Capture(c,tx,actor,runNov,facts,reasons);
        OpsSnapshotCapture.Freeze(c,tx,actor,runNov);

        var calcNov=SalEvidence.Calculate(c,tx,actor,runNov,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject{["erpRiderId"]=rider,["gross"]=300,["commission"]=0,["level1"]=new JsonArray(),["level2"]=new JsonArray()})
        });
        string riderNov=calcNov["riders"]![0]!["id"]!.GetValue<string>();
        var confirmed=SalDebtConfirm.Confirm(c,tx,actor,runNov,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderNov,["allocations"]=new JsonArray(new JsonObject{["debtId"]=debtId,["amount"]=40})})
        });
        Assert.AreEqual("confirmed",confirmed["rows"]![0]!["debtConfirmStatus"]!.GetValue<string>());
        Assert.AreEqual(2L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM fin_journal WHERE purpose='debt_confirm' AND company_id=@c",("@c",company)));
        object? srcOct=MoneySchemaFixture.Scalar(c,tx,"SELECT source_id FROM fin_journal WHERE purpose='debt_confirm' AND period_month='2026-10-01'");
        object? srcNov=MoneySchemaFixture.Scalar(c,tx,"SELECT source_id FROM fin_journal WHERE purpose='debt_confirm' AND period_month='2026-11-01'");
        Assert.AreNotEqual(srcOct?.ToString(),srcNov?.ToString());
        Assert.AreNotEqual(debtId,srcNov?.ToString());
        tx.Commit();
    }
}
