using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>TRIAL8: freeze bank-file rider meta; persist exclusions; treasury same rules;
/// self-approval only via approvals.approve_own for every role; never money-out; opening-balance no invented names.</summary>
[TestClass]
public sealed class Trial8FixesTests
{
    static ClaimsPrincipal Maker=>PayFixture.Actor("synthetic-trial8-maker","Admin");
    static ClaimsPrincipal Checker=>PayFixture.Actor("synthetic-trial8-checker","Admin");
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

    static (string FileId,string Company,string Rider,string Iban) BuildOneVerifiedFile(DbConnection c,DbTransaction tx,ClaimsPrincipal maker,ClaimsPrincipal checker,string company,string reg,string bank,bool confirmUnverified=false,string? name=null)
    {
        EnsurePeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string iban=MakeIban("80","000000000000008888");
        SeedRiderDoc(c,tx,rider,"2000000801",name??"مندوب مجمّد",confirmUnverified?null:iban,iban,"جدة",reg);
        string run=FrozenRun(c,tx,maker,company,"ninja",rider);
        SalEvidence.Gross(c,tx,run,rider,180m);
        var calc=SalPayCalc.Calculate(c,tx,maker,run,new JsonObject{["riders"]=new JsonArray(new JsonObject{["erpRiderId"]=rider})});
        string pr=calc["riders"]![0]!["id"]!.GetValue<string>();
        SalDebtConfirm.Confirm(c,tx,maker,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=pr,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,checker,run,new JsonObject{["payRunRiderIds"]=new JsonArray(pr)});
        var built=SalBankFiles.Build(c,tx,maker,new JsonObject
        {
            ["periodMonth"]="2026-10-01",["issuanceChoice"]="per_company",
            ["scopeCompanyId"]=company,["payingBankId"]=bank,
            ["confirmUnverifiedIbans"]=confirmUnverified
        });
        return(built.S("id"),company,rider,iban);
    }

    [TestMethod]
    public void H1_ViewExport_UsesFrozenMeta_RefusesLiveDrift_NeverForceConfirm()
    {
        using var c=OpenBank(false);
        var maker=BankActor();
        var checker=BankActor();
        string company=SeedCompany(c,"T8");
        string reg=SeedRegistration(c,company,"7010000801");
        string bank=SeedBank(c,company,reg);
        using var tx=c.BeginTransaction();
        var(fileId,_,rider,iban)=BuildOneVerifiedFile(c,tx,maker,checker,company,reg,bank);

        // Later rider edit must not change the frozen export fields.
        var live=Store.Get(c,rider,"rider",tx)!;
        live["name"]="اسم معدّل بعد البناء";
        live["iban"]=MakeIban("10","000000000000009999");
        live["verifiedIban"]=live.S("iban");
        live["address"]="الرياض";
        Store.Save(c,"rider",live,tx);

        var view=SalBankFiles.View(c,tx,maker,fileId);
        Assert.AreEqual("مندوب مجمّد",view["sheetRows"]![0]!["name"]!.GetValue<string>(),"view must use frozen name");
        Assert.AreEqual(iban,view["sheetRows"]![0]!["iban"]!.GetValue<string>(),"view must use frozen IBAN");
        Assert.AreEqual("جدة",view["sheetRows"]![0]!["address"]!.GetValue<string>());
        Assert.IsFalse(view.B("confirmUnverifiedIbans"),"view must never force-confirm unverified IBANs");

        var refuse=Assert.ThrowsExactly<ApiError>(()=>SalBankFiles.Export(c,tx,maker,fileId,"bulk_csv"));
        Assert.AreEqual(409,refuse.Status,"export refuses when live rider meta differs from frozen");
        tx.Commit();
    }

    [TestMethod]
    public void H2_TreasuryPreview_SameExclusions_UnverifiedNeedsConfirm()
    {
        using var f=new PayFixture();
        string sponsor=FinancialCore.Sponsors[0].Id;
        string verified=MakeIban("80","000000000000001111");
        string unverified=MakeIban("10","000000000000002222");
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"rider",new(){["id"]="2000000811",["iqama"]="2000000811",["name"]="خزينة صالح",["verifiedIban"]=verified,["iban"]=verified,["address"]="جدة"},tx);
            Store.Save(c,"rider",new(){["id"]="2000000812",["iqama"]="2000000812",["name"]="خزينة غير موثق",["iban"]=unverified,["address"]="جدة"},tx);
            Store.Save(c,"rider",new(){["id"]="2000000813",["iqama"]="2000000813",["name"]="بلا عنوان",["verifiedIban"]=verified,["iban"]=verified,["address"]=""},tx);
            Store.Save(c,"rider",new(){["id"]="2000000814",["iqama"]="2000000814",["name"]="",["verifiedIban"]=verified,["iban"]=verified,["address"]="جدة"},tx);
            Store.Save(c,PayCycles.Kind,new()
            {
                ["id"]="cycle-t8-treasury",["companyId"]=sponsor,["period"]="2026-10",["status"]="approved",
                ["riders"]=new JsonArray(
                    new JsonObject{["rider"]="2000000811",["salaryApprovedAt"]="2026-10-20T00:00:00Z",["net"]=100,["salaryNet"]=100},
                    new JsonObject{["rider"]="2000000812",["salaryApprovedAt"]="2026-10-20T00:00:00Z",["net"]=80,["salaryNet"]=80},
                    new JsonObject{["rider"]="2000000813",["salaryApprovedAt"]="2026-10-20T00:00:00Z",["net"]=70,["salaryNet"]=70},
                    new JsonObject{["rider"]="2000000814",["salaryApprovedAt"]="2026-10-20T00:00:00Z",["net"]=60,["salaryNet"]=60})
            },tx);
            return true;
        });
        var actor=UserAccess.Principal(new JsonObject{["username"]="synthetic-t8-treas",["role"]="Admin",
            ["customPermissions"]=new JsonArray("finance.bankfile.export","payroll.view")});
        var preview=f.Store.Read(c=>BankPayrollFiles.Preview(c,actor,new(){["sponsorId"]=sponsor,["from"]="2026-10",["to"]="2026-10"}));
        var excluded=(preview["excludedRows"] as JsonArray??[]).OfType<JsonObject>().ToList();
        Assert.IsTrue(excluded.Any(e=>e.S("reason")=="iban_unverified"),"unverified without confirm");
        Assert.IsTrue(excluded.Any(e=>e.S("reason")=="address_missing"));
        Assert.IsTrue(excluded.Any(e=>e.S("reason")=="name_missing"));
        Assert.AreEqual(1,(preview["rows"] as JsonArray??[]).Count,"only verified+complete included");

        var confirmed=f.Store.Read(c=>BankPayrollFiles.Preview(c,actor,new(){["sponsorId"]=sponsor,["from"]="2026-10",["to"]="2026-10",["confirmUnverifiedIbans"]=true}));
        Assert.IsTrue((confirmed["rows"] as JsonArray??[]).OfType<JsonObject>().Any(r=>r.S("iqama")=="2000000812"),"confirm includes unverified");
        Assert.IsFalse((confirmed["excludedRows"] as JsonArray??[]).OfType<JsonObject>().Any(e=>e.S("iqama")=="2000000812"&&e.S("reason")=="iban_unverified"));
    }

    [TestMethod]
    public void H3_ValidateApproval_SelfCheck_EveryRole_NonDualNeedsGrant()
    {
        using var f=new PayFixture();
        var admin=Actor("synthetic-t8-admin","Admin",new JsonArray("approvals.decide"));
        var req=new JsonObject
        {
            ["id"]="t8-non-dual-self",["kind"]="generic",["status"]="pending",
            ["requestedBy"]="synthetic-t8-admin",["targetDepartment"]="FIN",["requiredRankLevel"]=1,
            ["createdAt"]=DateTimeOffset.UtcNow.ToString("O")
        };
        // Without grant: blocked for ordinary Admin on non-dual
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
        {
            AccessPolicy.ValidateApproval(admin,req,new(),c:c,tx:tx);
            return true;
        })).Status);

        var withGrant=Actor("synthetic-t8-admin2","Admin",new JsonArray("approvals.decide",AccessPolicy.ApproveOwnPermission));
        var req2=new JsonObject
        {
            ["id"]="t8-non-dual-ok",["kind"]="generic",["status"]="pending",
            ["requestedBy"]="synthetic-t8-admin2",["targetDepartment"]="FIN",["requiredRankLevel"]=1,
            ["createdAt"]=DateTimeOffset.UtcNow.ToString("O")
        };
        f.Store.Write((c,tx)=>
        {
            AccessPolicy.ValidateApproval(withGrant,req2,new(),c:c,tx:tx,record:req2);
            Assert.IsTrue(req2.B("selfApproved")||req2.B("selfApprovedException"));
            return true;
        });
    }

    [TestMethod]
    public void H4_SystemAdmin_SelfOnlyWithApproveOwn_NeverMoneyOut()
    {
        using var f=new PayFixture();
        var sys=Actor("synthetic-t8-sys","SystemAdmin",new JsonArray("approvals.decide","finance.journal.create","finance.journal.approve"));
        var nonDual=new JsonObject
        {
            ["id"]="t8-sys-nondual",["kind"]="generic",["status"]="pending",
            ["requestedBy"]="synthetic-t8-sys",["targetDepartment"]="MGT",["requiredRankLevel"]=1,
            ["createdAt"]=DateTimeOffset.UtcNow.ToString("O")
        };
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
        {
            AccessPolicy.ValidateApproval(sys,nonDual,new(),c:c,tx:tx);
            return true;
        })).Status,"SystemAdmin without approve_own blocked");

        var dual=new JsonObject
        {
            ["id"]="t8-sys-dual",["kind"]="payment_out",["status"]="pending",
            ["requestedBy"]="synthetic-t8-sys",["targetDepartment"]="MGT",["requiredRankLevel"]=1,
            ["dualPermission"]=ApprovalsArea.DualPaymentOut,
            ["createdAt"]=DateTimeOffset.UtcNow.ToString("O")
        };
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
        {
            AccessPolicy.ValidateApproval(sys,dual,new(),"approvals.decide",c,tx,"approval.decide",dual,ApprovalsArea.DualPaymentOut);
            return true;
        })).Status,"SystemAdmin dual money-out never self");

        var withOwn=Actor("synthetic-t8-sys2","SystemAdmin",new JsonArray("approvals.decide",AccessPolicy.ApproveOwnPermission,"finance.journal.create","finance.journal.approve"));
        string company=FinancialCore.Sponsors[0].Id;
        // Non-money-out draft OK with grant
        f.Store.Write((c,tx)=>
        {
            EnsureCompany(c,tx,company);
            EnsurePeriodStore(c,tx,company,"2026-11-01");
            var draft=ManualJournals.Create(c,tx,withOwn,new()
            {
                ["companyId"]=company,["date"]="2026-11-20",["reference"]="T8-SYS-OK",
                ["description"]="قيد غير صرف",
                ["lines"]=new JsonArray(
                    new JsonObject{["accountCode"]="512101",["debit"]=2,["credit"]=0,["note"]="a"},
                    new JsonObject{["accountCode"]="212103",["debit"]=0,["credit"]=2,["note"]="b"})
            });
            ManualJournals.Approve(c,tx,withOwn,draft.S("id"));
            return true;
        });
        // Money-out still blocked with grant
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
        {
            var draft=ManualJournals.Create(c,tx,withOwn,new()
            {
                ["companyId"]=company,["date"]="2026-11-21",["reference"]="T8-SYS-BANK",
                ["description"]="صرف",
                ["lines"]=new JsonArray(
                    new JsonObject{["accountCode"]="541401",["debit"]=2,["credit"]=0,["note"]="a"},
                    new JsonObject{["accountCode"]="111201",["debit"]=0,["credit"]=2,["note"]="b"})
            });
            ManualJournals.Approve(c,tx,withOwn,draft.S("id"));
            return true;
        })).Status);
    }

    [TestMethod]
    public void M1_OpeningBalance_RejectsMissingName_NoPlaceholder()
    {
        // Preview/commit must not invent «مندوب {iqama}» or «مندوب رصيد افتتاحي».
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Debts.RequireOpeningRiderName("","")).Status);
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Debts.RequireOpeningRiderName("   ",null)).Status);
        Assert.AreEqual("محمد",Debts.RequireOpeningRiderName("","محمد"));
        Assert.AreEqual("علي",Debts.RequireOpeningRiderName("علي","قديم"));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Debts.RequireOpeningRiderName("2000000820","")).Status,
            "digits-only cleaned name must not become a placeholder");
    }

    [TestMethod]
    public void M2_ExclusionsPersisted_NameMissing_SharedRiderCountedOnce()
    {
        using var c=OpenBank(false);
        string company=SeedCompany(c,"T8E");
        string reg=SeedRegistration(c,company,"7010000802");
        string bank=SeedBank(c,company,reg);
        var maker=BankActor();
        var checker=BankActor();
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);

        string named=MoneySchemaFixture.Id(), noName=MoneySchemaFixture.Id();
        string goodIban=MakeIban("80","000000000000004444");
        SeedRiderDoc(c,tx,named,"2000000831","مندوب مزدوج",goodIban,goodIban,"جدة",reg);
        SeedRiderDoc(c,tx,noName,"2000000832","",goodIban,goodIban,"جدة",reg); // name_missing

        // Two apps for the same named rider → one sheet row. Calculate both before any salary approval.
        string runN=MoneySchemaFixture.Id(), runK=MoneySchemaFixture.Id();
        var calcByRun=new Dictionary<string,string[]>(StringComparer.Ordinal);
        foreach(var (run,app) in new[]{(runN,"ninja"),(runK,"keeta")})
        {
            MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month,payout_blocked) VALUES(@id,@c,@app,'2026-10-01',0)",
                ("@id",run),("@c",company),("@app",app));
            var facts=new FakeOpsLiveFactsReader();
            foreach(string kind in AllKinds)facts.Rows[kind]=[];
            string acc=MoneySchemaFixture.Id();
            var riders=new[]{named,noName};
            facts.Rows["ninja_daily"]=riders.Select(r=>(JsonObject)new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["erpPlatformAccountId"]=acc,["riderProfileId"]=r,["erpRiderId"]=r,["orders"]=1,["reportDate"]="2026-10-01",["workDate"]="2026-10-01",["gross"]=150m,["RiderAmount"]=150m}).ToList();
            facts.Rows["platform_account"]=[new(){["id"]=acc,["externalAccountId"]="A1",["code"]="A1"}];
            facts.Rows["assignment"]=riders.Select(r=>(JsonObject)new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["actualRiderProfileId"]=r,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31",["appCode"]=app}).ToList();
            facts.Rows["scheme"]=riders.Select(r=>(JsonObject)new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=r,["schemeCode"]="commission",["source"]="pay_settings"}).ToList();
            facts.Rows["eligibility"]=riders.Select(r=>(JsonObject)new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=r,["eligible"]=true,["source"]="pay_settings"}).ToList();
            var reasons=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var kv in facts.Rows)if(kv.Value.Count==0)reasons[kv.Key]="none";
            OpsSnapshotCapture.Capture(c,tx,maker,run,facts,reasons);
            OpsSnapshotCapture.Freeze(c,tx,maker,run);
            foreach(string r in riders)SalEvidence.Gross(c,tx,run,r,150m);
            var calc=SalPayCalc.Calculate(c,tx,maker,run,new JsonObject{["riders"]=new JsonArray(riders.Select(r=>(JsonNode?)new JsonObject{["erpRiderId"]=r}).ToArray())});
            calcByRun[run]=calc["riders"]!.AsArray().OfType<JsonObject>().Select(x=>x.S("id")).ToArray();
        }
        foreach(var (run,prIds) in calcByRun)
        {
            SalDebtConfirm.Confirm(c,tx,maker,run,new JsonObject{["rows"]=new JsonArray(prIds.Select(id=>(JsonNode?)new JsonObject{["payRunRiderId"]=id,["amount"]=0}).ToArray())});
            SalSalaryApprove.Approve(c,tx,checker,run,new JsonObject{["payRunRiderIds"]=new JsonArray(prIds.Select(id=>(JsonNode?)JsonValue.Create(id)).ToArray())});
        }

        var built=SalBankFiles.Build(c,tx,maker,new JsonObject
        {
            ["periodMonth"]="2026-10-01",["issuanceChoice"]="combined",
            ["payingBankId"]=bank
        });
        Assert.IsTrue((built["excludedRows"] as JsonArray??[]).OfType<JsonObject>().Any(e=>e.S("reason")=="name_missing"));
        Assert.IsTrue(built.N("excludedCount")>=1);
        Assert.IsTrue(built.N("excludedTotal")>0);
        Assert.AreEqual(1,built["sheetRows"]!.AsArray().Count,"shared rider once in sheet");
        Assert.AreEqual(1m,built.N("lineCount"),"reported count = exported sheet rows");
        Assert.IsTrue(built["lines"]!.AsArray().Count>=2,"two app detail lines before consolidation");
        Assert.AreEqual(built.N("totalAmount"),built["control"]!.AsObject().N("totalAmount"));
        Assert.AreEqual(1m,built["control"]!.AsObject().N("riderCount"));

        // Later view must still show persisted exclusions (not empty)
        var later=SalBankFiles.View(c,tx,maker,built.S("id"));
        Assert.IsTrue((later["excludedRows"] as JsonArray??[]).Count>0,"exclusions persisted with file");
        Assert.IsTrue(later.N("excludedCount")>=1);
        tx.Commit();
    }

    // --- helpers ---
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
