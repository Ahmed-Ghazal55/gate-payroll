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
}
