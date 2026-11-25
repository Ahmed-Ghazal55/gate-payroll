using System.Data.Common;
namespace Payroll;

/// <summary>U-W2-11b (K1 / M-0636): high-water marks for Omar keeta.vw_keeta_*_v1 views.
/// Capture advances per view; never full re-scan of older rows.</summary>
public static class SchemaKeetaHwm
{
    public static readonly string[] Views=
    [
        "keeta.vw_keeta_daily_performance_v1",
        "keeta.vw_keeta_vda_daily_v1",
        "keeta.vw_keeta_verification_daily_v1",
        "keeta.vw_keeta_eligibility_v1",
        "keeta.vw_keeta_violations_v1",
        "keeta.vw_keeta_violation_actions_v1"
    ];

    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c);
        string rev=SchemaSql.IsSql(c)?"bigint":"INTEGER";
        SchemaSql.EnsureTable(c,tx,"sal","keeta_view_hwm",$"""
            id {g} NOT NULL, view_name {SchemaSql.Varchar(c,80)} NOT NULL,
            last_revision {rev} NOT NULL DEFAULT 0,
            last_snapshot_id {g} NULL,
            last_created_at {SchemaSql.DateTime(c)} NULL,
            advanced_at {SchemaSql.DateTime(c)} NOT NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_keeta_view_hwm {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_keeta_view_hwm UNIQUE (view_name),
            CONSTRAINT CK_sal_keeta_hwm_revision CHECK (last_revision>=0)
            """);
        string now=SchemaSql.NowLiteral(c);
        foreach(string view in Views)
        {
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"sal","keeta_view_hwm")}(id,view_name,last_revision,advanced_at,created_at)
                SELECT @id,@v,0,{now},{now} WHERE NOT EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"sal","keeta_view_hwm")} WHERE view_name=@v)
                """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@v",view));
        }
    }
}
