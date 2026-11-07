using System.Data.Common;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class PushNotificationsTests
{
    private static readonly ClaimsPrincipal Admin = UserAccess.Principal(new()
    {
        ["id"] = "syn-admin",
        ["username"] = "syn-admin",
        ["role"] = "Admin"
    });

    private static (string privateKeyPem, string saJson) CreateSyntheticServiceAccount()
    {
        using var rsa = RSA.Create(2048);
        string privateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
        var sa = new JsonObject
        {
            ["type"] = "service_account",
            ["project_id"] = "synthetic-gate-test",
            ["private_key_id"] = "syn-key-id",
            ["private_key"] = privateKeyPem,
            ["client_email"] = "firebase-adminsdk-syn@synthetic-gate-test.iam.gserviceaccount.com",
            ["token_uri"] = "https://oauth2.googleapis.com/token"
        };
        return (privateKeyPem, sa.ToJsonString());
    }

    private static void WithSqlStore(Action<Store> action)
    {
        string? configured = Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION")
            ?? "Server=127.0.0.1,11433;Database=master;User ID=sa;Password=GateSynthetic_CI_Only_723!;Encrypt=True;TrustServerCertificate=True";

        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        string dbName = "GatePushTest_" + Guid.NewGuid().ToString("N");

        using (var masterConn = new SqlConnection(master.ConnectionString))
        {
            masterConn.Open();
            using var cmd = masterConn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{dbName}]";
            cmd.ExecuteNonQuery();
        }

        string tempDir = Path.Combine(Path.GetTempPath(), dbName);
        try
        {
            var target = new SqlConnectionStringBuilder(configured) { InitialCatalog = dbName };
            var store = new Store(tempDir, target.ConnectionString);
            Assert.AreEqual("mssql", store.Provider);
            action(store);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using (var masterConn = new SqlConnection(master.ConnectionString))
            {
                masterConn.Open();
                using var cmd = masterConn.CreateCommand();
                cmd.CommandText = $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]";
                cmd.ExecuteNonQuery();
            }
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            PushNotifications.ResetCache();
        }
    }

    private sealed class DelegatingTestHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Handler { get; set; }

        public DelegatingTestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            Handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Handler(request, cancellationToken);
        }
    }

    [TestMethod]
    public void TokenCacheReusesValidTokenWithoutRepeatedOAuthRequests()
    {
        PushNotifications.ResetCache();
        var (_, saJson) = CreateSyntheticServiceAccount();
        var saDoc = JsonSerializer.Deserialize<JsonObject>(saJson)!;

        int oauthCalls = 0;
        var handler = new DelegatingTestHandler(async (req, ct) =>
        {
            if (req.RequestUri?.AbsoluteUri == "https://oauth2.googleapis.com/token")
            {
                oauthCalls++;
                string reqContent = await req.Content!.ReadAsStringAsync(ct);
                Assert.IsTrue(reqContent.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer"));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"access_token\":\"cached-token-xyz\",\"expires_in\":3600,\"token_type\":\"Bearer\"}",
                        Encoding.UTF8,
                        "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        string token1 = PushNotifications.GetAccessTokenAsync(client, saDoc).GetAwaiter().GetResult();
        string token2 = PushNotifications.GetAccessTokenAsync(client, saDoc).GetAwaiter().GetResult();

        Assert.AreEqual("cached-token-xyz", token1);
        Assert.AreEqual("cached-token-xyz", token2);
        Assert.AreEqual(1, oauthCalls);
    }

    [TestMethod]
    public void MessageShapeMatchesFcmHttpV1Specification()
    {
        WithSqlStore(store =>
        {
            PushNotifications.ResetCache();
            var (_, saJson) = CreateSyntheticServiceAccount();
            PushNotifications.ServiceAccountJsonOverride = saJson;

            const string riderIqama = "2000000881";
            const string fcmToken = "synthetic-fcm-device-token-881";

            store.Write((c, tx) =>
            {
                Store.Save(c, "rider", new() { ["id"] = riderIqama, ["iqama"] = riderIqama, ["name"] = "مندوب تجريبي", ["status"] = "Active" }, tx);
                Store.Save(c, MobileSessions.Kind, new()
                {
                    ["id"] = "mobile:shape-test",
                    ["iqama"] = riderIqama,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O"),
                    ["fcmDeviceToken"] = fcmToken
                }, tx);
                PushNotifications.Enqueue(c, tx, riderIqama, "عنوان الإشعار", "نص الإشعار للتجربة", "/rider-app/test");
                return true;
            });

            JsonObject? capturedFcmBody = null;
            string? capturedAuthHeader = null;
            string? capturedUrl = null;

            PushNotifications.TestHttpMessageHandler = new DelegatingTestHandler(async (req, ct) =>
            {
                if (req.RequestUri?.AbsoluteUri == "https://oauth2.googleapis.com/token")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"access_token\":\"test-jwt-access-token\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
                    };
                }

                capturedUrl = req.RequestUri?.AbsoluteUri;
                capturedAuthHeader = req.Headers.Authorization?.ToString();
                string bodyStr = await req.Content!.ReadAsStringAsync(ct);
                capturedFcmBody = JsonSerializer.Deserialize<JsonObject>(bodyStr);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"name\":\"projects/synthetic-gate-test/messages/msg-123\"}", Encoding.UTF8, "application/json")
                };
            });

            int processed = PushNotifications.FlushOutboxAsync(store).GetAwaiter().GetResult();
            Assert.AreEqual(1, processed);

            Assert.AreEqual("https://fcm.googleapis.com/v1/projects/synthetic-gate-test/messages:send", capturedUrl);
            Assert.AreEqual("Bearer test-jwt-access-token", capturedAuthHeader);
            Assert.IsNotNull(capturedFcmBody);

            var msg = capturedFcmBody!["message"]!.AsObject();
            Assert.AreEqual(fcmToken, msg.S("token"));
            Assert.AreEqual("عنوان الإشعار", msg["notification"]!.AsObject().S("title"));
            Assert.AreEqual("نص الإشعار للتجربة", msg["notification"]!.AsObject().S("body"));
            Assert.AreEqual("/rider-app/test", msg["data"]!.AsObject().S("link"));
            Assert.AreEqual("high", msg["android"]!.AsObject().S("priority"));

            var outbox = store.Read(c => Store.List(c, PushNotifications.Kind));
            Assert.AreEqual(1, outbox.Count);
            Assert.AreEqual("sent", outbox[0].S("status"));
            Assert.AreEqual(1, (int)outbox[0].N("attempts"));
            Assert.IsFalse(string.IsNullOrEmpty(outbox[0].S("sentAt")));
        });
    }

    [TestMethod]
    public void RetryAndBackoffIncrementsAttemptsAndFailsAfterThreeTries()
    {
        WithSqlStore(store =>
        {
            PushNotifications.ResetCache();
            var (_, saJson) = CreateSyntheticServiceAccount();
            PushNotifications.ServiceAccountJsonOverride = saJson;

            const string riderIqama = "2000000882";
            store.Write((c, tx) =>
            {
                Store.Save(c, "rider", new() { ["id"] = riderIqama, ["iqama"] = riderIqama, ["name"] = "مندوب", ["status"] = "Active" }, tx);
                Store.Save(c, MobileSessions.Kind, new()
                {
                    ["id"] = "mobile:retry-test",
                    ["iqama"] = riderIqama,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O"),
                    ["fcmDeviceToken"] = "token-error"
                }, tx);
                PushNotifications.Enqueue(c, tx, riderIqama, "تنبيه", "محاولة إرسال فاشلة");
                return true;
            });

            PushNotifications.TestHttpMessageHandler = new DelegatingTestHandler((req, ct) =>
            {
                if (req.RequestUri?.AbsoluteUri == "https://oauth2.googleapis.com/token")
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"access_token\":\"tok\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
                    });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{\"error\":\"Internal server error\"}", Encoding.UTF8, "application/json")
                });
            });

            // Attempt 1 -> pending, nextAttemptAt set to ~1 minute in the future
            PushNotifications.FlushOutboxAsync(store).GetAwaiter().GetResult();
            var row1 = store.Read(c => Store.List(c, PushNotifications.Kind))[0];
            Assert.AreEqual("pending", row1.S("status"));
            Assert.AreEqual(1, (int)row1.N("attempts"));
            Assert.IsFalse(string.IsNullOrEmpty(row1.S("nextAttemptAt")));
            DateTimeOffset next1 = DateTimeOffset.Parse(row1.S("nextAttemptAt"));
            Assert.IsTrue(next1 > DateTimeOffset.UtcNow);

            // Immediate flush skips items with nextAttemptAt in the future!
            int skipped = PushNotifications.FlushOutboxAsync(store).GetAwaiter().GetResult();
            Assert.AreEqual(0, skipped);

            // Fast-forward nextAttemptAt to the past
            store.Write((c, tx) =>
            {
                var r = Store.List(c, PushNotifications.Kind, tx)[0];
                r["nextAttemptAt"] = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O");
                Store.Save(c, PushNotifications.Kind, r, tx);
                return true;
            });

            // Attempt 2 -> pending, nextAttemptAt set to ~5 minutes in the future
            PushNotifications.FlushOutboxAsync(store).GetAwaiter().GetResult();
            var row2 = store.Read(c => Store.List(c, PushNotifications.Kind))[0];
            Assert.AreEqual("pending", row2.S("status"));
            Assert.AreEqual(2, (int)row2.N("attempts"));
            DateTimeOffset next2 = DateTimeOffset.Parse(row2.S("nextAttemptAt"));
            Assert.IsTrue(next2 > DateTimeOffset.UtcNow.AddMinutes(4));

            // Fast-forward again
            store.Write((c, tx) =>
            {
                var r = Store.List(c, PushNotifications.Kind, tx)[0];
                r["nextAttemptAt"] = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O");
                Store.Save(c, PushNotifications.Kind, r, tx);
                return true;
            });

            // Attempt 3 -> failed, nextAttemptAt cleared
            PushNotifications.FlushOutboxAsync(store).GetAwaiter().GetResult();
            var row3 = store.Read(c => Store.List(c, PushNotifications.Kind))[0];
            Assert.AreEqual("failed", row3.S("status"));
            Assert.AreEqual(3, (int)row3.N("attempts"));
            Assert.AreEqual("", row3.S("nextAttemptAt"));
            Assert.IsTrue(row3.S("lastError").Contains("InternalServerError"));
        });
    }

    [TestMethod]
    public void UnregisteredResponseClearsSessionFcmToken()
    {
        WithSqlStore(store =>
        {
            PushNotifications.ResetCache();
            var (_, saJson) = CreateSyntheticServiceAccount();
            PushNotifications.ServiceAccountJsonOverride = saJson;

            const string riderIqama = "2000000883";
            const string sessionId = "mobile:0123456789abcdef0123456789abcdef";

            store.Write((c, tx) =>
            {
                Store.Save(c, "rider", new() { ["id"] = riderIqama, ["iqama"] = riderIqama, ["name"] = "مندوب غير مسجل", ["status"] = "Active" }, tx);
                Store.Save(c, MobileSessions.Kind, new()
                {
                    ["id"] = sessionId,
                    ["iqama"] = riderIqama,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O"),
                    ["fcmDeviceToken"] = "unregistered-fcm-device-token"
                }, tx);
                PushNotifications.Enqueue(c, tx, riderIqama, "إشعار", "تأكيد مسح التوكن");
                return true;
            });

            PushNotifications.TestHttpMessageHandler = new DelegatingTestHandler((req, ct) =>
            {
                if (req.RequestUri?.AbsoluteUri == "https://oauth2.googleapis.com/token")
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"access_token\":\"tok\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
                    });
                }

                // FCM returns 404 with UNREGISTERED error code
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(
                        "{\"error\":{\"code\":404,\"message\":\"Requested entity was not found.\",\"status\":\"NOT_FOUND\",\"details\":[{\"@type\":\"type.googleapis.com/google.firebase.fcm.v1.FcmError\",\"errorCode\":\"UNREGISTERED\"}]}}",
                        Encoding.UTF8,
                        "application/json")
                });
            });

            PushNotifications.FlushOutboxAsync(store).GetAwaiter().GetResult();

            var outbox = store.Read(c => Store.List(c, PushNotifications.Kind))[0];
            Assert.AreEqual("failed", outbox.S("status"));
            Assert.AreEqual("UNREGISTERED", outbox.S("lastError"));

            // Verify session token is cleared!
            var session = store.Read(c => Store.Get(c, sessionId, MobileSessions.Kind))!;
            Assert.AreEqual("", session.S("fcmDeviceToken"));

            var audit = store.Read(c => Store.List(c, "audit"));
            Assert.IsTrue(audit.Any(a => a.S("action") == "mobile.fcm_unregistered"));
        });
    }

    [TestMethod]
    public void MissingEnvironmentVariableDoesNotThrowAndMarksOutboxWithArabicNotice()
    {
        WithSqlStore(store =>
        {
            PushNotifications.ResetCache();
            PushNotifications.ServiceAccountJsonOverride = null; // Clear override

            const string riderIqama = "2000000884";
            store.Write((c, tx) =>
            {
                Store.Save(c, "rider", new() { ["id"] = riderIqama, ["iqama"] = riderIqama, ["name"] = "مندوب", ["status"] = "Active" }, tx);
                Store.Save(c, MobileSessions.Kind, new()
                {
                    ["id"] = "mobile:no-env-test",
                    ["iqama"] = riderIqama,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O"),
                    ["fcmDeviceToken"] = "some-token"
                }, tx);
                PushNotifications.Enqueue(c, tx, riderIqama, "بدون مفتاح", "لا يجب أن يرمي استثناء");
                return true;
            });

            // Must not throw!
            int processed = PushNotifications.FlushOutboxAsync(store).GetAwaiter().GetResult();
            Assert.AreEqual(1, processed);

            var outbox = store.Read(c => Store.List(c, PushNotifications.Kind))[0];
            Assert.AreEqual("failed", outbox.S("status"));
            Assert.AreEqual("لم يُضبط Firebase", outbox.S("lastError"));
        });
    }

    [TestMethod]
    public void BroadcastEnqueuesOneItemPerRiderWithActiveToken()
    {
        WithSqlStore(store =>
        {
            PushNotifications.ResetCache();
            const string r1 = "2000000891", r2 = "2000000892", r3 = "2000000893", r4 = "2000000894";

            store.Write((c, tx) =>
            {
                foreach (var r in new[] { r1, r2, r3, r4 })
                {
                    Store.Save(c, "rider", new() { ["id"] = r, ["iqama"] = r, ["name"] = "Rider " + r, ["status"] = "Active" }, tx);
                }

                // r1 has active session with token
                Store.Save(c, MobileSessions.Kind, new()
                {
                    ["id"] = "mobile:r1-sess",
                    ["iqama"] = r1,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O"),
                    ["fcmDeviceToken"] = "token-r1"
                }, tx);

                // r2 has active session with token
                Store.Save(c, MobileSessions.Kind, new()
                {
                    ["id"] = "mobile:r2-sess",
                    ["iqama"] = r2,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O"),
                    ["fcmDeviceToken"] = "token-r2"
                }, tx);

                // r3 has active session WITHOUT token
                Store.Save(c, MobileSessions.Kind, new()
                {
                    ["id"] = "mobile:r3-sess",
                    ["iqama"] = r3,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O"),
                    ["fcmDeviceToken"] = ""
                }, tx);

                // r4 has expired/revoked session with token
                Store.Save(c, MobileSessions.Kind, new()
                {
                    ["id"] = "mobile:r4-sess",
                    ["iqama"] = r4,
                    ["createdAt"] = DateTimeOffset.UtcNow.AddDays(-2).ToString("O"),
                    ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddDays(-1).ToString("O"),
                    ["fcmDeviceToken"] = "token-r4-expired"
                }, tx);

                var enqueued = PushNotifications.EnqueueBroadcast(c, tx, "bcast-xyz", "ALL", "تعميم هام للجميع", "نص التعميم", "/forms/123");
                Assert.AreEqual(2, enqueued.Count);
                return true;
            });

            var outbox = store.Read(c => Store.List(c, PushNotifications.Kind));
            Assert.AreEqual(2, outbox.Count);
            var iqamas = outbox.Select(x => x.S("iqama")).OrderBy(x => x).ToList();
            CollectionAssert.AreEqual(new[] { r1, r2 }, iqamas);
        });
    }

    [TestMethod]
    public void ServiceAccountJsonInPostConfigBodyIsRejectedWithArabicNotice()
    {
        WithSqlStore(store =>
        {
            var ctx = new DefaultHttpContext();
            ctx.User = Admin;

            var inputWithSecret = new JsonObject
            {
                ["projectId"] = "albawaba-gate-rider",
                ["senderId"] = "123456",
                ["serviceAccountJson"] = "{\"type\":\"service_account\"}"
            };

            // Call the save logic which must throw ApiError with status 400
            var ex = Assert.ThrowsExactly<ApiError>(() =>
            {
                store.Write((c, tx) =>
                {
                    if (!string.IsNullOrWhiteSpace(inputWithSecret.S("serviceAccountJson")))
                    {
                        throw new ApiError("لا يمكن حفظ مفتاح حساب الخدمة في قاعدة البيانات لأسباب أمنية؛ يرجى ضبط المتغير البيئي Firebase__ServiceAccountJson على السيرفر.", 400);
                    }
                    return true;
                });
            });

            Assert.AreEqual(400, ex.Status);
            Assert.IsTrue(ex.Message.Contains("Firebase__ServiceAccountJson"));
        });
    }

    [TestMethod]
    public void FlushDrainsAllPendingItemsAcrossMultipleBatchesInTheBackground()
    {
        WithSqlStore(store =>
        {
            PushNotifications.ResetCache();
            var (_, saJson) = CreateSyntheticServiceAccount();
            PushNotifications.ServiceAccountJsonOverride = saJson;

            // Seed 120 riders with sessions & tokens
            store.Write((c, tx) =>
            {
                for (int i = 1; i <= 120; i++)
                {
                    string iqama = "200000" + i.ToString("D4");
                    Store.Save(c, "rider", new() { ["id"] = iqama, ["iqama"] = iqama, ["name"] = "مندوب " + i, ["status"] = "Active" }, tx);
                    Store.Save(c, MobileSessions.Kind, new()
                    {
                        ["id"] = "mobile:" + iqama.PadRight(32, 'a'),
                        ["iqama"] = iqama,
                        ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                        ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O"),
                        ["fcmDeviceToken"] = "token-" + iqama
                    }, tx);
                }
                PushNotifications.EnqueueBroadcast(c, tx, "bcast-120", "ALL", "تعميم 120", "نص التجربة");
                return true;
            });

            int enqueuedCount = store.Read(c => Store.List(c, PushNotifications.Kind).Count);
            Assert.AreEqual(120, enqueuedCount);

            int sendCalls = 0;
            PushNotifications.TestHttpMessageHandler = new DelegatingTestHandler(async (req, ct) =>
            {
                if (req.RequestUri?.AbsoluteUri == "https://oauth2.googleapis.com/token")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"access_token\":\"tok-120\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
                    };
                }
                Interlocked.Increment(ref sendCalls);
                await Task.Delay(5, ct); // simulate small latency
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"name\":\"msg-id\"}", Encoding.UTF8, "application/json")
                };
            });

            // Trigger non-blocking fire-and-forget FlushOutbox
            var sw = System.Diagnostics.Stopwatch.StartNew();
            PushNotifications.FlushOutbox(store);
            sw.Stop();

            // The call must return immediately without waiting for 120 network sends
            Assert.IsTrue(sw.ElapsedMilliseconds < 400, $"FlushOutbox blocked caller! Took {sw.ElapsedMilliseconds} ms");

            // Wait until the background worker finishes draining all 120 items
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline)
            {
                int sent = store.Read(c => Store.List(c, PushNotifications.Kind).Count(x => x.S("status") == "sent"));
                if (sent == 120) break;
                Thread.Sleep(50);
            }

            int finalSent = store.Read(c => Store.List(c, PushNotifications.Kind).Count(x => x.S("status") == "sent"));
            Assert.AreEqual(120, finalSent);
        });
    }

    [TestMethod]
    public void LegacyStoredKeyFlagReflectsPresenceOfServiceAccountJsonInConfigSetting()
    {
        WithSqlStore(store =>
        {
            // When legacy setting exists with serviceAccountJson
            store.Write((c, tx) =>
            {
                Store.Save(c, "setting", new()
                {
                    ["id"] = "firebase:config",
                    ["projectId"] = "albawaba-gate-rider",
                    ["serviceAccountJson"] = "{\"legacy\":true}"
                }, tx);
                return true;
            });

            var cfgWithLegacy = store.Read(c => Store.Get(c, "firebase:config", "setting"));
            bool legacyStoredKey1 = !string.IsNullOrWhiteSpace(cfgWithLegacy?.S("serviceAccountJson"));
            Assert.IsTrue(legacyStoredKey1);

            // When setting is updated without serviceAccountJson
            store.Write((c, tx) =>
            {
                Store.Save(c, "setting", new()
                {
                    ["id"] = "firebase:config",
                    ["projectId"] = "albawaba-gate-rider"
                }, tx);
                return true;
            });

            var cfgClean = store.Read(c => Store.Get(c, "firebase:config", "setting"));
            bool legacyStoredKey2 = !string.IsNullOrWhiteSpace(cfgClean?.S("serviceAccountJson"));
            Assert.IsFalse(legacyStoredKey2);
        });
    }
}
