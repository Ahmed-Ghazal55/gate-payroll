using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
[TestClass]
public sealed class MobileSessionTests
{
    const string Rider = "2000000701", Other = "2000000702";
    const string Token = "rmob_" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string OtherToken = "rmob_" + "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    static readonly ClaimsPrincipal Admin = UserAccess.Principal(new() { ["id"] = "syn-admin", ["username"] = "syn-admin", ["role"] = "Admin" });
    static ClaimsPrincipal Scoped(string sponsor, bool permission = true) => UserAccess.Principal(new()
    {
        ["id"] = "syn-scoped", ["username"] = "syn-scoped", ["role"] = "Reviewer",
        ["customPermissions"] = permission ? new JsonArray("mobile.sessions") : new JsonArray(),
        ["financialScope"] = new JsonObject { ["sponsorIds"] = new JsonArray(sponsor) }
    });
    static DefaultHttpContext Context(string token = Token)
    {
        var ctx = new DefaultHttpContext(); ctx.Request.Headers.Authorization = "Bearer " + token; return ctx;
    }
    static string Id(string token) => "mobile:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    static void Run(Action<Store> body)
    {
        string dir = Path.Combine(Path.GetTempPath(), "gate-mobile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(dir);
            store.Write((c, tx) =>
            {
                foreach (var (rider, sponsor, token) in new[] { (Rider, "BW", Token), (Other, "EX", OtherToken) })
                {
                    Store.Save(c, "rider", new() { ["id"] = rider, ["iqama"] = rider, ["name"] = "Synthetic rider", ["status"] = "Active", ["financeSponsorId"] = FinancialCore.ResolveSponsor(sponsor).Id }, tx);
                    Store.Save(c, MobileSessions.Kind, new() { ["id"] = Id(token), ["iqama"] = rider, ["language"] = "en", ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"), ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(1).ToString("O"), ["fcmDeviceToken"] = "synthetic-fcm-token" }, tx);
                }
                return true;
            });
            body(store);
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    static void Change(Store s, string field, string value) => s.Write((c, tx) =>
    { var row = Store.Get(c, Id(Token), MobileSessions.Kind, tx)!; row[field] = value; Store.Save(c, MobileSessions.Kind, row, tx); return true; });
    static JsonObject Reason => new() { ["reason"] = "Synthetic device replacement" };

    [TestMethod]
    public void RevokedSessionFailsEverySharedAuthenticationPathAndKeepsAuditHistory()
    {
        Run(s =>
        {
            var session = MobileSessions.Authenticate(s, Context());
            var view = s.Write((c, tx) => MobileSessions.Revoke(c, tx, Admin, MobileSessions.PublicId(session), Reason));
            Assert.AreEqual("revoked", view.S("status"));
            Assert.AreEqual(401, Assert.ThrowsExactly<ApiError>(() => MobileSessions.Authenticate(s, Context())).Status);
            Assert.AreEqual(401, Assert.ThrowsExactly<ApiError>(() => s.Read(c => FinancialCore.AuthenticatedMobileRider(c, Context()))).Status);
            Assert.AreEqual(401, Assert.ThrowsExactly<ApiError>(() => s.Read(c => PayStatements.Mobile(c, Context(), "2026-10"))).Status);
            Assert.AreEqual(2, s.Read(c => Store.List(c, MobileSessions.Kind).Count));
            var row = s.Read(c => Store.Get(c, Id(Token), MobileSessions.Kind))!;
            Assert.AreEqual("syn-admin", row.S("revokedBy")); Assert.AreEqual(Reason.S("reason"), row.S("revokeReason"));
            Assert.AreEqual(1, s.Read(c => Store.List(c, "audit").Count(a => a.S("action") == "mobile.session_revoked")));
            s.Write((c, tx) => MobileSessions.Revoke(c, tx, Admin, view.S("id"), Reason));
            Assert.AreEqual(1, s.Read(c => Store.List(c, "audit").Count(a => a.S("action") == "mobile.session_revoked")));
        });
    }
    [TestMethod]
    public void ExpiredMissingMalformedAndInactiveRiderSessionsFailClosed()
    {
        Run(s =>
        {
            foreach (string expiry in new[] { DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O"), "invalid" })
            { Change(s, "expiresAtUtc", expiry); Assert.AreEqual(401, Assert.ThrowsExactly<ApiError>(() => MobileSessions.Authenticate(s, Context())).Status); }
            Assert.AreEqual("expired", s.Read(c => MobileSessions.List(c, Admin, Rider, "expired").Single().S("status")));
            Change(s, "expiresAtUtc", DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
            Assert.AreEqual(401, Assert.ThrowsExactly<ApiError>(() => MobileSessions.Authenticate(s, Context("invalid"))).Status);
            Assert.AreEqual(401, Assert.ThrowsExactly<ApiError>(() => MobileSessions.Authenticate(s, Context("rmob_" + new string('c', 64)))).Status);
            Assert.AreEqual(401, Assert.ThrowsExactly<ApiError>(() => MobileSessions.Authenticate(s, Context(), Other)).Status);
            s.Write((c, tx) => { var r = Store.Get(c, Rider, "rider", tx)!; r["status"] = "Archived"; Store.Save(c, "rider", r, tx); return true; });
            Assert.AreEqual(401, Assert.ThrowsExactly<ApiError>(() => MobileSessions.Authenticate(s, Context())).Status);
        });
    }
    [TestMethod]
    public void CompanyScopePermissionAndRequiredReasonApplyToListsAndEveryRevocation()
    {
        Run(s =>
        {
            var user = Scoped("BW"); var other = s.Read(c => MobileSessions.List(c, Admin, Other, null).Single());
            Assert.AreEqual(1, s.Read(c => MobileSessions.List(c, user, null, null).Count));
            Assert.AreEqual(0, s.Read(c => MobileSessions.List(c, user, Other, null).Count));
            Assert.AreEqual(2, s.Read(c => MobileSessions.List(c, Admin, null, null).Count));
            Assert.AreEqual(403, Assert.ThrowsExactly<ApiError>(() => s.Read(c => MobileSessions.List(c, Scoped("BW", false), null, null))).Status);
            Assert.IsFalse(AccessPolicy.Allowed(Scoped("BW", false), "GET", "/api/rider-mobile/sessions"));
            Assert.IsTrue(AccessPolicy.Allowed(user, "POST", "/api/rider-mobile/sessions/revoke-all"));
            Assert.AreEqual(404, Assert.ThrowsExactly<ApiError>(() => s.Write((c, tx) => MobileSessions.Revoke(c, tx, user, other.S("id"), Reason))).Status);
            Assert.AreEqual(404, Assert.ThrowsExactly<ApiError>(() => s.Write((c, tx) => MobileSessions.RevokeAll(c, tx, user, Other, Reason))).Status);
            Assert.ThrowsExactly<ApiError>(() => s.Write((c, tx) => MobileSessions.RevokeAll(c, tx, user, Rider, new() { ["reason"] = " " })));
            Assert.AreEqual(1, s.Write((c, tx) => MobileSessions.RevokeAll(c, tx, user, Rider, Reason)));
            Assert.AreEqual(0, s.Write((c, tx) => MobileSessions.RevokeAll(c, tx, user, Rider, Reason)));
            Assert.AreEqual(1, s.Read(c => MobileSessions.List(c, Admin, null, "active").Count));
        });
    }
    [TestMethod]
    public void SafeViewsOnlyExposeHashPrefixAndFcmFingerprintIncludingOwnSessions()
    {
        Run(s =>
        {
            var own = s.Read(c => MobileSessions.Mine(c, Context())); Assert.AreEqual(1, own.Count);
            string raw = own[0].ToJsonString(); Assert.AreEqual(16, own[0].S("id").Length);
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("synthetic-fcm-token")))[..8].ToLowerInvariant(), own[0].S("deviceFingerprint"));
            Assert.IsTrue(own[0].B("fcmRegistered"));
            foreach (string secret in new[] { Token, OtherToken, Id(Token), Id(Token)[7..], "synthetic-fcm-token", "fcmDeviceToken", "accessToken" }) Assert.IsFalse(raw.Contains(secret));
            Assert.AreEqual(401, Assert.ThrowsExactly<ApiError>(() => s.Read(c => MobileSessions.Mine(c, Context("invalid")))).Status);
        });
    }
    [TestMethod]
    public void LastSeenChangesAtMostOncePerFiveMinutesAcrossConcurrentReaders()
    {
        Run(s =>
        {
            var first = MobileSessions.Authenticate(s, Context()).S("lastSeenAt");
            Parallel.For(0, 12, _ => MobileSessions.Authenticate(s, Context()));
            Assert.AreEqual(first, s.Read(c => Store.Get(c, Id(Token), MobileSessions.Kind)!.S("lastSeenAt")));
            Change(s, "lastSeenAt", DateTimeOffset.UtcNow.AddMinutes(-6).ToString("O"));
            Parallel.For(0, 12, _ => MobileSessions.Authenticate(s, Context()));
            string updated = s.Read(c => Store.Get(c, Id(Token), MobileSessions.Kind)!.S("lastSeenAt"));
            Assert.AreNotEqual(first, updated);
            Assert.AreEqual(updated, MobileSessions.Authenticate(s, Context()).S("lastSeenAt"));
        });
    }
    [TestMethod]
    public void LogoutOnlyRevokesCurrentSessionAndCannotUseStaffCookies()
    {
        Run(s =>
        {
            s.Write((c, tx) => { MobileSessions.Logout(c, tx, Context()); return true; });
            Assert.AreEqual(401, Assert.ThrowsExactly<ApiError>(() => MobileSessions.Authenticate(s, Context())).Status);
            Assert.AreEqual(Other, MobileSessions.Authenticate(s, Context(OtherToken)).S("iqama"));
            var cookieOnly = new DefaultHttpContext { User = Admin };
            Assert.AreEqual(401, Assert.ThrowsExactly<ApiError>(() => s.Write((c, tx) => { MobileSessions.Logout(c, tx, cookieOnly); return true; })).Status);
        });
    }
}
