using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-13a: canonical entitlement. Design §0, §2.1, §13–14, §16. Empty at startup; no legacy reader changes.</summary>
public static class SchemaPayPayable
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c);
        string available=SchemaSql.IsSql(c)?"AS (amount_due-amount_reserved-amount_paid) PERSISTED":"REAL GENERATED ALWAYS AS (round(amount_due-amount_reserved-amount_paid,2)) STORED";
        SchemaSql.EnsureTable(c,tx,"pay","payable",$"""
            id {g} NOT NULL,
            kind {SchemaSql.Varchar(c,40)} NOT NULL,
            source_table {SchemaSql.Varchar(c,60)} NOT NULL,
            source_id {g} NOT NULL,
            company_id {g} NOT NULL,
            erp_rider_id {g} NULL,
            amount_due {m} NOT NULL,
            amount_reserved {m} NOT NULL DEFAULT 0,
            amount_paid {m} NOT NULL DEFAULT 0,
            available_balance {available},
            currency_code {SchemaSql.Varchar(c,3)} NOT NULL DEFAULT 'SAR',
            status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1,
            due_on {SchemaSql.Date(c)} NULL,
            period_month {SchemaSql.Date(c)} NOT NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_pay_payable {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_payable_source UNIQUE (source_table,source_id),
            CONSTRAINT UQ_payable_scope UNIQUE (id,company_id,period_month),
            CONSTRAINT UQ_payable_company UNIQUE (id,company_id),
            CONSTRAINT CK_payable_nonnegative CHECK (amount_due>=0 AND amount_reserved>=0 AND amount_paid>=0),
            CONSTRAINT CK_payable_amounts CHECK (amount_reserved+amount_paid<=amount_due),
            CONSTRAINT CK_payable_currency CHECK (currency_code='SAR'),
            CONSTRAINT CK_payable_status CHECK (status IN (1,2,3,4)),
            CONSTRAINT CK_payable_month CHECK ({SchemaSql.MonthCheck(c)}),
            CONSTRAINT FK_payable_company FOREIGN KEY (company_id) REFERENCES {SchemaSql.Q(c,"hr","company")}(id) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureMonthIndex(c,tx,"pay","payable");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","payable","IX_payable_company","company_id");
        SchemaSql.EnsureRowVersion(c,tx,"pay","payable");
        EnsureCounters(c,tx);
    }
    internal static void EnsureCounters(DbConnection c,DbTransaction? tx)
    {
        string p=SchemaSql.Q(c,"pay","payable"),a=SchemaSql.Q(c,"pay","payment_allocation");
        bool claims=SchemaSql.TableExists(c,tx,"pay","payment_allocation");
        string Sum(string alias,int status)=>claims?$"(SELECT COALESCE(SUM(amount),0) FROM {a} WHERE payable_id={alias}.id AND claim_status={status})":"0";
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER pay.TR_payable_claim_totals ON {p} AFTER INSERT,UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted p WHERE p.amount_reserved<>{Sum("p",1)} OR p.amount_paid<>{Sum("p",2)})
                    THROW 51003,'Payable counters must match allocation claims',1;
                END;
                """);
        else
        {
            SchemaSql.Exec(c,tx,"DROP TRIGGER IF EXISTS TR_payable_claim_totals;");
            foreach(string op in new[]{"INSERT","UPDATE"})
            {
                SchemaSql.Exec(c,tx,$"DROP TRIGGER IF EXISTS TR_payable_claim_totals_{op};");
                SchemaSql.Exec(c,tx,$"""
                    CREATE TRIGGER TR_payable_claim_totals_{op} AFTER {op} ON {p} BEGIN
                      SELECT RAISE(ABORT,'Payable counters must match allocation claims') WHERE
                        NEW.amount_reserved<>round({Sum("NEW",1)},2) OR NEW.amount_paid<>round({Sum("NEW",2)},2);
                    END;
                    """);
            }
        }
    }

}
