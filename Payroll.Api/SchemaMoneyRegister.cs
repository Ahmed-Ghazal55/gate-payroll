using System.Data.Common;
namespace Payroll;

/// <summary>Additive register helpers. No legacy writers/readers or backfill.</summary>
public static class SchemaMoneyRegister
{
    public static void Immutable(DbConnection c,DbTransaction? tx,string schema,string table,params string[] columns)=>
        SchemaMoneyGuard.Rule(c,tx,schema,table,$"TR_{schema}_{table}_immutable",_=>"1=0",
            (o,n)=>$"{n}.id IS NULL OR ({SchemaMoneyGuard.Changed(c,n,o,columns.Prepend("id").ToArray())})");

    /// <summary>SQL Server-only: INSTEAD OF UPDATE so an immutable message wins over FK when keys are rewritten.
    /// Only one INSTEAD OF UPDATE is allowed per table — caller must not install another.</summary>
    public static void ImmutableInsteadOfUpdate(DbConnection c,DbTransaction? tx,string schema,string table,params string[] columns)
    {
        if(!SchemaSql.IsSql(c)){Immutable(c,tx,schema,table,columns);return;}
        string t=SchemaSql.Q(c,schema,table),name=$"TR_{schema}_{table}_immutable";
        string cols=InsertableColumns(c,tx,schema,table);
        string invalid=$"{SchemaMoneyGuard.Changed(c,"n","o",columns.Prepend("id").ToArray())}";
        SchemaSql.Exec(c,tx,$"""
            CREATE OR ALTER TRIGGER {schema}.{name} ON {t} INSTEAD OF UPDATE AS BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted o LEFT JOIN inserted n ON n.id=o.id WHERE n.id IS NULL OR ({invalid}))
                THROW 51030,'{name}',1;
              UPDATE tgt SET {string.Join(",",cols.Split(',').Select(col=>$"{col}=n.{col}"))}
              FROM {t} tgt INNER JOIN inserted n ON tgt.id=n.id;
            END;
            """);
    }

    public static string InsertableColumns(DbConnection c,DbTransaction? tx,string schema,string table)
    {
        using var cmd=c.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="SELECT STRING_AGG(QUOTENAME(c.name),',') WITHIN GROUP (ORDER BY c.column_id) FROM sys.columns c WHERE c.object_id=OBJECT_ID(@obj) AND c.is_computed=0 AND c.system_type_id<>189";
        var p=cmd.CreateParameter();p.ParameterName="@obj";p.Value=$"{schema}.{table}";cmd.Parameters.Add(p);
        return (string)(cmd.ExecuteScalar()??throw new InvalidOperationException($"No insertable columns for {schema}.{table}."));
    }

    /// <summary>Register the row's app-created v7 id as the guard id too. Keys survive archival.
    /// keySql must produce the same canonical string on both providers.</summary>
    public static void BusinessKey(DbConnection c,DbTransaction? tx,string schema,string table,Func<string,string> keySql)
    {
        string t=SchemaSql.Q(c,schema,table),guard=SchemaSql.Q(c,"ref","business_key_guard"),name=$"TR_{schema}_{table}_key";
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"CREATE OR ALTER TRIGGER {schema}.{name} ON {t} AFTER INSERT AS BEGIN SET NOCOUNT ON; INSERT INTO {guard}(id,object_type,business_key,row_id) SELECT n.id,'{schema}.{table}',{keySql("n")},n.id FROM inserted n; END;");
        else
            SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER {name} AFTER INSERT ON {t} BEGIN INSERT INTO {guard}(id,object_type,business_key,row_id) VALUES(NEW.id,'{schema}.{table}',{keySql("NEW")},NEW.id); END;");
    }
    public static string IdString(DbConnection c,string expression)=>SchemaSql.IsSql(c)?$"LOWER(CONVERT(varchar(36),{expression}))":$"lower({expression})";
    public static string Join(DbConnection c,params string[] parts)=>string.Join(SchemaSql.IsSql(c)?"+'|' +":"||'|'||",parts);

    /// <summary>Financial workflow: 1 draft, 2 submitted, 3 approved, 4 rejected, 5 cancelled.
    /// General approval is permission-based (M-0672); authorization belongs to W2, not a role-name CHECK.</summary>
    public static void Workflow(DbConnection c,DbTransaction? tx,string schema,string table,params string[] frozenColumns)
    {
        void State()=>SchemaMoneyGuard.Rule(c,tx,schema,table,$"TR_{schema}_{table}_state",
            n=>$"{n}.status=3 AND ({n}.approved_by IS NULL OR {n}.approved_at IS NULL OR {n}.run_mode=1)",
            (o,n)=>$"{n}.id IS NULL OR ({o}.status<>{n}.status AND NOT ({o}.status=1 AND {n}.status IN (2,5) OR {o}.status=2 AND {n}.status IN (3,4,5))) OR ({o}.status>=3 AND ({SchemaMoneyGuard.Changed(c,n,o,frozenColumns.Concat(new[]{"approved_by","approved_at","run_mode","payout_blocked"}).ToArray())}))");
        void Flags()=>SchemaMoneyFlags.Ensure(c,tx,schema,table,o=>$"{o}.status>=3 OR {o}.payout_blocked=1 OR {o}.run_mode=1");
        // Callers may install more specific AFTER UPDATE rules after Workflow; keep Flags last on SQL Server so those win.
        if(SchemaSql.IsSql(c)){State();Flags();}else{Flags();State();}
        SchemaMoneyGuard.Retain(c,tx,schema,table);
        SchemaSql.EnsureRowVersion(c,tx,schema,table);
    }
    public static string WorkflowColumns(DbConnection c)=>$"status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1 CHECK(status IN (1,2,3,4,5)), run_mode {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0 CHECK(run_mode IN (0,1)), payout_blocked {SchemaSql.Bit(c)} NOT NULL DEFAULT 0 CHECK(payout_blocked IN (0,1)), approved_by {SchemaSql.GuidType(c)} NULL, approved_at {SchemaSql.DateTime(c)} NULL";
}
