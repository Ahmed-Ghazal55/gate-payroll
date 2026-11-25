using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>RWC4a3F: Codex RWC4a3 open rows — H11 debt-after-consolidate 240; H11 combined zero/negative; New HIGH per-account shared reverse. Exact SAR; failing test first.</summary>
[TestClass]
public sealed class SalRwc4a3FixesTests
{
    static ClaimsPrincipal Admin(string? name=null)
    {
        var id=new ClaimsIdentity("synthetic");
        id.AddClaim(new Claim(ClaimTypes.Name,name??MoneySchemaFixture.Id()));
        id.AddClaim(new Claim(ClaimTypes.Role,"Admin"));
        id.AddClaim(new Claim("permission","pay.manage"));
        id.AddClaim(new Claim("permission","pay.approve"));
        id.AddClaim(new Claim("permission",ApprovalsArea.DualSharedRiderRelease));
        id.AddClaim(new Claim("permission","approvals.decide"));
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
        SchemaAuthCatalog.Ensure(c);SchemaApprovals.Ensure(c);
        return c;
    }

    static readonly string[] AllKinds=["ninja_daily","ninja_daily_deduction","platform_account","assignment","housing","fuel","vehicle","scheme","eligibility",
        "keeta_invoice_row","hs_ops","hs_wallet","platform_ops","chefz_settlement","jahez_day"];

    static string FrozenRun(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,string company,string app,
        string rider,string? account=null,string scheme="commission",Action<FakeOpsLiveFactsReader,string>? customize=null)
    {
        string run=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month) VALUES(@id,@c,@app,'2026-10-01')",("@id",run),("@c",company),("@app",app));
        var facts=new FakeOpsLiveFactsReader();
        foreach(string kind in AllKinds)facts.Rows[kind]=[];
        string acc=account??MoneySchemaFixture.Id();
        string primary=app switch{"ninja"=>"ninja_daily","keeta"=>"keeta_invoice_row","hs"=>"hs_ops","amazon"=>"platform_ops","chefz"=>"chefz_settlement","jahez"=>"jahez_day",_=>"assignment"};
        facts.Rows[primary]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["erpPlatformAccountId"]=acc,["riderProfileId"]=rider,["erpRiderId"]=rider,["orders"]=1,["reportDate"]="2026-10-01",["workDate"]="2026-10-01",["gross"]=100,["RiderAmount"]=800}];
        facts.Rows["platform_account"]=[new(){["id"]=acc,["externalAccountId"]="A1",["code"]="A1"}];
        facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["actualRiderProfileId"]=rider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31",["appCode"]=app}];
        facts.Rows["scheme"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["schemeCode"]=scheme,["source"]="pay_settings"}];
        facts.Rows["eligibility"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["eligible"]=true,["source"]="pay_settings"}];
        customize?.Invoke(facts,acc);
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

    static JsonObject Rider(string erp,JsonArray? l1=null,JsonArray? l2=null)
        =>new(){["erpRiderId"]=erp,["level1"]=l1??new JsonArray(),["level2"]=l2??new JsonArray()};

    static JsonObject Calc(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,string run,JsonObject rider)
        =>SalPayCalc.Calculate(c,tx,actor,run,new JsonObject{["riders"]=new JsonArray(rider)})["riders"]!.AsArray().OfType<JsonObject>().Single();

    static JsonArray SharedFuel50()
        =>new(new JsonObject{["itemCode"]="fuel",["amount"]=50,["accountCode"]="112511"});

    static JsonArray MixedFuel20Vehicle30()
        =>new(
            new JsonObject{["itemCode"]="fuel",["amount"]=20,["accountCode"]="112511"},
            new JsonObject{["itemCode"]="vehicle",["amount"]=30,["accountCode"]="421101"});

    // ---------------- H11: debt 60 after consolidate → pay 240 ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_ConfirmDebt60_AfterConsolidate_PayablesSumCombinedNet240(bool sqlServer)
    {
        // Same shared-fuel setup as RWC4a2: nets 150+100, combined 300.
        // Confirm old-debt 60 after consolidate → combined must become 240; approve both pays 240 (§40.5 / §40.6).
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string ninja=FrozenRun(c,tx,actor,company,"ninja",rider,customize:(f,acc)=>
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-10",["effectiveTo"]="2026-10-31",["appCode"]="keeta"}));
        string keeta=FrozenRun(c,tx,actor,company,"keeta",rider,customize:(f,acc)=>
        {
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=150,["deductions"]=0,["orders"]=15}];
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-09",["appCode"]="ninja"});
        });
        SalEvidence.Gross(c,tx,ninja,rider,200m);
        string ninjaRiderId=Calc(c,tx,actor,ninja,Rider(rider,SharedFuel50())).S("id");
        string keetaRiderId=Calc(c,tx,actor,keeta,Rider(rider,SharedFuel50())).S("id");
        Assert.AreEqual(300m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        string debtId=OpenDebt(c,tx,company,rider,60);
        SalDebtConfirm.Confirm(c,tx,actor,ninja,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=ninjaRiderId,["debtId"]=debtId,["amount"]=60})});
        Assert.AreEqual(240m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        SalDebtConfirm.Confirm(c,tx,actor,keeta,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=keetaRiderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,Admin(),ninja,new JsonObject{["payRunRiderIds"]=new JsonArray(ninjaRiderId)});
        SalSalaryApprove.Approve(c,tx,Admin(),keeta,new JsonObject{["payRunRiderIds"]=new JsonArray(keetaRiderId)});
        decimal ninjaPay=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(amount_due),0) FROM pay_payable WHERE source_id=@r",("@r",ninjaRiderId)));
        decimal keetaPay=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(amount_due),0) FROM pay_payable WHERE source_id=@r",("@r",keetaRiderId)));
        Assert.AreEqual(240m,J.Money(ninjaPay+keetaPay));
        tx.Commit();
    }

    // ---------------- H11: combined zero +100/−100 → pay 0; debt from combined (RWC4m expects 0) ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_CombinedZero_Plus100Minus100_Pays0_Debt0(bool sqlServer)
    {
        // Ninja net +100, Keeta net −100 → combined 0. Payables 0; carryover from combined shortfall = 0 (RWC4m / §40.5).
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string ninja=FrozenRun(c,tx,actor,company,"ninja",rider,customize:(f,acc)=>
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-10",["effectiveTo"]="2026-10-31",["appCode"]="keeta"}));
        string keeta=FrozenRun(c,tx,actor,company,"keeta",rider,customize:(f,acc)=>
        {
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=100,["deductions"]=0,["orders"]=10}];
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-09",["appCode"]="ninja"});
        });
        SalEvidence.Gross(c,tx,ninja,rider,100m);
        string ninjaRiderId=Calc(c,tx,actor,ninja,Rider(rider)).S("id");
        string keetaRiderId=Calc(c,tx,actor,keeta,Rider(rider,new JsonArray(new JsonObject{["itemCode"]="cash_advance",["amount"]=200,["accountCode"]="112501"}))).S("id");
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        Assert.AreEqual(-100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT salary_net FROM sal_pay_run_rider WHERE id=@id",("@id",keetaRiderId))));
        SalDebtConfirm.Confirm(c,tx,actor,ninja,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=ninjaRiderId,["amount"]=0})});
        SalDebtConfirm.Confirm(c,tx,actor,keeta,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=keetaRiderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,Admin(),ninja,new JsonObject{["payRunRiderIds"]=new JsonArray(ninjaRiderId)});
        SalSalaryApprove.Approve(c,tx,Admin(),keeta,new JsonObject{["payRunRiderIds"]=new JsonArray(keetaRiderId)});
        decimal totalPay=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(amount_due),0) FROM pay_payable WHERE erp_rider_id=@r",("@r",rider)));
        Assert.AreEqual(0m,totalPay);
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(balance),0) FROM debt_debt WHERE erp_rider_id=@r AND category='NegativeSalaryCarryover'",("@r",rider))));
        tx.Commit();
    }

    // ---------------- New HIGH: reverse each shared deduction on its own account ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NewHigh_SharedFuel20Vehicle30_SharedOnceDebitsEachAccount(bool sqlServer)
    {
        // Duplicate fuel 20 + vehicle 30 across apps → boost 50 must Dr 112511=20 and Dr 421101=30 (not fuel 50).
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string ninja=FrozenRun(c,tx,actor,company,"ninja",rider,customize:(f,acc)=>
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-10",["effectiveTo"]="2026-10-31",["appCode"]="keeta"}));
        string keeta=FrozenRun(c,tx,actor,company,"keeta",rider,customize:(f,acc)=>
        {
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=150,["deductions"]=0,["orders"]=15}];
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-09",["appCode"]="ninja"});
        });
        SalEvidence.Gross(c,tx,ninja,rider,200m);
        string ninjaRiderId=Calc(c,tx,actor,ninja,Rider(rider,MixedFuel20Vehicle30())).S("id");
        string keetaRiderId=Calc(c,tx,actor,keeta,Rider(rider,MixedFuel20Vehicle30())).S("id");
        Assert.AreEqual(300m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        SalDebtConfirm.Confirm(c,tx,actor,ninja,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=ninjaRiderId,["amount"]=0})});
        SalDebtConfirm.Confirm(c,tx,actor,keeta,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=keetaRiderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,Admin(),ninja,new JsonObject{["payRunRiderIds"]=new JsonArray(ninjaRiderId)});
        SalSalaryApprove.Approve(c,tx,Admin(),keeta,new JsonObject{["payRunRiderIds"]=new JsonArray(keetaRiderId)});
        Assert.AreEqual(20m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(debit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='112511') AND journal_id IN (SELECT id FROM fin_journal WHERE purpose='salary_shared_once')")));
        Assert.AreEqual(30m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(debit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='421101') AND journal_id IN (SELECT id FROM fin_journal WHERE purpose='salary_shared_once')")));
        Assert.AreEqual(50m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(credit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='212103') AND journal_id IN (SELECT id FROM fin_journal WHERE purpose='salary_shared_once')")));
        tx.Commit();
    }
}
