using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W1-08: fin.account GUID PK + code UQ + leaf posting flag + nature + target CoA seed. Design §7 / §7.1 / §0.1. Legacy dbo.records chart untouched.</summary>
public static class SchemaFinAccount
{
    // status: 1 active, 2 stopped, 3 display, 4 reserved
    // nature: 1 debit-normal, 2 credit-normal
    // account_type: 1 asset, 2 liability, 3 equity, 4 revenue, 5 expense, 6 contra
    // company_scope: 0 all, 1 by_setting, 2 all_except_own_books
    // is_postable: leaf-only posting (activation is separate via is_active/status)
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchema(c,tx,"fin");
        EnsureAccount(c,tx);
        SeedTargetChart(c,tx);
    }

    public static Guid StableId(string code)
    {
        Span<byte> hash=stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes("fin.account:"+code),hash);
        var b=hash[..16].ToArray();
        b[6]=(byte)((b[6]&0x0F)|0x70); // version 7 shape
        b[8]=(byte)((b[8]&0x3F)|0x80);
        return new Guid(b);
    }

    static void EnsureAccount(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"fin","account");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'fin.account',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  code varchar(16) NOT NULL,
                  parent_id {g} NULL,
                  name_ar nvarchar(200) NOT NULL,
                  account_type {SchemaSql.Tiny(c)} NOT NULL,
                  nature {SchemaSql.Tiny(c)} NOT NULL,
                  is_postable {SchemaSql.Bit(c)} NOT NULL,
                  status {SchemaSql.Tiny(c)} NOT NULL CONSTRAINT DF_fin_account_status DEFAULT(1),
                  is_active {SchemaSql.Bit(c)} NOT NULL CONSTRAINT DF_fin_account_active DEFAULT(1),
                  company_scope {SchemaSql.Tiny(c)} NOT NULL CONSTRAINT DF_fin_account_scope DEFAULT(0),
                  level_no {SchemaSql.Int(c)} NOT NULL CONSTRAINT DF_fin_account_level DEFAULT(1),
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_fin_account PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_fin_account_code UNIQUE (code),
                  CONSTRAINT UQ_fin_account_id_postable UNIQUE (id, is_postable),
                  CONSTRAINT CK_fin_account_nature CHECK (nature IN (1,2)),
                  CONSTRAINT CK_fin_account_type CHECK (account_type BETWEEN 1 AND 6),
                  CONSTRAINT CK_fin_account_status CHECK (status IN (1,2,3,4)),
                  CONSTRAINT CK_fin_account_scope CHECK (company_scope IN (0,1,2)),
                  CONSTRAINT CK_fin_account_postable CHECK (is_postable IN (0,1)),
                  CONSTRAINT FK_fin_account_parent FOREIGN KEY (parent_id) REFERENCES {t}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","account","IX_fin_account_parent_id","parent_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  code TEXT NOT NULL,
                  parent_id {g} NULL,
                  name_ar TEXT NOT NULL,
                  account_type INTEGER NOT NULL,
                  nature INTEGER NOT NULL,
                  is_postable INTEGER NOT NULL,
                  status INTEGER NOT NULL DEFAULT 1,
                  is_active INTEGER NOT NULL DEFAULT 1,
                  company_scope INTEGER NOT NULL DEFAULT 0,
                  level_no INTEGER NOT NULL DEFAULT 1,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_fin_account_code UNIQUE (code),
                  CONSTRAINT UQ_fin_account_id_postable UNIQUE (id, is_postable),
                  CONSTRAINT CK_fin_account_nature CHECK (nature IN (1,2)),
                  CONSTRAINT CK_fin_account_type CHECK (account_type BETWEEN 1 AND 6),
                  CONSTRAINT CK_fin_account_status CHECK (status IN (1,2,3,4)),
                  CONSTRAINT CK_fin_account_scope CHECK (company_scope IN (0,1,2)),
                  CONSTRAINT CK_fin_account_postable CHECK (is_postable IN (0,1)),
                  CONSTRAINT FK_fin_account_parent FOREIGN KEY (parent_id) REFERENCES {t}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"fin","account","IX_fin_account_parent_id","parent_id");
        }
    }

    static void SeedTargetChart(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"fin","account");
        string now=SchemaSql.NowLiteral(c);
        var accounts=FinanceViews.Accounts
            .Select(a=>new {
                Code=a.S("code"),
                NameAr=a.S("nameAr"),
                Parent=a.S("parent"),
                Posting=a.B("posting"),
                Nature=a.S("nature"),
                Status=a.S("status"),
                Company=a.S("company"),
                Level=a["level"]?.GetValue<int>()??1
            })
            .OrderBy(a=>a.Level).ThenBy(a=>a.Code,StringComparer.Ordinal)
            .ToList();
        var byCode=new Dictionary<string,Guid>(StringComparer.Ordinal);
        foreach(var a in accounts)
        {
            object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {t} WHERE code=@code",("@code",a.Code));
            Guid id;
            if(existing is Guid eg)id=eg;
            else if(existing is string es&&Guid.TryParse(es,out var ep))id=ep;
            else
            {
                id=StableId(a.Code);
                byte nature=a.Nature=="credit"?(byte)2:(byte)1;
                byte type=DeriveAccountType(a.Code,nature);
                byte status=MapStatus(a.Status);
                byte active=(byte)(status==1?1:0);
                byte scope=MapCompanyScope(a.Company);
                object? parentId=string.IsNullOrWhiteSpace(a.Parent)?DBNull.Value:(object)SchemaSql.GuidText(byCode.TryGetValue(a.Parent,out var pid)?pid:StableId(a.Parent));
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {t}(id,code,parent_id,name_ar,account_type,nature,is_postable,status,is_active,company_scope,level_no,created_at)
                    VALUES(@id,@code,@parent,@name,@type,@nature,@postable,@status,@active,@scope,@level,{now})
                    """,
                    ("@id",SchemaSql.GuidText(id)),("@code",a.Code),("@parent",parentId),("@name",a.NameAr),
                    ("@type",type),("@nature",nature),("@postable",a.Posting?1:0),
                    ("@status",status),("@active",active),("@scope",scope),("@level",a.Level));
            }
            byCode[a.Code]=id;
        }
    }

    public static byte DeriveAccountType(string code,byte nature)
    {
        if(string.IsNullOrEmpty(code))return 1;
        byte baseType=code[0] switch { '1'=>1, '2'=>2, '3'=>3, '4'=>4, '5'=>5, _=>1 };
        if(baseType==1&&nature==2)return 6; // contra-asset (credit-normal under assets) — design §7.1 + BR §20.5
        return baseType;
    }

    public static byte MapStatus(string status)=>status switch
    {
        "active"=>1,
        "stopped"=>2,
        "display"=>3,
        "reserved"=>4,
        _=>2
    };

    public static byte MapCompanyScope(string company)
    {
        if(company.Contains("إلا",StringComparison.Ordinal))return 2;
        if(company.Contains("إعداد",StringComparison.Ordinal)||company.Contains("حسب",StringComparison.Ordinal))return 1;
        return 0;
    }

    public static int ExpectedSeedCount()=>FinanceViews.Accounts.Count();
}
