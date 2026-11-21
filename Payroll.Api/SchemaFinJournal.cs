using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-09 (journal half): fin.journal + fin.journal_line. Design §7 / §7.1 / §14 / §16 / BR §24.8 / §28.
/// Posted/reversed/cancelled journals and their lines are frozen and never deleted (design §7.1 step 8).
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
        EnsurePostedGuards(c,tx);
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

    /// <summary>Design §7.1 step 8: reject later UPDATE of posted journal/lines; never delete money evidence.</summary>
    static void EnsurePostedGuards(DbConnection c,DbTransaction? tx)
    {
        string journal=SchemaSql.Q(c,"fin","journal");
        string[] frozen=["voucher_no","period_id","period_month","journal_date","source_type","source_id","company_id","purpose","run_mode","payout_blocked","currency_code","reverses_journal_id","posted_at","posted_by","narration_ar"];
        // SQL Server: UQ_fin_journal_source must surface; SQLite no_replace covers the same key.
        bool sqlReplace=!SchemaSql.IsSql(c);
        SchemaMoneyGuard.Retain(c,tx,"fin","journal","o.voucher_no=NEW.voucher_no OR (o.source_type=NEW.source_type AND o.source_id=NEW.source_id AND o.company_id=NEW.company_id AND o.purpose=NEW.purpose)",replace:sqlReplace);
        SchemaMoneyGuard.Retain(c,tx,"fin","journal_line","o.journal_id=NEW.journal_id AND o.line_no=NEW.line_no",replace:sqlReplace);
        // SQL Server AFTER triggers fire in create order; SQLite BEFORE fires last-created first — opposite.
        // Create state before money_flags on SQL Server (and the reverse on SQLite) so TR_fin_journal_state wins.
        void State()=>SchemaMoneyGuard.Rule(c,tx,"fin","journal","TR_fin_journal_state",
            n=>$"{n}.status IN (2,3) AND {n}.run_mode=1",
            (o,n)=>$"{n}.id IS NULL OR ({o}.status<>{n}.status AND NOT (({o}.status=1 AND {n}.status IN (2,4)) OR ({o}.status=2 AND {n}.status=3))) OR ({o}.status IN (2,3,4) AND ({SchemaMoneyGuard.Changed(c,n,o,frozen)}))");
        void Flags()=>SchemaMoneyFlags.Ensure(c,tx,"fin","journal",o=>$"{o}.status IN (2,3,4)");
        if(SchemaSql.IsSql(c)){State();Flags();}else{Flags();State();}
        SchemaMoneyGuard.Rule(c,tx,"fin","journal_line","TR_fin_journal_line_frozen",
            n=>$"EXISTS(SELECT 1 FROM {journal} j WHERE j.id={n}.journal_id AND j.status IN (2,3,4))",
            (o,n)=>$"{n}.id IS NULL OR EXISTS(SELECT 1 FROM {journal} j WHERE j.id={o}.journal_id AND j.status IN (2,3,4)) OR EXISTS(SELECT 1 FROM {journal} j WHERE j.id={n}.journal_id AND j.status IN (2,3,4))");
    }
}
