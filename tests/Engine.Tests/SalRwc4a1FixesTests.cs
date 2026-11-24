using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>RWC4a1F: Codex RWC4a1 open rows — H1 commission bind, H11 consolidate+release, New HIGH −30 posting. Exact SAR; failing test first.</summary>
[TestClass]
public sealed class SalRwc4a1FixesTests
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

    static decimal Liability212103(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx)
        =>Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(credit-debit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='212103')"));

    static JsonObject Rider(string erp,JsonArray? l1=null,JsonArray? l2=null,decimal? commission=null)
    {
        var row=new JsonObject{["erpRiderId"]=erp,["level1"]=l1??new JsonArray(),["level2"]=l2??new JsonArray()};
        if(commission is {} c)row["commission"]=c;
        return row;
    }

    static JsonObject Calc(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,string run,JsonObject rider)
        =>SalPayCalc.Calculate(c,tx,actor,run,new JsonObject{["riders"]=new JsonArray(rider)})["riders"]!.AsArray().OfType<JsonObject>().Single();

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

    // ---------------- H1 ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H1_Keeta_CallerCommissionWithoutFrozenEvidence_Rejected_Gross100(bool sqlServer)
    {
        // Frozen Keeta row: gross 100, no commission field → commission binds to 0; caller 40 → 409; net 100.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"keeta",rider,customize:(f,acc)=>
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=100,["deductions"]=0,["orders"]=10}]);
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Calc(c,tx,actor,run,Rider(rider,commission:40))).Status);
        Assert.AreEqual(100m,Calc(c,tx,actor,run,Rider(rider,commission:0)).N("salaryNet"));
        Assert.AreEqual(0m,Calc(c,tx,actor,run,Rider(rider)).N("commission"));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H1_Ninja_CallerCommissionWithoutFrozenEvidence_Rejected_Gross500(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",rider);
        SalEvidence.Gross(c,tx,run,rider,500m);
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Calc(c,tx,actor,run,Rider(rider,commission:50))).Status);
        Assert.AreEqual(500m,Calc(c,tx,actor,run,Rider(rider)).N("salaryNet"));
        tx.Commit();
    }

    // ---------------- H11 ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_BothAppsCalculated_Consolidates_CombinedNet350_AllowsApprove(bool sqlServer)
    {
        // Ninja net 200 + Keeta net 150 = combined 350; shared fuel once (ninja only) → hold cleared; approve ok.
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
        string ninjaRiderId=Calc(c,tx,actor,ninja,Rider(rider)).S("id");
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='open'",("@r",rider)));
        string keetaRiderId=Calc(c,tx,actor,keeta,Rider(rider)).S("id");
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider)));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='open'",("@r",rider)));
        Assert.AreEqual(350m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        SalDebtConfirm.Confirm(c,tx,actor,ninja,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=ninjaRiderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,Admin(),ninja,new JsonObject{["payRunRiderIds"]=new JsonArray(ninjaRiderId)});
        Assert.AreEqual(200m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount_due FROM pay_payable WHERE source_id=@r",("@r",ninjaRiderId))));
        _=keetaRiderId;
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_SharedFuelTwice_ConsolidateAddsBackOne_Combined250(bool sqlServer)
    {
        // Each app net after fuel 50: ninja 150, keeta 100 → raw 250; duplicate fuel added back once → 300.
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
            f.Rows["scheme"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["schemeCode"]="commission",["source"]="pay_settings"}];
        });
        SalEvidence.Gross(c,tx,ninja,rider,200m);
        Calc(c,tx,actor,ninja,Rider(rider,new JsonArray(new JsonObject{["itemCode"]="fuel",["amount"]=50,["accountCode"]="112511"})));
        Calc(c,tx,actor,keeta,Rider(rider,new JsonArray(new JsonObject{["itemCode"]="fuel",["amount"]=50,["accountCode"]="112511"})));
        Assert.AreEqual(300m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_ExceptionRelease_ClearsHold_AllowsFirstAppApprove(bool sqlServer)
    {
        // Second app missing; dual release (approvals.dual.shared_rider_release) clears hold → ninja approve with net 200.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        // Actor names must be GUID-shaped for SQL uniqueidentifier audit columns; still two distinct people for dual release.
        var maker=Admin(MoneySchemaFixture.Id());
        var checker=Admin(MoneySchemaFixture.Id());
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string ninja=FrozenRun(c,tx,maker,company,"ninja",rider,customize:(f,acc)=>
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-10",["effectiveTo"]="2026-10-31",["appCode"]="keeta"}));
        SalEvidence.Gross(c,tx,ninja,rider,200m);
        string riderId=Calc(c,tx,maker,ninja,Rider(rider)).S("id");
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='open'",("@r",rider)));
        string reason="وصلت فاتورة كيتا يدويًا";
        var opened=SalSharedRiders.RequestRelease(c,tx,maker,rider,"2026-10-01",reason);
        Assert.AreEqual(ApprovalsArea.DualSharedRiderRelease,ApprovalsArea.ResolveDual(opened));
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>SalSharedRiders.Release(c,tx,checker,rider,"2026-10-01",reason)).Status);
        ApprovalsArea.Decide(c,tx,checker,opened.S("id"),new(){["decision"]="approve",["note"]="independent checker"});
        var released=SalSharedRiders.Release(c,tx,checker,rider,"2026-10-01",reason);
        Assert.AreEqual("cleared",released.S("status"));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='open'",("@r",rider)));
        SalDebtConfirm.Confirm(c,tx,maker,ninja,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,checker,ninja,new JsonObject{["payRunRiderIds"]=new JsonArray(riderId)});
        Assert.AreEqual(200m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount_due FROM pay_payable WHERE source_id=@r",("@r",riderId))));
        tx.Commit();
    }

    // ---------------- New HIGH ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NewHigh_ApproveMinus30_Accrues100_Recovers130_LiabilityZero_Debt30(bool sqlServer)
    {
        // gross 100 − L1 120 − mudad 10 = −30. Accrue 100; recover 130; carryover 30 → 212103=0, debt 30, no payable.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",erp);
        string lineId=MatchedMudad(c,tx,actor,company,erp,10,'c');
        SalEvidence.Gross(c,tx,run,erp,100m);
        Calc(c,tx,actor,run,Rider(erp,new JsonArray(new JsonObject{["itemCode"]="cash_advance",["amount"]=120,["accountCode"]="112501"})));
        var applied=SalPayCalc.ApplyMudad(c,tx,actor,new JsonObject{["payRunId"]=run,["erpRiderId"]=erp,["mudadLineId"]=lineId,["amount"]=10});
        Assert.AreEqual(-30m,applied.N("salaryNet"));
        string riderId=applied.S("payRunRiderId");
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,Admin(),run,new JsonObject{["payRunRiderIds"]=new JsonArray(riderId)});
        Assert.AreEqual(100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(credit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='212103') AND journal_id IN (SELECT id FROM fin_journal WHERE purpose='salary_accrual')")));
        Assert.AreEqual(130m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(debit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='212103') AND journal_id IN (SELECT id FROM fin_journal WHERE purpose='salary_recovery')")));
        Assert.AreEqual(0m,Liability212103(c,tx));
        Assert.AreEqual(30m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE erp_rider_id=@r AND category='NegativeSalaryCarryover'",("@r",erp))));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM pay_payable WHERE source_id=@r",("@r",riderId)));
        tx.Commit();
    }
}
