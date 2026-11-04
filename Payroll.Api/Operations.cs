using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static class Operations
{
    public static readonly string[] Kinds=["city","housing","housing_residence","vehicle","vehicle_assignment","custody_handover","sponsorship_period","legal_case","platform_account"];
    static readonly Dictionary<string,string[]> Fields=new(){
        ["city"]=["code","nameAr","nameEn","name","globalCityId"],
        ["housing"]=["code","nameAr","nameEn","cityId","openedDate","closedDate"],
        ["housing_residence"]=["riderId","housingId","roomSourceId","effectiveFrom","effectiveTo"],
        ["vehicle"]=["assetNumber","serialNumber","plateNumberAr","plateNumberEn","vin","chassisNumber","modelYear","vehicleType","fuelType","ownershipType","currentOdometer","currentOperationalStatus","registrationType","companyId","registeredOwnerCompanyId","cityId"],
        ["vehicle_assignment"]=["riderId","vehicleId","startedAt","endedAt","startOdometer","endOdometer","isRealRider","permissionStartsOn","permissionEndsOn"],
        ["custody_handover"]=["assignmentId","eventTypeCode","occurredAt","direction"],
        ["sponsorship_period"]=["riderId","companyId","sponsorId","effectiveFrom","effectiveTo"],
        ["legal_case"]=["companyId","number","personName","personTypeCode","riderId","partyRoleCode","caseDate","caseTime","details"],
        ["platform_account"]=["code","externalAccountId","platformCode","registeredRiderId","companyId","cityId","paymentModelCode","startDate","endDate"]
    };
    static JsonObject Pick(JsonObject row,IEnumerable<string> fields)
    {var result=new JsonObject();foreach(string key in fields.Prepend("id"))if(row.ContainsKey(key))result[key]=row[key]?.DeepClone();return result;}
    static bool Company(ClaimsPrincipal actor,string company)=>company!=""?FinancialScopes.Sponsor(actor,company):FinancialScopes.AllCompanies(actor);
    static bool Person(DbConnection c,ClaimsPrincipal actor,string rider)
    {var person=Store.Get(c,rider,"rider");return person!=null&&(FinancialScopes.Rider(actor,person)||FinancialScopes.AllCompanies(actor));}
    public static bool Visible(DbConnection c,ClaimsPrincipal actor,string kind,JsonObject row)
    {
        if(kind=="city")return FinancialCore.Sponsors.Any(s=>FinancialScopes.Sponsor(actor,s.Id));
        if(kind=="platform_account")return Company(actor,row.S("companyId"))&&FinancialScopes.Platform(actor,PayPlatforms.For(row.S("platformCode")).Platform);
        if(kind is "vehicle" or "legal_case" or "sponsorship_period")return Company(actor,row.S("companyId",row.S("sponsorId")));
        if(kind=="housing")return FinancialScopes.AllCompanies(actor)||Store.List(c,"housing_residence").Any(r=>r.S("housingId")==row.S("id")&&Person(c,actor,r.S("riderId")));
        if(kind=="housing_residence")return Person(c,actor,row.S("riderId"));
        if(kind=="vehicle_assignment")return Person(c,actor,row.S("riderId"))&&Store.Get(c,row.S("vehicleId"),"vehicle") is {} vehicle&&Company(actor,vehicle.S("companyId"));
        if(kind=="custody_handover")return Store.Get(c,row.S("assignmentId"),"vehicle_assignment") is {} assignment&&Visible(c,actor,"vehicle_assignment",assignment);
        return false;
    }
    static JsonObject Assignment(JsonObject source)=>Pick(source,["riderId","RawEffectiveFrom","RawEffectiveTo","EffectiveFrom","EffectiveTo","Status","IsRealRider"]);
    public static IEnumerable<JsonObject> List(DbConnection c,ClaimsPrincipal actor,string kind)
    {
        AccessPolicy.Demand(actor,"operations.view");if(!Kinds.Contains(kind))throw new ApiError("نوع عمليات غير مسموح.",404);
        foreach(var row in Store.List(c,kind).Where(r=>Visible(c,actor,kind,r)))
        {
            var result=Pick(row,Fields[kind]);
            if(kind=="platform_account")result["assignments"]=new JsonArray((row["assignments"] as JsonArray??[]).OfType<JsonObject>().Select(a=>(JsonNode?)Assignment(a)).ToArray());
            // Housing can contain people from several companies. Only structural room data is exposed here.
            if(kind=="housing"){result["rooms"]=new JsonArray((row["rooms"] as JsonArray??[]).OfType<JsonObject>().Select(r=>(JsonNode?)Pick(r,["Code","NameAr","NameEn","FloorId","Capacity"])).ToArray());result["externalOccupantsOmitted"]=true;}
            yield return result;
        }
    }
    public static IEnumerable<JsonObject> RiderAccounts(DbConnection c,ClaimsPrincipal actor,string rider)
    {
        AccessPolicy.Demand(actor,"operations.view");rider=J.Iqama(rider);var person=Store.Get(c,rider,"rider")??throw new ApiError("المندوب غير موجود.",404);FinancialScopes.DemandRider(actor,person);
        foreach(var account in List(c,actor,"platform_account"))
        {
            var assignments=account["assignments"]!.AsArray().OfType<JsonObject>().Where(a=>a.S("riderId")==rider).ToList();if(assignments.Count==0)continue;account["assignments"]=new JsonArray(assignments.Select(a=>(JsonNode?)a.Clone()).ToArray());account["relationship"]="actual_operator";yield return account;
        }
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/operations/{kind}",(string kind,HttpContext ctx)=>store.Read(c=>ApiPagination.Apply(List(c,ctx.User,kind),ctx.Request.Query,true))).WithTags("Operations").WithSummary("Read allowlisted ERP operations with company scope and pagination");
        api.MapGet("/riders/{iqama}/accounts",(string iqama,HttpContext ctx)=>store.Read(c=>ApiPagination.Apply(RiderAccounts(c,ctx.User,iqama),ctx.Request.Query,true))).WithTags("Operations").WithSummary("Read actual rider platform assignments and receive/deliver dates");
    }
}
