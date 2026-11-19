using System.Data;
using System.Globalization;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Executes OperationsLiveQuery against a throwaway SQL Server database (H4).
/// Runs only when PAYROLL_TEST_SQL_CONNECTION is set (reviewer / sqltest.sh).
/// </summary>
[TestClass]
public sealed class OperationsLiveSqlServerTests
{
    static readonly Guid CityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    static readonly Guid GlobalCityId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    static readonly Guid SponsorA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    static readonly Guid SponsorB = Guid.Parse("44444444-4444-4444-4444-444444444444");
    static readonly Guid VehicleId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    static readonly Guid EmpInScope = Guid.Parse("66666666-6666-6666-6666-666666666666");
    static readonly Guid EmpOutScope = Guid.Parse("77777777-7777-7777-7777-777777777777");
    static readonly Guid ProfileIn = Guid.Parse("88888888-8888-8888-8888-888888888888");
    static readonly Guid HousingId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    static readonly Guid RoomId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    static readonly Guid PlatformId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    static readonly Guid AccountActive = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    static readonly Guid AccountAssigned = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    static readonly Guid FuelCardId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    static readonly Guid AssignmentOpen = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    static readonly Guid AssignmentEnded = Guid.Parse("12121212-1212-1212-1212-121212121212");
    static readonly Guid AssignmentLate = Guid.Parse("13131313-1313-1313-1313-131313131313");
    static readonly Guid ExpenseKeep = Guid.Parse("14141414-1414-1414-1414-141414141414");
    static readonly Guid ExpenseReversed = Guid.Parse("15151515-1515-1515-1515-151515151515");
    static readonly Guid ExpenseReversal = Guid.Parse("16161616-1616-1616-1616-161616161616");
    static readonly Guid OilOpId = Guid.Parse("17171717-1717-1717-1717-171717171717");
    static readonly Guid PurchaseId = Guid.Parse("18181818-1818-1818-1818-181818181818");
    static readonly Guid PurchaseOctBillReceivedNov = Guid.Parse("19191919-1919-1919-1919-191919191919");
    static readonly Guid PurchaseReversed = Guid.Parse("1a1a1a1a-1a1a-1a1a-1a1a-1a1a1a1a1a1a");
    static readonly Guid PurchaseUtcBoundary = Guid.Parse("1b1b1b1b-1b1b-1b1b-1b1b-1b1b1b1b1b1b");
    static readonly Guid PurchaseOutside = Guid.Parse("1c1c1c1c-1c1c-1c1c-1c1c-1c1c1c1c1c1c");
    static readonly Guid PurchaseDeleted = Guid.Parse("1d1d1d1d-1d1d-1d1d-1d1d-1d1d1d1d1d1d");

    [TestMethod]
    public void OperationsLiveQueries_ExecuteOnThrowawaySqlServer()
    {
        string? configured = Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Inconclusive("Operations live SQL requires PAYROLL_TEST_SQL_CONNECTION.");
            return;
        }
        var options = new SqlConnectionStringBuilder(configured);
        if (!System.Text.RegularExpressions.Regex.IsMatch(options.DataSource, @"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            Assert.Inconclusive("Operations live SQL requires loopback, not staging/production.");
            return;
        }

        string name = "GateOpsLive_" + Guid.NewGuid().ToString("N");
        options.InitialCatalog = "master";
        using var admin = new SqlConnection(options.ConnectionString);
        admin.Open();
        using (var create = admin.CreateCommand())
        {
            create.CommandText = "CREATE DATABASE [" + name + "]";
            create.ExecuteNonQuery();
        }
        try
        {
            options.InitialCatalog = name;
            using var conn = new SqlConnection(options.ConnectionString);
            conn.Open();
            CreateSchema(conn);
            Seed(conn);

            var all = new LiveOperationsQuery.Scope(true, [], [], [], []);
            var oneCompany = new LiveOperationsQuery.Scope(false, ["7000000001"], [EmpInScope.ToString("D")], [ProfileIn.ToString("D")], []);
            var onePlatform = new LiveOperationsQuery.Scope(true, [], [], [], ["KEETA"], PlatformRestricted: true);

            var from = new DateOnly(2026, 11, 1);
            var to = new DateOnly(2026, 11, 30);

            // H1 day clipping + amounts
            var period = OperationsLiveQuery.VehiclesPeriodReport(conn, from, to, "vehicle", 1, 50, "", all, detailed: true);
            Assert.IsTrue(period.Items.Count >= 1);
            Assert.IsFalse(period.Items.Any(r => r.ContainsKey("vin") || r.ContainsKey("Vin")));
            var row = period.Items.First(r => string.Equals(r["id"]?.ToString(), VehicleId.ToString("D"), StringComparison.OrdinalIgnoreCase));
            // Open assignment from Oct 1 → clipped to Nov 1–30 = 30 days; late start after 14:00 Nov 10 → from Nov 11; ended inside counted.
            Assert.AreEqual(35m, (decimal)row["operatingDays"]!.GetValue<decimal>(), "operating days must clip to period");
            decimal expectedCost =
                Math.Round(30m * (1800m / 30m), 2, MidpointRounding.AwayFromZero)
                + Math.Round(1m * (1800m / 30m), 2, MidpointRounding.AwayFromZero)
                + Math.Round(4m * (1800m / 30m), 2, MidpointRounding.AwayFromZero);
            Assert.AreEqual(expectedCost, row["amountToCollect"]!.GetValue<decimal>());
            // M2: only unreversed MaintenanceMaterialUsage (100); reversed original excluded
            Assert.AreEqual(100m, row["sparePartsCost"]!.GetValue<decimal>());
            Assert.AreEqual(1L, row["oilChangesCount"]!.GetValue<long>());
            Assert.IsFalse(string.IsNullOrEmpty(row["riderNameAr"]?.GetValue<string>()));

            var riderPeriod = OperationsLiveQuery.VehiclesPeriodReport(conn, from, to, "rider", 1, 50, "", all, detailed: true);
            Assert.IsTrue(riderPeriod.Items.Count >= 1);
            Assert.IsTrue(riderPeriod.Items.All(r => r["sparePartsCost"] is not null));

            // H2 housing residents scoped
            var roomsAll = OperationsLiveQuery.HousingRooms(conn, HousingId.ToString("D"), all);
            var roomsScoped = OperationsLiveQuery.HousingRooms(conn, HousingId.ToString("D"), oneCompany);
            int CountResidents(JsonObject body) =>
                (body["rooms"] as JsonArray ?? []).OfType<JsonObject>()
                    .Sum(room => (room["residents"] as JsonArray ?? []).Count);
            Assert.AreEqual(2, CountResidents(roomsAll.Body));
            Assert.AreEqual(1, CountResidents(roomsScoped.Body));

            // H5 active = Status IN (1,2)
            var cities = OperationsLiveQuery.CitiesSummary(conn, all);
            Assert.IsTrue(cities.Items.Count >= 1);
            Assert.AreEqual(2L, cities.Items[0]["accountsActive"]!.GetValue<long>());

            // M6 platform scope on city account counts
            var citiesPlat = OperationsLiveQuery.CitiesSummary(conn, onePlatform);
            Assert.AreEqual(2L, citiesPlat.Items[0]["accountsTotal"]!.GetValue<long>());

            // M7 purchases unscoped for company users + all period receipts (InvoiceDate OR Riyadh ReceivedAtUtc, all statuses)
            var purchasesAll = OperationsLiveQuery.PurchaseInvoices(conn, from, to, 1, 50, "", all, null);
            Assert.AreEqual(4L, purchasesAll.Total);
            Assert.AreEqual(1150m, purchasesAll.Summary["totalAmount"]!.GetValue<decimal>());
            Assert.IsTrue(purchasesAll.Items.Any(r => r["status"]!.GetValue<string>() == "مرحّل"));
            Assert.IsTrue(purchasesAll.Items.Any(r => r["status"]!.GetValue<string>() == "معكوس"));
            var purchaseDetailOutside = OperationsLiveQuery.PurchaseInvoices(conn, from, to, 1, 1, "", all, PurchaseOutside.ToString("D"));
            Assert.AreEqual(1L, purchaseDetailOutside.Total);
            var purchasesScoped = OperationsLiveQuery.PurchaseInvoices(conn, from, to, 1, 50, "", oneCompany, null);
            Assert.AreEqual(0L, purchasesScoped.Total);
            try
            {
                _ = OperationsLiveQuery.PurchaseInvoices(conn, from, to, 1, 1, "", oneCompany, PurchaseId.ToString("D"));
                Assert.Fail("detail must 404 for company scope");
            }
            catch (ImportFailure ex) { Assert.AreEqual("purchase_not_found", ex.Code); }

            // H3 export = no OFFSET, all rows
            var housingExport = OperationsLiveQuery.HousingExport(conn, "", "", "", all, 30);
            Assert.AreEqual(housingExport.Total, housingExport.Items.Count);
            var fuelExport = OperationsLiveQuery.FuelCardsExport(conn, new DateOnly(2026, 11, 1), "", "", all, 30);
            Assert.AreEqual(fuelExport.Total, fuelExport.Items.Count);
            Assert.AreEqual("PetroApp", fuelExport.Items[0]["provider"]!.GetValue<string>());

            // paging totals match aggregates
            var paged = OperationsLiveQuery.VehiclesPeriodReport(conn, from, to, "vehicle", 1, 1, "", all, detailed: false);
            Assert.AreEqual(period.Total, paged.Total);
            Assert.AreEqual(period.Summary["amountToCollect"]!.GetValue<decimal>(), paged.Summary["amountToCollect"]!.GetValue<decimal>());

            var oil = OperationsLiveQuery.OilChangesPeriod(conn, from, to, 1, 50, "", all, export: true);
            Assert.AreEqual(1L, oil.Total);
            Assert.AreEqual(oil.Total, oil.Items.Count);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var drop = admin.CreateCommand();
            drop.CommandText = "ALTER DATABASE [" + name + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + name + "]";
            drop.ExecuteNonQuery();
        }
    }

    static void CreateSchema(SqlConnection conn)
    {
        Exec(conn, "CREATE SCHEMA app");
        Exec(conn, "CREATE SCHEMA platform");
        Exec(conn, "CREATE SCHEMA maintenance");
        Exec(conn, """
            CREATE TABLE platform.GlobalCities (Id uniqueidentifier NOT NULL PRIMARY KEY, Code nvarchar(50) NULL, NameAr nvarchar(200) NULL, NameEn nvarchar(200) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.OperatingCities (Id uniqueidentifier NOT NULL PRIMARY KEY, GlobalCityId uniqueidentifier NOT NULL, Status int NOT NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.Sponsors (Id uniqueidentifier NOT NULL PRIMARY KEY, RegistryNameAr nvarchar(200) NULL, UnifiedNationalNumber nvarchar(20) NULL, Status int NOT NULL DEFAULT 1, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.Employees (Id uniqueidentifier NOT NULL PRIMARY KEY, FullNameAr nvarchar(200) NULL, IqamaNo varchar(20) NULL, SponsorId uniqueidentifier NULL, OperatingCityId uniqueidentifier NULL, Status int NOT NULL DEFAULT 3, IsEmployee bit NOT NULL DEFAULT 0, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.RiderProfiles (Id uniqueidentifier NOT NULL PRIMARY KEY, EmployeeId uniqueidentifier NOT NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.Vehicles (Id uniqueidentifier NOT NULL PRIMARY KEY, AssetNumber nvarchar(50) NULL, PlateNumberAr nvarchar(50) NULL, PlateNumberEn nvarchar(50) NULL, Vin nvarchar(50) NULL, ChassisNumber nvarchar(50) NULL, SerialNumber nvarchar(50) NULL, VehicleType int NOT NULL, ModelYear int NULL, CurrentOdometer bigint NULL, CurrentOperationalStatus int NULL, OperatingCityId uniqueidentifier NULL, SponsorId uniqueidentifier NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.RiderVehicleAssignments (Id uniqueidentifier NOT NULL PRIMARY KEY, RiderProfileId uniqueidentifier NOT NULL, VehicleId uniqueidentifier NOT NULL, StartedAtUtc datetimeoffset NOT NULL, EndedAtUtc datetimeoffset NULL, StartOdometer bigint NULL, EndOdometer bigint NULL, IsRealRider bit NOT NULL DEFAULT 1, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.RealRiders (Id uniqueidentifier NOT NULL PRIMARY KEY, RiderVehicleAssignmentId uniqueidentifier NOT NULL, Name nvarchar(200) NULL, IqamaNo varchar(20) NULL);
            CREATE TABLE app.VehicleOdometerReadings (Id uniqueidentifier NOT NULL PRIMARY KEY, VehicleId uniqueidentifier NOT NULL, Reading bigint NOT NULL, RecordedAtUtc datetimeoffset NOT NULL, IsVoided bit NOT NULL DEFAULT 0);
            CREATE TABLE app.Housing (Id uniqueidentifier NOT NULL PRIMARY KEY, Code nvarchar(50) NULL, NameAr nvarchar(200) NULL, Status int NOT NULL, CityId uniqueidentifier NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.HousingRooms (Id uniqueidentifier NOT NULL PRIMARY KEY, HousingId uniqueidentifier NOT NULL, Name nvarchar(100) NULL, Capacity int NOT NULL, CurrentOccupancy int NOT NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.HousingResidencePeriods (Id uniqueidentifier NOT NULL PRIMARY KEY, EmployeeId uniqueidentifier NOT NULL, RoomId uniqueidentifier NOT NULL, EffectiveFrom date NOT NULL, EffectiveTo date NULL);
            CREATE TABLE platform.ClientPlatforms (Id uniqueidentifier NOT NULL PRIMARY KEY, Code nvarchar(50) NULL, NameAr nvarchar(200) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.PlatformRiderAccounts (Id uniqueidentifier NOT NULL PRIMARY KEY, ClientPlatformId uniqueidentifier NOT NULL, Code nvarchar(50) NULL, Status int NOT NULL, OperatingCityId uniqueidentifier NULL, SponsorId uniqueidentifier NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.RiderClientAssignments (Id uniqueidentifier NOT NULL PRIMARY KEY, PlatformRiderAccountId uniqueidentifier NOT NULL, EffectiveTo date NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.FuelCards (Id uniqueidentifier NOT NULL PRIMARY KEY, Provider int NOT NULL, CardNumber nvarchar(50) NULL, PlateNumberText nvarchar(50) NULL, SponsorId uniqueidentifier NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.FuelCardRiderAssignments (Id uniqueidentifier NOT NULL PRIMARY KEY, FuelCardId uniqueidentifier NOT NULL, EmployeeId uniqueidentifier NULL, EffectiveFrom date NOT NULL, EffectiveTo date NULL, AssignmentReason nvarchar(200) NULL, EndReason nvarchar(200) NULL);
            CREATE TABLE app.FuelCardMonthlyUsages (Id uniqueidentifier NOT NULL PRIMARY KEY, FuelCardId uniqueidentifier NOT NULL, EmployeeId uniqueidentifier NULL, RiderProfileId uniqueidentifier NULL, ReportMonth date NOT NULL, TotalLiters decimal(18,2) NULL, AmountBeforeTax decimal(18,2) NULL, VatAmount decimal(18,2) NULL, TotalAmount decimal(18,2) NULL, TransactionCount int NULL, FuelType nvarchar(50) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE maintenance.VehicleExpenses (Id uniqueidentifier NOT NULL PRIMARY KEY, VehicleId uniqueidentifier NOT NULL, RiderVehicleAssignmentId uniqueidentifier NULL, RiderProfileId uniqueidentifier NULL, ExpenseType nvarchar(100) NOT NULL, SourceEntityType nvarchar(100) NOT NULL, SourceEntityId uniqueidentifier NULL, OccurredOn date NOT NULL, AmountBeforeTax decimal(18,2) NOT NULL, TaxAmount decimal(18,2) NULL, TotalAmount decimal(18,2) NOT NULL, ReversalOfExpenseId uniqueidentifier NULL);
            CREATE TABLE maintenance.OilChangeOperations (Id uniqueidentifier NOT NULL PRIMARY KEY, VehicleId uniqueidentifier NULL, PerformedAtUtc datetimeoffset NOT NULL, OdometerAtChange bigint NOT NULL, OilQuantityLiters decimal(9,3) NOT NULL, OilCost decimal(18,2) NOT NULL, OilFilterCost decimal(18,2) NOT NULL, LaborCost decimal(18,2) NOT NULL, OtherCost decimal(18,2) NOT NULL, TotalCost decimal(18,2) NOT NULL, IsCancelled bit NOT NULL DEFAULT 0, Notes nvarchar(200) NULL, VehicleTypeSnapshot int NOT NULL DEFAULT 2);
            CREATE TABLE maintenance.Suppliers (Id uniqueidentifier NOT NULL PRIMARY KEY, LegalNameAr nvarchar(200) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE maintenance.PurchaseReceipts (Id uniqueidentifier NOT NULL PRIMARY KEY, ReceiptNumber nvarchar(50) NULL, SupplierInvoiceNumber nvarchar(50) NULL, InvoiceDate date NOT NULL, ReceivedAtUtc datetimeoffset NULL, Subtotal decimal(18,2) NOT NULL, TaxAmount decimal(18,2) NOT NULL, TotalAmount decimal(18,2) NOT NULL, Status nvarchar(50) NULL, SupplierId uniqueidentifier NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE maintenance.PurchaseReceiptLines (Id uniqueidentifier NOT NULL PRIMARY KEY, PurchaseReceiptId uniqueidentifier NOT NULL, PurchaseUnit nvarchar(50) NULL, PackageCount decimal(18,2) NULL, ReceivedBaseQuantity decimal(18,2) NULL, LineSubtotal decimal(18,2) NULL, TaxAmount decimal(18,2) NULL, InventoryValuationAmount decimal(18,2) NULL, LotNumber nvarchar(50) NULL);
            """);
    }

    static void Seed(SqlConnection conn)
    {
        Exec(conn, $"""
            INSERT INTO platform.GlobalCities(Id,Code,NameAr,NameEn) VALUES('{GlobalCityId}','JED',N'جدة','Jeddah');
            INSERT INTO app.OperatingCities(Id,GlobalCityId,Status) VALUES('{CityId}','{GlobalCityId}',1);
            INSERT INTO app.Sponsors(Id,RegistryNameAr,UnifiedNationalNumber,Status) VALUES
              ('{SponsorA}',N'شركة أ','7000000001',1),('{SponsorB}',N'شركة ب','7000000002',1);
            INSERT INTO app.Employees(Id,FullNameAr,IqamaNo,SponsorId,OperatingCityId,Status,IsEmployee) VALUES
              ('{EmpInScope}',N'مندوب داخل','2500000001','{SponsorA}','{CityId}',3,0),
              ('{EmpOutScope}',N'مندوب خارج','2500000002','{SponsorB}','{CityId}',3,0);
            INSERT INTO app.RiderProfiles(Id,EmployeeId) VALUES('{ProfileIn}','{EmpInScope}');
            INSERT INTO app.Vehicles(Id,AssetNumber,PlateNumberAr,VehicleType,OperatingCityId,SponsorId,CurrentOdometer)
              VALUES('{VehicleId}','A-1',N'أ ب ج 1234',2,'{CityId}','{SponsorA}',12000);
            -- open since Oct (clipped to Nov 30 days); ended inside Nov 1–2 (1 day after late? ended Nov 2 10:00 from Nov 1 10:00 = 1 day);
            -- late pickup Nov 10 15:00 → billing from Nov 11 through Nov 30 = 20 days
            -- started before period (ends after period → 30 clipped days); ended inside (Nov1–Nov2 = 1 day);
            -- pickup after 14:00 on Nov10 → billing from Nov11 to Nov15 exclusive end = 4 days. Non-overlapping.
            INSERT INTO app.RiderVehicleAssignments(Id,RiderProfileId,VehicleId,StartedAtUtc,EndedAtUtc,StartOdometer,EndOdometer,IsRealRider) VALUES
              ('{AssignmentOpen}','{ProfileIn}','{VehicleId}','2026-10-01T10:00:00+03:00','2026-12-05T10:00:00+03:00',10000,12000,1),
              ('{AssignmentEnded}','{ProfileIn}','{VehicleId}','2026-11-01T10:00:00+03:00','2026-11-02T10:00:00+03:00',10000,10100,1),
              ('{AssignmentLate}','{ProfileIn}','{VehicleId}','2026-11-10T15:00:00+03:00','2026-11-15T10:00:00+03:00',11000,11500,1);
            INSERT INTO app.Housing(Id,Code,NameAr,Status,CityId) VALUES('{HousingId}','H1',N'سكن تجريبي',2,'{GlobalCityId}');
            INSERT INTO app.HousingRooms(Id,HousingId,Name,Capacity,CurrentOccupancy) VALUES('{RoomId}','{HousingId}',N'غرفة 1',4,2);
            INSERT INTO app.HousingResidencePeriods(Id,EmployeeId,RoomId,EffectiveFrom) VALUES
              (NEWID(),'{EmpInScope}','{RoomId}','2026-10-01'),
              (NEWID(),'{EmpOutScope}','{RoomId}','2026-10-01');
            INSERT INTO platform.ClientPlatforms(Id,Code,NameAr) VALUES('{PlatformId}','KEETA',N'كيتا');
            INSERT INTO app.PlatformRiderAccounts(Id,ClientPlatformId,Code,Status,OperatingCityId,SponsorId) VALUES
              ('{AccountActive}','{PlatformId}','A1',1,'{CityId}','{SponsorA}'),
              ('{AccountAssigned}','{PlatformId}','A2',2,'{CityId}','{SponsorA}');
            INSERT INTO app.RiderClientAssignments(Id,PlatformRiderAccountId,EffectiveTo) VALUES (NEWID(),'{AccountAssigned}',NULL);
            INSERT INTO maintenance.VehicleExpenses(Id,VehicleId,RiderVehicleAssignmentId,ExpenseType,SourceEntityType,OccurredOn,AmountBeforeTax,TotalAmount) VALUES
              ('{ExpenseKeep}','{VehicleId}','{AssignmentOpen}','MaintenanceMaterial','MaintenanceMaterialUsage','2026-11-05',100,100),
              ('{ExpenseReversed}','{VehicleId}','{AssignmentOpen}','MaintenanceMaterial','MaintenanceMaterialUsage','2026-11-06',50,50);
            INSERT INTO maintenance.VehicleExpenses(Id,VehicleId,RiderVehicleAssignmentId,ExpenseType,SourceEntityType,OccurredOn,AmountBeforeTax,TotalAmount,ReversalOfExpenseId) VALUES
              ('{ExpenseReversal}','{VehicleId}','{AssignmentOpen}','MaintenanceMaterial','MaintenanceMaterialUsage','2026-11-07',-50,-50,'{ExpenseReversed}');
            INSERT INTO maintenance.VehicleExpenses(Id,VehicleId,ExpenseType,SourceEntityType,OccurredOn,AmountBeforeTax,TotalAmount)
              VALUES(NEWID(),'{VehicleId}','MaintenanceLabor','OilChangeOperation','2026-11-08',80,80);
            INSERT INTO maintenance.OilChangeOperations(Id,VehicleId,PerformedAtUtc,OdometerAtChange,OilQuantityLiters,OilCost,OilFilterCost,LaborCost,OtherCost,TotalCost)
              VALUES('{OilOpId}','{VehicleId}','2026-11-12T12:00:00+03:00',11200,4,40,10,20,5,75);
            INSERT INTO maintenance.PurchaseReceipts(Id,ReceiptNumber,InvoiceDate,ReceivedAtUtc,Subtotal,TaxAmount,TotalAmount,Status,IsDeleted) VALUES
              ('{PurchaseId}','R-1','2026-11-15','2026-11-15T10:00:00+03:00',100,15,115,'1',0),
              ('{PurchaseOctBillReceivedNov}','R-2','2026-10-28','2026-11-02T09:00:00+03:00',200,30,230,'Posted',0),
              ('{PurchaseReversed}','R-3','2026-11-10','2026-12-02T09:00:00+03:00',300,45,345,'2',0),
              ('{PurchaseUtcBoundary}','R-4','2026-10-31','2026-10-31T22:00:00+00:00',400,60,460,'1',0),
              ('{PurchaseOutside}','R-5','2026-10-10','2026-10-12T09:00:00+03:00',500,75,575,'1',0),
              ('{PurchaseDeleted}','R-6','2026-11-20','2026-11-20T09:00:00+03:00',600,90,690,'1',1);
            INSERT INTO app.FuelCards(Id,Provider,CardNumber,PlateNumberText,SponsorId) VALUES('{FuelCardId}',1,'FC-1',N'أ ب ج 1234','{SponsorA}');
            INSERT INTO app.FuelCardMonthlyUsages(Id,FuelCardId,EmployeeId,ReportMonth,TotalLiters,AmountBeforeTax,VatAmount,TotalAmount,TransactionCount,FuelType)
              VALUES(NEWID(),'{FuelCardId}','{EmpInScope}','2026-11-01',40,100,15,115,3,N'91');
            INSERT INTO app.FuelCardRiderAssignments(Id,FuelCardId,EmployeeId,EffectiveFrom) VALUES(NEWID(),'{FuelCardId}','{EmpInScope}','2026-11-01');
            """);
    }

    static void Exec(SqlConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 60;
        cmd.ExecuteNonQuery();
    }
}
