using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ErpImport;
using Payroll;

/// <summary>
/// Live-read projection rules for ImportPlan and HungerStationFacts (no ImportExecutor / sync commit).
/// Restored after S1 removed ErpImportTests and HungerStationFactTests.
/// </summary>
[TestClass]
public sealed class ImportPlanTests
{
    static readonly FinancialCore.SponsorDef[] Catalog=[new("synthetic-company","SYN","synthetic-employer","شركة مصطنعة","مصطنعة","synthetic-registration")];
    static string Identity(int n)=>"2"+n.ToString("D9");

    sealed class Fixture:IDisposable
    {
        public SqliteConnection Source { get; }=new("Data Source=:memory:");
        public Fixture()
        {
            Source.Open();
            foreach(var table in SourceReader.Catalog)
            {
                using var cmd=Source.CreateCommand();
                cmd.CommandText="CREATE TABLE ["+table.Name+"]("+string.Join(',',table.Fields.Split(' ').Select(f=>"["+f+"] "+(f is "IsDeleted" or "IsEmployee" or "IsRealRider" or "IsCurrent" or "Frozen" or "Status" or "EngagementType" or "EventType" or "PaymentModel"?"INTEGER":"TEXT")+(f=="Id"?" PRIMARY KEY":"")))+")";
                cmd.ExecuteNonQuery();
            }
            Add("Sponsors",new(){["Id"]="sponsor",["EmployerIdentityNumber"]="synthetic-employer",["RegistryNameAr"]="شركة مصطنعة",["CommercialRegistrationNumber"]="synthetic-registration",["Status"]=1});
            Add("GlobalCities",new(){["Id"]="city",["NameAr"]="مدينة مصطنعة",["Code"]="SYN",["Status"]=1});
            Add("OperatingCities",new(){["Id"]="operation-city",["GlobalCityId"]="city",["Status"]=1});
            for(int i=1;i<=3;i++)
            {
                Add("Employees",new(){["Id"]="employee"+i,["IqamaNo"]=Identity(i),["FullNameAr"]="DO_NOT_EMIT_PERSON_"+i,["PrimaryPhone"]="DO_NOT_EMIT_PHONE",["Iban"]="SA0380000000608010167519",["IsEmployee"]=i==3?1:0,["EngagementType"]=i==2?2:1,["Status"]=3,["SponsorId"]="sponsor",["OperatingCityId"]="operation-city"});
                if(i<3)Add("RiderProfiles",new(){["Id"]="profile"+i,["EmployeeId"]="employee"+i});
            }
            Add("Housing",new(){["Id"]="housing",["CityId"]="city",["NameAr"]="سكن مصطنع"});
            Add("HousingFloors",new(){["Id"]="floor",["HousingId"]="housing",["Name"]="Synthetic Floor"});
            Add("HousingRooms",new(){["Id"]="room",["HousingId"]="housing",["FloorId"]="floor",["Capacity"]="2"});
            Add("HousingResidencePeriods",new(){["Id"]="residence",["RoomId"]="room",["EmployeeId"]="employee2",["EffectiveFrom"]="2026-10-01"});
            Add("Vehicles",new(){["Id"]="vehicle",["AssetNumber"]="SYN-ASSET",["SponsorId"]="sponsor",["OperatingCityId"]="operation-city",["CurrentAssignmentId"]="assignment"});
            Add("VehicleRegistrations",new(){["Id"]="registration",["VehicleId"]="vehicle",["RegistrationNumber"]="SYN-REG"});
            Add("RiderVehicleAssignments",new(){["Id"]="assignment",["RiderProfileId"]="profile2",["VehicleId"]="vehicle",["StartedAtUtc"]="2026-10-01T00:00:00Z",["EndedAtUtc"]="2026-10-31T00:00:00Z",["IsRealRider"]=0});
            Add("RealRiders",new(){["Id"]="real-rider",["RiderVehicleAssignmentId"]="assignment",["Name"]="DO_NOT_EMIT_ALTERNATE",["IqamaNo"]=Identity(9)});
            foreach(int code in new[]{1,2,5})Add("RiderVehicleAssignmentEvents",new(){["Id"]="event"+code,["RiderVehicleAssignmentId"]="assignment",["EventType"]=code,["OccurredAtUtc"]="2026-10-03T00:00:00Z"});
            Add("ClientPlatforms",new(){["Id"]="platform",["Code"]="SYN-PLATFORM"});
            Add("PlatformRiderAccounts",new(){["Id"]="account",["ClientPlatformId"]="platform",["RegisteredEmployeeId"]="employee1",["SponsorId"]="sponsor",["OperatingCityId"]="operation-city"});
            Add("RiderClientAssignments",new(){["Id"]="platform-assignment",["PlatformRiderAccountId"]="account",["RiderProfileId"]="profile2",["EffectiveFrom"]="2026-10-01",["PaymentModel"]=1});
            Add("HrLegalCases",new(){["Id"]="case",["SponsorId"]="sponsor",["EmployeeId"]="employee2",["CaseNumber"]="SYN-CASE",["Details"]="DO_NOT_EMIT_CASE",["CaseDate"]="2026-11-03"});
            Add("HrLegalCaseHearings",new(){["Id"]="hearing",["LegalCaseId"]="case",["HearingDate"]="2026-11-04"});
        }
        public void Add(string table,JsonObject row)
        {
            using var cmd=Source.CreateCommand();var entries=row.ToArray();cmd.CommandText="INSERT INTO ["+table+"]("+string.Join(',',entries.Select(p=>"["+p.Key+"]"))+") VALUES("+string.Join(',',entries.Select((_,i)=>"@p"+i))+")";
            for(int i=0;i<entries.Length;i++)cmd.Parameters.AddWithValue("@p"+i,entries[i].Value?.ToString()??(object)DBNull.Value);cmd.ExecuteNonQuery();
        }
        public void Dispose(){Source.Dispose();SqliteConnection.ClearAllPools();}
    }

    static SourceSnapshot HsSnapshot()=>new(new(),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"synthetic_sqlite");
    static JsonObject HsEvent(string id,string type="RiderAdvance",decimal amount=100)=>new(){["SourceEventId"]=id,["IdempotencyKey"]="synthetic-key-"+id,["EventType"]=type,["SponsorId"]="synthetic-sponsor",["Amount"]=amount,["Currency"]="SAR",["ApprovedAt"]="2026-11-01T00:00:00Z",["ApprovedBy"]="synthetic-checker",["ReversesEventId"]=""};
    static ImportPlan HsPlan(SourceSnapshot source){var plan=new ImportPlan();HsFactsSource.AddToPlan(source,plan,new(){["synthetic-sponsor"]="synthetic-company"});return plan;}

    [TestMethod]
    public void InvalidIdentityAndDuplicateIdentityAreRejected()
    {
        using var f=new Fixture();
        var source=SourceReader.Read(f.Source);
        source.Tables["Employees"][0]["IqamaNo"]="invalid";
        var invalid=ImportPlan.Build(source,Catalog);
        Assert.AreEqual(1,invalid.Rejected["rider"]["invalid_identity"]);

        source.Tables["Employees"][0]["IqamaNo"]=Identity(1);
        source.Tables["Employees"][1]["IqamaNo"]=Identity(1);
        var duplicates=ImportPlan.Build(source,Catalog);
        Assert.AreEqual(2,duplicates.Rejected["rider"]["duplicate_identity"]);
    }

    [TestMethod]
    public void MissingCommercialRegistrationIsRejectedWithoutInventingADocument()
    {
        using var f=new Fixture();
        var source=SourceReader.Read(f.Source);
        source.Tables["Sponsors"][0]["CommercialRegistrationNumber"]="";
        source.Tables["Sponsors"][0]["ActiveFrom"]="2026-01-01";
        var missing=ImportPlan.Build(source,Catalog);
        Assert.AreEqual(1,missing.Rejected["commercial_registration"]["missing_registration_number"]);
        Assert.IsFalse(missing.Documents.Any(d=>d.Kind=="commercial_registration"));
    }

    [TestMethod]
    public void NinjaKeepsCaptainIdAndRejectsMissingCaptainId()
    {
        using var f=new Fixture();
        using(var cmd=f.Source.CreateCommand()){cmd.CommandText="UPDATE PlatformRiderAccounts SET ExternalAccountId='SYN-CAPTAIN'";cmd.ExecuteNonQuery();}
        f.Add("NinjaAccountDetails",new(){["Id"]="ninja-details",["PlatformRiderAccountId"]="account"});
        f.Add("NinjaDailyPerformance",new(){["Id"]="ninja-day",["PlatformRiderAccountId"]="account",["RiderClientAssignmentId"]="platform-assignment",["RiderProfileId"]="profile2",["CaptainId"]="SYN-CAPTAIN",["ReportDate"]="2026-10-01",["TotalOrders"]=10,["TotalWorkingHours"]=8.5m});
        f.Add("NinjaDailyDeductions",new(){["Id"]="ninja-deduction",["PlatformRiderAccountId"]="account",["RiderClientAssignmentId"]="platform-assignment",["RiderProfileId"]="profile2",["CaptainId"]="SYN-CAPTAIN",["ReportDate"]="2026-10-01",["TotalDeductions"]=5,["TotalCollectedAmountAtDelivery"]=20});
        var source=SourceReader.Read(f.Source);var plan=ImportPlan.Build(source,Catalog);
        var docs=plan.Documents.Where(d=>d.Kind.StartsWith("ninja_")).ToList();
        Assert.AreEqual(3,docs.Count);
        Assert.IsTrue(docs.All(x=>x.Body.S("CaptainId")=="SYN-CAPTAIN"));
        Assert.AreEqual(Identity(2),docs.Single(x=>x.Kind=="ninja_daily").Body.S("riderId"));
        source.Tables["NinjaDailyPerformance"][0]["CaptainId"]="";
        Assert.AreEqual(1,ImportPlan.Build(source,Catalog).Rejected["ninja_daily"]["missing_captain_id"]);
    }

    [TestMethod]
    public void JahezFactsRequireSessionLink()
    {
        using var f=new Fixture();
        f.Add("AccountSessions",new(){["Id"]="session",["PlatformRiderAccountId"]="account",["RiderClientAssignmentId"]="platform-assignment",["RiderProfileId"]="profile2",["StartedAtUtc"]="2026-10-01T00:00:00Z"});
        f.Add("EarningsStatements",new(){["Id"]="earnings",["HandoverId"]="session",["FromDate"]="2026-10-01",["ToDate"]="2026-10-31",["TotalDeliveryPrice"]=100});
        f.Add("PlatformTransactions",new(){["Id"]="txn",["AccountId"]="account",["HandoverId"]="session",["NetAmount"]=90,["ImportRowId"]="lineage-only"});
        f.Add("AccountingEntries",new(){["Id"]="ledger",["AccountId"]="account",["HandoverId"]="session",["Amount"]=-10,["ReversesEntryId"]="original"});
        f.Add("AccountFees",new(){["Id"]="fee",["HandoverId"]="session",["Amount"]=20,["WaivedAmount"]=5});
        f.Add("AccountFeeRefunds",new(){["Id"]="refund",["HandoverId"]="session",["AccountFeeId"]="fee",["RefundAmount"]=15});
        f.Add("Operations",new(){["Id"]="operation",["HandoverId"]="session"});
        f.Add("TransferredDebts",new(){["Id"]="transferred",["OperationId"]="operation",["RiderProfileId"]="profile2",["Amount"]=100,["PaidAmount"]=25});
        var source=SourceReader.Read(f.Source);var plan=ImportPlan.Build(source,Catalog);
        var docs=plan.Documents.Where(x=>x.Kind.StartsWith("jahez_")).ToList();
        Assert.AreEqual(7,docs.Count);
        Assert.IsTrue(docs.All(x=>x.Body.S("riderId")==Identity(2)));
        Assert.AreEqual("original",docs.Single(x=>x.Kind=="jahez_ledger_entry").Body.S("reversesSourceId"));
        source.Tables["PlatformTransactions"][0]["HandoverId"]="missing";
        Assert.AreEqual(1,ImportPlan.Build(source,Catalog).Rejected["jahez_transaction"]["missing_session_relation"]);
    }

    [TestMethod]
    public void HungerStationApprovalFilterRejectsUnapprovedAndUnknownCompany()
    {
        var source=HsSnapshot();
        var pending=HsEvent("synthetic-pending");pending.Remove("ApprovedBy");
        var unknown=HsEvent("synthetic-unknown");unknown["SponsorId"]="unknown";
        source.Tables[HsFactsSource.Table("hs_wallet_event")]=[pending,unknown];
        source.Tables[HsFactsSource.Table("hs_month_close")]=[new(){["CloseSnapshotId"]="synthetic-close",["C"]=100,["A"]=-40,["M"]=-80}];
        var plan=HsPlan(source);
        Assert.AreEqual(0,plan.Documents.Count);
        Assert.AreEqual(1,plan.Rejected["hs_wallet_event"]["approval_not_available"]);
        Assert.AreEqual(1,plan.Rejected["hs_wallet_event"]["company_relation_unknown"]);
        Assert.AreEqual(1,plan.Rejected["hs_month_close"]["approval_not_available"]);
    }

    [TestMethod]
    public void D20UsesKnownCmaAndNeverTreatsTheRawBalanceAsDebt()
    {
        var known=HungerStationFacts.MonthClose(new(){["C"]=100,["A"]=-40,["M"]=-80});
        Assert.AreEqual(-100m,known.N("expected"));
        Assert.AreEqual(60m,known.N("remainingOldCarry"));
        Assert.AreEqual(-140m,known.N("currentPeriodBalance"));
        Assert.IsFalse(known.B("postingAllowed"));
        foreach(string missing in new[]{"C","A","M"})
        {
            var raw=new JsonObject{["C"]=100,["A"]=-40,["M"]=-80};raw.Remove(missing);
            var result=HungerStationFacts.MonthClose(raw);
            Assert.AreEqual("unknown",result.S("status"));
            Assert.IsFalse(result.ContainsKey("currentPeriodBalance"));
            Assert.IsFalse(result.B("postingAllowed"));
        }
        foreach(var mapping in HungerStationFacts.AccountMapping)
        {
            var proposal=HungerStationFacts.Proposal("hs_wallet_event",new(){["status"]="approved",["sourceFacts"]=new JsonObject{["EventType"]=mapping.Key,["Amount"]=100,["C"]=100,["A"]=-40,["M"]=-80}});
            Assert.AreEqual(mapping.Value,proposal.S("accountCode"));
            Assert.IsFalse(proposal.B("postingAllowed"));
        }
        var blocked=HungerStationFacts.Proposal("hs_wallet_event",new(){["sourceFacts"]=new JsonObject{["EventType"]="NegativeRiderNet",["Amount"]=-80}});
        Assert.AreEqual("",blocked.S("accountCode"));
        StringAssert.Contains(blocked.ToJsonString(),"cma_unknown");
    }
}
