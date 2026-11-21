using System.Data.Common;
namespace Payroll;

/// <summary>Set-based, dual-provider guards. NULL-safe comparisons also protect approval timestamps.</summary>
public static class SchemaMoneyGuard
{
    public static string Changed(DbConnection c,string n,string o,params string[] columns)=>SchemaSql.IsSql(c)
        ?$"EXISTS(SELECT {string.Join(',',columns.Select(x=>$"{n}.{x}"))} EXCEPT SELECT {string.Join(',',columns.Select(x=>$"{o}.{x}"))})"
        :string.Join(" OR ",columns.Select(x=>$"{n}.{x} IS NOT {o}.{x}"));

    public static void Rule(DbConnection c,DbTransaction? tx,string schema,string table,string name,Func<string,string> invalidNew,Func<string,string,string>? invalidChange=null)
    {
        string t=SchemaSql.Q(c,schema,table);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER {schema}.{name} ON {t} AFTER INSERT,UPDATE AS BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted n WHERE {invalidNew("n")})
                    THROW 51030,'{name}',1;
                  {(invalidChange==null?"":$"IF EXISTS(SELECT 1 FROM deleted o LEFT JOIN inserted n ON n.id=o.id WHERE {invalidChange("o","n")}) THROW 51030,'{name}',1;")}
                END;
                """);
        else
            foreach(string op in new[]{"INSERT","UPDATE"})
                SchemaSql.SqliteTrigger(c,tx,$"""
                    CREATE TRIGGER {name}_{op} BEFORE {op} ON {t} BEGIN
                      SELECT RAISE(ABORT,'{name}') WHERE {invalidNew("NEW")};
                      {(op=="UPDATE"&&invalidChange!=null?$"SELECT RAISE(ABORT,'{name}') WHERE {invalidChange("OLD","NEW")};":"")}
                    END;
                    """);
    }
    /// <summary>Retention is unconditional. SQLite REPLACE must be rejected before its implicit delete,
    /// because the app deliberately does not enable recursive_triggers. Include every alternate unique key.
    /// SQL Server: INSTEAD OF DELETE/INSERT so the guard message wins over FK and UNIQUE (AFTER fires too late).</summary>
    /// <param name="replace">SQL Server allows only one INSTEAD OF INSERT per table; pass false when another
    /// INSTEAD OF INSERT (e.g. pay.TR_payment_state) already performs the replacement check and insert.</param>
    public static void Retain(DbConnection c,DbTransaction? tx,string schema,string table,string? conflict=null,bool replace=true)
    {
        string t=SchemaSql.Q(c,schema,table),name=$"TR_{schema}_{table}";
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"CREATE OR ALTER TRIGGER {schema}.{name}_no_delete ON {t} INSTEAD OF DELETE AS BEGIN IF EXISTS(SELECT 1 FROM deleted) THROW 51031,'Money rows cannot be deleted',1; END;");
            if(!replace)return;
            string match=conflict==null?"o.id=n.id":$"o.id=n.id OR ({conflict.Replace("NEW.","n.")})";
            string columns=InsertableColumns(c,tx,schema,table);
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER {schema}.{name}_no_replace ON {t} INSTEAD OF INSERT AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted n WHERE EXISTS(SELECT 1 FROM {t} o WHERE {match}))
                    THROW 51032,'Money row replacement is forbidden',1;
                  INSERT INTO {t} ({columns}) SELECT {columns} FROM inserted;
                END;
                """);
        }
        else
        {
            SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER {name}_no_delete BEFORE DELETE ON {t} BEGIN SELECT RAISE(ABORT,'Money rows cannot be deleted'); END;");
            SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER {name}_no_replace BEFORE INSERT ON {t} WHEN EXISTS(SELECT 1 FROM {t} o WHERE o.id=NEW.id{(conflict==null?"":" OR ("+conflict+")")}) BEGIN SELECT RAISE(ABORT,'Money row replacement is forbidden'); END;");
        }
    }

    /// <summary>Writable columns for INSTEAD OF INSERT (excludes rowversion/timestamp, which SQL Server fills).</summary>
    static string InsertableColumns(DbConnection c,DbTransaction? tx,string schema,string table)
    {
        using var cmd=c.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="SELECT STRING_AGG(QUOTENAME(c.name),',') WITHIN GROUP (ORDER BY c.column_id) FROM sys.columns c WHERE c.object_id=OBJECT_ID(@obj) AND c.is_computed=0 AND c.system_type_id<>189";
        var p=cmd.CreateParameter();p.ParameterName="@obj";p.Value=$"{schema}.{table}";cmd.Parameters.Add(p);
        return (string)(cmd.ExecuteScalar()??throw new InvalidOperationException($"No insertable columns for {schema}.{table}."));
    }

}
