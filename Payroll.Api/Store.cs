using System.Data;
using System.Data.Common;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
namespace Payroll;

// Additive document schema. Existing records and SQLite files remain readable by v0.4.
public sealed partial class Store
{
    public string DirectoryPath { get; }
    public string Provider { get; }
    public bool DataPreservationVerified { get; private set; }
    public bool ReleaseCheckpointPresent => File.Exists(Path.Combine(DirectoryPath,"release-backups",ReleaseSafety.Version,"checkpoint.json"));
    private readonly string connection;
    private readonly object writeGate = new();
    private long revision;
    public long Revision => Interlocked.Read(ref revision);
    public Store(string dir, string? sqlConnection = null)
    {
        DirectoryPath = Path.GetFullPath(dir); Directory.CreateDirectory(DirectoryPath);
        Provider = string.IsNullOrWhiteSpace(sqlConnection) ? "sqlite" : "mssql";
        if (Provider == "mssql")
        {
            var config = new SqlConnectionStringBuilder(sqlConnection!);
            if (string.IsNullOrWhiteSpace(config.DataSource) || string.IsNullOrWhiteSpace(config.InitialCatalog))
                throw new InvalidOperationException("SQL Server requires Data Source and Initial Catalog. Invalid configured SQL does not fall back to a different database.");
            connection = config.ConnectionString;
        }
        else connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(DirectoryPath, "payroll.db"), Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 30 }.ToString();
        using var c = Open(); var beforeUpgrade=ReleaseSafety.Checkpoint(c,DirectoryPath);
        string recordsDdl = Provider == "sqlite" ? """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_records_kind ON records(kind);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_run_period ON records(json_extract(body,'$.period'),json_extract(body,'$.iqama')) WHERE kind='run';
            CREATE UNIQUE INDEX IF NOT EXISTS ix_form_response ON records(json_extract(body,'$.campaignId'),json_extract(body,'$.iqama')) WHERE kind='submission';
            CREATE UNIQUE INDEX IF NOT EXISTS ix_user_name ON records(lower(json_extract(body,'$.username'))) WHERE kind='user';
            CREATE INDEX IF NOT EXISTS ix_debt_iqama ON records(json_extract(body,'$.iqama'),json_extract(body,'$.status')) WHERE kind='debt';
            CREATE INDEX IF NOT EXISTS ix_journal_sponsor ON records(json_extract(body,'$.creditorSponsorId'),json_extract(body,'$.period')) WHERE kind='journal';
            CREATE INDEX IF NOT EXISTS ix_company_children ON records(kind,json_extract(body,'$.companyId')) WHERE kind IN ('commercial_registration','company_document');
            CREATE INDEX IF NOT EXISTS ix_rider_doc_iqama ON records(json_extract(body,'$.iqama')) WHERE kind='rider_document';
            CREATE INDEX IF NOT EXISTS ix_pay_cycle_line_rider_period ON records(json_extract(body,'$.rider'),json_extract(body,'$.period')) WHERE kind='pay_cycle_line';
            CREATE INDEX IF NOT EXISTS ix_pay_cycle_line_period ON records(json_extract(body,'$.period')) WHERE kind='pay_cycle_line';
            CREATE INDEX IF NOT EXISTS ix_pay_cycle_period ON records(json_extract(body,'$.period')) WHERE kind='pay_cycle';
            CREATE INDEX IF NOT EXISTS ix_mobile_broadcast_target ON records(json_extract(body,'$.targetIqama'),json_extract(body,'$.createdAt')) WHERE kind='mobile_broadcast';
            CREATE INDEX IF NOT EXISTS ix_rider_ask_iqama ON records(json_extract(body,'$.riderIqama')) WHERE kind='rider_ask';
            CREATE INDEX IF NOT EXISTS ix_rider_sreq_iqama ON records(json_extract(body,'$.riderIqama')) WHERE kind='rider_service_request';
            CREATE INDEX IF NOT EXISTS ix_mobile_session_iqama ON records(json_extract(body,'$.iqama')) WHERE kind='mobile_session';
            CREATE INDEX IF NOT EXISTS ix_submission_iqama ON records(json_extract(body,'$.iqama')) WHERE kind='submission';
            CREATE INDEX IF NOT EXISTS ix_advance_req_iqama ON records(json_extract(body,'$.riderIqama')) WHERE kind='advance_request';
            CREATE INDEX IF NOT EXISTS ix_push_outbox_status ON records(json_extract(body,'$.status'),json_extract(body,'$.createdAt')) WHERE kind='push_outbox';
            CREATE INDEX IF NOT EXISTS ix_audit_at ON records(json_extract(body,'$.at'),id) WHERE kind='audit';
            """ : """
            IF OBJECT_ID(N'dbo.records', N'U') IS NULL
              CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL CHECK(ISJSON(body)=1));
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_records_kind')
              CREATE INDEX ix_records_kind ON dbo.records(kind);
            """;
        SchemaSql.WithMigrationLock(c,(conn,tx)=>
        {
            using(var cmd=conn.CreateCommand()){cmd.Transaction=tx;cmd.CommandText=recordsDdl;cmd.ExecuteNonQuery();}
            ErpDataIndexes.Ensure(conn,tx);
            // Rider SQL Server indexes/columns are applied only by the admin maintenance job (not startup).
            PayRunInputs.Ensure(conn,tx);
            SchemaRef.Ensure(conn,tx);
            SchemaHrCompany.Ensure(conn,tx);
            SchemaRef.EnsureLegacyAccountMapAfterCompany(conn,tx);
            SchemaOrgCostCentre.Ensure(conn,tx);
            SchemaAuthCatalog.Ensure(conn,tx);
            SchemaOrgStructure.Ensure(conn,tx);
            SchemaApprovals.Ensure(conn,tx);
            SchemaAuditNotify.Ensure(conn,tx);
            SchemaArchive.Ensure(conn,tx);
            SchemaFormApp.Ensure(conn,tx);
            SchemaFinAccount.Ensure(conn,tx);
            SchemaFinPeriod.Ensure(conn,tx);
            SchemaFinJournal.Ensure(conn,tx);
            SchemaFinPeriodBalance.Ensure(conn,tx);
            SchemaPayPayable.Ensure(conn,tx);
            SchemaPayAllocation.Ensure(conn,tx);
            SchemaSalInvoice.Ensure(conn,tx);
            SchemaSalPayRun.Ensure(conn,tx);
            SchemaOperationCapture.Ensure(conn,tx);
            SchemaDebt.Ensure(conn,tx);
            SchemaTreasury.Ensure(conn,tx);
            SchemaBankFile.Ensure(conn,tx);
        });
        // M9: captured cycle must have freeze rows (in-flight cycles cannot deploy without U2 + rows).
        foreach(var cycle in List(c,PayCycles.Kind).Where(PayRunInputs.Captured))
            if(PayRunInputs.CountForCycle(c,null,cycle.S("id"))==0)
                throw new InvalidOperationException("Captured pay cycle is missing pay_run_input rows.");
        if(beforeUpgrade!=null&&beforeUpgrade!=System.Text.Json.JsonSerializer.Serialize(ReleaseSafety.Capture(c)))throw new InvalidOperationException("Startup document preservation verification failed.");
        DataPreservationVerified=true;
    }
    private DbConnection Open()
    {
        DbConnection c = Provider == "sqlite" ? new SqliteConnection(connection) : new SqlConnection(connection);
        c.Open();
        if(c is SqliteConnection){using var fk=c.CreateCommand();fk.CommandText="PRAGMA foreign_keys=ON;";fk.ExecuteNonQuery();}
        if(c is SqlConnection){using var session=c.CreateCommand();session.CommandText="SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;";session.ExecuteNonQuery();}
        return c;
    }
    // WAL allows independent reads while a write transaction is open.
    public T Read<T>(Func<DbConnection, T> action) { using var c = Open(); return action(c); }
    /// <summary>Holds one open connection for the callback (session locks / multi-statement DDL).</summary>
    public void WithConnection(Action<DbConnection> action) { using var c = Open(); action(c); }
    public const string DocumentLock="GatePayroll.DocumentWrite",ErpMirrorLock="GatePayroll.ErpMirrorWrite";
    public T Write<T>(Func<DbConnection, DbTransaction, T> action, bool invalidateSummaries = true,CancellationToken cancellationToken=default,string? lockResource=null)
    {
        while(!Monitor.TryEnter(writeGate,100))cancellationToken.ThrowIfCancellationRequested();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();using var c = Open();
            using var cancel=cancellationToken.Register(()=>{try{c.Close();}catch{}});
            cancellationToken.ThrowIfCancellationRequested();using var tx = c.BeginTransaction(IsolationLevel.Serializable);
            if (Provider == "mssql")
            {
                string resource=string.IsNullOrWhiteSpace(lockResource)?DocumentLock:lockResource;
                using var guard = c.CreateCommand(); guard.Transaction = tx;
                guard.CommandText = "DECLARE @result int; EXEC @result=sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000; IF @result < 0 THROW 51000,'Payroll transaction busy',1;";
                var p=guard.CreateParameter();p.ParameterName="@resource";p.Value=resource;guard.Parameters.Add(p);
                guard.ExecuteNonQuery();
            }
            var result = action(c, tx); cancellationToken.ThrowIfCancellationRequested();tx.Commit(); if (invalidateSummaries) Interlocked.Increment(ref revision); return result;
        }
        finally{Monitor.Exit(writeGate);}
    }
    public static DbCommand Command(DbConnection c, DbTransaction? tx, string sql, params (string Name, object Value)[] values)
    {
        var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (name,value) in values) { var parameter = cmd.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; cmd.Parameters.Add(parameter); }
        return cmd;
    }
    public static List<JsonObject> List(DbConnection c, string kind, DbTransaction? tx = null)
    {
        // WMRG: W2 temporary kinds read from real W1 tables.
        if(kind=="approval_limit")return SchemaApprovals.ListLimits(c,tx);
        if(kind=="sla_policy")return SchemaApprovals.ListSla(c,tx);
        if(kind=="approval_decision")return SchemaApprovals.ListDecisions(c,tx);
        if(kind=="approval_bypass_grant")return SchemaApprovals.ListBypassGrants(c,tx);
        if(kind=="approval_request")return SchemaApprovals.ListRequests(c,tx);
        if(kind=="audit")return SchemaAuditNotify.ListChanges(c,tx);
        if(kind=="audit_read_summary_daily")return SchemaAuditNotify.ListReadSummaries(c,tx);
        if(kind=="notify_settings")return [SchemaAuditNotify.GetNotifySettings(c,tx)];
        if(kind=="task_item")return SchemaAuditNotify.ListTasks(c,tx);
        // Stable order is portable and keeps every legacy JSON body intact.
        using var cmd = Command(c, tx, c is SqliteConnection ? "SELECT body FROM records WHERE kind=@kind ORDER BY rowid DESC" : "SELECT body FROM records WHERE kind=@kind ORDER BY COALESCE(JSON_VALUE(body,'$.createdAt'),JSON_VALUE(body,'$.at'),JSON_VALUE(body,'$.approvedAt'),'') DESC,id DESC", ("@kind",kind));
        using var r = cmd.ExecuteReader(); var rows = new List<JsonObject>();
        while (r.Read()) rows.Add(JsonNode.Parse(r.GetString(0))!.AsObject()); return rows;
    }
    static string JsonFieldExpr(DbConnection c, string jsonField) => RiderDataIndexes.FieldExpr(c, jsonField);
    public static List<JsonObject> ListWhere(DbConnection c, string kind, DbTransaction? tx = null, int? limit = null, params (string JsonField, string Value)[] equals)
    {
        bool sqlite = c is SqliteConnection;
        string kindPred = RiderDataIndexes.KindSql(c, kind, "@kind", out bool kindLiteral);
        var sql = new System.Text.StringBuilder();
        sql.Append(sqlite ? "SELECT body FROM records WHERE " + kindPred : (limit.HasValue ? $"SELECT TOP ({Math.Max(1, limit.Value)}) body FROM records WHERE {kindPred}" : "SELECT body FROM records WHERE " + kindPred));
        var parameters = new List<(string Name, object Value)>();
        if (!kindLiteral) parameters.Add(("@kind", kind));
        for (int i = 0; i < equals.Length; i++)
        {
            sql.Append(" AND ").Append(JsonFieldExpr(c, equals[i].JsonField)).Append($"=@v{i}");
            parameters.Add(($"@v{i}", equals[i].Value));
        }
        sql.Append(sqlite
            ? " ORDER BY rowid DESC" + (limit.HasValue ? $" LIMIT {Math.Max(1, limit.Value)}" : "")
            : " ORDER BY COALESCE(JSON_VALUE(body,'$.createdAt'),JSON_VALUE(body,'$.at'),JSON_VALUE(body,'$.approvedAt'),'') DESC,id DESC");
        using var cmd = Command(c, tx, sql.ToString(), parameters.ToArray());
        using var r = cmd.ExecuteReader(); var rows = new List<JsonObject>();
        while (r.Read()) rows.Add(JsonNode.Parse(r.GetString(0))!.AsObject());
        return rows;
    }
    public static List<JsonObject> ListByIds(DbConnection c, string kind, IReadOnlyCollection<string> ids, DbTransaction? tx = null)
    {
        if (ids.Count == 0) return [];
        var unique = ids.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).ToList();
        if (unique.Count == 0) return [];
        var rows = new List<JsonObject>();
        const int chunkSize = 400;
        for (int offset = 0; offset < unique.Count; offset += chunkSize)
        {
            var chunk = unique.Skip(offset).Take(chunkSize).ToList();
            var parameters = new List<(string Name, object Value)> { ("@kind", kind) };
            var placeholders = new string[chunk.Count];
            for (int i = 0; i < chunk.Count; i++)
            {
                string pName = $"@id{i}";
                placeholders[i] = pName;
                parameters.Add((pName, chunk[i]));
            }
            string sql = $"SELECT body FROM records WHERE kind=@kind AND id IN ({string.Join(",", placeholders)})";
            using var cmd = Command(c, tx, sql, parameters.ToArray());
            using var r = cmd.ExecuteReader();
            while (r.Read()) rows.Add(JsonNode.Parse(r.GetString(0))!.AsObject());
        }
        return rows;
    }
    public static List<JsonObject> ListBroadcastsForRider(DbConnection c, string iqama, int limit, DbTransaction? tx = null)
    {
        int bounded = Math.Max(1, limit);
        string targetIqama = RiderDataIndexes.FieldExpr(c, "targetIqama");
        string createdAt = RiderDataIndexes.CreatedAtExpr(c);
        string sql = c is SqliteConnection
            ? $"SELECT body FROM records WHERE kind='mobile_broadcast' AND {targetIqama} IN ('ALL', @iqama) ORDER BY COALESCE({createdAt}, '') DESC, rowid DESC LIMIT {bounded}"
            : $"SELECT TOP ({bounded}) body FROM records WHERE kind='mobile_broadcast' AND {targetIqama} IN ('ALL', @iqama) ORDER BY COALESCE({createdAt}, '') DESC, id DESC";
        using var cmd = Command(c, tx, sql, ("@iqama", iqama));
        using var r = cmd.ExecuteReader(); var rows = new List<JsonObject>();
        while (r.Read()) rows.Add(JsonNode.Parse(r.GetString(0))!.AsObject());
        return rows;
    }
    public static HashSet<string> ListIdsByPrefix(DbConnection c, string kind, string idPrefix, DbTransaction? tx = null)
    {
        if (string.IsNullOrEmpty(idPrefix)) return [];
        char last = idPrefix[^1];
        string endExclusive = idPrefix[..^1] + (char)(last + 1);
        using var cmd = Command(c, tx, "SELECT id FROM records WHERE kind=@kind AND id >= @start AND id < @end", ("@kind", kind), ("@start", idPrefix), ("@end", endExclusive));
        using var r = cmd.ExecuteReader(); var ids = new HashSet<string>(StringComparer.Ordinal);
        while (r.Read()) ids.Add(r.GetString(0));
        return ids;
    }
    /// <summary>Open campaign/custom_form rows for a rider, filtered in SQL by status, expiry, target and company scalars.</summary>
    public static List<JsonObject> ListOpenFormsForRider(DbConnection c, string kind, string iqama, IReadOnlyCollection<string> companyKeys, string nowIso, DbTransaction? tx = null)
    {
        if (kind is not ("campaign" or "custom_form")) throw new ArgumentException("kind must be campaign or custom_form.", nameof(kind));
        bool sqlite = c is SqliteConnection;
        var companies = companyKeys.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();
        var sql = new System.Text.StringBuilder();
        // kind is allow-listed above; embed as SQL literal so filtered indexes stay eligible.
        sql.Append("SELECT body FROM records WHERE kind='").Append(kind).Append("' AND ");
        sql.Append(RiderDataIndexes.StatusExpr(c));
        sql.Append("='open' AND ");
        sql.Append(RiderDataIndexes.ExpiresAtExpr(c));
        sql.Append(" > @now AND (");
        sql.Append(RiderDataIndexes.TargetExpr(c)).Append(" IN ('','ALL','rider','riders',@iqama)");
        sql.Append(')');
        sql.Append(" AND (").Append(RiderDataIndexes.TargetIqamaExpr(c)).Append(" IN ('','ALL',@iqama))");
        string[] companyFields = ["companyId", "sponsorId", "targetCompanyId", "targetSponsorId"];
        sql.Append(" AND (");
        for (int i = 0; i < companyFields.Length; i++)
        {
            if (i > 0) sql.Append(" AND ");
            string expr = JsonFieldExpr(c, companyFields[i]);
            sql.Append('(').Append("COALESCE(").Append(expr).Append(",'') IN ('','ALL')");
            if (companies.Count > 0)
            {
                sql.Append(" OR ").Append(expr).Append(" IN (");
                for (int j = 0; j < companies.Count; j++)
                {
                    if (j > 0) sql.Append(',');
                    sql.Append("@c").Append(i).Append('_').Append(j);
                }
                sql.Append(')');
            }
            sql.Append(')');
        }
        sql.Append(')');
        sql.Append(sqlite
            ? " ORDER BY rowid DESC"
            : " ORDER BY COALESCE(" + RiderDataIndexes.CreatedAtExpr(c) + ",'') DESC,id DESC");
        var parameters = new List<(string Name, object Value)> { ("@iqama", iqama), ("@now", nowIso) };
        for (int i = 0; i < companyFields.Length; i++)
            for (int j = 0; j < companies.Count; j++)
                parameters.Add(($"@c{i}_{j}", companies[j]));
        using var cmd = Command(c, tx, sql.ToString(), parameters.ToArray());
        using var r = cmd.ExecuteReader(); var rows = new List<JsonObject>();
        while (r.Read()) rows.Add(JsonNode.Parse(r.GetString(0))!.AsObject());
        return rows;
    }
    public static JsonObject? Get(DbConnection c, string id, string kind, DbTransaction? tx = null)
    {
        if(kind=="approval_request")return SchemaApprovals.GetRequest(c,tx,id);
        if(kind=="approval_bypass_grant")return SchemaApprovals.GetBypassGrant(c,tx,id);
        if(kind=="approval_limit")return SchemaApprovals.ListLimits(c,tx).FirstOrDefault(r=>r.S("id")==id);
        if(kind=="sla_policy")return SchemaApprovals.ListSla(c,tx).FirstOrDefault(r=>r.S("id")==id);
        if(kind=="approval_decision")return SchemaApprovals.ListDecisions(c,tx).FirstOrDefault(r=>r.S("id")==id);
        if(kind=="audit")return SchemaAuditNotify.GetChange(c,tx,id);
        if(kind=="audit_read_summary_daily")return SchemaAuditNotify.GetReadSummary(c,tx,id);
        if(kind=="notify_settings")return id=="notify-settings:global"?SchemaAuditNotify.GetNotifySettings(c,tx):null;
        if(kind=="task_item")return SchemaAuditNotify.GetTask(c,tx,id);
        using var cmd = Command(c, tx, "SELECT body FROM records WHERE id=@id AND kind=@kind", ("@id",id),("@kind",kind));
        return cmd.ExecuteScalar() is string body ? JsonNode.Parse(body)!.AsObject() : null;
    }
    public static void Save(DbConnection c, string kind, JsonObject doc, DbTransaction tx,bool preserveSnapshot=false)
    {
        if(kind=="approval_limit"){SchemaApprovals.UpsertLimit(c,tx,doc);return;}
        if(kind=="sla_policy"){SchemaApprovals.UpsertSla(c,tx,doc);return;}
        if(kind=="approval_decision"){SchemaApprovals.SaveDecision(c,tx,doc);return;}
        if(kind=="approval_bypass_grant"){SchemaApprovals.SaveBypassGrant(c,tx,doc);return;}
        if(kind=="approval_request"){SchemaApprovals.SaveRequest(c,tx,doc);return;}
        if(kind=="audit"){SchemaAuditNotify.SaveChange(c,tx,doc);return;}
        if(kind=="audit_read_summary_daily"){SchemaAuditNotify.SaveReadSummary(c,tx,doc);return;}
        if(kind=="notify_settings"){SchemaAuditNotify.SaveNotifySettings(c,tx,doc);return;}
        if(kind=="task_item"){SchemaAuditNotify.SaveTask(c,tx,doc);return;}
        if(kind=="run")PayCycles.GuardLegacyRun(c,tx,doc);
        if(!preserveSnapshot)Vouchers.OnSaving(c,tx,kind,doc);
        if(kind is not ("voucher_sequence" or "voucher_index" or "odoo_voucher_index")&&TryBuffer(tx,kind,doc))return;
        string id = doc.Required("id");
        using var update = Command(c, tx, "UPDATE records SET body=@body WHERE id=@id AND kind=@kind",("@id",id),("@kind",kind),("@body",doc.ToJsonString()));
        if (update.ExecuteNonQuery() == 0)
        {
            using var insert = Command(c,tx,"INSERT INTO records(id,kind,body) VALUES(@id,@kind,@body)",("@id",id),("@kind",kind),("@body",doc.ToJsonString()));
            insert.ExecuteNonQuery();
        }
        Reporting.Refresh(c,tx,kind,doc);
    }
    public static void Audit(DbConnection c, DbTransaction tx, string action, string entity, string actor)
    {
        // M-0606: short data-change row only (never call for reads). Keep legacy keys for existing readers.
        Save(c, "audit", new()
        {
            ["id"] = Guid.NewGuid().ToString("N"),
            ["action"] = action,
            ["actionAr"] = AuditApi.LabelAr(action),
            ["entityId"] = entity,
            ["entityRef"] = entity,
            ["actor"] = actor,
            ["at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["occurredAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["source"] = "نظام"
        }, tx);
        Notifications.OnAudit(c,tx,action,entity,actor);
    }
}
