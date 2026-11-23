using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-14e / U-W2-12b (M-0684, M-0689, file 74): dated ownership, holder and assignment history
/// for platform accounts. No settlement calc (M-0688 deferred).</summary>
public static class SchemaOpsPlatformAccount
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchema(c,tx,"ops");
        string g=SchemaSql.GuidType(c),company=SchemaSql.Q(c,"hr","company");
        SchemaSql.EnsureTable(c,tx,"ops","platform_account_ownership",$"""
            id {g} NOT NULL, erp_platform_account_id {g} NOT NULL,
            company_id {g} NOT NULL, valid_from {SchemaSql.Date(c)} NOT NULL, valid_to {SchemaSql.Date(c)} NULL,
            source {SchemaSql.Varchar(c,40)} NOT NULL, taken_at {SchemaSql.DateTime(c)} NOT NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_ops_account_ownership {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT CK_ops_ownership_source CHECK (source IN ('erp_snapshot','manual','import')),
            CONSTRAINT CK_ops_ownership_range CHECK (valid_to IS NULL OR valid_from<valid_to),
            CONSTRAINT FK_ops_ownership_company FOREIGN KEY (company_id) REFERENCES {company}(id) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureTable(c,tx,"ops","platform_account_holder",$"""
            id {g} NOT NULL, erp_platform_account_id {g} NOT NULL, erp_rider_id {g} NOT NULL,
            valid_from {SchemaSql.Date(c)} NOT NULL, valid_to {SchemaSql.Date(c)} NULL,
            source {SchemaSql.Varchar(c,40)} NOT NULL, taken_at {SchemaSql.DateTime(c)} NOT NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_ops_account_holder {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT CK_ops_holder_source CHECK (source IN ('erp_snapshot','manual','import')),
            CONSTRAINT CK_ops_holder_range CHECK (valid_to IS NULL OR valid_from<valid_to)
            """);
        SchemaSql.EnsureTable(c,tx,"ops","platform_account_assignment",$"""
            id {g} NOT NULL, erp_platform_account_id {g} NOT NULL, erp_rider_id {g} NOT NULL,
            valid_from {SchemaSql.Date(c)} NOT NULL, valid_to {SchemaSql.Date(c)} NULL,
            erp_assignment_id {g} NULL, source {SchemaSql.Varchar(c,40)} NOT NULL,
            taken_at {SchemaSql.DateTime(c)} NOT NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_ops_account_assignment {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT CK_ops_assignment_source CHECK (source IN ('erp_snapshot','manual','import','ops_capture')),
            CONSTRAINT CK_ops_assignment_range CHECK (valid_to IS NULL OR valid_from<valid_to)
            """);
        SchemaSql.CreateIndexIfMissing(c,tx,"ops","platform_account_ownership","IX_ops_ownership_account","erp_platform_account_id,valid_from");
        SchemaSql.CreateIndexIfMissing(c,tx,"ops","platform_account_holder","IX_ops_holder_account","erp_platform_account_id,valid_from");
        SchemaSql.CreateIndexIfMissing(c,tx,"ops","platform_account_assignment","IX_ops_assignment_account","erp_platform_account_id,valid_from");
        EnsureNoOverlap(c,tx,"platform_account_ownership","TR_ops_ownership_no_overlap","Open ownership intervals cannot overlap");
        EnsureNoOverlap(c,tx,"platform_account_holder","TR_ops_holder_no_overlap","Open holder intervals cannot overlap");
    }

    static void EnsureNoOverlap(DbConnection c,DbTransaction? tx,string table,string trigger,string message)
    {
        string t=SchemaSql.Q(c,"ops",table);
        string overlap(string n,string o)=>$"""
            {n}.erp_platform_account_id={o}.erp_platform_account_id AND {n}.id<>{o}.id AND
            {n}.valid_from<COALESCE({o}.valid_to,'9999-12-31') AND {o}.valid_from<COALESCE({n}.valid_to,'9999-12-31')
            """;
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE OR ALTER TRIGGER ops.{trigger} ON {t} AFTER INSERT,UPDATE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted n JOIN {t} o ON {overlap("n","o")})
                    THROW 51050,N'{message}',1;
                END;
                """);
        }
        else
        {
            foreach(string op in new[]{"INSERT","UPDATE"})
                SchemaSql.SqliteTrigger(c,tx,$"""
                    CREATE TRIGGER {trigger}_{op.ToLowerInvariant()} BEFORE {op} ON {t}
                    BEGIN SELECT RAISE(ABORT,'{message}') WHERE EXISTS(
                      SELECT 1 FROM {t} o WHERE {overlap("NEW","o")}); END;
                    """);
        }
    }
}
