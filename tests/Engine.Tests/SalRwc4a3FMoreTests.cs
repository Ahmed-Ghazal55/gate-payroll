using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>RWC4a3F more HIGH/MEDIUM — exact SAR; failing test first.</summary>
[TestClass]
public sealed class SalRwc4a3FMoreTests
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

    static JsonObject Rider(string erp,JsonArray? l1=null)=>new(){["erpRiderId"]=erp,["level1"]=l1??new JsonArray(),["level2"]=new JsonArray()};
    static JsonObject Calc(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,string run,JsonObject rider)
        =>SalPayCalc.Calculate(c,tx,actor,run,new JsonObject{["riders"]=new JsonArray(rider)})["riders"]!.AsArray().OfType<JsonObject>().Single();

    // ---- H11 CK bank: approve negative then positive remainder ----

    [TestMethod]
    [DataRow(false)]
    public void H11_ApprovePositiveThenNegative_Live_Payables800Plus100(bool sqlServer)
    {
        // Keeta 1000−fuel200=800; Ninja 100−fuel200=−100; dup fuel200 → combined 900.
        // Approve Keeta 800 then Ninja remainder 100 even though Ninja salary_net≤0 (CK must allow payable).
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        JsonArray Fuel()=>new(new JsonObject{["itemCode"]="fuel",["amount"]=200,["accountCode"]="112511"});
        string keeta=FrozenRun(c,tx,actor,company,"keeta",rider,customize:(f,acc)=>
        {
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=1000,["deductions"]=0,["orders"]=50}];
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-10",["effectiveTo"]="2026-10-31",["appCode"]="ninja"});
        });
        string ninja=FrozenRun(c,tx,actor,company,"ninja",rider,customize:(f,acc)=>
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-09",["appCode"]="keeta"}));
        SalEvidence.Gross(c,tx,ninja,rider,100m);
        string keetaId=Calc(c,tx,actor,keeta,Rider(rider,Fuel())).S("id");
        string ninjaId=Calc(c,tx,actor,ninja,Rider(rider,Fuel())).S("id");
        Assert.AreEqual(900m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT combined_net FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='cleared'",("@r",rider))));
        Assert.AreEqual(-100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT salary_net FROM sal_pay_run_rider WHERE id=@id",("@id",ninjaId))));
        SalDebtConfirm.Confirm(c,tx,actor,keeta,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=keetaId,["amount"]=0})});
        SalDebtConfirm.Confirm(c,tx,actor,ninja,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=ninjaId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,Admin(),keeta,new JsonObject{["payRunRiderIds"]=new JsonArray(keetaId)});
        SalSalaryApprove.Approve(c,tx,Admin(),ninja,new JsonObject{["payRunRiderIds"]=new JsonArray(ninjaId)});
        Assert.AreEqual(800m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount_due FROM pay_payable WHERE source_id=@r",("@r",keetaId))));
        Assert.AreEqual(100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount_due FROM pay_payable WHERE source_id=@r",("@r",ninjaId))));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    public void H11_CrossCompanyConsolidation_409(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string companyA=MoneySchemaFixture.Company(c);
        string companyB=MoneySchemaFixture.Company(c);
        var actor=Admin();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,companyA);EnsurePeriod(c,tx,companyB);
        string rider=MoneySchemaFixture.Id();
        string ninja=FrozenRun(c,tx,actor,companyA,"ninja",rider,customize:(f,acc)=>
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-10",["effectiveTo"]="2026-10-31",["appCode"]="keeta"}));
        string keeta=FrozenRun(c,tx,actor,companyB,"keeta",rider,customize:(f,acc)=>
        {
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=100,["deductions"]=0,["orders"]=10}];
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-09",["appCode"]="ninja"});
        });
        SalEvidence.Gross(c,tx,ninja,rider,100m);
        Calc(c,tx,actor,ninja,Rider(rider));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Calc(c,tx,actor,keeta,Rider(rider))).Status);
        tx.Commit();
    }

    // ---- Linked debt wrong caller account → credit control 112501 ----

    [TestMethod]
    [DataRow(false)]
    public void LinkedAdvance300_Caller112505_Credits112501(bool sqlServer)
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
        // Caller invents 112505 — settle must still credit Advance control 112501.
        var row=Calc(c,tx,actor,run,Rider(erp,new JsonArray(new JsonObject{["itemCode"]="advance",["amount"]=300,["accountCode"]="112505",["debtId"]=debtId})));
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=row.S("id"),["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,Admin(),run,new JsonObject{["payRunRiderIds"]=new JsonArray(row.S("id"))});
        decimal cr112501=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT COALESCE(SUM(credit-debit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='112501')"));
        Assert.AreEqual(300m,cr112501);
        Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debtId))));
        tx.Commit();
    }

    // ---- TGA partial allocation 409 ----

    [TestMethod]
    [DataRow(false)]
    public void AllocateTga_Partial30of100_409(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var actor=Admin();
        string invoice=MoneySchemaFixture.Invoice(c,company,month:"2026-10-01");
        string line=MoneySchemaFixture.Id(),account=MoneySchemaFixture.Id(),tga=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_line(id,invoice_id,period_month,source_row,account_key,erp_platform_account_id,gross,deductions,net,orders) VALUES(@id,@inv,'2026-10-01',1,'acc',@acc,1000,100,900,90)",
            ("@id",line),("@inv",invoice),("@acc",account));
        MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_item(id,line_id,period_month,item_code,amount,bearer_status,platform_deducted,due_before_tga) VALUES(@id,@line,'2026-10-01','tga',100,1,1,1000)",
            ("@id",tga),("@line",line));
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalDistribution.AllocateTga(c,tx,actor,new JsonObject
        {
            ["invoiceId"]=invoice,["itemId"]=tga,
            ["allocations"]=new JsonArray(new JsonObject{["bearer"]="rider",["erpRiderId"]=rider,["amount"]=30})
        })).Status);
        tx.Commit();
    }

    // ---- Debt confirm open hold 409 ----

    [TestMethod]
    [DataRow(false)]
    public void DebtConfirm_OpenHold_409(bool sqlServer)
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
        SalEvidence.Gross(c,tx,ninja,rider,300m);
        string riderId=Calc(c,tx,actor,ninja,Rider(rider)).S("id");
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='open'",("@r",rider)));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalDebtConfirm.Confirm(c,tx,actor,ninja,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})
        })).Status);
        tx.Commit();
    }
}
