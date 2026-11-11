using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
namespace ErpImport;

public sealed record SourceWatermark(string TableName,string ColumnKind,string LastValue,string RunId,DateTimeOffset UpdatedAtUtc);

/// <summary>Target-side watermarks for incremental ERP reads. Counts/opaque tokens only.</summary>
public static class SourceWatermarks
{
    public const string SqlTable="sync.SourceWatermarks";
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        if(c is SqlConnection)
            cmd.CommandText="""
                IF SCHEMA_ID(N'sync') IS NULL EXEC(N'CREATE SCHEMA sync');
                IF OBJECT_ID(N'sync.SourceWatermarks',N'U') IS NULL
                  CREATE TABLE sync.SourceWatermarks(
                    TableName nvarchar(128) NOT NULL CONSTRAINT PK_SourceWatermarks PRIMARY KEY,
                    ColumnKind nvarchar(32) NOT NULL,
                    LastValue nvarchar(128) NOT NULL,
                    RunId nvarchar(64) NOT NULL,
                    UpdatedAtUtc datetime2 NOT NULL);
                """;
        else
            cmd.CommandText="""
                CREATE TABLE IF NOT EXISTS sync_source_watermarks(
                  table_name TEXT NOT NULL PRIMARY KEY,
                  column_kind TEXT NOT NULL,
                  last_value TEXT NOT NULL,
                  run_id TEXT NOT NULL,
                  updated_at_utc TEXT NOT NULL);
                """;
        cmd.ExecuteNonQuery();
    }
    public static Dictionary<string,SourceWatermark> Load(DbConnection c,DbTransaction? tx=null)
    {
        Ensure(c,tx);
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=c is SqlConnection
            ?"SELECT TableName,ColumnKind,LastValue,RunId,UpdatedAtUtc FROM sync.SourceWatermarks"
            :"SELECT table_name,column_kind,last_value,run_id,updated_at_utc FROM sync_source_watermarks";
        var map=new Dictionary<string,SourceWatermark>(StringComparer.OrdinalIgnoreCase);
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
        {
            string updated=reader.GetValue(4) switch
            {
                DateTime dt=>dt.ToString("O",CultureInfo.InvariantCulture),
                DateTimeOffset dto=>dto.ToString("O",CultureInfo.InvariantCulture),
                _=>reader.GetString(4)
            };
            DateTimeOffset.TryParse(updated,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var at);
            map[reader.GetString(0)]=new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),at);
        }
        return map;
    }
    public static void Save(DbConnection c,DbTransaction? tx,IEnumerable<SourceWatermark> rows)
    {
        Ensure(c,tx);
        foreach(var row in rows)
        {
            using var cmd=c.CreateCommand();cmd.Transaction=tx;
            if(c is SqlConnection)
            {
                cmd.CommandText="""
                    MERGE sync.SourceWatermarks AS t
                    USING (SELECT @table AS TableName) AS s ON t.TableName=s.TableName
                    WHEN MATCHED THEN UPDATE SET ColumnKind=@kind,LastValue=@value,RunId=@run,UpdatedAtUtc=@at
                    WHEN NOT MATCHED THEN INSERT(TableName,ColumnKind,LastValue,RunId,UpdatedAtUtc) VALUES(@table,@kind,@value,@run,@at);
                    """;
                Add(cmd,"@table",row.TableName);Add(cmd,"@kind",row.ColumnKind);Add(cmd,"@value",row.LastValue);Add(cmd,"@run",row.RunId);Add(cmd,"@at",row.UpdatedAtUtc.UtcDateTime);
            }
            else
            {
                cmd.CommandText="""
                    INSERT INTO sync_source_watermarks(table_name,column_kind,last_value,run_id,updated_at_utc)
                    VALUES(@table,@kind,@value,@run,@at)
                    ON CONFLICT(table_name) DO UPDATE SET
                      column_kind=excluded.column_kind,last_value=excluded.last_value,run_id=excluded.run_id,updated_at_utc=excluded.updated_at_utc;
                    """;
                Add(cmd,"@table",row.TableName);Add(cmd,"@kind",row.ColumnKind);Add(cmd,"@value",row.LastValue);Add(cmd,"@run",row.RunId);Add(cmd,"@at",row.UpdatedAtUtc.ToString("O",CultureInfo.InvariantCulture));
            }
            cmd.ExecuteNonQuery();
        }
    }
    public static IEnumerable<SourceWatermark> FromNext(IReadOnlyDictionary<string,string> next,string runId,DateTimeOffset at,IReadOnlyDictionary<string,SourceWatermark>? previous=null)
    {
        foreach(var (key,value) in next)
        {
            if(key.StartsWith("__",StringComparison.Ordinal))continue;
            string kind=previous?.GetValueOrDefault(key)?.ColumnKind
                ??(key.StartsWith("dataset:",StringComparison.OrdinalIgnoreCase)?"dataset_version"
                :SourceReadProfiles.All.FirstOrDefault(p=>p.Key.Equals(key,StringComparison.OrdinalIgnoreCase))?.ColumnKind switch
                {
                    SourceColumnKind.RowVersion=>"rowversion",
                    SourceColumnKind.CreatedAt=>"created_at",
                    SourceColumnKind.Id=>"id",
                    SourceColumnKind.OpenPeriod=>"id",
                    _=>"rowversion"
                });
            yield return new(key,kind,value,runId,at);
        }
    }
    public static IEnumerable<SourceWatermark> ReadDatasetVersions(DbConnection source,string runId,DateTimeOffset at,CancellationToken cancellationToken=default)
    {
        if(source is not SqlConnection)yield break;
        if(!SourceReader.OptionalAvailablePublic(source,new("app","DatasetVersions","Id ModuleKey Version LastChangedAtUtc IsDeleted",true),cancellationToken))yield break;
        using var cmd=source.CreateCommand();using var cancel=SyncCancellation.Register(cmd,cancellationToken);
        cmd.CommandText="SELECT [ModuleKey],[Version] FROM [app].[DatasetVersions] WHERE [IsDeleted]=0";
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
            yield return new("dataset:"+reader.GetString(0),"dataset_version",Convert.ToInt64(reader.GetValue(1)).ToString(CultureInfo.InvariantCulture),runId,at);
    }
    static void Add(DbCommand cmd,string name,object value)
    {var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value;cmd.Parameters.Add(p);}
}
