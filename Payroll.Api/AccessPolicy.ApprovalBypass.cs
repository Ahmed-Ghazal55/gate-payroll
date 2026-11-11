using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class AccessPolicy
{
    public const string BypassPermission="approval.bypass_maker_checker",BypassGrantKind="approval_bypass_grant";
    public static JsonObject? ActiveBypassGrant(DbConnection c,DbTransaction? tx,string username)
    {
        string name=UserAccess.Normalize(username);var today=Today();
        return Store.List(c,BypassGrantKind,tx).Where(g=>UserAccess.Normalize(g.S("username"))==name&&g.S("revokedAt")==""
            &&DateOnly.TryParseExact(g.S("from"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var from)
            &&DateOnly.TryParseExact(g.S("to"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var to)
            &&from<=today&&to>=today).OrderByDescending(g=>g.S("to")).FirstOrDefault();
    }
    public static bool BypassActive(ClaimsPrincipal actor,DbConnection? c=null,DbTransaction? tx=null)
    {
        if(actor.Identity?.IsAuthenticated!=true||string.IsNullOrWhiteSpace(actor.Identity.Name))return false;
        if(c!=null)return ActiveBypassGrant(c,tx,actor.Identity.Name)!=null;
        return string.Equals(actor.FindFirstValue("approvalBypass"),"1",StringComparison.Ordinal)
            &&DateOnly.TryParseExact(actor.FindFirstValue("approvalBypassUntil")??"","yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var until)
            &&until>=Today();
    }
    public static JsonObject GrantBypass(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        J.Need(Administrator(actor)||Has(actor,"users.manage"),"منح تجاوز الاعتماد لمدير الصلاحيات فقط.",403);
        string username=UserAccess.Normalize(input.Required("username"));
        var user=Store.List(c,"user",tx).FirstOrDefault(u=>UserAccess.Normalize(u.S("username"))==username)??throw new ApiError("المستخدم غير موجود.",404);
        string fromRaw=input.Required("from"),toRaw=input.Required("to"),reason=input.S("reason").Trim();
        J.Need(DateOnly.TryParseExact(fromRaw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var from),"تاريخ البداية غير صالح.");
        J.Need(DateOnly.TryParseExact(toRaw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var to)&&to>=from&&to<=Today().AddDays(365),"تاريخ النهاية غير صالح.");
        J.Need(reason.Length is >=3 and <=500&&!reason.Any(char.IsControl),"سبب المنح مطلوب من 3 إلى 500 حرف.");
        var grant=new JsonObject
        {
            ["id"]="approval-bypass:"+Guid.NewGuid().ToString("N"),
            ["userId"]=user.S("id"),["username"]=user.S("username"),
            ["from"]=from.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
            ["to"]=to.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
            ["reason"]=reason,["grantedBy"]=actor.Identity!.Name,["grantedAt"]=DateTimeOffset.UtcNow.ToString("O"),
            ["revokedAt"]="",["revokedBy"]="",["permission"]=BypassPermission
        };
        Store.Save(c,BypassGrantKind,grant,tx);Store.Audit(c,tx,"approval.bypass_granted",grant.S("id"),actor.Identity!.Name!);
        return grant;
    }
    public static JsonObject RevokeBypass(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        J.Need(Administrator(actor)||Has(actor,"users.manage"),"سحب تجاوز الاعتماد لمدير الصلاحيات فقط.",403);
        var grant=Store.Get(c,id,BypassGrantKind,tx)??throw new ApiError("المنح غير موجود.",404);
        J.Need(grant.S("revokedAt")=="","المنح مسحوب مسبقًا.");
        grant["revokedAt"]=DateTimeOffset.UtcNow.ToString("O");grant["revokedBy"]=actor.Identity!.Name;
        Store.Save(c,BypassGrantKind,grant,tx);Store.Audit(c,tx,"approval.bypass_revoked",id,actor.Identity!.Name!);
        return grant;
    }
    public static void MapBypass(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/admin/approval-bypass",(HttpContext ctx)=>
        {
            J.Need(Administrator(ctx.User)||Has(ctx.User,"users.manage"),"للمسؤول فقط.",403);
            return store.Read(c=>Store.List(c,BypassGrantKind).OrderByDescending(g=>g.S("grantedAt")));
        }).WithTags("Administration").WithSummary("List approval.bypass_maker_checker grants");
        api.MapPost("/admin/approval-bypass",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>GrantBypass(c,tx,ctx.User,input))).WithTags("Administration").WithSummary("Grant dated approval.bypass_maker_checker");
        api.MapPost("/admin/approval-bypass/{id}/revoke",(string id,HttpContext ctx)=>store.Write((c,tx)=>RevokeBypass(c,tx,ctx.User,id))).WithTags("Administration").WithSummary("Revoke an approval bypass grant");
    }
}
