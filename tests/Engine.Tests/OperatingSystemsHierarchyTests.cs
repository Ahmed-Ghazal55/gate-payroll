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

    [TestMethod]
    public void HardDelete_CustomLeafWithoutReferences_SucceedsAndIsAudited()
    {
        WithStore(store =>
        {
            ConfigLists.Seed(store);
            var maker = PayFixture.Maker;
            store.Write((c, tx) =>
            {
                ConfigLists.Upsert(c, tx, maker, "operating_systems", new System.Text.Json.Nodes.JsonObject
                {
                    ["code"] = "kafala.custom_test",
                    ["nameAr"] = "اختبار حذف",
                    ["active"] = true,
                    ["values"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["kind"] = "leaf",
                        ["parent"] = "kafala",
                        ["vehicleMode"] = "company_bike",
                        ["operatingCostD"] = 500m
                    }
                });
                return true;
            });

            var view = store.Read(c =>
            {
                var groups = ConfigLists.Groups;
                var def = groups.First(g => g.Group == "operating_systems");
                return ConfigLists.Items(c, null, "operating_systems");
            });
            Assert.IsTrue(view.Any(i => i.S("code") == "kafala.custom_test"));

            var deleted = store.Write((c, tx) => ConfigLists.DeleteOperatingSystem(c, tx, maker, "kafala.custom_test"));
            Assert.IsTrue(deleted.B("deleted"));
            Assert.IsNull(store.Read(c => ConfigLists.Current(c, null, "operating_systems", "kafala.custom_test")));
            Assert.IsTrue(store.Read(c => Store.List(c, "audit").Any(a => a.S("action") == "config.item_deleted")));
        });
    }

    [TestMethod]
    public void HardDelete_ParentWithChildren_Returns409WithExplanation()
    {
        WithStore(store =>
        {
            ConfigLists.Seed(store);
            var err = Assert.ThrowsExactly<ApiError>(() =>
                store.Write((c, tx) => ConfigLists.DeleteOperatingSystem(c, tx, PayFixture.Maker, "kafala")));
            Assert.AreEqual(409, err.Status);
            // Seeded parent is protected even before reference scan (CORE1b).
            StringAssert.Contains(err.Message, "مزروعة");
        });
    }
}
