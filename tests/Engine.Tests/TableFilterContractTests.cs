using System.Reflection;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class TableFilterContractTests
{
    sealed class TestHostEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Gate Payroll";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    static ClaimsPrincipal ScopedSponsorUser(string sponsorId, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "scoped-tester"),
            new(ClaimTypes.Role, "Accountant"),
            new("finance.sponsorIds", sponsorId),
            new("finance.platforms", "Jahez"),
            new("finance.categoryCodes", "CashAdvance"),
            new("finance.categoryCodes", "Equipment")
        };
        foreach (string perm in permissions)
            claims.Add(new Claim("permission", perm));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [TestMethod]
    public void RejectsNonWhitelistedColumnsInFiltersAndSortAcrossDebtsOperationsAndWorkforce()
    {
        using var f = new PayFixture();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Operations:Source"] = "store" })
            .Build();
        var env = new TestHostEnv("Development");

        // 1. Debts rejects unwhitelisted filter column and sort column
        var badFilterCtx = new DefaultHttpContext();
        badFilterCtx.Request.QueryString = new QueryString("?page=1&pageSize=25&filters=[{\"col\":\"secretColumn\",\"op\":\"eq\",\"value\":\"x\"}]");
        var ex1 = Assert.ThrowsExactly<ApiError>(() =>
            f.Store.Read(c => Debts.ListPaged(c, PayFixture.Maker, badFilterCtx.Request.Query)));
        Assert.AreEqual(400, ex1.Status);

        var badSortCtx = new DefaultHttpContext();
        badSortCtx.Request.QueryString = new QueryString("?page=1&pageSize=25&sort=[{\"col\":\"notAllowed\",\"dir\":\"asc\"}]");
        var ex2 = Assert.ThrowsExactly<ApiError>(() =>
            f.Store.Read(c => Debts.ListPaged(c, PayFixture.Maker, badSortCtx.Request.Query)));
        Assert.AreEqual(400, ex2.Status);

        // 2. Operations rejects unwhitelisted filter column
        var opsPurchases = typeof(OperationsLiveApi).GetMethod("Purchases", BindingFlags.NonPublic | BindingFlags.Static)!;
        var badOpsCtx = new DefaultHttpContext { User = PayFixture.Maker };
        badOpsCtx.Request.QueryString = new QueryString("?from=2026-11-01&to=2026-11-30&filters=[{\"col\":\"vin\",\"op\":\"contains\",\"value\":\"123\"}]");
        var ex3 = Assert.ThrowsExactly<TargetInvocationException>(() =>
            opsPurchases.Invoke(null, [f.Store, cfg, env, badOpsCtx]));
        Assert.IsInstanceOfType<ApiError>(ex3.InnerException);
        Assert.AreEqual(400, ((ApiError)ex3.InnerException!).Status);

        // 3. Workforce rejects unwhitelisted sort column
        var wfList = typeof(WorkforceApi).GetMethod("List", BindingFlags.NonPublic | BindingFlags.Static)!;
        var badWfCtx = new DefaultHttpContext { User = PayFixture.Maker };
        badWfCtx.Request.QueryString = new QueryString("?page=1&pageSize=25&sort=[{\"col\":\"passwordHash\",\"dir\":\"desc\"}]");
        var ex4 = Assert.ThrowsExactly<TargetInvocationException>(() =>
            wfList.Invoke(null, [f.Store, cfg, env, badWfCtx]));
        Assert.IsInstanceOfType<ApiError>(ex4.InnerException);
        Assert.AreEqual(400, ((ApiError)ex4.InnerException!).Status);
    }

    [TestMethod]
    public void EvaluatesAllFilterOperatorsAndSortByColorOnServerContract()
    {
        var refDate = new DateOnly(2026, 11, 19);
        var rows = new List<JsonObject>
        {
            new() { ["id"] = "1", ["name"] = "شركة بترو اب", ["city"] = "الرياض", ["status"] = "active", ["amount"] = 1500m, ["date"] = "2026-11-10" },
            new() { ["id"] = "2", ["name"] = "شركة سيارة اب", ["city"] = "الرياض", ["status"] = "pending_approval", ["amount"] = 2400m, ["date"] = "2026-11-15" },
            new() { ["id"] = "3", ["name"] = "مؤسسة الوقود", ["city"] = "جدة", ["status"] = "cancelled", ["amount"] = 800m, ["date"] = "2026-10-20" },
            new() { ["id"] = "4", ["name"] = "شركة بترو الخليج", ["city"] = "الدمام", ["status"] = "active", ["amount"] = 3200m, ["date"] = "2026-09-05" }
        };

        // Text operators: contains, notContains, beginsWith, endsWith, eq, neq
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("name", "contains", "بترو")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("name", "notContains", "بترو")], refDate).Count);
        Assert.AreEqual(3, TableFilterContract.ApplyFilters(rows, [new("name", "beginsWith", "شركه")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("name", "endsWith", "اب")], refDate).Count);
        Assert.AreEqual(1, TableFilterContract.ApplyFilters(rows, [new("city", "eq", "جده")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("city", "neq", "الرياض")], refDate).Count);

        // Display label mapping (Arabic label "نشط" matches stored raw "active")
        var activeRows = TableFilterContract.ApplyFilters(rows, [new("status", "eq", "نشط")], refDate);
        Assert.AreEqual(2, activeRows.Count);

        // Checkbox value list (op = "in")
        var inRows = TableFilterContract.ApplyFilters(rows, [new("city", "in", Values: ["جدة", "الدمام"])], refDate);
        CollectionAssert.AreEquivalent(new[] { "3", "4" }, inRows.Select(r => r.S("id")).ToList());

        // Color filter (op = "color": warning -> pending_approval, danger -> cancelled)
        var warnRows = TableFilterContract.ApplyFilters(rows, [new("status", "color", "warning")], refDate);
        Assert.AreEqual(1, warnRows.Count);
        Assert.AreEqual("2", warnRows[0].S("id"));

        // Number operators: eq, neq, gt, gte, lt, lte, between, topN
        Assert.AreEqual(1, TableFilterContract.ApplyFilters(rows, [new("amount", "eq", "1500")], refDate).Count);
        Assert.AreEqual(3, TableFilterContract.ApplyFilters(rows, [new("amount", "neq", "1500")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("amount", "gt", "1500")], refDate).Count);
        Assert.AreEqual(3, TableFilterContract.ApplyFilters(rows, [new("amount", "gte", "1500")], refDate).Count);
        Assert.AreEqual(1, TableFilterContract.ApplyFilters(rows, [new("amount", "lt", "1500")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("amount", "lte", "1500")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("amount", "between", "1000", "2500")], refDate).Count);
        var top2 = TableFilterContract.ApplyFilters(rows, [new("amount", "topN", "2")], refDate);
        CollectionAssert.AreEquivalent(new[] { "2", "4" }, top2.Select(r => r.S("id")).ToList());

        // Date operators: eq, neq, before, after, between, thisMonth, lastMonth
        Assert.AreEqual(1, TableFilterContract.ApplyFilters(rows, [new("date", "eq", "2026-11-10")], refDate).Count);
        Assert.AreEqual(3, TableFilterContract.ApplyFilters(rows, [new("date", "neq", "2026-11-10")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("date", "before", "2026-11-01")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("date", "after", "2026-11-01")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("date", "between", "2026-11-01", "2026-11-30")], refDate).Count);
        Assert.AreEqual(2, TableFilterContract.ApplyFilters(rows, [new("date", "thisMonth")], refDate).Count);
        Assert.AreEqual(1, TableFilterContract.ApplyFilters(rows, [new("date", "lastMonth")], refDate).Count);

        // Sort by color puts target tone at index 0
        var sortedByDanger = TableFilterContract.ApplySort(rows, [new("status", "asc", "danger")]);
        Assert.AreEqual("3", sortedByDanger[0].S("id"));

        // Numeric descending sort
        var sortedByAmountDesc = TableFilterContract.ApplySort(rows, [new("amount", "desc")]);
        Assert.AreEqual("4", sortedByAmountDesc[0].S("id"));
        Assert.AreEqual("3", sortedByAmountDesc[^1].S("id"));
    }

    [TestMethod]
    public void EnforcesScopeAndPermissionsOnDebtsWorkforceAndOperationsServerFilters()
    {
        using var f = new PayFixture();
        var sp1 = FinancialCore.Sponsors[0].Id;
        var sp2 = FinancialCore.Sponsors[1].Id;

        f.Store.Write((c, tx) =>
        {
            Store.Save(c, "debt", new JsonObject
            {
                ["id"] = "D-SP1", ["iqama"] = "2200000001", ["riderName"] = "مندوب أول",
                ["creditorSponsorId"] = sp1, ["category"] = "CashAdvance", ["platform"] = "Jahez",
                ["originalAmount"] = 1000m, ["remainingBalance"] = 600m, ["status"] = "active",
                ["createdAt"] = "2026-11-10"
            }, tx);
            Store.Save(c, "debt", new JsonObject
            {
                ["id"] = "D-SP2", ["iqama"] = "2200000002", ["riderName"] = "مندوب ثان",
                ["creditorSponsorId"] = sp2, ["category"] = "CashAdvance", ["platform"] = "Jahez",
                ["originalAmount"] = 2000m, ["remainingBalance"] = 1800m, ["status"] = "active",
                ["createdAt"] = "2026-11-12"
            }, tx);
            Store.Save(c, "rider", new JsonObject
            {
                ["id"] = "2200000001", ["iqama"] = "2200000001", ["name"] = "مندوب أول",
                ["sponsorId"] = sp1, ["sponsor"] = sp1, ["platforms"] = "Jahez", ["city"] = "الرياض",
                ["engagementType"] = "SponsoredInternal", ["status"] = "Active",
                ["iban"] = "SA0380000000608010167519"
            }, tx);
            return true;
        });

        // 1. Scoped user only sees sp1 debts even when filtering by status=active or remainingBalance>=500
        var scopedActor = ScopedSponsorUser(sp1, "debts.view", "riders.view");
        var debtCtx = new DefaultHttpContext();
        debtCtx.Request.QueryString = new QueryString("?page=1&pageSize=50&filters=[{\"col\":\"remainingBalance\",\"op\":\"gte\",\"value\":\"500\"}]&sort=[{\"col\":\"remainingBalance\",\"dir\":\"desc\"}]");
        var debtPage = f.Store.Read(c => JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(Debts.ListPaged(c, scopedActor, debtCtx.Request.Query)))!.AsObject());
        var debtItems = debtPage["items"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.AreEqual(1, debtItems.Count);
        Assert.AreEqual("D-SP1", debtItems[0].S("id"));

        // Distinct values for scoped user only include sp1
        var distinctCtx = new DefaultHttpContext();
        distinctCtx.Request.QueryString = new QueryString("?column=creditorSponsorId");
        var distinctObj = f.Store.Read(c => Debts.Distinct(c, scopedActor, distinctCtx.Request.Query));
        var distinctSponsors = distinctObj["values"]!.AsArray().Select(x => x!.ToString()).ToList();
        CollectionAssert.AreEqual(new[] { sp1 }, distinctSponsors);
        Assert.IsNotNull(distinctObj["counts"]);

        // 2. Workforce requires riders.view permission and strips IBAN before filtering/distinct for users without PermIban
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Operations:Source"] = "store" })
            .Build();
        var env = new TestHostEnv("Development");
        var wfDistinct = typeof(WorkforceApi).GetMethod("DistinctValues", BindingFlags.NonPublic | BindingFlags.Static)!;

        // Without riders.view -> 403
        var noPermUser = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "none")], "test"));
        var noPermCtx = new DefaultHttpContext { User = noPermUser };
        noPermCtx.Request.QueryString = new QueryString("?column=city");
        var permEx = Assert.ThrowsExactly<TargetInvocationException>(() =>
            wfDistinct.Invoke(null, [f.Store, cfg, env, noPermCtx]));
        Assert.IsInstanceOfType<ApiError>(permEx.InnerException);
        Assert.AreEqual(403, ((ApiError)permEx.InnerException!).Status);

        // User with riders.view but WITHOUT riders.iban.view gets 0 IBAN distinct values
        var wfIbanCtx = new DefaultHttpContext { User = scopedActor };
        wfIbanCtx.Request.QueryString = new QueryString("?column=iban");
        var wfIbanRes = (JsonObject)wfDistinct.Invoke(null, [f.Store, cfg, env, wfIbanCtx])!;
        Assert.AreEqual(0, wfIbanRes["values"]!.AsArray().Count);
    }

    [TestMethod]
    public void EnforcesWhitelistScopeAndPermissionsOnBroadcastsAndServiceRequests()
    {
        using var f = new PayFixture();
        var sp1 = FinancialCore.Sponsors[0].Id;
        var sp2 = FinancialCore.Sponsors[1].Id;

        f.Store.Write((c, tx) =>
        {
            Store.Save(c, "rider", new JsonObject
            {
                ["id"] = "2200000011", ["iqama"] = "2200000011", ["name"] = "مندوب نطاق أول",
                ["sponsorId"] = sp1, ["financeSponsorId"] = sp1, ["platforms"] = "Jahez", ["status"] = "Active"
            }, tx);
            Store.Save(c, "rider", new JsonObject
            {
                ["id"] = "2200000022", ["iqama"] = "2200000022", ["name"] = "مندوب نطاق ثان",
                ["sponsorId"] = sp2, ["financeSponsorId"] = sp2, ["platforms"] = "Jahez", ["status"] = "Active"
            }, tx);
            Store.Save(c, "mobile_broadcast", new JsonObject
            {
                ["id"] = "mb-1", ["titleAr"] = "إشعار نطاق أول", ["kind"] = "individual", ["category"] = "warning",
                ["targetIqama"] = "2200000011", ["sentBy"] = "admin", ["createdAt"] = "2026-11-10T10:00:00Z"
            }, tx);
            Store.Save(c, "mobile_broadcast", new JsonObject
            {
                ["id"] = "mb-2", ["titleAr"] = "إشعار نطاق ثان", ["kind"] = "individual", ["category"] = "warning",
                ["targetIqama"] = "2200000022", ["sentBy"] = "admin", ["createdAt"] = "2026-11-11T10:00:00Z"
            }, tx);
            Store.Save(c, RiderAsks.ServiceKind, new JsonObject
            {
                ["id"] = "sreq:1", ["riderIqama"] = "2200000011", ["riderName"] = "مندوب نطاق أول",
                ["companyId"] = sp1, ["requestType"] = "annual_leave", ["direction"] = "rider_to_admin",
                ["reason"] = "إجازة سنوية", ["status"] = "open", ["createdAt"] = "2026-11-12T08:00:00Z"
            }, tx);
            Store.Save(c, RiderAsks.ServiceKind, new JsonObject
            {
                ["id"] = "sreq:2", ["riderIqama"] = "2200000022", ["riderName"] = "مندوب نطاق ثان",
                ["companyId"] = sp2, ["requestType"] = "annual_leave", ["direction"] = "rider_to_admin",
                ["reason"] = "إجازة ثانية", ["status"] = "open", ["createdAt"] = "2026-11-13T08:00:00Z"
            }, tx);
            return true;
        });

        // 1. Broadcasts: permission required (403 without mobile.broadcast)
        var noPerm = ScopedSponsorUser(sp1, "debts.view");
        var emptyCtx = new DefaultHttpContext();
        emptyCtx.Request.QueryString = new QueryString("?column=kind");
        var exPerm = Assert.ThrowsExactly<ApiError>(() =>
            f.Store.Read(c => FinancialCore.DistinctBroadcasts(c, noPerm, emptyCtx.Request.Query)));
        Assert.AreEqual(403, exPerm.Status);

        // 2. Broadcasts: rejects unwhitelisted filter column (400)
        var scopedBroadcastUser = ScopedSponsorUser(sp1, "mobile.broadcast");
        var badBcCtx = new DefaultHttpContext();
        badBcCtx.Request.QueryString = new QueryString("?page=1&pageSize=25&filters=[{\"col\":\"secretField\",\"op\":\"eq\",\"value\":\"x\"}]");
        var exBc = Assert.ThrowsExactly<ApiError>(() =>
            f.Store.Read(c => ApiPagination.Apply(
                FinancialCore.ListScopedBroadcasts(c, scopedBroadcastUser, badBcCtx.Request.Query),
                badBcCtx.Request.Query,
                whitelist: FinancialCore.BroadcastAllowedColumns)));
        Assert.AreEqual(400, exBc.Status);

        // 3. Broadcasts: scope enforcement + distinct + counts
        var okBcCtx = new DefaultHttpContext();
        okBcCtx.Request.QueryString = new QueryString("?column=targetIqama");
        var bcDistinct = f.Store.Read(c => FinancialCore.DistinctBroadcasts(c, scopedBroadcastUser, okBcCtx.Request.Query));
        var bcTargets = bcDistinct["values"]!.AsArray().Select(x => x!.ToString()).ToList();
        CollectionAssert.AreEqual(new[] { "2200000011" }, bcTargets);

        // 4. Service Requests: rejects unwhitelisted sort column (400)
        var badSrCtx = new DefaultHttpContext();
        badSrCtx.Request.QueryString = new QueryString("?page=1&pageSize=25&sort=[{\"col\":\"passwordHash\",\"dir\":\"asc\"}]");
        var exSr = Assert.ThrowsExactly<ApiError>(() =>
            f.Store.Read(c => RiderAsks.ListScopedServiceRequests(c, scopedBroadcastUser, null, null, null, null, badSrCtx.Request.Query)));
        Assert.AreEqual(400, exSr.Status);

        // 5. Service Requests: scope + filters + distinct
        var okSrCtx = new DefaultHttpContext();
        okSrCtx.Request.QueryString = new QueryString("?page=1&pageSize=25&filters=[{\"col\":\"requestType\",\"op\":\"eq\",\"value\":\"annual_leave\"}]");
        var srPage = f.Store.Read(c => JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(
            RiderAsks.ListScopedServiceRequests(c, scopedBroadcastUser, null, null, null, null, okSrCtx.Request.Query)))!.AsObject());
        var srItems = srPage["items"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.AreEqual(1, srItems.Count);
        Assert.AreEqual("sreq:1", srItems[0].S("id"));

        var srDistinctCtx = new DefaultHttpContext();
        srDistinctCtx.Request.QueryString = new QueryString("?column=riderIqama");
        var srDistinct = f.Store.Read(c => RiderAsks.DistinctServiceRequests(c, scopedBroadcastUser, null, null, null, null, srDistinctCtx.Request.Query));
        CollectionAssert.AreEqual(new[] { "2200000011" }, srDistinct["values"]!.AsArray().Select(x => x!.ToString()).ToList());
    }
}
