using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using Claim=System.Security.Claims.Claim;

/// <summary>TRIAL5: grok-TRIAL4 HIGH rows (bank upload audit keys; zero entitlement journal).</summary>
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

    static string FrozenKeeta(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string company,string rider)
    {
        string run=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month) VALUES(@id,@c,'keeta','2026-10-01')",("@id",run),("@c",company));
        var facts=new FakeOpsLiveFactsReader();
        foreach(string kind in new[]{"ninja_daily","ninja_daily_deduction","platform_account","assignment","housing","fuel","vehicle","scheme","eligibility",
            "keeta_invoice_row","hs_ops","hs_wallet","platform_ops","chefz_settlement","jahez_day"})
            facts.Rows[kind]=[];
        string account=MoneySchemaFixture.Id();
        // Commission equals gross so entitlement is 0 (COA1 B2 / grok-TRIAL4).
        facts.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["erpPlatformAccountId"]=account,["riderProfileId"]=rider,["erpRiderId"]=rider,["orders"]=1,["reportDate"]="2026-10-01",["RiderAmount"]=100,["gross"]=100,["commission"]=100}];
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
        EnsureSalPeriod(c,tx,company);
        string rider=MoneySchemaFixture.Id();
        string run=FrozenKeeta(c,tx,maker,company,rider);
        // Bind calc from frozen evidence first, then force gross==commission for the accrual edge case.
        var calc=SalEvidence.Calculate(c,tx,maker,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["schemeType"]="commission",
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        });
        string riderId=calc["riders"]![0]!["id"]!.GetValue<string>();
        MoneySchemaFixture.Exec(c,tx,
            "UPDATE sal_pay_run_rider SET gross=100,commission=100,salary_net=0 WHERE id=@id",
            ("@id",riderId));
        SalDebtConfirm.Confirm(c,tx,maker,run,new JsonObject
        {
            ["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})
        });
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
        tx.Commit();
    }
}
