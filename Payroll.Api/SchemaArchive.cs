using System.Data.Common;
namespace Payroll;

/// <summary>U-W2-39 / M-0632 / G3: archive policies + restore requests. Never hard-delete.</summary>
public static class SchemaArchive
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchema(c,tx,"archive");
        EnsurePolicy(c,tx);
        EnsureRestore(c,tx);
        SeedDefaultPolicy(c,tx);
    }

    static void EnsurePolicy(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"archive","policy");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'archive.policy',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  code varchar(40) NOT NULL,
                  name_ar nvarchar(200) NOT NULL,
                  retention_years {SchemaSql.Int(c)} NOT NULL,
                  hard_delete_allowed {SchemaSql.Bit(c)} NOT NULL CONSTRAINT DF_archive_policy_hd DEFAULT(0),
                  restore_view_hours {SchemaSql.Int(c)} NOT NULL CONSTRAINT DF_archive_policy_hours DEFAULT(72),
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_archive_policy PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_archive_policy_code UNIQUE (code),
                  CONSTRAINT CK_archive_policy_retention CHECK (retention_years >= 3),
                  CONSTRAINT CK_archive_policy_no_delete CHECK (hard_delete_allowed = 0),
                  CONSTRAINT CK_archive_policy_hours CHECK (restore_view_hours BETWEEN 1 AND 720)
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  code TEXT NOT NULL,
                  name_ar TEXT NOT NULL,
                  retention_years INTEGER NOT NULL,
                  hard_delete_allowed INTEGER NOT NULL DEFAULT 0,
                  restore_view_hours INTEGER NOT NULL DEFAULT 72,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_archive_policy_code UNIQUE (code),
                  CONSTRAINT CK_archive_policy_retention CHECK (retention_years >= 3),
                  CONSTRAINT CK_archive_policy_no_delete CHECK (hard_delete_allowed = 0),
                  CONSTRAINT CK_archive_policy_hours CHECK (restore_view_hours BETWEEN 1 AND 720)
                );
                """);
    }

    static void EnsureRestore(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"archive","restore_request");string g=SchemaSql.GuidType(c);string user=SchemaSql.Q(c,"auth","user");
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'archive.restore_request',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  policy_id {g} NOT NULL,
                  entity_type varchar(80) NOT NULL,
                  period_from {SchemaSql.Date(c)} NOT NULL,
                  period_to {SchemaSql.Date(c)} NOT NULL,
                  reason_ar nvarchar(400) NOT NULL,
                  status varchar(20) NOT NULL,
                  approval_request_id {g} NULL,
                  requested_by {g} NOT NULL,
                  approved_by {g} NULL,
                  expires_at {SchemaSql.DateTime(c)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_archive_restore PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_archive_restore_status CHECK (status IN ('pending','active','rejected','expired','cancelled')),
                  CONSTRAINT CK_archive_restore_period CHECK (period_to >= period_from),
                  CONSTRAINT FK_archive_restore_requester FOREIGN KEY (requested_by) REFERENCES {user}(id)
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  policy_id {g} NOT NULL,
                  entity_type TEXT NOT NULL,
                  period_from TEXT NOT NULL,
                  period_to TEXT NOT NULL,
                  reason_ar TEXT NOT NULL,
                  status TEXT NOT NULL,
                  approval_request_id {g} NULL,
                  requested_by {g} NOT NULL,
                  approved_by {g} NULL,
                  expires_at TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_archive_restore_status CHECK (status IN ('pending','active','rejected','expired','cancelled')),
                  CONSTRAINT CK_archive_restore_period CHECK (period_to >= period_from),
                  CONSTRAINT FK_archive_restore_requester FOREIGN KEY (requested_by) REFERENCES {user}(id)
                );
                """);
        SchemaSql.CreateIndexIfMissing(c,tx,"archive","restore_request","IX_archive_restore_status","status");
    }

    static void SeedDefaultPolicy(DbConnection c,DbTransaction? tx)
    {
        object? n=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"archive","policy")} WHERE code=@c",("@c","default_3y"));
        if(Convert.ToInt64(n!)>0)return;
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"archive","policy")}(id,code,name_ar,retention_years,hard_delete_allowed,restore_view_hours,created_at)
            VALUES(@id,'default_3y',@name,3,0,72,{SchemaSql.NowLiteral(c)})
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@name","أرشفة بعد ٣ سنوات بدون حذف"));
    }
}
