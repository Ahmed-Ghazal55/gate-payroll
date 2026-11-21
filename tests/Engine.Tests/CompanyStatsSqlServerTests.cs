using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Executes CompanyStatsLiveQuery.Read against a throwaway SQL Server database.
/// Runs only when PAYROLL_TEST_SQL_CONNECTION is set on loopback.
/// </summary>
[TestClass]
public sealed class CompanyStatsSqlServerTests
{
    static readonly Guid SponsorA = Guid.Parse("31111111-1111-1111-1111-111111111111");
    static readonly Guid SponsorB = Guid.Parse("32222222-2222-2222-2222-222222222222");
    static readonly Guid PlatformJahez = Guid.Parse("41111111-1111-1111-1111-111111111111");
    static readonly Guid PlatformKeeta = Guid.Parse("42222222-2222-2222-2222-222222222222");
    static readonly Guid AccountJahezAssigned = Guid.Parse("51111111-1111-1111-1111-111111111111");
    static readonly Guid AccountKeetaAvail = Guid.Parse("52222222-2222-2222-2222-222222222222");
    static readonly Guid AccountBKeeta = Guid.Parse("53333333-3333-3333-3333-333333333333");

    [TestMethod]
    public void CompanyStatsLiveQuery_ExecutesOnThrowawaySqlServer()
    {
        string? configured = Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Inconclusive("Company stats live SQL requires PAYROLL_TEST_SQL_CONNECTION.");
            return;
        }
        var options = new SqlConnectionStringBuilder(configured);
        if (!System.Text.RegularExpressions.Regex.IsMatch(
            options.DataSource,
            @"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            Assert.Inconclusive("Company stats live SQL requires loopback, not staging/production.");
            return;
        }

        string name = "GateCoStats_" + Guid.NewGuid().ToString("N");
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

            // 1. All companies, unrestricted platforms
            var allScope = new LiveOperationsQuery.Scope(true, [], [], [], []);
            var all = CompanyStatsLiveQuery.Read(conn, allScope);
            Assert.AreEqual(2, all.Rows.Count);
            var rowA = all.Rows.Single(r => r["unifiedNumber"]!.GetValue<string>() == "7000000001");
            Assert.AreEqual(3L, rowA["sponsoredTotal"]!.GetValue<long>());
            Assert.AreEqual(2L, rowA["sponsoredRiders"]!.GetValue<long>());
            Assert.AreEqual(1L, rowA["sponsoredAdmins"]!.GetValue<long>());
            Assert.AreEqual(2L, rowA["sponsoredActive"]!.GetValue<long>());
            Assert.AreEqual(1L, rowA["sponsoredOnLeave"]!.GetValue<long>());
            Assert.AreEqual(2L, rowA["platformAccountsTotal"]!.GetValue<long>());
            Assert.AreEqual(2L, rowA["platformAccountsActive"]!.GetValue<long>());
            Assert.AreEqual(1L, rowA["platformAccountsAssigned"]!.GetValue<long>());
            var byPlatA = (rowA["byPlatform"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            Assert.AreEqual(2, byPlatA.Count);

            // 2. Single company scope
            var oneCompany = new LiveOperationsQuery.Scope(false, ["7000000001"], [], [], []);
            var scoped = CompanyStatsLiveQuery.Read(conn, oneCompany);
            Assert.AreEqual(1, scoped.Rows.Count);
            Assert.AreEqual("7000000001", scoped.Rows[0]["unifiedNumber"]!.GetValue<string>());

            // 3. Platform-restricted scope (JAHEZ only)
            var jahezOnly = new LiveOperationsQuery.Scope(false, ["7000000001"], [], [], ["JAHEZ"], PlatformRestricted: true);
            var platScoped = CompanyStatsLiveQuery.Read(conn, jahezOnly);
            Assert.AreEqual(1, platScoped.Rows.Count);
            Assert.AreEqual(3L, platScoped.Rows[0]["sponsoredTotal"]!.GetValue<long>());
            Assert.AreEqual(1L, platScoped.Rows[0]["platformAccountsTotal"]!.GetValue<long>());
            var platRows = (platScoped.Rows[0]["byPlatform"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            Assert.AreEqual(1, platRows.Count);
            Assert.AreEqual("JAHEZ", platRows[0]["code"]!.GetValue<string>());

            // 4. Platform-restricted empty list -> 0 platform accounts
            var noPlat = new LiveOperationsQuery.Scope(false, ["7000000001"], [], [], [], PlatformRestricted: true);
            var noPlatResult = CompanyStatsLiveQuery.Read(conn, noPlat);
            Assert.AreEqual(1, noPlatResult.Rows.Count);
            Assert.AreEqual(0L, noPlatResult.Rows[0]["platformAccountsTotal"]!.GetValue<long>());
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
            CREATE TABLE app.Sponsors (Id uniqueidentifier NOT NULL PRIMARY KEY, RegistryNameAr nvarchar(200) NULL, UnifiedNationalNumber nvarchar(20) NULL, CommercialRegistrationNumber nvarchar(50) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.Employees (Id uniqueidentifier NOT NULL PRIMARY KEY, SponsorId uniqueidentifier NULL, EngagementType int NOT NULL DEFAULT 1, Status int NOT NULL DEFAULT 3, IsEmployee bit NOT NULL DEFAULT 0, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE platform.ClientPlatforms (Id uniqueidentifier NOT NULL PRIMARY KEY, Code nvarchar(50) NULL, NameAr nvarchar(200) NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.PlatformRiderAccounts (Id uniqueidentifier NOT NULL PRIMARY KEY, ClientPlatformId uniqueidentifier NOT NULL, SponsorId uniqueidentifier NULL, Status int NOT NULL, IsDeleted bit NOT NULL DEFAULT 0);
            CREATE TABLE app.RiderClientAssignments (Id uniqueidentifier NOT NULL PRIMARY KEY, PlatformRiderAccountId uniqueidentifier NOT NULL, EffectiveTo date NULL, IsDeleted bit NOT NULL DEFAULT 0);
            """);
    }

    static void Seed(SqlConnection conn)
    {
        Exec(conn, $"""
            INSERT INTO app.Sponsors(Id,RegistryNameAr,UnifiedNationalNumber,CommercialRegistrationNumber) VALUES
              ('{SponsorA}',N'شركة تجريبية أ','7000000001','4000000001'),
              ('{SponsorB}',N'شركة تجريبية ب','7000000002','4000000002');
            INSERT INTO app.Employees(Id,SponsorId,EngagementType,Status,IsEmployee,IsDeleted) VALUES
              (NEWID(),'{SponsorA}',1,3,0,0),
              (NEWID(),'{SponsorA}',1,5,0,0),
              (NEWID(),'{SponsorA}',1,3,1,0),
              (NEWID(),'{SponsorA}',2,3,0,0),
              (NEWID(),'{SponsorA}',1,3,0,1),
              (NEWID(),'{SponsorB}',1,3,0,0);
            INSERT INTO platform.ClientPlatforms(Id,Code,NameAr) VALUES
              ('{PlatformJahez}','JAHEZ',N'جاهز'),
              ('{PlatformKeeta}','KEETA',N'كيتا');
            INSERT INTO app.PlatformRiderAccounts(Id,ClientPlatformId,SponsorId,Status) VALUES
              ('{AccountJahezAssigned}','{PlatformJahez}','{SponsorA}',2),
              ('{AccountKeetaAvail}','{PlatformKeeta}','{SponsorA}',1),
              ('{AccountBKeeta}','{PlatformKeeta}','{SponsorB}',1);
            INSERT INTO app.RiderClientAssignments(Id,PlatformRiderAccountId,EffectiveTo) VALUES
              (NEWID(),'{AccountJahezAssigned}',NULL);
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
