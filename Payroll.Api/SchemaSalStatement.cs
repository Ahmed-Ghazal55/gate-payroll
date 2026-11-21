using System.Data.Common;
namespace Payroll;

/// <summary>U-W2-15b / X10: rider statement snapshot frozen at final salary approval (never live-read Omar).</summary>
public static class SchemaSalStatement
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c);
        string rider=SchemaSql.Q(c,"sal","pay_run_rider");
        SchemaSql.EnsureTable(c,tx,"sal","rider_statement_snapshot",$"""
            id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            pay_run_rider_id {g} NOT NULL, erp_rider_id {g} NOT NULL,
            company_id {g} NOT NULL, app_code {SchemaSql.Varchar(c,20)} NOT NULL,
            payload {SchemaSql.Json(c)} NOT NULL,
            frozen_at {SchemaSql.DateTime(c)} NOT NULL, frozen_by {SchemaSql.Text(c,128)} NOT NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_rider_statement {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_sal_rider_statement UNIQUE(pay_run_rider_id),
            CONSTRAINT CK_sal_statement_month CHECK({SchemaSql.MonthCheck(c)}),
            CONSTRAINT FK_sal_statement_rider FOREIGN KEY(pay_run_rider_id,period_month) REFERENCES {rider}(id,period_month) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureMonthIndex(c,tx,"sal","rider_statement_snapshot");
        SchemaSql.EnsureRowVersion(c,tx,"sal","rider_statement_snapshot");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","rider_statement_snapshot","IX_sal_statement_erp","erp_rider_id,period_month");
        SchemaMoneyGuard.Retain(c,tx,"sal","rider_statement_snapshot","o.pay_run_rider_id=NEW.pay_run_rider_id");
    }
}
