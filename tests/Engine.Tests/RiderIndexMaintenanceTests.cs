using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class RiderIndexMaintenanceTests
{
    static readonly ClaimsPrincipal Admin = UserAccess.Principal(new()
    {
        ["id"] = "syn-admin",
        ["username"] = "syn-admin",
        ["role"] = "Admin"
    });
    static readonly ClaimsPrincipal Reviewer = UserAccess.Principal(new()
    {
        ["id"] = "syn-reviewer",
        ["username"] = "syn-reviewer",
        ["role"] = "Reviewer"
    });

    [TestCleanup]
    public void Cleanup() => RiderIndexMaintenance.ResetForTests();

    [TestMethod]
    public void Sqlite_StartupSafe_JobIdempotent_StatusAndConcurrentRefuse_QueriesStable()
    {
        string dir = Path.Combine(Path.GetTempPath(), "m4f-sqlite-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(dir);
            Assert.AreEqual("sqlite", store.Provider);
            SeedRiderRows(store);

            var beforeDebts = store.Read(c => Store.ListWhere(c, "debt", null, null, ("iqama", "2000000001")));
            var beforeBroadcasts = store.Read(c => Store.ListBroadcastsForRider(c, "2000000001", 10));
            Assert.AreEqual(1, beforeDebts.Count);
            Assert.AreEqual(1, beforeBroadcasts.Count);

            var pending = RiderIndexMaintenance.ReadStatus(store, Admin);
            Assert.IsFalse(pending.B("running"));
            Assert.IsTrue(((JsonArray)pending["allowedActions"]!).Any(a => a?.ToString() == "run"));

            RiderIndexMaintenance.RunSynchronously(store, timeout: 60, minData: 0, minLog: 0);
            var after = RiderIndexMaintenance.ReadStatus(store, Admin);
            Assert.IsFalse(after.B("running"));
            foreach (var row in ((JsonArray)after["indexes"]!).OfType<JsonObject>())
                Assert.AreEqual("done", row.S("status"), row.S("name"));

            RiderIndexMaintenance.RunSynchronously(store, timeout: 60, minData: 0, minLog: 0); // idempotent

            // Simulate concurrent run.
            typeof(RiderIndexMaintenance).GetField("_running", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .SetValue(null, 1);
            Assert.AreEqual(409, Assert.ThrowsExactly<ApiError>(() => RiderIndexMaintenance.RunSynchronously(store)).Status);
            RiderIndexMaintenance.ResetForTests();

            var afterDebts = store.Read(c => Store.ListWhere(c, "debt", null, null, ("iqama", "2000000001")));
            var afterBroadcasts = store.Read(c => Store.ListBroadcastsForRider(c, "2000000001", 10));
            Assert.AreEqual(beforeDebts.Count, afterDebts.Count);
            Assert.AreEqual(beforeDebts[0].S("id"), afterDebts[0].S("id"));
            Assert.AreEqual(beforeBroadcasts[0].S("id"), afterBroadcasts[0].S("id"));

            Assert.IsFalse(UserAccess.Allowed("Reviewer", "GET", "/api/system/maintenance/rider-indexes"));
            Assert.IsFalse(UserAccess.Allowed("Reviewer", "POST", "/api/system/maintenance/rider-indexes"));
            Assert.IsTrue(AccessPolicy.Administrator(Admin));
            Assert.IsFalse(AccessPolicy.Administrator(Reviewer));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            RiderIndexMaintenance.ResetForTests();
        }
    }

    [TestMethod]
    public void SqlServer_StartupCreatesNothing_JobCreatesIndexes_Idempotent_ConcurrentRefuse_QueriesMatch()
    {
        string? configured = Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Inconclusive("SQL integration requires PAYROLL_TEST_SQL_CONNECTION (gp-m4f-sql).");
            return;
        }
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(master.DataSource, @"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        string name = "GateM4F_" + Guid.NewGuid().ToString("N");
        string dir = Path.Combine(Path.GetTempPath(), name);
        using var admin = new SqlConnection(master.ConnectionString);
        admin.Open();
        using (var create = admin.CreateCommand())
        {
            create.CommandText = "CREATE DATABASE [" + name + "]";
            create.ExecuteNonQuery();
        }
        try
        {
            var target = new SqlConnectionStringBuilder(configured) { InitialCatalog = name };
            var store = new Store(dir, target.ConnectionString);
            Assert.AreEqual("mssql", store.Provider);
            store.Read(c =>
            {
                Assert.IsFalse(RiderDataIndexes.AnyRiderColumnPresent(c), "startup must not create rider columns");
                Assert.IsFalse(RiderDataIndexes.AnyRiderIndexPresent(c), "startup must not create rider indexes");
                return true;
            });

            SeedRiderRows(store);
            var beforeDebts = store.Read(c => Store.ListWhere(c, "debt", null, null, ("iqama", "2000000001")));
            var beforeForms = store.Read(c => Store.ListOpenFormsForRider(c, "campaign", "2000000001", ["COMP-A"], DateTimeOffset.UtcNow.ToString("O")));
            Assert.AreEqual(1, beforeDebts.Count);
            Assert.AreEqual(1, beforeForms.Count);

            RiderIndexMaintenance.RunSynchronously(store, timeout: 120, minData: 0, minLog: 0);
            store.Read(c =>
            {
                Assert.IsTrue(RiderDataIndexes.AnyRiderColumnPresent(c));
                Assert.IsTrue(RiderDataIndexes.AnyRiderIndexPresent(c));
                foreach (var index in RiderDataIndexes.Indexes)
                {
                    Assert.IsTrue(RiderDataIndexes.IndexMatches(c, index), index.Name);
                    Assert.IsTrue(RiderDataIndexes.StepCompleted(c, index.Name, RiderDataIndexes.DefinitionHash(index)), index.Name);
                }
                return true;
            });

            var status = RiderIndexMaintenance.ReadStatus(store, Admin);
            foreach (var row in ((JsonArray)status["indexes"]!).OfType<JsonObject>())
                Assert.AreEqual("done", row.S("status"), row.S("name"));

            RiderIndexMaintenance.RunSynchronously(store, timeout: 120, minData: 0, minLog: 0); // idempotent

            // Concurrent refuse via in-process running flag.
            typeof(RiderIndexMaintenance).GetField("_running", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .SetValue(null, 1);
            Assert.AreEqual(409, Assert.ThrowsExactly<ApiError>(() => RiderIndexMaintenance.RunSynchronously(store)).Status);
            RiderIndexMaintenance.ResetForTests();

            // Concurrent refuse via session app lock held on another connection.
            using (var locker = new SqlConnection(target.ConnectionString))
            {
                locker.Open();
                using (var lockCmd = locker.CreateCommand())
                {
                    lockCmd.CommandText = """
                        DECLARE @result int;
                        EXEC @result = sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0;
                        SELECT @result;
                        """;
                    var p = lockCmd.CreateParameter(); p.ParameterName = "@resource"; p.Value = RiderDataIndexes.AppLockResource; lockCmd.Parameters.Add(p);
                    Assert.IsTrue(Convert.ToInt32(lockCmd.ExecuteScalar()) >= 0);
                }
                Assert.AreEqual(409, Assert.ThrowsExactly<ApiError>(() => RiderIndexMaintenance.RunSynchronously(store)).Status);
                using (var unlock = locker.CreateCommand())
                {
                    unlock.CommandText = "EXEC sp_releaseapplock @Resource=@resource, @LockOwner='Session';";
                    var p = unlock.CreateParameter(); p.ParameterName = "@resource"; p.Value = RiderDataIndexes.AppLockResource; unlock.Parameters.Add(p);
                    unlock.ExecuteNonQuery();
                }
            }

            var afterDebts = store.Read(c => Store.ListWhere(c, "debt", null, null, ("iqama", "2000000001")));
            var afterForms = store.Read(c => Store.ListOpenFormsForRider(c, "campaign", "2000000001", ["COMP-A"], DateTimeOffset.UtcNow.ToString("O")));
            Assert.AreEqual(beforeDebts[0].S("id"), afterDebts[0].S("id"));
            Assert.AreEqual(beforeForms[0].S("id"), afterForms[0].S("id"));
            Assert.AreEqual(beforeDebts[0].S("status"), afterDebts[0].S("status"));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var drop = admin.CreateCommand();
            drop.CommandText = "ALTER DATABASE [" + name + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + name + "]";
            drop.ExecuteNonQuery();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            RiderIndexMaintenance.ResetForTests();
        }
    }

    static void SeedRiderRows(Store store)
    {
        string future = DateTimeOffset.UtcNow.AddDays(7).ToString("O");
        store.Write((c, tx) =>
        {
            Store.Save(c, "debt", new()
            {
                ["id"] = "debt-1",
                ["iqama"] = "2000000001",
                ["status"] = "active",
                ["totalAmount"] = 10,
                ["remainingBalance"] = 10,
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
            }, tx);
            Store.Save(c, "mobile_broadcast", new()
            {
                ["id"] = "bc-1",
                ["targetIqama"] = "2000000001",
                ["titleAr"] = "تنبيه",
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
            }, tx);
            Store.Save(c, "campaign", new()
            {
                ["id"] = "camp-1",
                ["status"] = "open",
                ["expiresAt"] = future,
                ["target"] = "ALL",
                ["targetIqama"] = "ALL",
                ["companyId"] = "COMP-A",
                ["sponsorId"] = "",
                ["targetCompanyId"] = "",
                ["targetSponsorId"] = "",
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
            }, tx);
            return true;
        });
    }
}
