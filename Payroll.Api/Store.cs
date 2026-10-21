using Microsoft.Data.Sqlite;
using System.Text.Json.Nodes;
namespace Payroll;

public sealed class Store
{
    public string DirectoryPath { get; }
    private readonly string connection; private readonly object gate = new();
    public Store(string dir)
    {
        DirectoryPath = Path.GetFullPath(dir); Directory.CreateDirectory(DirectoryPath);
        connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(DirectoryPath, "payroll.db"), Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ix_records_kind ON records(kind); CREATE UNIQUE INDEX IF NOT EXISTS ix_run_period ON records(json_extract(body,'$.period'),json_extract(body,'$.iqama')) WHERE kind='run'; CREATE UNIQUE INDEX IF NOT EXISTS ix_form_response ON records(json_extract(body,'$.campaignId'),json_extract(body,'$.iqama')) WHERE kind='submission';"; cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var c = new SqliteConnection(connection); c.Open(); return c; }
    public T Read<T>(Func<SqliteConnection, T> action) { lock (gate) { using var c = Open(); return action(c); } }
    public T Write<T>(Func<SqliteConnection, SqliteTransaction, T> action) { lock (gate) { using var c = Open(); using var tx = c.BeginTransaction(); var result = action(c, tx); tx.Commit(); return result; } }
    public static List<JsonObject> List(SqliteConnection c, string kind, SqliteTransaction? tx = null)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "SELECT body FROM records WHERE kind=$kind ORDER BY rowid DESC"; cmd.Parameters.AddWithValue("$kind", kind); using var r = cmd.ExecuteReader(); var rows = new List<JsonObject>();
        while (r.Read()) rows.Add(JsonNode.Parse(r.GetString(0))!.AsObject()); return rows;
    }
    public static JsonObject? Get(SqliteConnection c, string id, string kind, SqliteTransaction? tx = null) => List(c, kind, tx).FirstOrDefault(x => x.S("id") == id);
    public static void Save(SqliteConnection c, string kind, JsonObject doc, SqliteTransaction tx)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "INSERT INTO records(id,kind,body) VALUES($id,$kind,$body) ON CONFLICT(id) DO UPDATE SET body=excluded.body WHERE records.kind=excluded.kind";
        cmd.Parameters.AddWithValue("$id", doc.Required("id")); cmd.Parameters.AddWithValue("$kind", kind); cmd.Parameters.AddWithValue("$body", doc.ToJsonString()); cmd.ExecuteNonQuery();
    }
    public static void Audit(SqliteConnection c, SqliteTransaction tx, string action, string entity, string actor) => Save(c, "audit", new() { ["id"] = Guid.NewGuid().ToString("N"), ["action"] = action, ["entityId"] = entity, ["actor"] = actor, ["at"] = DateTimeOffset.UtcNow.ToString("O") }, tx);
}
