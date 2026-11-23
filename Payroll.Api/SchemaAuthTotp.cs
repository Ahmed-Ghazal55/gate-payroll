using System.Data.Common;
namespace Payroll;

/// <summary>U-W2-38 / G7 / X24: auth.totp_secret + backup codes. Secrets encrypted at rest; codes hashed.</summary>
public static class SchemaAuthTotp
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchema(c,tx,"auth");
        EnsureSecret(c,tx);
        EnsureBackup(c,tx);
    }

    static void EnsureSecret(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"auth","totp_secret");string g=SchemaSql.GuidType(c);string user=SchemaSql.Q(c,"auth","user");
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'auth.totp_secret',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  user_id {g} NOT NULL,
                  secret_encrypted {SchemaSql.Binary(c,512)} NOT NULL,
                  verified_at {SchemaSql.DateTime(c)} NULL,
                  setup_deadline {SchemaSql.DateTime(c)} NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_auth_totp_secret PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_auth_totp_secret_user UNIQUE (user_id),
                  CONSTRAINT FK_auth_totp_user FOREIGN KEY (user_id) REFERENCES {user}(id)
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  user_id {g} NOT NULL,
                  secret_encrypted BLOB NOT NULL,
                  verified_at TEXT NULL,
                  setup_deadline TEXT NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_auth_totp_secret_user UNIQUE (user_id),
                  CONSTRAINT FK_auth_totp_user FOREIGN KEY (user_id) REFERENCES {user}(id)
                );
                """);
        SchemaSql.CreateIndexIfMissing(c,tx,"auth","totp_secret","IX_auth_totp_secret_user_id","user_id");
    }

    static void EnsureBackup(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"auth","totp_backup_code");string g=SchemaSql.GuidType(c);string user=SchemaSql.Q(c,"auth","user");
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'auth.totp_backup_code',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  user_id {g} NOT NULL,
                  code_hash {SchemaSql.Binary(c,32)} NOT NULL,
                  used_at {SchemaSql.DateTime(c)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_auth_totp_backup PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT FK_auth_totp_backup_user FOREIGN KEY (user_id) REFERENCES {user}(id)
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  user_id {g} NOT NULL,
                  code_hash BLOB NOT NULL,
                  used_at TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT FK_auth_totp_backup_user FOREIGN KEY (user_id) REFERENCES {user}(id)
                );
                """);
        SchemaSql.CreateIndexIfMissing(c,tx,"auth","totp_backup_code","IX_auth_totp_backup_user_id","user_id");
    }
}
