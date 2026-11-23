using System.Data.Common;
namespace Payroll;

/// <summary>U-W2-07c / M-0727: optional recurring payees (مدفوعات دورية) outside payroll.</summary>
public static class SchemaRecurringPayees
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c),company=SchemaSql.Q(c,"hr","company"),json=SchemaSql.IsSql(c)?"nvarchar(max)":"TEXT";
        SchemaSql.EnsureTable(c,tx,"pay","recurring_payee",$"""
            id {g} NOT NULL {SchemaSql.PrimaryKey(c)}, company_id {g} NOT NULL REFERENCES {company}(id),
            name_ar {SchemaSql.Text(c,200)} NOT NULL,
            payee_type {SchemaSql.Varchar(c,40)} NOT NULL CHECK(payee_type IN ('trial_outside_sponsorship','egypt_office','other')),
            amount {m} NOT NULL CHECK(amount>0),
            currency_code {SchemaSql.Varchar(c,3)} NOT NULL,
            payment_method {SchemaSql.Varchar(c,40)} NOT NULL,
            bank_details {SchemaSql.Text(c,500)} NOT NULL DEFAULT '',
            cost_centre_id {g} NOT NULL REFERENCES {SchemaSql.Q(c,"org","cost_centre")}(id),
            erp_person_id {g} NULL,
            active_from {SchemaSql.Date(c)} NOT NULL, active_to {SchemaSql.Date(c)} NULL,
            notes {SchemaSql.Text(c,500)} NOT NULL DEFAULT '',
            status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1 CHECK(status IN (1,2)),
            {SchemaSql.MoneyAuditCols(c)}
            """);
        SchemaMoneyGuard.Retain(c,tx,"pay","recurring_payee");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","recurring_payee","IX_recurring_payee_company","company_id,status");
        SchemaSql.EnsureTable(c,tx,"pay","recurring_run",$"""
            id {g} NOT NULL {SchemaSql.PrimaryKey(c)}, company_id {g} NOT NULL REFERENCES {company}(id),
            period_month {SchemaSql.Date(c)} NOT NULL,
            status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1 CHECK(status IN (1,2,3,4,5)),
            content_hash {SchemaSql.Varchar(c,64)} NOT NULL,
            submitted_by {SchemaSql.Text(c,128)} NULL, approved_by {SchemaSql.Text(c,128)} NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT UQ_recurring_run_period UNIQUE(company_id,period_month),
            CONSTRAINT CK_recurring_run_month CHECK({SchemaSql.MonthCheck(c)})
            """);
        SchemaMoneyGuard.Retain(c,tx,"pay","recurring_run","o.company_id=NEW.company_id AND o.period_month=NEW.period_month");
        SchemaSql.EnsureMonthIndex(c,tx,"pay","recurring_run");
        SchemaSql.EnsureTable(c,tx,"pay","recurring_run_line",$"""
            id {g} NOT NULL {SchemaSql.PrimaryKey(c)}, run_id {g} NOT NULL REFERENCES {SchemaSql.Q(c,"pay","recurring_run")}(id),
            payee_id {g} NULL REFERENCES {SchemaSql.Q(c,"pay","recurring_payee")}(id),
            name_ar {SchemaSql.Text(c,200)} NOT NULL,
            amount {m} NOT NULL CHECK(amount>0),
            currency_code {SchemaSql.Varchar(c,3)} NOT NULL,
            exchange_rate {(SchemaSql.IsSql(c)?"decimal(18,6)":"REAL")} NOT NULL DEFAULT 0 CHECK(exchange_rate>=0),
            transfer_fee {m} NOT NULL DEFAULT 0 CHECK(transfer_fee>=0),
            fx_difference {m} NOT NULL DEFAULT 0,
            sar_amount {m} NOT NULL DEFAULT 0 CHECK(sar_amount>=0),
            payment_method {SchemaSql.Varchar(c,40)} NOT NULL,
            bank_details {SchemaSql.Text(c,500)} NOT NULL DEFAULT '',
            cost_centre_id {g} NOT NULL REFERENCES {SchemaSql.Q(c,"org","cost_centre")}(id),
            skipped {SchemaSql.Bit(c)} NOT NULL DEFAULT 0 CHECK(skipped IN (0,1)),
            notes {SchemaSql.Text(c,500)} NOT NULL DEFAULT '',
            document_id {g} NULL REFERENCES {SchemaSql.Q(c,"pay","document")}(id),
            payment_id {g} NULL REFERENCES {SchemaSql.Q(c,"pay","payment")}(id),
            {SchemaSql.MoneyAuditCols(c)}
            """);
        SchemaMoneyGuard.Retain(c,tx,"pay","recurring_run_line");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","recurring_run_line","IX_recurring_run_line_run","run_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","recurring_run_line","IX_recurring_run_line_payee","payee_id");
        _=json;
    }
}
