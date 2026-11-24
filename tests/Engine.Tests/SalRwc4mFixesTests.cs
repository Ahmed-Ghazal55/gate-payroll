using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>RWC4mF: Codex RWC4m open rows — H11 combined-balance carryover; New HIGH refresh combined_net after Mudad. Exact SAR; failing test first.</summary>
[TestClass]
public sealed class SalRwc4mFixesTests
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

    static JsonObject Rider(string erp,JsonArray? l1=null,JsonArray? l2=null)
        =>new(){["erpRiderId"]=erp,["level1"]=l1??new JsonArray(),["level2"]=l2??new JsonArray()};

    static JsonObject Calc(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,string run,JsonObject rider)
        =>SalPayCalc.Calculate(c,tx,actor,run,new JsonObject{["riders"]=new JsonArray(rider)})["riders"]!.AsArray().OfType<JsonObject>().Single();

    static JsonArray SharedFuel50()
        =>new(new JsonObject{["itemCode"]="fuel",["amount"]=50,["accountCode"]="112511"});

    static (string Ninja,string Keeta,string NinjaRider,string KeetaRider) SharedTwoApps(
        System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,string company,string rider,
        decimal ninjaGross,JsonArray ninjaL1,decimal keetaGross,JsonArray keetaL1)
    {
        string ninja=FrozenRun(c,tx,actor,company,"ninja",rider,customize:(f,acc)=>
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-10",["effectiveTo"]="2026-10-31",["appCode"]="keeta"}));
        string keeta=FrozenRun(c,tx,actor,company,"keeta",rider,customize:(f,acc)=>
        {
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=keetaGross,["deductions"]=0,["orders"]=10}];
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-09",["appCode"]="ninja"});
        });
        SalEvidence.Gross(c,tx,ninja,rider,ninjaGross);
        string ninjaRiderId=Calc(c,tx,actor,ninja,Rider(rider,ninjaL1)).S("id");
        string keetaRiderId=Calc(c,tx,actor,keeta,Rider(rider,keetaL1)).S("id");
        return (ninja,keeta,ninjaRiderId,keetaRiderId);
    }

    static void ConfirmAndApproveBoth(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,
        string firstRun,string firstRider,string secondRun,string secondRider)
    {
        SalDebtConfirm.Confirm(c,tx,actor,firstRun,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=firstRider,["amount"]=0})});
        SalDebtConfirm.Confirm(c,tx,actor,secondRun,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=secondRider,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,Admin(),firstRun,new JsonObject{["payRunRiderIds"]=new JsonArray(firstRider)});
        SalSalaryApprove.Approve(c,tx,Admin(),secondRun,new JsonObject{["payRunRiderIds"]=new JsonArray(secondRider)});
    }

    static decimal CarryoverBalance(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,string rider)
        =>Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(balance),0) FROM debt_debt WHERE erp_rider_id=@r AND category='NegativeSalaryCarryover'",("@r",rider)));

    // ---------------- H11: carryover from combined balance ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_CombinedZero_Plus100Minus100_Pays0_Debt0_NoStrandedLiability(bool sqlServer)
    {
        // Ninja +100 / Keeta −100 → combined 0. Pay 0; debt from combined shortfall = 0 (not app −100); 212103 liability 0 (§40.5).
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        var (ninja,keeta,ninjaRiderId,keetaRiderId)=SharedTwoApps(c,tx,actor,company,rider,
            100m,new JsonArray(),100m,new JsonArray(new JsonObject{["itemCode"]="cash_advance",["amount"]=200,["accountCode"]="112501"}));
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        ConfirmAndApproveBoth(c,tx,actor,ninja,ninjaRiderId,keeta,keetaRiderId);
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(amount_due),0) FROM pay_payable WHERE erp_rider_id=@r",("@r",rider))));
        Assert.AreEqual(0m,CarryoverBalance(c,tx,rider));
        Assert.AreEqual(0m,Liability212103(c,tx));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_CombinedMinus50_Plus100Minus150_Pays0_Debt50(bool sqlServer)
    {
        // Ninja +100 / Keeta −150 → combined −50. Pay 0; carryover debt 50 (not 150); liability 0.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        var (ninja,keeta,ninjaRiderId,keetaRiderId)=SharedTwoApps(c,tx,actor,company,rider,
            100m,new JsonArray(),100m,new JsonArray(new JsonObject{["itemCode"]="cash_advance",["amount"]=250,["accountCode"]="112501"}));
        Assert.AreEqual(-50m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        ConfirmAndApproveBoth(c,tx,actor,ninja,ninjaRiderId,keeta,keetaRiderId);
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(amount_due),0) FROM pay_payable WHERE erp_rider_id=@r",("@r",rider))));
        Assert.AreEqual(50m,CarryoverBalance(c,tx,rider));
        Assert.AreEqual(0m,Liability212103(c,tx));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_CombinedPlus50_Plus100Minus50_Pays50_Debt0(bool sqlServer)
    {
        // Ninja +100 / Keeta −50 → combined 50. Pay 50; no carryover debt; liability matches payable.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        var (ninja,keeta,ninjaRiderId,keetaRiderId)=SharedTwoApps(c,tx,actor,company,rider,
            100m,new JsonArray(),100m,new JsonArray(new JsonObject{["itemCode"]="cash_advance",["amount"]=150,["accountCode"]="112501"}));
        Assert.AreEqual(50m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        ConfirmAndApproveBoth(c,tx,actor,ninja,ninjaRiderId,keeta,keetaRiderId);
        Assert.AreEqual(50m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(amount_due),0) FROM pay_payable WHERE erp_rider_id=@r",("@r",rider))));
        Assert.AreEqual(0m,CarryoverBalance(c,tx,rider));
        Assert.AreEqual(50m,Liability212103(c,tx));
        tx.Commit();
    }

    // ---------------- New HIGH: Mudad after consolidate refreshes combined_net ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NewHigh_Mudad60_AfterConsolidate_Combined240_Pay240_FuelDebit50(bool sqlServer)
    {
        // Shared fuel 50 twice → combined 300. Matched Mudad 60 on ninja after consolidate → combined 240;
        // approve both pays 240; shared-once fuel reverse Dr 112511 = 50 (not 110).
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        var (ninja,keeta,ninjaRiderId,keetaRiderId)=SharedTwoApps(c,tx,actor,company,rider,
            200m,SharedFuel50(),150m,SharedFuel50());
        Assert.AreEqual(300m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        string lineId=MatchedMudad(c,tx,actor,company,rider,60,'b');
        var applied=SalPayCalc.ApplyMudad(c,tx,actor,new JsonObject{["payRunId"]=ninja,["erpRiderId"]=rider,["mudadLineId"]=lineId,["amount"]=60});
        Assert.AreEqual(90m,applied.N("salaryNet"));
        Assert.AreEqual(240m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        ConfirmAndApproveBoth(c,tx,actor,ninja,ninjaRiderId,keeta,keetaRiderId);
        decimal ninjaPay=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(amount_due),0) FROM pay_payable WHERE source_id=@r",("@r",ninjaRiderId)));
        decimal keetaPay=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(amount_due),0) FROM pay_payable WHERE source_id=@r",("@r",keetaRiderId)));
        Assert.AreEqual(240m,J.Money(ninjaPay+keetaPay));
        Assert.AreEqual(50m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(debit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='112511') AND journal_id IN (SELECT id FROM fin_journal WHERE purpose='salary_shared_once')")));
        tx.Commit();
    }
}
