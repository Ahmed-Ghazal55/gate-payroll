using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Payroll;

public static partial class EvaluationSurvey
{
    public const string DeptMovement = "الحركة والمركبات";
    public const string DeptFinance = "المالية والحسابات";
    public const string DeptHr = "الموارد البشرية";
    public const string DeptSupport = "الدعم الفني والكول سنتر";
    public const string DeptOperations = "العمليات";
    public static readonly string[] FinanceFactors = ["responsiveness", "accuracy", "correctionSpeed", "clarity"];
    public static readonly string[] FinanceReasons = ["not_dealt_finance"];
    public static readonly (string Key, string Title, string Department)[] DepartmentSections =
    [
        ("dept:movement", "الحركة والمركبات", DeptMovement),
        ("dept:finance", "المالية والحسابات", DeptFinance),
        ("dept:hr", "الموارد البشرية", DeptHr),
        ("dept:support", "الكول سنتر والدعم الفني", DeptSupport)
    ];

    /// <summary>Arabic-first section titles (UI platform names); English/Urdu for the rider form.</summary>
    public static (string Ar, string En, string Ur) SectionTitles(string key)
    {
        if (key.StartsWith("app:", StringComparison.Ordinal))
        {
            var (ar, en, ur) = PlatformTitle(key["app:".Length..]);
            return ("تطبيق " + ar, en + " app", ur + " ایپ");
        }
        return key switch
        {
            "dept:movement" => (DeptMovement, "Movement & vehicles", "نقل و گاڑیاں"),
            "dept:finance" => (DeptFinance, "Finance & accounts", "مالیہ اور حسابات"),
            "dept:hr" => (DeptHr, "Human resources", "انسانی وسائل"),
            "dept:support" => ("الكول سنتر والدعم الفني", "Call centre & support", "کال سینٹر اور سپورٹ"),
            _ => (key, key, key)
        };
    }

    static (string Ar, string En, string Ur) PlatformTitle(string platform) => platform switch
    {
        "Jahez" => ("جاهز", "Jahez", "جاہز"),
        "Keeta" => ("كيتا", "Keeta", "کیٹا"),
        "HungerStation" => ("هنقرستيشن", "HungerStation", "ہنگر اسٹیشن"),
        "Ninja" => ("نينجا", "Ninja", "ننجا"),
        "Chefz" => ("ذا شفز", "The Chefz", "دی شیفز"),
        "Amazon" => ("أمازون", "Amazon", "ایمیزون"),
        _ => (platform, platform, platform)
    };

    public static bool IsSectioned(JsonObject round) =>
        round["sections"] is JsonArray sections && sections.Count > 0;

    public static string ReplyId(string roundId, string sectionKey, string iqama) =>
        "evreply:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(roundId + ":" + sectionKey + ":" + iqama))).ToLowerInvariant();

    public static JsonObject? ActiveSection(JsonObject round) => round["activeSection"] as JsonObject;

    static bool InDepartment(JsonObject staff, string department) =>
        Strings(staff, "departments").Contains(department);

    static bool MatchesAppSection(JsonObject staff, string platform) =>
        staff.B("allApps") || Strings(staff, "apps").Contains(platform);

    public static IEnumerable<JsonObject> SectionMembers(JsonObject round, JsonObject section)
    {
        var all = round["staff"]!.AsArray().OfType<JsonObject>().ToDictionary(s => s.S("id"), s => s);
        var added = Strings(section, "added").ToHashSet(StringComparer.Ordinal);
        var removed = Strings(section, "removed").ToHashSet(StringComparer.Ordinal);
        string key = section.S("key");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var staff in all.Values)
        {
            string id = staff.S("id");
            if (removed.Contains(id)) continue;
            bool match = key.StartsWith("app:", StringComparison.Ordinal)
                ? MatchesAppSection(staff, key["app:".Length..])
                : DepartmentSections.Any(d => d.Key == key && InDepartment(staff, d.Department));
            if (match || added.Contains(id)) ids.Add(id);
        }
        foreach (string id in added)
            if (all.ContainsKey(id) && !removed.Contains(id)) ids.Add(id);
        return ids.Select(id => all[id]);
    }

    static JsonObject BuildSection(string key, string kind, IEnumerable<JsonObject> roundStaff, string[]? added = null, string[]? removed = null)
    {
        var titles = SectionTitles(key);
        var section = new JsonObject
        {
            ["key"] = key,
            ["title"] = titles.Ar,
            ["titleEn"] = titles.En,
            ["titleUr"] = titles.Ur,
            ["kind"] = kind,
            ["sharingToken"] = CampaignLinks.NewToken(),
            ["added"] = Array(added ?? []),
            ["removed"] = Array(removed ?? [])
        };
        section["staff"] = Array(SectionMembers(new JsonObject { ["staff"] = Array(roundStaff) }, section)
            .GroupBy(s => s.S("id")).Select(g => g.First()));
        return section;
    }

    public static JsonArray BuildSections(IReadOnlyList<JsonObject> staff)
    {
        var sections = new JsonArray();
        foreach (var dept in DepartmentSections)
            sections.Add(BuildSection(dept.Key, "dept", staff));
        foreach (var platform in PayPlatforms.Table.Select(p => p.Platform))
            sections.Add(BuildSection("app:" + platform, "app", staff));
        return sections;
    }

    public static void RefreshSectionStaff(JsonObject round)
    {
        if (!IsSectioned(round)) return;
        var staff = round["staff"]!.AsArray().OfType<JsonObject>().ToList();
        var refreshed = new JsonArray();
        foreach (var prior in round["sections"]!.AsArray().OfType<JsonObject>())
        {
            var titles = SectionTitles(prior.S("key"));
            var section = new JsonObject
            {
                ["key"] = prior.S("key"),
                ["title"] = prior.S("title") is { Length: > 0 } t ? t : titles.Ar,
                ["titleEn"] = prior.S("titleEn") is { Length: > 0 } te ? te : titles.En,
                ["titleUr"] = prior.S("titleUr") is { Length: > 0 } tu ? tu : titles.Ur,
                ["kind"] = prior.S("kind"),
                ["sharingToken"] = prior.S("sharingToken"),
                ["added"] = prior["added"]?.DeepClone() ?? new JsonArray(),
                ["removed"] = prior["removed"]?.DeepClone() ?? new JsonArray()
            };
            section["staff"] = Array(SectionMembers(round, section).GroupBy(s => s.S("id")).Select(g => g.First()));
            refreshed.Add(section);
        }
        round["sections"] = refreshed;
    }

    static JsonObject SectionByKey(JsonObject round, string sectionKey) =>
        round["sections"]!.AsArray().OfType<JsonObject>().SingleOrDefault(s => s.S("key") == sectionKey)
        ?? throw new ApiError("قسم التقييم غير موجود.", 404);

    public static JsonObject AddSectionStaff(DbConnection c, DbTransaction tx, ClaimsPrincipal user, string roundId, string sectionKey, string staffId)
    {
        Demand(user, true);
        var round = Round(c, roundId, tx);
        J.Need(IsSectioned(round), "هذه الجولة بدون أقسام.");
        J.Need(round.S("status") is not "closed", "لا يمكن تعديل جولة مغلقة.", "round_closed", 409);
        var staff = round["staff"]!.AsArray().OfType<JsonObject>().SingleOrDefault(s => s.S("id") == staffId)
            ?? throw new ApiError("الموظف غير موجود في الجولة.", 404);
        var section = SectionByKey(round, sectionKey);
        var removed = Strings(section, "removed").Where(id => id != staffId).ToArray();
        var added = Strings(section, "added").Append(staffId).Distinct(StringComparer.Ordinal).ToArray();
        section["removed"] = Array(removed);
        section["added"] = Array(added);
        RefreshSectionStaff(round);
        round["updatedAt"] = Now();
        Store.Save(c, RoundKind, round, tx);
        Store.Audit(c, tx, "evaluation.section_staff_added", roundId + ":" + sectionKey, user.Identity!.Name!);
        return RoundViewDoc(round, user);
    }

    public static JsonObject RemoveSectionStaff(DbConnection c, DbTransaction tx, ClaimsPrincipal user, string roundId, string sectionKey, string staffId)
    {
        Demand(user, true);
        var round = Round(c, roundId, tx);
        J.Need(IsSectioned(round), "هذه الجولة بدون أقسام.");
        J.Need(round.S("status") is not "closed", "لا يمكن تعديل جولة مغلقة.", "round_closed", 409);
        var section = SectionByKey(round, sectionKey);
        var added = Strings(section, "added").Where(id => id != staffId).ToArray();
        var removed = Strings(section, "removed").Append(staffId).Distinct(StringComparer.Ordinal).ToArray();
        section["added"] = Array(added);
        section["removed"] = Array(removed);
        RefreshSectionStaff(round);
        round["updatedAt"] = Now();
        Store.Save(c, RoundKind, round, tx);
        Store.Audit(c, tx, "evaluation.section_staff_removed", roundId + ":" + sectionKey, user.Identity!.Name!);
        return RoundViewDoc(round, user);
    }

    public static JsonObject PutStaff(DbConnection c, DbTransaction tx, ClaimsPrincipal user, string id, JsonObject input)
    {
        Demand(user, true);
        var prior = Store.Get(c, id, StaffKind, tx) ?? throw new ApiError("بطاقة الموظف غير موجودة.", 404);
        string name = Text(input, "name", 200, true);
        var doc = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["active"] = input.NullableBool("active") ?? prior.NullableBool("active") ?? true,
            ["excluded"] = input.NullableBool("excluded") ?? prior.B("excluded"),
            ["pendingRoleApproval"] = input.NullableBool("pendingRoleApproval") ?? prior.B("pendingRoleApproval"),
            ["allCities"] = input.NullableBool("allCities") ?? prior.B("allCities"),
            ["allApps"] = input.NullableBool("allApps") ?? prior.B("allApps")
        };
        foreach (string field in new[] { "cities", "departments", "roles", "apps" })
            doc[field] = Array(Strings(input, field));
        J.Need(Strings(doc, "apps").All(a => PayPlatforms.Table.Any(p => p.Platform == a)), "استخدم معرفات المنصات المعتمدة.");
        J.Need(Strings(doc, "cities").Length > 0, "مدينة الموظف مطلوبة.");
        if (doc.B("pendingRoleApproval")) doc["roles"] = new JsonArray("بانتظار موافقة غزال");
        Store.Save(c, StaffKind, doc, tx);
        Store.Audit(c, tx, "evaluation.staff_updated", id, user.Identity!.Name!);
        return doc;
    }

    public static JsonObject DeleteRound(DbConnection c, DbTransaction tx, ClaimsPrincipal user, string id, JsonObject input, Store store)
    {
        Demand(user, true);
        var round = Round(c, id, tx);
        J.Need(Text(input, "title", 200, true) == round.S("title"), "أكد الحذف بكتابة عنوان الجولة كما هو.", "title_mismatch");
        var replies = Store.ListWhere(c, ResponseKind, tx, null, ("roundId", id)).ToList();
        int attachmentsDeleted = 0;
        foreach (var reply in replies)
        {
            foreach (var att in Store.ListWhere(c, AttachmentKind, tx, null, ("replyId", reply.S("id"))).ToList())
            {
                DeleteAttachmentFileIfSafe(store, att.S("relativePath"));
                using var delAtt = Store.Command(c, tx, "DELETE FROM records WHERE id=@id AND kind=@kind", ("@id", att.S("id")), ("@kind", AttachmentKind));
                delAtt.ExecuteNonQuery();
                attachmentsDeleted++;
            }
            using var delReply = Store.Command(c, tx, "DELETE FROM records WHERE id=@id AND kind=@kind", ("@id", reply.S("id")), ("@kind", ResponseKind));
            delReply.ExecuteNonQuery();
        }
        int complaintsDeleted = 0;
        foreach (var reply in replies)
        {
            foreach (var complaint in Store.ListWhere(c, RiderAsks.ServiceKind, tx, null, ("evaluationResponseId", reply.S("id"))).ToList())
            {
                using var del = Store.Command(c, tx, "DELETE FROM records WHERE id=@id AND kind=@kind", ("@id", complaint.S("id")), ("@kind", RiderAsks.ServiceKind));
                del.ExecuteNonQuery();
                complaintsDeleted++;
            }
        }
        using var delRound = Store.Command(c, tx, "DELETE FROM records WHERE id=@id AND kind=@kind", ("@id", id), ("@kind", RoundKind));
        delRound.ExecuteNonQuery();
        var summary = new JsonObject
        {
            ["repliesDeleted"] = replies.Count,
            ["attachmentsDeleted"] = attachmentsDeleted,
            ["complaintsDeleted"] = complaintsDeleted
        };
        Store.Audit(c, tx, "evaluation.deleted", summary.ToJsonString(), user.Identity!.Name!);
        return summary;
    }

    static JsonObject RoundViewDoc(JsonObject round, ClaimsPrincipal user)
    {
        var view = round.Clone();
        view.Remove("activeSection");
        if (IsSectioned(view))
        {
            // Public form uses section tokens; do not expose the legacy round token as a share link.
            foreach (var section in view["sections"]!.AsArray().OfType<JsonObject>())
                section["staff"] = Array(SectionMembers(view, section).GroupBy(s => s.S("id")).Select(g => g.First()));
        }
        view["allowedActions"] = CanView(user) && AccessPolicy.Has(user, "forms.manage")
            ? Array(round.S("status") switch
            {
                "draft" => new[] { "open", "refresh-staff", "delete" },
                "open" => new[] { "pause", "close", "refresh-staff", "delete" },
                "paused" => new[] { "open", "close", "refresh-staff", "delete" },
                "closed" => new[] { "delete" },
                _ => new[] { "delete" }
            })
            : new JsonArray();
        return view;
    }

    static JsonObject ParseFinanceAnswer(JsonObject input, Store? store, DbConnection? c, DbTransaction? tx, string replyId, ref long replyBytes, AttachmentWriteBatch? batch)
    {
        var card = input["financeAnswer"] as JsonObject ?? throw new ApiError("بطاقة المالية مطلوبة.", 400, "finance_required");
        string reason = card.S("reason");
        var answer = new JsonObject { ["reason"] = reason, ["notes"] = Text(card, "notes", 2000) };
        if (reason != "")
        {
            J.Need(FinanceReasons.Contains(reason), "سبب غير صالح للمالية.", "invalid_finance_reason");
            J.Need(card["overall"] == null && FactorRatings(card).Count == 0, "اختر التقييم أو سبب عدم التقييم.", "rating_or_reason");
        }
        else
        {
            answer["overall"] = Rating(card, "overall", 5);
            var raw = FactorRatings(card);
            J.Need(raw.All(p => FinanceFactors.Contains(p.Key)), "عامل مالية غير معروف.", "invalid_finance_factor");
            var clean = new JsonObject();
            foreach (var pair in raw)
            {
                if (pair.Value is null || pair.Value.GetValueKind() is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined) continue;
                clean[pair.Key] = Rating(raw, pair.Key, 5);
            }
            answer["factorRatings"] = clean;
        }
        answer["complaint"] = card.B("complaint");
        answer["complaintDetails"] = Text(card, "complaintDetails", 2000);
        J.Need(!answer.B("complaint") || answer.S("complaintDetails").Length >= 5, "تفاصيل الشكوى مطلوبة.", "complaint_details");
        if (card["attachments"] is JsonArray arr && arr.Count > 0)
        {
            if (arr.All(x => x is JsonObject o && o.S("id") != "" && o.S("dataBase64") == ""))
                answer["attachments"] = PreservedAttachments(c, tx, replyId, "finance", "finance", arr);
            else
            {
                J.Need(store != null && c != null && tx != null && batch != null, "رفع المرفقات غير متاح.", "attachment_unavailable");
                answer["attachments"] = PersistAttachments(store!, c!, tx!, replyId, "finance", "finance", card, ref replyBytes, batch!);
            }
        }
        else answer["attachments"] = new JsonArray();
        return answer;
    }
}
