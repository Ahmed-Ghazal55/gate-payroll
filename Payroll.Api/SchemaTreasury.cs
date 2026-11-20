using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-16. Design §6 / §14 / §16; bank/reconciliation/count registers, no backfill or ERP writes.
/// Company cash boxes are distinct from M-0676's external vault (unit 4, blocked on owner mapping).
/// Status: workflows 1 draft / 2 submitted / 3 approved / 4 rejected / 5 cancelled.</summary>
public static class SchemaTreasury
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c),company=SchemaSql.Q(c,"hr","company"),account=SchemaSql.Q(c,"fin","account");
        foreach(string table in new[]{"bank_account","cash_box"})
        {
            SchemaSql.EnsureTable(c,tx,"treasury",table,$"""
                id {g} NOT NULL, company_id {g} NOT NULL, registration_id {g} NOT NULL,
                code {SchemaSql.Varchar(c,40)} NOT NULL, name_ar {SchemaSql.Text(c,200)} NOT NULL,
                account_id {g} NOT NULL, status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1 CHECK(status IN (1,2)),
                {SchemaSql.MoneyAuditCols(c)}, CONSTRAINT PK_treasury_{table} {SchemaSql.PrimaryKey(c)}(id),
                CONSTRAINT UQ_treasury_{table}_code UNIQUE(code),
                CONSTRAINT UQ_treasury_{table}_scope UNIQUE(id,company_id,registration_id),
                CONSTRAINT FK_treasury_{table}_company FOREIGN KEY(company_id) REFERENCES {company}(id) ON DELETE NO ACTION,
                CONSTRAINT FK_treasury_{table}_registration FOREIGN KEY(registration_id) REFERENCES {SchemaSql.Q(c,"hr","commercial_registration")}(id) ON DELETE NO ACTION,
                CONSTRAINT FK_treasury_{table}_account FOREIGN KEY(account_id) REFERENCES {account}(id) ON DELETE NO ACTION
                """);
            SchemaMoneyGuard.Rule(c,tx,"treasury",table,$"TR_treasury_{table}_scope",n=>$"NOT EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"hr","commercial_registration")} r WHERE r.id={n}.registration_id AND r.company_id={n}.company_id) OR NOT EXISTS(SELECT 1 FROM {account} a WHERE a.id={n}.account_id AND a.is_postable=1)",
                (o,n)=>$"{n}.id IS NULL OR ({SchemaMoneyGuard.Changed(c,n,o,"id","company_id","registration_id","account_id","code")})");
            SchemaMoneyGuard.Retain(c,tx,"treasury",table,"o.code=NEW.code");
            SchemaSql.EnsureRowVersion(c,tx,"treasury",table);
            foreach(string column in new[]{"company_id","registration_id","account_id"})SchemaSql.CreateIndexIfMissing(c,tx,"treasury",table,$"IX_{table}_{column}",column);
        }
        string bank=SchemaSql.Q(c,"treasury","bank_account"),statement=SchemaSql.Q(c,"treasury","bank_statement"),line=SchemaSql.Q(c,"treasury","bank_statement_line");
        SchemaSql.EnsureTable(c,tx,"treasury","bank_statement",$"""
            id {g} NOT NULL, company_id {g} NOT NULL, registration_id {g} NOT NULL, bank_account_id {g} NOT NULL,
            period_month {SchemaSql.Date(c)} NOT NULL, file_hash {SchemaSql.Binary(c,32)} NOT NULL,
            opening_balance {m} NOT NULL, closing_balance {m} NOT NULL,
            {SchemaMoneyRegister.WorkflowColumns(c)}, {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_treasury_statement {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_treasury_statement_scope UNIQUE(id,company_id,period_month),
            CONSTRAINT CK_treasury_statement_month CHECK({SchemaSql.MonthCheck(c)}),
            CONSTRAINT FK_treasury_statement_bank FOREIGN KEY(bank_account_id,company_id,registration_id) REFERENCES {bank}(id,company_id,registration_id) ON DELETE NO ACTION
            """);
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"treasury","bank_statement","UQ_treasury_statement_file","bank_account_id,file_hash","status<>5");
        SchemaMoneyRegister.Workflow(c,tx,"treasury","bank_statement","company_id","registration_id","bank_account_id","period_month","file_hash","opening_balance","closing_balance");
        SchemaMoneyRegister.Immutable(c,tx,"treasury","bank_statement","company_id","registration_id","bank_account_id","period_month");
        SchemaMoneyGuard.Retain(c,tx,"treasury","bank_statement","o.bank_account_id=NEW.bank_account_id AND o.file_hash=NEW.file_hash AND o.status<>5 AND NEW.status<>5");
        SchemaSql.EnsureTable(c,tx,"treasury","bank_statement_line",$"""
            id {g} NOT NULL, statement_id {g} NOT NULL, company_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            line_no {SchemaSql.Int(c)} NOT NULL CHECK(line_no>0), occurred_on {SchemaSql.Date(c)} NOT NULL,
            amount {m} NOT NULL CHECK(amount<>0), reference {SchemaSql.Varchar(c,100)} NULL,
            CONSTRAINT PK_treasury_statement_line {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_treasury_statement_line UNIQUE(statement_id,line_no),
            CONSTRAINT UQ_treasury_statement_line_scope UNIQUE(id,company_id,period_month),
            CONSTRAINT CK_treasury_statement_line_month CHECK({SchemaSql.MonthCheck(c)} AND {SchemaDebt.DateInMonth(c,"occurred_on")}),
            CONSTRAINT FK_treasury_statement_line FOREIGN KEY(statement_id,company_id,period_month) REFERENCES {statement}(id,company_id,period_month) ON DELETE NO ACTION
            """);
        // Uploaded bank facts are immutable immediately. Corrections use a new upload/cancel, never row edits.
        SchemaMoneyRegister.Immutable(c,tx,"treasury","bank_statement_line","statement_id","company_id","period_month","line_no","occurred_on","amount","reference");
        SchemaMoneyGuard.Retain(c,tx,"treasury","bank_statement_line","o.statement_id=NEW.statement_id AND o.line_no=NEW.line_no");
        SchemaMoneyRegister.BusinessKey(c,tx,"treasury","bank_statement_line",n=>SchemaMoneyRegister.Join(c,SchemaMoneyRegister.IdString(c,$"{n}.statement_id"),SchemaSql.IsSql(c)?$"CONVERT(varchar(12),{n}.line_no)":$"CAST({n}.line_no AS TEXT)"));
        SchemaMoneyGuard.Rule(c,tx,"treasury","bank_statement_line","TR_statement_line_open",n=>$"EXISTS(SELECT 1 FROM {statement} s WHERE s.id={n}.statement_id AND s.status>=3)");
        SchemaMoneyGuard.Rule(c,tx,"treasury","bank_statement","TR_statement_total",n=>$"{n}.status=3 AND round({n}.closing_balance-{n}.opening_balance,2)<>round(COALESCE((SELECT SUM(amount) FROM {line} l WHERE l.statement_id={n}.id),0),2)");
        EnsureMatches(c,tx);
        EnsureCounts(c,tx);
        EnsureJahez(c,tx);
        foreach(string table in new[]{"bank_statement","bank_statement_line","bank_match","vault_count","jahez_recon_run","jahez_recon_line"})SchemaSql.EnsureMonthIndex(c,tx,"treasury",table);
        foreach(var (table,columns) in new[]{("bank_statement","bank_account_id,company_id,registration_id"),("bank_statement_line","statement_id,company_id,period_month"),("bank_match","statement_line_id,company_id,period_month"),("bank_match","journal_line_id"),("vault_count","cash_box_id,company_id,registration_id"),("vault_count","journal_id"),("jahez_recon_run","company_id"),("jahez_recon_line","recon_run_id,company_id,period_month"),("jahez_recon_line","journal_id")})
            SchemaSql.CreateIndexIfMissing(c,tx,"treasury",table,$"IX_{table}_{columns.Split(',')[0]}",columns);
    }

    static void EnsureMatches(DbConnection c,DbTransaction? tx)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c),match=SchemaSql.Q(c,"treasury","bank_match"),line=SchemaSql.Q(c,"treasury","bank_statement_line"),statement=SchemaSql.Q(c,"treasury","bank_statement"),bank=SchemaSql.Q(c,"treasury","bank_account"),jl=SchemaSql.Q(c,"fin","journal_line"),j=SchemaSql.Q(c,"fin","journal");
        SchemaSql.EnsureTable(c,tx,"treasury","bank_match",$"""
            id {g} NOT NULL, statement_line_id {g} NOT NULL, journal_line_id {g} NOT NULL,
            company_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL, amount {m} NOT NULL CHECK(amount>0),
            status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1 CHECK(status IN (1,5)),
            {SchemaSql.MoneyAuditCols(c)}, CONSTRAINT PK_treasury_bank_match {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT FK_treasury_match_statement FOREIGN KEY(statement_line_id,company_id,period_month) REFERENCES {line}(id,company_id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_treasury_match_journal FOREIGN KEY(journal_line_id) REFERENCES {jl}(id) ON DELETE NO ACTION
            """);
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"treasury","bank_match","UQ_treasury_match_active","statement_line_id,journal_line_id","status=1");
        SchemaMoneyGuard.Retain(c,tx,"treasury","bank_match","o.statement_line_id=NEW.statement_line_id AND o.journal_line_id=NEW.journal_line_id AND o.status=1 AND NEW.status=1");
        SchemaMoneyRegister.Immutable(c,tx,"treasury","bank_match","statement_line_id","journal_line_id","company_id","period_month","amount");
        SchemaMoneyGuard.Rule(c,tx,"treasury","bank_match","TR_treasury_match_source",n=>$"{n}.status=1 AND NOT EXISTS(SELECT 1 FROM {line} l JOIN {statement} s ON s.id=l.statement_id JOIN {bank} b ON b.id=s.bank_account_id JOIN {jl} jl ON jl.id={n}.journal_line_id JOIN {j} j ON j.id=jl.journal_id WHERE l.id={n}.statement_line_id AND jl.company_id={n}.company_id AND jl.account_id=b.account_id AND j.status=2 AND j.run_mode=0 AND s.status<>5 AND ((l.amount>0 AND jl.debit>0) OR (l.amount<0 AND jl.credit>0)))",
            (o,n)=>$"{o}.status=5 AND {n}.status<>5");
        string invalid(string n)=>$"{n}.status=1 AND (EXISTS(SELECT 1 FROM {line} l WHERE l.id={n}.statement_line_id AND ABS(l.amount)<(SELECT COALESCE(SUM(amount),0) FROM {match} x WHERE x.statement_line_id=l.id AND x.status=1)) OR EXISTS(SELECT 1 FROM {jl} l WHERE l.id={n}.journal_line_id AND l.debit+l.credit<(SELECT COALESCE(SUM(amount),0) FROM {match} x WHERE x.journal_line_id=l.id AND x.status=1)))";
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"CREATE OR ALTER TRIGGER treasury.TR_bank_match_totals ON {match} AFTER INSERT,UPDATE AS BEGIN SET NOCOUNT ON; DECLARE @locked uniqueidentifier; SELECT @locked=id FROM {line} WITH (UPDLOCK,HOLDLOCK) WHERE id IN(SELECT statement_line_id FROM inserted); SELECT @locked=id FROM {jl} WITH (UPDLOCK,HOLDLOCK) WHERE id IN(SELECT journal_line_id FROM inserted); IF EXISTS(SELECT 1 FROM inserted n WHERE {invalid("n")}) THROW 51041,'Bank match exceeds source amount',1; END;");
        else foreach(string op in new[]{"INSERT","UPDATE"})
            SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_bank_match_totals_{op} AFTER {op} ON {match} BEGIN SELECT RAISE(ABORT,'Bank match exceeds source amount') WHERE {invalid("NEW")}; END;");
    }

    static void EnsureCounts(DbConnection c,DbTransaction? tx)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c);
        SchemaSql.EnsureTable(c,tx,"treasury","vault_count",$"""
            id {g} NOT NULL, cash_box_id {g} NOT NULL, company_id {g} NOT NULL, registration_id {g} NOT NULL,
            period_month {SchemaSql.Date(c)} NOT NULL, counted_on {SchemaSql.Date(c)} NOT NULL,
            book_balance {m} NOT NULL, counted_amount {m} NOT NULL CHECK(counted_amount>=0),
            journal_id {g} NULL, {SchemaMoneyRegister.WorkflowColumns(c)}, {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_treasury_vault_count {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT CK_treasury_count_month CHECK({SchemaSql.MonthCheck(c)} AND {SchemaDebt.DateInMonth(c,"counted_on")}),
            CONSTRAINT FK_treasury_count_box FOREIGN KEY(cash_box_id,company_id,registration_id) REFERENCES {SchemaSql.Q(c,"treasury","cash_box")}(id,company_id,registration_id) ON DELETE NO ACTION,
            CONSTRAINT FK_treasury_count_journal FOREIGN KEY(journal_id) REFERENCES {SchemaSql.Q(c,"fin","journal")}(id) ON DELETE NO ACTION
            """);
        SchemaMoneyRegister.Workflow(c,tx,"treasury","vault_count","cash_box_id","company_id","registration_id","period_month","counted_on","book_balance","counted_amount","journal_id");
        // No guessed over/short accounts: the design explicitly names 422202 (over) and 542102 (short).
        string journal=SchemaSql.Q(c,"fin","journal"),jl=SchemaSql.Q(c,"fin","journal_line"),account=SchemaSql.Q(c,"fin","account");
        SchemaMoneyGuard.Rule(c,tx,"treasury","vault_count","TR_vault_count_journal",n=>$"{n}.status=3 AND {n}.counted_amount<>{n}.book_balance AND NOT EXISTS(SELECT 1 FROM {journal} j JOIN {jl} l ON l.journal_id=j.id JOIN {account} a ON a.id=l.account_id WHERE j.id={n}.journal_id AND j.company_id={n}.company_id AND j.period_month={n}.period_month AND j.status=2 AND j.run_mode=0 AND j.purpose='vault_over_short' AND (({n}.counted_amount>{n}.book_balance AND a.code='422202' AND l.credit={n}.counted_amount-{n}.book_balance) OR ({n}.counted_amount<{n}.book_balance AND a.code='542102' AND l.debit={n}.book_balance-{n}.counted_amount)))");
    }

    static void EnsureJahez(DbConnection c,DbTransaction? tx)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c),run=SchemaSql.Q(c,"treasury","jahez_recon_run");
        SchemaSql.EnsureTable(c,tx,"treasury","jahez_recon_run",$"""
            id {g} NOT NULL, company_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            business_date {SchemaSql.Date(c)} NOT NULL, version_no {SchemaSql.Tiny(c)} NOT NULL DEFAULT 3 CHECK(version_no IN (2,3)),
            reconciliation_state {SchemaSql.Varchar(c,24)} NOT NULL DEFAULT 'open' CHECK(reconciliation_state IN ('matched','open','waiting_handover','variance_decided','day_approved')),
            {SchemaMoneyRegister.WorkflowColumns(c)}, {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_treasury_jahez_recon {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_treasury_jahez_recon_scope UNIQUE(id,company_id,period_month),
            CONSTRAINT UQ_treasury_jahez_recon_day UNIQUE(company_id,business_date,version_no),
            CONSTRAINT CK_treasury_jahez_recon_month CHECK({SchemaSql.MonthCheck(c)} AND {SchemaDebt.DateInMonth(c,"business_date")}),
            CONSTRAINT FK_treasury_jahez_recon_company FOREIGN KEY(company_id) REFERENCES {SchemaSql.Q(c,"hr","company")}(id) ON DELETE NO ACTION
            """);
        SchemaMoneyRegister.Workflow(c,tx,"treasury","jahez_recon_run","company_id","period_month","business_date","version_no","reconciliation_state");
        SchemaMoneyGuard.Retain(c,tx,"treasury","jahez_recon_run","o.company_id=NEW.company_id AND o.business_date=NEW.business_date AND o.version_no=NEW.version_no");
        SchemaSql.EnsureTable(c,tx,"treasury","jahez_recon_line",$"""
            id {g} NOT NULL, recon_run_id {g} NOT NULL, company_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            erp_vault_id {g} NOT NULL, erp_handover_id {g} NULL, expected_amount {m} NOT NULL, handed_amount {m} NOT NULL,
            journal_id {g} NULL,
            CONSTRAINT PK_treasury_jahez_line {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_treasury_jahez_line UNIQUE(recon_run_id,erp_vault_id),
            CONSTRAINT FK_treasury_jahez_line_run FOREIGN KEY(recon_run_id,company_id,period_month) REFERENCES {run}(id,company_id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_treasury_jahez_line_journal FOREIGN KEY(journal_id) REFERENCES {SchemaSql.Q(c,"fin","journal")}(id) ON DELETE NO ACTION
            """);
        SchemaMoneyGuard.Retain(c,tx,"treasury","jahez_recon_line","o.recon_run_id=NEW.recon_run_id AND o.erp_vault_id=NEW.erp_vault_id");
        SchemaMoneyGuard.Rule(c,tx,"treasury","jahez_recon_line","TR_jahez_line_frozen",n=>$"EXISTS(SELECT 1 FROM {run} r WHERE r.id={n}.recon_run_id AND r.status>=3)",
            (o,n)=>$"{n}.id IS NULL OR ({SchemaMoneyGuard.Changed(c,n,o,"id","recon_run_id","company_id","period_month","erp_vault_id")}) OR EXISTS(SELECT 1 FROM {run} r WHERE r.id={o}.recon_run_id AND r.status>=3)");
        SchemaMoneyGuard.Rule(c,tx,"treasury","jahez_recon_run","TR_jahez_day_state",n=>$"({n}.status=3 AND {n}.reconciliation_state<>'day_approved') OR ({n}.status<>3 AND {n}.reconciliation_state='day_approved')");
    }

    /// <summary>Match writers lock both sources BEFORE insert, in stable id order; a trigger error aborts the whole batch.</summary>
    public static void LockMatchSources(DbConnection c,DbTransaction tx,string statementLine,string journalLine)
    {
        string hint=SchemaSql.IsSql(c)?" WITH (UPDLOCK,HOLDLOCK)":"";
        foreach(var (table,id) in new[]{(SchemaSql.Q(c,"treasury","bank_statement_line"),statementLine),(SchemaSql.Q(c,"fin","journal_line"),journalLine)})
            if(SchemaSql.Scalar(c,tx,$"SELECT id FROM {table}{hint} WHERE id=@id",("@id",id)) is null)throw new InvalidOperationException("Bank matching source not found.");
    }
}
