using ErpImport;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Terminated list platform scope against a throwaway SQL Server DB.
/// Runs only when PAYROLL_TEST_SQL_CONNECTION is set on loopback.
/// </summary>
[TestClass]
public sealed class WorkforceCasesSqlServerTests
{
    static readonly Guid SponsorA = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    static readonly Guid PlatformJahez = Guid.Parse("b1111111-1111-1111-1111-111111111111");
    static readonly Guid PlatformKeeta = Guid.Parse("b2222222-2222-2222-2222-222222222222");
    static readonly Guid EmpJahez = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    static readonly Guid EmpKeeta = Guid.Parse("c2222222-2222-2222-2222-222222222222");
    static readonly Guid ProfileJahez = Guid.Parse("d1111111-1111-1111-1111-111111111111");
    static readonly Guid ProfileKeeta = Guid.Parse("d2222222-2222-2222-2222-222222222222");
    static readonly Guid AccountJahez = Guid.Parse("e1111111-1111-1111-1111-111111111111");
    static readonly Guid AccountKeeta = Guid.Parse("e2222222-2222-2222-2222-222222222222");

    [TestMethod]
    public void TerminatedAndFleeing_PlatformRestricted_ReturnsOnlyMatchingPlatform()
    {
        string? configured = Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Inconclusive("Workforce cases live SQL requires PAYROLL_TEST_SQL_CONNECTION.");
            return;
        }
        var options = new SqlConnectionStringBuilder(configured);
        if (!System.Text.RegularExpressions.Regex.IsMatch(
            options.DataSource,
            @"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            Assert.Inconclusive("Workforce cases live SQL requires loopback, not staging/production.");
            return;
        }

        string name = "GateWfCases_" + Guid.NewGuid().ToString("N");
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
            var jahezOnly = new LiveOperationsQuery.Scope(true, [], [], [], ["JAHEZ"], PlatformRestricted: true);

            var allRows = WorkforceLiveQuery.TerminatedAndFleeing(conn, all);
            Assert.AreEqual(2, allRows.Count);

            var scoped = WorkforceLiveQuery.TerminatedAndFleeing(conn, jahezOnly);
            Assert.AreEqual(1, scoped.Count);
            Assert.AreEqual(EmpJahez.ToString("D"), scoped[0]["id"]?.GetValue<string>(), ignoreCase: true);
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
        Exec(conn, """
            CREATE TABLE app.Sponsors (
                Id uniqueidentifier NOT NULL PRIMARY KEY,
                RegistryNameAr nvarchar(200) NULL,
                UnifiedNationalNumber nvarchar(20) NULL,
                IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.OperationalWorkTypes (Id uniqueidentifier NOT NULL PRIMARY KEY, NameAr nvarchar(200) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.OperatingCities (Id uniqueidentifier NOT NULL PRIMARY KEY, GlobalCityId uniqueidentifier NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE platform.GlobalCities (Id uniqueidentifier NOT NULL PRIMARY KEY, NameAr nvarchar(200) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.Employees (
                Id uniqueidentifier NOT NULL PRIMARY KEY,
                SponsorId uniqueidentifier NULL,
                OperationalWorkTypeId uniqueidentifier NULL,
                OperatingCityId uniqueidentifier NULL,
                IqamaNo nvarchar(20) NULL,
                FullNameAr nvarchar(200) NULL,
                Nationality nvarchar(50) NULL,
                PrimaryPhone nvarchar(30) NULL,
                Status int NOT NULL DEFAULT 3,
                TerminationDate date NULL,
                StatusReason nvarchar(200) NULL,
                IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE platform.ClientPlatforms (
                Id uniqueidentifier NOT NULL PRIMARY KEY,
                Code nvarchar(50) NULL,
                NameAr nvarchar(200) NULL,
                IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.RiderProfiles (
                Id uniqueidentifier NOT NULL PRIMARY KEY,
                EmployeeId uniqueidentifier NOT NULL,
                IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.PlatformRiderAccounts (
                Id uniqueidentifier NOT NULL PRIMARY KEY,
                ClientPlatformId uniqueidentifier NOT NULL,
                IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.RiderClientAssignments (
                Id uniqueidentifier NOT NULL PRIMARY KEY,
                RiderProfileId uniqueidentifier NOT NULL,
                PlatformRiderAccountId uniqueidentifier NOT NULL,
                EffectiveTo date NULL,
                IsDeleted bit NOT NULL DEFAULT 0);
            """);
    }

    static void Seed(SqlConnection conn)
    {
        Exec(conn, $"""
            INSERT INTO app.Sponsors(Id,RegistryNameAr,UnifiedNationalNumber) VALUES ('{SponsorA}',N'شركة تجريبية','7000000001');
            INSERT INTO platform.ClientPlatforms(Id,Code,NameAr) VALUES
              ('{PlatformJahez}','JAHEZ',N'جاهز'),
              ('{PlatformKeeta}','KEETA',N'كيتا');
            INSERT INTO app.Employees(Id,SponsorId,IqamaNo,FullNameAr,Status,TerminationDate,IsDeleted) VALUES
              ('{EmpJahez}','{SponsorA}','2000000101',N'منتهٍ جاهز',6,'2026-10-01',0),
              ('{EmpKeeta}','{SponsorA}','2000000102',N'منتهٍ كيتا',6,'2026-10-02',0);
            INSERT INTO app.RiderProfiles(Id,EmployeeId) VALUES
              ('{ProfileJahez}','{EmpJahez}'),
              ('{ProfileKeeta}','{EmpKeeta}');
            INSERT INTO app.PlatformRiderAccounts(Id,ClientPlatformId) VALUES
              ('{AccountJahez}','{PlatformJahez}'),
              ('{AccountKeeta}','{PlatformKeeta}');
            -- Ended assignments (terminated people usually have no current one).
            INSERT INTO app.RiderClientAssignments(Id,RiderProfileId,PlatformRiderAccountId,EffectiveTo) VALUES
              (NEWID(),'{ProfileJahez}','{AccountJahez}','2026-09-30'),
              (NEWID(),'{ProfileKeeta}','{AccountKeeta}','2026-09-30');
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
