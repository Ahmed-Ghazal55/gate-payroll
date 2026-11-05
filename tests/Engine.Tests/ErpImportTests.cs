using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ErpImport;
using Payroll;

[TestClass]
public sealed class ErpImportTests
{
    static readonly FinancialCore.SponsorDef[] Catalog=[new("synthetic-company","SYN","synthetic-employer","شركة مصطنعة","مصطنعة","synthetic-registration")];
    static string Identity(int n)=>"2"+n.ToString("D9");
    sealed class Fixture:IDisposable
    {
        public string DirectoryPath { get; }=Path.Combine(Path.GetTempPath(),"gate-erp-import-"+Guid.NewGuid().ToString("N"));
        public SqliteConnection Source { get; }=new("Data Source=:memory:");
        public Fixture()
        {
            Directory.CreateDirectory(DirectoryPath);Source.Open();
            foreach(var table in SourceReader.Catalog)
            {
                using var cmd=Source.CreateCommand();cmd.CommandText="CREATE TABLE ["+table.Name+"]("+string.Join(',',table.Fields.Split(' ').Select(f=>"["+f+"] "+(f is "IsDeleted" or "IsEmployee" or "IsRealRider" or "IsCurrent" or "Status" or "EngagementType" or "EventType" or "PaymentModel"?"INTEGER":"TEXT")+(f=="Id"?" PRIMARY KEY":"")))+")";cmd.ExecuteNonQuery();
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
        public void Dispose(){Source.Dispose();SqliteConnection.ClearAllPools();Directory.Delete(DirectoryPath,true);}
    }
    [TestMethod]
    public void DryRunReadsSyntheticSqliteOnceAndDoesNotInitializeTarget()
    {
        using var f=new Fixture();using var target=new SqliteConnection("Data Source=:memory:");target.Open();
        var snapshot=SourceReader.Read(f.Source);var plan=ImportPlan.Build(snapshot,Catalog);var summary=ImportExecutor.Run(snapshot,plan,ImportExecutor.Target(target));
        Assert.AreEqual(2,summary.Counts.Single(c=>c.Kind=="rider").New);
        Assert.AreEqual(1,summary.Observations["external_riders"]);Assert.AreEqual(1,summary.Observations["sponsored_riders"]);
        Assert.AreEqual(1,summary.Rejected["rider"]["employee_without_rider_profile"]);
        using var cmd=target.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table'";Assert.AreEqual(0L,cmd.ExecuteScalar());
        string output=JsonSerializer.Serialize(summary);Assert.DoesNotContain("DO_NOT_EMIT",output);Assert.DoesNotContain(Identity(1),output);Assert.DoesNotContain("SA0380000000608010167519",output);
        Assert.AreEqual("source_table_absent",summary.Unavailable["sponsorship_period"]);
    }
    [TestMethod]
    public void CommitIsIdempotentAuditedAndPreservesExistingPolicyFields()
    {
        using var f=new Fixture();var target=new Store(f.DirectoryPath);var source=SourceReader.Read(f.Source);var plan=ImportPlan.Build(source,Catalog);
        target.Write((c,tx)=>{Store.Save(c,"rider",new(){["id"]=Identity(1),["policyId"]="synthetic-approved-policy",["bankVerified"]=true},tx);return true;});
        var first=ImportExecutor.Run(source,plan,target.Read(ImportExecutor.Target),target);Assert.IsTrue(first.Counts.Sum(c=>c.New)>0);
        int audits=target.Read(c=>Store.List(c,"audit").Count);
        var second=ImportExecutor.Run(source,plan,target.Read(ImportExecutor.Target),target);Assert.AreEqual(0,second.Counts.Sum(c=>c.New+c.Changed));Assert.AreEqual(audits,target.Read(c=>Store.List(c,"audit").Count));
        Assert.AreEqual("synthetic-approved-policy",target.Read(c=>Store.Get(c,Identity(1),"rider")!.S("policyId")));
        Assert.IsTrue(target.Read(c=>Store.Get(c,Identity(1),"rider")!.B("bankVerified")));
        Assert.IsTrue(target.Read(c=>Store.List(c,"audit").All(a=>a.S("actor")=="erp-import")));
        source.Tables["Employees"][0]["FullNameAr"]="DO_NOT_EMIT_UPDATED";
        var changed=ImportExecutor.Run(source,ImportPlan.Build(source,Catalog),target.Read(ImportExecutor.Target),target);Assert.AreEqual(1,changed.Counts.Single(c=>c.Kind=="rider").Changed);
    }
    [TestMethod]
    public void RelationsPreserveOwnerVersusOperatorRoomsVehiclesAndOctoberCustody()
    {
        using var f=new Fixture();var plan=ImportPlan.Build(SourceReader.Read(f.Source),Catalog);
        JsonObject Doc(string kind)=>plan.Documents.Single(d=>d.Kind==kind).Body;
        var account=Doc("platform_account");Assert.AreEqual(Identity(1),account.S("registeredRiderId"));Assert.AreEqual(Identity(2),account["assignments"]![0]!.S("riderId"));
        Assert.AreEqual("erp:city:city",Doc("housing").S("cityId"));Assert.AreEqual("erp:housing:housing",Doc("housing_residence").S("housingId"));
        Assert.AreEqual("erp:vehicle:vehicle",Doc("vehicle_assignment").S("vehicleId"));Assert.IsFalse(Doc("vehicle_assignment").B("isRealRider"));
        Assert.AreEqual(2,plan.Documents.Count(d=>d.Kind=="custody_handover"));Assert.AreEqual(1,plan.Observations["october_custody_handover"]);Assert.AreEqual(1,plan.Observations["october_custody_return"]);
        Assert.AreEqual(1,Doc("legal_case")["hearings"]!.AsArray().Count);Assert.AreEqual("synthetic-company",Doc("company").S("id"));
        Assert.IsFalse(SourceReader.Catalog.Any(t=>t.Fields.Contains("Password",StringComparison.OrdinalIgnoreCase)||t.Fields.Contains("UserName",StringComparison.OrdinalIgnoreCase)));
    }
    [TestMethod]
    public void RejectionsAndTransactionRollbackNeverLeavePartialRows()
    {
        using var f=new Fixture();var source=SourceReader.Read(f.Source);source.Tables["Employees"][0]["IqamaNo"]="invalid";source.Tables["HousingResidencePeriods"][0]["EffectiveTo"]="2026-09-01";
        var plan=ImportPlan.Build(source,Catalog);Assert.AreEqual(1,plan.Rejected["rider"]["invalid_identity"]);Assert.AreEqual(1,plan.Rejected["housing_residence"]["invalid_date_range"]);
        var target=new Store(f.DirectoryPath);plan.Documents.Add(new("rider",new JsonObject()));
        Assert.ThrowsExactly<ApiError>(()=>ImportExecutor.Run(source,plan,target.Read(ImportExecutor.Target),target));
        Assert.AreEqual(0,target.Read(c=>Store.List(c,"company").Count));Assert.AreEqual(0,target.Read(c=>Store.List(c,"audit").Count));
    }
    [TestMethod]
    public void IdentityChangesAndUnapprovedConnectionsStopBeforeWrites()
    {
        using var f=new Fixture();var source=SourceReader.Read(f.Source);var target=new Store(f.DirectoryPath);var plan=ImportPlan.Build(source,Catalog);
        ImportExecutor.Run(source,plan,target.Read(ImportExecutor.Target),target);
        source.Tables["Employees"][0]["IqamaNo"]=Identity(8);
        Assert.AreEqual("source_identity_changed_requires_review",Assert.ThrowsExactly<ImportFailure>(()=>ImportExecutor.Run(source,ImportPlan.Build(source,Catalog),target.Read(ImportExecutor.Target),target)).Code);
        foreach(string endpoint in new[]{"production.invalid","localhost.attacker.invalid"})Assert.ThrowsExactly<ImportFailure>(()=>Connections.Source(new Microsoft.Data.SqlClient.SqlConnectionStringBuilder{DataSource=endpoint,InitialCatalog="erp_copy"}.ConnectionString));
        using var local=Connections.Source(new Microsoft.Data.SqlClient.SqlConnectionStringBuilder{DataSource="localhost,1433",InitialCatalog="erp_copy"}.ConnectionString);Assert.AreEqual("erp_copy",local.Database);
    }
    [TestMethod]
    public void MissingCrDuplicateIdentitiesAndUnknownRelationshipsAreNotInvented()
    {
        using var f=new Fixture();var source=SourceReader.Read(f.Source);
        source.Tables["Sponsors"][0]["CommercialRegistrationNumber"]="";
        source.Tables["Sponsors"][0]["ActiveFrom"]="2026-01-01";
        var missing=ImportPlan.Build(source,Catalog);Assert.AreEqual(1,missing.Rejected["commercial_registration"]["missing_registration_number"]);
        Assert.IsFalse(missing.Documents.Any(d=>d.Kind=="commercial_registration"));
        source.Tables["Employees"][1]["IqamaNo"]=source.Tables["Employees"][0].S("IqamaNo");
        var duplicates=ImportPlan.Build(source,Catalog);Assert.AreEqual(2,duplicates.Rejected["rider"]["duplicate_identity"]);
        source.Tables["Employees"][1]["IqamaNo"]=Identity(2);source.Tables["Employees"][1]["EngagementType"]=99;
        Assert.AreEqual(1,ImportPlan.Build(source,Catalog).Rejected["rider"]["unknown_relationship_code"]);
    }
    [TestMethod]
    public void ConcurrentTargetChangeStopsTheWholeImportBeforeSaving()
    {
        using var f=new Fixture();var source=SourceReader.Read(f.Source);var plan=ImportPlan.Build(source,Catalog);var target=new Store(f.DirectoryPath);
        target.Write((c,tx)=>{Store.Save(c,"rider",new(){["id"]=Identity(1),["bankVerified"]=false},tx);return true;});
        var before=target.Read(ImportExecutor.Target);
        target.Write((c,tx)=>{var row=Store.Get(c,Identity(1),"rider",tx)!;row["bankVerified"]=true;Store.Save(c,"rider",row,tx);return true;});
        Assert.AreEqual("target_changed_retry_dry_run",Assert.ThrowsExactly<ImportFailure>(()=>ImportExecutor.Run(source,plan,before,target)).Code);
        Assert.AreEqual(0,target.Read(c=>Store.List(c,"company").Count));Assert.AreEqual(0,target.Read(c=>Store.List(c,"audit").Count));
        Assert.IsTrue(target.Read(c=>Store.Get(c,Identity(1),"rider")!.B("bankVerified")));
    }
    [TestMethod]
    public void AdministrativeAccountProfilesStayAsReferencesButOutOfTheRiderDirectory()
    {
        using var f=new Fixture();f.Add("RiderProfiles",new(){["Id"]="administrative-profile",["EmployeeId"]="employee3"});
        var source=SourceReader.Read(f.Source);var plan=ImportPlan.Build(source,Catalog);
        var people=plan.Documents.Where(d=>d.Kind=="rider").Select(d=>d.Body).ToList();
        Assert.AreEqual(3,people.Count);Assert.AreEqual(2,people.Count(ErpRiderClassification.ListedAsRider));
        var admin=people.Single(d=>d.S("id")==Identity(3));Assert.AreEqual("administrative",admin.S("personRole"));Assert.IsFalse(admin.B("isDeliveryRider"));
        Assert.AreEqual(1,plan.Observations["administrative_rider_profiles"]);Assert.AreEqual(1,plan.Observations["sponsored_riders"]);
        Assert.IsTrue(ErpRiderClassification.ListedAsRider(new JsonObject{["id"]="legacy",["isEmployee"]=true})); // Legacy contract unchanged.
    }
    [TestMethod]
    public void ExistingDatabaseGetsIndexesWithoutChangingItsDocuments()
    {
        using var f=new Fixture();var store=new Store(f.DirectoryPath);store.Write((c,tx)=>{Store.Save(c,"setting",new(){["id"]="synthetic-setting",["value"]="preserved"},tx);return true;});
        store=new Store(f.DirectoryPath);Assert.AreEqual("preserved",store.Read(c=>Store.Get(c,"synthetic-setting","setting")!.S("value")));
        int indexes=store.Read(c=>{using var cmd=c.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name LIKE 'ix_erp_%'";return Convert.ToInt32(cmd.ExecuteScalar());});Assert.AreEqual(8,indexes);
    }
    [TestMethod]
    public void FuelPullPreservesDatedAssignmentAndAmountsWithoutGuessingUnmatchedRiders()
    {
        using var f=new Fixture();
        f.Add("FuelCards",new(){["Id"]="fuel",["SponsorId"]="sponsor",["OperatingCityId"]="operation-city",["CardNumber"]="SYN-CARD"});
        f.Add("FuelCardRiderAssignments",new(){["Id"]="fuel-assignment",["FuelCardId"]="fuel",["EmployeeId"]="employee2",["RiderProfileId"]="profile2",["EffectiveFrom"]="2026-10-01",["EffectiveTo"]="2026-10-31"});
        f.Add("FuelCardMonthlyUsages",new(){["Id"]="usage",["FuelCardId"]="fuel",["EmployeeId"]="employee2",["ReportMonth"]="2026-10-01",["TotalLiters"]=10,["TotalAmount"]=23,["AmountBeforeTax"]=20,["VatAmount"]=3,["TransactionCount"]=2});
        f.Add("FuelCardMonthlyUsages",new(){["Id"]="unmatched",["FuelCardId"]="fuel",["ReportMonth"]="2026-10-01",["TotalAmount"]=999});
        var source=SourceReader.Read(f.Source);var plan=ImportPlan.Build(source,Catalog);
        var usage=plan.Documents.Single(d=>d.Kind=="fuel_usage_month").Body;
        Assert.AreEqual(Identity(2),usage.S("riderId"));Assert.AreEqual(23m,usage.N("amount"));Assert.AreEqual(3m,usage.N("vat"));
        Assert.AreEqual("2026-10-31",plan.Documents.Single(d=>d.Kind=="fuel_card_assignment").Body.S("effectiveTo"));
        Assert.AreEqual(1,plan.Rejected["fuel_usage_month"]["unmatched_rider"]);
        var summary=ImportExecutor.Run(source,plan,[]);Assert.AreEqual(23m,summary.Totals["fuel_usage_month"]["amount"]);
        Assert.DoesNotContain("SYN-CARD",JsonSerializer.Serialize(summary));
        var target=new Store(f.DirectoryPath);ImportExecutor.Run(source,plan,target.Read(ImportExecutor.Target),target);
        Assert.AreEqual(0,ImportExecutor.Run(source,plan,target.Read(ImportExecutor.Target)).Counts.Sum(c=>c.New+c.Changed));
    }
    [TestMethod]
    public void OptionalPullSourcesDistinguishAbsentEmptyAndInvalidContracts()
    {
        using var f=new Fixture();using(var q=f.Source.CreateCommand()){q.CommandText="DROP TABLE FuelCards";q.ExecuteNonQuery();}
        Assert.AreEqual("source_table_absent",ImportPlan.Build(SourceReader.Read(f.Source),Catalog).Unavailable["fuel_card"]);
        using(var q=f.Source.CreateCommand()){q.CommandText="CREATE TABLE FuelCards(Id TEXT PRIMARY KEY)";q.ExecuteNonQuery();}
        Assert.AreEqual("source_contract_missing_fields:FuelCards",Assert.ThrowsExactly<ImportFailure>(()=>SourceReader.Read(f.Source)).Code);
    }
    [TestMethod]
    public void CustodyAndVehicleFactsPreserveReturnsAccidentInstallmentsAndReversalLineage()
    {
        using var f=new Fixture();
        f.Add("RiderInventoryIssues",new(){["Id"]="issue",["RiderProfileId"]="profile2",["IssuedAtUtc"]="2026-10-01T00:00:00Z"});
        f.Add("RiderInventoryIssueLines",new(){["Id"]="line",["RiderInventoryIssueId"]="issue",["Quantity"]=3,["ReturnedQuantity"]=1,["ExpectedReturn"]=true,["TotalCost"]=30});
        f.Add("RiderPhoneSimAssignments",new(){["Id"]="sim",["RiderProfileId"]="profile2",["PhoneSimCardId"]="sim-card",["EffectiveFrom"]="2026-10-01",["EffectiveTo"]="2026-10-31"});
        f.Add("VehicleAccidents",new(){["Id"]="accident",["VehicleId"]="vehicle",["RiderProfileId"]="profile2",["EmployeeId"]="employee2"});
        f.Add("VehicleAccidentCases",new(){["Id"]="case2",["VehicleAccidentId"]="accident",["EstimatedRepairCost"]=50});
        f.Add("VehicleAccidentInstallments",new(){["Id"]="inst",["VehicleAccidentId"]="accident",["PeriodFrom"]="2026-10-01",["PeriodTo"]="2026-10-31",["Amount"]=20});
        f.Add("VehicleExpenses",new(){["Id"]="expense",["VehicleId"]="vehicle",["RiderProfileId"]="profile2",["TotalAmount"]=-23,["AmountBeforeTax"]=-20,["TaxAmount"]=-3,["ReversalOfExpenseId"]="original"});
        var source=SourceReader.Read(f.Source);var plan=ImportPlan.Build(source,Catalog);JsonObject D(string kind)=>plan.Documents.Single(x=>x.Kind==kind).Body;
        Assert.AreEqual(1m,D("custody_equipment").N("returnedQuantity"));Assert.IsTrue(D("custody_equipment")["lines"]![0]!.B("ExpectedReturn"));
        Assert.AreEqual(Identity(2),D("custody_sim").S("riderId"));Assert.AreEqual(1,D("vehicle_accident")["cases"]!.AsArray().Count);Assert.AreEqual(1,D("vehicle_accident")["installments"]!.AsArray().Count);
        Assert.AreEqual("original",D("vehicle_expense").S("reversesSourceId"));Assert.AreEqual(-23m,ImportExecutor.Run(source,plan,[]).Totals["vehicle_expense"]["amount"]);
    }
    [TestMethod]
    public void ChefzFinalDailyAndWalletFactsUseTheActualOperatorAndPreserveFinancialBreakdown()
    {
        using var f=new Fixture();
        f.Add("AccountDetails",new(){["Id"]="chefz-details",["PlatformRiderAccountId"]="account",["ReportIdNumber"]="SYN-CHEFZ"});
        f.Add("DailyPerformance",new(){["Id"]="chefz-day",["PlatformRiderAccountId"]="account",["RiderClientAssignmentId"]="platform-assignment",["RiderProfileId"]="profile2",["ReportDate"]="2026-10-10",["TotalValidOffers"]=12,["AcceptedOffers"]=10});
        f.Add("WalletSettlements",new(){["Id"]="chefz-wallet",["PlatformRiderAccountId"]="account",["RiderClientAssignmentId"]="platform-assignment",["RiderProfileId"]="profile2",["PeriodFrom"]="2026-10-01",["PeriodTo"]="2026-10-31",["DeliveryFees"]=100,["VatAmount"]=15,["Tips"]=5,["GrossEarnings"]=120,["CompanyAmount"]=20,["RiderAmount"]=100});
        var source=SourceReader.Read(f.Source);var plan=ImportPlan.Build(source,Catalog);
        var account=plan.Documents.Single(x=>x.Kind=="chefz_account").Body;Assert.AreEqual(Identity(1),account.S("registeredRiderId"));
        var daily=plan.Documents.Single(x=>x.Kind=="chefz_daily").Body;Assert.AreEqual(Identity(2),daily.S("riderId"));
        var settlement=plan.Documents.Single(x=>x.Kind=="chefz_settlement").Body;Assert.AreEqual(15m,settlement.N("vat"));Assert.AreEqual(120m,settlement.N("gross"));
        Assert.AreEqual(100m,ImportExecutor.Run(source,plan,[]).Totals["chefz_settlement"]["riderAmount"]);
        source.Tables["DailyPerformance"][0]["RiderClientAssignmentId"]="missing";
        Assert.AreEqual(1,ImportPlan.Build(source,Catalog).Rejected["chefz_daily"]["unmatched_assignment"]);
    }
    [TestMethod]
    public void JahezFinalFactsLinkThroughSessionsAndKeepLedgerReversalsWithoutImportRows()
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
        var source=SourceReader.Read(f.Source);var plan=ImportPlan.Build(source,Catalog);var docs=plan.Documents.Where(x=>x.Kind.StartsWith("jahez_")).ToList();
        Assert.AreEqual(7,docs.Count);Assert.IsTrue(docs.All(x=>x.Body.S("riderId")==Identity(2)));
        Assert.AreEqual("original",docs.Single(x=>x.Kind=="jahez_ledger_entry").Body.S("reversesSourceId"));
        Assert.AreEqual(90m,ImportExecutor.Run(source,plan,[]).Totals["jahez_transaction"]["net"]);
        Assert.IsFalse(SourceReader.Catalog.Any(x=>x.Name.EndsWith("ImportRows")||x.Name.EndsWith("Sources")));
        source.Tables["PlatformTransactions"][0]["HandoverId"]="missing";
        Assert.AreEqual(1,ImportPlan.Build(source,Catalog).Rejected["jahez_transaction"]["missing_session_relation"]);
    }
    [TestMethod]
    public void NinjaKeepsCaptainIdOnAccountsDaysAndDeductionsWithoutUsingSourceRows()
    {
        using var f=new Fixture();using(var cmd=f.Source.CreateCommand()){cmd.CommandText="UPDATE PlatformRiderAccounts SET ExternalAccountId='SYN-CAPTAIN'";cmd.ExecuteNonQuery();}
        f.Add("NinjaAccountDetails",new(){["Id"]="ninja-details",["PlatformRiderAccountId"]="account"});
        f.Add("NinjaDailyPerformance",new(){["Id"]="ninja-day",["PlatformRiderAccountId"]="account",["RiderClientAssignmentId"]="platform-assignment",["RiderProfileId"]="profile2",["CaptainId"]="SYN-CAPTAIN",["ReportDate"]="2026-10-01",["TotalOrders"]=10,["TotalWorkingHours"]=8.5m});
        f.Add("NinjaDailyDeductions",new(){["Id"]="ninja-deduction",["PlatformRiderAccountId"]="account",["RiderClientAssignmentId"]="platform-assignment",["RiderProfileId"]="profile2",["CaptainId"]="SYN-CAPTAIN",["ReportDate"]="2026-10-01",["TotalDeductions"]=5,["TotalCollectedAmountAtDelivery"]=20});
        var source=SourceReader.Read(f.Source);var plan=ImportPlan.Build(source,Catalog);var docs=plan.Documents.Where(d=>d.Kind.StartsWith("ninja_")).ToList();
        Assert.AreEqual(3,docs.Count);Assert.IsTrue(docs.All(x=>x.Body.S("CaptainId")=="SYN-CAPTAIN"));
        Assert.AreEqual(Identity(2),docs.Single(x=>x.Kind=="ninja_daily").Body.S("riderId"));Assert.AreEqual(8.5m,ImportExecutor.Run(source,plan,[]).Totals["ninja_daily"]["hours"]);
        source.Tables["NinjaDailyPerformance"][0]["CaptainId"]="";Assert.AreEqual(1,ImportPlan.Build(source,Catalog).Rejected["ninja_daily"]["missing_captain_id"]);
    }
}
