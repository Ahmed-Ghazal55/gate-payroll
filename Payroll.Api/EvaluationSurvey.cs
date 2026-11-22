using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Payroll;

// A specialized form backed by the existing document store. No staff data is embedded in source.
public static partial class EvaluationSurvey
{
    public const string StaffKind = "evaluation_staff", RoundKind = "evaluation_round", ResponseKind = "evaluation_response";
    public const string ViewPermission = "forms.evaluations.view", ExportPermission = "forms.evaluations.export";
    public static readonly string[] StaffDimensions = ["professional", "treatment", "support", "responseSpeed", "problemSolving"];
    public static readonly string[] StaffReasons = ["not_dealt", "unknown_person", "not_worked_app"];
    public static readonly string[] AppReasons = ["unavailable_city", "not_worked"];
    // The spec defines these as reasons, not additional numeric rating scales.
    public static readonly string[] Factors = ["orderVolume", "fairPay", "workingHours", "shiftTimes", "flexibility", "customerService", "appProblems", "worthIt"];
    static readonly Dictionary<string, string> NationalityAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["saudi"] = "saudi", ["سعودي"] = "saudi", ["سعودی"] = "saudi",
        ["egyptian"] = "egyptian", ["مصري"] = "egyptian", ["مصری"] = "egyptian",
        ["yemeni"] = "yemeni", ["يمني"] = "yemeni", ["یمنی"] = "yemeni",
        ["sudanese"] = "sudanese", ["سوداني"] = "sudanese", ["سوڈانی"] = "sudanese",
        ["pakistani"] = "pakistani", ["باكستاني"] = "pakistani", ["پاکستانی"] = "pakistani",
        ["indian"] = "indian", ["هندي"] = "indian", ["بھارتی"] = "indian",
        ["bangladeshi"] = "bangladeshi", ["بنغلاديشي"] = "bangladeshi", ["بنگلہ دیشی"] = "bangladeshi",
        ["syrian"] = "syrian", ["سوري"] = "syrian", ["شامی"] = "syrian",
        ["jordanian"] = "jordanian", ["أردني"] = "jordanian", ["اردنی"] = "jordanian",
        ["filipino"] = "filipino", ["فلبيني"] = "filipino", ["فلپائنی"] = "filipino"
    };
    static string Now() => DateTimeOffset.UtcNow.ToString("O");
    static JsonArray Array(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
    static JsonArray Array(IEnumerable<JsonObject> values) => new(values.Select(v => (JsonNode?)v.Clone()).ToArray());
    public static string ReplyId(string roundId, string iqama) =>
        "evreply:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(roundId + ":" + iqama))).ToLowerInvariant();
    public static string CanonicalNationality(string raw, out string display)
    {
        string value = raw.Trim();
        if (NationalityAliases.TryGetValue(value, out var id))
        {
            display = id switch
            {
                "saudi" => "Saudi", "egyptian" => "Egyptian", "yemeni" => "Yemeni", "sudanese" => "Sudanese",
                "pakistani" => "Pakistani", "indian" => "Indian", "bangladeshi" => "Bangladeshi",
                "syrian" => "Syrian", "jordanian" => "Jordanian", "filipino" => "Filipino", _ => value
            };
            return id;
        }
        // Unknown free-text: stable slug from normalized text so languages that share a spelling still group.
        display = value;
        string slug = string.Concat(value.Normalize().ToLowerInvariant().Where(ch => !char.IsWhiteSpace(ch)));
        return string.IsNullOrEmpty(slug) ? "unknown" : "other:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(slug))).ToLowerInvariant()[..16];
    }
    static string Text(JsonObject input, string key, int max, bool required = false)
    {
        string value = input.S(key);
        J.Need(value.Length <= max && (!required || value.Length > 0) && !value.Any(ch => char.IsControl(ch) && ch is not '\n' and not '\r'), "بيانات غير صالحة: " + key, "invalid_field");
        return value;
    }
    static string[] Strings(JsonObject input, string key, int max = 100)
    {
        if (input[key] == null) return [];
        J.Need(input[key] is JsonArray, "قائمة غير صالحة: " + key);
        var a = (JsonArray)input[key]!;
        J.Need(a.Count <= max && a.All(v => v is JsonValue j && j.TryGetValue<string>(out _)), "قائمة غير صالحة: " + key);
        var result = a.Select(v => v!.ToString().Trim()).ToArray();
        J.Need(result.All(v => v.Length is > 0 and <= 200 && !v.Any(char.IsControl)), "قيمة غير صالحة: " + key);
        return result.Distinct(StringComparer.Ordinal).ToArray();
    }
    public static bool IsSupervisor(ClaimsPrincipal user) => user.FindAll(ClaimTypes.Role).Any(c => c.Value.Contains("Supervisor", StringComparison.OrdinalIgnoreCase));
    public static bool CanView(ClaimsPrincipal user) => !IsSupervisor(user) && AccessPolicy.Has(user, ViewPermission);
    static void Demand(ClaimsPrincipal user, bool manage = false)
    {
        if (!CanView(user)) throw new ApiError("التقييمات سرية ومتاحة للإدارة المخولة فقط.", 403);
        if (manage) AccessPolicy.Demand(user, "forms.manage");
    }
    public static JsonObject ImportStaff(DbConnection c, DbTransaction tx, ClaimsPrincipal user, JsonObject input)
    {
        Demand(user, true);
        var items = input["staff"] as JsonArray ?? throw new ApiError("قائمة الموظفين مطلوبة.");
        J.Need(items.Count is > 0 and <= 500, "حجم قائمة الموظفين غير صالح.");
        var imported = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var raw = item as JsonObject ?? throw new ApiError("موظف غير صالح.");
            string name = Text(raw, "name", 200, true);
            // Seed has no ids: deterministic keys make repeat imports idempotent and merge multi-role people.
            string id = "evstaff:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name.Normalize().ToUpperInvariant()))).ToLowerInvariant();
            var prior = imported.GetValueOrDefault(id) ?? Store.Get(c, id, StaffKind, tx);
            var doc = new JsonObject { ["id"] = id, ["name"] = name, ["active"] = raw.NullableBool("active") ?? true, ["excluded"] = raw.B("excluded"), ["pendingRoleApproval"] = raw.B("pendingRoleApproval") };
            foreach (string field in new[] { "cities", "departments", "roles", "apps" })
                doc[field] = Array(Strings(raw, field).Concat(prior == null ? [] : Strings(prior, field)).Distinct(StringComparer.Ordinal));
            J.Need(Strings(doc, "apps").All(a => PayPlatforms.Table.Any(p => p.Platform == a)), "استخدم معرفات المنصات المعتمدة في ملف الاستيراد.");
            J.Need(Strings(doc, "cities").Length > 0, "مدينة الموظف مطلوبة.");
            if (doc.B("pendingRoleApproval")) doc["roles"] = new JsonArray("بانتظار موافقة غزال");
            imported[id] = doc;
        }
        foreach (var doc in imported.Values) Store.Save(c, StaffKind, doc, tx);
        Store.Audit(c, tx, "evaluation.staff_imported", imported.Count.ToString(), user.Identity!.Name!);
        return new() { ["imported"] = imported.Count, ["excluded"] = imported.Values.Count(s => s.B("excluded")) };
    }
    public static JsonObject Round(DbConnection c, string id, DbTransaction? tx = null) =>
        Store.Get(c, id, RoundKind, tx) ?? throw new ApiError("جولة التقييم غير موجودة.", 404);
    public static JsonObject CreateRound(DbConnection c, DbTransaction tx, ClaimsPrincipal user, JsonObject input)
    {
        Demand(user, true);
        var staff = Store.List(c, StaffKind, tx).Where(s => (s.NullableBool("active") ?? true) && !s.B("excluded")).ToList();
        J.Need(staff.Count > 0, "استورد قائمة الموظفين أولًا.");
        var round = new JsonObject
        {
            ["id"] = "evround:" + Guid.NewGuid().ToString("N"), ["kind"] = "evaluation", ["schema"] = "evaluation-v1",
            ["title"] = Text(input, "title", 200, true), ["status"] = "draft", ["createdAt"] = Now(),
            ["sharingToken"] = CampaignLinks.NewToken(), ["staff"] = Array(staff),
            ["apps"] = Array(PayPlatforms.Table.Select(a => a.Platform)), ["cities"] = Array(staff.SelectMany(s => Strings(s, "cities")).Distinct()),
            ["schemaVersion"] = 1
        };
        Store.Save(c, RoundKind, round, tx);
        Store.Audit(c, tx, "evaluation.created", round.S("id"), user.Identity!.Name!);
        return round;
    }
    public static JsonObject Transition(DbConnection c, DbTransaction tx, ClaimsPrincipal user, string id, string status)
    {
        Demand(user, true); var round = Round(c, id, tx); string prior = round.S("status");
        J.Need(prior switch { "draft" => status == "open", "open" => status is "paused" or "closed", "paused" => status is "open" or "closed", _ => false }, "انتقال حالة غير مسموح.");
        round["status"] = status; round["updatedAt"] = Now();
        Store.Save(c, RoundKind, round, tx); Store.Audit(c, tx, "evaluation." + status, id, user.Identity!.Name!);
        return round;
    }
    public static JsonObject ByToken(DbConnection c, string token, DbTransaction? tx = null)
    {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) throw new ApiError("النموذج غير متاح.", 404);
        var round = Store.ListWhere(c, RoundKind, tx, 1, ("sharingToken", token)).SingleOrDefault() ?? throw new ApiError("النموذج غير متاح.", 404);
        if (round.S("status") != "open") throw new ApiError("جولة التقييم غير متاحة حاليًا.", 409);
        return round;
    }
    static List<JsonObject> Cards(JsonObject input, string field)
    {
        var cards = input[field] as JsonArray ?? throw new ApiError("إجابات البطاقات مطلوبة: " + field);
        J.Need(cards.Count <= 500 && cards.All(x => x is JsonObject), "بطاقات غير صالحة.");
        return cards.OfType<JsonObject>().ToList();
    }
    static decimal Rating(JsonObject input, string key, int max)
    {
        J.Need(input[key] is JsonValue j && j.GetValueKind() == JsonValueKind.Number && decimal.TryParse(j.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= max && decimal.Truncate(n) == n, "تقييم خارج النطاق: " + key);
        return input.N(key);
    }
    static JsonArray Answers(JsonObject round, JsonObject input, bool staff, string city, Store? store, DbConnection? c, DbTransaction? tx, string replyId, ref long replyBytes)
    {
        string field = staff ? "staffAnswers" : "appAnswers", key = staff ? "staffId" : "app";
        var expected = staff ? round["staff"]!.AsArray().OfType<JsonObject>().Where(s => Strings(s, "cities").Contains(city)).Select(s => s.S("id")).ToArray() : Strings(round, "apps");
        var cards = Cards(input, field);
        J.Need(cards.Count == expected.Length && cards.Select(a => a.S(key)).Distinct().Count() == expected.Length && cards.All(a => expected.Contains(a.S(key))), "أجب عن كل بطاقة مرة واحدة.", "cards_incomplete");
        var clean = new JsonArray();
        foreach (var card in cards)
        {
            string reason = card.S("reason");
            var answer = new JsonObject { [key] = card.S(key), ["reason"] = reason, ["notes"] = Text(card, "notes", 2000) };
            if (reason != "")
            {
                J.Need((staff ? StaffReasons : AppReasons).Contains(reason), "سبب غير صالح.", "invalid_reason");
                J.Need(card["ratings"] == null && card["overall"] == null && Strings(card, "factors").Length == 0, "اختر التقييم أو سبب عدم التقييم.", "rating_or_reason");
            }
            else if (staff)
            {
                var ratings = card["ratings"] as JsonObject ?? throw new ApiError("التقييمات الخمسة مطلوبة.", 400, "ratings_required");
                J.Need(ratings.Count == StaffDimensions.Length && ratings.All(p => StaffDimensions.Contains(p.Key)), "أسئلة تقييم غير صالحة.", "invalid_ratings");
                answer["ratings"] = new JsonObject(StaffDimensions.Select(k => KeyValuePair.Create<string, JsonNode?>(k, JsonValue.Create(Rating(ratings, k, 10)))));
            }
            else
            {
                answer["overall"] = Rating(card, "overall", 5);
                var factors = Strings(card, "factors"); J.Need(factors.All(Factors.Contains), "عامل تقييم غير معروف.", "invalid_factor");
                answer["factors"] = Array(factors);
            }
            if (staff)
            {
                answer["complaint"] = card.B("complaint"); answer["complaintDetails"] = Text(card, "complaintDetails", 2000);
                J.Need(!answer.B("complaint") || answer.S("complaintDetails").Length >= 5, "تفاصيل الشكوى مطلوبة.", "complaint_details");
            }
            if (card["attachments"] is JsonArray arr && arr.Count > 0)
            {
                if (arr.All(x => x is JsonObject o && o.S("id") != "" && o.S("dataBase64") == ""))
                    answer["attachments"] = arr.DeepClone();
                else
                {
                    J.Need(store != null && c != null && tx != null, "رفع المرفقات غير متاح.", "attachment_unavailable");
                    answer["attachments"] = PersistAttachments(store!, c!, tx!, replyId, key, card.S(key), card, ref replyBytes);
                }
            }
            else answer["attachments"] = new JsonArray();
            clean.Add(answer);
        }
        return clean;
    }
    public static JsonObject Submit(DbConnection c, DbTransaction tx, string token, JsonObject input, string? authenticatedIqama = null, Store? store = null)
    {
        var round = ByToken(c, token, tx);
        string iqama = J.Iqama(authenticatedIqama ?? input.Required("iqama"));
        J.Need(authenticatedIqama == null || input.S("iqama") is "" || J.Iqama(input.S("iqama")) == iqama, "هوية المندوب لا تطابق الجلسة.", "identity_mismatch");
        string id = ReplyId(round.S("id"), iqama);
        if (Store.Get(c, id, ResponseKind, tx) != null) throw new ApiError("سبق إرسال هذا الرد؛ يمكنك تعديله إن كانت الجولة ما زالت مفتوحة.", 409, "already_submitted");
        string city = Text(input, "city", 100, true); J.Need(Strings(round, "cities").Contains(city), "اختر مدينة من الجولة.", "invalid_city");
        var workApps = Strings(input, "workApps"); J.Need(workApps.Length > 0 && workApps.All(a => Strings(round, "apps").Contains(a)), "اختر منصات العمل الحالية.", "invalid_apps");
        string nationalityRaw = Text(input, "nationality", 100, true);
        string nationalityId = CanonicalNationality(nationalityRaw, out string nationalityDisplay);
        var rider = Store.Get(c, iqama, "rider", tx);
        string companyId = rider == null ? "" : new[] { "financeSponsorId", "sponsorId", "companyId" }.Select(k => rider.S(k)).FirstOrDefault(v => v != "") ?? "";
        long replyBytes = 0;
        var reply = new JsonObject
        {
            ["id"] = id, ["roundId"] = round.S("id"), ["schemaVersion"] = 1, ["iqama"] = iqama,
            ["name"] = authenticatedIqama == null ? Text(input, "name", 200, true) : rider?.Required("name") ?? throw new ApiError("المندوب غير موجود.", 404, "not_found"),
            ["city"] = city, ["nationality"] = nationalityDisplay, ["nationalityId"] = nationalityId,
            ["phone"] = input.S("phone") == "" ? "" : RiderDirectory.Phone(input.S("phone")), ["workApps"] = Array(workApps),
            ["companyId"] = companyId, ["identityVerified"] = authenticatedIqama != null, ["editCount"] = 0,
            ["staffAnswers"] = Answers(round, input, true, city, store, c, tx, id, ref replyBytes),
            ["appAnswers"] = Answers(round, input, false, city, store, c, tx, id, ref replyBytes),
            ["createdAt"] = Now()
        };
        Store.Save(c, ResponseKind, reply, tx);
        foreach (var answer in reply["staffAnswers"]!.AsArray().OfType<JsonObject>().Where(a => a.B("complaint")))
            CreateComplaint(c, tx, reply, answer);
        Store.Audit(c, tx, "evaluation.submitted", id, authenticatedIqama == null ? "public-form" : "rider-app");
        return new() { ["receipt"] = id, ["allowedActions"] = new JsonArray("edit"), ["editCount"] = 0, ["editsRemaining"] = MaxEdits, ["editPolicyPending"] = false, ["uploadsPolicyPending"] = false };
    }
    static void CreateComplaint(DbConnection c, DbTransaction tx, JsonObject reply, JsonObject answer)
    {
        var titles = RiderAsks.RequestTypeTitles("complaint");
        var request = new JsonObject
        {
            ["id"] = "sreq:" + Guid.NewGuid().ToString("N"), ["requestType"] = "complaint", ["source"] = "evaluation",
            ["evaluationResponseId"] = reply.S("id"), ["direction"] = "rider_to_admin", ["riderIqama"] = reply.S("iqama"),
            ["riderName"] = reply.S("name"), ["companyId"] = reply.S("companyId"),
            ["titleAr"] = titles.Ar, ["titleEn"] = titles.En, ["titleUr"] = titles.Ur,
            ["reason"] = titles.Ar, ["status"] = "open", ["approvalStage"] = "awaiting_review", ["confidential"] = true,
            ["requiresSecondApprover"] = false, ["createdAt"] = reply.S("createdAt"), ["updatedAt"] = reply.S("createdAt"),
            ["payload"] = new JsonObject { ["targetKind"] = "supervisor", ["targetId"] = answer.S("staffId"), ["subject"] = titles.Ar, ["details"] = answer.S("complaintDetails") },
            ["replies"] = new JsonArray(), ["timeline"] = new JsonArray(new JsonObject { ["at"] = reply.S("createdAt"), ["status"] = "open", ["labelAr"] = titles.Ar, ["labelEn"] = titles.En, ["labelUr"] = titles.Ur, ["byRole"] = "rider" })
        };
        Store.Save(c, RiderAsks.ServiceKind, request, tx);
    }
    static bool InScope(DbConnection c, ClaimsPrincipal user, JsonObject row)
    {
        if (AccessPolicy.Administrator(user)) return true;
        bool company = row.S("companyId") != "" && FinancialScopes.Sponsor(user, row.S("companyId"));
        if (!user.FindAll("finance.sponsorIds").Any() && AccessPolicy.Has(user, SubmissionChoices.PermAllCompanies)) company = true;
        if (!company && FinancialScopes.AllCompanies(user)) company = true;
        // An absent platform scope means unrestricted; an explicit scope must cover every current work app.
        var platforms = user.FindAll("finance.platforms").Select(p => p.Value).ToArray();
        return company && (platforms.Length == 0 || Strings(row, "workApps").All(platforms.Contains));
    }
    public static bool CanSeeComplaint(DbConnection c, ClaimsPrincipal user, JsonObject request, DbTransaction? tx = null)
    {
        if (!CanView(user)) return false;
        var response = Store.Get(c, request.S("evaluationResponseId"), ResponseKind, tx);
        if (response == null || !InScope(c, user, response)) return false;
        if (AccessPolicy.Administrator(user)) return true;
        var scope = user.FindAll("finance.platforms").Select(p => p.Value).ToArray();
        var staff = Round(c, response.S("roundId"), tx)["staff"]!.AsArray().OfType<JsonObject>().SingleOrDefault(s => s.S("id") == request["payload"].S("targetId"));
        return staff != null && (scope.Length == 0 || Strings(staff, "apps").All(scope.Contains));
    }
    public static List<JsonObject> Responses(DbConnection c, ClaimsPrincipal user, string id)
    {
        Demand(user); Round(c, id);
        var scope = user.FindAll("finance.platforms").Select(p => p.Value).ToArray();
        var round = Round(c, id);
        return Store.ListWhere(c, ResponseKind, null, null, ("roundId", id)).Where(r => InScope(c, user, r)).Select(r =>
        {
            if (AccessPolicy.Administrator(user) || scope.Length == 0) return r;
            r["appAnswers"] = Array(r["appAnswers"]!.AsArray().OfType<JsonObject>().Where(a => scope.Contains(a.S("app"))));
            var ids = round["staff"]!.AsArray().OfType<JsonObject>().Where(s => Strings(s, "apps").All(scope.Contains)).Select(s => s.S("id")).ToHashSet();
            r["staffAnswers"] = Array(r["staffAnswers"]!.AsArray().OfType<JsonObject>().Where(a => ids.Contains(a.S("staffId"))));
            return r;
        }).ToList();
    }
    public static JsonObject Statistics(DbConnection c, ClaimsPrincipal user, string id, string? city = null, string? nationalityId = null)
    {
        var rows = Responses(c, user, id);
        if (!string.IsNullOrEmpty(city)) rows = rows.Where(r => r.S("city") == city).ToList();
        if (!string.IsNullOrEmpty(nationalityId)) rows = rows.Where(r => r.S("nationalityId") == nationalityId || r.S("nationality") == nationalityId).ToList();
        JsonObject Summary(string key, IEnumerable<JsonObject> subset)
        {
            var list = subset.ToList();
            var staffRatings = list.SelectMany(r => r["staffAnswers"]!.AsArray().OfType<JsonObject>()).Where(a => a.S("reason") == "").Select(a => StaffDimensions.Average(d => a["ratings"]!.N(d))).ToList();
            var appRatings = list.SelectMany(r => r["appAnswers"]!.AsArray().OfType<JsonObject>()).Where(a => a.S("reason") == "").Select(a => a.N("overall")).ToList();
            return new() { ["key"] = key, ["responseCount"] = list.Count, ["complaintCount"] = list.Sum(r => r["staffAnswers"]!.AsArray().OfType<JsonObject>().Count(a => a.B("complaint"))),
                ["staffRatedCount"] = staffRatings.Count, ["appRatedCount"] = appRatings.Count,
                ["staffAverage"] = staffRatings.Count == 0 ? null : JsonValue.Create(Math.Round(staffRatings.Average(), 2)),
                ["appAverage"] = appRatings.Count == 0 ? null : JsonValue.Create(Math.Round(appRatings.Average(), 2)) };
        }
        var staff = rows.SelectMany(r => r["staffAnswers"]!.AsArray().OfType<JsonObject>()).ToList();
        var apps = rows.SelectMany(r => r["appAnswers"]!.AsArray().OfType<JsonObject>()).ToList();
        JsonArray GroupCards(List<JsonObject> cards, string key, bool supervisor) => Array(cards.GroupBy(a => a.S(key)).Select(g =>
        {
            var rated = g.Where(a => a.S("reason") == "").ToList();
            var values = rated.Select(a => supervisor ? StaffDimensions.Average(d => a["ratings"]!.N(d)) : a.N("overall")).ToList();
            var result = new JsonObject { ["key"] = g.Key, ["count"] = g.Count(), ["ratedCount"] = rated.Count, ["skippedCount"] = g.Count() - rated.Count,
                ["average"] = values.Count == 0 ? null : JsonValue.Create(Math.Round(values.Average(), 2)),
                ["complaintCount"] = g.Count(a => a.B("complaint")), ["reasons"] = Array(g.Where(a => a.S("reason") != "").GroupBy(a => a.S("reason")).Select(reason => new JsonObject { ["key"] = reason.Key, ["count"] = reason.Count() })) };
            if (supervisor) result["dimensions"] = Array(StaffDimensions.Select(d => new JsonObject { ["key"] = d, ["count"] = rated.Count, ["average"] = rated.Count == 0 ? null : JsonValue.Create(Math.Round(rated.Average(a => a["ratings"]!.N(d)), 2)) }));
            else result["distribution"] = Array(Enumerable.Range(1, 5).Select(n => new JsonObject { ["stars"] = n, ["count"] = rated.Count(a => a.N("overall") == n) }));
            return result;
        }));
        return new()
        {
            ["roundId"] = id, ["totalResponses"] = rows.Count,
            ["byCity"] = Array(rows.GroupBy(r => r.S("city")).Select(g => Summary(g.Key, g))),
            ["byNationality"] = Array(rows.GroupBy(r => r.S("nationalityId") == "" ? r.S("nationality") : r.S("nationalityId")).Select(g => Summary(g.Key, g))),
            ["bySupervisor"] = GroupCards(staff, "staffId", true), ["byApp"] = GroupCards(apps, "app", false),
            ["commonFactors"] = Array(apps.SelectMany(a => Strings(a, "factors").Select(f => (App: a.S("app"), Factor: f))).GroupBy(p => p).OrderByDescending(g => g.Count()).Select(g => new JsonObject { ["app"] = g.Key.App, ["key"] = g.Key.Factor, ["count"] = g.Count() })),
            ["dailyTrend"] = Array(rows.GroupBy(r => r.S("createdAt")[..10]).OrderBy(g => g.Key).Select(g => Summary(g.Key, g)))
        };
    }
}
