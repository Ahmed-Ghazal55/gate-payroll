using System.Data.Common;
namespace Payroll;

/// <summary>RM3V NH-1: paid/reversed payments keep their identity and never reopen.
/// Blocked/simulation flags are sticky from creation or first setting, including queued payments.</summary>
public static class SchemaPaymentState
{
    public static void Ensure(DbConnection c,DbTransaction? tx)
    {
        string p=SchemaSql.Q(c,"pay","payment"),a=SchemaSql.Q(c,"pay","payment_allocation");
        string Invalid(string o,string n)=>$"{n}.status<>{o}.status AND NOT (({o}.status=1 AND {n}.status IN (2,4)) OR ({o}.status=2 AND {n}.status=3))";
        string Changed(string o,string n)=>string.Join(" OR ",new[]{"company_id","payer_company_id","period_month","paid_on","amount","currency_code","payment_no"}.Select(x=>$"{n}.{x}<>{o}.{x}"));
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER pay.TR_payment_state ON {p} AFTER INSERT,UPDATE AS BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted o LEFT JOIN inserted n ON n.id=o.id WHERE
                    n.id IS NULL OR ({Invalid("o","n")}) OR
                    (o.status IN (2,3) AND ({Changed("o","n")})) OR
                    (o.run_mode=1 AND n.run_mode<>o.run_mode) OR (o.payout_blocked=1 AND n.payout_blocked=0))
                    THROW 51022,'Invalid payment transition or frozen identity/flags',1;
                  IF EXISTS(SELECT 1 FROM inserted n WHERE (n.status=2 AND (n.run_mode=1 OR n.payout_blocked=1)) OR
                    (n.status=4 AND EXISTS(SELECT 1 FROM {a} a WHERE a.payment_id=n.id AND a.claim_status=2)))
                    THROW 51023,'Blocked payment cannot pay; posted claims cannot be cancelled',1;
                END;
                """);
        else
            foreach(string op in new[]{"INSERT","UPDATE"})
            {
                string guard=op=="INSERT"?"":$"""
                    SELECT RAISE(ABORT,'Invalid payment transition or frozen identity/flags') WHERE
                      NEW.id<>OLD.id OR ({Invalid("OLD","NEW")}) OR
                      (OLD.status IN (2,3) AND ({Changed("OLD","NEW")})) OR
                      (OLD.run_mode=1 AND NEW.run_mode<>OLD.run_mode) OR (OLD.payout_blocked=1 AND NEW.payout_blocked=0);
                    """;
                SchemaSql.SqliteTrigger(c,tx,$"""
                    CREATE TRIGGER TR_payment_state_{op} BEFORE {op} ON {p} BEGIN
                      {guard}
                      SELECT RAISE(ABORT,'Blocked payment cannot pay; posted claims cannot be cancelled') WHERE
                        (NEW.status=2 AND (NEW.run_mode=1 OR NEW.payout_blocked=1)) OR
                        (NEW.status=4 AND EXISTS(SELECT 1 FROM {a} WHERE payment_id=NEW.id AND claim_status=2));
                    END;
                    """);
            }
    }
}
