using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>TRIAL7: bank-file never invents rider meta; self-approve claim + money-out by posting lines;
/// fold grok-TRIAL6 HIGH timing/reconciledBalance/open_original/carry.</summary>
[TestClass]
public sealed class Trial7FixesTests
{
    static ClaimsPrincipal Maker=>PayFixture.Actor("synthetic-trial7-maker","Admin");
    static ClaimsPrincipal Checker=>PayFixture.Actor("synthetic-trial7-checker","Admin");
    static ClaimsPrincipal Actor(string name,string role="Admin",JsonArray? custom=null)
        =>UserAccess.Principal(new JsonObject
        {
            ["username"]=name,["role"]=role,
            ["customPermissions"]=custom??new JsonArray()
        });

    static ClaimsPrincipal BankActor(string? name=null)
    {
        var id=new ClaimsIdentity("synthetic");
        id.AddClaim(new Claim(ClaimTypes.Name,name??MoneySchemaFixture.Id()));
        id.AddClaim(new Claim(ClaimTypes.Role,"Admin"));
        id.AddClaim(new Claim("department","FIN"));
        id.AddClaim(new Claim("rankLevel","5"));
        foreach(string p in new[]{"pay.view","pay.manage","pay.approve","finance.bankfile.export","salaries.bankfile.mark_sent",
            ApprovalsArea.DualBankFile,ApprovalsArea.DualSharedRiderRelease,"approvals.decide"})
            id.AddClaim(new Claim("permission",p));
        return new ClaimsPrincipal(id);
    }

    static DbConnection OpenBank(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,null,"IF OBJECT_ID(N'dbo.records',N'U') IS NULL CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL);");
        else
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
        SchemaTreasury.Ensure(c);
        SchemaSalInvoice.Ensure(c);SchemaSalDistribution.Ensure(c);
        SchemaSalPayRun.Ensure(c);SchemaHrMudad.Ensure(c);SchemaSalMudad.Ensure(c);SchemaDebt.Ensure(c);
        SchemaOperationCapture.Ensure(c);SchemaSalStatement.Ensure(c);SchemaFinPeriodBalance.Ensure(c);
        SchemaBankFile.Ensure(c);
        SchemaAuthCatalog.Ensure(c);SchemaApprovals.Ensure(c);
        SalRiderRequests.Ensure(c);
        return c;
    }

    static string SeedCompany(DbConnection c,string code)
    {
        string id=MoneySchemaFixture.Id();
        string unified="7"+System.Security.Cryptography.RandomNumberGenerator.GetInt32(0,1_000_000_000).ToString("D9");
        MoneySchemaFixture.Exec(c,null,"INSERT INTO hr_company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,@code,@u,@name,1,datetime('now'))",
            ("@id",id),("@code",code),("@u",unified),("@name","شركة "+code));
        return id;
    }
    static string SeedRegistration(DbConnection c,string companyId,string crNumber)
    {
        string id=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO hr_commercial_registration(id,company_id,cr_number,status,created_at) VALUES(@id,@c,@cr,1,datetime('now'))",
            ("@id",id),("@c",companyId),("@cr",crNumber));
        return id;
    }
    static string SeedBank(DbConnection c,string companyId,string registrationId,string accountCode="111201")
    {
        string id=MoneySchemaFixture.Id();
        string accId=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE code=@code",("@code",accountCode))!;
        MoneySchemaFixture.Exec(c,null,"INSERT INTO treasury_bank_account(id,company_id,registration_id,code,name_ar,account_id,status,created_at) VALUES(@id,@c,@r,@code,@name,@acc,1,datetime('now'))",
            ("@id",id),("@c",companyId),("@r",registrationId),("@code","BNK-"+id[^12..]),("@name","بنك الرواتب"),("@acc",accId));
        return id;
    }
    static void EnsurePeriod(DbConnection c,DbTransaction tx,string company,string month="2026-10-01")
    {
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) SELECT @id,@c,@m,1,datetime('now') WHERE NOT EXISTS(SELECT 1 FROM fin_accounting_period WHERE company_id=@c AND period_month=@m)",
            ("@id",MoneySchemaFixture.Id()),("@c",company),("@m",month));
    }
    static string MakeIban(string bankCode2,string account18)
    {
        string body=bankCode2+account18;
        int rem=0;
        foreach(char ch in body+"SA00")
            foreach(char d in char.IsAsciiLetter(ch)?((int)ch-'A'+10).ToString():ch.ToString())
                rem=(rem*10+d-'0')%97;
        int check=98-rem;
        return $"SA{check:D2}{body}";
    }
    static void SeedRiderDoc(DbConnection c,DbTransaction tx,string erpRiderId,string iqama,string name,string? verifiedIban,string? iban,string? address,string? registrationId=null)
    {
        var doc=new JsonObject
        {
            ["id"]=erpRiderId,["erpRiderId"]=erpRiderId,["iqama"]=iqama,["name"]=name,
            ["iban"]=iban??"",["verifiedIban"]=verifiedIban??"",["address"]=address??""
        };
        if(registrationId!=null)doc["registrationId"]=registrationId;
        Store.Save(c,"rider",doc,tx);
    }
    static readonly string[] AllKinds=["ninja_daily","ninja_daily_deduction","platform_account","assignment","housing","fuel","vehicle","scheme","eligibility",
        "keeta_invoice_row","hs_ops","hs_wallet","platform_ops","chefz_settlement","jahez_day"];
    static string FrozenRun(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string company,string app,string rider)
    {
        string run=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month,payout_blocked) VALUES(@id,@c,@app,'2026-10-01',0)",
            ("@id",run),("@c",company),("@app",app));
        var facts=new FakeOpsLiveFactsReader();
        foreach(string kind in AllKinds)facts.Rows[kind]=[];
        string acc=MoneySchemaFixture.Id();
        facts.Rows["ninja_daily"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["erpPlatformAccountId"]=acc,["riderProfileId"]=rider,["erpRiderId"]=rider,["orders"]=1,["reportDate"]="2026-10-01",["workDate"]="2026-10-01",["gross"]=500m,["RiderAmount"]=500m}];
        facts.Rows["platform_account"]=[new(){["id"]=acc,["externalAccountId"]="A1",["code"]="A1"}];
        facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["actualRiderProfileId"]=rider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31",["appCode"]=app}];
        facts.Rows["scheme"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["schemeCode"]="commission",["source"]="pay_settings"}];
        facts.Rows["eligibility"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["eligible"]=true,["source"]="pay_settings"}];
        var reasons=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var kv in facts.Rows)if(kv.Value.Count==0)reasons[kv.Key]="none";
        OpsSnapshotCapture.Capture(c,tx,actor,run,facts,reasons);
        OpsSnapshotCapture.Freeze(c,tx,actor,run);
        return run;
    }

    [TestMethod]
    public void X1_BankFile_NeverFabricates_ExcludesByReason_TotalsMatchIncludedOnly()
    {
        using var c=OpenBank(false);
        string company=SeedCompany(c,"T7");
        string reg=SeedRegistration(c,company,"7010000701");
        string bank=SeedBank(c,company,reg);
        var maker=BankActor();
        var checker=BankActor();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);

        string good=MoneySchemaFixture.Id(),noIban=MoneySchemaFixture.Id(),badIban=MoneySchemaFixture.Id(),
            badIqama=MoneySchemaFixture.Id(),noAddr=MoneySchemaFixture.Id(),unverified=MoneySchemaFixture.Id();
        string goodIban=MakeIban("80","000000000000001111");
        string unverifiedIban=MakeIban("10","000000000000002222");
        SeedRiderDoc(c,tx,good,"2000000701","مندوب صالح",goodIban,goodIban,"جدة",reg);
        SeedRiderDoc(c,tx,noIban,"2000000702","بلا آيبان",null,null,"جدة",reg);
        SeedRiderDoc(c,tx,badIban,"2000000703","آيبان خاطئ",null,"SA0000000000000000000000","جدة",reg);
        SeedRiderDoc(c,tx,badIqama,"3000000704","إقامة خاطئة",goodIban,goodIban,"جدة",reg); // iqama must start 1/2
        SeedRiderDoc(c,tx,noAddr,"2000000705","بلا عنوان",goodIban,goodIban,null,reg);
        SeedRiderDoc(c,tx,unverified,"2000000706","آيبان غير موثق",null,unverifiedIban,"جدة",reg);

        var riders=new[]{good,noIban,badIban,badIqama,noAddr,unverified};
        // One frozen run with all riders (multiple pay runs hit SQLite money-row replacement guards).
        string run=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month,payout_blocked) VALUES(@id,@c,'ninja','2026-10-01',0)",
            ("@id",run),("@c",company));
        var facts=new FakeOpsLiveFactsReader();
        foreach(string kind in AllKinds)facts.Rows[kind]=[];
        string acc=MoneySchemaFixture.Id();
        facts.Rows["ninja_daily"]=riders.Select(r=>(JsonObject)new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["erpPlatformAccountId"]=acc,["riderProfileId"]=r,["erpRiderId"]=r,["orders"]=1,["reportDate"]="2026-10-01",["workDate"]="2026-10-01",["gross"]=200m,["RiderAmount"]=200m}).ToList();
        facts.Rows["platform_account"]=[new(){["id"]=acc,["externalAccountId"]="A1",["code"]="A1"}];
        facts.Rows["assignment"]=riders.Select(r=>(JsonObject)new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["actualRiderProfileId"]=r,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31",["appCode"]="ninja"}).ToList();
        facts.Rows["scheme"]=riders.Select(r=>(JsonObject)new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=r,["schemeCode"]="commission",["source"]="pay_settings"}).ToList();
        facts.Rows["eligibility"]=riders.Select(r=>(JsonObject)new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=r,["eligible"]=true,["source"]="pay_settings"}).ToList();
        var reasons=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var kv in facts.Rows)if(kv.Value.Count==0)reasons[kv.Key]="none";
        OpsSnapshotCapture.Capture(c,tx,maker,run,facts,reasons);
        OpsSnapshotCapture.Freeze(c,tx,maker,run);
        foreach(string r in riders)SalEvidence.Gross(c,tx,run,r,200m);
        var calc=SalPayCalc.Calculate(c,tx,maker,run,new JsonObject
        {
            ["riders"]=new JsonArray(riders.Select(r=>(JsonNode?)new JsonObject{["erpRiderId"]=r}).ToArray())
        });
        var prIds=calc["riders"]!.AsArray().OfType<JsonObject>().Select(x=>x.S("id")).ToArray();
        SalDebtConfirm.Confirm(c,tx,maker,run,new JsonObject{["rows"]=new JsonArray(prIds.Select(id=>(JsonNode?)new JsonObject{["payRunRiderId"]=id,["amount"]=0}).ToArray())});
        SalSalaryApprove.Approve(c,tx,checker,run,new JsonObject{["payRunRiderIds"]=new JsonArray(prIds.Select(id=>(JsonNode?)JsonValue.Create(id)).ToArray())});

        var built=SalBankFiles.Build(c,tx,maker,new JsonObject
        {
            ["periodMonth"]="2026-10-01",
            ["issuanceChoice"]="per_company",
            ["scopeCompanyId"]=company,
            ["payingBankId"]=bank
        });
        var excluded=(built["excludedRows"] as JsonArray??[]).OfType<JsonObject>().ToList();
        Assert.IsTrue(excluded.Any(e=>e.S("reason")=="iban_missing"),"iban_missing");
        Assert.IsTrue(excluded.Any(e=>e.S("reason")=="iban_invalid"),"iban_invalid");
        Assert.IsTrue(excluded.Any(e=>e.S("reason")=="iqama_invalid"),"iqama_invalid");
        Assert.IsTrue(excluded.Any(e=>e.S("reason")=="address_missing"),"address_missing");
        Assert.IsTrue(excluded.Any(e=>e.S("reason")=="iban_unverified"),"iban_unverified without confirm");
        Assert.AreEqual(1,built["sheetRows"]!.AsArray().Count,"only verified rider included");
        Assert.AreEqual(built.N("totalAmount"),built["sheetRows"]!.AsArray().OfType<JsonObject>().Sum(r=>r.N("amount")));
        // Never invent fixed fallback IBAN
        Assert.IsFalse(excluded.Any(e=>e.S("iban")=="SA0380000000608010167519"));
        Assert.IsFalse(built.ToJsonString().Contains("SA0380000000608010167519",StringComparison.Ordinal));
        tx.Commit();
    }

    [TestMethod]
    public void X1_BankFile_ConfirmUnverifiedIbans_IncludesAndReportsCount()
    {
        using var c=OpenBank(false);
        string company=SeedCompany(c,"T7U");
        string reg=SeedRegistration(c,company,"7010000702");
        string bank=SeedBank(c,company,reg);
        var maker=BankActor();
        var checker=BankActor();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string iban=MakeIban("80","000000000000003333");
        SeedRiderDoc(c,tx,rider,"2000000710","آيبان غير موثق فقط",null,iban,"الرياض",reg);
        string run=FrozenRun(c,tx,maker,company,"ninja",rider);
        SalEvidence.Gross(c,tx,run,rider,250m);
        var calc=SalPayCalc.Calculate(c,tx,maker,run,new JsonObject{["riders"]=new JsonArray(new JsonObject{["erpRiderId"]=rider})});
        string pr=calc["riders"]![0]!["id"]!.GetValue<string>();
        SalDebtConfirm.Confirm(c,tx,maker,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=pr,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,checker,run,new JsonObject{["payRunRiderIds"]=new JsonArray(pr)});

        var without=Assert.ThrowsExactly<ApiError>(()=>SalBankFiles.Build(c,tx,maker,new JsonObject
        {
            ["periodMonth"]="2026-10-01",["issuanceChoice"]="per_company",
            ["scopeCompanyId"]=company,["payingBankId"]=bank
        }));
        // All excluded → 409 no eligible
        Assert.AreEqual(409,without.Status);

        var withConfirm=SalBankFiles.Build(c,tx,maker,new JsonObject
        {
            ["periodMonth"]="2026-10-01",["issuanceChoice"]="per_company",
            ["scopeCompanyId"]=company,["payingBankId"]=bank,
            ["confirmUnverifiedIbans"]=true
        });
        Assert.AreEqual(1,withConfirm["sheetRows"]!.AsArray().Count);
        Assert.IsTrue(withConfirm.N("unverifiedIbanCount")>=1);
        Assert.AreEqual(withConfirm.N("totalAmount"),withConfirm["sheetRows"]!.AsArray().OfType<JsonObject>().Sum(r=>r.N("amount")));
        tx.Commit();
    }

    [TestMethod]
    public void X2_AdminWithoutApproveOwn_Blocked_MoneyOutByCreditLines_BlockedEvenWithGrant()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        // Admin without grant — blocked
        string draftId="";
        f.Store.Write((c,tx)=>
        {
            EnsureCompany(c,tx,company);
            EnsurePeriodStore(c,tx,company,"2026-11-01");
            var draft=ManualJournals.Create(c,tx,Maker,new()
            {
                ["companyId"]=company,["date"]="2026-11-10",["reference"]="T7-SELF-1",
                ["description"]="اعتماد ذاتي بدون منحة",
                ["lines"]=new JsonArray(
                    new JsonObject{["accountCode"]="512101",["debit"]=5,["credit"]=0,["note"]="a"},
                    new JsonObject{["accountCode"]="212103",["debit"]=0,["credit"]=5,["note"]="b"})
            });
            draftId=draft.S("id");
            return true;
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,Maker,draftId))).Status);

        // With grant but draft credits a bank account → still blocked; zero fin.journal rows
        var withGrant=Actor("synthetic-trial7-moneyout","Admin",new JsonArray(AccessPolicy.ApproveOwnPermission,"finance.journal.create","finance.journal.approve"));
        string moneyDraft="";
        long before=0;
        f.Store.Write((c,tx)=>
        {
            before=Convert.ToInt64(SchemaSql.Scalar(c,tx,"SELECT COUNT(*) FROM fin_journal")??0L);
            var draft=ManualJournals.Create(c,tx,withGrant,new()
            {
                ["companyId"]=company,["date"]="2026-11-11",["reference"]="T7-BANK-CR",
                ["description"]="صرف بنكي عبر قيد يدوي",
                ["lines"]=new JsonArray(
                    new JsonObject{["accountCode"]="541401",["debit"]=12,["credit"]=0,["note"]="fee"},
                    new JsonObject{["accountCode"]="111201",["debit"]=0,["credit"]=12,["note"]="bank out"}),
                ["reconKind"]="payment"
            });
            moneyDraft=draft.S("id");
            return true;
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,withGrant,moneyDraft))).Status);
        f.Store.Read(c=>
        {
            Assert.AreEqual(before,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(*) FROM fin_journal")??0L));
            return true;
        });

        // SystemAdmin self-path also blocked for money-out by lines
        var sysAdmin=Actor("synthetic-trial7-sysadmin","SystemAdmin",new JsonArray("finance.journal.create","finance.journal.approve"));
        string sysDraft="";
        f.Store.Write((c,tx)=>
        {
            var draft=ManualJournals.Create(c,tx,sysAdmin,new()
            {
                ["companyId"]=company,["date"]="2026-11-12",["reference"]="T7-SYS-BANK",
                ["description"]="مدير نظام يصرف بنفسه",
                ["lines"]=new JsonArray(
                    new JsonObject{["accountCode"]="541401",["debit"]=3,["credit"]=0,["note"]="a"},
                    new JsonObject{["accountCode"]="111201",["debit"]=0,["credit"]=3,["note"]="b"})
            });
            sysDraft=draft.S("id");
            return true;
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,sysAdmin,sysDraft))).Status);
    }

    [TestMethod]
    public void GrokT6_Timing_NotInUnmatched_ReconciledIncludesTiming_WhenNoResidual()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string statementId="",lineId="";
        f.Store.Write((c,tx)=>
        {
            EnsureCompany(c,tx,company);
            string bank=EnsureBankStore(c,tx,company);
            EnsurePeriodStore(c,tx,company,"2026-11-01");
            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=50,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-11-12",["amount"]=50,["reference"]="TIMING-T7"})
            });
            statementId=uploaded.S("id");
            lineId=uploaded["lines"]!.AsArray().OfType<JsonObject>().Single().S("id");
            TreasuryBankStatementsApi.LineAction(c,tx,Maker,statementId,lineId,new(){["kind"]="timing",["note"]="شيك لم يُحصّل بعد"});
            return true;
        });
        var view=f.Store.Read(c=>TreasuryBankStatementsApi.View(c,null,Maker,statementId));
        Assert.AreEqual(1m,view.N("timingCount"));
        Assert.AreEqual(0m,view.N("unmatchedCount"),"timing must not inflate unmatched");
        Assert.AreEqual(50m,view.N("reconciledBalance"),"opening+matched+timing when residual unmatched=0");
        Assert.AreEqual(50m,view.N("bankClosingBalance"));
    }

    [TestMethod]
    public void GrokT6_CarryToNext_AppearsOnNextMonthStatementTotals()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string bank="",novId="",decId="";
        f.Store.Write((c,tx)=>
        {
            EnsureCompany(c,tx,company);
            bank=EnsureBankStore(c,tx,company);
            EnsurePeriodStore(c,tx,company,"2026-11-01");
            EnsurePeriodStore(c,tx,company,"2026-12-01");
            var nov=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=40,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-11-20",["amount"]=40,["reference"]="CHQ-NOV"})
            });
            novId=nov.S("id");
            string lineId=nov["lines"]!.AsArray().OfType<JsonObject>().Single().S("id");
            TreasuryBankStatementsApi.LineAction(c,tx,Maker,novId,lineId,new(){["kind"]="timing",["note"]="شيك مرحّل للشهر التالي"});
            var dec=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-12",["openingBalance"]=40,["closingBalance"]=45,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-12-02",["amount"]=5,["reference"]="DEC-ONLY"})
            });
            decId=dec.S("id");
            return true;
        });
        var decView=f.Store.Read(c=>TreasuryBankStatementsApi.View(c,null,Maker,decId));
        Assert.IsTrue(decView.N("priorTimingCarriedAmount")>=40m||decView.N("timingCarriedForwardAmount")>=40m,
            "next month must surface prior uncleared timing amount");
        Assert.IsTrue(decView.N("timingCount")>=1||decView.N("priorTimingCount")>=1);
    }

    [TestMethod]
    public void GrokT6_OpenOriginal_CreatesReversingDraft_ReferenceMissIs404()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string statementId="",lineId="",journalId="",draftId="",bank="";
        f.Store.Write((c,tx)=>
        {
            EnsureCompany(c,tx,company);
            bank=EnsureBankStore(c,tx,company);
            Guid periodId=EnsurePeriodStore(c,tx,company,"2026-11-01");
            string bankAcc=AccountId(c,tx,"111201");
            string equity=AccountId(c,tx,"212103");
            journalId=SchemaSql.GuidText(SchemaSql.NewId());
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal")}
                (id,voucher_no,period_id,company_id,period_month,journal_date,source_type,source_id,purpose,status,run_mode,narration_ar,created_at)
                VALUES(@id,'JV-T7',@p,@c,'2026-11-01','2026-11-08','synthetic',@id,'manual',1,0,'REV-REF-T7',{SchemaSql.NowLiteral(c)})
                """,("@id",journalId),("@p",SchemaSql.GuidText(periodId)),("@c",company));
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
                (id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,narration_ar,created_at)
                VALUES(@id,@p,@c,'2026-11-01',@j,1,@a,@cc,20,0,'REV-REF-T7',{SchemaSql.NowLiteral(c)})
                """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@p",SchemaSql.GuidText(periodId)),("@c",company),("@j",journalId),("@a",bankAcc),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)));
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
                (id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at)
                VALUES(@id,@p,@c,'2026-11-01',@j,2,@a,@cc,0,20,{SchemaSql.NowLiteral(c)})
                """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@p",SchemaSql.GuidText(periodId)),("@c",company),("@j",journalId),("@a",equity),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)));
            SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"fin","journal")} SET status=2,posted_at={SchemaSql.NowLiteral(c)},posted_by='tester' WHERE id=@id",("@id",journalId));

            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=20,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-11-08",["amount"]=20,["reference"]="REV-REF-T7"})
            });
            statementId=uploaded.S("id");
            lineId=uploaded["lines"]!.AsArray().OfType<JsonObject>().Single().S("id");
            var opened=TreasuryBankStatementsApi.LineAction(c,tx,Maker,statementId,lineId,new(){["kind"]="open_original"});
            Assert.AreEqual("reverse",opened.S("formAction"));
            draftId=opened.S("documentId");
            Assert.IsTrue(draftId.StartsWith("manual-journal:",StringComparison.Ordinal),"must create reversing draft");
            var draft=Store.Get(c,draftId,"manual_journal",tx)!;
            Assert.AreEqual("draft",draft.S("status"));
            Assert.AreEqual(journalId,draft.S("reversesJournalId"));
            var lines=draft["lines"]!.AsArray().OfType<JsonObject>().ToList();
            Assert.AreEqual(2,lines.Count);
            // Swapped: original Dr bank 20 / Cr equity 20 → reverse Cr bank 20 / Dr equity 20
            Assert.IsTrue(lines.Any(l=>l.S("accountCode")=="111201"&&l.N("credit")==20m));
            Assert.IsTrue(lines.Any(l=>l.S("accountCode")=="212103"&&l.N("debit")==20m));
            Assert.AreEqual(2,Convert.ToInt32(SchemaSql.Scalar(c,tx,$"SELECT status FROM {SchemaSql.Q(c,"fin","journal")} WHERE id=@id",("@id",journalId))));
            return true;
        });
        // Maker cannot approve own reversing draft (money-out credit to bank)
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,Maker,draftId))).Status);

        // Reference miss → 404 (no amount-only fallback). Reuse same bank.
        f.Store.Write((c,tx)=>
        {
            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=20,["closingBalance"]=27,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-11-09",["amount"]=7,["reference"]="NO-SUCH-REF-T7"})
            });
            string sid=uploaded.S("id");
            string lid=uploaded["lines"]!.AsArray().OfType<JsonObject>().Single().S("id");
            var err=Assert.ThrowsExactly<ApiError>(()=>TreasuryBankStatementsApi.LineAction(c,tx,Maker,sid,lid,new(){["kind"]="open_original"}));
            Assert.AreEqual(404,err.Status);
            return true;
        });
    }

    [TestMethod]
    public void GrokT6_ApproveOwn_ShowsOnNonMoneyOutDraft_IndependentForView()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var withPerm=Actor("synthetic-trial7-view","Admin",new JsonArray(AccessPolicy.ApproveOwnPermission,"finance.journal.create","finance.journal.approve"));
        JsonObject view=null!;
        f.Store.Write((c,tx)=>
        {
            EnsureCompany(c,tx,company);
            EnsurePeriodStore(c,tx,company,"2026-11-01");
            var draft=ManualJournals.Create(c,tx,withPerm,new()
            {
                ["companyId"]=company,["date"]="2026-11-13",["reference"]="T7-VIEW-OK",
                ["description"]="قيد غير صرف",
                ["lines"]=new JsonArray(
                    new JsonObject{["accountCode"]="512101",["debit"]=4,["credit"]=0,["note"]="a"},
                    new JsonObject{["accountCode"]="212103",["debit"]=0,["credit"]=4,["note"]="b"})
            });
            view=ManualJournals.WithActions(withPerm,draft,c,tx);
            return true;
        });
        Assert.IsTrue(view["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="approve"),"approve_own claim must show Approve on non-money-out");
    }

    [TestMethod]
    public void GrokT6_ManualJournal_DraftLinesEditable()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string draftId="";
        f.Store.Write((c,tx)=>
        {
            EnsureCompany(c,tx,company);
            EnsurePeriodStore(c,tx,company,"2026-11-01");
            var draft=ManualJournals.Create(c,tx,Maker,new()
            {
                ["companyId"]=company,["date"]="2026-11-14",["reference"]="T7-EDIT-1",
                ["description"]="مسودة قابلة للتعديل",
                ["lines"]=new JsonArray(
                    new JsonObject{["accountCode"]="114108",["debit"]=9,["credit"]=0,["note"]="suspense"},
                    new JsonObject{["accountCode"]="111201",["debit"]=0,["credit"]=9,["note"]="bank"})
            });
            draftId=draft.S("id");
            var updated=ManualJournals.Update(c,tx,Maker,draftId,new()
            {
                ["description"]="مسودة معدّلة",
                ["lines"]=new JsonArray(
                    new JsonObject{["accountCode"]="114108",["debit"]=11,["credit"]=0,["note"]="suspense2"},
                    new JsonObject{["accountCode"]="111201",["debit"]=0,["credit"]=11,["note"]="bank2"})
            });
            Assert.AreEqual("مسودة معدّلة",updated.S("description"));
            Assert.AreEqual(11m,updated["lines"]!.AsArray().OfType<JsonObject>().Sum(l=>l.N("debit")));
            return true;
        });
    }

    // --- helpers mirrored from Trial6 ---
    static void EnsureCompany(DbConnection c,DbTransaction tx,string companyId)
    {
        SchemaHrCompany.Ensure(c,tx);SchemaFinAccount.Ensure(c,tx);SchemaOrgCostCentre.Ensure(c,tx);
        SchemaTreasury.Ensure(c,tx);SchemaFinJournal.Ensure(c,tx);SchemaFinPeriod.Ensure(c,tx);SchemaFinPeriodBalance.Ensure(c,tx);
        var sp=FinancialCore.ResolveSponsor(companyId);
        object? exists=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"hr","company")} WHERE id=@id",("@id",companyId));
        if(exists is not null and not DBNull)return;
        string unified=sp.UnifiedNumber.Length>=10?sp.UnifiedNumber:"7"+companyId.Replace("-","")[..9];
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"hr","company")}(id,code,unified_number,name_ar,status,created_at)
            VALUES(@id,@code,@u,@n,1,{SchemaSql.NowLiteral(c)})
            """,("@id",companyId),("@code",sp.Code),("@u",unified),("@n",sp.NameAr));
    }
    static Guid EnsurePeriodStore(DbConnection c,DbTransaction tx,string companyId,string month)
    {
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE company_id=@c AND period_month=@m",("@c",companyId),("@m",month));
        if(existing is not null and not DBNull)return existing switch{Guid g=>g,_=>Guid.Parse(Convert.ToString(existing,CultureInfo.InvariantCulture)!)};
        Guid id=SchemaSql.NewId();
        SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"fin","accounting_period")}(id,company_id,period_month,state,created_at) VALUES(@id,@c,@m,1,{SchemaSql.NowLiteral(c)})",
            ("@id",SchemaSql.GuidText(id)),("@c",companyId),("@m",month));
        return id;
    }
    static string EnsureBankStore(DbConnection c,DbTransaction tx,string companyId)
    {
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"hr","commercial_registration")} WHERE company_id=@c",("@c",companyId));
        string regId;
        if(existing is not null and not DBNull)
            regId=existing switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(existing,CultureInfo.InvariantCulture)!};
        else
        {
            regId=SchemaSql.GuidText(SchemaSql.NewId());
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"hr","commercial_registration")}(id,company_id,cr_number,status,created_at)
                VALUES(@id,@c,@cr,1,{SchemaSql.NowLiteral(c)})
                """,("@id",regId),("@c",companyId),("@cr",regId.Replace("-","")[..20]));
        }
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"treasury","bank_account")}(id,company_id,registration_id,code,name_ar,account_id)
            VALUES(@id,@c,@r,@code,'بنك اختبار',@a)
            """,("@id",id),("@c",companyId),("@r",regId),("@code","BNK-"+id[..8]),("@a",AccountId(c,tx,"111201")));
        return id;
    }
    static string AccountId(DbConnection c,DbTransaction tx,string code)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","account")} WHERE code=@c",("@c",code));
        Assert.IsNotNull(id,$"missing CoA {code}");
        return id switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(id,CultureInfo.InvariantCulture)!};
    }
}
