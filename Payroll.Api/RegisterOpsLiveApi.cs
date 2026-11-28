using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
namespace Payroll;

/// <summary>
/// Live register-sheet ops display (A..AH) from Omar's ERP read-only path.
/// Short in-memory cache; no stored snapshots on read. Snapshot only via pay-cycle capture.
/// </summary>
public static class RegisterOpsLiveApi
{
    static readonly TimeSpan LiveReadTimeout = TimeSpan.FromSeconds(50);
    static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(RegisterOpsLiveQuery.ShortCacheSeconds);

    public static void Map(RouteGroupBuilder api, Store store, IConfiguration config, IHostEnvironment env)
    {
        api.MapGet("/pay/apps/{app}/live/register-ops", (string app, HttpContext ctx, IMemoryCache cache) =>
                Read(store, config, env, ctx, cache, app))
            .WithTags("Payroll")
            .WithSummary("Live register-sheet operations display (A..AH) from Omar ERP; expected eligibility only");
    }

    static object Read(Store store, IConfiguration config, IHostEnvironment env, HttpContext ctx, IMemoryCache cache, string app)
    {
        AccessPolicy.Demand(ctx.User, "pay.view");
        app = (app ?? "").Trim().ToLowerInvariant();
        J.Need(app is "keeta" or "ninja", "التطبيق يجب أن يكون كيتا أو نينجا.");
        DemandPlatform(ctx.User, app);

        var request = Parse(ctx, app);
        if (Operations.SourceMode(config, env) == Operations.SourceStore)
            return Empty(request, ctx.User);

        string cacheKey = CacheKey(ctx.User, request);
        if (cache.TryGetValue(cacheKey, out JsonObject? cached) && cached != null)
        {
            var hit = cached.DeepClone()!.AsObject();
            hit["cache"] = "hit";
            hit["allowedActions"] = Actions(ctx.User);
            return hit;
        }

        return LiveRead(ctx, token =>
        {
            using var erp = Operations.OpenErp(config);
            return store.Read(c =>
            {
                var page = RegisterOpsLiveQuery.Query(erp, Operations.LiveScope(c, ctx.User), request, token);
                var envelope = Envelope(page, request, ctx.User);
                cache.Set(cacheKey, envelope.DeepClone()!.AsObject(), new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheTtl,
                    Size = 1
                });
                envelope["cache"] = "miss";
                return envelope;
            });
        }, app + "-register-ops");
    }

    static RegisterOpsLiveQuery.Request Parse(HttpContext ctx, string app)
    {
        var q = ctx.Request.Query;
        string registerRaw = (q["register"].ToString() ?? "").Trim();
        J.Need(registerRaw != "", "اختر السجل (إكسبرس أو البوابا أو التجارية).");
        string register = KeetaOperationsImport.ResolveRegister(registerRaw);
        var sponsor = FinancialCore.Sponsors.Single(s => s.Code == register);

        DateOnly from;
        DateOnly to;
        string fromRaw = (q["from"].ToString() ?? "").Trim();
        string toRaw = (q["to"].ToString() ?? "").Trim();
        if (fromRaw == "" || toRaw == "")
        {
            if (app == "ninja")
            {
                from = new DateOnly(2026, 10, 1);
                to = new DateOnly(2026, 10, 31);
            }
            else
            {
                from = new DateOnly(2026, 11, 1);
                to = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            }
        }
        else
        {
            bool okFrom = DateOnly.TryParse(fromRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out from);
            bool okTo = DateOnly.TryParse(toRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out to);
            J.Need(okFrom && okTo && to >= from, "فترة غير صالحة (from/to بصيغة سنة-شهر-يوم).");
        }

        int page = 1, pageSize = 200;
        if (int.TryParse(q["page"], NumberStyles.None, CultureInfo.InvariantCulture, out int p) && p > 0) page = p;
        if (int.TryParse(q["pageSize"], NumberStyles.None, CultureInfo.InvariantCulture, out int ps) && ps > 0)
            pageSize = Math.Min(ps, RegisterOpsLiveQuery.MaxPageSize);

        return new(app, register, sponsor.UnifiedNumber, from, to, page, pageSize);
    }

    static void DemandPlatform(ClaimsPrincipal user, string app)
    {
        string platform = app == "keeta" ? "Keeta" : "Ninja";
        if (!FinancialScopes.Platform(user, platform))
            throw new ApiError("المنصة خارج نطاقك.", 403);
    }

    static string CacheKey(ClaimsPrincipal user, RegisterOpsLiveQuery.Request request)
    {
        string userKey = user.Identity?.Name ?? "";
        string iban = AccessPolicy.Has(user, WorkforceApi.PermIban) ? "iban1" : "iban0";
        return $"register-ops:{request.App}:{request.RegisterCode}:{request.From:yyyy-MM-dd}:{request.To:yyyy-MM-dd}:{request.Page}:{request.PageSize}:{userKey}:{iban}";
    }

    static JsonObject Empty(RegisterOpsLiveQuery.Request request, ClaimsPrincipal user) => new()
    {
        ["items"] = new JsonArray(),
        ["displayRows"] = new JsonArray(),
        ["total"] = 0,
        ["page"] = request.Page,
        ["pageSize"] = request.PageSize,
        ["register"] = request.RegisterCode,
        ["registerSheet"] = request.RegisterCode switch { "EX" => "Express", "BW" => "Albwaba", "TR" => "Togary", _ => request.RegisterCode },
        ["columns"] = new JsonArray(RegisterOpsLiveQuery.Headers.Select(h => (JsonNode)h).ToArray()),
        ["counts"] = new JsonObject { ["rows"] = 0, ["dismissed"] = 0, ["duplicateIdRows"] = 0, ["uniqueIds"] = 0 },
        ["coverage"] = new JsonObject
        {
            ["requestedFrom"] = request.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["requestedTo"] = request.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["firstDay"] = "",
            ["lastDay"] = "",
            ["missingDays"] = new JsonArray(),
            ["missingDayCount"] = 0,
            ["hasGapAtPeriodEnd"] = false
        },
        ["erpSources"] = new JsonArray((RegisterOpsLiveQuery.ErpSources.TryGetValue(request.App, out var s) ? s : []).Select(x => (JsonNode)x).ToArray()),
        ["source"] = Operations.SourceStore,
        ["asOf"] = DateTimeOffset.UtcNow.ToString("O"),
        ["cache"] = "bypass",
        ["allowedActions"] = Actions(user)
    };

    static JsonObject Envelope(RegisterOpsLiveQuery.PageResult page, RegisterOpsLiveQuery.Request request, ClaimsPrincipal user)
    {
        bool canViewIban = AccessPolicy.Has(user, WorkforceApi.PermIban);
        var rows = page.DisplayRows.Select(i => i.DeepClone()!.AsObject()).ToList();
        RegisterOpsLiveQuery.MaskIbanUnlessPermitted(rows, canViewIban);
        return new()
        {
            ["items"] = new JsonArray(rows.Select(i => (JsonNode?)i.DeepClone()).ToArray()),
            ["displayRows"] = new JsonArray(rows.Select(i => (JsonNode?)i).ToArray()),
            ["total"] = page.Total,
            ["page"] = request.Page,
            ["pageSize"] = request.PageSize,
            ["register"] = page.Register,
            ["registerSheet"] = page.RegisterSheet,
            ["columns"] = new JsonArray(page.Columns.Select(h => (JsonNode)h).ToArray()),
            ["counts"] = page.Counts.DeepClone(),
            ["coverage"] = page.Coverage.DeepClone(),
            ["erpSources"] = new JsonArray(page.Sources.Select(x => (JsonNode)x).ToArray()),
            ["source"] = Operations.SourceLive,
            ["asOf"] = page.AsOf.ToString("O"),
            ["allowedActions"] = Actions(user)
        };
    }

    static JsonArray Actions(ClaimsPrincipal user)
    {
        var actions = new JsonArray();
        if (AccessPolicy.Has(user, "pay.view")) actions.Add("refresh");
        if (AccessPolicy.Has(user, "pay.manage")) actions.Add("capture");
        return actions;
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
