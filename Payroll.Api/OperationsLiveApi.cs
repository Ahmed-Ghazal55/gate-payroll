using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.SqlClient;
namespace Payroll;

/// <summary>Operations &amp; fleet live reads (U-W2-34): cities, housing, vehicles period, fuel. View-only.</summary>
public static class OperationsLiveApi
{
    static readonly TimeSpan LiveReadTimeout = TimeSpan.FromSeconds(20);
    const int DefaultExportTimeoutSeconds = 120;

    public static void Map(RouteGroupBuilder api, Store store, IConfiguration config, IHostEnvironment env)
    {
        api.MapGet("/operations/cities-summary", (HttpContext ctx) => Cities(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Per operating city live summary (O3)");
        api.MapGet("/operations/cities/{id}", (string id, HttpContext ctx) => City(store, config, env, id, ctx))
            .WithTags("Operations").WithSummary("Operating city detail split by registration and app (O3)");
        api.MapGet("/operations/housing", (HttpContext ctx) => Housing(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Housing list with occupancy KPIs (O4)");
        api.MapGet("/operations/housing/{id}/rooms", (string id, HttpContext ctx) => HousingRooms(store, config, env, id, ctx))
            .WithTags("Operations").WithSummary("Housing rooms and residents (O4)");
        api.MapGet("/operations/vehicles/period-report", (HttpContext ctx) => PeriodReport(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Vehicle/rider period report (O5)");
        api.MapGet("/operations/vehicles/purchase-invoices", (HttpContext ctx) => Purchases(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Vehicle purchase receipts (O5)");
        api.MapGet("/operations/vehicles/purchase-invoices/{id}", (string id, HttpContext ctx) => PurchaseDetail(store, config, env, id, ctx))
            .WithTags("Operations").WithSummary("Purchase receipt lines (O5)");
        api.MapGet("/operations/vehicles/oil-changes", (HttpContext ctx) => OilChangesPeriod(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Oil changes in period (M9)");
        api.MapGet("/operations/vehicles/{id}/oil-changes", (string id, HttpContext ctx) => OilChanges(store, config, env, id, ctx))
            .WithTags("Operations").WithSummary("Oil changes for a vehicle (O5)");
        api.MapGet("/operations/vehicles/{id}", (string id, HttpContext ctx) => Vehicle(store, config, env, id, ctx))
            .WithTags("Operations").WithSummary("Vehicle details drawer including VIN (O5)");
        api.MapGet("/operations/fuel-cards", (HttpContext ctx) => Fuel(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Fuel cards and monthly usage (O6)");
        api.MapGet("/operations/fuel-cards/assignments", (HttpContext ctx) => FuelLog(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Fuel card hand-over log (O6)");
        api.MapGet("/operations/cities-summary/export", (HttpContext ctx) => ExportCities(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Export cities summary");
        api.MapGet("/operations/housing/export", (HttpContext ctx) => ExportHousing(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Export housing list");
        api.MapGet("/operations/vehicles/period-report/export", (HttpContext ctx) => ExportPeriod(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Export vehicles period report");
        api.MapGet("/operations/fuel-cards/export", (HttpContext ctx) => ExportFuel(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Export fuel cards");
        api.MapGet("/operations/distinct", (HttpContext ctx) => DistinctValues(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Distinct column values for operations table filters");
    }

    static object DistinctValues(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        string column = (ctx.Request.Query["column"].ToString() ?? "").Trim();
        string tab = (ctx.Request.Query["tab"].ToString() ?? "cities").Trim().ToLowerInvariant();
        string view = (ctx.Request.Query["view"].ToString() ?? "").Trim().ToLowerInvariant();
        if (column.Length == 0)
            return new JsonObject { ["column"] = "", ["values"] = new JsonArray() };

        // Closed sets that never require a heavy scan
        if (tab == "fuel" && column is "status")
            return new JsonObject { ["column"] = column, ["values"] = new JsonArray("نشطة", "غير نشطة") };
        if (tab == "vehicles" && view == "invoices" && column is "status")
            return new JsonObject { ["column"] = column, ["values"] = new JsonArray("مرحّل", "معكوس") };
        if (tab == "fuel" && column is "provider" or "providerNameAr" or "cardName")
        {
            if (column is "provider" or "providerNameAr")
                return new JsonObject { ["column"] = column, ["values"] = new JsonArray("شركة بترو اب", "شركة سيارة اب") };
        }

        string field = (tab, view, column) switch
        {
            ("cities", _, "nameAr") => "nameAr",
            ("cities", _, "status") => "status",
            ("cities", _, "apps") or ("cities", _, "appsWorking") => "appsWorking",
            ("housing", _, "city") => "city",
            ("housing", _, "status") => "status",
            ("vehicles", "invoices", "sup") or ("vehicles", "invoices", "supplierNameAr") => "supplierNameAr",
            ("vehicles", "invoices", "status") => "status",
            ("vehicles", "oil", "plate") or ("vehicles", "oil", "plateNumberAr") => "plateNumberAr",
            ("vehicles", "oil", "vehicleType") => "vehicleType",
            ("vehicles", _, "city") => "city",
            ("vehicles", _, "vehicleType") => "vehicleType",
            ("vehicles", _, "plate") or ("vehicles", _, "plateNumberAr") => "plateNumberAr",
            ("fuel", _, "cardName") => "cardName",
            ("fuel", _, "cardNumber") or ("fuel", _, "card") => "cardNumber",
            ("fuel", _, "plate") or ("fuel", _, "plateNumber") => "plateNumber",
            ("fuel", _, "fuelType") => "fuelType",
            _ => ""
        };
        if (field.Length == 0)
            return new JsonObject { ["column"] = column, ["values"] = new JsonArray() };

        var (from, to) = ParsePeriod(ctx, defaultCurrentMonth: true);
        DateOnly month = ParseMonth(ctx);
        string mode = (ctx.Request.Query["mode"].ToString() is { Length: > 0 } m && m == "rider") ? "rider" : "vehicle";
        List<JsonObject> rows = [];
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
        {
            rows = store.Read(c => tab switch
            {
                "cities" => BuildStoreCities(c, ctx.User).Items.ToList(),
                "housing" => ExtractItems(BuildStoreHousing(c, ctx.User, 1, 200, "", "", "")),
                "vehicles" when view == "invoices" => ExtractItems(BuildStorePurchases(c, ctx.User, from, to, 1, 200, "")),
                "vehicles" => ExtractItems(BuildStorePeriod(c, ctx.User, from, to, mode, 1, 200)),
                "fuel" => ExtractItems(BuildStoreFuel(c, ctx.User, month, 1, 200)),
                _ => new List<JsonObject>()
            });
        }
        else
        {
            rows = LiveRead(ctx, token =>
            {
                using var erp = OpenErp(config);
                return store.Read(c =>
                {
                    var scope = Operations.LiveScope(c, ctx.User);
                    return (tab, view) switch
                    {
                        ("cities", _) => OperationsLiveQuery.CitiesSummary(erp, scope, token).Items.ToList(),
                        ("housing", _) => OperationsLiveQuery.HousingPage(erp, 1, OperationsLiveQuery.MaxPageSize, "", "", "", scope, token).Items.ToList(),
                        ("vehicles", "invoices") => OperationsLiveQuery.PurchaseInvoices(erp, from, to, 1, OperationsLiveQuery.MaxPageSize, "", scope, null, token).Items.ToList(),
                        ("vehicles", "oil") => OperationsLiveQuery.OilChangesPeriod(erp, from, to, 1, OperationsLiveQuery.MaxPageSize, "", scope, export: false, token).Items.ToList(),
                        ("vehicles", _) => OperationsLiveQuery.VehiclesPeriodReport(erp, from, to, mode, 1, OperationsLiveQuery.MaxPageSize, "", scope, detailed: false, token).Items.ToList(),
                        ("fuel", "log") => OperationsLiveQuery.FuelAssignments(erp, from, to, 1, OperationsLiveQuery.MaxPageSize, scope, token).Items.ToList(),
                        ("fuel", _) => OperationsLiveQuery.FuelCards(erp, month, "", 1, OperationsLiveQuery.MaxPageSize, "", scope, token).Items.ToList(),
                        _ => new List<JsonObject>()
                    };
                });
            }, "operations-distinct");
        }
        var values = rows
            .Select(r => r.S(field).Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .Select(v => (JsonNode?)v)
            .ToArray();
        return new JsonObject { ["column"] = column, ["values"] = new JsonArray(values) };
    }

    static object Cities(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                var page = BuildStoreCities(c, ctx.User);
                return Envelope(new { items = page.Items, total = page.Total, summary = page.Summary }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                var page = OperationsLiveQuery.CitiesSummary(erp, Operations.LiveScope(c, ctx.User), token);
                return Envelope(new { items = page.Items, total = page.Total, summary = page.Summary }, Operations.SourceLive, page.AsOf, Actions(ctx.User));
            });
        }, "operations-cities");
    }

    static object City(Store store, IConfiguration config, IHostEnvironment env, string id, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                var hit = BuildStoreCities(c, ctx.User).Items.FirstOrDefault(r => r.S("id") == id)
                    ?? throw new ApiError("المدينة غير موجودة.", 404);
                hit["byRegistration"] = new JsonArray();
                hit["byApp"] = new JsonArray();
                return Envelope(hit, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                try
                {
                    var detail = OperationsLiveQuery.CityDetail(erp, id, Operations.LiveScope(c, ctx.User), token);
                    return Envelope(detail.Body, Operations.SourceLive, detail.AsOf, Actions(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "city_not_found") { throw new ApiError("المدينة غير موجودة.", 404); }
            });
        }, "operations-city");
    }

    static object Housing(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        var (page, pageSize, city, status, search) = ParsePage(ctx);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c => Envelope(BuildStoreHousing(c, ctx.User, page, pageSize, city, status, search), Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User)));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                var result = OperationsLiveQuery.HousingPage(erp, page, pageSize, city, status, search, Operations.LiveScope(c, ctx.User), token);
                return Envelope(new { items = result.Items, total = result.Total, page, pageSize, summary = result.Summary }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
            });
        }, "operations-housing");
    }

    static object HousingRooms(Store store, IConfiguration config, IHostEnvironment env, string id, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                var house = Operations.List(c, ctx.User, "housing").FirstOrDefault(r => r.S("id") == id)
                    ?? throw new ApiError("السكن غير موجود.", 404);
                return Envelope(new
                {
                    id = house.S("id"),
                    nameAr = house.S("nameAr"),
                    code = house.S("code"),
                    status = house.S("status"),
                    city = "",
                    supervisor = (string?)null,
                    supervisorSource = OperationsLiveQuery.NoSourceYet,
                    rooms = house["rooms"] ?? new JsonArray()
                }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                try
                {
                    var detail = OperationsLiveQuery.HousingRooms(erp, id, Operations.LiveScope(c, ctx.User), token);
                    return Envelope(detail.Body, Operations.SourceLive, detail.AsOf, Actions(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "housing_not_found") { throw new ApiError("السكن غير موجود.", 404); }
            });
        }, "operations-housing-rooms");
    }

    static object PeriodReport(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        var (from, to) = ParsePeriod(ctx, defaultCurrentMonth: true);
        string mode = (ctx.Request.Query["mode"].ToString() is { Length: > 0 } m ? m : "vehicle").Trim();
        if (mode is not ("vehicle" or "rider")) throw new ApiError("وضع العرض يجب أن يكون vehicle أو rider.");
        var (page, pageSize, _, _, search) = ParsePage(ctx);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c => Envelope(BuildStorePeriod(c, ctx.User, from, to, mode, page, pageSize), Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User)));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                try
                {
                    var result = OperationsLiveQuery.VehiclesPeriodReport(erp, from, to, mode, page, pageSize, search, Operations.LiveScope(c, ctx.User), detailed: false, token);
                    AssertNoVin(result.Items);
                    return Envelope(new
                    {
                        items = result.Items,
                        total = result.Total,
                        page,
                        pageSize,
                        summary = result.Summary,
                        mode,
                        from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "invalid_period") { throw new ApiError("فترة غير صالحة."); }
            });
        }, "operations-vehicles-period");
    }

    static object Vehicle(Store store, IConfiguration config, IHostEnvironment env, string id, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                var row = Operations.List(c, ctx.User, "vehicle").FirstOrDefault(r => r.S("id") == id)
                    ?? throw new ApiError("المركبة غير موجودة.", 404);
                return Envelope(row, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                try
                {
                    var detail = OperationsLiveQuery.VehicleDetail(erp, id, Operations.LiveScope(c, ctx.User), token);
                    return Envelope(detail.Body, Operations.SourceLive, detail.AsOf, Actions(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "vehicle_not_found") { throw new ApiError("المركبة غير موجودة.", 404); }
            });
        }, "operations-vehicle");
    }

    static object OilChanges(Store store, IConfiguration config, IHostEnvironment env, string id, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
        {
            _ = id;
            return Envelope(new { items = Array.Empty<object>(), total = 0, summary = new { oilChangesCount = 0, oilChangesCost = 0, message = OperationsLiveQuery.NoSourceYet } }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
        }
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                try
                {
                    var result = OperationsLiveQuery.VehicleOilChanges(erp, id, Operations.LiveScope(c, ctx.User), token);
                    return Envelope(new { items = result.Items, total = result.Total, summary = result.Summary }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "vehicle_not_found") { throw new ApiError("المركبة غير موجودة.", 404); }
            });
        }, "operations-oil");
    }

    static object OilChangesPeriod(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        var (from, to) = ParsePeriod(ctx, defaultCurrentMonth: true);
        var (page, pageSize, _, _, search) = ParsePage(ctx);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return Envelope(new { items = Array.Empty<object>(), total = 0L, page, pageSize, summary = new { oilChangesCount = (long?)null, oilChangesCost = (decimal?)null, message = OperationsLiveQuery.NoSourceYet }, from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                try
                {
                    var result = OperationsLiveQuery.OilChangesPeriod(erp, from, to, page, pageSize, search, Operations.LiveScope(c, ctx.User), export: false, token);
                    return Envelope(new { items = result.Items, total = result.Total, page, pageSize, summary = result.Summary, from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "invalid_period") { throw new ApiError("فترة غير صالحة."); }
            });
        }, "operations-oil-period");
    }

    static object Purchases(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        var (from, to) = ParsePeriod(ctx, defaultCurrentMonth: true);
        var (page, pageSize, _, _, search) = ParsePage(ctx);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c => Envelope(BuildStorePurchases(c, ctx.User, from, to, page, pageSize, search), Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User)));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                try
                {
                    var result = OperationsLiveQuery.PurchaseInvoices(erp, from, to, page, pageSize, search, Operations.LiveScope(c, ctx.User), null, token);
                    return Envelope(new { items = result.Items, total = result.Total, page, pageSize, summary = result.Summary }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "invalid_period") { throw new ApiError("فترة غير صالحة."); }
            });
        }, "operations-purchases");
    }

    static object PurchaseDetail(Store store, IConfiguration config, IHostEnvironment env, string id, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                if (!FinancialScopes.AllCompanies(ctx.User)) throw new ApiError("فاتورة المشتريات غير موجودة.", 404);
                var hit = Store.Get(c, id, "purchase_invoice") ?? throw new ApiError("فاتورة المشتريات غير موجودة.", 404);
                if (hit["isDeleted"]?.GetValue<bool>() == true) throw new ApiError("فاتورة المشتريات غير موجودة.", 404);
                return Envelope(MapStorePurchaseRow(hit), Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        DateOnly from = new(2020, 1, 1);
        DateOnly to = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                try
                {
                    var result = OperationsLiveQuery.PurchaseInvoices(erp, from, to, 1, 1, "", Operations.LiveScope(c, ctx.User), id, token);
                    return Envelope(result.Items[0], Operations.SourceLive, result.AsOf, Actions(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "purchase_not_found") { throw new ApiError("فاتورة المشتريات غير موجودة.", 404); }
            });
        }, "operations-purchase");
    }

    static object Fuel(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        DateOnly month = ParseMonth(ctx);
        string provider = (ctx.Request.Query["provider"].ToString() ?? "").Trim();
        var (page, pageSize, _, _, search) = ParsePage(ctx);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c => Envelope(BuildStoreFuel(c, ctx.User, month, page, pageSize), Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User)));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                var result = OperationsLiveQuery.FuelCards(erp, month, provider, page, pageSize, search, Operations.LiveScope(c, ctx.User), token);
                return Envelope(new { items = result.Items, total = result.Total, page, pageSize, summary = result.Summary, month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture) }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
            });
        }, "operations-fuel");
    }

    static object FuelLog(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        var (from, to) = ParsePeriod(ctx, defaultCurrentMonth: true);
        var (page, pageSize, _, _, _) = ParsePage(ctx);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return Envelope(new { items = Array.Empty<object>(), total = 0L, page, pageSize, summary = new { assignmentsTotal = 0 } }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                try
                {
                    var result = OperationsLiveQuery.FuelAssignments(erp, from, to, page, pageSize, Operations.LiveScope(c, ctx.User), token);
                    return Envelope(new { items = result.Items, total = result.Total, page, pageSize, summary = result.Summary }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "invalid_period") { throw new ApiError("فترة غير صالحة."); }
            });
        }, "operations-fuel-log");
    }

    static IResult ExportCities(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        List<JsonObject> items;
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            items = store.Read(c => BuildStoreCities(c, ctx.User).Items.ToList());
        else
            items = LiveRead(ctx, token =>
            {
                using var erp = OpenErp(config);
                return store.Read(c => OperationsLiveQuery.CitiesSummary(erp, Operations.LiveScope(c, ctx.User), token).Items.ToList());
            }, "operations-cities-export", TimeSpan.FromSeconds(ExportTimeoutSeconds(config)));
        var cols = new[] { "المدينة", "الحالة", "السجلات", "سيارات", "دراجات", "مناديب", "تطبيقات", "حسابات إجمالي", "نشط", "مسند" }.Select(h => new ExcelColumn(h)).ToArray();
        var rows = items.Select(r => new object?[] { r.S("nameAr"), r.S("status"), r.S("registrationNames"), r.N("vehiclesCars"), r.N("vehiclesMotorbikes"), r.N("ridersTotal"), r.S("appsWorking"), r.N("accountsTotal"), r.N("accountsActive"), r.N("accountsAssigned") }).ToList();
        return Results.File(ExcelFiles.Write(cols, rows), ExcelFiles.Mime, "operations-cities.xlsx");
    }

    static IResult ExportHousing(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        string city = (ctx.Request.Query["city"].ToString() ?? "").Trim();
        string status = (ctx.Request.Query["status"].ToString() ?? "").Trim();
        string search = (ctx.Request.Query["q"].ToString() is { Length: > 0 } q ? q : ctx.Request.Query["search"].ToString() ?? "").Trim();
        List<JsonObject> items;
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            items = store.Read(c => ExtractItems(BuildStoreHousing(c, ctx.User, 1, 50_000, city, status, search)).ToList());
        else
            items = LiveRead(ctx, token =>
            {
                using var erp = OpenErp(config);
                return store.Read(c => OperationsLiveQuery.HousingExport(erp, city, status, search, Operations.LiveScope(c, ctx.User), ExportTimeoutSeconds(config), token).Items.ToList());
            }, "operations-housing-export", TimeSpan.FromSeconds(ExportTimeoutSeconds(config)));
        var cols = new[] { "السكن", "المدينة", "غرف", "سعة", "مقيمون", "لكل غرفة", "الحالة" }.Select(h => new ExcelColumn(h)).ToArray();
        var rows = items.Select(r => new object?[] { r.S("nameAr"), r.S("city"), r.N("roomsCount"), r.N("capacity"), r.N("residentsTotal"), r.N("ridersPerRoom"), r.S("status") }).ToList();
        return Results.File(ExcelFiles.Write(cols, rows), ExcelFiles.Mime, "operations-housing.xlsx");
    }

    static IResult ExportPeriod(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        bool detailed = (ctx.Request.Query["detail"].ToString() ?? "").Equals("detailed", StringComparison.OrdinalIgnoreCase);
        var (from, to) = ParsePeriod(ctx, defaultCurrentMonth: true);
        string mode = (ctx.Request.Query["mode"].ToString() is { Length: > 0 } m ? m : "vehicle").Trim();
        List<JsonObject> items;
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            items = store.Read(c => ExtractItems(BuildStorePeriod(c, ctx.User, from, to, mode, 1, 50_000)).ToList());
        else
            items = LiveRead(ctx, token =>
            {
                using var erp = OpenErp(config);
                return store.Read(c =>
                {
                    var result = OperationsLiveQuery.VehiclesPeriodReport(erp, from, to, mode, 1, OperationsLiveQuery.MaxPageSize, "", Operations.LiveScope(c, ctx.User), detailed: true, token);
                    AssertNoVin(result.Items);
                    return result.Items.ToList();
                });
            }, "operations-vehicles-export", TimeSpan.FromSeconds(ExportTimeoutSeconds(config)));
        var headers = detailed
            ? new[] { "لوحة", "نوع", "مندوب", "إقامة", "مدينة", "أيام", "قطع غيار", "استقطاع", "تغييرات زيت", "تكلفة زيت", "عداد" }
            : new[] { "لوحة", "نوع", "مندوب", "أيام", "قطع غيار", "استقطاع", "تكلفة زيت" };
        var cols = headers.Select(h => new ExcelColumn(h)).ToArray();
        var rows = items.Select(r => detailed
            ? new object?[] { r.S("plateNumberAr"), r.S("vehicleType"), r.S("riderNameAr"), r.S("iqama"), r.S("city"), r.N("operatingDays"), r.N("sparePartsCost"), r["amountToCollect"]?.ToString(), r.N("oilChangesCount"), r.N("oilChangesCost"), r["oilMileage"]?.ToString() }
            : new object?[] { r.S("plateNumberAr"), r.S("vehicleType"), r.S("riderNameAr"), r.N("operatingDays"), r.N("sparePartsCost"), r["amountToCollect"]?.ToString(), r.N("oilChangesCost") }).ToList();
        return Results.File(ExcelFiles.Write(cols, rows), ExcelFiles.Mime, detailed ? "operations-vehicles-detailed.xlsx" : "operations-vehicles-summary.xlsx");
    }

    static IResult ExportFuel(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        DateOnly month = ParseMonth(ctx);
        string provider = (ctx.Request.Query["provider"].ToString() ?? "").Trim();
        string search = (ctx.Request.Query["q"].ToString() ?? "").Trim();
        List<JsonObject> items;
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            items = store.Read(c => ExtractItems(BuildStoreFuel(c, ctx.User, month, 1, 50_000)).ToList());
        else
            items = LiveRead(ctx, token =>
            {
                using var erp = OpenErp(config);
                return store.Read(c => OperationsLiveQuery.FuelCardsExport(erp, month, provider, search, Operations.LiveScope(c, ctx.User), ExportTimeoutSeconds(config), token).Items.ToList());
            }, "operations-fuel-export", TimeSpan.FromSeconds(ExportTimeoutSeconds(config)));
        var cols = new[] { "اسم البطاقة", "رقم البطاقة", "لوحة", "مندوب", "رقم الإقامة", "لتر", "قبل الضريبة", "ضريبة", "إجمالي", "سقف", "الحالة" }.Select(h => new ExcelColumn(h)).ToArray();
        var rows = items.Select(r => new object?[] { r.S("cardName", r.S("providerNameAr", r.S("provider"))), r.S("cardNumber"), r.S("plateNumber"), r.S("riderNameAr"), r.S("iqama"), r["liters"]?.ToString(), r["amountBeforeTax"]?.ToString(), r["vat"]?.ToString(), r["totalInclVat"]?.ToString(), OperationsLiveQuery.NoSourceYet, OperationsLiveQuery.FuelStatusLabel(r.S("status")) }).ToList();
        return Results.File(ExcelFiles.Write(cols, rows), ExcelFiles.Mime, "operations-fuel.xlsx");
    }

    static OperationsLiveQuery.PageResult BuildStoreCities(DbConnection c, ClaimsPrincipal user)
    {
        var cities = Operations.List(c, user, "city").Select(r => new JsonObject
        {
            ["id"] = r.S("id"),
            ["code"] = r.S("code"),
            ["nameAr"] = r.S("nameAr", r.S("name")),
            ["nameEn"] = r.S("nameEn"),
            ["status"] = null,
            ["activeRegistrations"] = null,
            ["registrationNames"] = null,
            ["vehiclesCars"] = null,
            ["vehiclesMotorbikes"] = null,
            ["vehiclesTotal"] = null,
            ["ridersTotal"] = null,
            ["appsWorking"] = null,
            ["accountsTotal"] = null,
            ["accountsActive"] = null,
            ["accountsAssigned"] = null
        }).ToList();
        var summary = new JsonObject
        {
            ["activeCities"] = cities.Count,
            ["totalVehicles"] = null,
            ["totalRiders"] = null,
            ["platformAccountsActive"] = null,
            ["platformAccountsAssigned"] = null
        };
        return new(cities, cities.Count, summary, DateTimeOffset.UtcNow);
    }

    static object BuildStoreHousing(DbConnection c, ClaimsPrincipal user, int page, int pageSize, string city, string status, string search)
    {
        var all = Operations.List(c, user, "housing").Select(r =>
        {
            var rooms = (r["rooms"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            long capacity = rooms.Sum(x => (long)x.N("Capacity", x.N("capacity")));
            return new JsonObject
            {
                ["id"] = r.S("id"),
                ["code"] = r.S("code"),
                ["nameAr"] = r.S("nameAr"),
                ["city"] = city,
                ["cityCode"] = "",
                ["roomsCount"] = rooms.Count,
                ["capacity"] = capacity,
                ["residentsTotal"] = null,
                ["ridersPerRoom"] = null,
                ["status"] = r.S("status", ""),
                ["supervisor"] = null,
                ["supervisorSource"] = OperationsLiveQuery.NoSourceYet
            };
        }).Where(r =>
            (city.Length == 0 || r.S("city").Contains(city, StringComparison.OrdinalIgnoreCase)) &&
            (status.Length == 0 || r.S("status") == status) &&
            (search.Length == 0 || r.S("nameAr").Contains(search, StringComparison.OrdinalIgnoreCase) || r.S("code").Contains(search, StringComparison.OrdinalIgnoreCase))
        ).ToList();
        int skip = (page - 1) * pageSize;
        var items = all.Skip(skip).Take(pageSize).ToList();
        var summary = new JsonObject
        {
            ["housingsTotal"] = all.Count,
            ["roomsTotal"] = all.Sum(r => (long)r.N("roomsCount")),
            ["residentsTotal"] = null,
            ["occupancyPercent"] = null
        };
        return new { items, total = all.Count, page, pageSize, summary };
    }

    static object BuildStorePeriod(DbConnection c, ClaimsPrincipal user, DateOnly from, DateOnly to, string mode, int page, int pageSize)
    {
        _ = from; _ = to; _ = mode;
        var vehicles = Operations.List(c, user, "vehicle").Select(r => new JsonObject
        {
            ["id"] = r.S("id"),
            ["assetNumber"] = r.S("assetNumber"),
            ["plateNumberAr"] = r.S("plateNumberAr"),
            ["vehicleType"] = r.S("vehicleType"),
            ["city"] = "",
            ["riderNameAr"] = "",
            ["iqama"] = "",
            ["operatingDays"] = null,
            ["sparePartsCost"] = null,
            ["amountToCollect"] = null,
            ["oilChangesCount"] = null,
            ["oilChangesCost"] = null,
            ["oilMileage"] = null
        }).ToList();
        AssertNoVin(vehicles);
        int skip = (page - 1) * pageSize;
        var items = vehicles.Skip(skip).Take(pageSize).ToList();
        var summary = new JsonObject
        {
            ["vehiclesOperating"] = vehicles.Count,
            ["totalOperatingDays"] = null,
            ["sparePartsCost"] = null,
            ["amountToCollect"] = null,
            ["oilChangesCost"] = null
        };
        return new { items, total = vehicles.Count, page, pageSize, summary, mode, from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
    }

    static object BuildStoreFuel(DbConnection c, ClaimsPrincipal user, DateOnly month, int page, int pageSize)
    {
        _ = month;
        var rows = Operations.List(c, user, "fuel_usage").Select(r =>
        {
            string provider = r.S("provider");
            string providerAr = OperationsLiveQuery.FuelProviderNameAr(provider);
            string notes = r.S("cardNotes", r.S("notes"));
            return new JsonObject
            {
                ["id"] = r.S("id"),
                ["cardNumber"] = r.S("fuelCardId"),
                ["provider"] = provider,
                ["providerNameAr"] = providerAr,
                ["cardName"] = !string.IsNullOrWhiteSpace(notes) ? notes : providerAr,
                ["cardNotes"] = notes,
                ["plateNumber"] = "",
                ["riderNameAr"] = "",
                ["iqama"] = r.S("riderId"),
                ["liters"] = r.N("liters"),
                ["amountBeforeTax"] = r.N("amountBeforeTax"),
                ["vat"] = r.N("vat"),
                ["totalInclVat"] = r.N("amount", r.N("totalAmount")),
                ["transactionCount"] = null,
                ["fuelType"] = r.S("fuelType"),
                ["cap"] = null,
                ["capSource"] = OperationsLiveQuery.NoSourceYet,
                ["status"] = OperationsLiveQuery.FuelStatusLabel(r.S("status", "active"))
            };
        }).ToList();
        int skip = (page - 1) * pageSize;
        var items = rows.Skip(skip).Take(pageSize).ToList();
        var summary = new JsonObject
        {
            ["activeCards"] = rows.Count,
            ["consumingRiders"] = rows.Select(r => r.S("iqama")).Where(x => x != "").Distinct().Count(),
            ["totalLiters"] = rows.Sum(r => r.N("liters")),
            ["totalInclVat"] = rows.Sum(r => r.N("totalInclVat")),
            ["capExceeded"] = null,
            ["capExceededSource"] = OperationsLiveQuery.NoSourceYet
        };
        return new { items, total = rows.Count, page, pageSize, summary, month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture) };
    }

    static object BuildStorePurchases(DbConnection c, ClaimsPrincipal user, DateOnly from, DateOnly to, int page, int pageSize, string search)
    {
        if (!FinancialScopes.AllCompanies(user))
            return new { items = Array.Empty<object>(), total = 0L, page, pageSize, summary = new JsonObject { ["invoicesTotal"] = 0, ["totalAmount"] = 0, ["message"] = "فواتير المشتريات بلا عمود شركة — مخفية لنطاق شركة" } };
        var stored = Store.List(c, "purchase_invoice").ToList();
        if (stored.Count == 0)
            return new { items = Array.Empty<object>(), total = 0L, page, pageSize, summary = new JsonObject { ["invoicesTotal"] = 0, ["totalAmount"] = 0, ["message"] = OperationsLiveQuery.NoSourceYet } };
        var matched = stored
            .Where(r => r["isDeleted"]?.GetValue<bool>() != true)
            .Where(r =>
            {
                DateOnly? invDate = DateOnly.TryParse(r.S("invoiceDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
                DateTimeOffset? recAt = DateTimeOffset.TryParse(r.S("receivedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
                return OperationsLiveQuery.PurchaseInvoiceInPeriod(invDate, recAt, from, to);
            })
            .Select(MapStorePurchaseRow)
            .Where(r => search.Length == 0
                || r.S("receiptNumber").Contains(search, StringComparison.OrdinalIgnoreCase)
                || r.S("supplierInvoiceNumber").Contains(search, StringComparison.OrdinalIgnoreCase)
                || r.S("supplierNameAr").Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.S("receivedAt"))
            .ThenByDescending(r => r.S("invoiceDate"))
            .ThenByDescending(r => r.S("receiptNumber"))
            .ToList();
        int skip = (page - 1) * pageSize;
        var items = matched.Skip(skip).Take(pageSize).ToList();
        var summary = new JsonObject
        {
            ["invoicesTotal"] = matched.Count,
            ["totalAmount"] = matched.Sum(r => r.N("totalAmount"))
        };
        return new { items, total = (long)matched.Count, page, pageSize, summary };
    }

    static JsonObject MapStorePurchaseRow(JsonObject r) => new()
    {
        ["id"] = r.S("id"),
        ["receiptNumber"] = r.S("receiptNumber"),
        ["supplierInvoiceNumber"] = r.S("supplierInvoiceNumber"),
        ["invoiceDate"] = r.S("invoiceDate"),
        ["receivedAt"] = r.S("receivedAt"),
        ["subtotal"] = r.N("subtotal"),
        ["taxAmount"] = r.N("taxAmount"),
        ["totalAmount"] = r.N("totalAmount"),
        ["status"] = OperationsLiveQuery.PurchaseStatusLabel(r.S("status")),
        ["supplierNameAr"] = r.S("supplierNameAr")
    };

    static void AssertNoVin(IEnumerable<JsonObject> items)
    {
        foreach (var item in items)
        {
            if (item.ContainsKey("vin") || item.ContainsKey("Vin"))
                throw new InvalidOperationException("VIN must not appear on period/list rows.");
        }
    }

    static List<JsonObject> ExtractItems(object body)
    {
        var node = JsonSerializer.SerializeToNode(body, JsonSerializerOptions.Web)!.AsObject();
        return (node["items"] as JsonArray ?? []).OfType<JsonObject>().ToList();
    }

    static (int Page, int PageSize, string City, string Status, string Search) ParsePage(HttpContext ctx)
    {
        int page = 1, pageSize = 50;
        if (int.TryParse(ctx.Request.Query["page"], NumberStyles.None, CultureInfo.InvariantCulture, out int p) && p > 0) page = p;
        if (int.TryParse(ctx.Request.Query["pageSize"], NumberStyles.None, CultureInfo.InvariantCulture, out int ps) && ps > 0)
            pageSize = Math.Min(ps, OperationsLiveQuery.MaxPageSize);
        string city = (ctx.Request.Query["city"].ToString() ?? "").Trim();
        string status = (ctx.Request.Query["status"].ToString() ?? "").Trim();
        string search = (ctx.Request.Query["q"].ToString() is { Length: > 0 } q ? q : ctx.Request.Query["search"].ToString() ?? "").Trim();
        return (page, pageSize, city, status, search);
    }

    static (DateOnly From, DateOnly To) ParsePeriod(HttpContext ctx, bool defaultCurrentMonth)
    {
        string fromRaw = (ctx.Request.Query["from"].ToString() ?? "").Trim();
        string toRaw = (ctx.Request.Query["to"].ToString() ?? "").Trim();
        if (fromRaw.Length == 0 && toRaw.Length == 0 && defaultCurrentMonth)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            return (new DateOnly(today.Year, today.Month, 1), today);
        }
        if (!DateOnly.TryParse(fromRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
            !DateOnly.TryParse(toRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
            throw new ApiError("تاريخ البداية والنهاية مطلوبان بصيغة yyyy-MM-dd.");
        if (to < from) throw new ApiError("فترة غير صالحة.");
        return (from, to);
    }

    static DateOnly ParseMonth(HttpContext ctx)
    {
        string raw = (ctx.Request.Query["month"].ToString() ?? "").Trim();
        if (raw.Length == 0)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            return new DateOnly(today.Year, today.Month, 1);
        }
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return new DateOnly(d.Year, d.Month, 1);
        if (DateOnly.TryParseExact(raw + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m))
            return m;
        throw new ApiError("الشهر يجب أن يكون yyyy-MM أو yyyy-MM-dd.");
    }

    static JsonArray Actions(ClaimsPrincipal user)
    {
        var actions = new JsonArray();
        if (AccessPolicy.Has(user, "operations.view"))
        {
            actions.Add("export");
            actions.Add("refresh");
        }
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

    static int ExportTimeoutSeconds(IConfiguration config)
    {
        if (int.TryParse(config["Operations:ExportTimeoutSeconds"], NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 30)
            return n;
        return DefaultExportTimeoutSeconds;
    }
}
