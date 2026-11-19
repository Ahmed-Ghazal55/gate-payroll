using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.SqlClient;
namespace Payroll;

/// <summary>Workforce live list, reduced 360, Saudi/GOSI, cases and HR catalogs (H3/H4/H6/H9/H10/H11/H13/H27).</summary>
public static class WorkforceApi
{
    static readonly TimeSpan LiveReadTimeout = TimeSpan.FromSeconds(20);

    public static void Map(RouteGroupBuilder api, Store store, IConfiguration config, IHostEnvironment env)
    {
        api.MapGet("/workforce", (HttpContext ctx) => List(store, config, env, ctx))
            .WithTags("Workforce").WithSummary("Paged workforce directory (live ERP when configured)");
        api.MapGet("/workforce/summary", (HttpContext ctx) => Summary(store, config, env, ctx))
            .WithTags("Workforce").WithSummary("KPI counts for workforce filter cards");
        api.MapGet("/workforce/{id}/360", (string id, HttpContext ctx) => Profile360(store, config, env, id, ctx))
            .WithTags("Workforce").WithSummary("Reduced workforce 360 (H6)");
        api.MapGet("/workforce/saudi-payroll-employees", (HttpContext ctx) => Saudi(store, config, env, ctx))
            .WithTags("Workforce").WithSummary("Omar GOSI payroll employees (H10)");
        api.MapGet("/workforce/terminated", (HttpContext ctx) => Terminated(store, config, env, ctx))
            .WithTags("Workforce").WithSummary("Terminated and fleeing people (H11)");
        api.MapGet("/workforce/absence-cases", (HttpContext ctx) => Absence(store, config, env, ctx))
            .WithTags("Workforce").WithSummary("Absence compliance cases (H11)");
        api.MapGet("/workforce/data-quality", (HttpContext ctx) => DataQuality(store, config, env, ctx))
            .WithTags("Workforce").WithSummary("Workforce data-quality issues from live or store");
        api.MapGet("/hr-catalogs/{kind}", (string kind, HttpContext ctx) => Catalogs(store, config, env, kind, ctx))
            .WithTags("HR Catalogs").WithSummary("Live HR catalogs from Omar ERP (H9)");
    }

    static object List(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "riders.view");
        var request = ParseList(ctx);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                var page = StoreList(c, ctx.User, request);
                return Envelope(new { items = page.Items, total = page.Total, page = request.Page, pageSize = request.PageSize, summary = page.Summary }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            var page = WorkforceLiveQuery.Page(erp, request with { QueryAlt = request.Query.Length == 0 ? "" : TextSearch.Normalize(request.Query) }, token);
            var items = ScopeFilter(store, ctx.User, page.Items);
            return Envelope(new { items, total = page.Total, page = request.Page, pageSize = request.PageSize, summary = page.Summary }, Operations.SourceLive, page.AsOf, Actions(ctx.User));
        }, "workforce");
    }

    static object Summary(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "riders.view");
        var request = ParseList(ctx) with { Page = 1, PageSize = 1, Skip = 0, Take = 1 };
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                var all = StoreList(c, ctx.User, request with { Page = 1, PageSize = 10_000, Skip = 0, Take = 10_000 });
                return Envelope(new { summary = all.Summary }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            var page = WorkforceLiveQuery.Page(erp, request with { QueryAlt = request.Query.Length == 0 ? "" : TextSearch.Normalize(request.Query) }, token);
            return Envelope(new { summary = page.Summary }, Operations.SourceLive, page.AsOf, Actions(ctx.User));
        }, "workforce");
    }

    static object Profile360(Store store, IConfiguration config, IHostEnvironment env, string id, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "riders.view_360");
        AccessPolicy.Demand(ctx.User, "debts.view");
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c => Envelope(Store360(c, ctx.User, id), Operations.SourceStore, DateTimeOffset.UtcNow, Actions360(ctx.User)));
        string erpId = id;
        if (!Guid.TryParse(id, out _))
        {
            erpId = store.Read(c =>
            {
                string iqama;
                try { iqama = J.Iqama(id); }
                catch { throw new ApiError("معرّف الموظف غير صالح.", 400); }
                var rider = Store.List(c, "rider").FirstOrDefault(r => r.S("iqama") == iqama || r.S("id") == iqama);
                if (rider == null) throw new ApiError("الموظف غير موجود.", 404);
                FinancialScopes.DemandRider(ctx.User, rider);
                string linked = rider.S("erpSourceId");
                return linked == "" ? "" : linked;
            });
            if (erpId == "")
                return store.Read(c => Envelope(Store360(c, ctx.User, id), Operations.SourceLive, DateTimeOffset.UtcNow, Actions360(ctx.User)));
        }
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            try
            {
                var body = WorkforceLiveQuery.Person360(erp, erpId, token);
                AttachLocalFinance(store, ctx.User, body);
                return Envelope(body, Operations.SourceLive, DateTimeOffset.UtcNow, Actions360(ctx.User));
            }
            catch (ImportFailure ex) when (ex.Code == "employee_not_found")
            {
                throw new ApiError("الموظف غير موجود.", 404);
            }
        }, "workforce-360");
    }

    static object Saudi(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "riders.view");
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return Envelope(new { items = Array.Empty<JsonObject>(), total = 0, message = WorkforceLiveQuery.NoSourceYet }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            var items = WorkforceLiveQuery.SaudiPayrollEmployees(erp, token);
            return Envelope(new { items, total = items.Count }, Operations.SourceLive, DateTimeOffset.UtcNow, Actions(ctx.User));
        }, "saudi-payroll");
    }

    static object Terminated(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "riders.view");
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                var items = Store.List(c, "rider").Where(ErpRiderClassification.ListedAsRider)
                    .Where(r => FinancialScopes.Rider(ctx.User, r))
                    .Where(r => r.S("status") is "Terminated" or "Fleeing" or "Archived")
                    .Select(StorePerson)
                    .ToList();
                return Envelope(new { items, total = items.Count }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            var items = WorkforceLiveQuery.TerminatedAndFleeing(erp, token);
            return Envelope(new { items, total = items.Count }, Operations.SourceLive, DateTimeOffset.UtcNow, Actions(ctx.User));
        }, "terminated");
    }

    static object Absence(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "riders.view");
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return Envelope(new { items = Array.Empty<JsonObject>(), total = 0 }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            var items = WorkforceLiveQuery.AbsenceCases(erp, token);
            return Envelope(new { items, total = items.Count }, Operations.SourceLive, DateTimeOffset.UtcNow, Actions(ctx.User));
        }, "absence-cases");
    }

    static object DataQuality(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "riders.view");
        var request = ParseList(ctx) with { Page = 1, PageSize = 200, Skip = 0, Take = 200, MissingIban = false };
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                var people = Store.List(c, "rider").Where(ErpRiderClassification.ListedAsRider).Where(r => FinancialScopes.Rider(ctx.User, r)).ToList();
                var issues = people.SelectMany(QualityIssues).ToList();
                return Envelope(new { items = issues, total = issues.Count }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            var page = WorkforceLiveQuery.Page(erp, request with { QueryAlt = "" }, token);
            var issues = page.Items.SelectMany(QualityIssuesLive).ToList();
            return Envelope(new { items = issues, total = issues.Count }, Operations.SourceLive, page.AsOf, Actions(ctx.User));
        }, "data-quality");
    }

    static object Catalogs(Store store, IConfiguration config, IHostEnvironment env, string kind, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "riders.view");
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return Envelope(new { items = Array.Empty<JsonObject>(), total = 0, kind, message = WorkforceLiveQuery.NoSourceYet }, Operations.SourceStore, DateTimeOffset.UtcNow, new JsonArray("export"));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            try
            {
                var items = WorkforceLiveQuery.Catalog(erp, kind, token);
                return Envelope(new { items, total = items.Count, kind }, Operations.SourceLive, DateTimeOffset.UtcNow, new JsonArray("export"));
            }
            catch (ImportFailure ex) when (ex.Code.StartsWith("hr_catalog_kind_unsupported", StringComparison.Ordinal))
            {
                throw new ApiError("نوع الكتالوج غير مدعوم.", 404);
            }
        }, "hr-catalogs");
    }

    static WorkforceLiveQuery.ListRequest ParseList(HttpContext ctx)
    {
        try
        {
            var dict = ctx.Request.Query.Keys.ToDictionary(k => k, k => ctx.Request.Query[k].ToString(), StringComparer.OrdinalIgnoreCase);
            return WorkforceLiveQuery.ParseRequest(dict);
        }
        catch (ImportFailure ex) when (ex.Code == "invalid_page") { throw new ApiError("رقم الصفحة وحجمها يجب أن يكونا عددين صحيحين موجبين."); }
        catch (ImportFailure ex) when (ex.Code == "invalid_live_sort") { throw new ApiError("حقل الترتيب غير صالح."); }
    }

    sealed record StorePage(IReadOnlyList<JsonObject> Items, long Total, JsonObject Summary);

    static StorePage StoreList(DbConnection c, ClaimsPrincipal actor, WorkforceLiveQuery.ListRequest request)
    {
        var rows = Store.List(c, "rider").Where(ErpRiderClassification.ListedAsRider).Where(r => FinancialScopes.Rider(actor, r)).Select(StorePerson).ToList();
        rows = FilterStore(rows, request);
        long total = rows.Count;
        var page = rows.Skip(request.Skip).Take(request.Take).ToList();
        return new(page, total, StoreSummary(rows));
    }

    static List<JsonObject> FilterStore(List<JsonObject> rows, WorkforceLiveQuery.ListRequest request)
    {
        IEnumerable<JsonObject> q = rows;
        if (request.Query.Length > 0)
            q = q.Where(r => TextSearch.Matches(request.Query, r.S("iqama"), r.S("name"), r.S("phone"), r.S("platformWorkId")));
        if (request.Sponsorship is "sponsored" or "SponsoredInternal")
            q = q.Where(r => r.S("engagementType") == "SponsoredInternal");
        else if (request.Sponsorship is "external" or "OutsideRider" or "External")
            q = q.Where(r => r.S("engagementType") is "External" or "OutsideRider");
        if (request.WorkType is "admin" or "إداري")
            q = q.Where(r => r.B("isEmployee") || r.S("workType") == "admin");
        else if (request.WorkType is "rider" or "مناديب" or "مندوب")
            q = q.Where(r => !r.B("isEmployee") && r.S("workType") != "admin");
        if (request.Status.Length > 0)
            q = q.Where(r => string.Equals(r.S("status"), request.Status, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.S("statusCode"), request.Status, StringComparison.OrdinalIgnoreCase));
        if (request.MissingIban)
            q = q.Where(r => r.S("engagementType") == "SponsoredInternal" && string.IsNullOrWhiteSpace(r.S("iban")));
        if (request.SponsorUnified.Length > 0)
            q = q.Where(r => r.S("sponsorUnified") == request.SponsorUnified || r.S("sponsorId") == request.SponsorUnified || r.S("sponsor") == request.SponsorUnified);
        return request.Sort.ToLowerInvariant() switch
        {
            "-name" => q.OrderByDescending(r => r.S("name")).ToList(),
            "iqama" or "id" => q.OrderBy(r => r.S("iqama")).ToList(),
            "-iqama" or "-id" => q.OrderByDescending(r => r.S("iqama")).ToList(),
            "status" => q.OrderBy(r => r.S("status")).ToList(),
            "-status" => q.OrderByDescending(r => r.S("status")).ToList(),
            _ => q.OrderBy(r => r.S("name")).ToList()
        };
    }

    static JsonObject StoreSummary(IEnumerable<JsonObject> rows)
    {
        var list = rows.ToList();
        return new JsonObject
        {
            ["totalCount"] = list.Count,
            ["sponsoredCount"] = list.Count(r => r.S("engagementType") == "SponsoredInternal"),
            ["sponsoredRiders"] = list.Count(r => r.S("engagementType") == "SponsoredInternal" && !r.B("isEmployee")),
            ["sponsoredAdmins"] = list.Count(r => r.S("engagementType") == "SponsoredInternal" && r.B("isEmployee")),
            ["sponsoredMissingIban"] = list.Count(r => r.S("engagementType") == "SponsoredInternal" && string.IsNullOrWhiteSpace(r.S("iban")))
        };
    }

    static JsonObject StorePerson(JsonObject r) => new()
    {
        ["id"] = r.S("erpSourceId", r.S("id")),
        ["erpEmployeeId"] = r.S("erpSourceId"),
        ["iqama"] = r.S("iqama", r.S("id")),
        ["name"] = r.S("name"),
        ["phone"] = r.S("phone"),
        ["nationality"] = r.S("nationality"),
        ["iban"] = r.S("iban"),
        ["engagementType"] = r.S("engagementType"),
        ["isEmployee"] = r.B("isEmployee") || r.S("personRole") == "administrative",
        ["workType"] = r.B("isEmployee") || r.S("personRole") == "administrative" ? "admin" : "rider",
        ["status"] = r.S("status"),
        ["sponsorNameAr"] = r.S("sponsor"),
        ["sponsorId"] = r.S("sponsorId"),
        ["sponsorUnified"] = "",
        ["jobTitle"] = WorkforceLiveQuery.NoSourceYet,
        ["workTypeNameAr"] = r.S("operationalRole"),
        ["city"] = r.S("city"),
        ["housing"] = WorkforceLiveQuery.NoSourceYet,
        ["platform"] = r.S("platforms"),
        ["platformWorkId"] = "",
        ["paymentModel"] = WorkforceLiveQuery.NoSourceYet,
        ["hasCompanyVehicle"] = false,
        ["companyVehicleType"] = "",
        ["drivingLicense"] = r.S("drivingLicense"),
        ["mudadSalary"] = WorkforceLiveQuery.NoSourceYet,
        ["operationalRole"] = r.S("operationalRole")
    };

    static JsonObject Store360(DbConnection c, ClaimsPrincipal actor, string key)
    {
        string iqama = key;
        try { iqama = J.Iqama(key); } catch { /* may be erp guid — fall through */ }
        var rider = Store.Get(c, iqama, "rider")
            ?? Store.List(c, "rider").FirstOrDefault(r => r.S("erpSourceId") == key)
            ?? throw new ApiError("المندوب غير موجود.", 404);
        FinancialScopes.DemandRider(actor, rider);
        var person = StorePerson(rider);
        var debts = Store.List(c, "debt").Where(d => d.S("iqama") == rider.S("iqama") && FinancialScopes.Debt(actor, d)).ToList();
        var works = Store.List(c, "work").Where(w => w.S("iqama") == rider.S("iqama")).ToList();
        return new JsonObject
        {
            ["person"] = person,
            ["workRecord3Months"] = new JsonArray(works.Take(50).Select(w => (JsonNode?)new JsonObject
            {
                ["month"] = w.S("period", w.S("from")),
                ["platform"] = w.S("platform"),
                ["workDays"] = WorkforceLiveQuery.NoSourceYet,
                ["orders"] = w.N("orders"),
                ["netDue"] = WorkforceLiveQuery.NoSourceYet,
                ["status"] = w.S("status")
            }).ToArray()),
            ["financialEvaluation"] = new JsonObject { ["status"] = "unavailable", ["message"] = WorkforceLiveQuery.NoSourceYet },
            ["iqamaImage"] = new JsonObject { ["status"] = "pending_permission", ["message"] = WorkforceLiveQuery.ImagePending },
            ["ibanImage"] = new JsonObject { ["status"] = "pending_permission", ["message"] = WorkforceLiveQuery.ImagePending },
            ["promissoryNotes"] = new JsonArray(),
            ["platformAccounts"] = new JsonArray(),
            ["financialPosition"] = new JsonObject
            {
                ["activeDebtsCount"] = debts.Count(d => d.S("status") == "active"),
                ["totalRemainingDebt"] = J.Money(debts.Sum(d => d.N("remainingBalance"))),
                ["statementPath"] = "/app/riders/statement/?iqama=" + Uri.EscapeDataString(rider.S("iqama")),
                ["message"] = "افتح كشف الحساب التفصيلي"
            }
        };
    }

    static void AttachLocalFinance(Store store, ClaimsPrincipal actor, JsonObject body)
    {
        string iqama = body["person"] is JsonObject p ? p.S("iqama") : "";
        if (iqama == "") return;
        store.Read(c =>
        {
            var debts = Store.List(c, "debt").Where(d => d.S("iqama") == iqama && FinancialScopes.Debt(actor, d)).ToList();
            if (body["financialPosition"] is not JsonObject pos) return false;
            pos["activeDebtsCount"] = debts.Count(d => d.S("status") == "active");
            pos["totalRemainingDebt"] = J.Money(debts.Sum(d => d.N("remainingBalance")));
            return true;
        });
    }

    static List<JsonObject> ScopeFilter(Store store, ClaimsPrincipal actor, IReadOnlyList<JsonObject> items)
    {
        if (FinancialScopes.AllCompanies(actor)) return items.ToList();
        return store.Read(c =>
        {
            var allowedIqama = Store.List(c, "rider").Where(r => FinancialScopes.Rider(actor, r)).Select(r => r.S("iqama")).ToHashSet(StringComparer.Ordinal);
            var allowedErp = Store.List(c, "rider").Where(r => FinancialScopes.Rider(actor, r)).Select(r => r.S("erpSourceId")).Where(id => id != "").ToHashSet(StringComparer.OrdinalIgnoreCase);
            return items.Where(i => allowedIqama.Contains(i.S("iqama")) || allowedErp.Contains(i.S("id")) || allowedErp.Contains(i.S("erpEmployeeId"))).ToList();
        });
    }

    static IEnumerable<JsonObject> QualityIssues(JsonObject r)
    {
        if (string.IsNullOrWhiteSpace(r.S("phone")))
            yield return Issue(r, "missing_phone", "store", "أكمل الجوال");
        if (string.IsNullOrWhiteSpace(r.S("city")))
            yield return Issue(r, "missing_city", "store", "أكمل المدينة");
        if (r.S("engagementType") == "SponsoredInternal" && string.IsNullOrWhiteSpace(r.S("iban")))
            yield return Issue(r, "missing_iban_sponsored", "store", "تنبيه آيبان مفقود للمكفول");
    }

    static IEnumerable<JsonObject> QualityIssuesLive(JsonObject r)
    {
        if (r.S("engagementType") == "SponsoredInternal" && string.IsNullOrWhiteSpace(r.S("iban")))
            yield return Issue(r, "missing_iban_sponsored", "erp_live", "تنبيه آيبان مفقود للمكفول");
        if (string.IsNullOrWhiteSpace(r.S("platform")))
            yield return Issue(r, "no_active_platform", "erp_live", "بدون منصة نشطة");
    }

    static JsonObject Issue(JsonObject r, string kind, string source, string action) => new()
    {
        ["name"] = r.S("name"),
        ["iqama"] = r.S("iqama"),
        ["issueKind"] = kind,
        ["source"] = source,
        ["suggestedAction"] = action
    };

    static JsonArray Actions(ClaimsPrincipal user)
    {
        var actions = new JsonArray();
        if (AccessPolicy.Has(user, "riders.view")) actions.Add("export");
        if (AccessPolicy.Has(user, "riders.view_360")) actions.Add("view_360");
        return actions;
    }

    static JsonArray Actions360(ClaimsPrincipal user)
    {
        var actions = Actions(user);
        if (AccessPolicy.Has(user, "debts.view")) actions.Add("open_statement");
        return actions;
    }

    static JsonObject Envelope(object body, string source, DateTimeOffset asOf, JsonArray allowedActions)
    {
        var result = JsonSerializer.SerializeToNode(body, JsonSerializerOptions.Web)!.AsObject();
        result["source"] = source;
        result["asOf"] = asOf.ToString("O");
        result["allowedActions"] = allowedActions.DeepClone();
        return result;
    }

    static SqlConnection OpenErp(IConfiguration config)
    {
        // Reuse Operations connection rules (approved readonly catalog/user only).
        string sourceConnection = config.GetConnectionString("ErpReadonly") ?? config["ERP_READONLY_DB"] ?? "";
        if (string.IsNullOrWhiteSpace(sourceConnection)) throw new ApiError("اتصال مصدر التشغيل غير مضبوط.", 503);
        try
        {
            var source = Connections.Source(sourceConnection);
            source.Open();
            SourceReader.VerifyReadOnly(source);
            return source;
        }
        catch (ImportFailure ex) when (ex.Code is "unapproved_source" or "source_connection_missing" or "source_has_write_permissions")
        {
            throw new ApiError("مصدر التشغيل غير معتمد للقراءة فقط.", 503);
        }
        catch (SqlException) { throw new ApiError("تعذر الاتصال بمصدر التشغيل. حاول مرة أخرى.", 503); }
    }

    static T LiveRead<T>(HttpContext ctx, Func<CancellationToken, T> read, string kind)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
        cancel.CancelAfter(LiveReadTimeout);
        try { return read(cancel.Token); }
        catch (Exception ex) when (cancel.IsCancellationRequested && !ctx.RequestAborted.IsCancellationRequested && ex is OperationCanceledException or SqlException or ImportFailure)
        {
            throw new ApiError("قراءة مصدر التشغيل استغرقت وقتًا طويلًا. حاول مرة أخرى.", 503);
        }
        catch (Exception ex) when (ex is SqlException or ImportFailure or DbException)
        {
            throw ErpLiveErrors.Fail(kind, ex);
        }
    }
}
