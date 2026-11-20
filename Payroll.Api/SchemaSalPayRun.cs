using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-14c: empty cycle core. Design §4.1–2 E8 / BR §32, §38.5, §40.4–6.
/// Engine stage 0–9 maps to UI 1–10. Level 1 → net → Level 2; debt confirmation precedes salary approval.
/// Flags describe the run; runtime posting/payout guards remain U-W2-31. Capture gate installed by SchemaOperationCapture.</summary>
public static class SchemaSalPayRun
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c),run=SchemaSql.Q(c,"sal","pay_run"),rider=SchemaSql.Q(c,"sal","pay_run_rider"),hold=SchemaSql.Q(c,"sal","shared_rider_hold");
        SchemaSql.EnsureTable(c,tx,"sal","pay_run",$"""
            id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            app_code {SchemaSql.Varchar(c,20)} NOT NULL, company_id {g} NOT NULL,
            stage {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0, status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1,
            run_mode {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0, payout_blocked {SchemaSql.Bit(c)} NOT NULL DEFAULT 0,
            currency_code {SchemaSql.Varchar(c,3)} NOT NULL DEFAULT 'SAR',
            capture_state {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0, calc_started_at {SchemaSql.DateTime(c)} NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_pay_run {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_run_scope UNIQUE (id,company_id,period_month),
            CONSTRAINT UQ_sal_run_month UNIQUE (id,period_month),
            CONSTRAINT UQ_sal_run_mode_scope UNIQUE (id,company_id,period_month,run_mode),
            CONSTRAINT CK_sal_run_stage CHECK (stage BETWEEN 0 AND 9),
            CONSTRAINT CK_sal_run_status CHECK (status IN (1,2,3,4,5)),
            CONSTRAINT CK_sal_run_flags CHECK (run_mode IN (0,1) AND payout_blocked IN (0,1)),
            CONSTRAINT CK_sal_run_currency CHECK (currency_code='SAR'),
            CONSTRAINT CK_sal_run_month CHECK ({SchemaSql.MonthCheck(c)}),
            CONSTRAINT CK_sal_run_capture CHECK (capture_state IN (0,1,2) AND (stage<=1 OR capture_state>0) AND (capture_state=0 OR calc_started_at IS NOT NULL)),
            CONSTRAINT FK_sal_run_company FOREIGN KEY (company_id) REFERENCES {SchemaSql.Q(c,"hr","company")}(id) ON DELETE NO ACTION
            """);
        SchemaMoneyFlags.Ensure(c,tx,"sal","pay_run",o=>$"{o}.capture_state>0 OR {o}.status IN (2,3,5)");
        // Approved/closed status requires capture: owned here (not CK_sal_run_capture) so SQL Server reports TR_run_state, not the CHECK.
        SchemaMoneyGuard.Rule(c,tx,"sal","pay_run","TR_run_state",
            n=>$"{n}.status IN (2,3,5) AND {n}.capture_state=0",
            (o,n)=>$"{o}.status IN (2,3,4,5) AND ({n}.id IS NULL OR {n}.status=1 OR ({o}.status=4 AND {n}.status<>4))");
        // L3: one active live run and one active simulation may share app, company and month.
        SchemaSql.DropIndexIfExists(c,tx,"sal","pay_run","UQ_sal_run_active");
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"sal","pay_run","UQ_sal_run_active_mode","app_code,company_id,period_month,run_mode","status<>4");
        SchemaSql.EnsureTable(c,tx,"sal","pay_run_rider",$"""
            id {g} NOT NULL, pay_run_id {g} NOT NULL, company_id {g} NOT NULL,
            period_month {SchemaSql.Date(c)} NOT NULL, erp_rider_id {g} NOT NULL,
            run_mode {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0,
            gross {m} NOT NULL, commission {m} NOT NULL, salary_net {m} NOT NULL,
            old_debt_instalment {m} NOT NULL DEFAULT 0, salary_approved_at {SchemaSql.DateTime(c)} NULL,
            debt_confirm_status {SchemaSql.Varchar(c,12)} NOT NULL DEFAULT 'pending',
            bank_eligible {SchemaSql.Bit(c)} NOT NULL DEFAULT 0, keeta_ineligible {SchemaSql.Bit(c)} NOT NULL DEFAULT 0,
            payable_id {g} NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_pay_run_rider {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_run_rider UNIQUE (pay_run_id,erp_rider_id),
            CONSTRAINT UQ_sal_rider_month UNIQUE (id,period_month),
            CONSTRAINT CK_sal_rider_instalment CHECK (old_debt_instalment>=0),
            CONSTRAINT CK_sal_rider_debt CHECK (debt_confirm_status IN ('pending','confirmed','deferred') AND (debt_confirm_status<>'deferred' OR old_debt_instalment=0)),
            CONSTRAINT CK_sal_rider_approval CHECK (salary_approved_at IS NULL OR debt_confirm_status IN ('confirmed','deferred')),
            CONSTRAINT CK_sal_rider_flags CHECK (bank_eligible IN (0,1) AND keeta_ineligible IN (0,1) AND run_mode IN (0,1)),
            CONSTRAINT CK_sal_rider_bank CHECK (bank_eligible=0 OR (run_mode=0 AND salary_approved_at IS NOT NULL AND debt_confirm_status IN ('confirmed','deferred') AND payable_id IS NOT NULL AND salary_net>0)),
            CONSTRAINT FK_sal_rider_run FOREIGN KEY (pay_run_id,company_id,period_month,run_mode) REFERENCES {run}(id,company_id,period_month,run_mode) ON DELETE NO ACTION,
            CONSTRAINT FK_sal_rider_payable FOREIGN KEY (payable_id,company_id,period_month) REFERENCES {SchemaSql.Q(c,"pay","payable")}(id,company_id,period_month) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureTable(c,tx,"sal","pay_run_item",$"""
            id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL, pay_run_rider_id {g} NOT NULL,
            line_no {SchemaSql.Int(c)} NOT NULL, stage_code {SchemaSql.Varchar(c,40)} NOT NULL,
            item_code {SchemaSql.Varchar(c,40)} NOT NULL, amount {m} NOT NULL,
            affects_net {SchemaSql.Bit(c)} NOT NULL, account_code {SchemaSql.Varchar(c,16)} NOT NULL,
            erp_platform_account_id {g} NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_pay_run_item {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_run_item_line UNIQUE (pay_run_rider_id,line_no),
            CONSTRAINT CK_sal_item_line CHECK (line_no>0),
            CONSTRAINT CK_sal_item_stage CHECK (stage_code IN ('invoice','distribution','commission','level1','net','level2','old_debt','adjustment')),
            CONSTRAINT CK_sal_item_affects CHECK (affects_net IN (0,1)),
            CONSTRAINT FK_sal_item_rider FOREIGN KEY (pay_run_rider_id,period_month) REFERENCES {rider}(id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_sal_item_account FOREIGN KEY (account_code) REFERENCES {SchemaSql.Q(c,"fin","account")}(code) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureTable(c,tx,"sal","shared_rider_hold",$"""
            id {g} NOT NULL, erp_rider_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            status {SchemaSql.Varchar(c,10)} NOT NULL DEFAULT 'open',
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_shared_hold {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT CK_sal_shared_hold_status CHECK (status IN ('open','cleared')),
            CONSTRAINT CK_sal_shared_hold_month CHECK ({SchemaSql.MonthCheck(c)})
            """);
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"sal","shared_rider_hold","UQ_sal_shared_hold_open","erp_rider_id,period_month","status='open'");
        SchemaSql.EnsureTable(c,tx,"sal","shared_rider_hold_app",$"""
            id {g} NOT NULL, hold_id {g} NOT NULL, app_code {SchemaSql.Varchar(c,20)} NOT NULL,
            received {SchemaSql.Bit(c)} NOT NULL DEFAULT 0,
            CONSTRAINT PK_sal_shared_hold_app {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_shared_hold_app UNIQUE (hold_id,app_code),
            CONSTRAINT CK_sal_shared_hold_received CHECK (received IN (0,1)),
            CONSTRAINT FK_sal_shared_hold_app FOREIGN KEY (hold_id) REFERENCES {hold}(id) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureTable(c,tx,"sal","rider_request",$"""
            id {g} NOT NULL, erp_rider_id {g} NOT NULL, company_id {g} NOT NULL,
            period_month {SchemaSql.Date(c)} NOT NULL, app_code {SchemaSql.Varchar(c,20)} NOT NULL,
            kind {SchemaSql.Varchar(c,12)} NOT NULL, amount {m} NOT NULL DEFAULT 0,
            status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_rider_request {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT CK_sal_request_kind CHECK (kind IN ('advance','hold','deduction')),
            CONSTRAINT CK_sal_request_status CHECK (status IN (1,2,3,4,5,6)),
            CONSTRAINT CK_sal_request_amount CHECK (amount>=0),
            CONSTRAINT CK_sal_request_month CHECK ({SchemaSql.MonthCheck(c)}),
            CONSTRAINT FK_sal_request_company FOREIGN KEY (company_id) REFERENCES {SchemaSql.Q(c,"hr","company")}(id) ON DELETE NO ACTION
            """);
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"sal","rider_request","UQ_sal_request_hold","erp_rider_id,period_month,app_code","kind='hold' AND status<4");
        foreach(string table in new[]{"pay_run","pay_run_rider","pay_run_item","rider_request"})
        {SchemaSql.EnsureMonthIndex(c,tx,"sal",table);SchemaSql.EnsureRowVersion(c,tx,"sal",table);}
        SchemaSql.EnsureRowVersion(c,tx,"sal","shared_rider_hold");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","pay_run","IX_sal_run_company","company_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","pay_run_rider","IX_sal_rider_run","pay_run_id,company_id,period_month,run_mode");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","pay_run_rider","IX_sal_rider_payable","payable_id,company_id,period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","pay_run_item","IX_sal_item_rider","pay_run_rider_id,period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","pay_run_item","IX_sal_item_account","account_code");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","rider_request","IX_sal_request_company","company_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","pay_run_rider","IX_sal_rider_erp","erp_rider_id,period_month");
        // SQL Server: filtered UQ_sal_run_active_mode must surface; SQLite no_replace covers the same active key.
        bool sqlReplace=!SchemaSql.IsSql(c);
        SchemaMoneyGuard.Retain(c,tx,"sal","pay_run","o.status<>4 AND NEW.status<>4 AND o.app_code=NEW.app_code AND o.company_id=NEW.company_id AND o.period_month=NEW.period_month AND o.run_mode=NEW.run_mode",replace:sqlReplace);
        SchemaMoneyGuard.Retain(c,tx,"sal","pay_run_item","o.pay_run_rider_id=NEW.pay_run_rider_id AND o.line_no=NEW.line_no",replace:sqlReplace);
        SchemaMoneyGuard.Retain(c,tx,"sal","pay_run_rider","o.pay_run_id=NEW.pay_run_id AND o.erp_rider_id=NEW.erp_rider_id",replace:sqlReplace);
        EnsureApprovedAmounts(c,tx);
        EnsureRiderProvenance(c,tx);
        EnsureClaimRunFlags(c,tx);
        EnsureHoldBlocksBank(c,tx);
    }

    /// <summary>After salary approval, rider money figures and their items are fixed; linked payable.amount_due follows.</summary>
    static void EnsureApprovedAmounts(DbConnection c,DbTransaction? tx)
    {
        string rider=SchemaSql.Q(c,"sal","pay_run_rider"),item=SchemaSql.Q(c,"sal","pay_run_item"),payable=SchemaSql.Q(c,"pay","payable");
        string approved(string alias)=>SchemaSql.IsSql(c)?$"{alias}.salary_approved_at IS NOT NULL":$"{alias}.salary_approved_at IS NOT NULL";
        SchemaMoneyGuard.Rule(c,tx,"sal","pay_run_rider","TR_rider_amounts_frozen",_=>"1=0",
            (o,n)=>$"{approved(o)} AND ({n}.id IS NULL OR ({SchemaMoneyGuard.Changed(c,n,o,"salary_net","gross","commission","old_debt_instalment")}))");
        SchemaMoneyGuard.Rule(c,tx,"sal","pay_run_item","TR_item_amounts_frozen",
            n=>$"EXISTS(SELECT 1 FROM {rider} r WHERE r.id={n}.pay_run_rider_id AND {approved("r")})",
            (o,n)=>$"{n}.id IS NULL OR EXISTS(SELECT 1 FROM {rider} r WHERE r.id=COALESCE({n}.pay_run_rider_id,{o}.pay_run_rider_id) AND {approved("r")})");
        SchemaMoneyGuard.Rule(c,tx,"pay","payable","TR_payable_due_frozen",_=>"1=0",
            (o,n)=>$"({SchemaMoneyGuard.Changed(c,n,o,"amount_due")}) AND EXISTS(SELECT 1 FROM {rider} r WHERE r.payable_id={o}.id AND {approved("r")})");
    }

    static void EnsureRiderProvenance(DbConnection c,DbTransaction? tx)
    {
        string p=SchemaSql.Q(c,"pay","payable"),a=SchemaSql.Q(c,"pay","payment_allocation"),r=SchemaSql.Q(c,"sal","pay_run_rider"),run=SchemaSql.Q(c,"sal","pay_run");
        SchemaMoneyGuard.Rule(c,tx,"sal","pay_run_rider","TR_rider_provenance",
            n=>$"""
                EXISTS(SELECT 1 FROM {run} pr JOIN {a} a ON a.payable_id={n}.payable_id
                  WHERE pr.id={n}.pay_run_id AND (pr.run_mode=1 AND a.claim_status IN (1,2) OR pr.payout_blocked=1 AND a.claim_status=2))
                """,
            (o,n)=>$"{n}.id IS NULL OR ({SchemaMoneyGuard.Changed(c,n,o,"id","pay_run_id","company_id","period_month","run_mode")}) OR ({o}.payable_id IS NOT NULL AND ({SchemaMoneyGuard.Changed(c,n,o,"payable_id")}))");
        // Fail closed for an unlinked salary source; its source id cannot be redirected to hide the owning run.
        SchemaMoneyGuard.Rule(c,tx,"pay","payment_allocation","TR_claim_salary_source",
            n=>$"""
                {n}.claim_status IN (1,2) AND EXISTS(SELECT 1 FROM {p} p WHERE p.id={n}.payable_id AND (p.kind='pay_run_rider' OR p.source_table='sal.pay_run_rider')
                  AND (p.kind<>'pay_run_rider' OR p.source_table<>'sal.pay_run_rider' OR NOT EXISTS(SELECT 1 FROM {r} r WHERE r.id=p.source_id AND r.company_id=p.company_id AND r.period_month=p.period_month)))
                """);
        SchemaMoneyGuard.Rule(c,tx,"pay","payable","TR_payable_source",n=>"1=0",
            (o,n)=>$"{n}.id IS NULL OR ({SchemaMoneyGuard.Changed(c,n,o,"id","source_table","source_id","kind","company_id","period_month")})");
        // A live, already-paid source cannot be turned into a blocked/simulation source after the fact.
        SchemaMoneyGuard.Rule(c,tx,"sal","pay_run","TR_run_existing_claims",
            n=>$"""
                EXISTS(SELECT 1 FROM {r} r JOIN {p} p ON p.id=r.payable_id OR p.source_id=r.id
                  JOIN {a} a ON a.payable_id=p.id WHERE r.pay_run_id={n}.id
                  AND ({n}.run_mode=1 AND a.claim_status IN (1,2) OR {n}.payout_blocked=1 AND a.claim_status=2))
                """);
    }

    /// <summary>L1 / BR shared-rider hold (M-0587): a rider with an open hold for the month is never bank eligible.
    /// Opening (or reopening) a hold clears the stored flag, and the flag cannot be set while the hold is open.</summary>
    static void EnsureHoldBlocksBank(DbConnection c,DbTransaction? tx)
    {
        string rider=SchemaSql.Q(c,"sal","pay_run_rider"),hold=SchemaSql.Q(c,"sal","shared_rider_hold");
        const string message="Rider with an open shared-rider hold cannot be bank eligible";
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER sal.TR_rider_bank_hold ON {rider} AFTER INSERT,UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted r JOIN {hold} h ON h.erp_rider_id=r.erp_rider_id AND h.period_month=r.period_month AND h.status='open' WHERE r.bank_eligible=1)
                    THROW 51021,'{message}',1;
                END;
                """);
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER sal.TR_hold_clears_bank ON {hold} AFTER INSERT,UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  UPDATE r SET bank_eligible=0 FROM {rider} r
                    JOIN inserted h ON h.erp_rider_id=r.erp_rider_id AND h.period_month=r.period_month
                    WHERE h.status='open' AND r.bank_eligible=1;
                END;
                """);
        }
        else
            foreach(bool insert in new[]{true,false})
            {
                string op=insert?"INSERT":"UPDATE",suffix=insert?"insert":"update";
                SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_rider_bank_hold_{suffix} BEFORE {op} ON {rider} WHEN NEW.bank_eligible=1 BEGIN SELECT RAISE(ABORT,'{message}') WHERE EXISTS(SELECT 1 FROM {hold} WHERE erp_rider_id=NEW.erp_rider_id AND period_month=NEW.period_month AND status='open'); END;");
                SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_hold_clears_bank_{suffix} AFTER {(insert?"INSERT":"UPDATE OF status,erp_rider_id,period_month")} ON {hold} WHEN NEW.status='open' BEGIN UPDATE {rider} SET bank_eligible=0 WHERE erp_rider_id=NEW.erp_rider_id AND period_month=NEW.period_month AND bank_eligible=1; END;");
            }
    }

    /// <summary>H2: a claim on a rider payable follows its run. Simulation runs never reserve or pay;
    /// payout-blocked runs (e.g. the Ninja parallel run) may reserve for the bank-file comparison but never post.
    /// Checked only when a claim enters status 1 or 2, so releases and reversals stay possible.</summary>
    static void EnsureClaimRunFlags(DbConnection c,DbTransaction? tx)
    {
        string p=SchemaSql.Q(c,"pay","payable"),a=SchemaSql.Q(c,"pay","payment_allocation"),rider=SchemaSql.Q(c,"sal","pay_run_rider"),run=SchemaSql.Q(c,"sal","pay_run");
        const string message="Payable of a simulation or payout-blocked run cannot be claimed or paid";
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER pay.TR_allocation_run_flags ON {a} AFTER INSERT,UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.id=i.id
                    JOIN {p} p ON p.id=i.payable_id
                    JOIN {rider} r ON r.payable_id=i.payable_id OR p.source_id=r.id JOIN {run} pr ON pr.id=r.pay_run_id
                    WHERE i.claim_status IN (1,2) AND (d.id IS NULL OR d.claim_status<>i.claim_status)
                      AND (pr.run_mode=1 OR (i.claim_status=2 AND pr.payout_blocked=1)))
                    THROW 51018,'{message}',1;
                END;
                """);
        else
            foreach(bool insert in new[]{true,false})
                SchemaSql.SqliteTrigger(c,tx,$"""
                    CREATE TRIGGER TR_allocation_run_flags_{(insert?"insert":"update")} BEFORE {(insert?"INSERT":"UPDATE OF claim_status")} ON {a}
                    WHEN NEW.claim_status IN (1,2){(insert?"":" AND NEW.claim_status<>OLD.claim_status")} BEGIN
                      SELECT RAISE(ABORT,'{message}') WHERE EXISTS(SELECT 1 FROM {p} p JOIN {rider} r ON r.payable_id=p.id OR p.source_id=r.id JOIN {run} pr ON pr.id=r.pay_run_id
                        WHERE p.id=NEW.payable_id AND (pr.run_mode=1 OR (NEW.claim_status=2 AND pr.payout_blocked=1)));
                    END;
                    """);
    }
}
