using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace Payroll;

public sealed partial class Store
{
    sealed class BufferedRows { public Dictionary<string,(string Kind,string Body)> Rows { get; }=[]; }
    static readonly ConditionalWeakTable<DbTransaction,BufferedRows> BufferedTransactions=new();
    // Explicit importer-only transport optimization. Callers still use Save and Audit;
    // regular application transactions retain immediate writes and read-your-writes.
    public static void BufferedWrites(DbConnection c,DbTransaction tx,Action save)
    {
        if(c is not SqlConnection sql||tx is not SqlTransaction transaction){save();return;}
        var buffered=new BufferedRows();BufferedTransactions.Add(tx,buffered);
        try
        {
            save();if(buffered.Rows.Count==0)return;
            using(var create=Command(c,tx,"CREATE TABLE #GateStoreBuffered(id nvarchar(128) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY,kind nvarchar(40) COLLATE DATABASE_DEFAULT NOT NULL,body nvarchar(max) NOT NULL);"))create.ExecuteNonQuery();
            using var table=new DataTable();table.Columns.Add("id",typeof(string));table.Columns.Add("kind",typeof(string));table.Columns.Add("body",typeof(string));
            foreach(var row in buffered.Rows)table.Rows.Add(row.Key,row.Value.Kind,row.Value.Body);
            using(var copy=new SqlBulkCopy(sql,SqlBulkCopyOptions.CheckConstraints,transaction))
            {
                copy.DestinationTableName="#GateStoreBuffered";copy.BatchSize=1000;copy.BulkCopyTimeout=120;
                foreach(string name in new[]{"id","kind","body"})copy.ColumnMappings.Add(name,name);
                copy.WriteToServer(table);
            }
            using var apply=Command(c,tx,"""
                IF EXISTS(SELECT 1 FROM dbo.records r JOIN #GateStoreBuffered s ON s.id=r.id WHERE s.kind<>r.kind)
                    THROW 51001,'Record kind conflict',1;
                UPDATE r SET body=s.body FROM dbo.records r JOIN #GateStoreBuffered s ON s.id=r.id AND s.kind=r.kind;
                INSERT INTO dbo.records(id,kind,body)
                    SELECT s.id,s.kind,s.body FROM #GateStoreBuffered s WHERE NOT EXISTS(SELECT 1 FROM dbo.records r WHERE r.id=s.id);
                DROP TABLE #GateStoreBuffered;
                """);apply.CommandTimeout=120;apply.ExecuteNonQuery();
            Reporting.RefreshMany(c,tx,buffered.Rows.Select(row=>new ReportDocument(row.Key,row.Value.Kind,row.Value.Body)));
        }
        finally{BufferedTransactions.Remove(tx);}
    }
    static bool TryBuffer(DbTransaction tx,string kind,JsonObject doc)
    {
        if(!BufferedTransactions.TryGetValue(tx,out var buffer))return false;
        string id=doc.Required("id");if(buffer.Rows.TryGetValue(id,out var previous)&&previous.Kind!=kind)throw new InvalidOperationException("Buffered record kind conflict.");
        buffer.Rows[id]=(kind,doc.ToJsonString());return true;
    }
}
