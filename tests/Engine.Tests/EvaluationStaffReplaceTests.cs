using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class EvaluationStaffReplaceTests
{
    static readonly ClaimsPrincipal Admin = PayFixture.Maker;
    static JsonArray List(params string[] values) => new(values.Select(v => (JsonNode?)v).ToArray());
    static JsonObject Card(string name) => new()
    {
        ["name"] = name, ["cities"] = List("City A", "City B"), ["departments"] = List(EvaluationSurvey.DeptMovement),
        ["roles"] = List("Old role"), ["apps"] = List("Ninja", "Keeta"),
        ["appCities"] = new JsonObject { ["Keeta"] = List("City B") }, ["allApps"] = true
    };
    static JsonObject Import(PayFixture f, params JsonObject[] cards) => f.Store.Write((c, tx) =>
        EvaluationSurvey.ImportStaff(c, tx, Admin, new() { ["staff"] = new JsonArray(cards.Select(x => (JsonNode?)x).ToArray()) }));
    static JsonObject Stored(PayFixture f, string name) =>
        f.Store.Read(c => Store.List(c, EvaluationSurvey.StaffKind)).Single(s => s.S("name") == name);
    static string[] Values(JsonObject card, string key) => card[key]!.AsArray().Select(v => v!.GetValue<string>()).ToArray();

    [TestMethod]
    public void ImportReplacesStoredListsInsteadOfMerging()
    {
        using var f = new PayFixture();
        Import(f, Card("Synthetic one"), Card("Synthetic untouched"));
        Import(f, new JsonObject
        {
            ["name"] = "Synthetic one", ["cities"] = List("City A"), ["departments"] = List(EvaluationSurvey.DeptFinance),
            ["roles"] = List("New role"), ["apps"] = List("Ninja"), ["appCities"] = new JsonObject { ["Ninja"] = List("City A") }
        });
        var card = Stored(f, "Synthetic one");
        CollectionAssert.AreEqual(new[] { "City A" }, Values(card, "cities"));
        CollectionAssert.AreEqual(new[] { EvaluationSurvey.DeptFinance }, Values(card, "departments"));
        CollectionAssert.AreEqual(new[] { "New role" }, Values(card, "roles"));
        CollectionAssert.AreEqual(new[] { "Ninja" }, Values(card, "apps"));
        CollectionAssert.AreEqual(new[] { "Ninja" }, card["appCities"]!.AsObject().Select(p => p.Key).ToArray());
        Assert.IsTrue(card.B("allApps"), "a flag the file does not mention keeps its stored value");
        CollectionAssert.AreEqual(new[] { "City A", "City B" }, Values(Stored(f, "Synthetic untouched"), "cities"));
    }

    [TestMethod]
    public void EmptyListsKeepStoredValuesAndAreReported()
    {
        using var f = new PayFixture();
        Import(f, Card("Synthetic one"), new JsonObject { ["name"] = "Synthetic bare", ["cities"] = List("City A") });
        var result = Import(f,
            new JsonObject
            {
                ["name"] = "Synthetic one", ["cities"] = new JsonArray(), ["departments"] = new JsonArray(), ["roles"] = List("New role"),
                ["apps"] = new JsonArray(), ["appCities"] = new JsonObject()
            },
            new JsonObject { ["name"] = "Synthetic bare", ["cities"] = List("City A"), ["apps"] = new JsonArray() });
        var card = Stored(f, "Synthetic one");
        CollectionAssert.AreEqual(new[] { "City A", "City B" }, Values(card, "cities"));
        CollectionAssert.AreEqual(new[] { EvaluationSurvey.DeptMovement }, Values(card, "departments"));
        CollectionAssert.AreEqual(new[] { "New role" }, Values(card, "roles"));
        CollectionAssert.AreEqual(new[] { "Ninja", "Keeta" }, Values(card, "apps"));
        CollectionAssert.AreEqual(new[] { "City B" }, Values(card["appCities"]!.AsObject(), "Keeta"));
        // Reported only where something stored was really kept.
        var kept = result["kept"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual("Synthetic one", kept.S("name"));
        CollectionAssert.AreEquivalent(new[] { "cities", "departments", "apps", "appCities" }, Values(kept, "fields"));
        // A per-app entry with no cities is also empty: it must not drop the stored limit.
        Import(f, new JsonObject { ["name"] = "Synthetic one", ["appCities"] = new JsonObject { ["Keeta"] = new JsonArray() } });
        CollectionAssert.AreEqual(new[] { "City B" }, Values(Stored(f, "Synthetic one")["appCities"]!.AsObject(), "Keeta"));
    }

    [TestMethod]
    public void NullKeepsAndBlankTextIsRejectedWithoutSaving()
    {
        using var f = new PayFixture();
        Import(f, Card("Synthetic one"));
        var kept = Import(f, new JsonObject { ["name"] = "Synthetic one", ["cities"] = null, ["departments"] = null, ["roles"] = null, ["apps"] = null, ["appCities"] = null });
        Assert.IsEmpty(kept["kept"]!.AsArray(), "null means not mentioned, not empty");
        CollectionAssert.AreEqual(new[] { "Ninja", "Keeta" }, Values(Stored(f, "Synthetic one"), "apps"));
        foreach (string field in new[] { "cities", "departments", "roles", "apps", "appCities" })
            Assert.Throws<ApiError>(() => Import(f, new JsonObject { ["name"] = "Synthetic one", [field] = "" }), field);
        foreach (string field in new[] { "cities", "departments", "roles", "apps" })
            Assert.Throws<ApiError>(() => Import(f, new JsonObject { ["name"] = "Synthetic one", [field] = List("") }), field);
        Assert.Throws<ApiError>(() => Import(f, new JsonObject { ["name"] = "Synthetic one", ["appCities"] = new JsonObject { ["Keeta"] = List("") } }));
        var card = Stored(f, "Synthetic one");
        CollectionAssert.AreEqual(new[] { "City A", "City B" }, Values(card, "cities"));
        CollectionAssert.AreEqual(new[] { "Old role" }, Values(card, "roles"));
        CollectionAssert.AreEqual(new[] { "City B" }, Values(card["appCities"]!.AsObject(), "Keeta"));
    }

    [TestMethod]
    public void FieldMissingFromFileKeepsStoredValue()
    {
        using var f = new PayFixture();
        Import(f, Card("Synthetic one"));
        Import(f, new JsonObject { ["name"] = "Synthetic one", ["roles"] = List("New role") });
        var card = Stored(f, "Synthetic one");
        CollectionAssert.AreEqual(new[] { "City A", "City B" }, Values(card, "cities"));
        CollectionAssert.AreEqual(new[] { "Ninja", "Keeta" }, Values(card, "apps"));
        CollectionAssert.AreEqual(new[] { "New role" }, Values(card, "roles"));
        CollectionAssert.AreEqual(new[] { "City B" }, Values(card["appCities"]!.AsObject(), "Keeta"));
    }

    [TestMethod]
    public void RowsOfTheSamePersonInOneFileStillCombine()
    {
        using var f = new PayFixture();
        Import(f, Card("Synthetic one"));
        // First row does not mention cities; the second does. The stored cities must not leak into the result.
        Import(f,
            new JsonObject { ["name"] = "Synthetic one", ["roles"] = List("Role 1"), ["apps"] = List("Ninja") },
            new JsonObject { ["name"] = "Synthetic one", ["cities"] = List("City C"), ["roles"] = List("Role 2"), ["apps"] = List("Jahez") });
        var card = Stored(f, "Synthetic one");
        CollectionAssert.AreEqual(new[] { "City C" }, Values(card, "cities"));
        CollectionAssert.AreEquivalent(new[] { "Role 1", "Role 2" }, Values(card, "roles"));
        CollectionAssert.AreEquivalent(new[] { "Ninja", "Jahez" }, Values(card, "apps"));
    }

    [TestMethod]
    public void DeleteCardNeedsNameAndPermissionAndKeepsExistingRounds()
    {
        using var f = new PayFixture();
        Import(f, Card("Synthetic one"), Card("Synthetic two"));
        var round = f.Store.Write((c, tx) => EvaluationSurvey.CreateRound(c, tx, Admin, new() { ["title"] = "Synthetic round" }));
        string id = Stored(f, "Synthetic one").S("id");
        Assert.ThrowsExactly<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.DeleteStaff(c, tx, Admin, id, new() { ["name"] = "Wrong" })));
        var reader = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "synthetic-reader"), new Claim("permission", EvaluationSurvey.ViewPermission)
        }, "test"));
        Assert.Throws<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.DeleteStaff(c, tx, reader, id, new() { ["name"] = "Synthetic one" })));
        Assert.HasCount(2, f.Store.Read(c => Store.List(c, EvaluationSurvey.StaffKind)));

        var result = f.Store.Write((c, tx) => EvaluationSurvey.DeleteStaff(c, tx, Admin, id, new() { ["name"] = "Synthetic one" }));
        Assert.AreEqual(id, result.S("id"));
        Assert.AreEqual("Synthetic two", f.Store.Read(c => Store.List(c, EvaluationSurvey.StaffKind)).Single().S("name"));
        // An existing round keeps its own copy, so its replies and scorecards stay readable.
        var stored = f.Store.Read(c => EvaluationSurvey.Round(c, round.S("id")));
        Assert.IsTrue(stored["staff"]!.AsArray().OfType<JsonObject>().Any(s => s.S("id") == id));
        // After a refresh the deleted card leaves the round.
        var refreshed = f.Store.Write((c, tx) => EvaluationSurvey.RefreshStaff(c, tx, Admin, round.S("id")));
        Assert.IsFalse(refreshed["staff"]!.AsArray().OfType<JsonObject>().Any(s => s.S("id") == id));
        Assert.ThrowsExactly<ApiError>(() => f.Store.Write((c, tx) => EvaluationSurvey.DeleteStaff(c, tx, Admin, id, new() { ["name"] = "Synthetic one" })));
    }

    [TestMethod]
    public void StaffListOffersEditAndDeleteOnlyToManagers()
    {
        using var f = new PayFixture();
        Import(f, Card("Synthetic one"));
        var managed = f.Store.Read(c => EvaluationSurvey.StaffList(c, Admin)).Single();
        CollectionAssert.AreEqual(new[] { "edit", "delete" }, Values(managed, "allowedActions"));
        var reader = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "synthetic-reader"), new Claim("permission", EvaluationSurvey.ViewPermission)
        }, "test"));
        Assert.IsEmpty(Values(f.Store.Read(c => EvaluationSurvey.StaffList(c, reader)).Single(), "allowedActions"));
    }
}
