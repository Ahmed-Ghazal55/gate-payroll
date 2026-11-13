using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
namespace Payroll;

/// <summary>Frozen ERP/ops rows for one payroll cycle. Additive dual-engine table (SQL Server + SQLite).</summary>
public static class PayRunInputs
{
    public const int CapturePageSize=500;
    /// <summary>TODO 2026-11-13: remove ErpSyncJob / S1–S4 mirror path after first payroll (Ninja 2026-10).</summary>
    public const string MirrorRemovalTodo="TODO 2026-11-13: remove ErpSyncJob and S1–S4 mirror after first payroll";

    public sealed record Row(string CycleId,string Platform,string PeriodFrom,string PeriodTo,string Kind,string DocId,JsonObject Body,string? SourceRowversion,DateTimeOffset CapturedAt,string CapturedBy,string BodyHash);
    public sealed record CaptureResult(DateTimeOffset CapturedAt,Dictionary<string,int> Counts);

    public static IReadOnlyList<string> KindsForApp(string app)=>app switch
    {
        "ninja"=>["ninja_daily","ninja_daily_deduction","platform_account","vehicle_assignment","housing_residence","vehicle"],
        "chefz"=>["chefz_daily","platform_account","vehicle_assignment","housing_residence","vehicle"],
        "keeta" or "hs" or "jahez"=>["platform_account","vehicle_assignment","housing_residence","vehicle"],
        _=>["platform_account","vehicle_assignment","housing_residence","vehicle"]
    };

    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        if(c is SqlConnection)
            cmd.CommandText="""
                IF SCHEMA_ID(N'pay') IS NULL EXEC(N'CREATE SCHEMA pay');
                IF OBJECT_ID(N'pay.PayRunInput',N'U') IS NULL
                  CREATE TABLE pay.PayRunInput(
                    CycleId nvarchar(128) NOT NULL,
                    Platform nvarchar(40) NOT NULL,
                    PeriodFrom char(10) NOT NULL,
                    PeriodTo char(10) NOT NULL,
                    Kind nvarchar(64) NOT NULL,
                    DocId nvarchar(128) NOT NULL,
                    Body nvarchar(max) NOT NULL CHECK(ISJSON(Body)=1),
                    SourceRowversion nvarchar(128) NULL,
                    CapturedAt datetime2 NOT NULL,
                    CapturedBy nvarchar(128) NOT NULL,
                    BodyHash char(64) NOT NULL,
                    CONSTRAINT PK_PayRunInput PRIMARY KEY(CycleId,Kind,DocId));
                IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'pay.PayRunInput') AND name=N'IX_PayRunInput_CycleKind')
                  CREATE INDEX IX_PayRunInput_CycleKind ON pay.PayRunInput(CycleId,Kind);
                """;
        else
            cmd.CommandText="""
                CREATE TABLE IF NOT EXISTS pay_run_input(
                  cycle_id TEXT NOT NULL,
                  platform TEXT NOT NULL,
                  period_from TEXT NOT NULL,
                  period_to TEXT NOT NULL,
                  kind TEXT NOT NULL,
                  doc_id TEXT NOT NULL,
                  body TEXT NOT NULL,
                  source_rowversion TEXT NULL,
                  captured_at TEXT NOT NULL,
                  captured_by TEXT NOT NULL,
                  body_hash TEXT NOT NULL,
                  PRIMARY KEY(cycle_id,kind,doc_id));
                CREATE INDEX IF NOT EXISTS ix_pay_run_input_cycle_kind ON pay_run_input(cycle_id,kind);
                """;
        cmd.ExecuteNonQuery();
    }

    public static string HashBody(JsonObject body)
    {
        string canonical=FinancialImports.Canonical(body);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static bool Sealed(JsonObject cycle)
    {
        string status=cycle.S("status");
        if(status is "approved" or "partially_approved" or "cancelled" or "reversed")return true;
        return (cycle["riders"] as JsonArray??[]).OfType<JsonObject>().Any(r=>r.S("salaryApprovedAt")!="");
    }

    public static bool Captured(JsonObject cycle)=>cycle.S("inputsCapturedAt")!="";

    public static List<JsonObject> List(DbConnection c,DbTransaction? tx,string cycleId,string kind)
    {
        Ensure(c,tx);
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        if(c is SqlConnection)
        {
            cmd.CommandText="SELECT Body FROM pay.PayRunInput WHERE CycleId=@cycle AND Kind=@kind ORDER BY DocId";
            Add(cmd,"@cycle",cycleId);Add(cmd,"@kind",kind);
        }
        else
        {
            cmd.CommandText="SELECT body FROM pay_run_input WHERE cycle_id=@cycle AND kind=@kind ORDER BY doc_id";
            Add(cmd,"@cycle",cycleId);Add(cmd,"@kind",kind);
        }
        var rows=new List<JsonObject>();
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
        {
            var body=JsonNode.Parse(reader.GetString(0)) as JsonObject??new JsonObject();
            rows.Add(body);
        }
        return rows;
    }

    public static JsonObject? Get(DbConnection c,DbTransaction? tx,string cycleId,string kind,string docId)
        =>List(c,tx,cycleId,kind).FirstOrDefault(r=>r.S("id")==docId);

    public static CaptureResult Replace(DbConnection c,DbTransaction tx,string cycleId,string platform,string periodFrom,string periodTo,string actor,IEnumerable<(string Kind,string DocId,JsonObject Body,string? Rowversion)> rows,DateTimeOffset? at=null)
    {
        Ensure(c,tx);
        var capturedAt=at??DateTimeOffset.UtcNow;
        using(var del=c.CreateCommand())
        {
            del.Transaction=tx;
            if(c is SqlConnection){del.CommandText="DELETE FROM pay.PayRunInput WHERE CycleId=@cycle";Add(del,"@cycle",cycleId);}
            else{del.CommandText="DELETE FROM pay_run_input WHERE cycle_id=@cycle";Add(del,"@cycle",cycleId);}
            del.ExecuteNonQuery();
        }
        var counts=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var (kind,docId,body,rowversion) in rows)
        {
            string hash=HashBody(body);
            using var ins=c.CreateCommand();ins.Transaction=tx;
            if(c is SqlConnection)
            {
                ins.CommandText="""
                    INSERT INTO pay.PayRunInput(CycleId,Platform,PeriodFrom,PeriodTo,Kind,DocId,Body,SourceRowversion,CapturedAt,CapturedBy,BodyHash)
                    VALUES(@cycle,@platform,@from,@to,@kind,@doc,@body,@rv,@at,@by,@hash);
                    """;
                Add(ins,"@cycle",cycleId);Add(ins,"@platform",platform);Add(ins,"@from",periodFrom);Add(ins,"@to",periodTo);
                Add(ins,"@kind",kind);Add(ins,"@doc",docId);Add(ins,"@body",body.ToJsonString());
                Add(ins,"@rv",(object?)rowversion??DBNull.Value);Add(ins,"@at",capturedAt.UtcDateTime);Add(ins,"@by",actor);Add(ins,"@hash",hash);
            }
            else
            {
                ins.CommandText="""
                    INSERT INTO pay_run_input(cycle_id,platform,period_from,period_to,kind,doc_id,body,source_rowversion,captured_at,captured_by,body_hash)
                    VALUES(@cycle,@platform,@from,@to,@kind,@doc,@body,@rv,@at,@by,@hash);
                    """;
                Add(ins,"@cycle",cycleId);Add(ins,"@platform",platform);Add(ins,"@from",periodFrom);Add(ins,"@to",periodTo);
                Add(ins,"@kind",kind);Add(ins,"@doc",docId);Add(ins,"@body",body.ToJsonString());
                Add(ins,"@rv",(object?)rowversion??DBNull.Value);Add(ins,"@at",capturedAt.ToString("O",CultureInfo.InvariantCulture));Add(ins,"@by",actor);Add(ins,"@hash",hash);
            }
            ins.ExecuteNonQuery();
            counts[kind]=counts.GetValueOrDefault(kind)+1;
        }
        return new(capturedAt,counts);
    }

    static void Add(DbCommand cmd,string name,object value)
    {var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value;cmd.Parameters.Add(p);}
}

/// <summary>Thin alias used by payroll readers.</summary>
public static class RunInputs
{
    public static List<JsonObject> List(DbConnection c,DbTransaction? tx,string cycleId,string kind)=>PayRunInputs.List(c,tx,cycleId,kind);
}
