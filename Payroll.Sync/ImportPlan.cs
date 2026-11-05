using System.Data.Common;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace ErpImport;

public sealed record PlannedDocument(string Kind,JsonObject Body);
public sealed class ImportPlan
{
    public static readonly string[] Kinds=["company","commercial_registration","city","rider","housing","housing_residence","vehicle","vehicle_assignment","custody_handover","platform_account","sponsorship_period","legal_case","hs_wallet_event","hs_remittance","hs_remittance_allocation","hs_handover_review","hs_month_close",..PullContracts.Kinds];
    public List<PlannedDocument> Documents { get; }=[];
    public Dictionary<string,Dictionary<string,int>> Rejected { get; }=[];
    public Dictionary<string,int> Observations { get; }=[];
    public Dictionary<string,string> Unavailable { get; }=new(){["sponsorship_period"]="source_table_absent"};
    internal void Reject(string kind,string reason){if(!Rejected.TryGetValue(kind,out var counts))Rejected[kind]=counts=[];counts[reason]=counts.GetValueOrDefault(reason)+1;}
    void Observe(string reason)=>Observations[reason]=Observations.GetValueOrDefault(reason)+1;
    static string Key(string kind,string source)=>"erp:"+kind+":"+source;
    static JsonArray Array(IEnumerable<JsonObject> rows)=>new(rows.Select(x=>(JsonNode?)x.Clone()).ToArray());
    static string Date(JsonObject row,string field)=>row.S(field).Split('T')[0];
    static JsonObject Doc(string kind,JsonObject row)=>new(){["id"]=Key(kind,row.S("Id")),["erpSourceId"]=row.S("Id"),["erpSource"]=kind,["sourceDeleted"]=row.B("IsDeleted"),["erpStatusCode"]=row["Status"]?.DeepClone()};
    public static ImportPlan Build(SourceSnapshot snapshot,IReadOnlyList<ISyncSponsor> sponsorCatalog)
    {
        var plan=new ImportPlan();List<JsonObject> Rows(string table)=>snapshot.Tables.GetValueOrDefault(table)??[];
        Dictionary<string,JsonObject> Index(string table)=>Rows(table).ToDictionary(r=>r.S("Id"));
        var companyProfiles=Index("CompanyProfile");
        foreach(var profile in companyProfiles.Values)if(profile.S("CommercialRegistrationNumber")!="")plan.Observe("company_profile_registration_needs_legal_entity_mapping");
        var sponsors=Index("Sponsors");var companies=new Dictionary<string,string>();
        foreach(var row in Rows("Sponsors"))
        {
            var byReg=sponsorCatalog.Where(s=>row.S("CommercialRegistrationNumber")!=""&&s.CommercialReg==row.S("CommercialRegistrationNumber")).ToArray();
            var matches=byReg.Length>0?byReg:sponsorCatalog.Where(s=>s.Id==row.S("Id")||row.S("EmployerIdentityNumber")!=""&&s.UnifiedNumber==row.S("EmployerIdentityNumber")||row.S("UnifiedNationalNumber")!=""&&s.UnifiedNumber==row.S("UnifiedNationalNumber")).ToArray();
            if(matches.Length>1){plan.Reject("company","ambiguous_existing_company");continue;}
            string id=matches.Length==1?matches[0].Id:Key("company",row.S("Id"));companies[row.S("Id")]=id;
            if(matches.Length==1)plan.Observe("company_reuses_payroll_sponsor_key");else plan.Observe("company_without_existing_finance_catalog_match");
            var doc=Doc("company",row);doc["id"]=id;doc["sponsorId"]=id;doc["nameAr"]=row.S("RegistryNameAr");doc["shortNameAr"]=matches.FirstOrDefault()?.ShortNameAr??row.S("RegistryNameAr");doc["code"]=matches.FirstOrDefault()?.Code??"";doc["nameEn"]=row.S("RegistryNameEn");doc["commercialReg"]=row.S("CommercialRegistrationNumber");doc["unifiedNumber"]=row.S("UnifiedNationalNumber",row.S("EmployerIdentityNumber"));doc["employerIdentityNumber"]=row.S("EmployerIdentityNumber");doc["city"]=row.S("AddressCity");doc["status"]=row.B("IsDeleted")?"archived":row.N("Status")==1?"active":"inactive";doc["companyProfileId"]=row.S("CompanyProfileId");doc["companyProfile"]=companyProfiles.GetValueOrDefault(row.S("CompanyProfileId"))?.Clone();
            plan.Documents.Add(new("company",doc));
            if(row.S("CommercialRegistrationNumber")==""){plan.Reject("commercial_registration","missing_registration_number");continue;}
            var registration=Doc("commercial_registration",row);registration["companyId"]=id;registration["company"]=id;registration["number"]=row.S("CommercialRegistrationNumber");registration["city"]=row.S("AddressCity");registration["issueDate"]="";registration["expiryDate"]="";registration["activeFrom"]=Date(row,"ActiveFrom");registration["activeTo"]=Date(row,"ActiveTo");registration["status"]=doc.S("status");
            plan.Documents.Add(new("commercial_registration",registration)); // Sponsor activity dates are not CR issue/expiry dates.
        }
        string Company(string id)=>companies.GetValueOrDefault(id)??"";
        var global=Index("GlobalCities");var operating=Index("OperatingCities");
        string City(string id)=>global.ContainsKey(id)?Key("city",id):operating.TryGetValue(id,out var o)?Key("city",o.S("GlobalCityId")):"";
        foreach(var row in Rows("GlobalCities"))
        {
            var doc=Doc("city",row);foreach(string field in new[]{"Code","NameAr","NameEn","RegionAr","RegionEn","CountryCode"})doc[char.ToLowerInvariant(field[0])+field[1..]]=row[field]?.DeepClone();doc["operatingCities"]=Array(Rows("OperatingCities").Where(o=>o.S("GlobalCityId")==row.S("Id")));plan.Documents.Add(new("city",doc));
        }
        var employeeRows=Index("Employees");var profiles=Index("RiderProfiles");
        var riderEmployees=Rows("RiderProfiles").Select(r=>r.S("EmployeeId")).ToHashSet();
        var identities=Rows("Employees").Where(e=>riderEmployees.Contains(e.S("Id"))).GroupBy(e=>e.S("IqamaNo")).ToDictionary(g=>g.Key,g=>g.Count());
        var riders=new Dictionary<string,string>();var profileRiders=new Dictionary<string,string>();
        string Rider(string employee)=>riders.GetValueOrDefault(employee)??"";
        foreach(var row in Rows("Employees"))
        {
            if(!riderEmployees.Contains(row.S("Id"))){plan.Reject("rider","employee_without_rider_profile");continue;}
            string identity=row.S("IqamaNo");if(!Regex.IsMatch(identity,"^[12][0-9]{9}$")){plan.Reject("rider","invalid_identity");continue;}
            if(identities[identity]>1){plan.Reject("rider","duplicate_identity");continue;}
            string relationship=row.N("EngagementType") switch{1=>"SponsoredInternal",2=>"External",_=>""};
            if(relationship==""){plan.Reject("rider","unknown_relationship_code");continue;}
            var doc=Doc("rider",row);doc["id"]=identity;doc["iqama"]=identity;doc["name"]=row.S("FullNameAr",row.S("FullNameEn"));doc["nameEn"]=row.S("FullNameEn");doc["nationality"]=row.S("Nationality");doc["phone"]=row.S("PrimaryPhone");doc["iban"]=row.S("Iban");doc["erpRiderProfileIds"]=new JsonArray(Rows("RiderProfiles").Where(p=>p.S("EmployeeId")==row.S("Id")).Select(p=>(JsonNode?)JsonValue.Create(p.S("Id"))).ToArray());doc["engagementType"]=relationship;doc["isEmployee"]=row.B("IsEmployee");doc["personRole"]=row.B("IsEmployee")?"administrative":"rider";doc["isDeliveryRider"]=!row.B("IsEmployee");doc["sponsorId"]=Company(row.S("SponsorId"));doc["sponsor"]=sponsors.GetValueOrDefault(row.S("SponsorId"))?.S("RegistryNameAr")??"";doc["cityId"]=City(row.S("OperatingCityId"));doc["city"]=operating.TryGetValue(row.S("OperatingCityId"),out var op)&&global.TryGetValue(op.S("GlobalCityId"),out var gc)?gc.S("NameAr"):"";doc["hireDate"]=Date(row,"HireDate");doc["status"]=row.B("IsDeleted")?"Archived":row.N("Status") switch{1=>"Draft",2=>"Onboarding",3=>"Active",4=>"Suspended",5=>"OnLeave",6=>"Terminated",7=>"Archived",8=>"Fleeing",9=>"Accident",10=>"Sick",_=>"NeedsReview"};
            if(row.S("SponsorId")!=""&&doc.S("sponsorId")==""){plan.Reject("rider","missing_company_relation");continue;}
            if(relationship=="SponsoredInternal"&&row.S("SponsorId")=="")plan.Observe("sponsored_rider_without_current_sponsor");
            plan.Observe(row.B("IsEmployee")?"administrative_rider_profiles":relationship=="External"?"external_riders":"sponsored_riders");riders[row.S("Id")]=identity;plan.Documents.Add(new("rider",doc));
        }
        foreach(var p in profiles.Values)if(riders.TryGetValue(p.S("EmployeeId"),out var id))profileRiders[p.S("Id")]=id;
        var housing=Index("Housing");var rooms=Index("HousingRooms");
        foreach(var row in Rows("Housing"))
        {
            var doc=Doc("housing",row);doc["code"]=row.S("Code");doc["nameAr"]=row.S("NameAr");doc["nameEn"]=row.S("NameEn");doc["cityId"]=City(row.S("CityId"));doc["openedDate"]=Date(row,"OpenedDate");doc["closedDate"]=Date(row,"ClosedDate");doc["floors"]=Array(Rows("HousingFloors").Where(x=>x.S("HousingId")==row.S("Id")));var localRooms=Rows("HousingRooms").Where(x=>x.S("HousingId")==row.S("Id")).ToList();doc["rooms"]=Array(localRooms);var roomIds=localRooms.Select(r=>r.S("Id")).ToHashSet();doc["externalOccupants"]=Array(Rows("HousingExternalOccupants").Where(x=>roomIds.Contains(x.S("RoomId"))));plan.Documents.Add(new("housing",doc));
        }
        foreach(var row in Rows("HousingResidencePeriods"))
        {
            if(!rooms.TryGetValue(row.S("RoomId"),out var room)||!housing.ContainsKey(room.S("HousingId"))){plan.Reject("housing_residence","missing_room_relation");continue;}
            var doc=Doc("housing_residence",row);doc["employeeSourceId"]=row.S("EmployeeId");doc["riderId"]=Rider(row.S("EmployeeId"));doc["housingId"]=Key("housing",room.S("HousingId"));doc["roomSourceId"]=row.S("RoomId");doc["effectiveFrom"]=Date(row,"EffectiveFrom");doc["effectiveTo"]=Date(row,"EffectiveTo");if(!Dates(doc,"effectiveFrom","effectiveTo")){plan.Reject("housing_residence","invalid_date_range");continue;}plan.Documents.Add(new("housing_residence",doc));
        }
        var vehicles=Index("Vehicles");
        foreach(var row in Rows("Vehicles"))
        {
            var doc=Doc("vehicle",row);foreach(string field in new[]{"AssetNumber","SerialNumber","PlateNumberAr","PlateNumberEn","Vin","ChassisNumber","ModelYear","VehicleType","FuelType","OwnershipType","RegistrationType"})doc[char.ToLowerInvariant(field[0])+field[1..]]=row[field]?.DeepClone();doc["companyId"]=Company(row.S("SponsorId"));doc["registeredOwnerCompanyId"]=Company(row.S("RegisteredOwnerSponsorId"));doc["registeredOwnerSupplierSourceId"]=row.S("RegisteredOwnerSupplierId");doc["manufacturerSourceId"]=row.S("VehicleManufacturerId");doc["modelSourceId"]=row.S("VehicleModelId");doc["registrations"]=Array(Rows("VehicleRegistrations").Where(x=>x.S("VehicleId")==row.S("Id")));doc["insurancePolicies"]=Array(Rows("VehicleInsurancePolicies").Where(x=>x.S("VehicleId")==row.S("Id")));plan.Documents.Add(new("vehicle",doc));
        }
        var validAssignments=new HashSet<string>();
        foreach(var row in Rows("RiderVehicleAssignments"))
        {
            if(!vehicles.ContainsKey(row.S("VehicleId"))){plan.Reject("vehicle_assignment","missing_vehicle_relation");continue;}
            string rider=profileRiders.GetValueOrDefault(row.S("RiderProfileId"))??"";
            if(rider==""){plan.Reject("vehicle_assignment","unmapped_rider_relation");continue;}
            var doc=Doc("vehicle_assignment",row);doc["riderId"]=rider;doc["vehicleId"]=Key("vehicle",row.S("VehicleId"));doc["startedAt"]=row.S("StartedAtUtc");doc["endedAt"]=row.S("EndedAtUtc");doc["startOdometer"]=row["StartOdometer"]?.DeepClone();doc["endOdometer"]=row["EndOdometer"]?.DeepClone();doc["isRealRider"]=row["IsRealRider"]?.DeepClone();doc["realRiders"]=Array(Rows("RealRiders").Where(r=>r.S("RiderVehicleAssignmentId")==row.S("Id")));doc["permissionStartsOn"]=Date(row,"PermissionStartsOn");doc["permissionEndsOn"]=Date(row,"PermissionEndsOn");doc["previousAssignmentSourceId"]=row.S("PreviousAssignmentId");if(!Dates(doc,"startedAt","endedAt")){plan.Reject("vehicle_assignment","invalid_date_range");continue;}validAssignments.Add(row.S("Id"));plan.Documents.Add(new("vehicle_assignment",doc));
        }
        foreach(var row in Rows("RiderVehicleAssignmentEvents"))
        {
            if(!validAssignments.Contains(row.S("RiderVehicleAssignmentId"))){plan.Reject("custody_handover","unmapped_assignment_relation");continue;}
            if(row.N("EventType") is not (1 or 2 or 3 or 4)){plan.Reject("custody_handover","non_handover_event");continue;}
            var doc=Doc("custody_handover",row);doc["assignmentId"]=Key("vehicle_assignment",row.S("RiderVehicleAssignmentId"));doc["eventTypeCode"]=row["EventType"]?.DeepClone();doc["occurredAt"]=row.S("OccurredAtUtc");doc["direction"]=row.N("EventType") is 1 or 4?"handover":"return";if(doc.S("occurredAt").StartsWith("2026-10",StringComparison.Ordinal))plan.Observe("october_custody_"+doc.S("direction"));plan.Documents.Add(new("custody_handover",doc));
        }
        var platforms=Index("ClientPlatforms");
        foreach(var row in Rows("PlatformRiderAccounts"))
        {
            if(!platforms.TryGetValue(row.S("ClientPlatformId"),out var platform)){plan.Reject("platform_account","missing_platform_relation");continue;}
            var doc=Doc("platform_account",row);doc["code"]=row.S("Code");doc["externalAccountId"]=row.S("ExternalAccountId");doc["platformCode"]=platform.S("Code");doc["registeredEmployeeSourceId"]=row.S("RegisteredEmployeeId");doc["registeredRiderId"]=Rider(row.S("RegisteredEmployeeId"));doc["companyId"]=Company(row.S("SponsorId"));doc["cityId"]=City(row.S("OperatingCityId"));doc["paymentModelCode"]=row["PaymentModel"]?.DeepClone();doc["startDate"]=Date(row,"StartDate");doc["endDate"]=Date(row,"EndDate");doc["assignments"]=Array(Rows("RiderClientAssignments").Where(a=>a.S("PlatformRiderAccountId")==row.S("Id")).Select(a=>{var operation=a.Clone();operation["riderId"]=profileRiders.GetValueOrDefault(a.S("RiderProfileId"))??"";return operation;}));doc["registrations"]=Array(Rows("PlatformAccountRegistrations").Where(a=>a.S("PlatformRiderAccountId")==row.S("Id")));plan.Documents.Add(new("platform_account",doc));
        }
        foreach(var row in Rows("HrLegalCases"))
        {
            string company=Company(row.S("SponsorId"));if(company==""){plan.Reject("legal_case","missing_company_relation");continue;}
            var doc=Doc("legal_case",row);doc["companyId"]=company;doc["number"]=row.S("CaseNumber");doc["personName"]=row.S("PersonName");doc["personTypeCode"]=row["PersonType"]?.DeepClone();doc["employeeSourceId"]=row.S("EmployeeId");doc["riderId"]=Rider(row.S("EmployeeId")) is var legalRider&&legalRider!=""?legalRider:profileRiders.GetValueOrDefault(row.S("RiderProfileId"))??"";doc["riderProfileSourceId"]=row.S("RiderProfileId");doc["partyRoleCode"]=row["SponsorPartyRole"]?.DeepClone();doc["caseDate"]=Date(row,"CaseDate");doc["caseTime"]=row.S("CaseTime");doc["details"]=row.S("Details");doc["hearings"]=Array(Rows("HrLegalCaseHearings").Where(h=>h.S("LegalCaseId")==row.S("Id")));plan.Documents.Add(new("legal_case",doc));
        }
        PullContracts.AddToPlan(snapshot,plan,companies,riders,profileRiders,City);
        HsFactsSource.AddToPlan(snapshot,plan,companies);
        return plan;
    }
    static bool Dates(JsonObject row,string start,string end)=>row.S(end)==""||StringComparer.Ordinal.Compare(row.S(start),row.S(end))<=0;
    public static JsonObject Merge(JsonObject? existing,JsonObject proposed)
    {
        var merged=existing?.Clone()??new JsonObject();
        string[] certificateFields=["nameAr","nameEn","nameOnCertificateAr","unifiedNumber","commercialReg","crNumber","crType","entityType","characteristic","crVersion","issueDate","issueDateHijri","annualConfirmation","annualConfirmationHijri","city","address","capitalSar","activities","activitiesIncomplete"];
        foreach(var field in proposed)
        {
            if(existing?.S("registrySource")=="company-certificates"&&certificateFields.Contains(field.Key)&&existing.ContainsKey(field.Key))continue;
            merged[field.Key]=field.Value?.DeepClone();
        }
        if(existing?.B("debtOnly")==true&&proposed.S("erpSource")=="rider")merged["debtOnly"]=false;
        return merged;
    }
}
