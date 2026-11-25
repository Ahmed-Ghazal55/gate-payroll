using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-14d + M-0678 issuance storage; design §4.2, BR §32 / §35 / §38.5.
/// Empty schema, no export endpoint/reader switch. W2 issues under one TX, locks payables BEFORE claim INSERT,
/// then inserts each line referencing its reserved allocation. Inter-company rows store evidence, not guessed account mappings.
/// File: 1 draft, 2 approved, 3 sent, 4 cancelled. Line: 1 active, 4 cancelled.</summary>
public static class SchemaBankFile
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c),file=SchemaSql.Q(c,"sal","bank_file"),line=SchemaSql.Q(c,"sal","bank_file_line"),company=SchemaSql.Q(c,"hr","company"),allocation=SchemaSql.Q(c,"pay","payment_allocation");
        SchemaSql.EnsureTable(c,tx,"sal","bank_file",$"""
            id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            issuance_choice {SchemaSql.Varchar(c,16)} NOT NULL CHECK(issuance_choice IN ('per_company','combined')),
            scope_company_id {g} NULL, scope_registration_id {g} NULL,
            paying_company_id {g} NOT NULL, paying_registration_id {g} NOT NULL, paying_bank_id {g} NOT NULL,
            status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1 CHECK(status IN (1,2,3,4)),
            file_hash {SchemaSql.Binary(c,32)} NULL,
            run_mode {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0 CHECK(run_mode=0),
            payout_blocked {SchemaSql.Bit(c)} NOT NULL DEFAULT 0 CHECK(payout_blocked IN (0,1)),
            currency_code {SchemaSql.Varchar(c,3)} NOT NULL DEFAULT 'SAR' CHECK(currency_code='SAR'),
            confirm_unverified_ibans {SchemaSql.Bit(c)} NOT NULL DEFAULT 0 CHECK(confirm_unverified_ibans IN (0,1)),
            excluded_json {SchemaSql.Json(c)} NULL,
            excluded_count {SchemaSql.Int(c)} NOT NULL DEFAULT 0,
            excluded_total {m} NOT NULL DEFAULT 0,
            approved_by {g} NULL, approved_at {SchemaSql.DateTime(c)} NULL,
            sent_by {g} NULL, sent_at {SchemaSql.DateTime(c)} NULL,
            {SchemaSql.MoneyAuditCols(c)}, CONSTRAINT PK_sal_bank_file {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_sal_bank_file_month UNIQUE(id,period_month),
            CONSTRAINT CK_bank_file_month CHECK({SchemaSql.MonthCheck(c)}),
            CONSTRAINT CK_bank_file_scope CHECK((issuance_choice='per_company' AND scope_company_id IS NOT NULL) OR (issuance_choice='combined' AND scope_company_id IS NULL AND scope_registration_id IS NULL)),
            CONSTRAINT CK_bank_file_sent CHECK(status<>3 OR (sent_by IS NOT NULL AND sent_at IS NOT NULL)),
            CONSTRAINT CK_bank_file_approved CHECK(status NOT IN (2,3) OR (approved_by IS NOT NULL AND approved_at IS NOT NULL AND created_by IS NOT NULL)),
            CONSTRAINT FK_bank_file_bank FOREIGN KEY(paying_bank_id,paying_company_id,paying_registration_id) REFERENCES {SchemaSql.Q(c,"treasury","bank_account")}(id,company_id,registration_id) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_file_scope FOREIGN KEY(scope_company_id) REFERENCES {company}(id) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_file_registration FOREIGN KEY(scope_registration_id) REFERENCES {SchemaSql.Q(c,"hr","commercial_registration")}(id) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureTable(c,tx,"sal","bank_file_line",$"""
            id {g} NOT NULL, bank_file_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            pay_run_rider_id {g} NOT NULL, payable_id {g} NOT NULL, company_id {g} NOT NULL,
            registration_id {g} NOT NULL, payable_period_month {SchemaSql.Date(c)} NOT NULL,
            payment_id {g} NOT NULL, allocation_id {g} NOT NULL,
            amount {m} NOT NULL CHECK(amount>0), bank_short_code {SchemaSql.Varchar(c,8)} NOT NULL,
            rider_iqama {SchemaSql.Varchar(c,20)} NULL, rider_name {(SchemaSql.IsSql(c)?"nvarchar(200)":"TEXT")} NULL,
            rider_iban {SchemaSql.Varchar(c,34)} NULL, rider_address {(SchemaSql.IsSql(c)?"nvarchar(500)":"TEXT")} NULL,
            iban_unverified {SchemaSql.Bit(c)} NOT NULL DEFAULT 0 CHECK(iban_unverified IN (0,1)),
            status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1 CHECK(status IN (1,4)),
            {SchemaSql.MoneyAuditCols(c)}, CONSTRAINT PK_sal_bank_file_line {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_bank_file_line_month UNIQUE(id,period_month),
            CONSTRAINT UQ_bank_file_allocation UNIQUE(allocation_id),
            CONSTRAINT FK_bank_line_file FOREIGN KEY(bank_file_id,period_month) REFERENCES {file}(id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_line_rider FOREIGN KEY(pay_run_rider_id,payable_period_month) REFERENCES {SchemaSql.Q(c,"sal","pay_run_rider")}(id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_line_payable FOREIGN KEY(payable_id,company_id,payable_period_month) REFERENCES {SchemaSql.Q(c,"pay","payable")}(id,company_id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_line_payment FOREIGN KEY(payment_id,period_month) REFERENCES {SchemaSql.Q(c,"pay","payment")}(id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_line_allocation FOREIGN KEY(allocation_id) REFERENCES {allocation}(id) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_line_registration FOREIGN KEY(registration_id) REFERENCES {SchemaSql.Q(c,"hr","commercial_registration")}(id) ON DELETE NO ACTION
            """);
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"sal","bank_file_line","UQ_bank_line_rider_active","pay_run_rider_id","status=1");
        foreach(string table in new[]{"bank_file","bank_file_line"}){SchemaSql.EnsureMonthIndex(c,tx,"sal",table);SchemaSql.EnsureRowVersion(c,tx,"sal",table);}
        SchemaMoneyGuard.Retain(c,tx,"sal","bank_file");
        SchemaMoneyGuard.Retain(c,tx,"sal","bank_file_line","o.allocation_id=NEW.allocation_id OR (o.pay_run_rider_id=NEW.pay_run_rider_id AND o.status=1 AND NEW.status=1)");
        SchemaMoneyRegister.ImmutableInsteadOfUpdate(c,tx,"sal","bank_file_line","bank_file_id","period_month","pay_run_rider_id","payable_id","company_id","registration_id","payable_period_month","payment_id","allocation_id","amount","bank_short_code","rider_iqama","rider_name","rider_iban","rider_address","iban_unverified","generation");
        EnsureFrozenMeta(c,tx);
        EnsureLines(c,tx);
        EnsureSettlement(c,tx);
        // SQL Server AFTER: create TR_bank_file_state before money_flags; SQLite BEFORE: flags first so state wins last.
        if(!SchemaSql.IsSql(c))SchemaMoneyFlags.Ensure(c,tx,"sal","bank_file",o=>$"{o}.status>=2 OR {o}.payout_blocked=1");
        EnsureHeader(c,tx);
        if(SchemaSql.IsSql(c))SchemaMoneyFlags.Ensure(c,tx,"sal","bank_file",o=>$"{o}.status>=2 OR {o}.payout_blocked=1");
        foreach(var (table,columns) in new[]{("bank_file","paying_bank_id,paying_company_id,paying_registration_id"),("bank_file","scope_company_id"),("bank_file","scope_registration_id"),("bank_file_line","bank_file_id,period_month"),("bank_file_line","pay_run_rider_id,payable_period_month"),("bank_file_line","payable_id,company_id,payable_period_month"),("bank_file_line","payment_id,period_month"),("bank_file_line","registration_id")})
            SchemaSql.CreateIndexIfMissing(c,tx,"sal",table,$"IX_{table}_{columns.Split(',')[0]}",columns);
        SchemaMoneyRegister.BusinessKey(c,tx,"sal","bank_file_line",n=>SchemaMoneyRegister.IdString(c,$"{n}.allocation_id"));
        // The comparison label is data returned by the export projection; no external vault balances enter it.
        string view=SchemaSql.Q(c,"sal","bank_file_export");
        string select=$"SELECT f.*, CASE WHEN payout_blocked=1 THEN N'لا يُرسل' ELSE N'' END AS payout_label FROM {file} f";
        if(SchemaSql.IsSql(c))SchemaSql.Exec(c,tx,$"CREATE OR ALTER VIEW {view} AS {select};");
        else{SchemaSql.Exec(c,tx,$"DROP VIEW IF EXISTS {view}");SchemaSql.Exec(c,tx,$"CREATE VIEW {view} AS {select.Replace("N'","'")};");}
    }

    /// <summary>TRIAL8: additive frozen rider meta on lines + exclusion snapshot on the file header (existing DBs).</summary>
    static void EnsureFrozenMeta(DbConnection c,DbTransaction? tx)
    {
        string file=SchemaSql.Q(c,"sal","bank_file"),line=SchemaSql.Q(c,"sal","bank_file_line");
        string m=SchemaSql.Decimal(c);
        if(SchemaSql.IsSql(c))
        {
            foreach(var (col,type) in new[]
            {
                ("confirm_unverified_ibans",$"{SchemaSql.Bit(c)} NOT NULL CONSTRAINT DF_bank_file_confirm_uv DEFAULT(0)"),
                ("excluded_json",SchemaSql.Json(c)+" NULL"),
                ("excluded_count",$"{SchemaSql.Int(c)} NOT NULL CONSTRAINT DF_bank_file_excl_count DEFAULT(0)"),
                ("excluded_total",$"{m} NOT NULL CONSTRAINT DF_bank_file_excl_total DEFAULT(0)")
            })
                SchemaSql.Exec(c,tx,$"IF COL_LENGTH(N'sal.bank_file',N'{col}') IS NULL ALTER TABLE {file} ADD {col} {type};");
            foreach(var (col,type) in new[]
            {
                ("rider_iqama",$"{SchemaSql.Varchar(c,20)} NULL"),
                ("rider_name","nvarchar(200) NULL"),
                ("rider_iban",$"{SchemaSql.Varchar(c,34)} NULL"),
                ("rider_address","nvarchar(500) NULL"),
                ("iban_unverified",$"{SchemaSql.Bit(c)} NOT NULL CONSTRAINT DF_bank_line_iban_uv DEFAULT(0)")
            })
                SchemaSql.Exec(c,tx,$"IF COL_LENGTH(N'sal.bank_file_line',N'{col}') IS NULL ALTER TABLE {line} ADD {col} {type};");
        }
        else
        {
            foreach(var (col,type) in new[]{("confirm_unverified_ibans","INTEGER NOT NULL DEFAULT 0"),("excluded_json","TEXT NULL"),("excluded_count","INTEGER NOT NULL DEFAULT 0"),("excluded_total","TEXT NOT NULL DEFAULT '0'")})
            {
                object? has=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM pragma_table_info('sal_bank_file') WHERE name='{col}'");
                if(has is null or DBNull)SchemaSql.Exec(c,tx,$"ALTER TABLE {file} ADD COLUMN {col} {type};");
            }
            foreach(var (col,type) in new[]{("rider_iqama","TEXT NULL"),("rider_name","TEXT NULL"),("rider_iban","TEXT NULL"),("rider_address","TEXT NULL"),("iban_unverified","INTEGER NOT NULL DEFAULT 0")})
            {
                object? has=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM pragma_table_info('sal_bank_file_line') WHERE name='{col}'");
                if(has is null or DBNull)SchemaSql.Exec(c,tx,$"ALTER TABLE {line} ADD COLUMN {col} {type};");
            }
        }
    }

    static void EnsureLines(DbConnection c,DbTransaction? tx)
    {
        string f=SchemaSql.Q(c,"sal","bank_file"),a=SchemaSql.Q(c,"pay","payment_allocation"),p=SchemaSql.Q(c,"pay","payment"),r=SchemaSql.Q(c,"sal","pay_run_rider"),run=SchemaSql.Q(c,"sal","pay_run"),hold=SchemaSql.Q(c,"sal","shared_rider_hold"),reg=SchemaSql.Q(c,"hr","commercial_registration");
        string eligible(string n)=>$"""
            EXISTS(SELECT 1 FROM {f} f JOIN {r} r ON r.id={n}.pay_run_rider_id JOIN {run} pr ON pr.id=r.pay_run_id
              JOIN {p} p ON p.id={n}.payment_id JOIN {a} a ON a.id={n}.allocation_id
              JOIN {reg} cr ON cr.id={n}.registration_id
              WHERE f.id={n}.bank_file_id AND f.status=1 AND pr.run_mode=0 AND p.run_mode=0
                AND r.salary_approved_at IS NOT NULL AND r.debt_confirm_status IN ('confirmed','deferred')
                AND r.payable_id={n}.payable_id AND r.company_id={n}.company_id AND cr.company_id={n}.company_id
                AND p.payer_company_id=f.paying_company_id AND p.status=1
                AND f.payout_blocked>=pr.payout_blocked AND f.payout_blocked>=p.payout_blocked
                AND (f.issuance_choice='combined' OR (f.scope_company_id={n}.company_id AND (f.scope_registration_id IS NULL OR f.scope_registration_id={n}.registration_id)))
                AND a.payment_id={n}.payment_id AND a.payment_line_id={n}.id AND a.payable_id={n}.payable_id AND a.company_id={n}.company_id
                AND a.period_month={n}.period_month AND a.payable_period_month={n}.payable_period_month AND a.amount={n}.amount AND a.claim_status=1
                AND NOT EXISTS(SELECT 1 FROM {hold} h WHERE h.erp_rider_id=r.erp_rider_id AND h.period_month=r.period_month AND h.status='open'))
            """;
        // Eligibility at INSERT, with the reservation in the same TX. Updates cannot change the source or amount.
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"CREATE OR ALTER TRIGGER sal.TR_bank_line_insert ON {SchemaSql.Q(c,"sal","bank_file_line")} AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted n WHERE n.status<>1 OR NOT ({eligible("n")})) THROW 51042,'Bank line is not eligible or allocation does not match',1; END;");
        else SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_bank_line_insert BEFORE INSERT ON {SchemaSql.Q(c,"sal","bank_file_line")} BEGIN SELECT RAISE(ABORT,'Bank line is not eligible or allocation does not match') WHERE NEW.status<>1 OR NOT ({eligible("NEW")}); END;");
        SchemaMoneyGuard.Rule(c,tx,"sal","bank_file_line","TR_bank_line_state",_=>"1=0",
            (o,n)=>$"{o}.status<>{n}.status AND ({o}.status<>1 OR {n}.status<>4 OR EXISTS(SELECT 1 FROM {a} a WHERE a.id={n}.allocation_id AND a.claim_status IN (1,2)))");
        // A bank allocation cannot be posted while its file is draft or comparison-only, even through direct SQL.
        SchemaMoneyGuard.Rule(c,tx,"pay","payment_allocation","TR_bank_claim_state",n=>$"{n}.claim_status=2 AND EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"sal","bank_file_line")} l JOIN {f} f ON f.id=l.bank_file_id WHERE l.allocation_id={n}.id AND (l.status<>1 OR f.status NOT IN (2,3) OR f.payout_blocked=1))");
        // SalPayRun installs TR_allocation_run_flags earlier; on SQL Server first-created fires first — force bank claim first.
        if(SchemaSql.IsSql(c))SchemaSql.Exec(c,tx,"EXEC sp_settriggerorder @triggername=N'pay.TR_bank_claim_state', @order=N'First', @stmttype=N'UPDATE';");
        // Freeze the paying company/amount on a queued payment as soon as a bank line claims it.
        SchemaMoneyGuard.Rule(c,tx,"pay","payment","TR_bank_payment_identity",_=>"1=0",
            (o,n)=>$"EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"sal","bank_file_line")} l WHERE l.payment_id={o}.id) AND ({n}.id IS NULL OR ({SchemaMoneyGuard.Changed(c,n,o,"payer_company_id","company_id","period_month","amount","run_mode")}))");
    }

    static void EnsureHeader(DbConnection c,DbTransaction? tx)
    {
        string line=SchemaSql.Q(c,"sal","bank_file_line"),a=SchemaSql.Q(c,"pay","payment_allocation"),p=SchemaSql.Q(c,"pay","payment"),r=SchemaSql.Q(c,"sal","pay_run_rider"),run=SchemaSql.Q(c,"sal","pay_run"),hold=SchemaSql.Q(c,"sal","shared_rider_hold"),reg=SchemaSql.Q(c,"hr","commercial_registration");
        string maker(string n)=>SchemaSql.IsSql(c)?$"LOWER({n}.created_by)=LOWER(CONVERT(varchar(36),{n}.approved_by))":$"lower({n}.created_by)=lower({n}.approved_by)";
        SchemaMoneyGuard.Rule(c,tx,"sal","bank_file","TR_bank_file_state",n=>$"""
            ({n}.scope_registration_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM {reg} cr WHERE cr.id={n}.scope_registration_id AND cr.company_id={n}.scope_company_id))
            OR ({n}.status IN (2,3) AND ({maker(n)} OR NOT EXISTS(SELECT 1 FROM {line} l WHERE l.bank_file_id={n}.id AND l.status=1)))
            OR ({n}.status=4 AND EXISTS(SELECT 1 FROM {line} l WHERE l.bank_file_id={n}.id AND l.status=1))
            OR ({n}.status=3 AND {n}.payout_blocked=1)
            OR ({n}.status=3 AND EXISTS(SELECT 1 FROM {line} l WHERE l.bank_file_id={n}.id AND l.status=1 AND l.company_id<>{n}.paying_company_id AND NOT EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"fin","inter_company_settlement")} s WHERE s.bank_file_line_id=l.id AND s.status=2)))
            OR ({n}.status IN (2,3) AND EXISTS(SELECT 1 FROM {line} l JOIN {r} r ON r.id=l.pay_run_rider_id JOIN {run} pr ON pr.id=r.pay_run_id JOIN {p} p ON p.id=l.payment_id JOIN {a} a ON a.id=l.allocation_id
              WHERE l.bank_file_id={n}.id AND l.status=1 AND (r.salary_approved_at IS NULL OR r.debt_confirm_status='pending' OR pr.run_mode=1 OR p.run_mode=1
                OR {n}.payout_blocked<pr.payout_blocked OR {n}.payout_blocked<p.payout_blocked
                OR EXISTS(SELECT 1 FROM {hold} h WHERE h.erp_rider_id=r.erp_rider_id AND h.period_month=r.period_month AND h.status='open')
                OR ({n}.status=2 AND a.claim_status NOT IN (1,2)) OR ({n}.status=3 AND (a.claim_status<>2 OR p.status<>2)))))
            """,
            (o,n)=>$"""
                {n}.id IS NULL OR ({SchemaMoneyGuard.Changed(c,n,o,"id","period_month")})
                OR ({SchemaMoneyGuard.Changed(c,n,o,"created_by")})
                OR (EXISTS(SELECT 1 FROM {line} l WHERE l.bank_file_id={o}.id) AND ({SchemaMoneyGuard.Changed(c,n,o,"issuance_choice","scope_company_id","scope_registration_id","paying_company_id","paying_registration_id","paying_bank_id")}))
                OR ({n}.status<>{o}.status AND NOT ({o}.status=1 AND {n}.status IN (2,4) OR {o}.status=2 AND {n}.status IN (3,4)))
                OR ({o}.status>=2 AND ({SchemaMoneyGuard.Changed(c,n,o,"issuance_choice","scope_company_id","scope_registration_id","paying_company_id","paying_registration_id","paying_bank_id","currency_code","approved_by","approved_at","created_by","file_hash")}))
                OR ({o}.status=3 AND ({SchemaMoneyGuard.Changed(c,n,o,"sent_by","sent_at")}))
                """);
    }

    static void EnsureSettlement(DbConnection c,DbTransaction? tx)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c),line=SchemaSql.Q(c,"sal","bank_file_line"),file=SchemaSql.Q(c,"sal","bank_file"),j=SchemaSql.Q(c,"fin","journal");
        SchemaSql.EnsureTable(c,tx,"fin","inter_company_settlement",$"""
            id {g} NOT NULL, bank_file_line_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            paying_company_id {g} NOT NULL, entitlement_company_id {g} NOT NULL, amount {m} NOT NULL CHECK(amount>0),
            payer_journal_id {g} NULL, entitlement_journal_id {g} NULL,
            status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1 CHECK(status IN (1,2)),
            {SchemaSql.MoneyAuditCols(c)}, CONSTRAINT PK_fin_inter_company_settlement {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_fin_inter_company_bank_line UNIQUE(bank_file_line_id),
            CONSTRAINT CK_fin_inter_company_distinct CHECK(paying_company_id<>entitlement_company_id),
            CONSTRAINT FK_inter_company_bank_line FOREIGN KEY(bank_file_line_id,period_month) REFERENCES {line}(id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_inter_company_payer FOREIGN KEY(paying_company_id) REFERENCES {SchemaSql.Q(c,"hr","company")}(id) ON DELETE NO ACTION,
            CONSTRAINT FK_inter_company_entitlement FOREIGN KEY(entitlement_company_id) REFERENCES {SchemaSql.Q(c,"hr","company")}(id) ON DELETE NO ACTION,
            CONSTRAINT FK_inter_company_payer_journal FOREIGN KEY(payer_journal_id) REFERENCES {j}(id) ON DELETE NO ACTION,
            CONSTRAINT FK_inter_company_entitlement_journal FOREIGN KEY(entitlement_journal_id) REFERENCES {j}(id) ON DELETE NO ACTION
            """);
        string jl=SchemaSql.Q(c,"fin","journal_line");
        SchemaMoneyGuard.Rule(c,tx,"fin","inter_company_settlement","TR_inter_company_source",n=>$"""
            NOT EXISTS(SELECT 1 FROM {line} l JOIN {file} f ON f.id=l.bank_file_id WHERE l.id={n}.bank_file_line_id AND l.company_id={n}.entitlement_company_id AND f.paying_company_id={n}.paying_company_id AND l.amount={n}.amount)
            OR ({n}.status=2 AND NOT EXISTS(SELECT 1 FROM {j} p JOIN {j} e ON e.id={n}.entitlement_journal_id JOIN {line} l ON l.id={n}.bank_file_line_id JOIN {file} f ON f.id=l.bank_file_id
              WHERE p.id={n}.payer_journal_id AND p.company_id={n}.paying_company_id AND e.company_id={n}.entitlement_company_id
                AND p.status=2 AND e.status=2 AND p.run_mode=0 AND e.run_mode=0 AND f.payout_blocked=0 AND l.status=1
                AND f.status IN (2,3) AND EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"pay","payment_allocation")} a WHERE a.id=l.allocation_id AND a.claim_status=2)
                AND p.period_month={n}.period_month AND e.period_month={n}.period_month AND p.purpose='bank_inter_company' AND e.purpose='bank_inter_company'
                AND p.source_type='inter_company_settlement' AND p.source_id={n}.id AND e.source_type='inter_company_settlement' AND e.source_id={n}.id
                AND (SELECT COALESCE(SUM(pl.debit),0) FROM {jl} pl WHERE pl.journal_id=p.id)={n}.amount
                AND (SELECT COALESCE(SUM(el.debit),0) FROM {jl} el WHERE el.journal_id=e.id)={n}.amount))
            """,
            (o,n)=>$"{n}.id IS NULL OR ({SchemaMoneyGuard.Changed(c,n,o,"id","bank_file_line_id","period_month","paying_company_id","entitlement_company_id","amount")}) OR ({o}.status=2 AND ({SchemaMoneyGuard.Changed(c,n,o,"status","payer_journal_id","entitlement_journal_id")}))");
        SchemaMoneyGuard.Retain(c,tx,"fin","inter_company_settlement","o.bank_file_line_id=NEW.bank_file_line_id");
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"fin","inter_company_settlement","UQ_inter_company_payer_journal","payer_journal_id","payer_journal_id IS NOT NULL");
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"fin","inter_company_settlement","UQ_inter_company_entitlement_journal","entitlement_journal_id","entitlement_journal_id IS NOT NULL");
        SchemaSql.EnsureMonthIndex(c,tx,"fin","inter_company_settlement");
        foreach(string column in new[]{"paying_company_id","entitlement_company_id","payer_journal_id","entitlement_journal_id"})SchemaSql.CreateIndexIfMissing(c,tx,"fin","inter_company_settlement","IX_inter_company_"+column,column);
    }
}
