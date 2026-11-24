using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>WC4D: Codex RWC4c open rows (H1, H3, H4/H9, H10, H11, H12, H13) — failing test first, exact SAR.</summary>
[TestClass]
public sealed class SalWc4dFixesTests
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

    static readonly string[] AllKinds=["ninja_daily","ninja_daily_deduction","platform_account","assignment","housing","fuel","vehicle","scheme","eligibility",
        "keeta_invoice_row","hs_ops","hs_wallet","platform_ops","chefz_settlement","jahez_day"];

    /// <summary>Frozen run with one rider/account; customize may replace any kind's rows (live-shaped rows included).</summary>
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
            ("@id",debtId),("@r",erp),("@c",company),("@cat",category),("@p",balance),("@i",balance));
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id,created_at) VALUES(@id,@d,@c,'2026-10-01','2026-10-01',@a,'synthetic',@id,datetime('now'))",
            ("@id",MoneySchemaFixture.Id()),("@d",debtId),("@c",company),("@a",balance));
        return debtId;
    }

    static decimal Liability212103(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx)
        =>Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(credit-debit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='212103')"));

    static JsonObject Rider(string erp,JsonArray? l1=null,JsonArray? l2=null)=>new()
    {
        ["erpRiderId"]=erp,["level1"]=l1??new JsonArray(),["level2"]=l2??new JsonArray()
    };

    static JsonObject Calc(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,string run,JsonObject rider)
        =>SalPayCalc.Calculate(c,tx,actor,run,new JsonObject{["riders"]=new JsonArray(rider)})["riders"]!.AsArray().OfType<JsonObject>().Single();

    // ---------------- H1 ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H1_Keeta_OneInvoiceRowNeverServesTwoRiders(bool sqlServer)
    {
        // One Keeta invoice row (gross 100) on account A; frozen assignment puts riders R1 and R2 on A → no silent 100 for each.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string r1=MoneySchemaFixture.Id(),r2=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"keeta",r1,customize:(f,acc)=>
        {
            f.Rows["assignment"]=
            [
                new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["actualRiderProfileId"]=r1,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-15"},
                new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["actualRiderProfileId"]=r2,["effectiveFrom"]="2026-10-16",["effectiveTo"]="2026-10-31"}
            ];
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=100,["deductions"]=0,["orders"]=10}];
            f.Rows["scheme"]=[];f.Rows["eligibility"]=[];
        });
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Calc(c,tx,actor,run,Rider(r1))).Status);
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Calc(c,tx,actor,run,Rider(r2))).Status);
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H1_Keeta_EachRiderGetsHisOwnAccountRow_100And250(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string r1=MoneySchemaFixture.Id(),r2=MoneySchemaFixture.Id(),a2=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"keeta",r1,customize:(f,acc)=>
        {
            f.Rows["assignment"]=
            [
                new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["actualRiderProfileId"]=r1,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31"},
                new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=a2,["actualRiderProfileId"]=r2,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31"}
            ];
            f.Rows["keeta_invoice_row"]=
            [
                new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=100,["deductions"]=0,["orders"]=10},
                new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=a2,["gross"]=250,["deductions"]=0,["orders"]=25}
            ];
            f.Rows["scheme"]=[];f.Rows["eligibility"]=[];
        });
        Assert.AreEqual(100m,Calc(c,tx,actor,run,Rider(r1)).N("gross"));
        Assert.AreEqual(250m,Calc(c,tx,actor,run,Rider(r2)).N("salaryNet"));
        // Caller gross that differs from the rider's own row is rejected.
        var forged=Rider(r2);forged["gross"]=100;
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Calc(c,tx,actor,run,forged)).Status);
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H1_Ninja_GrossOnlyFromRiderDistribution_Never_Caller(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",rider);
        var caller=Rider(rider);caller["gross"]=500;
        // No per-rider evidence: caller gross 500 is not money.
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Calc(c,tx,actor,run,caller)).Status);
        SalEvidence.Gross(c,tx,run,rider,500m);
        Assert.AreEqual(500m,Calc(c,tx,actor,run,Rider(rider)).N("salaryNet"));
        var forged=Rider(rider);forged["gross"]=999;
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Calc(c,tx,actor,run,forged)).Status);
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H1_Chefz_CallerCommissionRejected_RiderAmountAlreadyNet(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"chefz",rider,customize:(f,acc)=>
            f.Rows["chefz_settlement"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["RiderAmount"]=800,["GrossEarnings"]=1000,["CompanyAmount"]=200}]);
        var forged=Rider(rider);forged["commission"]=50;
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Calc(c,tx,actor,run,forged)).Status);
        var row=Calc(c,tx,actor,run,Rider(rider));
        Assert.AreEqual(800m,row.N("salaryNet"));
        Assert.AreEqual(0m,row.N("commission"));
        tx.Commit();
    }

    // ---------------- H3 ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H3_NetZeroAfterDebt500_StillAccrues500_LiabilityZero(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",rider);
        SalEvidence.Gross(c,tx,run,rider,500m);
        string riderId=Calc(c,tx,actor,run,Rider(rider)).S("id");
        string debtId=OpenDebt(c,tx,company,rider,500);
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject
        {
            ["payRunRiderId"]=riderId,["allocations"]=new JsonArray(new JsonObject{["debtId"]=debtId,["amount"]=500})
        })});
        var approved=SalSalaryApprove.Approve(c,tx,Admin(),run,new JsonObject{["payRunRiderIds"]=new JsonArray(riderId)});
        Assert.AreEqual(0m,approved["rows"]![0]!.AsObject().N("salaryNet"));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM pay_payable WHERE source_id=@r",("@r",riderId)));
        Assert.AreEqual(500m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(credit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='212103') AND journal_id IN (SELECT id FROM fin_journal WHERE purpose='salary_accrual')")));
        Assert.AreEqual(0m,Liability212103(c,tx));
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debtId))));
        tx.Commit();
    }

    // ---------------- H4 / H9 ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H4_JahezCapture_KeepsRiderAndAccount_FromFrozenAssignment(bool sqlServer)
    {
        // Live jahez_daily_order shape: companyId, platformAccountId, date, count (no rider). Rider resolved from the run's assignment.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string r1=MoneySchemaFixture.Id(),r2=MoneySchemaFixture.Id();
        string account="";
        string run=FrozenRun(c,tx,actor,company,"jahez",r1,customize:(f,acc)=>
        {
            account=acc;
            f.Rows["assignment"]=
            [
                new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["actualRiderProfileId"]=r1,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-10"},
                new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["actualRiderProfileId"]=r2,["effectiveFrom"]="2026-10-11",["effectiveTo"]="2026-10-31"}
            ];
            f.Rows["jahez_day"]=[new(){["id"]=MoneySchemaFixture.Id(),["companyId"]=company,["platformAccountId"]=acc,["date"]="2026-10-05",["count"]=9}];
        });
        var payload=JsonNode.Parse(MoneySchemaFixture.Scalar(c,tx,"SELECT payload FROM sal_ops_snapshot s WHERE kind='jahez_day' AND id IN (SELECT snapshot_id FROM fin_operation_snapshot_link WHERE operation_id=@run)",("@run",run))!.ToString()!)!.AsObject();
        Assert.AreEqual(r1,payload.S("erpRiderId"));
        Assert.AreEqual(account,payload.S("platformRiderAccountId"));
        Assert.AreEqual(9m,payload.N("orders"));
        Assert.IsFalse(payload.ContainsKey("net"));
        Assert.IsFalse(payload.ContainsKey("commission"));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H9_ChefzLiveFieldNames_NormalisedBeforeAllowlist_Calc800(bool sqlServer)
    {
        // Operations.Project("chefz_settlement") shape: riderId, platformAccountId, gross, companyAmount, riderAmount.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"chefz",rider,customize:(f,acc)=>
            f.Rows["chefz_settlement"]=[new(){["id"]=MoneySchemaFixture.Id(),["riderId"]=rider,["companyId"]=company,["platformAccountId"]=acc,
                ["periodFrom"]="2026-10-01",["periodTo"]="2026-10-31",["gross"]=1000,["companyAmount"]=200,["riderAmount"]=800}]);
        var payload=JsonNode.Parse(MoneySchemaFixture.Scalar(c,tx,"SELECT payload FROM sal_ops_snapshot s WHERE kind='chefz_settlement' AND id IN (SELECT snapshot_id FROM fin_operation_snapshot_link WHERE operation_id=@run)",("@run",run))!.ToString()!)!.AsObject();
        Assert.AreEqual(rider,payload.S("erpRiderId"));
        Assert.AreEqual(800m,payload.N("RiderAmount"));
        var row=Calc(c,tx,actor,run,Rider(rider));
        Assert.AreEqual(800m,row.N("gross"));
        Assert.AreEqual(800m,row.N("salaryNet"));
        tx.Commit();
    }

    // ---------------- H10 ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H10_WalletEventConsumedOnce_Repeated300Rejected(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"hs",rider,customize:(f,_)=>
            f.Rows["hs_wallet"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["balance"]=300,["amount"]=300,["asOf"]="2026-10-15",["fundingType"]="custody",["eventType"]="topup"}]);
        SalEvidence.Gross(c,tx,run,rider,1000m);
        var twice=Rider(rider,new JsonArray(
            new JsonObject{["itemCode"]="hs_wallet_topup",["amount"]=300,["accountCode"]="112501",["fundingType"]="custody"},
            new JsonObject{["itemCode"]="hs_wallet_topup",["amount"]=300,["accountCode"]="112501",["fundingType"]="custody"}));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Calc(c,tx,actor,run,twice)).Status);
        var once=Calc(c,tx,actor,run,Rider(rider,new JsonArray(
            new JsonObject{["itemCode"]="hs_wallet_topup",["amount"]=300,["accountCode"]="112501",["fundingType"]="custody"})));
        Assert.AreEqual(300m,once.N("level1Total"));
        Assert.AreEqual(700m,once.N("salaryNet"));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H10_RefundClassFromEvent_NotCaller_550(bool sqlServer)
    {
        // Frozen: custody top-up 300 + invoice_close CHARGE 250. Caller labels the 250 close_refund → still charge → L1 550, net 450.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"hs",rider,customize:(f,_)=>
            f.Rows["hs_wallet"]=
            [
                new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["balance"]=300,["amount"]=300,["asOf"]="2026-10-15",["fundingType"]="custody",["eventType"]="topup"},
                new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["balance"]=250,["amount"]=250,["asOf"]="2026-10-20",["fundingType"]="invoice_close",["eventType"]="charge"}
            ]);
        SalEvidence.Gross(c,tx,run,rider,1000m);
        var row=Calc(c,tx,actor,run,Rider(rider,new JsonArray(
            new JsonObject{["itemCode"]="hs_wallet_topup",["amount"]=300,["accountCode"]="112501",["fundingType"]="custody"},
            new JsonObject{["itemCode"]="hs_wallet_close",["amount"]=250,["accountCode"]="112513",["fundingType"]="invoice_close",["eventType"]="close_refund",["offsetsTopup"]=true})));
        Assert.AreEqual(550m,row.N("level1Total"));
        Assert.AreEqual(450m,row.N("salaryNet"));
        tx.Commit();
    }

    // ---------------- H11 ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_FirstAppCalculated_SecondAppOnlyInFrozenTimeline_HoldBlocksApproval(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",rider,customize:(f,acc)=>
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-10",["effectiveTo"]="2026-10-31",["appCode"]="keeta"}));
        SalEvidence.Gross(c,tx,run,rider,200m);
        string riderId=Calc(c,tx,actor,run,Rider(rider)).S("id");
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='open'",("@r",rider)));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_shared_rider_hold_app ha INNER JOIN sal_shared_rider_hold h ON h.id=ha.hold_id WHERE h.erp_rider_id=@r AND ha.app_code='keeta' AND ha.received=1",("@r",rider)));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_shared_rider_hold_app ha INNER JOIN sal_shared_rider_hold h ON h.id=ha.hold_id WHERE h.erp_rider_id=@r AND ha.app_code='ninja' AND ha.received=1",("@r",rider)));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})})).Status);
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_SecondRunExistsNotCalculated_NinjaFirst_HoldOpened(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        FrozenRun(c,tx,actor,company,"keeta",rider);
        string ninja=FrozenRun(c,tx,actor,company,"ninja",rider);
        SalEvidence.Gross(c,tx,ninja,rider,200m);
        string riderId=Calc(c,tx,actor,ninja,Rider(rider)).S("id");
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='open'",("@r",rider)));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalDebtConfirm.Confirm(c,tx,actor,ninja,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})})).Status);
        tx.Commit();
    }

    // ---------------- H12 ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H12_SameDebtLinkedAndConfirmed_Rejected_AccrualEqualsPayable140(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",rider);
        string debtId=OpenDebt(c,tx,company,rider,60);
        SalEvidence.Gross(c,tx,run,rider,200m);
        string riderId=Calc(c,tx,actor,run,Rider(rider,new JsonArray(
            new JsonObject{["itemCode"]="cross_app_debt",["amount"]=60,["accountCode"]="112507",["debtId"]=debtId}))).S("id");
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject
        {
            ["payRunRiderId"]=riderId,["allocations"]=new JsonArray(new JsonObject{["debtId"]=debtId,["amount"]=60})
        })})).Status);
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,Admin(),run,new JsonObject{["payRunRiderIds"]=new JsonArray(riderId)});
        Assert.AreEqual(140m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount_due FROM pay_payable WHERE source_id=@r",("@r",riderId))));
        Assert.AreEqual(140m,Liability212103(c,tx));
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debtId))));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H12_LinkedDebtExhaustedBeforeApproval_Rejected(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",rider);
        string debtId=OpenDebt(c,tx,company,rider,60);
        SalEvidence.Gross(c,tx,run,rider,200m);
        // Linked amount above the open balance is stale at calc time.
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Calc(c,tx,actor,run,Rider(rider,new JsonArray(
            new JsonObject{["itemCode"]="cross_app_debt",["amount"]=80,["accountCode"]="112507",["debtId"]=debtId})))).Status);
        string riderId=Calc(c,tx,actor,run,Rider(rider,new JsonArray(
            new JsonObject{["itemCode"]="cross_app_debt",["amount"]=60,["accountCode"]="112507",["debtId"]=debtId}))).S("id");
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})});
        // Another source settles the debt before approval (balance 60 → 0).
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id,created_at) VALUES(@id,@d,@c,'2026-10-01','2026-10-01',-60,'synthetic',@id,datetime('now'))",
            ("@id",MoneySchemaFixture.Id()),("@d",debtId),("@c",company));
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debtId))));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalSalaryApprove.Approve(c,tx,Admin(),run,new JsonObject{["payRunRiderIds"]=new JsonArray(riderId)})).Status);
        tx.Commit();
    }

    // ---------------- H13 ----------------

    static string MatchedMudad(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,string company,string erp,decimal basic,char hash)
    {
        var upload=MudadApi.Upload(c,tx,actor,new JsonObject
        {
            ["companyId"]=company,["periodMonth"]="2026-10-01",["fileKind"]="salaries",
            ["fileName"]="m.xlsx",["fileHashHex"]=new string(hash,64),
            ["lines"]=new JsonArray(new JsonObject{["iqama"]="10000001"+((int)hash).ToString("D2"),["officialName"]="Z",["basicSalary"]=basic,["grossSalary"]=basic})
        });
        string lineId=upload["lines"]![0]!["id"]!.GetValue<string>();
        MudadApi.Match(c,tx,actor,upload.S("id"),new JsonObject{["matches"]=new JsonArray(new JsonObject{["lineId"]=lineId,["erpEmployeeId"]=erp})});
        return lineId;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H13_ApplyMudad_UnprotectedNinja_KeepsNegative_Minus30(bool sqlServer)
    {
        // gross 100, personal L1 120, Mudad 10 → −30 carry-over (no zero floor outside Keeta §22.9.2).
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",erp);
        string lineId=MatchedMudad(c,tx,actor,company,erp,10,'a');
        SalEvidence.Gross(c,tx,run,erp,100m);
        Calc(c,tx,actor,run,Rider(erp,new JsonArray(new JsonObject{["itemCode"]="cash_advance",["amount"]=120,["accountCode"]="112501"})));
        var applied=SalPayCalc.ApplyMudad(c,tx,actor,new JsonObject{["payRunId"]=run,["erpRiderId"]=erp,["mudadLineId"]=lineId,["amount"]=10});
        Assert.AreEqual(-30m,applied.N("salaryNet"));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H13_ApplyMudad_CompanyOperatingNotDeducted_150(bool sqlServer)
    {
        // Salary scheme: fuel 80 stays company cost (affects_net=0). gross 200 − Mudad 50 = 150 (not 70).
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",erp,scheme:"salary");
        string lineId=MatchedMudad(c,tx,actor,company,erp,50,'b');
        SalEvidence.Gross(c,tx,run,erp,200m);
        var calc=Calc(c,tx,actor,run,Rider(erp,new JsonArray(new JsonObject{["itemCode"]="fuel",["amount"]=80,["accountCode"]="112511"})));
        Assert.AreEqual(200m,calc.N("salaryNet"));
        var applied=SalPayCalc.ApplyMudad(c,tx,actor,new JsonObject{["payRunId"]=run,["erpRiderId"]=erp,["mudadLineId"]=lineId,["amount"]=50});
        Assert.AreEqual(150m,applied.N("salaryNet"));
        tx.Commit();
    }
}
