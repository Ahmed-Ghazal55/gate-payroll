using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.Cookies;
namespace Payroll;

/// <summary>U-W2-38: TOTP setup/verify/backup + 14-day finance window (G7, X24). Secret encrypted; never logged; never returned after setup.</summary>
public static class Auth2faApi
{
    public const int SetupWindowDays=14,BackupCount=10,TotpDigits=6,TotpStepSeconds=30;
    static readonly HashSet<string> FinanceRoles=new(StringComparer.OrdinalIgnoreCase)
    {
        "FinancialManager","Accountant","FinancialAccountant","SuperAdmin","SystemAdmin","Admin"
    };

    public static bool RequiresTwoFactor(ClaimsPrincipal actor,JsonObject? userDoc=null)
    {
        if(actor.Identity?.IsAuthenticated!=true)return false;
        string role=actor.FindFirstValue(ClaimTypes.Role)??userDoc?.S("role")??"";
        if(FinanceRoles.Contains(role))return true;
        if(AccessPolicy.Has(actor,"users.manage"))return true;
        return false;
    }

    public static bool RequiresTwoFactor(JsonObject user)
    {
        string role=UserAccess.Role(user);
        if(FinanceRoles.Contains(role))return true;
        // Prefer effective permissions (role template + grants); fall back to direct grants only.
        if(user[UserAccess.RoleBaseline] is JsonArray||user["customPermissions"] is JsonArray)
            return UserAccess.EffectivePermissions(user).Any(p=>string.Equals(p,"users.manage",StringComparison.OrdinalIgnoreCase));
        var custom=user["customPermissions"] as JsonArray??[];
        return custom.Any(n=>string.Equals(n?.ToString(),"users.manage",StringComparison.OrdinalIgnoreCase));
    }

    public static bool RequiresTwoFactor(DbConnection c,DbTransaction? tx,JsonObject user)
        =>RequiresTwoFactor(UserAccess.WithRole(c,tx,user));

    public const string EnrollmentClaim="enrollmentOnly";

    public static ClaimsPrincipal EnrollmentPrincipal(JsonObject u)
    {
        var claims=new List<Claim>
        {
            new(ClaimTypes.NameIdentifier,u.S("id")),
            new(ClaimTypes.Name,u.S("username")),
            new(ClaimTypes.Role,UserAccess.Role(u)),
            new("securityVersion",u.S("securityVersion")),
            new(EnrollmentClaim,"true")
        };
        return new(new ClaimsIdentity(claims,CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static bool IsEnrollmentOnly(ClaimsPrincipal actor)
        =>actor.HasClaim(EnrollmentClaim,"true");

    /// <summary>Password already verified. Modes: full | challenge | enrollment.</summary>
    public static JsonObject CompletePasswordLogin(DbConnection c,DbTransaction tx,string dataDir,JsonObject user,string? code)
    {
        var withRole=UserAccess.WithRole(c,tx,user);
        if(!RequiresTwoFactor(withRole))
            return LoginSession("full",enrollmentOnly:false,challenge:false);
        StartWindowIfNeeded(c,tx,withRole);
        string userId=SchemaApprovals.EnsureAuthUserId(c,tx,user.S("username"));
        var row=Load(c,tx,userId);
        if(row?.VerifiedAt!=null)
        {
            if(string.IsNullOrWhiteSpace(code))
                return new()
                {
                    ["mode"]="challenge",
                    ["requiresTwoFactor"]=true,
                    ["enrollmentOnly"]=false,
                    ["sessionClaims"]=new JsonArray(),
                    ["messageAr"]="أدخل رمز التحقق الثنائي أو رمزاً احتياطياً."
                };
            J.Need(ValidateLoginFactor(c,tx,dataDir,userId,row.Value,code.Trim()),"رمز التحقق غير صحيح.",401);
            return LoginSession("full",enrollmentOnly:false,challenge:false);
        }
        bool pastDeadline=row!=null&&DateTimeOffset.UtcNow>row.Value.Deadline.ToUniversalTime();
        if(pastDeadline)
            return LoginSession("enrollment",enrollmentOnly:true,challenge:false);
        return LoginSession("full",enrollmentOnly:false,challenge:false);
    }

    static JsonObject LoginSession(string mode,bool enrollmentOnly,bool challenge)=>new()
    {
        ["mode"]=mode,
        ["requiresTwoFactor"]=challenge||mode=="challenge",
        ["enrollmentOnly"]=enrollmentOnly,
        ["sessionClaims"]=mode=="full"?new JsonArray("full"):mode=="enrollment"?new JsonArray("enrollment"):new JsonArray()
    };

    static bool ValidateLoginFactor(DbConnection c,DbTransaction tx,string dataDir,string userId,
        (string? Id,byte[]? Encrypted,DateTimeOffset? VerifiedAt,DateTimeOffset Deadline) row,string code)
    {
        try
        {
            string secret=DecryptSecret(dataDir,row.Encrypted!);
            if(TotpMatches(secret,code,DateTimeOffset.UtcNow))return true;
        }
        catch{/* try backup */}
        return TryConsumeBackup(c,tx,userId,code);
    }

    public static byte[] ProtectKey(string dataDir)
    {
        string dir=Path.Combine(dataDir,"keys");Directory.CreateDirectory(dir);
        string path=Path.Combine(dir,"totp.key");
        if(File.Exists(path))
        {
            byte[] existing=File.ReadAllBytes(path);
            if(existing.Length>=32)return existing[..32];
        }
        byte[] key=RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(path,key);
        return key;
    }

    public static byte[] EncryptSecret(string dataDir,string plaintext)
    {
        byte[] key=ProtectKey(dataDir);
        byte[] nonce=RandomNumberGenerator.GetBytes(12);
        byte[] plain=Encoding.UTF8.GetBytes(plaintext);
        byte[] cipher=new byte[plain.Length];
        byte[] tag=new byte[16];
        using var aes=new AesGcm(key,16);
        aes.Encrypt(nonce,plain,cipher,tag);
        byte[] packed=new byte[12+16+cipher.Length];
        Buffer.BlockCopy(nonce,0,packed,0,12);
        Buffer.BlockCopy(tag,0,packed,12,16);
        Buffer.BlockCopy(cipher,0,packed,28,cipher.Length);
        return packed;
    }

    public static string DecryptSecret(string dataDir,byte[] packed)
    {
        J.Need(packed.Length>28,"سجل التحقق الثنائي تالف.",500);
        byte[] key=ProtectKey(dataDir);
        byte[] nonce=packed[..12],tag=packed[12..28],cipher=packed[28..];
        byte[] plain=new byte[cipher.Length];
        using var aes=new AesGcm(key,16);
        aes.Decrypt(nonce,cipher,tag,plain);
        return Encoding.UTF8.GetString(plain);
    }

    public static string NewSecret()
    {
        byte[] raw=RandomNumberGenerator.GetBytes(20);
        return Base32(raw);
    }

    public static string ComputeTotp(string base32Secret,DateTimeOffset at)
    {
        byte[] key=FromBase32(base32Secret);
        long counter=at.ToUnixTimeSeconds()/TotpStepSeconds;
        byte[] msg=BitConverter.GetBytes(counter);
        if(BitConverter.IsLittleEndian)Array.Reverse(msg);
        byte[] hash=HMACSHA1.HashData(key,msg);
        int offset=hash[^1]&0x0F;
        int binary=((hash[offset]&0x7F)<<24)|((hash[offset+1]&0xFF)<<16)|((hash[offset+2]&0xFF)<<8)|(hash[offset+3]&0xFF);
        return (binary%(int)Math.Pow(10,TotpDigits)).ToString(CultureInfo.InvariantCulture).PadLeft(TotpDigits,'0');
    }

    public static bool TotpMatches(string secret,string code,DateTimeOffset at)
    {
        if(string.IsNullOrWhiteSpace(code)||code.Length!=TotpDigits||!code.All(char.IsDigit))return false;
        for(int w=-1;w<=1;w++)
            if(CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(ComputeTotp(secret,at.AddSeconds(w*TotpStepSeconds))),
                Encoding.ASCII.GetBytes(code)))return true;
        return false;
    }

    static byte[] HashBackup(string code)
    {
        byte[] salt=Encoding.UTF8.GetBytes("Gate.Payroll.TotpBackup.v1");
        return Rfc2898DeriveBytes.Pbkdf2(code.Trim().ToUpperInvariant(),salt,120000,HashAlgorithmName.SHA256,32);
    }

    static string[] NewBackupCodes()
    {
        var list=new string[BackupCount];
        Span<byte> b=stackalloc byte[5];
        for(int i=0;i<BackupCount;i++)
        {
            RandomNumberGenerator.Fill(b);
            list[i]=Convert.ToHexString(b)[..10];
        }
        return list;
    }

    public static void EnsureDeadline(DbConnection c,DbTransaction tx,string username,DateTimeOffset deadline)
    {
        string userId=SchemaApprovals.EnsureAuthUserId(c,tx,username);
        object? exists=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"auth","totp_secret")} WHERE user_id=@u",("@u",userId));
        string dl=deadline.UtcDateTime.ToString("O",CultureInfo.InvariantCulture);
        if(exists==null)
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"auth","totp_secret")}(id,user_id,secret_encrypted,verified_at,setup_deadline,created_at)
                VALUES(@id,@u,@sec,NULL,@dl,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@u",userId),
                ("@sec",EncryptSecret(Path.GetTempPath(),NewSecret())),("@dl",dl));
        else
            SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"auth","totp_secret")} SET setup_deadline=@dl, verified_at=NULL, updated_at={SchemaSql.NowLiteral(c)} WHERE user_id=@u",
                ("@dl",dl),("@u",userId));
    }

    static (string? Id,byte[]? Encrypted,DateTimeOffset? VerifiedAt,DateTimeOffset Deadline)? Load(DbConnection c,DbTransaction? tx,string userId)
    {
        using var cmd=Store.Command(c,tx,$"""
            SELECT id,secret_encrypted,verified_at,setup_deadline FROM {SchemaSql.Q(c,"auth","totp_secret")} WHERE user_id=@u
            """,("@u",userId));
        using var r=cmd.ExecuteReader();
        if(!r.Read())return null;
        DateTimeOffset? verified=r.IsDBNull(2)?null:DateTimeOffset.Parse(Convert.ToDateTime(r.GetValue(2),CultureInfo.InvariantCulture).ToString("O"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal);
        DateTimeOffset deadline=DateTimeOffset.Parse(Convert.ToDateTime(r.GetValue(3),CultureInfo.InvariantCulture).ToString("O"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal);
        return (r.GetValue(0).ToString(),(byte[])r.GetValue(1),verified,deadline);
    }

    public static bool LoginBlocked(DbConnection c,string dataDir,JsonObject user)
    {
        // Past-deadline users get an enrollment-only session; hard block is no longer used at login.
        // Kept for tests that assert the deadline state (verified=false and past deadline).
        if(!RequiresTwoFactor(c,null,user)&&!RequiresTwoFactor(user))return false;
        try
        {
            object? id=SchemaSql.Scalar(c,null,$"SELECT id FROM {SchemaSql.Q(c,"auth","user")} WHERE username=@u OR username=@n",
                ("@u",user.S("username")),("@n",UserAccess.Normalize(user.S("username"))));
            if(id==null)return false; // window starts on successful login write
            string userId=id switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(id,CultureInfo.InvariantCulture)!};
            var row=Load(c,null,userId);
            if(row==null)return false;
            if(row.Value.VerifiedAt!=null)return false;
            return DateTimeOffset.UtcNow>row.Value.Deadline.ToUniversalTime();
        }
        catch{return false;}
    }

    public static void StartWindowIfNeeded(DbConnection c,DbTransaction tx,JsonObject user)
    {
        if(!RequiresTwoFactor(UserAccess.WithRole(c,tx,user))&&!RequiresTwoFactor(user))return;
        string userId=SchemaApprovals.EnsureAuthUserId(c,tx,user.S("username"));
        if(Load(c,tx,userId)!=null)return;
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"auth","totp_secret")}(id,user_id,secret_encrypted,verified_at,setup_deadline,created_at)
            VALUES(@id,@u,@sec,NULL,@dl,{SchemaSql.NowLiteral(c)})
            """,
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@u",userId),
            ("@sec",EncryptSecret(Path.GetTempPath(),NewSecret())), // placeholder until real Setup
            ("@dl",DateTimeOffset.UtcNow.AddDays(SetupWindowDays).UtcDateTime.ToString("O",CultureInfo.InvariantCulture)));
    }

    public static JsonObject Setup(DbConnection c,DbTransaction tx,string dataDir,ClaimsPrincipal actor,JsonObject input)
    {
        string target=input.S("username");
        bool adminSetup=target!=""&&!string.Equals(target,actor.Identity?.Name,StringComparison.OrdinalIgnoreCase);
        if(adminSetup)AccessPolicy.Demand(actor,"users.manage");
        else J.Need(actor.Identity?.IsAuthenticated==true,"سجل الدخول.",401);
        string username=adminSetup?target:actor.Identity!.Name!;
        var doc=UserAccess.Find(c,username,tx)??throw new ApiError("المستخدم غير موجود.",404);
        string userId=SchemaApprovals.EnsureAuthUserId(c,tx,username);
        var existing=Load(c,tx,userId);
        if(existing?.VerifiedAt!=null)
            return new(){["verified"]=true,["secret"]="",["allowedActions"]=new JsonArray("setup_2fa"),["messageAr"]="التحقق الثنائي مفعّل مسبقاً."};

        string secret=NewSecret();
        byte[] enc=EncryptSecret(dataDir,secret);
        string deadline=(existing?.Deadline??DateTimeOffset.UtcNow.AddDays(SetupWindowDays)).UtcDateTime.ToString("O",CultureInfo.InvariantCulture);
        if(existing==null)
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"auth","totp_secret")}(id,user_id,secret_encrypted,verified_at,setup_deadline,created_by,created_at)
                VALUES(@id,@u,@sec,NULL,@dl,@by,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@u",userId),("@sec",enc),("@dl",deadline),("@by",actor.Identity!.Name!));
        else
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"auth","totp_secret")}
                SET secret_encrypted=@sec, verified_at=NULL, updated_by=@by, updated_at={SchemaSql.NowLiteral(c)}
                WHERE user_id=@u
                """,("@sec",enc),("@by",actor.Identity!.Name!),("@u",userId));

        SchemaSql.Exec(c,tx,$"DELETE FROM {SchemaSql.Q(c,"auth","totp_backup_code")} WHERE user_id=@u",("@u",userId));
        var codes=NewBackupCodes();
        foreach(string code in codes)
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"auth","totp_backup_code")}(id,user_id,code_hash,used_at,created_by,created_at)
                VALUES(@id,@u,@h,NULL,@by,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@u",userId),("@h",HashBackup(code)),("@by",actor.Identity!.Name!));

        Store.Audit(c,tx,"auth.2fa_setup",userId,actor.Identity!.Name!);
        return new()
        {
            ["verified"]=false,
            ["secret"]=secret,
            ["otpauthUrl"]=$"otpauth://totp/Gate:{Uri.EscapeDataString(username)}?secret={secret}&issuer=Gate&digits={TotpDigits}&period={TotpStepSeconds}",
            ["backupCodes"]=new JsonArray(codes.Select(x=>(JsonNode?)x).ToArray()),
            ["setupDeadline"]=deadline,
            ["allowedActions"]=new JsonArray("setup_2fa"),
            ["messageAr"]="احفظ الرمز الاحتياطي الآن؛ لن يُعرض السر مرة أخرى بعد التفعيل."
        };
    }

    public static JsonObject Verify(DbConnection c,DbTransaction tx,string dataDir,ClaimsPrincipal actor,JsonObject input)
    {
        J.Need(actor.Identity?.IsAuthenticated==true,"سجل الدخول.",401);
        string username=actor.Identity!.Name!;
        string userId=SchemaApprovals.EnsureAuthUserId(c,tx,username);
        var row=Load(c,tx,userId)??throw new ApiError("ابدأ إعداد التحقق الثنائي أولاً.",400);
        string code=input.Required("code").Trim();
        bool ok=false;
        try
        {
            string secret=DecryptSecret(dataDir,row.Encrypted!);
            ok=TotpMatches(secret,code,DateTimeOffset.UtcNow);
        }
        catch{/* wrong key / corrupt — try backup */}
        if(!ok)ok=TryConsumeBackup(c,tx,userId,code);
        J.Need(ok,"رمز التحقق غير صحيح.",400);
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"auth","totp_secret")}
            SET verified_at={SchemaSql.NowLiteral(c)}, updated_by=@by, updated_at={SchemaSql.NowLiteral(c)}
            WHERE user_id=@u
            """,("@by",username),("@u",userId));
        Store.Audit(c,tx,"auth.2fa_verify",userId,username);
        return new(){["verified"]=true,["secret"]="",["allowedActions"]=new JsonArray("verify_2fa"),["messageAr"]="تم تفعيل التحقق الثنائي."};
    }

    static bool TryConsumeBackup(DbConnection c,DbTransaction tx,string userId,string code)
    {
        using var cmd=Store.Command(c,tx,$"""
            SELECT id,code_hash FROM {SchemaSql.Q(c,"auth","totp_backup_code")} WHERE user_id=@u AND used_at IS NULL
            """,("@u",userId));
        using var r=cmd.ExecuteReader();
        var candidates=new List<(string Id,byte[] Hash)>();
        while(r.Read())candidates.Add((r.GetValue(0).ToString()!,(byte[])r.GetValue(1)));
        r.Close();
        byte[] attempt=HashBackup(code);
        foreach(var (id,hash) in candidates)
        {
            if(!CryptographicOperations.FixedTimeEquals(hash,attempt))continue;
            SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"auth","totp_backup_code")} SET used_at={SchemaSql.NowLiteral(c)} WHERE id=@id AND used_at IS NULL",("@id",id));
            return true;
        }
        return false;
    }

    public static JsonObject RegenBackupCodes(DbConnection c,DbTransaction tx,string dataDir,ClaimsPrincipal actor,JsonObject input)
    {
        J.Need(actor.Identity?.IsAuthenticated==true,"سجل الدخول.",401);
        string username=actor.Identity!.Name!;
        string userId=SchemaApprovals.EnsureAuthUserId(c,tx,username);
        var row=Load(c,tx,userId)??throw new ApiError("التحقق الثنائي غير مهيأ.",400);
        J.Need(row.VerifiedAt!=null,"فعّل التحقق الثنائي أولاً.",400);
        string code=input.Required("code").Trim();
        string secret=DecryptSecret(dataDir,row.Encrypted!);
        J.Need(TotpMatches(secret,code,DateTimeOffset.UtcNow),"رمز التحقق غير صحيح.",400);
        SchemaSql.Exec(c,tx,$"DELETE FROM {SchemaSql.Q(c,"auth","totp_backup_code")} WHERE user_id=@u",("@u",userId));
        var codes=NewBackupCodes();
        foreach(string b in codes)
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"auth","totp_backup_code")}(id,user_id,code_hash,used_at,created_by,created_at)
                VALUES(@id,@u,@h,NULL,@by,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@u",userId),("@h",HashBackup(b)),("@by",username));
        Store.Audit(c,tx,"auth.2fa_backup_regen",userId,username);
        return new(){["backupCodes"]=new JsonArray(codes.Select(x=>(JsonNode?)x).ToArray()),["allowedActions"]=new JsonArray("regen_backup"),["messageAr"]="أُصدرت رموز احتياطية جديدة."};
    }

    public static JsonObject AdminReset(DbConnection c,DbTransaction tx,string dataDir,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,"users.manage");
        string username=input.Required("username");
        J.Need(!string.Equals(username,actor.Identity?.Name,StringComparison.OrdinalIgnoreCase)||AccessPolicy.Administrator(actor),
            "لا تصفّر تحققك بنفسك إلا كمسؤول نظام.",403);
        _=UserAccess.Find(c,username,tx)??throw new ApiError("المستخدم غير موجود.",404);
        string userId=SchemaApprovals.EnsureAuthUserId(c,tx,username);
        SchemaSql.Exec(c,tx,$"DELETE FROM {SchemaSql.Q(c,"auth","totp_backup_code")} WHERE user_id=@u",("@u",userId));
        SchemaSql.Exec(c,tx,$"DELETE FROM {SchemaSql.Q(c,"auth","totp_secret")} WHERE user_id=@u",("@u",userId));
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"auth","totp_secret")}(id,user_id,secret_encrypted,verified_at,setup_deadline,created_by,created_at)
            VALUES(@id,@u,@sec,NULL,@dl,@by,{SchemaSql.NowLiteral(c)})
            """,
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@u",userId),
            ("@sec",EncryptSecret(dataDir,NewSecret())),
            ("@dl",DateTimeOffset.UtcNow.AddDays(SetupWindowDays).UtcDateTime.ToString("O",CultureInfo.InvariantCulture)),
            ("@by",actor.Identity!.Name!));
        Store.Audit(c,tx,"auth.2fa_reset",userId,actor.Identity!.Name!);
        return new(){["reset"]=true,["setupDeadline"]=DateTimeOffset.UtcNow.AddDays(SetupWindowDays).ToString("O"),["allowedActions"]=new JsonArray("reset_2fa"),["messageAr"]="أُعيد ضبط التحقق الثنائي؛ مهلة إعداد جديدة ١٤ يوماً."};
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/auth/2fa/setup",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Setup(c,tx,store.DirectoryPath,ctx.User,input)))
            .WithTags("Administration").WithSummary("Start TOTP setup; secret returned once");
        api.MapPost("/auth/2fa/verify",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Verify(c,tx,store.DirectoryPath,ctx.User,input)))
            .WithTags("Administration").WithSummary("Verify TOTP or single-use backup code");
        api.MapPost("/auth/2fa/backup-codes",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>RegenBackupCodes(c,tx,store.DirectoryPath,ctx.User,input)))
            .WithTags("Administration").WithSummary("Regenerate hashed backup codes");
        api.MapPost("/auth/2fa/reset",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>AdminReset(c,tx,store.DirectoryPath,ctx.User,input)))
            .WithTags("Administration").WithSummary("Admin reset of TOTP with new 14-day window");
    }

    static string Base32(byte[] data)
    {
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb=new StringBuilder((data.Length*8+4)/5);
        int bitBuffer=0,bitsInBuffer=0;
        foreach(byte b in data)
        {
            bitBuffer=(bitBuffer<<8)|b;bitsInBuffer+=8;
            while(bitsInBuffer>=5)
            {
                bitsInBuffer-=5;
                sb.Append(alphabet[(bitBuffer>>bitsInBuffer)&31]);
            }
        }
        if(bitsInBuffer>0)sb.Append(alphabet[(bitBuffer<<(5-bitsInBuffer))&31]);
        return sb.ToString();
    }

    static byte[] FromBase32(string input)
    {
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        string s=input.Trim().TrimEnd('=').ToUpperInvariant();
        var output=new List<byte>(s.Length*5/8);
        int bitBuffer=0,bits=0;
        foreach(char ch in s)
        {
            int val=alphabet.IndexOf(ch);
            J.Need(val>=0,"سر التحقق الثنائي غير صالح.",400);
            bitBuffer=(bitBuffer<<5)|val;bits+=5;
            if(bits>=8){bits-=8;output.Add((byte)((bitBuffer>>bits)&0xFF));}
        }
        return output.ToArray();
    }
}
