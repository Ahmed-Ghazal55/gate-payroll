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
        api.MapGet("/operations/vehicles/oil-changes/export", (HttpContext ctx) => ExportOilChanges(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Export vehicle oil changes");
        api.MapGet("/operations/vehicles/purchase-invoices/export", (HttpContext ctx) => ExportPurchases(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Export vehicle purchase invoices");
        api.MapGet("/operations/fuel-cards/export", (HttpContext ctx) => ExportFuel(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Export fuel cards");
        api.MapGet("/operations/fuel-log/export", (HttpContext ctx) => ExportFuelLog(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Export fuel card assignment log");
        api.MapGet("/operations/distinct", (HttpContext ctx) => DistinctValues(store, config, env, ctx))
            .WithTags("Operations").WithSummary("Distinct column values for operations table filters");
    }

    static readonly HashSet<string> CitiesColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "code", "nameAr", "nameEn", "status", "regs", "registrationNames", "activeRegistrations",
        "veh", "vehiclesCars", "vehiclesMotorbikes", "vehiclesTotal", "riders", "ridersTotal",
        "apps", "appsWorking", "acc", "accountsTotal", "accountsActive", "accountsAssigned"
    };

    static readonly HashSet<string> HousingColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "code", "nameAr", "city", "cityCode", "rooms", "roomsCount", "capacity",
        "res", "residentsTotal", "per", "ridersPerRoom", "status"
    };

    static readonly HashSet<string> VehiclesPeriodColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "vehicleId", "assetNumber", "plate", "plateNumberAr", "vehicleType", "city",
        "rider", "riderNameAr", "iqama", "days", "operatingDays", "parts", "sparePartsCost",
        "collect", "amountToCollect", "oil", "oilChangesCount", "oilChangesCost", "oilMileage"
    };

    static readonly HashSet<string> PurchaseInvoiceColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "num", "receiptNumber", "supInv", "supplierInvoiceNumber", "sup", "supplierNameAr",
        "date", "invoiceDate", "receivedAt", "subtotal", "taxAmount", "total", "totalAmount", "status"
    };

    static readonly HashSet<string> OilChangeColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "plate", "plateNumberAr", "vehicleType", "date", "performedAt", "odo", "odometer",
        "liters", "oilLiters", "oil", "oilCost", "filter", "filterCost", "labor", "laborCost",
        "other", "otherCost", "total", "totalCost"
    };

    static readonly HashSet<string> FuelColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "cardName", "provider", "providerNameAr", "cardNotes", "card", "cardNumber",
        "rider", "riderNameAr", "iqama", "plate", "plateNumber", "liters", "before",
        "amountBeforeTax", "vat", "total", "totalInclVat", "transactionCount", "fuelType",
        "status", "from", "effectiveFrom", "to", "effectiveTo", "assignmentReason", "endReason"
    };

    static readonly TableFilterContract.FilterOptions OpsFilterOptions = new(
        CompositeColumns: new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["riderNameAr"] = ["riderNameAr", "iqama"],
            ["rider"] = ["riderNameAr", "iqama"]
        });

    static IReadOnlySet<string> WhitelistFor(string tab, string view) => (tab, view) switch
    {
        ("cities", _) => CitiesColumns,
        ("housing", _) => HousingColumns,
        ("vehicles", "invoices") => PurchaseInvoiceColumns,
        ("vehicles", "oil") => OilChangeColumns,
        ("vehicles", _) => VehiclesPeriodColumns,
        ("fuel", _) => FuelColumns,
        _ => CitiesColumns
    };

    static IReadOnlyDictionary<string, string> AliasesFor(string tab, string view) => (tab, view) switch
    {
        ("cities", _) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["regs"] = "registrationNames",
            ["veh"] = "vehiclesTotal",
            ["riders"] = "ridersTotal",
            ["apps"] = "appsWorking",
            ["acc"] = "accountsTotal"
        },
        ("housing", _) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["rooms"] = "roomsCount",
            ["res"] = "residentsTotal",
            ["per"] = "ridersPerRoom"
        },
        ("vehicles", "invoices") => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["num"] = "receiptNumber",
            ["supInv"] = "supplierInvoiceNumber",
            ["sup"] = "supplierNameAr",
            ["date"] = "invoiceDate",
            ["total"] = "totalAmount"
        },
        ("vehicles", "oil") => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["plate"] = "plateNumberAr",
            ["date"] = "performedAt",
            ["odo"] = "odometer",
            ["liters"] = "oilLiters",
            ["oil"] = "oilCost",
            ["filter"] = "filterCost",
            ["labor"] = "laborCost",
            ["other"] = "otherCost",
            ["total"] = "totalCost"
        },
        ("vehicles", _) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["plate"] = "plateNumberAr",
            ["rider"] = "riderNameAr",
            ["days"] = "operatingDays",
            ["parts"] = "sparePartsCost",
            ["collect"] = "amountToCollect",
            ["oil"] = "oilChangesCost"
        },
        ("fuel", _) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["card"] = "cardNumber",
            ["rider"] = "riderNameAr",
            ["plate"] = "plateNumber",
            ["before"] = "amountBeforeTax",
            ["total"] = "totalInclVat",
            ["from"] = "effectiveFrom",
            ["to"] = "effectiveTo"
        },
        _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };

    static object DistinctValues(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        string column = (ctx.Request.Query["column"].ToString() ?? "").Trim();
        string tab = (ctx.Request.Query["tab"].ToString() ?? "cities").Trim().ToLowerInvariant();
        string view = (ctx.Request.Query["view"].ToString() ?? "").Trim().ToLowerInvariant();
        var whitelist = WhitelistFor(tab, view);
        var aliases = AliasesFor(tab, view);
        _ = TableFilterContract.ParseFilters(ctx.Request.Query, whitelist, aliases);

        if (column.Length == 0 || !whitelist.Contains(column))
            return new JsonObject { ["column"] = column, ["values"] = new JsonArray(), ["counts"] = new JsonObject(), ["hasEmpty"] = false, ["emptyCount"] = 0 };

        var (from, to) = ParsePeriod(ctx, defaultCurrentMonth: true);
        DateOnly month = ParseMonth(ctx);
        string mode = (ctx.Request.Query["mode"].ToString() is { Length: > 0 } m && m == "rider") ? "rider" : "vehicle";
        var (_, _, city, status, search) = ParsePage(ctx);
        string provider = (ctx.Request.Query["provider"].ToString() ?? "").Trim();
        List<JsonObject> rows = [];
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
        {
            rows = store.Read(c => (tab, view) switch
            {
                ("cities", _) => BuildStoreCities(c, ctx.User).Items.ToList(),
                ("housing", _) => ExtractItems(BuildStoreHousing(c, ctx.User, 1, 50_000, city, status, search)),
                ("vehicles", "invoices") => ExtractItems(BuildStorePurchases(c, ctx.User, from, to, 1, 50_000, search)),
                ("vehicles", "oil") => new List<JsonObject>(),
                ("vehicles", _) => ExtractItems(BuildStorePeriod(c, ctx.User, from, to, mode, 1, 50_000)),
                ("fuel", "log") => new List<JsonObject>(),
                ("fuel", _) => ExtractItems(BuildStoreFuel(c, ctx.User, month, 1, 50_000)),
                _ => new List<JsonObject>()
            });
            if (search.Length > 0 && tab is "cities" or "vehicles" or "fuel" && view != "invoices")
            {
                rows = rows.Where(r => TextSearch.Matches(search, string.Join(' ', r.Select(kv => kv.Value?.ToString() ?? "")))).ToList();
            }
            if (provider.Length > 0 && tab == "fuel" && view != "log")
            {
                rows = rows.Where(r => string.Equals(r.S("provider"), provider, StringComparison.OrdinalIgnoreCase) || string.Equals(r.S("providerNameAr"), provider, StringComparison.OrdinalIgnoreCase)).ToList();
            }
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
                        ("housing", _) => OperationsLiveQuery.HousingExport(erp, city, status, search, scope, ExportTimeoutSeconds(config), token).Items.ToList(),
                        ("vehicles", "invoices") => OperationsLiveQuery.PurchaseInvoices(erp, from, to, 1, OperationsLiveQuery.MaxPageSize, search, scope, null, token, unpaged: true).Items.ToList(),
                        ("vehicles", "oil") => OperationsLiveQuery.OilChangesPeriod(erp, from, to, 1, OperationsLiveQuery.MaxPageSize, search, scope, export: true, token).Items.ToList(),
                        ("vehicles", _) => OperationsLiveQuery.VehiclesPeriodReport(erp, from, to, mode, 1, OperationsLiveQuery.MaxPageSize, search, scope, detailed: true, token).Items.ToList(),
                        ("fuel", "log") => OperationsLiveQuery.FuelAssignments(erp, from, to, 1, OperationsLiveQuery.MaxPageSize, scope, token, unpaged: true, search: search).Items.ToList(),
                        ("fuel", _) => OperationsLiveQuery.FuelCardsExport(erp, month, provider, search, scope, ExportTimeoutSeconds(config), token).Items.ToList(),
                        _ => new List<JsonObject>()
                    };
                });
            }, "operations-distinct", TimeSpan.FromSeconds(ExportTimeoutSeconds(config)));
        }
        return TableFilterContract.BuildDistinct(rows, column, ctx.Request.Query, whitelist, aliases, options: OpsFilterOptions);
    }

    static (IReadOnlyList<JsonObject> Items, long Total, JsonNode? Summary) ApplyContractAndPage(
        IReadOnlyList<JsonObject> sourceItems,
        long sourceTotal,
        JsonNode? baseSummary,
        int page,
        int pageSize,
        IQueryCollection query,
        string tab,
        string view,
        bool alreadyPaged)
    {
        var whitelist = WhitelistFor(tab, view);
        var aliases = AliasesFor(tab, view);
        var filters = TableFilterContract.ParseFilters(query, whitelist, aliases);
        var sorts = TableFilterContract.ParseSort(query, whitelist, aliases);
        if (filters.Count == 0 && sorts.Count == 0)
            return (sourceItems, sourceTotal, baseSummary);

        var rows = sourceItems.ToList();
        if (filters.Count > 0) rows = TableFilterContract.ApplyFilters(rows, filters, options: OpsFilterOptions);
        if (sorts.Count > 0) rows = TableFilterContract.ApplySort(rows, sorts, options: OpsFilterOptions);
        long total = rows.Count;
        var summary = filters.Count > 0 ? BuildFilteredSummary(tab, view, rows, baseSummary) : baseSummary;
        if (!alreadyPaged)
        {
            int skip = Math.Max(0, (page - 1) * pageSize);
            rows = rows.Skip(skip).Take(pageSize).ToList();
        }
        return (rows, total, summary);
    }

    static JsonNode? BuildFilteredSummary(string tab, string view, IReadOnlyList<JsonObject> rows, JsonNode? baseSummary)
    {
        long SumLong(string key) => rows.Sum(r => (long)r.N(key));
        decimal SumDec(string key) => rows.Sum(r => r.N(key));
        bool AnyNonNull(string key) => rows.Any(r => r[key] != null);

        return (tab, view) switch
        {
            ("cities", _) => new JsonObject
            {
                ["activeCities"] = rows.Count,
                ["totalVehicles"] = AnyNonNull("vehiclesTotal") ? SumLong("vehiclesTotal") : (JsonNode?)null,
                ["totalRiders"] = AnyNonNull("ridersTotal") ? SumLong("ridersTotal") : (JsonNode?)null,
                ["platformAccountsActive"] = AnyNonNull("accountsActive") ? SumLong("accountsActive") : (JsonNode?)null,
                ["platformAccountsAssigned"] = AnyNonNull("accountsAssigned") ? SumLong("accountsAssigned") : (JsonNode?)null
            },
            ("housing", _) => BuildFilteredHousingSummary(rows),
            ("vehicles", "invoices") => new JsonObject
            {
                ["invoicesTotal"] = (long)rows.Count,
                ["totalAmount"] = SumDec("totalAmount")
            },
            ("vehicles", "oil") => new JsonObject
            {
                ["oilChangesCount"] = (long)rows.Count,
                ["oilChangesCost"] = SumDec("totalCost"),
                ["totalLiters"] = SumDec("oilLiters")
            },
            ("vehicles", _) => new JsonObject
            {
                ["vehiclesOperating"] = (long)rows.Count,
                ["totalOperatingDays"] = AnyNonNull("operatingDays") ? SumDec("operatingDays") : (JsonNode?)null,
                ["sparePartsCost"] = AnyNonNull("sparePartsCost") ? SumDec("sparePartsCost") : (JsonNode?)null,
                ["amountToCollect"] = AnyNonNull("amountToCollect") ? SumDec("amountToCollect") : (JsonNode?)null,
                ["oilChangesCost"] = AnyNonNull("oilChangesCost") ? SumDec("oilChangesCost") : (JsonNode?)null
            },
            ("fuel", "log") => new JsonObject
            {
                ["assignmentsTotal"] = (long)rows.Count
            },
            ("fuel", _) => new JsonObject
            {
                ["activeCards"] = (long)rows.Count(r => OperationsLiveQuery.FuelStatusLabel(r.S("status")) == "نشطة"),
                ["consumingRiders"] = (long)rows.Where(r => r.N("liters") > 0 && r.S("iqama") != "").Select(r => r.S("iqama")).Distinct().Count(),
                ["totalLiters"] = SumDec("liters"),
                ["totalInclVat"] = SumDec("totalInclVat"),
                ["capExceeded"] = null,
                ["capExceededSource"] = OperationsLiveQuery.NoSourceYet
            },
            _ => baseSummary
        };
    }

    static JsonObject BuildFilteredHousingSummary(IReadOnlyList<JsonObject> rows)
    {
        long rooms = rows.Sum(r => (long)r.N("roomsCount"));
        bool hasResidents = rows.Any(r => r["residentsTotal"] != null);
        long residents = hasResidents ? rows.Sum(r => (long)r.N("residentsTotal")) : 0;
        long capacity = rows.Sum(r => (long)r.N("capacity"));
        return new JsonObject
        {
            ["housingsTotal"] = (long)rows.Count,
            ["roomsTotal"] = rooms,
            ["residentsTotal"] = hasResidents ? residents : (JsonNode?)null,
            ["occupancyPercent"] = hasResidents ? (capacity <= 0 ? 0.0 : Math.Round(100.0 * residents / capacity, 1)) : (JsonNode?)null
        };
    }

    static object Cities(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        _ = TableFilterContract.ParseFilters(ctx.Request.Query, CitiesColumns, AliasesFor("cities", ""));
        _ = TableFilterContract.ParseSort(ctx.Request.Query, CitiesColumns, AliasesFor("cities", ""));
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                var page = BuildStoreCities(c, ctx.User);
                var (items, total, summary) = ApplyContractAndPage(page.Items, page.Total, page.Summary, 1, 200, ctx.Request.Query, "cities", "", alreadyPaged: true);
                return Envelope(new { items, total, summary }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                var page = OperationsLiveQuery.CitiesSummary(erp, Operations.LiveScope(c, ctx.User), token);
                var (items, total, summary) = ApplyContractAndPage(page.Items, page.Total, page.Summary, 1, 200, ctx.Request.Query, "cities", "", alreadyPaged: true);
                return Envelope(new { items, total, summary }, Operations.SourceLive, page.AsOf, Actions(ctx.User));
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
        _ = TableFilterContract.ParseFilters(ctx.Request.Query, HousingColumns, AliasesFor("housing", ""));
        _ = TableFilterContract.ParseSort(ctx.Request.Query, HousingColumns, AliasesFor("housing", ""));
        var (page, pageSize, city, status, search) = ParsePage(ctx);
        bool hasContract = TableFilterContract.HasContractParams(ctx.Request.Query);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                if (!hasContract)
                    return Envelope(BuildStoreHousing(c, ctx.User, page, pageSize, city, status, search), Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
                var full = BuildStoreHousing(c, ctx.User, 1, 50_000, city, status, search);
                var node = JsonSerializer.SerializeToNode(full, JsonSerializerOptions.Web)!.AsObject();
                var all = ExtractItems(full);
                var (items, total, summary) = ApplyContractAndPage(all, all.Count, node["summary"], page, pageSize, ctx.Request.Query, "housing", "", alreadyPaged: false);
                return Envelope(new { items, total, page, pageSize, summary }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                var scope = Operations.LiveScope(c, ctx.User);
                var result = hasContract
                    ? OperationsLiveQuery.HousingExport(erp, city, status, search, scope, ExportTimeoutSeconds(config), token)
                    : OperationsLiveQuery.HousingPage(erp, page, pageSize, city, status, search, scope, token);
                var (items, total, summary) = ApplyContractAndPage(result.Items, result.Total, result.Summary, page, pageSize, ctx.Request.Query, "housing", "", alreadyPaged: !hasContract);
                return Envelope(new { items, total, page, pageSize, summary }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
            });
        }, "operations-housing", hasContract ? TimeSpan.FromSeconds(ExportTimeoutSeconds(config)) : null);
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
        _ = TableFilterContract.ParseFilters(ctx.Request.Query, VehiclesPeriodColumns, AliasesFor("vehicles", ""));
        _ = TableFilterContract.ParseSort(ctx.Request.Query, VehiclesPeriodColumns, AliasesFor("vehicles", ""));
        var (from, to) = ParsePeriod(ctx, defaultCurrentMonth: true);
        string mode = (ctx.Request.Query["mode"].ToString() is { Length: > 0 } m ? m : "vehicle").Trim();
        if (mode is not ("vehicle" or "rider")) throw new ApiError("وضع العرض يجب أن يكون vehicle أو rider.");
        var (page, pageSize, _, _, search) = ParsePage(ctx);
        bool hasContract = TableFilterContract.HasContractParams(ctx.Request.Query);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                if (!hasContract)
                    return Envelope(BuildStorePeriod(c, ctx.User, from, to, mode, page, pageSize), Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
                var full = BuildStorePeriod(c, ctx.User, from, to, mode, 1, 50_000);
                var node = JsonSerializer.SerializeToNode(full, JsonSerializerOptions.Web)!.AsObject();
                var all = ExtractItems(full);
                var (items, total, summary) = ApplyContractAndPage(all, all.Count, node["summary"], page, pageSize, ctx.Request.Query, "vehicles", "", alreadyPaged: false);
                return Envelope(new { items, total, page, pageSize, summary, mode, from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                try
                {
                    var result = hasContract
                        ? OperationsLiveQuery.VehiclesPeriodReport(erp, from, to, mode, 1, OperationsLiveQuery.MaxPageSize, search, Operations.LiveScope(c, ctx.User), detailed: true, token)
                        : OperationsLiveQuery.VehiclesPeriodReport(erp, from, to, mode, page, pageSize, search, Operations.LiveScope(c, ctx.User), detailed: false, token);
                    AssertNoVin(result.Items);
                    var (items, total, summary) = ApplyContractAndPage(result.Items, result.Total, result.Summary, page, pageSize, ctx.Request.Query, "vehicles", "", alreadyPaged: !hasContract);
                    return Envelope(new
                    {
                        items,
                        total,
                        page,
                        pageSize,
                        summary,
                        mode,
                        from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "invalid_period") { throw new ApiError("فترة غير صالحة."); }
            });
        }, "operations-vehicles-period", hasContract ? TimeSpan.FromSeconds(ExportTimeoutSeconds(config)) : null);
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
        _ = TableFilterContract.ParseFilters(ctx.Request.Query, OilChangeColumns, AliasesFor("vehicles", "oil"));
        _ = TableFilterContract.ParseSort(ctx.Request.Query, OilChangeColumns, AliasesFor("vehicles", "oil"));
        var (from, to) = ParsePeriod(ctx, defaultCurrentMonth: true);
        var (page, pageSize, _, _, search) = ParsePage(ctx);
        bool hasContract = TableFilterContract.HasContractParams(ctx.Request.Query);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return Envelope(new { items = Array.Empty<object>(), total = 0L, page, pageSize, summary = new { oilChangesCount = (long?)null, oilChangesCost = (decimal?)null, message = OperationsLiveQuery.NoSourceYet }, from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                try
                {
                    var result = hasContract
                        ? OperationsLiveQuery.OilChangesPeriod(erp, from, to, 1, OperationsLiveQuery.MaxPageSize, search, Operations.LiveScope(c, ctx.User), export: true, token)
                        : OperationsLiveQuery.OilChangesPeriod(erp, from, to, page, pageSize, search, Operations.LiveScope(c, ctx.User), export: false, token);
                    var (items, total, summary) = ApplyContractAndPage(result.Items, result.Total, result.Summary, page, pageSize, ctx.Request.Query, "vehicles", "oil", alreadyPaged: !hasContract);
                    return Envelope(new { items, total, page, pageSize, summary, from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "invalid_period") { throw new ApiError("فترة غير صالحة."); }
            });
        }, "operations-oil-period", hasContract ? TimeSpan.FromSeconds(ExportTimeoutSeconds(config)) : null);
    }

    static object Purchases(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        _ = TableFilterContract.ParseFilters(ctx.Request.Query, PurchaseInvoiceColumns, AliasesFor("vehicles", "invoices"));
        _ = TableFilterContract.ParseSort(ctx.Request.Query, PurchaseInvoiceColumns, AliasesFor("vehicles", "invoices"));
        var (from, to) = ParsePeriod(ctx, defaultCurrentMonth: true);
        var (page, pageSize, _, _, search) = ParsePage(ctx);
        bool hasContract = TableFilterContract.HasContractParams(ctx.Request.Query);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                if (!hasContract)
                    return Envelope(BuildStorePurchases(c, ctx.User, from, to, page, pageSize, search), Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
                var full = BuildStorePurchases(c, ctx.User, from, to, 1, 50_000, search);
                var node = JsonSerializer.SerializeToNode(full, JsonSerializerOptions.Web)!.AsObject();
                var all = ExtractItems(full);
                var (items, total, summary) = ApplyContractAndPage(all, all.Count, node["summary"], page, pageSize, ctx.Request.Query, "vehicles", "invoices", alreadyPaged: false);
                return Envelope(new { items, total, page, pageSize, summary }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                try
                {
                    var result = hasContract
                        ? OperationsLiveQuery.PurchaseInvoices(erp, from, to, 1, OperationsLiveQuery.MaxPageSize, search, Operations.LiveScope(c, ctx.User), null, token, unpaged: true)
                        : OperationsLiveQuery.PurchaseInvoices(erp, from, to, page, pageSize, search, Operations.LiveScope(c, ctx.User), null, token);
                    var (items, total, summary) = ApplyContractAndPage(result.Items, result.Total, result.Summary, page, pageSize, ctx.Request.Query, "vehicles", "invoices", alreadyPaged: !hasContract);
                    return Envelope(new { items, total, page, pageSize, summary }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "invalid_period") { throw new ApiError("فترة غير صالحة."); }
            });
        }, "operations-purchases", hasContract ? TimeSpan.FromSeconds(ExportTimeoutSeconds(config)) : null);
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
        _ = TableFilterContract.ParseFilters(ctx.Request.Query, FuelColumns, AliasesFor("fuel", ""));
        _ = TableFilterContract.ParseSort(ctx.Request.Query, FuelColumns, AliasesFor("fuel", ""));
        DateOnly month = ParseMonth(ctx);
        string provider = (ctx.Request.Query["provider"].ToString() ?? "").Trim();
        var (page, pageSize, _, _, search) = ParsePage(ctx);
        bool hasContract = TableFilterContract.HasContractParams(ctx.Request.Query);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c =>
            {
                if (!hasContract)
                    return Envelope(BuildStoreFuel(c, ctx.User, month, page, pageSize), Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
                var full = BuildStoreFuel(c, ctx.User, month, 1, 50_000);
                var node = JsonSerializer.SerializeToNode(full, JsonSerializerOptions.Web)!.AsObject();
                var all = ExtractItems(full);
                var (items, total, summary) = ApplyContractAndPage(all, all.Count, node["summary"], page, pageSize, ctx.Request.Query, "fuel", "", alreadyPaged: false);
                return Envelope(new { items, total, page, pageSize, summary, month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture) }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
            });
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                var scope = Operations.LiveScope(c, ctx.User);
                var result = hasContract
                    ? OperationsLiveQuery.FuelCardsExport(erp, month, provider, search, scope, ExportTimeoutSeconds(config), token)
                    : OperationsLiveQuery.FuelCards(erp, month, provider, page, pageSize, search, scope, token);
                var (items, total, summary) = ApplyContractAndPage(result.Items, result.Total, result.Summary, page, pageSize, ctx.Request.Query, "fuel", "", alreadyPaged: !hasContract);
                return Envelope(new { items, total, page, pageSize, summary, month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture) }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
            });
        }, "operations-fuel", hasContract ? TimeSpan.FromSeconds(ExportTimeoutSeconds(config)) : null);
    }

    static object FuelLog(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        _ = TableFilterContract.ParseFilters(ctx.Request.Query, FuelColumns, AliasesFor("fuel", "log"));
        _ = TableFilterContract.ParseSort(ctx.Request.Query, FuelColumns, AliasesFor("fuel", "log"));
        var (from, to) = ParsePeriod(ctx, defaultCurrentMonth: true);
        var (page, pageSize, _, _, search) = ParsePage(ctx);
        bool hasContract = TableFilterContract.HasContractParams(ctx.Request.Query);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return Envelope(new { items = Array.Empty<object>(), total = 0L, page, pageSize, summary = new { assignmentsTotal = 0 } }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                try
                {
                    var result = hasContract
                        ? OperationsLiveQuery.FuelAssignments(erp, from, to, 1, OperationsLiveQuery.MaxPageSize, Operations.LiveScope(c, ctx.User), token, unpaged: true, search: search)
                        : OperationsLiveQuery.FuelAssignments(erp, from, to, page, pageSize, Operations.LiveScope(c, ctx.User), token, unpaged: false, search: search);
                    var (items, total, summary) = ApplyContractAndPage(result.Items, result.Total, result.Summary, page, pageSize, ctx.Request.Query, "fuel", "log", alreadyPaged: !hasContract);
                    return Envelope(new { items, total, page, pageSize, summary }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "invalid_period") { throw new ApiError("فترة غير صالحة."); }
            });
        }, "operations-fuel-log", hasContract ? TimeSpan.FromSeconds(ExportTimeoutSeconds(config)) : null);
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
        items = TableFilterContract.Apply(items, ctx.Request.Query, CitiesColumns, AliasesFor("cities", ""));
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
        items = TableFilterContract.Apply(items, ctx.Request.Query, HousingColumns, AliasesFor("housing", ""));
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
        string search = (ctx.Request.Query["q"].ToString() is { Length: > 0 } q ? q : ctx.Request.Query["search"].ToString() ?? "").Trim();
        List<JsonObject> items;
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
        {
            items = store.Read(c => ExtractItems(BuildStorePeriod(c, ctx.User, from, to, mode, 1, 50_000)).ToList());
            if (search.Length > 0)
                items = items.Where(r => TextSearch.Matches(search, $"{r.S("plateNumberAr")} {r.S("assetNumber")} {r.S("riderNameAr")} {r.S("iqama")} {r.S("city")}")).ToList();
        }
        else
            items = LiveRead(ctx, token =>
            {
                using var erp = OpenErp(config);
                return store.Read(c =>
                {
                    var result = OperationsLiveQuery.VehiclesPeriodReport(erp, from, to, mode, 1, OperationsLiveQuery.MaxPageSize, search, Operations.LiveScope(c, ctx.User), detailed: true, token);
                    AssertNoVin(result.Items);
                    return result.Items.ToList();
                });
            }, "operations-vehicles-export", TimeSpan.FromSeconds(ExportTimeoutSeconds(config)));
        items = TableFilterContract.Apply(items, ctx.Request.Query, VehiclesPeriodColumns, AliasesFor("vehicles", ""), options: OpsFilterOptions);
        var headers = detailed
            ? new[] { "لوحة", "نوع", "مندوب", "إقامة", "مدينة", "أيام", "قطع غيار", "استقطاع", "تغييرات زيت", "تكلفة زيت", "عداد" }
            : new[] { "لوحة", "نوع", "مندوب", "أيام", "قطع غيار", "استقطاع", "تكلفة زيت" };
        var cols = headers.Select(h => new ExcelColumn(h)).ToArray();
        var rows = items.Select(r => detailed
            ? new object?[] { r.S("plateNumberAr"), r.S("vehicleType"), r.S("riderNameAr"), r.S("iqama"), r.S("city"), r.N("operatingDays"), r.N("sparePartsCost"), r["amountToCollect"]?.ToString(), r.N("oilChangesCount"), r.N("oilChangesCost"), r["oilMileage"]?.ToString() }
            : new object?[] { r.S("plateNumberAr"), r.S("vehicleType"), r.S("riderNameAr"), r.N("operatingDays"), r.N("sparePartsCost"), r["amountToCollect"]?.ToString(), r.N("oilChangesCost") }).ToList();
        return Results.File(ExcelFiles.Write(cols, rows), ExcelFiles.Mime, detailed ? "operations-vehicles-detailed.xlsx" : "operations-vehicles-summary.xlsx");
    }

    static IResult ExportOilChanges(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        var (from, to) = ParsePeriod(ctx, defaultCurrentMonth: true);
        string search = (ctx.Request.Query["q"].ToString() is { Length: > 0 } q ? q : ctx.Request.Query["search"].ToString() ?? "").Trim();
        List<JsonObject> items;
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            items = [];
        else
            items = LiveRead(ctx, token =>
            {
                using var erp = OpenErp(config);
                return store.Read(c => OperationsLiveQuery.OilChangesPeriod(erp, from, to, 1, OperationsLiveQuery.MaxPageSize, search, Operations.LiveScope(c, ctx.User), export: true, token).Items.ToList());
            }, "operations-oil-export", TimeSpan.FromSeconds(ExportTimeoutSeconds(config)));
        items = TableFilterContract.Apply(items, ctx.Request.Query, OilChangeColumns, AliasesFor("vehicles", "oil"));
        var cols = new[] { "اللوحة", "التاريخ", "العداد", "لترات", "تكلفة الزيت", "الفلتر", "أجور", "أخرى", "الإجمالي" }.Select(h => new ExcelColumn(h)).ToArray();
        var rows = items.Select(r => new object?[] { r.S("plateNumberAr"), r.S("performedAt"), r["odometer"]?.ToString(), r["oilLiters"]?.ToString(), r["oilCost"]?.ToString(), r["filterCost"]?.ToString(), r["laborCost"]?.ToString(), r["otherCost"]?.ToString(), r["totalCost"]?.ToString() }).ToList();
        return Results.File(ExcelFiles.Write(cols, rows), ExcelFiles.Mime, "operations-oil-changes.xlsx");
    }

    static IResult ExportPurchases(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        var (from, to) = ParsePeriod(ctx, defaultCurrentMonth: true);
        string search = (ctx.Request.Query["q"].ToString() is { Length: > 0 } q ? q : ctx.Request.Query["search"].ToString() ?? "").Trim();
        List<JsonObject> items;
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            items = store.Read(c => ExtractItems(BuildStorePurchases(c, ctx.User, from, to, 1, 50_000, search)).ToList());
        else
            items = LiveRead(ctx, token =>
            {
                using var erp = OpenErp(config);
                return store.Read(c => OperationsLiveQuery.PurchaseInvoices(erp, from, to, 1, OperationsLiveQuery.MaxPageSize, search, Operations.LiveScope(c, ctx.User), null, token, unpaged: true).Items.ToList());
            }, "operations-purchases-export", TimeSpan.FromSeconds(ExportTimeoutSeconds(config)));
        items = TableFilterContract.Apply(items, ctx.Request.Query, PurchaseInvoiceColumns, AliasesFor("vehicles", "invoices"));
        var cols = new[] { "رقم الاستلام", "فاتورة المورد", "المورد", "تاريخ الفاتورة", "تاريخ الاستلام", "الإجمالي", "الحالة" }.Select(h => new ExcelColumn(h)).ToArray();
        var rows = items.Select(r => new object?[] { r.S("receiptNumber"), r.S("supplierInvoiceNumber"), r.S("supplierNameAr"), r.S("invoiceDate"), r.S("receivedAt"), r["totalAmount"]?.ToString(), r.S("status") }).ToList();
        return Results.File(ExcelFiles.Write(cols, rows), ExcelFiles.Mime, "operations-purchases.xlsx");
    }

    static IResult ExportFuel(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        DateOnly month = ParseMonth(ctx);
        string provider = (ctx.Request.Query["provider"].ToString() ?? "").Trim();
        string search = (ctx.Request.Query["q"].ToString() is { Length: > 0 } q ? q : ctx.Request.Query["search"].ToString() ?? "").Trim();
        List<JsonObject> items;
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
        {
            items = store.Read(c => ExtractItems(BuildStoreFuel(c, ctx.User, month, 1, 50_000)).ToList());
            if (provider.Length > 0)
                items = items.Where(r => string.Equals(r.S("provider"), provider, StringComparison.OrdinalIgnoreCase) || string.Equals(r.S("providerNameAr"), provider, StringComparison.OrdinalIgnoreCase)).ToList();
            if (search.Length > 0)
                items = items.Where(r => TextSearch.Matches(search, $"{r.S("cardNumber")} {r.S("cardName")} {r.S("cardNotes")} {r.S("riderNameAr")} {r.S("iqama")} {r.S("plateNumber")}")).ToList();
        }
        else
            items = LiveRead(ctx, token =>
            {
                using var erp = OpenErp(config);
                return store.Read(c => OperationsLiveQuery.FuelCardsExport(erp, month, provider, search, Operations.LiveScope(c, ctx.User), ExportTimeoutSeconds(config), token).Items.ToList());
            }, "operations-fuel-export", TimeSpan.FromSeconds(ExportTimeoutSeconds(config)));
        items = TableFilterContract.Apply(items, ctx.Request.Query, FuelColumns, AliasesFor("fuel", ""), options: OpsFilterOptions);
        var cols = new[] { "اسم البطاقة", "رقم البطاقة", "لوحة", "مندوب", "رقم الإقامة", "لتر", "قبل الضريبة", "ضريبة", "إجمالي", "سقف", "الحالة" }.Select(h => new ExcelColumn(h)).ToArray();
        var rows = items.Select(r => new object?[] { r.S("cardName", r.S("providerNameAr", r.S("provider"))), r.S("cardNumber"), r.S("plateNumber"), r.S("riderNameAr"), r.S("iqama"), r["liters"]?.ToString(), r["amountBeforeTax"]?.ToString(), r["vat"]?.ToString(), r["totalInclVat"]?.ToString(), OperationsLiveQuery.NoSourceYet, OperationsLiveQuery.FuelStatusLabel(r.S("status")) }).ToList();
        return Results.File(ExcelFiles.Write(cols, rows), ExcelFiles.Mime, "operations-fuel.xlsx");
    }

    static IResult ExportFuelLog(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "operations.view");
        var (from, to) = ParsePeriod(ctx, defaultCurrentMonth: true);
        string search = (ctx.Request.Query["q"].ToString() is { Length: > 0 } q ? q : ctx.Request.Query["search"].ToString() ?? "").Trim();
        List<JsonObject> items;
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            items = [];
        else
            items = LiveRead(ctx, token =>
            {
                using var erp = OpenErp(config);
                return store.Read(c => OperationsLiveQuery.FuelAssignments(erp, from, to, 1, OperationsLiveQuery.MaxPageSize, Operations.LiveScope(c, ctx.User), token, unpaged: true, search: search).Items.ToList());
            }, "operations-fuel-log-export", TimeSpan.FromSeconds(ExportTimeoutSeconds(config)));
        items = TableFilterContract.Apply(items, ctx.Request.Query, FuelColumns, AliasesFor("fuel", "log"), options: OpsFilterOptions);
        var cols = new[] { "اسم البطاقة", "رقم البطاقة", "المندوب", "رقم الإقامة", "المركبة", "من", "إلى", "سبب التسليم", "سبب الإنهاء" }.Select(h => new ExcelColumn(h)).ToArray();
        var rows = items.Select(r => new object?[] { r.S("cardName", r.S("providerNameAr", r.S("provider"))), r.S("cardNumber"), r.S("riderNameAr"), r.S("iqama"), r.S("plateNumber"), r.S("effectiveFrom"), r.S("effectiveTo"), r.S("assignmentReason"), r.S("endReason") }).ToList();
        return Results.File(ExcelFiles.Write(cols, rows), ExcelFiles.Mime, "operations-fuel-log.xlsx");
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
