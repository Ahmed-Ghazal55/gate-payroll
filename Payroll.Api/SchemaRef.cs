using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-ref: empty logical schemas + ref.* catalogues + business_key_guard (design §13 step 1, §16).</summary>
public static class SchemaRef
{
    public static readonly string[] LogicalSchemas =
    [
        "hr","ops","sal","pay","fin","debt","treasury","form","app","auth","org","audit","notify","task","chat","ref"
    ];

    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchemas(c,tx,LogicalSchemas);
        EnsureBusinessKeyGuard(c,tx);
        EnsureSaudiBank(c,tx);
        EnsureErpEventType(c,tx);
        EnsureSetting(c,tx);
        EnsureLegacyKeyMap(c,tx);
        SeedSaudiBanks(c,tx);
        SeedErpEventTypes(c,tx);
    }

    /// <summary>Call after hr.company exists: restrictive company FK + index (R3 / planner decision 4).</summary>
    public static void EnsureLegacyAccountMapAfterCompany(DbConnection c,DbTransaction? tx=null)=>EnsureLegacyAccountMap(c,tx);

    static void EnsureBusinessKeyGuard(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"ref","business_key_guard");
        string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'ref.business_key_guard',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  object_type varchar(60) NOT NULL,
                  business_key varchar(200) NOT NULL,
                  row_id {g} NOT NULL,
                  CONSTRAINT PK_business_key_guard PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_business_key_guard UNIQUE (object_type, business_key)
                );
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  object_type TEXT NOT NULL,
                  business_key TEXT NOT NULL,
                  row_id {g} NOT NULL,
                  CONSTRAINT UQ_business_key_guard UNIQUE (object_type, business_key)
                );
                """);
        }
    }

    static void EnsureSaudiBank(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"ref","saudi_bank");
        string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'ref.saudi_bank',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  code varchar(4) NOT NULL,
                  short_code varchar(8) NOT NULL,
                  name_ar nvarchar(100) NOT NULL,
                  active {SchemaSql.Bit(c)} NOT NULL CONSTRAINT DF_saudi_bank_active DEFAULT(1),
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_saudi_bank PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_saudi_bank_code UNIQUE (code),
                  CONSTRAINT CK_saudi_bank_code CHECK (LEN(code) BETWEEN 2 AND 4)
                );
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  code TEXT NOT NULL,
                  short_code TEXT NOT NULL,
                  name_ar TEXT NOT NULL,
                  active INTEGER NOT NULL DEFAULT 1,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_saudi_bank_code UNIQUE (code),
                  CONSTRAINT CK_saudi_bank_code CHECK (length(code) BETWEEN 2 AND 4)
                );
                """);
        }
    }

    static void EnsureErpEventType(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"ref","erp_event_type");
        string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'ref.erp_event_type',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  code varchar(40) NOT NULL,
                  name_ar nvarchar(200) NOT NULL,
                  erp_source nvarchar(200) NULL,
                  active {SchemaSql.Bit(c)} NOT NULL CONSTRAINT DF_erp_event_type_active DEFAULT(1),
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_erp_event_type PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_erp_event_type_code UNIQUE (code)
                );
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  code TEXT NOT NULL,
                  name_ar TEXT NOT NULL,
                  erp_source TEXT NULL,
                  active INTEGER NOT NULL DEFAULT 1,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_erp_event_type_code UNIQUE (code)
                );
                """);
        }
    }

    static void EnsureSetting(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"ref","setting");
        string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'ref.setting',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  [key] varchar(80) NOT NULL,
                  value_json nvarchar(max) NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_ref_setting PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_ref_setting_key UNIQUE ([key])
                );
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  key TEXT NOT NULL,
                  value_json TEXT NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_ref_setting_key UNIQUE (key)
                );
                """);
        }
    }

    static void EnsureLegacyAccountMap(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"ref","legacy_account_map");
        string g=SchemaSql.GuidType(c);
        string company=SchemaSql.Q(c,"hr","company");
        if(!SchemaSql.TableExists(c,tx,"hr","company"))
            throw new InvalidOperationException("ref.legacy_account_map requires hr.company first.");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'ref.legacy_account_map',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  legacy_code varchar(32) NOT NULL,
                  target_code varchar(32) NOT NULL,
                  company_id {g} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_legacy_account_map PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT FK_legacy_account_map_company FOREIGN KEY (company_id) REFERENCES {company}(id)
                );
                """);
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'ref.legacy_account_map',N'U') IS NOT NULL
                  AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_legacy_account_map_company' AND parent_object_id=OBJECT_ID(N'ref.legacy_account_map'))
                ALTER TABLE {t} ADD CONSTRAINT FK_legacy_account_map_company FOREIGN KEY (company_id) REFERENCES {company}(id);
                """);
            SchemaSql.DropUniqueConstraintIfExists(c,tx,"ref","legacy_account_map","UQ_legacy_account_map");
            SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"ref","legacy_account_map","UQ_legacy_account_map","legacy_code, company_id","company_id IS NOT NULL");
            SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"ref","legacy_account_map","UQ_legacy_account_map_global","legacy_code","company_id IS NULL");
            SchemaSql.CreateIndexIfMissing(c,tx,"ref","legacy_account_map","IX_legacy_account_map_company_id","company_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  legacy_code TEXT NOT NULL,
                  target_code TEXT NOT NULL,
                  company_id TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT FK_legacy_account_map_company FOREIGN KEY (company_id) REFERENCES {company}(id)
                );
                """);
            SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"ref","legacy_account_map","UQ_legacy_account_map","legacy_code, company_id","company_id IS NOT NULL");
            SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"ref","legacy_account_map","UQ_legacy_account_map_global","legacy_code","company_id IS NULL");
            SchemaSql.CreateIndexIfMissing(c,tx,"ref","legacy_account_map","IX_legacy_account_map_company_id","company_id");
        }
    }

    /// <summary>Empty map for later cutover: source kind + source id → GUID v7. Created empty; never filled at startup in this phase.</summary>
    static void EnsureLegacyKeyMap(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"ref","legacy_key_map");
        string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'ref.legacy_key_map',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  source_kind varchar(40) NOT NULL,
                  source_id nvarchar(128) NOT NULL,
                  target_id {g} NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_legacy_key_map PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_legacy_key_map_source UNIQUE (source_kind, source_id)
                );
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  source_kind TEXT NOT NULL,
                  source_id TEXT NOT NULL,
                  target_id {g} NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_legacy_key_map_source UNIQUE (source_kind, source_id)
                );
                """);
        }
    }

    static readonly (string Code,string Short,string NameAr)[] SaudiBanks=
    [
        ("80","RJHI","مصرف الراجحي"),("10","NCBK","البنك الأهلي السعودي"),("05","INMA","مصرف الإنماء"),
        ("20","RIBL","بنك الرياض"),("15","ALBI","بنك البلاد"),("45","SABB","البنك السعودي الأول"),
        ("30","ARNB","البنك العربي الوطني"),("55","BSFR","البنك السعودي الفرنسي"),("60","BJAZ","بنك الجزيرة"),
        ("65","SIBC","البنك السعودي للاستثمار"),("90","GULF","بنك الخليج الدولي"),("78","STCJ","بنك إس تي سي")
    ];

    static void SeedSaudiBanks(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"ref","saudi_bank");
        string now=SchemaSql.NowLiteral(c);
        foreach(var b in SaudiBanks)
        {
            object? exists=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {t} WHERE code=@code",("@code",b.Code));
            if(Convert.ToInt64(exists!)>0)continue;
            SchemaSql.Exec(c,tx,$"INSERT INTO {t}(id,code,short_code,name_ar,active,created_at) VALUES(@id,@code,@short,@name,1,{now})",
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@code",b.Code),("@short",b.Short),("@name",b.NameAr));
        }
    }

    static readonly (string Code,string NameAr)[] ErpEvents=
    [
        ("person.status","تغيير حالة شخص"),("person.sponsorship","تغيير كفالة"),
        ("platform_account.start","بدء حساب منصة"),("vehicle.handover","تسليم مركبة")
    ];

    static void SeedErpEventTypes(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"ref","erp_event_type");
        string now=SchemaSql.NowLiteral(c);
        foreach(var e in ErpEvents)
        {
            object? exists=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {t} WHERE code=@code",("@code",e.Code));
            if(Convert.ToInt64(exists!)>0)continue;
            SchemaSql.Exec(c,tx,$"INSERT INTO {t}(id,code,name_ar,active,created_at) VALUES(@id,@code,@name,1,{now})",
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@code",e.Code),("@name",e.NameAr));
        }
    }
}
