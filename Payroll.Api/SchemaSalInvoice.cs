using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-14a. Design §4.1–2 (E8), §14, §16. Invoice approval covers totals/company items;
/// rider bearer assignments belong to the later distribution stage. No ingestion or posting runs here.</summary>
public static class SchemaSalInvoice
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c),invoice=SchemaSql.Q(c,"sal","platform_invoice"),line=SchemaSql.Q(c,"sal","platform_invoice_line");
        SchemaSql.EnsureTable(c,tx,"sal","platform_invoice",$"""
            id {g} NOT NULL, app_code {SchemaSql.Varchar(c,20)} NOT NULL,
            company_id {g} NOT NULL, erp_cr_id {g} NOT NULL, city_code {SchemaSql.Varchar(c,40)} NOT NULL,
            period_from {SchemaSql.Date(c)} NOT NULL, period_to {SchemaSql.Date(c)} NOT NULL,
            period_month {SchemaSql.Date(c)} NOT NULL, file_hash {SchemaSql.Binary(c,32)} NOT NULL,
            gross {m} NOT NULL, deductions {m} NOT NULL, net {m} NOT NULL,
            status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1,
            uploaded_by {g} NULL, uploaded_at {SchemaSql.DateTime(c)} NULL,
            approved_by {g} NULL, approved_at {SchemaSql.DateTime(c)} NULL, journal_id {g} NULL,
            run_mode {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0, payout_blocked {SchemaSql.Bit(c)} NOT NULL DEFAULT 0,
            posting_owner {SchemaSql.Varchar(c,20)} NOT NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_platform_invoice {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_invoice_scope UNIQUE (id,company_id,period_month),
            CONSTRAINT UQ_sal_invoice_month UNIQUE (id,period_month),
            CONSTRAINT CK_sal_invoice_totals CHECK (gross>=0 AND deductions>=0 AND net>=0),
            CONSTRAINT CK_sal_invoice_net CHECK ({(SchemaSql.IsSql(c)?"net=gross-deductions":"abs(net-(gross-deductions))<0.005")}),
            CONSTRAINT CK_sal_invoice_status CHECK (status IN (1,2,3,4,5)),
            CONSTRAINT CK_sal_invoice_flags CHECK (run_mode IN (0,1) AND payout_blocked IN (0,1)),
            CONSTRAINT CK_sal_invoice_owner CHECK (posting_owner IN ('invoice','agent_settlement')),
            CONSTRAINT CK_sal_invoice_month CHECK ({SchemaSql.MonthCheck(c)} AND period_from<=period_to),
            CONSTRAINT CK_sal_invoice_hash CHECK ({(SchemaSql.IsSql(c)?"DATALENGTH":"length")}(file_hash)=32),
            CONSTRAINT FK_sal_invoice_company FOREIGN KEY (company_id) REFERENCES {SchemaSql.Q(c,"hr","company")}(id) ON DELETE NO ACTION,
            CONSTRAINT FK_sal_invoice_journal FOREIGN KEY (journal_id) REFERENCES {SchemaSql.Q(c,"fin","journal")}(id) ON DELETE NO ACTION
            """);
        SchemaMoneyFlags.Ensure(c,tx,"sal","platform_invoice",Frozen);
        // L2: a cancelled file may be uploaded again; only active invoices own a file hash.
        SchemaSql.DropUniqueConstraintIfExists(c,tx,"sal","platform_invoice","UQ_sal_invoice_hash");
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"sal","platform_invoice","UQ_sal_invoice_hash_active","app_code,file_hash","status<>4");
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"sal","platform_invoice","UQ_sal_invoice_active","app_code,erp_cr_id,period_from,period_to","status<>4");
        SchemaSql.EnsureTable(c,tx,"sal","platform_invoice_line",$"""
            id {g} NOT NULL, invoice_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            source_row {SchemaSql.Int(c)} NOT NULL, account_key {SchemaSql.Varchar(c,100)} NOT NULL,
            erp_platform_account_id {g} NOT NULL, gross {m} NOT NULL, deductions {m} NOT NULL, net {m} NOT NULL,
            orders {SchemaSql.Int(c)} NOT NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_platform_invoice_line {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_invoice_line_row UNIQUE (invoice_id,source_row),
            CONSTRAINT UQ_sal_invoice_line_month UNIQUE (id,period_month),
            CONSTRAINT CK_sal_invoice_line_values CHECK (source_row>0 AND gross>=0 AND orders>=0),
            CONSTRAINT FK_sal_invoice_line_header FOREIGN KEY (invoice_id,period_month) REFERENCES {invoice}(id,period_month) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureTable(c,tx,"sal","platform_invoice_item",$"""
            id {g} NOT NULL, line_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            item_code {SchemaSql.Varchar(c,40)} NOT NULL, amount {m} NOT NULL,
            bearer_status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1,
            platform_deducted {SchemaSql.Bit(c)} NOT NULL DEFAULT 0,
            due_before_tga {m} NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_platform_invoice_item {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_invoice_item UNIQUE (line_id,item_code),
            CONSTRAINT CK_sal_invoice_item_bearer CHECK (bearer_status IN (1,2,3,4)),
            CONSTRAINT CK_sal_invoice_item_deducted CHECK (platform_deducted IN (0,1)),
            CONSTRAINT FK_sal_invoice_item_line FOREIGN KEY (line_id,period_month) REFERENCES {line}(id,period_month) ON DELETE NO ACTION
            """);
        foreach(string table in new[]{"platform_invoice","platform_invoice_line","platform_invoice_item"})
        {SchemaSql.EnsureMonthIndex(c,tx,"sal",table);SchemaSql.EnsureRowVersion(c,tx,"sal",table);}
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","platform_invoice","IX_sal_invoice_company","company_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","platform_invoice","IX_sal_invoice_journal","journal_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","platform_invoice_line","IX_sal_invoice_line_parent","invoice_id,period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","platform_invoice_item","IX_sal_invoice_item_parent","line_id,period_month");
        SchemaMoneyGuard.Retain(c,tx,"sal","platform_invoice","o.status<>4 AND NEW.status<>4 AND (o.app_code=NEW.app_code AND (o.file_hash=NEW.file_hash OR (o.erp_cr_id=NEW.erp_cr_id AND o.period_from=NEW.period_from AND o.period_to=NEW.period_to)))");
        SchemaMoneyGuard.Retain(c,tx,"sal","platform_invoice_line","o.invoice_id=NEW.invoice_id AND o.source_row=NEW.source_row");
        SchemaMoneyGuard.Retain(c,tx,"sal","platform_invoice_item","o.line_id=NEW.line_id AND o.item_code=NEW.item_code");
        EnsureApprovedFrozen(c,tx);EnsureNoOverlap(c,tx);
        SchemaMoneyGuard.Rule(c,tx,"sal","platform_invoice","TR_invoice_state",n=>"1=0",(o,n)=>$"""
            ({o}.approved_at IS NOT NULL AND ({SchemaMoneyGuard.Changed(c,n,o,"approved_at")})) OR
            ({n}.status<>{o}.status AND NOT
              (({o}.status=1 AND {n}.status IN (2,3,4)) OR ({o}.status=2 AND {n}.status IN (3,4)) OR ({o}.status=3 AND {n}.status IN (4,5)))) OR
            (({Frozen(o)}) AND ({n}.id IS NULL OR {n}.status=1))
            """);
    }

    static string Frozen(string alias)=>$"({alias}.approved_at IS NOT NULL OR {alias}.status IN (2,3,4,5))";
    static readonly string[] HeaderColumns=["app_code","company_id","erp_cr_id","city_code","period_from","period_to","period_month","file_hash","gross","deductions","net","posting_owner"];
    static readonly string[] LineColumns=["id","invoice_id","period_month","source_row","account_key","erp_platform_account_id","gross","deductions","net","orders"];
    static string Changed(DbConnection c,string[] columns,string n,string o)=>
        string.Join(" OR ",columns.Select(x=>SchemaSql.IsSql(c)?$"{n}.{x}<>{o}.{x}":$"{n}.{x} IS NOT {o}.{x}"));

    /// <summary>L2: after approval the header totals and scope and its lines are frozen. Status, journal link and
    /// item bearer assignment (rider split after approval, Ghazal answer 3) stay writable; cancel is a status change.</summary>
    static void EnsureApprovedFrozen(DbConnection c,DbTransaction? tx)
    {
        string invoice=SchemaSql.Q(c,"sal","platform_invoice"),line=SchemaSql.Q(c,"sal","platform_invoice_line");
        const string header="Approved invoice totals and scope are frozen",lines="Lines of an approved invoice are frozen";
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER sal.TR_platform_invoice_frozen ON {invoice} AFTER UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted o LEFT JOIN inserted n ON n.id=o.id WHERE {Frozen("o")} AND (n.id IS NULL OR {Changed(c,HeaderColumns,"n","o")}))
                    THROW 51022,'{header}',1;
                END;
                """);
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER sal.TR_platform_invoice_line_frozen ON {line} AFTER INSERT,UPDATE,DELETE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted n FULL OUTER JOIN deleted o ON o.id=n.id
                    WHERE (n.id IS NULL OR o.id IS NULL OR {Changed(c,LineColumns,"n","o")})
                      AND EXISTS(SELECT 1 FROM {invoice} i WHERE i.id IN (n.invoice_id,o.invoice_id) AND {Frozen("i")}))
                    THROW 51022,'{lines}',1;
                END;
                """);
        }
        else
        {
            string Parent(string id)=>$"EXISTS(SELECT 1 FROM {invoice} i WHERE i.id={id} AND {Frozen("i")})";
            SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_platform_invoice_frozen BEFORE UPDATE ON {invoice} WHEN {Frozen("OLD")} AND (NEW.id<>OLD.id OR {Changed(c,HeaderColumns,"NEW","OLD")}) BEGIN SELECT RAISE(ABORT,'{header}'); END;");
            SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_platform_invoice_line_frozen_insert BEFORE INSERT ON {line} WHEN {Parent("NEW.invoice_id")} BEGIN SELECT RAISE(ABORT,'{lines}'); END;");
            SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_platform_invoice_line_frozen_delete BEFORE DELETE ON {line} WHEN {Parent("OLD.invoice_id")} BEGIN SELECT RAISE(ABORT,'{lines}'); END;");
            SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_platform_invoice_line_frozen_update BEFORE UPDATE ON {line} WHEN ({Parent("OLD.invoice_id")} OR {Parent("NEW.invoice_id")}) AND ({Changed(c,LineColumns,"NEW","OLD")}) BEGIN SELECT RAISE(ABORT,'{lines}'); END;");
        }
    }

    /// <summary>L2: active (not cancelled) invoices of one app and registration may not cover overlapping days.
    /// AFTER triggers, so an exact duplicate still reports UQ_sal_invoice_active.</summary>
    static void EnsureNoOverlap(DbConnection c,DbTransaction? tx)
    {
        string invoice=SchemaSql.Q(c,"sal","platform_invoice");
        const string message="Active invoice periods of one registration cannot overlap";
        string Overlap(string n)=>$"EXISTS(SELECT 1 FROM {invoice} x WHERE x.id<>{n}.id AND x.status<>4 AND x.app_code={n}.app_code AND x.erp_cr_id={n}.erp_cr_id AND x.period_from<={n}.period_to AND {n}.period_from<=x.period_to)";
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER sal.TR_platform_invoice_overlap ON {invoice} AFTER INSERT,UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted n WHERE n.status<>4 AND {Overlap("n")})
                    THROW 51023,'{message}',1;
                END;
                """);
        else
            foreach(bool insert in new[]{true,false})
                SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_platform_invoice_overlap_{(insert?"insert":"update")} AFTER {(insert?"INSERT":"UPDATE OF app_code,erp_cr_id,period_from,period_to,status")} ON {invoice} WHEN NEW.status<>4 AND {Overlap("NEW")} BEGIN SELECT RAISE(ABORT,'{message}'); END;");
    }
}
