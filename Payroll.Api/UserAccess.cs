using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
namespace Payroll;

public static class UserAccess
{
    public sealed record PermissionDef(string Code, string ModuleCode, string ModuleAr, string NameAr, string DescriptionAr);
    public sealed record RoleDef(string Code, string NameAr, string DefaultDepartment, int DefaultRankLevel, string SummaryAr, string[] DefaultPermissions);

    // 1. كتالوج الصلاحيات الذرية الدقيقة (Atomic Permissions) مع الشرح التشغيلي لكل صلاحية
    public static readonly PermissionDef[] PermissionsCatalog =
    [
        new("treasury.opening","Treasury","الخزينة","اعتماد تسوية افتتاح موثقة","معاينة وتأكيد تسوية الأرصدة القديمة دون تعديل الوثائق الأصلية."),
        new("treasury.journals","Treasury","الخزينة","عرض قيود الأستاذ ضمن النطاق","عرض القيود غير النقدية مع نطاق السجل القانوني."),
        new("treasury.liability.settle","Treasury","الخزينة","سداد التزام مثبت","دفع التزام معتمد من نقدية الشركة مع فحص الرصيد والمستند."),
        new("debts.create.equipment","Debts","المديونيات","تسجيل عهد التجهيزات","لا يمنح إنشاء بقية مخالفات وخدمات الأسطول."),
        // إدارة المناديب والملفات
        new("riders.view", "Riders", "دليل المناديب والملفات", "استعراض دليل المناديب", "عرض جدول المناديب وبيانات الكفالة والهوية والبحث والتصفية."),
        new("riders.view_360", "Riders", "دليل المناديب والملفات", "عرض الملف الشامل 360° والوثائق", "الاطلاع على ملف المندوب الشامل وقائمة التحقق من الوثائق السبعة وتحميل المرفقات."),
        new("riders.edit", "Riders", "دليل المناديب والملفات", "إضافة وتعديل بيانات ومرفقات المناديب", "تحديث بيانات المندوب ورفع وتجديد الوثائق الرسمية (الإقامة، الرخصة، كرت التشغيل)."),
        new("riders.sync_baseline", "Riders", "دليل المناديب والملفات", "مزامنة واستيراد الدليل الأساسي", "استيراد ومزامنة البيانات المرجعية الشاملة للمناديب واعتمادها."),

        // إدارة المديونيات وطلبات الموارد البشرية والسلف
        new("debts.view", "Debts", "نظام المديونيات والاستقطاعات", "استعراض سجل المديونيات وكشوف الحساب", "عرض المديونيات النشطة والمسددة وكشف حساب المندوب 360°."),
        new("debts.create.hr", "Debts", "نظام المديونيات والاستقطاعات", "إنشاء طلبات ورسوم الموارد البشرية (HR)", "تسجيل طلبات رخص العمل وتجديد الإقامة ونقل الكفالة والتأشيرات والتأمين الطبي وتحديد آلية التحمل."),
        new("debts.create.advance", "Debts", "نظام المديونيات والاستقطاعات", "إنشاء السلف النقدية والبنكية والعهد", "صرف وإثبات السلف النقدية من الصندوق الداخلي أو السلف البنكية من حسابات البنوك."),
        new("debts.create.fleet", "Debts", "نظام المديونيات والاستقطاعات", "تسجيل مديونيات الأسطول والمخالفات والحوادث", "إثبات المخالفات المرورية وتلفيات الحوادث وأقساط المركبات وفروقات الوقود."),
        new("debts.create.platform", "Debts", "نظام المديونيات والاستقطاعات", "إثبات مديونيات كاش المنصات المرحلة (جاهز)", "تسجيل مديونيات كاش تطبيق جاهز والمنصات المرحلة على المناديب."),
        new("debts.reschedule", "Debts", "نظام المديونيات والاستقطاعات", "إعادة جدولة الأقساط وتعديل حالة المديونية", "تعديل قيمة القسط الشهري أو إيقاف/تفعيل الاستقطاع بعد مراجعة الاتفاق."),
        new("debts.collect", "Debts", "نظام المديونيات والاستقطاعات", "تحصيل وسداد المديونيات (مفرد ومجمع)", "قبض المديونيات نقداً أو بنكياً وإصدار سندات التحصيل ورفع شيت التحصيل المجمع."),
        new("debts.waive", "Debts", "نظام المديونيات والاستقطاعات", "منح إعفاء وتسوية إدارية للمديونية", "اعتماد خصم أو إعفاء إداري كلي/جزئي وترحيله لحساب مصروف الإعفاءات (5202)."),
        new("debts.transfer_sponsor", "Debts", "نظام المديونيات والاستقطاعات", "شراء ونقل المديونية بين السجلات التجارية", "ترحيل ملكية المديونية مالياً بين البوابا وإكسبرس والتجارية عبر حساب جاري السجلات الشقيقة (1310)."),
        new("debts.import_opening", "Debts", "نظام المديونيات والاستقطاعات", "رفع واعتماد أرصدة أول المدة للمديونيات", "معاينة وترحيل شيت أرصدة أول المدة للمديونيات مقابل حساب (3101)."),

        // الصناديق والخزائن وشجرة الحسابات وصندوق جاهز
        new("treasury.view", "Treasury", "الخزينة والصناديق وشجرة الحسابات", "استعراض أرصدة السجلات وشجرة الحسابات والقيود", "الاطلاع على أرصدة الصندوق الداخلي الرئيسي وفروعه والبنوك ودفتر القيود المحاسبية."),
        new("treasury.transfer", "Treasury", "الخزينة والصناديق وشجرة الحسابات", "التحويل المالي بين الصناديق والبنوك", "إنشاء قيود التحويل والتمويل بين الصندوق الداخلي الرئيسي وحسابات البنوك."),
        new("treasury.jahez.manage", "Treasury", "الخزينة والصناديق وشجرة الحسابات", "إدارة صندوق كاش جاهز ورسوم فتح الحسابات", "تحصيل كاش تطبيق جاهز وقبض رسوم فتح حسابات جاهز (200 ر.س) في عهدة المشرف."),
        new("treasury.jahez.handover", "Treasury", "الخزينة والصناديق وشجرة الحسابات", "تسليم وتصفير عهدة صندوق جاهز للصندوق الداخلي", "ترحيل وتصفير رصيد خزنة جاهز الميدانية (SUP.*) في نهاية اليوم إلى الصندوق الداخلي الرئيسي (MAIN.*)."),

        // الاعتمادات المتبادلة والإشعارات وتطبيق المندوب
        new("approvals.view", "Approvals", "الاعتمادات المتبادلة والإشعارات", "استعراض طلبات الاعتماد ومركز الإشعارات", "متابعة الطلبات المتبادلة بين الموارد البشرية والمالية والتشغيل وقائمة المهام."),
        new("approvals.decide", "Approvals", "الاعتمادات المتبادلة والإشعارات", "البت في طلبات الاعتماد المتبادل (موافقة/رفض/تعليق)", "اعتماد أو رفض أو تعليق المعاملات المشتركة بين الأقسام حسب الرتبة الوظيفية."),
        new("mobile.broadcast", "Approvals", "الاعتمادات المتبادلة والإشعارات", "إرسال إشعارات وتوجيهات لتطبيق المندوب", "بث الإشعارات المالية والإدارية والتشغيلية وروابط النماذج لبوابة المندوب."),

        new("forms.view", "Controls", "ضوابط العمليات", "عرض النماذج والردود", "تُفحص في الخادم ولا تمنح بمجرد ظهور الزر."),
        new("forms.delete", "Controls", "ضوابط العمليات", "حذف النماذج والردود", "تُفحص في الخادم ولا تمنح بمجرد ظهور الزر."),
        new("payments.record", "Controls", "ضوابط العمليات", "تسجيل دفعات الرواتب", "تُفحص في الخادم ولا تمنح بمجرد ظهور الزر."),
        new("adjustments.create", "Controls", "ضوابط العمليات", "تسجيل تعديلات الرواتب", "تُفحص في الخادم ولا تمنح بمجرد ظهور الزر."),
        new("imports.view", "Controls", "ضوابط العمليات", "معاينة مصادر التشغيل", "تُفحص في الخادم ولا تمنح بمجرد ظهور الزر."),
        new("imports.commit", "Controls", "ضوابط العمليات", "اعتماد استيراد التشغيل", "تُفحص في الخادم ولا تمنح بمجرد ظهور الزر."),
        new("policies.view", "Controls", "ضوابط العمليات", "عرض سياسات الرواتب", "تُفحص في الخادم ولا تمنح بمجرد ظهور الزر."),
        new("policies.manage", "Controls", "ضوابط العمليات", "إدارة واعتماد السياسات", "تُفحص في الخادم ولا تمنح بمجرد ظهور الزر."),
        new("inquiry.issue", "Controls", "ضوابط العمليات", "إصدار رموز استعلام مالي", "تُفحص في الخادم ولا تمنح بمجرد ظهور الزر."),
        new("treasury.disburse", "Controls", "ضوابط العمليات", "ترحيل صرف الخزينة", "تُفحص في الخادم ولا تمنح بمجرد ظهور الزر."),
        new("riders.financial_edit", "Controls", "ضوابط العمليات", "تعديل بيانات المندوب المالية", "تُفحص في الخادم ولا تمنح بمجرد ظهور الزر."),
        new("debts.create.housing", "Controls", "ضوابط العمليات", "إنشاء استقطاعات السكن", "تُفحص في الخادم ولا تمنح بمجرد ظهور الزر."),
        new("debts.create.administrative", "Controls", "ضوابط العمليات", "إنشاء خصومات إدارية", "تُفحص في الخادم ولا تمنح بمجرد ظهور الزر."),
        // الرواتب والنماذج وإدارة النظام
        new("payroll.view", "Payroll", "مسيرات الرواتب ودفتر الأستاذ", "معاينة مسيرات الرواتب ودفتر الأستاذ", "استعراض معاينات الرواتب واستقطاعات الأقساط وكشوف الرواتب."),
        new("payroll.approve", "Payroll", "مسيرات الرواتب ودفتر الأستاذ", "اعتماد مسيرات الرواتب النهائية", "قفل واعتماد مسير الرواتب الشهري وترحيل الأقساط تلقائياً."),
        new("forms.review", "Forms", "النماذج والحملات واستبيانات المناديب", "مراجعة واعتماد ردود نماذج المناديب", "فحص الهوية واعتماد أو رفض ردود المناديب على الحملات والاستبيانات."),
        new("forms.manage", "Forms", "النماذج والحملات واستبيانات المناديب", "إنشاء ونشر النماذج والحملات", "تصميم الاستبيانات وإصدار روابط المشاركة ورموز QR."),
        new("users.manage", "Admin", "إدارة المستخدمين والأدوار والنظام", "إدارة المستخدمين والأدوار والصلاحيات (RBAC)", "إنشاء الحسابات وتعيين الأدوار والأقسام والرتب والصلاحيات المخصصة.")
    ];

    // 2. مصفوفة الأدوار المعتمدة (تشمل الأدوار الأساسية المتوافقة مع الاختبارات + الأدوار التشغيلية الثمانية لمنظومة البوابة)
    public static readonly RoleDef[] RolesCatalog =
    new RoleDef[]
    {
        new("Admin", "مدير النظام العام (Super Admin)", "MGT", 5,
            "صلاحية كاملة ومطلقة على كافة الخزائن، السجلات التجارية، المديونيات، الاعتمادات، الرواتب، وإدارة المستخدمين.",
            PermissionsCatalog.Select(p => p.Code).ToArray()),

        new("SuperAdmin", "المدير العام / الإدارة العليا", "MGT", 5,
            "صلاحية شاملة للإدارة العليا والرقابة المالية والتشغيلية واعتماد السياسات والرواتب.",
            PermissionsCatalog.Select(p => p.Code).ToArray()),

        new("Accountant", "محاسب مالي ومسؤول خزينة", "FIN", 3,
            "إدارة الصندوق الداخلي والبنوك، تسجيل وتحصيل المديونيات والسلف وطلبات HR، التحويل بين الصناديق، واستيراد أرصدة أول المدة.",
            [
                "riders.view", "riders.view_360",
                "debts.view", "debts.create.hr", "debts.create.advance", "debts.create.fleet", "debts.create.platform",
                "debts.reschedule", "debts.collect", "debts.waive", "debts.transfer_sponsor", "debts.import_opening",
                "treasury.view", "treasury.transfer", "treasury.jahez.manage", "treasury.jahez.handover",
                "approvals.view", "mobile.broadcast", "payroll.view"
            ]),

        new("FinancialAccountant", "رئيس الحسابات والخزينة المركزية", "FIN", 4,
            "الإشراف المالي الكامل على الخزينة المركزية (MAIN) والبنوك (BANK)، مطابقة وتصديق تحصيلات HR وتصفيات جاهز، واعتماد القيود.",
            [
                "riders.view", "riders.view_360",
                "debts.view", "debts.create.hr", "debts.create.advance", "debts.create.fleet", "debts.create.platform",
                "debts.reschedule", "debts.collect", "debts.waive", "debts.transfer_sponsor", "debts.import_opening",
                "treasury.view", "treasury.transfer", "treasury.jahez.manage", "treasury.jahez.handover",
                "approvals.view", "approvals.decide", "mobile.broadcast", "payroll.view"
            ]),

        new("HRSpecialist", "أخصائي الموارد البشرية والعلاقات الحكومية", "HR", 2,
            "إنشاء طلبات خدمات الموارد البشرية (رخص العمل، الإقامات، نقل الكفالة، التأشيرات، التأمين)، تحديد نسب التحمل، وتوثيق ملفات المناديب.",
            [
                "riders.view", "riders.view_360", "riders.edit",
                "debts.view", "debts.create.hr", "debts.create.advance",
                "approvals.view", "approvals.decide", "mobile.broadcast", "forms.review"
            ]),

        new("OperationsManager", "مدير التشغيل والمنصات", "OPS", 4,
            "الإشراف التشغيلي على المناديب والمنصات، متابعة المديونيات وكشوف الحساب، ومراجعة الاعتمادات والتقارير.",
            [
                "riders.view", "riders.view_360", "riders.edit",
                "debts.view", "debts.create.platform", "debts.create.fleet", "debts.reschedule",
                "treasury.view", "treasury.jahez.manage", "treasury.jahez.handover",
                "approvals.view", "approvals.decide", "mobile.broadcast", "payroll.view", "forms.review"
            ]),

        new("PlatformsSupervisor", "مشرف منصات وعهدة صندوق جاهز", "OPS", 2,
            "إدارة معاملات تطبيق جاهز، تحصيل مديونيات كاش جاهز ورسوم فتح الحسابات (SUP.*)، وتسليم وتصفير العهدة للصندوق الداخلي.",
            [
                "riders.view", "riders.view_360",
                "debts.view", "debts.create.platform", "debts.collect",
                "treasury.view", "treasury.jahez.manage", "treasury.jahez.handover",
                "approvals.view", "mobile.broadcast"
            ]),

        new("FleetSupervisor", "مشرف الحركة والأسطول والورشة", "FLT", 2,
            "تسجيل المخالفات المرورية وتلفيات الحوادث وأقساط المركبات وفروقات الوقود ومتابعة مديونيات الأسطول.",
            [
                "riders.view", "riders.view_360", "riders.edit",
                "debts.view", "debts.create.fleet",
                "approvals.view"
            ]),

        new("HousingSupervisor", "مشرف السكن والإعاشة", "HR", 1,
            "متابعة المناديب في السكن وتسجيل استقطاعات السكن والإيجارات.",
            [
                "riders.view", "debts.view", "approvals.view"
            ]),

        new("WarehouseOfficer", "أمين المستودع والعهد والتجهيزات", "FLT", 1,
            "تسجيل عهد الجوالات والشنط والتجهيزات ومتابعة إخلاء الطرف.",
            [
                "riders.view", "debts.view", "debts.create.equipment", "approvals.view"
            ]),

        new("Reviewer", "مراجع البيانات والنماذج", "OPS", 1,
            "مراجعة وتدقيق ردود المناديب على النماذج والحملات والتحقق من الهوية وجودة بيانات الدليل.",
            [
                "riders.view", "forms.review"
            ])
    }.Select(r=>r with { DefaultPermissions=r.DefaultPermissions.Concat(AccessPolicy.CompatibilityDefaults(r.Code)).Distinct(StringComparer.Ordinal).ToArray() }).ToArray();

    public static readonly string[] Roles = RolesCatalog.Select(r => r.Code).ToArray();

    public static RoleDef ResolveRoleDef(string? role) =>
        RolesCatalog.FirstOrDefault(r => string.Equals(r.Code, role, StringComparison.OrdinalIgnoreCase))
        ?? throw new ApiError("دور غير معروف.");

    public static string Role(JsonObject u) => u.S("role", u.S("id") == "administrator" ? "Admin" : "Reviewer");
    public static string Status(JsonObject u) => u.S("status", "active");
    public static string Department(JsonObject u)
    {
        var def = ResolveRoleDef(Role(u));
        string dept = u.S("department", def.DefaultDepartment);
        return string.IsNullOrWhiteSpace(dept) ? def.DefaultDepartment : dept;
    }
    public static int RankLevel(JsonObject u)
    {
        var def = ResolveRoleDef(Role(u));
        int rank = (int)u.N("rankLevel", def.DefaultRankLevel);
        return rank is >= 1 and <= 5 ? rank : def.DefaultRankLevel;
    }
    public static string[] EffectivePermissions(JsonObject u)
    {
        string role = Role(u);
        if (role is "Admin" or "SuperAdmin") return PermissionsCatalog.Select(p => p.Code).ToArray();
        var def = ResolveRoleDef(role);
        var set = new HashSet<string>(def.DefaultPermissions, StringComparer.OrdinalIgnoreCase);
        if (u["customPermissions"] is JsonArray custom)
        {
            foreach (var item in custom)
            {
                string code = item?.GetValue<string>() ?? "";
                if (PermissionsCatalog.FirstOrDefault(p => p.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) is {} permission)
                    set.Add(permission.Code);
            }
        }
        if(u["deniedPermissions"] is JsonArray denied)foreach(var permission in denied)set.Remove(permission?.GetValue<string>()??"");
        return set.ToArray();
    }

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
    public static JsonObject? Find(DbConnection c, string username, DbTransaction? tx = null) =>
        Store.List(c, "user", tx).SingleOrDefault(u => Normalize(u.S("username")) == Normalize(username));

    public static JsonObject Public(JsonObject u)
    {
        var roleDef = ResolveRoleDef(Role(u));
        string deptCode = Department(u);
        string deptAr = FinancialCore.DepartmentsCatalog
            .OfType<JsonObject>()
            .FirstOrDefault(d => d.S("code") == deptCode)?.S("nameAr") ?? deptCode;
        var perms = new JsonArray();
        foreach (var p in EffectivePermissions(u)) perms.Add(p);
        var customPerms = new JsonArray();
        if (u["customPermissions"] is JsonArray cp)
            foreach (var x in cp) customPerms.Add(x?.GetValue<string>() ?? "");

        return new()
        {
            ["id"] = u.S("id"),
            ["username"] = u.S("username"),
            ["role"] = Role(u),
            ["roleNameAr"] = roleDef.NameAr,
            ["roleSummaryAr"] = roleDef.SummaryAr,
            ["department"] = deptCode,
            ["departmentNameAr"] = deptAr,
            ["rankLevel"] = RankLevel(u),
            ["status"] = Status(u),
            ["permissions"] = perms,
            ["customPermissions"] = customPerms,
            ["deniedPermissions"] = u["deniedPermissions"]?.DeepClone()??new JsonArray(),
            ["financialScope"] = FinancialScopes.Effective(u),
            ["createdAt"] = u.S("createdAt"),
            ["lastLoginAt"] = u.S("lastLoginAt")
        };
    }

    public static JsonObject RiderView(JsonObject rider)
    {
        var result = new JsonObject();
        foreach (string field in new[] { "id", "iqama", "name", "phone", "nationality", "sponsor", "sponsorId", "engagementType", "city", "operationalRole", "drivingLicense", "platforms", "status", "policyId", "workPreference" })
            result[field] = rider.S(field);
        return result;
    }

    public static void ValidatePermissionList(JsonArray? permissions)
    {
        if(permissions==null)return;
        J.Need(permissions.Count<=PermissionsCatalog.Length&&permissions.OfType<JsonValue>().Count()==permissions.Count,"قائمة صلاحيات غير صالحة.");
        foreach(var p in permissions)J.Need(PermissionsCatalog.Any(x=>x.Code==p!.GetValue<string>()),"صلاحية غير معروفة.");
    }
    public static JsonObject Create(string username, string password, string role, string? id = null, string? department = null, int? rankLevel = null, JsonArray? customPermissions = null)
    {
        J.Need(username.Length is >= 3 and <= 60 && Regex.IsMatch(username, @"^[\p{L}\p{N}_.@-]+$"), "اسم المستخدم من3إلى60حرفًا دون مسافات أو محارف خاصة.");
        J.Need(password.Length is >= 12 and <= 200, "كلمة المرور من12إلى200حرف.");
        J.Need(Roles.Contains(role), "دور غير صالح.");
        var roleDef = ResolveRoleDef(role);
        string dept = string.IsNullOrWhiteSpace(department) ? roleDef.DefaultDepartment : department.Trim().ToUpperInvariant();
        J.Need(rankLevel==null||rankLevel is >=1 and <=5,"رتبة غير صالحة.");
        J.Need(FinancialCore.DepartmentsCatalog.OfType<JsonObject>().Any(d=>d.S("code")==dept),"قسم غير صالح.");
        ValidatePermissionList(customPermissions);
        int rank = rankLevel??roleDef.DefaultRankLevel;
        var salt = RandomNumberGenerator.GetBytes(16);
        var doc = new JsonObject
        {
            ["id"] = id ?? Guid.NewGuid().ToString("N"),
            ["username"] = username,
            ["role"] = role,
            ["department"] = dept,
            ["rankLevel"] = rank,
            ["status"] = "active",
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["lastLoginAt"] = "",
            ["securityVersion"] = Guid.NewGuid().ToString("N"),
            ["salt"] = Convert.ToBase64String(salt),
            ["hash"] = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32))
        };
        if (customPermissions != null) doc["customPermissions"] = customPermissions.DeepClone();
        return doc;
    }

    public static bool Password(JsonObject? u, string password)
    {
        // Always perform PBKDF2, including unknown usernames, to reduce enumeration by timing.
        if (password.Length > 200) return false;
        var salt = u == null ? new byte[16] : Convert.FromBase64String(u.S("salt"));
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(hash, u == null ? new byte[32] : Convert.FromBase64String(u.S("hash"))) && u != null && Status(u) == "active";
    }

    public static ClaimsPrincipal Principal(JsonObject u)
    {
        var claims=new List<Claim>{new(ClaimTypes.NameIdentifier,u.S("id")),new(ClaimTypes.Name,u.S("username")),new(ClaimTypes.Role,Role(u)),new("department",Department(u)),new("rankLevel",RankLevel(u).ToString()),new("securityVersion",u.S("securityVersion"))};
        claims.AddRange(EffectivePermissions(u).Select(p=>new Claim("permission",p)));
        claims.AddRange(FinancialScopes.Claims(u));
        return new(new ClaimsIdentity(claims,CookieAuthenticationDefaults.AuthenticationScheme));
    }
    // Persist only the session identity. Permissions/scopes are loaded from the current user per request.
    public static ClaimsPrincipal SessionPrincipal(JsonObject u)=>new(new ClaimsIdentity(new[]{
        new Claim(ClaimTypes.NameIdentifier,u.S("id")),new Claim(ClaimTypes.Name,u.S("username")),new Claim(ClaimTypes.Role,Role(u)),new Claim("securityVersion",u.S("securityVersion"))
    },CookieAuthenticationDefaults.AuthenticationScheme));

    public static async Task ValidateCookie(CookieValidatePrincipalContext context)
    {
        var store = context.HttpContext.RequestServices.GetRequiredService<Store>();
        var u = store.Read(c => Find(c, context.Principal?.Identity?.Name ?? ""));
        if (u == null || !Roles.Contains(Role(u)) || Status(u) != "active" || (context.Principal!.FindFirst("securityVersion") is { } version && version.Value != u.S("securityVersion")))
        { context.RejectPrincipal(); await context.HttpContext.SignOutAsync(); return; }
        // Upgrade legacy administrator cookies without invalidating a working production session.
        context.ReplacePrincipal(Principal(u));
        context.ShouldRenew = false;
    }

    public static bool Allowed(string role,string method,string path)
    {
        if(!Roles.Contains(role))return false;
        return AccessPolicy.Allowed(Principal(new JsonObject{["role"]=role,["username"]="permission-test"}),method,path);
    }

    public static void Map(RouteGroupBuilder api, Store store)
    {
        api.MapGet("/roles-catalog", () => new
        {
            roles = RolesCatalog,
            permissions = PermissionsCatalog,
            departments = FinancialCore.DepartmentsCatalog,
            financialScopeCatalog=new{sponsors=FinancialCore.Sponsors,accounts=FinancialCore.ChartOfAccounts.Where(a=>a.IsVault),categories=FinancialCore.CategoriesCatalog,platforms=new[]{"Jahez","Keeta","HungerStation","Ninja","Other"}}
        });

        api.MapGet("/users", () => store.Read(c => Store.List(c, "user").Select(Public)));

        api.MapPost("/users", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            string name = input.Required("username");
            J.Need(Find(c, name, tx) == null, "اسم المستخدم مسجل بالفعل.");
            string role = input.Required("role");
            string? dept = input.ContainsKey("department") ? input.S("department") : null;
            int? rank = input.ContainsKey("rankLevel") ? (int)input.N("rankLevel") : null;
            var customPerms = input["customPermissions"] as JsonArray;
            var u = Create(name, input.Required("password"), role, null, dept, rank, customPerms);
            if(input["deniedPermissions"] is JsonArray denied){ValidatePermissionList(denied);u["deniedPermissions"]=denied.DeepClone();}
            if(input["financialScope"] is JsonObject scope)u["financialScope"]=FinancialScopes.Normalize(scope);
            Store.Save(c, "user", u, tx);
            Store.Audit(c, tx, "admin.user_create", u.S("id"), ctx.User.Identity!.Name!);
            return Public(u);
        }));

        api.MapPut("/users/{id}/status", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            var u = Store.Get(c, id, "user", tx) ?? throw new ApiError("الحساب غير موجود.", 404);
            string role = input.S("role", Role(u)), status = input.S("status", Status(u));
            J.Need(Roles.Contains(role) && status is "active" or "suspended", "دور أو حالة غير صالحة.");
            J.Need(!(Role(u) is "Admin" or "SuperAdmin" && Status(u) == "active" && (role is not ("Admin" or "SuperAdmin") || status != "active") && Store.List(c, "user", tx).Count(x => Role(x) is "Admin" or "SuperAdmin" && Status(x) == "active") == 1), "لا يمكن تعطيل آخر مسؤول نشط.");
            bool roleChanged=Role(u)!=role;
            if(roleChanged){var definition=ResolveRoleDef(role);if(!input.ContainsKey("department"))u["department"]=definition.DefaultDepartment;if(!input.ContainsKey("rankLevel"))u["rankLevel"]=definition.DefaultRankLevel;}
            u["role"] = role;
            u["status"] = status;
            if (input.ContainsKey("department") && !string.IsNullOrWhiteSpace(input.S("department")))
                u["department"] = input.S("department").Trim().ToUpperInvariant();
            if (input.ContainsKey("rankLevel"))
            {
                int rnk = (int)input.N("rankLevel");
                if (rnk is >= 1 and <= 5) u["rankLevel"] = rnk;
            }
            if(input["customPermissions"] is JsonArray cp){ValidatePermissionList(cp);u["customPermissions"]=cp.DeepClone();}
            if(input["financialScope"] is JsonObject scope)u["financialScope"]=FinancialScopes.Normalize(scope);
            if(input["deniedPermissions"] is JsonArray denied){ValidatePermissionList(denied);u["deniedPermissions"]=denied.DeepClone();}
            J.Need(FinancialCore.DepartmentsCatalog.OfType<JsonObject>().Any(d=>d.S("code")==Department(u)),"قسم غير صالح.");
            if(input.ContainsKey("rankLevel"))J.Need(input.N("rankLevel") is >=1 and <=5&&input.N("rankLevel")==Math.Truncate(input.N("rankLevel")),"رتبة غير صالحة.");
            u["securityVersion"] = Guid.NewGuid().ToString("N");
            Store.Save(c, "user", u, tx);
            Store.Audit(c, tx, "admin.user_update", id, ctx.User.Identity!.Name!);
            return Public(u);
        }));
    }
}
