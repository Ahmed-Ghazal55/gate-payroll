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
    public sealed record PermissionDef(string Code, string ModuleCode, string ModuleAr, string NameAr, string DescriptionAr, bool Deprecated=false);
    public sealed record RoleDef(string Code, string NameAr, string DefaultDepartment, int DefaultRankLevel, string SummaryAr, string[] DefaultPermissions);

    // 1. كتالوج الصلاحيات الذرية الدقيقة (Atomic Permissions) مع الشرح التشغيلي لكل صلاحية
    // Deprecated entries stay for stored-user aliases but are hidden from the role editor catalog.
    public static IEnumerable<PermissionDef> EditorPermissionsCatalog=>PermissionsCatalog.Where(p=>!p.Deprecated);
    public static readonly PermissionDef[] PermissionsCatalog =
    [
        new("erp.sync.run","Admin","إدارة النظام","تشغيل مزامنة ERP (مهجور)","مهجور: أُزيلت نقاط المزامنة؛ الصلاحية تبقى كاسم مستعار فقط.",Deprecated:true),
        new("approval.bypass_maker_checker","Admin","إدارة النظام","تجاوز شرط الشخصين","منح مؤرخ بسحب؛ يرفع شرط المجهّز ≠ المعتمد فقط مع سجل وشارة."),
        new("approvals.dual.bank_file","Admin","إدارة النظام","اعتماد ملف البنك بشخصين","صلاحية مستقلة لإصدار وإرسال ملف البنك باعتماد شخصين مختلفين؛ قابلة للمنح لأي دور."),
        new("approvals.dual.payment_out","Admin","إدارة النظام","اعتماد الصرف بشخصين","صلاحية مستقلة لصرف الأموال للخارج باعتماد شخصين مختلفين؛ قابلة للمنح لأي دور."),
        new("approvals.dual.shared_rider_release","Admin","إدارة النظام","فك حجز المندوب المشترك بشخصين","صلاحية مستقلة لفك حجز المندوب المشترك باعتماد شخصين مختلفين؛ قابلة للمنح لأي دور."),
        new("operations.view","Operations","العمليات","عرض العمليات","عرض العمليات وحسابات المناديب ضمن الشركة والمنصة."),
        new("pay.view","Payroll","الرواتب","عرض إطار الرواتب","عرض إعدادات التطبيقات والدورات والكشوف ضمن النطاق."),
        new("pay.manage","Payroll","الرواتب","تحضير الرواتب","إنشاء مسودات الإعدادات وتحضير الدورات دون اعتماد ذاتي."),
        new("pay.approve","Payroll","الرواتب","اعتماد الرواتب","اعتماد مستقل لإعدادات ودورات المسار الجديد."),
        new("platforms.hs.view","Platforms","المنصات","مراجعة وقائع هنقر","عرض الفواتير والاعتراضات والوقائع والمعاينات ضمن النطاق."),
        new("platforms.hs.manage","Platforms","المنصات","تحضير وقائع هنقر","استيراد الوقائع واقتراح التوزيع والاعتراض والتصحيح دون اعتماد ذاتي."),
        new("platforms.hs.approve","Platforms","المنصات","اعتماد وقائع هنقر","اعتماد مستقل للوقائع والتوزيع والعكس ضمن النطاق المالي."),
        new("companies.view","Companies","الشركات والسجلات","عرض الشركات والسجلات","عرض الشركات والسجلات والوثائق ضمن النطاق."),
        new("companies.manage","Companies","الشركات والسجلات","إدارة الشركات والسجلات","تعديل الشركات والسجلات والوثائق ضمن النطاق مع سجل مراجعة."),
        new("companies.balances.view","Companies","الشركات والسجلات","عرض أرصدة الشركات","عرض جدول الأرصدة المالية لكل شركة في تبويب الإحصائيات."),
        new("finance.bankfile.export","Treasury","الخزينة","معاينة وتصدير ملف البنك","تصدير الرواتب المعتمدة ضمن نطاق الشركة دون تسجيل دفع أو تعديل قيد."),
        new("treasury.opening","Treasury","الخزينة","اعتماد تسوية افتتاح موثقة","معاينة وتأكيد تسوية الأرصدة القديمة دون تعديل الوثائق الأصلية."),
        new("treasury.jahez.receive","Treasury","الخزينة","استلام عهدة جاهز بعد عدها","قرار مالي مستقل يطابق التصفيات والرسوم كلًا على حدة قبل ترحيلهما."),
        new("finance.journal.create","Treasury","الخزينة","إنشاء قيد يدوي","مسودة متوازنة في نطاق الشركة والفترة المفتوحة."),
        new("finance.journal.approve","Treasury","الخزينة","اعتماد قيد يدوي","مراجع مستقل فقط؛ العكس بقيد منفصل."),
        new("treasury.journals","Treasury","الخزينة","عرض قيود الأستاذ ضمن النطاق","عرض القيود غير النقدية مع نطاق السجل القانوني."),
        new("treasury.liability.settle","Treasury","الخزينة","سداد التزام مثبت","دفع التزام معتمد من نقدية الشركة مع فحص الرصيد والمستند."),
        new("debts.create.equipment","Debts","المديونيات","تسجيل عهد التجهيزات","لا يمنح إنشاء بقية مخالفات وخدمات الأسطول."),
        // إدارة المناديب والملفات
        new("riders.view", "Riders", "دليل المناديب والملفات", "استعراض دليل المناديب", "عرض جدول المناديب وبيانات الكفالة والهوية والبحث والتصفية."),
        new("riders.view_360", "Riders", "دليل المناديب والملفات", "عرض الملف الشامل 360° والوثائق", "الاطلاع على ملف المندوب الشامل وقائمة التحقق من الوثائق السبعة وتحميل المرفقات."),
        new("riders.iban.view", "Riders", "دليل المناديب والملفات", "عرض أرقام الآيبان", "الاطلاع على أرقام الآيبان في دليل القوى العاملة والملف 360 والتصدير (H20)."),
        new("riders.saudi_salary.view", "Riders", "دليل المناديب والملفات", "عرض رواتب موظفي التأمينات", "الاطلاع على رواتب موظفي الرواتب السعوديين من نظام عمر (H20)."),
        new("riders.legal_cases.view", "Riders", "دليل المناديب والملفات", "عرض القضايا القانونية", "الاطلاع على قضايا الموارد البشرية المرتبطة بالمناديب والموظفين (H20)."),
        new("riders.unified_number.view", "Riders", "دليل المناديب والملفات", "عرض الرقم الموحّد للكفيل", "الاطلاع على الرقم الموحّد للسجل في تبويب موظفي الرواتب السعوديين (H10)."),
        new("riders.edit", "Riders", "دليل المناديب والملفات", "إضافة وتعديل بيانات ومرفقات المناديب", "تحديث بيانات المندوب ورفع وتجديد الوثائق الرسمية (الإقامة، الرخصة، كرت التشغيل)."),

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
        new("accounting.chart.view", "Treasury", "الخزينة والصناديق وشجرة الحسابات", "عرض دليل الحسابات", "استعراض شجرة الحسابات النشطة والموقوفة دون تعديل."),
        new("accounting.chart.create", "Treasury", "الخزينة والصناديق وشجرة الحسابات", "إنشاء حساب في الدليل", "إضافة حساب جديد تحت أب؛ الطبيعة مقفولة على الأب."),
        new("accounting.chart.edit", "Treasury", "الخزينة والصناديق وشجرة الحسابات", "تعديل حساب في الدليل", "تعديل الاسم وأعلام الترحيل؛ لا حذف ولا تغيير رمز عليه ترحيل."),
        new("accounting.chart.deactivate", "Treasury", "الخزينة والصناديق وشجرة الحسابات", "إيقاف أو تفعيل حساب", "إيقاف حساب مستخدم يحتاج شخصاً ثانياً؛ لا حذف نهائي."),
        new("treasury.transfer", "Treasury", "الخزينة والصناديق وشجرة الحسابات", "التحويل المالي بين الصناديق والبنوك", "إنشاء قيود التحويل والتمويل بين الصندوق الداخلي الرئيسي وحسابات البنوك."),
        new("treasury.jahez.manage", "Treasury", "الخزينة والصناديق وشجرة الحسابات", "إدارة صندوق كاش جاهز ورسوم فتح الحسابات", "تحصيل كاش تطبيق جاهز وقبض رسوم فتح حسابات جاهز (200 ر.س) في عهدة المشرف."),
        new("treasury.jahez.handover", "Treasury", "الخزينة والصناديق وشجرة الحسابات", "تسليم وتصفير عهدة صندوق جاهز للصندوق الداخلي", "ترحيل وتصفير رصيد خزنة جاهز الميدانية (SUP.*) في نهاية اليوم إلى الصندوق الداخلي الرئيسي (MAIN.*)."),

        // الاعتمادات المتبادلة والإشعارات وتطبيق المندوب
        new("advances.request", "Approvals", "الاعتمادات المتبادلة والإشعارات", "رفع طلبات السلف للمناديب تحت الإشراف", "المشرف يطلب سلفة نقدية أو بنكية لمندوب تحت إشرافه حسب نظام الرتب (BR §25)."),
        new("advances.review", "Approvals", "الاعتمادات المتبادلة والإشعارات", "مراجعة طلبات السلف واعتمادها", "متاحة للمحاسب المكلّف فقط عبر التوجيه؛ الصلاحية وحدها لا تكفي."),
        new("advances.payout", "Approvals", "الاعتمادات المتبادلة والإشعارات", "صرف السلف المعتمدة من الخزنة أو البنك", "متاحة لمسؤول الخزنة أو البنك المكلّف فقط."),
        new("org.manage", "Approvals", "الاعتمادات المتبادلة والإشعارات", "إدارة الأقسام والرتب والتفويضات والإشراف والتوجيه", "سجلات مؤرخة لا تُحذف."),
        new("approvals.view", "Approvals", "الاعتمادات المتبادلة والإشعارات", "استعراض طلبات الاعتماد ومركز الإشعارات", "متابعة الطلبات المتبادلة بين الموارد البشرية والمالية والتشغيل وقائمة المهام."),
        new("approvals.decide", "Approvals", "الاعتمادات المتبادلة والإشعارات", "البت في طلبات الاعتماد المتبادل (موافقة/رفض/تعليق)", "اعتماد أو رفض أو تعليق المعاملات المشتركة بين الأقسام حسب الرتبة الوظيفية."),
        new("mobile.sessions", "Approvals", "الاعتمادات المتبادلة والإشعارات", "إدارة جلسات تطبيق المندوب", "عرض وإلغاء الجلسات ضمن نطاق الشركة دون كشف رموز الدخول أو الجهاز."),
        new("mobile.broadcast", "Approvals", "الاعتمادات المتبادلة والإشعارات", "إرسال إشعارات وتوجيهات لتطبيق المندوب", "بث الإشعارات المالية والإدارية والتشغيلية وروابط النماذج لبوابة المندوب."),
        new("mobile.complaints.confidential", "Approvals", "الاعتمادات المتبادلة والإشعارات", "الاطلاع على شكاوى المناديب السرية", "قراءة ومعالجة شكاوى المناديب السرية المخفية عن المشرفين والموظفين."),
        new("riders.iban_data.approve_first", "Approvals", "الاعتمادات المتبادلة والإشعارات", "الاعتماد الأول لتحديث الآيبان والبيانات", "اعتماد الخطوة الأولى فقط لطلبات تحديث الآيبان أو البيانات الشخصية للمناديب دون تفعيل نهائي."),
        new("riders.iban_data.approve_second", "Approvals", "الاعتمادات المتبادلة والإشعارات", "الاعتماد الثاني النهائي لتحديث الآيبان والبيانات", "اعتماد الخطوة الثانية النهائية لطلبات تحديث الآيبان أو البيانات الشخصية من شخص مختلف عن المعتمد الأول."),

        new("forms.view", "Controls", "ضوابط العمليات", "عرض النماذج والردود", "عرض النماذج والحملات وردود المناديب."),
        new("forms.delete", "Controls", "ضوابط العمليات", "حذف النماذج والردود (مهجور)", "مهجور: الحذف يبقى لمسؤول النظام فقط؛ الصلاحية لا تفتح أي مسار.",Deprecated:true),
        new("payments.record", "Controls", "ضوابط العمليات", "تسجيل دفعات الرواتب", "تسجيل دفعات الرواتب اليدوية في المسار القديم."),
        new("adjustments.create", "Controls", "ضوابط العمليات", "تسجيل تعديلات الرواتب", "إضافة تعديلات رواتب في المسار القديم."),
        new("imports.view", "Controls", "ضوابط العمليات", "معاينة مصادر التشغيل", "فتح مركز الاستيراد ومعاينة ملفات التشغيل وتحميل النماذج."),
        new("imports.commit", "Controls", "ضوابط العمليات", "اعتماد استيراد التشغيل", "اعتماد حفظ ملف التشغيل بعد معاينته."),
        new("policies.view", "Controls", "ضوابط العمليات", "عرض سياسات الرواتب", "الاطلاع على سياسات الرواتب."),
        new("policies.manage", "Controls", "ضوابط العمليات", "إدارة واعتماد السياسات", "إنشاء وتعديل واعتماد سياسات الرواتب."),
        new("inquiry.issue", "Controls", "ضوابط العمليات", "إصدار رموز استعلام مالي", "إصدار رمز يتيح للمندوب الاستعلام عن حسابه."),
        new("treasury.disburse", "Controls", "ضوابط العمليات", "ترحيل صرف الخزينة", "ترحيل عمليات الصرف من الخزينة."),
        new("riders.financial_edit", "Controls", "ضوابط العمليات", "تعديل بيانات المندوب المالية", "تعديل الآيبان والبيانات المالية للمندوب."),
        new("debts.create.housing", "Controls", "ضوابط العمليات", "إنشاء استقطاعات السكن", "تسجيل استقطاعات السكن والإيجار."),
        new("debts.create.administrative", "Controls", "ضوابط العمليات", "إنشاء خصومات إدارية", "تسجيل الخصومات الإدارية وأرصدة المديونية القديمة."),
        // الرواتب والنماذج وإدارة النظام
        new("payroll.view", "Payroll", "مسيرات الرواتب ودفتر الأستاذ", "معاينة مسيرات الرواتب ودفتر الأستاذ", "استعراض معاينات الرواتب واستقطاعات الأقساط وكشوف الرواتب."),
        new("payroll.approve", "Payroll", "مسيرات الرواتب ودفتر الأستاذ", "اعتماد مسيرات الرواتب النهائية", "قفل واعتماد مسير الرواتب الشهري وترحيل الأقساط تلقائياً."),
        new("forms.review", "Forms", "النماذج والحملات واستبيانات المناديب", "مراجعة واعتماد ردود نماذج المناديب", "فحص الهوية واعتماد أو رفض ردود المناديب على الحملات والاستبيانات."),
        new("forms.manage", "Forms", "النماذج والحملات واستبيانات المناديب", "إنشاء ونشر النماذج والحملات", "تصميم الاستبيانات وإصدار روابط المشاركة ورموز QR."),
        new("forms.responses.allCompanies", "Forms", "النماذج والحملات واستبيانات المناديب", "عرض ردود كل الشركات", "رؤية ومراجعة ردود النماذج لكل الشركات عند غياب نطاق شركة؛ بدونها لا تظهر أي ردود خارج النطاق."),
        new("users.manage", "Admin", "إدارة المستخدمين والأدوار والنظام", "إدارة المستخدمين والأدوار والصلاحيات (RBAC)", "إنشاء الحسابات وتعيين الأدوار والأقسام والرتب والصلاحيات المخصصة."),
        new("admin.audit.view", "Admin", "إدارة المستخدمين والأدوار والنظام", "عرض سجل التدقيق", "استعراض أحداث تغيير البيانات والاعتمادات الذاتية دون أحداث القراءة."),
        new("admin.read_summary.view", "Admin", "إدارة المستخدمين والأدوار والنظام", "عرض ملخص القراءة اليومي", "ملخص يومي لقراءات المناطق الحساسة والتصدير دون صف لكل قراءة."),
        new("admin.settings.manage", "Admin", "إدارة المستخدمين والأدوار والنظام", "إدارة إعدادات النظام", "قراءة وحفظ إعدادات النظام في المرجع."),
        new("admin.notify.manage", "Admin", "إدارة المستخدمين والأدوار والنظام", "إدارة إعدادات الإشعارات", "مصفوفة التنبيهات وقنوات الإشعار."),
        new("admin.org.manage", "Admin", "إدارة المستخدمين والأدوار والنظام", "إدارة حدود الاعتماد وسياسات المهلة", "حدود الرتب وسياسات التصعيد."),
        new("notify.send.general", "Admin", "إدارة المستخدمين والأدوار والنظام", "إرسال إشعار عام", "بث إشعار عام للأدوار أو الأقسام."),
        new("notify.send.custom", "Admin", "إدارة المستخدمين والأدوار والنظام", "إرسال إشعار مخصص", "إشعار لمستخدمين محددين."),
        new("tasks.assign", "Admin", "إدارة المستخدمين والأدوار والنظام", "تعيين المهام", "إنشاء وتعيين مهام تشغيلية."),
        new("tasks.manage", "Admin", "إدارة المستخدمين والأدوار والنظام", "إدارة المهام", "تعديل وإغلاق المهام التشغيلية."),
        new("audit.view", "Admin", "إدارة المستخدمين والأدوار والنظام", "عرض سجل التدقيق", "قراءة سجل تدقيق العمليات مع التصفية بالفترة والتصفح صفحة بصفحة.")
    ];

    // 2. مصفوفة الأدوار المعتمدة (تشمل الأدوار الأساسية المتوافقة مع الاختبارات + الأدوار التشغيلية الثمانية لمنظومة البوابة)
    public static readonly RoleDef[] RolesCatalog =
    new RoleDef[]
    {
        new("Admin", "مدير النظام العام", "MGT", 5,
            "صلاحية كاملة ومطلقة على كافة الخزائن، السجلات التجارية، المديونيات، الاعتمادات، الرواتب، وإدارة المستخدمين.",
            PermissionsCatalog.Select(p => p.Code).ToArray()),

        new("SuperAdmin", "المدير العام / الإدارة العليا", "MGT", 5,
            "صلاحية شاملة للإدارة العليا والرقابة المالية والتشغيلية واعتماد السياسات والرواتب.",
            PermissionsCatalog.Select(p => p.Code).ToArray()),

        new("SystemAdmin", "مسؤول النظام", "MGT", 5,
            "كل صلاحيات الكتالوج والاستثناءات الافتراضية؛ الاعتماد الذاتي يظهر في تقرير المراجعة (M-0667).",
            PermissionsCatalog.Select(p => p.Code).ToArray()),

        new("FinancialManager", "المدير المالي", "FIN", 4,
            "قراءة التقارير المالية ومعاينة وتصدير ملف البنك ضمن النطاق.",
            ["treasury.view","treasury.journals","payroll.view","riders.view","debts.view","finance.bankfile.export","platforms.hs.view","pay.view","operations.view","accounting.chart.view","accounting.chart.create","accounting.chart.edit","accounting.chart.deactivate"]),

        new("Accountant", "محاسب مالي ومسؤول خزينة", "FIN", 3,
            "إدارة الصندوق الداخلي والبنوك، تسجيل وتحصيل المديونيات والسلف وطلبات HR، التحويل بين الصناديق، واستيراد أرصدة أول المدة.",
            [
                "pay.view", "pay.manage", "platforms.hs.view", "platforms.hs.manage",
                "operations.view", "riders.view", "riders.view_360", "riders.iban.view",
                "debts.view", "debts.create.hr", "debts.create.advance", "debts.create.fleet", "debts.create.platform",
                "debts.reschedule", "debts.collect", "debts.waive", "debts.transfer_sponsor", "debts.import_opening",
                "treasury.view", "treasury.transfer", "treasury.jahez.manage", "treasury.jahez.handover",
                "accounting.chart.view", "accounting.chart.create", "accounting.chart.edit",
                "approvals.view", "mobile.broadcast", "payroll.view"
            ]),

        new("FinancialAccountant", "رئيس الحسابات والخزينة المركزية", "FIN", 4,
            "الإشراف المالي الكامل على الخزينة المركزية (MAIN) والبنوك (BANK)، مطابقة وتصديق تحصيلات HR وتصفيات جاهز، واعتماد القيود.",
            [
                "pay.view", "pay.manage", "platforms.hs.view", "platforms.hs.manage", "platforms.hs.approve", "pay.approve",
                "operations.view", "riders.view", "riders.view_360", "riders.iban.view",
                "debts.view", "debts.create.hr", "debts.create.advance", "debts.create.fleet", "debts.create.platform",
                "debts.reschedule", "debts.collect", "debts.waive", "debts.transfer_sponsor", "debts.import_opening",
                "treasury.view", "treasury.transfer", "treasury.jahez.manage", "treasury.jahez.handover",
                "accounting.chart.view", "accounting.chart.create", "accounting.chart.edit", "accounting.chart.deactivate",
                "approvals.view", "approvals.decide", "mobile.broadcast", "payroll.view"
            ]),

        new("HRSpecialist", "أخصائي الموارد البشرية والعلاقات الحكومية", "HR", 2,
            "إنشاء طلبات خدمات الموارد البشرية (رخص العمل، الإقامات، نقل الكفالة، التأشيرات، التأمين)، تحديد نسب التحمل، وتوثيق ملفات المناديب.",
            [
                "operations.view", "riders.view", "riders.view_360", "riders.edit",
                "riders.iban.view", "riders.saudi_salary.view", "riders.legal_cases.view", "riders.unified_number.view",
                "debts.view", "debts.create.hr", "debts.create.advance",
                "approvals.view", "approvals.decide", "mobile.broadcast", "forms.review"
            ]),

        new("OperationsManager", "مدير التشغيل والمنصات", "OPS", 4,
            "الإشراف التشغيلي على المناديب والمنصات، متابعة المديونيات وكشوف الحساب، ومراجعة الاعتمادات والتقارير.",
            [
                "operations.view", "riders.view", "riders.view_360", "riders.edit",
                "debts.view", "debts.create.platform", "debts.create.fleet", "debts.reschedule",
                "treasury.view", "treasury.jahez.manage", "treasury.jahez.handover",
                "approvals.view", "approvals.decide", "mobile.broadcast", "payroll.view", "forms.review"
            ]),

        new("PlatformsSupervisor", "مشرف منصات وعهدة صندوق جاهز", "OPS", 2,
            "إدارة معاملات تطبيق جاهز، تحصيل مديونيات كاش جاهز ورسوم فتح الحسابات (SUP.*)، وتسليم وتصفير العهدة للصندوق الداخلي.",
            [
                "operations.view", "riders.view", "riders.view_360",
                "debts.view", "debts.create.platform", "debts.collect",
                "treasury.view", "treasury.jahez.manage", "treasury.jahez.handover",
                "approvals.view", "mobile.broadcast"
            ]),

        new("FleetSupervisor", "مشرف الحركة والأسطول والورشة", "FLT", 2,
            "تسجيل المخالفات المرورية وتلفيات الحوادث وأقساط المركبات وفروقات الوقود ومتابعة مديونيات الأسطول.",
            [
                "operations.view", "riders.view", "riders.view_360", "riders.edit",
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

        new("Supervisor", "مشرف تشغيل", "OPS", 2,
            "متابعة مناديبه ورفع طلبات السلف لهم حسب نظام الرتب (BR §25)؛ لا يعتمد ولا يصرف.",
            [
                "operations.view", "riders.view", "riders.view_360",
                "debts.view", "advances.request", "approvals.view"
            ]),

        new("Reviewer", "مراجع البيانات والنماذج", "OPS", 1,
            "مراجعة وتدقيق ردود المناديب على النماذج والحملات والتحقق من الهوية وجودة بيانات الدليل.",
            [
                "riders.view", "forms.review"
            ])
    }.Select(r=>r with { DefaultPermissions=r.DefaultPermissions.Concat(AccessPolicy.CompatibilityDefaults(r.Code)).Concat(ManualJournals.Grants(r.Code)).Distinct(StringComparer.Ordinal).ToArray() }).ToArray();

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
    // OP-8 / U-W2-26: old draft codes → canonical section.resource.action codes.
    public static readonly Dictionary<string,string> PermissionAliases=new(StringComparer.OrdinalIgnoreCase)
    {
        ["finance.coa.view"]="accounting.chart.view",
        ["finance.coa.create"]="accounting.chart.create",
        ["finance.coa.edit"]="accounting.chart.edit",
        ["finance.coa.deactivate"]="accounting.chart.deactivate",
        ["mudad.view"]="people.mudad.manage",
        ["mudad.upload"]="people.mudad.manage",
        ["iban.view"]="people.iban.view",
        ["saudi.salary.view"]="people.saudi_salary.view",
        ["notify.settings.manage"]="admin.notify.manage"
    };
    public static string CanonicalPermission(string code)=>
        PermissionAliases.TryGetValue(code,out var mapped)?mapped:code;
    public static string[] EffectivePermissions(JsonObject u)
    {
        string role = Role(u);
        if (role is "Admin" or "SuperAdmin" or "SystemAdmin") return PermissionsCatalog.Select(p => p.Code).ToArray();
        var def = ResolveRoleDef(role);
        // An edited role template is attached by WithRole for the current read only; never stored on the user.
        var baseline = u[RoleBaseline] is JsonArray edited ? edited.Select(p => p?.ToString() ?? "") : def.DefaultPermissions;
        var set = new HashSet<string>(baseline.Select(CanonicalPermission), StringComparer.OrdinalIgnoreCase);
        if (u["customPermissions"] is JsonArray custom)
        {
            foreach (var item in custom)
            {
                string code = CanonicalPermission(item?.GetValue<string>() ?? "");
                if (PermissionsCatalog.FirstOrDefault(p => p.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) is {} permission)
                    set.Add(permission.Code);
            }
        }
        if(u["deniedPermissions"] is JsonArray denied)
            foreach(var permission in denied)set.Remove(CanonicalPermission(permission?.GetValue<string>()??""));
        return set.Where(code=>PermissionsCatalog.Any(p=>p.Code.Equals(code,StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    public static JsonObject EffectivePermissionsView(DbConnection c,DbTransaction? tx,JsonObject u)
    {
        var withRole=WithRole(c,tx,u);
        var rolePerms=withRole[RoleBaseline] is JsonArray edited
            ?edited.Select(p=>CanonicalPermission(p?.ToString()??"")).Where(p=>p!="").Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            :ResolveRoleDef(Role(u)).DefaultPermissions;
        var grants=(u["customPermissions"] as JsonArray??[]).Select(p=>CanonicalPermission(p?.ToString()??"")).Where(p=>p!="").ToArray();
        var denies=(u["deniedPermissions"] as JsonArray??[]).Select(p=>CanonicalPermission(p?.ToString()??"")).Where(p=>p!="").ToArray();
        var effective=EffectivePermissions(withRole);
        return new()
        {
            ["userId"]=u.S("id"),
            ["username"]=u.S("username"),
            ["role"]=Role(u),
            ["rolePermissions"]=new JsonArray(rolePerms.Select(p=>(JsonNode?)p).ToArray()),
            ["grants"]=new JsonArray(grants.Select(p=>(JsonNode?)p).ToArray()),
            ["denies"]=new JsonArray(denies.Select(p=>(JsonNode?)p).ToArray()),
            ["effective"]=new JsonArray(effective.Select(p=>(JsonNode?)p).ToArray()),
            ["aliases"]=new JsonObject(PermissionAliases.ToDictionary(kv=>kv.Key,kv=>(JsonNode?)kv.Value)),
            ["allowedActions"]=new JsonArray("copy_permissions")
        };
    }

    public static JsonObject CopyPermissions(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string targetId,JsonObject input)
    {
        AccessPolicy.Demand(actor,"users.manage");
        var target=Store.Get(c,targetId,"user",tx)??throw new ApiError("المستخدم المستهدف غير موجود.",404);
        string sourceUserId=input.S("sourceUserId"),sourceRole=input.S("sourceRole");
        string[] source;
        string sourceLabel;
        if(!string.IsNullOrWhiteSpace(sourceUserId))
        {
            var src=Store.Get(c,sourceUserId,"user",tx)??throw new ApiError("المستخدم المصدر غير موجود.",404);
            source=EffectivePermissions(WithRole(c,tx,src));
            sourceLabel="user:"+src.S("username");
        }
        else if(!string.IsNullOrWhiteSpace(sourceRole))
        {
            var def=ResolveRoleDef(sourceRole);
            var edited=Store.Get(c,def.Code,RolePermissionsKind,tx);
            source=edited?["permissions"] is JsonArray list
                ?list.Select(p=>CanonicalPermission(p?.ToString()??"")).Where(p=>p!="").ToArray()
                :def.DefaultPermissions;
            sourceLabel="role:"+def.Code;
        }
        else throw new ApiError("حدد مستخدماً أو دوراً مصدراً.",400);
        var before=EffectivePermissions(WithRole(c,tx,target));
        // Match source exactly: grants/denies vs the target role baseline (no leftover baseline privileges).
        var baseline=WithRole(c,tx,target)[RoleBaseline] is JsonArray editedBaseline
            ?editedBaseline.Select(p=>CanonicalPermission(p?.ToString()??"")).Where(p=>p!="").ToHashSet(StringComparer.OrdinalIgnoreCase)
            :ResolveRoleDef(Role(target)).DefaultPermissions.Select(CanonicalPermission).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wanted=source.Select(CanonicalPermission).Where(p=>p!="").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var grants=wanted.Where(p=>!baseline.Contains(p)).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        var denies=baseline.Where(p=>!wanted.Contains(p)).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        var afterDoc=target.Clone();
        afterDoc["customPermissions"]=new JsonArray(grants.Select(p=>(JsonNode?)p).ToArray());
        afterDoc["deniedPermissions"]=new JsonArray(denies.Select(p=>(JsonNode?)p).ToArray());
        GuardManager(c,tx,actor,target,afterDoc);
        target["customPermissions"]=afterDoc["customPermissions"]!.DeepClone();
        target["deniedPermissions"]=afterDoc["deniedPermissions"]!.DeepClone();
        target["securityVersion"]=Guid.NewGuid().ToString("N");
        Store.Save(c,"user",target,tx);
        var after=EffectivePermissions(WithRole(c,tx,target));
        AuditAccessChange(c,tx,actor,"admin.copy_permissions",targetId,before,after,sourceLabel);
        return new()
        {
            ["targetUserId"]=targetId,
            ["source"]=sourceLabel,
            ["before"]=new JsonArray(before.Select(p=>(JsonNode?)p).ToArray()),
            ["after"]=new JsonArray(after.Select(p=>(JsonNode?)p).ToArray()),
            ["allowedActions"]=new JsonArray()
        };
    }

    public const string RolePermissionsKind = "role_permissions", RoleBaseline = "__roleBaseline";
    public const string SystemAdminMigrationMarker = "migration:system-admin-permissions:v1";
    // M-0667: existing SystemAdmin accounts and role templates get the full permission catalog once.
    public static void SeedSystemAdmin(Store store)=>store.Write((c,tx)=>
    {
        if(Store.Get(c,SystemAdminMigrationMarker,"setting",tx)!=null)return false;
        var full=new JsonArray(PermissionsCatalog.Select(p=>(JsonNode?)p.Code).ToArray());
        var template=Store.Get(c,"SystemAdmin",RolePermissionsKind,tx);
        if(template==null||template["permissions"] is not JsonArray existing||existing.Count!=PermissionsCatalog.Length)
        {
            Store.Save(c,RolePermissionsKind,new(){["id"]="SystemAdmin",["permissions"]=full.DeepClone(),["updatedBy"]="system-migration",["updatedAt"]=DateTimeOffset.UtcNow.ToString("O")},tx);
            Store.Audit(c,tx,"admin.role_permissions","SystemAdmin","system-migration");
        }
        foreach(var user in Store.List(c,"user",tx).Where(u=>Role(u)=="SystemAdmin"))
        {
            Store.Audit(c,tx,"admin.system_admin_permissions_seeded",user.S("id"),"system-migration");
        }
        Store.Save(c,"setting",new(){["id"]=SystemAdminMigrationMarker,["completed"]=true},tx);return true;
    });
    public static JsonObject WithRole(DbConnection c, DbTransaction? tx, JsonObject u)
    {
        var edited = Store.Get(c, Role(u), RolePermissionsKind, tx);
        if (edited?["permissions"] is not JsonArray list) return u;
        var copy = u.Clone(); copy[RoleBaseline] = list.DeepClone(); return copy;
    }
    public static bool MustChangePassword(JsonObject u) => u.B("mustChangePassword");
    public static void SetPassword(JsonObject u, string password, bool mustChange)
    {
        J.Need(password.Length is >= 12 and <= 200, "كلمة المرور من12إلى200حرف.");
        var salt = RandomNumberGenerator.GetBytes(16);
        u["salt"] = Convert.ToBase64String(salt);
        u["hash"] = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32));
        u["mustChangePassword"] = mustChange; u["securityVersion"] = Guid.NewGuid().ToString("N"); u["failedLogins"] = 0; u["lockedUntil"] = "";
    }
    static bool PrivilegedRole(string role) => role is "Admin" or "SuperAdmin" or "SystemAdmin";
    // A delegated manager never edits administrators or themselves, and never grants what they lack.
    static void GuardManager(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, JsonObject? before, JsonObject after)
    {
        if (AccessPolicy.Administrator(actor)) return;
        AccessPolicy.Demand(actor, "users.manage");
        J.Need(before == null || !string.Equals(before.S("username"), actor.Identity?.Name, StringComparison.OrdinalIgnoreCase), "لا تعدّل صلاحيات حسابك.", 403);
        J.Need(before == null || !PrivilegedRole(Role(before)), "تعديل مسؤول النظام لمسؤول النظام فقط.", 403);
        J.Need(!PrivilegedRole(Role(after)), "منح دور مسؤول النظام لمسؤول النظام فقط.", 403);
        foreach (string permission in EffectivePermissions(WithRole(c, tx, after)))
            if (before == null || !EffectivePermissions(WithRole(c, tx, before)).Contains(permission))
                J.Need(AccessPolicy.Has(actor, permission), "لا تمنح صلاحية لا تملكها: " + permission, 403);
    }
    static void AuditAccessChange(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string action, string entity, IEnumerable<string> before, IEnumerable<string> after, string note = "")
    {
        var old = before.ToHashSet(StringComparer.Ordinal); var fresh = after.ToHashSet(StringComparer.Ordinal);
        Store.Save(c, "audit", new() { ["id"] = Guid.NewGuid().ToString("N"), ["action"] = action, ["entity"] = entity, ["actor"] = actor.Identity!.Name, ["at"] = DateTimeOffset.UtcNow.ToString("O"), ["note"] = note,
            ["added"] = new JsonArray(fresh.Except(old).Order(StringComparer.Ordinal).Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
            ["removed"] = new JsonArray(old.Except(fresh).Order(StringComparer.Ordinal).Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()) }, tx);
    }

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
    static IEnumerable<string> KnownCodes(JsonNode? list)=>(list as JsonArray??[]).Select(x=>x?.GetValue<string>()??"").Where(code=>PermissionsCatalog.Any(p=>p.Code==code));
    public const string FormerUsernameClaim="formerUsername";
    public static IEnumerable<string> FormerUsernames(JsonObject u)=>(u["formerUsernames"] as JsonArray??[]).Select(n=>n?.GetValue<string>()??"").Where(n=>n!="");
    /// <summary>A name is taken if any user has it now or had it before (old names stay reserved so history keeps one owner).</summary>
    public static bool NameTaken(DbConnection c,string username,DbTransaction? tx=null,string? exceptUserId=null)
        =>Store.List(c,"user",tx).Any(u=>u.S("id")!=exceptUserId&&(Normalize(u.S("username"))==Normalize(username)||FormerUsernames(u).Any(f=>Normalize(f)==Normalize(username))));
    static void ValidateUsername(string username)=>J.Need(username.Length is >= 3 and <= 60 && Regex.IsMatch(username, @"^[\p{L}\p{N}_.@-]+$"), "اسم المستخدم من3إلى60حرفًا دون مسافات أو محارف خاصة.");
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
        // Only codes still in the catalog: a stored code removed in an older release grants nothing and would make every save fail validation.
        var customPerms = new JsonArray(KnownCodes(u["customPermissions"]).Select(x => (JsonNode?)x).ToArray());

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
            ["mustChangePassword"] = u.B("mustChangePassword"),
            ["customPermissions"] = customPerms,
            ["deniedPermissions"] = new JsonArray(KnownCodes(u["deniedPermissions"]).Select(x => (JsonNode?)x).ToArray()),
            ["formerUsernames"] = new JsonArray(FormerUsernames(u).Select(n=>(JsonNode?)n).ToArray()),
            ["financialScope"] = FinancialScopes.Effective(u),
            ["createdAt"] = u.S("createdAt"),
            ["lastLoginAt"] = u.S("lastLoginAt"),
            ["approvalExceptionUntil"]=u.S("approvalExceptionUntil"),["approvalExceptionReason"]=u.S("approvalExceptionReason"),
            ["allowedActions"]=AccessPolicy.ApprovalException(Principal(u))?new JsonArray("self_approve_exception"):new JsonArray(),
            ["approvalBypassBadge"]=false
        };
    }
    public static JsonObject PublicWithBypass(DbConnection c,DbTransaction? tx,JsonObject u)
    {
        var view=Public(u);var grant=AccessPolicy.ActiveBypassGrant(c,tx,u.S("username"));
        if(grant!=null)
        {
            view["approvalBypassUntil"]=grant.S("to");view["approvalBypassReason"]=grant.S("reason");view["approvalBypassBadge"]=true;
            var actions=view["allowedActions"] as JsonArray??[];if(!actions.Any(a=>a?.ToString()=="approval_bypass"))actions.Add("approval_bypass");view["allowedActions"]=actions;
        }
        return view;
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
    /// <summary>Admin-only login rename. Keeps the user id, reserves the old name, moves dated bypass grants, ends sessions, audits from→to.</summary>
    public static JsonObject Rename(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string id, JsonObject input)
    {
        J.Need(AccessPolicy.Administrator(actor), "تغيير اسم المستخدم لمسؤول النظام فقط.", 403);
        var u = Store.Get(c, id, "user", tx) ?? throw new ApiError("الحساب غير موجود.", 404);
        string next = input.Required("username").Trim(), reason = input.Required("reason").Trim(), old = u.S("username");
        ValidateUsername(next);
        J.Need(reason.Length is >= 3 and <= 500 && !reason.Any(char.IsControl), "اكتب سبب تغيير اسم المستخدم (3 إلى 500 حرف).");
        J.Need(Normalize(next) != Normalize(old), "الاسم الجديد مطابق للحالي.");
        J.Need(!NameTaken(c, next, tx, id), "اسم المستخدم مسجل بالفعل أو كان مستخدمًا قبل كده.");
        var former = new JsonArray(FormerUsernames(u).Append(old).Distinct(StringComparer.OrdinalIgnoreCase).Select(n => (JsonNode?)n).ToArray());
        u["username"] = next; u["formerUsernames"] = former; u["securityVersion"] = Guid.NewGuid().ToString("N");
        Store.Save(c, "user", u, tx);
        // Dated bypass grants follow the person; auth.user.username drives SQL bypass list joins.
        if(SchemaSql.TableExists(c,tx,"auth","user"))
            SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"auth","user")} SET username=@n WHERE username=@old OR username=@oldNorm",
                ("@n",next),("@old",old),("@oldNorm",Normalize(old)));
        foreach (var grant in Store.List(c, AccessPolicy.BypassGrantKind, tx).Where(g => g.S("userId") == id || Normalize(g.S("username")) == Normalize(old)))
        { grant["username"] = next; Store.Save(c, AccessPolicy.BypassGrantKind, grant, tx); }
        Store.Save(c, "audit", new() { ["id"] = Guid.NewGuid().ToString("N"), ["action"] = "admin.username_changed", ["entityId"] = id, ["actor"] = actor.Identity!.Name, ["at"] = DateTimeOffset.UtcNow.ToString("O"), ["from"] = old, ["to"] = next, ["reason"] = reason }, tx);
        return Public(WithRole(c, tx, u));
    }
    public static JsonObject Create(string username, string password, string role, string? id = null, string? department = null, int? rankLevel = null, JsonArray? customPermissions = null)
    {
        ValidateUsername(username);
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
        if(MustChangePassword(u))claims.Add(new("mustChangePassword","true"));
        foreach(var former in FormerUsernames(u))claims.Add(new(FormerUsernameClaim,former));
        claims.AddRange(FinancialScopes.Claims(u));
        foreach(string field in new[]{"approvalExceptionUntil","approvalExceptionReason"})if(u.S(field)!="")claims.Add(new(field,u.S(field)));
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
        context.ReplacePrincipal(store.Read(c=>Principal(WithRole(c,null,u))));
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
            permissions = EditorPermissionsCatalog,
            permissionAliases = PermissionAliases,
            departments = FinancialCore.DepartmentsCatalog,
            rolePermissions=store.Read(c=>Store.List(c,RolePermissionsKind).ToDictionary(r=>r.S("id"),r=>r["permissions"]?.DeepClone())),
            financialScopeCatalog=new{sponsors=FinancialCore.Sponsors,accounts=FinancialCore.ChartOfAccounts.Where(a=>a.IsVault),categories=FinancialCore.CategoriesCatalog,platforms=new[]{"Jahez","Keeta","HungerStation","Ninja","Chefz","Amazon","Other"}}
        }).WithTags("Administration").WithSummary("Read roles catalog");
        api.MapGet("/users/{id}/effective-permissions",(string id,HttpContext ctx)=>
        {
            AccessPolicy.Demand(ctx.User,"users.manage");
            return store.Read(c=>EffectivePermissionsView(c,null,Store.Get(c,id,"user")??throw new ApiError("الحساب غير موجود.",404)));
        }).WithTags("Administration").WithSummary("Read effective permissions breakdown for a user");
        api.MapPost("/users/{id}/copy-permissions",(string id,JsonObject input,HttpContext ctx)=>
            store.Write((c,tx)=>CopyPermissions(c,tx,ctx.User,id,input)))
            .WithTags("Administration").WithSummary("Copy permissions from a user or role onto a target user");

        api.MapGet("/admin/approval-exceptions",(HttpContext ctx)=>{J.Need(AccessPolicy.Administrator(ctx.User)||ctx.User.IsInRole("SystemAdmin"),"للمسؤول فقط.",403);return store.Read(c=>Store.List(c,"audit").Where(a=>a.B("selfApprovedException")||a.S("action") is "approval.self_exception" or "approval.bypass_maker_checker").OrderByDescending(a=>a.S("at")));}).WithTags("Administration").WithSummary("Self-approval audit report (dated exceptions and bypass uses)");
        api.MapGet("/users", () => store.Read(c => Store.List(c, "user").Select(u => Public(WithRole(c, null, u))).ToList())).WithTags("Administration").WithSummary("Read users");

        api.MapPost("/users", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            string name = input.Required("username");
            J.Need(!NameTaken(c, name, tx), "اسم المستخدم مسجل بالفعل أو كان مستخدمًا قبل كده.");
            string role = input.Required("role");
            string? dept = input.ContainsKey("department") ? input.S("department") : null;
            int? rank = input.ContainsKey("rankLevel") ? (int)input.N("rankLevel") : null;
            var customPerms = input["customPermissions"] as JsonArray;
            var u = Create(name, input.Required("password"), role, null, dept, rank, customPerms);
            if(input["deniedPermissions"] is JsonArray denied){ValidatePermissionList(denied);u["deniedPermissions"]=denied.DeepClone();}
            if(input["financialScope"] is JsonObject scope)u["financialScope"]=FinancialScopes.Normalize(scope);
            AccessPolicy.ConfigureApprovalException(c,tx,ctx.User,u,input);
            GuardManager(c, tx, ctx.User, null, u);
            Store.Save(c, "user", u, tx);
            Store.Audit(c, tx, "admin.user_create", u.S("id"), ctx.User.Identity!.Name!);
            return Public(u);
        })).WithTags("Administration").WithSummary("Submit users");

        JsonObject UpdateUser(string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            var u = Store.Get(c, id, "user", tx) ?? throw new ApiError("الحساب غير موجود.", 404);
            var before = u.Clone();
            string role = input.S("role", Role(u)), status = input.S("status", Status(u));
            J.Need(Roles.Contains(role) && status is "active" or "suspended", "دور أو حالة غير صالحة.");
            J.Need(!(PrivilegedRole(Role(u)) && Status(u) == "active" && (!PrivilegedRole(role) || status != "active") && Store.List(c, "user", tx).Count(x => PrivilegedRole(Role(x)) && Status(x) == "active") == 1), "لا يمكن تعطيل آخر مسؤول نشط.");
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
            AccessPolicy.ConfigureApprovalException(c,tx,ctx.User,u,input);
            GuardManager(c, tx, ctx.User, before, u);
            Store.Save(c, "user", u, tx);
            Store.Audit(c, tx, "admin.user_update", id, ctx.User.Identity!.Name!);
            return Public(u);
        });
        api.MapPost("/users/{id}/reset-password", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            var u = Store.Get(c, id, "user", tx) ?? throw new ApiError("الحساب غير موجود.", 404);
            GuardManager(c, tx, ctx.User, u, u);
            SetPassword(u, input.Required("password"), true);
            Store.Save(c, "user", u, tx); Store.Audit(c, tx, "admin.password_reset", id, ctx.User.Identity!.Name!);
            return Public(WithRole(c, tx, u));
        })).WithTags("Administration").WithSummary("Set a temporary password the user must change at next login");
        api.MapPost("/me/password", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            var found = Find(c, ctx.User.Identity!.Name!, tx) ?? throw new ApiError("الحساب غير موجود.", 404);
            J.Need(Password(found, input.S("currentPassword")), "كلمة المرور الحالية غير صحيحة.", 403);
            string next = input.Required("newPassword"); J.Need(next != input.S("currentPassword"), "اختر كلمة مرور مختلفة.");
            SetPassword(found, next, false);
            Store.Save(c, "user", found, tx); Store.Audit(c, tx, "admin.password_changed", found.S("id"), found.S("username"));
            return Public(WithRole(c, tx, found));
        })).WithTags("Administration").WithSummary("Change my own password").RequireRateLimiting("sensitive");
        api.MapPut("/roles/{code}/permissions", (string code, JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            J.Need(AccessPolicy.Administrator(ctx.User), "تعديل الأدوار لمسؤول النظام فقط.", 403);
            var def = ResolveRoleDef(code); J.Need(!PrivilegedRole(def.Code), "أدوار مسؤول النظام ثابتة.");
            var list = input["permissions"] as JsonArray ?? throw new ApiError("حدد صلاحيات الدور."); ValidatePermissionList(list);
            string reason = input.Required("reason").Trim(); J.Need(reason.Length is >= 3 and <= 500, "اكتب سبب تعديل الدور.");
            var before = Store.Get(c, def.Code, RolePermissionsKind, tx)?["permissions"] as JsonArray;
            AuditAccessChange(c, tx, ctx.User, "admin.role_permissions", def.Code, before?.Select(p => p?.ToString() ?? "") ?? def.DefaultPermissions, list.Select(p => p?.ToString() ?? ""), reason);
            Store.Save(c, RolePermissionsKind, new() { ["id"] = def.Code, ["permissions"] = list.DeepClone(), ["updatedBy"] = ctx.User.Identity!.Name, ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O") }, tx);
            foreach (var user in Store.List(c, "user", tx).Where(u => Role(u) == def.Code)) { user["securityVersion"] = Guid.NewGuid().ToString("N"); Store.Save(c, "user", user, tx); }
            return new JsonObject { ["code"] = def.Code, ["permissions"] = list.DeepClone() };
        })).WithTags("Administration").WithSummary("Replace a role template and end its users' sessions");
        api.MapPut("/users/{id}/username", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) => Rename(c, tx, ctx.User, id, input)))
            .WithTags("Administration").WithSummary("Rename a login (admin only); old names stay reserved and keep maker-checker history; ends the user's sessions");
        api.MapPut("/users/{id}", UpdateUser).WithTags("Administration").WithSummary("Update a user and invalidate existing sessions");
        api.MapPut("/users/{id}/status", UpdateUser).WithTags("Administration").WithSummary("Legacy alias for updating a user");
    }
}
