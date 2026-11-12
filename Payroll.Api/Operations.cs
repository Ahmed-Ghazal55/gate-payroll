using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.SqlClient;
namespace Payroll;

public static class Operations
{
    public static readonly string[] Kinds=["city","housing","housing_residence","vehicle","vehicle_assignment","custody_handover","sponsorship_period","legal_case","platform_account","vehicle_operational_status","platform_day_usage","rider_work_timeline","custody_equipment","vehicle_expense","chefz_account","chefz_account_type_change","chefz_daily","chefz_settlement","chefz_order_row","ninja_daily","ninja_daily_deduction","ninja_evaluation_snapshot","jahez_commission_policy","jahez_daily_order"];
    static readonly Dictionary<string,string[]> Fields=new(){
        ["city"]=["code","nameAr","nameEn","name","globalCityId"],
        ["housing"]=["code","nameAr","nameEn","cityId","openedDate","closedDate"],
        ["housing_residence"]=["riderId","housingId","roomSourceId","effectiveFrom","effectiveTo"],
        ["vehicle"]=["assetNumber","serialNumber","plateNumberAr","plateNumberEn","vin","chassisNumber","modelYear","vehicleType","fuelType","ownershipType","currentOdometer","currentOperationalStatus","registrationType","companyId","registeredOwnerCompanyId","cityId"],
        ["vehicle_assignment"]=["riderId","vehicleId","startedAt","endedAt","startOdometer","endOdometer","isRealRider","permissionStartsOn","permissionEndsOn"],
        ["vehicle_operational_status"]=["vehicleId","companyId","statusCode","effectiveFrom","effectiveTo","reason","reasonCode"],
        ["custody_handover"]=["assignmentId","eventTypeCode","occurredAt","direction"],
        ["custody_equipment"]=["riderId","companyId","issueNumber","issuedAt","quantity","returnedQuantity","totalCost"],
        ["vehicle_expense"]=["vehicleId","riderId","companyId","type","occurredOn","amount","vat","amountBeforeTax"],
        ["sponsorship_period"]=["riderId","companyId","sponsorId","effectiveFrom","effectiveTo"],
        ["legal_case"]=["companyId","number","personName","personTypeCode","riderId","partyRoleCode","caseDate","caseTime","details"],
        ["platform_account"]=["code","externalAccountId","platformCode","registeredRiderId","companyId","cityId","paymentModelCode","startDate","endDate"],
        ["platform_day_usage"]=["riderId","companyId","platformAccountId","date","orders","hours","collectedAmount","deductionsAmount"],
        ["rider_work_timeline"]=["riderId","companyId","platformCode","platformAccountId","accountCode","externalAccountId","effectiveFrom","effectiveTo","endReasonCode","endReason","nextRiderId","paymentModelCode"],
        ["chefz_account"]=["platformAccountId","companyId","registeredRiderId","reportIdNumber","accountType","vehicleType","dashboardStatus"],
        ["chefz_account_type_change"]=["platformAccountId","companyId","previousTypeCode","accountType","effectiveFrom"],
        ["chefz_daily"]=["riderId","companyId","platformAccountId","date","totalValidOffers","acceptedOffers","declinedOffers","missedOffers"],
        ["chefz_settlement"]=["riderId","companyId","platformAccountId","periodFrom","periodTo","gross","companyAmount","riderAmount"],
        ["chefz_order_row"]=["riderId","companyId","platformAccountId","date","orderId","deliveryFees","cashCollected"],
        ["ninja_daily"]=["riderId","companyId","platformAccountId","date","orders","hours","CaptainId"],
        ["ninja_daily_deduction"]=["riderId","companyId","platformAccountId","date","collectedAmount","amount","CaptainId"],
        ["ninja_evaluation_snapshot"]=["frozen","policyVersion","inputRevision","reason","createdAt"],
        ["jahez_commission_policy"]=["companyId","platformAccountId","from","to","rate"],
        ["jahez_daily_order"]=["companyId","platformAccountId","date","count"]
    };
    public static JsonObject Project(JsonObject row,string kind)
    {
        var result=Pick(row,Fields[kind]);
        if(kind=="platform_account")result["assignments"]=new JsonArray((row["assignments"] as JsonArray??[]).OfType<JsonObject>().Select(a=>(JsonNode?)Assignment(a)).ToArray());
        if(kind=="housing"){result["rooms"]=new JsonArray((row["rooms"] as JsonArray??[]).OfType<JsonObject>().Select(r=>(JsonNode?)Pick(r,["Code","NameAr","NameEn","FloorId","Capacity"])).ToArray());result["externalOccupantsOmitted"]=true;}
        return result;
    }
    static JsonObject Pick(JsonObject row,IEnumerable<string> fields)
    {var result=new JsonObject();foreach(string key in fields.Prepend("id"))if(row.ContainsKey(key))result[key]=row[key]?.DeepClone();return result;}
    static bool Company(ClaimsPrincipal actor,string company)=>company!=""?FinancialScopes.Sponsor(actor,company):FinancialScopes.AllCompanies(actor);
    static bool Person(DbConnection c,ClaimsPrincipal actor,string rider)
    {
        if(rider=="")return FinancialScopes.AllCompanies(actor);
        var person=Store.Get(c,rider,"rider");
        if(person!=null)return FinancialScopes.Rider(actor,person)||FinancialScopes.AllCompanies(actor);
        // Live ERP row with no payroll rider copy: only all-company actors see it.
        return FinancialScopes.AllCompanies(actor);
    }
    public static bool Visible(DbConnection c,ClaimsPrincipal actor,string kind,JsonObject row,Func<string,string,JsonObject?>? parent=null,Func<string,IEnumerable<JsonObject>>? liveKind=null)
    {
        JsonObject? Get(string k,string id)=>id==""?null:parent?.Invoke(k,id)??Store.Get(c,id,k);
        IEnumerable<JsonObject> Residences()=>liveKind?.Invoke("housing_residence")??Store.List(c,"housing_residence");
        if(kind=="city")return FinancialCore.Sponsors.Any(s=>FinancialScopes.Sponsor(actor,s.Id));
        if(kind=="platform_account")return Company(actor,row.S("companyId"))&&FinancialScopes.Platform(actor,PayPlatforms.For(row.S("platformCode")).Platform);
        if(kind is "vehicle" or "legal_case" or "sponsorship_period" or "vehicle_expense" or "chefz_account" or "chefz_account_type_change" or "jahez_commission_policy" or "jahez_daily_order")return Company(actor,row.S("companyId",row.S("sponsorId")));
        if(kind=="vehicle_operational_status")return Company(actor,row.S("companyId"))||Get("vehicle",row.S("vehicleId")) is {} statusVehicle&&Company(actor,statusVehicle.S("companyId",row.S("companyId")));
        if(kind=="housing")return FinancialScopes.AllCompanies(actor)||Residences().Any(r=>r.S("housingId")==row.S("id")&&Person(c,actor,r.S("riderId")));
        if(kind is "housing_residence" or "custody_equipment" or "platform_day_usage" or "rider_work_timeline" or "chefz_daily" or "chefz_settlement" or "chefz_order_row" or "ninja_daily" or "ninja_daily_deduction")return Person(c,actor,row.S("riderId"));
        if(kind=="vehicle_assignment")return Person(c,actor,row.S("riderId"))&&Get("vehicle",row.S("vehicleId")) is {} vehicle&&Company(actor,vehicle.S("companyId"));
        if(kind=="custody_handover")return Get("vehicle_assignment",row.S("assignmentId")) is {} assignment&&Visible(c,actor,"vehicle_assignment",assignment,parent,liveKind);
        if(kind=="ninja_evaluation_snapshot")return AccessPolicy.Has(actor,"operations.view");
        return false;
    }
    static JsonObject Assignment(JsonObject source)=>Pick(source,["riderId","RawEffectiveFrom","RawEffectiveTo","EffectiveFrom","EffectiveTo","Status","IsRealRider"]);
    public static IEnumerable<JsonObject> List(DbConnection c,ClaimsPrincipal actor,string kind)
    {
        AccessPolicy.Demand(actor,"operations.view");if(!Kinds.Contains(kind))throw new ApiError("نوع عمليات غير مسموح.",404);
        foreach(var row in Store.List(c,kind).Where(r=>Visible(c,actor,kind,r)))
            yield return Project(row,kind);
    }
    public static IEnumerable<JsonObject> ListLive(DbConnection payroll,ClaimsPrincipal actor,string kind,SourceSnapshot snapshot)
    {
        AccessPolicy.Demand(actor,"operations.view");if(!Kinds.Contains(kind))throw new ApiError("نوع عمليات غير مسموح.",404);
        var docs=LiveOperationsSource.Documents(snapshot,FinancialCore.Sponsors,kind);
        string[] parentKinds=kind switch
        {
            "custody_handover"=>["vehicle_assignment","vehicle"],
            "vehicle_assignment"=>["vehicle"],
            "vehicle_operational_status"=>["vehicle"],
            "housing"=>["housing_residence"],
            _=>[]
        };
        IReadOnlyDictionary<(string Kind,string Id),JsonObject> index=parentKinds.Length==0
            ?new Dictionary<(string,string),JsonObject>()
            :LiveOperationsSource.Index(snapshot,FinancialCore.Sponsors,parentKinds);
        JsonObject? Parent(string k,string id)=>index.GetValueOrDefault((k,id));
        IEnumerable<JsonObject> LiveKind(string k)=>k==kind?docs:LiveOperationsSource.Documents(snapshot,FinancialCore.Sponsors,k);
        foreach(var row in docs.Where(r=>Visible(payroll,actor,kind,r,Parent,LiveKind)))
            yield return Project(row,kind);
    }
    public static IEnumerable<JsonObject> RiderAccounts(DbConnection c,ClaimsPrincipal actor,string rider)
    {
        AccessPolicy.Demand(actor,"operations.view");rider=J.Iqama(rider);var person=Store.Get(c,rider,"rider")??throw new ApiError("المندوب غير موجود.",404);FinancialScopes.DemandRider(actor,person);
        foreach(var account in List(c,actor,"platform_account"))
        {
            var assignments=account["assignments"]!.AsArray().OfType<JsonObject>().Where(a=>a.S("riderId")==rider).ToList();if(assignments.Count==0)continue;account["assignments"]=new JsonArray(assignments.Select(a=>(JsonNode?)a.Clone()).ToArray());account["relationship"]="actual_operator";yield return account;
        }
    }
    public static IEnumerable<JsonObject> RiderAccountsLive(DbConnection payroll,ClaimsPrincipal actor,string rider,SourceSnapshot snapshot)
    {
        AccessPolicy.Demand(actor,"operations.view");rider=J.Iqama(rider);var person=Store.Get(payroll,rider,"rider")??throw new ApiError("المندوب غير موجود.",404);FinancialScopes.DemandRider(actor,person);
        foreach(var account in ListLive(payroll,actor,"platform_account",snapshot))
        {
            var assignments=account["assignments"]!.AsArray().OfType<JsonObject>().Where(a=>a.S("riderId")==rider).ToList();if(assignments.Count==0)continue;account["assignments"]=new JsonArray(assignments.Select(a=>(JsonNode?)a.Clone()).ToArray());account["relationship"]="actual_operator";yield return account;
        }
    }
    static SqlConnection OpenErp(IConfiguration config)
    {
        string sourceConnection=config.GetConnectionString("ErpReadonly")??config["ERP_READONLY_DB"]??"";
        if(string.IsNullOrWhiteSpace(sourceConnection))throw new ApiError("اتصال مصدر التشغيل غير مضبوط.",503);
        try{var source=Connections.Source(sourceConnection);source.Open();SourceReader.VerifyReadOnly(source);return source;}
        catch(ImportFailure ex) when(ex.Code is "unapproved_source" or "source_connection_missing" or "source_has_write_permissions"){throw new ApiError("مصدر التشغيل غير معتمد للقراءة فقط.",503);}
    }
    public static void Map(RouteGroupBuilder api,Store store,IConfiguration config)
    {
        api.MapGet("/operations/{kind}",(string kind,HttpContext ctx)=>{
            using var erp=OpenErp(config);
            var snapshot=LiveOperationsSource.Read(erp,kind,ctx.RequestAborted);
            return store.Read(c=>ApiPagination.Apply(ListLive(c,ctx.User,kind,snapshot),ctx.Request.Query,true));
        }).WithTags("Operations").WithSummary("Read allowlisted ERP operations live from logistics ERP with company scope and pagination");
        api.MapGet("/riders/{iqama}/accounts",(string iqama,HttpContext ctx)=>{
            using var erp=OpenErp(config);
            var snapshot=LiveOperationsSource.Read(erp,"platform_account",ctx.RequestAborted);
            return store.Read(c=>ApiPagination.Apply(RiderAccountsLive(c,ctx.User,iqama,snapshot),ctx.Request.Query,true));
        }).WithTags("Operations").WithSummary("Read actual rider platform assignments live from logistics ERP");
    }
}
