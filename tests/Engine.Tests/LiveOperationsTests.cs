using ErpImport;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.Json.Nodes;

[TestClass]
public sealed class LiveOperationsTests
{
    static string Rider(int n)=>"2"+n.ToString("D9");
    static JsonObject SponsorRow(string id,string commercialReg)=>new()
    {
        ["Id"]=id,["CompanyProfileId"]="",["EmployerIdentityNumber"]="",["RegistryNameAr"]="Synthetic",["RegistryNameEn"]="Synthetic",
        ["CommercialRegistrationNumber"]=commercialReg,["UnifiedNationalNumber"]="",["SponsorType"]=1,["Status"]=1,
        ["ActiveFrom"]="2020-01-01",["ActiveTo"]="",["AddressCity"]="Riyadh",["IsDeleted"]=false
    };
    static JsonObject VehicleRow(string id,string sponsorId,string plate)=>new()
    {
        ["Id"]=id,["AssetNumber"]="A-"+id,["SerialNumber"]="",["PlateNumberAr"]=plate,["PlateNumberEn"]=plate,
        ["Vin"]="",["ChassisNumber"]="",["VehicleManufacturerId"]="",["VehicleModelId"]="",["ModelYear"]=2024,
        ["VehicleType"]=1,["FuelType"]=1,["OwnershipType"]=1,["AcquisitionDate"]="2024-01-01",["CurrentOdometer"]=0,
        ["CurrentOperationalStatus"]=1,["OperatingCityId"]="",["SponsorId"]=sponsorId,["RegisteredOwnerSponsorId"]=sponsorId,
        ["RegisteredOwnerSupplierId"]="",["RegistrationType"]=1,["CurrentAssignmentId"]="",["IsDeleted"]=false
    };
    [TestMethod]
    public void LiveSourceRejectsNonReadonlyCatalogOrUser()
    {
        Assert.AreEqual("unapproved_source",Assert.ThrowsExactly<ImportFailure>(()=>Connections.Source(new SqlConnectionStringBuilder
        {
            DataSource="db67927.public.databaseasp.net",InitialCatalog="db67927",UserID="db67927",Password="synthetic"
        }.ConnectionString)).Code);
        Assert.AreEqual("unapproved_source",Assert.ThrowsExactly<ImportFailure>(()=>Connections.Source(new SqlConnectionStringBuilder
        {
            DataSource="db67927.public.databaseasp.net",InitialCatalog="other",UserID="db67927_readonly",Password="synthetic"
        }.ConnectionString)).Code);
    }
    [TestMethod]
    public void VehicleKindCatalogNeverTouchesLedgerTables()
    {
        var names=LiveOperationsSource.TablesFor("vehicle").Select(t=>t.Name).ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(names.Contains("Vehicles"));
        Assert.IsFalse(names.Contains("AccountingEntries"));
        Assert.IsFalse(names.Contains("AuditEntries"));
        Assert.IsFalse(names.Contains("PlatformTransactions"));
    }
    [TestMethod]
    public void LiveProjectionKeepsOperationsFieldsAndDropsForeignCompany()
    {
        using var f=new PayFixture();
        string own=FinancialCore.Sponsors[0].Id,other=FinancialCore.Sponsors[1].Id;
        var actor=UserAccess.Principal(new JsonObject
        {
            ["username"]="synthetic-live-ops",["role"]="FinancialAccountant",
            ["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(own),["platforms"]=new JsonArray("Keeta"),["accountCodes"]=new JsonArray(),["categoryCodes"]=new JsonArray()}
        });
        f.Store.Write((c,tx)=>{Store.Save(c,"rider",new(){["id"]=Rider(1),["sponsorId"]=own},tx);return true;});
        var snapshot=new SourceSnapshot(new Dictionary<string,List<JsonObject>>
        {
            ["Sponsors"]=[SponsorRow("s-own",FinancialCore.Sponsors[0].CommercialReg),SponsorRow("s-other",FinancialCore.Sponsors[1].CommercialReg)],
            ["CompanyProfile"]=[],
            ["Vehicles"]=[VehicleRow("v-own","s-own","أ ب ج 1234"),VehicleRow("v-other","s-other","أ ب ج 9999")],
            ["VehicleRegistrations"]=[],
            ["VehicleInsurancePolicies"]=[]
        },DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"synthetic","selected");
        var rows=f.Store.Read(c=>Operations.ListLive(c,actor,"vehicle",snapshot).ToList());
        Assert.AreEqual(1,rows.Count);
        Assert.AreEqual("أ ب ج 1234",rows[0].S("plateNumberAr"));
        Assert.AreEqual(own,rows[0].S("companyId"));
        Assert.IsTrue(rows[0].ContainsKey("assetNumber"));
        Assert.IsFalse(rows[0].ToJsonString().Contains("s-other",StringComparison.Ordinal));
        Assert.AreEqual(other,FinancialCore.Sponsors[1].Id);
    }
    [TestMethod]
    public void MissingPayrollRiderIsHiddenFromScopedActor()
    {
        using var f=new PayFixture();
        string own=FinancialCore.Sponsors[0].Id;
        var actor=UserAccess.Principal(new JsonObject
        {
            ["username"]="synthetic-live-ops-rider",["role"]="FinancialAccountant",
            ["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(own),["platforms"]=new JsonArray("Keeta"),["accountCodes"]=new JsonArray(),["categoryCodes"]=new JsonArray()}
        });
        var snapshot=new SourceSnapshot(new Dictionary<string,List<JsonObject>>
        {
            ["Sponsors"]=[SponsorRow("s-own",FinancialCore.Sponsors[0].CommercialReg)],
            ["CompanyProfile"]=[],
            ["Employees"]=[new(){["Id"]="e1",["IqamaNo"]=Rider(9),["FullNameAr"]="Synthetic",["FullNameEn"]="",["Nationality"]="",["PrimaryPhone"]="",["Iban"]="",["IsEmployee"]=false,["EngagementType"]=1,["Status"]=3,["HireDate"]="2024-01-01",["SponsorId"]="s-own",["OperatingCityId"]="",["OperationalWorkTypeId"]="",["ContractStartDate"]="",["ContractEndDate"]="",["TerminationDate"]="",["ResidencyProfession"]="",["IsDeleted"]=false}],
            ["RiderProfiles"]=[new(){["Id"]="p1",["EmployeeId"]="e1",["TShirtSize"]="",["IsDeleted"]=false}],
            ["Housing"]=[new(){["Id"]="h1",["Code"]="H1",["NameAr"]="بيت",["NameEn"]="Home",["CityId"]="",["OpenedDate"]="2024-01-01",["ClosedDate"]="",["Status"]=1,["IsDeleted"]=false}],
            ["HousingFloors"]=[],["HousingRooms"]=[new(){["Id"]="r1",["HousingId"]="h1",["FloorId"]="",["Name"]="1",["Capacity"]=1,["CurrentOccupancy"]=0,["IsDeleted"]=false}],
            ["HousingExternalOccupants"]=[],
            ["HousingResidencePeriods"]=[new(){["Id"]="res1",["EmployeeId"]="e1",["RoomId"]="r1",["EffectiveFrom"]="2024-01-01",["EffectiveTo"]=""}],
            ["GlobalCities"]=[],["OperatingCities"]=[]
        },DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"synthetic","selected");
        Assert.AreEqual(0,f.Store.Read(c=>Operations.ListLive(c,actor,"housing_residence",snapshot).Count()));
        Assert.AreEqual(1,f.Store.Read(c=>Operations.ListLive(c,PayFixture.Maker,"housing_residence",snapshot).Count()));
    }
}
