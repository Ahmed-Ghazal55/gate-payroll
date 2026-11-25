using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.RegularExpressions;

/// <summary>Live upgrade: build the 0.33.8 shape of sal.bank_file* (origin/main), insert rows, start 0.34 twice, rows unchanged.</summary>
[TestClass]
public sealed class SchemaUpgradeFrom0338Tests
{
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

    static string RepoRoot()
    {
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir!=null;dir=dir.Parent)
        {
            string git=Path.Combine(dir.FullName,".git");
            // Worktrees use a .git file; normal clones use a .git directory.
            if((Directory.Exists(git)||File.Exists(git))&&File.Exists(Path.Combine(dir.FullName,"Payroll.Api","SchemaBankFile.cs")))
                return dir.FullName;
        }
        throw new InvalidOperationException("gate-payroll repo root not found from test base directory.");
    }

    static string GitShow(string spec)
    {
        var start=new ProcessStartInfo
        {
            FileName="git",
            Arguments="show "+spec,
            WorkingDirectory=RepoRoot(),
            RedirectStandardOutput=true,
            RedirectStandardError=true,
            UseShellExecute=false
        };
        using var proc=Process.Start(start)??throw new InvalidOperationException("git show failed to start");
        string stdout=proc.StandardOutput.ReadToEnd();
        string stderr=proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.AreEqual(0,proc.ExitCode,"git show "+spec+" failed: "+stderr);
        return stdout;
    }

    /// <summary>origin/main SchemaBankFile EnsureTable bodies — tables exist without frozen rider meta.</summary>
    static void EnsureMain0338BankFileTables(SqlConnection c)
    {
        string g="uniqueidentifier",m="decimal(18,2)",audit="created_by nvarchar(128) NULL, created_at datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()), updated_by nvarchar(128) NULL, updated_at datetime2 NULL, row_version rowversion NOT NULL, generation varchar(10) NOT NULL DEFAULT 'new' CHECK (generation IN ('new','legacy'))";
        SchemaSql.EnsureSchema(c,null,"sal");
        SchemaSql.EnsureTable(c,null,"sal","bank_file",$"""
            id {g} NOT NULL, period_month date NOT NULL,
            issuance_choice varchar(16) NOT NULL CHECK(issuance_choice IN ('per_company','combined')),
            scope_company_id {g} NULL, scope_registration_id {g} NULL,
            paying_company_id {g} NOT NULL, paying_registration_id {g} NOT NULL, paying_bank_id {g} NOT NULL,
            status tinyint NOT NULL DEFAULT 1 CHECK(status IN (1,2,3,4)),
            file_hash varbinary(32) NULL,
            run_mode tinyint NOT NULL DEFAULT 0 CHECK(run_mode=0),
            payout_blocked bit NOT NULL DEFAULT 0 CHECK(payout_blocked IN (0,1)),
            currency_code varchar(3) NOT NULL DEFAULT 'SAR' CHECK(currency_code='SAR'),
            approved_by {g} NULL, approved_at datetime2 NULL,
            sent_by {g} NULL, sent_at datetime2 NULL,
            {audit}, CONSTRAINT PK_sal_bank_file PRIMARY KEY NONCLUSTERED(id),
            CONSTRAINT UQ_sal_bank_file_month UNIQUE(id,period_month),
            CONSTRAINT CK_bank_file_month CHECK(period_month = DATEFROMPARTS(YEAR(period_month), MONTH(period_month), 1)),
            CONSTRAINT CK_bank_file_scope CHECK((issuance_choice='per_company' AND scope_company_id IS NOT NULL) OR (issuance_choice='combined' AND scope_company_id IS NULL AND scope_registration_id IS NULL)),
            CONSTRAINT CK_bank_file_sent CHECK(status<>3 OR (sent_by IS NOT NULL AND sent_at IS NOT NULL)),
            CONSTRAINT CK_bank_file_approved CHECK(status NOT IN (2,3) OR (approved_by IS NOT NULL AND approved_at IS NOT NULL AND created_by IS NOT NULL)),
            CONSTRAINT FK_bank_file_bank FOREIGN KEY(paying_bank_id,paying_company_id,paying_registration_id) REFERENCES [treasury].[bank_account](id,company_id,registration_id) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_file_scope FOREIGN KEY(scope_company_id) REFERENCES [hr].[company](id) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_file_registration FOREIGN KEY(scope_registration_id) REFERENCES [hr].[commercial_registration](id) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureTable(c,null,"sal","bank_file_line",$"""
            id {g} NOT NULL, bank_file_id {g} NOT NULL, period_month date NOT NULL,
            pay_run_rider_id {g} NOT NULL, payable_id {g} NOT NULL, company_id {g} NOT NULL,
            registration_id {g} NOT NULL, payable_period_month date NOT NULL,
            payment_id {g} NOT NULL, allocation_id {g} NOT NULL,
            amount {m} NOT NULL CHECK(amount>0), bank_short_code varchar(8) NOT NULL,
            status tinyint NOT NULL DEFAULT 1 CHECK(status IN (1,4)),
            {audit}, CONSTRAINT PK_sal_bank_file_line PRIMARY KEY NONCLUSTERED(id),
            CONSTRAINT UQ_bank_file_line_month UNIQUE(id,period_month),
            CONSTRAINT UQ_bank_file_allocation UNIQUE(allocation_id),
            CONSTRAINT FK_bank_line_file FOREIGN KEY(bank_file_id,period_month) REFERENCES [sal].[bank_file](id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_line_rider FOREIGN KEY(pay_run_rider_id,payable_period_month) REFERENCES [sal].[pay_run_rider](id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_line_payable FOREIGN KEY(payable_id,company_id,payable_period_month) REFERENCES [pay].[payable](id,company_id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_line_payment FOREIGN KEY(payment_id,period_month) REFERENCES [pay].[payment](id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_line_allocation FOREIGN KEY(allocation_id) REFERENCES [pay].[payment_allocation](id) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_line_registration FOREIGN KEY(registration_id) REFERENCES [hr].[commercial_registration](id) ON DELETE NO ACTION
            """);
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
        string mainBank=GitShow("origin/main:Payroll.Api/SchemaBankFile.cs");
        Assert.IsFalse(mainBank.Contains("EnsureFrozenMeta",StringComparison.Ordinal),"origin/main must be the pre-frozen-meta bank file schema");
        Assert.IsFalse(mainBank.Contains("rider_iqama",StringComparison.Ordinal),"origin/main bank_file_line has no rider meta columns");
        Assert.IsTrue(mainBank.Contains("ImmutableInsteadOfUpdate(c,tx,\"sal\",\"bank_file_line\",\"bank_file_id\",\"period_month\",\"pay_run_rider_id\",\"payable_id\",\"company_id\",\"registration_id\",\"payable_period_month\",\"payment_id\",\"allocation_id\",\"amount\",\"bank_short_code\",\"generation\")",StringComparison.Ordinal));

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
                EnsureMain0338BankFileTables(bootstrap);

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
