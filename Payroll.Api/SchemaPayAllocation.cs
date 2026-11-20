using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-13b. Design §2.1 / §14 / §16. Triggers own reserved/paid counters atomically;
/// Ghazal answer 11: payer_company_id is the paying bank/company chosen at bank-file issue. A payment may settle
/// payables of other companies (per registration or one combined sheet); a claim keeps its payable's company,
/// so claim.company_id &lt;&gt; payment.payer_company_id marks an inter-company settlement.
/// future payment writers must not also increment them. No endpoints, backfill or reader switch.</summary>
public static class SchemaPayAllocation
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c),payable=SchemaSql.Q(c,"pay","payable");
        SchemaSql.EnsureTable(c,tx,"pay","payment_request",$"""
            id {g} NOT NULL, company_id {g} NOT NULL, payable_id {g} NOT NULL,
            payable_period_month {SchemaSql.Date(c)} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            amount {m} NOT NULL, status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1,
            run_mode {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0, payout_blocked {SchemaSql.Bit(c)} NOT NULL DEFAULT 0,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_pay_payment_request {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_payment_request_scope UNIQUE (id,company_id,period_month),
            CONSTRAINT CK_payment_request_amount CHECK (amount>0),
            CONSTRAINT CK_payment_request_status CHECK (status IN (1,2,4)),
            CONSTRAINT CK_payment_request_flags CHECK (run_mode IN (0,1) AND payout_blocked IN (0,1)),
            CONSTRAINT CK_payment_request_month CHECK ({SchemaSql.MonthCheck(c)}),
            CONSTRAINT FK_payment_request_payable FOREIGN KEY (payable_id,company_id,payable_period_month) REFERENCES {payable}(id,company_id,period_month) ON DELETE NO ACTION
            """);
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"pay","payment_request","UQ_payment_request_open","payable_id","status=1");
        string dateInMonth=SchemaSql.IsSql(c)?"paid_on>=period_month AND paid_on<DATEADD(month,1,period_month)":"date(paid_on) IS NOT NULL AND paid_on=date(paid_on) AND paid_on>=period_month AND paid_on<date(period_month,'+1 month')";
        SchemaSql.EnsureTable(c,tx,"pay","payment",$"""
            id {g} NOT NULL, payment_no {SchemaSql.Varchar(c,40)} NOT NULL,
            company_id {g} NOT NULL, payer_company_id {g} NOT NULL,
            request_id {g} NULL, request_period_month {SchemaSql.Date(c)} NULL,
            period_month {SchemaSql.Date(c)} NOT NULL, paid_on {SchemaSql.Date(c)} NOT NULL,
            amount {m} NOT NULL, currency_code {SchemaSql.Varchar(c,3)} NOT NULL DEFAULT 'SAR',
            status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1,
            run_mode {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0, payout_blocked {SchemaSql.Bit(c)} NOT NULL DEFAULT 0,
            journal_id {g} NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_pay_payment {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_payment_no UNIQUE (payment_no),
            CONSTRAINT UQ_payment_scope UNIQUE (id,company_id,period_month),
            CONSTRAINT UQ_payment_month UNIQUE (id,period_month),
            CONSTRAINT CK_payment_amount CHECK (amount>0),
            CONSTRAINT CK_payment_currency CHECK (currency_code='SAR'),
            CONSTRAINT CK_payment_status CHECK (status IN (1,2,3,4)),
            CONSTRAINT CK_payment_flags CHECK (run_mode IN (0,1) AND payout_blocked IN (0,1)),
            CONSTRAINT CK_payment_month CHECK ({SchemaSql.MonthCheck(c)} AND {dateInMonth}),
            CONSTRAINT CK_payment_request_pair CHECK ((request_id IS NULL AND request_period_month IS NULL) OR (request_id IS NOT NULL AND request_period_month IS NOT NULL)),
            CONSTRAINT FK_payment_company FOREIGN KEY (company_id) REFERENCES {SchemaSql.Q(c,"hr","company")}(id) ON DELETE NO ACTION,
            CONSTRAINT FK_payment_payer FOREIGN KEY (payer_company_id) REFERENCES {SchemaSql.Q(c,"hr","company")}(id) ON DELETE NO ACTION,
            CONSTRAINT FK_payment_request FOREIGN KEY (request_id,company_id,request_period_month) REFERENCES {SchemaSql.Q(c,"pay","payment_request")}(id,company_id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_payment_journal FOREIGN KEY (journal_id) REFERENCES {SchemaSql.Q(c,"fin","journal")}(id) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureTable(c,tx,"pay","payment_allocation",$"""
            id {g} NOT NULL, payment_id {g} NOT NULL, payment_line_id {g} NOT NULL,
            payable_id {g} NOT NULL, company_id {g} NOT NULL,
            payable_period_month {SchemaSql.Date(c)} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            amount {m} NOT NULL, currency_code {SchemaSql.Varchar(c,3)} NOT NULL DEFAULT 'SAR',
            claim_status {SchemaSql.Tiny(c)} NOT NULL,
            reserved_at {SchemaSql.DateTime(c)} NOT NULL DEFAULT ({SchemaSql.NowLiteral(c)}),
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_pay_payment_allocation {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_pay_alloc_payable_line UNIQUE (payable_id,payment_line_id),
            CONSTRAINT CK_pay_alloc_amount CHECK (amount>0),
            CONSTRAINT CK_pay_alloc_currency CHECK (currency_code='SAR'),
            CONSTRAINT CK_pay_alloc_status CHECK (claim_status IN (1,2,3,4)),
            CONSTRAINT CK_pay_alloc_month CHECK ({SchemaSql.MonthCheck(c)}),
            CONSTRAINT FK_pay_alloc_payable FOREIGN KEY (payable_id,company_id,payable_period_month) REFERENCES {payable}(id,company_id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_pay_alloc_payment FOREIGN KEY (payment_id,period_month) REFERENCES {SchemaSql.Q(c,"pay","payment")}(id,period_month) ON DELETE NO ACTION
            """);
        foreach(string table in new[]{"payment_request","payment","payment_allocation"})
        {
            SchemaSql.EnsureMonthIndex(c,tx,"pay",table);SchemaSql.EnsureRowVersion(c,tx,"pay",table);
            SchemaSql.CreateIndexIfMissing(c,tx,"pay",table,$"IX_{table}_company","company_id");
        }
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","payment_request","IX_payment_request_payable","payable_id,company_id,payable_period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","payment","IX_payment_request","request_id,company_id,request_period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","payment","IX_payment_journal","journal_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","payment","IX_payment_payer","payer_company_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","payment_allocation","IX_pay_alloc_payment","payment_id,period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","payment_allocation","IX_pay_alloc_payable","payable_id,company_id,payable_period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","payment_allocation","IX_pay_alloc_line","payment_line_id");
        SchemaMoneyFlags.Ensure(c,tx,"pay","payment",o=>$"{o}.status IN (2,3) OR {o}.payout_blocked=1 OR {o}.run_mode=1");
        SchemaMoneyGuard.Retain(c,tx,"pay","payment","o.payment_no=NEW.payment_no");
        SchemaMoneyGuard.Retain(c,tx,"pay","payment_request","o.payable_id=NEW.payable_id AND o.status=1 AND NEW.status=1");
        SchemaPaymentState.Ensure(c,tx);
        EnsureRequestFlags(c,tx);
        EnsureTransitions(c,tx);
        EnsureClaims(c,tx);
        SchemaPayPayable.EnsureCounters(c,tx);
    }

    /// <summary>A payment linked to a request must carry the same run_mode and payout_blocked flags.</summary>
    static void EnsureRequestFlags(DbConnection c,DbTransaction? tx)
    {
        string payment=SchemaSql.Q(c,"pay","payment"),request=SchemaSql.Q(c,"pay","payment_request");
        SchemaMoneyGuard.Rule(c,tx,"pay","payment","TR_payment_request_flags",
            n=>$"{n}.request_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM {request} r WHERE r.id={n}.request_id AND r.company_id={n}.company_id AND r.period_month={n}.request_period_month AND r.run_mode={n}.run_mode AND r.payout_blocked={n}.payout_blocked)",
            (o,n)=>$"{n}.request_id IS NOT NULL AND ({SchemaMoneyGuard.Changed(c,n,o,"request_id","request_period_month","run_mode","payout_blocked")}) AND NOT EXISTS(SELECT 1 FROM {request} r WHERE r.id={n}.request_id AND r.company_id={n}.company_id AND r.period_month={n}.request_period_month AND r.run_mode={n}.run_mode AND r.payout_blocked={n}.payout_blocked)");
    }

    /// <summary>Design §2.1 writer contract. Call inside the claim transaction BEFORE inserting or changing claims on
    /// this payable. SQL Server takes UPDLOCK,HOLDLOCK on the payable row, so two writers queue instead of both inserting
    /// and then deadlocking inside the claim trigger; SQLite writers are already serialized. Lock several payables in
    /// ascending id order. Returns the balance still available.
    /// Retry rule: on SQL Server any error inside a trigger (51xxx) or a deadlock dooms the whole transaction, while SQLite
    /// aborts only the failed statement. So a writer always rolls back the whole batch, and retries the whole batch
    /// (never one statement) only when <see cref="IsRetryable"/> says so.</summary>
    public static decimal LockPayable(DbConnection c,DbTransaction tx,string payableId)
    {
        string hint=SchemaSql.IsSql(c)?" WITH (UPDLOCK,HOLDLOCK)":"";
        object? available=SchemaSql.Scalar(c,tx,$"SELECT available_balance FROM {SchemaSql.Q(c,"pay","payable")}{hint} WHERE id=@id",("@id",payableId));
        return available is null or DBNull?throw new InvalidOperationException("Payable not found."):Convert.ToDecimal(available);
    }
    /// <summary>Deadlock victim (1205) or lock timeout (1222) on SQL Server; busy (5) or locked (6) on SQLite.
    /// Business-rule errors from the money triggers are never retryable.</summary>
    public static bool IsRetryable(DbException ex)=>
        ex is Microsoft.Data.SqlClient.SqlException sql?sql.Number is 1205 or 1222:
        ex is Microsoft.Data.Sqlite.SqliteException lite&&lite.SqliteErrorCode is 5 or 6;

    static void EnsureTransitions(DbConnection c,DbTransaction? tx)
    {
        string a=SchemaSql.Q(c,"pay","payment_allocation"),payment=SchemaSql.Q(c,"pay","payment");
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER pay.TR_allocation_transition ON {a} AFTER UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted o LEFT JOIN inserted n ON n.id=o.id WHERE n.id IS NULL
                    OR n.payment_id<>o.payment_id OR n.payable_id<>o.payable_id OR n.payment_line_id<>o.payment_line_id
                    OR n.amount<>o.amount OR n.company_id<>o.company_id OR n.period_month<>o.period_month
                    OR n.payable_period_month<>o.payable_period_month OR n.currency_code<>o.currency_code)
                    THROW 51004,'Allocation keys and amount are immutable',1;
                  IF EXISTS(SELECT 1 FROM inserted n JOIN deleted o ON o.id=n.id
                    WHERE n.claim_status<>o.claim_status AND NOT
                    ((o.claim_status=1 AND n.claim_status IN (2,3,4)) OR
                     (o.claim_status=2 AND n.claim_status=4 AND EXISTS(SELECT 1 FROM {payment} p WHERE p.id=n.payment_id AND p.status=3))))
                    THROW 51005,'Invalid allocation status transition; posted claims require reversed payment',1;
                END;
                """);
        else
            SchemaSql.SqliteTrigger(c,tx,$"""
                CREATE TRIGGER TR_allocation_transition BEFORE UPDATE ON {a} BEGIN
                  SELECT RAISE(ABORT,'Allocation keys and amount are immutable') WHERE
                    NEW.id<>OLD.id OR NEW.payment_id<>OLD.payment_id OR NEW.payable_id<>OLD.payable_id OR NEW.payment_line_id<>OLD.payment_line_id
                    OR NEW.amount<>OLD.amount OR NEW.company_id<>OLD.company_id OR NEW.period_month<>OLD.period_month
                    OR NEW.payable_period_month<>OLD.payable_period_month OR NEW.currency_code<>OLD.currency_code;
                  SELECT RAISE(ABORT,'Invalid allocation status transition; posted claims require reversed payment') WHERE
                    NEW.claim_status<>OLD.claim_status AND NOT
                    ((OLD.claim_status=1 AND NEW.claim_status IN (2,3,4)) OR
                     (OLD.claim_status=2 AND NEW.claim_status=4 AND EXISTS(SELECT 1 FROM {payment} p WHERE p.id=NEW.payment_id AND p.status=3)));
                END;
                """);
    }

    /// <summary>Active (reserved or posted) claims on one payment, rounded like the stored money values.</summary>
    static string ClaimedOn(DbConnection c,string paymentId)=>
        $"round(COALESCE((SELECT SUM(amount) FROM {SchemaSql.Q(c,"pay","payment_allocation")} WHERE payment_id={paymentId} AND claim_status IN (1,2)),0),2)";

    static void EnsureClaims(DbConnection c,DbTransaction? tx)
    {
        string a=SchemaSql.Q(c,"pay","payment_allocation"),p=SchemaSql.Q(c,"pay","payable"),payment=SchemaSql.Q(c,"pay","payment");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER pay.TR_allocation_totals ON {a} AFTER INSERT,UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.id=i.id JOIN {payment} p ON p.id=i.payment_id
                    WHERE i.claim_status IN (1,2) AND (d.id IS NULL OR d.claim_status<>i.claim_status)
                      AND (p.run_mode=1 OR (i.claim_status=2 AND p.payout_blocked=1)))
                    THROW 51017,'Simulation or payout-blocked payment cannot claim',1;
                  DECLARE @locked uniqueidentifier;
                  SELECT @locked=p.id FROM {p} p WITH (UPDLOCK,HOLDLOCK)
                    WHERE p.id IN (SELECT payable_id FROM inserted UNION SELECT payable_id FROM deleted);
                  IF EXISTS(SELECT 1 FROM inserted i JOIN {p} p ON p.id=i.payable_id WHERE i.claim_status IN (1,2) AND p.status=4)
                    THROW 51001,'Cancelled payable cannot receive claims',1;
                  UPDATE p SET amount_reserved=s.reserved,amount_paid=s.paid,
                    status=CASE WHEN p.status=4 THEN 4 WHEN s.paid=p.amount_due AND s.paid>0 THEN 3 WHEN s.reserved+s.paid>0 THEN 2 ELSE 1 END
                  FROM {p} p CROSS APPLY (
                    SELECT COALESCE(SUM(CASE WHEN a.claim_status=1 THEN a.amount ELSE 0 END),0) reserved,
                           COALESCE(SUM(CASE WHEN a.claim_status=2 THEN a.amount ELSE 0 END),0) paid
                    FROM {a} a WHERE a.payable_id=p.id) s
                  WHERE p.id IN (SELECT payable_id FROM inserted UNION SELECT payable_id FROM deleted);
                  SELECT @locked=p.id FROM {payment} p WITH (UPDLOCK,HOLDLOCK) WHERE p.id IN (SELECT payment_id FROM inserted);
                  IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.id=i.id JOIN {payment} p ON p.id=i.payment_id
                    WHERE i.claim_status IN (1,2) AND (d.id IS NULL OR d.claim_status<>i.claim_status) AND p.status IN (3,4))
                    THROW 51019,'Reversed or cancelled payment cannot receive claims',1;
                  IF EXISTS(SELECT 1 FROM {payment} p WHERE p.id IN (SELECT payment_id FROM inserted)
                    AND p.amount<(SELECT COALESCE(SUM(x.amount),0) FROM {a} x WHERE x.payment_id=p.id AND x.claim_status IN (1,2)))
                    THROW 51020,'Claims exceed payment amount',1;
                END;
                """);
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER pay.TR_payment_claim_cover ON {payment} AFTER UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted p
                    WHERE p.amount<(SELECT COALESCE(SUM(x.amount),0) FROM {a} x WHERE x.payment_id=p.id AND x.claim_status IN (1,2)))
                    THROW 51020,'Claims exceed payment amount',1;
                END;
                """);
            SchemaSql.Exec(c,tx,$"CREATE OR ALTER TRIGGER pay.TR_allocation_no_delete ON {a} INSTEAD OF DELETE AS BEGIN THROW 51002,'Cancel or release allocations instead of deleting',1; END;");
        }
        else
        {
            foreach(string operation in new[]{"INSERT","UPDATE"})
            {
                bool insert=operation=="INSERT";string targets=insert?"NEW.payable_id":"NEW.payable_id,OLD.payable_id";
                string fresh=insert?"NEW.claim_status IN (1,2)":"NEW.claim_status IN (1,2) AND NEW.claim_status<>OLD.claim_status";
                SchemaSql.SqliteTrigger(c,tx,$"""
                    CREATE TRIGGER TR_allocation_{(insert?"insert":"update")} AFTER {operation} ON {a} BEGIN
                      SELECT RAISE(ABORT,'Simulation or payout-blocked payment cannot claim') WHERE {fresh} AND EXISTS(SELECT 1 FROM {payment} WHERE id=NEW.payment_id AND (run_mode=1 OR (NEW.claim_status=2 AND payout_blocked=1)));
                      SELECT RAISE(ABORT,'Cancelled payable cannot receive claims') WHERE NEW.claim_status IN (1,2) AND (SELECT status FROM {p} WHERE id=NEW.payable_id)=4;
                      UPDATE {p} SET
                        amount_reserved=round(COALESCE((SELECT SUM(amount) FROM {a} WHERE payable_id={p}.id AND claim_status=1),0),2),
                        amount_paid=round(COALESCE((SELECT SUM(amount) FROM {a} WHERE payable_id={p}.id AND claim_status=2),0),2)
                        WHERE id IN ({targets});
                      UPDATE {p} SET status=CASE WHEN status=4 THEN 4 WHEN amount_paid=amount_due AND amount_paid>0 THEN 3 WHEN amount_reserved+amount_paid>0 THEN 2 ELSE 1 END WHERE id IN ({targets});
                      SELECT RAISE(ABORT,'Reversed or cancelled payment cannot receive claims') WHERE {fresh} AND (SELECT status FROM {payment} WHERE id=NEW.payment_id) IN (3,4);
                      SELECT RAISE(ABORT,'Claims exceed payment amount') WHERE {ClaimedOn(c,"NEW.payment_id")}>(SELECT amount FROM {payment} WHERE id=NEW.payment_id);
                    END;
                    """);
            }
            SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_payment_claim_cover AFTER UPDATE OF amount ON {payment} BEGIN SELECT RAISE(ABORT,'Claims exceed payment amount') WHERE {ClaimedOn(c,"NEW.id")}>NEW.amount; END;");
            // L4: INSERT OR REPLACE deletes the old claim without firing delete triggers (recursive_triggers is off),
            // which would bypass the no-delete and transition rules. Reject a reused id or claim key before the delete.
            SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_allocation_no_replace BEFORE INSERT ON {a} WHEN EXISTS(SELECT 1 FROM {a} WHERE id=NEW.id OR (payable_id=NEW.payable_id AND payment_line_id=NEW.payment_line_id)) BEGIN SELECT RAISE(ABORT,'UQ_pay_alloc_payable_line: claim id or key already used; replacing a claim is forbidden'); END;");
            SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_allocation_no_delete BEFORE DELETE ON {a} BEGIN SELECT RAISE(ABORT,'Cancel or release allocations instead of deleting'); END;");
        }
    }
}
