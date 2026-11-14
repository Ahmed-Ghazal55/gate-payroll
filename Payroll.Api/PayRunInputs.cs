using System.Data;
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
    public const int InsertChunkSize=200;

    /// <summary>Required kinds that must have rows (or an explicit zero-kind reason) before capture succeeds.</summary>
    public static IReadOnlyList<string> RequiredKinds(string app)=>app switch
    {
        "ninja"=>["ninja_daily","platform_account","vehicle_assignment"],
        "chefz"=>["platform_account","vehicle_assignment"],
        _=>["platform_account","vehicle_assignment"]
    };

    public static IReadOnlyList<string> KindsForApp(string app)=>app switch
    {
        "ninja"=>["ninja_daily","platform_account","vehicle_assignment","housing_residence","vehicle"],
        "chefz"=>["platform_account","vehicle_assignment","housing_residence","vehicle"],
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
                    Kind nvarchar(64) NOT NULL,
                    DocId nvarchar(128) NOT NULL,
                    Body nvarchar(max) NOT NULL CHECK(ISJSON(Body)=1),
                    CapturedAt datetime2 NOT NULL,
                    CapturedBy nvarchar(128) NOT NULL,
                    CONSTRAINT PK_PayRunInput PRIMARY KEY(CycleId,Kind,DocId));
                """;
        else
            cmd.CommandText="""
                CREATE TABLE IF NOT EXISTS pay_run_input(
                  cycle_id TEXT NOT NULL,
                  kind TEXT NOT NULL,
                  doc_id TEXT NOT NULL,
                  body TEXT NOT NULL,
                  captured_at TEXT NOT NULL,
                  captured_by TEXT NOT NULL,
                  PRIMARY KEY(cycle_id,kind,doc_id));
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

    /// <summary>A0: capture/recapture only while stage ≤ 1 and not sealed. Later input change = cancel and recreate.</summary>
    public static bool CaptureAllowed(JsonObject cycle)=>!Sealed(cycle)&&(int)cycle.N("stage")<=1;

    public static bool Captured(JsonObject cycle)=>cycle.S("inputsCapturedAt")!="";

    public static List<JsonObject> List(DbConnection c,DbTransaction? tx,string cycleId,string kind)
    {
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
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        if(c is SqlConnection)
        {
            cmd.CommandText="SELECT Body FROM pay.PayRunInput WHERE CycleId=@cycle AND Kind=@kind AND DocId=@doc";
            Add(cmd,"@cycle",cycleId);Add(cmd,"@kind",kind);Add(cmd,"@doc",docId);
        }
        else
        {
            cmd.CommandText="SELECT body FROM pay_run_input WHERE cycle_id=@cycle AND kind=@kind AND doc_id=@doc";
            Add(cmd,"@cycle",cycleId);Add(cmd,"@kind",kind);Add(cmd,"@doc",docId);
        }
        using var reader=cmd.ExecuteReader();
        if(!reader.Read())return null;
        return JsonNode.Parse(reader.GetString(0)) as JsonObject;
    }

    public static int CountForCycle(DbConnection c,DbTransaction? tx,string cycleId)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        if(c is SqlConnection){cmd.CommandText="SELECT COUNT_BIG(*) FROM pay.PayRunInput WHERE CycleId=@cycle";Add(cmd,"@cycle",cycleId);}
        else{cmd.CommandText="SELECT COUNT(*) FROM pay_run_input WHERE cycle_id=@cycle";Add(cmd,"@cycle",cycleId);}
        return Convert.ToInt32(cmd.ExecuteScalar()??0,CultureInfo.InvariantCulture);
    }

    public sealed record CaptureResult(DateTimeOffset CapturedAt,Dictionary<string,int> Counts);

    public static CaptureResult Replace(DbConnection c,DbTransaction tx,string cycleId,string actor,IEnumerable<(string Kind,string DocId,JsonObject Body)> rows,DateTimeOffset? at=null)
    {
        var capturedAt=at??DateTimeOffset.UtcNow;
        using(var del=c.CreateCommand())
        {
            del.Transaction=tx;
            if(c is SqlConnection){del.CommandText="DELETE FROM pay.PayRunInput WHERE CycleId=@cycle";Add(del,"@cycle",cycleId);}
            else{del.CommandText="DELETE FROM pay_run_input WHERE cycle_id=@cycle";Add(del,"@cycle",cycleId);}
            del.ExecuteNonQuery();
        }
        var material=rows.Select(r=>(r.Kind,r.DocId,Body:r.Body.ToJsonString())).ToList();
        var counts=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var g in material.GroupBy(r=>r.Kind,StringComparer.Ordinal))counts[g.Key]=g.Count();
        if(c is SqlConnection)InsertSqlServer(c,tx,cycleId,actor,capturedAt,material);
        else InsertSqlite(c,tx,cycleId,actor,capturedAt,material);
        return new(capturedAt,counts);
    }

    static void InsertSqlServer(DbConnection c,DbTransaction tx,string cycleId,string actor,DateTimeOffset capturedAt,List<(string Kind,string DocId,string Body)> rows)
    {
        for(int i=0;i<rows.Count;i+=InsertChunkSize)
        {
            var chunk=rows.Skip(i).Take(InsertChunkSize).ToList();
            var arr=new JsonArray(chunk.Select(r=>(JsonNode?)new JsonObject{["k"]=r.Kind,["d"]=r.DocId,["b"]=r.Body}).ToArray());
            using var cmd=c.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="""
                INSERT INTO pay.PayRunInput(CycleId,Kind,DocId,Body,CapturedAt,CapturedBy)
                SELECT @cycle,j.k,j.d,j.b,@at,@by
                FROM OPENJSON(@rows) WITH(k nvarchar(64) '$.k', d nvarchar(128) '$.d', b nvarchar(max) '$.b') j;
                """;
            Add(cmd,"@cycle",cycleId);Add(cmd,"@rows",arr.ToJsonString());
            Add(cmd,"@at",capturedAt.UtcDateTime);Add(cmd,"@by",actor);
            cmd.ExecuteNonQuery();
        }
    }

    static void InsertSqlite(DbConnection c,DbTransaction tx,string cycleId,string actor,DateTimeOffset capturedAt,List<(string Kind,string DocId,string Body)> rows)
    {
        string at=capturedAt.ToString("O",CultureInfo.InvariantCulture);
        for(int i=0;i<rows.Count;i+=InsertChunkSize)
        {
            var chunk=rows.Skip(i).Take(InsertChunkSize).ToList();
            using var cmd=c.CreateCommand();cmd.Transaction=tx;
            var sb=new StringBuilder("INSERT INTO pay_run_input(cycle_id,kind,doc_id,body,captured_at,captured_by) VALUES ");
            for(int j=0;j<chunk.Count;j++)
            {
                if(j>0)sb.Append(',');
                sb.Append($"(@c{j},@k{j},@d{j},@b{j},@at,@by)");
                Add(cmd,$"@c{j}",cycleId);Add(cmd,$"@k{j}",chunk[j].Kind);Add(cmd,$"@d{j}",chunk[j].DocId);Add(cmd,$"@b{j}",chunk[j].Body);
            }
            Add(cmd,"@at",at);Add(cmd,"@by",actor);
            cmd.CommandText=sb.ToString();
            cmd.ExecuteNonQuery();
        }
    }

    static void Add(DbCommand cmd,string name,object value)
    {var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value;cmd.Parameters.Add(p);}
}
