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
    public static string NewEditReceipt() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static bool ReceiptEquals(string a, string b) =>
        a.Length == b.Length && a.Length == 64 && a.All(Uri.IsHexDigit) && b.All(Uri.IsHexDigit)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    /// <summary>Stages attachment bytes until the document transaction commits, then writes (or discards).</summary>
    public sealed class AttachmentWriteBatch
    {
        readonly List<(string Path, byte[] Bytes)> _items = new();
        public int Count => _items.Count;
        public void Stage(string path, byte[] bytes) => _items.Add((path, bytes));
        public void Flush()
        {
            foreach (var (path, bytes) in _items)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            _items.Clear();
        }
        public void Discard()
        {
            foreach (var (path, _) in _items)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
            }
            _items.Clear();
        }
    }
    public static T WriteWithAttachments<T>(Store store, Func<DbConnection, DbTransaction, AttachmentWriteBatch, T> action)
    {
        var batch = new AttachmentWriteBatch();
        try
        {
            var result = store.Write((c, tx) => action(c, tx, batch));
            batch.Flush();
            return result;
        }
        catch
        {
            batch.Discard();
            throw;
        }
    }
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
            var doc = new JsonObject { ["id"] = id, ["name"] = name, ["active"] = raw.NullableBool("active") ?? true, ["excluded"] = raw.B("excluded"), ["pendingRoleApproval"] = raw.B("pendingRoleApproval"),
                // Remote teams (call centre, technical support) serve riders in every work city; their own office is not a work city.
                ["allCities"] = raw.NullableBool("allCities") ?? prior?.NullableBool("allCities") ?? false,
                ["allApps"] = raw.NullableBool("allApps") ?? prior?.NullableBool("allApps") ?? false };
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
            ["apps"] = Array(PayPlatforms.Table.Select(a => a.Platform)), ["cities"] = Array(WorkCities(staff)),
            ["schemaVersion"] = 2, ["sections"] = BuildSections(staff)
        };
        J.Need(Strings(round, "cities").Length > 0, "لا توجد مدينة عمل: كل الموظفين معلّمون «كل المدن».");
        Store.Save(c, RoundKind, round, tx);
        Store.Audit(c, tx, "evaluation.created", round.S("id"), user.Identity!.Name!);
        return round;
    }
    /// <summary>Work cities a rider can pick: cities of city-bound staff, minus the offices of remote (all-cities) staff.</summary>
    public static string[] WorkCities(IEnumerable<JsonObject> staff)
    {
        var list = staff.ToList();
        var offices = list.Where(s => s.B("allCities")).SelectMany(s => Strings(s, "cities")).ToHashSet(StringComparer.Ordinal);
        return list.Where(s => !s.B("allCities")).SelectMany(s => Strings(s, "cities")).Where(c => !offices.Contains(c)).Distinct(StringComparer.Ordinal).ToArray();
    }
    public static bool ServesCity(JsonObject staff, string city) => staff.B("allCities") || Strings(staff, "cities").Contains(city);
    /// <summary>Re-reads the current staff list into an unclosed round. Existing replies keep their answers; a later edit must answer the city's current cards.</summary>
    public static JsonObject RefreshStaff(DbConnection c, DbTransaction tx, ClaimsPrincipal user, string id)
    {
        Demand(user, true); var round = Round(c, id, tx);
        J.Need(round.S("status") is "draft" or "open" or "paused", "لا يمكن تحديث موظفي جولة مغلقة.", "round_closed", 409);
        var staff = Store.List(c, StaffKind, tx).Where(s => (s.NullableBool("active") ?? true) && !s.B("excluded")).ToList();
        J.Need(staff.Count > 0, "استورد قائمة الموظفين أولًا.");
        var cities = WorkCities(staff);
        J.Need(cities.Length > 0, "لا توجد مدينة عمل: كل الموظفين معلّمون «كل المدن».");
        round["staff"] = Array(staff); round["cities"] = Array(cities); round["updatedAt"] = Now();
        if (IsSectioned(round)) RefreshSectionStaff(round);
        Store.Save(c, RoundKind, round, tx); Store.Audit(c, tx, "evaluation.staff_refreshed", id, user.Identity!.Name!);
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
        foreach (var candidate in Store.List(c, RoundKind, tx))
        {
            if (!IsSectioned(candidate)) continue;
            var section = candidate["sections"]!.AsArray().OfType<JsonObject>().FirstOrDefault(s => s.S("sharingToken") == token);
            if (section == null) continue;
            if (candidate.S("status") != "open") throw new ApiError("جولة التقييم غير متاحة حاليًا.", 409);
            var view = candidate.Clone();
            view["activeSection"] = section.Clone();
            return view;
        }
        var round = Store.ListWhere(c, RoundKind, tx, 1, ("sharingToken", token)).SingleOrDefault() ?? throw new ApiError("النموذج غير متاح.", 404);
        // Sectioned rounds are opened only by section tokens; the stored round token stays for legacy rounds without sections.
        if (IsSectioned(round)) throw new ApiError("النموذج غير متاح.", 404);
        if (round.S("status") != "open") throw new ApiError("جولة التقييم غير متاحة حاليًا.", 409);
        return round;
    }
    static List<JsonObject> Cards(JsonObject input, string field)
    {
        var cards = input[field] as JsonArray ?? throw new ApiError("إجابات البطاقات مطلوبة: " + field);
        J.Need(cards.Count <= 500 && cards.All(x => x is JsonObject), "بطاقات غير صالحة.");
        return cards.OfType<JsonObject>().ToList();
    }
    static List<JsonObject> CardsOptional(JsonObject input, string field)
    {
        if (input[field] == null) return [];
        return Cards(input, field);
    }
    static decimal Rating(JsonObject input, string key, int max)
    {
        J.Need(input[key] is JsonValue j && j.GetValueKind() == JsonValueKind.Number && decimal.TryParse(j.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= max && decimal.Truncate(n) == n, "تقييم خارج النطاق: " + key);
        return input.N(key);
    }
    static JsonObject FactorRatings(JsonObject card) => card["factorRatings"] as JsonObject ?? new JsonObject();
    static JsonObject ParseFactorRatings(JsonObject card)
    {
        var raw = FactorRatings(card);
        J.Need(raw.All(p => Factors.Contains(p.Key)), "عامل تقييم غير معروف.", "invalid_factor");
        var clean = new JsonObject();
        foreach (var pair in raw)
        {
            if (pair.Value is null || pair.Value.GetValueKind() is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            clean[pair.Key] = Rating(raw, pair.Key, 5);
        }
        return clean;
    }
    static JsonArray Answers(JsonObject round, JsonObject input, bool staff, string city, Store? store, DbConnection? c, DbTransaction? tx, string replyId, ref long replyBytes, AttachmentWriteBatch? batch)
    {
        string field = staff ? "staffAnswers" : "appAnswers", key = staff ? "staffId" : "app";
        var section = ActiveSection(round);
        string[] expected;
        if (section != null)
        {
            if (staff)
                expected = SectionMembers(round, section).Where(s => ServesCity(s, city)).Select(s => s.S("id")).Distinct().ToArray();
            else if (section.S("kind") == "app")
                expected = [section.S("key")["app:".Length..]];
            else
                expected = [];
        }
        else
            expected = staff ? round["staff"]!.AsArray().OfType<JsonObject>().Where(s => ServesCity(s, city)).Select(s => s.S("id")).ToArray() : Strings(round, "apps");
        var cards = expected.Length == 0 && !staff ? CardsOptional(input, field) : Cards(input, field);
        J.Need(cards.Count == expected.Length && cards.Select(a => a.S(key)).Distinct().Count() == expected.Length && cards.All(a => expected.Contains(a.S(key))), "أجب عن كل بطاقة مرة واحدة.", "cards_incomplete");
        var clean = new JsonArray();
        foreach (var card in cards)
        {
            string reason = card.S("reason");
            var answer = new JsonObject { [key] = card.S(key), ["reason"] = reason, ["notes"] = Text(card, "notes", 2000) };
            if (reason != "")
            {
                J.Need((staff ? StaffReasons : AppReasons).Contains(reason), "سبب غير صالح.", "invalid_reason");
                J.Need(card["ratings"] == null && card["overall"] == null && Strings(card, "factors").Length == 0 && FactorRatings(card).Count == 0, "اختر التقييم أو سبب عدم التقييم.", "rating_or_reason");
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
                answer["factorRatings"] = ParseFactorRatings(card);
            }
            if (staff)
            {
                answer["complaint"] = card.B("complaint"); answer["complaintDetails"] = Text(card, "complaintDetails", 2000);
                J.Need(!answer.B("complaint") || answer.S("complaintDetails").Length >= 5, "تفاصيل الشكوى مطلوبة.", "complaint_details");
            }
            if (card["attachments"] is JsonArray arr && arr.Count > 0)
            {
                if (arr.All(x => x is JsonObject o && o.S("id") != "" && o.S("dataBase64") == ""))
                    answer["attachments"] = PreservedAttachments(c, tx, replyId, key, card.S(key), arr);
                else
                {
                    J.Need(store != null && c != null && tx != null && batch != null, "رفع المرفقات غير متاح.", "attachment_unavailable");
                    answer["attachments"] = PersistAttachments(store!, c!, tx!, replyId, key, card.S(key), card, ref replyBytes, batch!);
                }
            }
            else answer["attachments"] = new JsonArray();
            clean.Add(answer);
        }
        return clean;
    }
    public static JsonObject Submit(DbConnection c, DbTransaction tx, string token, JsonObject input, string? authenticatedIqama = null, Store? store = null, AttachmentWriteBatch? attachments = null)
    {
        var round = ByToken(c, token, tx);
        var section = ActiveSection(round);
        string iqama = J.Iqama(authenticatedIqama ?? input.Required("iqama"));
        J.Need(authenticatedIqama == null || input.S("iqama") is "" || J.Iqama(input.S("iqama")) == iqama, "هوية المندوب لا تطابق الجلسة.", "identity_mismatch");
        var rider = Store.Get(c, iqama, "rider", tx);
        // A directory rider may bind a reply only through the mobile or survey session — never from a typed iqama alone.
        if (authenticatedIqama == null && rider != null)
            throw new ApiError("أرسل الرد من جلسة المندوب أو رابط التقييم الآمن.", 401, "session_required");
        string id = section == null ? ReplyId(round.S("id"), iqama) : ReplyId(round.S("id"), section.S("key"), iqama);
        if (Store.Get(c, id, ResponseKind, tx) != null) throw new ApiError("سبق إرسال هذا الرد؛ يمكنك تعديله إن كانت الجولة ما زالت مفتوحة.", 409, "already_submitted");
        string city = Text(input, "city", 100, true); J.Need(Strings(round, "cities").Contains(city), "اختر مدينة من الجولة.", "invalid_city");
        var workApps = Strings(input, "workApps"); J.Need(workApps.Length > 0 && workApps.All(a => Strings(round, "apps").Contains(a)), "اختر منصات العمل الحالية.", "invalid_apps");
        string nationalityRaw = Text(input, "nationality", 100, true);
        string nationalityId = CanonicalNationality(nationalityRaw, out string nationalityDisplay);
        string companyId = rider == null ? "" : new[] { "financeSponsorId", "sponsorId", "companyId" }.Select(k => rider.S(k)).FirstOrDefault(v => v != "") ?? "";
        string editReceipt = NewEditReceipt();
        long replyBytes = 0;
        var reply = new JsonObject
        {
            ["id"] = id, ["roundId"] = round.S("id"), ["schemaVersion"] = section == null ? 1 : 2, ["iqama"] = iqama,
            ["name"] = authenticatedIqama == null ? Text(input, "name", 200, true) : rider?.Required("name") ?? throw new ApiError("المندوب غير موجود.", 404, "not_found"),
            ["city"] = city, ["nationality"] = nationalityDisplay, ["nationalityId"] = nationalityId,
            ["phone"] = input.S("phone") == "" ? "" : RiderDirectory.Phone(input.S("phone")), ["workApps"] = Array(workApps),
            ["companyId"] = companyId, ["identityVerified"] = authenticatedIqama != null, ["editCount"] = 0,
            ["editReceipt"] = editReceipt,
            ["staffAnswers"] = Answers(round, input, true, city, store, c, tx, id, ref replyBytes, attachments),
            ["appAnswers"] = Answers(round, input, false, city, store, c, tx, id, ref replyBytes, attachments),
            ["createdAt"] = Now()
        };
        if (section != null)
        {
            reply["sectionKey"] = section.S("key");
            reply["sectionTitle"] = section.S("title");
            if (section.S("kind") == "app")
            {
                reply["financeAnswer"] = ParseFinanceAnswer(input, store, c, tx, id, ref replyBytes, attachments);
                reply["systemsAnswer"] = ParseSystemsAnswer(input, store, c, tx, id, ref replyBytes, attachments);
            }
        }
        Store.Save(c, ResponseKind, reply, tx);
        foreach (var answer in reply["staffAnswers"]!.AsArray().OfType<JsonObject>().Where(a => a.B("complaint")))
            CreateComplaint(c, tx, reply, answer);
        if (reply["financeAnswer"] is JsonObject finance && finance.B("complaint"))
            CreateDepartmentComplaint(c, tx, reply, finance, DeptFinance);
        if (reply["systemsAnswer"] is JsonObject systems && systems.B("complaint"))
            CreateDepartmentComplaint(c, tx, reply, systems, DeptSystems);
        Store.Audit(c, tx, "evaluation.submitted", id, authenticatedIqama == null ? "public-form" : "rider-app");
        return new() { ["receipt"] = editReceipt, ["allowedActions"] = new JsonArray("edit"), ["editCount"] = 0, ["editsRemaining"] = MaxEdits, ["editPolicyPending"] = false, ["uploadsPolicyPending"] = false };
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
    static void CreateDepartmentComplaint(DbConnection c, DbTransaction tx, JsonObject reply, JsonObject answer, string departmentTarget)
    {
        var titles = RiderAsks.RequestTypeTitles("complaint");
        string app = reply["appAnswers"]!.AsArray().OfType<JsonObject>().Select(a => a.S("app")).FirstOrDefault(a => a != "") ?? "";
        var request = new JsonObject
        {
            ["id"] = "sreq:" + Guid.NewGuid().ToString("N"), ["requestType"] = "complaint", ["source"] = "evaluation",
            ["evaluationResponseId"] = reply.S("id"), ["direction"] = "rider_to_admin", ["riderIqama"] = reply.S("iqama"),
            ["riderName"] = reply.S("name"), ["companyId"] = reply.S("companyId"),
            ["titleAr"] = titles.Ar, ["titleEn"] = titles.En, ["titleUr"] = titles.Ur,
            ["reason"] = titles.Ar, ["status"] = "open", ["approvalStage"] = "awaiting_review", ["confidential"] = true,
            ["requiresSecondApprover"] = false, ["createdAt"] = reply.S("createdAt"), ["updatedAt"] = reply.S("createdAt"),
            ["payload"] = new JsonObject
            {
                ["targetKind"] = "department", ["target"] = departmentTarget, ["subject"] = titles.Ar,
                ["details"] = answer.S("complaintDetails"), ["app"] = app
            },
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
        // Absent platform scope = unrestricted. Sectioned app replies key off sectionKey; dept/legacy keep workApps.
        var platforms = user.FindAll("finance.platforms").Select(p => p.Value).ToArray();
        if (!company) return false;
        if (platforms.Length == 0) return true;
        string sectionKey = row.S("sectionKey");
        if (sectionKey.StartsWith("app:", StringComparison.Ordinal))
            return platforms.Contains(sectionKey["app:".Length..]);
        return Strings(row, "workApps").All(platforms.Contains);
    }
    static bool FinanceAnswerInPlatformScope(JsonObject row, string[] platforms) =>
        platforms.Length == 0
        || (row.S("sectionKey").StartsWith("app:", StringComparison.Ordinal)
            && platforms.Contains(row.S("sectionKey")["app:".Length..]));
    public static bool CanSeeComplaint(DbConnection c, ClaimsPrincipal user, JsonObject request, DbTransaction? tx = null)
    {
        if (!CanView(user)) return false;
        var response = Store.Get(c, request.S("evaluationResponseId"), ResponseKind, tx);
        if (response == null || !InScope(c, user, response)) return false;
        if (AccessPolicy.Administrator(user)) return true;
        var scope = user.FindAll("finance.platforms").Select(p => p.Value).ToArray();
        // Finance card complaints target a department (no staff card); visibility follows reply scope + payload.app.
        if (request["payload"].S("targetKind") == "department")
            return scope.Length == 0 || scope.Contains(request["payload"].S("app"));
        var staff = Round(c, response.S("roundId"), tx)["staff"]!.AsArray().OfType<JsonObject>().SingleOrDefault(s => s.S("id") == request["payload"].S("targetId"));
        return staff != null && (scope.Length == 0 || Strings(staff, "apps").All(scope.Contains));
    }
    public static List<JsonObject> Responses(DbConnection c, ClaimsPrincipal user, string id, string? supervisorId = null, string? department = null, string? section = null)
    {
        Demand(user); Round(c, id);
        var scope = user.FindAll("finance.platforms").Select(p => p.Value).ToArray();
        var round = Round(c, id);
        var rows = Store.ListWhere(c, ResponseKind, null, null, ("roundId", id)).Where(r => InScope(c, user, r)).Select(r =>
        {
            if (AccessPolicy.Administrator(user) || scope.Length == 0) return r;
            r["appAnswers"] = Array(r["appAnswers"]!.AsArray().OfType<JsonObject>().Where(a => scope.Contains(a.S("app"))));
            var ids = round["staff"]!.AsArray().OfType<JsonObject>().Where(s => Strings(s, "apps").All(scope.Contains)).Select(s => s.S("id")).ToHashSet();
            r["staffAnswers"] = Array(r["staffAnswers"]!.AsArray().OfType<JsonObject>().Where(a => ids.Contains(a.S("staffId"))));
            if (!FinanceAnswerInPlatformScope(r, scope))
            {
                r.Remove("financeAnswer");
                r.Remove("systemsAnswer");
            }
            return r;
        }).ToList();
        if (!string.IsNullOrEmpty(section))
            rows = rows.Where(r => r.S("sectionKey") == section).ToList();
        if (!string.IsNullOrEmpty(supervisorId))
            rows = rows.Where(r => r["staffAnswers"]!.AsArray().OfType<JsonObject>().Any(a => a.S("staffId") == supervisorId && a.S("reason") == "")).ToList();
        if (!string.IsNullOrEmpty(department))
        {
            var deptIds = round["staff"]!.AsArray().OfType<JsonObject>()
                .Where(s => Strings(s, "departments").Contains(department!)).Select(s => s.S("id")).ToHashSet();
            rows = rows.Where(r => r["staffAnswers"]!.AsArray().OfType<JsonObject>().Any(a => deptIds.Contains(a.S("staffId")) && a.S("reason") == "")).ToList();
        }
        return rows;
    }
    public static JsonObject Statistics(DbConnection c, ClaimsPrincipal user, string id, string? city = null, string? nationalityId = null, string? section = null)
    {
        var rows = Responses(c, user, id, section: section);
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
            ["byNationality"] = Array(rows.GroupBy(r => r.S("nationalityId") == "" ? r.S("nationality") : r.S("nationalityId")).Select(g =>
            {
                var bucket = Summary(g.Key, g);
                bucket["label"] = g.Select(r => r.S("nationality")).FirstOrDefault(v => v != "") ?? g.Key;
                return bucket;
            })),
            ["bySupervisor"] = GroupCards(staff, "staffId", true), ["byApp"] = GroupCards(apps, "app", false),
            ["factorAverages"] = Array(apps.Where(a => a.S("reason") == "").SelectMany(a =>
            {
                var ratings = a["factorRatings"] as JsonObject ?? new JsonObject();
                return Factors.Where(f => ratings[f] is JsonValue).Select(f => (App: a.S("app"), Factor: f, Stars: ratings.N(f)));
            }).GroupBy(p => (p.App, p.Factor)).OrderBy(g => g.Key.App, StringComparer.Ordinal).ThenBy(g => System.Array.IndexOf(Factors, g.Key.Factor))
              .Select(g => new JsonObject { ["app"] = g.Key.App, ["key"] = g.Key.Factor, ["count"] = g.Count(), ["average"] = Math.Round(g.Average(x => x.Stars), 2) })),
            ["dailyTrend"] = Array(rows.GroupBy(r => r.S("createdAt")[..10]).OrderBy(g => g.Key).Select(g => Summary(g.Key, g)))
        };
    }
}
