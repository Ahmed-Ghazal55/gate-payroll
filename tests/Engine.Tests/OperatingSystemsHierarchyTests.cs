using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class OperatingSystemsHierarchyTests
{
    static void WithStore(Action<Store> body)
    {
        string dir = Path.Combine(Path.GetTempPath(), "gate-opsys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { body(new Store(dir)); }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void SeedCreatesTwoParentsSixLeavesAndOldCodesResolve()
    {
        WithStore(store =>
        {
            ConfigLists.Seed(store);

            var items = store.Read(c => ConfigLists.Items(c, null, "operating_systems"));
            Assert.IsTrue(items.Any(i => i.S("code") == "kafala"));
            Assert.IsTrue(items.Any(i => i.S("code") == "external"));
            Assert.AreEqual("parent", store.Read(c => ConfigLists.Current(c, null, "operating_systems", "kafala"))!["values"].S("kind"));
            Assert.AreEqual("parent", store.Read(c => ConfigLists.Current(c, null, "operating_systems", "external"))!["values"].S("kind"));

            foreach (var code in new[]
                     {
                         "kafala.own_vehicle", "kafala.company_car", "kafala.company_bike",
                         "external.own_vehicle", "external.company_car", "external.company_bike"
                     })
            {
                var cur = store.Read(c => ConfigLists.Current(c, null, "operating_systems", code));
                Assert.IsNotNull(cur, code);
                Assert.AreEqual("leaf", cur!["values"].S("kind"), code);
            }

            Assert.AreEqual("own_vehicle", ConfigLists.CanonicalOperatingSystemCode("own_car"));
            Assert.AreEqual("company_bike", ConfigLists.CanonicalOperatingSystemCode("bike"));

            var resolvedOwn = store.Read(c => ConfigLists.ResolveOperatingSystem(c, null, "own_car"));
            Assert.IsNotNull(resolvedOwn);
            Assert.AreEqual("vehicle_mode", resolvedOwn!["values"].S("kind"));

            var resolvedBike = store.Read(c => ConfigLists.ResolveOperatingSystem(c, null, "bike"));
            Assert.IsNotNull(resolvedBike);
            Assert.AreEqual("vehicle_mode", resolvedBike!["values"].S("kind"));

            Assert.AreEqual(2000m, store.Read(c => ConfigLists.Current(c, null, "operating_systems", "kafala.own_vehicle"))!["values"].N("operatingCostD"));
            Assert.AreEqual(3500m, store.Read(c => ConfigLists.Current(c, null, "operating_systems", "kafala.company_car"))!["values"].N("operatingCostD"));
            Assert.AreEqual(1000m, store.Read(c => ConfigLists.Current(c, null, "operating_systems", "external.own_vehicle"))!["values"].N("operatingCostD"));
        });
    }
}
