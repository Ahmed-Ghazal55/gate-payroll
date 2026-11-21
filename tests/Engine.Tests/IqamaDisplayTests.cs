using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>H27: iqama is shown in full in tables, 360, search and exports — never hash-only or masked.</summary>
[TestClass]
public sealed class IqamaDisplayTests
{
    const string FullIqama = "2000000999";

    static ClaimsPrincipal Viewer => UserAccess.Principal(new()
    {
        ["username"] = "synthetic-iqama-viewer",
        ["role"] = "Admin"
    });

    [TestMethod]
    public void RiderViewKeepsFullIqama()
    {
        var rider = new JsonObject
        {
            ["id"] = FullIqama,
            ["iqama"] = FullIqama,
            ["name"] = "Synthetic Rider",
            ["status"] = "Active"
        };
        var view = UserAccess.RiderView(rider);
        Assert.AreEqual(FullIqama, view.S("iqama"));
        Assert.AreEqual(FullIqama, view.S("id"));
        Assert.IsFalse(view.S("iqama").Contains('*', StringComparison.Ordinal));
    }

    [TestMethod]
    public void GlobalSearchMatchesAndReturnsFullIqama()
    {
        string path = Path.Combine(Path.GetTempPath(), "gate-iqama-display-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(path);
            store.Write((c, tx) =>
            {
                Store.Save(c, "rider", new JsonObject
                {
                    ["id"] = FullIqama,
                    ["iqama"] = FullIqama,
                    ["name"] = "Synthetic Iqama Rider",
                    ["status"] = "Active",
                    ["isDeliveryRider"] = true,
                    ["personRole"] = "rider"
                }, tx);
                return true;
            });
            var hits = store.Read(c => GlobalSearch.Search(c, Viewer, FullIqama).ToList());
            Assert.IsTrue(hits.Count >= 1);
            Assert.IsTrue(hits.Any(h => h.Subtitle == FullIqama || h.Id == FullIqama));
            Assert.IsFalse(hits.Any(h => (h.Subtitle ?? "").Contains('*', StringComparison.Ordinal)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }
}
