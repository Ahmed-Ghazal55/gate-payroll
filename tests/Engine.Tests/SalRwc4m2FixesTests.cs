using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>RWC4m2F: Codex RWC4m2 open rows — H12 affects_net settle; H11 pending carryover; shared reverse without boost; late Mudad guard. Exact SAR; failing test first.</summary>
[TestClass]
public sealed class SalRwc4m2FixesTests
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

    static string OpenDebt(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,string company,string erp,decimal balance,string category="FuelOverage")
    {
        string debtId=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO debt_debt(id,erp_rider_id,company_id,category,principal,balance,instalment,status,created_at) VALUES(@id,@r,@c,@cat,@p,0,@i,'open',datetime('now'))",
            ("@id",debtId),("@r",erp),("@c",company),("@cat",category),("@p",balance),("@i",Math.Min(balance,80m)));
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id,created_at) VALUES(@id,@d,@c,'2026-10-01','2026-10-01',@a,'synthetic',@id,datetime('now'))",
            ("@id",MoneySchemaFixture.Id()),("@d",debtId),("@c",company),("@a",balance));
        return debtId;
    }

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

    static JsonObject Rider(string erp,JsonArray? l1=null,JsonArray? l2=null,string? scheme=null)
    {
        var row=new JsonObject{["erpRiderId"]=erp,["level1"]=l1??new JsonArray(),["level2"]=l2??new JsonArray()};
        if(scheme is {} s)row["schemeType"]=s;
        return row;
    }

    static JsonObject Calc(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,string run,JsonObject rider)
        =>SalPayCalc.Calculate(c,tx,actor,run,new JsonObject{["riders"]=new JsonArray(rider)})["riders"]!.AsArray().OfType<JsonObject>().Single();

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
        SalSalaryApprove.Approve(c,tx,actor,firstRun,new JsonObject{["payRunRiderIds"]=new JsonArray(firstRider)});
        SalSalaryApprove.Approve(c,tx,actor,secondRun,new JsonObject{["payRunRiderIds"]=new JsonArray(secondRider)});
    }

    static decimal CarryoverBalance(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,string rider)
        =>Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(balance),0) FROM debt_debt WHERE erp_rider_id=@r AND category='NegativeSalaryCarryover'",("@r",rider)));

    static JsonArray Fuel(decimal amount)
        =>new(new JsonObject{["itemCode"]="fuel",["amount"]=amount,["accountCode"]="112511"});

    // ---------------- H12: settle only affects_net=1 ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H12_CompanyPaidFuel20_LinkedDebt_NotSettled_Pay100_Liability100(bool sqlServer)
    {
        // Salary scheme: fuel20 company cost (affects_net=0) with debt_id. gross100 → net100/pay100.
        // Must NOT settle debt20 against 212103 (would leave liability80). Debt stays open at 20.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",erp,scheme:"salary");
        SalEvidence.Gross(c,tx,run,erp,100m);
        string debtId=OpenDebt(c,tx,company,erp,20m);
        var row=Calc(c,tx,actor,run,Rider(erp,new JsonArray(new JsonObject
        {
            ["itemCode"]="fuel",["amount"]=20,["accountCode"]="112511",["debtId"]=debtId,["operating"]=true
        }),scheme:"salary"));
        Assert.AreEqual(100m,row.N("salaryNet"));
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT affects_net FROM sal_pay_run_item WHERE pay_run_rider_id=@r AND item_code='fuel' AND amount=20",("@r",row.S("id")))));
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=row.S("id"),["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,actor,run,new JsonObject{["payRunRiderIds"]=new JsonArray(row.S("id"))});
        Assert.AreEqual(100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount_due FROM pay_payable WHERE source_id=@r",("@r",row.S("id")))));
        Assert.AreEqual(100m,Liability212103(c,tx));
        Assert.AreEqual(20m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debtId))));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM debt_debt_movement WHERE debt_id=@d AND source_type='pay_item_debt_settle'",("@d",debtId)));
        tx.Commit();
    }

    // ---------------- H11: exception release keeps standalone carryover while apps pending ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_ExceptionRelease_NetMinus30_PendingApp_Carryover30_LiabilityZero(bool sqlServer)
    {
        // Dual exception release with Keeta still missing; ninja net −30 → carryover debt 30 (not 0 from combined_net=0).
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var maker=Admin(MoneySchemaFixture.Id());
        var checker=Admin(MoneySchemaFixture.Id());
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string ninja=FrozenRun(c,tx,maker,company,"ninja",rider,customize:(f,acc)=>
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-10",["effectiveTo"]="2026-10-31",["appCode"]="keeta"}));
        SalEvidence.Gross(c,tx,ninja,rider,100m);
        string riderId=Calc(c,tx,maker,ninja,Rider(rider,new JsonArray(new JsonObject{["itemCode"]="cash_advance",["amount"]=130,["accountCode"]="112501"}))).S("id");
        Assert.AreEqual(-30m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT salary_net FROM sal_pay_run_rider WHERE id=@id",("@id",riderId))));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='open'",("@r",rider)));
        string reason="فاتورة كيتا ناقصة — فك استثناء";
        var opened=SalSharedRiders.RequestRelease(c,tx,maker,rider,"2026-10-01",reason);
        ApprovalsArea.Decide(c,tx,checker,opened.S("id"),new(){["decision"]="approve",["note"]="independent checker"});
        Assert.AreEqual("cleared",SalSharedRiders.Release(c,tx,checker,rider,"2026-10-01",reason).S("status"));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_shared_rider_hold_app WHERE hold_id=(SELECT id FROM sal_shared_rider_hold WHERE erp_rider_id=@r) AND received=0",("@r",rider)));
        SalDebtConfirm.Confirm(c,tx,maker,ninja,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,checker,ninja,new JsonObject{["payRunRiderIds"]=new JsonArray(riderId)});
        Assert.AreEqual(30m,CarryoverBalance(c,tx,rider));
        Assert.AreEqual(0m,Liability212103(c,tx));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM pay_payable WHERE source_id=@r",("@r",riderId)));
        tx.Commit();
    }

    // ---------------- Shared fuel reverse independent of payable boost ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SharedFuel200Each_Combined0_Reverse200_Liability0(bool sqlServer)
    {
        // gross100 / fuel200 each → combined 0, pay 0, debt 0; duplicate reverse must still post 200 → liability 0 (not −200).
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        var (ninja,keeta,ninjaRiderId,keetaRiderId)=SharedTwoApps(c,tx,actor,company,rider,100m,Fuel(200m),100m,Fuel(200m));
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        ConfirmAndApproveBoth(c,tx,actor,ninja,ninjaRiderId,keeta,keetaRiderId);
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(amount_due),0) FROM pay_payable WHERE erp_rider_id=@r",("@r",rider))));
        Assert.AreEqual(0m,CarryoverBalance(c,tx,rider));
        Assert.AreEqual(200m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(debit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='112511') AND journal_id IN (SELECT id FROM fin_journal WHERE purpose='salary_shared_once')")));
        Assert.AreEqual(0m,Liability212103(c,tx));
        tx.Commit();
    }

    // ---------------- Late Mudad after sibling approval ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LateMudad_AfterSiblingApproved_Rejected_CombinedStays200(bool sqlServer)
    {
        // Approve A net100; applying Mudad150 on B would leave payable100 / combined50 / stranded liability — reject 409.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        var (ninja,keeta,ninjaRiderId,keetaRiderId)=SharedTwoApps(c,tx,actor,company,rider,100m,new JsonArray(),100m,new JsonArray());
        Assert.AreEqual(200m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        SalDebtConfirm.Confirm(c,tx,actor,ninja,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=ninjaRiderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,actor,ninja,new JsonObject{["payRunRiderIds"]=new JsonArray(ninjaRiderId)});
        Assert.AreEqual(100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount_due FROM pay_payable WHERE source_id=@r",("@r",ninjaRiderId))));
        string lineId=MatchedMudad(c,tx,actor,company,rider,150,'7');
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalPayCalc.ApplyMudad(c,tx,actor,new JsonObject
        {
            ["payRunId"]=keeta,["erpRiderId"]=rider,["mudadLineId"]=lineId,["amount"]=150
        })).Status);
        Assert.AreEqual(200m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        Assert.AreEqual(100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT salary_net FROM sal_pay_run_rider WHERE id=@id",("@id",keetaRiderId))));
        tx.Commit();
    }
}
