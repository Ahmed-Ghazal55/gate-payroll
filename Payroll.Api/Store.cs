using System.Data;
using System.Data.Common;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
namespace Payroll;

// Additive document schema. Existing records and SQLite files remain readable by v0.4.
public sealed class Store
{
    public string DirectoryPath { get; }
    public string Provider { get; }
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
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = Provider == "sqlite" ? """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_records_kind ON records(kind);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_run_period ON records(json_extract(body,'$.period'),json_extract(body,'$.iqama')) WHERE kind='run';
            CREATE UNIQUE INDEX IF NOT EXISTS ix_form_response ON records(json_extract(body,'$.campaignId'),json_extract(body,'$.iqama')) WHERE kind='submission';
            CREATE UNIQUE INDEX IF NOT EXISTS ix_user_name ON records(lower(json_extract(body,'$.username'))) WHERE kind='user';
            """ : """
            IF OBJECT_ID(N'dbo.records', N'U') IS NULL
              CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL CHECK(ISJSON(body)=1));
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_records_kind')
              CREATE INDEX ix_records_kind ON dbo.records(kind);
            """;
        cmd.ExecuteNonQuery();
    }
    private DbConnection Open()
    {
        DbConnection c = Provider == "sqlite" ? new SqliteConnection(connection) : new SqlConnection(connection);
        c.Open(); return c;
    }
    // WAL allows independent reads while a write transaction is open.
    public T Read<T>(Func<DbConnection, T> action) { using var c = Open(); return action(c); }
    public T Write<T>(Func<DbConnection, DbTransaction, T> action, bool invalidateSummaries = true)
    {
        lock (writeGate)
        {
            using var c = Open(); using var tx = c.BeginTransaction(IsolationLevel.Serializable);
            if (Provider == "mssql")
            {
                using var guard = c.CreateCommand(); guard.Transaction = tx;
                guard.CommandText = "DECLARE @result int; EXEC @result=sp_getapplock @Resource='GatePayroll.DocumentWrite',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000; IF @result < 0 THROW 51000,'Payroll transaction busy',1;";
                guard.ExecuteNonQuery();
            }
            var result = action(c, tx); tx.Commit(); if (invalidateSummaries) Interlocked.Increment(ref revision); return result;
        }
    }
    public static DbCommand Command(DbConnection c, DbTransaction? tx, string sql, params (string Name, object Value)[] values)
    {
        var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (name,value) in values) { var parameter = cmd.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; cmd.Parameters.Add(parameter); }
        return cmd;
    }
    public static List<JsonObject> List(DbConnection c, string kind, DbTransaction? tx = null)
    {
        // Stable order is portable and keeps every legacy JSON body intact.
        using var cmd = Command(c, tx, c is SqliteConnection ? "SELECT body FROM records WHERE kind=@kind ORDER BY rowid DESC" : "SELECT body FROM records WHERE kind=@kind ORDER BY COALESCE(JSON_VALUE(body,'$.createdAt'),JSON_VALUE(body,'$.at'),JSON_VALUE(body,'$.approvedAt'),'') DESC,id DESC", ("@kind",kind));
        using var r = cmd.ExecuteReader(); var rows = new List<JsonObject>();
        while (r.Read()) rows.Add(JsonNode.Parse(r.GetString(0))!.AsObject()); return rows;
    }
    public static JsonObject? Get(DbConnection c, string id, string kind, DbTransaction? tx = null)
    {
        using var cmd = Command(c, tx, "SELECT body FROM records WHERE id=@id AND kind=@kind", ("@id",id),("@kind",kind));
        return cmd.ExecuteScalar() is string body ? JsonNode.Parse(body)!.AsObject() : null;
    }
    public static void Save(DbConnection c, string kind, JsonObject doc, DbTransaction tx)
    {
        string id = doc.Required("id");
        using var update = Command(c, tx, "UPDATE records SET body=@body WHERE id=@id AND kind=@kind",("@id",id),("@kind",kind),("@body",doc.ToJsonString()));
        if (update.ExecuteNonQuery() == 0)
        {
            using var insert = Command(c,tx,"INSERT INTO records(id,kind,body) VALUES(@id,@kind,@body)",("@id",id),("@kind",kind),("@body",doc.ToJsonString()));
            insert.ExecuteNonQuery();
        }
    }
    public static void Audit(DbConnection c, DbTransaction tx, string action, string entity, string actor) => Save(c, "audit", new() { ["id"] = Guid.NewGuid().ToString("N"), ["action"] = action, ["entityId"] = entity, ["actor"] = actor, ["at"] = DateTimeOffset.UtcNow.ToString("O") }, tx);
}
