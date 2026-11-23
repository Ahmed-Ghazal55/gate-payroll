using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Payroll;

/// <summary>
/// Rider-app filter index catalog for SQL Server. Startup never creates columns or indexes.
/// Non-persisted computed columns and indexes are applied one-by-one by the admin maintenance job.
/// Query helpers emit the same CONVERT/JSON expressions as the column definitions so plans stay
/// correct before and after the job (slower without indexes; eligible once indexes exist).
/// </summary>
public static class RiderDataIndexes
{
    public const string AppLockResource = "GatePayroll.RiderIndexMaintenance";
    public const int DefaultCommandTimeoutSeconds = 3600;
    public const int DefaultMinFreeDataMb = 512;
    public const int DefaultMinFreeLogMb = 256;

    public sealed record ColumnSpec(string Name, string SqlType, string Expression);

    public sealed record IndexSpec(
        string Name,
        string[] Columns,
        string KindLiteral,
        string CreateSql);

    public static readonly ColumnSpec[] Columns =
    [
        new("rec_iqama", "nvarchar(128)", "CONVERT(nvarchar(128),JSON_VALUE(body,'$.iqama'))"),
        new("rec_rider", "nvarchar(128)", "CONVERT(nvarchar(128),JSON_VALUE(body,'$.rider'))"),
        new("rec_rider_iqama", "nvarchar(128)", "CONVERT(nvarchar(128),JSON_VALUE(body,'$.riderIqama'))"),
        new("rec_period", "nvarchar(40)", "CONVERT(nvarchar(40),JSON_VALUE(body,'$.period'))"),
        new("rec_target_iqama", "nvarchar(128)", "CONVERT(nvarchar(128),JSON_VALUE(body,'$.targetIqama'))"),
        new("rec_status", "nvarchar(64)", "CONVERT(nvarchar(64),JSON_VALUE(body,'$.status'))"),
        new("rec_created_at", "nvarchar(40)", "CONVERT(nvarchar(40),JSON_VALUE(body,'$.createdAt'))"),
        new("rec_campaign_id", "nvarchar(128)", "CONVERT(nvarchar(128),JSON_VALUE(body,'$.campaignId'))"),
        new("rec_expires_at", "nvarchar(40)", "CONVERT(nvarchar(40),JSON_VALUE(body,'$.expiresAt'))"),
        new("rec_company_id", "nvarchar(128)", "CONVERT(nvarchar(128),JSON_VALUE(body,'$.companyId'))"),
        new("rec_sponsor_id", "nvarchar(128)", "CONVERT(nvarchar(128),JSON_VALUE(body,'$.sponsorId'))"),
        new("rec_target_company_id", "nvarchar(128)", "CONVERT(nvarchar(128),COALESCE(JSON_VALUE(body,'$.targetCompanyId'),JSON_VALUE(body,'$.targetSponsorId')))"),
        new("rec_target", "nvarchar(64)", "CONVERT(nvarchar(64),COALESCE(JSON_VALUE(body,'$.target'),JSON_VALUE(body,'$.targetAudience')))"),
    ];

    public static readonly IndexSpec[] Indexes =
    [
        Idx("ix_debt_iqama", ["rec_iqama", "rec_status"], "debt"),
        Idx("ix_pay_cycle_line_rider_period", ["rec_rider", "rec_period"], "pay_cycle_line"),
        Idx("ix_pay_cycle_line_period", ["rec_period"], "pay_cycle_line"),
        Idx("ix_pay_cycle_period", ["rec_period"], "pay_cycle"),
        Idx("ix_run_iqama_period", ["rec_iqama", "rec_period"], "run"),
        Idx("ix_mobile_broadcast_target", ["rec_target_iqama", "rec_created_at"], "mobile_broadcast"),
        Idx("ix_rider_ask_iqama", ["rec_rider_iqama"], "rider_ask"),
        Idx("ix_rider_sreq_iqama", ["rec_rider_iqama"], "rider_service_request"),
        Idx("ix_mobile_session_iqama", ["rec_iqama"], "mobile_session"),
        Idx("ix_submission_iqama", ["rec_iqama"], "submission"),
        Idx("ix_submission_campaign_iqama", ["rec_campaign_id", "rec_iqama"], "submission"),
        Idx("ix_advance_req_iqama", ["rec_rider_iqama"], "advance_request"),
        Idx("ix_push_outbox_status", ["rec_status", "rec_created_at"], "push_outbox"),
        Idx("ix_campaign_open_expiry", ["rec_status", "rec_expires_at", "rec_company_id", "rec_target_iqama"], "campaign"),
        Idx("ix_custom_form_open_expiry", ["rec_status", "rec_expires_at", "rec_target", "rec_target_iqama"], "custom_form"),
    ];

    // evaluation_response index is created at Store startup (not the rider maintenance job);
    // keep its kind literal so ListWhere can use the filtered index.
    static readonly HashSet<string> AllowedKinds = new(
        Indexes.Select(i => i.KindLiteral).Append("evaluation_response"),
        StringComparer.Ordinal);

    static readonly Dictionary<string, string> JsonFieldExpressions = new(StringComparer.Ordinal)
    {
        ["iqama"] = "CONVERT(nvarchar(128),JSON_VALUE(body,'$.iqama'))",
        ["rider"] = "CONVERT(nvarchar(128),JSON_VALUE(body,'$.rider'))",
        ["riderIqama"] = "CONVERT(nvarchar(128),JSON_VALUE(body,'$.riderIqama'))",
        ["period"] = "CONVERT(nvarchar(40),JSON_VALUE(body,'$.period'))",
        ["targetIqama"] = "CONVERT(nvarchar(128),JSON_VALUE(body,'$.targetIqama'))",
        ["status"] = "CONVERT(nvarchar(64),JSON_VALUE(body,'$.status'))",
        ["createdAt"] = "CONVERT(nvarchar(40),JSON_VALUE(body,'$.createdAt'))",
        ["campaignId"] = "CONVERT(nvarchar(128),JSON_VALUE(body,'$.campaignId'))",
        ["expiresAt"] = "CONVERT(nvarchar(40),JSON_VALUE(body,'$.expiresAt'))",
        ["companyId"] = "CONVERT(nvarchar(128),JSON_VALUE(body,'$.companyId'))",
        ["sponsorId"] = "CONVERT(nvarchar(128),JSON_VALUE(body,'$.sponsorId'))",
        ["targetCompanyId"] = "CONVERT(nvarchar(128),COALESCE(JSON_VALUE(body,'$.targetCompanyId'),JSON_VALUE(body,'$.targetSponsorId')))",
        ["targetSponsorId"] = "CONVERT(nvarchar(128),COALESCE(JSON_VALUE(body,'$.targetCompanyId'),JSON_VALUE(body,'$.targetSponsorId')))",
        ["target"] = "CONVERT(nvarchar(64),COALESCE(JSON_VALUE(body,'$.target'),JSON_VALUE(body,'$.targetAudience')))",
        ["targetAudience"] = "CONVERT(nvarchar(64),COALESCE(JSON_VALUE(body,'$.target'),JSON_VALUE(body,'$.targetAudience')))",
        // Matches Store.cs rec_round_id / ix_evaluation_response_round (startup computed column).
        ["roundId"] = "CONVERT(nvarchar(128),JSON_VALUE(body,'$.roundId'))",
    };

    static IndexSpec Idx(string name, string[] columns, string kind)
    {
        string create = $"CREATE INDEX {name} ON dbo.records({string.Join(",", columns)}) WHERE kind=''{kind}''";
        return new IndexSpec(name, columns, kind, create);
    }

    public static string DefinitionHash(IndexSpec index)
    {
        string material = index.Name + "|" + string.Join(",", index.Columns) + "|kind=" + index.KindLiteral;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    public static bool IsAllowedKind(string kind) => AllowedKinds.Contains(kind);

    /// <summary>Kind filter as a SQL literal when allow-listed; otherwise a parameter placeholder name.</summary>
    public static string KindSql(DbConnection c, string kind, string paramName, out bool usedLiteral)
    {
        if (IsAllowedKind(kind))
        {
            usedLiteral = true;
            // kind is from the fixed allow-list only.
            return $"kind='{kind}'";
        }
        usedLiteral = false;
        return $"kind={paramName}";
    }

    public static string FieldExpr(DbConnection c, string jsonField)
    {
        if (string.IsNullOrEmpty(jsonField) || !char.IsAsciiLetter(jsonField[0]) || !jsonField.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
            throw new ArgumentException("Invalid JSON field name.", nameof(jsonField));
        if (c is SqliteConnection)
            return $"json_extract(body,'$.{jsonField}')";
        if (JsonFieldExpressions.TryGetValue(jsonField, out string? expr))
            return expr;
        return $"JSON_VALUE(body,'$.{jsonField}')";
    }

    public static string TargetExpr(DbConnection c) =>
        c is SqliteConnection
            ? "COALESCE(json_extract(body,'$.target'),json_extract(body,'$.targetAudience'),'')"
            : "COALESCE(" + JsonFieldExpressions["target"] + ",'')";

    public static string TargetIqamaExpr(DbConnection c) =>
        c is SqliteConnection
            ? "COALESCE(json_extract(body,'$.targetIqama'),'')"
            : "COALESCE(" + JsonFieldExpressions["targetIqama"] + ",'')";

    public static string StatusExpr(DbConnection c) => FieldExpr(c, "status");
    public static string ExpiresAtExpr(DbConnection c) => FieldExpr(c, "expiresAt");
    public static string CreatedAtExpr(DbConnection c) => FieldExpr(c, "createdAt");

    public static void EnsureOpsTable(DbConnection c)
    {
        if (c is SqliteConnection)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS maintenance_step(
                  step_name TEXT NOT NULL PRIMARY KEY,
                  definition_hash TEXT NOT NULL,
                  completed_at TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
            return;
        }
        using (var schema = c.CreateCommand())
        {
            schema.CommandText = "IF SCHEMA_ID(N'ops') IS NULL EXEC(N'CREATE SCHEMA ops');";
            schema.ExecuteNonQuery();
        }
        using var table = c.CreateCommand();
        table.CommandText = """
            IF OBJECT_ID(N'ops.maintenance_step', N'U') IS NULL
              CREATE TABLE ops.maintenance_step(
                step_name nvarchar(128) NOT NULL PRIMARY KEY,
                definition_hash nvarchar(64) NOT NULL,
                completed_at datetime2 NOT NULL
              );
            """;
        table.ExecuteNonQuery();
    }

    public static void EnsureColumns(DbConnection c, int commandTimeoutSeconds)
    {
        if (c is SqliteConnection) return;
        using var session = c.CreateCommand();
        session.CommandText = "SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET QUOTED_IDENTIFIER ON; SET NUMERIC_ROUNDABORT OFF;";
        session.ExecuteNonQuery();
        foreach (var col in Columns)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandTimeout = commandTimeoutSeconds;
            cmd.CommandText = $"IF COL_LENGTH('dbo.records','{col.Name}') IS NULL ALTER TABLE dbo.records ADD {col.Name} AS {col.Expression};";
            cmd.ExecuteNonQuery();
        }
    }

    public static bool IndexMatches(DbConnection c, IndexSpec index)
    {
        if (c is SqliteConnection)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='index' AND name=@name";
            var p = cmd.CreateParameter(); p.ParameterName = "@name"; p.Value = index.Name; cmd.Parameters.Add(p);
            return cmd.ExecuteScalar() is not null;
        }
        using var check = c.CreateCommand();
        check.CommandText = """
            SELECT i.is_disabled, ic.key_ordinal, COL_NAME(ic.object_id, ic.column_id) AS col_name, i.filter_definition
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
            WHERE i.object_id=OBJECT_ID(N'dbo.records') AND i.name=@name AND ic.is_included_column=0
            ORDER BY ic.key_ordinal;
            """;
        var name = check.CreateParameter(); name.ParameterName = "@name"; name.Value = index.Name; check.Parameters.Add(name);
        using var r = check.ExecuteReader();
        var cols = new List<string>();
        string? filter = null;
        bool disabled = false;
        while (r.Read())
        {
            disabled = r.GetBoolean(0);
            cols.Add(r.GetString(2));
            filter = r.IsDBNull(3) ? null : r.GetString(3);
        }
        if (disabled || cols.Count != index.Columns.Length) return false;
        for (int i = 0; i < index.Columns.Length; i++)
            if (!string.Equals(cols[i], index.Columns[i], StringComparison.OrdinalIgnoreCase)) return false;
        string expectedFilter = $"(kind='{index.KindLiteral}')";
        string normalized = (filter ?? "").Replace(" ", "", StringComparison.Ordinal).Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal);
        string expected = expectedFilter.Replace(" ", "", StringComparison.Ordinal);
        return string.Equals(normalized, expected, StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"kind=N'{index.KindLiteral}'", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"kind='{index.KindLiteral}'", StringComparison.OrdinalIgnoreCase);
    }

    public static bool StepCompleted(DbConnection c, string stepName, string definitionHash)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = c is SqliteConnection
            ? "SELECT 1 FROM maintenance_step WHERE step_name=@n AND definition_hash=@h"
            : "SELECT 1 FROM ops.maintenance_step WHERE step_name=@n AND definition_hash=@h";
        var n = cmd.CreateParameter(); n.ParameterName = "@n"; n.Value = stepName; cmd.Parameters.Add(n);
        var h = cmd.CreateParameter(); h.ParameterName = "@h"; h.Value = definitionHash; cmd.Parameters.Add(h);
        return cmd.ExecuteScalar() is not null;
    }

    public static void RecordStep(DbConnection c, string stepName, string definitionHash)
    {
        using var cmd = c.CreateCommand();
        if (c is SqliteConnection)
        {
            cmd.CommandText = """
                INSERT INTO maintenance_step(step_name,definition_hash,completed_at)
                VALUES(@n,@h,@at)
                ON CONFLICT(step_name) DO UPDATE SET definition_hash=excluded.definition_hash, completed_at=excluded.completed_at;
                """;
            var at = cmd.CreateParameter(); at.ParameterName = "@at"; at.Value = DateTimeOffset.UtcNow.ToString("O"); cmd.Parameters.Add(at);
        }
        else
        {
            cmd.CommandText = """
                MERGE ops.maintenance_step AS t
                USING (SELECT @n AS step_name, @h AS definition_hash, SYSUTCDATETIME() AS completed_at) AS s
                ON t.step_name=s.step_name
                WHEN MATCHED THEN UPDATE SET definition_hash=s.definition_hash, completed_at=s.completed_at
                WHEN NOT MATCHED THEN INSERT(step_name,definition_hash,completed_at) VALUES(s.step_name,s.definition_hash,s.completed_at);
                """;
        }
        var n = cmd.CreateParameter(); n.ParameterName = "@n"; n.Value = stepName; cmd.Parameters.Add(n);
        var h = cmd.CreateParameter(); h.ParameterName = "@h"; h.Value = definitionHash; cmd.Parameters.Add(h);
        cmd.ExecuteNonQuery();
    }

    public static void CreateIndex(DbConnection c, IndexSpec index, int commandTimeoutSeconds)
    {
        if (c is SqliteConnection) return;
        if (IndexMatches(c, index)) return;
        using var drop = c.CreateCommand();
        drop.CommandTimeout = commandTimeoutSeconds;
        drop.CommandText = $"IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.records') AND name=N'{index.Name}') DROP INDEX {index.Name} ON dbo.records;";
        drop.ExecuteNonQuery();
        using var create = c.CreateCommand();
        create.CommandTimeout = commandTimeoutSeconds;
        create.CommandText = $"CREATE INDEX {index.Name} ON dbo.records({string.Join(",", index.Columns)}) WHERE kind='{index.KindLiteral}';";
        create.ExecuteNonQuery();
    }

    public static (int DataFreeMb, int LogFreeMb) FreeSpaceMb(DbConnection c)
    {
        if (c is SqliteConnection) return (int.MaxValue, int.MaxValue);
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT
              type_desc,
              CAST((size - CAST(FILEPROPERTY(name, 'SpaceUsed') AS int)) AS bigint) * 8 / 1024 AS free_mb
            FROM sys.database_files;
            """;
        int data = 0, log = 0;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string type = r.GetString(0);
            int free = r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetInt64(1));
            if (type.Equals("ROWS", StringComparison.OrdinalIgnoreCase)) data += free;
            else if (type.Equals("LOG", StringComparison.OrdinalIgnoreCase)) log += free;
        }
        return (data, log);
    }

    public static bool RollbackInProgress(DbConnection c)
    {
        if (c is SqliteConnection) return false;
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM sys.dm_exec_requests
            WHERE database_id = DB_ID() AND (
              command LIKE N'%ROLLBACK%' OR status = N'rollback'
            );
            """;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    /// <summary>True when SQL Server already has any rider computed column (startup must never create these).</summary>
    public static bool AnyRiderColumnPresent(DbConnection c)
    {
        if (c is SqliteConnection) return false;
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN COL_LENGTH('dbo.records','rec_iqama') IS NULL THEN 0 ELSE 1 END;";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) == 1;
    }

    public static bool AnyRiderIndexPresent(DbConnection c)
    {
        if (c is SqliteConnection) return false;
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.records') AND name=N'ix_debt_iqama') THEN 1 ELSE 0 END;";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) == 1;
    }
}
