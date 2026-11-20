using System.Data.Common;
namespace Payroll;

/// <summary>H2: once a money operation is frozen, approved or posted, its run mode is fixed and a payout block
/// can only be added, never removed. <paramref name="frozen"/> builds the frozen predicate for an alias of the old row.</summary>
public static class SchemaMoneyFlags
{
    public static void Ensure(DbConnection c,DbTransaction? tx,string schema,string table,Func<string,string> frozen)
    {
        string t=SchemaSql.Q(c,schema,table),name=$"TR_{table}_money_flags";
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER {schema}.{name} ON {t} AFTER UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted o LEFT JOIN inserted n ON n.id=o.id WHERE ({frozen("o")})
                    AND (n.id IS NULL OR n.run_mode<>o.run_mode OR (o.payout_blocked=1 AND n.payout_blocked=0)))
                    THROW 51016,'Frozen money operation cannot change run mode or remove payout block',1;
                END;
                """);
        else
            SchemaSql.SqliteTrigger(c,tx,$"""
                CREATE TRIGGER {name} BEFORE UPDATE ON {t}
                WHEN ({frozen("OLD")}) AND (NEW.id<>OLD.id OR NEW.run_mode<>OLD.run_mode OR (OLD.payout_blocked=1 AND NEW.payout_blocked=0))
                BEGIN SELECT RAISE(ABORT,'Frozen money operation cannot change run mode or remove payout block'); END;
                """);
    }
}
