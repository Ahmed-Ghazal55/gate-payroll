using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PaySettings
{
    public static JsonObject DefaultSchemesCatalog()
        => JsonNode.Parse(typeof(PaySettings).Assembly.GetManifestResourceStream("Payroll.PayDefaultSchemes.v1.json")!)!.AsObject();

    /// <summary>
    /// Seed default app schemes (and Ninja parent contract terms) as unapproved drafts.
    /// Maker-checker is always preserved: every created document stays status="draft" for independent approval.
    /// </summary>
    public static JsonObject SeedDefaults(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string app, string? period = null)
    {
        app = PayPlatforms.App(app);
        // NSET1: Ninja-only seed path (Keeta work system = form responses later; no SYS Keeta gates in catalog).
        J.Need(app == "ninja", "استيراد الأنظمة الافتراضية متاح لنينجا فقط في هذا الإصدار.");
        Scope(actor, "pay.manage", app, true);
        if (!AccessPolicy.Administrator(actor))
            throw new ApiError("استيراد الأنظمة الافتراضية متاح لمسؤول النظام فقط.", 403);
        string resolvedPeriod = J.Period(string.IsNullOrWhiteSpace(period) ? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM") : period);
        var catalog = DefaultSchemesCatalog();
        var appEntry = catalog[app] as JsonObject
            ?? throw new ApiError($"لا توجد قوالب أنظمة افتراضية مسجلة لتطبيق {app}.", 404);

        var drafts = new JsonArray();
        int created = 0, existing = 0;

        JsonObject EnsureDraft(string kind, string code, JsonObject data)
        {
            var found = Store.List(c, kind, tx)
                .Where(d => d.S("app") == app && d.S("period") == resolvedPeriod && d.S("code") == code && d.S("status") != "rejected")
                .OrderByDescending(d => d.N("version"))
                .FirstOrDefault();
            if (found != null)
            {
                existing++;
                return found.Clone();
            }
            var createdDoc = Create(c, tx, actor, kind, app, new JsonObject
            {
                ["period"] = resolvedPeriod,
                ["code"] = code,
                ["data"] = data.DeepClone().AsObject()
            });
            created++;
            return createdDoc.Clone();
        }

        var eligibilityByCode = new Dictionary<string, string>(StringComparer.Ordinal);
        if (appEntry["eligibility"] is JsonArray eligibilities)
        {
            foreach (var item in eligibilities.OfType<JsonObject>())
            {
                string code = item.Required("code");
                var data = PayValues.Object(item["data"]);
                var doc = EnsureDraft("pay_eligibility", code, data);
                eligibilityByCode[code] = doc.S("id");
                drafts.Add(doc);
            }
        }

        if (app == "ninja")
        {
            drafts.Add(EnsureDraft("pay_terms", "default", NinjaBilling.Seed()));
        }

        if (appEntry["schemes"] is JsonArray schemes)
        {
            foreach (var item in schemes.OfType<JsonObject>())
            {
                string code = item.Required("code");
                var data = PayValues.Object(item["data"]).DeepClone().AsObject();
                string eligCode = item.S("eligibilityCode");
                if (eligCode != "" && data.S("eligibilitySetId") == "")
                {
                    J.Need(eligibilityByCode.TryGetValue(eligCode, out string? eligId) && eligId != "",
                        $"شروط الأهلية الافتراضية ({eligCode}) غير مهيأة.");
                    data["eligibilitySetId"] = eligId;
                }
                drafts.Add(EnsureDraft("pay_scheme", code, data));
            }
        }

        return new JsonObject
        {
            ["app"] = app,
            ["period"] = resolvedPeriod,
            ["drafts"] = drafts,
            ["created"] = created,
            ["existing"] = existing
        };
    }
}
