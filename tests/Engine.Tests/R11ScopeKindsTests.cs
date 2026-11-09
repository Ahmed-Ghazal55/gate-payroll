using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ErpImport;
using Payroll;

[TestClass]
public sealed class R11ScopeKindsTests
{
    static readonly FinancialCore.SponsorDef[] Catalog=[new("synthetic-company","SYN","synthetic-employer","شركة مصطنعة","مصطنعة","synthetic-registration")];
    static string Identity(int n)=>"2"+n.ToString("D9");
    [TestMethod]
    public void NewDisplayKindsMapVehicleStatusDayUsageChefzJahezNinjaAndTimeline()
    {
        // Reuse the same synthetic SQLite shape as ErpImportTests via a local fixture clone.
        using var source=new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");source.Open();
        foreach(var table in SourceReader.Catalog)
        {
            using var cmd=source.CreateCommand();
            cmd.CommandText="CREATE TABLE ["+table.Name+"]("+string.Join(',',table.Fields.Split(' ').Select(f=>"["+f+"] "+(f is "IsDeleted" or "IsEmployee" or "IsRealRider" or "IsCurrent" or "Frozen"?"INTEGER":"TEXT")+(f=="Id"?" PRIMARY KEY":"")))+")";
            cmd.ExecuteNonQuery();
        }
        void Add(string table,JsonObject row)
        {
            using var cmd=source.CreateCommand();var entries=row.ToArray();cmd.CommandText="INSERT INTO ["+table+"]("+string.Join(',',entries.Select(p=>"["+p.Key+"]"))+") VALUES("+string.Join(',',entries.Select((_,i)=>"@p"+i))+")";
            for(int i=0;i<entries.Length;i++)cmd.Parameters.AddWithValue("@p"+i,entries[i].Value?.ToString()??(object)DBNull.Value);cmd.ExecuteNonQuery();
        }
        Add("Sponsors",new(){["Id"]="sponsor",["EmployerIdentityNumber"]="synthetic-employer",["RegistryNameAr"]="شركة مصطنعة",["CommercialRegistrationNumber"]="synthetic-registration",["Status"]="1"});
        Add("GlobalCities",new(){["Id"]="city",["NameAr"]="مدينة",["Code"]="SYN",["Status"]="1"});
        Add("OperatingCities",new(){["Id"]="operation-city",["GlobalCityId"]="city",["Status"]="1"});
        Add("Employees",new(){["Id"]="employee1",["IqamaNo"]=Identity(1),["IsEmployee"]="0",["EngagementType"]="1",["Status"]="3",["SponsorId"]="sponsor",["OperatingCityId"]="operation-city"});
        Add("Employees",new(){["Id"]="employee2",["IqamaNo"]=Identity(2),["IsEmployee"]="0",["EngagementType"]="1",["Status"]="3",["SponsorId"]="sponsor",["OperatingCityId"]="operation-city"});
        Add("RiderProfiles",new(){["Id"]="profile1",["EmployeeId"]="employee1"});Add("RiderProfiles",new(){["Id"]="profile2",["EmployeeId"]="employee2"});
        Add("Vehicles",new(){["Id"]="vehicle",["AssetNumber"]="SYN-ASSET",["SponsorId"]="sponsor",["OperatingCityId"]="operation-city"});
        Add("ClientPlatforms",new(){["Id"]="platform",["Code"]="ninja"});
        Add("PlatformRiderAccounts",new(){["Id"]="account",["ClientPlatformId"]="platform",["Code"]="ACC",["ExternalAccountId"]="CAP1",["RegisteredEmployeeId"]="employee1",["SponsorId"]="sponsor",["OperatingCityId"]="operation-city"});
        Add("RiderClientAssignments",new(){["Id"]="platform-assignment",["PlatformRiderAccountId"]="account",["RiderProfileId"]="profile2",["EffectiveFrom"]="2026-10-01",["EffectiveTo"]="2026-10-31",["PaymentModel"]="1"});
        Add("VehicleOperationalStatusPeriods",new(){["Id"]="status",["VehicleId"]="vehicle",["Status"]="3",["EffectiveFromUtc"]="2026-10-01T00:00:00Z",["EffectiveToUtc"]="2026-10-05T00:00:00Z",["Reason"]="idle"});
        Add("PlatformAccountDayUsages",new(){["Id"]="day",["PlatformRiderAccountId"]="account",["RiderClientAssignmentId"]="platform-assignment",["BusinessDate"]="2026-10-10",["OrdersCount"]="7",["WorkingHours"]="5.5",["CollectedAmount"]="40",["DeductionsAmount"]="2",["TotalValidOffers"]="10",["DeclinedOffers"]="1",["MissedOffers"]="2"});
        Add("RiderAssignmentEvents",new(){["Id"]="assign-event",["RiderClientAssignmentId"]="platform-assignment",["FromStatus"]="1",["ToStatus"]="4",["OccurredAtUtc"]="2026-10-31T00:00:00Z",["Reason"]="handed_over"});
        Add("AccountTypeChanges",new(){["Id"]="type-change",["PlatformRiderAccountId"]="account",["PreviousType"]="1",["AccountType"]="2",["EffectiveFrom"]="2026-10-01",["CreatedAtUtc"]="2026-10-01T00:00:00Z"});
        Add("ImportedOrderRows",new(){["Id"]="order-row",["PlatformRiderAccountId"]="account",["RiderClientAssignmentId"]="platform-assignment",["ImportId"]="import",["BusinessDate"]="2026-10-12",["OrderId"]="SYN-ORDER",["SourceRowNumber"]="2",["UsageSequence"]="1",["DeliveryFees"]="12",["CashCollected"]="3",["Tips"]="1",["Compensation"]="0",["Bonuses"]="0",["Deductions"]="0"});
        Add("EvaluationSnapshots",new(){["Id"]="eval",["Frozen"]=1,["InputRevision"]="r1",["PolicyVersion"]="p1",["Reason"]="month freeze",["ResultJson"]="{}",["QueryJson"]="{}",["AccountIdsJson"]="[]",["CreatedAtUtc"]="2026-10-31T00:00:00Z"});
        Add("AccountSessions",new(){["Id"]="session",["PlatformRiderAccountId"]="account",["RiderClientAssignmentId"]="platform-assignment",["RiderProfileId"]="profile2",["StartedAtUtc"]="2026-10-01T00:00:00Z"});
        Add("CommissionPolicies",new(){["Id"]="policy",["HandoverId"]="session",["ApprovalRequestId"]="apr",["FromDate"]="2026-10-01",["ToDate"]="2026-10-31",["Rate"]="0.15"});
        Add("DailyOrders",new(){["Id"]="daily-order",["HandoverId"]="session",["BatchId"]="batch",["ImportRowId"]="row",["Date"]="2026-10-10",["Count"]="4"});
        var plan=ImportPlan.Build(SourceReader.Read(source),Catalog);
        Assert.AreEqual(3m,plan.Documents.Single(d=>d.Kind=="vehicle_operational_status").Body.N("statusCode"));
        Assert.AreEqual(7m,plan.Documents.Single(d=>d.Kind=="platform_day_usage").Body.N("orders"));
        Assert.AreEqual(2m,plan.Documents.Single(d=>d.Kind=="chefz_account_type_change").Body.N("accountType"));
        Assert.AreEqual(Identity(2),plan.Documents.Single(d=>d.Kind=="chefz_order_row").Body.S("riderId"));
        Assert.IsTrue(plan.Documents.Single(d=>d.Kind=="ninja_evaluation_snapshot").Body.B("frozen"));
        Assert.AreEqual(0.15m,plan.Documents.Single(d=>d.Kind=="jahez_commission_policy").Body.N("rate"));
        Assert.AreEqual(4m,plan.Documents.Single(d=>d.Kind=="jahez_daily_order").Body.N("count"));
        var timeline=plan.Documents.Single(d=>d.Kind=="rider_work_timeline").Body;
        Assert.AreEqual(Identity(2),timeline.S("riderId"));Assert.AreEqual("ACC",timeline.S("accountCode"));Assert.AreEqual("CAP1",timeline.S("externalAccountId"));
        Assert.AreEqual(4m,timeline.N("endReasonCode"));Assert.AreEqual(1,timeline["days"]!.AsArray().Count);
        Assert.IsNull(timeline["cancelledOrders"]);Assert.IsNull(timeline["eligibility"]);
        Assert.IsTrue(Operations.Kinds.Contains("rider_work_timeline"));
    }
}
