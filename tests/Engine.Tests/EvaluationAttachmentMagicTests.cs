using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class EvaluationAttachmentMagicTests
{
    static readonly ClaimsPrincipal Admin = PayFixture.Maker;
    static JsonObject OpenRound(PayFixture f)
    {
        var opened = f.Store.Write((c, tx) =>
        {
            EvaluationSurvey.ImportStaff(c, tx, Admin, new()
            {
                ["staff"] = new JsonArray(new JsonObject
                {
                    ["name"] = "Synthetic staff", ["cities"] = new JsonArray("Synthetic city"),
                    ["departments"] = new JsonArray("Synthetic department"), ["roles"] = new JsonArray("Synthetic role"),
                    ["apps"] = new JsonArray("Ninja"), ["active"] = true
                })
            });
            var draft = EvaluationSurvey.CreateRound(c, tx, Admin, new() { ["title"] = "Synthetic magic round" });
            return EvaluationSurvey.Transition(c, tx, Admin, draft.S("id"), "open");
        });
        return f.MakeLegacyEvaluationRound(opened);
    }
    static JsonObject Reply(JsonObject round) => new()
    {
        ["iqama"] = "2000000091", ["name"] = "Synthetic rider", ["city"] = "Synthetic city", ["nationality"] = "Synthetic nationality",
        ["workApps"] = new JsonArray("Ninja"),
        ["staffAnswers"] = new JsonArray(round["staff"]!.AsArray().OfType<JsonObject>().Select(s => (JsonNode?)new JsonObject
        {
            ["staffId"] = s.S("id"),
            ["ratings"] = new JsonObject(EvaluationSurvey.StaffDimensions.Select(k => KeyValuePair.Create<string, JsonNode?>(k, JsonValue.Create(8))))
        }).ToArray()),
        ["appAnswers"] = new JsonArray(round["apps"]!.AsArray().Select(a => (JsonNode?)new JsonObject { ["app"] = a!.ToString(), ["reason"] = "not_worked" }).ToArray())
    };
    static byte[] Pad(byte[] magic, int min = 32)
    {
        if (magic.Length >= min) return magic;
        var all = new byte[min];
        Buffer.BlockCopy(magic, 0, all, 0, magic.Length);
        return all;
    }
    static readonly (string File, string Type, string Kind, byte[] Bytes)[] Samples =
    [
        ("a.jpg", "image/jpeg", "image", Pad([0xFF, 0xD8, 0xFF, 0xE0])),
        ("a.png", "image/png", "image", Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])),
        ("a.webp", "image/webp", "image", Pad(Encoding.ASCII.GetBytes("RIFF").Concat(new byte[] { 16, 0, 0, 0 }).Concat(Encoding.ASCII.GetBytes("WEBP")).ToArray())),
        ("a.webm", "audio/webm", "voice", Pad([0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x02, 0x03, 0x04])),
        ("a.ogg", "audio/ogg", "voice", Pad(Encoding.ASCII.GetBytes("OggS").Concat(new byte[] { 0, 2, 0, 0 }).ToArray())),
        ("a.mp3", "audio/mpeg", "voice", Pad(Encoding.ASCII.GetBytes("ID3").Concat(new byte[] { 3, 0, 0, 0 }).ToArray())),
        ("b.mp3", "audio/mpeg", "voice", Pad([0xFF, 0xFB, 0x10, 0x00])),
        ("c.mp3", "audio/mpeg", "voice", Pad([0xFF, 0xF3, 0x10, 0x00])),
        ("d.mp3", "audio/mpeg", "voice", Pad([0xFF, 0xF2, 0x10, 0x00])),
        ("a.m4a", "audio/mp4", "voice", Pad(new byte[] { 0, 0, 0, 0x18 }.Concat(Encoding.ASCII.GetBytes("ftyp")).Concat(Encoding.ASCII.GetBytes("M4A ")).ToArray())),
        ("a.aac", "audio/aac", "voice", Pad(new byte[] { 0, 0, 0, 0x18 }.Concat(Encoding.ASCII.GetBytes("ftyp")).Concat(Encoding.ASCII.GetBytes("mp42")).ToArray())),
        ("a.wav", "audio/wav", "voice", Pad(Encoding.ASCII.GetBytes("RIFF").Concat(new byte[] { 16, 0, 0, 0 }).Concat(Encoding.ASCII.GetBytes("WAVE")).ToArray())),
    ];

    [TestMethod]
    public void MagicBytesAcceptEachSupportedTypeAndRejectMismatch()
    {
        using var f = new PayFixture();
        foreach (var sample in Samples)
        {
            var round = OpenRound(f);
            var input = Reply(round);
            var att = new JsonObject
            {
                ["kind"] = sample.Kind, ["fileName"] = sample.File, ["contentType"] = sample.Type,
                ["dataBase64"] = Convert.ToBase64String(sample.Bytes)
            };
            if (sample.Kind == "voice") att["durationSeconds"] = 8;
            input["staffAnswers"]![0]!["attachments"] = new JsonArray(att);
            EvaluationSurvey.WriteWithAttachments(f.Store, (c, tx, batch) => EvaluationSurvey.Submit(c, tx, round.S("sharingToken"), input, store: f.Store, attachments: batch));
        }

        var mismatchRound = OpenRound(f);
        var bad = Reply(mismatchRound);
        bad["staffAnswers"]![0]!["attachments"] = new JsonArray(new JsonObject
        {
            ["kind"] = "image", ["fileName"] = "fake.png", ["contentType"] = "image/png",
            ["dataBase64"] = Convert.ToBase64String(Pad([0xFF, 0xD8, 0xFF, 0xE0]))
        });
        var ex = Assert.ThrowsExactly<ApiError>(() => EvaluationSurvey.WriteWithAttachments(f.Store, (c, tx, batch) => EvaluationSurvey.Submit(c, tx, mismatchRound.S("sharingToken"), bad, store: f.Store, attachments: batch)));
        Assert.AreEqual("attachment_type", ex.Code);
    }

    [TestMethod]
    public async Task PlaybackSendsNosniffAndInlineSafeContentDisposition()
    {
        using var f = new PayFixture();
        var round = OpenRound(f);
        var input = Reply(round);
        byte[] png = Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        input["staffAnswers"]![0]!["attachments"] = new JsonArray(new JsonObject
        {
            ["kind"] = "image", ["fileName"] = "evil\"name\r\n.png", ["contentType"] = "image/png",
            ["dataBase64"] = Convert.ToBase64String(png)
        });
        EvaluationSurvey.WriteWithAttachments(f.Store, (c, tx, batch) => EvaluationSurvey.Submit(c, tx, round.S("sharingToken"), input, store: f.Store, attachments: batch));
        var meta = f.Store.Read(c => Store.List(c, EvaluationSurvey.AttachmentKind)).Single();
        var result = f.Store.Read(c => EvaluationSurvey.PlayAttachment(f.Store, c, Admin, meta.S("id")));
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        await result.ExecuteAsync(ctx);
        Assert.AreEqual("nosniff", ctx.Response.Headers["X-Content-Type-Options"].ToString());
        string disposition = ctx.Response.Headers.ContentDisposition.ToString();
        Assert.IsTrue(disposition.StartsWith("inline", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(disposition.Contains('\r') || disposition.Contains('\n') || disposition.Contains('"'));
        Assert.AreEqual("image/png", ctx.Response.ContentType);
    }
}
