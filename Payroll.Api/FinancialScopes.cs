using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static class FinancialScopes
{
    private static bool FinanceRole(string role)=>role is "Admin" or "SuperAdmin" or "Accountant" or "FinancialAccountant";
    public static JsonObject Normalize(JsonObject scope)
    {
        var result=new JsonObject();
        foreach(string key in new[]{"sponsorIds","accountCodes","platforms","categoryCodes"})
        {
            var list=scope[key] as JsonArray??new JsonArray();
            J.Need(list.Count<=150,"نطاق مالي أكبر من الحد المسموح.");
            var values=list.Select(v=>v?.ToString().Trim()??"").Distinct(StringComparer.Ordinal).ToList();
            foreach(string value in values)
            {
                J.Need(value!="*"&&value.Length is >0 and <=128,"قيمة نطاق مالي غير صالحة.");
                if(key=="sponsorIds")FinancialCore.ResolveSponsor(value);
                if(key=="accountCodes")FinancialCore.ResolveVault(value);
                if(key=="categoryCodes")FinancialCore.ResolveCategory(value);
                if(key=="platforms")J.Need(new[]{"Jahez","Keeta","HungerStation","Ninja","Other"}.Contains(value),"منصة غير صالحة في النطاق.");
            }
            if(key=="accountCodes")values=values.Select(v=>FinancialCore.ResolveVault(v).Code).Distinct().ToList();
            if(key=="categoryCodes")values=values.Select(v=>FinancialCore.ResolveCategory(v).Code).Distinct().ToList();
            if(key=="sponsorIds")values=values.Select(v=>FinancialCore.ResolveSponsor(v).Id).Distinct().ToList();
            result[key]=new JsonArray(values.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray());
        }
        return result;
    }
    public static JsonObject Effective(JsonObject user)
    {
        if(user["financialScope"] is JsonObject scope)return Normalize(scope);
        // Preserve legacy finance accounts; operational users start with no assigned legal entities.
        string role=UserAccess.Role(user);bool finance=FinanceRole(role);
        var categories=FinancialCore.CategoriesCatalog.Where(c=>finance||UserAccess.EffectivePermissions(user).Contains(AccessPolicy.CategoryPermission(c.Code))).Select(c=>c.Code);
        return new JsonObject{
            ["sponsorIds"]=new JsonArray((finance?FinancialCore.Sponsors.Select(s=>s.Id):[]).Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray()),
            ["accountCodes"]=new JsonArray((finance?FinancialCore.ChartOfAccounts.Where(a=>a.IsVault).Select(a=>a.Code):role=="PlatformsSupervisor"?FinancialCore.ChartOfAccounts.Where(a=>a.VaultKind=="JahezCashbox").Select(a=>a.Code):[]).Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray()),
            ["platforms"]=new JsonArray((role=="PlatformsSupervisor"?new[]{"Jahez"}:new[]{"Jahez","Keeta","HungerStation","Ninja","Other"}).Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray()),
            ["categoryCodes"]=new JsonArray(categories.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray())
        };
    }
    public static IEnumerable<Claim> Claims(JsonObject user)
    {
        var scope=Effective(user);
        foreach(var pair in scope)foreach(var value in pair.Value!.AsArray())yield return new Claim("finance."+pair.Key,value!.ToString());
    }
    private static bool Has(ClaimsPrincipal user,string key,string value)=>AccessPolicy.Administrator(user)||user.HasClaim("finance."+key,value);
    public static bool Sponsor(ClaimsPrincipal user,string? id)=>!string.IsNullOrEmpty(id)&&Has(user,"sponsorIds",FinancialCore.FindSponsor(id)?.Id??id);
    public static bool AllCompanies(ClaimsPrincipal user)=>FinancialCore.Sponsors.All(s=>Sponsor(user,s.Id));
    public static bool Account(ClaimsPrincipal user,string code)
    {
        var account=FinancialCore.ResolveAccount(code);
        if(!account.IsVault&&AccessPolicy.Has(user,"treasury.journals"))return FinancialCore.Sponsors.Any(s=>Sponsor(user,s.Id));
        if(!account.IsVault)return account.DefaultSponsorId==null?FinancialCore.ChartOfAccounts.Where(a=>a.IsVault&&FinancialCore.IsDescendantOrSelf(a.Code,code)).Any(a=>Account(user,a.Code)):Sponsor(user,account.DefaultSponsorId);
        return Sponsor(user,account.DefaultSponsorId)&&Has(user,"accountCodes",account.Code);
    }
    public static bool Category(ClaimsPrincipal user,string category)=>Has(user,"categoryCodes",category);
    public static bool Debt(ClaimsPrincipal user,JsonObject debt)=>(Sponsor(user,debt.S("creditorSponsorId"))||debt.S("creditorSponsorId")==""&&AllCompanies(user)&&FinanceRole(user.FindFirstValue(ClaimTypes.Role)??""))&&Category(user,debt.S("category"))&&(debt.S("platform")==""||Has(user,"platforms",debt.S("platform")));
    public static bool Rider(ClaimsPrincipal user,JsonObject rider)=>rider.S("financeSponsorId")!=""?Sponsor(user,rider.S("financeSponsorId")):Sponsor(user,rider.S("sponsorId"))||Sponsor(user,FinancialCore.FindSponsor(rider.S("sponsor"))?.Id);
    public static bool Ledger(ClaimsPrincipal user,JsonObject row)=>Sponsor(user,row.S("sponsorId"))||row.S("sponsorId")==""&&AllCompanies(user);
    public static void DemandRider(ClaimsPrincipal user,JsonObject rider){if(!Rider(user,rider)&&!AllCompanies(user))throw new ApiError("المندوب خارج النطاق المالي للمستخدم.",403);}
    public static void DemandSponsor(ClaimsPrincipal user,string id){if(!Sponsor(user,id))throw new ApiError("السجل التجاري خارج النطاق المالي للمستخدم.",403);}
    public static void DemandAccount(ClaimsPrincipal user,string code){if(!Account(user,code))throw new ApiError("الحساب المالي خارج النطاق المسند للمستخدم.",403);}
    public static void DemandDebt(ClaimsPrincipal user,JsonObject debt){if(!Debt(user,debt))throw new ApiError("المديونية خارج نطاق الشركة أو الفئة أو المنصة المسندة للمستخدم.",403);}
    public static bool Line(ClaimsPrincipal user,JsonObject line)=>Sponsor(user,line.S("sponsorId"))&&(FinancialCore.ResolveAccount(line.S("accountCode")).IsVault?Account(user,line.S("accountCode")):AccessPolicy.Has(user,"treasury.journals"));
    public static JsonObject? Journal(ClaimsPrincipal user,JsonObject journal)
    {
        var lines=(journal["lines"] as JsonArray)?.OfType<JsonObject>().Where(l=>Line(user,l)).ToList()??[];
        bool financial=AccessPolicy.Has(user,"treasury.journals");
        if(!financial&&!lines.Any(l=>FinancialCore.ResolveAccount(l.S("accountCode")).IsVault))return null;
        if(lines.Count==0)return null;
        var result=journal.Clone();result["lines"]=new JsonArray(lines.Select(l=>(JsonNode?)l.Clone()).ToArray());
        result["totalDebit"]=J.Money(lines.Sum(l=>l.N("debit")));result["totalCredit"]=J.Money(lines.Sum(l=>l.N("credit")));
        result["scopeFiltered"]=lines.Count!=(journal["lines"] as JsonArray)?.Count;return result;
    }
    public static bool Approval(ClaimsPrincipal user,JsonObject request,DbConnection c,DbTransaction? tx=null)
    {
        if(AccessPolicy.Administrator(user))return true;
        string department=user.FindFirstValue("department")??"";
        if(request.S("fromDepartment")!=department&&request.S("targetDepartment")!=department)return false;
        string debtId=request["payload"].S("debtId",request.S("referenceId"));
        var debt=Store.Get(c,debtId,"debt",tx);
        return debt!=null?Debt(user,debt):Sponsor(user,request.S("sponsorId"));
    }
}
