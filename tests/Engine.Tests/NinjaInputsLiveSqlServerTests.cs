using ErpImport;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>Executes NinjaInputsLiveQuery against a throwaway SQL Server database.</summary>
[TestClass]
public sealed class NinjaInputsLiveSqlServerTests
{
    static readonly Guid SponsorA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    static readonly Guid SponsorB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");
    static readonly Guid PlatformNinja = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc1");
    static readonly Guid AccountA = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd1");
    static readonly Guid AccountB = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeee1");
    static readonly Guid EmpA = Guid.Parse("ffffffff-ffff-ffff-ffff-fffffffffff1");
    static readonly Guid ProfileA = Guid.Parse("12121212-1212-1212-1212-121212121211");
    static readonly Guid AssignA = Guid.Parse("13131313-1313-1313-1313-131313131311");
    static readonly Guid Perf1 = Guid.Parse("14141414-1414-1414-1414-141414141411");
    static readonly Guid Perf2 = Guid.Parse("15151515-1515-1515-1515-151515151511");
    static readonly Guid PerfOut = Guid.Parse("16161616-1616-1616-1616-161616161611");
    static readonly Guid Ded1 = Guid.Parse("17171717-1717-1717-1717-171717171711");

    [TestMethod]
    public void NinjaDailyPerformance_ExecutesOnThrowawaySqlServer()
    {
        string? configured = Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Inconclusive("Ninja live SQL requires PAYROLL_TEST_SQL_CONNECTION.");
            return;
        }
        var options = new SqlConnectionStringBuilder(configured);
        if (!System.Text.RegularExpressions.Regex.IsMatch(options.DataSource, @"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            Assert.Inconclusive("Ninja live SQL requires loopback, not staging/production.");
            return;
        }

        string name = "GateNinjaLive_" + Guid.NewGuid().ToString("N");
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
            var oneCompany = new LiveOperationsQuery.Scope(false, ["7000000001"], [], [], []);
            var period = new NinjaInputsLiveQuery.PeriodRequest(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 1, 50);

            var page = NinjaInputsLiveQuery.DailyPerformance(conn, all, period);
            Assert.AreEqual(3L, page.Total);
            Assert.AreEqual(3, page.Items.Count);
            Assert.AreEqual("2026-10-01", page.Coverage.S("firstDay"));
            Assert.AreEqual("2026-10-29", page.Coverage.S("lastDay"));
            Assert.IsTrue(page.Coverage.B("hasGapAtPeriodEnd"));
            Assert.IsTrue(page.Items.All(r => r.S("captainId").Length > 0));
            Assert.IsTrue(page.Items.Any(r => r.S("externalAccountId") == "OUT-SCOPE"));

            var scoped = NinjaInputsLiveQuery.DailyPerformance(conn, oneCompany, period);
            Assert.AreEqual(2L, scoped.Total);
            Assert.IsTrue(scoped.Items.All(r => r.S("ownerUnifiedNumber") == "7000000001"));
            Assert.IsFalse(scoped.Items.Any(r => r.S("externalAccountId") == "OUT-SCOPE"));

            var emptyScope = new LiveOperationsQuery.Scope(false, [], [], [], []);
            var blocked = NinjaInputsLiveQuery.DailyPerformance(conn, emptyScope, period);
            Assert.AreEqual(0L, blocked.Total);
            Assert.AreEqual(0, blocked.Items.Count);

            var deductions = NinjaInputsLiveQuery.DailyDeductions(conn, oneCompany, period);
            Assert.AreEqual(1L, deductions.Total);
            Assert.AreEqual(1, deductions.Items.Count);
            Assert.AreEqual(ProfileA.ToString("D"), deductions.Items[0].S("actualRiderProfileId"), ignoreCase: true);
            Assert.AreEqual(15m, deductions.Items[0]["deductionsAmount"]!.GetValue<decimal>());
            Assert.AreEqual("2026-10-01", deductions.Coverage.S("firstDay"));
            Assert.AreEqual("2026-10-01", deductions.Coverage.S("lastDay"));
            Assert.IsTrue(deductions.Coverage.B("hasGapAtPeriodEnd"));
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
        Exec(conn, "CREATE SCHEMA ninja");
        Exec(conn, """
            CREATE TABLE app.Sponsors (Id uniqueidentifier NOT NULL PRIMARY KEY, UnifiedNationalNumber nvarchar(20) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE platform.ClientPlatforms (Id uniqueidentifier NOT NULL PRIMARY KEY, Code nvarchar(50) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.Employees (Id uniqueidentifier NOT NULL PRIMARY KEY, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.RiderProfiles (Id uniqueidentifier NOT NULL PRIMARY KEY, EmployeeId uniqueidentifier NOT NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.PlatformRiderAccounts (
                Id uniqueidentifier NOT NULL PRIMARY KEY, ClientPlatformId uniqueidentifier NOT NULL,
                Code nvarchar(50) NULL, ExternalAccountId nvarchar(50) NULL, Status int NOT NULL DEFAULT 1,
                RegisteredEmployeeId uniqueidentifier NULL, PaymentModel int NULL, SponsorId uniqueidentifier NULL,
                IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.RiderClientAssignments (
                Id uniqueidentifier NOT NULL PRIMARY KEY, RiderProfileId uniqueidentifier NOT NULL,
                PlatformRiderAccountId uniqueidentifier NOT NULL, EffectiveFrom date NOT NULL, EffectiveTo date NULL,
                Status int NOT NULL DEFAULT 1, PaymentModel int NULL, NinjaRiderType int NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE ninja.NinjaDailyPerformance (
                Id uniqueidentifier NOT NULL PRIMARY KEY, PlatformRiderAccountId uniqueidentifier NOT NULL,
                RiderClientAssignmentId uniqueidentifier NULL, RiderProfileId uniqueidentifier NULL,
                SourceId uniqueidentifier NULL, ReportDate date NOT NULL, CaptainId nvarchar(50) NOT NULL,
                SourceRowNumber int NULL, TotalOrders decimal(18,2) NULL, TotalWorkingHours decimal(18,2) NULL);
            CREATE TABLE ninja.NinjaDailyDeductions (
                Id uniqueidentifier NOT NULL PRIMARY KEY, PlatformRiderAccountId uniqueidentifier NOT NULL,
                RiderClientAssignmentId uniqueidentifier NULL, RiderProfileId uniqueidentifier NULL,
                SourceId uniqueidentifier NULL, ReportDate date NOT NULL, CaptainId nvarchar(50) NOT NULL,
                SourceRowNumber int NULL, TotalCollectedAmountAtDelivery decimal(18,2) NULL, TotalDeductions decimal(18,2) NULL);
            """);
    }

    static void Seed(SqlConnection conn)
    {
        Exec(conn, $"INSERT INTO app.Sponsors(Id,UnifiedNationalNumber) VALUES('{SponsorA}','7000000001'),('{SponsorB}','7000000002')");
        Exec(conn, $"INSERT INTO platform.ClientPlatforms(Id,Code) VALUES('{PlatformNinja}','NINJA')");
        Exec(conn, $"INSERT INTO app.Employees(Id) VALUES('{EmpA}')");
        Exec(conn, $"INSERT INTO app.RiderProfiles(Id,EmployeeId) VALUES('{ProfileA}','{EmpA}')");
        Exec(conn, $"""
            INSERT INTO app.PlatformRiderAccounts(Id,ClientPlatformId,Code,ExternalAccountId,Status,RegisteredEmployeeId,PaymentModel,SponsorId)
            VALUES ('{AccountA}','{PlatformNinja}','A1','CAP-A',2,'{EmpA}',1,'{SponsorA}'),
                   ('{AccountB}','{PlatformNinja}','B1','OUT-SCOPE',2,'{EmpA}',2,'{SponsorB}')
            """);
        Exec(conn, $"""
            INSERT INTO app.RiderClientAssignments(Id,RiderProfileId,PlatformRiderAccountId,EffectiveFrom,EffectiveTo,PaymentModel)
            VALUES ('{AssignA}','{ProfileA}','{AccountA}','2026-10-01',NULL,1)
            """);
        Exec(conn, $"""
            INSERT INTO ninja.NinjaDailyPerformance(Id,PlatformRiderAccountId,RiderClientAssignmentId,RiderProfileId,ReportDate,CaptainId,SourceRowNumber,TotalOrders,TotalWorkingHours)
            VALUES ('{Perf1}','{AccountA}','{AssignA}','{ProfileA}','2026-10-01','CAP-A',1,10,8),
                   ('{Perf2}','{AccountA}','{AssignA}','{ProfileA}','2026-10-29','CAP-A',2,12,9),
                   ('{PerfOut}','{AccountB}',NULL,'{ProfileA}','2026-10-15','OUT-SCOPE',1,5,4)
            """);
        Exec(conn, $"""
            INSERT INTO ninja.NinjaDailyDeductions(Id,PlatformRiderAccountId,RiderClientAssignmentId,RiderProfileId,ReportDate,CaptainId,SourceRowNumber,TotalCollectedAmountAtDelivery,TotalDeductions)
            VALUES ('{Ded1}','{AccountA}','{AssignA}','{ProfileA}','2026-10-01','CAP-A',1,100,15)
            """);
    }

    static void Exec(SqlConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
