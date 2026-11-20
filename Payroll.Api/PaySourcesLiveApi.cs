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
    }

    static object Fuel(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "pay.view");
        DateOnly month = ParseMonth(ctx);
        var (page, pageSize, search) = ParsePage(ctx);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return store.Read(c => Envelope(StoreFuel(c, ctx.User, month, page, pageSize), Operations.SourceStore, DateTimeOffset.UtcNow, Actions(ctx.User)));
        return LiveRead(ctx, token =>
        {
            using var erp = OpenErp(config);
            return store.Read(c =>
            {
                var result = PaySourcesLiveQuery.FuelConsumption(erp, month, page, pageSize, search, Operations.LiveScope(c, ctx.User), token);
                return Envelope(new
                {
                    items = result.Items,
                    total = result.Total,
                    page,
                    pageSize,
                    summary = result.Summary,
                    month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture)
                }, Operations.SourceLive, result.AsOf, Actions(ctx.User));
            });
        }, "pay-sources-fuel");
    }
    static object StoreFuel(DbConnection c, ClaimsPrincipal user, DateOnly month, int page, int pageSize)
    {
        string period = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        string reportMonth = month.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var rows = Operations.List(c, user, "fuel_usage")
            .Where(r =>
            {
                string rm = r.S("ReportMonth", r.S("reportMonth", r.S("month", r.S("period"))));
                if (rm.Length >= 7 && rm[..7] == period) return true;
                return rm.StartsWith(reportMonth, StringComparison.Ordinal);
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
                ["reportMonth"] = reportMonth,
                ["liters"] = r.N("TotalLiters", r.N("liters")),
                ["amountBeforeTax"] = r.N("AmountBeforeTax", r.N("amountBeforeTax")),
                ["vat"] = r.N("VatAmount", r.N("vat")),
                ["totalInclVat"] = r.N("TotalAmount", r.N("totalAmount", r.N("amount"))),
                ["transactionCount"] = r.N("TransactionCount", r.N("transactionCount")),
                ["fuelType"] = r.S("FuelType", r.S("fuelType")),
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
        return new { items, total = rows.Count, page, pageSize, summary, month = period };
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
