using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class AuditPagingTests
{
    static IQueryCollection Q(params (string Key, string Value)[] values) =>
        new QueryCollection(values.ToDictionary(x => x.Key, x => (StringValues)x.Value));

    static JsonObject Row(string id, string action, string actor, string at) => new()
    {
        ["id"] = id,
        ["action"] = action,
        ["actor"] = actor,
        ["at"] = at,
        ["entityId"] = "entity-" + id
    };

    static void Seed(Store store, int count, string actionPrefix = "synthetic.audit")
    {
        store.Write((c, tx) =>
        {
            for (int i = 0; i < count; i++)
            {
                string at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
                    .AddMinutes(i)
                    .ToString("O", CultureInfo.InvariantCulture);
                Store.Save(c, "audit", Row($"a{i:D4}", $"{actionPrefix}.{i % 7}", "synthetic-actor", at), tx);
            }
            return true;
        });
    }

    static void WithSqlite(Action<Store> action)
    {
        string path = Path.Combine(Path.GetTempPath(), "gate-audit-page-" + Guid.NewGuid().ToString("N"));
        try { action(new Store(path)); }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(path)) Directory.Delete(path, true); }
    }

    static void WithSqlServer(Action<Store> action)
    {
        string? configured = Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Inconclusive("SQL Server audit paging requires PAYROLL_TEST_SQL_CONNECTION.");
            return;
        }
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(master.DataSource, @"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        string name = "GateAuditPage_" + Guid.NewGuid().ToString("N");
        string dir = Path.Combine(Path.GetTempPath(), name);
        using var admin = new SqlConnection(master.ConnectionString);
        admin.Open();
        using (var create = admin.CreateCommand()) { create.CommandText = "CREATE DATABASE [" + name + "]"; create.ExecuteNonQuery(); }
        try
        {
            var target = new SqlConnectionStringBuilder(configured) { InitialCatalog = name };
            action(new Store(dir, target.ConnectionString));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var drop = admin.CreateCommand();
            drop.CommandText = "ALTER DATABASE [" + name + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + name + "]";
            drop.ExecuteNonQuery();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    static void OnProvider(bool sqlServer, Action<Store> action)
    {
        if (sqlServer) WithSqlServer(action);
        else WithSqlite(action);
    }

    static JsonObject AsPage(object result) =>
        JsonSerializer.SerializeToNode(result)!.AsObject();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AlwaysPages_StableSort_AndCapsPageSize(bool sqlServer)
    {
        OnProvider(sqlServer, store =>
        {
            Seed(store, 250);
            var page1 = AsPage(store.Read(c => AuditQuery.ListPaged(c, Q(("page", "1"), ("pageSize", "50")))));
            Assert.AreEqual(250, page1["total"]!.GetValue<long>());
            Assert.AreEqual(1, page1["page"]!.GetValue<int>());
            Assert.AreEqual(50, page1["pageSize"]!.GetValue<int>());
            var items1 = page1["items"]!.AsArray();
            Assert.AreEqual(50, items1.Count);
            // Newest first (stable: at DESC, id DESC).
            Assert.AreEqual("a0249", items1[0]!["id"]!.GetValue<string>());
            Assert.AreEqual("a0200", items1[49]!["id"]!.GetValue<string>());

            var page2 = AsPage(store.Read(c => AuditQuery.ListPaged(c, Q(("page", "2"), ("pageSize", "50")))));
            Assert.AreEqual("a0199", page2["items"]![0]!["id"]!.GetValue<string>());

            var capped = AsPage(store.Read(c => AuditQuery.ListPaged(c, Q(("page", "1"), ("pageSize", "999")))));
            Assert.AreEqual(200, capped["pageSize"]!.GetValue<int>());
            Assert.AreEqual(200, capped["items"]!.AsArray().Count);

            // Default page when omitted.
            var defaults = AsPage(store.Read(c => AuditQuery.ListPaged(c, Q())));
            Assert.AreEqual(1, defaults["page"]!.GetValue<int>());
            Assert.AreEqual(50, defaults["pageSize"]!.GetValue<int>());

            Assert.ThrowsExactly<ApiError>(() => store.Read(c => AuditQuery.ListPaged(c, Q(("page", "0")))));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FiltersFromToActionAndSearchInSql(bool sqlServer)
    {
        OnProvider(sqlServer, store =>
        {
            store.Write((c, tx) =>
            {
                Store.Save(c, "audit", Row("early", "rider.save", "alice", "2026-02-01T10:00:00.0000000+00:00"), tx);
                Store.Save(c, "audit", Row("mid", "admin.user_update", "bob", "2026-02-15T12:00:00.0000000+00:00"), tx);
                Store.Save(c, "audit", Row("late", "rider.save", "carol", "2026-02-28T18:00:00.0000000+00:00"), tx);
                return true;
            });

            var ranged = AsPage(store.Read(c => AuditQuery.ListPaged(c, Q(
                ("page", "1"),
                ("from", "2026-02-10T00:00:00Z"),
                ("to", "2026-02-20T00:00:00Z")))));
            Assert.AreEqual(1, ranged["total"]!.GetValue<long>());
            Assert.AreEqual("mid", ranged["items"]![0]!["id"]!.GetValue<string>());

            var byAction = AsPage(store.Read(c => AuditQuery.ListPaged(c, Q(("page", "1"), ("action", "rider.save")))));
            Assert.AreEqual(2, byAction["total"]!.GetValue<long>());

            var byQ = AsPage(store.Read(c => AuditQuery.ListPaged(c, Q(("page", "1"), ("q", "bob")))));
            Assert.AreEqual(1, byQ["total"]!.GetValue<long>());
            Assert.AreEqual("mid", byQ["items"]![0]!["id"]!.GetValue<string>());
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ManySyntheticRows_SecondPageDoesNotReloadWholeTableShape(bool sqlServer)
    {
        OnProvider(sqlServer, store =>
        {
            const int n = 1200;
            Seed(store, n, "bulk.audit");
            var page = AsPage(store.Read(c => AuditQuery.ListPaged(c, Q(("page", "5"), ("pageSize", "100")))));
            Assert.AreEqual(n, page["total"]!.GetValue<long>());
            Assert.AreEqual(100, page["items"]!.AsArray().Count);
            Assert.AreEqual(5, page["page"]!.GetValue<int>());
            // Page 5 (0-based offset 400) → ids a0799..a0700 with newest-first seed.
            Assert.AreEqual("a0799", page["items"]![0]!["id"]!.GetValue<string>());
            Assert.AreEqual("a0700", page["items"]![99]!["id"]!.GetValue<string>());
        });
    }
}
