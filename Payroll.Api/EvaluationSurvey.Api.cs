using System.Data.Common;
using System.Text.Json.Nodes;

namespace Payroll;

public static partial class EvaluationSurvey
{
    // Localized descriptors are the contract for both the web form and the rider app.
    static JsonObject Label(string key, string ar, string en, string ur) => new() { ["id"] = key, ["labelAr"] = ar, ["labelEn"] = en, ["labelUr"] = ur };
    public static JsonObject Schema(JsonObject round, string? city = null)
    {
        if (!string.IsNullOrEmpty(city)) J.Need(Strings(round, "cities").Contains(city), "مدينة غير موجودة في الجولة.");
        var section = ActiveSection(round);
        var staffSource = section == null
            ? round["staff"]!.AsArray().OfType<JsonObject>()
            : SectionMembers(round, section);
        var apps = section == null
            ? round["apps"]!.DeepClone()
            : section.S("kind") == "app" ? Array(new[] { section.S("key")["app:".Length..] }) : new JsonArray();
        var schema = new JsonObject
        {
            ["schema"] = section == null ? "evaluation-v1" : "evaluation-section-v1",
            ["roundId"] = round.S("id"),
            ["title"] = section == null ? round.S("title") : section.S("title"),
            ["titleEn"] = section == null ? round.S("title") : (section.S("titleEn") is { Length: > 0 } te ? te : section.S("title")),
            ["titleUr"] = section == null ? round.S("title") : (section.S("titleUr") is { Length: > 0 } tu ? tu : section.S("title")),
            ["noticeAr"] = string.Join("\n", new[]
            {
                "تقييمك وشكواك سرية تمامًا.",
                "لا يرى المشرف أو الموظف الذي تقيّمه اسمك ولا إجابتك؛ تراجعها الإدارة المخوّلة فقط لتحسين العمل وحل المشاكل.",
                "لن يُستخدم أي تقييم أو شكوى ضدك بأي شكل. تكلّم بحرية واشرح مشكلتك بالتفصيل."
            }),
            ["noticeEn"] = string.Join("\n", new[]
            {
                "Your rating and complaint are completely confidential.",
                "The supervisor or staff member you rate cannot see your name or your answers; only authorized management reviews them to improve work and solve problems.",
                "No rating or complaint will be used against you in any way. Speak freely and explain your problem in detail."
            }),
            ["noticeUr"] = string.Join("\n", new[]
            {
                "آپ کی درجہ بندی اور شکایت بالکل خفیہ ہیں۔",
                "جس سپروائزر یا ملازم کی آپ درجہ بندی کرتے ہیں وہ آپ کا نام یا جواب نہیں دیکھ سکتا؛ صرف مجاز انتظامیہ کام بہتر بنانے اور مسائل حل کرنے کے لیے ان کا جائزہ لیتی ہے۔",
                "کوئی درجہ بندی یا شکایت آپ کے خلاف کسی بھی طرح استعمال نہیں کی جائے گی۔ آزادانہ بات کریں اور اپنا مسئلہ تفصیل سے بیان کریں۔"
            }),
            ["noticeLines"] = new JsonArray(
                new JsonObject
                {
                    ["ar"] = "تقييمك وشكواك سرية تمامًا.",
                    ["en"] = "Your rating and complaint are completely confidential.",
                    ["ur"] = "آپ کی درجہ بندی اور شکایت بالکل خفیہ ہیں۔"
                },
                new JsonObject
                {
                    ["ar"] = "لا يرى المشرف أو الموظف الذي تقيّمه اسمك ولا إجابتك؛ تراجعها الإدارة المخوّلة فقط لتحسين العمل وحل المشاكل.",
                    ["en"] = "The supervisor or staff member you rate cannot see your name or your answers; only authorized management reviews them to improve work and solve problems.",
                    ["ur"] = "جس سپروائزر یا ملازم کی آپ درجہ بندی کرتے ہیں وہ آپ کا نام یا جواب نہیں دیکھ سکتا؛ صرف مجاز انتظامیہ کام بہتر بنانے اور مسائل حل کرنے کے لیے ان کا جائزہ لیتی ہے۔"
                },
                new JsonObject
                {
                    ["ar"] = "لن يُستخدم أي تقييم أو شكوى ضدك بأي شكل. تكلّم بحرية واشرح مشكلتك بالتفصيل.",
                    ["en"] = "No rating or complaint will be used against you in any way. Speak freely and explain your problem in detail.",
                    ["ur"] = "کوئی درجہ بندی یا شکایت آپ کے خلاف کسی بھی طرح استعمال نہیں کی جائے گی۔ آزادانہ بات کریں اور اپنا مسئلہ تفصیل سے بیان کریں۔"
                }),
            ["complaintConfidentialityReminderAr"] = "شكواك سرية ولن تُستخدم ضدك.",
            ["complaintConfidentialityReminderEn"] = "Your complaint is confidential and will not be used against you.",
            ["complaintConfidentialityReminderUr"] = "آپ کی شکایت خفیہ ہے اور آپ کے خلاف استعمال نہیں ہوگی۔",
            ["cities"] = round["cities"]!.DeepClone(), ["apps"] = apps,
            ["staff"] = Array(staffSource.Where(s =>
            {
                if (string.IsNullOrEmpty(city)) return true;
                return ServesSectionCity(s, section, city);
            }).Select(s => new JsonObject
            {
                // Remote staff are listed under every work city, so older clients that filter by cities still show them.
                ["id"] = s.S("id"), ["name"] = s.S("name"), ["cities"] = s.B("allCities") ? round["cities"]!.DeepClone() : s["cities"]!.DeepClone(), ["departments"] = s["departments"]!.DeepClone(),
                ["apps"] = s["apps"]!.DeepClone(), ["roles"] = s.B("pendingRoleApproval") ? new JsonArray("بانتظار موافقة غزال") : s["roles"]!.DeepClone(),
                ["allApps"] = s.B("allApps"), ["allCities"] = s.B("allCities"),
                ["appCities"] = s["appCities"]?.DeepClone() ?? new JsonObject()
            })),
            ["departmentOptions"] = Array(StaffDepartmentOptions),
            ["identityFields"] = Array(new[] {
                Label("iqama", "رقم الإقامة", "Iqama", "اقامہ نمبر"), Label("name", "الاسم", "Name", "نام"),
                Label("city", "مدينة العمل", "Work city", "کام کا شہر"), Label("nationality", "الجنسية", "Nationality", "قومیت"),
                Label("workApps", "منصات العمل الحالية", "Current work platforms", "موجودہ کام کے پلیٹ فارم"), Label("phone", "الجوال (اختياري)", "Phone (optional)", "فون (اختیاری)") }),
            ["staffRatingMin"] = 1, ["staffRatingMax"] = 10, ["appRatingMin"] = 1, ["appRatingMax"] = 5,
            ["staffDimensions"] = Array(new[] {
                Label("professional", "المستوى الفني والمهني", "Professional level", "پیشہ ورانہ سطح"),
                Label("treatment", "الاحترام وطريقة التعامل", "Respect and manner", "احترام اور رویہ"),
                Label("support", "الدعم المقدم", "Support given", "فراہم کردہ تعاون"),
                Label("responseSpeed", "سرعة الاستجابة", "Response speed", "جواب دینے کی رفتار"),
                Label("problemSolving", "حل المشكلات وتسهيل العمل", "Problem solving", "مسائل کا حل") }),
            ["staffReasons"] = Array(new[] {
                Label("not_dealt", "لم أتعامل معه", "Never dealt with this person", "ان سے کبھی معاملہ نہیں ہوا"),
                Label("unknown_person", "لا أعرفه", "Do not know this person", "ان کو نہیں جانتا"),
                Label("not_worked_app", "لم أعمل في منصته", "Did not work on their platform", "ان کے پلیٹ فارم پر کام نہیں کیا") }),
            ["appReasons"] = Array(new[] {
                Label("unavailable_city", "غير متاح في مكتب المدينة", "Unavailable in this city office", "اس شہر کے دفتر میں دستیاب نہیں"),
                Label("not_worked", "لم أعمل عليها", "Did not work on this platform", "اس پلیٹ فارم پر کام نہیں کیا") }),
            ["factors"] = Array(new[] {
                Label("orderVolume", "عدد الطلبات", "Order volume", "آرڈرز کی تعداد"), Label("fairPay", "عدالة احتساب الأجر", "Fair pay calculation", "اجرت کا منصفانہ حساب"),
                Label("workingHours", "عدد ساعات العمل", "Working hours", "کام کے اوقات"), Label("shiftTimes", "مواعيد الورديات", "Shift times", "شفٹ کے اوقات"),
                Label("flexibility", "المرونة", "Flexibility", "لچک"), Label("customerService", "خدمة عملاء المنصة", "Platform customer service", "پلیٹ فارم کسٹمر سروس"),
                Label("appProblems", "مشكلات التطبيق", "App problems", "ایپ کے مسائل"), Label("worthIt", "هل العمل يستحق", "Is the work worth it", "کیا یہ کام قابل قدر ہے") }),
            ["factorMode"] = "optionalStars", ["editPolicyPending"] = false, ["uploadsPolicyPending"] = false,
            ["maxEdits"] = MaxEdits, ["attachmentRetentionDays"] = (int)AttachmentRetention.TotalDays,
            ["attachmentLimits"] = new JsonObject { ["imagesPerCard"] = 2, ["voicePerCard"] = 1, ["voiceSeconds"] = 60, ["replyBytes"] = 6 * 1024 * 1024 },
            ["nationalities"] = Array(new[] {
                Label("saudi", "سعودي", "Saudi", "سعودی"), Label("egyptian", "مصري", "Egyptian", "مصری"),
                Label("yemeni", "يمني", "Yemeni", "یمنی"), Label("sudanese", "سوداني", "Sudanese", "سوڈانی"),
                Label("pakistani", "باكستاني", "Pakistani", "پاکستانی"), Label("indian", "هندي", "Indian", "بھارتی"),
                Label("bangladeshi", "بنغلاديشي", "Bangladeshi", "بنگلہ دیشی"), Label("syrian", "سوري", "Syrian", "شامی"),
                Label("jordanian", "أردني", "Jordanian", "اردنی"), Label("filipino", "فلبيني", "Filipino", "فلپائنی")
            }),
            ["allowedActions"] = new JsonArray("submit")
        };
        if (section != null)
        {
            schema["sectionKey"] = section.S("key");
            schema["sectionKind"] = section.S("kind");
            schema["includeFinanceCard"] = section.S("kind") == "app";
            schema["includeSystemsCard"] = section.S("kind") == "app";
            schema["financeFactors"] = Array(new[] {
                Label("responsiveness", "تجاوب القسم المالي مع استفساراتك وشكاواك", "Finance responsiveness", "مالیہ کا جواب"),
                Label("accuracy", "احتساب الراتب بدون أخطاء", "Salary accuracy", "تنخواہ کی درستگی"),
                Label("correctionSpeed", "سرعة تعديل الأخطاء والفروقات بعد الإبلاغ", "Correction speed", "درستگی کی رفتار"),
                Label("clarity", "وضوح تفاصيل الراتب والخصومات", "Pay clarity", "تنخواہ کی وضاحت")
            });
            schema["financeReasons"] = Array(new[] {
                Label("not_dealt_finance", "لم أتعامل مع القسم المالي", "Did not deal with finance", "مالیہ سے معاملہ نہیں ہوا")
            });
            schema["systemsTitleAr"] = "تقييم الأنظمة والبيانات";
            schema["systemsTitleEn"] = "Systems and data";
            schema["systemsTitleUr"] = "نظام اور ڈیٹا";
            schema["systemsFactors"] = Array(new[] {
                Label("formsEase", "سهولة النماذج والروابط اللي بتوصلك", "Ease of forms and links that reach you", "فارم اور لنکس کی آسانی"),
                Label("dataClarity", "وضوح البيانات والجداول المشاركة معك", "Clarity of shared data and tables", "شیئر کردہ ڈیٹا اور جدولوں کی وضاحت"),
                Label("helpfulness", "دور الأنظمة في تسهيل شغلك", "How systems help your work", "نظام کا کام آسان بنانا"),
                Label("responsiveness", "الاستجابة لمقترحاتك وشكاواك وطلباتك", "Response to your suggestions, complaints and requests", "تجاویز، شکایات اور درخواستوں کا جواب")
            });
            schema["systemsReasons"] = Array(new[] {
                Label("not_used_forms_data", "لم أستخدم نماذج أو بيانات الشركة", "Did not use company forms or data", "کمپنی کے فارم یا ڈیٹا استعمال نہیں کیے")
            });
        }
        return schema;
    }
    static JsonObject RoundView(JsonObject round, HttpContext ctx) => RoundViewDoc(round, ctx.User);
    static JsonObject FilterStatistics(JsonObject stats, string dimension)
    {
        string field = dimension switch { "city" => "byCity", "app" => "byApp", "supervisor" => "bySupervisor", "nationality" => "byNationality", "factors" => "factorAverages", "trend" => "dailyTrend", _ => throw new ApiError("نوع إحصاء غير صالح.") };
        return new() { ["roundId"] = stats.S("roundId"), ["totalResponses"] = stats.N("totalResponses"), ["items"] = stats[field]!.DeepClone() };
    }
    public static byte[] ExportReplies(DbConnection c, System.Security.Claims.ClaimsPrincipal user, string id, string? section = null)
    {
        Demand(user); AccessPolicy.Demand(user, ExportPermission);
        var round = Round(c, id); var schema = Schema(round);
        var names = round["staff"]!.AsArray().OfType<JsonObject>().ToDictionary(s => s.S("id"), s => s.S("name"));
        var dimensions = schema["staffDimensions"]!.AsArray().OfType<JsonObject>().ToDictionary(d => d.S("id"), d => d.S("labelAr"));
        var factors = schema["factors"]!.AsArray().OfType<JsonObject>().ToDictionary(d => d.S("id"), d => d.S("labelAr"));
        var apps = Strings(round, "apps");
        var columns = new List<ExcelColumn> { new("الرد"), new("القسم"), new("الإقامة"), new("الاسم"), new("المدينة"), new("الجنسية"), new("الجوال"), new("تاريخ الإرسال"), new("نوع البطاقة"), new("البطاقة"), new("اسم الموظف"), new("سبب عدم التقييم"), new("التقييم العام", true) };
        columns.AddRange(StaffDimensions.Select(d => new ExcelColumn(dimensions[d], true)));
        columns.Add(new("ملاحظات")); columns.Add(new("شكوى")); columns.Add(new("تفاصيل الشكوى"));
        columns.AddRange(Factors.Select(f => new ExcelColumn(factors[f], true)));
        columns.Add(new("مالية: العام", true));
        columns.AddRange(FinanceFactors.Select(f => new ExcelColumn("مالية: " + (f switch
        {
            "responsiveness" => "التجاوب",
            "accuracy" => "الدقة",
            "correctionSpeed" => "سرعة التصحيح",
            "clarity" => "الوضوح",
            _ => f
        }), true)));
        columns.Add(new("مالية: السبب")); columns.Add(new("مالية: ملاحظات")); columns.Add(new("مالية: مرفقات", true)); columns.Add(new("مالية: شكوى"));
        columns.Add(new("أنظمة: العام", true));
        columns.AddRange(SystemsFactors.Select(f => new ExcelColumn("أنظمة: " + (f switch
        {
            "formsEase" => "سهولة النماذج",
            "dataClarity" => "وضوح البيانات",
            "helpfulness" => "تسهيل الشغل",
            "responsiveness" => "الاستجابة",
            _ => f
        }), true)));
        columns.Add(new("أنظمة: السبب")); columns.Add(new("أنظمة: ملاحظات")); columns.Add(new("أنظمة: مرفقات", true)); columns.Add(new("أنظمة: شكوى"));
        columns.AddRange(apps.Select(a => new ExcelColumn("منصة العمل: " + a)));
        var cells = new List<object?[]>();
        foreach (var row in Responses(c, user, id, section: section))
        {
            var finance = row["financeAnswer"] as JsonObject;
            var systems = row["systemsAnswer"] as JsonObject;
            // Prefer Arabic section title; never export the raw sectionKey (app:Ninja / dept:hr).
            string sectionLabel = row.S("sectionTitle");
            if (sectionLabel == "" && row.S("sectionKey") != "")
            {
                var match = round["sections"] is JsonArray secs
                    ? secs.OfType<JsonObject>().FirstOrDefault(s => s.S("key") == row.S("sectionKey"))
                    : null;
                sectionLabel = match?.S("title") ?? row.S("sectionKey");
            }
            bool financeWritten = false;
            bool systemsWritten = false;
            object? FinanceVal(Func<JsonObject, object?> read) =>
                finance == null || financeWritten ? null : read(finance);
            object? SystemsVal(Func<JsonObject, object?> read) =>
                systems == null || systemsWritten ? null : read(systems);
            foreach (bool staff in new[] { true, false })
            foreach (var card in row[staff ? "staffAnswers" : "appAnswers"]!.AsArray().OfType<JsonObject>())
            {
                var values = new List<object?>
                {
                    row.S("id"), sectionLabel,
                    row.S("iqama"), row.S("name"), row.S("city"), row.S("nationality"), row.S("phone"), row.S("createdAt"),
                    staff ? "موظف" : "منصة", card.S(staff ? "staffId" : "app"), staff ? names.GetValueOrDefault(card.S("staffId"), "") : "",
                    card.S("reason"), card["overall"] == null ? null : card.N("overall")
                };
                values.AddRange(StaffDimensions.Select(d => (object?)(card["ratings"]?[d] == null ? null : card["ratings"]!.N(d))));
                values.Add(card.S("notes")); values.Add(card.B("complaint") ? "نعم" : "لا"); values.Add(card.S("complaintDetails"));
                values.AddRange(Factors.Select(f => (object?)(card["factorRatings"]?[f] == null ? null : card["factorRatings"]!.N(f))));
                // Finance / systems columns once per reply: first card row only (later rows stay blank so column sums stay correct).
                values.Add(FinanceVal(fin => fin["overall"] == null ? null : fin.N("overall")));
                values.AddRange(FinanceFactors.Select(f => FinanceVal(fin => fin["factorRatings"]?[f] == null ? null : fin["factorRatings"]!.N(f))));
                values.Add(FinanceVal(fin => fin.S("reason")));
                values.Add(FinanceVal(fin => fin.S("notes")));
                values.Add(FinanceVal(fin => fin["attachments"] is JsonArray a ? a.Count : 0));
                values.Add(FinanceVal(fin => fin.B("complaint") ? "نعم" : "لا"));
                if (finance != null) financeWritten = true;
                values.Add(SystemsVal(sys => sys["overall"] == null ? null : sys.N("overall")));
                values.AddRange(SystemsFactors.Select(f => SystemsVal(sys => sys["factorRatings"]?[f] == null ? null : sys["factorRatings"]!.N(f))));
                values.Add(SystemsVal(sys => sys.S("reason")));
                values.Add(SystemsVal(sys => sys.S("notes")));
                values.Add(SystemsVal(sys => sys["attachments"] is JsonArray a ? a.Count : 0));
                values.Add(SystemsVal(sys => sys.B("complaint") ? "نعم" : "لا"));
                if (systems != null) systemsWritten = true;
                values.AddRange(apps.Select(a => (object?)(Strings(row, "workApps").Contains(a) ? "نعم" : "")));
                cells.Add(values.ToArray());
            }
        }
        return ExportWorkbook(columns, cells);
    }
    public static void Map(WebApplication app, RouteGroupBuilder api, Store store)
    {
        // Flat api.Map* paths (not MapGroup) so tools/api_inventory.py can index them.
        api.MapGet("/forms/evaluations/staff", (HttpContext ctx) => { Demand(ctx.User); return store.Read(c => Store.List(c, StaffKind)); }).WithTags("Forms and responses").WithSummary("List evaluation staff cards");
        api.MapPost("/forms/evaluations/staff/import", (JsonObject input, HttpContext ctx) => store.Write((c, tx) => ImportStaff(c, tx, ctx.User, input))).WithTags("Forms and responses").WithSummary("Import evaluation staff cards");
        api.MapPut("/forms/evaluations/staff/{id}", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) => PutStaff(c, tx, ctx.User, id, input))).WithTags("Forms and responses").WithSummary("Update one evaluation staff card");
        api.MapGet("/forms/evaluations/rounds", (HttpContext ctx) => { Demand(ctx.User); return store.Read(c => Store.List(c, RoundKind).Select(r => RoundView(r, ctx)).ToList()); }).WithTags("Forms and responses").WithSummary("List evaluation rounds");
        api.MapPost("/forms/evaluations/rounds", (JsonObject input, HttpContext ctx) => store.Write((c, tx) => RoundView(CreateRound(c, tx, ctx.User, input), ctx))).WithTags("Forms and responses").WithSummary("Create evaluation round");
        api.MapGet("/forms/evaluations/rounds/{id}", (string id, HttpContext ctx) => { Demand(ctx.User); return store.Read(c => RoundView(Round(c, id), ctx)); }).WithTags("Forms and responses").WithSummary("Read evaluation round");
        api.MapDelete("/forms/evaluations/rounds/{id}", (string id, [Microsoft.AspNetCore.Mvc.FromBody] JsonObject input, HttpContext ctx) => store.Write((c, tx) => DeleteRound(c, tx, ctx.User, id, input ?? new JsonObject(), store))).WithTags("Forms and responses").WithSummary("Delete evaluation round after typing its title");
        api.MapPost("/forms/evaluations/rounds/{id}/sections/{sectionKey}/staff/{staffId}", (string id, string sectionKey, string staffId, HttpContext ctx) =>
            store.Write((c, tx) => AddSectionStaff(c, tx, ctx.User, id, Uri.UnescapeDataString(sectionKey), staffId))).WithTags("Forms and responses").WithSummary("Add staff override to an evaluation section");
        api.MapDelete("/forms/evaluations/rounds/{id}/sections/{sectionKey}/staff/{staffId}", (string id, string sectionKey, string staffId, HttpContext ctx) =>
            store.Write((c, tx) => RemoveSectionStaff(c, tx, ctx.User, id, Uri.UnescapeDataString(sectionKey), staffId))).WithTags("Forms and responses").WithSummary("Remove staff override from an evaluation section");
        api.MapPost("/forms/evaluations/rounds/{id}/{action}", (string id, string action, HttpContext ctx) => store.Write((c, tx) => RoundView(action == "refresh-staff" ? RefreshStaff(c, tx, ctx.User, id) : Transition(c, tx, ctx.User, id, action switch { "open" => "open", "pause" => "paused", "close" => "closed", _ => throw new ApiError("إجراء غير معروف.", 404) }), ctx))).WithTags("Forms and responses").WithSummary("Open, pause, close or refresh staff of an evaluation round");
        api.MapGet("/forms/evaluations/rounds/{id}/responses", (string id, string? supervisorId, string? department, string? section, HttpContext ctx) => store.Read(c => ApiPagination.Apply(Responses(c, ctx.User, id, supervisorId, department, section), ctx.Request.Query, always: true,
            whitelist: new HashSet<string>(StringComparer.Ordinal) { "id", "iqama", "name", "city", "nationality", "phone", "companyId", "createdAt", "workApps", "sectionKey", "sectionTitle" }))).WithTags("Forms and responses").WithSummary("Page evaluation replies");
        api.MapGet("/forms/evaluations/rounds/{id}/statistics", (string id, string? city, string? nationalityId, string? section, HttpContext ctx) => store.Read(c => Statistics(c, ctx.User, id, city, nationalityId, section))).WithTags("Forms and responses").WithSummary("Evaluation statistics for charts");
        api.MapGet("/forms/evaluations/rounds/{id}/statistics/{dimension}", (string id, string dimension, string? city, string? nationalityId, string? section, HttpContext ctx) => store.Read(c => FilterStatistics(Statistics(c, ctx.User, id, city, nationalityId, section), dimension))).WithTags("Forms and responses").WithSummary("Evaluation statistics by dimension");
        api.MapGet("/forms/evaluations/rounds/{id}/export", (string id, string? section, HttpContext ctx) => Results.File(store.Read(c => ExportReplies(c, ctx.User, id, section)), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "evaluations.xlsx")).WithTags("Forms and responses").WithSummary("Export evaluation replies workbook");
        api.MapGet("/forms/evaluations/rounds/{id}/scorecards/supervisors", (string id, string? city, string? app, string? department, string? supervisorId, string? nationalityId, string? section, HttpContext ctx) =>
            store.Read(c => SupervisorScorecard(c, ctx.User, id, city, app, department, supervisorId, nationalityId, section))).WithTags("Forms and responses").WithSummary("Per-supervisor evaluation scorecard");
        api.MapGet("/forms/evaluations/rounds/{id}/scorecards/apps", (string id, string? city, string? app, string? department, string? supervisorId, string? nationalityId, string? section, HttpContext ctx) =>
            store.Read(c => AppScorecard(c, ctx.User, id, city, app, department, supervisorId, nationalityId, section))).WithTags("Forms and responses").WithSummary("Per-app evaluation scorecard");
        api.MapGet("/forms/evaluations/rounds/{id}/scorecards/finance", (string id, string? city, string? app, string? nationalityId, string? section, HttpContext ctx) =>
            store.Read(c => FinanceScorecard(c, ctx.User, id, city, app, nationalityId, section))).WithTags("Forms and responses").WithSummary("Finance-card evaluation scorecard per app");
        api.MapGet("/forms/evaluations/rounds/{id}/scorecards/systems", (string id, string? city, string? app, string? nationalityId, string? section, HttpContext ctx) =>
            store.Read(c => SystemsScorecard(c, ctx.User, id, city, app, nationalityId, section))).WithTags("Forms and responses").WithSummary("Systems-card evaluation scorecard per app");
        api.MapGet("/forms/evaluations/rounds/{id}/scorecards/heatmap", (string id, string? city, string? app, string? department, string? supervisorId, string? nationalityId, string? section, HttpContext ctx) =>
            store.Read(c => SupervisorDimensionHeatmap(c, ctx.User, id, city, app, department, supervisorId, nationalityId, section))).WithTags("Forms and responses").WithSummary("Supervisor × dimension heatmap");
        api.MapGet("/forms/evaluations/rounds/{id}/scorecards/city-app", (string id, string? city, string? app, string? department, string? supervisorId, string? nationalityId, string? section, HttpContext ctx) =>
            store.Read(c => CityAppMatrix(c, ctx.User, id, city, app, department, supervisorId, nationalityId, section))).WithTags("Forms and responses").WithSummary("City × app evaluation matrix");
        api.MapGet("/forms/evaluations/rounds/{id}/scorecards/{kind}/export", (string id, string kind, string? city, string? app, string? department, string? supervisorId, string? nationalityId, string? section, HttpContext ctx) =>
            Results.File(store.Read(c => ExportScorecard(c, ctx.User, id, kind, city, app, department, supervisorId, nationalityId, section)), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"evaluation-{kind}.xlsx")).WithTags("Forms and responses").WithSummary("Export an evaluation scorecard workbook");
        api.MapGet("/forms/evaluations/attachments/{attachmentId}", (string attachmentId, HttpContext ctx) => store.Read(c => PlayAttachment(store, c, ctx.User, attachmentId))).WithTags("Forms and responses").WithSummary("Play or download an evaluation attachment");
        app.MapGet("/api/public/evaluations/{token}", (string token, string? city) => store.Read(c => Schema(ByToken(c, token), city))).RequireRateLimiting("sensitive").WithTags("Forms and responses").WithSummary("Public evaluation schema");
        app.MapPost("/api/public/evaluations/{token}", (string token, JsonObject input) =>
            WriteWithAttachments(store, (c, tx, batch) => Submit(c, tx, token, input, store: store, attachments: batch)))
            .RequireRateLimiting("sensitive").WithTags("Forms and responses").WithSummary("Submit public evaluation reply");
        app.MapGet("/api/rider-mobile/evaluations", (HttpContext ctx) =>
        {
            MobileSessions.Authenticate(store, ctx);
            return store.Read(c => Store.ListWhere(c, RoundKind, null, null, ("status", "open")).Where(r => !IsSectioned(r)).Select(r =>
            {
                string token = r.S("sharingToken");
                string hosted = CampaignLinks.Base(app.Configuration, ctx) + "/rider-form.html?evaluation=" + Uri.EscapeDataString(token);
                return new { id = r.S("id"), title = r.S("title"), token, schema = "evaluation-v1", hostedUrl = hosted };
            }).ToList());
        }).RequireRateLimiting("sensitive").WithTags("Riders").WithSummary("Open evaluation rounds for signed-in rider");
        app.MapPost("/api/rider-mobile/evaluations/{token}/launch", (string token, HttpContext ctx) =>
        {
            var session = MobileSessions.Authenticate(store, ctx);
            string baseUrl = CampaignLinks.Base(app.Configuration, ctx);
            return store.Write((c, tx) => IssueLaunch(c, tx, session.S("iqama"), token, baseUrl));
        }).RequireRateLimiting("sensitive").WithTags("Riders").WithSummary("Issue one-time hosted evaluation launch URL");
        app.MapPost("/api/public/evaluations/{token}/session", (string token, JsonObject input) => store.Write((c, tx) =>
            ExchangeLaunch(c, tx, token, input.Required("code")))).RequireRateLimiting("sensitive").WithTags("Forms and responses").WithSummary("Exchange one-time launch code for survey-scoped session");
        app.MapGet("/api/rider-mobile/evaluations/{token}", (string token, string? city, HttpContext ctx) =>
        { AuthenticateEvaluation(store, ctx, token); return store.Read(c => Schema(ByToken(c, token), city)); }).RequireRateLimiting("sensitive").WithTags("Riders").WithSummary("Evaluation schema for signed-in rider");
        app.MapPost("/api/rider-mobile/evaluations/{token}", (string token, JsonObject input, HttpContext ctx) =>
            WriteWithAttachments(store, (c, tx, batch) =>
            { var session = ValidateEvaluation(c, ctx, token, tx); return Submit(c, tx, token, input, session.S("iqama"), store, batch); }))
            .RequireRateLimiting("sensitive").WithTags("Riders").WithSummary("Submit evaluation reply as signed-in rider");
        app.MapPut("/api/rider-mobile/evaluations/{token}", (string token, JsonObject input, HttpContext ctx) =>
            WriteWithAttachments(store, (c, tx, batch) =>
            { var session = ValidateEvaluation(c, ctx, token, tx); return Edit(c, tx, token, input, session.S("iqama"), store, batch); }))
            .RequireRateLimiting("sensitive").WithTags("Riders").WithSummary("Edit evaluation reply as signed-in rider");
        app.MapPut("/api/public/evaluations/{token}", (string token, JsonObject input) =>
            WriteWithAttachments(store, (c, tx, batch) =>
            {
                var round = ByToken(c, token, tx);
                var section = ActiveSection(round);
                string iqama = J.Iqama(input.Required("iqama"));
                string id = section == null ? ReplyId(round.S("id"), iqama) : ReplyId(round.S("id"), section.S("key"), iqama);
                var reply = Store.Get(c, id, ResponseKind, tx) ?? throw new ApiError("الرد غير موجود.", 404, "not_found");
                // Possession of the random edit receipt is the only public proof — never ReplyId or a typed iqama alone.
                if (!ReceiptEquals(input.S("receipt"), reply.S("editReceipt")))
                    throw new ApiError("إيصال التعديل غير صالح.", 403, "edit_receipt_invalid");
                return Edit(c, tx, token, input, iqama, store, batch);
            })).RequireRateLimiting("sensitive").WithTags("Forms and responses").WithSummary("Edit public evaluation reply with receipt");
    }
}
