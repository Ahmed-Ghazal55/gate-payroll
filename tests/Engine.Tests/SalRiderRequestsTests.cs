using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>U-W2-16: shared rider requests + release with release-mode payload (hold by default;
/// release = recorded two-person decision; payload hash re-checked at release; Chefz consumes Omar RiderAmount). Both providers.</summary>
[TestClass]
public sealed class SalRiderRequestsTests
{
    static ClaimsPrincipal Actor(string? name=null,string role="Admin",string? companyId=null,string? platform=null,params string[] permissions)
    {
        var id=new ClaimsIdentity("synthetic");
        id.AddClaim(new Claim(ClaimTypes.Name,name??MoneySchemaFixture.Id()));
        id.AddClaim(new Claim(ClaimTypes.Role,role));
        id.AddClaim(new Claim("department","FIN"));
        id.AddClaim(new Claim("rankLevel","5"));
        foreach(string p in permissions)id.AddClaim(new Claim("permission",p));
        if(companyId!=null||platform!=null)
        {
            var scope=new JsonObject
            {
                ["sponsorIds"]=companyId!=null?new JsonArray(companyId):new JsonArray(),
                ["accountCodes"]=new JsonArray(),
                ["platforms"]=platform!=null?new JsonArray(platform):new JsonArray(),
                ["categoryCodes"]=new JsonArray()
            };
            id.AddClaim(new Claim("financialScope",scope.ToJsonString()));
        }
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
        string rider,decimal riderAmount=800m,Action<FakeOpsLiveFactsReader,string>? customize=null)
    {
        string run=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month) VALUES(@id,@c,@app,'2026-10-01')",("@id",run),("@c",company),("@app",app));
        var facts=new FakeOpsLiveFactsReader();
        foreach(string kind in AllKinds)facts.Rows[kind]=[];
        string acc=MoneySchemaFixture.Id();
        string primary=app switch{"ninja"=>"ninja_daily","keeta"=>"keeta_invoice_row","hs"=>"hs_ops","amazon"=>"platform_ops","chefz"=>"chefz_settlement","jahez"=>"jahez_day",_=>"assignment"};
        facts.Rows[primary]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["erpPlatformAccountId"]=acc,["riderProfileId"]=rider,["erpRiderId"]=rider,["orders"]=1,["reportDate"]="2026-10-01",["workDate"]="2026-10-01",["gross"]=riderAmount,["RiderAmount"]=riderAmount,["GrossEarnings"]=1000m,["CompanyAmount"]=200m}];
        facts.Rows["platform_account"]=[new(){["id"]=acc,["externalAccountId"]="A1",["code"]="A1"}];
        facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["actualRiderProfileId"]=rider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31",["appCode"]=app}];
        facts.Rows["scheme"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["schemeCode"]="commission",["source"]="pay_settings"}];
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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SharedRiderHoldByDefault_ReleaseWithReleaseModePayload_ReChecksHashOnRelease(bool sqlServer)
    {
        // Ninja net 600 + Keeta assignment → held by default (open). Release requires 2-person decision bound to releaseMode + net hash.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var maker=Actor(MoneySchemaFixture.Id());
        var checker=Actor(MoneySchemaFixture.Id());
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string ninja=FrozenRun(c,tx,maker,company,"ninja",rider,customize:(f,_)=>
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=rider,
                ["effectiveFrom"]="2026-10-12",["effectiveTo"]="2026-10-31",["appCode"]="keeta"}));
        SalEvidence.Gross(c,tx,ninja,rider,600m);
        var calc=SalPayCalc.Calculate(c,tx,maker,ninja,new JsonObject{["riders"]=new JsonArray(new JsonObject{["erpRiderId"]=rider})});
        string riderRowId=calc["riders"]![0]!["id"]!.GetValue<string>();
        Assert.AreEqual(600m,calc["riders"]![0]!["salaryNet"]!.GetValue<decimal>());

        string holdId=(string)MoneySchemaFixture.Scalar(c,tx,"SELECT id FROM sal_shared_rider_hold WHERE erp_rider_id=@r AND status='open'",("@r",rider))!;
        var list=SalRiderRequests.List(c,tx,maker,null,null,"2026-10-01",company,null,rider);
        var holdItem=list["items"]!.AsArray().OfType<JsonObject>().Single(i=>i.S("id")==holdId);
        Assert.AreEqual("shared_hold",holdItem.S("requestType"));
        CollectionAssert.Contains(holdItem["allowedActions"]!.AsArray().Select(a=>a!.ToString()).ToList(),"release");

        // Request release with releaseMode = bank_file_separate
        string reason="وصلت فاتورة كيتا يدويًا";
        var opened=SalRiderRequests.RequestRelease(c,tx,maker,holdId,new JsonObject{["releaseMode"]="bank_file_separate",["reason"]=reason});
        Assert.AreEqual("pending",opened.S("status"));
        Assert.AreEqual(600m,opened.N("amountSar"));

        // Release before second-person decision → 403
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>SalRiderRequests.Release(c,tx,checker,holdId,
            new JsonObject{["releaseMode"]="bank_file_separate",["reason"]=reason})).Status);

        // Checker approves the dual request
        ApprovalsArea.Decide(c,tx,checker,opened.S("id"),new JsonObject{["decision"]="approve",["note"]="موافقة مراجع ثانٍ"});

        // Release with altered releaseMode ("individual_transfer" instead of "bank_file_separate") → 403 hash mismatch
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>SalRiderRequests.Release(c,tx,checker,holdId,
            new JsonObject{["releaseMode"]="individual_transfer",["reason"]=reason})).Status);

        // Alter net on the run after approval (600 -> 650) → 403 hash mismatch
        MoneySchemaFixture.Exec(c,tx,"UPDATE sal_pay_run_rider SET salary_net=650 WHERE id=@id",("@id",riderRowId));
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>SalRiderRequests.Release(c,tx,checker,holdId,
            new JsonObject{["releaseMode"]="bank_file_separate",["reason"]=reason})).Status);

        // Restore approved net 600 → release succeeds
        MoneySchemaFixture.Exec(c,tx,"UPDATE sal_pay_run_rider SET salary_net=600 WHERE id=@id",("@id",riderRowId));
        var released=SalRiderRequests.Release(c,tx,checker,holdId,
            new JsonObject{["releaseMode"]="bank_file_separate",["reason"]=reason});
        Assert.IsTrue(released.B("released"));
        Assert.AreEqual("bank_file_separate",released.S("releaseMode"));
        Assert.AreEqual("cleared",released.S("status"));

        // Salary approval now succeeds and creates payable of 600 SAR
        SalDebtConfirm.Confirm(c,tx,maker,ninja,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderRowId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,checker,ninja,new JsonObject{["payRunRiderIds"]=new JsonArray(riderRowId)});
        Assert.AreEqual(600m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount_due FROM pay_payable WHERE source_id=@r",("@r",riderRowId))));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HoldRequest_ApproveBlocksBankEligible_ReleaseModesControlBankEligibility(bool sqlServer)
    {
        // Two riders with approved salary 450 and 520. Hold request approved → bank_eligible=0.
        // Rider 1 released with individual_transfer → bank_eligible stays 0.
        // Rider 2 released with bank_file_separate → bank_eligible restored to 1.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var maker=Actor(MoneySchemaFixture.Id());
        var checker=Actor(MoneySchemaFixture.Id());
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);

        string r1=MoneySchemaFixture.Id(),r2=MoneySchemaFixture.Id();
        string run=FrozenRun(c,tx,maker,company,"ninja",r1,customize:(f,acc)=>
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["actualRiderProfileId"]=r2,
                ["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31",["appCode"]="ninja"}));
        SalEvidence.Gross(c,tx,run,r1,450m);
        SalEvidence.Gross(c,tx,run,r2,520m);
        var calc=SalPayCalc.Calculate(c,tx,maker,run,new JsonObject{["riders"]=new JsonArray(new JsonObject{["erpRiderId"]=r1},new JsonObject{["erpRiderId"]=r2})});
        string pr1=calc["riders"]![0]!["id"]!.GetValue<string>();
        string pr2=calc["riders"]![1]!["id"]!.GetValue<string>();
        SalDebtConfirm.Confirm(c,tx,maker,run,new JsonObject{["rows"]=new JsonArray(
            new JsonObject{["payRunRiderId"]=pr1,["amount"]=0},
            new JsonObject{["payRunRiderId"]=pr2,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,checker,run,new JsonObject{["payRunRiderIds"]=new JsonArray(pr1,pr2)});
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT bank_eligible FROM sal_pay_run_rider WHERE id=@id",("@id",pr1)));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT bank_eligible FROM sal_pay_run_rider WHERE id=@id",("@id",pr2)));

        // Create and approve hold requests for both riders
        var h1=SalRiderRequests.Create(c,tx,maker,new JsonObject
        {
            ["companyId"]=company,["erpRiderId"]=r1,["periodMonth"]="2026-10-01",["appCode"]="ninja",
            ["kind"]="hold",["reasonCode"]="custody_check",["note"]="فحص عهدة"
        });
        var h2=SalRiderRequests.Create(c,tx,maker,new JsonObject
        {
            ["companyId"]=company,["erpRiderId"]=r2,["periodMonth"]="2026-10-01",["appCode"]="ninja",
            ["kind"]="hold",["reasonCode"]="custody_check",["note"]="فحص عهدة ٢"
        });
        SalRiderRequests.Approve(c,tx,checker,h1.S("id"),new JsonObject{["note"]="معتمد"});
        SalRiderRequests.Approve(c,tx,checker,h2.S("id"),new JsonObject{["note"]="معتمد"});
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,tx,"SELECT bank_eligible FROM sal_pay_run_rider WHERE id=@id",("@id",pr1)));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,tx,"SELECT bank_eligible FROM sal_pay_run_rider WHERE id=@id",("@id",pr2)));

        // Release h1 with individual_transfer
        var req1=SalRiderRequests.RequestRelease(c,tx,maker,h1.S("id"),new JsonObject{["releaseMode"]="individual_transfer",["reason"]="تحويل فردي من الخزينة"});
        ApprovalsArea.Decide(c,tx,checker,req1.S("id"),new JsonObject{["decision"]="approve",["note"]="موافق"});
        var rel1=SalRiderRequests.Release(c,tx,checker,h1.S("id"),new JsonObject{["releaseMode"]="individual_transfer",["reason"]="تحويل فردي من الخزينة"});
        Assert.AreEqual("individual_transfer",rel1.S("releaseMode"));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,tx,"SELECT bank_eligible FROM sal_pay_run_rider WHERE id=@id",("@id",pr1)));

        // Release h2 with bank_file_separate
        var req2=SalRiderRequests.RequestRelease(c,tx,maker,h2.S("id"),new JsonObject{["releaseMode"]="bank_file_separate",["reason"]="إدراج في ملف بنك منفصل"});
        ApprovalsArea.Decide(c,tx,checker,req2.S("id"),new JsonObject{["decision"]="approve",["note"]="موافق"});
        var rel2=SalRiderRequests.Release(c,tx,checker,h2.S("id"),new JsonObject{["releaseMode"]="bank_file_separate",["reason"]="إدراج في ملف بنك منفصل"});
        Assert.AreEqual("bank_file_separate",rel2.S("releaseMode"));
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT bank_eligible FROM sal_pay_run_rider WHERE id=@id",("@id",pr2)));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ChefzDeductionAndHoldRequests_ConsumeOmarRiderAmount800_NeverRebuildCommission(bool sqlServer)
    {
        // Omar Chefz RiderAmount = 800 (GrossEarnings 1000, CompanyAmount 200). Approved deduction request = 75.
        // Calc consumes 800 with commission = 0, applies 75 L1 → net = 725.
        using var c=Open(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var maker=Actor(MoneySchemaFixture.Id());
        var checker=Actor(MoneySchemaFixture.Id());
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();

        var ded=SalRiderRequests.Create(c,tx,maker,new JsonObject
        {
            ["companyId"]=company,["erpRiderId"]=rider,["periodMonth"]="2026-10-01",["appCode"]="chefz",
            ["kind"]="deduction",["amount"]=75m,["reasonCode"]="damage",["note"]="خصم تلفيات شيفز"
        });
        var approvedDed=SalRiderRequests.Approve(c,tx,checker,ded.S("id"),new JsonObject{["note"]="معتمد للمستوى الأول"});
        Assert.AreEqual("approved",approvedDed.S("status"));

        string run=FrozenRun(c,tx,maker,company,"chefz",rider,riderAmount:800m);
        var calc=SalPayCalc.Calculate(c,tx,maker,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject{["erpRiderId"]=rider})
        });
        var row=calc["riders"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual(800m,row.N("gross"));
        Assert.AreEqual(0m,row.N("commission"));
        Assert.AreEqual(75m,row.N("level1Total"));
        Assert.AreEqual(725m,row.N("salaryNet"));

        // Direct hold on Chefz rider binds to the 725 SAR net without rebuilding commission
        var hold=SalRiderRequests.Create(c,tx,checker,new JsonObject
        {
            ["companyId"]=company,["erpRiderId"]=rider,["periodMonth"]="2026-10-01",["appCode"]="chefz",
            ["kind"]="hold",["reasonCode"]="audit",["note"]="مراجعة شيفز",["directApprove"]=true
        });
        Assert.AreEqual("approved",hold.S("status"));
        var relReq=SalRiderRequests.RequestRelease(c,tx,maker,hold.S("id"),new JsonObject{["releaseMode"]="bank_file_separate",["reason"]="اكتملت مراجعة شيفز"});
        Assert.AreEqual(725m,relReq.N("amountSar"));
        tx.Commit();
    }
}
