using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>U-W2-15c debt confirm before approval + U-W2-15b final approve, bypass, statement snapshot, negative-net debt.</summary>
[TestClass]
public sealed class SalDebtConfirmApproveTests
{
    static ClaimsPrincipal Actor(string name,params string[] permissions)
    {
        var id=new ClaimsIdentity("synthetic");
        id.AddClaim(new Claim(ClaimTypes.Name,name));
        // Admin skips financial-scope sponsor checks used by ops capture (same as SalPayCalcTests).
        id.AddClaim(new Claim(ClaimTypes.Role,"Admin"));
        foreach(string p in permissions)id.AddClaim(new Claim("permission",p));
        return new ClaimsPrincipal(id);
    }

    static ClaimsPrincipal Admin(string name)
    {
        var id=new ClaimsIdentity("synthetic");
        id.AddClaim(new Claim(ClaimTypes.Name,name));
        id.AddClaim(new Claim(ClaimTypes.Role,"Admin"));
        return new ClaimsPrincipal(id);
    }

    static System.Data.Common.DbConnection Open(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,null,"IF OBJECT_ID(N'dbo.records',N'U') IS NULL CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL);");
        else
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
        SchemaSalInvoice.Ensure(c);SchemaSalDistribution.Ensure(c);SchemaSalPayRun.Ensure(c);
        SchemaHrMudad.Ensure(c);SchemaSalMudad.Ensure(c);SchemaDebt.Ensure(c);
        SchemaOperationCapture.Ensure(c);SchemaSalStatement.Ensure(c);
        SchemaFinPeriodBalance.Ensure(c);
        return c;
    }

    static string FrozenRun(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,string company,string app="ninja",string? erpRiderId=null)
    {
        string run=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month) VALUES(@id,@c,@app,'2026-10-01')",("@id",run),("@c",company),("@app",app));
        var facts=new FakeOpsLiveFactsReader();
        foreach(string kind in new[]{"ninja_daily","ninja_daily_deduction","platform_account","assignment","housing","fuel","vehicle","scheme","eligibility",
            "keeta_invoice_row","hs_ops","hs_wallet","platform_ops","chefz_settlement","jahez_day"})
            facts.Rows[kind]=[];
        string primary=app switch{"ninja"=>"ninja_daily","keeta"=>"keeta_invoice_row","hs"=>"hs_ops","amazon"=>"platform_ops","chefz"=>"chefz_settlement","jahez"=>"jahez_day",_=>"assignment"};
        string account=MoneySchemaFixture.Id(),rider=erpRiderId??MoneySchemaFixture.Id();
        facts.Rows[primary]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["riderProfileId"]=rider,["erpRiderId"]=rider,["orders"]=1,["reportDate"]="2026-10-01"}];
        if(app=="ninja")facts.Rows["ninja_daily_deduction"]=[];
        facts.Rows["platform_account"]=[new(){["id"]=account,["externalAccountId"]="A1",["code"]="A1"}];
        facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=rider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31"}];
        var reasons=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var kv in facts.Rows)if(kv.Value.Count==0)reasons[kv.Key]="none";
        OpsSnapshotCapture.Capture(c,tx,actor,run,facts,reasons);
        OpsSnapshotCapture.Freeze(c,tx,actor,run);
        return run;
    }

    static (string run,string riderId,string erp,string company) SeedCalc(
        System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,
        string company,decimal gross=500,decimal instalment=0,string app="ninja",string? erpRiderId=null)
    {
        string period=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) SELECT @id,@c,'2026-10-01',1,datetime('now') WHERE NOT EXISTS(SELECT 1 FROM fin_accounting_period WHERE company_id=@c AND period_month='2026-10-01')",("@id",period),("@c",company));
        string erp=erpRiderId??MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,actor,company,app,erp);
        var calc=SalEvidence.Calculate(c,tx,actor,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=erp,["gross"]=gross,["commission"]=0,
                ["oldDebtInstalment"]=instalment,
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        });
        return (run,calc["riders"]![0]!["id"]!.GetValue<string>(),erp,company);
    }

    static string OpenDebtWithBalance(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,
        string company,string erp,decimal balance,string category="LegacyOpeningDebt")
    {
        string debtId=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO debt_debt(id,erp_rider_id,company_id,category,principal,balance,instalment,status,created_at) VALUES(@id,@r,@c,@cat,@p,0,@i,'open',datetime('now'))",
            ("@id",debtId),("@r",erp),("@c",company),("@cat",category),("@p",balance),("@i",Math.Min(balance,80m)));
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id,created_at) VALUES(@id,@d,@c,'2026-10-01','2026-10-01',@a,'synthetic',@id,datetime('now'))",
            ("@id",MoneySchemaFixture.Id()),("@d",debtId),("@c",company),("@a",balance));
        return debtId;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DebtConfirm_ZeroDefers_NoMovement_ElseMovementJournalOneTx(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var maker=Actor(MoneySchemaFixture.Id(),"pay.manage","pay.approve");
        using var tx=c.BeginTransaction();
        var (run,riderId,erp,_)=SeedCalc(c,tx,maker,company,gross:400,instalment:100);

        var deferred=SalDebtConfirm.Confirm(c,tx,maker,run,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})
        });
        Assert.AreEqual("deferred",deferred["rows"]![0]!["debtConfirmStatus"]!.GetValue<string>());
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM debt_debt_movement WHERE pay_run_rider_id=@id",("@id",riderId)));

        // Second rider needs its own frozen assignment on a fresh run (assigned riders only).
        MoneySchemaFixture.Exec(c,tx,"UPDATE sal_pay_run SET status=4 WHERE id=@id",("@id",run));
        string erp2=MoneySchemaFixture.Id();
        var (run2,rider2,_,_)=SeedCalc(c,tx,maker,company,gross:500,instalment:80,erpRiderId:erp2);
        string debtId=OpenDebtWithBalance(c,tx,company,erp2,200);
        // Confirm against the second run
        run=run2;

        var confirmed=SalDebtConfirm.Confirm(c,tx,maker,run,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=rider2,["amount"]=80,["debtId"]=debtId})
        });
        Assert.AreEqual("confirmed",confirmed["rows"]![0]!["debtConfirmStatus"]!.GetValue<string>());
        Assert.AreEqual(420m,confirmed["rows"]![0]!["salaryNet"]!.GetValue<decimal>());
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM debt_debt_movement WHERE pay_run_rider_id=@id AND source_type='salary_deduction'",("@id",rider2)));
        Assert.AreEqual(120m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debtId))));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM fin_journal WHERE purpose='debt_confirm' AND status=2"));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Approve_RefusesUntilDebtConfirm_SnapshotAndNegativeDebt(bool sqlServer)
    {
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var maker=Actor(MoneySchemaFixture.Id(),"pay.manage","pay.approve");
        var checker=Actor(MoneySchemaFixture.Id(),"pay.manage","pay.approve");
        using var tx=c.BeginTransaction();
        var (run,riderId,_,_)=SeedCalc(c,tx,maker,company,gross:200,instalment:0);

        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalSalaryApprove.Approve(c,tx,checker,run,new JsonObject
        {
            ["payRunRiderIds"]=new JsonArray(riderId)
        })).Status);

        SalDebtConfirm.Confirm(c,tx,maker,run,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})
        });

        var approved=SalSalaryApprove.Approve(c,tx,checker,run,new JsonObject
        {
            ["payRunRiderIds"]=new JsonArray(riderId)
        });
        Assert.IsFalse(string.IsNullOrEmpty(approved["rows"]![0]!["salaryApprovedAt"]!.GetValue<string>()));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM sal_rider_statement_snapshot WHERE pay_run_rider_id=@id",("@id",riderId)));

        MoneySchemaFixture.Exec(c,tx,"UPDATE sal_pay_run SET status=4 WHERE id=@id",("@id",run));
        string erpNeg=MoneySchemaFixture.Id();
        var (runNeg,riderNeg,_,_)=SeedCalc(c,tx,maker,company,gross:50,instalment:0,erpRiderId:erpNeg);
        // Overwrite net to negative for carryover-at-approve path.
        MoneySchemaFixture.Exec(c,tx,"UPDATE sal_pay_run_rider SET salary_net=-70,gross=50 WHERE id=@id",("@id",riderNeg));
        Assert.AreEqual(-70m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT salary_net FROM sal_pay_run_rider WHERE id=@id",("@id",riderNeg))));
        SalDebtConfirm.Confirm(c,tx,maker,runNeg,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderNeg,["amount"]=0})
        });
        var negApproved=SalSalaryApprove.Approve(c,tx,checker,runNeg,new JsonObject
        {
            ["payRunRiderIds"]=new JsonArray(riderNeg)
        });
        Assert.IsTrue(negApproved["rows"]![0]!.AsObject().B("negativeDebtCreated"));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM debt_debt WHERE category='NegativeSalaryCarryover' AND erp_rider_id=@r",("@r",erpNeg)));
        Assert.AreEqual(70m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE category='NegativeSalaryCarryover' AND erp_rider_id=@r",("@r",erpNeg))));
        tx.Commit();
    }

    [TestMethod]
    public void OrdinarySalary_SelfApprovalBlocked_Ghazal1124()
    {
        // Ghazal 11-24: preparer cannot approve/post the same salary sheet.
        using var c=Open(false);
        string company=MoneySchemaFixture.Company(c);
        string user=MoneySchemaFixture.Id();
        var actor=Actor(user,"pay.manage","pay.approve");
        using var tx=c.BeginTransaction();
        var (run,riderId,_,_)=SeedCalc(c,tx,actor,company,gross:300,instalment:0);
        SalDebtConfirm.Confirm(c,tx,actor,run,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>SalSalaryApprove.Approve(c,tx,actor,run,new JsonObject
        {
            ["payRunRiderIds"]=new JsonArray(riderId)
        })).Status);
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM pay_payable WHERE source_id=@r",("@r",riderId)));
        tx.Commit();
    }
}
