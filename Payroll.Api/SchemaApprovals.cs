using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-18c: approvals.request/decision + auth.bypass_grant + auth.approval_limit.
/// Design §9.1 / BR §35 / M-0672 / M-0677. Dated bypass only; decision-time membership/delegation/bypass evidence.
/// Does not seed a live bypass row (auth.user catalog stays empty until cutover).</summary>
public static class SchemaApprovals
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchemas(c,tx,"approvals","auth");
        EnsureApprovalLimit(c,tx);
        EnsureBypassGrant(c,tx);
        EnsureRequest(c,tx);
        EnsureDecision(c,tx);
    }

    static void EnsureApprovalLimit(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"auth","approval_limit");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'auth.approval_limit',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  rank varchar(8) NOT NULL,
                  department varchar(40) NOT NULL,
                  limit_amount {SchemaSql.Decimal(c)} NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_auth_approval_limit PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_auth_approval_limit UNIQUE (rank, department),
                  CONSTRAINT CK_auth_approval_limit_amount CHECK (limit_amount >= 0)
                );
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  rank TEXT NOT NULL,
                  department TEXT NOT NULL,
                  limit_amount REAL NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_auth_approval_limit UNIQUE (rank, department),
                  CONSTRAINT CK_auth_approval_limit_amount CHECK (limit_amount >= 0)
                );
                """);
        }
    }

    static void EnsureBypassGrant(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"auth","bypass_grant");string g=SchemaSql.GuidType(c);string user=SchemaSql.Q(c,"auth","user");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'auth.bypass_grant',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  user_id {g} NOT NULL,
                  scope varchar(80) NOT NULL,
                  valid_from {SchemaSql.DateTime(c)} NOT NULL,
                  valid_to {SchemaSql.DateTime(c)} NOT NULL,
                  reason nvarchar(400) NOT NULL,
                  granted_by {g} NOT NULL,
                  revoked_at {SchemaSql.DateTime(c)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_auth_bypass_grant PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_bypass_dates CHECK (valid_to > valid_from),
                  CONSTRAINT FK_auth_bypass_user FOREIGN KEY (user_id) REFERENCES {user}(id),
                  CONSTRAINT FK_auth_bypass_granted_by FOREIGN KEY (granted_by) REFERENCES {user}(id)
                );
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  user_id {g} NOT NULL,
                  scope TEXT NOT NULL,
                  valid_from TEXT NOT NULL,
                  valid_to TEXT NOT NULL,
                  reason TEXT NOT NULL,
                  granted_by {g} NOT NULL,
                  revoked_at TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_bypass_dates CHECK (valid_to > valid_from),
                  CONSTRAINT FK_auth_bypass_user FOREIGN KEY (user_id) REFERENCES {user}(id),
                  CONSTRAINT FK_auth_bypass_granted_by FOREIGN KEY (granted_by) REFERENCES {user}(id)
                );
                """);
        }
        SchemaSql.CreateIndexIfMissing(c,tx,"auth","bypass_grant","IX_auth_bypass_user_id","user_id");
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"auth","bypass_grant","UQ_auth_bypass_active_scope","user_id, scope","revoked_at IS NULL");
    }

    static void EnsureRequest(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"approvals","request");string g=SchemaSql.GuidType(c);string user=SchemaSql.Q(c,"auth","user");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'approvals.request',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  kind varchar(80) NOT NULL,
                  title_ar nvarchar(200) NOT NULL,
                  body_ar nvarchar(400) NULL,
                  from_department varchar(40) NULL,
                  target_department varchar(40) NOT NULL,
                  required_rank_level {SchemaSql.Int(c)} NOT NULL CONSTRAINT DF_approvals_req_rank DEFAULT(1),
                  reference_id {g} NULL,
                  status varchar(20) NOT NULL,
                  payload_json {SchemaSql.Json(c)} NULL,
                  requested_by {g} NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_approvals_request PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_approvals_request_status CHECK (status IN ('pending','approved','rejected','cancelled','escalated')),
                  CONSTRAINT CK_approvals_request_rank CHECK (required_rank_level BETWEEN 1 AND 5),
                  CONSTRAINT FK_approvals_request_user FOREIGN KEY (requested_by) REFERENCES {user}(id)
                );
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  kind TEXT NOT NULL,
                  title_ar TEXT NOT NULL,
                  body_ar TEXT NULL,
                  from_department TEXT NULL,
                  target_department TEXT NOT NULL,
                  required_rank_level INTEGER NOT NULL DEFAULT 1,
                  reference_id {g} NULL,
                  status TEXT NOT NULL,
                  payload_json TEXT NULL,
                  requested_by {g} NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_approvals_request_status CHECK (status IN ('pending','approved','rejected','cancelled','escalated')),
                  CONSTRAINT CK_approvals_request_rank CHECK (required_rank_level BETWEEN 1 AND 5),
                  CONSTRAINT FK_approvals_request_user FOREIGN KEY (requested_by) REFERENCES {user}(id)
                );
                """);
        }
        SchemaSql.CreateIndexIfMissing(c,tx,"approvals","request","IX_approvals_request_kind","kind");
        SchemaSql.CreateIndexIfMissing(c,tx,"approvals","request","IX_approvals_request_status","status");
    }

    static void EnsureDecision(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"approvals","decision");string g=SchemaSql.GuidType(c);
        string request=SchemaSql.Q(c,"approvals","request");
        string user=SchemaSql.Q(c,"auth","user");
        string bypass=SchemaSql.Q(c,"auth","bypass_grant");
        string delegation=SchemaSql.Q(c,"org","delegation");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'approvals.decision',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  request_id {g} NOT NULL,
                  decided_by {g} NOT NULL,
                  decided_at {SchemaSql.DateTime(c)} NOT NULL,
                  outcome varchar(20) NOT NULL,
                  membership_snapshot_json nvarchar(max) NOT NULL,
                  delegation_id {g} NULL,
                  bypass_grant_id {g} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_approvals_decision PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_approvals_decision_outcome CHECK (outcome IN ('approved','rejected','held','escalated')),
                  CONSTRAINT FK_approvals_decision_request FOREIGN KEY (request_id) REFERENCES {request}(id),
                  CONSTRAINT FK_approvals_decision_user FOREIGN KEY (decided_by) REFERENCES {user}(id),
                  CONSTRAINT FK_approvals_decision_bypass FOREIGN KEY (bypass_grant_id) REFERENCES {bypass}(id),
                  CONSTRAINT FK_approvals_decision_delegation FOREIGN KEY (delegation_id) REFERENCES {delegation}(id)
                );
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  request_id {g} NOT NULL,
                  decided_by {g} NOT NULL,
                  decided_at TEXT NOT NULL,
                  outcome TEXT NOT NULL,
                  membership_snapshot_json TEXT NOT NULL,
                  delegation_id {g} NULL,
                  bypass_grant_id {g} NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_approvals_decision_outcome CHECK (outcome IN ('approved','rejected','held','escalated')),
                  CONSTRAINT FK_approvals_decision_request FOREIGN KEY (request_id) REFERENCES {request}(id),
                  CONSTRAINT FK_approvals_decision_user FOREIGN KEY (decided_by) REFERENCES {user}(id),
                  CONSTRAINT FK_approvals_decision_bypass FOREIGN KEY (bypass_grant_id) REFERENCES {bypass}(id),
                  CONSTRAINT FK_approvals_decision_delegation FOREIGN KEY (delegation_id) REFERENCES {delegation}(id)
                );
                """);
        }
        SchemaSql.CreateIndexIfMissing(c,tx,"approvals","decision","IX_approvals_decision_request_id","request_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"approvals","decision","IX_approvals_decision_bypass_grant_id","bypass_grant_id");
    }
}
