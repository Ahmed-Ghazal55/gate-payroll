using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
namespace Payroll;

/// <summary>Shared dual-dialect helpers for additive schema Ensure (SQL Server + SQLite).</summary>
public static class SchemaSql
{
    public const string MigrationLockResource="GatePayroll.SchemaMigration";
    static readonly object SqliteMigrationGate=new();
    public static bool IsSql(DbConnection c)=>c is SqlConnection;
    public static Guid NewId()=>Guid.CreateVersion7();
    /// <summary>Serialize master DDL so two app instances cannot collide. SQL Server uses sp_getapplock; SQLite uses a process lock (single-connection Ensure is otherwise fine).</summary>
    public static void WithMigrationLock(DbConnection c,Action<DbConnection,DbTransaction?> action)
    {
        try
        {
            if(IsSql(c))
            {
                using var tx=c.BeginTransaction();
                try
                {
                    using(var guard=c.CreateCommand())
                    {
                        guard.Transaction=tx;
                        guard.CommandText="DECLARE @result int; EXEC @result=sp_getapplock @Resource=@resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=60000; IF @result < 0 THROW 51000,N'Schema migration busy',1;";
                        var p=guard.CreateParameter();p.ParameterName="@resource";p.Value=MigrationLockResource;guard.Parameters.Add(p);
                        guard.ExecuteNonQuery();
                    }
                    action(c,tx);
                    tx.Commit();
                }
                catch
                {
                    try{tx.Rollback();}catch{/* already failed */}
                    throw;
                }
            }
            else
            {
                lock(SqliteMigrationGate)action(c,null);
            }
        }
        catch(DbException ex)
        {
            throw new InvalidOperationException(SanitizeSchemaError(ex),ex);
        }
    }
    /// <summary>Fail startup with a clear message; strip connection/server noise and never echo parameter values.</summary>
    public static string SanitizeSchemaError(Exception ex)
    {
        string raw=ex.GetBaseException().Message??"";
        // Keep SQL number / short text; drop host, catalog, and long dumps.
        string trimmed=raw.Length>240?raw[..240]+"…":raw;
        foreach(char ch in new[]{'\r','\n','\t'})trimmed=trimmed.Replace(ch,' ');
        return "Master schema migration failed: "+trimmed;
    }
    /// <summary>Store GUID parameters as D-format text so SQLite reopen matches (blob Guid params do not).</summary>
    public static string GuidText(Guid id)=>id.ToString("D");
    public static string GuidType(DbConnection c)=>IsSql(c)?"uniqueidentifier":"TEXT";
    public static string PrimaryKey(DbConnection c)=>IsSql(c)?"PRIMARY KEY NONCLUSTERED":"PRIMARY KEY";
    public static string RowVersion(DbConnection c)=>IsSql(c)?"rowversion NOT NULL":"BLOB NOT NULL DEFAULT (randomblob(8)) CHECK (length(row_version)=8)";
    public static string MoneyAuditCols(DbConnection c)=>
        $"created_by {Text(c,128)} NULL, created_at {DateTime(c)} NOT NULL DEFAULT ({NowLiteral(c)}), updated_by {Text(c,128)} NULL, updated_at {DateTime(c)} NULL, row_version {RowVersion(c)}, generation {Varchar(c,10)} NOT NULL DEFAULT 'new' CHECK (generation IN ('new','legacy'))";
    public static string MonthCheck(DbConnection c,string column="period_month")=>IsSql(c)
        ?$"{column} = DATEFROMPARTS(YEAR({column}), MONTH({column}), 1)"
        :$"length({column})=10 AND date({column}) IS NOT NULL AND {column}=date({column}) AND strftime('%d',{column})='01'";
    /// <summary>DDL identifiers and definitions are code constants, never request input.</summary>
    public static void EnsureTable(DbConnection c,DbTransaction? tx,string schema,string table,string columns)
    {
        EnsureSchema(c,tx,schema);
        string create=$"CREATE TABLE {Q(c,schema,table)} ({columns});";
        Exec(c,tx,IsSql(c)?$"IF OBJECT_ID(N'{schema}.{table}',N'U') IS NULL {create}":create.Replace("CREATE TABLE ","CREATE TABLE IF NOT EXISTS "));
    }
    public static void EnsureMonthIndex(DbConnection c,DbTransaction? tx,string schema,string table)
    {
        string name=$"CX_{schema}_{table}";
        if(IsSql(c))Exec(c,tx,$"IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'{schema}.{table}') AND name=N'{name}') CREATE CLUSTERED INDEX [{name}] ON {Q(c,schema,table)}(period_month,id);");
        else CreateIndexIfMissing(c,tx,schema,table,name,"period_month,id");
    }
    /// <summary>SQLite test equivalent of a native SQL Server rowversion. Writers compare the previous token in their UPDATE predicate.</summary>
    public static void EnsureRowVersion(DbConnection c,DbTransaction? tx,string schema,string table)
    {
        if(IsSql(c))return;
        string t=Q(c,schema,table);
        Exec(c,tx,$"CREATE TRIGGER IF NOT EXISTS TR_{schema}_{table}_version AFTER UPDATE ON {t} WHEN NEW.row_version=OLD.row_version BEGIN UPDATE {t} SET row_version=randomblob(8) WHERE id=NEW.id; END;");
    }
    public static string Text(DbConnection c,int n)=>IsSql(c)?$"nvarchar({n})":"TEXT";
    public static string Varchar(DbConnection c,int n)=>IsSql(c)?$"varchar({n})":"TEXT";
    public static string DateTime(DbConnection c)=>IsSql(c)?"datetime2":"TEXT";
    public static string Date(DbConnection c)=>IsSql(c)?"date":"TEXT";
    public static string Decimal(DbConnection c)=>IsSql(c)?"decimal(18,2)":"REAL";
    public static string Binary(DbConnection c,int n)=>IsSql(c)?$"varbinary({n})":"BLOB";
    public static string Int(DbConnection c)=>IsSql(c)?"int":"INTEGER";
    public static string Tiny(DbConnection c)=>IsSql(c)?"tinyint":"INTEGER";
    public static string Bit(DbConnection c)=>IsSql(c)?"bit":"INTEGER";
    public static string Json(DbConnection c)=>IsSql(c)?"nvarchar(max)":"TEXT";
    public static string Q(DbConnection c,string schema,string table)=>IsSql(c)?$"[{schema}].[{table}]":$"{schema}_{table}";
    public static void Exec(DbConnection c,DbTransaction? tx,string sql)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;cmd.ExecuteNonQuery();
    }
    public static void Exec(DbConnection c,DbTransaction? tx,string sql,params (string Name,object Value)[] values)
    {
        using var cmd=Store.Command(c,tx,sql,values);cmd.ExecuteNonQuery();
    }
    public static object? Scalar(DbConnection c,DbTransaction? tx,string sql,params (string Name,object Value)[] values)
    {
        using var cmd=Store.Command(c,tx,sql,values);return cmd.ExecuteScalar();
    }
    public static void EnsureSchema(DbConnection c,DbTransaction? tx,string schema)
    {
        if(!IsSql(c))return;
        Exec(c,tx,$"IF SCHEMA_ID(N'{schema}') IS NULL EXEC(N'CREATE SCHEMA [{schema}]');");
    }
    public static void EnsureSchemas(DbConnection c,DbTransaction? tx,params string[] schemas)
    {foreach(var s in schemas)EnsureSchema(c,tx,s);}
    public static bool TableExists(DbConnection c,DbTransaction? tx,string schema,string table)
    {
        if(IsSql(c))
            return Convert.ToInt32(Scalar(c,tx,"SELECT CASE WHEN OBJECT_ID(@name,N'U') IS NULL THEN 0 ELSE 1 END",("@name",$"{schema}.{table}"))!)==1;
        return Convert.ToInt64(Scalar(c,tx,"SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=@name",("@name",$"{schema}_{table}"))!)>0;
    }
    public static string AuditCols(DbConnection c)=>
        $"created_by {Text(c,128)} NULL, created_at {DateTime(c)} NOT NULL, updated_by {Text(c,128)} NULL, updated_at {DateTime(c)} NULL, row_version {Binary(c,8)} NULL";
    public static string NowLiteral(DbConnection c)=>IsSql(c)?"SYSUTCDATETIME()":"datetime('now')";
    public static string GuidLiteral(DbConnection c,Guid id)=>IsSql(c)?$"'{id:D}'":$"'{id:D}'";
    public static void CreateIndexIfMissing(DbConnection c,DbTransaction? tx,string schema,string table,string indexName,string columns)
    {
        string t=Q(c,schema,table);
        if(IsSql(c))
            Exec(c,tx,$"IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'{schema}.{table}') AND name=N'{indexName}') CREATE INDEX [{indexName}] ON {t}({columns});");
        else
            Exec(c,tx,$"CREATE INDEX IF NOT EXISTS {indexName} ON {t}({columns});");
    }
    /// <summary>Drop a named UNIQUE constraint if present (SQL Server key constraint or SQLite table rebuild is not needed when CREATE IF NOT EXISTS is fresh).</summary>
    public static void DropUniqueConstraintIfExists(DbConnection c,DbTransaction? tx,string schema,string table,string constraintName)
    {
        if(IsSql(c))
            Exec(c,tx,$"IF EXISTS(SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'{schema}.{table}') AND name=N'{constraintName}') ALTER TABLE {Q(c,schema,table)} DROP CONSTRAINT [{constraintName}];");
        // SQLite: inline UNIQUE on an already-created table cannot be dropped without rebuild; new CREATE definitions omit it.
    }
    public static void DropIndexIfExists(DbConnection c,DbTransaction? tx,string schema,string table,string indexName)
    {
        if(IsSql(c))Exec(c,tx,$"IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'{schema}.{table}') AND name=N'{indexName}') DROP INDEX [{indexName}] ON {Q(c,schema,table)};");
        else Exec(c,tx,$"DROP INDEX IF EXISTS {indexName};");
    }
    /// <summary>Filtered/partial unique index so multiple NULLs are allowed on both providers.</summary>
    public static void CreateFilteredUniqueIndexIfMissing(DbConnection c,DbTransaction? tx,string schema,string table,string indexName,string columns,string whereSql)
    {
        string t=Q(c,schema,table);
        if(IsSql(c))
            Exec(c,tx,$"IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'{schema}.{table}') AND name=N'{indexName}') CREATE UNIQUE INDEX [{indexName}] ON {t}({columns}) WHERE {whereSql};");
        else
            Exec(c,tx,$"CREATE UNIQUE INDEX IF NOT EXISTS {indexName} ON {t}({columns}) WHERE {whereSql};");
    }
}
