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
    const string DefaultTokenUri = "https://oauth2.googleapis.com/token";
    const string FcmSendUrlTemplate = "https://fcm.googleapis.com/v1/projects/{0}/messages:send";

    private static readonly HttpClient DefaultHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    // Testing hooks for offline unit tests
    public static HttpMessageHandler? TestHttpMessageHandler { get; set; }
    public static string? ServiceAccountJsonOverride { get; set; }

    public static HttpClient GetHttpClient() => TestHttpMessageHandler != null
        ? new HttpClient(TestHttpMessageHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(10) }
        : DefaultHttpClient;

    private static string? _cachedAccessToken;
    private static DateTimeOffset _tokenExpiryUtc = DateTimeOffset.MinValue;
    private static readonly object _tokenLock = new();

    public static void ResetCache()
    {
        lock (_tokenLock)
        {
            _cachedAccessToken = null;
            _tokenExpiryUtc = DateTimeOffset.MinValue;
        }
        TestHttpMessageHandler = null;
        ServiceAccountJsonOverride = null;
    }

    public static bool HasServiceAccount()
    {
        string? json = ServiceAccountJsonOverride ?? Environment.GetEnvironmentVariable("Firebase__ServiceAccountJson");
        return !string.IsNullOrWhiteSpace(json);
    }

    public static string? GetServiceAccountJson()
    {
        return ServiceAccountJsonOverride ?? Environment.GetEnvironmentVariable("Firebase__ServiceAccountJson");
    }

    public static JsonObject Enqueue(DbConnection c, DbTransaction tx, string iqama, string title, string body, string? link = null)
    {
        J.Iqama(iqama);
        var item = new JsonObject
        {
            ["id"] = "pout_" + Guid.NewGuid().ToString("N"),
            ["iqama"] = iqama,
            ["title"] = title,
            ["body"] = body,
            ["link"] = link ?? "",
            ["status"] = "pending",
            ["attempts"] = 0,
            ["lastError"] = "",
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O")
        };
        Store.Save(c, Kind, item, tx);
        return item;
    }

    public static List<JsonObject> EnqueueBroadcast(DbConnection c, DbTransaction tx, string broadcastId, string targetIqama, string title, string body, string? link = null)
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
                var item = Enqueue(c, tx, iqama, title, body, link);
                item["broadcastId"] = broadcastId;
                Store.Save(c, Kind, item, tx);
                enqueued.Add(item);
            }
        }
        else
        {
            var item = Enqueue(c, tx, J.Iqama(targetIqama), title, body, link);
            item["broadcastId"] = broadcastId;
            Store.Save(c, Kind, item, tx);
            enqueued.Add(item);
        }
        return enqueued;
    }

    public static async Task<int> FlushOutboxAsync(Store store)
    {
        var pendingItems = store.Read(c =>
            Store.List(c, Kind)
                .Where(x => x.S("status") == "pending" && x.N("attempts") < 3)
                .OrderBy(x => x.S("createdAt"))
                .Take(50)
                .ToList());

        if (pendingItems.Count == 0) return 0;

        int processed = 0;
        foreach (var item in pendingItems)
        {
            await ProcessItemAsync(store, item);
            processed++;
        }
        return processed;
    }

    public static void FlushOutbox(Store store)
    {
        try
        {
            Task.Run(() => FlushOutboxAsync(store)).GetAwaiter().GetResult();
        }
        catch
        {
            // Background push errors do not roll back callers
        }
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

        // Find active device token for the rider
        var (token, sessionId) = store.Read<(string?, string?)>(c =>
        {
            var sessions = Store.List(c, MobileSessions.Kind)
                .Where(s => s.S("iqama") == iqama && MobileSessions.Status(s) == "active" && !string.IsNullOrWhiteSpace(s.S("fcmDeviceToken")))
                .OrderByDescending(s => s.S("lastSeenAt"))
                .ToList();
            if (sessions.Count == 0) return (null, null);
            return (sessions[0].S("fcmDeviceToken"), sessions[0].S("id"));
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

            var fcmMessage = new JsonObject
            {
                ["message"] = new JsonObject
                {
                    ["token"] = token,
                    ["notification"] = new JsonObject
                    {
                        ["title"] = item.S("title"),
                        ["body"] = item.S("body")
                    },
                    ["data"] = new JsonObject
                    {
                        ["link"] = item.S("link"),
                        ["id"] = id
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
                        row["status"] = isUnregistered || attempts >= 3 ? "failed" : "pending";
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
                    row["status"] = attempts >= 3 ? "failed" : "pending";
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

    public static void Map(WebApplication app, RouteGroupBuilder api, Store store)
    {
        // 1. Device token update for rider's active bearer session
        app.MapPost("/api/rider-mobile/device-token", (JsonObject input, HttpContext ctx) =>
        {
            MobileSessions.Authenticate(store, ctx);
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
    }
}
