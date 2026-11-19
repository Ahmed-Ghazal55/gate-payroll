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
    }
}
