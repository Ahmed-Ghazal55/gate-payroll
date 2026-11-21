using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-15; design §5 / §16, BR §29.2 / §32 / §40.6. Empty schema only.
/// The movement ledger owns balance (initially zero); principal records the agreed principal, not a second opening balance.
/// Opening balances are positive import movements. Zero/deferred instalments never enter the ledger.
/// W2 writers lock the debt before INSERT and post the journal in that same transaction.</summary>
public static class SchemaDebt
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c),d=SchemaSql.Q(c,"debt","debt"),mov=SchemaSql.Q(c,"debt","debt_movement"),company=SchemaSql.Q(c,"hr","company");
        SchemaSql.EnsureTable(c,tx,"debt","debt",$"""
            id {g} NOT NULL, erp_rider_id {g} NOT NULL, company_id {g} NOT NULL,
            category {SchemaSql.Varchar(c,40)} NOT NULL, principal {m} NOT NULL CHECK(principal>=0),
            balance {m} NOT NULL DEFAULT 0 CHECK(balance>=0), instalment {m} NULL CHECK(instalment>=0),
            status {SchemaSql.Varchar(c,12)} NOT NULL DEFAULT 'open' CHECK(status IN ('open','settled','waived','absorbed','cancelled','transferred')),
            platform {SchemaSql.Varchar(c,20)} NULL,
            promissory_flag {SchemaSql.Bit(c)} NOT NULL DEFAULT 0 CHECK(promissory_flag IN (0,1)),
            {SchemaSql.MoneyAuditCols(c)}, CONSTRAINT PK_debt_debt {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_debt_company UNIQUE(id,company_id),
            CONSTRAINT FK_debt_company FOREIGN KEY(company_id) REFERENCES {company}(id) ON DELETE NO ACTION
            """);
        EnsureDebtPlatformColumn(c,tx);
        SchemaSql.EnsureTable(c,tx,"debt","debt_movement",$"""
            id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL, debt_id {g} NOT NULL, company_id {g} NOT NULL,
            occurred_on {SchemaSql.Date(c)} NOT NULL, amount {m} NOT NULL CHECK(amount<>0),
            source_type {SchemaSql.Varchar(c,40)} NOT NULL, source_id {g} NOT NULL,
            pay_run_rider_id {g} NULL, pay_run_period_month {SchemaSql.Date(c)} NULL, payment_id {g} NULL, journal_id {g} NULL,
            run_mode {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0 CHECK(run_mode=0),
            payout_blocked {SchemaSql.Bit(c)} NOT NULL DEFAULT 0 CHECK(payout_blocked IN (0,1)),
            {SchemaSql.MoneyAuditCols(c)}, CONSTRAINT PK_debt_movement {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_debt_mov_source UNIQUE(source_type,source_id),
            CONSTRAINT CK_debt_mov_month CHECK({SchemaSql.MonthCheck(c)} AND {DateInMonth(c,"occurred_on")}),
            CONSTRAINT CK_debt_mov_salary CHECK(source_type NOT IN ('salary_deduction','carryover_112507') OR pay_run_rider_id IS NOT NULL),
            CONSTRAINT CK_debt_mov_salary_month CHECK((pay_run_rider_id IS NULL AND pay_run_period_month IS NULL) OR (pay_run_rider_id IS NOT NULL AND pay_run_period_month IS NOT NULL)),
            CONSTRAINT CK_debt_mov_disbursement CHECK(source_type<>'new_disbursement' OR payment_id IS NOT NULL),
            CONSTRAINT FK_debt_mov_debt FOREIGN KEY(debt_id,company_id) REFERENCES {d}(id,company_id) ON DELETE NO ACTION,
            CONSTRAINT FK_debt_mov_rider FOREIGN KEY(pay_run_rider_id,pay_run_period_month) REFERENCES {SchemaSql.Q(c,"sal","pay_run_rider")}(id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_debt_mov_payment FOREIGN KEY(payment_id,period_month) REFERENCES {SchemaSql.Q(c,"pay","payment")}(id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_debt_mov_journal FOREIGN KEY(journal_id) REFERENCES {SchemaSql.Q(c,"fin","journal")}(id) ON DELETE NO ACTION
            """);
        foreach(string table in new[]{"import_batch","collection","disbursement","waiver"})EnsureDocument(c,tx,table);
        // Permanent file identity: same company + kind + content hash cannot open a second batch.
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"debt","import_batch","UQ_debt_import_file","company_id,import_kind,file_hash","file_hash IS NOT NULL");
        SchemaMoneyGuard.Retain(c,tx,"debt","import_batch","o.company_id=NEW.company_id AND o.import_kind=NEW.import_kind AND o.file_hash=NEW.file_hash");
        SchemaSql.EnsureTable(c,tx,"debt","import_line",$"""
            id {g} NOT NULL, import_batch_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            line_no {SchemaSql.Int(c)} NOT NULL CHECK(line_no>0), source_row {SchemaSql.Int(c)} NOT NULL CHECK(source_row>0),
            debt_id {g} NOT NULL, company_id {g} NOT NULL, amount {m} NOT NULL CHECK(amount<>0),
            CONSTRAINT PK_debt_import_line {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_debt_import_line UNIQUE(import_batch_id,line_no),
            CONSTRAINT UQ_debt_import_source_row UNIQUE(import_batch_id,source_row),
            CONSTRAINT FK_debt_import_batch FOREIGN KEY(import_batch_id,company_id,period_month) REFERENCES {SchemaSql.Q(c,"debt","import_batch")}(id,company_id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_debt_import_line_debt FOREIGN KEY(debt_id,company_id) REFERENCES {d}(id,company_id) ON DELETE NO ACTION
            """);
        SchemaMoneyGuard.Retain(c,tx,"debt","import_line","o.import_batch_id=NEW.import_batch_id AND (o.line_no=NEW.line_no OR o.source_row=NEW.source_row)");
        SchemaMoneyGuard.Rule(c,tx,"debt","import_line","TR_debt_import_line_frozen",
            n=>$"EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"debt","import_batch")} b WHERE b.id={n}.import_batch_id AND b.status>=3)",
            (o,n)=>$"{n}.id IS NULL OR ({SchemaMoneyGuard.Changed(c,n,o,"id","import_batch_id","company_id","period_month")}) OR EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"debt","import_batch")} b WHERE b.id={o}.import_batch_id AND b.status>=3)");
        SchemaSql.EnsureMonthIndex(c,tx,"debt","import_line");
        SchemaSql.CreateIndexIfMissing(c,tx,"debt","import_line","IX_debt_import_batch","import_batch_id,company_id,period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"debt","import_line","IX_debt_import_line_debt","debt_id,company_id");
        SchemaSql.EnsureMonthIndex(c,tx,"debt","debt_movement");
        SchemaSql.EnsureRowVersion(c,tx,"debt","debt");
        SchemaSql.CreateIndexIfMissing(c,tx,"debt","debt","IX_debt_rider","erp_rider_id,company_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"debt","debt","IX_debt_company","company_id");
        foreach(var (column,index) in new[]{("debt_id,company_id","debt"),("pay_run_rider_id,pay_run_period_month","rider"),("payment_id,period_month","payment"),("journal_id","journal")})
            SchemaSql.CreateIndexIfMissing(c,tx,"debt","debt_movement","IX_debt_mov_"+index,column);
        SchemaMoneyGuard.Retain(c,tx,"debt","debt");
        SchemaMoneyGuard.Retain(c,tx,"debt","debt_movement","o.source_type=NEW.source_type AND o.source_id=NEW.source_id");
        SchemaMoneyRegister.Immutable(c,tx,"debt","debt_movement","period_month","debt_id","company_id","occurred_on","amount","source_type","source_id","journal_id","payment_id","pay_run_rider_id","pay_run_period_month","run_mode","payout_blocked","generation");
        SchemaMoneyRegister.BusinessKey(c,tx,"debt","debt_movement",n=>SchemaMoneyRegister.Join(c,$"{n}.source_type",SchemaMoneyRegister.IdString(c,$"{n}.source_id")));
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"debt","debt_movement","UQ_debt_disb_payment","payment_id","source_type='new_disbursement' AND payment_id IS NOT NULL");
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"debt","debt_movement","UQ_debt_carryover_rider","pay_run_rider_id","source_type='carryover_112507' AND pay_run_rider_id IS NOT NULL");
        EnsureMovementRules(c,tx);
        EnsureBalance(c,tx);
    }

    internal static string DateInMonth(DbConnection c,string column)=>SchemaSql.IsSql(c)?$"{column}>=period_month AND {column}<DATEADD(month,1,period_month)":$"date({column}) IS NOT NULL AND {column}=date({column}) AND {column}>=period_month AND {column}<date(period_month,'+1 month')";

    static void EnsureDocument(DbConnection c,DbTransaction? tx,string table)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c);
        string extra=table switch
        {
            "import_batch"=>$"file_hash {SchemaSql.Binary(c,32)} NOT NULL CHECK(length_placeholder), import_kind {SchemaSql.Varchar(c,10)} NOT NULL CHECK(import_kind IN ('opening','add','reduce')),",
            "disbursement"=>$"payment_id {g} NULL, CONSTRAINT FK_debt_disbursement_payment FOREIGN KEY(payment_id,period_month) REFERENCES {SchemaSql.Q(c,"pay","payment")}(id,period_month) ON DELETE NO ACTION,",
            "waiver"=>$"expense_account_code {SchemaSql.Varchar(c,16)} NOT NULL CHECK(expense_account_code IN ('526601','526602','526603','526801','526802','526803','526804','526805','526806')), CONSTRAINT FK_debt_waiver_account FOREIGN KEY(expense_account_code) REFERENCES {SchemaSql.Q(c,"fin","account")}(code) ON DELETE NO ACTION,",
            _=>""
        };
        extra=extra.Replace("length_placeholder",SchemaSql.IsSql(c)?"DATALENGTH(file_hash)=32":"length(file_hash)=32");
        // Put table constraints after columns on both dialects.
        int constraint=extra.IndexOf("CONSTRAINT",StringComparison.Ordinal);
        string extraColumns=constraint<0?extra:extra[..constraint],extraConstraints=constraint<0?"":extra[constraint..];
        SchemaSql.EnsureTable(c,tx,"debt",table,$"""
            id {g} NOT NULL, company_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            {(table=="import_batch"?"":$"debt_id {g} NOT NULL,")}
            amount {m} NOT NULL CHECK(amount>0), reason {SchemaSql.Text(c,400)} NOT NULL,
            {extraColumns} {SchemaMoneyRegister.WorkflowColumns(c)}, {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_debt_{table} {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_debt_{table}_scope UNIQUE(id,company_id,period_month),
            {extraConstraints}
            CONSTRAINT CK_debt_{table}_month CHECK({SchemaSql.MonthCheck(c)}),
            CONSTRAINT FK_debt_{table}_company FOREIGN KEY(company_id) REFERENCES {SchemaSql.Q(c,"hr","company")}(id) ON DELETE NO ACTION
            {(table=="import_batch"?"":$", CONSTRAINT FK_debt_{table}_debt FOREIGN KEY(debt_id,company_id) REFERENCES {SchemaSql.Q(c,"debt","debt")}(id,company_id) ON DELETE NO ACTION")}
            """);
        var frozen=new List<string>{"company_id","period_month","amount","reason"};
        if(table!="import_batch")frozen.Add("debt_id");
        if(table=="import_batch")frozen.AddRange(new[]{"file_hash","import_kind"});
        if(table=="disbursement")frozen.Add("payment_id");
        if(table=="waiver")frozen.Add("expense_account_code");
        SchemaMoneyRegister.Workflow(c,tx,"debt",table,frozen.ToArray());
        SchemaMoneyRegister.Immutable(c,tx,"debt",table,"company_id","period_month");
        SchemaSql.EnsureMonthIndex(c,tx,"debt",table);
        SchemaSql.CreateIndexIfMissing(c,tx,"debt",table,$"IX_debt_{table}_company","company_id");
        if(table!="import_batch")SchemaSql.CreateIndexIfMissing(c,tx,"debt",table,$"IX_debt_{table}_debt","debt_id,company_id");
        if(table=="disbursement")SchemaSql.CreateIndexIfMissing(c,tx,"debt",table,"IX_debt_disbursement_payment","payment_id,period_month");
        if(table=="waiver")SchemaSql.CreateIndexIfMissing(c,tx,"debt",table,"IX_debt_waiver_account","expense_account_code");
    }

    static void EnsureMovementRules(DbConnection c,DbTransaction? tx)
    {
        string d=SchemaSql.Q(c,"debt","debt"),r=SchemaSql.Q(c,"sal","pay_run_rider"),run=SchemaSql.Q(c,"sal","pay_run"),p=SchemaSql.Q(c,"pay","payment"),j=SchemaSql.Q(c,"fin","journal");
        SchemaMoneyGuard.Rule(c,tx,"debt","debt_movement","TR_debt_movement_source",n=>$"""
            NOT EXISTS(SELECT 1 FROM {d} d WHERE d.id={n}.debt_id AND d.status='open')
            OR ({n}.journal_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM {j} j WHERE j.id={n}.journal_id AND j.company_id={n}.company_id AND j.period_month={n}.period_month AND j.run_mode=0 AND j.status=2))
            OR ({n}.pay_run_rider_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM {r} r JOIN {run} pr ON pr.id=r.pay_run_id JOIN {d} d ON d.id={n}.debt_id
              WHERE r.id={n}.pay_run_rider_id AND r.company_id={n}.company_id AND r.erp_rider_id=d.erp_rider_id AND pr.run_mode=0
              AND (({n}.source_type='salary_deduction' AND {n}.amount<0 AND r.salary_approved_at IS NULL AND r.debt_confirm_status='confirmed' AND -{n}.amount<=r.old_debt_instalment AND -{n}.amount<=r.salary_net)
                OR ({n}.source_type='carryover_112507' AND {n}.amount>0 AND r.salary_approved_at IS NOT NULL AND r.keeta_ineligible=0 AND r.salary_net<0 AND {n}.amount=-r.salary_net))))
            OR ({n}.source_type='new_disbursement' AND ({n}.amount<=0 OR NOT EXISTS(SELECT 1 FROM {p} p WHERE p.id={n}.payment_id AND p.company_id={n}.company_id AND p.status=2 AND p.run_mode=0 AND p.payout_blocked=0 AND p.amount={n}.amount)))
            """);
        foreach(var (kind,table,sign) in new[]{("collection","collection",-1),("waiver","waiver",-1),("absorption","waiver",-1),("new_disbursement","disbursement",1)})
            SchemaMoneyGuard.Rule(c,tx,"debt","debt_movement","TR_debt_source_"+(kind=="absorption"?"absorption":table),n=>$"{n}.source_type='{kind}' AND NOT EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"debt",table)} s WHERE s.id={n}.source_id AND s.debt_id={n}.debt_id AND s.company_id={n}.company_id AND s.period_month={n}.period_month AND s.status=3 AND s.run_mode=0 AND {n}.amount=s.amount*({sign}){(table=="disbursement"?$" AND s.payment_id={n}.payment_id":"")})");
        SchemaMoneyGuard.Rule(c,tx,"debt","debt_movement","TR_debt_source_import",n=>$"{n}.source_type='import' AND NOT EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"debt","import_line")} l JOIN {SchemaSql.Q(c,"debt","import_batch")} b ON b.id=l.import_batch_id WHERE l.id={n}.source_id AND l.debt_id={n}.debt_id AND l.company_id={n}.company_id AND l.period_month={n}.period_month AND l.amount={n}.amount AND b.status=3 AND b.run_mode=0 AND (b.import_kind='reduce' AND l.amount<0 OR b.import_kind IN ('opening','add') AND l.amount>0))");
    }

    static void EnsureDebtPlatformColumn(DbConnection c,DbTransaction? tx)
    {
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF COL_LENGTH(N'debt.debt',N'platform') IS NULL ALTER TABLE {SchemaSql.Q(c,"debt","debt")} ADD platform {SchemaSql.Varchar(c,20)} NULL;
                IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'debt.debt') AND name=N'CK_debt_debt_status')
                  ALTER TABLE {SchemaSql.Q(c,"debt","debt")} DROP CONSTRAINT CK_debt_debt_status;
                IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'debt.debt') AND name=N'CK_debt_debt_status')
                  ALTER TABLE {SchemaSql.Q(c,"debt","debt")} ADD CONSTRAINT CK_debt_debt_status CHECK (status IN ('open','settled','waived','absorbed','cancelled','transferred'));
                """);
            return;
        }
        // SQLite: fresh EnsureTable already has platform + absorbed; rebuild CHECK only when upgrading an older in-memory shape is not required for tests.
        object? cols=SchemaSql.Scalar(c,tx,"SELECT COUNT(1) FROM pragma_table_info('debt_debt') WHERE name='platform'");
        if(Convert.ToInt64(cols!)==0)
            SchemaSql.Exec(c,tx,"ALTER TABLE debt_debt ADD COLUMN platform TEXT NULL");
    }

    static void EnsureBalance(DbConnection c,DbTransaction? tx)
    {
        string d=SchemaSql.Q(c,"debt","debt"),mov=SchemaSql.Q(c,"debt","debt_movement");
        SchemaMoneyGuard.Rule(c,tx,"debt","debt","TR_debt_balance",n=>$"{n}.balance<>round(COALESCE((SELECT SUM(amount) FROM {mov} m WHERE m.debt_id={n}.id),0),2) OR ({n}.status<>'open' AND {n}.balance<>0)",
            (o,n)=>$"{n}.id IS NULL OR ({SchemaMoneyGuard.Changed(c,n,o,"id","erp_rider_id","company_id")}) OR ({o}.status<>'open' AND {n}.status<>{o}.status)");
        // Total salary deductions for a rider cannot exceed min(confirmed instalment, positive net).
        string overCap=SchemaSql.IsSql(c)
            ?"CASE WHEN r.salary_net>0 AND r.old_debt_instalment<=r.salary_net THEN r.old_debt_instalment WHEN r.salary_net>0 THEN r.salary_net ELSE 0 END < -(SELECT COALESCE(SUM(amount),0) FROM "+mov+" m WHERE m.pay_run_rider_id=r.id AND m.source_type='salary_deduction')"
            :"CASE WHEN r.salary_net>0 AND r.old_debt_instalment<=r.salary_net THEN r.old_debt_instalment WHEN r.salary_net>0 THEN r.salary_net ELSE 0 END < -COALESCE((SELECT SUM(amount) FROM "+mov+" m WHERE m.pay_run_rider_id=r.id AND m.source_type='salary_deduction'),0)";
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"CREATE OR ALTER TRIGGER debt.TR_debt_movement_balance ON {mov} AFTER INSERT AS BEGIN SET NOCOUNT ON; UPDATE d SET balance=s.total FROM {d} d CROSS APPLY(SELECT COALESCE(SUM(amount),0) total FROM {mov} m WHERE m.debt_id=d.id) s WHERE d.id IN(SELECT debt_id FROM inserted); IF EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r WHERE r.id IN(SELECT pay_run_rider_id FROM inserted) AND ({overCap})) THROW 51040,'Debt deductions exceed net or instalment',1; END;");
        else
            SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_debt_movement_balance AFTER INSERT ON {mov} BEGIN UPDATE {d} SET balance=round((SELECT SUM(amount) FROM {mov} WHERE debt_id=NEW.debt_id),2) WHERE id=NEW.debt_id; SELECT RAISE(ABORT,'Debt deductions exceed net or instalment') WHERE EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r WHERE r.id=NEW.pay_run_rider_id AND ({overCap})); END;");
    }

    /// <summary>Call inside the journal+movement transaction BEFORE INSERT. Compare row_version in any metadata UPDATE.
    /// Lock multiple debts by ascending id; on concurrency mismatch retry the whole transaction once.</summary>
    public static decimal LockDebt(DbConnection c,DbTransaction tx,string debtId)
    {
        object? value=SchemaSql.Scalar(c,tx,$"SELECT balance FROM {SchemaSql.Q(c,"debt","debt")}{(SchemaSql.IsSql(c)?" WITH (UPDLOCK,HOLDLOCK)":"")} WHERE id=@id",("@id",debtId));
        return value is null or DBNull?throw new InvalidOperationException("Debt not found."):Convert.ToDecimal(value);
    }
}
