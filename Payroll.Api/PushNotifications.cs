using System.Collections.Concurrent;
using System.Data.Common;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Payroll;

public static class PushNotifications
{
    public const string Kind = "push_outbox";
    public const int OutboxBatchSize = 50;
    public const int MaxPortalBroadcasts = 50;
    public const int MaxPortalDebts = 100;
    public const int MaxRiderListItems = 100;
    public const int DefaultReadLimitPerMinute = 60;
    public const int DefaultWriteLimitPerMinute = 20;

    // Individual-only notices that can NEVER be broadcast to ALL riders (Requirement 5b).
    public static readonly string[] IndividualOnlyNoticeCategories =
    [
        "summons",
        "dismissal_warning",
        "administrative_deduction",
        "appointment_alert",
        "administrative_warning"
    ];

    public static bool IsIndividualOnlyNotice(string? category) =>
        !string.IsNullOrWhiteSpace(category) &&
        IndividualOnlyNoticeCategories.Contains(category.Trim(), StringComparer.OrdinalIgnoreCase);

    public static (string Ar, string En, string Ur) DefaultNoticeTitles(string category) =>
        category.Trim().ToLowerInvariant() switch
        {
            "summons" => ("استدعاء إداري", "Administrative Summons", "انتظامی طلب نامہ"),
            "dismissal_warning" => ("إنذار فصل", "Dismissal Warning", "برطرفی کی وارننگ"),
            "administrative_deduction" => ("إشعار خصم إداري", "Administrative Deduction Notice", "انتظامی کٹوتی کی اطلاع"),
            "appointment_alert" => ("تنبيه موعد مراجعة", "Appointment Alert", "ملاقات کی اطلاع"),
            "administrative_warning" => ("إنذار إداري", "Administrative Warning", "انتظامی وارننگ"),
            "financial" => ("إشعار مالي", "Financial Notice", "مالیاتی اطلاع"),
            "form_link" => ("نموذج مطلوب تعبئته", "Form Required", "فارم پُر کرنے کی اطلاع"),
            "administrative" => ("تعميم إداري", "Administrative Notice", "انتظامی اعلامیہ"),
            _ => ("إشعار تشغيلي", "Operational Notice", "آپریشنل اطلاع")
        };

    const string DefaultTokenUri = "https://oauth2.googleapis.com/token";
    const string FcmSendUrlTemplate = "https://fcm.googleapis.com/v1/projects/{0}/messages:send";

    private static readonly HttpClient DefaultHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    // Testing hooks for offline unit tests
    public static HttpMessageHandler? TestHttpMessageHandler { get; set; }
    public static string? ServiceAccountJsonOverride { get; set; }
    public static int? ReadPermitLimitOverride { get; set; }
    public static int? WritePermitLimitOverride { get; set; }
    public static int? IpReadPermitLimitOverride { get; set; }
    public static int? IpWritePermitLimitOverride { get; set; }
    public static Func<DateTimeOffset>? ClockOverride { get; set; }

    public const int DefaultIpReadLimitPerMinute = 180;
    public const int DefaultIpWriteLimitPerMinute = 60;
    /// <summary>Live per-IP read limit (config/env); defaults unchanged. Test overrides still win when set.</summary>
    public static int IpReadLimitPerMinute { get; set; } = DefaultIpReadLimitPerMinute;
    /// <summary>Live per-IP write limit (config/env); defaults unchanged. Test overrides still win when set.</summary>
    public static int IpWriteLimitPerMinute { get; set; } = DefaultIpWriteLimitPerMinute;

    private static readonly ConcurrentDictionary<string, (long WindowMinute, int Count)> _rateWindows = new();
    private static readonly ConcurrentDictionary<long, ConcurrentDictionary<string, byte>> _keysByMinute = new();
    private static long _lastEvictionMinute;
    public static int RateWindowBucketCount => _rateWindows.Count;
    public static int LastEvictionRemovedCount { get; private set; }

    public static HttpClient GetHttpClient() => TestHttpMessageHandler != null
        ? new HttpClient(TestHttpMessageHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(10) }
        : DefaultHttpClient;

    private static string? _cachedAccessToken;
    private static DateTimeOffset _tokenExpiryUtc = DateTimeOffset.MinValue;
    private static readonly object _tokenLock = new();
    private static int _isFlushing = 0;

    public static void ResetCache()
    {
        lock (_tokenLock)
        {
            _cachedAccessToken = null;
            _tokenExpiryUtc = DateTimeOffset.MinValue;
        }
        TestHttpMessageHandler = null;
        ServiceAccountJsonOverride = null;
        ReadPermitLimitOverride = null;
        WritePermitLimitOverride = null;
        IpReadPermitLimitOverride = null;
        IpWritePermitLimitOverride = null;
        IpReadLimitPerMinute = DefaultIpReadLimitPerMinute;
        IpWriteLimitPerMinute = DefaultIpWriteLimitPerMinute;
        ClockOverride = null;
        _rateWindows.Clear();
        _keysByMinute.Clear();
        LastEvictionRemovedCount = 0;
        Interlocked.Exchange(ref _lastEvictionMinute, 0);
        Interlocked.Exchange(ref _isFlushing, 0);
    }

    /// <summary>
    /// Applies RiderMobile IP rate-limit settings. Invalid values fall back to defaults and
    /// return warning messages (never throws / never crashes startup).
    /// </summary>
    public static IReadOnlyList<string> ConfigureIpLimits(IConfiguration config)
    {
        var warnings = new List<string>();
        IpReadLimitPerMinute = ParseLimit(
            config["RiderMobile:IpReadLimitPerMinute"],
            DefaultIpReadLimitPerMinute,
            "RiderMobile:IpReadLimitPerMinute",
            warnings);
        IpWriteLimitPerMinute = ParseLimit(
            config["RiderMobile:IpWriteLimitPerMinute"],
            DefaultIpWriteLimitPerMinute,
            "RiderMobile:IpWriteLimitPerMinute",
            warnings);
        return warnings;
    }

    static int ParseLimit(string? raw, int fallback, string key, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        if (!int.TryParse(raw.Trim(), out int value) || value < 1 || value > 100_000)
        {
            warnings.Add($"Invalid {key} value; using default {fallback}.");
            return fallback;
        }
        return value;
    }

    /// <summary>Evict only expired minute buckets (O(expired keys)), never a full dictionary scan.</summary>
    static void EvictStaleRateWindows(long currentMinute)
    {
        long prev = Interlocked.Read(ref _lastEvictionMinute);
        if (currentMinute <= prev) return;
        if (Interlocked.CompareExchange(ref _lastEvictionMinute, currentMinute, prev) != prev) return;
        int removed = 0;
        foreach (var minute in _keysByMinute.Keys)
        {
            if (minute >= currentMinute) continue;
            if (!_keysByMinute.TryRemove(minute, out var keys)) continue;
            foreach (var key in keys.Keys)
            {
                if (_rateWindows.TryRemove(key, out _)) removed++;
            }
        }
        LastEvictionRemovedCount = removed;
    }

    static void EnforceBucket(HttpContext ctx, string bucketKey, int limit)
    {
        long currentMinute = (ClockOverride?.Invoke() ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 60;
        EvictStaleRateWindows(currentMinute);
        var updated = _rateWindows.AddOrUpdate(
            bucketKey,
            _ => (currentMinute, 1),
            (_, existing) => existing.WindowMinute == currentMinute ? (currentMinute, existing.Count + 1) : (currentMinute, 1));
        _keysByMinute.GetOrAdd(currentMinute, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))
            .TryAdd(bucketKey, 0);
        if (updated.Count > limit)
        {
            ctx.Response.Headers.RetryAfter = "60";
            throw new ApiError("طلبات كثيرة؛ حاول بعد قليل.", 429);
        }
    }

    // Per-IP check BEFORE auth: never trusts unauthenticated Authorization headers as bucket keys.
    public static void EnforceRiderIpRateLimit(HttpContext ctx, bool write)
    {
        string ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";
        int limit = write
            ? (IpWritePermitLimitOverride ?? IpWriteLimitPerMinute)
            : (IpReadPermitLimitOverride ?? IpReadLimitPerMinute);
        EnforceBucket(ctx, (write ? "IP:W:" : "IP:R:") + ip, limit);
    }

    // Per-session check AFTER auth: enforces per-rider quota on the authenticated session id.
    public static void EnforceRiderSessionRateLimit(HttpContext ctx, JsonObject session, bool write)
    {
        string sessionKey = session.S("id");
        J.Need(!string.IsNullOrWhiteSpace(sessionKey), "جلسة المندوب غير صالحة.", 401);
        int limit = write
            ? (WritePermitLimitOverride ?? DefaultWriteLimitPerMinute)
            : (ReadPermitLimitOverride ?? DefaultReadLimitPerMinute);
        EnforceBucket(ctx, (write ? "S:W:" : "S:R:") + sessionKey, limit);
    }

    public static JsonObject AuthenticateAndRateLimit(Store store, HttpContext ctx, bool write, string? iqama = null)
    {
        EnforceRiderIpRateLimit(ctx, write);
        var session = MobileSessions.Authenticate(store, ctx, iqama);
        EnforceRiderSessionRateLimit(ctx, session, write);
        return session;
    }

    public static void EnforceRiderRateLimit(HttpContext ctx, bool write) => EnforceRiderIpRateLimit(ctx, write);

    public static bool HasServiceAccount()
    {
        string? json = ServiceAccountJsonOverride ?? Environment.GetEnvironmentVariable("Firebase__ServiceAccountJson");
        return !string.IsNullOrWhiteSpace(json);
    }

    public static string? GetServiceAccountJson()
    {
        return ServiceAccountJsonOverride ?? Environment.GetEnvironmentVariable("Firebase__ServiceAccountJson");
    }

    public static (string Title, string Body) ResolveLocalizedText(JsonObject item, string? language)
    {
        string lang = (language ?? "ar").Trim().ToLowerInvariant();
        string title = lang switch
        {
            "en" => item.S("titleEn").Trim() is { Length: > 0 } te ? te : item.S("titleAr", item.S("title")),
            "ur" => item.S("titleUr").Trim() is { Length: > 0 } tu ? tu : item.S("titleAr", item.S("title")),
            _ => item.S("titleAr", item.S("title"))
        };
        string body = lang switch
        {
            "en" => item.S("bodyEn").Trim() is { Length: > 0 } be ? be : item.S("bodyAr", item.S("body")),
            "ur" => item.S("bodyUr").Trim() is { Length: > 0 } bu ? bu : item.S("bodyAr", item.S("body")),
            _ => item.S("bodyAr", item.S("body"))
        };
        return (title, body);
    }

    public static JsonObject Enqueue(DbConnection c, DbTransaction tx, string iqama, string title, string body, string? link = null,
        string? titleEn = null, string? titleUr = null, string? bodyEn = null, string? bodyUr = null)
    {
        J.Iqama(iqama);
        var item = new JsonObject
        {
            ["id"] = "pout_" + Guid.NewGuid().ToString("N"),
            ["iqama"] = iqama,
            ["title"] = title,
            ["titleAr"] = title,
            ["titleEn"] = string.IsNullOrWhiteSpace(titleEn) ? title : titleEn,
            ["titleUr"] = string.IsNullOrWhiteSpace(titleUr) ? title : titleUr,
            ["body"] = body,
            ["bodyAr"] = body,
            ["bodyEn"] = string.IsNullOrWhiteSpace(bodyEn) ? body : bodyEn,
            ["bodyUr"] = string.IsNullOrWhiteSpace(bodyUr) ? body : bodyUr,
            ["link"] = link ?? "",
            ["status"] = "pending",
            ["attempts"] = 0,
            ["nextAttemptAt"] = "",
            ["lastError"] = "",
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O")
        };
        Store.Save(c, Kind, item, tx);
        return item;
    }

    public static List<JsonObject> EnqueueBroadcast(DbConnection c, DbTransaction tx, string broadcastId, string targetIqama, string title, string body, string? link = null,
        string? titleEn = null, string? titleUr = null, string? bodyEn = null, string? bodyUr = null)
    {
        var enqueued = new List<JsonObject>();
        if (targetIqama == "ALL")
        {
            // Find all riders that have active sessions with a non-empty fcmDeviceToken
            var sessions = Store.List(c, MobileSessions.Kind, tx);
            var activeRidersWithToken = sessions
                .Where(s => MobileSessions.Status(s) == "active" && !string.IsNullOrWhiteSpace(s.S("fcmDeviceToken")))
                .Select(s => s.S("iqama"))
                .Distinct()
                .ToList();

            foreach (var iqama in activeRidersWithToken)
            {
                var item = Enqueue(c, tx, iqama, title, body, link, titleEn, titleUr, bodyEn, bodyUr);
                item["broadcastId"] = broadcastId;
                Store.Save(c, Kind, item, tx);
                enqueued.Add(item);
            }
        }
        else
        {
            var item = Enqueue(c, tx, J.Iqama(targetIqama), title, body, link, titleEn, titleUr, bodyEn, bodyUr);
            item["broadcastId"] = broadcastId;
            Store.Save(c, Kind, item, tx);
            enqueued.Add(item);
        }
        return enqueued;
    }

    public static async Task<int> FlushOutboxAsync(Store store)
    {
        if (Interlocked.CompareExchange(ref _isFlushing, 1, 0) != 0)
        {
            return 0;
        }

        try
        {
            int totalProcessed = 0;
            while (true)
            {
                var nowIso = DateTimeOffset.UtcNow.ToString("O");
                var pendingBatch = store.Read(c =>
                    Store.ListWhere(c, Kind, null, null, ("status", "pending"))
                        .Where(x => x.N("attempts") < 3 &&
                                    (string.IsNullOrEmpty(x.S("nextAttemptAt")) || string.CompareOrdinal(x.S("nextAttemptAt"), nowIso) <= 0))
                        .OrderBy(x => x.S("createdAt"))
                        .Take(OutboxBatchSize)
                        .ToList());

                if (pendingBatch.Count == 0) break;

                foreach (var item in pendingBatch)
                {
                    await ProcessItemAsync(store, item);
                    totalProcessed++;
                }
            }
            return totalProcessed;
        }
        finally
        {
            Interlocked.Exchange(ref _isFlushing, 0);
        }
    }

    public static void FlushOutbox(Store store)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await FlushOutboxAsync(store);
            }
            catch
            {
                // Background push errors do not roll back callers
            }
        });
    }

    private static async Task ProcessItemAsync(Store store, JsonObject item)
    {
        string id = item.S("id");
        string iqama = item.S("iqama");
        int attempts = (int)item.N("attempts") + 1;
        item["attempts"] = attempts;
        item["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");

        string? saJson = GetServiceAccountJson();
        if (string.IsNullOrWhiteSpace(saJson))
        {
            store.Write((c, tx) =>
            {
                var row = Store.Get(c, id, Kind, tx);
                if (row == null) return false;
                row["status"] = "failed";
                row["attempts"] = attempts;
                row["lastError"] = "لم يُضبط Firebase";
                row["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
                Store.Save(c, Kind, row, tx);
                return true;
            }, false);
            return;
        }

        // Find active device token and session language for the rider
        var (token, sessionId, language) = store.Read<(string?, string?, string)>(c =>
        {
            var sessions = Store.ListWhere(c, MobileSessions.Kind, null, null, ("iqama", iqama))
                .Where(s => MobileSessions.Status(s) == "active" && !string.IsNullOrWhiteSpace(s.S("fcmDeviceToken")))
                .OrderByDescending(s => s.S("lastSeenAt"))
                .ToList();
            if (sessions.Count == 0) return (null, null, "ar");
            string lang = sessions[0].S("language", "ar").Trim().ToLowerInvariant();
            if (lang is not ("ar" or "en" or "ur")) lang = "ar";
            return (sessions[0].S("fcmDeviceToken"), sessions[0].S("id"), lang);
        });

        if (string.IsNullOrWhiteSpace(token))
        {
            store.Write((c, tx) =>
            {
                var row = Store.Get(c, id, Kind, tx);
                if (row == null) return false;
                row["status"] = "failed";
                row["attempts"] = attempts;
                row["lastError"] = "لا يوجد رمز جهاز نشط للمندوب";
                row["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
                Store.Save(c, Kind, row, tx);
                return true;
            }, false);
            return;
        }

        HttpClient client = GetHttpClient();

        try
        {
            var sa = JsonSerializer.Deserialize<JsonObject>(saJson);
            string projectId = sa?.S("project_id") ?? "";
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new InvalidOperationException("project_id مفقود في مفتاح الخدمة.");
            }

            string accessToken = await GetAccessTokenAsync(client, sa!);

            var (localizedTitle, localizedBody) = ResolveLocalizedText(item, language);
            var fcmMessage = new JsonObject
            {
                ["message"] = new JsonObject
                {
                    ["token"] = token,
                    ["notification"] = new JsonObject
                    {
                        ["title"] = localizedTitle,
                        ["body"] = localizedBody
                    },
                    ["data"] = new JsonObject
                    {
                        ["link"] = item.S("link"),
                        ["id"] = id,
                        ["language"] = language
                    },
                    ["android"] = new JsonObject
                    {
                        ["priority"] = "high",
                        ["notification"] = new JsonObject
                        {
                            ["channel_id"] = "default",
                            ["click_action"] = "OPEN_APP"
                        }
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, string.Format(FcmSendUrlTemplate, projectId))
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken) },
                Content = new StringContent(fcmMessage.ToJsonString(), Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(request);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                store.Write((c, tx) =>
                {
                    var row = Store.Get(c, id, Kind, tx);
                    if (row == null) return false;
                    row["status"] = "sent";
                    row["attempts"] = attempts;
                    row["sentLanguage"] = language;
                    row["nextAttemptAt"] = "";
                    row["sentAt"] = DateTimeOffset.UtcNow.ToString("O");
                    row["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
                    Store.Save(c, Kind, row, tx);
                    return true;
                }, false);
            }
            else
            {
                bool isUnregistered = responseBody.Contains("UNREGISTERED") || response.StatusCode == System.Net.HttpStatusCode.NotFound;

                store.Write((c, tx) =>
                {
                    var row = Store.Get(c, id, Kind, tx);
                    if (row != null)
                    {
                        row["attempts"] = attempts;
                        if (isUnregistered || attempts >= 3)
                        {
                            row["status"] = "failed";
                            row["nextAttemptAt"] = "";
                        }
                        else
                        {
                            row["status"] = "pending";
                            var delay = attempts == 1 ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(5);
                            row["nextAttemptAt"] = DateTimeOffset.UtcNow.Add(delay).ToString("O");
                        }
                        row["lastError"] = isUnregistered ? "UNREGISTERED" : $"FCM error {response.StatusCode}";
                        row["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
                        Store.Save(c, Kind, row, tx);
                    }

                    // If token is UNREGISTERED, clear token from that session
                    if (isUnregistered && !string.IsNullOrEmpty(sessionId))
                    {
                        var session = Store.Get(c, sessionId, MobileSessions.Kind, tx);
                        if (session != null)
                        {
                            session["fcmDeviceToken"] = "";
                            session["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
                            Store.Save(c, MobileSessions.Kind, session, tx);
                            string pubId = session.S("id").StartsWith("mobile:") && session.S("id").Length >= "mobile:".Length + 16
                                ? MobileSessions.PublicId(session)
                                : session.S("id");
                            Store.Audit(c, tx, "mobile.fcm_unregistered", pubId, "system");
                        }
                    }
                    return true;
                }, false);
            }
        }
        catch (Exception ex)
        {
            store.Write((c, tx) =>
            {
                var row = Store.Get(c, id, Kind, tx);
                if (row != null)
                {
                    row["attempts"] = attempts;
                    if (attempts >= 3)
                    {
                        row["status"] = "failed";
                        row["nextAttemptAt"] = "";
                    }
                    else
                    {
                        row["status"] = "pending";
                        var delay = attempts == 1 ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(5);
                        row["nextAttemptAt"] = DateTimeOffset.UtcNow.Add(delay).ToString("O");
                    }
                    row["lastError"] = ex.Message;
                    row["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
                    Store.Save(c, Kind, row, tx);
                }
                return true;
            }, false);
        }
        finally
        {
            if (TestHttpMessageHandler != null)
            {
                client.Dispose();
            }
        }
    }

    public static async Task<string> GetAccessTokenAsync(HttpClient client, JsonObject sa)
    {
        lock (_tokenLock)
        {
            if (!string.IsNullOrEmpty(_cachedAccessToken) && DateTimeOffset.UtcNow < _tokenExpiryUtc.AddMinutes(-5))
            {
                return _cachedAccessToken;
            }
        }

        string clientEmail = sa.Required("client_email");
        string privateKeyPem = sa.Required("private_key");
        string tokenUri = sa.S("token_uri", DefaultTokenUri);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = new { alg = "RS256", typ = "JWT" };
        var payload = new
        {
            iss = clientEmail,
            sub = clientEmail,
            aud = tokenUri,
            scope = "https://www.googleapis.com/auth/firebase.messaging",
            iat = now,
            exp = now + 3600
        };

        string headerB64 = Base64Url(JsonSerializer.Serialize(header));
        string payloadB64 = Base64Url(JsonSerializer.Serialize(payload));
        string dataToSign = $"{headerB64}.{payloadB64}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        byte[] signature = rsa.SignData(Encoding.UTF8.GetBytes(dataToSign), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        string assertion = $"{dataToSign}.{Base64Url(signature)}";

        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenUri)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion
            })
        };

        var response = await client.SendAsync(tokenRequest);
        string resText = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"فشل الحصول على توكن OAuth2 من جوجل: {response.StatusCode} - {resText}");
        }

        var resDoc = JsonSerializer.Deserialize<JsonObject>(resText) ?? throw new InvalidOperationException("استجابة غير صالحة من خادم التوكن.");
        string accessToken = resDoc.Required("access_token");
        int expiresIn = (int)resDoc.N("expires_in");
        if (expiresIn <= 0) expiresIn = 3600;

        lock (_tokenLock)
        {
            _cachedAccessToken = accessToken;
            _tokenExpiryUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        }

        return accessToken;
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Base64Url(string text)
    {
        return Base64Url(Encoding.UTF8.GetBytes(text));
    }

    public static JsonObject NotifyApprovedStatementsForPeriod(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string period)
    {
        AccessPolicy.Demand(actor, "mobile.broadcast");
        period = J.Period(period);
        var lineRiders = Store.ListWhere(c, PayCycles.LineKind, tx, null, ("period", period))
            .GroupBy(l => l.S("cycleId"))
            .ToDictionary(g => g.Key, g => g.Select(x => x.S("rider")).ToHashSet(StringComparer.Ordinal));

        var cycleRiders = Store.ListWhere(c, PayCycles.Kind, tx, null, ("period", period))
            .Where(d => d.S("status") is not ("cancelled" or "reversed") && PayCycles.Visible(actor, d))
            .SelectMany(d =>
            {
                if (!lineRiders.TryGetValue(d.S("id"), out var rSet)) return Enumerable.Empty<string>();
                if (d.S("status") == "approved") return rSet;
                if (d.S("status") == "partially_approved")
                {
                    return (d["riders"] as JsonArray ?? []).OfType<JsonObject>()
                        .Where(r => r.S("salaryApprovedAt") != "" && rSet.Contains(r.S("rider")))
                        .Select(r => r.S("rider"));
                }
                return Enumerable.Empty<string>();
            });

        var legacyRiders = Store.ListWhere(c, "run", tx, null, ("period", period))
            .Where(r => r.S("approvedAt") != "" && FinancialScopes.Ledger(actor, r))
            .Select(r => r.S("iqama"));

        var targetRiders = cycleRiders.Concat(legacyRiders)
            .Where(iq => !string.IsNullOrWhiteSpace(iq))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(iq => iq, StringComparer.Ordinal)
            .ToList();

        var existingBroadcastIds = Store.ListIdsByPrefix(c, "mobile_broadcast", $"stmt_notice:{period}:", tx);

        int notifiedCount = 0;
        int enqueuedCount = 0;
        int skippedCount = 0;
        string actorName = actor.Identity?.Name ?? "administrator";

        foreach (string iqama in targetRiders)
        {
            string noticeId = $"stmt_notice:{period}:{iqama}";
            if (existingBroadcastIds.Contains(noticeId))
            {
                skippedCount++;
                continue;
            }

            string titleAr = $"كشف الحساب المعتمد لشهر {period}";
            string titleEn = $"Approved Statement for {period}";
            string titleUr = $"ماہ {period} کا منظور شدہ بیان";
            string bodyAr = "صدر كشف راتبك المعتمد لهذا الشهر. افتح التطبيق للاطلاع على البنود والرصيد النهائي.";
            string bodyEn = "Your approved monthly statement is ready. Open the app to view the items and final balance.";
            string bodyUr = "اس مہینے کا منظور شدہ بیان تیار ہے۔ تفصیلات اور حتمی بیلنس دیکھنے کے لیے ایپ کھولیں۔";
            string link = $"/rider-app/statement?period={period}";

            var notice = new JsonObject
            {
                ["id"] = noticeId,
                ["kind"] = "statement_ready",
                ["category"] = "financial",
                ["titleAr"] = titleAr,
                ["titleEn"] = titleEn,
                ["titleUr"] = titleUr,
                ["bodyAr"] = bodyAr,
                ["bodyEn"] = bodyEn,
                ["bodyUr"] = bodyUr,
                ["targetIqama"] = iqama,
                ["actionUrl"] = link,
                ["requiresAck"] = false,
                ["deliveryChannel"] = "in_app",
                ["deliveryStatus"] = "stored",
                ["acknowledgedBy"] = new JsonArray(),
                ["readReceipts"] = new JsonArray(),
                ["sentBy"] = actorName,
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
            };
            Store.Save(c, "mobile_broadcast", notice, tx);
            var enqueued = EnqueueBroadcast(c, tx, noticeId, iqama, titleAr, bodyAr, link, titleEn, titleUr, bodyEn, bodyUr);
            notifiedCount++;
            enqueuedCount += enqueued.Count;
        }

        Store.Audit(c, tx, "mobile.statements_notified", period, actorName);
        return new JsonObject
        {
            ["ok"] = true,
            ["period"] = period,
            ["notifiedRidersCount"] = notifiedCount,
            ["enqueuedPushCount"] = enqueuedCount,
            ["skippedAlreadyNotifiedCount"] = skippedCount,
            ["batchSize"] = OutboxBatchSize
        };
    }

    public static void Map(WebApplication app, RouteGroupBuilder api, Store store)
    {
        // 1. Device token update for rider's active bearer session
        app.MapPost("/api/rider-mobile/device-token", (JsonObject input, HttpContext ctx) =>
        {
            AuthenticateAndRateLimit(store, ctx, write: true);
            return store.Write((c, tx) =>
            {
                var session = MobileSessions.Validate(c, ctx, tx: tx);
                string fcm = input.S("fcmDeviceToken").Trim();
                J.Need(fcm.Length <= 4096, "رمز جهاز طويل جدًا.");
                session["fcmDeviceToken"] = fcm;
                session["lastSeenAt"] = DateTimeOffset.UtcNow.ToString("O");
                Store.Save(c, MobileSessions.Kind, session, tx);
                string pubId = session.S("id").StartsWith("mobile:") && session.S("id").Length >= "mobile:".Length + 16
                    ? MobileSessions.PublicId(session)
                    : session.S("id");
                Store.Audit(c, tx, "mobile.device_token_updated", pubId, "rider:" + session.S("iqama"));
                return new { ok = true, fcmRegistered = fcm.Length > 0 };
            }, false);
        }).WithTags("Riders").WithSummary("Update FCM device token for current mobile session");

        // 2. Language switch for rider's active bearer session (ar / en / ur)
        app.MapPost("/api/rider-mobile/language", (JsonObject input, HttpContext ctx) =>
        {
            AuthenticateAndRateLimit(store, ctx, write: true);
            return store.Write((c, tx) =>
            {
                var session = MobileSessions.Validate(c, ctx, tx: tx);
                string lang = input.Required("language").Trim().ToLowerInvariant();
                J.Need(lang is "ar" or "en" or "ur", "اللغة المدعومة: العربية (ar) أو الإنجليزية (en) أو الأردية (ur).");
                session["language"] = lang;
                session["lastSeenAt"] = DateTimeOffset.UtcNow.ToString("O");
                Store.Save(c, MobileSessions.Kind, session, tx);
                return new { ok = true, language = lang };
            }, false);
        }).WithTags("Riders").WithSummary("Switch rider mobile session language (ar, en, ur)");

        // 3. Bulk statement notification via the push outbox in batches (never sends salary amounts in FCM payload)
        api.MapPost("/rider-mobile/statements/notify-period", (JsonObject input, HttpContext ctx) =>
        {
            var res = store.Write((c, tx) => NotifyApprovedStatementsForPeriod(c, tx, ctx.User, input.Required("period")));
            FlushOutbox(store);
            return res;
        }).WithTags("Riders").WithSummary("Enqueue batched statement-ready notifications for riders with approved statements in a period");
    }
}
