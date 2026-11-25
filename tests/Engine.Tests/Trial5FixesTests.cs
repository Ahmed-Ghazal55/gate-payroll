using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>TRIAL5: grok-TRIAL4 HIGH rows (bank upload audit; salary Independent; zero journal lines).</summary>
[TestClass]
public sealed class Trial5FixesTests
{
    static ClaimsPrincipal Admin(string? name=null)
    {
        var id=new ClaimsIdentity("synthetic");
        id.AddClaim(new Claim(ClaimTypes.Name,name??MoneySchemaFixture.Id()));
        id.AddClaim(new Claim(ClaimTypes.Role,"Admin"));
        id.AddClaim(new Claim("permission","pay.manage"));
        id.AddClaim(new Claim("permission","pay.approve"));
        return new ClaimsPrincipal(id);
    }

    static DbConnection OpenSal(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,null,"IF OBJECT_ID(N'dbo.records',N'U') IS NULL CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL);");
        else
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
        SchemaSalInvoice.Ensure(c);SchemaSalDistribution.Ensure(c);
        SchemaSalPayRun.Ensure(c);SchemaDebt.Ensure(c);
        SchemaOperationCapture.Ensure(c);SchemaSalStatement.Ensure(c);SchemaFinPeriodBalance.Ensure(c);
        return c;
    }

    static string FrozenKeeta(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string company,string rider,decimal gross=100,decimal commission=100)
    {
        string run=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month) VALUES(@id,@c,'keeta','2026-10-01')",("@id",run),("@c",company));
        var facts=new FakeOpsLiveFactsReader();
        foreach(string kind in new[]{"ninja_daily","ninja_daily_deduction","platform_account","assignment","housing","fuel","vehicle","scheme","eligibility",
            "keeta_invoice_row","hs_ops","hs_wallet","platform_ops","chefz_settlement","jahez_day"})
            facts.Rows[kind]=[];
        string account=MoneySchemaFixture.Id();
        facts.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["erpPlatformAccountId"]=account,["riderProfileId"]=rider,["erpRiderId"]=rider,["orders"]=1,["reportDate"]="2026-10-01",["RiderAmount"]=gross,["gross"]=gross,["commission"]=commission}];
        facts.Rows["platform_account"]=[new(){["id"]=account,["externalAccountId"]="A1",["code"]="A1"}];
        facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=rider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31"}];
        facts.Rows["scheme"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["schemeCode"]="commission",["source"]="pay_settings"}];
        facts.Rows["eligibility"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["eligible"]=true,["source"]="pay_settings"}];
        var reasons=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var kv in facts.Rows)if(kv.Value.Count==0)reasons[kv.Key]="none";
        OpsSnapshotCapture.Capture(c,tx,actor,run,facts,reasons);
        OpsSnapshotCapture.Freeze(c,tx,actor,run);
        return run;
    }

    static void EnsureSalPeriod(DbConnection c,DbTransaction tx,string company)
    {
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE company_id=@c AND period_month=@m",("@c",company),("@m","2026-10-01"));
        if(existing is not null and not DBNull)return;
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","accounting_period")}(id,company_id,period_month,state,created_at)
            VALUES(@id,@c,'2026-10-01',1,{SchemaSql.NowLiteral(c)})
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@c",company));
    }

    static void EnsureSponsorCompany(DbConnection c,DbTransaction tx,string companyId)
    {
        var sp=FinancialCore.ResolveSponsor(companyId);
        object? exists=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"hr","company")} WHERE id=@id",("@id",companyId));
        if(exists is not null and not DBNull)return;
        string unified=sp.UnifiedNumber.Length>=10?sp.UnifiedNumber:"7"+companyId.Replace("-","")[..9];
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"hr","company")}(id,code,unified_number,name_ar,status,created_at)
            VALUES(@id,@code,@u,@n,1,{SchemaSql.NowLiteral(c)})
            """,("@id",companyId),("@code",sp.Code),("@u",unified),("@n",sp.NameAr));
    }

    static string EnsureRegistration(DbConnection c,DbTransaction tx,string companyId)
    {
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"hr","commercial_registration")} WHERE company_id=@c",("@c",companyId));
        if(existing is not null and not DBNull)return existing switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(existing,CultureInfo.InvariantCulture)!};
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"hr","commercial_registration")}(id,company_id,cr_number,status,created_at)
            VALUES(@id,@c,@cr,1,{SchemaSql.NowLiteral(c)})
            """,("@id",id),("@c",companyId),("@cr",id.Replace("-","")[..20]));
        return id;
    }

    static string AccountId(DbConnection c,DbTransaction tx,string code)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","account")} WHERE code=@c",("@c",code));
        Assert.IsNotNull(id,$"missing CoA {code}");
        return id switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(id,CultureInfo.InvariantCulture)!};
    }

    static string EnsureBankAccount(DbConnection c,DbTransaction tx,string companyId,string registrationId,string accountCode)
    {
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"treasury","bank_account")}(id,company_id,registration_id,code,name_ar,account_id)
            VALUES(@id,@c,@r,@code,'بنك اختبار',@a)
            """,("@id",id),("@c",companyId),("@r",registrationId),("@code","BNK-"+id[..8]),("@a",AccountId(c,tx,accountCode)));
        return id;
    }

    static (string run,string riderId) PrepRider(DbConnection c,DbTransaction tx,ClaimsPrincipal preparer,string company,string rider,decimal gross,decimal commission)
    {
        EnsureSalPeriod(c,tx,company);
        string run=FrozenKeeta(c,tx,preparer,company,rider,gross,commission);
        var calc=SalEvidence.Calculate(c,tx,preparer,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["schemeType"]="commission",
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        });
        string riderId=calc["riders"]![0]!["id"]!.GetValue<string>();
        MoneySchemaFixture.Exec(c,tx,
            "UPDATE sal_pay_run_rider SET gross=@g,commission=@cm,salary_net=@n WHERE id=@id",
            ("@g",gross),("@cm",commission),("@n",Math.Max(0m,gross-commission)),("@id",riderId));
        SalDebtConfirm.Confirm(c,tx,preparer,run,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})
        });
        return (run,riderId);
    }

    [TestMethod]
    public void BankStatement_Upload_AuditHasEntityRecordIdLineCountFileHash()
    {
        // grok-TRIAL4 HIGH: Store.Audit must keep entity/recordId; upload also stores lineCount + fileHash.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string statementId="";
        string expectedHash="";
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            string reg=EnsureRegistration(c,tx,company);
            string bank=EnsureBankAccount(c,tx,company,reg,"111201");
            var lines=new JsonArray(new JsonObject{["occurredOn"]="2026-11-10",["amount"]=100,["reference"]="SYN-DEP"});
            string fileContent=string.Join("\n",lines.OfType<JsonObject>().Select(l=>$"{l.S("occurredOn")}|{l.S("reference")}|{l.N("amount")}"));
            expectedHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fileContent)));
            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,PayFixture.Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=100,
                ["lines"]=lines
            });
            statementId=uploaded.S("id");
            return true;
        });
        var audit=f.Store.Read(c=>Store.List(c,"audit").First(a=>a.S("action")=="treasury.bank_statement_uploaded"));
        Assert.AreEqual(statementId,audit.S("entity"));
        Assert.AreEqual(statementId,audit.S("recordId"));
        Assert.AreEqual(1m,audit.N("lineCount"));
        Assert.AreEqual(expectedHash,audit.S("fileHash"),ignoreCase:true);
    }

    [TestMethod]
    public void BankAdjust_ApproveBlockedUntilBankTimingAccount_RejectCloses()
    {
        // TRIAL6: generic adjust path removed (409); vault variance accounts never used.
        using var f=new PayFixture();
        var maker=PayFixture.Actor("synthetic-trial5-bank-maker","Admin");
        string company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            string reg=EnsureRegistration(c,tx,company);
            string bank=EnsureBankAccount(c,tx,company,reg,"111201");
            object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE company_id=@c AND period_month=@m",("@c",company),("@m","2026-11-01"));
            if(existing is null or DBNull)
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"fin","accounting_period")}(id,company_id,period_month,state,created_at)
                    VALUES(@id,@c,'2026-11-01',1,{SchemaSql.NowLiteral(c)})
                    """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@c",company));
            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=100,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-11-10",["amount"]=100,["reference"]="SYN-ADJ"})
            });
            var err=Assert.ThrowsExactly<ApiError>(()=>TreasuryBankStatementsApi.RequestAdjust(c,tx,maker,uploaded.S("id"),new(){["amount"]=5,["reason"]="فرق توقيت بنكي تجريبي"}));
            Assert.AreEqual(409,err.Status);
            Assert.IsTrue(err.Message.Contains("سطر",StringComparison.Ordinal)||err.Message.Contains("قيد",StringComparison.Ordinal)||err.Message.Contains("422202",StringComparison.Ordinal));
            return true;
        });
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>TreasuryBankStatementsApi.ApproveAdjust(c,tx,maker,"legacy-adjust"))).Status);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SalarySheet_PreparerCannotApprove_403_NoJournal(bool sqlServer)
    {
        // grok-TRIAL4 HIGH / Ghazal: preparer never approves or posts the same salary sheet.
        if(sqlServer&&string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION")))
        {Assert.Inconclusive("SQL Server connection not configured");return;}
        using var c=OpenSal(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var preparer=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        var (run,riderId)=PrepRider(c,tx,preparer,company,rider,300,0);
        long journalsBefore=Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM fin_journal")??0L);
        var err=Assert.ThrowsExactly<ApiError>(()=>SalSalaryApprove.Approve(c,tx,preparer,run,new JsonObject
        {
            ["payRunRiderIds"]=new JsonArray(riderId)
        }));
        Assert.AreEqual(403,err.Status);
        Assert.AreEqual(journalsBefore,Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM fin_journal")??0L));
        object? approvedAt=MoneySchemaFixture.Scalar(c,tx,"SELECT salary_approved_at FROM sal_pay_run_rider WHERE id=@id",("@id",riderId));
        Assert.IsTrue(approvedAt is null or DBNull);
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SalaryApprove_GrossEqualsCommission_PostsBalancedJournal_NoZeroPayableLine(bool sqlServer)
    {
        // grok-TRIAL4 HIGH / COA1 B2: entitlement 0 → omit Cr 212103; Dr receivable / Cr agent revenue only.
        if(sqlServer&&string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION")))
        {Assert.Inconclusive("SQL Server connection not configured");return;}
        using var c=OpenSal(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var maker=Admin();
        var checker=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        var (run,riderId)=PrepRider(c,tx,maker,company,rider,100,100);
        var approved=SalSalaryApprove.Approve(c,tx,checker,run,new JsonObject
        {
            ["payRunRiderIds"]=new JsonArray(riderId)
        });
        Assert.IsFalse(string.IsNullOrEmpty(approved["rows"]![0]!["salaryApprovedAt"]!.GetValue<string>()));
        long journals=Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,
            "SELECT COUNT(*) FROM fin_journal WHERE purpose='salary_accrual' AND status=2 AND source_id=@r",
            ("@r",riderId)));
        Assert.AreEqual(1L,journals);
        long zeroLines=Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,"""
            SELECT COUNT(*) FROM fin_journal_line jl
            INNER JOIN fin_journal j ON j.id=jl.journal_id
            WHERE j.purpose='salary_accrual' AND j.source_id=@r AND jl.debit=0 AND jl.credit=0
            """,("@r",riderId)));
        Assert.AreEqual(0L,zeroLines);
        long payableLines=Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,"""
            SELECT COUNT(*) FROM fin_journal_line jl
            INNER JOIN fin_journal j ON j.id=jl.journal_id
            INNER JOIN fin_account a ON a.id=jl.account_id
            WHERE j.purpose='salary_accrual' AND j.source_id=@r AND a.code='212103'
            """,("@r",riderId)));
        Assert.AreEqual(0L,payableLines);
        decimal sumD=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"""
            SELECT COALESCE(SUM(jl.debit),0) FROM fin_journal_line jl
            INNER JOIN fin_journal j ON j.id=jl.journal_id
            WHERE j.purpose='salary_accrual' AND j.source_id=@r
            """,("@r",riderId)),CultureInfo.InvariantCulture);
        decimal sumC=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"""
            SELECT COALESCE(SUM(jl.credit),0) FROM fin_journal_line jl
            INNER JOIN fin_journal j ON j.id=jl.journal_id
            WHERE j.purpose='salary_accrual' AND j.source_id=@r
            """,("@r",riderId)),CultureInfo.InvariantCulture);
        Assert.AreEqual(100m,sumD);
        Assert.AreEqual(100m,sumC);
        // Full gross/commission trail stays on the salary sheet snapshot notes (not as zero journal lines).
        string payload=(string)MoneySchemaFixture.Scalar(c,tx,
            "SELECT payload FROM sal_rider_statement_snapshot WHERE pay_run_rider_id=@r",
            ("@r",riderId))!;
        Assert.IsTrue(payload.Contains("\"gross\":100",StringComparison.Ordinal)||payload.Contains("\"gross\": 100",StringComparison.Ordinal));
        Assert.IsTrue(payload.Contains("commission",StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(payload.Contains("notes",StringComparison.OrdinalIgnoreCase));
        tx.Commit();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SalaryApprove_NormalCommission_KeepsPayableLineBalanced(bool sqlServer)
    {
        // Normal case unchanged: gross 100 / commission 30 → Cr 212103 70 + Cr agent 30.
        if(sqlServer&&string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION")))
        {Assert.Inconclusive("SQL Server connection not configured");return;}
        using var c=OpenSal(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        var maker=Admin();
        var checker=Admin();
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        var (run,riderId)=PrepRider(c,tx,maker,company,rider,100,30);
        SalSalaryApprove.Approve(c,tx,checker,run,new JsonObject{["payRunRiderIds"]=new JsonArray(riderId)});
        decimal payableCr=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"""
            SELECT COALESCE(SUM(jl.credit),0) FROM fin_journal_line jl
            INNER JOIN fin_journal j ON j.id=jl.journal_id
            INNER JOIN fin_account a ON a.id=jl.account_id
            WHERE j.purpose='salary_accrual' AND j.source_id=@r AND a.code='212103'
            """,("@r",riderId)),CultureInfo.InvariantCulture);
        Assert.AreEqual(70m,payableCr);
        decimal sumD=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"""
            SELECT COALESCE(SUM(jl.debit),0) FROM fin_journal_line jl
            INNER JOIN fin_journal j ON j.id=jl.journal_id
            WHERE j.purpose='salary_accrual' AND j.source_id=@r
            """,("@r",riderId)),CultureInfo.InvariantCulture);
        decimal sumC=Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"""
            SELECT COALESCE(SUM(jl.credit),0) FROM fin_journal_line jl
            INNER JOIN fin_journal j ON j.id=jl.journal_id
            WHERE j.purpose='salary_accrual' AND j.source_id=@r
            """,("@r",riderId)),CultureInfo.InvariantCulture);
        Assert.AreEqual(sumD,sumC);
        tx.Commit();
    }

    [TestMethod]
    public void JournalPosting_DropsZeroAmountLines_KeepsBalance()
    {
        // Shared builder: never post a line with 0 debit and 0 credit.
        var c=MoneySchemaFixture.Open(false);
        SchemaOrgCostCentre.Ensure(c);SchemaFinAccount.Ensure(c);SchemaFinPeriod.Ensure(c);SchemaFinJournal.Ensure(c);SchemaFinPeriodBalance.Ensure(c);
        using(c)
        {
            string company=MoneySchemaFixture.Company(c),period=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-11-01',1,datetime('now'))",("@id",period),("@c",company));
            string debit=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=1 AND nature=1 ORDER BY code LIMIT 1")!;
            string credit=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=1 AND nature=2 ORDER BY code LIMIT 1")!;
            var posted=JournalPosting.Post(c,null,new JournalPosting.Draft(
                Guid.Parse(company),Guid.Parse(period),new DateOnly(2026,11,1),new DateOnly(2026,11,15),
                "test_source",Guid.NewGuid(),"manual",0,false,"synthetic",
                [
                    new(Guid.Parse(debit),SchemaOrgCostCentre.NoneId,"none",100m,0m,"مدين"),
                    new(Guid.Parse(credit),SchemaOrgCostCentre.NoneId,"none",0m,0m,"صفر يُحذف"),
                    new(Guid.Parse(credit),SchemaOrgCostCentre.NoneId,"none",0m,100m,"دائن")
                ]));
            Assert.IsFalse(posted.AlreadyPosted);
            Assert.AreEqual(2L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,
                "SELECT COUNT(*) FROM fin_journal_line WHERE journal_id=@id",
                ("@id",SchemaSql.GuidText(posted.JournalId)))!));
            Assert.AreEqual(0L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,
                "SELECT COUNT(*) FROM fin_journal_line WHERE journal_id=@id AND debit=0 AND credit=0",
                ("@id",SchemaSql.GuidText(posted.JournalId)))!));
        }
    }

    [TestMethod]
    public void RiderRequest_PreparerCannotApprove_PathCovered()
    {
        // Path: rider request approve (Independent already enforced; regression lock).
        var c=MoneySchemaFixture.Payments(false);
        using(c)
        {
            SchemaSalPayRun.Ensure(c);SchemaAuthCatalog.Ensure(c);
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
            string company=MoneySchemaFixture.Company(c);
            var maker=Admin();
            using var tx=c.BeginTransaction();
            var hold=SalRiderRequests.Create(c,tx,maker,new JsonObject
            {
                ["companyId"]=company,["erpRiderId"]=MoneySchemaFixture.Id(),["periodMonth"]="2026-10-01",["appCode"]="ninja",
                ["kind"]="hold",["reasonCode"]="shared",["note"]="إيقاف مصطنع"
            });
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>SalRiderRequests.Approve(c,tx,maker,hold.S("id"),new JsonObject{["note"]="نفس الشخص"})).Status);
            tx.Commit();
        }
    }

    [TestMethod]
    public void PaymentDocument_PreparerCannotApprove_PathCovered()
    {
        // Path: payment document approve (Independent already enforced; regression lock).
        using var f=new PaymentsEngineTests.Fixture(false);
        var input=f.Input("purchase");
        input["supplierId"]=MoneySchemaFixture.Id();
        f.Store.Write((c,tx)=>{
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"pay","supplier")}(id,company_id,name_ar,status,created_by,created_at)
                VALUES(@id,@c,'مورد مصطنع',1,@by,{SchemaSql.NowLiteral(c)})
                """,("@id",input.S("supplierId")),("@c",f.Company),("@by",f.Maker.Identity!.Name!));
            return true;
        });
        var doc=f.Create(input);
        f.Store.Write((c,tx)=>PaymentsEngine.Transition(c,tx,f.Maker,doc.S("id"),"submit",new()));
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaymentsEngine.Transition(c,tx,f.Maker,doc.S("id"),"approve",new()))).Status);
    }
}
