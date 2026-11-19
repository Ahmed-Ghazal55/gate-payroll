using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-13b. Design §2.1 / §14 / §16. Triggers own reserved/paid counters atomically;
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
            company_id {g} NOT NULL, request_id {g} NULL, request_period_month {SchemaSql.Date(c)} NULL,
            period_month {SchemaSql.Date(c)} NOT NULL, paid_on {SchemaSql.Date(c)} NOT NULL,
            amount {m} NOT NULL, currency_code {SchemaSql.Varchar(c,3)} NOT NULL DEFAULT 'SAR',
            status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1,
            run_mode {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0, payout_blocked {SchemaSql.Bit(c)} NOT NULL DEFAULT 0,
            journal_id {g} NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_pay_payment {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_payment_no UNIQUE (payment_no),
            CONSTRAINT UQ_payment_scope UNIQUE (id,company_id,period_month),
            CONSTRAINT CK_payment_amount CHECK (amount>=0),
            CONSTRAINT CK_payment_currency CHECK (currency_code='SAR'),
            CONSTRAINT CK_payment_status CHECK (status IN (1,2,3,4)),
            CONSTRAINT CK_payment_flags CHECK (run_mode IN (0,1) AND payout_blocked IN (0,1)),
            CONSTRAINT CK_payment_month CHECK ({SchemaSql.MonthCheck(c)} AND {dateInMonth}),
            CONSTRAINT CK_payment_request_pair CHECK ((request_id IS NULL AND request_period_month IS NULL) OR (request_id IS NOT NULL AND request_period_month IS NOT NULL)),
            CONSTRAINT FK_payment_company FOREIGN KEY (company_id) REFERENCES {SchemaSql.Q(c,"hr","company")}(id) ON DELETE NO ACTION,
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
            CONSTRAINT FK_pay_alloc_payment FOREIGN KEY (payment_id,company_id,period_month) REFERENCES {SchemaSql.Q(c,"pay","payment")}(id,company_id,period_month) ON DELETE NO ACTION
            """);
        foreach(string table in new[]{"payment_request","payment","payment_allocation"})
        {
            SchemaSql.EnsureMonthIndex(c,tx,"pay",table);SchemaSql.EnsureRowVersion(c,tx,"pay",table);
            SchemaSql.CreateIndexIfMissing(c,tx,"pay",table,$"IX_{table}_company","company_id");
        }
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","payment_request","IX_payment_request_payable","payable_id,company_id,payable_period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","payment","IX_payment_request","request_id,company_id,request_period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","payment","IX_payment_journal","journal_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","payment_allocation","IX_pay_alloc_payment","payment_id,company_id,period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","payment_allocation","IX_pay_alloc_payable","payable_id,company_id,payable_period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","payment_allocation","IX_pay_alloc_line","payment_line_id");
        EnsureTransitions(c,tx);
        EnsureClaims(c,tx);
    }

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
            SchemaSql.Exec(c,tx,$"""
                CREATE TRIGGER IF NOT EXISTS TR_allocation_transition BEFORE UPDATE ON {a} BEGIN
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

    static void EnsureClaims(DbConnection c,DbTransaction? tx)
    {
        string a=SchemaSql.Q(c,"pay","payment_allocation"),p=SchemaSql.Q(c,"pay","payable");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER pay.TR_allocation_totals ON {a} AFTER INSERT,UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
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
                END;
                """);
            SchemaSql.Exec(c,tx,$"CREATE OR ALTER TRIGGER pay.TR_allocation_no_delete ON {a} INSTEAD OF DELETE AS BEGIN THROW 51002,'Cancel or release allocations instead of deleting',1; END;");
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER pay.TR_payable_claim_totals ON {p} AFTER UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted p WHERE EXISTS(SELECT 1 FROM {a} WHERE payable_id=p.id)
                    AND (p.amount_reserved<>(SELECT COALESCE(SUM(amount),0) FROM {a} WHERE payable_id=p.id AND claim_status=1)
                      OR p.amount_paid<>(SELECT COALESCE(SUM(amount),0) FROM {a} WHERE payable_id=p.id AND claim_status=2)))
                    THROW 51003,'Payable counters must match allocation claims',1;
                END;
                """);
        }
        else
        {
            foreach(string operation in new[]{"INSERT","UPDATE OF payable_id,amount,claim_status"})
            {
                bool insert=operation=="INSERT";string targets=insert?"NEW.payable_id":"NEW.payable_id,OLD.payable_id";
                SchemaSql.Exec(c,tx,$"""
                    CREATE TRIGGER IF NOT EXISTS TR_allocation_{(insert?"insert":"update")} AFTER {operation} ON {a} BEGIN
                      SELECT RAISE(ABORT,'Cancelled payable cannot receive claims') WHERE NEW.claim_status IN (1,2) AND (SELECT status FROM {p} WHERE id=NEW.payable_id)=4;
                      UPDATE {p} SET
                        amount_reserved=round(COALESCE((SELECT SUM(amount) FROM {a} WHERE payable_id={p}.id AND claim_status=1),0),2),
                        amount_paid=round(COALESCE((SELECT SUM(amount) FROM {a} WHERE payable_id={p}.id AND claim_status=2),0),2)
                        WHERE id IN ({targets});
                      UPDATE {p} SET status=CASE WHEN status=4 THEN 4 WHEN amount_paid=amount_due AND amount_paid>0 THEN 3 WHEN amount_reserved+amount_paid>0 THEN 2 ELSE 1 END WHERE id IN ({targets});
                    END;
                    """);
            }
            SchemaSql.Exec(c,tx,$"CREATE TRIGGER IF NOT EXISTS TR_allocation_no_delete BEFORE DELETE ON {a} BEGIN SELECT RAISE(ABORT,'Cancel or release allocations instead of deleting'); END;");
            SchemaSql.Exec(c,tx,$"""
                CREATE TRIGGER IF NOT EXISTS TR_payable_claim_totals AFTER UPDATE ON {p}
                WHEN EXISTS(SELECT 1 FROM {a} WHERE payable_id=NEW.id) BEGIN
                  SELECT RAISE(ABORT,'Payable counters must match allocation claims') WHERE
                    NEW.amount_reserved<>round(COALESCE((SELECT SUM(amount) FROM {a} WHERE payable_id=NEW.id AND claim_status=1),0),2)
                    OR NEW.amount_paid<>round(COALESCE((SELECT SUM(amount) FROM {a} WHERE payable_id=NEW.id AND claim_status=2),0),2);
                END;
                """);
        }
    }
}
