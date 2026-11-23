using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-11a: hr.company* masters (design §13 step 2 / people draft §4). Schema-only; legacy dbo.records keeps read/write ownership until cutover.</summary>
public static class SchemaHrCompany
{
    // status: 1 active, 2 inactive, 3 archived
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchema(c,tx,"hr");
        EnsureCompany(c,tx);
        EnsureCommercialRegistration(c,tx);
        EnsureCompanyActivity(c,tx);
        EnsureCompanyDocument(c,tx);
        EnsureCompanyKpiMonthly(c,tx);
    }

    static void EnsureCompany(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"hr","company");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'hr.company',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  erp_sponsor_id {g} NULL,
                  code varchar(20) NOT NULL,
                  unified_number varchar(20) NOT NULL,
                  name_ar nvarchar(200) NOT NULL,
                  name_en nvarchar(200) NULL,
                  short_name_ar nvarchar(100) NULL,
                  entity_type nvarchar(80) NULL,
                  city nvarchar(100) NULL,
                  capital_sar {SchemaSql.Decimal(c)} NULL,
                  address nvarchar(400) NULL,
                  annual_confirmation_date {SchemaSql.Date(c)} NULL,
                  annual_confirmation_hijri nvarchar(40) NULL,
                  status {SchemaSql.Tiny(c)} NOT NULL CONSTRAINT DF_hr_company_status DEFAULT(1),
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_hr_company PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_hr_company_code UNIQUE (code),
                  CONSTRAINT UQ_hr_company_unified UNIQUE (unified_number),
                  CONSTRAINT CK_hr_company_status CHECK (status IN (1,2,3))
                );
                """);
            SchemaSql.DropUniqueConstraintIfExists(c,tx,"hr","company","UQ_hr_company_erp_sponsor");
            SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"hr","company","UQ_hr_company_erp_sponsor","erp_sponsor_id","erp_sponsor_id IS NOT NULL");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  erp_sponsor_id {g} NULL,
                  code TEXT NOT NULL,
                  unified_number TEXT NOT NULL,
                  name_ar TEXT NOT NULL,
                  name_en TEXT NULL,
                  short_name_ar TEXT NULL,
                  entity_type TEXT NULL,
                  city TEXT NULL,
                  capital_sar REAL NULL,
                  address TEXT NULL,
                  annual_confirmation_date TEXT NULL,
                  annual_confirmation_hijri TEXT NULL,
                  status INTEGER NOT NULL DEFAULT 1,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_hr_company_code UNIQUE (code),
                  CONSTRAINT UQ_hr_company_unified UNIQUE (unified_number),
                  CONSTRAINT CK_hr_company_status CHECK (status IN (1,2,3))
                );
                """);
            SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"hr","company","UQ_hr_company_erp_sponsor","erp_sponsor_id","erp_sponsor_id IS NOT NULL");
        }
    }

    static void EnsureCommercialRegistration(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"hr","commercial_registration");string g=SchemaSql.GuidType(c);string company=SchemaSql.Q(c,"hr","company");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'hr.commercial_registration',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  company_id {g} NOT NULL,
                  cr_number varchar(32) NOT NULL,
                  cr_type nvarchar(40) NULL,
                  characteristic nvarchar(80) NULL,
                  cr_version nvarchar(40) NULL,
                  issue_date {SchemaSql.Date(c)} NULL,
                  expiry_date {SchemaSql.Date(c)} NULL,
                  status {SchemaSql.Tiny(c)} NOT NULL CONSTRAINT DF_hr_cr_status DEFAULT(1),
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_hr_commercial_registration PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_hr_cr_number UNIQUE (cr_number),
                  CONSTRAINT CK_hr_cr_status CHECK (status IN (1,2,3)),
                  CONSTRAINT FK_hr_cr_company FOREIGN KEY (company_id) REFERENCES {company}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"hr","commercial_registration","IX_hr_cr_company_id","company_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  company_id {g} NOT NULL,
                  cr_number TEXT NOT NULL,
                  cr_type TEXT NULL,
                  characteristic TEXT NULL,
                  cr_version TEXT NULL,
                  issue_date TEXT NULL,
                  expiry_date TEXT NULL,
                  status INTEGER NOT NULL DEFAULT 1,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_hr_cr_number UNIQUE (cr_number),
                  CONSTRAINT CK_hr_cr_status CHECK (status IN (1,2,3)),
                  CONSTRAINT FK_hr_cr_company FOREIGN KEY (company_id) REFERENCES {company}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"hr","commercial_registration","IX_hr_cr_company_id","company_id");
        }
    }

    static void EnsureCompanyActivity(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"hr","company_activity");string g=SchemaSql.GuidType(c);string company=SchemaSql.Q(c,"hr","company");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'hr.company_activity',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  company_id {g} NOT NULL,
                  isic_code varchar(16) NOT NULL,
                  name_ar nvarchar(200) NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_hr_company_activity PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_hr_company_activity UNIQUE (company_id, isic_code),
                  CONSTRAINT FK_hr_activity_company FOREIGN KEY (company_id) REFERENCES {company}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"hr","company_activity","IX_hr_activity_company_id","company_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  company_id {g} NOT NULL,
                  isic_code TEXT NOT NULL,
                  name_ar TEXT NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_hr_company_activity UNIQUE (company_id, isic_code),
                  CONSTRAINT FK_hr_activity_company FOREIGN KEY (company_id) REFERENCES {company}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"hr","company_activity","IX_hr_activity_company_id","company_id");
        }
    }

    static void EnsureCompanyDocument(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"hr","company_document");string g=SchemaSql.GuidType(c);string company=SchemaSql.Q(c,"hr","company");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'hr.company_document',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  company_id {g} NOT NULL,
                  doc_type nvarchar(80) NOT NULL,
                  file_path nvarchar(400) NOT NULL,
                  file_hash {SchemaSql.Binary(c,32)} NULL,
                  uploaded_by nvarchar(128) NULL,
                  uploaded_at {SchemaSql.DateTime(c)} NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_hr_company_document PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT FK_hr_document_company FOREIGN KEY (company_id) REFERENCES {company}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"hr","company_document","IX_hr_document_company_id","company_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  company_id {g} NOT NULL,
                  doc_type TEXT NOT NULL,
                  file_path TEXT NOT NULL,
                  file_hash BLOB NULL,
                  uploaded_by TEXT NULL,
                  uploaded_at TEXT NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT FK_hr_document_company FOREIGN KEY (company_id) REFERENCES {company}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"hr","company_document","IX_hr_document_company_id","company_id");
        }
    }

    static void EnsureCompanyKpiMonthly(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"hr","company_kpi_monthly");string g=SchemaSql.GuidType(c);string company=SchemaSql.Q(c,"hr","company");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'hr.company_kpi_monthly',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  company_id {g} NOT NULL,
                  period_month {SchemaSql.Date(c)} NOT NULL,
                  kpi_code varchar(40) NOT NULL,
                  value {SchemaSql.Decimal(c)} NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_hr_company_kpi_monthly PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_hr_company_kpi_monthly UNIQUE (company_id, period_month, kpi_code),
                  CONSTRAINT FK_hr_kpi_company FOREIGN KEY (company_id) REFERENCES {company}(id)
                );
                """);
            if(Convert.ToInt32(SchemaSql.Scalar(c,tx,"SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'hr.company_kpi_monthly') AND name=N'CX_hr_company_kpi_monthly') THEN 1 ELSE 0 END")!)==0)
                SchemaSql.Exec(c,tx,$"CREATE CLUSTERED INDEX CX_hr_company_kpi_monthly ON {t}(period_month, id);");
            SchemaSql.CreateIndexIfMissing(c,tx,"hr","company_kpi_monthly","IX_hr_kpi_company_id","company_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  company_id {g} NOT NULL,
                  period_month TEXT NOT NULL,
                  kpi_code TEXT NOT NULL,
                  value REAL NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_hr_company_kpi_monthly UNIQUE (company_id, period_month, kpi_code),
                  CONSTRAINT FK_hr_kpi_company FOREIGN KEY (company_id) REFERENCES {company}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"hr","company_kpi_monthly","IX_hr_kpi_company_id","company_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"hr","company_kpi_monthly","CX_hr_company_kpi_monthly","period_month, id");
        }
    }

}
