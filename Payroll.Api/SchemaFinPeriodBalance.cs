using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
namespace Payroll;

/// <summary>U-W1-10: fin.period_balance with non-null dimension_key grain. Design §7.1 N4 / §16. No backfill; balances filled by posting service later.
/// dimension_key is never caller-supplied on SQL Server (PERSISTED HASHBYTES). On SQLite the shared C# helper computes the 32-byte BLOB and CHECK enforces length.</summary>
public static class SchemaFinPeriodBalance
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchema(c,tx,"fin");
        EnsureBalance(c,tx);
    }

    /// <summary>Canonical dimension string: company_id|account_id|cost_centre_id|app_code (lowercase D-format GUIDs, app sentinel 'none').</summary>
    public static string CanonicalDimensionString(Guid companyId,Guid accountId,Guid costCentreId,string appCode)
        =>$"{companyId:D}|{accountId:D}|{costCentreId:D}|{appCode}";

    /// <summary>SHA-256 of the canonical dimension string — always 32 bytes; identical on every runtime. Shared by SQLite inserts and SQL Server HASHBYTES expression.</summary>
    public static byte[] ComputeDimensionKey(Guid companyId,Guid accountId,Guid costCentreId,string appCode)
    {
        string canon=CanonicalDimensionString(companyId,accountId,costCentreId,appCode);
        return SHA256.HashData(Encoding.UTF8.GetBytes(canon));
    }

    /// <summary>SQL Server PERSISTED expression — must match <see cref="CanonicalDimensionString"/> byte-for-byte (ASCII/UTF-8 for these chars).</summary>
    public static string SqlServerDimensionKeyExpression =>
        "CAST(HASHBYTES('SHA2_256', LOWER(CONVERT(varchar(36), company_id)) + '|' + LOWER(CONVERT(varchar(36), account_id)) + '|' + LOWER(CONVERT(varchar(36), cost_centre_id)) + '|' + CONVERT(varchar(20), app_code)) AS binary(32))";

    static void EnsureBalance(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"fin","period_balance");string g=SchemaSql.GuidType(c);
        string period=SchemaSql.Q(c,"fin","accounting_period");
        string account=SchemaSql.Q(c,"fin","account");
        string company=SchemaSql.Q(c,"hr","company");
        string cc=SchemaSql.Q(c,"org","cost_centre");
        string money=SchemaSql.Decimal(c);
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'fin.period_balance',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  period_id {g} NOT NULL,
                  period_month {SchemaSql.Date(c)} NOT NULL,
                  company_id {g} NOT NULL,
                  account_id {g} NOT NULL,
                  cost_centre_id {g} NOT NULL,
                  app_code varchar(20) NOT NULL,
                  dimension_key AS ({SqlServerDimensionKeyExpression}) PERSISTED,
                  opening {money} NOT NULL CONSTRAINT DF_fin_pb_opening DEFAULT(0),
                  debit {money} NOT NULL CONSTRAINT DF_fin_pb_debit DEFAULT(0),
                  credit {money} NOT NULL CONSTRAINT DF_fin_pb_credit DEFAULT(0),
                  closing {money} NOT NULL CONSTRAINT DF_fin_pb_closing DEFAULT(0),
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_fin_period_balance PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_fin_period_balance_grain UNIQUE (period_id, dimension_key),
                  CONSTRAINT UQ_fin_period_balance_dims UNIQUE (period_id, company_id, account_id, cost_centre_id, app_code),
                  CONSTRAINT CK_fin_period_balance_app CHECK (app_code <> ''),
                  CONSTRAINT FK_fin_pb_period_company FOREIGN KEY (company_id, period_id, period_month) REFERENCES {period}(company_id, id, period_month),
                  CONSTRAINT FK_fin_pb_company FOREIGN KEY (company_id) REFERENCES {company}(id),
                  CONSTRAINT FK_fin_pb_account FOREIGN KEY (account_id) REFERENCES {account}(id),
                  CONSTRAINT FK_fin_pb_cc FOREIGN KEY (cost_centre_id) REFERENCES {cc}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'fin.period_balance') AND name=N'CX_fin_period_balance')
                CREATE CLUSTERED INDEX [CX_fin_period_balance] ON {t}(period_month, dimension_key);
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","period_balance","IX_fin_pb_period_id","period_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","period_balance","IX_fin_pb_account_id","account_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","period_balance","IX_fin_pb_company_id","company_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","period_balance","IX_fin_pb_cc_id","cost_centre_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  period_id {g} NOT NULL,
                  period_month TEXT NOT NULL,
                  company_id {g} NOT NULL,
                  account_id {g} NOT NULL,
                  cost_centre_id {g} NOT NULL,
                  app_code TEXT NOT NULL,
                  dimension_key BLOB NOT NULL,
                  opening REAL NOT NULL DEFAULT 0,
                  debit REAL NOT NULL DEFAULT 0,
                  credit REAL NOT NULL DEFAULT 0,
                  closing REAL NOT NULL DEFAULT 0,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_fin_period_balance_grain UNIQUE (period_id, dimension_key),
                  CONSTRAINT UQ_fin_period_balance_dims UNIQUE (period_id, company_id, account_id, cost_centre_id, app_code),
                  CONSTRAINT CK_fin_period_balance_app CHECK (app_code <> ''),
                  CONSTRAINT CK_fin_period_balance_dim_len CHECK (length(dimension_key) = 32),
                  CONSTRAINT FK_fin_pb_period_company FOREIGN KEY (company_id, period_id, period_month) REFERENCES {period}(company_id, id, period_month),
                  CONSTRAINT FK_fin_pb_company FOREIGN KEY (company_id) REFERENCES {company}(id),
                  CONSTRAINT FK_fin_pb_account FOREIGN KEY (account_id) REFERENCES {account}(id),
                  CONSTRAINT FK_fin_pb_cc FOREIGN KEY (cost_centre_id) REFERENCES {cc}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","period_balance","IX_fin_pb_period_month_dim","period_month, dimension_key");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","period_balance","IX_fin_pb_period_id","period_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","period_balance","IX_fin_pb_account_id","account_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","period_balance","IX_fin_pb_company_id","company_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","period_balance","IX_fin_pb_cc_id","cost_centre_id");
        }
    }
}
