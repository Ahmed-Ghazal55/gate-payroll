using System.Data.Common;
namespace Payroll;

/// <summary>Additive register helpers. No legacy writers/readers or backfill.</summary>
public static class SchemaMoneyRegister
{
    public static void Immutable(DbConnection c,DbTransaction? tx,string schema,string table,params string[] columns)=>
        SchemaMoneyGuard.Rule(c,tx,schema,table,$"TR_{schema}_{table}_immutable",_=>"1=0",
            (o,n)=>$"{n}.id IS NULL OR ({SchemaMoneyGuard.Changed(c,n,o,columns.Prepend("id").ToArray())})");

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
        SchemaMoneyGuard.Rule(c,tx,schema,table,$"TR_{schema}_{table}_state",
            n=>$"{n}.status=3 AND ({n}.approved_by IS NULL OR {n}.approved_at IS NULL OR {n}.run_mode=1)",
            (o,n)=>$"{n}.id IS NULL OR ({o}.status<>{n}.status AND NOT ({o}.status=1 AND {n}.status IN (2,5) OR {o}.status=2 AND {n}.status IN (3,4,5))) OR ({o}.status>=3 AND ({SchemaMoneyGuard.Changed(c,n,o,frozenColumns.Concat(new[]{"approved_by","approved_at","run_mode","payout_blocked"}).ToArray())}))");
        SchemaMoneyFlags.Ensure(c,tx,schema,table,o=>$"{o}.status>=3 OR {o}.payout_blocked=1 OR {o}.run_mode=1");
        SchemaMoneyGuard.Retain(c,tx,schema,table);
        SchemaSql.EnsureRowVersion(c,tx,schema,table);
    }
    public static string WorkflowColumns(DbConnection c)=>$"status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1 CHECK(status IN (1,2,3,4,5)), run_mode {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0 CHECK(run_mode IN (0,1)), payout_blocked {SchemaSql.Bit(c)} NOT NULL DEFAULT 0 CHECK(payout_blocked IN (0,1)), approved_by {SchemaSql.GuidType(c)} NULL, approved_at {SchemaSql.DateTime(c)} NULL";
}
