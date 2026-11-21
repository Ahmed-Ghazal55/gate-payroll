using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>WC1F / U-W1-18c: auth.approval_limit, org.sla_policy, approvals.decision (design §9 / §9.1 / draft 67).
/// Additive Ensure for both providers; replaces generic document-kind storage for these three.</summary>
public static class SchemaApprovals
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchema(c,tx,"auth");
        SchemaSql.EnsureSchema(c,tx,"org");
        SchemaSql.EnsureSchema(c,tx,"approvals");
        EnsureApprovalLimit(c,tx);
        EnsureSlaPolicy(c,tx);
        EnsureDecision(c,tx);
    }

    static void EnsureApprovalLimit(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"auth","approval_limit");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'auth.approval_limit',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  rank_code varchar(8) NOT NULL,
                  department varchar(40) NOT NULL,
                  limit_sar {SchemaSql.Decimal(c)} NULL,
                  unlimited {SchemaSql.Bit(c)} NOT NULL CONSTRAINT DF_auth_limit_unlimited DEFAULT(0),
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_auth_approval_limit PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_auth_approval_limit UNIQUE (rank_code, department),
                  CONSTRAINT CK_auth_approval_limit_sar CHECK (limit_sar IS NULL OR limit_sar>=0)
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  rank_code TEXT NOT NULL,
                  department TEXT NOT NULL,
                  limit_sar REAL NULL,
                  unlimited INTEGER NOT NULL DEFAULT 0,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_auth_approval_limit UNIQUE (rank_code, department),
                  CONSTRAINT CK_auth_approval_limit_sar CHECK (limit_sar IS NULL OR limit_sar>=0)
                );
                """);
    }

    static void EnsureSlaPolicy(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"org","sla_policy");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'org.sla_policy',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  request_type varchar(80) NOT NULL,
                  working_days {SchemaSql.Int(c)} NOT NULL CONSTRAINT DF_org_sla_days DEFAULT(3),
                  auto_approve {SchemaSql.Bit(c)} NOT NULL CONSTRAINT DF_org_sla_auto DEFAULT(0),
                  valid_from {SchemaSql.DateTime(c)} NOT NULL,
                  valid_to {SchemaSql.DateTime(c)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_org_sla_policy PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_org_sla_policy_type UNIQUE (request_type),
                  CONSTRAINT CK_org_sla_days CHECK (working_days BETWEEN 1 AND 30),
                  CONSTRAINT CK_org_sla_auto CHECK (auto_approve=0)
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  request_type TEXT NOT NULL,
                  working_days INTEGER NOT NULL DEFAULT 3,
                  auto_approve INTEGER NOT NULL DEFAULT 0,
                  valid_from TEXT NOT NULL,
                  valid_to TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_org_sla_policy_type UNIQUE (request_type),
                  CONSTRAINT CK_org_sla_days CHECK (working_days BETWEEN 1 AND 30),
                  CONSTRAINT CK_org_sla_auto CHECK (auto_approve=0)
                );
                """);
    }

    static void EnsureDecision(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"approvals","decision");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'approvals.decision',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  request_id varchar(128) NOT NULL,
                  decided_by varchar(128) NOT NULL,
                  decided_at {SchemaSql.DateTime(c)} NOT NULL,
                  outcome varchar(20) NOT NULL,
                  membership_snapshot_json {SchemaSql.Json(c)} NOT NULL,
                  delegation_id {g} NULL,
                  bypass_grant_id varchar(128) NULL,
                  note_ar nvarchar(500) NULL,
                  dual_permission varchar(80) NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_approvals_decision PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT CK_approvals_decision_outcome CHECK (outcome IN ('approved','rejected','suspended'))
                );
                """);
        else
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  request_id TEXT NOT NULL,
                  decided_by TEXT NOT NULL,
                  decided_at TEXT NOT NULL,
                  outcome TEXT NOT NULL,
                  membership_snapshot_json TEXT NOT NULL,
                  delegation_id {g} NULL,
                  bypass_grant_id TEXT NULL,
                  note_ar TEXT NULL,
                  dual_permission TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT CK_approvals_decision_outcome CHECK (outcome IN ('approved','rejected','suspended'))
                );
                """);
        SchemaSql.CreateIndexIfMissing(c,tx,"approvals","decision","IX_approvals_decision_request","request_id, decided_at");
    }

    public static bool HasLimits(DbConnection c,DbTransaction? tx)
        =>SchemaSql.TableExists(c,tx,"auth","approval_limit")
          &&Convert.ToInt64(SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"auth","approval_limit")}")!)>0;

    public static bool HasSla(DbConnection c,DbTransaction? tx)
        =>SchemaSql.TableExists(c,tx,"org","sla_policy")
          &&Convert.ToInt64(SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"org","sla_policy")}")!)>0;

    public static List<JsonObject> ListLimits(DbConnection c,DbTransaction? tx)
    {
        if(!SchemaSql.TableExists(c,tx,"auth","approval_limit"))return [];
        var rows=new List<JsonObject>();
        using var cmd=Store.Command(c,tx,$"SELECT id,rank_code,department,limit_sar,unlimited FROM {SchemaSql.Q(c,"auth","approval_limit")}");
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
        {
            bool unlimited=Convert.ToInt32(reader["unlimited"])==1||reader["limit_sar"] is DBNull;
            rows.Add(new()
            {
                ["id"]=reader["id"].ToString(),
                ["rank"]=reader["rank_code"].ToString(),
                ["department"]=reader["department"].ToString(),
                ["limitSar"]=unlimited?null:JsonValue.Create(Convert.ToDecimal(reader["limit_sar"])),
                ["unlimited"]=unlimited
            });
        }
        return rows;
    }

    public static JsonObject? FindLimit(DbConnection c,DbTransaction? tx,string rank,string department)
        =>ListLimits(c,tx).FirstOrDefault(l=>l.S("rank")==rank&&l.S("department")==department);

    public static void UpsertLimit(DbConnection c,DbTransaction tx,JsonObject row)
    {
        if(!SchemaSql.TableExists(c,tx,"auth","approval_limit"))return;
        // Keep document-kind id on the JSON row; SQL table uses its own GUID PK.
        string sqlId=existingLimitId(c,tx,row.S("rank"),row.S("department"))??SchemaSql.GuidText(SchemaSql.NewId());
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"auth","approval_limit")} WHERE rank_code=@r AND department=@d",
            ("@r",row.S("rank")),("@d",row.S("department")));
        if(existing==null)
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"auth","approval_limit")}
                (id,rank_code,department,limit_sar,unlimited,created_at,updated_by,updated_at)
                VALUES(@id,@r,@d,@lim,@u,@at,@by,@at)
                """,
                ("@id",sqlId),("@r",row.S("rank")),("@d",row.S("department")),
                ("@lim",(object?)(row.B("unlimited")||row["limitSar"] is null?null:row.N("limitSar"))??DBNull.Value),
                ("@u",row.B("unlimited")||row["limitSar"] is null?1:0),
                ("@at",now),("@by",(object?)row.S("updatedBy")??DBNull.Value));
        else
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"auth","approval_limit")}
                SET limit_sar=@lim, unlimited=@u, updated_by=@by, updated_at=@at
                WHERE rank_code=@r AND department=@d
                """,
                ("@r",row.S("rank")),("@d",row.S("department")),
                ("@lim",(object?)(row.B("unlimited")||row["limitSar"] is null?null:row.N("limitSar"))??DBNull.Value),
                ("@u",row.B("unlimited")||row["limitSar"] is null?1:0),
                ("@at",now),("@by",(object?)row.S("updatedBy")??DBNull.Value));
    }

    static string? existingLimitId(DbConnection c,DbTransaction tx,string rank,string department)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"auth","approval_limit")} WHERE rank_code=@r AND department=@d",
            ("@r",rank),("@d",department));
        return id?.ToString();
    }

    public static List<JsonObject> ListSla(DbConnection c,DbTransaction? tx)
    {
        if(!SchemaSql.TableExists(c,tx,"org","sla_policy"))return [];
        var rows=new List<JsonObject>();
        using var cmd=Store.Command(c,tx,$"SELECT id,request_type,working_days,auto_approve FROM {SchemaSql.Q(c,"org","sla_policy")} WHERE valid_to IS NULL");
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
            rows.Add(new()
            {
                ["id"]=reader["id"].ToString(),
                ["requestType"]=reader["request_type"].ToString(),
                ["workingDays"]=Convert.ToInt32(reader["working_days"]),
                ["autoApprove"]=false
            });
        return rows;
    }

    public static void UpsertSla(DbConnection c,DbTransaction tx,JsonObject row)
    {
        if(!SchemaSql.TableExists(c,tx,"org","sla_policy"))return;
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"org","sla_policy")} WHERE request_type=@t",("@t",row.S("requestType")));
        string sqlId=existing?.ToString()??SchemaSql.GuidText(SchemaSql.NewId());
        if(existing==null)
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"org","sla_policy")}
                (id,request_type,working_days,auto_approve,valid_from,created_at,updated_by,updated_at)
                VALUES(@id,@t,@d,0,@at,@at,@by,@at)
                """,
                ("@id",sqlId),("@t",row.S("requestType")),("@d",(int)row.N("workingDays",3)),
                ("@at",now),("@by",(object?)row.S("updatedBy")??DBNull.Value));
        else
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"org","sla_policy")}
                SET working_days=@d, auto_approve=0, updated_by=@by, updated_at=@at
                WHERE request_type=@t
                """,
                ("@t",row.S("requestType")),("@d",(int)row.N("workingDays",3)),
                ("@at",now),("@by",(object?)row.S("updatedBy")??DBNull.Value));
    }

    public static void SaveDecision(DbConnection c,DbTransaction tx,JsonObject row)
    {
        Store.Save(c,ApprovalsArea.DecisionKind,row,tx);
        if(!SchemaSql.TableExists(c,tx,"approvals","decision"))return;
        // SQL uniqueidentifier needs D-format; document-kind ids may be 32-hex N-format.
        string sqlId=SchemaSql.GuidText(SchemaSql.NewId());
        string now=row.S("decidedAt",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));
        string snapshot=(row["membershipSnapshot"] as JsonObject)?.ToJsonString()??"{}";
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"approvals","decision")}
            (id,request_id,decided_by,decided_at,outcome,membership_snapshot_json,bypass_grant_id,note_ar,dual_permission,created_at)
            VALUES(@id,@req,@by,@at,@out,@snap,@grant,@note,@dual,@at)
            """,
            ("@id",sqlId),("@req",row.S("requestId")),("@by",row.S("decidedBy")),("@at",now),
            ("@out",row.S("outcome")),("@snap",snapshot),
            ("@grant",(object?)(string.IsNullOrWhiteSpace(row.S("bypassGrantId"))?null:row.S("bypassGrantId"))??DBNull.Value),
            ("@note",(object?)row.S("note")??DBNull.Value),
            ("@dual",(object?)(string.IsNullOrWhiteSpace(row.S("dualPermission"))?null:row.S("dualPermission"))??DBNull.Value));
    }
}
