using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-18c: approvals.request/decision + auth.bypass_grant + auth.approval_limit.
/// Design §9.1 / BR §35 / M-0672 / M-0677. Dated bypass only; decision-time membership/delegation/bypass evidence.
/// Dated overlap guard replaces the revoked_at IS NULL unique so a later non-overlapping grant is allowed.
/// Seeds the M-0677 dated grant only when auth.user username ghazal already exists — never creates users.</summary>
public static partial class SchemaApprovals
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchemas(c,tx,"approvals","auth");
        // decision.delegation_id FK needs org.delegation (U-W1-18b) before this unit's DDL.
        SchemaOrgStructure.Ensure(c,tx);
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
        SchemaSql.CreateIndexIfMissing(c,tx,"auth","bypass_grant","IX_auth_bypass_granted_by","granted_by");
        SchemaSql.DropIndexIfExists(c,tx,"auth","bypass_grant","UQ_auth_bypass_active_scope");
        EnsureNoBypassOverlap(c,tx);
        SeedConfirmedBypassGrant(c,tx);
    }

    /// <summary>M-0677: insert-if-missing dated self-approval exception for username ghazal when that user row already exists.</summary>
    static void SeedConfirmedBypassGrant(DbConnection c,DbTransaction? tx)
    {
        string user=SchemaSql.Q(c,"auth","user");string grant=SchemaSql.Q(c,"auth","bypass_grant");
        object? userId=SchemaSql.Scalar(c,tx,$"SELECT id FROM {user} WHERE username=@name",("@name","ghazal"));
        if(userId is null or DBNull)return;
        string idText=userId switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(userId)!};
        const string scope="pay.approvals";
        const string reason="Ninja October parallel (payout_blocked=1); revoke before first real payment";
        const string validFrom="2026-10-01T00:00:00";
        const string validTo="2026-12-31T23:59:59";
        object? exists=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {grant} WHERE user_id=@u AND scope=@scope AND valid_to=@to",
            ("@u",idText),("@scope",scope),("@to",validTo));
        if(Convert.ToInt64(exists!)>0)return;
        SchemaSql.Exec(c,tx,$"INSERT INTO {grant}(id,user_id,scope,valid_from,valid_to,reason,granted_by,revoked_at,created_at) VALUES(@id,@u,@scope,@from,@to,@reason,@u,NULL,{SchemaSql.NowLiteral(c)})",
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@u",idText),("@scope",scope),("@from",validFrom),("@to",validTo),("@reason",reason));
    }

    /// <summary>Unrevoked bypass grants for one user and scope may not cover overlapping windows; expired unrevoked rows may be followed by a later grant.</summary>
    static void EnsureNoBypassOverlap(DbConnection c,DbTransaction? tx)
    {
        string grant=SchemaSql.Q(c,"auth","bypass_grant");
        const string message="Bypass grant date ranges for one user and scope cannot overlap";
        string Overlap(string n)=>$"EXISTS(SELECT 1 FROM {grant} x WHERE x.id<>{n}.id AND x.user_id={n}.user_id AND x.scope={n}.scope AND x.revoked_at IS NULL AND {n}.revoked_at IS NULL AND x.valid_from<={n}.valid_to AND {n}.valid_from<=x.valid_to)";
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER auth.TR_bypass_grant_overlap ON {grant} AFTER INSERT, UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted n WHERE n.revoked_at IS NULL AND {Overlap("n")})
                    THROW 51041,'{message}',1;
                END;
                """);
        else
            foreach(bool insert in new[]{true,false})
                SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_auth_bypass_grant_overlap_{(insert?"insert":"update")} AFTER {(insert?"INSERT":"UPDATE OF user_id,scope,valid_from,valid_to,revoked_at")} ON {grant} WHEN NEW.revoked_at IS NULL AND {Overlap("NEW")} BEGIN SELECT RAISE(ABORT,'{message}'); END;");
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
        SchemaSql.CreateIndexIfMissing(c,tx,"approvals","request","IX_approvals_request_requested_by","requested_by");
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
        SchemaSql.CreateIndexIfMissing(c,tx,"approvals","decision","IX_approvals_decision_decided_by","decided_by");
        SchemaSql.CreateIndexIfMissing(c,tx,"approvals","decision","IX_approvals_decision_delegation_id","delegation_id");
    }
}
