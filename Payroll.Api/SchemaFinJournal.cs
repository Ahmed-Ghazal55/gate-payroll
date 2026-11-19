using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-09 (journal half): fin.journal + fin.journal_line. Design §7 / §7.1 / §14 / §16 / BR §24.8 / §28. Posted-row immutability enforced by posting service later; columns and CHECKs land now.
/// Journal period_month is FK-bound to the period row; CK_fin_journal_date_in_period keeps journal_date inside that month.</summary>
public static class SchemaFinJournal
{
    // journal.status: 1 draft, 2 posted, 3 reversed, 4 cancelled
    // run_mode: 0 live, 1 simulation
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchema(c,tx,"fin");
        EnsureJournal(c,tx);
        EnsureJournalLine(c,tx);
    }

    static void EnsureJournal(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"fin","journal");string g=SchemaSql.GuidType(c);
        string period=SchemaSql.Q(c,"fin","accounting_period");
        string company=SchemaSql.Q(c,"hr","company");
        // Per-provider: journal_date must fall in [period_month, last day of that month].
        string dateInPeriod=SchemaSql.IsSql(c)
            ?"journal_date >= period_month AND journal_date < DATEADD(month, 1, period_month)"
            :"journal_date >= period_month AND journal_date < date(period_month, '+1 month')";
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'fin.journal',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  voucher_no varchar(40) NOT NULL,
                  period_id {g} NOT NULL,
                  period_month {SchemaSql.Date(c)} NOT NULL,
                  journal_date {SchemaSql.Date(c)} NOT NULL,
                  source_type varchar(40) NOT NULL,
                  source_id {g} NOT NULL,
                  company_id {g} NOT NULL,
                  purpose varchar(40) NOT NULL,
                  status {SchemaSql.Tiny(c)} NOT NULL,
                  run_mode {SchemaSql.Tiny(c)} NOT NULL CONSTRAINT DF_fin_journal_run_mode DEFAULT(0),
                  payout_blocked {SchemaSql.Bit(c)} NOT NULL CONSTRAINT DF_fin_journal_payout_blocked DEFAULT(0),
                  currency_code char(3) NOT NULL CONSTRAINT DF_fin_journal_currency DEFAULT('SAR'),
                  reverses_journal_id {g} NULL,
                  posted_at {SchemaSql.DateTime(c)} NULL,
                  posted_by {SchemaSql.Text(c,128)} NULL,
                  narration_ar nvarchar(400) NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_fin_journal PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_fin_journal_voucher UNIQUE (voucher_no),
                  CONSTRAINT UQ_fin_journal_source UNIQUE (source_type, source_id, company_id, purpose),
                  CONSTRAINT UQ_fin_journal_id_company_period UNIQUE (id, company_id, period_id, period_month),
                  CONSTRAINT CK_fin_journal_status CHECK (status IN (1,2,3,4)),
                  CONSTRAINT CK_fin_journal_run_mode CHECK (run_mode IN (0,1)),
                  CONSTRAINT CK_fin_journal_currency CHECK (currency_code = 'SAR'),
                  CONSTRAINT CK_fin_journal_date_in_period CHECK ({dateInPeriod}),
                  CONSTRAINT FK_fin_journal_period_company FOREIGN KEY (company_id, period_id, period_month) REFERENCES {period}(company_id, id, period_month),
                  CONSTRAINT FK_fin_journal_company FOREIGN KEY (company_id) REFERENCES {company}(id),
                  CONSTRAINT FK_fin_journal_reverses FOREIGN KEY (reverses_journal_id) REFERENCES {t}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'fin.journal') AND name=N'CX_fin_journal')
                CREATE CLUSTERED INDEX [CX_fin_journal] ON {t}(period_month, id);
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal","IX_fin_journal_period_id","period_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal","IX_fin_journal_company_id","company_id");
            SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"fin","journal","UQ_fin_journal_reverses","reverses_journal_id","reverses_journal_id IS NOT NULL");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  voucher_no TEXT NOT NULL,
                  period_id {g} NOT NULL,
                  period_month TEXT NOT NULL,
                  journal_date TEXT NOT NULL,
                  source_type TEXT NOT NULL,
                  source_id {g} NOT NULL,
                  company_id {g} NOT NULL,
                  purpose TEXT NOT NULL,
                  status INTEGER NOT NULL,
                  run_mode INTEGER NOT NULL DEFAULT 0,
                  payout_blocked INTEGER NOT NULL DEFAULT 0,
                  currency_code TEXT NOT NULL DEFAULT 'SAR',
                  reverses_journal_id {g} NULL,
                  posted_at TEXT NULL,
                  posted_by TEXT NULL,
                  narration_ar TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_fin_journal_voucher UNIQUE (voucher_no),
                  CONSTRAINT UQ_fin_journal_source UNIQUE (source_type, source_id, company_id, purpose),
                  CONSTRAINT UQ_fin_journal_id_company_period UNIQUE (id, company_id, period_id, period_month),
                  CONSTRAINT CK_fin_journal_status CHECK (status IN (1,2,3,4)),
                  CONSTRAINT CK_fin_journal_run_mode CHECK (run_mode IN (0,1)),
                  CONSTRAINT CK_fin_journal_currency CHECK (currency_code = 'SAR'),
                  CONSTRAINT CK_fin_journal_date_in_period CHECK ({dateInPeriod}),
                  CONSTRAINT FK_fin_journal_period_company FOREIGN KEY (company_id, period_id, period_month) REFERENCES {period}(company_id, id, period_month),
                  CONSTRAINT FK_fin_journal_company FOREIGN KEY (company_id) REFERENCES {company}(id),
                  CONSTRAINT FK_fin_journal_reverses FOREIGN KEY (reverses_journal_id) REFERENCES {t}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal","IX_fin_journal_period_month_id","period_month, id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal","IX_fin_journal_period_id","period_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal","IX_fin_journal_company_id","company_id");
            SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"fin","journal","UQ_fin_journal_reverses","reverses_journal_id","reverses_journal_id IS NOT NULL");
        }
    }

    static void EnsureJournalLine(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"fin","journal_line");string g=SchemaSql.GuidType(c);
        string journal=SchemaSql.Q(c,"fin","journal");
        string account=SchemaSql.Q(c,"fin","account");
        string period=SchemaSql.Q(c,"fin","accounting_period");
        string company=SchemaSql.Q(c,"hr","company");
        string cc=SchemaSql.Q(c,"org","cost_centre");
        string money=SchemaSql.Decimal(c);
        if(SchemaSql.IsSql(c))
        {
            // Leaf-only posting: composite FK (account_id, is_postable) → account(id, is_postable) with line CHECK is_postable=1.
            // Line company/period/period_month must match the journal header and the period row.
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'fin.journal_line',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  period_id {g} NOT NULL,
                  period_month {SchemaSql.Date(c)} NOT NULL,
                  journal_id {g} NOT NULL,
                  line_no {SchemaSql.Int(c)} NOT NULL,
                  account_id {g} NOT NULL,
                  is_postable {SchemaSql.Bit(c)} NOT NULL CONSTRAINT DF_fin_jl_postable DEFAULT(1),
                  company_id {g} NOT NULL,
                  cost_centre_id {g} NOT NULL,
                  app_code varchar(20) NULL,
                  erp_platform_account_id {g} NULL,
                  erp_rider_id {g} NULL,
                  supplier_id {g} NULL,
                  debit {money} NOT NULL,
                  credit {money} NOT NULL,
                  narration_ar nvarchar(400) NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_fin_journal_line PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_fin_journal_line_no UNIQUE (journal_id, line_no),
                  CONSTRAINT CK_fin_journal_line_debit CHECK (debit >= 0),
                  CONSTRAINT CK_fin_journal_line_credit CHECK (credit >= 0),
                  CONSTRAINT CK_fin_journal_line_one_side CHECK (debit = 0 OR credit = 0),
                  CONSTRAINT CK_fin_journal_line_nonzero CHECK (debit + credit > 0),
                  CONSTRAINT CK_fin_journal_line_postable CHECK (is_postable = 1),
                  CONSTRAINT FK_fin_journal_line_journal_scope FOREIGN KEY (journal_id, company_id, period_id, period_month) REFERENCES {journal}(id, company_id, period_id, period_month),
                  CONSTRAINT FK_fin_journal_line_period_company FOREIGN KEY (company_id, period_id, period_month) REFERENCES {period}(company_id, id, period_month),
                  CONSTRAINT FK_fin_journal_line_account_postable FOREIGN KEY (account_id, is_postable) REFERENCES {account}(id, is_postable),
                  CONSTRAINT FK_fin_journal_line_company FOREIGN KEY (company_id) REFERENCES {company}(id),
                  CONSTRAINT FK_fin_journal_line_cc FOREIGN KEY (cost_centre_id) REFERENCES {cc}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'fin.journal_line') AND name=N'CX_fin_journal_line')
                CREATE CLUSTERED INDEX [CX_fin_journal_line] ON {t}(period_month, id);
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal_line","IX_fin_journal_line_journal_id","journal_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal_line","IX_fin_journal_line_account_id","account_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal_line","IX_fin_journal_line_company_id","company_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal_line","IX_fin_journal_line_cc_id","cost_centre_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal_line","IX_fin_journal_line_platform","erp_platform_account_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  period_id {g} NOT NULL,
                  period_month TEXT NOT NULL,
                  journal_id {g} NOT NULL,
                  line_no INTEGER NOT NULL,
                  account_id {g} NOT NULL,
                  is_postable INTEGER NOT NULL DEFAULT 1,
                  company_id {g} NOT NULL,
                  cost_centre_id {g} NOT NULL,
                  app_code TEXT NULL,
                  erp_platform_account_id {g} NULL,
                  erp_rider_id {g} NULL,
                  supplier_id {g} NULL,
                  debit REAL NOT NULL,
                  credit REAL NOT NULL,
                  narration_ar TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_fin_journal_line_no UNIQUE (journal_id, line_no),
                  CONSTRAINT CK_fin_journal_line_debit CHECK (debit >= 0),
                  CONSTRAINT CK_fin_journal_line_credit CHECK (credit >= 0),
                  CONSTRAINT CK_fin_journal_line_one_side CHECK (debit = 0 OR credit = 0),
                  CONSTRAINT CK_fin_journal_line_nonzero CHECK (debit + credit > 0),
                  CONSTRAINT CK_fin_journal_line_postable CHECK (is_postable = 1),
                  CONSTRAINT FK_fin_journal_line_journal_scope FOREIGN KEY (journal_id, company_id, period_id, period_month) REFERENCES {journal}(id, company_id, period_id, period_month),
                  CONSTRAINT FK_fin_journal_line_period_company FOREIGN KEY (company_id, period_id, period_month) REFERENCES {period}(company_id, id, period_month),
                  CONSTRAINT FK_fin_journal_line_account_postable FOREIGN KEY (account_id, is_postable) REFERENCES {account}(id, is_postable),
                  CONSTRAINT FK_fin_journal_line_company FOREIGN KEY (company_id) REFERENCES {company}(id),
                  CONSTRAINT FK_fin_journal_line_cc FOREIGN KEY (cost_centre_id) REFERENCES {cc}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal_line","IX_fin_journal_line_period_month_id","period_month, id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal_line","IX_fin_journal_line_journal_id","journal_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal_line","IX_fin_journal_line_account_id","account_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal_line","IX_fin_journal_line_company_id","company_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal_line","IX_fin_journal_line_cc_id","cost_centre_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","journal_line","IX_fin_journal_line_platform","erp_platform_account_id");
        }
    }
}
