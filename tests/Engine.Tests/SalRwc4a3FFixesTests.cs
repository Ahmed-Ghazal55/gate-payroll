using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>RWC4a3F: Codex RWC4a3 rows 4–5–7–20 — H11 recalc/sibling; H13 mudad cross-app; protected_salary; approve actions. Exact SAR; failing test first.</summary>
[TestClass]
public sealed class SalRwc4a3FFixesTests
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
        string rider,string? account=null,string scheme="commission",bool? eligible=null,Action<FakeOpsLiveFactsReader,string>? customize=null)
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
        bool elig=eligible??true;
        facts.Rows["eligibility"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["eligible"]=elig,["source"]="pay_settings"}];
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

    static JsonObject Rider(string erp,JsonArray? l1=null,JsonArray? l2=null,string? scheme=null,bool? keetaIneligible=null,decimal? commission=null)
    {
        var row=new JsonObject{["erpRiderId"]=erp,["level1"]=l1??new JsonArray(),["level2"]=l2??new JsonArray()};
        if(scheme is {} s)row["schemeType"]=s;
        if(keetaIneligible is {} k)row["keetaIneligible"]=k;
        if(commission is {} c)row["commission"]=c;
        return row;
    }

    static JsonObject Calc(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,string run,JsonObject rider)
        =>SalPayCalc.Calculate(c,tx,actor,run,new JsonObject{["riders"]=new JsonArray(rider)})["riders"]!.AsArray().OfType<JsonObject>().Single();

    static JsonArray Fuel50()=>new(new JsonObject{["itemCode"]="fuel",["amount"]=50,["accountCode"]="112511"});

    static string[] AllowedActions(JsonObject result)
        =>result["allowedActions"]?.AsArray().Select(x=>x!.ToString()).ToArray()??[];

    // ---------------- GROUP A: H11 recalc / sibling ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_RecalcAfterConsolidate_Combined350_Payables300Plus50(bool sqlServer)
    {
        // Ninja net 300, Keeta net 50 (fuel on Keeta only) → combined 350. Recalc refreshes cleared hold; approve 300+50.
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
        SalEvidence.Gross(c,tx,ninja,rider,300m);
        string ninjaRiderId=Calc(c,tx,actor,ninja,Rider(rider)).S("id");
        string keetaRiderId=Calc(c,tx,actor,keeta,Rider(rider,Fuel50())).S("id");
        Assert.AreEqual(350m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        var recalc=Calc(c,tx,actor,ninja,Rider(rider));
        Assert.AreEqual(ninjaRiderId,recalc.S("id"));
        Assert.AreEqual(350m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        SalDebtConfirm.Confirm(c,tx,actor,ninja,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=ninjaRiderId,["amount"]=0})});
        SalDebtConfirm.Confirm(c,tx,actor,keeta,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=keetaRiderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,actor,ninja,new JsonObject{["payRunRiderIds"]=new JsonArray(ninjaRiderId)});
        SalSalaryApprove.Approve(c,tx,actor,keeta,new JsonObject{["payRunRiderIds"]=new JsonArray(keetaRiderId)});
        decimal ninjaPay=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(amount_due),0) FROM pay_payable WHERE source_id=@r",("@r",ninjaRiderId)));
        decimal keetaPay=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(amount_due),0) FROM pay_payable WHERE source_id=@r",("@r",keetaRiderId)));
        Assert.AreEqual(300m,ninjaPay);
        Assert.AreEqual(50m,keetaPay);
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_RecalcAfterSiblingApproved_409(bool sqlServer)
    {
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
        _=Calc(c,tx,actor,keeta,Rider(rider)).S("id");
        SalDebtConfirm.Confirm(c,tx,actor,ninja,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=ninjaRiderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,actor,ninja,new JsonObject{["payRunRiderIds"]=new JsonArray(ninjaRiderId)});
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Calc(c,tx,actor,keeta,Rider(rider,new JsonArray(new JsonObject{["itemCode"]="cash_advance",["amount"]=120,["accountCode"]="112501"})))).Status);
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_CalcMudadLineAfterSiblingApproved_409(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string ninja=FrozenRun(c,tx,actor,company,"ninja",rider);
        string keeta=FrozenRun(c,tx,actor,company,"keeta",rider,customize:(f,acc)=>
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=100,["deductions"]=0,["orders"]=10}]);
        SalEvidence.Gross(c,tx,ninja,rider,100m);
        string ninjaRiderId=Calc(c,tx,actor,ninja,Rider(rider)).S("id");
        _=Calc(c,tx,actor,keeta,Rider(rider)).S("id");
        string lineId=MatchedMudad(c,tx,actor,company,rider,100,'c');
        SalDebtConfirm.Confirm(c,tx,actor,ninja,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=ninjaRiderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,actor,ninja,new JsonObject{["payRunRiderIds"]=new JsonArray(ninjaRiderId)});
        var keetaRider=Rider(rider);
        keetaRider["mudadLineId"]=lineId;
        keetaRider["mudadAmount"]=100;
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Calc(c,tx,actor,keeta,keetaRider)).Status);
        tx.Commit();
    }

    // ---------------- GROUP B: H13 Mudad cross-app ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Mudad_SameLineSecondApp_409_TotalRecovered1000(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string ninja=FrozenRun(c,tx,actor,company,"ninja",rider,scheme:"salary");
        string keeta=FrozenRun(c,tx,actor,company,"keeta",rider,scheme:"salary",customize:(f,acc)=>
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=1000,["deductions"]=0,["orders"]=10}]);
        SalEvidence.Gross(c,tx,ninja,rider,1000m);
        string lineId=MatchedMudad(c,tx,actor,company,rider,1000,'4');
        Calc(c,tx,actor,ninja,Rider(rider,scheme:"salary"));
        Calc(c,tx,actor,keeta,Rider(rider,scheme:"salary"));
        var applied=SalPayCalc.ApplyMudad(c,tx,actor,new JsonObject{["payRunId"]=ninja,["erpRiderId"]=rider,["mudadLineId"]=lineId,["amount"]=1000});
        Assert.AreEqual(1000m,applied.N("amount"));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalPayCalc.ApplyMudad(c,tx,actor,new JsonObject
        {
            ["payRunId"]=keeta,["erpRiderId"]=rider,["mudadLineId"]=lineId,["amount"]=1000
        })).Status);
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Mudad_CalcPath_WritesDeductionRow(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",erp,scheme:"salary");
        SalEvidence.Gross(c,tx,run,erp,500m);
        string lineId=MatchedMudad(c,tx,actor,company,erp,500,'5');
        var row=Rider(erp,scheme:"salary");
        row["mudadLineId"]=lineId;
        row["mudadAmount"]=500;
        string riderId=Calc(c,tx,actor,run,row).S("id");
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_mudad_deduction WHERE pay_run_rider_id=@r AND mudad_line_id=@l AND amount=500",("@r",riderId),("@l",lineId)));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Mudad_PartialAmount_409(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",erp,scheme:"salary");
        SalEvidence.Gross(c,tx,run,erp,500m);
        Calc(c,tx,actor,run,Rider(erp,scheme:"salary"));
        string lineId=MatchedMudad(c,tx,actor,company,erp,500,'6');
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalPayCalc.ApplyMudad(c,tx,actor,new JsonObject
        {
            ["payRunId"]=run,["erpRiderId"]=erp,["mudadLineId"]=lineId,["amount"]=400
        })).Status);
        tx.Commit();
    }

    // ---------------- GROUP C: protected_salary + Keeta commission ineligible ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void KeetaCommissionIneligible_NetMinus50_Debt50_212103Zero(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"keeta",erp,scheme:"commission",eligible:false,customize:(f,acc)=>
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=100,["deductions"]=0,["orders"]=10,["commission"]=0}]);
        var calc=Calc(c,tx,actor,run,Rider(erp,new JsonArray(new JsonObject{["itemCode"]="fuel",["amount"]=150,["accountCode"]="112511"}),
            scheme:"commission",keetaIneligible:true,commission:0));
        Assert.AreEqual(-50m,calc.N("salaryNet"));
        Assert.AreEqual(0,Convert.ToInt32(MoneySchemaFixture.Scalar(c,tx,"SELECT protected_salary FROM sal_pay_run_rider WHERE id=@id",("@id",calc.S("id")))));
        string riderId=calc.S("id");
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,actor,run,new JsonObject{["payRunRiderIds"]=new JsonArray(riderId)});
        Assert.AreEqual(50m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(balance),0) FROM debt_debt WHERE erp_rider_id=@r AND category='NegativeSalaryCarryover'",("@r",erp))));
        Assert.AreEqual(0m,Liability212103(c,tx));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void KeetaNoSchemeRow_CalcAndMudadSameFloor(bool sqlServer)
    {
        // Ineligible without frozen scheme row → not protected; calc and ApplyMudad both keep negative net.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"keeta",erp,eligible:false,customize:(f,acc)=>
        {
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=100,["deductions"]=0,["orders"]=10}];
            f.Rows["scheme"]=[];
        });
        var calc=Calc(c,tx,actor,run,Rider(erp,new JsonArray(new JsonObject{["itemCode"]="cash_advance",["amount"]=130,["accountCode"]="112501"}),keetaIneligible:true));
        Assert.AreEqual(-30m,calc.N("salaryNet"));
        Assert.AreEqual(0,Convert.ToInt32(MoneySchemaFixture.Scalar(c,tx,"SELECT protected_salary FROM sal_pay_run_rider WHERE id=@id",("@id",calc.S("id")))));
        string lineId=MatchedMudad(c,tx,actor,company,erp,10,'7');
        var applied=SalPayCalc.ApplyMudad(c,tx,actor,new JsonObject{["payRunId"]=run,["erpRiderId"]=erp,["mudadLineId"]=lineId,["amount"]=10});
        Assert.AreEqual(-40m,applied.N("salaryNet"));
        tx.Commit();
    }

    // ---------------- GROUP D: export_bank_file LOW ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Approve_ParallelRun_NoExportAction(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",erp);
        SalEvidence.Gross(c,tx,run,erp,100m);
        MoneySchemaFixture.Exec(c,tx,"UPDATE sal_pay_run SET payout_blocked=1 WHERE id=@id",("@id",run));
        string riderId=Calc(c,tx,actor,run,Rider(erp)).S("id");
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})});
        var approved=SalSalaryApprove.Approve(c,tx,actor,run,new JsonObject{["payRunRiderIds"]=new JsonArray(riderId)});
        CollectionAssert.DoesNotContain(AllowedActions(approved),"export_bank_file");
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Approve_NoPayable_NoExportAction(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",erp);
        SalEvidence.Gross(c,tx,run,erp,100m);
        string riderId=Calc(c,tx,actor,run,Rider(erp,new JsonArray(new JsonObject{["itemCode"]="cash_advance",["amount"]=150,["accountCode"]="112501"}))).S("id");
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})});
        var approved=SalSalaryApprove.Approve(c,tx,actor,run,new JsonObject{["payRunRiderIds"]=new JsonArray(riderId)});
        Assert.AreEqual(0,AllowedActions(approved).Length);
        Assert.AreEqual(0,Convert.ToInt32(MoneySchemaFixture.Scalar(c,tx,"SELECT bank_eligible FROM sal_pay_run_rider WHERE id=@id",("@id",riderId))));
        tx.Commit();
    }
}