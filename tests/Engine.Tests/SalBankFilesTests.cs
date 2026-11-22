using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>U-W2-17 / U-W2-17b / U-W2-17c: sal.bank_file + sal.bank_file_line build, export, mark-sent,
/// issuance choices, and automatic inter-company settlement journals. Both providers.</summary>
[TestClass]
public sealed class SalBankFilesTests
{
    static ClaimsPrincipal Actor(string? name=null,string role="Admin",string? companyId=null,string? platform=null)
    {
        var id=new ClaimsIdentity("synthetic");
        id.AddClaim(new Claim(ClaimTypes.Name,name??MoneySchemaFixture.Id()));
        id.AddClaim(new Claim(ClaimTypes.Role,role));
        id.AddClaim(new Claim("department","FIN"));
        id.AddClaim(new Claim("rankLevel","5"));
        foreach(string p in new[]{"pay.view","pay.manage","pay.approve","finance.bankfile.export","salaries.bankfile.mark_sent",
            ApprovalsArea.DualBankFile,ApprovalsArea.DualSharedRiderRelease,"approvals.decide"})
            id.AddClaim(new Claim("permission",p));
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
        SchemaTreasury.Ensure(c);
        SchemaSalInvoice.Ensure(c);SchemaSalDistribution.Ensure(c);
        SchemaSalPayRun.Ensure(c);SchemaHrMudad.Ensure(c);SchemaSalMudad.Ensure(c);SchemaDebt.Ensure(c);
        SchemaOperationCapture.Ensure(c);SchemaSalStatement.Ensure(c);SchemaFinPeriodBalance.Ensure(c);
        SchemaBankFile.Ensure(c);
        SchemaAuthCatalog.Ensure(c);SchemaApprovals.Ensure(c);
        SalRiderRequests.Ensure(c);
        return c;
    }

    static string SeedCompany(System.Data.Common.DbConnection c,string code)
    {
        string id=MoneySchemaFixture.Id();
        string unified="7"+System.Security.Cryptography.RandomNumberGenerator.GetInt32(0,1_000_000_000).ToString("D9");
        MoneySchemaFixture.Exec(c,null,"INSERT INTO hr_company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,@code,@u,@name,1,datetime('now'))",
            ("@id",id),("@code",code),("@u",unified),("@name","شركة "+code));
        return id;
    }

    static string SeedRegistration(System.Data.Common.DbConnection c,string companyId,string crNumber)
    {
        string id=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO hr_commercial_registration(id,company_id,cr_number,status,created_at) VALUES(@id,@c,@cr,1,datetime('now'))",
            ("@id",id),("@c",companyId),("@cr",crNumber));
        return id;
    }

    static string SeedBank(System.Data.Common.DbConnection c,string companyId,string registrationId,string accountCode="111201")
    {
        string id=MoneySchemaFixture.Id();
        string accId=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE code=@code",("@code",accountCode))!;
        MoneySchemaFixture.Exec(c,null,"INSERT INTO treasury_bank_account(id,company_id,registration_id,code,name_ar,account_id,status,created_at) VALUES(@id,@c,@r,@code,@name,@acc,1,datetime('now'))",
            ("@id",id),("@c",companyId),("@r",registrationId),("@code","BNK-"+id[^12..]),("@name","بنك الرواتب"),("@acc",accId));
        return id;
    }

    static void EnsurePeriod(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,string company)
    {
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) SELECT @id,@c,'2026-10-01',1,datetime('now') WHERE NOT EXISTS(SELECT 1 FROM fin_accounting_period WHERE company_id=@c AND period_month='2026-10-01')",
            ("@id",MoneySchemaFixture.Id()),("@c",company));
    }

    static readonly string[] AllKinds=["ninja_daily","ninja_daily_deduction","platform_account","assignment","housing","fuel","vehicle","scheme","eligibility",
        "keeta_invoice_row","hs_ops","hs_wallet","platform_ops","chefz_settlement","jahez_day"];

    static string FrozenRun(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal actor,string company,string app,
        string rider,bool payoutBlocked=false,Action<FakeOpsLiveFactsReader,string>? customize=null)
    {
        string run=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month,payout_blocked) VALUES(@id,@c,@app,'2026-10-01',@pb)",
            ("@id",run),("@c",company),("@app",app),("@pb",payoutBlocked?1:0));
        var facts=new FakeOpsLiveFactsReader();
        foreach(string kind in AllKinds)facts.Rows[kind]=[];
        string acc=MoneySchemaFixture.Id();
        string primary=app switch{"ninja"=>"ninja_daily","keeta"=>"keeta_invoice_row","hs"=>"hs_ops","amazon"=>"platform_ops","chefz"=>"chefz_settlement","jahez"=>"jahez_day",_=>"assignment"};
        facts.Rows[primary]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["erpPlatformAccountId"]=acc,["riderProfileId"]=rider,["erpRiderId"]=rider,["orders"]=1,["reportDate"]="2026-10-01",["workDate"]="2026-10-01",["gross"]=500m,["RiderAmount"]=500m}];
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

    static void SeedRiderDoc(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,string erpRiderId,string iqama,string name,string iban,string? registrationId=null)
    {
        var doc=new JsonObject
        {
            ["id"]=erpRiderId,["erpRiderId"]=erpRiderId,["iqama"]=iqama,["name"]=name,
            ["iban"]=iban,["verifiedIban"]=iban,["address"]="الرياض"
        };
        if(registrationId!=null)doc["registrationId"]=registrationId;
        Store.Save(c,"rider",doc,tx);
    }

    static string ApprovedRider(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,ClaimsPrincipal maker,ClaimsPrincipal checker,
        string company,string app,string rider,decimal gross,bool payoutBlocked=false,Action<FakeOpsLiveFactsReader,string>? customize=null)
    {
        string run=FrozenRun(c,tx,maker,company,app,rider,payoutBlocked,customize);
        if(app=="ninja")SalEvidence.Gross(c,tx,run,rider,gross);
        var calc=SalPayCalc.Calculate(c,tx,maker,run,new JsonObject{["riders"]=new JsonArray(new JsonObject{["erpRiderId"]=rider})});
        string prId=calc["riders"]![0]!["id"]!.GetValue<string>();
        SalDebtConfirm.Confirm(c,tx,maker,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=prId,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,checker,run,new JsonObject{["payRunRiderIds"]=new JsonArray(prId)});
        return prId;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Build_ExportCsvAndXlsx_4LetterBankShortCode_LocksPayable_AndMarkSentDualHash(bool sqlServer)
    {
        // U-W2-17: 2 eligible riders (Al Rajhi 80 -> RJHI 650 SAR, SNB 10 -> NCBK 350 SAR, total 1000 SAR)
        // + 1 unapproved rider excluded. Build locks payables, export produces 4-letter bank code first column,
        // mark-sent requires 2-person approval bound to file_hash and posts Dr 212103 / Cr Bank 1000 SAR.
        using var c=Open(sqlServer);
        string company=SeedCompany(c,"BW");
        string reg=SeedRegistration(c,company,"1010000101");
        string bank=SeedBank(c,company,reg,"111201");
        var maker=Actor(MoneySchemaFixture.Id());
        var checker=Actor(MoneySchemaFixture.Id());
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);

        string r1=MoneySchemaFixture.Id(),r2=MoneySchemaFixture.Id(),rPending=MoneySchemaFixture.Id();
        string rajhiIban=MakeIban("80","000000000000001234");
        string snbIban=MakeIban("10","000000000000005678");
        SeedRiderDoc(c,tx,r1,"2000000001","أحمد الراجحي",rajhiIban,reg);
        SeedRiderDoc(c,tx,r2,"2000000002","سالم الأهلي",snbIban,reg);
        SeedRiderDoc(c,tx,rPending,"2000000003","خالد معلق",rajhiIban,reg);

        string run=FrozenRun(c,tx,maker,company,"ninja",r1,customize:(f,acc)=>
        {
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["actualRiderProfileId"]=r2,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31",["appCode"]="ninja"});
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["actualRiderProfileId"]=rPending,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31",["appCode"]="ninja"});
        });
        SalEvidence.Gross(c,tx,run,r1,650m);
        SalEvidence.Gross(c,tx,run,r2,350m);
        SalEvidence.Gross(c,tx,run,rPending,400m);
        var calc=SalPayCalc.Calculate(c,tx,maker,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject{["erpRiderId"]=r1},new JsonObject{["erpRiderId"]=r2},new JsonObject{["erpRiderId"]=rPending})
        });
        string pr1=calc["riders"]![0]!["id"]!.GetValue<string>();
        string pr2=calc["riders"]![1]!["id"]!.GetValue<string>();
        SalDebtConfirm.Confirm(c,tx,maker,run,new JsonObject{["rows"]=new JsonArray(
            new JsonObject{["payRunRiderId"]=pr1,["amount"]=0},
            new JsonObject{["payRunRiderId"]=pr2,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,checker,run,new JsonObject{["payRunRiderIds"]=new JsonArray(pr1,pr2)});

        // Build bank file
        var built=SalBankFiles.Build(c,tx,maker,new JsonObject
        {
            ["periodMonth"]="2026-10-01",
            ["issuanceChoice"]="per_company",
            ["scopeCompanyId"]=company,
            ["payingBankId"]=bank
        });
        string fileId=built.S("id");
        Assert.AreEqual(1000m,built.N("totalAmount"));
        Assert.AreEqual(2,built["lines"]!.AsArray().Count);
        Assert.AreEqual(1,built["excludedRows"]!.AsArray().Count);
        CollectionAssert.AreEquivalent(new[]{"RJHI","NCBK"},built["lines"]!.AsArray().OfType<JsonObject>().Select(l=>l.S("bankShortCode")).ToList());

        // Attempting to build another bank file for the same payables fails (payables already reserved)
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalBankFiles.Build(c,tx,maker,new JsonObject
        {
            ["periodMonth"]="2026-10-01",["issuanceChoice"]="per_company",["scopeCompanyId"]=company,["payingBankId"]=bank
        })).Status);

        // Export CSV: UTF-8 BOM + Header + 4-letter bank code first column
        var csv=SalBankFiles.Export(c,tx,maker,fileId,"bulk_csv");
        string csvText=Encoding.UTF8.GetString(csv.Bytes[3..]);
        var csvLines=csvText.Split("\r\n",StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(BankPayrollFiles.Header,csvLines[0]);
        Assert.IsTrue(csvLines.Any(l=>l.StartsWith($"RJHI,{rajhiIban},650.00,",StringComparison.Ordinal)));
        Assert.IsTrue(csvLines.Any(l=>l.StartsWith($"NCBK,{snbIban},350.00,",StringComparison.Ordinal)));

        // Export XLSX: valid workbook with البنك first column
        var xlsx=SalBankFiles.Export(c,tx,maker,fileId,"xlsx");
        var sheets=Imports.Read(xlsx.Bytes,xlsx.Filename);
        Assert.AreEqual("البنك",sheets[0].Rows[0][0]);
        CollectionAssert.AreEquivalent(new[]{"RJHI","NCBK"},sheets[0].Rows.Skip(1).Select(r=>r[0]).ToList());

        // Mark-sent before dual approval → 403
        var dualReq=SalBankFiles.RequestMarkSent(c,tx,maker,fileId);
        Assert.AreEqual(1000m,dualReq.N("amountSar"));
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>SalBankFiles.MarkSent(c,tx,checker,fileId)).Status);

        // Checker approves dual request
        ApprovalsArea.Decide(c,tx,checker,dualReq.S("id"),new JsonObject{["decision"]="approve",["note"]="موافقة إرسال البنك"});

        // MarkSent succeeds and posts Dr 212103 1000 / Cr 111201 1000
        var sent=SalBankFiles.MarkSent(c,tx,checker,fileId);
        Assert.AreEqual("sent",sent.S("status"));
        Assert.AreEqual(3,(int)sent.N("statusCode"));
        Assert.AreEqual(1000m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,
            "SELECT COALESCE(SUM(debit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='212103') AND journal_id IN (SELECT id FROM fin_journal WHERE purpose='bank_payroll_payout' AND source_id=@f)",("@f",fileId))));
        Assert.AreEqual(1000m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,
            "SELECT COALESCE(SUM(credit),0) FROM fin_journal_line WHERE account_id=(SELECT id FROM fin_account WHERE code='111201') AND journal_id IN (SELECT id FROM fin_journal WHERE purpose='bank_payroll_payout' AND source_id=@f)",("@f",fileId))));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HeldSharedRiders_ExcludedFromSinglePlatform_AppearOnceAfterConsolidation(bool sqlServer)
    {
        // U-W2-17 / X26 / §40.5: Shared rider works Ninja (600 SAR) and Keeta (400 SAR); single-app rider works Ninja (500 SAR).
        // 1. Single-platform Ninja file includes only the single-app rider (500 SAR) and excludes the shared rider.
        // 2. After both Ninja and Keeta are approved for the shared rider, a consolidated file includes the shared rider
        //    and the exported sheet/CSV contains the shared rider ONCE with 1000.00 SAR.
        using var c=Open(sqlServer);
        string company=SeedCompany(c,"BW");
        string reg=SeedRegistration(c,company,"1010000102");
        string bank=SeedBank(c,company,reg,"111201");
        var maker=Actor(MoneySchemaFixture.Id());
        var checker=Actor(MoneySchemaFixture.Id());
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);

        string soloRider=MoneySchemaFixture.Id(),sharedRider=MoneySchemaFixture.Id();
        string rajhiIban=MakeIban("80","000000000000001234");
        string snbIban=MakeIban("10","000000000000005678");
        SeedRiderDoc(c,tx,soloRider,"2000000011","مندوب منفرد",rajhiIban,reg);
        SeedRiderDoc(c,tx,sharedRider,"2000000012","مندوب مشترك",snbIban,reg);

        string ninjaRun=FrozenRun(c,tx,maker,company,"ninja",soloRider,customize:(f,acc)=>
        {
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["actualRiderProfileId"]=sharedRider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-15",["appCode"]="ninja"});
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=sharedRider,["effectiveFrom"]="2026-10-16",["effectiveTo"]="2026-10-31",["appCode"]="keeta"});
        });
        string keetaRun=FrozenRun(c,tx,maker,company,"keeta",sharedRider,customize:(f,acc)=>
        {
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=400m,["deductions"]=0m,["orders"]=10}];
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=MoneySchemaFixture.Id(),["actualRiderProfileId"]=sharedRider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-15",["appCode"]="ninja"});
        });

        SalEvidence.Gross(c,tx,ninjaRun,soloRider,500m);
        SalEvidence.Gross(c,tx,ninjaRun,sharedRider,600m);
        var calcNinja=SalPayCalc.Calculate(c,tx,maker,ninjaRun,new JsonObject{["riders"]=new JsonArray(new JsonObject{["erpRiderId"]=soloRider},new JsonObject{["erpRiderId"]=sharedRider})});
        string prSolo=calcNinja["riders"]![0]!["id"]!.GetValue<string>();
        string prSharedNinja=calcNinja["riders"]![1]!["id"]!.GetValue<string>();

        var calcKeeta=SalPayCalc.Calculate(c,tx,maker,keetaRun,new JsonObject{["riders"]=new JsonArray(new JsonObject{["erpRiderId"]=sharedRider})});
        string prSharedKeeta=calcKeeta["riders"]![0]!["id"]!.GetValue<string>();

        SalDebtConfirm.Confirm(c,tx,maker,ninjaRun,new JsonObject{["rows"]=new JsonArray(
            new JsonObject{["payRunRiderId"]=prSolo,["amount"]=0},
            new JsonObject{["payRunRiderId"]=prSharedNinja,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,checker,ninjaRun,new JsonObject{["payRunRiderIds"]=new JsonArray(prSolo,prSharedNinja)});

        SalDebtConfirm.Confirm(c,tx,maker,keetaRun,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=prSharedKeeta,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,checker,keetaRun,new JsonObject{["payRunRiderIds"]=new JsonArray(prSharedKeeta)});

        // 1. Single-platform Ninja bank file excludes the shared rider (X26) and includes only soloRider (500 SAR)
        var ninjaFile=SalBankFiles.Build(c,tx,maker,new JsonObject
        {
            ["periodMonth"]="2026-10-01",["issuanceChoice"]="per_company",["scopeCompanyId"]=company,
            ["payingBankId"]=bank,["appCode"]="ninja"
        });
        Assert.AreEqual(500m,ninjaFile.N("totalAmount"));
        Assert.AreEqual(1,ninjaFile["sheetRows"]!.AsArray().Count);
        Assert.AreEqual("2000000011",ninjaFile["sheetRows"]![0]!["iqama"]!.GetValue<string>());

        // 2. Consolidated all-platform bank file includes sharedRider's Ninja (600) + Keeta (400) = 1000 SAR in ONE sheet row
        var consolFile=SalBankFiles.Build(c,tx,maker,new JsonObject
        {
            ["periodMonth"]="2026-10-01",["issuanceChoice"]="per_company",["scopeCompanyId"]=company,
            ["payingBankId"]=bank
        });
        Assert.AreEqual(1000m,consolFile.N("totalAmount"));
        Assert.AreEqual(2,consolFile["lines"]!.AsArray().Count); // 2 payable allocation lines
        Assert.AreEqual(1,consolFile["sheetRows"]!.AsArray().Count); // 1 consolidated rider transfer row
        Assert.AreEqual("2000000012",consolFile["sheetRows"]![0]!["iqama"]!.GetValue<string>());
        Assert.AreEqual(1000m,consolFile["sheetRows"]![0]!["amount"]!.GetValue<decimal>());

        var csv=SalBankFiles.Export(c,tx,maker,consolFile.S("id"),"bulk_csv");
        string csvText=Encoding.UTF8.GetString(csv.Bytes[3..]);
        var dataRows=csvText.Split("\r\n",StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
        Assert.AreEqual(1,dataRows.Count);
        StringAssert.StartsWith(dataRows[0],$"NCBK,{snbIban},1000.00,");
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PayoutBlocked_AllowsDoNotSendExport_RefusesMarkSentWith409(bool sqlServer)
    {
        // U-W2-17 / BR §38.5: payout_blocked=1 run builds a bank file with payout_blocked=1 and payout_label="لا يُرسل".
        // Export succeeds with DO-NOT-SEND_ prefix and clean CSV bytes; mark-sent is rejected with 409.
        using var c=Open(sqlServer);
        string company=SeedCompany(c,"BW");
        string reg=SeedRegistration(c,company,"1010000103");
        string bank=SeedBank(c,company,reg,"111201");
        var maker=Actor(MoneySchemaFixture.Id());
        var checker=Actor(MoneySchemaFixture.Id());
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,company);

        string rider=MoneySchemaFixture.Id();
        SeedRiderDoc(c,tx,rider,"2000000021","مندوب موازي",MakeIban("80","000000000000001234"),reg);
        ApprovedRider(c,tx,maker,checker,company,"ninja",rider,550m,payoutBlocked:true);

        var built=SalBankFiles.Build(c,tx,maker,new JsonObject
        {
            ["periodMonth"]="2026-10-01",["issuanceChoice"]="per_company",["scopeCompanyId"]=company,["payingBankId"]=bank
        });
        Assert.IsTrue(built.B("payoutBlocked"));
        Assert.IsTrue(built.B("doNotSend"));
        Assert.AreEqual("لا يُرسل",built.S("payoutLabel"));

        var csv=SalBankFiles.Export(c,tx,maker,built.S("id"),"bulk_csv");
        StringAssert.StartsWith(csv.Filename,"DO-NOT-SEND_");
        string csvText=Encoding.UTF8.GetString(csv.Bytes[3..]);
        Assert.IsFalse(csvText.Contains("لا يُرسل",StringComparison.Ordinal));

        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>SalBankFiles.MarkSent(c,tx,checker,built.S("id"))).Status);
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void IssuanceChoices_PerRegistration_PerCompany_Combined_StoredOnBankFile_AndDriveSheetShape(bool sqlServer)
    {
        // U-W2-17b: issuance choice stored on sal.bank_file (paying bank/company/registration;
        // per_registration vs per_company vs combined driving the sheet shape).
        using var c=Open(sqlServer);
        string compA=SeedCompany(c,"BW");
        string regA1=SeedRegistration(c,compA,"1010000201");
        string regA2=SeedRegistration(c,compA,"1010000202");
        string bankA1=SeedBank(c,compA,regA1,"111201");

        string compB=SeedCompany(c,"EX");
        string regB1=SeedRegistration(c,compB,"1010000301");
        string bankB1=SeedBank(c,compB,regB1,"111202");

        var maker=Actor(MoneySchemaFixture.Id());
        var checker=Actor(MoneySchemaFixture.Id());
        using var tx=c.BeginTransaction();
        EnsurePeriod(c,tx,compA);
        EnsurePeriod(c,tx,compB);

        string rajhiIban=MakeIban("80","000000000000001234");
        string snbIban=MakeIban("10","000000000000005678");
        string inmaIban=MakeIban("05","000000000000009012");

        string rA1=MoneySchemaFixture.Id(),rA2=MoneySchemaFixture.Id(),rA3=MoneySchemaFixture.Id(),rB1=MoneySchemaFixture.Id();
        SeedRiderDoc(c,tx,rA1,"2000000101","مندوب سجل أ١",rajhiIban,regA1);
        SeedRiderDoc(c,tx,rA2,"2000000102","مندوب سجل أ٢",snbIban,regA2);
        SeedRiderDoc(c,tx,rA3,"2000000103","مندوب شركة أ كيتا",rajhiIban,regA1);
        SeedRiderDoc(c,tx,rB1,"2000000201","مندوب شركة ب",inmaIban,regB1);

        // Company A Ninja run: rA1 (400 SAR on regA1) and rA2 (300 SAR on regA2)
        string runANinja=FrozenRun(c,tx,maker,compA,"ninja",rA1,customize:(f,acc)=>
            f.Rows["assignment"].Add(new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=acc,["actualRiderProfileId"]=rA2,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31",["appCode"]="ninja"}));
        SalEvidence.Gross(c,tx,runANinja,rA1,400m);
        SalEvidence.Gross(c,tx,runANinja,rA2,300m);
        var calcANinja=SalPayCalc.Calculate(c,tx,maker,runANinja,new JsonObject{["riders"]=new JsonArray(new JsonObject{["erpRiderId"]=rA1},new JsonObject{["erpRiderId"]=rA2})});
        string prA1=calcANinja["riders"]![0]!["id"]!.GetValue<string>();
        string prA2=calcANinja["riders"]![1]!["id"]!.GetValue<string>();
        SalDebtConfirm.Confirm(c,tx,maker,runANinja,new JsonObject{["rows"]=new JsonArray(
            new JsonObject{["payRunRiderId"]=prA1,["amount"]=0},
            new JsonObject{["payRunRiderId"]=prA2,["amount"]=0})});
        SalSalaryApprove.Approve(c,tx,checker,runANinja,new JsonObject{["payRunRiderIds"]=new JsonArray(prA1,prA2)});

        // 1. per_registration for regA1 (resolving payingBankId from payingCompanyId + payingRegistrationId):
        //    Only rA1 (400 SAR) enters; rA2 (regA2) stays unclaimed.
        var fileReg=SalBankFiles.Build(c,tx,maker,new JsonObject
        {
            ["periodMonth"]="2026-10-01",
            ["issuanceChoice"]="per_registration",
            ["scopeCompanyId"]=compA,
            ["scopeRegistrationId"]=regA1,
            ["payingCompanyId"]=compA,
            ["payingRegistrationId"]=regA1
        });
        Assert.AreEqual("per_registration",fileReg.S("issuanceChoice"));
        Assert.AreEqual("per_registration",fileReg.S("sheetShape"));
        Assert.AreEqual(compA,fileReg.S("scopeCompanyId"));
        Assert.AreEqual(regA1,fileReg.S("scopeRegistrationId"));
        Assert.AreEqual(compA,fileReg.S("payingCompanyId"));
        Assert.AreEqual(regA1,fileReg.S("payingRegistrationId"));
        Assert.AreEqual(bankA1,fileReg.S("payingBankId"));
        Assert.AreEqual("BW",fileReg.S("payingCompanyCode"));
        Assert.AreEqual("1010000201",fileReg.S("payingRegistrationNumber"));
        Assert.AreEqual("1010000201",fileReg.S("scopeRegistrationNumber"));
        Assert.AreEqual(400m,fileReg.N("totalAmount"));
        Assert.AreEqual(1,fileReg["sheetRows"]!.AsArray().Count);
        Assert.AreEqual("2000000101",fileReg["sheetRows"]![0]!["iqama"]!.GetValue<string>());

        // 2. per_company for compA: picks up the remaining unclaimed rider rA2 (300 SAR on regA2)
        var fileComp=SalBankFiles.Build(c,tx,maker,new JsonObject
        {
            ["periodMonth"]="2026-10-01",
            ["issuanceChoice"]="per_company",
            ["scopeCompanyId"]=compA,
            ["payingBankId"]=bankA1
        });
        Assert.AreEqual("per_company",fileComp.S("issuanceChoice"));
        Assert.AreEqual("per_company",fileComp.S("sheetShape"));
        Assert.AreEqual(compA,fileComp.S("scopeCompanyId"));
        Assert.AreEqual("",fileComp.S("scopeRegistrationId"));
        Assert.AreEqual(300m,fileComp.N("totalAmount"));
        Assert.AreEqual(1,fileComp["sheetRows"]!.AsArray().Count);
        Assert.AreEqual("2000000102",fileComp["sheetRows"]![0]!["iqama"]!.GetValue<string>());

        // 3. combined across Company A (rA3 on Keeta = 350 SAR) and Company B (rB1 on Ninja = 500 SAR), paid by Bank A1:
        ApprovedRider(c,tx,maker,checker,compA,"keeta",rA3,350m,customize:(f,acc)=>
            f.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpPlatformAccountId"]=acc,["gross"]=350m,["deductions"]=0m,["orders"]=10}]);
        ApprovedRider(c,tx,maker,checker,compB,"ninja",rB1,500m);

        var fileCombined=SalBankFiles.Build(c,tx,maker,new JsonObject
        {
            ["periodMonth"]="2026-10-01",
            ["issuanceChoice"]="combined",
            ["payingBankId"]=bankA1
        });
        Assert.AreEqual("combined",fileCombined.S("issuanceChoice"));
        Assert.AreEqual("combined",fileCombined.S("sheetShape"));
        Assert.AreEqual("",fileCombined.S("scopeCompanyId"));
        Assert.AreEqual("",fileCombined.S("scopeRegistrationId"));
        Assert.AreEqual(compA,fileCombined.S("payingCompanyId"));
        Assert.AreEqual(regA1,fileCombined.S("payingRegistrationId"));
        Assert.AreEqual(bankA1,fileCombined.S("payingBankId"));
        Assert.AreEqual(850m,fileCombined.N("totalAmount"));
        Assert.AreEqual(2,fileCombined["sheetRows"]!.AsArray().Count);
        Assert.AreEqual(1,fileCombined["settlements"]!.AsArray().Count);
        Assert.AreEqual(500m,fileCombined["settlements"]![0]!["amount"]!.GetValue<decimal>());
        _=bankB1;
        tx.Commit();
    }
}
