using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-14a. Design §4.1–2 (E8), §14, §16. Invoice approval covers totals/company items;
/// rider bearer assignments belong to the later distribution stage. No ingestion or posting runs here.</summary>
public static class SchemaSalInvoice
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c),invoice=SchemaSql.Q(c,"sal","platform_invoice"),line=SchemaSql.Q(c,"sal","platform_invoice_line");
        SchemaSql.EnsureTable(c,tx,"sal","platform_invoice",$"""
            id {g} NOT NULL, app_code {SchemaSql.Varchar(c,20)} NOT NULL,
            company_id {g} NOT NULL, erp_cr_id {g} NOT NULL, city_code {SchemaSql.Varchar(c,40)} NOT NULL,
            period_from {SchemaSql.Date(c)} NOT NULL, period_to {SchemaSql.Date(c)} NOT NULL,
            period_month {SchemaSql.Date(c)} NOT NULL, file_hash {SchemaSql.Binary(c,32)} NOT NULL,
            gross {m} NOT NULL, deductions {m} NOT NULL, net {m} NOT NULL,
            status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1,
            uploaded_by {g} NULL, uploaded_at {SchemaSql.DateTime(c)} NULL,
            approved_by {g} NULL, approved_at {SchemaSql.DateTime(c)} NULL, journal_id {g} NULL,
            run_mode {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0, payout_blocked {SchemaSql.Bit(c)} NOT NULL DEFAULT 0,
            posting_owner {SchemaSql.Varchar(c,20)} NOT NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_platform_invoice {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_invoice_scope UNIQUE (id,company_id,period_month),
            CONSTRAINT UQ_sal_invoice_month UNIQUE (id,period_month),
            CONSTRAINT UQ_sal_invoice_hash UNIQUE (app_code,file_hash),
            CONSTRAINT CK_sal_invoice_totals CHECK (gross>=0 AND deductions>=0 AND net>=0),
            CONSTRAINT CK_sal_invoice_status CHECK (status IN (1,2,3,4,5)),
            CONSTRAINT CK_sal_invoice_flags CHECK (run_mode IN (0,1) AND payout_blocked IN (0,1)),
            CONSTRAINT CK_sal_invoice_owner CHECK (posting_owner IN ('invoice','agent_settlement')),
            CONSTRAINT CK_sal_invoice_month CHECK ({SchemaSql.MonthCheck(c)} AND period_from<=period_to),
            CONSTRAINT CK_sal_invoice_hash CHECK ({(SchemaSql.IsSql(c)?"DATALENGTH":"length")}(file_hash)=32),
            CONSTRAINT FK_sal_invoice_company FOREIGN KEY (company_id) REFERENCES {SchemaSql.Q(c,"hr","company")}(id) ON DELETE NO ACTION,
            CONSTRAINT FK_sal_invoice_journal FOREIGN KEY (journal_id) REFERENCES {SchemaSql.Q(c,"fin","journal")}(id) ON DELETE NO ACTION
            """);
        SchemaMoneyFlags.Ensure(c,tx,"sal","platform_invoice",o=>$"{o}.approved_at IS NOT NULL OR {o}.status IN (2,3,5)");
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"sal","platform_invoice","UQ_sal_invoice_active","app_code,erp_cr_id,period_from,period_to","status<>4");
        SchemaSql.EnsureTable(c,tx,"sal","platform_invoice_line",$"""
            id {g} NOT NULL, invoice_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            source_row {SchemaSql.Int(c)} NOT NULL, account_key {SchemaSql.Varchar(c,100)} NOT NULL,
            erp_platform_account_id {g} NOT NULL, gross {m} NOT NULL, deductions {m} NOT NULL, net {m} NOT NULL,
            orders {SchemaSql.Int(c)} NOT NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_platform_invoice_line {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_invoice_line_row UNIQUE (invoice_id,source_row),
            CONSTRAINT UQ_sal_invoice_line_month UNIQUE (id,period_month),
            CONSTRAINT CK_sal_invoice_line_values CHECK (source_row>0 AND gross>=0 AND orders>=0),
            CONSTRAINT FK_sal_invoice_line_header FOREIGN KEY (invoice_id,period_month) REFERENCES {invoice}(id,period_month) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureTable(c,tx,"sal","platform_invoice_item",$"""
            id {g} NOT NULL, line_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            item_code {SchemaSql.Varchar(c,40)} NOT NULL, amount {m} NOT NULL,
            bearer_status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1,
            platform_deducted {SchemaSql.Bit(c)} NOT NULL DEFAULT 0,
            due_before_tga {m} NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_platform_invoice_item {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_invoice_item UNIQUE (line_id,item_code),
            CONSTRAINT CK_sal_invoice_item_bearer CHECK (bearer_status IN (1,2,3,4)),
            CONSTRAINT CK_sal_invoice_item_deducted CHECK (platform_deducted IN (0,1)),
            CONSTRAINT FK_sal_invoice_item_line FOREIGN KEY (line_id,period_month) REFERENCES {line}(id,period_month) ON DELETE NO ACTION
            """);
        foreach(string table in new[]{"platform_invoice","platform_invoice_line","platform_invoice_item"})
        {SchemaSql.EnsureMonthIndex(c,tx,"sal",table);SchemaSql.EnsureRowVersion(c,tx,"sal",table);}
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","platform_invoice","IX_sal_invoice_company","company_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","platform_invoice","IX_sal_invoice_journal","journal_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","platform_invoice_line","IX_sal_invoice_line_parent","invoice_id,period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","platform_invoice_item","IX_sal_invoice_item_parent","line_id,period_month");
    }
}
