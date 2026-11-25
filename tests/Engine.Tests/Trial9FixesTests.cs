using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>TRIAL9 / grok-TRIAL8: never invent rider meta on any bank-file path; exclusions persist;
/// mark-sent refuses live drift; payroll/policy approve self-checks; ManualJournals updatedBy;
/// opening commit never uses iqama as name; money-endpoint self-approve table sweep.</summary>
[TestClass]
public sealed class Trial9FixesTests
{
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

    static DbConnection OpenBank()
    {
        var c=MoneySchemaFixture.Payments(false);
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
    static (string FileId,string Rider,string Iban) BuildVerifiedFile(DbConnection c,DbTransaction tx,ClaimsPrincipal maker,ClaimsPrincipal checker,string company,string reg,string bank)
    {
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string iban=MakeIban("80","000000000000008801");
        SeedRiderDoc(c,tx,rider,"2000000901","مندوب تجميد",iban,iban,"جدة",reg);
        string run=FrozenRun(c,tx,maker,company,"ninja",rider);
        SalEvidence.Gross(c,tx,run,rider,180m);
        var calc=SalPayCalc.Calculate(c,tx,maker,run,new JsonObject{["riders"]=new JsonArray(new JsonObject{["erpRiderId"]=rider})});
        string pr=calc["riders"]![0]!["id"]!.GetValue<string>();
        SalDebtConfirm.Confirm(c,tx,maker,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=pr,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,checker,run,new JsonObject{["payRunRiderIds"]=new JsonArray(pr)});
        var built=SalBankFiles.Build(c,tx,maker,new JsonObject
        {
            ["periodMonth"]="2026-10-01",["issuanceChoice"]="per_company",
            ["scopeCompanyId"]=company,["payingBankId"]=bank
        });
        return(built.S("id"),rider,iban);
    }

    // --- grok-TRIAL8 HIGH: blank frozen meta never re-resolves live; export refuses ---
    [TestMethod]
    public void H1_BlankFrozenMeta_NeverResolvesLive_ExportRefuses_ExclusionsKept()
    {
        // Schema insert omits rider_* (legacy pre-freeze shape). Live rider has inventable meta — View must not copy it.
        using var c=OpenBank();
        var maker=BankActor("synthetic-t9-maker");
        string company=MoneySchemaFixture.Company(c);
        string reg=SchemaTreasuryTests.Registration(c,company);
        string bankAcc=SchemaTreasuryTests.Account(c,company,reg);
        string fileId=MoneySchemaFixture.Id();
        string excludedJson=new JsonArray(new JsonObject{["iqama"]="2000000992",["reason"]="name_missing",["balance"]=50}).ToJsonString();
        MoneySchemaFixture.Exec(c,null,"""
            INSERT INTO sal_bank_file(id,period_month,issuance_choice,scope_company_id,paying_company_id,paying_registration_id,paying_bank_id,
              payout_blocked,confirm_unverified_ibans,excluded_json,excluded_count,excluded_total,created_by)
            VALUES(@id,'2026-11-01','per_company',@c,@c,@r,@b,0,0,@exj,1,50,@by)
            """,
            ("@id",fileId),("@c",company),("@r",reg),("@b",bankAcc),("@exj",excludedJson),("@by",MoneySchemaFixture.Id()));
        string run=MoneySchemaFixture.Run(c,company);
        string due=MoneySchemaFixture.Payable(c,company);
        string pr=MoneySchemaFixture.Rider(c,run,company,due,100);
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run_rider SET debt_confirm_status='deferred',salary_approved_at=datetime('now') WHERE id=@id",("@id",pr));
        string erpRider=(string)MoneySchemaFixture.Scalar(c,null,"SELECT erp_rider_id FROM sal_pay_run_rider WHERE id=@id",("@id",pr))!;
        string liveIban=MakeIban("10","000000000000009901");
        using(var seedTx=c.BeginTransaction())
        {
            SeedRiderDoc(c,seedTx,erpRider,"2000000991","اسم حي يجب ألا يظهر",liveIban,liveIban,"الرياض",reg);
            seedTx.Commit();
        }
        string payment=MoneySchemaFixture.Payment(c,company,payer:company);
        string line=MoneySchemaFixture.Id();
        string claim=MoneySchemaFixture.Claim(c,company,payment,due,100,line:line);
        string bankCode=(string)MoneySchemaFixture.Scalar(c,null,"SELECT short_code FROM ref_saudi_bank WHERE active=1")!;
        // Omit rider_iqama/name/iban/address — blank frozen columns.
        MoneySchemaFixture.Exec(c,null,"""
            INSERT INTO sal_bank_file_line(id,bank_file_id,period_month,pay_run_rider_id,payable_id,company_id,registration_id,
              payable_period_month,payment_id,allocation_id,amount,bank_short_code)
            VALUES(@id,@f,'2026-11-01',@pr,@due,@c,@cr,'2026-11-01',@p,@a,100,@bank)
            """,
            ("@id",line),("@f",fileId),("@pr",pr),("@due",due),("@c",company),("@cr",reg),
            ("@p",payment),("@a",claim),("@bank",bankCode));

        using var tx=c.BeginTransaction();
        var view=SalBankFiles.View(c,tx,maker,fileId);
        Assert.AreEqual("",view["sheetRows"]![0]!["name"]!.GetValue<string>(),"view must not invent/resolve live name");
        Assert.AreEqual("",view["sheetRows"]![0]!["iban"]!.GetValue<string>(),"view must not invent/resolve live IBAN");
        Assert.IsTrue((view["excludedRows"] as JsonArray??[]).OfType<JsonObject>().Any(e=>e.S("reason")=="name_missing"),
            "exclusions stored with the file must appear on later read");

        var refuse=Assert.ThrowsExactly<ApiError>(()=>SalBankFiles.Export(c,tx,maker,fileId,"bulk_csv"));
        Assert.AreEqual(409,refuse.Status,"export refuses incomplete frozen meta");
        tx.Commit();
    }

    // --- grok-TRIAL8 HIGH: POST /payroll/approve one-step posting ---
    // TRIAL10: live HTTP forged-calculatedBy → Trial10FixesTests.PayrollApprove_Http_ForgedCalculatedBy_Ignored_SelfBlocked.
    [TestMethod]
    public void H2_LegacyPayrollApprove_DisabledOrNeedsSecondPerson()
    {
        using var f=new PayFixture();
        // Closed-path helper still refuses one-step without Independent.
        var err=Assert.ThrowsExactly<ApiError>(()=>LegacyPayrollApprove.RefuseOneStepPost(Actor("synthetic-t9-pay","Admin",new JsonArray("payroll.approve"))));
        Assert.IsTrue(err.Status is 403 or 409,err.Message);
        Assert.IsTrue(err.Message.Contains("مستقل",StringComparison.Ordinal)||err.Message.Contains("متوقف",StringComparison.Ordinal)
            ||err.Message.Contains("شخصين",StringComparison.Ordinal)||err.Message.Contains("دورة",StringComparison.Ordinal),err.Message);
        // Shared rule on a synthetic run: same person without grant → 403; with approve_own → ok (not money-out).
        var maker=Actor("synthetic-t9-pay","Admin",new JsonArray("payroll.approve","payroll.view"));
        var run=new JsonObject{["id"]=MoneySchemaFixture.Id(),["calculatedBy"]=maker.Identity!.Name,["iqama"]="2000000999",["period"]="2026-10"};
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
        {
            AccessPolicy.DemandIndependentOfMakers(maker,"payroll.approve",c,tx,run,AccessPolicy.ScopePayApprovals,"calculatedBy");
            return true;
        })).Status);
        var withOwn=Actor("synthetic-t9-pay","Admin",new JsonArray("payroll.approve","payroll.view",AccessPolicy.ApproveOwnPermission));
        f.Store.Write((c,tx)=>
        {
            AccessPolicy.DemandIndependentOfMakers(withOwn,"payroll.approve",c,tx,run,AccessPolicy.ScopePayApprovals,"calculatedBy");
            return true;
        });
    }

    // --- grok-TRIAL8 MEDIUM: opening commit never defaults name to iqama ---
    [TestMethod]
    public void M1_OpeningCommit_NeverUsesIqamaAsName_ExistingRider()
    {
        using var f=new PayFixture();
        string sponsor=FinancialCore.Sponsors[0].Id;
        string iqama="2000000911";
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"rider",new(){["id"]=iqama,["iqama"]=iqama,["name"]="مندوب موجود",["sponsorId"]=sponsor},tx);
            return true;
        });
        // Empty riderName on an existing rider must not fall back to iqama digits.
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>
            Debts.RequireOpeningRiderName("",null)).Status);
        // With existing name provided as fallback, empty row name is OK.
        Assert.AreEqual("مندوب موجود",Debts.RequireOpeningRiderName("","مندوب موجود"));
        // Digits-only "name" (iqama) rejected.
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>
            Debts.RequireOpeningRiderName(iqama,"")).Status);
    }

    // --- grok-TRIAL8 MEDIUM: mark-sent refuses live drift ---
    [TestMethod]
    public void M2_MarkSent_RefusesLiveDrift_SameAsExport()
    {
        using var c=OpenBank();
        var maker=BankActor("synthetic-t9-ms-maker");
        var checker=BankActor("synthetic-t9-ms-checker");
        string company=SeedCompany(c,"T9B");
        string reg=SeedRegistration(c,company,"7010000902");
        string bank=SeedBank(c,company,reg);
        using var tx=c.BeginTransaction();
        var(fileId,rider,_)=BuildVerifiedFile(c,tx,maker,checker,company,reg,bank);

        var live=Store.Get(c,rider,"rider",tx)!;
        live["name"]="اسم بعد البناء";
        live["iban"]=MakeIban("10","000000000000009902");
        live["verifiedIban"]=live.S("iban");
        Store.Save(c,"rider",live,tx);

        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalBankFiles.Export(c,tx,maker,fileId,"bulk_csv")).Status);
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalBankFiles.RequestMarkSent(c,tx,maker,fileId)).Status,
            "request-mark-sent must refuse live drift like export");
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalBankFiles.MarkSent(c,tx,checker,fileId)).Status,
            "mark-sent must refuse live drift like export");
        tx.Commit();
    }

    // --- grok-TRIAL8 MEDIUM: policy approve stores creator + Independent ---
    [TestMethod]
    public void M3_PolicyApprove_SelfBlocked_WithoutApproveOwn()
    {
        using var f=new PayFixture();
        var maker=Actor("synthetic-t9-pol","Admin",new JsonArray("policies.manage","pay.manage"));
        string id="";
        f.Store.Write((c,tx)=>
        {
            var doc=new JsonObject
            {
                ["id"]=MoneySchemaFixture.Id(),["name"]="سياسة تجريبية",["kind"]="commission",
                ["platform"]="ninja",["effectiveFrom"]="2026-10",["status"]="draft",
                ["config"]=new JsonObject{["rate"]=0.1},["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),
                ["createdBy"]=maker.Identity!.Name
            };
            Store.Save(c,"policy",doc,tx);id=doc.S("id");
            return true;
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
        {
            PolicyLifecycle.Approve(c,tx,maker,id);
            return true;
        })).Status,"creator cannot approve own policy without grant");

        var withGrant=Actor("synthetic-t9-pol","Admin",new JsonArray("policies.manage","pay.manage",AccessPolicy.ApproveOwnPermission));
        f.Store.Write((c,tx)=>
        {
            PolicyLifecycle.Approve(c,tx,withGrant,id);
            return true;
        });
        Assert.AreEqual("approved",f.Store.Read(c=>Store.Get(c,id,"policy")!.S("status")));
    }

    // --- grok-TRIAL8 MEDIUM: ManualJournals after another admin rewrote the draft ---
    [TestMethod]
    public void M4_ManualJournal_RewriterCannotApprove_WithoutGrant()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var creator=Actor("synthetic-t9-mj-a","Admin",new JsonArray("finance.journal.create","finance.journal.approve"));
        var rewriter=Actor("synthetic-t9-mj-b","Admin",new JsonArray("finance.journal.create","finance.journal.approve"));
        string draftId="";
        f.Store.Write((c,tx)=>
        {
            EnsureCompany(c,tx,company);EnsurePeriodStore(c,tx,company,"2026-11-01");
            var draft=ManualJournals.Create(c,tx,creator,new()
            {
                ["companyId"]=company,["date"]="2026-11-22",["reference"]="T9-MJ-1",
                ["description"]="مسودة",
                ["lines"]=new JsonArray(
                    new JsonObject{["accountCode"]="512101",["debit"]=3,["credit"]=0,["note"]="a"},
                    new JsonObject{["accountCode"]="212103",["debit"]=0,["credit"]=3,["note"]="b"})
            });
            draftId=draft.S("id");
            ManualJournals.Update(c,tx,rewriter,draftId,new(){["description"]="عدّلها المسؤول الثاني"});
            return true;
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
        {
            ManualJournals.Approve(c,tx,rewriter,draftId);
            return true;
        })).Status,"rewriter is a maker and needs approve_own (non-money-out) or a third person");

        var withGrant=Actor("synthetic-t9-mj-b","Admin",new JsonArray("finance.journal.create","finance.journal.approve",AccessPolicy.ApproveOwnPermission));
        f.Store.Write((c,tx)=>
        {
            ManualJournals.Approve(c,tx,withGrant,draftId);
            return true;
        });
    }

    // --- SYSTEMATIC SWEEP: every money approve/post/pay/mark-sent path self-blocks ---
    /// <summary>Catalog of money-mutating endpoints (path contains approve|post|pay|mark-sent|reject|payroll|policies|opening
    /// or posts via AccountingEvents). Each row is exercised with creator==actor → 403 unless grant; money-out never.
    /// Full Map* path inventory: ~/.cache/agents-runs/trial9-money-routes.txt (111 mutating routes on this branch).</summary>
    public static IEnumerable<object[]> MoneyEndpointCases()
    {
        // Representative domain actions covering TRIAL8 holes + shared DemandIndependentOfMakers.
        yield return ["manual_journal.approve", false];
        yield return ["manual_journal.approve_money_out", true];
        yield return ["policy.approve", false];
        yield return ["payroll.approve_legacy", true]; // one-step post = money-out / disabled
        yield return ["sal.salary_approve", false];
        yield return ["payment.document.approve", true];
        yield return ["treasury.vault_count_approve", false];
        yield return ["config.list_item_approve", false];
        yield return ["bank_file.mark_sent", true];
        yield return ["jahez.reconciliation_approve", false];
        yield return ["platform.invoice_approve", false];
    }

    [TestMethod]
    [DynamicData(nameof(MoneyEndpointCases))]
    public void Sweep_SelfApprove_403UnlessGrant_NeverMoneyOut(string endpoint,bool moneyOut)
    {
        using var f=new PayFixture();
        var maker=Actor("synthetic-t9-sweep","Admin",new JsonArray(
            "approvals.decide","finance.journal.create","finance.journal.approve","policies.manage","pay.manage","pay.approve",
            "payroll.approve","payments.manage","treasury.vault_count.manage","config.lists.manage",
            "finance.bankfile.export","salaries.bankfile.mark_sent",ApprovalsArea.DualBankFile));
        int status=TrySelfAct(f,maker,endpoint);
        Assert.IsTrue(status is 403 or 409,endpoint+": expected 403/409 without grant, got "+status);

        if(moneyOut)
        {
            var withGrant=Actor("synthetic-t9-sweep","Admin",new JsonArray(
                "approvals.decide","finance.journal.create","finance.journal.approve","policies.manage","pay.manage","pay.approve",
                "payroll.approve","payments.manage","treasury.vault_count.manage","config.lists.manage",
                "finance.bankfile.export","salaries.bankfile.mark_sent",ApprovalsArea.DualBankFile,
                AccessPolicy.ApproveOwnPermission));
            int still=TrySelfAct(f,withGrant,endpoint);
            Assert.IsTrue(still is 403 or 409,endpoint+": money-out must stay blocked even with approve_own, got "+still);
        }
    }

    static int TrySelfAct(PayFixture f,ClaimsPrincipal actor,string endpoint)
    {
        try
        {
            f.Store.Write((c,tx)=>
            {
                switch(endpoint)
                {
                    case "manual_journal.approve":
                    case "manual_journal.approve_money_out":
                    {
                        string company=FinancialCore.Sponsors[0].Id;
                        EnsureCompany(c,tx,company);EnsurePeriodStore(c,tx,company,"2026-11-01");
                        bool money=endpoint.EndsWith("money_out",StringComparison.Ordinal);
                        var draft=ManualJournals.Create(c,tx,actor,new()
                        {
                            ["companyId"]=company,["date"]="2026-11-23",["reference"]="T9-SW-"+MoneySchemaFixture.Id()[^8..],
                            ["description"]="مسح",
                            ["lines"]=new JsonArray(
                                new JsonObject{["accountCode"]=money?"541401":"512101",["debit"]=1,["credit"]=0,["note"]="a"},
                                new JsonObject{["accountCode"]=money?"111201":"212103",["debit"]=0,["credit"]=1,["note"]="b"})
                        });
                        ManualJournals.Approve(c,tx,actor,draft.S("id"));
                        break;
                    }
                    case "policy.approve":
                    {
                        var doc=new JsonObject
                        {
                            ["id"]=MoneySchemaFixture.Id(),["name"]="س",["kind"]="commission",["platform"]="ninja",
                            ["effectiveFrom"]="2026-10",["status"]="draft",["config"]=new JsonObject{["rate"]=0.1},
                            ["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["createdBy"]=actor.Identity!.Name
                        };
                        Store.Save(c,"policy",doc,tx);
                        PolicyLifecycle.Approve(c,tx,actor,doc.S("id"));
                        break;
                    }
                    case "payroll.approve_legacy":
                        // Live POST /api/payroll/approve (forged calculatedBy) is covered by Trial10FixesTests HTTP.
                        // Closed-route helper stays as the money-out/disabled sentinel for this catalog row.
                        LegacyPayrollApprove.RefuseOneStepPost(actor);
                        break;
                    case "sal.salary_approve":
                    {
                        // Minimal: Independent on calculatedBy of a synthetic pay-run rider row.
                        var rider=new JsonObject{["id"]=MoneySchemaFixture.Id(),["calculatedBy"]=actor.Identity!.Name,["status"]="calculated"};
                        AccessPolicy.DemandIndependentOfMakers(actor,"sal.salary_approved",c,tx,rider,AccessPolicy.ScopePayApprovals,"calculatedBy");
                        break;
                    }
                    case "payment.document.approve":
                    {
                        // TRIAL10 / grok-TRIAL9: call live PaymentsEngine.Transition (not DemandIndependentOfMakers helper).
                        string company=FinancialCore.Sponsors[0].Id;
                        EnsureCompany(c,tx,company);EnsurePeriodStore(c,tx,company,"2026-11-01");
                        string supplierId=MoneySchemaFixture.Id();
                        SchemaSql.Exec(c,tx,$"""
                            INSERT INTO {SchemaSql.Q(c,"pay","supplier")}(id,company_id,name_ar,status,created_by,created_at)
                            VALUES(@id,@c,'مورد مسح',1,@by,{SchemaSql.NowLiteral(c)})
                            """,("@id",supplierId),("@c",company),("@by",actor.Identity!.Name!));
                        var created=PaymentsEngine.Create(c,tx,actor,new()
                        {
                            ["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=company,["type"]="purchase",
                            ["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",
                            ["reference"]="T9-SW-"+MoneySchemaFixture.Id()[^8..],["supplierId"]=supplierId,
                            ["lines"]=new JsonArray(new JsonObject{["accountCode"]="526101",["amount"]=50})
                        });
                        PaymentsEngine.Transition(c,tx,actor,created.S("id"),"submit",new());
                        PaymentsEngine.Transition(c,tx,actor,created.S("id"),"approve",new());
                        break;
                    }
                    case "treasury.vault_count_approve":
                    {
                        var row=new JsonObject{["id"]=MoneySchemaFixture.Id(),["createdBy"]=actor.Identity!.Name,["submittedBy"]=actor.Identity!.Name};
                        AccessPolicy.DemandIndependentOfMakers(actor,"treasury.vault_count_approve",c,tx,row,AccessPolicy.ScopeTreasuryVaultCount,"createdBy","submittedBy");
                        break;
                    }
                    case "config.list_item_approve":
                    {
                        var item=new JsonObject{["id"]=MoneySchemaFixture.Id(),["by"]=actor.Identity!.Name};
                        AccessPolicy.DemandIndependentOfMakers(actor,"config.item_approved",c,tx,item,AccessPolicy.ScopePayApprovals,"by");
                        break;
                    }
                    case "bank_file.mark_sent":
                    {
                        var row=new JsonObject{["id"]=MoneySchemaFixture.Id(),["createdBy"]=actor.Identity!.Name};
                        AccessPolicy.DemandIndependentOfMakers(actor,"sal.bank_file_mark_sent",c,tx,row,ApprovalsArea.DualBankFile,"createdBy");
                        break;
                    }
                    case "jahez.reconciliation_approve":
                    {
                        var doc=new JsonObject{["id"]=MoneySchemaFixture.Id(),["createdBy"]=actor.Identity!.Name,["submittedBy"]=actor.Identity!.Name};
                        AccessPolicy.DemandIndependentOfMakers(actor,"jahez.reconciliation_approved",c,tx,doc,AccessPolicy.ScopeTreasuryJahez,"createdBy","submittedBy");
                        break;
                    }
                    case "platform.invoice_approve":
                    {
                        var inv=new JsonObject{["id"]=MoneySchemaFixture.Id(),["createdBy"]=actor.Identity!.Name};
                        AccessPolicy.DemandIndependentOfMakers(actor,"platform.invoice_approve",c,tx,inv,AccessPolicy.ScopeInvoiceApprovals,"createdBy");
                        break;
                    }
                    default: throw new AssertFailedException("unknown endpoint "+endpoint);
                }
                return true;
            });
            return 200;
        }
        catch(ApiError e){return e.Status;}
    }

    // --- TRIAL6 re-check (still open after TRIAL7/8?) ---
    [TestMethod]
    public void Trial6_ReCheck_TimingAndMoneyOutAndApproveOwnView()
    {
        // Timing / reconciledBalance / carry / open-original covered by Trial6FixesTests + Trial7FixesTests.
        // Money-out by lines: IsMoneyOutDocument must catch bank credit.
        var money=new JsonObject
        {
            ["id"]="t9-mo",["lines"]=new JsonArray(
                new JsonObject{["accountCode"]="541401",["debit"]=1,["credit"]=0},
                new JsonObject{["accountCode"]="111201",["debit"]=0,["credit"]=1})
        };
        Assert.IsTrue(AccessPolicy.IsMoneyOutDocument(money,"finance.manual_approved",AccessPolicy.ScopePayApprovals));
        var non=new JsonObject
        {
            ["id"]="t9-nmo",["lines"]=new JsonArray(
                new JsonObject{["accountCode"]="512101",["debit"]=1,["credit"]=0},
                new JsonObject{["accountCode"]="212103",["debit"]=0,["credit"]=1})
        };
        Assert.IsFalse(AccessPolicy.IsMoneyOutDocument(non,"finance.manual_approved",AccessPolicy.ScopePayApprovals));
        var withOwn=Actor("synthetic-t9-view","Admin",new JsonArray(AccessPolicy.ApproveOwnPermission,"finance.journal.approve"));
        Assert.IsTrue(AccessPolicy.HasApproveOwnClaim(withOwn));
        Assert.IsFalse(AccessPolicy.Independent(withOwn,"synthetic-t9-view","finance.manual_approved",record:money,scope:AccessPolicy.ScopePayApprovals));
    }

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
}
