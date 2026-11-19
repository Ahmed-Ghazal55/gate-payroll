using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-18a: auth.permission / role / role_permission / user* catalogues. No bypass/approvals (U-W1-18c).</summary>
public static class SchemaAuthCatalog
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSql.EnsureSchema(c,tx,"auth");
        EnsurePermission(c,tx);
        EnsureRole(c,tx);
        EnsureRolePermission(c,tx);
        EnsureUser(c,tx);
        EnsureUserPermission(c,tx);
        SeedCatalog(c,tx);
    }

    static void EnsurePermission(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"auth","permission");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'auth.permission',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  code varchar(80) NOT NULL,
                  section varchar(40) NOT NULL,
                  name_ar nvarchar(200) NOT NULL,
                  description_ar nvarchar(400) NULL,
                  deprecated {SchemaSql.Bit(c)} NOT NULL CONSTRAINT DF_auth_perm_deprecated DEFAULT(0),
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_auth_permission PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_auth_permission_code UNIQUE (code),
                  CONSTRAINT CK_auth_permission_code CHECK (code LIKE '%.%.%' OR code LIKE '%.%')
                );
                """);
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  code TEXT NOT NULL,
                  section TEXT NOT NULL,
                  name_ar TEXT NOT NULL,
                  description_ar TEXT NULL,
                  deprecated INTEGER NOT NULL DEFAULT 0,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_auth_permission_code UNIQUE (code),
                  CONSTRAINT CK_auth_permission_code CHECK (instr(code,'.')>0)
                );
                """);
        }
    }

    static void EnsureRole(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"auth","role");string g=SchemaSql.GuidType(c);
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'auth.role',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  code varchar(40) NOT NULL,
                  name_ar nvarchar(200) NOT NULL,
                  default_department varchar(40) NULL,
                  default_rank_level {SchemaSql.Int(c)} NOT NULL CONSTRAINT DF_auth_role_rank DEFAULT(1),
                  summary_ar nvarchar(400) NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_auth_role PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_auth_role_code UNIQUE (code),
                  CONSTRAINT CK_auth_role_rank CHECK (default_rank_level BETWEEN 1 AND 5)
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
                  default_department TEXT NULL,
                  default_rank_level INTEGER NOT NULL DEFAULT 1,
                  summary_ar TEXT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_auth_role_code UNIQUE (code),
                  CONSTRAINT CK_auth_role_rank CHECK (default_rank_level BETWEEN 1 AND 5)
                );
                """);
        }
    }

    static void EnsureRolePermission(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"auth","role_permission");string g=SchemaSql.GuidType(c);
        string role=SchemaSql.Q(c,"auth","role");string perm=SchemaSql.Q(c,"auth","permission");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'auth.role_permission',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  role_id {g} NOT NULL,
                  permission_id {g} NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_auth_role_permission PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_auth_role_permission UNIQUE (role_id, permission_id),
                  CONSTRAINT FK_auth_rp_role FOREIGN KEY (role_id) REFERENCES {role}(id),
                  CONSTRAINT FK_auth_rp_perm FOREIGN KEY (permission_id) REFERENCES {perm}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"auth","role_permission","IX_auth_rp_role_id","role_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"auth","role_permission","IX_auth_rp_permission_id","permission_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  role_id {g} NOT NULL,
                  permission_id {g} NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_auth_role_permission UNIQUE (role_id, permission_id),
                  CONSTRAINT FK_auth_rp_role FOREIGN KEY (role_id) REFERENCES {role}(id),
                  CONSTRAINT FK_auth_rp_perm FOREIGN KEY (permission_id) REFERENCES {perm}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"auth","role_permission","IX_auth_rp_role_id","role_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"auth","role_permission","IX_auth_rp_permission_id","permission_id");
        }
    }

    static void EnsureUser(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"auth","user");string g=SchemaSql.GuidType(c);string role=SchemaSql.Q(c,"auth","role");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'auth.user',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  username varchar(80) NOT NULL,
                  role_id {g} NOT NULL,
                  security_version {SchemaSql.Int(c)} NOT NULL CONSTRAINT DF_auth_user_secver DEFAULT(1),
                  status {SchemaSql.Tiny(c)} NOT NULL CONSTRAINT DF_auth_user_status DEFAULT(1),
                  department varchar(40) NULL,
                  rank_level {SchemaSql.Int(c)} NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_auth_user PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_auth_user_username UNIQUE (username),
                  CONSTRAINT CK_auth_user_status CHECK (status IN (1,2,3)),
                  CONSTRAINT FK_auth_user_role FOREIGN KEY (role_id) REFERENCES {role}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"auth","user","IX_auth_user_role_id","role_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  username TEXT NOT NULL,
                  role_id {g} NOT NULL,
                  security_version INTEGER NOT NULL DEFAULT 1,
                  status INTEGER NOT NULL DEFAULT 1,
                  department TEXT NULL,
                  rank_level INTEGER NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_auth_user_username UNIQUE (username),
                  CONSTRAINT CK_auth_user_status CHECK (status IN (1,2,3)),
                  CONSTRAINT FK_auth_user_role FOREIGN KEY (role_id) REFERENCES {role}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"auth","user","IX_auth_user_role_id","role_id");
        }
    }

    static void EnsureUserPermission(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"auth","user_permission");string g=SchemaSql.GuidType(c);
        string user=SchemaSql.Q(c,"auth","user");string perm=SchemaSql.Q(c,"auth","permission");
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                IF OBJECT_ID(N'auth.user_permission',N'U') IS NULL
                CREATE TABLE {t}(
                  id {g} NOT NULL,
                  user_id {g} NOT NULL,
                  permission_id {g} NOT NULL,
                  effect varchar(10) NOT NULL,
                  {SchemaSql.AuditCols(c)},
                  CONSTRAINT PK_auth_user_permission PRIMARY KEY NONCLUSTERED (id),
                  CONSTRAINT UQ_auth_user_permission UNIQUE (user_id, permission_id),
                  CONSTRAINT CK_auth_user_permission_effect CHECK (effect IN ('grant','deny')),
                  CONSTRAINT FK_auth_up_user FOREIGN KEY (user_id) REFERENCES {user}(id),
                  CONSTRAINT FK_auth_up_perm FOREIGN KEY (permission_id) REFERENCES {perm}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"auth","user_permission","IX_auth_up_user_id","user_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"auth","user_permission","IX_auth_up_permission_id","permission_id");
        }
        else
        {
            SchemaSql.Exec(c,tx,$"""
                CREATE TABLE IF NOT EXISTS {t}(
                  id {g} NOT NULL PRIMARY KEY,
                  user_id {g} NOT NULL,
                  permission_id {g} NOT NULL,
                  effect TEXT NOT NULL,
                  created_by TEXT NULL, created_at TEXT NOT NULL, updated_by TEXT NULL, updated_at TEXT NULL, row_version BLOB NULL,
                  CONSTRAINT UQ_auth_user_permission UNIQUE (user_id, permission_id),
                  CONSTRAINT CK_auth_user_permission_effect CHECK (effect IN ('grant','deny')),
                  CONSTRAINT FK_auth_up_user FOREIGN KEY (user_id) REFERENCES {user}(id),
                  CONSTRAINT FK_auth_up_perm FOREIGN KEY (permission_id) REFERENCES {perm}(id)
                );
                """);
            SchemaSql.CreateIndexIfMissing(c,tx,"auth","user_permission","IX_auth_up_user_id","user_id");
            SchemaSql.CreateIndexIfMissing(c,tx,"auth","user_permission","IX_auth_up_permission_id","permission_id");
        }
    }

    static void SeedCatalog(DbConnection c,DbTransaction? tx)
    {
        string permTable=SchemaSql.Q(c,"auth","permission");
        string roleTable=SchemaSql.Q(c,"auth","role");
        string rpTable=SchemaSql.Q(c,"auth","role_permission");
        string now=SchemaSql.NowLiteral(c);
        var permissionIds=new Dictionary<string,Guid>(StringComparer.OrdinalIgnoreCase);

        foreach(var p in UserAccess.PermissionsCatalog)
        {
            object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {permTable} WHERE code=@code",("@code",p.Code));
            if(existing is Guid g)permissionIds[p.Code]=g;
            else if(existing is string s&&Guid.TryParse(s,out var parsed))permissionIds[p.Code]=parsed;
            else
            {
                var id=SchemaSql.NewId();
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {permTable}(id,code,section,name_ar,description_ar,deprecated,created_at)
                    VALUES(@id,@code,@section,@name,@desc,@dep,{now})
                    """,
                    ("@id",id),("@code",p.Code),("@section",p.ModuleCode),("@name",p.NameAr),
                    ("@desc",p.DescriptionAr),("@dep",p.Deprecated?1:0));
                permissionIds[p.Code]=id;
            }
        }

        foreach(var role in UserAccess.RolesCatalog)
        {
            Guid roleId;
            object? existingRole=SchemaSql.Scalar(c,tx,$"SELECT id FROM {roleTable} WHERE code=@code",("@code",role.Code));
            if(existingRole is Guid rg)roleId=rg;
            else if(existingRole is string rs&&Guid.TryParse(rs,out var rp))roleId=rp;
            else
            {
                roleId=SchemaSql.NewId();
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {roleTable}(id,code,name_ar,default_department,default_rank_level,summary_ar,created_at)
                    VALUES(@id,@code,@name,@dept,@rank,@summary,{now})
                    """,
                    ("@id",roleId),("@code",role.Code),("@name",role.NameAr),("@dept",role.DefaultDepartment),
                    ("@rank",role.DefaultRankLevel),("@summary",role.SummaryAr));
            }

            foreach(string code in role.DefaultPermissions.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if(!permissionIds.TryGetValue(code,out var permissionId))continue;
                object? link=SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {rpTable} WHERE role_id=@r AND permission_id=@p",("@r",roleId),("@p",permissionId));
                if(Convert.ToInt64(link!)>0)continue;
                SchemaSql.Exec(c,tx,$"INSERT INTO {rpTable}(id,role_id,permission_id,created_at) VALUES(@id,@r,@p,{now})",
                    ("@id",SchemaSql.NewId()),("@r",roleId),("@p",permissionId));
            }
        }
        // Intentionally no auth.bypass_grant and no default admin self-approve row (BR §35 / §9.1).
    }
}
