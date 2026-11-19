using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-11b: org.cost_centre masters + 'none' sentinel (NOT period_balance). Design §13 step 3 / §7.</summary>
public static class SchemaOrgCostCentre
{
    public const string NoneCode="none";
    public static readonly Guid NoneId=Guid.Parse("019c18d5-62e1-7000-8000-00000000cc00");

    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchema(c,tx,"org");
        SchemaSql.EnsureSchema(c,tx,"hr");
        EnsureCostCentre(c,tx);
        SeedNone(c,tx);
    }

    static void EnsureCostCentre(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"org","cost_centre");string g=SchemaSql.GuidType(c);string company=SchemaSql.Q(c,"hr","company");
        // company_id optional; FK only when hr.company exists (U-W1-11a).
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'org.cost_centre',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  code varchar(40) NOT NULL,
                  name_ar nvarchar(200) NOT NULL,
                  kind varchar(20) NOT NULL,
                  company_id {g} NULL,
                  status {SchemaSql.Tiny(c)} NOT NULL CONSTRAINT DF_org_cc_status DEFAULT(1),
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_org_cost_centre PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_org_cost_centre_code UNIQUE (code),
                  CONSTRAINT CK_org_cost_centre_kind CHECK (kind IN ('none','city','housing','office','vehicle')),
                  CONSTRAINT CK_org_cost_centre_status CHECK (status IN (1,2,3)),
                  CONSTRAINT FK_org_cost_centre_company FOREIGN KEY (company_id) REFERENCES {company}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"org","cost_centre","IX_org_cost_centre_company_id","company_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  code TEXT NOT NULL,
                  name_ar TEXT NOT NULL,
                  kind TEXT NOT NULL,
                  company_id {g} NULL,
                  status INTEGER NOT NULL DEFAULT 1,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_org_cost_centre_code UNIQUE (code),
                  CONSTRAINT CK_org_cost_centre_kind CHECK (kind IN ('none','city','housing','office','vehicle')),
                  CONSTRAINT CK_org_cost_centre_status CHECK (status IN (1,2,3)),
                  CONSTRAINT FK_org_cost_centre_company FOREIGN KEY (company_id) REFERENCES {company}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"org","cost_centre","IX_org_cost_centre_company_id","company_id");
        }
    }

    static void SeedNone(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"org","cost_centre");
        object? exists=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {t} WHERE code=@code",("@code",NoneCode));
        if(Convert.ToInt64(exists!)>0)return;
        string now=SchemaSql.NowLiteral(c);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {t}(id,code,name_ar,kind,company_id,status,created_at)
            VALUES(@id,@code,@name,@kind,NULL,1,{now})
            """,
            ("@id",NoneId),("@code",NoneCode),("@name","بدون مركز تكلفة"),("@kind",NoneCode));
    }
}
