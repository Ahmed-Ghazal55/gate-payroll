using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-17: form.* / app.* metadata tables. Keep forever (M-0363). Schema-only this phase; legacy kinds remain the only reader/writer until cutover.</summary>
public static class SchemaFormApp
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchemas(c,tx,"form","app");
        EnsureCampaign(c,tx);
        EnsureSubmission(c,tx);
        EnsureSubmissionHistory(c,tx);
        EnsureCustomForm(c,tx);
        EnsureCustomResponse(c,tx);
        EnsureMobileSession(c,tx);
        EnsureMobileBroadcast(c,tx);
        EnsureBroadcastAck(c,tx);
        EnsurePushOutbox(c,tx);
        EnsureRiderAsk(c,tx);
    }

    static void EnsureCampaign(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"form","campaign");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'form.campaign',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  title nvarchar(200) NOT NULL,
                  kind varchar(40) NOT NULL,
                  status varchar(20) NOT NULL,
                  sharing_token varchar(128) NULL,
                  token_hash varchar(128) NULL,
                  expires_at {SchemaSql.DateTime(c)} NULL,
                  fields_json {SchemaSql.Json(c)} NULL,
                  body_json {SchemaSql.Json(c)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_form_campaign PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_form_campaign_token UNIQUE (sharing_token),
                  CONSTRAINT CK_form_campaign_status CHECK (status IN ('draft','open','closed','archived'))
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  title TEXT NOT NULL,
                  kind TEXT NOT NULL,
                  status TEXT NOT NULL,
                  sharing_token TEXT NULL,
                  token_hash TEXT NULL,
                  expires_at TEXT NULL,
                  fields_json TEXT NULL,
                  body_json TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_form_campaign_token UNIQUE (sharing_token),
                  CONSTRAINT CK_form_campaign_status CHECK (status IN ('draft','open','closed','archived'))
                );
                """);
    }

    static void EnsureSubmission(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"form","submission");string g=SchemaSql.GuidType(c);string campaign=SchemaSql.Q(c,"form","campaign");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'form.submission',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  campaign_id {g} NOT NULL,
                  person_key varchar(64) NOT NULL,
                  status varchar(20) NOT NULL,
                  answers_json {SchemaSql.Json(c)} NULL,
                  body_json {SchemaSql.Json(c)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_form_submission PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_form_submission_person UNIQUE (campaign_id, person_key),
                  CONSTRAINT CK_form_submission_status CHECK (status IN ('pending','approved','rejected','archived')),
                  CONSTRAINT FK_form_submission_campaign FOREIGN KEY (campaign_id) REFERENCES {campaign}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"form","submission","IX_form_submission_campaign_id","campaign_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  campaign_id {g} NOT NULL,
                  person_key TEXT NOT NULL,
                  status TEXT NOT NULL,
                  answers_json TEXT NULL,
                  body_json TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_form_submission_person UNIQUE (campaign_id, person_key),
                  CONSTRAINT CK_form_submission_status CHECK (status IN ('pending','approved','rejected','archived')),
                  CONSTRAINT FK_form_submission_campaign FOREIGN KEY (campaign_id) REFERENCES {campaign}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"form","submission","IX_form_submission_campaign_id","campaign_id");
        }
    }

    static void EnsureSubmissionHistory(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"form","submission_history");string g=SchemaSql.GuidType(c);string sub=SchemaSql.Q(c,"form","submission");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'form.submission_history',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  submission_id {g} NOT NULL,
                  event_code varchar(40) NOT NULL,
                  body_json {SchemaSql.Json(c)} NULL,
                  occurred_at {SchemaSql.DateTime(c)} NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_form_submission_history PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT FK_form_history_submission FOREIGN KEY (submission_id) REFERENCES {sub}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"form","submission_history","IX_form_history_submission_id","submission_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  submission_id {g} NOT NULL,
                  event_code TEXT NOT NULL,
                  body_json TEXT NULL,
                  occurred_at TEXT NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT FK_form_history_submission FOREIGN KEY (submission_id) REFERENCES {sub}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"form","submission_history","IX_form_history_submission_id","submission_id");
        }
    }

    static void EnsureCustomForm(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"form","custom_form");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'form.custom_form',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  title_ar nvarchar(200) NOT NULL,
                  status varchar(20) NOT NULL,
                  sharing_token varchar(128) NULL,
                  expires_at {SchemaSql.DateTime(c)} NULL,
                  fields_json {SchemaSql.Json(c)} NULL,
                  body_json {SchemaSql.Json(c)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_form_custom_form PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_form_custom_form_token UNIQUE (sharing_token),
                  CONSTRAINT CK_form_custom_form_status CHECK (status IN ('draft','open','closed','archived'))
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  title_ar TEXT NOT NULL,
                  status TEXT NOT NULL,
                  sharing_token TEXT NULL,
                  expires_at TEXT NULL,
                  fields_json TEXT NULL,
                  body_json TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_form_custom_form_token UNIQUE (sharing_token),
                  CONSTRAINT CK_form_custom_form_status CHECK (status IN ('draft','open','closed','archived'))
                );
                """);
    }

    static void EnsureCustomResponse(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"form","custom_response");string g=SchemaSql.GuidType(c);string form=SchemaSql.Q(c,"form","custom_form");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'form.custom_response',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  form_id {g} NOT NULL,
                  person_key varchar(64) NOT NULL,
                  status varchar(20) NOT NULL,
                  answers_json {SchemaSql.Json(c)} NULL,
                  body_json {SchemaSql.Json(c)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_form_custom_response PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_form_custom_response_person UNIQUE (form_id, person_key),
                  CONSTRAINT CK_form_custom_response_status CHECK (status IN ('pending','approved','rejected','archived')),
                  CONSTRAINT FK_form_custom_response_form FOREIGN KEY (form_id) REFERENCES {form}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"form","custom_response","IX_form_custom_response_form_id","form_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  form_id {g} NOT NULL,
                  person_key TEXT NOT NULL,
                  status TEXT NOT NULL,
                  answers_json TEXT NULL,
                  body_json TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_form_custom_response_person UNIQUE (form_id, person_key),
                  CONSTRAINT CK_form_custom_response_status CHECK (status IN ('pending','approved','rejected','archived')),
                  CONSTRAINT FK_form_custom_response_form FOREIGN KEY (form_id) REFERENCES {form}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"form","custom_response","IX_form_custom_response_form_id","form_id");
        }
    }

    static void EnsureMobileSession(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"app","mobile_session");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'app.mobile_session',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  erp_person_id {g} NULL,
                  person_key varchar(64) NOT NULL,
                  token_hash varchar(128) NOT NULL,
                  status varchar(20) NOT NULL,
                  expires_at {SchemaSql.DateTime(c)} NULL,
                  last_seen_at {SchemaSql.DateTime(c)} NULL,
                  body_json {SchemaSql.Json(c)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_app_mobile_session PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_app_mobile_session_token UNIQUE (token_hash),
                  CONSTRAINT CK_app_mobile_session_status CHECK (status IN ('active','expired','revoked'))
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  erp_person_id {g} NULL,
                  person_key TEXT NOT NULL,
                  token_hash TEXT NOT NULL,
                  status TEXT NOT NULL,
                  expires_at TEXT NULL,
                  last_seen_at TEXT NULL,
                  body_json TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_app_mobile_session_token UNIQUE (token_hash),
                  CONSTRAINT CK_app_mobile_session_status CHECK (status IN ('active','expired','revoked'))
                );
                """);
        SchemaSql.CreateIndexIfMissing(c,tx,"app","mobile_session","IX_app_mobile_session_person_key","person_key");
    }

    static void EnsureMobileBroadcast(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"app","mobile_broadcast");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'app.mobile_broadcast',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  target_scope varchar(20) NOT NULL,
                  target_person_key varchar(64) NULL,
                  title nvarchar(200) NOT NULL,
                  body_json {SchemaSql.Json(c)} NULL,
                  status varchar(20) NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_app_mobile_broadcast PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_app_mobile_broadcast_scope CHECK (target_scope IN ('all','individual')),
                  CONSTRAINT CK_app_mobile_broadcast_status CHECK (status IN ('draft','sent','cancelled'))
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  target_scope TEXT NOT NULL,
                  target_person_key TEXT NULL,
                  title TEXT NOT NULL,
                  body_json TEXT NULL,
                  status TEXT NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_app_mobile_broadcast_scope CHECK (target_scope IN ('all','individual')),
                  CONSTRAINT CK_app_mobile_broadcast_status CHECK (status IN ('draft','sent','cancelled'))
                );
                """);
    }

    static void EnsureBroadcastAck(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"app","broadcast_ack");string g=SchemaSql.GuidType(c);string b=SchemaSql.Q(c,"app","mobile_broadcast");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'app.broadcast_ack',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  broadcast_id {g} NOT NULL,
                  person_key varchar(64) NOT NULL,
                  acked_at {SchemaSql.DateTime(c)} NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_app_broadcast_ack PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_app_broadcast_ack UNIQUE (broadcast_id, person_key),
                  CONSTRAINT FK_app_broadcast_ack FOREIGN KEY (broadcast_id) REFERENCES {b}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"app","broadcast_ack","IX_app_broadcast_ack_broadcast_id","broadcast_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  broadcast_id {g} NOT NULL,
                  person_key TEXT NOT NULL,
                  acked_at TEXT NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_app_broadcast_ack UNIQUE (broadcast_id, person_key),
                  CONSTRAINT FK_app_broadcast_ack FOREIGN KEY (broadcast_id) REFERENCES {b}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"app","broadcast_ack","IX_app_broadcast_ack_broadcast_id","broadcast_id");
        }
    }

    static void EnsurePushOutbox(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"app","push_outbox");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'app.push_outbox',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  person_key varchar(64) NULL,
                  payload_json {SchemaSql.Json(c)} NOT NULL,
                  status varchar(20) NOT NULL,
                  attempts {SchemaSql.Int(c)} NOT NULL CONSTRAINT DF_app_push_attempts DEFAULT(0),
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_app_push_outbox PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_app_push_outbox_status CHECK (status IN ('pending','sent','failed','cancelled'))
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  person_key TEXT NULL,
                  payload_json TEXT NOT NULL,
                  status TEXT NOT NULL,
                  attempts INTEGER NOT NULL DEFAULT 0,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_app_push_outbox_status CHECK (status IN ('pending','sent','failed','cancelled'))
                );
                """);
    }

    static void EnsureRiderAsk(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"app","rider_ask");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'app.rider_ask',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  person_key varchar(64) NOT NULL,
                  ask_type varchar(40) NOT NULL,
                  status varchar(20) NOT NULL,
                  body_json {SchemaSql.Json(c)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_app_rider_ask PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_app_rider_ask_status CHECK (status IN ('open','converted','declined','cancelled'))
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  person_key TEXT NOT NULL,
                  ask_type TEXT NOT NULL,
                  status TEXT NOT NULL,
                  body_json TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_app_rider_ask_status CHECK (status IN ('open','converted','declined','cancelled'))
                );
                """);
        SchemaSql.CreateIndexIfMissing(c,tx,"app","rider_ask","IX_app_rider_ask_person_key","person_key");
    }

}
