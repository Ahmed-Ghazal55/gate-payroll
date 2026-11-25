using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.SqlClient;
namespace Payroll;

/// <summary>
/// L1 pay-run input readers from Omar's ERP (U-W2-34 / U-W2-11b / U-W2-10): fuel consumption,
/// vehicle assignment periods, housing periods, Keeta eligibility (expected), HS stamp/WalletPending.
/// Facts only; capture and screens call these later.
/// </summary>
public static class PaySourcesLiveApi
{
    static readonly TimeSpan LiveReadTimeout = TimeSpan.FromSeconds(20);

    public static void Map(RouteGroupBuilder api, Store store, IConfiguration config, IHostEnvironment env)
    {
        api.MapGet("/pay/sources/fuel-consumption", (HttpContext ctx) => Fuel(store, config, env, ctx))
            .WithTags("Payroll").WithSummary("L1 fuel consumption per rider/card/period (Omar live, facts only)");
        api.MapGet("/pay/sources/vehicle-assignment-periods", (HttpContext ctx) => Vehicles(store, config, env, ctx))
            .WithTags("Payroll").WithSummary("L1 vehicle assignment periods per rider (Omar live, facts only)");
        api.MapGet("/pay/sources/housing-periods", (HttpContext ctx) => Housing(store, config, env, ctx))
            .WithTags("Payroll").WithSummary("L1 housing residence periods per rider (Omar live, facts only)");
        api.MapGet("/pay/sources/keeta-eligibility", (HttpContext ctx) => Keeta(store, config, env, ctx))
            .WithTags("Payroll").WithSummary("Keeta expected eligibility result per user/period (Omar live; not approval)");
        api.MapGet("/pay/sources/hs-stamp-queue", (HttpContext ctx) => Hs(store, config, env, ctx))
            .WithTags("Payroll").WithSummary("HungerStation WalletPending queue and stamped financial events (read side)");
        // T12/T13: Jahez daily + Chefz %/daily from Omar PlatformDailyCommissionRates (read-only).
        api.MapGet("/pay/apps/{app}/omar-commission-rates", (string app, HttpContext ctx) => OmarRates(store, config, env, app, ctx))
            .WithTags("Payroll").WithSummary("Omar ERP daily/percentage commission rates per city (read-only; Jahez/Chefz)");
    }

    static object OmarRates(Store store, IConfiguration config, IHostEnvironment env, string app, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "pay.view");
        app = PayPlatforms.App(app);
        J.Need(app is "jahez" or "chefz", "عمولة نظام عمر متاحة لجاهز وشيفز فقط.");
        if (!FinancialScopes.Platform(ctx.User, PayPlatforms.For(app).Platform))
            throw new ApiError("التطبيق خارج نطاق المستخدم.", 403);
        DateOnly? asOf = null;
        string raw = ctx.Request.Query["asOf"].ToString();
        if (raw != "")
        {
            if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                asOf = d;
            else if (DateOnly.TryParseExact(raw + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m))
                asOf = m;
            else throw new ApiError("تاريخ asOf يجب أن يكون yyyy-MM-dd أو yyyy-MM.");
        }
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
        {
            // Store mode: no live ERP — surface missing so UI shows «غير متوفر في نظام عمر».
            var empty = PlatformCommissionLiveQuery.FromMappedRates(app, [], asOf ?? DateOnly.FromDateTime(DateTime.UtcNow));
            empty["source"] = Operations.SourceStore;
            empty["readAt"] = DateTimeOffset.UtcNow.ToString("O");
            empty["allowedActions"] = Actions(ctx.User);
            return empty;
        }
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            var result = PlatformCommissionLiveQuery.Read(erp, app, asOf, token);
            result["allowedActions"] = Actions(ctx.User);
            return result;
        }, "omar-commission-rates");
    }

    static object Fuel(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "pay.view");
        var (from, to) = ParseFuelRange(ctx);
        var (page, pageSize, search) = ParsePage(ctx);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c => Envelope(StoreFuel(c, ctx.User, from, to, page, pageSize), Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User)));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                var result = PaySourcesLiveQuery.FuelConsumption(erp, from, to, page, pageSize, search, Operations.LiveScope(c, ctx.User), token);
                return Envelope(new
                {
                    items = result.Items,
                    total = result.Total,
                    page,
                    pageSize,
                    summary = result.Summary,
                    from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
            });
        }, "pay-sources-fuel");
    }

    static object Vehicles(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "pay.view");
        var (from, to) = ParsePeriod(ctx);
        var (page, pageSize, search) = ParsePage(ctx);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c => Envelope(StoreVehiclePeriods(c, ctx.User, from, to, page, pageSize), Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User)));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                try
                {
                    var result = PaySourcesLiveQuery.VehicleAssignmentPeriods(erp, from, to, page, pageSize, search, Operations.LiveScope(c, ctx.User), token);
                    return Envelope(new
                    {
                        items = result.Items,
                        total = result.Total,
                        page,
                        pageSize,
                        summary = result.Summary,
                        from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "invalid_period") { throw new ApiError("فترة غير صالحة."); }
            });
        }, "pay-sources-vehicle");
    }

    static object Housing(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "pay.view");
        var (from, to) = ParsePeriod(ctx);
        var (page, pageSize, search) = ParsePage(ctx);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c => Envelope(StoreHousingPeriods(c, ctx.User, from, to, page, pageSize), Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User)));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                try
                {
                    var result = PaySourcesLiveQuery.HousingPeriods(erp, from, to, page, pageSize, search, Operations.LiveScope(c, ctx.User), token);
                    return Envelope(new
                    {
                        items = result.Items,
                        total = result.Total,
                        page,
                        pageSize,
                        summary = result.Summary,
                        from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
                }
                catch (ImportFailure ex) when (ex.Code == "invalid_period") { throw new ApiError("فترة غير صالحة."); }
            });
        }, "pay-sources-housing");
    }

    static object Keeta(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "pay.view");
        DateOnly month = ParseMonth(ctx);
        var (page, pageSize, search) = ParsePage(ctx);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c => Envelope(new
            {
                items = Array.Empty<JsonObject>(),
                total = 0L,
                page,
                pageSize,
                summary = new JsonObject { ["rows"] = 0L, ["eligible"] = 0L, ["notEligible"] = 0L, ["pending"] = 0L },
                month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                noteAr = "أهلية كيتا المتوقعة تُقرأ مباشرة من نظام عمر عند تفعيل القراءة الحية"
            }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User)));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                var result = PaySourcesLiveQuery.KeetaEligibility(erp, month, page, pageSize, search, Operations.LiveScope(c, ctx.User), token);
                return Envelope(new
                {
                    items = result.Items,
                    total = result.Total,
                    page,
                    pageSize,
                    summary = result.Summary,
                    month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    approval = "expected_only"
                }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
            });
        }, "pay-sources-keeta-eligibility");
    }

    static object Hs(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "pay.view");
        var (page, pageSize, search) = ParsePage(ctx);
        string disposition = (ctx.Request.Query["disposition"].ToString() ?? "").Trim();
        if (disposition.Length > 0 && disposition is not ("WalletPending" or "DeferredNextMonth"))
            throw new ApiError("قيمة disposition يجب أن تكون WalletPending أو DeferredNextMonth.");
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c => Envelope(new
            {
                items = Array.Empty<JsonObject>(),
                total = 0L,
                page,
                pageSize,
                summary = new JsonObject
                {
                    ["rows"] = 0L,
                    ["walletPending"] = 0L,
                    ["deferredNextMonth"] = 0L,
                    ["stampedEvents"] = 0L,
                    ["pendingSealEvents"] = 0L
                },
                noteAr = "طابور الختم ووضعية المحفظة يُقرآن من نظام عمر بعد تفعيل القراءة الحية"
            }, Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User)));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                var result = PaySourcesLiveQuery.HsStampQueue(erp, page, pageSize, search, disposition, Operations.LiveScope(c, ctx.User), token);
                return Envelope(new
                {
                    items = result.Items,
                    total = result.Total,
                    page,
                    pageSize,
                    summary = result.Summary
                }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
            });
        }, "pay-sources-hs-stamp");
    }

    static object StoreFuel(DbConnection c, ClaimsPrincipal user, DateOnly from, DateOnly to, int page, int pageSize)
    {
        string period = from.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var rows = Operations.List(c, user, "fuel_usage")
            .Where(r =>
            {
                string rm = r.S("ReportMonth", r.S("reportMonth", r.S("month", r.S("period"))));
                if (rm.Length >= 7 && rm[..7] == period) return true;
                return false;
            })
            .Select(r => new JsonObject
            {
                ["id"] = r.S("id"),
                ["fuelCardId"] = r.S("fuelCardId", r.S("FuelCardId")),
                ["cardNumber"] = r.S("cardNumber"),
                ["provider"] = r.S("provider"),
                ["plateNumber"] = r.S("plateNumber"),
                ["riderProfileId"] = r.S("riderProfileId", r.S("riderId")),
                ["employeeId"] = r.S("employeeId"),
                ["riderNameAr"] = r.S("riderNameAr"),
                ["iqama"] = r.S("iqama"),
                ["period"] = period,
                ["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["to"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["liters"] = r.N("TotalLiters", r.N("liters")),
                ["amountBeforeTax"] = r.N("AmountBeforeTax", r.N("amountBeforeTax")),
                ["vat"] = r.N("VatAmount", r.N("vat")),
                ["totalInclVat"] = r.N("TotalAmount", r.N("totalAmount", r.N("amount"))),
                ["transactionCount"] = r.N("TransactionCount", r.N("transactionCount")),
                ["fuelType"] = r.S("FuelType", r.S("fuelType")),
                ["usageSource"] = r.S("usageSource", "monthly"),
                ["coveredFrom"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["coveredTo"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["sponsorUnifiedNumber"] = ""
            }).ToList();
        int skip = (page - 1) * pageSize;
        var items = rows.Skip(skip).Take(pageSize).ToList();
        var summary = new JsonObject
        {
            ["rows"] = rows.Count,
            ["riders"] = rows.Select(r => r.S("riderProfileId")).Where(x => x != "").Distinct().Count(),
            ["totalLiters"] = rows.Sum(r => r.N("liters")),
            ["totalInclVat"] = rows.Sum(r => r.N("totalInclVat"))
        };
        return new
        {
            items,
            total = rows.Count,
            page,
            pageSize,
            summary,
            from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
    }

    static object StoreVehiclePeriods(DbConnection c, ClaimsPrincipal user, DateOnly from, DateOnly to, int page, int pageSize)
    {
        var rows = Operations.List(c, user, "vehicle_assignment")
            .Where(r => Overlaps(r, from, to, "startedAt", "endedAt", "StartedAtUtc", "EndedAtUtc"))
            .Select(r => new JsonObject
            {
                ["id"] = r.S("id"),
                ["riderProfileId"] = r.S("riderId", r.S("riderProfileId")),
                ["vehicleId"] = r.S("vehicleId"),
                ["isRealRider"] = r.B("isRealRider") || r.B("IsRealRider"),
                ["riderNameAr"] = r.S("riderNameAr"),
                ["iqama"] = r.S("iqama"),
                ["assetNumber"] = r.S("assetNumber"),
                ["plateNumberAr"] = r.S("plateNumberAr"),
                ["plateNumberEn"] = r.S("plateNumberEn"),
                ["vehicleType"] = r.S("vehicleType"),
                ["startedAt"] = r.S("startedAt", r.S("StartedAtUtc")),
                ["endedAt"] = r.S("endedAt", r.S("EndedAtUtc")),
                ["startOdometer"] = r.N("startOdometer"),
                ["endOdometer"] = r.N("endOdometer"),
                ["permissionStartsOn"] = r.S("permissionStartsOn"),
                ["permissionEndsOn"] = r.S("permissionEndsOn"),
                ["status"] = r.S("status"),
                ["sponsorUnifiedNumber"] = "",
                ["isOpen"] = r.S("endedAt", r.S("EndedAtUtc")) == ""
            }).ToList();
        int skip = (page - 1) * pageSize;
        var items = rows.Skip(skip).Take(pageSize).ToList();
        var summary = new JsonObject
        {
            ["rows"] = rows.Count,
            ["openAssignments"] = rows.Count(r => r.B("isOpen"))
        };
        return new
        {
            items,
            total = rows.Count,
            page,
            pageSize,
            summary,
            from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
    }

    static object StoreHousingPeriods(DbConnection c, ClaimsPrincipal user, DateOnly from, DateOnly to, int page, int pageSize)
    {
        var rows = Operations.List(c, user, "housing_residence")
            .Where(r => Overlaps(r, from, to, "effectiveFrom", "effectiveTo", "EffectiveFrom", "EffectiveTo"))
            .Select(r =>
            {
                var start = ParseDate(r.S("effectiveFrom", r.S("EffectiveFrom"))) ?? from;
                var end = ParseDate(r.S("effectiveTo", r.S("EffectiveTo")));
                var clipStart = start > from ? start : from;
                var clipEnd = end is null || end > to ? to : end.Value;
                int days = clipEnd < clipStart ? 0 : clipEnd.DayNumber - clipStart.DayNumber + 1;
                return new JsonObject
                {
                    ["id"] = r.S("id"),
                    ["employeeId"] = r.S("employeeId", r.S("EmployeeId")),
                    ["riderProfileId"] = r.S("riderId", r.S("riderProfileId")),
                    ["riderNameAr"] = r.S("riderNameAr"),
                    ["iqama"] = r.S("iqama"),
                    ["housingId"] = r.S("housingId"),
                    ["housingCode"] = r.S("housingCode"),
                    ["housingNameAr"] = r.S("housingNameAr"),
                    ["roomId"] = r.S("roomId", r.S("RoomId")),
                    ["roomName"] = r.S("roomName"),
                    ["effectiveFrom"] = r.S("effectiveFrom", r.S("EffectiveFrom")),
                    ["effectiveTo"] = r.S("effectiveTo", r.S("EffectiveTo")),
                    ["daysInSelectedPeriod"] = days,
                    ["isOpen"] = r.S("effectiveTo", r.S("EffectiveTo")) == "",
                    ["moveInReason"] = r.S("moveInReason"),
                    ["moveOutReason"] = r.S("moveOutReason"),
                    ["sponsorUnifiedNumber"] = ""
                };
            }).ToList();
        int skip = (page - 1) * pageSize;
        var items = rows.Skip(skip).Take(pageSize).ToList();
        var summary = new JsonObject
        {
            ["rows"] = rows.Count,
            ["openPeriods"] = rows.Count(r => r.B("isOpen")),
            ["daysInPeriod"] = rows.Sum(r => (long)r.N("daysInSelectedPeriod"))
        };
        return new
        {
            items,
            total = rows.Count,
            page,
            pageSize,
            summary,
            from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
    }

    static bool Overlaps(JsonObject row, DateOnly from, DateOnly to, params string[] keys)
    {
        string startRaw = "", endRaw = "";
        for (int i = 0; i + 1 < keys.Length; i += 2)
        {
            if (startRaw.Length == 0) startRaw = row.S(keys[i]);
            if (endRaw.Length == 0) endRaw = row.S(keys[i + 1]);
        }
        var start = ParseDate(startRaw);
        if (start is null) return false;
        var end = ParseDate(endRaw);
        if (start > to) return false;
        if (end is not null && end < from) return false;
        return true;
    }

    static DateOnly? ParseDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return DateOnly.FromDateTime(dto.ToOffset(TimeSpan.FromHours(3)).DateTime);
        return null;
    }

    static (int Page, int PageSize, string Search) ParsePage(HttpContext ctx)
    {
        int page = 1, pageSize = 50;
        if (int.TryParse(ctx.Request.Query["page"], NumberStyles.None, CultureInfo.InvariantCulture, out int p) && p > 0) page = p;
        if (int.TryParse(ctx.Request.Query["pageSize"], NumberStyles.None, CultureInfo.InvariantCulture, out int ps) && ps > 0)
            pageSize = Math.Min(ps, PaySourcesLiveQuery.MaxPageSize);
        string search = (ctx.Request.Query["q"].ToString() is { Length: > 0 } q ? q : ctx.Request.Query["search"].ToString() ?? "").Trim();
        return (page, pageSize, search);
    }

    static (DateOnly From, DateOnly To) ParsePeriod(HttpContext ctx)
    {
        string fromRaw = (ctx.Request.Query["from"].ToString() ?? "").Trim();
        string toRaw = (ctx.Request.Query["to"].ToString() ?? "").Trim();
        if (fromRaw.Length == 0 && toRaw.Length == 0)
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

    static (DateOnly From, DateOnly To) ParseFuelRange(HttpContext ctx)
    {
        string fromRaw = (ctx.Request.Query["from"].ToString() ?? "").Trim();
        string toRaw = (ctx.Request.Query["to"].ToString() ?? "").Trim();
        if (fromRaw.Length > 0 || toRaw.Length > 0)
            return ParsePeriod(ctx);
        DateOnly month = ParseMonth(ctx);
        return (month, new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month)));
    }

    static JsonArray Actions(ClaimsPrincipal user)
    {
        var actions = new JsonArray();
        if (AccessPolicy.Has(user, "pay.view")) actions.Add("refresh");
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

    static T LiveRead<T>(HttpContext ctx, Func<CancellationToken, T> read, string kind)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
        cancel.CancelAfter(LiveReadTimeout);
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
