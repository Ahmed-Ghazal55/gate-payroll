using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
namespace Payroll;

public static class UserAccess
{
    public static readonly string[] Roles = ["Admin", "Accountant", "Reviewer"];
    public static string Role(JsonObject u) => u.S("role", u.S("id") == "administrator" ? "Admin" : "Reviewer");
    public static string Status(JsonObject u) => u.S("status", "active");
    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
    public static JsonObject? Find(DbConnection c, string username, DbTransaction? tx = null) => Store.List(c,"user",tx).SingleOrDefault(u => Normalize(u.S("username")) == Normalize(username));
    public static JsonObject Public(JsonObject u) => new() { ["id"]=u.S("id"),["username"]=u.S("username"),["role"]=Role(u),["status"]=Status(u),["createdAt"]=u.S("createdAt"),["lastLoginAt"]=u.S("lastLoginAt") };
    public static JsonObject RiderView(JsonObject rider)
    {
        var result=new JsonObject();
        foreach(string field in new[]{"id","iqama","name","phone","nationality","sponsor","engagementType","city","operationalRole","status","policyId","workPreference"})result[field]=rider.S(field);
        return result;
    }
    public static JsonObject Create(string username, string password, string role, string? id = null)
    {
        J.Need(username.Length is >=3 and <=60 && Regex.IsMatch(username,@"^[\p{L}\p{N}_.@-]+$"),"اسم المستخدم من3إلى60حرفًا دون مسافات أو محارف خاصة.");
        J.Need(password.Length is >=12 and <=200,"كلمة المرور من12إلى200حرف."); J.Need(Roles.Contains(role),"دور غير صالح.");
        var salt=RandomNumberGenerator.GetBytes(16);
        return new() { ["id"]=id??Guid.NewGuid().ToString("N"),["username"]=username,["role"]=role,["status"]="active",["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["lastLoginAt"]="",["securityVersion"]=Guid.NewGuid().ToString("N"),["salt"]=Convert.ToBase64String(salt),["hash"]=Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password,salt,210000,HashAlgorithmName.SHA256,32)) };
    }
    public static bool Password(JsonObject? u, string password)
    {
        // Always perform PBKDF2, including unknown usernames, to reduce enumeration by timing.
        if(password.Length>200)return false;
        var salt=u==null?new byte[16]:Convert.FromBase64String(u.S("salt"));
        var hash=Rfc2898DeriveBytes.Pbkdf2(password,salt,210000,HashAlgorithmName.SHA256,32);
        return CryptographicOperations.FixedTimeEquals(hash,u==null?new byte[32]:Convert.FromBase64String(u.S("hash"))) && u!=null && Status(u)=="active";
    }
    public static ClaimsPrincipal Principal(JsonObject u) => new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,u.S("id")),new(ClaimTypes.Name,u.S("username")),new(ClaimTypes.Role,Role(u)),new("securityVersion",u.S("securityVersion"))],CookieAuthenticationDefaults.AuthenticationScheme));
    public static async Task ValidateCookie(CookieValidatePrincipalContext context)
    {
        var store=context.HttpContext.RequestServices.GetRequiredService<Store>();
        var u=store.Read(c=>Find(c,context.Principal?.Identity?.Name??""));
        if(u==null || Status(u)!="active" || (context.Principal!.FindFirst("securityVersion") is {} version && version.Value!=u.S("securityVersion")))
        { context.RejectPrincipal(); await context.HttpContext.SignOutAsync(); return; }
        // Upgrade legacy administrator cookies without invalidating a working production session.
        bool legacy=context.Principal?.FindFirst(ClaimTypes.Role)==null;
        context.ReplacePrincipal(Principal(u));
        context.ShouldRenew=legacy;
    }
    public static bool Allowed(string role, string method, string path)
    {
        if(role=="Admin") return true;
        if(path is "/api/me") return true;
        if(path.StartsWith("/api/users",StringComparison.Ordinal) || path.Contains("sync-baseline",StringComparison.Ordinal) || path.StartsWith("/api/forms/builder",StringComparison.Ordinal)) return false;
        if(path=="/api/excel/export") return role is "Reviewer" or "Accountant";
        if(path.Contains("/commit",StringComparison.Ordinal) || path.EndsWith("/approve",StringComparison.Ordinal)) return false;
        if(role=="Reviewer") return (method=="GET" && path=="/api/submissions" || method=="POST" && path.StartsWith("/api/submissions/",StringComparison.Ordinal) && path.EndsWith("/review",StringComparison.Ordinal)) || (method=="GET" && (path.StartsWith("/api/campaigns",StringComparison.Ordinal) || path.StartsWith("/api/riders",StringComparison.Ordinal) || path.StartsWith("/api/forms/catalog",StringComparison.Ordinal) || path.StartsWith("/api/forms/analytics",StringComparison.Ordinal) || path.StartsWith("/api/forms/responses",StringComparison.Ordinal) || path.Contains("/excel/choices/",StringComparison.Ordinal)));
        return role=="Accountant" && ((method=="GET" && path.StartsWith("/api/payroll",StringComparison.Ordinal)) || path.StartsWith("/api/ledger",StringComparison.Ordinal) || path.StartsWith("/api/payments",StringComparison.Ordinal) || path.StartsWith("/api/adjustments",StringComparison.Ordinal) || path.StartsWith("/api/debts",StringComparison.Ordinal) || path=="/api/inquiry-access" || (method=="GET" && (path.StartsWith("/api/riders",StringComparison.Ordinal) || path.StartsWith("/api/policies",StringComparison.Ordinal) || path=="/api/dashboard" || path.StartsWith("/api/imports",StringComparison.Ordinal) || path.StartsWith("/api/excel/",StringComparison.Ordinal))));
    }
    public static void Map(RouteGroupBuilder api, Store store)
    {
        api.MapGet("/users",()=>store.Read(c=>Store.List(c,"user").Select(Public)));
        api.MapPost("/users",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>{
            string name=input.Required("username"); J.Need(Find(c,name,tx)==null,"اسم المستخدم مسجل بالفعل.");
            var u=Create(name,input.Required("password"),input.Required("role")); Store.Save(c,"user",u,tx);Store.Audit(c,tx,"admin.user_create",u.S("id"),ctx.User.Identity!.Name!);return Public(u);
        }));
        api.MapPut("/users/{id}/status",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>{
            var u=Store.Get(c,id,"user",tx)??throw new ApiError("الحساب غير موجود.",404);
            string role=input.S("role",Role(u)),status=input.S("status",Status(u)); J.Need(Roles.Contains(role)&&status is "active" or "suspended","دور أو حالة غير صالحة.");
            J.Need(!(Role(u)=="Admin" && Status(u)=="active" && (role!="Admin" || status!="active") && Store.List(c,"user",tx).Count(x=>Role(x)=="Admin"&&Status(x)=="active")==1),"لا يمكن تعطيل آخر مسؤول نشط.");
            u["role"]=role;u["status"]=status;u["securityVersion"]=Guid.NewGuid().ToString("N");Store.Save(c,"user",u,tx);Store.Audit(c,tx,"admin.user_update",id,ctx.User.Identity!.Name!);return Public(u);
        }));
    }
}
