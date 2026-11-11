namespace ErpImport;

public enum SourceColumnKind{RowVersion,Id,CreatedAt,DatasetVersion,OpenPeriod}

public sealed record SourceReadProfile(string Schema,string Name,SourceColumnKind ColumnKind,string? Module=null,string? PeriodColumn=null,bool HardDeleteReconcile=false)
{
    public string Key=>Schema+"."+Name;
}

/// <summary>How each ERP allowlisted table is read incrementally (CR-004 / R11 T2).</summary>
public static class SourceReadProfiles
{
    // Modules match app.DatasetVersions.ModuleKey values used in ERP.
    public static readonly SourceReadProfile[] All=
    [
        P("app","Sponsors",SourceColumnKind.RowVersion,"workforce"),
        P("platform","CompanyProfile",SourceColumnKind.RowVersion,"workforce"),
        P("platform","GlobalCities",SourceColumnKind.RowVersion,"workforce"),
        P("app","OperatingCities",SourceColumnKind.RowVersion,"workforce"),
        P("app","Employees",SourceColumnKind.RowVersion,"workforce"),
        P("app","RiderProfiles",SourceColumnKind.RowVersion,"workforce"),
        P("app","Housing",SourceColumnKind.RowVersion,"housing"),
        P("app","HousingFloors",SourceColumnKind.RowVersion,"housing"),
        P("app","HousingRooms",SourceColumnKind.RowVersion,"housing"),
        P("app","HousingExternalOccupants",SourceColumnKind.RowVersion,"housing"),
        P("app","HousingResidencePeriods",SourceColumnKind.CreatedAt,"housing",period:"EffectiveFrom",reconcile:true),
        P("app","Vehicles",SourceColumnKind.RowVersion,"fleet"),
        P("app","VehicleRegistrations",SourceColumnKind.RowVersion,"fleet"),
        P("app","VehicleInsurancePolicies",SourceColumnKind.RowVersion,"fleet"),
        P("app","RiderVehicleAssignments",SourceColumnKind.RowVersion,"fleet"),
        P("app","RealRiders",SourceColumnKind.CreatedAt,"fleet"),
        P("app","RiderVehicleAssignmentEvents",SourceColumnKind.CreatedAt,"fleet"),
        P("app","VehicleOperationalStatusPeriods",SourceColumnKind.RowVersion,"fleet"),
        P("platform","ClientPlatforms",SourceColumnKind.RowVersion,"platform-operations"),
        P("app","PlatformRiderAccounts",SourceColumnKind.RowVersion,"platform-operations"),
        P("app","RiderClientAssignments",SourceColumnKind.RowVersion,"platform-operations"),
        P("app","RiderAssignmentEvents",SourceColumnKind.CreatedAt,"platform-operations"),
        P("app","PlatformAccountDayUsages",SourceColumnKind.OpenPeriod,"platform-operations",period:"BusinessDate",reconcile:true),
        P("app","PlatformAccountRegistrations",SourceColumnKind.RowVersion,"platform-operations"),
        P("app","HrLegalCases",SourceColumnKind.RowVersion,"hr"),
        P("app","HrLegalCaseHearings",SourceColumnKind.RowVersion,"hr"),
        P("app","VehicleOdometerReadings",SourceColumnKind.CreatedAt,"fleet"),
        P("app","FuelCards",SourceColumnKind.RowVersion,"fuel"),
        P("app","FuelCardRiderAssignments",SourceColumnKind.RowVersion,"fuel"),
        P("app","FuelCardMonthlyUsages",SourceColumnKind.RowVersion,"fuel"),
        P("maintenance","RiderInventoryIssues",SourceColumnKind.RowVersion,"custody"),
        P("maintenance","RiderInventoryIssueLines",SourceColumnKind.RowVersion,"custody"),
        P("app","RiderPhoneSimAssignments",SourceColumnKind.RowVersion,"custody"),
        P("app","VehicleAccidents",SourceColumnKind.RowVersion,"fleet"),
        P("app","VehicleAccidentCases",SourceColumnKind.RowVersion,"fleet"),
        P("app","VehicleAccidentInstallments",SourceColumnKind.RowVersion,"fleet"),
        P("maintenance","VehicleExpenses",SourceColumnKind.RowVersion,"fleet"),
        P("chefz","AccountDetails",SourceColumnKind.RowVersion,"chefz"),
        P("chefz","AccountTypeChanges",SourceColumnKind.CreatedAt,"chefz"),
        P("chefz","DailyPerformance",SourceColumnKind.OpenPeriod,"chefz",period:"ReportDate",reconcile:true),
        P("chefz","WalletSettlements",SourceColumnKind.OpenPeriod,"chefz",period:"PeriodFrom",reconcile:true),
        P("chefz","ImportedOrderRows",SourceColumnKind.OpenPeriod,"chefz",period:"BusinessDate",reconcile:true),
        P("jahez_v2","AccountSessions",SourceColumnKind.RowVersion,"jahez"),
        P("jahez_v2","EarningsStatements",SourceColumnKind.RowVersion,"jahez"),
        P("jahez_v2","PlatformTransactions",SourceColumnKind.OpenPeriod,"jahez",period:"OccurredAtUtc",reconcile:true),
        P("jahez_v2","AccountingEntries",SourceColumnKind.CreatedAt,"jahez"),
        P("jahez_v2","AccountFees",SourceColumnKind.RowVersion,"jahez"),
        P("jahez_v2","AccountFeeRefunds",SourceColumnKind.CreatedAt,"jahez"),
        P("jahez_v2","TransferredDebts",SourceColumnKind.RowVersion,"jahez"),
        P("jahez_v2","Operations",SourceColumnKind.RowVersion,"jahez"),
        P("jahez_v2","CommissionPolicies",SourceColumnKind.CreatedAt,"jahez"),
        P("jahez_v2","DailyOrders",SourceColumnKind.CreatedAt,"jahez"),
        P("ninja","NinjaAccountDetails",SourceColumnKind.RowVersion,"ninja"),
        P("ninja","NinjaDailyPerformance",SourceColumnKind.OpenPeriod,"ninja",period:"ReportDate",reconcile:true),
        P("ninja","NinjaDailyDeductions",SourceColumnKind.OpenPeriod,"ninja",period:"ReportDate",reconcile:true),
        P("ninja","EvaluationSnapshots",SourceColumnKind.CreatedAt,"ninja"),
        P("app","EmployeeDocuments",SourceColumnKind.RowVersion,"documents"),
        P("app","EmployeeDriverLicenses",SourceColumnKind.RowVersion,"documents"),
        P("jahez_v2","Requests",SourceColumnKind.RowVersion,"jahez"),
        P("jahez_v2","MoneyMovements",SourceColumnKind.CreatedAt,"jahez"),
        P("jahez_v3","TreasurySubAccounts",SourceColumnKind.RowVersion,"jahez"),
        P("jahez_v3","Postings",SourceColumnKind.CreatedAt,"jahez"),
        P("jahez_v2","CashboxHandovers",SourceColumnKind.RowVersion,"jahez"),
        P("jahez_v2","CashboxHandoverMovements",SourceColumnKind.CreatedAt,"jahez"),
        P("platform","DocumentTypes",SourceColumnKind.RowVersion,"documents"),
    ];
    static SourceReadProfile P(string schema,string name,SourceColumnKind kind,string? module=null,string? period=null,bool reconcile=false)
        =>new(schema,name,kind,module,period,reconcile);
    public static SourceReadProfile? For(string schema,string name)=>All.FirstOrDefault(p=>p.Schema.Equals(schema,StringComparison.OrdinalIgnoreCase)&&p.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
    public static SourceReadProfile? ForTable(string name)=>All.FirstOrDefault(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
    /// <summary>Calendar months that hard-delete tables must re-read (current + previous).</summary>
    public static (DateTime From,DateTime ToExclusive) OpenPeriodWindow(DateTimeOffset utcNow)
    {
        var local=TimeZoneInfo.ConvertTime(utcNow,TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows()?"Arab Standard Time":"Asia/Riyadh"));
        var month=new DateTime(local.Year,local.Month,1,0,0,0,DateTimeKind.Unspecified);
        return (month.AddMonths(-1),month.AddMonths(1));
    }
}
