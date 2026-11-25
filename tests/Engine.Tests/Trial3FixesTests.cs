using System.Security.Claims;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>TRIAL3: Grok TRIAL2 highs + T11 eligibility capture/eval (failing tests first).</summary>
[TestClass]
public sealed class Trial3FixesTests
{
    static ClaimsPrincipal Actor()=>UserAccess.Principal(new JsonObject{["username"]=MoneySchemaFixture.Id(),["role"]="Admin"});

    static JsonObject Global()=>new(){["id"]="synthetic-global",["status"]="approved",["effectiveFrom"]="2026-01-01",["data"]=PaySettings.Defaults()};
    static JsonObject SchemeDoc(string app,string type="daily_commission")=>new(){["id"]="synthetic-scheme",["app"]=app,["period"]="2026-10",["status"]="approved",["data"]=PayFixture.Scheme(type)};
    static PayFacts Facts(string app)=>new("synthetic-rider",app,"2026-10","sponsored_private",100,1000,100,30,"private",null,null);

    [TestMethod]
    [DataRow("jahez")]
    [DataRow("chefz")]
    [DataRow("hs")]
    public void T11_Capture_ZeroEligibilitySucceeds_ForAppsWithoutEligibility(string app)
    {
        var c=MoneySchemaFixture.Payments(false);
        using(c)
        {
            SchemaSalInvoice.Ensure(c);SchemaSalPayRun.Ensure(c);SchemaOperationCapture.Ensure(c);
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
            string company=MoneySchemaFixture.Company(c);
            string run=MoneySchemaFixture.Run(c,company,app,"2026-10-01");
            var facts=new FakeOpsLiveFactsReader();
            string account=MoneySchemaFixture.Id(),rider=MoneySchemaFixture.Id();
            string appKind=app switch
            {
                "jahez"=>"jahez_day",
                "chefz"=>"chefz_settlement",
                "hs"=>"hs_ops",
                _=>throw new InvalidOperationException(app)
            };
            facts.Rows[appKind]=[new(){["id"]=MoneySchemaFixture.Id(),["reportDate"]="2026-10-01",["platformRiderAccountId"]=account,["riderProfileId"]=rider}];
            if(app=="hs")facts.Rows["hs_wallet"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["amount"]=1m}];
            facts.Rows["platform_account"]=[new(){["id"]=account,["externalAccountId"]="CAP-1",["code"]="A1"}];
            facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=rider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31"}];
            facts.Rows["housing"]=[];
            facts.Rows["fuel"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["liters"]=1m,["totalInclVat"]=1m,["month"]="2026-10-01",["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31"}];
            facts.Rows["vehicle"]=[new(){["id"]=MoneySchemaFixture.Id(),["assetNumber"]="V1",["plateNumberAr"]="أ",["actualRiderProfileId"]=rider,["erpRiderId"]=rider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-15"}];
            facts.Rows["scheme"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["schemeCode"]="commission"}];
            facts.Rows["eligibility"]=[]; // T11: empty is OK — eligibility is not required
            using var tx=c.BeginTransaction();
            var captured=OpsSnapshotCapture.Capture(c,tx,Actor(),run,facts,zeroKindReasons:new Dictionary<string,string>{["housing"]="none"});
            Assert.IsTrue(captured.N("snapshotCount")>=1);
            Assert.AreEqual(0L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(1) FROM fin_operation_capture_manifest WHERE operation_id=@run AND required_kind='eligibility'",("@run",run))!));
        }
    }

    [TestMethod]
    [DataRow("keeta")]
    [DataRow("ninja")]
    public void T11_Capture_StillRequiresEligibility_ForKeetaAndNinja(string app)
    {
        var c=MoneySchemaFixture.Payments(false);
        using(c)
        {
            SchemaSalInvoice.Ensure(c);SchemaSalPayRun.Ensure(c);SchemaOperationCapture.Ensure(c);
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
            string company=MoneySchemaFixture.Company(c);
            string run=MoneySchemaFixture.Run(c,company,app,"2026-10-01");
            long required=Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_operation_required_kind WHERE operation_type='pay_run' AND app_code=@a AND required_kind='eligibility'",("@a",app))!);
            Assert.AreEqual(1L,required);
        }
    }

    [TestMethod]
    [DataRow("jahez")]
    [DataRow("chefz")]
    [DataRow("hs")]
    public void T11_Evaluate_SkipsEligibilityBlock_WhenAppHasNoEligibility(string app)
    {
        var scheme=SchemeDoc(app);scheme["data"]!["eligibilitySetId"]="synthetic-rules";
        // Missing eligibility document would fail for Keeta/Ninja; T11 apps must ignore it.
        var result=PayEvaluator.Evaluate(Facts(app),scheme,eligibility:null,Global());
        CollectionAssert.DoesNotContain(result.Blockers.ToArray(),"eligibility_missing");
    }

    [TestMethod]
    public void T13_FreelancerRow_IsNotMarkedAvailableFromOmar()
    {
        var rates=new[]
        {
            new JsonObject
            {
                ["id"]=2,["operatingCityId"]="city-jeddah",["cityCode"]="jeddah",["cityNameAr"]="جدة",
                ["effectiveFrom"]="2026-11-01",["dailyRate"]=10m,["kind"]="percentage",["kindCode"]=2
            }
        };
        var result=PlatformCommissionLiveQuery.FromMappedRates("chefz",rates,new DateOnly(2026,11,23));
        var freelancer=result["schemeRows"]!.AsArray().OfType<JsonObject>().Single(r=>r.S("identifierType")=="Freelancer");
        Assert.IsFalse(freelancer.B("available"));
        Assert.AreEqual("غير متوفر في نظام عمر",freelancer.S("message"));
        Assert.IsTrue(result["missing"]!.AsArray().OfType<JsonObject>().Any(m=>m.S("identifierType")=="Freelancer"||m.S("message").Contains("Freelancer",StringComparison.OrdinalIgnoreCase)||m.S("omarFile").Length>0));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void T6_ReleaseSafetyCapture_DisposesRecordsReaderBeforePayRunCount(bool sqlServer)
    {
        // SQL Server throws if a second command runs while a DataReader is still open on the same connection.
        ChartOfAccountsApiTests_WithStore(store=>{
            store.Write((c,tx)=>{Store.Save(c,"rider",new(){["id"]="r1",["iqama"]="2000000001",["name"]="Synthetic"},tx);return true;});
            var snap=store.Read(c=>ReleaseSafety.Capture(c));
            Assert.IsNotNull(snap);
        },sqlServer);
    }

    // Local copy of CoA fixture pattern so this SQL Server Capture row actually runs in sqltest (Grok MEDIUM / T6).
    static void ChartOfAccountsApiTests_WithStore(Action<Store> body,bool sqlServer=false)
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-rel-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            if(!sqlServer){body(new Store(dir));return;}
            string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
            if(string.IsNullOrWhiteSpace(configured))Assert.Inconclusive("PAYROLL_TEST_SQL_CONNECTION not set; isolated SQL Server required.");
            var builder=new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(configured){Pooling=false};
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(builder.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            builder.InitialCatalog="master";
            string db="GateRel_"+Guid.NewGuid().ToString("N");
            using(var admin=new Microsoft.Data.SqlClient.SqlConnection(builder.ToString())){admin.Open();SchemaSql.Exec(admin,null,$"CREATE DATABASE [{db}]");}
            builder.InitialCatalog=db;
            try{body(new Store(dir,builder.ToString()));}
            finally
            {
                using var admin=new Microsoft.Data.SqlClient.SqlConnection(new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(configured){InitialCatalog="master",Pooling=false}.ToString());
                admin.Open();
                SchemaSql.Exec(admin,null,$"ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]");
            }
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    [TestMethod]
    public void Coa1_A9_SalaryFuelAccountIs512104()
    {
        // BR §22.9: company share of salary-rider fuel → مصروف محروقات 512104 (not cash allowance 512116).
        string expected="512104";
        Assert.IsTrue(string.Equals(PayExpenseAccounts.SalaryFuel,expected,StringComparison.Ordinal),PayExpenseAccounts.SalaryFuel);
    }

    [TestMethod]
    public void Coa1_A10_CompensationMapsTo512118()
    {
        using var f=new PayFixture();
        ConfigLists.Seed(f.Store);
        var cur=f.Store.Read(c=>ConfigLists.Current(c,null,"pay_adjustment_types","compensation"));
        Assert.IsNotNull(cur);
        Assert.AreEqual("512118",cur!["values"]!.AsObject().S("account"));
    }

    [TestMethod]
    public void Coa1_A15_UnknownCompanyInterCompanyAccountReturns409()
    {
        using var f=new PayFixture();
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>SalBankFiles.InterCompanyAccountCode(c,null,MoneySchemaFixture.Id())));
        Assert.AreEqual(409,err.Status);
        StringAssert.Contains(err.Message,"الجاري");
    }

    [TestMethod]
    public void Coa1_A11_DeductionApprove_PostsDr112505_Cr421302()
    {
        // BR §29: approve supervisor/admin deduction → Dr 112505 / Cr 421302 (before salary recovery).
        var c=MoneySchemaFixture.Payments(false);
        using(c)
        {
            SchemaSalPayRun.Ensure(c);SchemaFinPeriodBalance.Ensure(c);SchemaAuthCatalog.Ensure(c);
            if(!SchemaSql.IsSql(c))
                MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
            string company=MoneySchemaFixture.Company(c);
            MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-10-01',1,datetime('now'))",
                ("@id",MoneySchemaFixture.Id()),("@c",company));
            var maker=Actor();var checker=Actor();
            using var tx=c.BeginTransaction();
            var ded=SalRiderRequests.Create(c,tx,maker,new JsonObject
            {
                ["companyId"]=company,["erpRiderId"]=MoneySchemaFixture.Id(),["periodMonth"]="2026-10-01",["appCode"]="ninja",
                ["kind"]="deduction",["amount"]=50m,["reasonCode"]="admin",["note"]="خصم إداري مصطنع"
            });
            SalRiderRequests.Approve(c,tx,checker,ded.S("id"),new JsonObject{["note"]="معتمد"});
            decimal debit=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"""
                SELECT COALESCE(SUM(pb.debit),0) FROM fin_period_balance pb
                JOIN fin_account a ON a.id=pb.account_id WHERE a.code='112505' AND pb.company_id=@c
                """,("@c",company))!);
            decimal credit=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"""
                SELECT COALESCE(SUM(pb.credit),0) FROM fin_period_balance pb
                JOIN fin_account a ON a.id=pb.account_id WHERE a.code='421302' AND pb.company_id=@c
                """,("@c",company))!);
            Assert.AreEqual(50m,debit);
            Assert.AreEqual(50m,credit);
            tx.Commit();
        }
    }

    [TestMethod]
    public void Coa1_B38_TrialBalance_IncludesFinPeriodBalance()
    {
        // Reports must read fin.period_balance together with the legacy journal store.
        var c=MoneySchemaFixture.Payments(false);
        using(c)
        {
            SchemaFinPeriodBalance.Ensure(c);
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
            string companyId=FinancialCore.Sponsors[0].Id;
            MoneySchemaFixture.Exec(c,null,"INSERT INTO hr_company(id,code,unified_number,name_ar,status,created_at) SELECT @id,'BW','7015658094','Synthetic BW',1,datetime('now') WHERE NOT EXISTS(SELECT 1 FROM hr_company WHERE id=@id)",("@id",companyId));
            string period=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-10-01',1,datetime('now'))",("@id",period),("@c",companyId));
            string debit=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE code='512101'")!;
            string credit=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE code='212103'")!;
            var draft=new JournalPosting.Draft(
                Guid.Parse(companyId),Guid.Parse(period),new DateOnly(2026,10,1),new DateOnly(2026,10,15),
                "trial3_b38",Guid.NewGuid(),"accrual",0,false,"synthetic",
                [
                    new(Guid.Parse(debit),SchemaOrgCostCentre.NoneId,"ninja",77m,0m,"مصروف راتب W2"),
                    new(Guid.Parse(credit),SchemaOrgCostCentre.NoneId,"ninja",0m,77m,"مستحقات مناديب")
                ]);
            JournalPosting.Post(c,null,draft);
            var admin=UserAccess.Principal(new JsonObject{["username"]="synthetic-admin",["role"]="Admin"});
            var report=FinanceViews.TrialBalance(c,admin,companyId,"2026-10","2026-10","target");
            var company=report["companies"]!.AsArray().OfType<JsonObject>().Single(x=>x.S("sponsorId")==companyId);
            var row512101=company["rows"]!.AsArray().OfType<JsonObject>().Single(r=>r.S("code")=="512101");
            Assert.IsTrue(row512101.N("periodDebit")>=77m,row512101.ToJsonString());
        }
    }
}
