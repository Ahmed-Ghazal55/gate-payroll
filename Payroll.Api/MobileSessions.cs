using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

// The stored hash is a credential identifier. Only a short prefix ever leaves this service.
public static class MobileSessions
{
    public const string Kind = "mobile_session";
    const int PrefixLength = 16;
    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string PublicId(JsonObject session) => session.S("id")["mobile:".Length..][..PrefixLength];
    public static string Status(JsonObject session, DateTimeOffset? clock = null) =>
        session.S("revokedAt") != "" ? "revoked" :
        !DateTimeOffset.TryParse(session.S("expiresAtUtc"), out var until) || until <= (clock ?? DateTimeOffset.UtcNow) ? "expired" : "active";
    static bool SeenDue(JsonObject session, DateTimeOffset now) =>
        !DateTimeOffset.TryParse(session.S("lastSeenAt"), out var seen) || seen <= now.AddMinutes(-5);

    public static JsonObject Validate(DbConnection c, HttpContext ctx, string? iqama = null, DbTransaction? tx = null)
    {
        string header = ctx.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer rmob_", StringComparison.Ordinal) || header.Length != 76 || !header[12..].All(Uri.IsHexDigit))
            throw new ApiError("جلسة المندوب غير صالحة.", 401);
        var session = Store.Get(c, "mobile:" + Hash(header[7..]), Kind, tx);
        var rider = session == null ? null : Store.Get(c, session.S("iqama"), "rider", tx);
        if (session == null || Status(session) != "active" || iqama != null && session.S("iqama") != iqama ||
            rider == null || !rider.S("status").Equals("Active", StringComparison.OrdinalIgnoreCase))
            throw new ApiError("جلسة المندوب منتهية أو الحساب موقوف.", 401);
        if (tx != null) Touch(c, tx, session);
        return session;
    }

    static void Touch(DbConnection c, DbTransaction tx, JsonObject session)
    {
        var now = DateTimeOffset.UtcNow;
        if (!SeenDue(session, now)) return;
        session["lastSeenAt"] = now.ToString("O");
        Store.Save(c, Kind, session, tx);
    }

    // Skip the write transaction entirely until five minutes have passed. Revalidate under the
    // normal Store write lock so a concurrent revocation cannot be overwritten by the touch.
    public static JsonObject Authenticate(Store store, HttpContext ctx, string? iqama = null)
    {
        var session = store.Read(c => Validate(c, ctx, iqama));
        return SeenDue(session, DateTimeOffset.UtcNow)
            ? store.Write((c, tx) => Validate(c, ctx, iqama, tx), false) : session;
    }

    public static JsonObject View(JsonObject session, JsonObject rider)
    {
        string fcm = session.S("fcmDeviceToken");
        return new()
        {
            ["id"] = PublicId(session), ["iqama"] = session.S("iqama"), ["riderName"] = rider.S("name"),
            ["language"] = session.S("language", "ar"), ["createdAt"] = session.S("createdAt"),
            ["expiresAtUtc"] = session.S("expiresAtUtc"), ["lastSeenAt"] = session.S("lastSeenAt"),
            ["status"] = Status(session), ["deviceFingerprint"] = fcm == "" ? "" : Hash(fcm)[..8].ToLowerInvariant(),
            ["fcmRegistered"] = fcm != ""
        };
    }

    static bool Scoped(ClaimsPrincipal user, JsonObject rider) => FinancialScopes.Rider(user, rider) || FinancialScopes.AllCompanies(user);
    public static List<JsonObject> List(DbConnection c, ClaimsPrincipal user, string? iqama, string? status)
    {
        AccessPolicy.Demand(user, "mobile.sessions");
        if (!string.IsNullOrEmpty(iqama)) J.Iqama(iqama);
        J.Need(string.IsNullOrEmpty(status) || status is "active" or "expired" or "revoked", "حالة الجلسة غير صالحة.");
        var riders = Store.List(c, "rider").Where(r => Scoped(user, r)).ToDictionary(r => r.S("id"));
        return Store.List(c, Kind).Where(s => riders.ContainsKey(s.S("iqama")) && (string.IsNullOrEmpty(iqama) || s.S("iqama") == iqama) &&
            (string.IsNullOrEmpty(status) || Status(s) == status)).OrderByDescending(s => s.S("createdAt"))
            .Select(s => View(s, riders[s.S("iqama")])).ToList();
    }

    public static List<JsonObject> Mine(DbConnection c, HttpContext ctx)
    {
        string iqama = Validate(c, ctx).S("iqama");
        var rider = Store.Get(c, iqama, "rider")!;
        return Store.List(c, Kind).Where(s => s.S("iqama") == iqama).OrderByDescending(s => s.S("createdAt")).Select(s => View(s, rider)).ToList();
    }

    static string Reason(JsonObject input)
    {
        string reason = input.Required("reason").Trim();
        J.Need(reason.Length is > 0 and <= 500, "سبب إلغاء الجلسة مطلوب، بحد أقصى 500 حرف.");
        return reason;
    }
    static void RevokeRow(DbConnection c, DbTransaction tx, JsonObject session, string actor, string reason, string action)
    {
        if (session.S("revokedAt") != "") return;
        session["revokedAt"] = DateTimeOffset.UtcNow.ToString("O"); session["revokedBy"] = actor; session["revokeReason"] = reason;
        Store.Save(c, Kind, session, tx);
        Store.Audit(c, tx, action, PublicId(session), actor);
    }
    public static JsonObject Revoke(DbConnection c, DbTransaction tx, ClaimsPrincipal user, string id, JsonObject input)
    {
        AccessPolicy.Demand(user, "mobile.sessions"); string reason = Reason(input);
        J.Need(id.Length == PrefixLength && id.All(Uri.IsHexDigit), "الجلسة غير موجودة.", 404);
        var rows = Store.List(c, Kind, tx).Where(s => PublicId(s).Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
        J.Need(rows.Count == 1, "الجلسة غير موجودة أو المعرف غير فريد.", rows.Count == 0 ? 404 : 409);
        var session = rows[0]; var rider = Store.Get(c, session.S("iqama"), "rider", tx);
        J.Need(rider != null && Scoped(user, rider), "الجلسة غير موجودة.", 404);
        RevokeRow(c, tx, session, user.Identity!.Name!, reason, "mobile.session_revoked");
        return View(session, rider!);
    }
    public static int RevokeAll(DbConnection c, DbTransaction tx, ClaimsPrincipal user, string iqama, JsonObject input)
    {
        AccessPolicy.Demand(user, "mobile.sessions"); J.Iqama(iqama); string reason = Reason(input);
        var rider = Store.Get(c, iqama, "rider", tx);
        J.Need(rider != null && Scoped(user, rider), "المندوب غير موجود.", 404);
        var sessions = Store.List(c, Kind, tx).Where(s => s.S("iqama") == iqama && s.S("revokedAt") == "").ToList();
        foreach (var session in sessions) RevokeRow(c, tx, session, user.Identity!.Name!, reason, "mobile.session_revoked");
        return sessions.Count;
    }
    public static void Logout(DbConnection c, DbTransaction tx, HttpContext ctx)
    {
        var session = Validate(c, ctx, tx: tx);
        RevokeRow(c, tx, session, "rider:" + session.S("iqama"), "Rider logout", "mobile.logout");
    }

    public static void Map(WebApplication app, RouteGroupBuilder api, Store store)
    {
        api.MapGet("/rider-mobile/sessions", (string? iqama, string? status, HttpContext ctx) => store.Read(c => ApiPagination.Apply(List(c, ctx.User, iqama, status), ctx.Request.Query)))
            .WithTags("Riders").WithSummary("List mobile sessions with safe identifiers within the caller's company scope");
        app.MapGet("/api/rider-mobile/sessions/mine", (HttpContext ctx) =>
        {
            Authenticate(store, ctx); return store.Read(c => Mine(c, ctx));
        }).WithTags("Riders").WithSummary("Read own sessions using an active rider bearer token");
        api.MapPost("/rider-mobile/sessions/{id}/revoke", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) => Revoke(c, tx, ctx.User, id, input), false))
            .WithTags("Riders").WithSummary("Revoke a scoped mobile session with a required audited reason");
        api.MapPost("/rider-mobile/sessions/revoke-all", (string iqama, JsonObject input, HttpContext ctx) => store.Write((c, tx) => new { revoked = RevokeAll(c, tx, ctx.User, iqama, input) }, false))
            .WithTags("Riders").WithSummary("Revoke all sessions for a scoped rider, retaining history");
        app.MapPost("/api/rider-mobile/logout", (HttpContext ctx) => store.Write((c, tx) => { Logout(c, tx, ctx); return new { ok = true }; }, false))
            .WithTags("Riders").WithSummary("End the rider's current bearer session");
    }
}
