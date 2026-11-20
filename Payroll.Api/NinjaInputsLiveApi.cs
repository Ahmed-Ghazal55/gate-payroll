using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.SqlClient;
namespace Payroll;

/// <summary>Live Ninja pay-run input readers (L1): performance, deductions, payment method, account bearers.</summary>
public static class NinjaInputsLiveApi
{
    static readonly TimeSpan LiveReadTimeout = TimeSpan.FromSeconds(20);

    public static void Map(RouteGroupBuilder api, Store store, IConfiguration config, IHostEnvironment env)
    {
        api.MapGet("/pay/apps/ninja/live/daily-performance", (HttpContext ctx) => DailyPerformance(store, config, env, ctx))
            .WithTags("Payroll").WithSummary("Live Ninja daily performance per account and day with coverage (L1)");
        api.MapGet("/pay/apps/ninja/live/daily-deductions", (HttpContext ctx) => DailyDeductions(store, config, env, ctx))
            .WithTags("Payroll").WithSummary("Live Ninja daily deductions per actual rider of each day with coverage (L1)");
    }

    static object DailyDeductions(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "pay.view");
        DemandNinjaPlatform(ctx.User);
        var request = ParsePeriod(ctx);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return EmptyPeriod(request, ctx.User);
        return LiveRead(ctx, token =>
        {
            using var erp = Operations.OpenErp(config);
            return store.Read(c =>
            {
                var page = NinjaInputsLiveQuery.DailyDeductions(erp, Operations.LiveScope(c, ctx.User), request, token);
                return Envelope(page, request.Page, request.PageSize, ctx.User);
            });
        }, "ninja-daily-deductions");
    }

    static object DailyPerformance(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx)
    {
        AccessPolicy.Demand(ctx.User, "pay.view");
        DemandNinjaPlatform(ctx.User);
        var request = ParsePeriod(ctx);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return EmptyPeriod(request, ctx.User);
        return LiveRead(ctx, token =>
        {
            using var erp = Operations.OpenErp(config);
            return store.Read(c =>
            {
                var page = NinjaInputsLiveQuery.DailyPerformance(erp, Operations.LiveScope(c, ctx.User), request, token);
                return Envelope(page, request.Page, request.PageSize, ctx.User);
            });
        }, "ninja-daily-performance");
    }

    public static void DemandNinjaPlatform(ClaimsPrincipal user)
    {
        if (!FinancialScopes.Platform(user, "Ninja"))
            throw new ApiError("المنصة خارج نطاقك.", 403);
    }

    internal static NinjaInputsLiveQuery.PeriodRequest ParsePeriod(HttpContext ctx)
    {
        try
        {
            var dict = ctx.Request.Query.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            return NinjaInputsLiveQuery.ParsePeriod(dict);
        }
        catch (ImportFailure ex) when (ex.Code == "period_required")
        {
            throw new ApiError("تاريخ البداية والنهاية مطلوبان بصيغة yyyy-MM-dd (from/to).");
        }
        catch (ImportFailure ex) when (ex.Code is "invalid_period" or "invalid_page")
        {
            throw new ApiError(ex.Code == "invalid_page" ? "رقم الصفحة غير صالح." : "فترة غير صالحة.");
        }
    }

    internal static (int Page, int PageSize) ParsePage(HttpContext ctx)
    {
        try
        {
            var dict = ctx.Request.Query.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            return NinjaInputsLiveQuery.ParsePage(dict);
        }
        catch (ImportFailure)
        {
            throw new ApiError("رقم الصفحة غير صالح.");
        }
    }

    internal static JsonObject EmptyPeriod(NinjaInputsLiveQuery.PeriodRequest request, ClaimsPrincipal user) => new()
    {
        ["items"] = new JsonArray(),
        ["total"] = 0,
        ["page"] = request.Page,
        ["pageSize"] = request.PageSize,
        ["coverage"] = new JsonObject
        {
            ["requestedFrom"] = request.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["requestedTo"] = request.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["firstDay"] = "",
            ["lastDay"] = "",
            ["total"] = 0,
            ["hasGapAtPeriodEnd"] = false
        },
        ["source"] = Operations.SourceStore,
        ["asOf"] = DateTimeOffset.UtcNow.ToString("O"),
        ["allowedActions"] = Actions(user)
    };

    internal static JsonObject EmptyPage(int page, int pageSize, ClaimsPrincipal user) => new()
    {
        ["items"] = new JsonArray(),
        ["total"] = 0,
        ["page"] = page,
        ["pageSize"] = pageSize,
        ["coverage"] = new JsonObject { ["total"] = 0 },
        ["source"] = Operations.SourceStore,
        ["asOf"] = DateTimeOffset.UtcNow.ToString("O"),
        ["allowedActions"] = Actions(user)
    };

    internal static JsonObject Envelope(NinjaInputsLiveQuery.PageResult page, int pageNum, int pageSize, ClaimsPrincipal user) => new()
    {
        ["items"] = new JsonArray(page.Items.Select(i => (JsonNode?)i.DeepClone()).ToArray()),
        ["total"] = page.Total,
        ["page"] = pageNum,
        ["pageSize"] = pageSize,
        ["coverage"] = page.Coverage.DeepClone(),
        ["source"] = Operations.SourceLive,
        ["asOf"] = page.AsOf.ToString("O"),
        ["allowedActions"] = Actions(user)
    };

    internal static JsonArray Actions(ClaimsPrincipal user)
    {
        var actions = new JsonArray();
        if (AccessPolicy.Has(user, "pay.view")) actions.Add("refresh");
        if (AccessPolicy.Has(user, "pay.manage")) actions.Add("capture");
        return actions;
    }

    internal static T LiveRead<T>(HttpContext ctx, Func<CancellationToken, T> read, string kind)
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
