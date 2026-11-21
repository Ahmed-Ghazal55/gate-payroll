using System.Data.Common;
namespace Payroll;

/// <summary>RM3V NH-1: paid/reversed payments keep their identity and never reopen.
/// Blocked/simulation flags are sticky from creation or first setting, including queued payments.
/// SQL Server uses INSTEAD OF so these messages win over FK checks and over TR_payment_money_flags order.</summary>
public static class SchemaPaymentState
{
    public static void Ensure(DbConnection c,DbTransaction? tx)
    {
        string p=SchemaSql.Q(c,"pay","payment"),a=SchemaSql.Q(c,"pay","payment_allocation"),j=SchemaSql.Q(c,"fin","journal"),request=SchemaSql.Q(c,"pay","payment_request");
        string NoReversal(string n)=>$"{n}.status=3 AND NOT EXISTS(SELECT 1 FROM {j} original JOIN {j} reverse_entry ON reverse_entry.reverses_journal_id=original.id WHERE original.id={n}.journal_id AND original.company_id={n}.company_id AND original.status=3 AND reverse_entry.company_id=original.company_id AND reverse_entry.status=2 AND reverse_entry.run_mode=0 AND reverse_entry.purpose='reversal')";
        string JournalChanged(string n,string o)=>$"EXISTS(SELECT {n}.journal_id EXCEPT SELECT {o}.journal_id)";
        string Invalid(string o,string n)=>$"{n}.status<>{o}.status AND NOT (({o}.status=1 AND {n}.status IN (2,4)) OR ({o}.status=2 AND {n}.status=3))";
        string Changed(string o,string n)=>string.Join(" OR ",new[]{"company_id","payer_company_id","period_month","paid_on","amount","currency_code","payment_no"}.Select(x=>$"{n}.{x}<>{o}.{x}"));
        string RequestMismatch(string n)=>$"{n}.request_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM {request} r WHERE r.id={n}.request_id AND r.company_id={n}.company_id AND r.period_month={n}.request_period_month AND r.run_mode={n}.run_mode AND r.payout_blocked={n}.payout_blocked)";
        string RequestChanged(string o,string n)=>$"{n}.request_id IS NOT NULL AND ({SchemaMoneyGuard.Changed(c,n,o,"request_id","request_period_month","run_mode","payout_blocked")}) AND {RequestMismatch(n)}";
        if(SchemaSql.IsSql(c))
        {
            string cols=InsertableColumns(c,tx,"pay","payment");
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER pay.TR_payment_state ON {p} INSTEAD OF INSERT AS BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted n WHERE EXISTS(SELECT 1 FROM {p} o WHERE o.id=n.id OR o.payment_no=n.payment_no))
                    THROW 51032,'Money row replacement is forbidden',1;
                  IF EXISTS(SELECT 1 FROM inserted n WHERE {RequestMismatch("n")})
                    THROW 51030,'TR_payment_request_flags',1;
                  IF EXISTS(SELECT 1 FROM inserted n WHERE ({NoReversal("n")}) OR (n.status=2 AND (n.run_mode=1 OR n.payout_blocked=1)) OR
                    (n.status=4 AND EXISTS(SELECT 1 FROM {a} a WHERE a.payment_id=n.id AND a.claim_status=2)))
                    THROW 51023,'Payout blocked or cancellation/reversal lacks evidence',1;
                  INSERT INTO {p} ({cols}) SELECT {cols} FROM inserted;
                END;
                """);
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER pay.TR_payment_state_upd ON {p} INSTEAD OF UPDATE AS BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted o LEFT JOIN inserted n ON n.id=o.id WHERE {RequestChanged("o","n")})
                    THROW 51030,'TR_payment_request_flags',1;
                  IF EXISTS(SELECT 1 FROM deleted o LEFT JOIN inserted n ON n.id=o.id WHERE
                    n.id IS NULL OR ({Invalid("o","n")}) OR
                    (o.status IN (2,3) AND ({Changed("o","n")} OR {JournalChanged("n","o")})) OR
                    (o.run_mode=1 AND n.run_mode<>o.run_mode) OR (o.payout_blocked=1 AND n.payout_blocked=0))
                    THROW 51022,'Invalid payment transition or frozen identity/flags',1;
                  IF EXISTS(SELECT 1 FROM inserted n WHERE ({NoReversal("n")}) OR (n.status=2 AND (n.run_mode=1 OR n.payout_blocked=1)) OR
                    (n.status=4 AND EXISTS(SELECT 1 FROM {a} a WHERE a.payment_id=n.id AND a.claim_status=2)))
                    THROW 51023,'Payout blocked or cancellation/reversal lacks evidence',1;
                  UPDATE t SET {string.Join(",",cols.Split(',').Select(col=>$"{col}=n.{col}"))}
                  FROM {p} t INNER JOIN inserted n ON t.id=n.id;
                END;
                """);
        }
        else
            foreach(string op in new[]{"INSERT","UPDATE"})
            {
                string guard=op=="INSERT"?"":$"""
                    SELECT RAISE(ABORT,'Invalid payment transition or frozen identity/flags') WHERE
                      NEW.id<>OLD.id OR ({Invalid("OLD","NEW")}) OR
                      (OLD.status IN (2,3) AND ({Changed("OLD","NEW")} OR {JournalChanged("NEW","OLD")})) OR
                      (OLD.run_mode=1 AND NEW.run_mode<>OLD.run_mode) OR (OLD.payout_blocked=1 AND NEW.payout_blocked=0);
                    """;
                SchemaSql.SqliteTrigger(c,tx,$"""
                    CREATE TRIGGER TR_payment_state_{op} BEFORE {op} ON {p} BEGIN
                      {guard}
                      SELECT RAISE(ABORT,'Payout blocked or cancellation/reversal lacks evidence') WHERE
                        ({NoReversal("NEW")}) OR (NEW.status=2 AND (NEW.run_mode=1 OR NEW.payout_blocked=1)) OR
                        (NEW.status=4 AND EXISTS(SELECT 1 FROM {a} WHERE payment_id=NEW.id AND claim_status=2));
                    END;
                    """);
            }
    }

    static string InsertableColumns(DbConnection c,DbTransaction? tx,string schema,string table)
    {
        using var cmd=c.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="SELECT STRING_AGG(QUOTENAME(c.name),',') WITHIN GROUP (ORDER BY c.column_id) FROM sys.columns c WHERE c.object_id=OBJECT_ID(@obj) AND c.is_computed=0 AND c.system_type_id<>189";
        var p=cmd.CreateParameter();p.ParameterName="@obj";p.Value=$"{schema}.{table}";cmd.Parameters.Add(p);
        return (string)(cmd.ExecuteScalar()??throw new InvalidOperationException($"No insertable columns for {schema}.{table}."));
    }
}
