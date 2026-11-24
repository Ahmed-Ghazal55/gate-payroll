using System.Globalization;
using ErpImport;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Executes PaySourcesLiveQuery against a throwaway SQL Server database.
/// Runs only when PAYROLL_TEST_SQL_CONNECTION is set (sqltest.sh).
/// </summary>
[TestClass]
public sealed class PaySourcesLiveSqlServerTests
{
    static readonly Guid SponsorA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    static readonly Guid EmpIn = Guid.Parse("66666666-6666-6666-6666-666666666666");
    static readonly Guid ProfileIn = Guid.Parse("88888888-8888-8888-8888-888888888888");
    static readonly Guid VehicleId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    static readonly Guid AssignmentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    static readonly Guid HousingId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    static readonly Guid RoomId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    static readonly Guid ResidenceId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    static readonly Guid FuelCardId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    static readonly Guid FuelUsageId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    static readonly Guid PlatformId = Guid.Parse("12121212-1212-1212-1212-121212121212");
    static readonly Guid AccountId = Guid.Parse("13131313-1313-1313-1313-131313131313");
    static readonly Guid SnapshotId = Guid.Parse("14141414-1414-1414-1414-141414141414");
    static readonly Guid EventId = Guid.Parse("15151515-1515-1515-1515-151515151515");
    static readonly Guid DetailsId = Guid.Parse("16161616-1616-1616-1616-161616161616");

    [TestMethod]
    public void PaySourcesLiveQueries_ExecuteOnThrowawaySqlServer()
    {
        string? configured = Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Inconclusive("Pay sources live SQL requires PAYROLL_TEST_SQL_CONNECTION.");
            return;
        }
        var options = new SqlConnectionStringBuilder(configured);
        if (!System.Text.RegularExpressions.Regex.IsMatch(options.DataSource, @"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            Assert.Inconclusive("Pay sources live SQL requires loopback, not staging/production.");
            return;
        }

        string name = "GatePaySources_" + Guid.NewGuid().ToString("N");
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
            var one = new LiveOperationsQuery.Scope(false, ["7000000001"], [], [], []);
            var month = new DateOnly(2026, 10, 1);
            var from = new DateOnly(2026, 10, 1);
            var to = new DateOnly(2026, 10, 31);

            var fuel = PaySourcesLiveQuery.FuelConsumption(conn, from, to, 1, 50, "", all);
            Assert.AreEqual(1, fuel.Items.Count);
            Assert.AreEqual(80m, fuel.Items[0]["totalInclVat"]!.GetValue<decimal>());
            Assert.AreEqual("monthly", fuel.Items[0]["usageSource"]!.GetValue<string>());

            var fuelScoped = PaySourcesLiveQuery.FuelConsumption(conn, from, to, 1, 50, "", one);
            Assert.AreEqual(1, fuelScoped.Items.Count);
            var fuelOut = PaySourcesLiveQuery.FuelConsumption(conn, from, to, 1, 50, "", new LiveOperationsQuery.Scope(false, ["7000000099"], [], [], []));
            Assert.AreEqual(0, fuelOut.Items.Count);

            var vehicles = PaySourcesLiveQuery.VehicleAssignmentPeriods(conn, from, to, 1, 50, "", all);
            Assert.AreEqual(1, vehicles.Items.Count);
            Assert.IsTrue(vehicles.Items[0]["isRealRider"]!.GetValue<bool>());

            var housing = PaySourcesLiveQuery.HousingPeriods(conn, from, to, 1, 50, "", all);
            Assert.AreEqual(1, housing.Items.Count);
            Assert.AreEqual(15L, housing.Items[0]["daysInSelectedPeriod"]!.GetValue<long>());

            var keeta = PaySourcesLiveQuery.KeetaEligibility(conn, new DateOnly(2026, 11, 1), 1, 50, "", all);
            Assert.AreEqual(1, keeta.Items.Count);
            Assert.AreEqual("expected_only", keeta.Items[0]["approval"]!.GetValue<string>());
            Assert.AreEqual(20L, keeta.Items[0]["validDays"]!.GetValue<long>());
            Assert.AreEqual(5L, keeta.Items[0]["invalidDays"]!.GetValue<long>());
            Assert.AreEqual(22L, keeta.Items[0]["requiredValidDays"]!.GetValue<long>());
            Assert.AreEqual(600L, keeta.Items[0]["monthlyOrdersRequired"]!.GetValue<long>());
            Assert.AreEqual("Slab", keeta.Items[0]["settlementScheme"]!.GetValue<string>());

            var hs = PaySourcesLiveQuery.HsStampQueue(conn, 1, 50, "", "", all);
            Assert.IsTrue(hs.Items.Count >= 2);
            Assert.IsTrue(hs.Items.Any(r => r["rowKind"]!.GetValue<string>() == "wallet_status"));
            Assert.IsTrue(hs.Items.Any(r => r["rowKind"]!.GetValue<string>() == "stamp_event"));
            Assert.AreEqual(1L, hs.Summary["walletPending"]!.GetValue<long>());
            Assert.AreEqual(1L, hs.Summary["stampedEvents"]!.GetValue<long>());
        }
        finally
        {
            using var drop = admin.CreateCommand();
            drop.CommandText = "ALTER DATABASE [" + name + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + name + "]";
            drop.ExecuteNonQuery();
        }
    }

    static void Exec(SqlConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static void CreateSchema(SqlConnection conn)
    {
        Exec(conn, "CREATE SCHEMA app");
        Exec(conn, "CREATE SCHEMA platform");
        Exec(conn, "CREATE SCHEMA keeta");
        Exec(conn, "CREATE SCHEMA finance_read");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE app.Sponsors (Id uniqueidentifier NOT NULL PRIMARY KEY, RegistryNameAr nvarchar(200) NULL, UnifiedNationalNumber nvarchar(20) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.Employees (Id uniqueidentifier NOT NULL PRIMARY KEY, SponsorId uniqueidentifier NULL, FullNameAr nvarchar(200) NULL, IqamaNo varchar(20) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.RiderProfiles (Id uniqueidentifier NOT NULL PRIMARY KEY, EmployeeId uniqueidentifier NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.Vehicles (Id uniqueidentifier NOT NULL PRIMARY KEY, AssetNumber nvarchar(50) NULL, PlateNumberAr nvarchar(50) NULL, PlateNumberEn nvarchar(50) NULL, VehicleType int NULL, SponsorId uniqueidentifier NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.RiderVehicleAssignments (Id uniqueidentifier NOT NULL PRIMARY KEY, RiderProfileId uniqueidentifier NOT NULL, VehicleId uniqueidentifier NOT NULL, StartedAtUtc datetimeoffset NOT NULL, EndedAtUtc datetimeoffset NULL, StartOdometer bigint NULL, EndOdometer bigint NULL, PermissionStartsOn date NULL, PermissionEndsOn date NULL, Status nvarchar(50) NULL, IsRealRider bit NOT NULL DEFAULT 1, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.RealRiders (Id uniqueidentifier NOT NULL PRIMARY KEY, RiderVehicleAssignmentId uniqueidentifier NOT NULL, Name nvarchar(200) NULL, IqamaNo varchar(20) NULL);
            CREATE TABLE app.Housing (Id uniqueidentifier NOT NULL PRIMARY KEY, Code nvarchar(50) NULL, NameAr nvarchar(200) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.HousingRooms (Id uniqueidentifier NOT NULL PRIMARY KEY, HousingId uniqueidentifier NOT NULL, Name nvarchar(100) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.HousingResidencePeriods (Id uniqueidentifier NOT NULL PRIMARY KEY, EmployeeId uniqueidentifier NOT NULL, RoomId uniqueidentifier NOT NULL, EffectiveFrom date NOT NULL, EffectiveTo date NULL, MoveInReason nvarchar(200) NULL, MoveOutReason nvarchar(200) NULL, SourceReference nvarchar(200) NULL);
            CREATE TABLE app.FuelCards (Id uniqueidentifier NOT NULL PRIMARY KEY, Provider int NOT NULL, CardNumber nvarchar(50) NULL, PlateNumberText nvarchar(50) NULL, SponsorId uniqueidentifier NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.FuelCardMonthlyUsages (Id uniqueidentifier NOT NULL PRIMARY KEY, FuelCardId uniqueidentifier NOT NULL, EmployeeId uniqueidentifier NULL, RiderProfileId uniqueidentifier NULL, ReportMonth date NOT NULL, TotalLiters decimal(18,2) NULL, AmountBeforeTax decimal(18,2) NULL, VatAmount decimal(18,2) NULL, TotalAmount decimal(18,2) NULL, TransactionCount int NULL, FuelType nvarchar(50) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE platform.ClientPlatforms (Id uniqueidentifier NOT NULL PRIMARY KEY, Code nvarchar(50) NOT NULL, NameAr nvarchar(100) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.PlatformRiderAccounts (Id uniqueidentifier NOT NULL PRIMARY KEY, ClientPlatformId uniqueidentifier NOT NULL, Code nvarchar(50) NULL, ExternalAccountId nvarchar(100) NULL, SponsorId uniqueidentifier NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.PlatformAccountBusinessDetails (Id uniqueidentifier NOT NULL PRIMARY KEY, PlatformRiderAccountId uniqueidentifier NOT NULL, SettlementModel nvarchar(100) NULL, OperationalDisposition nvarchar(100) NULL, DispositionReason nvarchar(400) NULL, DeferredUntil date NULL, SourceActualRiderProfileId uniqueidentifier NULL, UpdatedAtUtc datetimeoffset NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE keeta.vw_keeta_eligibility_v1 (
                SnapshotId uniqueidentifier NOT NULL,
                PlatformRiderAccountId uniqueidentifier NOT NULL,
                RiderProfileId uniqueidentifier NULL,
                SponsorId uniqueidentifier NULL,
                OperatingCityId uniqueidentifier NULL,
                Month date NOT NULL,
                DataThrough date NULL,
                Source nvarchar(50) NULL,
                RuleSetId uniqueidentifier NULL,
                RuleVersion int NULL,
                Status nvarchar(50) NULL,
                ValidDays int NULL,
                OrdersDelivered int NULL,
                ExpectedExperienceTier nvarchar(50) NULL,
                ExpectedCapacityTier nvarchar(50) NULL,
                ResultJson nvarchar(max) NULL,
                CreatedAtUtc datetimeoffset NOT NULL
            );
            CREATE TABLE finance_read.vw_hunger_financial_events_v1 (
                EventId uniqueidentifier NOT NULL,
                ClientPlatformId uniqueidentifier NULL,
                SupervisorEmployeeId uniqueidentifier NULL,
                PlatformRiderAccountId uniqueidentifier NULL,
                RiderProfileId uniqueidentifier NULL,
                RelatedEventId uniqueidentifier NULL,
                SupersedesEventId uniqueidentifier NULL,
                Kind nvarchar(100) NULL,
                Status nvarchar(100) NULL,
                Role nvarchar(100) NULL,
                Amount decimal(18,2) NULL,
                RiderBalanceChange decimal(18,2) NULL,
                BusinessDate date NULL,
                FromSubAccountId uniqueidentifier NULL,
                ToSubAccountId uniqueidentifier NULL,
                DestinationType nvarchar(100) NULL,
                DestinationChannel nvarchar(100) NULL,
                DestinationOwnerId uniqueidentifier NULL,
                ApprovalStatus nvarchar(100) NULL,
                ApprovedAtUtc datetimeoffset NULL,
                ApprovedByUserId uniqueidentifier NULL,
                RejectionReason nvarchar(400) NULL,
                SealAccountsJson nvarchar(max) NULL,
                ExceptionApprovalId uniqueidentifier NULL,
                IsSealed bit NULL,
                ExpenseLabel varchar(100) NULL,
                EvidenceReference nvarchar(200) NULL,
                CreatedAtUtc datetimeoffset NULL,
                CreatedByUserId uniqueidentifier NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    static void Seed(SqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO app.Sponsors(Id,RegistryNameAr,UnifiedNationalNumber) VALUES (@s,N'Synthetic Sponsor',N'7000000001');
            INSERT INTO app.Employees(Id,SponsorId,FullNameAr,IqamaNo) VALUES (@e,@s,N'Mandoub Sinthetic',N'2123456789');
            INSERT INTO app.RiderProfiles(Id,EmployeeId) VALUES (@p,@e);
            INSERT INTO app.Vehicles(Id,AssetNumber,PlateNumberAr,VehicleType,SponsorId) VALUES (@v,N'A-1',N'أ ب ج 1234',2,@s);
            INSERT INTO app.RiderVehicleAssignments(Id,RiderProfileId,VehicleId,StartedAtUtc,EndedAtUtc,IsRealRider)
              VALUES (@a,@p,@v,'2026-10-01T00:00:00+03:00','2026-10-31T00:00:00+03:00',1);
            INSERT INTO app.Housing(Id,Code,NameAr) VALUES (@h,N'H-1',N'Sakan Sinthetic');
            INSERT INTO app.HousingRooms(Id,HousingId,Name) VALUES (@r,@h,N'Room 1');
            INSERT INTO app.HousingResidencePeriods(Id,EmployeeId,RoomId,EffectiveFrom,EffectiveTo)
              VALUES (@res,@e,@r,'2026-10-01','2026-10-15');
            INSERT INTO app.FuelCards(Id,Provider,CardNumber,PlateNumberText,SponsorId) VALUES (@fc,1,N'CARD-1',N'أ ب ج 1234',@s);
            INSERT INTO app.FuelCardMonthlyUsages(Id,FuelCardId,EmployeeId,RiderProfileId,ReportMonth,TotalLiters,AmountBeforeTax,VatAmount,TotalAmount,TransactionCount,FuelType)
              VALUES (@fu,@fc,@e,@p,'2026-10-01',40,69.57,10.43,80,3,N'91');
            INSERT INTO platform.ClientPlatforms(Id,Code,NameAr) VALUES (@cp,N'KEETA',N'Keeta');
            INSERT INTO app.PlatformRiderAccounts(Id,ClientPlatformId,Code,ExternalAccountId,SponsorId) VALUES (@acc,@cp,N'K-1',N'1000000000000001',@s);
            INSERT INTO app.PlatformAccountBusinessDetails(Id,PlatformRiderAccountId,SettlementModel,OperationalDisposition,UpdatedAtUtc)
              VALUES (@bd,@acc,N'Slab',N'WalletPending',SYSUTCDATETIME());
            INSERT INTO keeta.vw_keeta_eligibility_v1(SnapshotId,PlatformRiderAccountId,RiderProfileId,SponsorId,Month,DataThrough,Source,RuleVersion,Status,ValidDays,OrdersDelivered,ResultJson,CreatedAtUtc)
              VALUES (@snap,@acc,@p,@s,'2026-11-01','2026-11-20',N'Vda',1,N'NotValid',20,100,
                      N'{"validDays":20,"invalidDays":5,"allowedLossDays":4,"requiredValidDays":22,"ordersDelivered":100,"monthlyOrdersRequired":600,"ordersRequiredSoFar":400}',
                      SYSUTCDATETIME());
            INSERT INTO finance_read.vw_hunger_financial_events_v1(EventId,PlatformRiderAccountId,Kind,Status,ApprovalStatus,Amount,BusinessDate,ApprovedAtUtc,CreatedAtUtc)
              VALUES (@ev,@acc,N'TopUp',N'Posted',N'Sealed',25.00,'2026-10-15',SYSUTCDATETIME(),SYSUTCDATETIME());
            """;
        void P(string n, Guid v) { var p = cmd.CreateParameter(); p.ParameterName = n; p.Value = v; cmd.Parameters.Add(p); }
        P("@s", SponsorA); P("@e", EmpIn); P("@p", ProfileIn); P("@v", VehicleId); P("@a", AssignmentId);
        P("@h", HousingId); P("@r", RoomId); P("@res", ResidenceId); P("@fc", FuelCardId); P("@fu", FuelUsageId);
        P("@cp", PlatformId); P("@acc", AccountId); P("@bd", DetailsId); P("@snap", SnapshotId); P("@ev", EventId);
        cmd.ExecuteNonQuery();
    }
}
