using System.Data.Common;
using System.Text.Json.Nodes;
namespace ErpImport;

/// <summary>
/// One-kind, SELECT-only ERP reads for operations screens. Never writes. Uses the same ImportPlan projection as sync.
/// </summary>
public static class LiveOperationsSource
{
    static SourceTable Req(string schema,string name)
    {
        var match=SourceReader.Catalog.FirstOrDefault(t=>t.Schema==schema&&t.Name==name)
            ??PullContracts.Tables.FirstOrDefault(t=>t.Schema==schema&&t.Name==name)
            ??throw new ImportFailure("live_ops_unknown_table:"+schema+"."+name);
        return match with{Optional=false};
    }
    static SourceTable Opt(string schema,string name)
    {
        var match=SourceReader.Catalog.FirstOrDefault(t=>t.Schema==schema&&t.Name==name)
            ??PullContracts.Tables.FirstOrDefault(t=>t.Schema==schema&&t.Name==name)
            ??new SourceTable(schema,name,"Id",true);
        return match with{Optional=true};
    }
    static IEnumerable<SourceTable> Sponsors()=>[Req("app","Sponsors"),Opt("platform","CompanyProfile")];
    static IEnumerable<SourceTable> Cities()=>[Req("platform","GlobalCities"),Opt("app","OperatingCities")];
    static IEnumerable<SourceTable> Riders()=>[Req("app","Employees"),Opt("app","RiderProfiles")];
    static IEnumerable<SourceTable> Vehicles()=>[Req("app","Vehicles"),Opt("app","VehicleRegistrations"),Opt("app","VehicleInsurancePolicies")];
    static IEnumerable<SourceTable> Assignments()=>[Req("app","RiderVehicleAssignments"),Opt("app","RealRiders"),..Vehicles(),..Riders(),..Sponsors()];
    static IEnumerable<SourceTable> PlatformAccounts()=>[Req("app","PlatformRiderAccounts"),Req("platform","ClientPlatforms"),Opt("app","RiderClientAssignments"),Opt("app","PlatformAccountRegistrations"),..Riders(),..Sponsors(),..Cities()];

    public static IReadOnlyList<SourceTable> TablesFor(string kind)=>kind switch
    {
        "city"=>Cities().ToArray(),
        "housing"=>[Req("app","Housing"),Opt("app","HousingFloors"),Opt("app","HousingRooms"),Opt("app","HousingExternalOccupants"),Opt("app","HousingResidencePeriods"),..Riders(),..Sponsors(),..Cities()],
        "housing_residence"=>[Req("app","HousingResidencePeriods"),Req("app","HousingRooms"),Req("app","Housing"),..Riders(),..Sponsors(),..Cities()],
        "vehicle"=>[..Vehicles(),..Sponsors()],
        "vehicle_assignment"=>Assignments().ToArray(),
        "custody_handover"=>[Req("app","RiderVehicleAssignmentEvents"),..Assignments()],
        "sponsorship_period"=>[],
        "legal_case"=>[Req("app","HrLegalCases"),Opt("app","HrLegalCaseHearings"),..Riders(),..Sponsors()],
        "platform_account"=>PlatformAccounts().ToArray(),
        "vehicle_operational_status"=>[Req("app","VehicleOperationalStatusPeriods"),..Vehicles(),..Sponsors()],
        "platform_day_usage"=>[Req("app","PlatformAccountDayUsages"),..PlatformAccounts()],
        "rider_work_timeline"=>[Req("app","RiderClientAssignments"),Req("app","PlatformRiderAccounts"),Req("platform","ClientPlatforms"),Opt("app","RiderAssignmentEvents"),Opt("app","PlatformAccountDayUsages"),..Riders(),..Sponsors()],
        "custody_equipment"=>[Req("maintenance","RiderInventoryIssues"),Opt("maintenance","RiderInventoryIssueLines"),..Riders(),..Sponsors()],
        "vehicle_expense"=>[Req("maintenance","VehicleExpenses"),..Vehicles(),..Riders(),..Sponsors()],
        "chefz_account"=>[Req("chefz","AccountDetails"),..PlatformAccounts()],
        "chefz_account_type_change"=>[Req("chefz","AccountTypeChanges"),..PlatformAccounts()],
        "chefz_daily"=>[Req("chefz","DailyPerformance"),..PlatformAccounts()],
        "chefz_settlement"=>[Req("chefz","WalletSettlements"),..PlatformAccounts()],
        "chefz_order_row"=>[Req("chefz","ImportedOrderRows"),..PlatformAccounts()],
        "ninja_daily"=>[Req("ninja","NinjaDailyPerformance"),..PlatformAccounts()],
        "ninja_daily_deduction"=>[Req("ninja","NinjaDailyDeductions"),..PlatformAccounts()],
        "ninja_evaluation_snapshot"=>[Opt("ninja","EvaluationSnapshots")],
        "jahez_commission_policy"=>[Req("jahez_v2","CommissionPolicies"),Opt("jahez_v2","AccountSessions"),..PlatformAccounts()],
        "jahez_daily_order"=>[Req("jahez_v2","DailyOrders"),Opt("jahez_v2","AccountSessions"),..PlatformAccounts()],
        _=>throw new ImportFailure("live_ops_kind_unsupported:"+kind)
    };

    public static SourceSnapshot Read(DbConnection source,string kind,CancellationToken cancellationToken=default)
    {
        var catalog=TablesFor(kind);
        if(catalog.Count==0)return new([],DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"selected_empty","selected");
        var tables=SourceReader.ReadTables(source,catalog,cancellationToken);
        return new(tables,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"selected_tables_read","selected");
    }

    public static IReadOnlyList<JsonObject> Documents(SourceSnapshot snapshot,IReadOnlyList<ISyncSponsor> sponsors,string kind)
    {
        if(kind=="sponsorship_period")return [];
        var plan=ImportPlan.Build(snapshot,sponsors);
        return plan.Documents.Where(d=>d.Kind==kind).Select(d=>d.Body).ToList();
    }

    public static IReadOnlyList<JsonObject> ReadDocuments(DbConnection source,IReadOnlyList<ISyncSponsor> sponsors,string kind,CancellationToken cancellationToken=default)
        =>Documents(Read(source,kind,cancellationToken),sponsors,kind);

    /// <summary>Parent docs from the same snapshot for visibility without payroll copies.</summary>
    public static IReadOnlyDictionary<(string Kind,string Id),JsonObject> Index(SourceSnapshot snapshot,IReadOnlyList<ISyncSponsor> sponsors,params string[] kinds)
    {
        var plan=ImportPlan.Build(snapshot,sponsors);
        var map=new Dictionary<(string,string),JsonObject>();
        foreach(var doc in plan.Documents)
        {
            if(kinds.Length>0&&!kinds.Contains(doc.Kind))continue;
            map[(doc.Kind,doc.Body.S("id"))]=doc.Body;
        }
        return map;
    }
}
