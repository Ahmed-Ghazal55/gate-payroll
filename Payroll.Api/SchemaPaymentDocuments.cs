using System.Data.Common;
namespace Payroll;

/// <summary>WP1 additive source documents. Canonical claims/counters remain owned by W1 allocation triggers.</summary>
public static class SchemaPaymentDocuments
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c),company=SchemaSql.Q(c,"hr","company"),account=SchemaSql.Q(c,"fin","account");
        SchemaSql.EnsureTable(c,tx,"pay","supplier",$"""
            id {g} NOT NULL {SchemaSql.PrimaryKey(c)}, name_ar {SchemaSql.Text(c,200)} NOT NULL,
            company_id {g} NOT NULL REFERENCES {company}(id), vat_no {SchemaSql.Varchar(c,20)} NULL,
            erp_maintenance_supplier_id {g} NULL, status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1 CHECK(status IN (1,2)),
            {SchemaSql.MoneyAuditCols(c)}
            """);
        SchemaSql.EnsureTable(c,tx,"pay","document",$"""
            id {g} NOT NULL {SchemaSql.PrimaryKey(c)}, doc_no {SchemaSql.Varchar(c,40)} NOT NULL UNIQUE,
            type {SchemaSql.Varchar(c,40)} NOT NULL, company_id {g} NOT NULL REFERENCES {company}(id),
            supplier_id {g} NULL REFERENCES {SchemaSql.Q(c,"pay","supplier")}(id),
            reference {SchemaSql.Varchar(c,100)} NOT NULL, idempotency_key {SchemaSql.Varchar(c,100)} NOT NULL,
            content_hash {SchemaSql.Varchar(c,64)} NOT NULL, platform {SchemaSql.Varchar(c,40)} NOT NULL DEFAULT '',
            occurred_on {SchemaSql.Date(c)} NOT NULL, due_on {SchemaSql.Date(c)} NOT NULL,
            period_month {SchemaSql.Date(c)} NOT NULL, amount {m} NOT NULL CHECK(amount>0),
            vat_amount {m} NOT NULL DEFAULT 0 CHECK(vat_amount>=0), currency_code {SchemaSql.Varchar(c,3)} NOT NULL DEFAULT 'SAR' CHECK(currency_code='SAR'),
            erp_receipt_id {g} NULL, metadata {(SchemaSql.IsSql(c)?"nvarchar(max)":"TEXT")} NOT NULL,
            {SchemaMoneyRegister.WorkflowColumns(c)}, {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT UQ_document_retry UNIQUE(company_id,idempotency_key),
            CONSTRAINT UQ_document_company UNIQUE(id,company_id),
            CONSTRAINT CK_document_month CHECK({SchemaSql.MonthCheck(c)})
            """);
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"pay","document","UQ_document_invoice","supplier_id,reference","supplier_id IS NOT NULL");
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"pay","document","UQ_document_receipt","erp_receipt_id","erp_receipt_id IS NOT NULL");
        SchemaSql.EnsureTable(c,tx,"pay","document_line",$"""
            id {g} NOT NULL {SchemaSql.PrimaryKey(c)}, document_id {g} NOT NULL, company_id {g} NOT NULL,
            line_no {SchemaSql.Int(c)} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            account_id {g} NOT NULL REFERENCES {account}(id), cost_centre_id {g} NOT NULL REFERENCES {SchemaSql.Q(c,"org","cost_centre")}(id),
            erp_rider_id {g} NULL, erp_platform_account_id {g} NULL, app_code {SchemaSql.Varchar(c,20)} NOT NULL DEFAULT 'none',
            amount {m} NOT NULL CHECK(amount>0), active {SchemaSql.Bit(c)} NOT NULL DEFAULT 1 CHECK(active IN (0,1)),
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT FK_document_line_company FOREIGN KEY(document_id,company_id) REFERENCES {SchemaSql.Q(c,"pay","document")}(id,company_id)
            """);
        SchemaSql.EnsureTable(c,tx,"pay","approval",$"""
            id {g} NOT NULL {SchemaSql.PrimaryKey(c)}, document_id {g} NOT NULL REFERENCES {SchemaSql.Q(c,"pay","document")}(id),
            user_id {g} NOT NULL REFERENCES {SchemaSql.Q(c,"auth","user")}(id), decided_at {SchemaSql.DateTime(c)} NOT NULL,
            note {SchemaSql.Text(c,500)} NULL, {SchemaSql.MoneyAuditCols(c)}
            """);
        SchemaSql.EnsureTable(c,tx,"pay","payment_detail",$"""
            id {g} NOT NULL {SchemaSql.PrimaryKey(c)} REFERENCES {SchemaSql.Q(c,"pay","payment")}(id),
            document_id {g} NOT NULL REFERENCES {SchemaSql.Q(c,"pay","document")}(id),
            idempotency_key {SchemaSql.Varchar(c,100)} NOT NULL, bank_account_id {g} NOT NULL REFERENCES {account}(id),
            reference {SchemaSql.Varchar(c,100)} NOT NULL, approval_request_id {SchemaSql.Varchar(c,128)} NOT NULL,
            content_hash {SchemaSql.Varchar(c,64)} NOT NULL, {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT UQ_payment_detail_retry UNIQUE(document_id,idempotency_key)
            """);
        SchemaSql.EnsureTable(c,tx,"pay","payment_source_line",$"""
            id {g} NOT NULL {SchemaSql.PrimaryKey(c)},payment_id {g} NOT NULL REFERENCES {SchemaSql.Q(c,"pay","payment")}(id),
            document_line_id {g} NOT NULL REFERENCES {SchemaSql.Q(c,"pay","document_line")}(id),
            period_month {SchemaSql.Date(c)} NOT NULL,amount {m} NOT NULL CHECK(amount>0),
            {SchemaSql.MoneyAuditCols(c)},CONSTRAINT UQ_payment_source_line UNIQUE(payment_id,document_line_id)
            """);
        SchemaMoneyGuard.Retain(c,tx,"pay","payment_source_line","o.payment_id=NEW.payment_id AND o.document_line_id=NEW.document_line_id");
        SchemaMoneyRegister.Immutable(c,tx,"pay","payment_source_line","payment_id","document_line_id","period_month","amount");SchemaSql.EnsureMonthIndex(c,tx,"pay","payment_source_line");
        SchemaMoneyRegister.Workflow(c,tx,"pay","document","type","company_id","supplier_id","reference","platform","period_month","occurred_on","due_on","amount","vat_amount","metadata","content_hash","erp_receipt_id","idempotency_key");
        SchemaMoneyGuard.Retain(c,tx,"pay","document","o.doc_no=NEW.doc_no OR (o.company_id=NEW.company_id AND o.idempotency_key=NEW.idempotency_key) OR (o.supplier_id=NEW.supplier_id AND o.reference=NEW.reference) OR (o.erp_receipt_id=NEW.erp_receipt_id AND NEW.erp_receipt_id IS NOT NULL)");
        foreach(string table in new[]{"supplier","document_line","approval","payment_detail"})SchemaMoneyGuard.Retain(c,tx,"pay",table);
        SchemaMoneyGuard.Retain(c,tx,"pay","payment_detail","o.document_id=NEW.document_id AND o.idempotency_key=NEW.idempotency_key");
        SchemaMoneyRegister.Immutable(c,tx,"pay","payment_detail","document_id","idempotency_key","bank_account_id","reference","approval_request_id","content_hash");
        SchemaMoneyRegister.Immutable(c,tx,"pay","approval","document_id","user_id","decided_at","note");
        // Submitted lines cannot be rewritten independently of the frozen source header.
        SchemaMoneyGuard.Rule(c,tx,"pay","document_line","TR_document_line_frozen",
            n=>$"EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"pay","document")} d WHERE d.id={n}.document_id AND d.status<>1)",
            (o,n)=>$"EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"pay","document")} d WHERE (d.id={o}.document_id OR d.id={n}.document_id) AND d.status<>1)");
        foreach(string table in new[]{"document","document_line"})SchemaSql.EnsureMonthIndex(c,tx,"pay",table);
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","document","IX_document_company_due","company_id,due_on,status");
        SchemaSql.CreateIndexIfMissing(c,tx,"pay","document_line","IX_document_lines","document_id,active");
    }
}
