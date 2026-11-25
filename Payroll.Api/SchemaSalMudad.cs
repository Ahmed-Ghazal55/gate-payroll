using System.Data.Common;
namespace Payroll;

/// <summary>U-W2-04 / U-W2-15a: sal.mudad_deduction links a pay-run rider to an hr.mudad_line (L1 personal).</summary>
public static class SchemaSalMudad
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c);
        string rider=SchemaSql.Q(c,"sal","pay_run_rider");
        string line=SchemaSql.Q(c,"hr","mudad_line");
        SchemaSql.EnsureTable(c,tx,"sal","mudad_deduction",$"""
            id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            pay_run_rider_id {g} NOT NULL, mudad_line_id {g} NOT NULL,
            amount {m} NOT NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_mudad_deduction {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_sal_mudad_deduction UNIQUE(pay_run_rider_id,mudad_line_id),
            CONSTRAINT CK_sal_mudad_amount CHECK(amount>=0),
            CONSTRAINT CK_sal_mudad_month CHECK({SchemaSql.MonthCheck(c)}),
            CONSTRAINT FK_sal_mudad_rider FOREIGN KEY(pay_run_rider_id,period_month) REFERENCES {rider}(id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_sal_mudad_line FOREIGN KEY(mudad_line_id) REFERENCES {line}(id) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureMonthIndex(c,tx,"sal","mudad_deduction");
        SchemaSql.EnsureRowVersion(c,tx,"sal","mudad_deduction");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","mudad_deduction","IX_sal_mudad_rider","pay_run_rider_id,period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","mudad_deduction","IX_sal_mudad_line","mudad_line_id");
        SchemaMoneyGuard.Retain(c,tx,"sal","mudad_deduction","o.pay_run_rider_id=NEW.pay_run_rider_id AND o.mudad_line_id=NEW.mudad_line_id");
    }
}
