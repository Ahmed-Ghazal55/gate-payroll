using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-09 (period half): fin.accounting_period open/closed states. Design §7.1 / BR §17 م11. Transitions beyond allowed state values are enforced by the posting service (admin+2nd reopen).
/// Unique (company_id, id, period_month) lets journals and balances FK the period start into the same row.</summary>
public static class SchemaFinPeriod
{
    // state: 1=open, 2=closed
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchema(c,tx,"fin");
        SchemaSql.EnsureSchema(c,tx,"hr");
        EnsurePeriod(c,tx);
    }

    static void EnsurePeriod(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"fin","accounting_period");string g=SchemaSql.GuidType(c);
        string company=SchemaSql.Q(c,"hr","company");
        // period_month is the period start (design §7.1); must be first day of month on both providers.
        // SQLite: also require canonical 'YYYY-MM-DD' text so '2026-11-01' and '2026-11-01 00:00:00' cannot both insert.
        string firstOfMonth=SchemaSql.IsSql(c)
            ?"period_month = DATEFROMPARTS(YEAR(period_month), MONTH(period_month), 1)"
            :"period_month = date(period_month) AND strftime('%d', period_month) = '01'";
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'fin.accounting_period',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  company_id {g} NOT NULL,
                  period_month {SchemaSql.Date(c)} NOT NULL,
                  state {SchemaSql.Tiny(c)} NOT NULL,
                  opened_at {SchemaSql.DateTime(c)} NULL,
                  opened_by {SchemaSql.Text(c,128)} NULL,
                  closed_at {SchemaSql.DateTime(c)} NULL,
                  closed_by {SchemaSql.Text(c,128)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_fin_accounting_period PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_fin_accounting_period UNIQUE (company_id, period_month),
                  CONSTRAINT UQ_fin_accounting_period_company_id UNIQUE (company_id, id),
                  CONSTRAINT UQ_fin_accounting_period_company_id_start UNIQUE (company_id, id, period_month),
                  CONSTRAINT CK_fin_accounting_period_state CHECK (state IN (1,2)),
                  CONSTRAINT CK_fin_accounting_period_start CHECK ({firstOfMonth}),
                  CONSTRAINT FK_fin_period_company FOREIGN KEY (company_id) REFERENCES {company}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","accounting_period","IX_fin_period_company_id","company_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","accounting_period","IX_fin_period_month","period_month");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  company_id {g} NOT NULL,
                  period_month TEXT NOT NULL,
                  state INTEGER NOT NULL,
                  opened_at TEXT NULL,
                  opened_by TEXT NULL,
                  closed_at TEXT NULL,
                  closed_by TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_fin_accounting_period UNIQUE (company_id, period_month),
                  CONSTRAINT UQ_fin_accounting_period_company_id UNIQUE (company_id, id),
                  CONSTRAINT UQ_fin_accounting_period_company_id_start UNIQUE (company_id, id, period_month),
                  CONSTRAINT CK_fin_accounting_period_state CHECK (state IN (1,2)),
                  CONSTRAINT CK_fin_accounting_period_start CHECK ({firstOfMonth}),
                  CONSTRAINT FK_fin_period_company FOREIGN KEY (company_id) REFERENCES {company}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","accounting_period","IX_fin_period_company_id","company_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","accounting_period","IX_fin_period_month","period_month");
        }
    }
}
