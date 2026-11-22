using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>RWC4a2F: Codex RWC4a2 open rows — H11 approve consumes combined_net; New HIGH protected Keeta floor reconciles recoveries. Exact SAR; failing test first.</summary>
[TestClass]
public sealed class SalRwc4a2FixesTests
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
        string rider,string? account=null,string scheme="commission",bool eligible=true,Action<FakeOpsLiveFactsReader,string>? customize=null)
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
        facts.Rows["eligibility"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["eligible"]=eligible,["source"]=eligible?"pay_settings":"keeta_eligibility_view"}];
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

    static JsonObject Rider(string erp,JsonArray? l1=null,JsonArray? l2=null,decimal? commission=null,string? scheme=null,bool? keetaIneligible=null)
    {
        var row=new JsonObject{["erpRiderId"]=erp,["level1"]=l1??new JsonArray(),["level2"]=l2??new JsonArray()};
        if(commission is {} c)row["commission"]=c;
        if(scheme is {} s)row["schemeType"]=s;
        if(keetaIneligible is {} k)row["keetaIneligible"]=k;
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

    // ---------------- H11 ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void H11_SharedFuelTwice_ApproveBoth_PayablesSumCombinedNet300(bool sqlServer)
    {
        // Each app net after fuel 50: ninja 150, keeta 100 → raw 250; duplicate fuel added back → combined 300.
        // Approving both must pay 300 total (§40.5), not 150+100=250.
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
        string ninjaRiderId=Calc(c,tx,actor,ninja,Rider(rider,new JsonArray(new JsonObject{["itemCode"]="fuel",["amount"]=50,["accountCode"]="112511"}))).S("id");
        string keetaRiderId=Calc(c,tx,actor,keeta,Rider(rider,new JsonArray(new JsonObject{["itemCode"]="fuel",["amount"]=50,["accountCode"]="112511"}))).S("id");
        Assert.AreEqual(300m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        SalDebtConfirm.Confirm(c,tx,actor,ninja,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=ninjaRiderId,["amount"]=0})});
        SalDebtConfirm.Confirm(c,tx,actor,keeta,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=keetaRiderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,actor,ninja,new JsonObject{["payRunRiderIds"]=new JsonArray(ninjaRiderId)});
        SalSalaryApprove.Approve(c,tx,actor,keeta,new JsonObject{["payRunRiderIds"]=new JsonArray(keetaRiderId)});
        decimal ninjaPay=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount_due FROM pay_payable WHERE source_id=@r",("@r",ninjaRiderId)));
        decimal keetaPay=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount_due FROM pay_payable WHERE source_id=@r",("@r",keetaRiderId)));
        Assert.AreEqual(300m,J.Money(ninjaPay+keetaPay));
        tx.Commit();
    }

    // ---------------- New HIGH ----------------

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NewHigh_ProtectedKeeta_Fuel100_Mudad10_Accrue100_Recover100_LiabilityZero(bool sqlServer)
    {
        // §22.9.2: gross 100, recovered fuel 100, Mudad 10 → floored net 0.
        // Accrue 100; recoveries must not exceed 100 (no unmatched debit 10); mudad residual 10 open; no payable.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"keeta",erp,scheme:"salary",eligible:false,customize:(f,acc)=>
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=100,["deductions"]=0,["orders"]=10}]);
        string lineId=MatchedMudad(c,tx,actor,company,erp,10,'a');
        var calc=Calc(c,tx,actor,run,Rider(erp,new JsonArray(new JsonObject{["itemCode"]="fuel",["amount"]=100,["accountCode"]="112511"}),
            scheme:"salary",keetaIneligible:true,commission:0));
        Assert.AreEqual(0m,calc.N("salaryNet"));
        var applied=SalPayCalc.ApplyMudad(c,tx,actor,new JsonObject{["payRunId"]=run,["erpRiderId"]=erp,["mudadLineId"]=lineId,["amount"]=10});
        Assert.AreEqual(0m,applied.N("salaryNet"));
        string riderId=applied.S("payRunRiderId");
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,actor,run,new JsonObject{["payRunRiderIds"]=new JsonArray(riderId)});
        Assert.AreEqual(100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(credit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='212103') AND journal_id IN (SELECT id FROM fin_journal WHERE purpose='salary_accrual')")));
        Assert.AreEqual(100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(debit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='212103') AND journal_id IN (SELECT id FROM fin_journal WHERE purpose='salary_recovery')")));
        Assert.AreEqual(0m,Liability212103(c,tx));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM pay_payable WHERE source_id=@r",("@r",riderId)));
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(amount),0) FROM sal_pay_run_item WHERE pay_run_rider_id=@r AND item_code='mudad_salary' AND amount<>0",("@r",riderId))));
        tx.Commit();
    }
}
