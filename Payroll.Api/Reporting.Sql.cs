using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace Payroll;

public static partial class Reporting
{
    static int sqliteNotice;static readonly ConditionalWeakTable<DbTransaction,SchemaState> Transactions=new();
    sealed record SchemaState(bool Ready);
    public static bool Initialized(DbConnection c,DbTransaction? tx=null)
    {
        if(c is not SqlConnection){if(Interlocked.Exchange(ref sqliteNotice,1)==0)Trace.TraceInformation("Reporting skipped: SQLite has no SQL Server rpt schema.");return false;}
        using var cmd=Store.Command(c,tx,"IF OBJECT_ID(N'rpt.Meta',N'U') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM rpt.Meta WHERE SchemaVersion=1 AND Id=1");return Convert.ToInt32(cmd.ExecuteScalar(),CultureInfo.InvariantCulture)==1;
    }
    public static void EnsureSchema(DbConnection c,DbTransaction tx)
    {
        if(c is not SqlConnection)throw new InvalidOperationException("reporting_requires_sql_server");
        using(var schema=Store.Command(c,tx,"IF SCHEMA_ID(N'rpt') IS NULL EXEC('CREATE SCHEMA rpt'); IF OBJECT_ID(N'rpt.Meta',N'U') IS NULL CREATE TABLE rpt.Meta(Id int NOT NULL PRIMARY KEY,SchemaVersion int NOT NULL);"))schema.ExecuteNonQuery();
        foreach(var table in Tables)
        {
            var columns=Common.Concat(table.Extra).ToList();string definition=string.Join(",",columns.Select(col=>$"[{col.Name}] {col.SqlType} {(col.Name is "RowKey" or "SourceRecordId" or "SourceSha"?"NOT NULL":"NULL")}"));
            using var create=Store.Command(c,tx,$"IF OBJECT_ID(N'rpt.{table.Name}',N'U') IS NULL CREATE TABLE rpt.[{table.Name}]({definition},CONSTRAINT [PK_rpt_{table.Name}] PRIMARY KEY(RowKey));");create.ExecuteNonQuery();
            foreach(string reference in columns.Select(col=>col.Name).Where(name=>name.EndsWith("Id",StringComparison.Ordinal)||name is "Period" or "AccountCode"))
            {using var index=Store.Command(c,tx,$"IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'rpt.{table.Name}') AND name=N'IX_{table.Name}_{reference}') CREATE INDEX [IX_{table.Name}_{reference}] ON rpt.[{table.Name}]([{reference}]);");index.ExecuteNonQuery();}
        }
        using var state=Store.Command(c,tx,"IF OBJECT_ID(N'rpt.SourceState',N'U') IS NULL CREATE TABLE rpt.SourceState(SourceRecordId nvarchar(128) NOT NULL PRIMARY KEY,Kind nvarchar(40) NOT NULL,SourceSha char(64) NOT NULL,RowCount int NOT NULL,ProjectionSha char(64) NOT NULL); IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'rpt.SourceState') AND name=N'IX_SourceState_Kind') CREATE INDEX IX_SourceState_Kind ON rpt.SourceState(Kind); IF NOT EXISTS(SELECT 1 FROM rpt.Meta WHERE Id=1) INSERT INTO rpt.Meta(Id,SchemaVersion) VALUES(1,1); IF EXISTS(SELECT 1 FROM rpt.Meta WHERE Id=1 AND SchemaVersion<>1) THROW 51002,'Reporting schema mismatch',1;");state.ExecuteNonQuery();
    }
    static Type TypeFor(ReportColumn column)=>column.SqlType switch{"bit"=>typeof(bool),"int"=>typeof(int),"datetimeoffset(7)"=>typeof(DateTimeOffset),"decimal(19,4)"=>typeof(decimal),_=>typeof(string)};
    static object ValueFor(ReportColumn column,JsonNode? node)
    {
        if(node==null)return DBNull.Value;return column.SqlType switch{"bit"=>node.GetValue<bool>(),"int"=>node.GetValue<int>(),"datetimeoffset(7)"=>DateTimeOffset.Parse(node.ToString(),CultureInfo.InvariantCulture),"decimal(19,4)"=>decimal.Parse(node.ToString(),CultureInfo.InvariantCulture),_=>node.ToString()};
    }
    static void Bulk(SqlConnection c,SqlTransaction tx,string table,IEnumerable<ReportColumn> columns,IEnumerable<JsonObject> rows)
    {
        var values=rows.ToList();if(values.Count==0)return;var fields=columns.ToList();using var data=new DataTable();foreach(var col in fields)data.Columns.Add(col.Name,TypeFor(col));foreach(var row in values)data.Rows.Add(fields.Select(col=>ValueFor(col,row[col.Name])).ToArray());
        using var copy=new SqlBulkCopy(c,SqlBulkCopyOptions.CheckConstraints,tx){DestinationTableName="rpt.["+table+"]",BatchSize=1000,BulkCopyTimeout=120};foreach(var col in fields)copy.ColumnMappings.Add(col.Name,col.Name);copy.WriteToServer(data);
    }
    public static void Refresh(DbConnection c,DbTransaction tx,string kind,JsonObject doc)=>RefreshMany(c,tx,[new(doc.Required("id"),kind,doc.ToJsonString())]);
    public static void RefreshMany(DbConnection c,DbTransaction tx,IEnumerable<ReportDocument> input,bool force=false)
    {
        var documents=input.Where(d=>Tables.Any(t=>t.Kind==d.Kind)).GroupBy(d=>d.Id).Select(g=>g.Last()).ToList();if(documents.Count==0)return;
        bool ready=force||Transactions.GetValue(tx,_=>new(Initialized(c,tx))).Ready;if(!ready)return;if(c is not SqlConnection sql||tx is not SqlTransaction transaction)return;
        var rows=documents.SelectMany(Project).ToList();var bySource=rows.GroupBy(r=>r.Values.S("SourceRecordId")).ToDictionary(g=>g.Key,g=>g.ToList());var stateRows=new List<JsonObject>();
        foreach(var table in Tables.Where(t=>documents.Any(d=>d.Kind==t.Kind)))
        {
            string ids=System.Text.Json.JsonSerializer.Serialize(documents.Where(d=>d.Kind==table.Kind).Select(d=>d.Id));
            using(var remove=Store.Command(c,tx,$"DELETE p FROM rpt.[{table.Name}] p JOIN OPENJSON(@ids) i ON p.SourceRecordId=i.[value]",("@ids",ids)))remove.ExecuteNonQuery();
            Bulk(sql,transaction,table.Name,Common.Concat(table.Extra),rows.Where(r=>r.Table==table.Name).Select(r=>r.Values));
        }
        foreach(var doc in documents){var projected=bySource.GetValueOrDefault(doc.Id)??[];stateRows.Add(new(){["SourceRecordId"]=doc.Id,["Kind"]=doc.Kind,["SourceSha"]=Sha(doc.Body),["RowCount"]=projected.Count,["ProjectionSha"]=RowsSha(projected)}); }
        using(var remove=Store.Command(c,tx,"DELETE p FROM rpt.SourceState p JOIN OPENJSON(@ids) i ON p.SourceRecordId=i.[value]",("@ids",System.Text.Json.JsonSerializer.Serialize(documents.Select(d=>d.Id)))))remove.ExecuteNonQuery();
        Bulk(sql,transaction,"SourceState",[C("SourceRecordId"),C("Kind","nvarchar(40)"),C("SourceSha","char(64)"),C("RowCount","int"),C("ProjectionSha","char(64)")],stateRows);
    }
    public static object DryRun(DbConnection c,DbTransaction? tx=null)
    {
        var docs=Snapshot(c,tx);docs.Add(Chart());var rows=docs.SelectMany(Project).ToList();return new{mode="dry_run",initialized=Initialized(c,tx),kinds=docs.GroupBy(d=>d.Kind).OrderBy(g=>g.Key).Select(g=>new{kind=g.Key,records=g.Count(),rows=rows.Count(r=>r.Kind==g.Key)}),tables=Tables.Select(t=>new{table="rpt."+t.Name,rows=rows.Count(r=>r.Table==t.Name)})};
    }
    public static object Rebuild(DbConnection c,DbTransaction tx)
    {
        string before=System.Text.Json.JsonSerializer.Serialize(ReleaseSafety.Capture(c,tx));var docs=Snapshot(c,tx);docs.Add(Chart());EnsureSchema(c,tx);
        foreach(var table in Tables){using var remove=Store.Command(c,tx,$"DELETE FROM rpt.[{table.Name}]");remove.ExecuteNonQuery();}using(var state=Store.Command(c,tx,"DELETE FROM rpt.SourceState"))state.ExecuteNonQuery();
        RefreshMany(c,tx,docs,true);J.Need(before==System.Text.Json.JsonSerializer.Serialize(ReleaseSafety.Capture(c,tx)),"report_rebuild_changed_records");var check=Check(c,tx);J.Need(check.B("consistent"),"report_rebuild_consistency_failed");return check;
    }
    static List<ReportRow> ReadRows(DbConnection c,DbTransaction? tx)
    {
        var result=new List<ReportRow>();foreach(var table in Tables)
        {
            var columns=Common.Concat(table.Extra).ToList();using var cmd=Store.Command(c,tx,$"SELECT {string.Join(',',columns.Select(col=>"["+col.Name+"]"))} FROM rpt.[{table.Name}]");using var reader=cmd.ExecuteReader();
            while(reader.Read())
            {var row=new JsonObject();for(int i=0;i<columns.Count;i++){var value=reader.GetValue(i);row[columns[i].Name]=value switch{DBNull=>null,DateTimeOffset date=>JsonValue.Create(date.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture)),decimal n=>JsonValue.Create(n),int n=>JsonValue.Create(n),bool b=>JsonValue.Create(b),_=>JsonValue.Create(value.ToString())};}result.Add(new(table.Name,table.Kind,row));}
        }return result;
    }
    public static JsonObject Check(DbConnection c,DbTransaction? tx=null)
    {
        if(!Initialized(c,tx))return new(){["consistent"]=false,["reason"]=c is SqlConnection?"reporting_not_initialized":"sqlite_no_rpt_schema"};
        var docs=Snapshot(c,tx);docs.Add(Chart());var expected=docs.SelectMany(Project).ToList();var actual=ReadRows(c,tx);var states=new List<JsonObject>();
        using(var cmd=Store.Command(c,tx,"SELECT SourceRecordId,Kind,SourceSha,RowCount,ProjectionSha FROM rpt.SourceState"))using(var r=cmd.ExecuteReader())while(r.Read())states.Add(new(){["id"]=r.GetString(0),["kind"]=r.GetString(1),["sha"]=r.GetString(2),["rows"]=r.GetInt32(3),["projection"]=r.GetString(4)});
        var kinds=new JsonArray();bool good=states.Count==docs.Count;
        foreach(string kind in Tables.Select(t=>t.Kind).Distinct().OrderBy(k=>k,StringComparer.Ordinal))
        {
            var source=docs.Where(d=>d.Kind==kind).ToList();var state=states.Where(s=>s.S("kind")==kind).ToList();var er=expected.Where(r=>r.Kind==kind).ToList();var ar=actual.Where(r=>r.Kind==kind).ToList();
            bool sourceMatch=source.Count==state.Count&&source.All(d=>state.Any(s=>s.S("id")==d.Id&&s.S("sha")==Sha(d.Body)&&s.N("rows")==er.Count(r=>r.Values.S("SourceRecordId")==d.Id)&&s.S("projection")==RowsSha(er.Where(r=>r.Values.S("SourceRecordId")==d.Id))));
            string expectedSha=RowsSha(er),actualSha=RowsSha(ar);bool match=sourceMatch&&er.Count==ar.Count&&expectedSha==actualSha;good&=match;
            kinds.Add(new JsonObject{["kind"]=kind,["records"]=source.Count,["expectedRows"]=er.Count,["actualRows"]=ar.Count,["sourceShaMatches"]=sourceMatch,["projectionShaMatches"]=expectedSha==actualSha,["consistent"]=match});
        }
        return new(){["consistent"]=good,["kinds"]=kinds};
    }
}
