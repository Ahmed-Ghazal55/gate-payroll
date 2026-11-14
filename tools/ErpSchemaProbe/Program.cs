using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ErpImport;
using Microsoft.Data.SqlClient;

// Metadata-only probe: INFORMATION_SCHEMA.COLUMNS for every table the live readers name.
// Never prints connection strings, passwords, or row values.

string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
string outPath=args.SkipWhile(a=>a!="--out").Skip(1).FirstOrDefault()
    ??Path.Combine(Directory.GetCurrentDirectory(),"docs/data/erp-schema.snapshot.json");
bool local=args.Contains("--local-copy");
string sourceFile=args.SkipWhile(a=>a!="--source-env").Skip(1).FirstOrDefault()
    ??Path.Combine(home,"Private/gate-data/erp-readonly.env");

using var source=local
    ?Connections.LocalCopy()
    :Connections.Source(Connections.Env(sourceFile,"ERP_READONLY_DB"));
source.Open();
SourceReader.VerifyReadOnly(source);

var wanted=new HashSet<(string Schema,string Table)>(new SchemaTableComparer());
foreach(var t in SourceReader.Catalog)wanted.Add((t.Schema,t.Name));
foreach(var t in PullContracts.Tables)wanted.Add((t.Schema,t.Name));
foreach(string kind in LiveKinds())
{
    var scope=new LiveOperationsQuery.Scope(false,["0000000000"],[],[],[]);
    var page=new LiveOperationsQuery.PageRequest(1,25,"id","",0,25,"","2026-10-01","2026-10-31","ninja","");
    var (countSql,pageSql,_)=LiveOperationsQuery.PreviewSql(kind,scope,page);
    foreach(var hit in ExtractTables(countSql).Concat(ExtractTables(pageSql)))wanted.Add(hit);
}
foreach(string view in new[]{
    "vw_hunger_financial_events_v1","vw_hunger_wallet_reviews_v1","vw_hunger_month_close_v1",
    "vw_hunger_adjustments_v1","vw_hunger_custody_v1"})
    wanted.Add(("finance_read",view));
wanted.Add(("hunger","DailyOperations"));

var columns=new JsonArray();
using(var cmd=source.CreateCommand())
{
    cmd.CommandTimeout=60;
    cmd.CommandText="""
        SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE
        FROM INFORMATION_SCHEMA.COLUMNS
        ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION
        """;
    using var reader=cmd.ExecuteReader();
    while(reader.Read())
    {
        string schema=reader.GetString(0),table=reader.GetString(1);
        if(!wanted.Contains((schema,table)))continue;
        columns.Add(new JsonObject
        {
            ["schema"]=schema,
            ["table"]=table,
            ["column"]=reader.GetString(2),
            ["dataType"]=reader.GetString(3),
            ["nullable"]=string.Equals(reader.GetString(4),"YES",StringComparison.OrdinalIgnoreCase)
        });
    }
}

var present=columns.OfType<JsonObject>().Select(c=>(c.S("schema"),c.S("table"))).ToHashSet(new SchemaTableComparer());
var missing=wanted.Where(t=>!present.Contains(t)).OrderBy(t=>t.Schema).ThenBy(t=>t.Table)
    .Select(t=>new JsonObject{["schema"]=t.Schema,["table"]=t.Table}).ToArray();

var doc=new JsonObject
{
    ["generatedAt"]=DateTimeOffset.UtcNow.ToString("O"),
    ["database"]=source.Database,
    ["columnCount"]=columns.Count,
    ["tableCount"]=present.Count,
    ["missingTables"]=new JsonArray(missing.Select(m=>(JsonNode?)m).ToArray()),
    ["columns"]=columns
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
File.WriteAllText(outPath,doc.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"wrote {outPath}");
Console.WriteLine($"tables={present.Count} columns={columns.Count} missing={missing.Length}");
foreach(var m in missing)Console.WriteLine($"missing {m.S("schema")}.{m.S("table")}");
return missing.Length==0?0:2;

static IEnumerable<string> LiveKinds()
{
    // Kinds that PreviewSql accepts (exclude sponsorship_period synthetic).
    string[] known=["city","housing","housing_residence","vehicle","vehicle_assignment","custody_handover",
        "legal_case","platform_account","vehicle_operational_status","platform_day_usage","rider_work_timeline",
        "custody_equipment","vehicle_expense","fuel_usage","chefz_account","chefz_account_type_change",
        "chefz_daily","chefz_settlement","chefz_order_row","ninja_daily","ninja_daily_deduction",
        "ninja_evaluation_snapshot","jahez_commission_policy","jahez_daily_order"];
    return known.Where(LiveOperationsQuery.HasKind);
}

static IEnumerable<(string Schema,string Table)> ExtractTables(string sql)
{
    foreach(Match m in Regex.Matches(sql,@"\[([A-Za-z_][A-Za-z0-9_]*)\]\.\[([A-Za-z_][A-Za-z0-9_]*)\]"))
        yield return (m.Groups[1].Value,m.Groups[2].Value);
}

sealed class SchemaTableComparer:IEqualityComparer<(string Schema,string Table)>
{
    public bool Equals((string Schema,string Table) x,(string Schema,string Table) y)
        =>string.Equals(x.Schema,y.Schema,StringComparison.OrdinalIgnoreCase)
          &&string.Equals(x.Table,y.Table,StringComparison.OrdinalIgnoreCase);
    public int GetHashCode((string Schema,string Table) obj)
        =>HashCode.Combine(obj.Schema.ToUpperInvariant(),obj.Table.ToUpperInvariant());
}

file static class JsonExt
{
    public static string S(this JsonObject o,string key)=>o[key]?.GetValue<string>()??"";
}
