using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>RWC4a3F extra HIGH/MEDIUM rows — exact SAR; failing test first.</summary>
[TestClass]
public sealed class SalRwc4a3FExtraTests
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
        =>MoneySchemaFixture.Exec(c,tx,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) SELECT @id,@c,'2026-10-01',1,datetime('now') WHERE NOT EXISTS(SELECT 1 FROM fin_accounting_period WHERE company_id=@c AND period_month='2026-10-01')",
            ("@id",MoneySchemaFixture.Id()),("@c",company));

    static decimal Acc(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,string code,bool credit=true)
    {
        string side=credit?"COALESCE(SUM(credit-debit),0)":"COALESCE(SUM(debit-credit),0)";
        return Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,$"SELECT {side} FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code=@code)",("@code",code)));
    }

    static decimal Liability212103(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx)=>Acc(c,tx,"212103");

    // ---- H8 §22.9.2 posting ----

    [TestMethod]
    [DataRow(false)]
    public void Keeta22_9_2_Wage600_D3500_Fuel400_Posts512121_421101_112511(bool sqlServer)
    {
        // wage 600 (300×2), D 3500 car, fuel 400 → Cr 421101 3500, Dr 512121 3300, Cr 112511 400, 212103 0, no payable, no debt
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"keeta",erp,scheme:"salary",customize:(f,acc)=>
        {
            f.Rows["eligibility"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=erp,["eligible"]=false,["source"]="pay_settings"}];
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=600,["deductions"]=0,["orders"]=300}];
            f.Rows["vehicle"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=erp,["vehicleType"]="car",["amount"]=3500}];
        });
        SalEvidence.Gross(c,tx,run,erp,600m);
        var row=SalPayCalc.Calculate(c,tx,actor,run,new JsonObject{["riders"]=new JsonArray(new JsonObject
        {
            ["erpRiderId"]=erp,["schemeType"]="salary",["keetaIneligible"]=true,
            ["level1"]=new JsonArray(
                new JsonObject{["itemCode"]="vehicle",["amount"]=3500,["accountCode"]="421101",["operating"]=true},
                new JsonObject{["itemCode"]="fuel",["amount"]=400,["accountCode"]="112511",["operating"]=true}),
            ["level2"]=new JsonArray()
        })})["riders"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual(0m,row.N("salaryNet"));
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=row.S("id"),["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,Admin(),run,new JsonObject{["payRunRiderIds"]=new JsonArray(row.S("id"))});
        Assert.AreEqual(3500m,Acc(c,tx,"421101"));
        Assert.AreEqual(3300m,Acc(c,tx,"512121",credit:false));
        Assert.AreEqual(400m,Acc(c,tx,"112511"));
        Assert.AreEqual(0m,Liability212103(c,tx));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM pay_payable WHERE source_id=@r",("@r",row.S("id"))));
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(balance),0) FROM debt_debt WHERE erp_rider_id=@r",("@r",erp))));
        tx.Commit();
    }

    // ---- H9 Chefz two settlements ----

    [TestMethod]
    [DataRow(false)]
    public void Chefz_TwoSettlements400_450_Gross850(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"chefz",erp,customize:(f,acc)=>
        {
            string acc2=MoneySchemaFixture.Id();
            f.Rows["chefz_settlement"]=
            [
                new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=erp,["erpPlatformAccountId"]=acc,["RiderAmount"]=400,["periodFrom"]="2026-10-01",["periodTo"]="2026-10-15"},
                new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=erp,["erpPlatformAccountId"]=acc2,["RiderAmount"]=450,["periodFrom"]="2026-10-16",["periodTo"]="2026-10-31"}
            ];
            f.Rows["platform_account"].Add(new(){["id"]=acc2,["externalAccountId"]="A2",["code"]="A2"});
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc2,["actualRiderProfileId"]=erp,["effectiveFrom"]="2026-10-16",["effectiveTo"]="2026-10-31",["appCode"]="chefz"});
        });
        var row=SalPayCalc.Calculate(c,tx,actor,run,new JsonObject{["riders"]=new JsonArray(new JsonObject
        {
            ["erpRiderId"]=erp,["level1"]=new JsonArray(),["level2"]=new JsonArray()
        })})["riders"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual(850m,row.N("gross"));
        Assert.AreEqual(850m,row.N("salaryNet"));
        tx.Commit();
    }

    // ---- Mudad scope ----

    [TestMethod]
    [DataRow(false)]
    public void Mudad_OtherCompanyMatch_403(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string companyA=MoneySchemaFixture.Company(c);
        string companyB=MoneySchemaFixture.Company(c);
        // Non-admin scoped to company A only (Admin bypasses FinancialScopes).
        var scoped=new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name,MoneySchemaFixture.Id()),
            new Claim("permission","people.mudad.manage"),
            new Claim("permission","salaries.mudad.edit"),
            new Claim("finance.sponsorIds",companyA)
        ],"synthetic"));
        using var tx=c.BeginTransaction();
        var upload=MudadApi.Upload(c,tx,Admin(),new JsonObject
        {
            ["companyId"]=companyB,["periodMonth"]="2026-10-01",["fileKind"]="salaries",
            ["fileName"]="m.xlsx",["fileHashHex"]=new string('a',64),
            ["lines"]=new JsonArray(new JsonObject{["iqama"]="1000000199",["officialName"]="Z",["basicSalary"]=1000,["grossSalary"]=1000})
        });
        string lineId=upload["lines"]![0]!["id"]!.GetValue<string>();
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>MudadApi.Match(c,tx,scoped,upload.S("id"),new JsonObject
        {
            ["matches"]=new JsonArray(new JsonObject{["lineId"]=lineId,["erpEmployeeId"]=MoneySchemaFixture.Id()})
        })).Status);
        tx.Commit();
    }

    // ---- Linked debt GL account ----

    [TestMethod]
    [DataRow(false)]
    public void LinkedAdvance300_Credits112501_RegisterEqualsGL(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string erp=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,"ninja",erp);
        SalEvidence.Gross(c,tx,run,erp,1000m);
        string debtId=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO debt_debt(id,erp_rider_id,company_id,category,principal,balance,instalment,status,created_at) VALUES(@id,@r,@c,'Advance',300,0,80,'open',datetime('now'))",
            ("@id",debtId),("@r",erp),("@c",company));
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id,created_at) VALUES(@id,@d,@c,'2026-10-01','2026-10-01',300,'synthetic',@id,datetime('now'))",
            ("@id",MoneySchemaFixture.Id()),("@d",debtId),("@c",company));
        var row=SalPayCalc.Calculate(c,tx,actor,run,new JsonObject{["riders"]=new JsonArray(new JsonObject
        {
            ["erpRiderId"]=erp,
            ["level1"]=new JsonArray(new JsonObject{["itemCode"]="advance",["amount"]=300,["accountCode"]="112501",["debtId"]=debtId}),
            ["level2"]=new JsonArray()
        })})["riders"]!.AsArray().OfType<JsonObject>().Single();
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=row.S("id"),["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,Admin(),run,new JsonObject{["payRunRiderIds"]=new JsonArray(row.S("id"))});
        Assert.AreEqual(300m,Acc(c,tx,"112501"));
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debtId))));
        Assert.AreEqual(700m,Liability212103(c,tx));
        tx.Commit();
    }

    // ---- LOW export action ----

    [TestMethod]
    [DataRow(false)]
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
        var row=SalPayCalc.Calculate(c,tx,actor,run,new JsonObject{["riders"]=new JsonArray(new JsonObject
        {
            ["erpRiderId"]=erp,
            ["level1"]=new JsonArray(new JsonObject{["itemCode"]="cash_advance",["amount"]=100,["accountCode"]="112501"}),
            ["level2"]=new JsonArray()
        })})["riders"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual(0m,row.N("salaryNet"));
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=row.S("id"),["amount"]=0})});
        var result=SalSalaryApprove.Approve(c,tx,Admin(),run,new JsonObject{["payRunRiderIds"]=new JsonArray(row.S("id"))});
        var actions=(result["allowedActions"] as JsonArray??[]).Select(n=>n!.GetValue<string>()).ToList();
        Assert.IsFalse(actions.Contains("export_bank_file"));
        tx.Commit();
    }
}
