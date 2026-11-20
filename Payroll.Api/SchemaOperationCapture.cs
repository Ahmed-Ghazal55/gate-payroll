using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-14b / R1-08: append-only used-field snapshots, typed kind/month links,
/// exhaustive per-app capture manifest, and a database freeze gate. No ERP reads or legacy input replacement.</summary>
public static class SchemaOperationCapture
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),snap=SchemaSql.Q(c,"sal","ops_snapshot"),link=SchemaSql.Q(c,"fin","operation_snapshot_link");
        SchemaSql.EnsureTable(c,tx,"fin","operation_required_kind",$"""
            id {g} NOT NULL, operation_type {SchemaSql.Varchar(c,40)} NOT NULL,
            app_code {SchemaSql.Varchar(c,20)} NOT NULL, required_kind {SchemaSql.Varchar(c,40)} NOT NULL,
            CONSTRAINT PK_operation_required_kind {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_operation_required_kind UNIQUE (operation_type,app_code,required_kind)
            """);
        SeedRequiredKinds(c,tx);
        SchemaSql.EnsureTable(c,tx,"sal","ops_snapshot",$"""
            id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            kind {SchemaSql.Varchar(c,40)} NOT NULL, erp_row_id {g} NOT NULL,
            scope_company_id {g} NOT NULL, scope_app {SchemaSql.Varchar(c,20)} NOT NULL,
            captured_at {SchemaSql.DateTime(c)} NOT NULL, captured_by {g} NOT NULL,
            source_fingerprint {SchemaSql.Binary(c,32)} NOT NULL, payload {SchemaSql.Json(c)} NOT NULL,
            generation {SchemaSql.Varchar(c,10)} NOT NULL DEFAULT 'new',
            CONSTRAINT PK_sal_ops_snapshot {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_ops_snapshot_kind_month UNIQUE (id,kind,period_month),
            CONSTRAINT UQ_ops_snapshot_content_ref UNIQUE (id,kind,erp_row_id,source_fingerprint,period_month),
            CONSTRAINT CK_ops_snapshot_month CHECK ({SchemaSql.MonthCheck(c)}),
            CONSTRAINT CK_ops_snapshot_generation CHECK (generation IN ('new','legacy')),
            CONSTRAINT CK_ops_snapshot_hash CHECK ({(SchemaSql.IsSql(c)?"DATALENGTH":"length")}(source_fingerprint)=32),
            CONSTRAINT CK_ops_snapshot_json CHECK ({(SchemaSql.IsSql(c)?"ISJSON":"json_valid")}(payload)=1),
            CONSTRAINT FK_ops_snapshot_company FOREIGN KEY (scope_company_id) REFERENCES {SchemaSql.Q(c,"hr","company")}(id) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureMonthIndex(c,tx,"sal","ops_snapshot");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","ops_snapshot","IX_ops_snapshot_company","scope_company_id");
        SchemaSql.EnsureTable(c,tx,"fin","operation_snapshot_link",$"""
            id {g} NOT NULL, operation_type {SchemaSql.Varchar(c,40)} NOT NULL, operation_id {g} NOT NULL,
            snapshot_id {g} NOT NULL, snapshot_kind {SchemaSql.Varchar(c,40)} NOT NULL,
            erp_row_id {g} NOT NULL, source_fingerprint {SchemaSql.Binary(c,32)} NOT NULL,
            period_month {SchemaSql.Date(c)} NOT NULL,
            CONSTRAINT PK_operation_snapshot_link {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_op_snap_link UNIQUE (operation_type,operation_id,snapshot_id),
            CONSTRAINT UQ_op_snap_content UNIQUE (operation_type,operation_id,snapshot_kind,erp_row_id,source_fingerprint),
            CONSTRAINT UQ_op_snap_link_scope UNIQUE (id,operation_type,operation_id,snapshot_kind,period_month),
            CONSTRAINT CK_op_snap_type CHECK (operation_type IN ('pay_run','platform_invoice','chefz_settlement','jahez_recon','payment')),
            CONSTRAINT FK_op_snap_snapshot FOREIGN KEY (snapshot_id,snapshot_kind,erp_row_id,source_fingerprint,period_month) REFERENCES {snap}(id,kind,erp_row_id,source_fingerprint,period_month) ON DELETE NO ACTION
            """);
        SchemaSql.CreateIndexIfMissing(c,tx,"fin","operation_snapshot_link","IX_op_snap_operation","operation_type,operation_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"fin","operation_snapshot_link","IX_op_snap_snapshot","snapshot_id,snapshot_kind,period_month");
        SchemaSql.EnsureTable(c,tx,"fin","operation_capture_manifest",$"""
            id {g} NOT NULL, operation_type {SchemaSql.Varchar(c,40)} NOT NULL, operation_id {g} NOT NULL,
            required_kind {SchemaSql.Varchar(c,40)} NOT NULL, status {SchemaSql.Tiny(c)} NOT NULL,
            empty_reason {SchemaSql.Text(c,200)} NULL, snapshot_link_id {g} NULL,
            period_month {SchemaSql.Date(c)} NOT NULL,
            CONSTRAINT PK_operation_capture_manifest {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_capture_manifest UNIQUE (operation_type,operation_id,required_kind),
            CONSTRAINT CK_manifest_status CHECK (status IN (1,2,3)),
            CONSTRAINT CK_manifest_type CHECK (operation_type IN ('pay_run','platform_invoice','chefz_settlement','jahez_recon','payment')),
            CONSTRAINT CK_manifest_month CHECK ({SchemaSql.MonthCheck(c)}),
            CONSTRAINT CK_manifest_empty CHECK (status<>2 OR (empty_reason IS NOT NULL AND {(SchemaSql.IsSql(c)?"LEN(LTRIM(RTRIM(empty_reason)))":"length(trim(empty_reason))")}>0)),
            CONSTRAINT CK_manifest_link CHECK ((status=1 AND snapshot_link_id IS NOT NULL) OR (status IN (2,3) AND snapshot_link_id IS NULL)),
            CONSTRAINT FK_manifest_link FOREIGN KEY (snapshot_link_id,operation_type,operation_id,required_kind,period_month) REFERENCES {link}(id,operation_type,operation_id,snapshot_kind,period_month) ON DELETE NO ACTION
            """);
        SchemaSql.CreateIndexIfMissing(c,tx,"fin","operation_capture_manifest","IX_manifest_link","snapshot_link_id,operation_type,operation_id,required_kind,period_month");
        EnsureEvidenceGuards(c,tx);EnsureFreezeGate(c,tx);
    }

    static void SeedRequiredKinds(DbConnection c,DbTransaction? tx)
    {
        string common="platform_account,assignment,housing,fuel,vehicle,scheme,eligibility";
        (string Operation,string App,string Kinds)[] groups=
        [
            ("pay_run","ninja","ninja_daily,"+common),("pay_run","keeta","keeta_invoice_row,"+common),
            ("pay_run","hs","hs_ops,"+common+",hs_wallet"),("pay_run","amazon","platform_ops,"+common),
            ("chefz_settlement","","chefz_settlement,platform_account"),("platform_invoice","","invoice_file,platform_account"),
            ("jahez_recon","","jahez_day,jahez_handover"),("payment","","payment_source")
        ];
        string t=SchemaSql.Q(c,"fin","operation_required_kind");
        foreach(var group in groups)
        foreach(string kind in group.Kinds.Split(','))
            SchemaSql.Exec(c,tx,$"INSERT INTO {t}(id,operation_type,app_code,required_kind) SELECT @id,@op,@app,@kind WHERE NOT EXISTS(SELECT 1 FROM {t} WHERE operation_type=@op AND app_code=@app AND required_kind=@kind)",("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@op",group.Operation),("@app",group.App),("@kind",kind));
    }

    static string Frozen(DbConnection c,string type,string id)=>$"""
        (({type}='pay_run' AND EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"sal","pay_run")} r WHERE r.id={id} AND r.capture_state>0))
        OR ({type}='platform_invoice' AND EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"sal","platform_invoice")} r WHERE r.id={id} AND (r.approved_at IS NOT NULL OR r.status IN (2,3,4,5))))
        OR ({type}='payment' AND EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"pay","payment")} r WHERE r.id={id} AND r.status IN (2,3))))
        """;

    static string WrongOwner(DbConnection c,string alias)=>$"""
        (({alias}.operation_type='pay_run' AND NOT EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"sal","pay_run")} r WHERE r.id={alias}.operation_id AND r.period_month={alias}.period_month))
        OR ({alias}.operation_type='platform_invoice' AND NOT EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"sal","platform_invoice")} r WHERE r.id={alias}.operation_id AND r.period_month={alias}.period_month))
        OR ({alias}.operation_type='payment' AND NOT EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"pay","payment")} r WHERE r.id={alias}.operation_id AND r.period_month={alias}.period_month)))
        """;

    static void EnsureEvidenceGuards(DbConnection c,DbTransaction? tx)
    {
        string snap=SchemaSql.Q(c,"sal","ops_snapshot");
        // Append-only evidence: recapture appends new rows while open; cancel/new run once frozen (BR §39.3).
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"CREATE OR ALTER TRIGGER sal.TR_ops_snapshot_immutable ON {snap} INSTEAD OF UPDATE,DELETE AS BEGIN THROW 51010,'Snapshots are append-only evidence',1; END;");
        else
        {
            foreach(string op in new[]{"UPDATE","DELETE"})SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_ops_snapshot_{op} BEFORE {op} ON {snap} BEGIN SELECT RAISE(ABORT,'Snapshots are append-only evidence'); END;");
            // REPLACE deletes silently when recursive_triggers is off (SQLite default). Reject it before the delete.
            SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_ops_snapshot_no_replace BEFORE INSERT ON {snap} WHEN EXISTS(SELECT 1 FROM {snap} WHERE id=NEW.id) BEGIN SELECT RAISE(ABORT,'Snapshot replacement is forbidden'); END;");
        }
        foreach(string table in new[]{"operation_snapshot_link","operation_capture_manifest"})
        {
            string t=SchemaSql.Q(c,"fin",table);
            if(SchemaSql.IsSql(c))
                SchemaSql.Exec(c,tx,$"""
                    CREATE OR ALTER TRIGGER fin.TR_{table}_frozen ON {t} AFTER INSERT,UPDATE,DELETE AS
                    BEGIN
                      SET NOCOUNT ON;
                      IF EXISTS(SELECT 1 FROM (SELECT operation_type,operation_id FROM inserted UNION SELECT operation_type,operation_id FROM deleted) x WHERE {Frozen(c,"x.operation_type","x.operation_id")})
                        THROW 51011,'Frozen operation evidence cannot change',1;
                      IF EXISTS(SELECT 1 FROM inserted x WHERE {WrongOwner(c,"x")})
                        THROW 51012,'Capture owner or period mismatch',1;
                    END;
                    """);
            else
            {
                SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_{table}_no_replace BEFORE INSERT ON {t} WHEN EXISTS(SELECT 1 FROM {t} WHERE id=NEW.id) BEGIN SELECT RAISE(ABORT,'Evidence replacement is forbidden'); END;");
                foreach(string op in new[]{"INSERT","UPDATE","DELETE"})
                {
                    string predicate=op=="INSERT"?Frozen(c,"NEW.operation_type","NEW.operation_id"):op=="DELETE"?Frozen(c,"OLD.operation_type","OLD.operation_id"):$"({Frozen(c,"NEW.operation_type","NEW.operation_id")} OR {Frozen(c,"OLD.operation_type","OLD.operation_id")})";
                    string owner=op=="DELETE"?"":$"SELECT RAISE(ABORT,'Capture owner or period mismatch') WHERE {WrongOwner(c,"NEW")};";
                    SchemaSql.SqliteTrigger(c,tx,$"CREATE TRIGGER TR_{table}_{op} BEFORE {op} ON {t} BEGIN SELECT RAISE(ABORT,'Frozen operation evidence cannot change') WHERE {predicate}; {owner} END;");
                }
            }
        }
    }

    static string Incomplete(DbConnection c,string r)
    {
        string kinds=SchemaSql.Q(c,"fin","operation_required_kind"),manifest=SchemaSql.Q(c,"fin","operation_capture_manifest"),link=SchemaSql.Q(c,"fin","operation_snapshot_link"),snap=SchemaSql.Q(c,"sal","ops_snapshot");
        return $"""
            (NOT EXISTS(SELECT 1 FROM {kinds} k WHERE k.operation_type='pay_run' AND k.app_code={r}.app_code)
            OR EXISTS(SELECT 1 FROM {kinds} k WHERE k.operation_type='pay_run' AND k.app_code={r}.app_code
              AND NOT EXISTS(SELECT 1 FROM {manifest} m WHERE m.operation_type='pay_run' AND m.operation_id={r}.id
                AND m.period_month={r}.period_month AND m.required_kind=k.required_kind AND m.status IN (1,2)))
            OR EXISTS(SELECT 1 FROM {manifest} m WHERE m.operation_type='pay_run' AND m.operation_id={r}.id AND (m.status=3 OR m.period_month<>{r}.period_month))
            OR EXISTS(SELECT 1 FROM {manifest} m JOIN {link} l ON l.id=m.snapshot_link_id JOIN {snap} s ON s.id=l.snapshot_id
              WHERE m.operation_type='pay_run' AND m.operation_id={r}.id AND m.status=1
                AND (s.scope_company_id<>{r}.company_id OR s.scope_app<>{r}.app_code)))
            """;
    }

    static void EnsureFreezeGate(DbConnection c,DbTransaction? tx)
    {
        string run=SchemaSql.Q(c,"sal","pay_run");
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER sal.TR_run_capture_gate ON {run} AFTER INSERT,UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted n JOIN deleted o ON o.id=n.id WHERE o.capture_state>0
                    AND (n.capture_state<o.capture_state OR n.company_id<>o.company_id OR n.app_code<>o.app_code OR n.period_month<>o.period_month OR n.calc_started_at<>o.calc_started_at))
                    THROW 51013,'Frozen run cannot reopen or change capture scope',1;
                  IF EXISTS(SELECT 1 FROM inserted n WHERE n.capture_state=0 AND n.calc_started_at IS NOT NULL)
                    THROW 51014,'Calculation requires frozen capture',1;
                  -- Completeness is checked only when the run freezes (0 -> >0), so a kind added later does not block runs already frozen.
                  IF EXISTS(SELECT 1 FROM inserted n LEFT JOIN deleted o ON o.id=n.id WHERE n.capture_state>0 AND COALESCE(o.capture_state,0)=0 AND {Incomplete(c,"n")})
                    THROW 51015,'Capture incomplete or snapshot scope mismatch',1;
                END;
                """);
        else
        {
            foreach(string op in new[]{"INSERT","UPDATE"})
            {
                string immutable=op=="INSERT"?"":$"""
                    SELECT RAISE(ABORT,'Frozen run cannot reopen or change capture scope') WHERE OLD.capture_state>0
                      AND (NEW.capture_state<OLD.capture_state OR NEW.company_id<>OLD.company_id OR NEW.app_code<>OLD.app_code OR NEW.period_month<>OLD.period_month OR NEW.calc_started_at<>OLD.calc_started_at);
                    """;
                SchemaSql.SqliteTrigger(c,tx,$"""
                    CREATE TRIGGER TR_run_capture_{op} BEFORE {op} ON {run} BEGIN
                      {immutable}
                      SELECT RAISE(ABORT,'Calculation requires frozen capture') WHERE NEW.capture_state=0 AND NEW.calc_started_at IS NOT NULL;
                      SELECT RAISE(ABORT,'Capture incomplete or snapshot scope mismatch') WHERE NEW.capture_state>0{(op=="INSERT"?"":" AND OLD.capture_state=0")} AND {Incomplete(c,"NEW")};
                    END;
                    """);
            }
        }
    }
}
