using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ErpImport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class ErpSchemaContractTests
{
    static readonly string SnapshotPath=FindSnapshot();
    static Dictionary<(string Schema,string Table),HashSet<string>>? _columns;

    static string FindSnapshot()
    {
        string dir=AppContext.BaseDirectory;
        for(int i=0;i<8;i++)
        {
            string candidate=Path.Combine(dir,"docs/data/erp-schema.snapshot.json");
            if(File.Exists(candidate))return candidate;
            dir=Path.GetFullPath(Path.Combine(dir,".."));
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../docs/data/erp-schema.snapshot.json"));
    }

    static Dictionary<(string Schema,string Table),HashSet<string>> Columns
    {
        get
        {
            if(_columns!=null)return _columns;
            Assert.IsTrue(File.Exists(SnapshotPath),$"Missing schema snapshot at {SnapshotPath}. Run tools/ErpSchemaProbe first.");
            var doc=JsonNode.Parse(File.ReadAllText(SnapshotPath))!.AsObject();
            var map=new Dictionary<(string,string),HashSet<string>>(new SchemaComparer());
            foreach(var row in doc["columns"]!.AsArray().OfType<JsonObject>())
            {
                var key=(row["schema"]!.GetValue<string>(),row["table"]!.GetValue<string>());
                if(!map.TryGetValue(key,out var set))map[key]=set=new(StringComparer.OrdinalIgnoreCase);
                set.Add(row["column"]!.GetValue<string>());
            }
            return _columns=map;
        }
    }

    static bool Has(string schema,string table,string column)
        =>Columns.TryGetValue((schema,table),out var set)&&set.Contains(column);

    static bool HasTable(string schema,string table)=>Columns.ContainsKey((schema,table));

    [TestMethod]
    public void SnapshotFileIsPresent()
    {
        Assert.IsTrue(File.Exists(SnapshotPath),SnapshotPath);
        Assert.IsTrue(Columns.Count>0);
    }

    [TestMethod]
    public void SourceReaderCatalogColumnsExistInSnapshot()
    {
        var missing=new List<string>();
        foreach(var t in SourceReader.Catalog)
        {
            if(!HasTable(t.Schema,t.Name)){if(!t.Optional)missing.Add($"table {t.Schema}.{t.Name}");continue;}
            foreach(string col in t.Fields.Split(' ',StringSplitOptions.RemoveEmptyEntries))
                if(!Has(t.Schema,t.Name,col))missing.Add($"{t.Schema}.{t.Name}.{col}");
        }
        Assert.AreEqual(0,missing.Count,string.Join("; ",missing.Take(20)));
    }

    [TestMethod]
    public void PullContractsCatalogColumnsExistInSnapshot()
    {
        var missing=new List<string>();
        foreach(var t in PullContracts.Tables)
        {
            if(!HasTable(t.Schema,t.Name)){missing.Add($"table {t.Schema}.{t.Name}");continue;}
            foreach(string col in t.Fields.Split(' ',StringSplitOptions.RemoveEmptyEntries))
                if(!Has(t.Schema,t.Name,col))missing.Add($"{t.Schema}.{t.Name}.{col}");
        }
        Assert.AreEqual(0,missing.Count,string.Join("; ",missing.Take(20)));
    }

    [TestMethod]
    public void PreviewSqlReferencesExistInSnapshot_AllFilterPaths()
    {
        var missing=new List<string>();
        string[] kinds=["ninja_daily","ninja_daily_deduction","chefz_daily","chefz_order_row","platform_day_usage",
            "jahez_daily_order","vehicle_assignment","housing_residence","rider_work_timeline","fuel_usage",
            "platform_account","vehicle","custody_handover","vehicle_expense","vehicle_operational_status"];
        var paths=new LiveOperationsQuery.PageRequest[]
        {
            new(1,25,"id","",0,25),
            new(1,25,"id","",0,25,"","2026-10-01","2026-10-31"),
            new(1,25,"id","",0,25,"","2026-10-01","2026-10-31","ninja"),
            new(1,25,"id","search",0,25,"بحث"),
            new(1,25,"id","",0,25,"","","","","aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            new(1,25,"-id","",0,25,"","2026-10-01","2026-10-31","ninja","aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        };
        var company=new LiveOperationsQuery.Scope(false,["1010000000"],[Guid.NewGuid().ToString("D")],[Guid.NewGuid().ToString("D")],["NINJA"]);
        var all=new LiveOperationsQuery.Scope(true,[],[],[],[]);
        foreach(string kind in kinds.Where(LiveOperationsQuery.HasKind))
        {
            string mainTable=LiveOperationsQuery.MainTable(kind);
            string? mainSchema=SchemaOf(kind);
            foreach(var scope in new[]{company,all})
            foreach(var page in paths)
            {
                var (countSql,pageSql,_)=LiveOperationsQuery.PreviewSql(kind,scope,page);
                foreach(string sql in new[]{countSql,pageSql})
                {
                    foreach(var (schema,table) in ExtractTables(sql))
                        if(!HasTable(schema,table))missing.Add($"table {schema}.{table} in {kind}");
                    foreach(string col in ExtractColumns(sql))
                    {
                        // Qualify against the kind's main table when present; also accept any referenced table that has the column.
                        bool ok=(mainSchema!=null&&Has(mainSchema,mainTable,col))
                            ||ExtractTables(sql).Any(t=>Has(t.Schema,t.Table,col))
                            ||IsSqlBuiltin(col);
                        if(!ok)missing.Add($"{kind}:{col}");
                    }
                }
            }
        }
        Assert.AreEqual(0,missing.Count,string.Join("; ",missing.Distinct().Take(40)));
    }

    [TestMethod]
    public void PeriodDateColumnsMatchPerKindSpec()
    {
        // A1/A2 proof: the period filter must name a column that exists on the kind's table.
        (string Kind,string Column)[] expected=
        [
            ("ninja_daily","ReportDate"),
            ("ninja_daily_deduction","ReportDate"),
            ("chefz_daily","ReportDate"),
            ("chefz_order_row","BusinessDate"),
            ("platform_day_usage","BusinessDate"),
            ("jahez_daily_order","Date"),
            ("vehicle_assignment","StartedAtUtc"),
        ];
        var page=new LiveOperationsQuery.PageRequest(1,25,"id","",0,25,"","2026-10-01","2026-10-31");
        var scope=new LiveOperationsQuery.Scope(true,[],[],[],[]);
        var wrong=new List<string>();
        foreach(var (kind,column) in expected)
        {
            if(!LiveOperationsQuery.HasKind(kind))continue;
            string sql=LiveOperationsQuery.PreviewSql(kind,scope,page).CountSql;
            if(!sql.Contains("["+column+"]",StringComparison.Ordinal))
                wrong.Add($"{kind} period SQL lacks [{column}]: {sql}");
            string? schema=SchemaOf(kind);
            string table=LiveOperationsQuery.MainTable(kind);
            if(schema!=null&&!Has(schema,table,column))
                wrong.Add($"{schema}.{table} missing {column} in snapshot");
            if(sql.Contains("[Date]",StringComparison.Ordinal)&&column!="Date")
                wrong.Add($"{kind} still filters on [Date]");
            if(sql.Contains("[StartedAt]",StringComparison.Ordinal)&&!sql.Contains("[StartedAtUtc]",StringComparison.Ordinal))
                wrong.Add($"{kind} still filters on [StartedAt] without Utc");
        }
        Assert.AreEqual(0,wrong.Count,string.Join("\n",wrong));
    }

    [TestMethod]
    public void CompanyScopeUsesUnifiedNationalNumber()
    {
        var page=new LiveOperationsQuery.PageRequest(1,25,"id","",0,25);
        var scope=new LiveOperationsQuery.Scope(false,["7000000000"],[],[],[]);
        string sql=LiveOperationsQuery.PreviewSql("platform_account",scope,page).CountSql;
        StringAssert.Contains(sql,"UnifiedNationalNumber");
        Assert.IsFalse(sql.Contains("CommercialRegistrationNumber",StringComparison.Ordinal),sql);
        Assert.IsTrue(Has("app","Sponsors","UnifiedNationalNumber"));
    }

    static string? SchemaOf(string kind)
    {
        // Mirror LiveOperationsQuery table schemas for the kinds under test.
        return kind switch
        {
            "ninja_daily" or "ninja_daily_deduction" or "ninja_evaluation_snapshot"=>"ninja",
            "chefz_daily" or "chefz_order_row" or "chefz_account" or "chefz_account_type_change" or "chefz_settlement"=>"chefz",
            "jahez_daily_order" or "jahez_commission_policy"=>"jahez_v2",
            "platform_day_usage" or "platform_account" or "vehicle_assignment" or "vehicle" or "housing_residence"
                or "rider_work_timeline" or "fuel_usage" or "custody_handover" or "vehicle_operational_status"
                or "legal_case" or "housing"=>"app",
            "vehicle_expense" or "custody_equipment"=>"maintenance",
            "city"=>"platform",
            _=>null
        };
    }

    static IEnumerable<(string Schema,string Table)> ExtractTables(string sql)
    {
        foreach(Match m in Regex.Matches(sql,@"\[([A-Za-z_][A-Za-z0-9_]*)\]\.\[([A-Za-z_][A-Za-z0-9_]*)\]"))
            yield return (m.Groups[1].Value,m.Groups[2].Value);
    }

    static IEnumerable<string> ExtractColumns(string sql)
    {
        var schemas=ExtractTables(sql).Select(t=>t.Schema).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tables=ExtractTables(sql).Select(t=>t.Table).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(Match m in Regex.Matches(sql,@"(?:\b[A-Za-z_][A-Za-z0-9_]*\.)?\[([A-Za-z_][A-Za-z0-9_]*)\]"))
        {
            string name=m.Groups[1].Value;
            if(schemas.Contains(name)||tables.Contains(name))continue;
            if(IsSqlBuiltin(name))continue;
            yield return name;
        }
    }

    static bool IsSqlBuiltin(string name)=>name is "value" or "bigint" or "uniqueidentifier";

    sealed class SchemaComparer:IEqualityComparer<(string Schema,string Table)>
    {
        public bool Equals((string Schema,string Table) x,(string Schema,string Table) y)
            =>string.Equals(x.Schema,y.Schema,StringComparison.OrdinalIgnoreCase)
              &&string.Equals(x.Table,y.Table,StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Schema,string Table) obj)
            =>HashCode.Combine(obj.Schema.ToUpperInvariant(),obj.Table.ToUpperInvariant());
    }
}
