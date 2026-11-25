using System.Data.Common;
namespace Payroll;

/// <summary>U-W2-13: app settlement combine, distribution shares, and item bearers (TGA / shared users).
/// Design §3.4–3.6 / 62 draft; residual on last bearer line; partition-ready by period_month.</summary>
public static class SchemaSalDistribution
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c);
        SchemaSql.EnsureTable(c,tx,"sal","app_settlement",$"""
            id {g} NOT NULL, app_code {SchemaSql.Varchar(c,20)} NOT NULL,
            period_from {SchemaSql.Date(c)} NOT NULL, period_to {SchemaSql.Date(c)} NOT NULL,
            period_month {SchemaSql.Date(c)} NOT NULL, status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1,
            gross {m} NOT NULL DEFAULT 0, deductions {m} NOT NULL DEFAULT 0, net {m} NOT NULL DEFAULT 0,
            run_mode {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0, payout_blocked {SchemaSql.Bit(c)} NOT NULL DEFAULT 0,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_app_settlement {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT CK_sal_settlement_status CHECK (status IN (1,2,3,4)),
            CONSTRAINT CK_sal_settlement_flags CHECK (run_mode IN (0,1) AND payout_blocked IN (0,1)),
            CONSTRAINT CK_sal_settlement_month CHECK ({SchemaSql.MonthCheck(c)} AND period_from<=period_to),
            CONSTRAINT CK_sal_settlement_money CHECK (gross>=0 AND deductions>=0 AND net>=0)
            """);
        SchemaSql.EnsureTable(c,tx,"sal","app_settlement_invoice",$"""
            id {g} NOT NULL, settlement_id {g} NOT NULL, invoice_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            CONSTRAINT PK_sal_settlement_invoice {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_settlement_invoice UNIQUE (settlement_id,invoice_id),
            CONSTRAINT FK_sal_settlement_invoice_header FOREIGN KEY (settlement_id) REFERENCES {SchemaSql.Q(c,"sal","app_settlement")}(id) ON DELETE NO ACTION,
            CONSTRAINT FK_sal_settlement_invoice_inv FOREIGN KEY (invoice_id) REFERENCES {SchemaSql.Q(c,"sal","platform_invoice")}(id) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureTable(c,tx,"sal","distribution",$"""
            id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            invoice_id {g} NOT NULL, line_id {g} NULL, erp_platform_account_id {g} NOT NULL,
            erp_rider_id {g} NOT NULL, orders {SchemaSql.Int(c)} NOT NULL DEFAULT 0,
            days {SchemaSql.Int(c)} NOT NULL DEFAULT 0, from_date {SchemaSql.Date(c)} NULL, to_date {SchemaSql.Date(c)} NULL,
            share_amount {m} NOT NULL, method {SchemaSql.Varchar(c,20)} NOT NULL,
            status {SchemaSql.Varchar(c,20)} NOT NULL DEFAULT 'ready',
            approved_by {g} NULL, approved_at {SchemaSql.DateTime(c)} NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_distribution {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_distribution_rider UNIQUE (invoice_id,erp_platform_account_id,erp_rider_id,from_date,to_date),
            CONSTRAINT CK_sal_distribution_method CHECK (method IN ('order_share','explicit','actual_rider','supervisor')),
            CONSTRAINT CK_sal_distribution_status CHECK (status IN ('ready','needs_decision','approved')),
            CONSTRAINT CK_sal_distribution_orders CHECK (orders>=0 AND days>=0 AND share_amount>=0),
            CONSTRAINT FK_sal_distribution_invoice FOREIGN KEY (invoice_id) REFERENCES {SchemaSql.Q(c,"sal","platform_invoice")}(id) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureTable(c,tx,"sal","item_bearer",$"""
            id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            item_id {g} NOT NULL, line_no {SchemaSql.Int(c)} NOT NULL,
            bearer {SchemaSql.Varchar(c,12)} NOT NULL, erp_rider_id {g} NULL,
            amount {m} NOT NULL, from_date {SchemaSql.Date(c)} NULL, to_date {SchemaSql.Date(c)} NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_item_bearer {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_item_bearer_line UNIQUE (item_id,line_no),
            CONSTRAINT CK_sal_item_bearer_kind CHECK (bearer IN ('company','rider')),
            CONSTRAINT CK_sal_item_bearer_rider CHECK ((bearer='company' AND erp_rider_id IS NULL) OR (bearer='rider' AND erp_rider_id IS NOT NULL)),
            CONSTRAINT CK_sal_item_bearer_amount CHECK (amount>=0 AND line_no>0),
            CONSTRAINT FK_sal_item_bearer_item FOREIGN KEY (item_id) REFERENCES {SchemaSql.Q(c,"sal","platform_invoice_item")}(id) ON DELETE NO ACTION
            """);
        foreach(string table in new[]{"app_settlement","distribution","item_bearer"})
            SchemaSql.EnsureMonthIndex(c,tx,"sal",table);
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","distribution","IX_sal_distribution_invoice","invoice_id,period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","item_bearer","IX_sal_item_bearer_item","item_id,period_month");
    }
}
