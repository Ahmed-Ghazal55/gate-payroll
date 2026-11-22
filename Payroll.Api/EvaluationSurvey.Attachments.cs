using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;

namespace Payroll;

public static partial class EvaluationSurvey
{
    public const string AttachmentKind = "evaluation_attachment";
    public static readonly TimeSpan AttachmentRetention = TimeSpan.FromDays(183); // ~6 months
    static readonly HashSet<string> ImageTypes = new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/jpg", "image/png", "image/webp" };
    static readonly HashSet<string> VoiceTypes = new(StringComparer.OrdinalIgnoreCase) { "audio/webm", "audio/ogg", "audio/mpeg", "audio/mp4", "audio/wav", "audio/x-wav", "audio/aac" };
    static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
    static readonly HashSet<string> VoiceExt = new(StringComparer.OrdinalIgnoreCase) { ".webm", ".ogg", ".mp3", ".m4a", ".wav", ".aac" };

    static JsonArray PersistAttachments(Store store, DbConnection c, DbTransaction tx, string replyId, string cardKey, string cardId, JsonObject card, ref long replyBytes)
    {
        var raw = card["attachments"] as JsonArray ?? new JsonArray();
        J.Need(raw.Count <= 3 && raw.All(x => x is JsonObject), "مرفقات البطاقة غير صالحة.", "attachment_invalid");
        int images = 0, voices = 0;
        var clean = new JsonArray();
        foreach (var node in raw.OfType<JsonObject>())
        {
            string kind = node.S("kind");
            J.Need(kind is "image" or "voice", "نوع المرفق غير مدعوم.", "attachment_kind");
            if (kind == "image") { images++; J.Need(images <= 2, "حد أقصى صورتان لكل بطاقة.", "attachment_image_limit"); }
            else { voices++; J.Need(voices <= 1, "حد أقصى تسجيل صوتي واحد لكل بطاقة.", "attachment_voice_limit"); }

            string fileName = Path.GetFileName(node.Required("fileName"));
            J.Need(fileName.Length is > 0 and <= 200 && !fileName.Contains('\\') && !fileName.Contains('/'), "اسم الملف غير صالح.", "attachment_name");
            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            string contentType = node.S("contentType");
            if (kind == "image")
            {
                J.Need(ImageExt.Contains(extension) && ImageTypes.Contains(contentType), "الصور JPEG أو PNG أو WebP فقط.", "attachment_type");
            }
            else
            {
                J.Need(VoiceExt.Contains(extension) && VoiceTypes.Contains(contentType), "الصوت WebM أو OGG أو MP3 أو WAV أو AAC فقط.", "attachment_type");
                J.Need(node["durationSeconds"] is JsonValue && node.N("durationSeconds") is >= 1 and <= 60, "التسجيل الصوتي حتى 60 ثانية.", "attachment_voice_duration");
            }

            string b64 = node.S("dataBase64");
            J.Need(b64.Length is > 0 and <= 9 * 1024 * 1024, "المرفق طويل جدًا.", "attachment_too_large");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(b64); }
            catch (FormatException) { throw new ApiError("بيانات المرفق غير صالحة.", 400, "attachment_invalid"); }
            int maxBytes = kind == "image" ? 512 * 1024 : 1024 * 1024;
            J.Need(bytes.Length > 0 && bytes.Length <= maxBytes, "حجم المرفق خارج الحد.", "attachment_too_large");
            replyBytes += bytes.Length;
            J.Need(replyBytes <= 6L * 1024 * 1024, "حجم الرد كاملًا حتى 6 ميجابايت.", "reply_too_large");

            string id = "evatt:" + Guid.NewGuid().ToString("N");
            string relative = Path.Combine("evaluation-attachments", replyId.Replace(':', '_'), id + extension);
            string path = Path.Combine(store.DirectoryPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);

            var meta = new JsonObject
            {
                ["id"] = id, ["replyId"] = replyId, ["cardKey"] = cardKey, ["cardId"] = cardId,
                ["kind"] = kind, ["fileName"] = fileName, ["contentType"] = contentType == "image/jpg" ? "image/jpeg" : contentType,
                ["sizeBytes"] = bytes.Length, ["relativePath"] = relative, ["createdAt"] = Now()
            };
            if (kind == "voice") meta["durationSeconds"] = (int)node.N("durationSeconds");
            Store.Save(c, AttachmentKind, meta, tx);

            var shown = new JsonObject
            {
                ["id"] = id, ["kind"] = kind, ["fileName"] = fileName, ["contentType"] = meta.S("contentType"),
                ["sizeBytes"] = bytes.Length, ["createdAt"] = meta.S("createdAt"),
                ["playbackUrl"] = $"/api/forms/evaluations/attachments/{id}"
            };
            if (kind == "voice") shown["durationSeconds"] = meta["durationSeconds"]!.DeepClone();
            clean.Add(shown);
        }
        return clean;
    }

    public static IResult PlayAttachment(Store store, DbConnection c, ClaimsPrincipal user, string id)
    {
        Demand(user);
        var meta = Store.Get(c, id, AttachmentKind) ?? throw new ApiError("المرفق غير موجود.", 404, "not_found");
        var reply = Store.Get(c, meta.S("replyId"), ResponseKind) ?? throw new ApiError("المرفق غير موجود.", 404, "not_found");
        J.Need(InScope(c, user, reply), "ليست لديك صلاحية لهذا المرفق.", "forbidden", 403);
        string basePath = Path.GetFullPath(Path.Combine(store.DirectoryPath, "evaluation-attachments")) + Path.DirectorySeparatorChar;
        string file = Path.GetFullPath(Path.Combine(store.DirectoryPath, meta.S("relativePath")));
        if (!file.StartsWith(basePath, StringComparison.Ordinal) || !File.Exists(file))
            throw new ApiError("الملف غير موجود.", 404, "not_found");
        return Results.File(file, meta.S("contentType"), meta.S("fileName"));
    }

    public static int PurgeExpiredAttachments(Store store, DateTimeOffset now)
    {
        var cutoff = now - AttachmentRetention;
        int removed = 0;
        store.Write((c, tx) =>
        {
            foreach (var att in Store.List(c, AttachmentKind, tx).ToList())
            {
                if (att.S("status") == "purged") continue;
                if (!DateTimeOffset.TryParse(att.S("createdAt"), out var created) || created > cutoff) continue;
                string path = Path.Combine(store.DirectoryPath, att.S("relativePath"));
                if (File.Exists(path)) File.Delete(path);
                var reply = Store.Get(c, att.S("replyId"), ResponseKind, tx);
                if (reply != null)
                {
                    foreach (string field in new[] { "staffAnswers", "appAnswers" })
                    {
                        foreach (var card in reply[field]!.AsArray().OfType<JsonObject>())
                        {
                            if (card["attachments"] is not JsonArray arr) continue;
                            var kept = new JsonArray();
                            foreach (var node in arr.OfType<JsonObject>())
                                if (node.S("id") != att.S("id")) kept.Add(node.DeepClone());
                            card["attachments"] = kept;
                        }
                    }
                    Store.Save(c, ResponseKind, reply, tx);
                }
                using var del = Store.Command(c, tx, "DELETE FROM records WHERE id=@id AND kind=@kind", ("@id", att.S("id")), ("@kind", AttachmentKind));
                del.ExecuteNonQuery();
                removed++;
            }
            if (removed > 0) Store.Audit(c, tx, "evaluation.attachments_purged", removed.ToString(), "system");
            return removed;
        });
        return removed;
    }
}

/// <summary>Hosted timer (no Hangfire): purge evaluation attachments older than six months.</summary>
public sealed class EvaluationAttachmentRetentionService(Store store, ILogger<EvaluationAttachmentRetentionService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(12));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                int n = EvaluationSurvey.PurgeExpiredAttachments(store, DateTimeOffset.UtcNow);
                if (n > 0) log.LogInformation("Purged {Count} expired evaluation attachments", n);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Evaluation attachment retention failed");
            }
        }
    }
}
