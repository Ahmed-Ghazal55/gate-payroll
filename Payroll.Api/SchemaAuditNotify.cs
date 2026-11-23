using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-19: audit.* / notify.* / task.* / chat.*. Design §9 / §16 / M-0606 / M-0607 / M-0614 / G2–G4.
/// change_log is partition-ready on (occurred_on, id); no read events (source excludes 'read'); one bulk import row uses bulk_count + file_hash.</summary>
public static partial class SchemaAuditNotify
{
    static readonly (string Code,string NameAr)[] ActionLabels=
    [
        ("create","إنشاء"),("update","تعديل"),("delete","حذف"),("approve","اعتماد"),
        ("reject","رفض"),("import","استيراد"),("grant","منح"),("revoke","سحب")
    ];

    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchemas(c,tx,"audit","notify","task","chat");
        EnsureActionLabel(c,tx);
        EnsureChangeLog(c,tx);
        EnsureReadSummaryDaily(c,tx);
        EnsureChatChannel(c,tx);
        EnsureNotify(c,tx);
        EnsureTask(c,tx);
        EnsureChat(c,tx);
        SchemaChatExtras.Ensure(c,tx);
        SeedActionLabels(c,tx);
    }

    static void EnsureActionLabel(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"audit","action_label");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'audit.action_label',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  action_code varchar(40) NOT NULL,
                  name_ar nvarchar(80) NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_audit_action_label PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_audit_action_label_code UNIQUE (action_code)
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  action_code TEXT NOT NULL,
                  name_ar TEXT NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_audit_action_label_code UNIQUE (action_code)
                );
                """);
    }

    static void EnsureChangeLog(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"audit","change_log");string g=SchemaSql.GuidType(c);
        string label=SchemaSql.Q(c,"audit","action_label");
        string user=SchemaSql.Q(c,"auth","user");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'audit.change_log',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  occurred_on {SchemaSql.Date(c)} NOT NULL,
                  occurred_at {SchemaSql.DateTime(c)} NOT NULL,
                  user_id {g} NULL,
                  username varchar(80) NULL,
                  display_name_ar nvarchar(200) NULL,
                  action_code varchar(40) NOT NULL,
                  entity_type varchar(40) NOT NULL,
                  entity_ref nvarchar(200) NOT NULL,
                  diff_short nvarchar(400) NOT NULL,
                  source varchar(20) NOT NULL,
                  bulk_count {SchemaSql.Int(c)} NULL,
                  file_hash {SchemaSql.Binary(c,32)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_audit_change_log PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_audit_change_log_source CHECK (source IN ('ui','api','job','import','system')),
                  CONSTRAINT CK_audit_change_log_bulk CHECK ((bulk_count IS NULL AND file_hash IS NULL) OR (bulk_count IS NOT NULL AND bulk_count > 0 AND file_hash IS NOT NULL)),
                  CONSTRAINT FK_audit_change_log_action FOREIGN KEY (action_code) REFERENCES {label}(action_code),
                  CONSTRAINT FK_audit_change_log_user FOREIGN KEY (user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'audit.change_log') AND name=N'CX_audit_change_log')
                CREATE CLUSTERED INDEX [CX_audit_change_log] ON {t}(occurred_on, id);
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  occurred_on TEXT NOT NULL,
                  occurred_at TEXT NOT NULL,
                  user_id {g} NULL,
                  username TEXT NULL,
                  display_name_ar TEXT NULL,
                  action_code TEXT NOT NULL,
                  entity_type TEXT NOT NULL,
                  entity_ref TEXT NOT NULL,
                  diff_short TEXT NOT NULL,
                  source TEXT NOT NULL,
                  bulk_count INTEGER NULL,
                  file_hash BLOB NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_audit_change_log_source CHECK (source IN ('ui','api','job','import','system')),
                  CONSTRAINT CK_audit_change_log_bulk CHECK ((bulk_count IS NULL AND file_hash IS NULL) OR (bulk_count IS NOT NULL AND bulk_count > 0 AND file_hash IS NOT NULL)),
                  CONSTRAINT FK_audit_change_log_action FOREIGN KEY (action_code) REFERENCES {label}(action_code),
                  CONSTRAINT FK_audit_change_log_user FOREIGN KEY (user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"audit","change_log","CX_audit_change_log","occurred_on,id");
        }
        SchemaSql.CreateIndexIfMissing(c,tx,"audit","change_log","IX_audit_change_log_action","action_code");
        SchemaSql.CreateIndexIfMissing(c,tx,"audit","change_log","IX_audit_change_log_user","user_id");
    }

    static void EnsureReadSummaryDaily(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"audit","read_summary_daily");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'audit.read_summary_daily',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  summary_date {SchemaSql.Date(c)} NOT NULL,
                  user_id {g} NOT NULL,
                  area varchar(40) NOT NULL,
                  read_count {SchemaSql.Int(c)} NOT NULL CONSTRAINT DF_audit_read_count DEFAULT(0),
                  export_count {SchemaSql.Int(c)} NOT NULL CONSTRAINT DF_audit_export_count DEFAULT(0),
                  sensitive_count {SchemaSql.Int(c)} NOT NULL CONSTRAINT DF_audit_sensitive_count DEFAULT(0),
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_audit_read_summary_daily PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_audit_read_summary_daily UNIQUE (summary_date, user_id, area),
                  CONSTRAINT CK_audit_read_summary_counts CHECK (read_count >= 0 AND export_count >= 0 AND sensitive_count >= 0)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'audit.read_summary_daily') AND name=N'CX_audit_read_summary_daily')
                CREATE CLUSTERED INDEX [CX_audit_read_summary_daily] ON {t}(summary_date, user_id, area);
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  summary_date TEXT NOT NULL,
                  user_id {g} NOT NULL,
                  area TEXT NOT NULL,
                  read_count INTEGER NOT NULL DEFAULT 0,
                  export_count INTEGER NOT NULL DEFAULT 0,
                  sensitive_count INTEGER NOT NULL DEFAULT 0,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_audit_read_summary_daily UNIQUE (summary_date, user_id, area),
                  CONSTRAINT CK_audit_read_summary_counts CHECK (read_count >= 0 AND export_count >= 0 AND sensitive_count >= 0)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"audit","read_summary_daily","CX_audit_read_summary_daily","summary_date,user_id,area");
        }
    }

    static void EnsureNotify(DbConnection c,DbTransaction? tx)
    {
        string g=SchemaSql.GuidType(c);string user=SchemaSql.Q(c,"auth","user");
        string notification=SchemaSql.Q(c,"notify","notification");
        string channel=SchemaSql.Q(c,"chat","channel");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'notify.notification',N'U') IS NULL
                CREATE TABLE {notification}(
                  id {g} NOT NULL,
                  user_id {g} NULL,
                  kind varchar(20) NOT NULL,
                  title_ar nvarchar(200) NOT NULL,
                  body_ar nvarchar(400) NULL,
                  status varchar(20) NOT NULL,
                  priority varchar(20) NOT NULL CONSTRAINT DF_notify_priority DEFAULT('normal'),
                  post_into_channel_id {g} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_notify_notification PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_notify_kind CHECK (kind IN ('general','custom','system','rider_app')),
                  CONSTRAINT CK_notify_status CHECK (status IN ('unread','read','closed')),
                  CONSTRAINT CK_notify_priority CHECK (priority IN ('low','normal','high','urgent')),
                  CONSTRAINT FK_notify_notification_user FOREIGN KEY (user_id) REFERENCES {user}(id),
                  CONSTRAINT FK_notify_notification_channel FOREIGN KEY (post_into_channel_id) REFERENCES {channel}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'notify.notification',N'U') IS NOT NULL
                  AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_notify_notification_channel' AND parent_object_id=OBJECT_ID(N'notify.notification'))
                ALTER TABLE {notification} ADD CONSTRAINT FK_notify_notification_channel FOREIGN KEY (post_into_channel_id) REFERENCES {channel}(id);
                """);
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'notify.subscription',N'U') IS NULL
                CREATE TABLE {SchemaSql.Q(c,"notify","subscription")}(
                  id {g} NOT NULL,
                  event_type varchar(80) NOT NULL,
                  audience_key varchar(80) NOT NULL,
                  channel varchar(20) NOT NULL,
                  enabled {SchemaSql.Bit(c)} NOT NULL CONSTRAINT DF_notify_sub_enabled DEFAULT(1),
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_notify_subscription PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_notify_subscription UNIQUE (event_type, audience_key, channel),
                  CONSTRAINT CK_notify_subscription_channel CHECK (channel IN ('in_app','push','email'))
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'notify.reply',N'U') IS NULL
                CREATE TABLE {SchemaSql.Q(c,"notify","reply")}(
                  id {g} NOT NULL,
                  notification_id {g} NOT NULL,
                  author_user_id {g} NOT NULL,
                  body_ar nvarchar(400) NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_notify_reply PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT FK_notify_reply_notification FOREIGN KEY (notification_id) REFERENCES {notification}(id),
                  CONSTRAINT FK_notify_reply_user FOREIGN KEY (author_user_id) REFERENCES {user}(id)
                );
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {notification}(
                  id {g} NOT NULL PRIMARY KEY,
                  user_id {g} NULL,
                  kind TEXT NOT NULL,
                  title_ar TEXT NOT NULL,
                  body_ar TEXT NULL,
                  status TEXT NOT NULL,
                  priority TEXT NOT NULL DEFAULT 'normal',
                  post_into_channel_id {g} NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_notify_kind CHECK (kind IN ('general','custom','system','rider_app')),
                  CONSTRAINT CK_notify_status CHECK (status IN ('unread','read','closed')),
                  CONSTRAINT CK_notify_priority CHECK (priority IN ('low','normal','high','urgent')),
                  CONSTRAINT FK_notify_notification_user FOREIGN KEY (user_id) REFERENCES {user}(id),
                  CONSTRAINT FK_notify_notification_channel FOREIGN KEY (post_into_channel_id) REFERENCES {channel}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {SchemaSql.Q(c,"notify","subscription")}(
                  id {g} NOT NULL PRIMARY KEY,
                  event_type TEXT NOT NULL,
                  audience_key TEXT NOT NULL,
                  channel TEXT NOT NULL,
                  enabled INTEGER NOT NULL DEFAULT 1,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_notify_subscription UNIQUE (event_type, audience_key, channel),
                  CONSTRAINT CK_notify_subscription_channel CHECK (channel IN ('in_app','push','email'))
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {SchemaSql.Q(c,"notify","reply")}(
                  id {g} NOT NULL PRIMARY KEY,
                  notification_id {g} NOT NULL,
                  author_user_id {g} NOT NULL,
                  body_ar TEXT NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT FK_notify_reply_notification FOREIGN KEY (notification_id) REFERENCES {notification}(id),
                  CONSTRAINT FK_notify_reply_user FOREIGN KEY (author_user_id) REFERENCES {user}(id)
                );
                """);
        }
        SchemaSql.CreateIndexIfMissing(c,tx,"notify","notification","IX_notify_notification_user_created","user_id, created_at");
        SchemaSql.CreateIndexIfMissing(c,tx,"notify","notification","IX_notify_notification_post_into_channel_id","post_into_channel_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"notify","reply","IX_notify_reply_notification_id","notification_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"notify","reply","IX_notify_reply_author_user_id","author_user_id");
    }

    static void EnsureChatChannel(DbConnection c,DbTransaction? tx)
    {
        string g=SchemaSql.GuidType(c);string channel=SchemaSql.Q(c,"chat","channel");
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'chat.channel',N'U') IS NULL
                CREATE TABLE {channel}(
                  id {g} NOT NULL,
                  code varchar(40) NOT NULL,
                  name_ar nvarchar(200) NOT NULL,
                  company_id {g} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_chat_channel PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_chat_channel_code UNIQUE (code)
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {channel}(
                  id {g} NOT NULL PRIMARY KEY,
                  code TEXT NOT NULL,
                  name_ar TEXT NOT NULL,
                  company_id {g} NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_chat_channel_code UNIQUE (code)
                );
                """);
    }

    static void EnsureTask(DbConnection c,DbTransaction? tx)
    {
        string g=SchemaSql.GuidType(c);string user=SchemaSql.Q(c,"auth","user");string item=SchemaSql.Q(c,"task","item");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'task.item',N'U') IS NULL
                CREATE TABLE {item}(
                  id {g} NOT NULL,
                  due_on {SchemaSql.Date(c)} NOT NULL,
                  assignee_user_id {g} NULL,
                  assignee_rank varchar(8) NULL,
                  status varchar(20) NOT NULL,
                  title_ar nvarchar(200) NOT NULL,
                  entity_type varchar(40) NULL,
                  entity_id {g} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_task_item PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_task_item_status CHECK (status IN ('open','in_progress','done','cancelled','escalated')),
                  CONSTRAINT FK_task_item_assignee FOREIGN KEY (assignee_user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'task.item') AND name=N'CX_task_item')
                CREATE CLUSTERED INDEX [CX_task_item] ON {item}(due_on, id);
                """);
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'task.comment',N'U') IS NULL
                CREATE TABLE {SchemaSql.Q(c,"task","comment")}(
                  id {g} NOT NULL,
                  task_id {g} NOT NULL,
                  author_user_id {g} NOT NULL,
                  body_ar nvarchar(400) NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_task_comment PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT FK_task_comment_item FOREIGN KEY (task_id) REFERENCES {item}(id),
                  CONSTRAINT FK_task_comment_user FOREIGN KEY (author_user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'task.attachment',N'U') IS NULL
                CREATE TABLE {SchemaSql.Q(c,"task","attachment")}(
                  id {g} NOT NULL,
                  task_id {g} NOT NULL,
                  file_hash {SchemaSql.Binary(c,32)} NOT NULL,
                  file_name nvarchar(200) NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_task_attachment PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT FK_task_attachment_item FOREIGN KEY (task_id) REFERENCES {item}(id)
                );
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {item}(
                  id {g} NOT NULL PRIMARY KEY,
                  due_on TEXT NOT NULL,
                  assignee_user_id {g} NULL,
                  assignee_rank TEXT NULL,
                  status TEXT NOT NULL,
                  title_ar TEXT NOT NULL,
                  entity_type TEXT NULL,
                  entity_id {g} NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_task_item_status CHECK (status IN ('open','in_progress','done','cancelled','escalated')),
                  CONSTRAINT FK_task_item_assignee FOREIGN KEY (assignee_user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"task","item","CX_task_item","due_on,id");
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {SchemaSql.Q(c,"task","comment")}(
                  id {g} NOT NULL PRIMARY KEY,
                  task_id {g} NOT NULL,
                  author_user_id {g} NOT NULL,
                  body_ar TEXT NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT FK_task_comment_item FOREIGN KEY (task_id) REFERENCES {item}(id),
                  CONSTRAINT FK_task_comment_user FOREIGN KEY (author_user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {SchemaSql.Q(c,"task","attachment")}(
                  id {g} NOT NULL PRIMARY KEY,
                  task_id {g} NOT NULL,
                  file_hash BLOB NOT NULL,
                  file_name TEXT NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT FK_task_attachment_item FOREIGN KEY (task_id) REFERENCES {item}(id)
                );
                """);
        }
        SchemaSql.CreateIndexIfMissing(c,tx,"task","item","IX_task_item_assignee_user_id","assignee_user_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"task","comment","IX_task_comment_task_id","task_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"task","comment","IX_task_comment_author_user_id","author_user_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"task","attachment","IX_task_attachment_task_id","task_id");
    }

    static void EnsureChat(DbConnection c,DbTransaction? tx)
    {
        string g=SchemaSql.GuidType(c);string user=SchemaSql.Q(c,"auth","user");
        string channel=SchemaSql.Q(c,"chat","channel");string message=SchemaSql.Q(c,"chat","message");
        EnsureChatChannel(c,tx);
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'chat.message',N'U') IS NULL
                CREATE TABLE {message}(
                  id {g} NOT NULL,
                  channel_id {g} NOT NULL,
                  author_user_id {g} NOT NULL,
                  body_ar nvarchar(max) NULL,
                  media_hash {SchemaSql.Binary(c,32)} NULL,
                  media_kind varchar(20) NULL,
                  created_at {SchemaSql.DateTime(c)} NOT NULL,
                  created_by {SchemaSql.Text(c,128)} NULL,
                  updated_by {SchemaSql.Text(c,128)} NULL,
                  updated_at {SchemaSql.DateTime(c)} NULL,
                  row_version {SchemaSql.Binary(c,8)} NULL,
                  CONSTRAINT PK_chat_message PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_chat_message_media CHECK (media_kind IS NULL OR media_kind IN ('image','voice')),
                  CONSTRAINT FK_chat_message_channel FOREIGN KEY (channel_id) REFERENCES {channel}(id),
                  CONSTRAINT FK_chat_message_author FOREIGN KEY (author_user_id) REFERENCES {user}(id)
                );
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {message}(
                  id {g} NOT NULL PRIMARY KEY,
                  channel_id {g} NOT NULL,
                  author_user_id {g} NOT NULL,
                  body_ar TEXT NULL,
                  media_hash BLOB NULL,
                  media_kind TEXT NULL,
                  created_at TEXT NOT NULL,
                  created_by TEXT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_chat_message_media CHECK (media_kind IS NULL OR media_kind IN ('image','voice')),
                  CONSTRAINT FK_chat_message_channel FOREIGN KEY (channel_id) REFERENCES {channel}(id),
                  CONSTRAINT FK_chat_message_author FOREIGN KEY (author_user_id) REFERENCES {user}(id)
                );
                """);
        }
        EnsureChatChildren(c,tx,message,user,g);
        SchemaSql.CreateIndexIfMissing(c,tx,"chat","message","IX_chat_message_channel_created","channel_id, created_at");
        SchemaSql.CreateIndexIfMissing(c,tx,"chat","message","IX_chat_message_author_user_id","author_user_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"chat","receipt","IX_chat_receipt_user_id","user_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"chat","moderation","IX_chat_moderation_message_id","message_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"chat","moderation","IX_chat_moderation_moderator_user_id","moderator_user_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"chat","block","IX_chat_block_blocked_user_id","blocked_user_id");
    }

    static void EnsureChatChildren(DbConnection c,DbTransaction? tx,string message,string user,string g)
    {
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'chat.receipt',N'U') IS NULL
                CREATE TABLE {SchemaSql.Q(c,"chat","receipt")}(
                  id {g} NOT NULL,
                  message_id {g} NOT NULL,
                  user_id {g} NOT NULL,
                  read_at {SchemaSql.DateTime(c)} NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_chat_receipt PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_chat_receipt UNIQUE (message_id, user_id),
                  CONSTRAINT FK_chat_receipt_message FOREIGN KEY (message_id) REFERENCES {message}(id),
                  CONSTRAINT FK_chat_receipt_user FOREIGN KEY (user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'chat.moderation',N'U') IS NULL
                CREATE TABLE {SchemaSql.Q(c,"chat","moderation")}(
                  id {g} NOT NULL,
                  message_id {g} NOT NULL,
                  moderator_user_id {g} NOT NULL,
                  action varchar(20) NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_chat_moderation PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_chat_moderation_action CHECK (action IN ('hide','delete','pin','announce')),
                  CONSTRAINT FK_chat_moderation_message FOREIGN KEY (message_id) REFERENCES {message}(id),
                  CONSTRAINT FK_chat_moderation_user FOREIGN KEY (moderator_user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'chat.block',N'U') IS NULL
                CREATE TABLE {SchemaSql.Q(c,"chat","block")}(
                  id {g} NOT NULL,
                  blocker_user_id {g} NOT NULL,
                  blocked_user_id {g} NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_chat_block PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_chat_block UNIQUE (blocker_user_id, blocked_user_id),
                  CONSTRAINT CK_chat_block_users CHECK (blocker_user_id <> blocked_user_id),
                  CONSTRAINT FK_chat_block_blocker FOREIGN KEY (blocker_user_id) REFERENCES {user}(id),
                  CONSTRAINT FK_chat_block_blocked FOREIGN KEY (blocked_user_id) REFERENCES {user}(id)
                );
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {SchemaSql.Q(c,"chat","receipt")}(
                  id {g} NOT NULL PRIMARY KEY,
                  message_id {g} NOT NULL,
                  user_id {g} NOT NULL,
                  read_at TEXT NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_chat_receipt UNIQUE (message_id, user_id),
                  CONSTRAINT FK_chat_receipt_message FOREIGN KEY (message_id) REFERENCES {message}(id),
                  CONSTRAINT FK_chat_receipt_user FOREIGN KEY (user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {SchemaSql.Q(c,"chat","moderation")}(
                  id {g} NOT NULL PRIMARY KEY,
                  message_id {g} NOT NULL,
                  moderator_user_id {g} NOT NULL,
                  action TEXT NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_chat_moderation_action CHECK (action IN ('hide','delete','pin','announce')),
                  CONSTRAINT FK_chat_moderation_message FOREIGN KEY (message_id) REFERENCES {message}(id),
                  CONSTRAINT FK_chat_moderation_user FOREIGN KEY (moderator_user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {SchemaSql.Q(c,"chat","block")}(
                  id {g} NOT NULL PRIMARY KEY,
                  blocker_user_id {g} NOT NULL,
                  blocked_user_id {g} NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_chat_block UNIQUE (blocker_user_id, blocked_user_id),
                  CONSTRAINT CK_chat_block_users CHECK (blocker_user_id <> blocked_user_id),
                  CONSTRAINT FK_chat_block_blocker FOREIGN KEY (blocker_user_id) REFERENCES {user}(id),
                  CONSTRAINT FK_chat_block_blocked FOREIGN KEY (blocked_user_id) REFERENCES {user}(id)
                );
                """);
        }
    }

    static void SeedActionLabels(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"audit","action_label");string now=SchemaSql.NowLiteral(c);
        foreach(var (code,name) in ActionLabels)
        {
            object? exists=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {t} WHERE action_code=@code",("@code",code));
            if(Convert.ToInt64(exists!)>0)continue;
            SchemaSql.Exec(c,tx,$"INSERT INTO {t}(id,action_code,name_ar,created_at) VALUES(@id,@code,@name,{now})",
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@code",code),("@name",name));
        }
    }
}
