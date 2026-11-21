using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.SqlClient;
namespace Payroll;

/// <summary>Workforce live list, reduced 360, Saudi/GOSI, cases and HR catalogs (H3/H4/H6/H9/H10/H11/H13/H20/H27).</summary>
public static class WorkforceApi
{
    public const string PermIban = "riders.iban.view";
    public const string PermSaudiSalary = "riders.saudi_salary.view";
    public const string PermLegalCases = "riders.legal_cases.view";
    public const string PermUnifiedNumber = "riders.unified_number.view";
    static readonly TimeSpan LiveReadTimeout = TimeSpan.FromSeconds(20);
    const int DefaultExportTimeoutSeconds = 120;

    public static void Map(RouteGroupBuilder api, Store store, IConfiguration config, IHostEnvironment env)
    {
        api.MapGet("/workforce", (HttpContext ctx) => List(store, config, env, ctx))
            .WithTags("Workforce").WithSummary("Paged workforce directory (live ERP when configured)");
        api.MapGet("/workforce/summary", (HttpContext ctx) => Summary(store, config, env, ctx))
            .WithTags("Workforce").WithSummary("KPI counts for workforce filter cards");
        api.MapGet("/workforce/distinct", (HttpContext ctx) => DistinctValues(store, config, env, ctx))
            .WithTags("Workforce").WithSummary("Distinct column values for workforce table filters");
        api.MapGet("/workforce/export", (HttpContext ctx) => Export(store, config, env, ctx))
            .WithTags("Workforce").WithSummary("Server-side Excel export of workforce rows (H21)");
        api.MapGet("/workforce/{id}/360", (string id, HttpContext ctx) => Profile360(store, config, env, id, ctx))
            .WithTags("Workforce").WithSummary("Reduced workforce 360 (H6)");
        api.MapGet("/workforce/saudi-payroll-employees", (HttpContext ctx) => Saudi(store, config, env, ctx))
            .WithTags("Workforce").WithSummary("Omar GOSI payroll employees (H10)");
        api.MapGet("/workforce/terminated", (HttpContext ctx) => Terminated(store, config, env, ctx))
            .WithTags("Workforce").WithSummary("Terminated and fleeing people (H11)");
        api.MapGet("/workforce/absence-cases", (HttpContext ctx) => Absence(store, config, env, ctx))
            .WithTags("Workforce").WithSummary("Absence compliance cases (H11)");
        api.MapGet("/workforce/legal-cases", (HttpContext ctx) => Legal(store, config, env, ctx))
            .WithTags("Workforce").WithSummary("HR legal cases (H11/H20)");
        api.MapGet("/workforce/data-quality", (HttpContext ctx) => DataQuality(store, config, env, ctx))
            .WithTags("Workforce").WithSummary("Workforce data-quality issues from live or store");
        api.MapGet("/hr-catalogs/{kind}", (string kind, HttpContext ctx) => Catalogs(store, config, env, kind, ctx))
            .WithTags("HR Catalogs").WithSummary("Live HR catalogs from Omar ERP (H9)");
    }

    static readonly HashSet<string> WorkforceAllowedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "erpEmployeeId", "person", "name", "iqama", "phone", "nationality",
        "status", "statusCode", "engagementType", "workType", "workTypeNameAr", "operationalRole",
        "sponsorNameAr", "sponsor", "sponsorId", "sponsorUnified",
        "platform", "platforms", "platformWorkId", "city", "cityCode", "housing",
        "jobTitle", "paymentModel", "hasCompanyVehicle", "companyVehicleType", "drivingLicense",
        "iban", "ibanSource", "ibanAlert", "mudadSalary",
        "issueKind", "source", "suggestedAction"
    };

    static readonly Dictionary<string, string> WorkforceColumnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["platforms"] = "platform",
        ["sponsor"] = "sponsorNameAr",
        ["person"] = "name"
    };

    static readonly TableFilterContract.FilterOptions WorkforceFilterOptions = new(
        CompositeColumns: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = ["name", "iqama"],
            ["person"] = ["name", "iqama"]
        });

    static object List(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "riders.view");
        var request = ParseList(ctx);
        bool hasContract = TableFilterContract.HasContractParams(ctx.Request.Query);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                if (!hasContract)
                {
                    var page = StoreList(c, ctx.User, request);
                    var items = page.Items.Select(r => StripSensitive(r, ctx.User)).ToList();
                    return Envelope(new { items, total = page.Total, page = request.Page, pageSize = request.PageSize, summary = page.Summary }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
                }
                var full = StoreList(c, ctx.User, request with { Page = 1, PageSize = 50_000, Skip = 0, Take = 50_000 });
                var all = full.Items.Select(r => StripSensitive(r, ctx.User)).ToList();
                var filtered = TableFilterContract.Apply(all, ctx.Request.Query, WorkforceAllowedColumns, WorkforceColumnAliases, options: WorkforceFilterOptions);
                long total = filtered.Count;
                var summary = StoreSummary(filtered);
                var paged = filtered.Skip(request.Skip).Take(request.Take).ToList();
                return Envelope(new { items = paged, total, page = request.Page, pageSize = request.PageSize, summary }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                var scope = Operations.LiveScope(c, ctx.User);
                var liveReq = request with { QueryAlt = request.Query.Length == 0 ? "" : TextSearch.Normalize(request.Query) };
                var page = hasContract
                    ? WorkforceLiveQuery.ExportAll(erp, liveReq with { Page = 1, Skip = 0, Take = int.MaxValue }, scope, ExportTimeoutSeconds(config), token)
                    : WorkforceLiveQuery.Page(erp, liveReq, scope, token);
                var items = page.Items.Select(r => StripSensitive(ApplyIbanAlert(r), ctx.User)).ToList();
                long total = page.Total;
                var summary = page.Summary;
                if (hasContract)
                {
                    items = TableFilterContract.Apply(items, ctx.Request.Query, WorkforceAllowedColumns, WorkforceColumnAliases, options: WorkforceFilterOptions);
                    total = items.Count;
                    summary = StoreSummary(items);
                    items = items.Skip(request.Skip).Take(request.Take).ToList();
                }
                return Envelope(new { items, total, page = request.Page, pageSize = request.PageSize, summary }, Operations.SourceLive, page.AsOf, Actions(ctx.User));
            });
        }, "workforce", hasContract ? TimeSpan.FromSeconds(ExportTimeoutSeconds(config)) : null);
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
            return store.Read(c =>
            {
                var scope = Operations.LiveScope(c, ctx.User);
                var (summary, asOf) = WorkforceLiveQuery.SummaryOnly(erp, request with { QueryAlt = request.Query.Length == 0 ? "" : TextSearch.Normalize(request.Query) }, scope, token);
                return Envelope(new { summary }, Operations.SourceLive, asOf, Actions(ctx.User));
            });
        }, "workforce");
    }

    static object DistinctValues(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "riders.view");
        _ = TableFilterContract.ParseFilters(ctx.Request.Query, WorkforceAllowedColumns, WorkforceColumnAliases);
        string column = (ctx.Request.Query["column"].ToString() ?? "").Trim();
        if (!WorkforceAllowedColumns.Contains(column))
            return new JsonObject { ["column"] = column, ["values"] = new JsonArray(), ["counts"] = new JsonObject() };
        var request = ParseList(ctx) with { Page = 1, PageSize = WorkforceLiveQuery.MaxPageSize, Skip = 0, Take = WorkforceLiveQuery.MaxPageSize };
        List<JsonObject> rows;
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
        {
            rows = store.Read(c => StoreList(c, ctx.User, request with { Page = 1, PageSize = 50_000, Skip = 0, Take = 50_000 }).Items.Select(r => StripSensitive(r, ctx.User)).ToList());
        }
        else
        {
            rows = LiveRead(ctx, token =>
            {
                using var erp = OpenErp(config);
                return store.Read(c =>
                {
                    var scope = Operations.LiveScope(c, ctx.User);
                    var fullReq = request with { Page = 1, Skip = 0, Take = int.MaxValue, QueryAlt = request.Query.Length == 0 ? "" : TextSearch.Normalize(request.Query) };
                    return WorkforceLiveQuery.ExportAll(erp, fullReq, scope, ExportTimeoutSeconds(config), token).Items.Select(r => StripSensitive(ApplyIbanAlert(r), ctx.User)).ToList();
                });
            }, "workforce-distinct", TimeSpan.FromSeconds(ExportTimeoutSeconds(config)));
        }
        return TableFilterContract.BuildDistinct(rows, column, ctx.Request.Query, WorkforceAllowedColumns, WorkforceColumnAliases, options: WorkforceFilterOptions);
    }

    static IResult Export(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "riders.view");
        var request = ParseList(ctx) with { Page = 1, PageSize = WorkforceLiveQuery.MaxPageSize, Skip = 0, Take = WorkforceLiveQuery.MaxPageSize };
        List<JsonObject> all = [];
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
        {
            store.Read(c =>
            {
                var page = StoreList(c, ctx.User, request with { Page = 1, PageSize = 50_000, Skip = 0, Take = 50_000 });
                all = page.Items.Select(r => StripSensitive(r, ctx.User)).ToList();
                return true;
            });
        }
        else
        {
            int exportTimeout = ExportTimeoutSeconds(config);
            LiveRead(ctx, token =>
            {
                using var erp = OpenErp(config);
                return store.Read(c =>
                {
                    var scope = Operations.LiveScope(c, ctx.User);
                    var req = request with
                    {
                        Page = 1,
                        Skip = 0,
                        Take = int.MaxValue,
                        QueryAlt = request.Query.Length == 0 ? "" : TextSearch.Normalize(request.Query)
                    };
                    var page = WorkforceLiveQuery.ExportAll(erp, req, scope, exportTimeout, token);
                    all = page.Items.Select(r => StripSensitive(ApplyIbanAlert(r), ctx.User)).ToList();
                    return true;
                });
            }, "workforce-export", TimeSpan.FromSeconds(exportTimeout));
        }
        if (TableFilterContract.HasContractParams(ctx.Request.Query))
            all = TableFilterContract.Apply(all, ctx.Request.Query, WorkforceAllowedColumns, WorkforceColumnAliases, options: WorkforceFilterOptions);
        bool showIban = AccessPolicy.Has(ctx.User, PermIban);
        // R1: IBAN, bank short code and bank name are three separate columns in the export.
        var headers = new[] { "الاسم", "رقم الإقامة", "الجوال", "الجنسية", "الحالة", "نوع العلاقة", "نوع العمل", "الكفيل", "المنصة", "معرف المنصة", "المدينة", "السكن", "المسمى", "نموذج الدفع", "مركبة الشركة", "الرخصة", showIban ? "الآيبان" : "الآيبان (مخفي)", "كود البنك", "اسم البنك", "تنبيه الآيبان", "راتب مدد" };
        var cols = headers.Select(h => new ExcelColumn(h)).ToArray();
        var rows = all.Select(r =>
        {
            var bank = showIban ? BankPayrollFiles.ExportBank(r.S("iban")) : ("", "");
            return new object?[]
        {
            r.S("name"),
            r.S("iqama"),
            r.S("phone"),
            r.S("nationality"),
            r.S("status"),
            r.S("engagementType"),
            r.S("workType"),
            r.S("sponsorNameAr"),
            r.S("platform"),
            r.S("platformWorkId"),
            r.S("city"),
            r.S("housing"),
            r.S("jobTitle"),
            r.S("paymentModel"),
            r.B("hasCompanyVehicle") ? r.S("companyVehicleType") : "",
            r.S("drivingLicense"),
            showIban ? r.S("iban") : "",
            bank.Item1,
            bank.Item2,
            r.S("ibanAlert"),
            r.S("mudadSalary")
        };
        }).ToList();
        return Results.File(ExcelFiles.Write(cols, rows), ExcelFiles.Mime, "workforce-export.xlsx");
    }

    static object Profile360(Store store, IConfiguration config, IHostEnvironment env, string id, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "riders.view_360");
        AccessPolicy.Demand(ctx.User, "debts.view");
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c => Envelope(Strip360(Store360(c, ctx.User, id), ctx.User), Operations.SourceStore, DateTimeOffset.UtcNow, Actions360(ctx.User)));
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
                DemandRiderOrNotFound(ctx.User, rider);
                string linked = rider.S("erpSourceId");
                return linked == "" ? "" : linked;
            });
            if (erpId == "")
                return store.Read(c => Envelope(Strip360(Store360(c, ctx.User, id), ctx.User), Operations.SourceLive, DateTimeOffset.UtcNow, Actions360(ctx.User)));
        }
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                var scope = Operations.LiveScope(c, ctx.User);
                try
                {
                    var body = WorkforceLiveQuery.Person360(erp, erpId, scope, token);
                    AttachLocalFinance(store, ctx.User, body);
                    AttachNetDue(store, ctx.User, body);
                    if (body["person"] is JsonObject person)
                        body["person"] = StripSensitive(ApplyIbanAlert(person), ctx.User);
                    return Envelope(Strip360(body, ctx.User), Operations.SourceLive, DateTimeOffset.UtcNow, Actions360(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "employee_not_found")
                {
                    throw new ApiError("الموظف غير موجود.", 404);
                }
            });
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
            return store.Read(c =>
            {
                var scope = Operations.LiveScope(c, ctx.User);
                var items = WorkforceLiveQuery.SaudiPayrollEmployees(erp, scope, token)
                    .Select(r => StripSaudi(r, ctx.User)).ToList();
                return Envelope(new { items, total = items.Count }, Operations.SourceLive, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        }, "saudi-payroll");
    }

    static object Terminated(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "riders.view");
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                var items = Store.List(c, "rider").Where(ErpRiderClassification.ListedAsRider)
                    .Where(r => FinancialScopes.Rider(ctx.User, r) || FinancialScopes.AllCompanies(ctx.User))
                    .Where(r => StorePlatformInScope(ctx.User, r))
                    .Where(r => r.S("status") is "Terminated" or "Fleeing" or "Archived")
                    .Select(StorePerson)
                    .ToList();
                return Envelope(new { items, total = items.Count }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                var scope = Operations.LiveScope(c, ctx.User);
                var items = WorkforceLiveQuery.TerminatedAndFleeing(erp, scope, token);
                return Envelope(new { items, total = items.Count }, Operations.SourceLive, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
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
            return store.Read(c =>
            {
                var scope = Operations.LiveScope(c, ctx.User);
                var items = WorkforceLiveQuery.AbsenceCases(erp, scope, token);
                return Envelope(new { items, total = items.Count }, Operations.SourceLive, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        }, "absence-cases");
    }

    static object Legal(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, PermLegalCases);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return Envelope(new { items = Array.Empty<JsonObject>(), total = 0, message = WorkforceLiveQuery.NoSourceYet }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                var scope = Operations.LiveScope(c, ctx.User);
                var items = WorkforceLiveQuery.LegalCases(erp, scope, token);
                return Envelope(new { items, total = items.Count }, Operations.SourceLive, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        }, "legal-cases");
    }

    static object DataQuality(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "riders.view");
        var request = ParseList(ctx);
        bool hasContract = TableFilterContract.HasContractParams(ctx.Request.Query);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                var people = Store.List(c, "rider").Where(ErpRiderClassification.ListedAsRider)
                    .Where(r => FinancialScopes.Rider(ctx.User, r) || FinancialScopes.AllCompanies(ctx.User))
                    .Where(r => StorePlatformInScope(ctx.User, r)).ToList();
                var issues = people.SelectMany(QualityIssues).ToList();
                if (hasContract)
                    issues = TableFilterContract.Apply(issues, ctx.Request.Query, WorkforceAllowedColumns, WorkforceColumnAliases);
                long total = issues.Count;
                var page = issues.Skip(request.Skip).Take(request.Take).ToList();
                return Envelope(new { items = page, total, page = request.Page, pageSize = request.PageSize }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                var scope = Operations.LiveScope(c, ctx.User);
                var (items, total) = WorkforceLiveQuery.DataQualityPage(erp, request with { QueryAlt = "" }, scope, token);
                var list = items.ToList();
                if (hasContract)
                {
                    list = TableFilterContract.Apply(list, ctx.Request.Query, WorkforceAllowedColumns, WorkforceColumnAliases);
                    total = list.Count;
                }
                return Envelope(new { items = list, total, page = request.Page, pageSize = request.PageSize }, Operations.SourceLive, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
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

    static readonly HashSet<string> NativeWorkforceSorts = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "-name", "status", "-status", "iqama", "-iqama", "id", "-id"
    };

    static WorkforceLiveQuery.ListRequest ParseList(HttpContext ctx)
    {
        _ = TableFilterContract.ParseFilters(ctx.Request.Query, WorkforceAllowedColumns, WorkforceColumnAliases);
        var sorts = TableFilterContract.ParseSort(ctx.Request.Query, WorkforceAllowedColumns, WorkforceColumnAliases);
        try
        {
            var dict = ctx.Request.Query.Keys.ToDictionary(k => k, k => ctx.Request.Query[k].ToString(), StringComparer.OrdinalIgnoreCase);
            if (sorts.Count > 0)
            {
                var primary = sorts[0];
                string candidate = string.Equals(primary.Dir, "desc", StringComparison.OrdinalIgnoreCase)
                    ? "-" + primary.Col
                    : primary.Col;
                dict["sort"] = NativeWorkforceSorts.Contains(candidate) ? candidate : "name";
            }
            var req = WorkforceLiveQuery.ParseRequest(dict);
            if (req.SponsorUnified.Length > 0 && FinancialCore.FindSponsor(req.SponsorUnified) is { } sponsor && !string.IsNullOrWhiteSpace(sponsor.UnifiedNumber))
                req = req with { SponsorUnified = sponsor.UnifiedNumber };
            return req;
        }
        catch (ImportFailure ex) when (ex.Code == "invalid_page") { throw new ApiError("رقم الصفحة وحجمها يجب أن يكونا عددين صحيحين موجبين."); }
        catch (ImportFailure ex) when (ex.Code == "invalid_live_sort") { throw new ApiError("حقل الترتيب غير صالح.", 400); }
        catch (ImportFailure ex) when (ex.Code == "invalid_live_status") { throw new ApiError("حالة غير معروفة.", 400); }
    }

    sealed record StorePage(IReadOnlyList<JsonObject> Items, long Total, JsonObject Summary);

    static StorePage StoreList(DbConnection c, ClaimsPrincipal actor, WorkforceLiveQuery.ListRequest request)
    {
        var rows = Store.List(c, "rider").Where(ErpRiderClassification.ListedAsRider)
            .Where(r => FinancialScopes.Rider(actor, r) || FinancialScopes.AllCompanies(actor))
            .Where(r => StorePlatformInScope(actor, r))
            .Select(StorePerson).ToList();
        rows = FilterStore(rows, request);
        long total = rows.Count;
        var page = rows.Skip(request.Skip).Take(request.Take).ToList();
        return new(page, total, StoreSummary(rows));
    }

    /// <summary>Non-admins are limited to finance.platforms claims (same as live PlatformRestricted).</summary>
    static bool StorePlatformInScope(ClaimsPrincipal actor, JsonObject rider)
    {
        if (AccessPolicy.Administrator(actor)) return true;
        var allowed = actor.FindAll("finance.platforms").Select(cl => cl.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0) return false;
        string raw = rider.S("platforms", rider.S("platform"));
        if (string.IsNullOrWhiteSpace(raw)) return false;
        foreach (string part in raw.Split([',', ';', '|', '/', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (allowed.Contains(part)) return true;
            var app = PayPlatforms.Table.FirstOrDefault(a =>
                string.Equals(a.Platform, part, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a.App, part, StringComparison.OrdinalIgnoreCase));
            if (app != null && allowed.Contains(app.Platform)) return true;
        }
        return false;
    }

    static void DemandRiderOrNotFound(ClaimsPrincipal user, JsonObject rider)
    {
        if (!FinancialScopes.Rider(user, rider) && !FinancialScopes.AllCompanies(user))
            throw new ApiError("الموظف غير موجود.", 404);
        if (!StorePlatformInScope(user, rider))
            throw new ApiError("الموظف غير موجود.", 404);
    }

    static int ExportTimeoutSeconds(IConfiguration config)
    {
        string raw = config["Workforce:ExportTimeoutSeconds"] ?? config["WORKFORCE_EXPORT_TIMEOUT_SECONDS"] ?? "";
        if (int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int seconds) && seconds is >= 30 and <= 600)
            return seconds;
        return DefaultExportTimeoutSeconds;
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
        if (request.Platform.Length > 0)
        {
            var accepted = new HashSet<string>(ErpPlatformCode.AcceptOrSelf(request.Platform), StringComparer.OrdinalIgnoreCase);
            string reqPlatform = PayPlatforms.For(request.Platform).Platform;
            q = q.Where(r =>
            {
                string rp = r.S("platform");
                return rp.Length > 0
                    && (accepted.Contains(rp)
                        || (reqPlatform != "Other" && string.Equals(PayPlatforms.For(rp).Platform, reqPlatform, StringComparison.OrdinalIgnoreCase)));
            });
        }
        if (request.CityCode.Length > 0)
            q = q.Where(r => string.Equals(r.S("cityCode"), request.CityCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.S("city"), request.CityCode, StringComparison.OrdinalIgnoreCase));
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
        var byCompany = list
            .Where(r => r.S("sponsorUnified") != "" || r.S("sponsorNameAr") != "")
            .GroupBy(r => r.S("sponsorUnified", r.S("sponsorNameAr")))
            .Select(g => (JsonNode?)new JsonObject
            {
                ["sponsorUnified"] = g.First().S("sponsorUnified"),
                ["sponsorNameAr"] = g.First().S("sponsorNameAr"),
                ["totalCount"] = g.Count()
            })
            .ToArray();
        return new JsonObject
        {
            ["totalCount"] = list.Count,
            ["sponsoredCount"] = list.Count(r => r.S("engagementType") == "SponsoredInternal"),
            ["sponsoredRiders"] = list.Count(r => r.S("engagementType") == "SponsoredInternal" && !r.B("isEmployee")),
            ["sponsoredAdmins"] = list.Count(r => r.S("engagementType") == "SponsoredInternal" && r.B("isEmployee")),
            ["sponsoredMissingIban"] = list.Count(r => r.S("engagementType") == "SponsoredInternal" && (r.S("ibanAlert").Length > 0 || (r["iban"] != null && string.IsNullOrWhiteSpace(r.S("iban"))))),
            ["byCompany"] = new JsonArray(byCompany)
        };
    }

    static JsonObject StorePerson(JsonObject r) => ApplyIbanAlert(new()
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
        ["sponsorUnified"] = r.S("sponsorUnified", FinancialCore.FindSponsor(r.S("sponsorId", r.S("sponsor")))?.UnifiedNumber ?? ""),
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
        ["operationalRole"] = r.S("operationalRole"),
        ["ibanSource"] = string.IsNullOrWhiteSpace(r.S("iban")) ? "" : "erp"
    });

    static JsonObject Store360(DbConnection c, ClaimsPrincipal actor, string key)
    {
        string iqama = key;
        try { iqama = J.Iqama(key); } catch { /* may be erp guid — fall through */ }
        var rider = Store.Get(c, iqama, "rider")
            ?? Store.List(c, "rider").FirstOrDefault(r => r.S("erpSourceId") == key)
            ?? throw new ApiError("الموظف غير موجود.", 404);
        DemandRiderOrNotFound(actor, rider);
        var person = StorePerson(rider);
        var debts = Store.List(c, "debt").Where(d => d.S("iqama") == rider.S("iqama") && FinancialScopes.Debt(actor, d)).ToList();
        var works = Store.List(c, "work").Where(w => w.S("iqama") == rider.S("iqama")).ToList();
        var netByPeriod = NetDueByPeriod(c, actor, rider.S("iqama"));
        return new JsonObject
        {
            ["person"] = person,
            ["workRecord3Months"] = new JsonArray(works.Take(50).Select(w =>
            {
                string period = w.S("period", w.S("from"));
                string month = period.Length >= 7 ? period[..7] : period;
                object net = netByPeriod.TryGetValue(month, out decimal due) ? due : WorkforceLiveQuery.NoSourceYet;
                return (JsonNode?)new JsonObject
                {
                    ["month"] = month,
                    ["platform"] = w.S("platform"),
                    ["workDays"] = WorkforceLiveQuery.NoSourceYet,
                    ["orders"] = w.N("orders"),
                    ["netDue"] = net is decimal d ? J.Money(d) : net.ToString(),
                    ["status"] = w.S("status")
                };
            }).ToArray()),
            ["financialEvaluation"] = new JsonObject { ["status"] = "unavailable", ["message"] = WorkforceLiveQuery.NoSourceYet },
            ["iqamaImage"] = new JsonObject { ["status"] = "pending_permission", ["message"] = WorkforceLiveQuery.ImagePending },
            ["ibanImage"] = new JsonObject { ["status"] = "pending_permission", ["message"] = WorkforceLiveQuery.ImagePending },
            ["promissoryNotes"] = new JsonArray(),
            ["platformAccounts"] = new JsonArray(),
            ["legalCases"] = new JsonArray(),
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

    static void AttachNetDue(Store store, ClaimsPrincipal actor, JsonObject body)
    {
        string iqama = body["person"] is JsonObject p ? p.S("iqama") : "";
        if (iqama == "" || body["workRecord3Months"] is not JsonArray works) return;
        store.Read(c =>
        {
            var netByPeriod = NetDueByPeriod(c, actor, iqama);
            foreach (var node in works)
            {
                if (node is not JsonObject row) continue;
                string month = row.S("month");
                if (month.Length >= 7 && netByPeriod.TryGetValue(month[..7], out decimal due))
                    row["netDue"] = J.Money(due);
                else if (row.S("netDue") == "" || row.S("netDue") == WorkforceLiveQuery.NoSourceYet)
                    row["netDue"] = WorkforceLiveQuery.NoSourceYet;
            }
            return true;
        });
    }

    static Dictionary<string, decimal> NetDueByPeriod(DbConnection c, ClaimsPrincipal actor, string iqama)
    {
        var map = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var run in Store.List(c, "run").Where(r => r.S("iqama") == iqama && FinancialScopes.Ledger(actor, r)))
        {
            string period = run.S("period");
            if (period.Length < 7) continue;
            string month = period[..7];
            map[month] = run.N("net", run.N("amount"));
        }
        foreach (var cycle in Store.List(c, "pay_cycle").Where(cy => cy.S("status") is "approved" or "salary_approved" or "ready"))
        {
            string period = cycle.S("period");
            if (period.Length < 7) continue;
            string month = period[..7];
            var rider = (cycle["riders"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(r => r.S("rider") == iqama);
            if (rider == null) continue;
            if (!FinancialScopes.AllCompanies(actor) && !FinancialScopes.Sponsor(actor, cycle.S("companyId"))) continue;
            map[month] = rider.N("net", rider.N("salaryNet"));
        }
        return map;
    }

    /// <summary>H35/H22: Mudad → Omar HR → forms. Alert when sponsored and still missing.</summary>
    static JsonObject ApplyIbanAlert(JsonObject person)
    {
        string mudad = person.S("mudadIban");
        string erp = person.S("iban");
        string form = person.S("formIban");
        string chosen = "";
        string source = "";
        if (!string.IsNullOrWhiteSpace(mudad) && mudad != WorkforceLiveQuery.NoSourceYet) { chosen = mudad; source = "mudad"; }
        else if (!string.IsNullOrWhiteSpace(erp)) { chosen = erp; source = "erp"; }
        else if (!string.IsNullOrWhiteSpace(form)) { chosen = form; source = "forms"; }
        if (chosen != "") person["iban"] = chosen;
        person["ibanSource"] = source;
        bool sponsored = person.S("engagementType") == "SponsoredInternal";
        person["ibanAlert"] = sponsored && string.IsNullOrWhiteSpace(chosen) ? "آيبان مفقود للمكفول" : "";
        return person;
    }

    static JsonObject StripSensitive(JsonObject person, ClaimsPrincipal user)
    {
        var copy = person.DeepClone().AsObject();
        if (!AccessPolicy.Has(user, PermIban))
        {
            copy["iban"] = "";
            copy.Remove("personalIban");
            copy.Remove("formIban");
            copy.Remove("mudadIban");
        }
        return copy;
    }

    static JsonObject StripSaudi(JsonObject row, ClaimsPrincipal user)
    {
        var copy = row.DeepClone().AsObject();
        if (!AccessPolicy.Has(user, PermSaudiSalary))
            copy["salary"] = "";
        if (!AccessPolicy.Has(user, PermIban))
            copy["personalIban"] = "";
        if (!AccessPolicy.Has(user, PermUnifiedNumber))
            copy["unifiedNationalNumber"] = "";
        return copy;
    }

    static JsonObject Strip360(JsonObject body, ClaimsPrincipal user)
    {
        var copy = body.DeepClone().AsObject();
        if (copy["person"] is JsonObject person)
            copy["person"] = StripSensitive(person, user);
        if (!AccessPolicy.Has(user, PermLegalCases))
            copy.Remove("legalCases");
        return copy;
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
        if (AccessPolicy.Has(user, PermIban)) actions.Add("view_iban");
        if (AccessPolicy.Has(user, PermSaudiSalary)) actions.Add("view_saudi_salary");
        if (AccessPolicy.Has(user, PermLegalCases)) actions.Add("view_legal_cases");
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

    static T LiveRead<T>(HttpContext ctx, Func<CancellationToken, T> read, string kind, TimeSpan? timeout = null)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
        cancel.CancelAfter(timeout ?? LiveReadTimeout);
        try { return read(cancel.Token); }
        catch (Exception ex) when (cancel.IsCancellationRequested && !ctx.RequestAborted.IsCancellationRequested && ex is OperationCanceledException or SqlException or ImportFailure)
        {
            throw new ApiError("قراءة مصدر التشغيل استغرقت وقتًا طويلًا. حاول مرة أخرى.", 503);
        }
        catch (SqlException ex) when (ex.Number is 208 or 207 || ex.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
        {
            throw ErpLiveErrors.Fail(kind, ex);
        }
        catch (Exception ex) when (ex is ImportFailure or DbException)
        {
            throw ErpLiveErrors.Fail(kind, ex);
        }
    }
}
