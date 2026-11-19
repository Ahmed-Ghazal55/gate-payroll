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
using Microsoft.Extensions.Configuration;
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
        string? configured = Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        string dbName = "GatePushTest_" + Guid.NewGuid().ToString("N");
        string tempDir = Path.Combine(Path.GetTempPath(), dbName);

        if (string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                var store = new Store(tempDir);
                action(store);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                PushNotifications.ResetCache();
            }
            return;
        }

        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        using (var masterConn = new SqlConnection(master.ConnectionString))
        {
            masterConn.Open();
            using var cmd = masterConn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{dbName}]";
            cmd.ExecuteNonQuery();
        }

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

    [TestMethod]
    public void IndividualNoticeCategoriesAreDistinguishedFromGeneralBroadcastsAndProvideThreeLanguageTitles()
    {
        foreach (string cat in new[] { "summons", "dismissal_warning", "administrative_deduction", "appointment_alert", "administrative_warning" })
        {
            Assert.IsTrue(PushNotifications.IsIndividualOnlyNotice(cat), cat);
            var (ar, en, ur) = PushNotifications.DefaultNoticeTitles(cat);
            Assert.IsFalse(string.IsNullOrWhiteSpace(ar));
            Assert.IsFalse(string.IsNullOrWhiteSpace(en));
            Assert.IsFalse(string.IsNullOrWhiteSpace(ur));
        }

        foreach (string cat in new[] { "operational", "financial", "administrative", "form_link", "" })
        {
            Assert.IsFalse(PushNotifications.IsIndividualOnlyNotice(cat), cat);
        }
    }

    [TestMethod]
    public void TwoWayServiceRequestsEnforceSecondApproverConfidentialComplaintsAndTimelineAndReplies()
    {
        WithSqlStore(store =>
        {
            PushNotifications.ResetCache();
            const string riderIqama = "2000000871";
            const string token = "rmob_" + "1111111111111111111111111111111111111111111111111111111111111111";
            string sessionId = "mobile:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

            store.Write((c, tx) =>
            {
                Store.Save(c, "rider", new()
                {
                    ["id"] = riderIqama,
                    ["iqama"] = riderIqama,
                    ["name"] = "مندوب اختبار شامل",
                    ["phone"] = "+966501112233",
                    ["city"] = "الرياض",
                    ["status"] = "Active",
                    ["financeSponsorId"] = FinancialCore.ResolveSponsor("BW").Id
                }, tx);
                Store.Save(c, MobileSessions.Kind, new()
                {
                    ["id"] = sessionId,
                    ["iqama"] = riderIqama,
                    ["language"] = "ar",
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O"),
                    ["fcmDeviceToken"] = "tok-service-test"
                }, tx);
                return true;
            });

            var riderCtx = new DefaultHttpContext();
            riderCtx.Request.Headers.Authorization = "Bearer " + token;

            var plainReviewer = UserAccess.Principal(new()
            {
                ["id"] = "syn-plain-rev",
                ["username"] = "syn-plain-rev",
                ["role"] = "Reviewer",
                ["customPermissions"] = new JsonArray("forms.review", "mobile.broadcast"),
                ["financialScope"] = new JsonObject { ["sponsorIds"] = new JsonArray(FinancialCore.ResolveSponsor("BW").Id) }
            });
            var reviewer1 = UserAccess.Principal(new()
            {
                ["id"] = "syn-rev-1",
                ["username"] = "syn-rev-1",
                ["role"] = "Reviewer",
                ["customPermissions"] = new JsonArray(RiderAsks.IbanDataApproveFirstPermission),
                ["financialScope"] = new JsonObject { ["sponsorIds"] = new JsonArray(FinancialCore.ResolveSponsor("BW").Id) }
            });
            var reviewer1RenamedWithBoth = UserAccess.Principal(new()
            {
                ["id"] = "syn-rev-1",
                ["username"] = "syn-rev-1-renamed",
                ["role"] = "Reviewer",
                ["customPermissions"] = new JsonArray(RiderAsks.IbanDataApproveFirstPermission, RiderAsks.IbanDataApproveSecondPermission),
                ["financialScope"] = new JsonObject { ["sponsorIds"] = new JsonArray(FinancialCore.ResolveSponsor("BW").Id) }
            });
            var reviewer2 = UserAccess.Principal(new()
            {
                ["id"] = "syn-rev-2",
                ["username"] = "syn-rev-2",
                ["role"] = "Reviewer",
                ["customPermissions"] = new JsonArray(RiderAsks.IbanDataApproveSecondPermission),
                ["financialScope"] = new JsonObject { ["sponsorIds"] = new JsonArray(FinancialCore.ResolveSponsor("BW").Id) }
            });

            // 1. Annual Leave Request (new form)
            var leave = store.Write((c, tx) => RiderAsks.CreateServiceRequest(c, tx, riderCtx, new()
            {
                ["requestType"] = "annual_leave",
                ["leaveType"] = "annual",
                ["startDate"] = "2026-12-01",
                ["endDate"] = "2026-12-15",
                ["destination"] = "outside_ksa",
                ["reason"] = "إجازة سنوية للسفر"
            }));
            Assert.AreEqual("annual_leave", leave.S("requestType"));
            Assert.AreEqual("open", leave.S("status"));
            Assert.AreEqual(15, (int)leave["payload"]!.AsObject().N("days"));
            Assert.AreEqual(1, leave["timeline"]!.AsArray().Count);

            // 2. IBAN Update with dedicated per-step permissions and two independent approvers (H1 / M-0359)
            const string validRajhiIban = "SA0380000000608010167519";
            var ibanReq = store.Write((c, tx) => RiderAsks.CreateServiceRequest(c, tx, riderCtx, new()
            {
                ["requestType"] = "iban_update",
                ["iban"] = validRajhiIban,
                ["bankCode"] = "80",
                ["reason"] = "حساب جديد باسمي"
            }));
            Assert.AreEqual("open", ibanReq.S("status"));
            Assert.IsTrue(ibanReq.B("requiresSecondApprover"));
            Assert.AreEqual("awaiting_first", ibanReq.S("approvalStage"));

            // Plain reviewer with only forms.review + mobile.broadcast cannot approve step 1 (403)
            Assert.AreEqual(403, Assert.ThrowsExactly<ApiError>(() =>
                store.Write((c, tx) => RiderAsks.DecideServiceRequest(c, tx, plainReviewer, ibanReq.S("id"), new()
                {
                    ["action"] = "approve"
                }))).Status);

            // Second-only approver cannot do step 1 (403)
            Assert.AreEqual(403, Assert.ThrowsExactly<ApiError>(() =>
                store.Write((c, tx) => RiderAsks.DecideServiceRequest(c, tx, reviewer2, ibanReq.S("id"), new()
                {
                    ["action"] = "approve"
                }))).Status);

            // First approval by reviewer1 (with approve_first) -> moves to first_approved
            var afterFirst = store.Write((c, tx) => RiderAsks.DecideServiceRequest(c, tx, reviewer1, ibanReq.S("id"), new()
            {
                ["action"] = "approve",
                ["note"] = "تمت مراجعة الهوية كاعتماد أول"
            }));
            Assert.AreEqual("first_approved", afterFirst.S("status"));
            Assert.AreEqual("awaiting_second", afterFirst.S("approvalStage"));
            Assert.AreEqual("syn-rev-1", afterFirst.S("firstApprovedById"));

            // Plain reviewer cannot approve step 2 (403)
            Assert.AreEqual(403, Assert.ThrowsExactly<ApiError>(() =>
                store.Write((c, tx) => RiderAsks.DecideServiceRequest(c, tx, plainReviewer, ibanReq.S("id"), new()
                {
                    ["action"] = "approve"
                }))).Status);

            // First-only approver cannot approve step 2 (403)
            Assert.AreEqual(403, Assert.ThrowsExactly<ApiError>(() =>
                store.Write((c, tx) => RiderAsks.DecideServiceRequest(c, tx, reviewer1, ibanReq.S("id"), new()
                {
                    ["action"] = "approve"
                }))).Status);

            // Same user id (even with a different username and both permissions) is refused with 403
            var selfApproveErr = Assert.ThrowsExactly<ApiError>(() =>
                store.Write((c, tx) => RiderAsks.DecideServiceRequest(c, tx, reviewer1RenamedWithBoth, ibanReq.S("id"), new()
                {
                    ["action"] = "approve"
                })));
            Assert.AreEqual(403, selfApproveErr.Status);

            // Second independent reviewer2 (different user id, with approve_second) approves -> moves to approved & updates verifiedIban
            var afterSecond = store.Write((c, tx) => RiderAsks.DecideServiceRequest(c, tx, reviewer2, ibanReq.S("id"), new()
            {
                ["action"] = "approve",
                ["note"] = "تمت المطابقة النهائية كاعتماد ثانٍ"
            }));
            Assert.AreEqual("approved", afterSecond.S("status"));
            Assert.AreEqual("completed", afterSecond.S("approvalStage"));
            Assert.AreEqual("syn-rev-2", afterSecond.S("secondApprovedById"));
            Assert.AreEqual(validRajhiIban, store.Read(c => Store.Get(c, riderIqama, "rider")!.S("verifiedIban")));

            // Submitting the same IBAN again auto-approves immediately (§1e)
            var autoIban = store.Write((c, tx) => RiderAsks.CreateServiceRequest(c, tx, riderCtx, new()
            {
                ["requestType"] = "iban_update",
                ["iban"] = validRajhiIban,
                ["bankCode"] = "80"
            }));
            Assert.AreEqual("approved", autoIban.S("status"));
            Assert.AreEqual("auto_approved", autoIban.S("approvalStage"));

            // 3. Confidential Complaint (M-0359): hidden from regular staff, visible to Admin
            var complaint = store.Write((c, tx) => RiderAsks.CreateServiceRequest(c, tx, riderCtx, new()
            {
                ["requestType"] = "complaint",
                ["targetKind"] = "supervisor",
                ["subject"] = "شكوى سرية تجريبية",
                ["details"] = "تفاصيل الشكوى السرية للاختبار فقط"
            }));
            Assert.IsTrue(complaint.B("confidential"));

            // reviewer1 without confidential permission cannot decide or reply on it (404)
            Assert.AreEqual(404, Assert.ThrowsExactly<ApiError>(() =>
                store.Write((c, tx) => RiderAsks.StaffReplyServiceRequest(c, tx, reviewer1, complaint.S("id"), new()
                {
                    ["message"] = "محاولة وصول غير مصرح"
                }))).Status);

            // Admin replies and rider replies back (two-way replies + timeline)
            store.Write((c, tx) => RiderAsks.StaffReplyServiceRequest(c, tx, Admin, complaint.S("id"), new()
            {
                ["message"] = "تم استلام الشكوى السرية وجاري التحقيق"
            }));
            var afterRiderReply = store.Write((c, tx) => RiderAsks.RiderReply(c, tx, riderCtx, complaint.S("id"), new()
            {
                ["message"] = "شكراً لكم، مرفق توضيح إضافي"
            }));
            Assert.AreEqual(2, afterRiderReply["replies"]!.AsArray().Count);
            Assert.AreEqual(3, afterRiderReply["timeline"]!.AsArray().Count);

            // 4. Outbound Admin-to-Rider request (requests both ways)
            var adminToRider = store.Write((c, tx) => RiderAsks.CreateAdminRequestToRider(c, tx, Admin, new()
            {
                ["iqama"] = riderIqama,
                ["requestType"] = "appointment",
                ["reason"] = "يرجى تحديد موعد لتحديث بيانات البطاقة"
            }));
            Assert.AreEqual("admin_to_rider", adminToRider.S("direction"));
            Assert.AreEqual("awaiting_rider", adminToRider.S("status"));
            var riderRepliedToAdmin = store.Write((c, tx) => RiderAsks.RiderReply(c, tx, riderCtx, adminToRider.S("id"), new()
            {
                ["message"] = "يناسبني يوم الأحد صباحاً"
            }));
            Assert.AreEqual("open", riderRepliedToAdmin.S("status"));
        });
    }

    [TestMethod]
    public void ShortThreeLanguageStatementLatestSummaryAndBulkPeriodNotificationWorkEndToEnd()
    {
        WithSqlStore(store =>
        {
            PushNotifications.ResetCache();
            const string riderIqama = "2000000872";
            const string token = "rmob_" + "2222222222222222222222222222222222222222222222222222222222222222";
            string sessionId = "mobile:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

            store.Write((c, tx) =>
            {
                Store.Save(c, "rider", new()
                {
                    ["id"] = riderIqama,
                    ["iqama"] = riderIqama,
                    ["name"] = "مندوب كشف مختصر",
                    ["status"] = "Active",
                    ["sponsorId"] = FinancialCore.ResolveSponsor("BW").Id
                }, tx);
                Store.Save(c, MobileSessions.Kind, new()
                {
                    ["id"] = sessionId,
                    ["iqama"] = riderIqama,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O"),
                    ["fcmDeviceToken"] = "tok-stmt-test"
                }, tx);
                Store.Save(c, "run", new()
                {
                    ["id"] = "syn-run-2026-10",
                    ["iqama"] = riderIqama,
                    ["period"] = "2026-10",
                    ["sponsorId"] = FinancialCore.ResolveSponsor("BW").Id,
                    ["approvedAt"] = "2026-11-01T10:00:00Z",
                    ["result"] = new JsonObject
                    {
                        ["policyName"] = "Tier Policy",
                        ["openingBalance"] = -100m,
                        ["periodMovement"] = 1850m,
                        ["closingBalance"] = 1750m,
                        ["lines"] = new JsonArray(
                            new JsonObject { ["category"] = "platform_settlement", ["platform"] = "Keeta", ["amount"] = 2200m },
                            new JsonObject { ["category"] = "company_fee", ["platform"] = "Keeta", ["amount"] = -200m },
                            new JsonObject { ["category"] = "debt_installment", ["amount"] = -150m }
                        )
                    }
                }, tx);
                return true;
            });

            var riderCtx = new DefaultHttpContext();
            riderCtx.Request.Headers.Authorization = "Bearer " + token;

            var stmt = store.Read(c => PayStatements.Mobile(c, riderCtx, "2026-10"));
            Assert.IsFalse(stmt.B("legalDocument"));
            Assert.AreEqual("لك", stmt.S("closingMarkerAr"));
            Assert.AreEqual("For you (لك)", stmt.S("closingMarkerEn"));
            Assert.AreEqual("آپ کے حق میں (لك)", stmt.S("closingMarkerUr"));

            var shortLines = stmt["shortLines"]!.AsArray().OfType<JsonObject>().ToList();
            Assert.IsTrue(shortLines.Count >= 3);
            Assert.AreEqual("عليك", shortLines[0].S("markerAr")); // carried -100
            Assert.AreEqual("لك", shortLines[1].S("markerAr"));   // gross +2200
            Assert.IsFalse(string.IsNullOrWhiteSpace(shortLines[1].S("labelEn")));
            Assert.IsFalse(string.IsNullOrWhiteSpace(shortLines[1].S("labelUr")));
            Assert.AreEqual(stmt.N("closing"), shortLines.Last().N("runningBalance"));
            Assert.IsTrue(stmt.B("shortLinesBalanced"));
            Assert.AreEqual(0m, stmt.N("shortLinesMismatch"));

            // Ensure no company/sponsor IDs leak in mobile statement cards
            Assert.IsTrue(stmt["cards"]!.AsArray().OfType<JsonObject>().All(card => !card.ContainsKey("companyId") && !card.ContainsKey("sponsorId")));

            // Latest approved summary for Home banner
            var latest = store.Read(c => PayStatements.LatestApprovedSummary(c, riderIqama));
            Assert.IsTrue(latest.B("available"));
            Assert.AreEqual("2026-10", latest.S("period"));
            Assert.AreEqual(1750m, latest.N("closing"));
            Assert.AreEqual("لك", latest.S("markerAr"));

            // Bulk statement notify for 2026-10 via outbox (idempotent and no salary numbers in push payload)
            var bulk1 = store.Write((c, tx) => PushNotifications.NotifyApprovedStatementsForPeriod(c, tx, Admin, "2026-10"));
            Assert.AreEqual(1, (int)bulk1.N("notifiedRidersCount"));
            Assert.AreEqual(1, (int)bulk1.N("enqueuedPushCount"));

            var outboxItem = store.Read(c => Store.List(c, PushNotifications.Kind).Single());
            Assert.IsFalse(outboxItem.S("body").Contains("1750"), "Salary amount must never be sent inside FCM push payload");

            var bulk2 = store.Write((c, tx) => PushNotifications.NotifyApprovedStatementsForPeriod(c, tx, Admin, "2026-10"));
            Assert.AreEqual(0, (int)bulk2.N("notifiedRidersCount"));
            Assert.AreEqual(1, (int)bulk2.N("skippedAlreadyNotifiedCount"));
        });
    }

    [TestMethod]
    public void PerRiderRateLimiterEnforcesIpBeforeAuthSessionAfterAndEvictsOldWindows()
    {
        PushNotifications.ResetCache();
        try
        {
            var now = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
            PushNotifications.ClockOverride = () => now;
            PushNotifications.IpWritePermitLimitOverride = 3;
            PushNotifications.WritePermitLimitOverride = 2;

            // 1. Forged random Bearer rmob_ tokens from the same IP hit IP limit and do NOT create fresh buckets
            for (int i = 0; i < 3; i++)
            {
                var forged = new DefaultHttpContext();
                forged.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.10");
                forged.Request.Headers.Authorization = "Bearer rmob_" + new string((char)('a' + i), 64);
                PushNotifications.EnforceRiderIpRateLimit(forged, write: true);
            }
            Assert.AreEqual(1, PushNotifications.RateWindowBucketCount);

            var fourthForged = new DefaultHttpContext();
            fourthForged.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.10");
            fourthForged.Request.Headers.Authorization = "Bearer rmob_" + new string('z', 64);
            var ipErr = Assert.ThrowsExactly<ApiError>(() => PushNotifications.EnforceRiderIpRateLimit(fourthForged, write: true));
            Assert.AreEqual(429, ipErr.Status);
            Assert.AreEqual("60", fourthForged.Response.Headers.RetryAfter.ToString());
            Assert.AreEqual(1, PushNotifications.RateWindowBucketCount);

            // 2. Per-session limit applies after auth even if requests come from distinct IPs
            var session = new JsonObject { ["id"] = "mobile:syn-session-1" };
            for (int i = 0; i < 2; i++)
            {
                var ctx = new DefaultHttpContext();
                ctx.Connection.RemoteIpAddress = IPAddress.Parse($"198.51.100.{20 + i}");
                PushNotifications.EnforceRiderIpRateLimit(ctx, write: true);
                PushNotifications.EnforceRiderSessionRateLimit(ctx, session, write: true);
            }
            var overSession = new DefaultHttpContext();
            overSession.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.25");
            PushNotifications.EnforceRiderIpRateLimit(overSession, write: true);
            Assert.AreEqual(429, Assert.ThrowsExactly<ApiError>(() => PushNotifications.EnforceRiderSessionRateLimit(overSession, session, write: true)).Status);

            // 3. Advancing to a later minute evicts all previous-minute windows
            now = now.AddMinutes(2);
            var freshCtx = new DefaultHttpContext();
            freshCtx.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.99");
            PushNotifications.EnforceRiderIpRateLimit(freshCtx, write: true);
            Assert.AreEqual(1, PushNotifications.RateWindowBucketCount);
            Assert.IsTrue(PushNotifications.LastEvictionRemovedCount > 0);
        }
        finally
        {
            PushNotifications.ResetCache();
        }
    }

    [TestMethod]
    public void PerIpLimitsAreConfigurableAndEvictionIsLinearInExpiredBuckets()
    {
        PushNotifications.ResetCache();
        try
        {
            Assert.AreEqual(180, PushNotifications.IpReadLimitPerMinute);
            Assert.AreEqual(60, PushNotifications.IpWriteLimitPerMinute);

            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RiderMobile:IpReadLimitPerMinute"] = "12",
                ["RiderMobile:IpWriteLimitPerMinute"] = "4"
            }).Build();
            PushNotifications.ConfigureIpLimits(cfg);
            Assert.AreEqual(12, PushNotifications.IpReadLimitPerMinute);
            Assert.AreEqual(4, PushNotifications.IpWriteLimitPerMinute);

            var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
            PushNotifications.ClockOverride = () => now;
            for (int i = 0; i < 4; i++)
            {
                var ctx = new DefaultHttpContext();
                ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
                PushNotifications.EnforceRiderIpRateLimit(ctx, write: true);
            }
            var blocked = new DefaultHttpContext();
            blocked.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
            Assert.AreEqual(429, Assert.ThrowsExactly<ApiError>(() => PushNotifications.EnforceRiderIpRateLimit(blocked, write: true)).Status);

            // Fresh clock window: seed many expired IP buckets, then one current request removes only those.
            PushNotifications.ResetCache();
            PushNotifications.IpReadLimitPerMinute = 10_000;
            PushNotifications.IpWriteLimitPerMinute = 10_000;
            now = new DateTimeOffset(2026, 11, 19, 13, 0, 0, TimeSpan.Zero);
            PushNotifications.ClockOverride = () => now;
            for (int i = 0; i < 2500; i++)
            {
                var ctx = new DefaultHttpContext();
                // Distinct /24 hosts so each write/read creates its own bucket without hitting limits.
                ctx.Connection.RemoteIpAddress = IPAddress.Parse($"198.51.{i / 256}.{i % 256}");
                PushNotifications.EnforceRiderIpRateLimit(ctx, write: i % 2 == 0);
            }
            int before = PushNotifications.RateWindowBucketCount;
            Assert.AreEqual(2500, before);

            now = now.AddMinutes(3);
            PushNotifications.ClockOverride = () => now;
            var fresh = new DefaultHttpContext();
            fresh.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.250");
            PushNotifications.EnforceRiderIpRateLimit(fresh, write: false);
            Assert.AreEqual(before, PushNotifications.LastEvictionRemovedCount);
            Assert.AreEqual(1, PushNotifications.RateWindowBucketCount);
        }
        finally
        {
            PushNotifications.ResetCache();
        }
    }

    [TestMethod]
    public void PushNotificationsDeliverTitleAndBodyInRiderSessionLanguage()
    {
        var (_, saJson) = CreateSyntheticServiceAccount();
        var sentPayloads = new Dictionary<string, (string Title, string Body, string Lang)>();

        WithSqlStore(store =>
        {
            PushNotifications.ResetCache();
            PushNotifications.ServiceAccountJsonOverride = saJson;
            PushNotifications.TestHttpMessageHandler = new DelegatingTestHandler(async (req, _) =>
            {
                if (req.RequestUri!.AbsoluteUri.Contains("oauth2.googleapis.com"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"access_token":"syn-tok","expires_in":3600}""", Encoding.UTF8, "application/json")
                    };
                }
                string json = await req.Content!.ReadAsStringAsync();
                var doc = JsonNode.Parse(json)!.AsObject();
                var msg = doc["message"]!.AsObject();
                string tok = msg.S("token");
                var notif = msg["notification"]!.AsObject();
                var data = msg["data"]!.AsObject();
                sentPayloads[tok] = (notif.S("title"), notif.S("body"), data.S("language"));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"name":"projects/synthetic/messages/1"}""", Encoding.UTF8, "application/json")
                };
            });

            var riders = new[] { ("2000000911", "ar", "fcm-tok-ar"), ("2000000912", "en", "fcm-tok-en"), ("2000000913", "ur", "fcm-tok-ur") };
            store.Write((c, tx) =>
            {
                foreach (var (iq, lang, fcm) in riders)
                {
                    Store.Save(c, "rider", new() { ["id"] = iq, ["iqama"] = iq, ["name"] = "Rider " + lang, ["status"] = "Active" }, tx);
                    Store.Save(c, MobileSessions.Kind, new()
                    {
                        ["id"] = "mobile:lang_" + lang,
                        ["iqama"] = iq,
                        ["language"] = lang,
                        ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                        ["lastSeenAt"] = DateTimeOffset.UtcNow.ToString("O"),
                        ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O"),
                        ["fcmDeviceToken"] = fcm
                    }, tx);
                }
                PushNotifications.EnqueueBroadcast(c, tx, "bcast-lang", "ALL",
                    "عنوان عربي", "نص عربي", "/rider-app/notifications",
                    "English Title", "اردو عنوان", "English Body", "اردو پیغام");
                return true;
            });

            int processed = PushNotifications.FlushOutboxAsync(store).GetAwaiter().GetResult();
            Assert.AreEqual(3, processed);
            Assert.AreEqual(("عنوان عربي", "نص عربي", "ar"), sentPayloads["fcm-tok-ar"]);
            Assert.AreEqual(("English Title", "English Body", "en"), sentPayloads["fcm-tok-en"]);
            Assert.AreEqual(("اردو عنوان", "اردو پیغام", "ur"), sentPayloads["fcm-tok-ur"]);
        });
    }

    [TestMethod]
    public void FilteredRiderQueriesFilterByIqamaAndPeriodAndUseSqliteIndexes()
    {
        WithSqlStore(store =>
        {
            const string riderA = "2000000921";
            const string riderB = "2000000922";
            store.Write((c, tx) =>
            {
                Store.Save(c, PayCycles.LineKind, new() { ["id"] = "pl-1", ["cycleId"] = "cyc-1", ["rider"] = riderA, ["period"] = "2026-10" }, tx);
                Store.Save(c, PayCycles.LineKind, new() { ["id"] = "pl-2", ["cycleId"] = "cyc-2", ["rider"] = riderA, ["period"] = "2026-09" }, tx);
                Store.Save(c, PayCycles.LineKind, new() { ["id"] = "pl-3", ["cycleId"] = "cyc-1", ["rider"] = riderB, ["period"] = "2026-10" }, tx);
                Store.Save(c, "mobile_broadcast", new() { ["id"] = "stmt_notice:2026-10:" + riderA, ["targetIqama"] = riderA, ["createdAt"] = "2026-11-01T10:00:00Z" }, tx);
                Store.Save(c, "mobile_broadcast", new() { ["id"] = "stmt_notice:2026-09:" + riderA, ["targetIqama"] = riderA, ["createdAt"] = "2026-10-01T10:00:00Z" }, tx);
                Store.Save(c, "mobile_broadcast", new() { ["id"] = "bcast-all", ["targetIqama"] = "ALL", ["createdAt"] = "2026-11-02T10:00:00Z" }, tx);
                Store.Save(c, "mobile_broadcast", new() { ["id"] = "bcast-other", ["targetIqama"] = riderB, ["createdAt"] = "2026-11-03T10:00:00Z" }, tx);
                Store.Save(c, RiderAsks.ServiceKind, new() { ["id"] = "sreq:a1", ["riderIqama"] = riderA, ["createdAt"] = "2026-11-01T10:00:00Z" }, tx);
                Store.Save(c, RiderAsks.ServiceKind, new() { ["id"] = "sreq:b1", ["riderIqama"] = riderB, ["createdAt"] = "2026-11-01T10:00:00Z" }, tx);
                return true;
            });

            store.Read(c =>
            {
                var lines = Store.ListWhere(c, PayCycles.LineKind, null, null, ("rider", riderA), ("period", "2026-10"));
                Assert.AreEqual(1, lines.Count);
                Assert.AreEqual("pl-1", lines[0].S("id"));

                var bcasts = Store.ListBroadcastsForRider(c, riderA, 2);
                Assert.AreEqual(2, bcasts.Count);
                Assert.AreEqual("bcast-all", bcasts[0].S("id"));
                Assert.AreEqual("stmt_notice:2026-10:" + riderA, bcasts[1].S("id"));

                var octNoticeIds = Store.ListIdsByPrefix(c, "mobile_broadcast", "stmt_notice:2026-10:");
                Assert.AreEqual(1, octNoticeIds.Count);
                Assert.IsTrue(octNoticeIds.Contains("stmt_notice:2026-10:" + riderA));

                var sreqs = Store.ListWhere(c, RiderAsks.ServiceKind, null, 10, ("riderIqama", riderA));
                Assert.AreEqual(1, sreqs.Count);
                Assert.AreEqual("sreq:a1", sreqs[0].S("id"));

                if (c is SqliteConnection)
                {
                    using var planCmd = Store.Command(c, null, "EXPLAIN QUERY PLAN SELECT body FROM records WHERE kind='pay_cycle_line' AND json_extract(body,'$.rider')=@r AND json_extract(body,'$.period')=@p", ("@r", riderA), ("@p", "2026-10"));
                    using var reader = planCmd.ExecuteReader();
                    var planLines = new List<string>();
                    while (reader.Read()) planLines.Add(reader.GetString(3));
                    Assert.IsTrue(planLines.Any(l => l.Contains("ix_pay_cycle_line_rider_period", StringComparison.Ordinal)),
                        "Expected SQLite index ix_pay_cycle_line_rider_period in query plan: " + string.Join("; ", planLines));
                }
                return true;
            });
        });
    }

    [TestMethod]
    public void InAppIbanFormRequiresPhoneAndCityWithoutPlaceholderFallback()
    {
        WithSqlStore(store =>
        {
            const string riderIqama = "2000000931";
            const string token = "rmob_" + "4444444444444444444444444444444444444444444444444444444444444444";
            const string campToken = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            string sessionId = "mobile:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

            store.Write((c, tx) =>
            {
                Store.Save(c, "rider", new()
                {
                    ["id"] = riderIqama,
                    ["iqama"] = riderIqama,
                    ["name"] = "مندوب اختبار",
                    ["status"] = "Active",
                    ["phone"] = "",
                    ["city"] = ""
                }, tx);
                Store.Save(c, MobileSessions.Kind, new()
                {
                    ["id"] = sessionId,
                    ["iqama"] = riderIqama,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O")
                }, tx);
                Store.Save(c, "campaign", new()
                {
                    ["id"] = "camp-iban-test",
                    ["kind"] = IbanChoices.Kind,
                    ["title"] = "حملة آيبان تجريبية",
                    ["status"] = "open",
                    ["sharingToken"] = campToken,
                    ["expiresAt"] = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                    ["cities"] = new JsonArray(new JsonObject { ["ar"] = "جدة", ["en"] = "Jeddah" })
                }, tx);
                return true;
            });

            var ctx = new DefaultHttpContext();
            ctx.Request.Headers.Authorization = "Bearer " + token;
            const string validRajhiIban = "SA0380000000608010167519";

            // Missing phone -> refused (400), never stores 0500000000
            Assert.AreEqual(400, Assert.ThrowsExactly<ApiError>(() =>
                RiderAsks.SubmitInAppForm(store, ctx, campToken, new()
                {
                    ["iban"] = validRajhiIban,
                    ["bankCode"] = "80",
                    ["city"] = "جدة"
                })).Status);

            // Missing city -> refused (400), never stores "الرياض"
            Assert.AreEqual(400, Assert.ThrowsExactly<ApiError>(() =>
                RiderAsks.SubmitInAppForm(store, ctx, campToken, new()
                {
                    ["iban"] = validRajhiIban,
                    ["bankCode"] = "80",
                    ["phone"] = "0512345678"
                })).Status);

            // Providing both phone and city succeeds and stores exact values
            RiderAsks.SubmitInAppForm(store, ctx, campToken, new()
            {
                ["iban"] = validRajhiIban,
                ["bankCode"] = "80",
                ["phone"] = "0512345678",
                ["city"] = "جدة"
            });
            var saved = store.Read(c => Store.ListWhere(c, "submission", null, null, ("iqama", riderIqama)).Single());
            Assert.AreEqual("+966512345678", saved.S("phone"));
            Assert.AreEqual("جدة", saved.S("city"));
        });
    }

    [TestMethod]
    public void InAppFormSubmissionAlwaysUsesNameFromRiderRecord()
    {
        WithSqlStore(store =>
        {
            const string riderTwoWords = "2000000941";
            const string riderOneWord = "2000000942";
            const string tokTwo = "rmob_" + "5555555555555555555555555555555555555555555555555555555555555555";
            const string tokOne = "rmob_" + "6666666666666666666666666666666666666666666666666666666666666666";
            const string campToken = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

            store.Write((c, tx) =>
            {
                Store.Save(c, "rider", new() { ["id"] = riderTwoWords, ["iqama"] = riderTwoWords, ["name"] = "الاسم الرسمي", ["status"] = "Active" }, tx);
                Store.Save(c, "rider", new() { ["id"] = riderOneWord, ["iqama"] = riderOneWord, ["name"] = "اسمواحد", ["status"] = "Active" }, tx);
                Store.Save(c, MobileSessions.Kind, new()
                {
                    ["id"] = "mobile:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokTwo))),
                    ["iqama"] = riderTwoWords,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O")
                }, tx);
                Store.Save(c, MobileSessions.Kind, new()
                {
                    ["id"] = "mobile:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokOne))),
                    ["iqama"] = riderOneWord,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O")
                }, tx);
                Store.Save(c, "campaign", new()
                {
                    ["id"] = "camp-name-test",
                    ["kind"] = "work-choice",
                    ["title"] = "حملة نظام العمل",
                    ["status"] = "open",
                    ["sharingToken"] = campToken,
                    ["expiresAt"] = DateTimeOffset.UtcNow.AddDays(7).ToString("O")
                }, tx);
                return true;
            });

            // 1. Client tries to spoof name -> ignored, stored rider name is saved
            var ctxTwo = new DefaultHttpContext();
            ctxTwo.Request.Headers.Authorization = "Bearer " + tokTwo;
            RiderAsks.SubmitInAppForm(store, ctxTwo, campToken, new()
            {
                ["name"] = "اسم مزور من العميل",
                ["workSystem"] = "tiers"
            });
            var saved = store.Read(c => Store.ListWhere(c, "submission", null, null, ("iqama", riderTwoWords)).Single());
            Assert.AreEqual("الاسم الرسمي", saved.S("name"));

            // 2. Stored rider name has < 2 words -> client-supplied name is NOT used (rejected by WorkChoices.Identity)
            var ctxOne = new DefaultHttpContext();
            ctxOne.Request.Headers.Authorization = "Bearer " + tokOne;
            Assert.AreEqual(400, Assert.ThrowsExactly<ApiError>(() =>
                RiderAsks.SubmitInAppForm(store, ctxOne, campToken, new()
                {
                    ["name"] = "اسم بديل من العميل",
                    ["workSystem"] = "tiers"
                })).Status);
        });
    }

    [TestMethod]
    public void StaffAccessToServiceRequestIsDeniedWhenRiderRecordIsMissing()
    {
        WithSqlStore(store =>
        {
            const string missingIqama = "2000000951";
            const string reqId = "sreq:orphan-rider-test";
            store.Write((c, tx) =>
            {
                Store.Save(c, RiderAsks.ServiceKind, new()
                {
                    ["id"] = reqId,
                    ["requestType"] = "appointment",
                    ["riderIqama"] = missingIqama,
                    ["status"] = "open",
                    ["titleAr"] = "طلب موعد",
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
                }, tx);
                return true;
            });

            var scopedReviewer = UserAccess.Principal(new()
            {
                ["id"] = "syn-scope-rev",
                ["username"] = "syn-scope-rev",
                ["role"] = "Reviewer",
                ["customPermissions"] = new JsonArray("forms.review", "mobile.broadcast"),
                ["financialScope"] = new JsonObject { ["sponsorIds"] = new JsonArray(FinancialCore.ResolveSponsor("BW").Id) }
            });

            Assert.AreEqual(404, Assert.ThrowsExactly<ApiError>(() =>
                store.Write((c, tx) => RiderAsks.StaffReplyServiceRequest(c, tx, scopedReviewer, reqId, new() { ["message"] = "رد" }))).Status);
            Assert.AreEqual(404, Assert.ThrowsExactly<ApiError>(() =>
                store.Write((c, tx) => RiderAsks.DecideServiceRequest(c, tx, Admin, reqId, new() { ["action"] = "approve" }))).Status);
        });
    }

    [TestMethod]
    public void FormsCatalogAndSubmitFilterByCompanyAndTarget()
    {
        WithSqlStore(store =>
        {
            const string riderBw = "2000000961";
            const string riderEx = "2000000962";
            const string tokEx = "rmob_" + "7777777777777777777777777777777777777777777777777777777777777777";
            const string bwCampTok = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
            const string openCampTok = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
            const string staffFormTok = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
            const string iqamaFormTok = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
            string bwId = FinancialCore.ResolveSponsor("BW").Id;
            string exId = FinancialCore.Sponsors.First(s => s.Id != bwId).Id;

            store.Write((c, tx) =>
            {
                Store.Save(c, "rider", new() { ["id"] = riderBw, ["iqama"] = riderBw, ["name"] = "مندوب البوابة", ["status"] = "Active", ["financeSponsorId"] = bwId }, tx);
                Store.Save(c, "rider", new() { ["id"] = riderEx, ["iqama"] = riderEx, ["name"] = "مندوب شركة أخرى", ["status"] = "Active", ["financeSponsorId"] = exId }, tx);
                Store.Save(c, MobileSessions.Kind, new()
                {
                    ["id"] = "mobile:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokEx))),
                    ["iqama"] = riderEx,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O")
                }, tx);
                string exp = DateTimeOffset.UtcNow.AddDays(7).ToString("O");
                Store.Save(c, "campaign", new() { ["id"] = "camp-bw-only", ["title"] = "حملة شركة البوابة", ["status"] = "open", ["sharingToken"] = bwCampTok, ["expiresAt"] = exp, ["companyId"] = bwId }, tx);
                Store.Save(c, "campaign", new() { ["id"] = "camp-all", ["title"] = "حملة عامة", ["status"] = "open", ["sharingToken"] = openCampTok, ["expiresAt"] = exp }, tx);
                Store.Save(c, "custom_form", new() { ["id"] = "qf-staff", ["titleAr"] = "نموذج موظفين", ["status"] = "open", ["sharingToken"] = staffFormTok, ["expiresAt"] = exp, ["target"] = "staff", ["fields"] = new JsonArray() }, tx);
                Store.Save(c, "custom_form", new() { ["id"] = "qf-bw-rider", ["titleAr"] = "نموذج فردي", ["status"] = "open", ["sharingToken"] = iqamaFormTok, ["expiresAt"] = exp, ["targetIqama"] = riderBw, ["fields"] = new JsonArray() }, tx);
                return true;
            });

            var bwForms = store.Read(c => RiderAsks.FormSchemasCatalog(c, riderBw))["activeCampaigns"]!.AsArray().OfType<JsonObject>().Select(x => x.S("id")).ToHashSet();
            Assert.IsTrue(bwForms.Contains("camp-bw-only"));
            Assert.IsTrue(bwForms.Contains("camp-all"));
            Assert.IsTrue(bwForms.Contains("qf-bw-rider"));
            Assert.IsFalse(bwForms.Contains("qf-staff"));

            var exForms = store.Read(c => RiderAsks.FormSchemasCatalog(c, riderEx))["activeCampaigns"]!.AsArray().OfType<JsonObject>().Select(x => x.S("id")).ToHashSet();
            Assert.IsFalse(exForms.Contains("camp-bw-only"));
            Assert.IsTrue(exForms.Contains("camp-all"));
            Assert.IsFalse(exForms.Contains("qf-bw-rider"));
            Assert.IsFalse(exForms.Contains("qf-staff"));

            // Rider from another company cannot submit a company-targeted campaign by token
            var ctxEx = new DefaultHttpContext();
            ctxEx.Request.Headers.Authorization = "Bearer " + tokEx;
            Assert.AreEqual(404, Assert.ThrowsExactly<ApiError>(() =>
                RiderAsks.SubmitInAppForm(store, ctxEx, bwCampTok, new() { ["workSystem"] = "tiers" })).Status);
        });
    }
}
