using System.Data.Common;
using System.Text.Json;
namespace ErpImport;

public static class SchemaInspector
{
    public static List<Dictionary<string,object?>> Query(DbConnection c,string sql)
    {
        using var command=c.CreateCommand();command.CommandText=sql;command.CommandTimeout=30;
        using var reader=command.ExecuteReader();var rows=new List<Dictionary<string,object?>>();
        while(reader.Read())rows.Add(Enumerable.Range(0,reader.FieldCount).ToDictionary(reader.GetName,i=>reader.IsDBNull(i)?null:reader.GetValue(i)));
        return rows;
    }
    public static object Read(DbConnection c)=>new
    {
        tables=Query(c,"SELECT s.name AS [schema],t.name AS [table],COALESCE(SUM(p.rows),0) AS metadata_rows FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id LEFT JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1) GROUP BY s.name,t.name ORDER BY s.name,t.name"),
        columns=Query(c,"SELECT s.name AS [schema],t.name AS [table],c.column_id,c.name AS [column],ty.name AS [type],c.max_length,c.precision,c.scale,c.is_nullable FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id ORDER BY s.name,t.name,c.column_id"),
        keys=Query(c,"SELECT s.name AS [schema],t.name AS [table],i.name AS [key],i.is_primary_key,i.is_unique,c.name AS [column],ic.key_ordinal FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.indexes i ON i.object_id=t.object_id AND (i.is_primary_key=1 OR i.is_unique=1) JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id ORDER BY s.name,t.name,i.name,ic.key_ordinal"),
        relations=Query(c,"SELECT s.name AS [schema],t.name AS [table],c.name AS [column],f.name AS [foreign_key],rs.name AS reference_schema,rt.name AS reference_table,rc.name AS reference_column FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id JOIN sys.tables t ON t.object_id=f.parent_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id JOIN sys.tables rt ON rt.object_id=fc.referenced_object_id JOIN sys.schemas rs ON rs.schema_id=rt.schema_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id ORDER BY s.name,t.name,f.name,fc.constraint_column_id")
    };
    public static void Write(DbConnection c,string path)=>File.WriteAllText(path,JsonSerializer.Serialize(Read(c),new JsonSerializerOptions{WriteIndented=true}));
}
