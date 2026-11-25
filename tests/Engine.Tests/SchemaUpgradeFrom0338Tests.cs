using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.RegularExpressions;

/// <summary>Live upgrade: build the 0.33.8 shape of sal.bank_file* from a committed fixture, insert rows, start 0.34 twice, rows unchanged.</summary>
[TestClass]
public sealed class SchemaUpgradeFrom0338Tests
{
    const string FixtureRelative="fixtures/schema-0.33.8-bank-file.sql";

    static bool TryOpenAdmin(out SqlConnectionStringBuilder builder,out SqlConnection admin)
    {
        builder=new SqlConnectionStringBuilder();
        admin=null!;
        string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if(string.IsNullOrWhiteSpace(configured)){Assert.Inconclusive("Explicit isolated loopback SQL required.");return false;}
        builder=new SqlConnectionStringBuilder(configured);
        Assert.IsTrue(Regex.IsMatch(builder.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",RegexOptions.IgnoreCase));
        builder.InitialCatalog="master";
        admin=new SqlConnection(builder.ToString());
        admin.Open();
        return true;
    }

    /// <summary>Committed 0.33.8 bank-file schema (no git, no network).</summary>
    static string Read0338Fixture()
    {
        string beside=Path.Combine(AppContext.BaseDirectory,FixtureRelative);
        if(File.Exists(beside))return File.ReadAllText(beside);
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir!=null;dir=dir.Parent)
        {
            string underTests=Path.Combine(dir.FullName,"tests","Engine.Tests",FixtureRelative);
            if(File.Exists(underTests))return File.ReadAllText(underTests);
            string underProject=Path.Combine(dir.FullName,FixtureRelative);
            if(File.Exists(underProject))return File.ReadAllText(underProject);
        }
        throw new InvalidOperationException("Missing fixture "+FixtureRelative+" (must ship with Engine.Tests; no git).");
    }

    static string Section(string fixture,string marker)
    {
        int start=fixture.IndexOf(marker,StringComparison.Ordinal);
        Assert.IsTrue(start>=0,"fixture missing section "+marker);
        start+=marker.Length;
        int end=fixture.IndexOf("\n-- TABLE ",start,StringComparison.Ordinal);
        string body=(end<0?fixture[start..]:fixture[start..end]).Trim();
        Assert.IsFalse(string.IsNullOrWhiteSpace(body),"fixture section "+marker+" is empty");
        return body;
    }

    /// <summary>0.33.8 SchemaBankFile EnsureTable bodies — tables exist without frozen rider meta.</summary>
    static void EnsureMain0338BankFileTables(SqlConnection c,string fixture)
    {
        Assert.IsFalse(fixture.Contains("rider_iqama",StringComparison.Ordinal),"0.33.8 fixture bank_file_line has no rider meta columns");
        Assert.IsFalse(fixture.Contains("confirm_unverified_ibans",StringComparison.Ordinal),"0.33.8 fixture bank_file has no confirm_unverified_ibans");
        Assert.IsFalse(fixture.Contains("EnsureFrozenMeta",StringComparison.Ordinal),"0.33.8 fixture must be pre-frozen-meta");
        Assert.IsTrue(fixture.Contains("ImmutableInsteadOfUpdate columns at 0.33.8: bank_file_id,period_month,pay_run_rider_id,payable_id,company_id,registration_id,payable_period_month,payment_id,allocation_id,amount,bank_short_code,generation",StringComparison.Ordinal));

        SchemaSql.EnsureSchema(c,null,"sal");
        SchemaSql.EnsureTable(c,null,"sal","bank_file",Section(fixture,"-- TABLE sal.bank_file"));
        SchemaSql.EnsureTable(c,null,"sal","bank_file_line",Section(fixture,"-- TABLE sal.bank_file_line"));
        static bool Missing(object? v)=>v is null or DBNull;
        Assert.IsTrue(Missing(SchemaSql.Scalar(c,null,"SELECT COL_LENGTH(N'sal.bank_file_line',N'rider_iqama')")),"0338 bank_file_line must lack rider_iqama");
        Assert.IsTrue(Missing(SchemaSql.Scalar(c,null,"SELECT COL_LENGTH(N'sal.bank_file',N'confirm_unverified_ibans')")),"0338 bank_file must lack confirm_unverified_ibans");
    }

    static void ApplyMasterExceptBankFile(SqlConnection c)
    {
        SchemaSql.Exec(c,null,"""
            IF OBJECT_ID(N'dbo.records', N'U') IS NULL
              CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL CHECK(ISJSON(body)=1));
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_records_kind')
              CREATE INDEX ix_records_kind ON dbo.records(kind);
            """);
        ErpDataIndexes.Ensure(c);
        PayRunInputs.Ensure(c);
        SchemaRef.Ensure(c);
        SchemaHrCompany.Ensure(c);
        SchemaRef.EnsureLegacyAccountMapAfterCompany(c);
        SchemaOrgCostCentre.Ensure(c);
        SchemaAuthCatalog.Ensure(c);
        SchemaOrgStructure.Ensure(c);
        SchemaApprovals.Ensure(c);
        SchemaAuditNotify.Ensure(c);
        SchemaArchive.Ensure(c);
        SchemaFormApp.Ensure(c);
        SchemaFinAccount.Ensure(c);
        SchemaFinPeriod.Ensure(c);
        SchemaFinJournal.Ensure(c);
        SchemaFinPeriodBalance.Ensure(c);
        SchemaPayPayable.Ensure(c);
        SchemaPayAllocation.Ensure(c);
        SchemaPaymentDocuments.Ensure(c);
        SchemaPaymentSources.Ensure(c);
        SchemaRecurringPayees.Ensure(c);
        SchemaSalInvoice.Ensure(c);
        SchemaSalDistribution.Ensure(c);
        SchemaOpsPlatformAccount.Ensure(c);
        SchemaKeetaHwm.Ensure(c);
        SchemaSalPayRun.Ensure(c);
        SchemaHrMudad.Ensure(c);
        SchemaSalMudad.Ensure(c);
        SchemaOperationCapture.Ensure(c);
        SchemaDebt.Ensure(c);
        SchemaTreasury.Ensure(c);
        // Intentionally skip SchemaBankFile — create the 0.33.8 table shape instead.
    }

    [TestMethod]
    public void SqlServerUpgradeFromMain0338PreservesSyntheticRows()
    {
        string fixture=Read0338Fixture();

        if(!TryOpenAdmin(out var builder,out var admin))return;
        string db="GateUpgrade0338_"+Guid.NewGuid().ToString("N");
        string dir=Path.Combine(Path.GetTempPath(),db);
        Directory.CreateDirectory(dir);
        using(var q=admin.CreateCommand()){q.CommandText="CREATE DATABASE ["+db+"]";q.ExecuteNonQuery();}
        builder.InitialCatalog=db;
        string cs=builder.ToString();
        try
        {
            using(var bootstrap=new SqlConnection(cs))
            {
                bootstrap.Open();
                ApplyMasterExceptBankFile(bootstrap);
                EnsureMain0338BankFileTables(bootstrap,fixture);

                string company=MoneySchemaFixture.Company(bootstrap);
                string registration=SchemaTreasuryTests.Registration(bootstrap,company);
                string bank=SchemaTreasuryTests.Account(bootstrap,company,registration);
                string run=MoneySchemaFixture.Run(bootstrap,company);
                string due=MoneySchemaFixture.Payable(bootstrap,company);
                string rider=MoneySchemaFixture.Rider(bootstrap,run,company,due,100);
                MoneySchemaFixture.Exec(bootstrap,null,"UPDATE sal_pay_run_rider SET debt_confirm_status='deferred',salary_approved_at=SYSUTCDATETIME() WHERE id=@id",("@id",rider));
                string payment=MoneySchemaFixture.Payment(bootstrap,company,payer:company);
                string lineId=MoneySchemaFixture.Id();
                string claim=MoneySchemaFixture.Claim(bootstrap,company,payment,due,100,line:lineId);
                string bankCode=(string)MoneySchemaFixture.Scalar(bootstrap,null,"SELECT short_code FROM ref_saudi_bank WHERE active=1")!;
                string fileId=MoneySchemaFixture.Id();
                MoneySchemaFixture.Exec(bootstrap,null,"""
                    INSERT INTO sal_bank_file(id,period_month,issuance_choice,scope_company_id,paying_company_id,paying_registration_id,paying_bank_id,payout_blocked,created_by)
                    VALUES(@id,'2026-11-01','combined',NULL,@c,@r,@b,0,@id)
                    """,("@id",fileId),("@c",company),("@r",registration),("@b",bank));
                MoneySchemaFixture.Exec(bootstrap,null,"""
                    INSERT INTO sal_bank_file_line(id,bank_file_id,period_month,pay_run_rider_id,payable_id,company_id,registration_id,payable_period_month,payment_id,allocation_id,amount,bank_short_code)
                    VALUES(@id,@f,'2026-11-01',@rider,@due,@c,@cr,'2026-11-01',@p,@a,100,@bank)
                    """,("@id",lineId),("@f",fileId),("@rider",rider),("@due",due),("@c",company),("@cr",registration),("@p",payment),("@a",claim),("@bank",bankCode));

                string body="""{"id":"upgrade-synth-1","name":"Synthetic preserved","marker":"rel34-upgrade"}""";
                SchemaSql.Exec(bootstrap,null,"INSERT INTO dbo.records(id,kind,body) VALUES(@id,N'test',@body)",("@id","upgrade-synth-1"),("@body",body));

                Assert.AreEqual(1,Convert.ToInt32(SchemaSql.Scalar(bootstrap,null,"SELECT COUNT(1) FROM sal.bank_file")!));
                Assert.AreEqual(1,Convert.ToInt32(SchemaSql.Scalar(bootstrap,null,"SELECT COUNT(1) FROM sal.bank_file_line")!));
                Assert.AreEqual(100m,Convert.ToDecimal(SchemaSql.Scalar(bootstrap,null,"SELECT amount FROM sal.bank_file_line WHERE id=@id",("@id",lineId))!));
                Assert.AreEqual(bankCode,(string)SchemaSql.Scalar(bootstrap,null,"SELECT bank_short_code FROM sal.bank_file_line WHERE id=@id",("@id",lineId))!);
            }

            var first=new Store(dir,cs);
            Assert.IsTrue(first.DataPreservationVerified);
            first.Read(c=>{
                Assert.IsNotNull(SchemaSql.Scalar(c,null,"SELECT COL_LENGTH(N'sal.bank_file_line',N'rider_iqama')"));
                Assert.IsNotNull(SchemaSql.Scalar(c,null,"SELECT COL_LENGTH(N'sal.bank_file',N'confirm_unverified_ibans')"));
                Assert.AreEqual(1,Convert.ToInt32(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM sal.bank_file")!));
                Assert.AreEqual(1,Convert.ToInt32(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM sal.bank_file_line")!));
                Assert.AreEqual(100m,Convert.ToDecimal(SchemaSql.Scalar(c,null,"SELECT amount FROM sal.bank_file_line")!));
                Assert.AreEqual("Synthetic preserved",Store.Get(c,"upgrade-synth-1","test")!.S("name"));
                Assert.AreEqual("rel34-upgrade",Store.Get(c,"upgrade-synth-1","test")!.S("marker"));
                return true;
            });

            var second=new Store(dir,cs);
            Assert.IsTrue(second.DataPreservationVerified);
            second.Read(c=>{
                Assert.AreEqual(1,Convert.ToInt32(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM sal.bank_file")!));
                Assert.AreEqual(1,Convert.ToInt32(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM sal.bank_file_line")!));
                Assert.AreEqual(100m,Convert.ToDecimal(SchemaSql.Scalar(c,null,"SELECT amount FROM sal.bank_file_line")!));
                Assert.AreEqual("Synthetic preserved",Store.Get(c,"upgrade-synth-1","test")!.S("name"));
                return true;
            });
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using(var q=admin.CreateCommand())
            {
                q.CommandText="ALTER DATABASE ["+db+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+db+"]";
                q.ExecuteNonQuery();
            }
            admin.Dispose();
            if(Directory.Exists(dir))Directory.Delete(dir,true);
        }
    }
}
