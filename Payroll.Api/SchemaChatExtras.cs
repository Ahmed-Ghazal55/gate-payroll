using System.Data.Common;
namespace Payroll;

/// <summary>U-W2-29 extras on W1 chat.*: membership + company scope + media duration (G4, G8, X25).</summary>
public static class SchemaChatExtras
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchema(c,tx,"chat");
        EnsureMembership(c,tx);
        EnsureChannelCompany(c,tx);
        EnsureMediaDurationColumn(c,tx);
        EnsureMediaBlob(c,tx);
    }

    static void EnsureChannelCompany(DbConnection c,DbTransaction? tx)
    {
        if(!SchemaSql.TableExists(c,tx,"chat","channel"))return;
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,"""
                IF COL_LENGTH(N'chat.channel',N'company_id') IS NULL
                  ALTER TABLE chat.channel ADD company_id uniqueidentifier NULL;
                """);
        else if(!SqliteHasColumn(c,tx,"chat_channel","company_id"))
            SchemaSql.Exec(c,tx,"ALTER TABLE chat_channel ADD COLUMN company_id TEXT NULL;");
        SchemaSql.CreateIndexIfMissing(c,tx,"chat","channel","IX_chat_channel_company_id","company_id");
    }

    static void EnsureMediaBlob(DbConnection c,DbTransaction? tx)
    {
        if(!SchemaSql.TableExists(c,tx,"chat","message"))return;
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,"""
                IF COL_LENGTH(N'chat.message',N'media_blob') IS NULL
                  ALTER TABLE chat.message ADD media_blob varbinary(max) NULL;
                """);
            SchemaSql.Exec(c,tx,"""
                IF COL_LENGTH(N'chat.message',N'media_content_type') IS NULL
                  ALTER TABLE chat.message ADD media_content_type varchar(80) NULL;
                """);
        }
        else
        {
            if(!SqliteHasColumn(c,tx,"chat_message","media_blob"))
                SchemaSql.Exec(c,tx,"ALTER TABLE chat_message ADD COLUMN media_blob BLOB NULL;");
            if(!SqliteHasColumn(c,tx,"chat_message","media_content_type"))
                SchemaSql.Exec(c,tx,"ALTER TABLE chat_message ADD COLUMN media_content_type TEXT NULL;");
        }
    }

    static void EnsureMembership(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"chat","membership");string g=SchemaSql.GuidType(c);
        string channel=SchemaSql.Q(c,"chat","channel");string user=SchemaSql.Q(c,"auth","user");
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'chat.membership',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  channel_id {g} NOT NULL,
                  user_id {g} NOT NULL,
                  company_id {g} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_chat_membership PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_chat_membership UNIQUE (channel_id, user_id),
                  CONSTRAINT FK_chat_membership_channel FOREIGN KEY (channel_id) REFERENCES {channel}(id),
                  CONSTRAINT FK_chat_membership_user FOREIGN KEY (user_id) REFERENCES {user}(id)
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  channel_id {g} NOT NULL,
                  user_id {g} NOT NULL,
                  company_id {g} NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_chat_membership UNIQUE (channel_id, user_id),
                  CONSTRAINT FK_chat_membership_channel FOREIGN KEY (channel_id) REFERENCES {channel}(id),
                  CONSTRAINT FK_chat_membership_user FOREIGN KEY (user_id) REFERENCES {user}(id)
                );
                """);
        SchemaSql.CreateIndexIfMissing(c,tx,"chat","membership","IX_chat_membership_user_id","user_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"chat","membership","IX_chat_membership_company_id","company_id");
    }

    static void EnsureMediaDurationColumn(DbConnection c,DbTransaction? tx)
    {
        if(!SchemaSql.TableExists(c,tx,"chat","message"))return;
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,"""
                IF COL_LENGTH(N'chat.message',N'media_duration_seconds') IS NULL
                  ALTER TABLE chat.message ADD media_duration_seconds int NULL;
                """);
            SchemaSql.Exec(c,tx,"""
                IF COL_LENGTH(N'chat.message',N'media_bytes') IS NULL
                  ALTER TABLE chat.message ADD media_bytes int NULL;
                """);
        }
        else
        {
            if(!SqliteHasColumn(c,tx,"chat_message","media_duration_seconds"))
                SchemaSql.Exec(c,tx,"ALTER TABLE chat_message ADD COLUMN media_duration_seconds INTEGER NULL;");
            if(!SqliteHasColumn(c,tx,"chat_message","media_bytes"))
                SchemaSql.Exec(c,tx,"ALTER TABLE chat_message ADD COLUMN media_bytes INTEGER NULL;");
        }
    }

    static bool SqliteHasColumn(DbConnection c,DbTransaction? tx,string table,string column)
    {
        using var cmd=Store.Command(c,tx,$"PRAGMA table_info({table})");
        using var r=cmd.ExecuteReader();
        while(r.Read())
            if(string.Equals(r.GetString(1),column,StringComparison.OrdinalIgnoreCase))return true;
        return false;
    }
}
