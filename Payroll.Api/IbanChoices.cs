using System.Data.Common;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Payroll;

public sealed record IbanAnswer(string Iqama, string Name, string Phone, string City, string Iban, string BankCode, string[] Platforms);

// IBAN collection campaign (0.9.1). Approval only marks the answer reviewed: it never writes the rider's
// verifiedIban, so the bank file is unchanged until the deferred comparison with approved IBANs.
public static class IbanChoices
{
    public const string Kind = "iban", Schema = "iban-v1";
    // Banks that open individual accounts and receive salary transfers (Ghazal 2026-11-02). Any other code is refused.
    public static readonly string[] SalaryBanks = ["80", "10", "05", "20", "15", "45", "30", "55", "60", "65", "90", "78", "36", "93", "95"];
    static readonly string[] Retired = ["40", "50"];
    public static readonly string[] Platforms = ["keeta", "hungerstation", "ninja", "amazon", "chefz", "jahez", "other"];
    static readonly string[] Keys = ["iqama", "name", "phone", "city", "iban", "bankCode", "platforms"];
    static readonly string[] Snapshot = ["name", "phone", "city", "iban", "bankCode", "bank"];
    public static bool Is(JsonObject campaign) => campaign.S("kind") == Kind;
    static readonly JsonArray DefaultCities = [new JsonObject { ["ar"] = "الرياض", ["en"] = "Riyadh" }, new JsonObject { ["ar"] = "جدة", ["en"] = "Jeddah" }];
    // Riders pick the city from the campaign's own list (set by the administrator); free text is refused.
    public static JsonArray Cities(JsonObject campaign) => (campaign["cities"] as JsonArray ?? DefaultCities).DeepClone().AsArray();
    public static JsonArray ValidateCities(JsonNode? value)
    {
        J.Need(value is JsonArray, "قائمة المدن غير صحيحة.");
        var cities = ((JsonArray)value!).Select(x => x as JsonObject).ToList();
        J.Need(cities.Count is >= 1 and <= 60 && cities.All(c => c != null), "أدخل من مدينة إلى60 مدينة.");
        var result = new JsonArray();
        foreach (var c in cities)
        {
            string ar = c!.S("ar").Trim(), en = c.S("en").Trim();
            J.Need(ar.Length is >= 2 and <= 60 && en.Length <= 60 && !(ar + en).Any(char.IsControl), "اسم المدينة بالعربي مطلوب وبحد أقصى60حرفًا.");
            result.Add(new JsonObject { ["ar"] = ar, ["en"] = en == "" ? ar : en });
        }
        J.Need(result.Select(c => c!.S("ar")).Distinct().Count() == result.Count, "يوجد اسم مدينة مكرر.");
        return result;
    }

    public static string NormalizeIban(string value)
    {
        // Pasted IBANs may carry spaces, dashes, dots, direction marks or an "IBAN:" label.
        string s = Regex.Replace(J.Digits(value).ToUpperInvariant(), @"[^0-9A-Z]", "");
        if (s.StartsWith("IBAN", StringComparison.Ordinal)) s = s[4..];
        return Regex.IsMatch(s, "^[0-9]{22}$") ? "SA" + s : s;
    }
    public static IbanAnswer Validate(JsonObject input, JsonObject campaign, bool edit = false)
    {
        J.Need(input.All(p => Keys.Contains(p.Key) || (edit && p.Key is "version" or "receipt")), "حقول نموذج الآيبان غير صحيحة.");
        var identity = WorkChoices.Identity(input);
        string phone = RiderDirectory.Phone(input.Required("phone"));
        string city = input.Required("city").Trim();
        J.Need(Cities(campaign).Any(c => c!.S("ar") == city), "اختر المدينة من القائمة.");
        string iban = J.Iban(NormalizeIban(input.Required("iban")));
        string code = iban.Substring(4, 2), chosen = input.S("bankCode").Trim();
        J.Need(!Retired.Contains(code), "هذا الآيبان من بنك اندمج في بنك آخر ولم يعد يستقبل التحويلات؛ اطلب آيبانك الجديد من البنك.");
        J.Need(SalaryBanks.Contains(code), "بنك هذا الآيبان غير مقبول لاستلام الرواتب في هذا النموذج؛ استخدم آيبان حساب في أحد البنوك المدرجة.");
        J.Need(chosen != "", "اختر البنك من القائمة.");
        J.Need(chosen == code, "البنك المختار لا يطابق رقم الآيبان؛ راجع الآيبان أو اختر البنك الصحيح.");
        J.Need(input["platforms"] is JsonArray, "اختر تطبيق عمل واحدًا على الأقل.");
        string[] platforms = ((JsonArray)input["platforms"]!).Select(x => x?.ToString() ?? "").ToArray();
        J.Need(platforms.Length is >= 1 && platforms.Length <= Platforms.Length && platforms.Distinct().Count() == platforms.Length && platforms.All(Platforms.Contains), "اختر تطبيق عمل واحدًا على الأقل من القائمة.");
        return new(identity.Iqama, identity.Name, phone, city, iban, code, Platforms.Where(platforms.Contains).ToArray());
    }
    static void Apply(JsonObject d, IbanAnswer a)
    {
        d["name"] = a.Name; d["phone"] = a.Phone; d["city"] = a.City; d["iban"] = a.Iban; d["bankCode"] = a.BankCode;
        d["bank"] = BankPayrollFiles.BankFor(a.BankCode); d["platforms"] = new JsonArray(a.Platforms.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
    }
    static bool Same(JsonObject d, IbanAnswer a) =>
        d.S("name") == a.Name && d.S("phone") == a.Phone && d.S("city") == a.City && d.S("iban") == a.Iban &&
        string.Join(",", (d["platforms"] as JsonArray)?.Select(x => x?.ToString()) ?? []) == string.Join(",", a.Platforms);
    static string Last4(JsonObject d) => d.S("iban").Length >= 4 ? d.S("iban")[^4..] : "";
    static JsonArray PlatformsOf(JsonObject d) => (d["platforms"] as JsonArray)?.DeepClone().AsArray() ?? new JsonArray();

    // Public lookup by Iqama: never return the full IBAN or the phone number.
    public static JsonObject View(JsonObject d) => new()
    {
        ["alreadySubmitted"] = true, ["iqama"] = d.S("iqama"), ["name"] = d.S("name"), ["city"] = d.S("city"), ["bankCode"] = d.S("bankCode"),
        ["platforms"] = PlatformsOf(d), ["ibanLast4"] = Last4(d), ["version"] = SubmissionChoices.Version(d), ["schema"] = Schema
    };
    static JsonObject Receipt(JsonObject d) => new()
    {
        ["receipt"] = d.S("id"), ["iqama"] = d.S("iqama"), ["name"] = d.S("name"), ["ibanLast4"] = Last4(d), ["bankCode"] = d.S("bankCode"), ["platforms"] = PlatformsOf(d),
        ["isEdited"] = d.B("isEdited"), ["version"] = SubmissionChoices.Version(d), ["status"] = d.S("status"), ["submittedAt"] = d.S("updatedAt", d.S("createdAt")), ["schema"] = Schema
    };
    public static IResult Create(Store store, JsonObject campaign, JsonObject input)
    {
        var answer = Validate(input, campaign);
        return store.Write<IResult>((c, tx) =>
        {
            campaign = SubmissionChoices.Open(c, tx, campaign); var existing = SubmissionChoices.Find(c, tx, campaign, answer.Iqama);
            if (existing != null) return Results.Json(View(existing), statusCode: 409);
            var d = new JsonObject { ["id"] = Guid.NewGuid().ToString("N"), ["campaignId"] = campaign.S("id"), ["schema"] = Schema, ["iqama"] = answer.Iqama, ["status"] = "pending", ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"), ["version"] = 1, ["isEdited"] = false, ["history"] = new JsonArray() };
            Apply(d, answer); Store.Save(c, "submission", d, tx); Store.Audit(c, tx, "submission.received", d.S("id"), "public-form"); return Results.Ok(Receipt(d));
        });
    }
    public static IResult Edit(Store store, JsonObject campaign, JsonObject input)
    {
        var answer = Validate(input, campaign, edit: true);
        return store.Write<IResult>((c, tx) =>
        {
            campaign = SubmissionChoices.Open(c, tx, campaign); var d = SubmissionChoices.Find(c, tx, campaign, answer.Iqama) ?? throw new ApiError("لم يتم العثور على رد سابق لتعديله.", 404);
            J.Need(input.S("receipt") == "" || input.S("receipt") == d.S("id"), "رقم الإيصال لا يطابق الإقامة والحملة."); SubmissionChoices.CheckVersion(d, input);
            if (!Same(d, answer))
            {
                var history = d["history"] as JsonArray ?? new JsonArray(); d["history"] = history;
                var entry = new JsonObject { ["submittedAt"] = d.S("updatedAt", d.S("createdAt")), ["version"] = SubmissionChoices.Version(d), ["status"] = d.S("status"), ["reviewNote"] = d.S("reviewNote"), ["reviewedAt"] = d.S("reviewedAt"), ["reviewedBy"] = d.S("reviewedBy"), ["platforms"] = PlatformsOf(d) };
                foreach (string field in Snapshot) entry[field] = d.S(field);
                history.Add(entry); Apply(d, answer);
                d["version"] = SubmissionChoices.Version(d) + 1; d["isEdited"] = true; d["status"] = "pending"; d["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
                foreach (string field in new[] { "reviewNote", "reviewedAt", "reviewedBy" }) d.Remove(field);
                Store.Audit(c, tx, "submission.edited", d.S("id"), "public-form"); Store.Save(c, "submission", d, tx);
            }
            return Results.Ok(Receipt(d));
        });
    }
    public static object[] Distribution(List<JsonObject> rows, Func<JsonObject, IEnumerable<string>> keys) =>
        rows.SelectMany(keys).GroupBy(x => x).Select(g => (object)new { value = g.Key, count = g.Count(), percentage = rows.Count == 0 ? 0 : Math.Round(100m * g.Count() / rows.Count, 2) }).ToArray();
}
