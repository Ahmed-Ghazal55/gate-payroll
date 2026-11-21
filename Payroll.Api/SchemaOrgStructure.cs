using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-18b: remaining org.* (non-CC). Design §9 / §13 step 12 / admin draft §8.
/// Append-only end dates: membership/delegation/supervision/routing close by setting valid_to / ended_at — never delete.</summary>
public static class SchemaOrgStructure
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchema(c,tx,"org");
        SchemaSql.EnsureSchema(c,tx,"auth");
        EnsureMembership(c,tx);
        EnsureDelegation(c,tx);
        EnsureSupervision(c,tx);
        EnsureRouting(c,tx);
        EnsureSlaPolicy(c,tx);
    }

    static void EnsureMembership(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"org","membership");string g=SchemaSql.GuidType(c);string user=SchemaSql.Q(c,"auth","user");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'org.membership',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  user_id {g} NOT NULL,
                  department varchar(40) NOT NULL,
                  rank varchar(8) NOT NULL,
                  rank_level {SchemaSql.Int(c)} NOT NULL,
                  valid_from {SchemaSql.Date(c)} NOT NULL,
                  valid_to {SchemaSql.Date(c)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_org_membership PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_org_membership_rank CHECK (rank IN ('C','B','A','A+')),
                  CONSTRAINT CK_org_membership_rank_level CHECK (rank_level BETWEEN 1 AND 4),
                  CONSTRAINT CK_org_membership_dates CHECK (valid_to IS NULL OR valid_to >= valid_from),
                  CONSTRAINT FK_org_membership_user FOREIGN KEY (user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'org.membership') AND name=N'CX_org_membership')
                CREATE CLUSTERED INDEX [CX_org_membership] ON {t}(valid_from, id);
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  user_id {g} NOT NULL,
                  department TEXT NOT NULL,
                  rank TEXT NOT NULL,
                  rank_level INTEGER NOT NULL,
                  valid_from TEXT NOT NULL,
                  valid_to TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_org_membership_rank CHECK (rank IN ('C','B','A','A+')),
                  CONSTRAINT CK_org_membership_rank_level CHECK (rank_level BETWEEN 1 AND 4),
                  CONSTRAINT CK_org_membership_dates CHECK (valid_to IS NULL OR valid_to >= valid_from),
                  CONSTRAINT FK_org_membership_user FOREIGN KEY (user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"org","membership","CX_org_membership","valid_from,id");
        }
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"org","membership","UQ_org_membership_active","user_id, department","valid_to IS NULL");
        SchemaSql.CreateIndexIfMissing(c,tx,"org","membership","IX_org_membership_user_id","user_id");
    }

    static void EnsureDelegation(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"org","delegation");string g=SchemaSql.GuidType(c);string user=SchemaSql.Q(c,"auth","user");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'org.delegation',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  from_user_id {g} NOT NULL,
                  to_user_id {g} NOT NULL,
                  operation varchar(80) NOT NULL,
                  valid_from {SchemaSql.Date(c)} NOT NULL,
                  until_date {SchemaSql.Date(c)} NOT NULL,
                  ended_at {SchemaSql.DateTime(c)} NULL,
                  reason nvarchar(400) NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_org_delegation PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_org_delegation_users CHECK (from_user_id <> to_user_id),
                  CONSTRAINT CK_org_delegation_dates CHECK (until_date >= valid_from),
                  CONSTRAINT FK_org_delegation_from FOREIGN KEY (from_user_id) REFERENCES {user}(id),
                  CONSTRAINT FK_org_delegation_to FOREIGN KEY (to_user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'org.delegation') AND name=N'CX_org_delegation')
                CREATE CLUSTERED INDEX [CX_org_delegation] ON {t}(valid_from, id);
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  from_user_id {g} NOT NULL,
                  to_user_id {g} NOT NULL,
                  operation TEXT NOT NULL,
                  valid_from TEXT NOT NULL,
                  until_date TEXT NOT NULL,
                  ended_at TEXT NULL,
                  reason TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_org_delegation_users CHECK (from_user_id <> to_user_id),
                  CONSTRAINT CK_org_delegation_dates CHECK (until_date >= valid_from),
                  CONSTRAINT FK_org_delegation_from FOREIGN KEY (from_user_id) REFERENCES {user}(id),
                  CONSTRAINT FK_org_delegation_to FOREIGN KEY (to_user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"org","delegation","CX_org_delegation","valid_from,id");
        }
        SchemaSql.CreateIndexIfMissing(c,tx,"org","delegation","IX_org_delegation_from","from_user_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"org","delegation","IX_org_delegation_to","to_user_id");
    }

    static void EnsureSupervision(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"org","supervision");string g=SchemaSql.GuidType(c);string user=SchemaSql.Q(c,"auth","user");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'org.supervision',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  supervisor_user_id {g} NOT NULL,
                  rider_iqama varchar(20) NOT NULL,
                  valid_from {SchemaSql.Date(c)} NOT NULL,
                  valid_to {SchemaSql.Date(c)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_org_supervision PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_org_supervision_dates CHECK (valid_to IS NULL OR valid_to >= valid_from),
                  CONSTRAINT FK_org_supervision_user FOREIGN KEY (supervisor_user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'org.supervision') AND name=N'CX_org_supervision')
                CREATE CLUSTERED INDEX [CX_org_supervision] ON {t}(valid_from, id);
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  supervisor_user_id {g} NOT NULL,
                  rider_iqama TEXT NOT NULL,
                  valid_from TEXT NOT NULL,
                  valid_to TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_org_supervision_dates CHECK (valid_to IS NULL OR valid_to >= valid_from),
                  CONSTRAINT FK_org_supervision_user FOREIGN KEY (supervisor_user_id) REFERENCES {user}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"org","supervision","CX_org_supervision","valid_from,id");
        }
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"org","supervision","UQ_org_supervision_active","supervisor_user_id, rider_iqama","valid_to IS NULL");
        SchemaSql.CreateIndexIfMissing(c,tx,"org","supervision","IX_org_supervision_supervisor","supervisor_user_id");
    }

    static void EnsureRouting(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"org","routing");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'org.routing',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  operation varchar(80) NOT NULL,
                  department varchar(40) NOT NULL,
                  min_rank varchar(8) NOT NULL,
                  min_rank_level {SchemaSql.Int(c)} NOT NULL,
                  users_json {SchemaSql.Json(c)} NOT NULL,
                  valid_from {SchemaSql.Date(c)} NOT NULL,
                  valid_to {SchemaSql.Date(c)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_org_routing PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_org_routing_rank CHECK (min_rank IN ('C','B','A','A+')),
                  CONSTRAINT CK_org_routing_rank_level CHECK (min_rank_level BETWEEN 1 AND 4),
                  CONSTRAINT CK_org_routing_dates CHECK (valid_to IS NULL OR valid_to >= valid_from)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'org.routing') AND name=N'CX_org_routing')
                CREATE CLUSTERED INDEX [CX_org_routing] ON {t}(valid_from, id);
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  operation TEXT NOT NULL,
                  department TEXT NOT NULL,
                  min_rank TEXT NOT NULL,
                  min_rank_level INTEGER NOT NULL,
                  users_json TEXT NOT NULL,
                  valid_from TEXT NOT NULL,
                  valid_to TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_org_routing_rank CHECK (min_rank IN ('C','B','A','A+')),
                  CONSTRAINT CK_org_routing_rank_level CHECK (min_rank_level BETWEEN 1 AND 4),
                  CONSTRAINT CK_org_routing_dates CHECK (valid_to IS NULL OR valid_to >= valid_from)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"org","routing","CX_org_routing","valid_from,id");
        }
        SchemaSql.CreateIndexIfMissing(c,tx,"org","routing","IX_org_routing_operation","operation");
    }

    static void EnsureSlaPolicy(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"org","sla_policy");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'org.sla_policy',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  request_type varchar(80) NOT NULL,
                  working_days {SchemaSql.Int(c)} NOT NULL CONSTRAINT DF_org_sla_working_days DEFAULT(3),
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_org_sla_policy PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_org_sla_policy_type UNIQUE (request_type),
                  CONSTRAINT CK_org_sla_working_days CHECK (working_days >= 1)
                );
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  request_type TEXT NOT NULL,
                  working_days INTEGER NOT NULL DEFAULT 3,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_org_sla_policy_type UNIQUE (request_type),
                  CONSTRAINT CK_org_sla_working_days CHECK (working_days >= 1)
                );
                """);
        }
    }
}
