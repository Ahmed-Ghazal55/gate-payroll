using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W1-11a: hr.company* masters (design §13 step 2 / people draft §4). Legacy kinds keep write ownership.</summary>
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
        BackfillFromLegacy(c,tx);
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
                  CONSTRAINT UQ_hr_company_erp_sponsor UNIQUE (erp_sponsor_id),
                  CONSTRAINT CK_hr_company_status CHECK (status IN (1,2,3))
                );
                """);
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
                  CONSTRAINT UQ_hr_company_erp_sponsor UNIQUE (erp_sponsor_id),
                  CONSTRAINT CK_hr_company_status CHECK (status IN (1,2,3))
                );
                """);
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

    static int StatusOf(string status)=>status switch{"archived"=>3,"inactive"=>2,_=>1};

    static bool TryGuid(string? value,out Guid id)
    {
        id=default;
        return !string.IsNullOrWhiteSpace(value)&&Guid.TryParse(value,out id);
    }

    static string? DateOrNull(string value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        if(DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d))return d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        if(DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var dto))return DateOnly.FromDateTime(dto.UtcDateTime).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        return null;
    }

    static void BackfillFromLegacy(DbConnection c,DbTransaction? tx)
    {
        // Dual-read copy only; Store.Save path for kind=company remains the writer.
        if(!SchemaSql.IsSql(c))
        {
            // records table may be missing on bare Ensure in memory tests.
            object? has=SchemaSql.Scalar(c,tx,"SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='records'");
            if(Convert.ToInt64(has!)==0)return;
        }
        else
        {
            object? has=SchemaSql.Scalar(c,tx,"SELECT CASE WHEN OBJECT_ID(N'dbo.records',N'U') IS NULL THEN 0 ELSE 1 END");
            if(Convert.ToInt32(has!)==0)return;
        }

        string company=SchemaSql.Q(c,"hr","company");
        string now=SchemaSql.NowLiteral(c);
        foreach(var doc in Store.List(c,"company",tx))
        {
            if(!TryGuid(doc.S("id"),out var id))continue;
            object? exists=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {company} WHERE id=@id",("@id",id));
            if(Convert.ToInt64(exists!)>0)continue;
            Guid? erp=TryGuid(doc.S("sponsorId",doc.S("id")),out var sponsor)?sponsor:null;
            string code=doc.S("code");if(code=="")code=id.ToString("N")[..8];
            string unified=doc.S("unifiedNumber");if(unified=="")unified="U-"+id.ToString("N")[..12];
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {company}(id,erp_sponsor_id,code,unified_number,name_ar,name_en,short_name_ar,entity_type,city,address,annual_confirmation_date,status,created_at,updated_at)
                VALUES(@id,@erp,@code,@unified,@nameAr,@nameEn,@short,@entity,@city,@address,@ac,@status,{now},{now})
                """,
                ("@id",id),("@erp",(object?)erp??DBNull.Value),("@code",code),("@unified",unified),
                ("@nameAr",doc.S("nameAr",doc.S("name","company"))),("@nameEn",NullIfEmpty(doc.S("nameEn"))),
                ("@short",NullIfEmpty(doc.S("shortNameAr"))),("@entity",NullIfEmpty(doc.S("entityType"))),
                ("@city",NullIfEmpty(doc.S("city"))),("@address",NullIfEmpty(doc.S("address"))),
                ("@ac",(object?)DateOrNull(doc.S("annualConfirmationDate"))??DBNull.Value),("@status",StatusOf(doc.S("status","active"))));
            BackfillActivities(c,tx,id,doc);
        }

        string cr=SchemaSql.Q(c,"hr","commercial_registration");
        foreach(var doc in Store.List(c,"commercial_registration",tx))
        {
            if(!TryGuid(doc.S("id").Replace("cr:",""),out var id))id=SchemaSql.NewId();
            if(!TryGuid(doc.S("companyId"),out var companyId))continue;
            object? parent=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {company} WHERE id=@id",("@id",companyId));
            if(Convert.ToInt64(parent!)==0)continue;
            string number=doc.S("number",doc.S("crNumber"));if(number=="")continue;
            object? exists=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {cr} WHERE cr_number=@n",("@n",number));
            if(Convert.ToInt64(exists!)>0)continue;
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {cr}(id,company_id,cr_number,cr_type,characteristic,issue_date,expiry_date,status,created_at)
                VALUES(@id,@cid,@n,@type,@ch,@issue,@exp,@status,{now})
                """,
                ("@id",id),("@cid",companyId),("@n",number),("@type",NullIfEmpty(doc.S("crType",doc.S("type")))),
                ("@ch",NullIfEmpty(doc.S("characteristic"))),("@issue",(object?)DateOrNull(doc.S("issueDate"))??DBNull.Value),
                ("@exp",(object?)DateOrNull(doc.S("expiryDate"))??DBNull.Value),("@status",StatusOf(doc.S("status","active"))));
        }

        string docs=SchemaSql.Q(c,"hr","company_document");
        foreach(var doc in Store.List(c,"company_document",tx))
        {
            if(!TryGuid(doc.S("companyId"),out var companyId))continue;
            object? parent=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {company} WHERE id=@id",("@id",companyId));
            if(Convert.ToInt64(parent!)==0)continue;
            Guid id=TryGuid(doc.S("id").Replace("company-doc:",""),out var parsed)?parsed:SchemaSql.NewId();
            object? exists=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {docs} WHERE id=@id",("@id",id));
            if(Convert.ToInt64(exists!)>0)continue;
            string path=doc.S("fileRelativePath",doc.S("file",""));if(path=="")continue;
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {docs}(id,company_id,doc_type,file_path,uploaded_by,uploaded_at,created_at)
                VALUES(@id,@cid,@type,@path,@by,@at,{now})
                """,
                ("@id",id),("@cid",companyId),("@type",doc.S("type","document")),("@path",path),
                ("@by",NullIfEmpty(doc.S("uploadedBy",doc.S("createdBy")))),
                ("@at",doc.S("createdAt")==""?DateTime.UtcNow.ToString("O"):doc.S("createdAt")));
        }
    }

    static void BackfillActivities(DbConnection c,DbTransaction? tx,Guid companyId,JsonObject doc)
    {
        if(doc["activities"] is not JsonArray activities)return;
        string t=SchemaSql.Q(c,"hr","company_activity");string now=SchemaSql.NowLiteral(c);
        foreach(var node in activities)
        {
            string isic=node is JsonObject o?o.S("isicCode",o.S("code")):node?.GetValue<string>()??"";
            if(isic=="")continue;
            string name=node is JsonObject named?named.S("nameAr",named.S("name",isic)):isic;
            object? exists=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {t} WHERE company_id=@cid AND isic_code=@isic",("@cid",companyId),("@isic",isic));
            if(Convert.ToInt64(exists!)>0)continue;
            SchemaSql.Exec(c,tx,$"INSERT INTO {t}(id,company_id,isic_code,name_ar,created_at) VALUES(@id,@cid,@isic,@name,{now})",
                ("@id",SchemaSql.NewId()),("@cid",companyId),("@isic",isic),("@name",name));
        }
    }

    static object NullIfEmpty(string value)=>string.IsNullOrWhiteSpace(value)?DBNull.Value:value;
}
