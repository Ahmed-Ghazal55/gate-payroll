using System.Security.Cryptography;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Payroll;

public static class FinancialCore
{
    public sealed record SponsorDef(string Id, string Code, string UnifiedNumber, string NameAr, string ShortNameAr, string CommercialReg);

    public sealed record AccountDef(
        string Code,
        string NameAr,
        string AccountType,
        string NaturalSide,
        string? ParentCode,
        string? DefaultSponsorId = null,
        bool IsVault = false,
        string VaultKind = "None",          // None | MainInternal | JahezCashbox | Bank | PetroApp
        string VaultPurpose = "None",       // None | DebtAndHr | GeneralAndSettlement | JahezSettlement | JahezFee | Combined | Bank | FuelClearing
        string AllowedDirections = "Dual",  // Dual | InOnly | OutOnly
        string[]? AllowedSectors = null,
        string OperationalScopeAr = ""
    );

    public sealed record CategoryMeta(
        string Code,
        string NameAr,
        string Sector,
        string ExpenseAccountCode,
        string ReceivableAccountCode,
        decimal DefaultReferenceCost,
        string[]? AllowedVaultKinds = null
    );

    // 1. السجلات التجارية الرسمية المعتمدة في نظام البوابة (Logistic-ERP Sponsor.cs)
    public static readonly SponsorDef[] Sponsors =
    [
        new("019c18d5-62e1-7000-8000-000000000041", "BW", "7015658094", "شركة البوابة المقبلة للتجارة", "سجل البوابا", "4030362130"),
        new("019c18d5-62e1-7000-8000-000000000042", "EX", "7034861059", "شركة إكسبرس جايت", "سجل إكسبرس", "4030491120"),
        new("019c18d5-62e1-7000-8000-000000000040", "TR", "7038745530", "مؤسسة البوابة المقبلة التجارية", "سجل التجارية", "4030512290")
    ];

    // 2. شجرة الحسابات الهرمية القياسية والصناديق والبنوك (متوافقة مع jahez_v3 والهيكل المحاسبي المعتمد)
    public static readonly AccountDef[] ChartOfAccounts =
    [
        // الأصول المتداولة - الخزائن والصندوق الداخلي الرئيسي والبنوك (طبيعتها: مدين Debit - تزيد بالقبض وتنقص بالصرف)
        new("1100", "الأصول المتداولة — الخزائن والبنوك", "Asset", "Debit", null, null, false, "None", "None", "Dual", null,
            "المظلة الرئيسية لكافة الصناديق النقدية وعهد جاهز وحسابات البنوك ومقاصة الوقود."),

        // أ) الصندوق الداخلي الرئيسي العام (الخزينة المركزية للشركة) وتتفرع تحته الحسابات الفرعية التخصصية
        new("1110", "الصندوق الداخلي الرئيسي العام — الخزينة المركزية (MAIN)", "Asset", "Debit", "1100", null, false, "None", "None", "Dual", null,
            "حساب الخزنة الرئيسية الكبرى للشركة، وتتفرع تحته حسابات المديونيات والسلف، وتصفيات جاهز المرحلة، ورسوم فتح الحسابات، وصندوق مؤسسة التجارية الموحد."),

        new("1111", "حساب المديونيات والسلف وطلبات الموارد البشرية بالصندوق الداخلي", "Asset", "Debit", "1110", null, false, "None", "None", "Dual", null,
            "حساب تجميعي تحت الصندوق الداخلي الرئيسي مخصص لحركة السلف النقدية وطلبات HR وتحصيل المديونيات."),
        new("MAIN.BW.DEBT", "حساب المديونيات والسلف وطلبات HR — سجل البوابا", "Asset", "Debit", "1111", Sponsors[0].Id, true,
            "MainInternal", "DebtAndHr", "Dual", ["HR", "Advances", "Fleet", "Housing", "Admin", "Platforms"],
            "حساب فرعي بالصندوق الداخلي الرئيسي لصرف وتحصيل المديونيات والسلف وطلبات HR لسجل البوابا."),
        new("MAIN.EX.DEBT", "حساب المديونيات والسلف وطلبات HR — سجل إكسبرس", "Asset", "Debit", "1111", Sponsors[1].Id, true,
            "MainInternal", "DebtAndHr", "Dual", ["HR", "Advances", "Fleet", "Housing", "Admin", "Platforms"],
            "حساب فرعي بالصندوق الداخلي الرئيسي لصرف وتحصيل المديونيات والسلف وطلبات HR لسجل إكسبرس."),

        new("1112", "حساب التحصيلات والتصفيات الداخلي بالصندوق الرئيسي", "Asset", "Debit", "1110", null, false, "None", "None", "Dual", null,
            "حساب تجميعي تحت الصندوق الداخلي الرئيسي لاستلام تصفيات صناديق جاهز والتحصيلات النقدية والعمليات الداخلية."),
        new("MAIN.BW.SET", "صندوق التحصيلات الداخلي — سجل البوابا", "Asset", "Debit", "1112", Sponsors[0].Id, true,
            "MainInternal", "GeneralAndSettlement", "Dual", ["HR", "Advances", "Fleet", "Housing", "Admin", "Platforms"],
            "صندوق التحصيلات الداخلي وتصفيات الكاش المرحلة بالصندوق الرئيسي لسجل البوابا."),
        new("MAIN.EX.SET", "صندوق التحصيلات الداخلي — سجل إكسبرس", "Asset", "Debit", "1112", Sponsors[1].Id, true,
            "MainInternal", "GeneralAndSettlement", "Dual", ["HR", "Advances", "Fleet", "Housing", "Admin", "Platforms"],
            "صندوق التحصيلات الداخلي وتصفيات الكاش المرحلة بالصندوق الرئيسي لسجل إكسبرس."),

        new("1113", "حساب رسوم فتح حسابات جاهز بالصندوق الداخلي (قبض فقط)", "Asset", "Debit", "1110", null, false, "None", "None", "InOnly", null,
            "حساب تجميعي تحت الصندوق الداخلي لتجميع رسوم فتح وتفعيل حسابات جاهز المحولة من المشرفين."),
        new("MAIN.BW.FEE", "حساب رسوم فتح حسابات جاهز — سجل البوابا", "Asset", "Debit", "1113", Sponsors[0].Id, true,
            "MainInternal", "JahezFee", "InOnly", ["JahezFee"],
            "مخصص حصرياً لقبض وتجميع رسوم فتح حسابات جاهز (200 ر.س) لسجل البوابا — لا يقبل الصرف أو السلف أو طلبات HR."),
        new("MAIN.EX.FEE", "حساب رسوم فتح حسابات جاهز — سجل إكسبرس", "Asset", "Debit", "1113", Sponsors[1].Id, true,
            "MainInternal", "JahezFee", "InOnly", ["JahezFee"],
            "مخصص حصرياً لقبض وتجميع رسوم فتح حسابات جاهز (200 ر.س) لسجل إكسبرس — لا يقبل الصرف أو السلف أو طلبات HR."),

        new("MAIN.TR", "حساب وصندوق مؤسسة التجارية الموحد — سجل التجارية", "Asset", "Debit", "1110", Sponsors[2].Id, false,
            "MainInternal", "Combined", "Dual", ["HR", "Advances", "Fleet", "Housing", "Admin", "Platforms", "JahezFee"],
            "حساب فرعي موحد تحت الصندوق الداخلي الرئيسي يدمج تصفيات جاهز ورسوم الحسابات والمديونيات والعمليات الداخلية لمؤسسة التجارية."),

        new("MAIN.TR.DEBT","صندوق السلف وHR — التجارية","Asset","Debit","MAIN.TR",Sponsors[2].Id,true,"MainInternal","DebtAndHr","Dual",["HR","Advances","Fleet","Housing","Admin","Platforms"]),
        new("MAIN.TR.SET","تصفيات جاهز الداخلية — التجارية","Asset","Debit","MAIN.TR",Sponsors[2].Id,true,"MainInternal","GeneralAndSettlement","Dual",["Platforms"]),
        new("MAIN.TR.FEE","رسوم جاهز الداخلية — التجارية","Asset","Debit","MAIN.TR",Sponsors[2].Id,true,"MainInternal","JahezFee","InOnly",["JahezFee"]),
        // ب) صناديق وخزائن جاهز الميدانية (SUP) — تمثل أرصدة ومعاملات تطبيق وشركة جاهز حصراً
        new("1120", "صندوق / خزنة تحصيلات جاهز للكاش (SUP — خاص بتطبيق جاهز فقط)", "Asset", "Debit", "1100", null, false, "None", "None", "Dual", null,
            "يمثل أرصدة ومعاملات تطبيق وشركة جاهز فقط؛ يتم التعامل فيه بين المناديب وشركة جاهز والصندوق الداخلي الرئيسي والبنوك المخصصة فقط، ولا علاقة له بالسلف أو طلبات HR."),
        new("SUP.BW.SET", "خزنة كاش جاهز للتصفيات — سجل البوابا", "Asset", "Debit", "1120", Sponsors[0].Id, true,
            "JahezCashbox", "JahezSettlement", "Dual", ["Platforms"],
            "خاص بمعاملات وأرصدة تطبيق جاهز فقط (بين المناديب وشركة جاهز والصندوق الداخلي والبنوك) — يمنع استخدامه في السلف أو طلبات HR."),
        new("SUP.EX.SET", "خزنة كاش جاهز للتصفيات — سجل إكسبرس", "Asset", "Debit", "1120", Sponsors[1].Id, true,
            "JahezCashbox", "JahezSettlement", "Dual", ["Platforms"],
            "خاص بمعاملات وأرصدة تطبيق جاهز فقط (بين المناديب وشركة جاهز والصندوق الداخلي والبنوك) — يمنع استخدامه في السلف أو طلبات HR."),
        new("SUP.TR.SET", "خزنة كاش جاهز للتصفيات — سجل التجارية", "Asset", "Debit", "1120", Sponsors[2].Id, true,
            "JahezCashbox", "JahezSettlement", "Dual", ["Platforms"],
            "خاص بمعاملات وأرصدة تطبيق جاهز فقط (بين المناديب وشركة جاهز والصندوق الداخلي والبنوك) — يمنع استخدامه في السلف أو طلبات HR."),
        new("SUP.BW.FEE", "عهدة رسوم فتح حسابات جاهز — سجل البوابا", "Asset", "Debit", "1120", Sponsors[0].Id, true,
            "JahezCashbox", "JahezFee", "InOnly", ["JahezFee"],
            "عهدة قبض رسوم فتح حساب جاهز جديد من المناديب فقط وتُصفّر بنهاية اليوم إلى حساب (MAIN.BW.FEE)."),
        new("SUP.EX.FEE", "عهدة رسوم فتح حسابات جاهز — سجل إكسبرس", "Asset", "Debit", "1120", Sponsors[1].Id, true,
            "JahezCashbox", "JahezFee", "InOnly", ["JahezFee"],
            "عهدة قبض رسوم فتح حساب جاهز جديد من المناديب فقط وتُصفّر بنهاية اليوم إلى حساب (MAIN.EX.FEE)."),
        new("SUP.TR.FEE", "عهدة رسوم فتح حسابات جاهز — سجل التجارية", "Asset", "Debit", "1120", Sponsors[2].Id, true,
            "JahezCashbox", "JahezFee", "InOnly", ["JahezFee"],
            "عهدة قبض رسوم فتح حساب جاهز جديد من المناديب فقط وتُصفّر بنهاية اليوم إلى حساب (MAIN.TR)."),

        // ج) حسابات البنوك الرسمية المخصصة (BANK)
        new("1130", "حسابات البنوك الرسمية (BANK)", "Asset", "Debit", "1100", null, false, "None", "None", "Dual", null,
            "الحسابات البنكية الرسمية للشركات والمؤسسة المعتمدة للتحويلات البنكية والسلف البنكية وتوريد الخزائن."),
        new("BANK.BW", "حساب البنك الرسمي — سجل البوابا", "Asset", "Debit", "1130", Sponsors[0].Id, true,
            "Bank", "Bank", "Dual", ["HR", "Advances", "Fleet", "Housing", "Admin", "Platforms", "JahezFee"],
            "الحساب البنكي الرسمي لسجل البوابا (مشترك البوابا/إكسبرس) للتحويلات البنكية والسلف البنكية وتوريد النقدية."),
        new("BANK.EX", "حساب البنك الرسمي — سجل إكسبرس", "Asset", "Debit", "1130", Sponsors[1].Id, true,
            "Bank", "Bank", "Dual", ["HR", "Advances", "Fleet", "Housing", "Admin", "Platforms", "JahezFee"],
            "الحساب البنكي الرسمي لسجل إكسبرس (مشترك البوابا/إكسبرس) للتحويلات البنكية والسلف البنكية وتوريد النقدية."),
        new("BANK.TR", "حساب البنك الرسمي المستقل — سجل التجارية", "Asset", "Debit", "1130", Sponsors[2].Id, true,
            "Bank", "Bank", "Dual", ["HR", "Advances", "Fleet", "Housing", "Admin", "Platforms", "JahezFee"],
            "الحساب البنكي الرسمي المستقل لمؤسسة البوابة المقبلة التجارية."),
        new("BANK.BW.JAHEZ","رصيد البنك المخصص لتسويات جاهز — البوابا","Asset","Debit","1130",Sponsors[0].Id,true,"Bank","JahezSettlement","Dual",["Platforms"]),
        new("BANK.EX.JAHEZ","رصيد البنك المخصص لتسويات جاهز — إكسبرس","Asset","Debit","1130",Sponsors[1].Id,true,"Bank","JahezSettlement","Dual",["Platforms"]),
        new("BANK.TR.JAHEZ","رصيد البنك المخصص لتسويات جاهز — التجارية","Asset","Debit","1130",Sponsors[2].Id,true,"Bank","JahezSettlement","Dual",["Platforms"]),

        // د) مقاصة تحصيل الوقود بترو آب (PETRO) — جهة قبض ومقاصة فقط
        new("1140", "مقاصة تحصيل الوقود بترو آب (PETRO — قبض ومقاصة فقط)", "Asset", "Debit", "1100", null, false, "None", "None", "InOnly", null,
            "حساب مقاصة استقطاعات وقود المناديب عبر بترو آب؛ يقبل التحصيل والمقاصة فقط ولا يقبل الصرف أو التحويل بين الصناديق."),
        new("PETRO.BW", "مقاصة بترو آب — سجل البوابا", "Asset", "Debit", "1140", Sponsors[0].Id, true,
            "PetroApp", "FuelClearing", "InOnly", ["Platforms", "FleetFuel"],
            "حساب مقاصة وقود بترو آب لسجل البوابا (قبض فقط من تسويات الوقود والمنصات)."),
        new("PETRO.EX", "مقاصة بترو آب — سجل إكسبرس", "Asset", "Debit", "1140", Sponsors[1].Id, true,
            "PetroApp", "FuelClearing", "InOnly", ["Platforms", "FleetFuel"],
            "حساب مقاصة وقود بترو آب لسجل إكسبرس (قبض فقط من تسويات الوقود والمنصات)."),
        new("PETRO.TR", "مقاصة بترو آب — سجل التجارية", "Asset", "Debit", "1140", Sponsors[2].Id, true,
            "PetroApp", "FuelClearing", "InOnly", ["Platforms", "FleetFuel"],
            "حساب مقاصة وقود بترو آب لسجل التجارية (قبض فقط من تسويات الوقود والمنصات)."),

        // الذمم المدينة (طبيعتها: مدين Debit - تزيد بنشوء الدين وتنقص بالسداد أو الإعفاء)
        new("1200", "الذمم المدينة (حسابات المناديب والموظفين والمنصات)", "Asset", "Debit", null),
        new("1210", "ذمم مدينة — مناديب (سلف وخدمات HR وحوادث)", "Asset", "Debit", "1200"),
        new("1211", "ذمم مدينة — عجز رواتب مثبت من المسيرات", "Asset", "Debit", "1200"),
        new("1220", "ذمم مدينة — موظفين إداريين (سلف وعهد)", "Asset", "Debit", "1200"),
        new("1230", "ذمم مدينة — مديونيات كاش المنصات المرحلة (تطبيق جاهز والمنصات)", "Asset", "Debit", "1200"),
        new("1240", "حساب شركة / منصة جاهز — تسويات ومستحقات المنصة", "Asset", "Debit", "1200"),
        new("1241", "تسويات ومستحقات المنصات الأخرى", "Asset", "Debit", "1200"),

        // حسابات المقاصة وجاري السجلات الشقيقة (عند شراء المديونية أو التحصيل نيابة عن سجل آخر)
        new("1300", "حسابات جاري السجلات الشقيقة والمقاصة", "InterCompany", "Debit", null),
        new("1310", "جاري السجلات الشقيقة (مقاصة بين البوابا وإكسبرس والتجارية)", "InterCompany", "Debit", "1300"),

        // الذمم الدائنة والمستحقات (طبيعتها: دائن Credit - تزيد باعتماد الرواتب وتنقص بالصرف أو استقطاع القسط)
        new("2100", "الذمم الدائنة ومستحقات المناديب والموظفين", "Liability", "Credit", null),
        new("2110", "ذمم دائنة — رواتب ومستحقات معتمدة", "Liability", "Credit", "2100"),
        new("2120", "ذمم دائنة — أمانات وتأمينات محتجزة", "Liability", "Credit", "2100"),
        new("2130", "التزامات خدمات وفواتير معتمدة غير مسددة", "Liability", "Credit", "2100"),
        new("2140", "التزامات تسويات ومديونيات محالة من المنصات", "Liability", "Credit", "2100"),
        new("2141", "التزامات تسويات المنصات الأخرى", "Liability", "Credit", "2100"),

        // حساب أرصدة أول المدة والمديونيات المرحلة
        new("3100", "حقوق الملكية وأرصدة أول المدة", "Equity", "Credit", null),
        new("3101", "حساب أرصدة أول المدة والمديونيات المرحلة", "Equity", "Credit", "3100"),

        // إيرادات رسوم فتح وتفعيل حسابات جاهز
        new("4100", "الإيرادات التشغيلية ورسوم التفعيل", "Revenue", "Credit", null),
        new("4101", "إيرادات رسوم فتح وتفعيل حسابات جاهز", "Revenue", "Credit", "4100"),
        new("4102", "إيراد عمولة الشركة المعتمدة من تسويات المنصات", "Revenue", "Credit", "4100"),
        new("4103", "إيراد خدمات وتحملات معتمدة للشركة", "Revenue", "Credit", "4100"),

        // حسابات مصروفات الموارد البشرية (تسمى بمسمياتها مسبوقة بكلمة "مصروف" - طبيعتها: مدين Debit)
        new("5100", "مصروفات الموارد البشرية والخدمات الحكومية (HR)", "Expense", "Debit", null),
        new("5101", "مصروف رخصة عمل", "Expense", "Debit", "5100"),
        new("5102", "مصروف إصدار وتجديد إقامة", "Expense", "Debit", "5100"),
        new("5103", "مصروف نقل كفالة", "Expense", "Debit", "5100"),
        new("5104", "مصروف تأشيرة خروج وعودة", "Expense", "Debit", "5100"),
        new("5105", "مصروف تأمين طبي وكرت صحي", "Expense", "Debit", "5100"),
        new("5106", "مصروف تغيير مهنة", "Expense", "Debit", "5100"),
        new("5107", "مصروف إصدار رخصة قيادة", "Expense", "Debit", "5100"),
        new("5108", "مصروف بطاقة سائق هيئة النقل", "Expense", "Debit", "5100"),
        new("5109", "مصروف طلبات ورسوم حكومية أخرى", "Expense", "Debit", "5100"),
        new("5110", "مصروف سكن وإيجارات مناديب", "Expense", "Debit", "5100"),

        // مصروفات الأسطول والتشغيل والإعفاءات الإدارية (طبيعتها: مدين Debit)
        new("5200", "مصروفات الأسطول والتشغيل والتسويات الإدارية", "Expense", "Debit", null),
        new("5201", "مصروف صيانة وحوادث مركبات وتجهيزات", "Expense", "Debit", "5200"),
        new("5202", "مصروف إعفاءات وتسويات مديونية إدارية", "Expense", "Debit", "5200"),
        new("5203", "مصروف مخالفات وتلفيات تشغيلية", "Expense", "Debit", "5200"),
        new("5301", "مصروف رواتب وعمولات التشغيل", "Expense", "Debit", "5200")
    ];

    // 3. كتالوج فئات المديونيات وربطها المباشر بحسابات المصروفات والذمم المدينة والصناديق المسموحة لكل نوع عملية
    public static readonly CategoryMeta[] CategoriesCatalog =
    [
        // البنود الثمانية + الإضافية لقسم الموارد البشرية (HR) — تُصرف وتُحصّل فقط عبر الصندوق الداخلي (MAIN) أو البنوك (BANK)
        new("WorkPermit", "رخصة عمل (مكتب العمل)", "HR", "5101", "1210", 9600m, ["MainInternal", "Bank"]),
        new("IqamaRenewal", "إصدار وتجديد الإقامة", "HR", "5102", "1210", 650m, ["MainInternal", "Bank"]),
        new("SponsorshipTransfer", "نقل كفالة", "HR", "5103", "1210", 2000m, ["MainInternal", "Bank"]),
        new("ExitReEntryVisa", "تأشيرة خروج وعودة (إصدار/تمديد)", "HR", "5104", "1210", 200m, ["MainInternal", "Bank"]),
        new("MedicalInsurance", "تأمين طبي وكرت صحي", "HR", "5105", "1210", 1000m, ["MainInternal", "Bank"]),
        new("ProfessionChange", "تغيير مهنة", "HR", "5106", "1210", 1000m, ["MainInternal", "Bank"]),
        new("DriverLicense", "إصدار رخصة قيادة", "HR", "5107", "1210", 400m, ["MainInternal", "Bank"]),
        new("DriverCard", "بطاقة سائق هيئة النقل", "HR", "5108", "1210", 250m, ["MainInternal", "Bank"]),
        new("OtherHrService", "طلبات ورسوم HR أخرى", "HR", "5109", "1210", 0m, ["MainInternal", "Bank"]),

        // السلف النقدية والبنكية والعهد — لا علاقة لصناديق جاهز (SUP.*) بها إطلاقاً
        new("Advance", "سلفة نقدية / بنكية", "Advances", "5109", "1210", 0m, ["MainInternal", "Bank"]),
        new("CashAdvance", "سلفة نقدية (من الصندوق الداخلي)", "Advances", "5109", "1210", 0m, ["MainInternal"]),
        new("BankAdvance", "سلفة بنكية (من حساب البنك)", "Advances", "5109", "1210", 0m, ["Bank"]),
        new("CashCustody", "عهدة نقدية مقابل سند لأمر", "Advances", "5109", "1220", 15000m, ["MainInternal", "Bank"]),

        // الحركة والأسطول ومخالفات تم
        new("TrafficFine", "مخالفة مرورية (تم / ساهر)", "Fleet", "5203", "1210", 0m, ["MainInternal", "Bank"]),
        new("AccidentDamage", "تحمل حادث / تلفيات ورشة", "Fleet", "5201", "1210", 0m, ["MainInternal", "Bank"]),
        new("VehicleInstallment", "قسط مركبة / دباب", "Fleet", "5201", "1210", 0m, ["MainInternal", "Bank"]),
        new("EquipmentLoan", "عهدة جوال / شنطة / تجهيزات", "Fleet", "5201", "1210", 0m, ["MainInternal", "Bank"]),
        new("FuelOverage", "فروقات استهلاك وقود وزيت", "Fleet", "5203", "1210", 0m, ["MainInternal", "Bank", "PetroApp"]),

        // المنصات والسكن والخصومات الإدارية
        new("PlatformTransferredDebt", "مديونية كاش منصة مرحلة (جاهز/شيفز/نينجا)", "Platforms", "5203", "1230", 0m, ["JahezCashbox", "MainInternal", "Bank", "PetroApp"]),
        new("NegativeSalaryCarryover", "ترحيل راتب بالسالب (عجز مسير)", "Platforms", "5301", "1210", 0m, ["MainInternal", "Bank"]),
        new("HousingDeduction", "استقطاع سكن وإيجارات", "Housing", "5110", "1210", 0m, ["MainInternal", "Bank"]),
        new("AdministrativeDeduction", "جزاء / خصم إداري", "Admin", "5203", "1210", 0m, ["MainInternal", "Bank"])
    ];

    // 4. كتالوج الأقسام والرتب لمنظومة الاعتمادات المتبادلة والإشعارات
    public static readonly JsonArray DepartmentsCatalog =
    [
        new JsonObject { ["code"] = "FIN", ["nameAr"] = "الشؤون المالية والخزينة", ["ranks"] = new JsonArray("محاسب صندوق (رتبة 1)", "محاسب عام (رتبة 2)", "رئيس حسابات (رتبة 3)", "مدير مالي (رتبة 4)") },
        new JsonObject { ["code"] = "HR", ["nameAr"] = "الموارد البشرية والعلاقات الحكومية", ["ranks"] = new JsonArray("أخصائي موارد بشرية (رتبة 1)", "مسؤول علاقات حكومية (رتبة 2)", "مدير موارد بشرية (رتبة 3)") },
        new JsonObject { ["code"] = "FLT", ["nameAr"] = "الحركة والأسطول والورشة", ["ranks"] = new JsonArray("مسؤول حركة (رتبة 1)", "مدير ورشة (رتبة 2)", "مدير أسطول (رتبة 3)") },
        new JsonObject { ["code"] = "OPS", ["nameAr"] = "تشغيل المنصات والتطبيقات", ["ranks"] = new JsonArray("مشرف ميداني (رتبة 1)", "مدير منصة (رتبة 2)", "مدير تشغيل (رتبة 3)") },
        new JsonObject { ["code"] = "MGT", ["nameAr"] = "الإدارة العليا والامتثال القانوني", ["ranks"] = new JsonArray("مراجع داخلي / قانوني (رتبة 4)", "مدير عام (رتبة 5)") }
    ];

    public static SponsorDef? FindSponsor(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        string value = key.Trim();
        return Sponsors.FirstOrDefault(sp => new[] { sp.Id, sp.Code, sp.UnifiedNumber, sp.CommercialReg, sp.NameAr, sp.ShortNameAr }.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
            ?? Sponsors.FirstOrDefault(sp => sp.Code == "EX" && (value.Contains("اكسبرس") || value.Contains("إكسبرس")))
            ?? Sponsors.FirstOrDefault(sp => sp.Code == "TR" && (value.Contains("مؤسسة") && value.Contains("تجاري")))
            ?? Sponsors.FirstOrDefault(sp => sp.Code == "BW" && value.Contains("شركة") && value.Contains("بواب"));
    }

    public static SponsorDef ResolveSponsor(string? key) => string.IsNullOrWhiteSpace(key) ? Sponsors[0] : FindSponsor(key) ?? throw new ApiError("السجل التجاري غير معروف؛ راجع الربط قبل الترحيل.");
    public static SponsorDef? AssignedDebtSponsor(JsonObject debt) => FindSponsor(debt.S("creditorSponsorId"));
    public static SponsorDef RequireDebtSponsor(JsonObject debt) => AssignedDebtSponsor(debt) ?? throw new ApiError("راجع ربط السجل التجاري للدين القديم أولًا من الجدولة.");
    public static AccountDef ResolveVault(string code) { var account = ResolveAccount(code); J.Need(account.IsVault, "اختر صندوقًا أو حساب بنك صالحًا."); return account; }

    private static readonly InquiryLimits MobileLimits = new();
    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static JsonObject MobileSession(DbConnection c, HttpContext ctx, string iqama, DbTransaction? tx = null)
    {
        string header = ctx.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer rmob_", StringComparison.Ordinal) || header.Length != 7 + 5 + 64) throw new ApiError("جلسة المندوب غير صالحة.", 401);
        var session = Store.Get(c, "mobile:" + TokenHash(header[7..]), "mobile_session", tx);
        var rider = Store.Get(c, iqama, "rider", tx);
        if (session == null || session.S("iqama") != iqama || !DateTimeOffset.TryParse(session.S("expiresAtUtc"), out var expiry) || expiry <= DateTimeOffset.UtcNow || rider == null || !rider.S("status").Equals("Active", StringComparison.OrdinalIgnoreCase)) throw new ApiError("جلسة المندوب منتهية أو الحساب موقوف.", 401);
        return session;
    }

    public static AccountDef ResolveAccount(string code) =>
        ChartOfAccounts.FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase))
        ?? throw new ApiError("رمز الحساب المالي أو الصندوق غير صالح: " + code);

    public static CategoryMeta ResolveCategory(string category) =>
        CategoriesCatalog.FirstOrDefault(c => string.Equals(c.Code, category, StringComparison.OrdinalIgnoreCase))
        ?? throw new ApiError("فئة المديونية أو الخدمة غير صالحة: " + category);

    public static string DefaultVaultForSponsor(string sponsorId, string kind = "MAIN", string? categoryCode = null)
    {
        var sp = ResolveSponsor(sponsorId);
        if (kind.Equals("BANK", StringComparison.OrdinalIgnoreCase) || string.Equals(categoryCode, "BankAdvance", StringComparison.OrdinalIgnoreCase))
            return "BANK." + sp.Code;
        if (kind.Equals("SUP", StringComparison.OrdinalIgnoreCase))
            return $"SUP.{sp.Code}.SET";
        if (kind.Equals("PETRO", StringComparison.OrdinalIgnoreCase))
            return "PETRO." + sp.Code;
        if(categoryCode=="PlatformTransferredDebt")return $"SUP.{sp.Code}.SET";
        if (kind.Equals("DEBT", StringComparison.OrdinalIgnoreCase))
            return $"MAIN.{sp.Code}.DEBT";
        if (!string.IsNullOrWhiteSpace(categoryCode))
        {
            var cat = CategoriesCatalog.FirstOrDefault(c => string.Equals(c.Code, categoryCode, StringComparison.OrdinalIgnoreCase));
            if (cat != null && cat.Sector is "HR" or "Advances" or "Fleet" or "Housing" or "Admin")
                return $"MAIN.{sp.Code}.DEBT";
        }
        return $"MAIN.{sp.Code}.DEBT";
    }

    // التحقق الصارم من صلاحية الحساب/الصندوق لنوع المعاملة (منع خلط صناديق جاهز ورسوم جاهز وبترو آب مع طلبات HR والسلف)
    public static AccountDef ValidateVaultForOperation(string vaultCode,string categoryCode,string operationKind="disbursement",string? platform=null)=>AccountingPolicy.ValidateOperation(vaultCode,categoryCode,operationKind,platform);
    public static (AccountDef From,AccountDef To) ValidateVaultTransfer(string fromVaultCode,string toVaultCode)
    {
        var from=ResolveVault(fromVaultCode);var to=ResolveVault(toVaultCode);AccountingPolicy.ValidateCompanyTransfer(from,to);return(from,to);
    }

    public static bool IsDescendantOrSelf(string candidateCode, string targetParentCode)
    {
        if (string.Equals(candidateCode, targetParentCode, StringComparison.OrdinalIgnoreCase)) return true;
        var current = ChartOfAccounts.FirstOrDefault(a => string.Equals(a.Code, candidateCode, StringComparison.OrdinalIgnoreCase));
        int guard = 0;
        while (current?.ParentCode != null && guard++ < 10)
        {
            if (string.Equals(current.ParentCode, targetParentCode, StringComparison.OrdinalIgnoreCase)) return true;
            current = ChartOfAccounts.FirstOrDefault(a => string.Equals(a.Code, current.ParentCode, StringComparison.OrdinalIgnoreCase));
        }
        return false;
    }

    public static JsonObject CreateJournalLine(string accountCode, string sponsorId, decimal debit, decimal credit, string note = "", string iqama = "", string debtId = "")
    {
        var acc = ResolveAccount(accountCode);
        J.Need(AccountingPolicy.PostingAccount(acc.Code),"الحساب تجميعي؛ اختر حساب ترحيل فرعيًا.");
        J.Need(acc.DefaultSponsorId==null||ResolveSponsor(sponsorId).Id==acc.DefaultSponsorId,"السجل لا يطابق ملكية الحساب.");
        var sp = ResolveSponsor(acc.DefaultSponsorId ?? sponsorId);
        J.Need(debit == J.Money(debit) && credit == J.Money(credit), "مبلغ القيد يجب أن يكون بدقة الهللة.");
        debit = J.Money(debit);
        credit = J.Money(credit);
        J.Need(debit >= 0 && credit >= 0 && (debit > 0 ^ credit > 0), "سطر القيد المحاسبي يجب أن يحتوي على طرف مدين أو دائن موجب.");

        bool increases = (acc.NaturalSide == "Debit" && debit > 0) || (acc.NaturalSide == "Credit" && credit > 0);
        string effectText = increases
            ? $"زيادة رصيد ({acc.NameAr}) بمقدار {(debit > 0 ? debit : credit):0.00} ر.س لأن الحساب طبيعته ({(acc.NaturalSide == "Debit" ? "مدين" : "دائن")}) وجاء في الطرف ({(debit > 0 ? "المدين" : "الدائن")})."
            : $"نقص رصيد ({acc.NameAr}) بمقدار {(debit > 0 ? debit : credit):0.00} ر.س لأن الحساب طبيعته ({(acc.NaturalSide == "Debit" ? "مدين" : "دائن")}) وجاء في الطرف ({(debit > 0 ? "المدين" : "الدائن")}).";

        return new JsonObject
        {
            ["accountCode"] = acc.Code,
            ["accountNameAr"] = acc.NameAr,
            ["accountType"] = acc.AccountType,
            ["naturalSide"] = acc.NaturalSide,
            ["sponsorId"] = sp.Id,
            ["sponsorCode"] = sp.Code,
            ["sponsorNameAr"] = sp.ShortNameAr,
            ["debit"] = debit,
            ["credit"] = credit,
            ["effectDirection"] = increases ? "increase" : "decrease",
            ["effectExplanationAr"] = effectText,
            ["iqama"] = iqama,
            ["debtId"] = debtId,
            ["note"] = note
        };
    }

    public static JsonObject PostJournal(DbConnection c, DbTransaction tx, string transactionType, string description, string period, string reference, JsonArray lines, string actor, string iqama = "", string debtId = "", string? originSponsorId = null, string? creditorSponsorId = null)
    {
        J.Need(lines.Count >= 2, "القيد المحاسبي المزدوج يتطلب طرفين على الأقل.");
        decimal totalDebit = 0m, totalCredit = 0m;
        int idx = 1;
        foreach (var node in lines)
        {
            var l = node!.AsObject();
            var canonical=CreateJournalLine(l.Required("accountCode"),l.Required("sponsorId"),l.N("debit"),l.N("credit"));
            J.Need(canonical.N("debit")==l.N("debit")&&canonical.N("credit")==l.N("credit"),"مبلغ القيد يحتاج تصحيح دقة الهللة.");
            l["lineNo"] = idx++;
            totalDebit = J.Money(totalDebit + l.N("debit"));
            totalCredit = J.Money(totalCredit + l.N("credit"));
        }
        J.Need(totalDebit > 0 && totalDebit == totalCredit, $"القيد المحاسبي غير متوازن: إجمالي المدين ({totalDebit:0.00}) لا يساوي إجمالي الدائن ({totalCredit:0.00}).");

        J.Need(!Store.List(c, "journal", tx).Any(j => j.S("sourceRef") == reference), "مرجع القيد مسجل مسبقًا.");
        J.Need(lines.OfType<JsonObject>().GroupBy(l=>l.S("sponsorId")).All(g=>g.Sum(l=>l.N("debit")-l.N("credit"))==0m),"قيد السجل غير متوازن؛ اذكر المقاصة بين السجلات صراحة بدل توليدها لإخفاء الخطأ.");
        idx = 1; foreach (var line in lines.OfType<JsonObject>()) line["lineNo"] = idx++;
        totalDebit = lines.OfType<JsonObject>().Sum(l => l.N("debit")); totalCredit = lines.OfType<JsonObject>().Sum(l => l.N("credit"));
        int seq = Store.List(c, "journal", tx).Count + 1;
        var originSp = ResolveSponsor(originSponsorId);
        var creditorSp = ResolveSponsor(creditorSponsorId ?? originSponsorId);
        var doc = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("N"),
            ["journalNumber"] = $"JE-{DateTime.UtcNow:yyyy}-{seq:D5}",
            ["transactionType"] = transactionType,
            ["description"] = description,
            ["period"] = J.Period(period),
            ["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ["sourceRef"] = reference,
            ["iqama"] = iqama,
            ["debtId"] = debtId,
            ["originSponsorId"] = originSp.Id,
            ["originSponsorNameAr"] = originSp.ShortNameAr,
            ["creditorSponsorId"] = creditorSp.Id,
            ["creditorSponsorNameAr"] = creditorSp.ShortNameAr,
            ["totalDebit"] = totalDebit,
            ["totalCredit"] = totalCredit,
            ["lines"] = lines,
            ["createdBy"] = actor,
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
        };
        Store.Save(c, "journal", doc, tx);
        return doc;
    }

    public static JsonObject CreateMutualApproval(DbConnection c, DbTransaction tx, string requestKind, string titleAr, string bodyAr, string fromDept, string toDept, int minRank, string referenceId, JsonObject payload, string actor)
    {
        var req = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("N"),
            ["kind"] = requestKind,
            ["titleAr"] = titleAr,
            ["bodyAr"] = bodyAr,
            ["fromDepartment"] = fromDept,
            ["targetDepartment"] = toDept,
            ["requiredRankLevel"] = minRank,
            ["referenceId"] = referenceId,
            ["status"] = "pending",
            ["payload"] = payload,
            ["requestedBy"] = actor,
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
        };
        Store.Save(c, "approval_request", req, tx);

        var notif = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("N"),
            ["tab"] = "approvals",
            ["severity"] = "warning",
            ["titleAr"] = titleAr,
            ["bodyAr"] = bodyAr,
            ["targetDepartment"] = toDept,
            ["requiredRankLevel"] = minRank,
            ["approvalId"] = req.S("id"),
            ["referenceId"] = referenceId,
            ["status"] = "unread",
            ["createdBy"] = actor,
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
        };
        Store.Save(c, "sys_notification", notif, tx);
        return req;
    }

    public static bool PromissoryArchiveEnabled(DbConnection c, DbTransaction? tx = null)
    {
        var flag = Store.Get(c, "feature:promissory_notes", "setting", tx);
        return flag != null && flag.B("enabled");
    }

    public static void Map(WebApplication app, RouteGroupBuilder api, Store store)
    {
        // 1. استعراض السجلات التجارية وأرصدتها المدينة والدائنة والخزائن (مع تفصيل الصندوق الداخلي وخزنة جاهز والبنوك)
        api.MapGet("/finance/sponsors", (HttpContext ctx) => store.Read(c =>
        {
            var journals = Store.List(c, "journal");
            var debts = Store.List(c, "debt").Where(d=>FinancialScopes.Debt(ctx.User,d)).ToList();
            return Sponsors.Where(sp=>FinancialScopes.Sponsor(ctx.User,sp.Id)).Select(sp =>
            {
                var spLines = journals.SelectMany(j => j["lines"]?.AsArray() ?? []).Select(x => x!.AsObject()).Where(l => l.S("sponsorId") == sp.Id && FinancialScopes.Line(ctx.User,l)).ToList();
                // Held cash includes restricted Jahez custody; fuel clearing is not physical cash.
                decimal vaultBalance = spLines.Where(l => l.S("accountCode").StartsWith("MAIN.", StringComparison.Ordinal) || l.S("accountCode").StartsWith("SUP.", StringComparison.Ordinal) || l.S("accountCode").StartsWith("BANK.", StringComparison.Ordinal)).Sum(l => l.N("debit") - l.N("credit"));
                decimal mainVaultBalance = spLines.Where(l => l.S("accountCode").StartsWith("MAIN.", StringComparison.Ordinal)).Sum(l => l.N("debit") - l.N("credit"));
                decimal jahezBoxBalance = spLines.Where(l => l.S("accountCode").StartsWith("SUP.", StringComparison.Ordinal)).Sum(l => l.N("debit") - l.N("credit"));
                decimal bankBalance = spLines.Where(l => l.S("accountCode").StartsWith("BANK.", StringComparison.Ordinal)).Sum(l => l.N("debit") - l.N("credit"));
                decimal petroBalance = spLines.Where(l => l.S("accountCode").StartsWith("PETRO.", StringComparison.Ordinal)).Sum(l => l.N("debit") - l.N("credit"));
                decimal receivableFromJournals = spLines.Where(l => l.S("accountCode") is "1210" or "1220" or "1230").Sum(l => l.N("debit") - l.N("credit"));
                decimal activeDebtRemaining = debts.Where(d => d.S("status") is "active" or "paused" && AssignedDebtSponsor(d)?.Id == sp.Id).Sum(d => d.N("remainingBalance"));
                decimal expensesTotal = spLines.Where(l => l.S("accountType") == "Expense").Sum(l => l.N("debit") - l.N("credit"));
                decimal waivesTotal = spLines.Where(l => l.S("accountCode") == "5202").Sum(l => l.N("debit") - l.N("credit"));
                decimal interCompanyNet = spLines.Where(l => l.S("accountType") == "InterCompany").Sum(l => l.N("debit") - l.N("credit"));
                decimal payablesTotal = spLines.Where(l => l.S("accountType") == "Liability").Sum(l => l.N("credit") - l.N("debit"));

                return new
                {
                    id = sp.Id,
                    code = sp.Code,
                    unifiedNumber = sp.UnifiedNumber,
                    commercialReg = sp.CommercialReg,
                    nameAr = sp.NameAr,
                    shortNameAr = sp.ShortNameAr,
                    vaultBalance = J.Money(vaultBalance),
                    mainVaultBalance = J.Money(mainVaultBalance),
                    jahezBoxBalance = J.Money(jahezBoxBalance),
                    bankBalance = J.Money(bankBalance),
                    petroBalance = J.Money(petroBalance),
                    receivablesBalance = J.Money(activeDebtRemaining),
                    receivablesFromJournals = J.Money(receivableFromJournals),
                    receivablesReconciliationDifference = J.Money(receivableFromJournals - activeDebtRemaining),
                    activeDebtsCount = debts.Count(d => d.S("status") is "active" or "paused" && AssignedDebtSponsor(d)?.Id == sp.Id),
                    payablesBalance = J.Money(payablesTotal),
                    expensesTotal = J.Money(expensesTotal),
                    waiversTotal = J.Money(waivesTotal),
                    interCompanyNet = J.Money(interCompanyNet)
                };
            }).ToList();
        }));

        // 2. شجرة الحسابات الهرمية وأرصدة الصناديق الرئيسية والفرعية والبنوك والمصروفات (مع التجميع التلقائي للحسابات الأب)
        api.MapGet("/finance/accounts", (string? sponsorId,HttpContext ctx) => store.Read(c =>
        {
            var journals = Store.List(c, "journal");
            var allLines = journals.SelectMany(j => j["lines"]?.AsArray() ?? []).Select(x => x!.AsObject()).Where(l=>FinancialScopes.Line(ctx.User,l)).ToList();
            if (!string.IsNullOrWhiteSpace(sponsorId))
            {
                var targetSp = ResolveSponsor(sponsorId).Id;
                allLines = allLines.Where(l => l.S("sponsorId") == targetSp).ToList();
            }

            return ChartOfAccounts.Where(a=>FinancialScopes.Account(ctx.User,a.Code)).Select(acc =>
            {
                bool isGroup = ChartOfAccounts.Any(child => string.Equals(child.ParentCode, acc.Code, StringComparison.OrdinalIgnoreCase));
                var matching = allLines.Where(l => IsDescendantOrSelf(l.S("accountCode"), acc.Code)).ToList();
                decimal debit = J.Money(matching.Sum(l => l.N("debit")));
                decimal credit = J.Money(matching.Sum(l => l.N("credit")));
                decimal netBalance = acc.NaturalSide == "Debit" ? J.Money(debit - credit) : J.Money(credit - debit);
                return new
                {
                    code = acc.Code,
                    nameAr = acc.NameAr,
                    accountType = acc.AccountType,
                    naturalSide = acc.NaturalSide,
                    naturalSideAr = acc.NaturalSide == "Debit" ? "مدين" : "دائن",
                    parentCode = acc.ParentCode,
                    isGroup,
                    isVault = acc.IsVault,
                    vaultKind = acc.VaultKind,
                    vaultPurpose = acc.VaultPurpose,
                    allowedDirections = acc.AllowedDirections,
                    allowedDirectionsAr = acc.AllowedDirections == "InOnly" ? "قبض فقط (InOnly)" : acc.AllowedDirections == "OutOnly" ? "صرف فقط (OutOnly)" : "قبض وصرف (Dual)",
                    allowedSectors = acc.AllowedSectors ?? [],
                    operationalScopeAr = acc.OperationalScopeAr,
                    defaultSponsorId = acc.DefaultSponsorId,
                    defaultSponsorNameAr = acc.DefaultSponsorId != null ? ResolveSponsor(acc.DefaultSponsorId).ShortNameAr : "كل السجلات",
                    debitTotal = debit,
                    creditTotal = credit,
                    netBalance,
                    ruleExplanationAr = acc.NaturalSide == "Debit"
                        ? "حساب طبيعته مدينة: يزيد عندما يُجعل مديناً (Debit)، وينقص عندما يُجعل دائناً (Credit)."
                        : "حساب طبيعته دائنة: يزيد عندما يُجعل دائناً (Credit)، وينقص عندما يُجعل مديناً (Debit)."
                };
            }).ToList();
        }));

        // 3. كتالوج فئات المديونيات وخدمات الـ HR والأقسام والرتب والصناديق المصنفة
        api.MapGet("/finance/catalog", (HttpContext ctx) => store.Read(c => new
        {
            sponsors = Sponsors.Where(sp=>FinancialScopes.Sponsor(ctx.User,sp.Id)),
            unassignedDebtCount = Store.List(c, "debt").Count(d => AssignedDebtSponsor(d) == null),
            categories = CategoriesCatalog.Where(cat=>FinancialScopes.Category(ctx.User,cat.Code)).Select(cat=>new {cat.Code,cat.NameAr,cat.Sector,cat.ExpenseAccountCode,cat.ReceivableAccountCode,cat.DefaultReferenceCost,cat.AllowedVaultKinds,requiredPermission=AccessPolicy.CategoryPermission(cat.Code)}),
            vaults = ChartOfAccounts.Where(a => a.IsVault&&FinancialScopes.Account(ctx.User,a.Code)),
            departments = DepartmentsCatalog,
            roles = UserAccess.RolesCatalog,
            permissions = UserAccess.PermissionsCatalog,
            promissoryNotesArchiveEnabled = PromissoryArchiveEnabled(c)
        }));

        // 4. سجل القيود المحاسبية المزدوجة (Journal Entries)
        api.MapGet("/finance/journals", (string? sponsorId, string? iqama, string? accountCode,HttpContext ctx) => store.Read(c =>
        {
            var list = Store.List(c, "journal").Select(j=>FinancialScopes.Journal(ctx.User,j)).Where(j=>j!=null).Select(j=>j!).ToList();
            if (!string.IsNullOrWhiteSpace(sponsorId))
            {
                var spId = ResolveSponsor(sponsorId).Id;
                list = list.Where(j => j.S("originSponsorId") == spId || j.S("creditorSponsorId") == spId || (j["lines"]?.AsArray().Any(l => l!.AsObject().S("sponsorId") == spId) ?? false)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(iqama)) list = list.Where(j => j.S("iqama") == iqama.Trim()).ToList();
            if (!string.IsNullOrWhiteSpace(accountCode))
            {
                string targetAcc = accountCode.Trim();
                list = list.Where(j => j["lines"]?.AsArray().Any(l => IsDescendantOrSelf(l!.AsObject().S("accountCode"), targetAcc)) ?? false).ToList();
            }
            return list;
        }));

        // 5. إنشاء قيد خزينة مباشر (إيداع / تمويل صندوق / تحويل بين الصناديق والبنوك مع تطبيق قواعد العزل المالي)
        api.MapPost("/finance/vault-transfer", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            string fromVault = input.Required("fromVaultCode");
            string toVault = input.Required("toVaultCode");
            var (fromAcc, toAcc) = ValidateVaultTransfer(fromVault, toVault);
            FinancialScopes.DemandAccount(ctx.User,fromAcc.Code);FinancialScopes.DemandAccount(ctx.User,toAcc.Code);

            decimal amount = Debts.Amount(input, "amount");
            AccountingPolicy.RequireAvailable(AccountingPolicy.Balance(c,tx,fromAcc.Code),amount);
            string refNo = input.Required("sourceRef");
            J.Need(!Store.List(c, "journal", tx).Any(j => j.S("sourceRef") == refNo), "مرجع التحويل مسجل من قبل.");

            var fromSp = ResolveSponsor(fromAcc.DefaultSponsorId ?? input.S("fromSponsorId"));
            var toSp = ResolveSponsor(toAcc.DefaultSponsorId ?? input.S("toSponsorId", fromSp.Id));

            var lines = new JsonArray
            {
                CreateJournalLine(toAcc.Code, toSp.Id, amount, 0m, "استلام تحويل/تغذية خزينة", "", ""),
                CreateJournalLine(fromAcc.Code, fromSp.Id, 0m, amount, "صرف تحويل/تصفير عهدة خزينة", "", "")
            };
            if (fromSp.Id != toSp.Id)
            {
                lines.Add(CreateJournalLine("1310", fromSp.Id, amount, 0m, $"إثبات حق لسجل ({fromSp.ShortNameAr}) طرف ({toSp.ShortNameAr}) مقابل التحويل"));
                lines.Add(CreateJournalLine("1310", toSp.Id, 0m, amount, $"إثبات التزام على ({toSp.ShortNameAr}) لصالح ({fromSp.ShortNameAr}) مقابل التحويل"));
            }

            var journal = PostJournal(c, tx, "vault_transfer", input.S("description", $"تحويل مالي من ({fromAcc.NameAr}) إلى ({toAcc.NameAr})"), input.S("period", DateTime.UtcNow.ToString("yyyy-MM")), refNo, lines, ctx.User.Identity!.Name!, "", "", fromSp.Id, toSp.Id);
            Store.Audit(c, tx, "finance.vault_transfer", journal.S("id"), ctx.User.Identity!.Name!);
            return journal;
        }));

        // 5-ب. قبض رسوم فتح وتفعيل حساب جاهز جديد (200 ر.س أو مبلغ مخصص) في حسابات رسوم جاهز المخصصة (SUP.*.FEE أو MAIN.*.FEE أو MAIN.TR)
        api.MapPost("/finance/jahez-fee", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            string iqama = input.S("iqama");
            if (!string.IsNullOrWhiteSpace(iqama)) iqama = DirectoryProfiles.Iqama(iqama);
            var sp = ResolveSponsor(input.S("sponsorId", "BW"));
            decimal amount = input.ContainsKey("amount") && !string.IsNullOrWhiteSpace(input.S("amount")) ? Debts.Amount(input, "amount") : 200m;
            string defaultFeeVault = $"SUP.{sp.Code}.FEE";
            string vaultCode = input.S("targetVaultCode", defaultFeeVault);
            var vaultAcc = ResolveVault(vaultCode);

            J.Need(vaultAcc.VaultPurpose is "JahezFee" or "Combined",
                $"مخالفة محاسبية: رسوم فتح حسابات جاهز تُقبض فقط في حسابات رسوم جاهز المخصصة (SUP.*.FEE أو MAIN.*.FEE أو MAIN.TR)، ولا يجوز إيداعها في ({vaultAcc.NameAr}).");

            var vaultSp = ResolveSponsor(vaultAcc.DefaultSponsorId ?? sp.Id);
            J.Need(vaultSp.Id==sp.Id,"سجل الرسوم لا يطابق ملكية صندوق التحصيل.");FinancialScopes.DemandAccount(ctx.User,vaultAcc.Code);
            string refNo = input.Required("sourceRef");
            string period = J.Period(input.S("period", DateTime.UtcNow.ToString("yyyy-MM")));

            var lines = new JsonArray
            {
                CreateJournalLine(vaultAcc.Code, vaultSp.Id, amount, 0m, $"قبض رسوم فتح وتفعيل حساب جاهز جديد في ({vaultAcc.NameAr})", iqama, ""),
                CreateJournalLine(AccountingPolicy.FeeCounterpart(input.S("beneficiary",AccountingPolicy.FeeBeneficiary)), vaultSp.Id, 0m, amount, "إيراد رسوم فتح حساب جاهز للشركة — قرار المالك 2026-10-26", iqama, "")
            };

            var journal = PostJournal(c, tx, "jahez_account_fee", input.S("description", $"قبض رسوم فتح حساب جاهز ({vaultSp.ShortNameAr}) — مرجع ({refNo})"), period, refNo, lines, ctx.User.Identity!.Name!, iqama, "", vaultSp.Id, vaultSp.Id);
            Store.Audit(c, tx, "finance.jahez_fee", journal.S("id"), ctx.User.Identity!.Name!);
            return journal;
        }));

        // 5-ج. تسليم وتصفير عهدة صندوق جاهز اليومية (Zero-Out EOD Handover) من خزائن جاهز (SUP.*) إلى الصندوق الداخلي الرئيسي (MAIN.*)
        api.MapPost("/finance/jahez-handover", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            var sp = ResolveSponsor(input.S("sponsorId", "BW"));
            string refNo = input.Required("sourceRef");
            string period = J.Period(input.S("period", DateTime.UtcNow.ToString("yyyy-MM")));

            var journals = Store.List(c, "journal", tx);
            var allLines = journals.SelectMany(j => j["lines"]?.AsArray() ?? []).Select(x => x!.AsObject()).ToList();

            string supSetCode = $"SUP.{sp.Code}.SET";
            string supFeeCode = $"SUP.{sp.Code}.FEE";
            string mainSetTarget = $"MAIN.{sp.Code}.SET";
            string mainFeeTarget = $"MAIN.{sp.Code}.FEE";

            decimal currentSetBal = J.Money(allLines.Where(l => l.S("accountCode") == supSetCode).Sum(l => l.N("debit") - l.N("credit")));
            decimal currentFeeBal = J.Money(allLines.Where(l => l.S("accountCode") == supFeeCode).Sum(l => l.N("debit") - l.N("credit")));

            decimal settlementAmount = input.ContainsKey("settlementAmount") && !string.IsNullOrWhiteSpace(input.S("settlementAmount"))
                ? Debts.OptionalAmount(input, "settlementAmount", 0m)
                : Math.Max(0m, currentSetBal);
            decimal feeAmount = input.ContainsKey("feeAmount") && !string.IsNullOrWhiteSpace(input.S("feeAmount"))
                ? Debts.OptionalAmount(input, "feeAmount", 0m)
                : Math.Max(0m, currentFeeBal);

            FinancialScopes.DemandSponsor(ctx.User,sp.Id);
            if(settlementAmount>0)FinancialScopes.DemandAccount(ctx.User,supSetCode);if(feeAmount>0)FinancialScopes.DemandAccount(ctx.User,supFeeCode);
            AccountingPolicy.RequireAvailable(currentSetBal,settlementAmount);
            AccountingPolicy.RequireAvailable(currentFeeBal,feeAmount);
            J.Need(settlementAmount + feeAmount > 0m, $"لا يوجد رصيد موجب في خزنة جاهز ({sp.ShortNameAr}) لتسليمه وتصفيره، أو لم يتم تحديد مبلغ التسليم.");

            var lines = new JsonArray();
            if (settlementAmount > 0m)
            {
                lines.Add(CreateJournalLine(mainSetTarget, sp.Id, settlementAmount, 0m, $"استلام تصفية خزنة كاش جاهز ({supSetCode}) بالصندوق الداخلي الرئيسي", "", ""));
                lines.Add(CreateJournalLine(supSetCode, sp.Id, 0m, settlementAmount, $"تصفير وتسليم عهدة خزنة كاش جاهز إلى ({mainSetTarget})", "", ""));
            }
            if (feeAmount > 0m)
            {
                lines.Add(CreateJournalLine(mainFeeTarget, sp.Id, feeAmount, 0m, $"استلام حصيلة رسوم فتح حسابات جاهز ({supFeeCode}) بالصندوق الداخلي الرئيسي", "", ""));
                lines.Add(CreateJournalLine(supFeeCode, sp.Id, 0m, feeAmount, $"تصفير وتسليم عهدة رسوم فتح حسابات جاهز إلى ({mainFeeTarget})", "", ""));
            }

            var journal = PostJournal(
                c, tx,
                "jahez_eod_handover",
                input.S("description", $"تسليم وتصفير عهدة صندوق جاهز اليومية ({sp.ShortNameAr}) إلى الصندوق الداخلي الرئيسي — تصفيات: {settlementAmount:0.00} ر.س | رسوم: {feeAmount:0.00} ر.س"),
                period,
                refNo,
                lines,
                ctx.User.Identity!.Name!,
                "", "", sp.Id, sp.Id
            );
            Store.Audit(c, tx, "finance.jahez_handover", journal.S("id"), ctx.User.Identity!.Name!);
            return new
            {
                sponsorCode = sp.Code,
                sponsorNameAr = sp.ShortNameAr,
                settlementHandedOver = settlementAmount,
                feeHandedOver = feeAmount,
                totalHandedOver = J.Money(settlementAmount + feeAmount),
                targetSettlementVault = mainSetTarget,
                targetFeeVault = mainFeeTarget,
                journal
            };
        }));

        // 6. كشف حساب المندوب أو الموظف الشامل 360 درجة (مدين / دائن / رصيد تراكمي متسلسل زمنياً بدقة / حسب السجل / مع القيود والرواتب)
        api.MapGet("/finance/statement/rider/{iqama}",(string iqama,string? sponsorId,HttpContext ctx)=>store.Read(c=>RiderStatements.Build(c,iqama,sponsorId,ctx.User)));

        // 7. منظومة الاعتمادات المتبادلة (المالية ⇄ HR ⇄ التشغيل ⇄ الحركة) ومركز الإشعارات والمهام
        api.MapGet("/approvals", (string? department, string? status,HttpContext ctx) => store.Read(c =>
        {
            var list = Store.List(c, "approval_request").Where(a=>FinancialScopes.Approval(ctx.User,a,c)).ToList();
            if (!string.IsNullOrWhiteSpace(department)) list = list.Where(a => a.S("targetDepartment") == department || a.S("fromDepartment") == department).ToList();
            if (!string.IsNullOrWhiteSpace(status)) list = list.Where(a => a.S("status") == status).ToList();
            return list;
        }));

        api.MapPost("/approvals/{id}/decide", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            var req = Store.Get(c, id, "approval_request", tx) ?? throw new ApiError("طلب الاعتماد غير موجود.", 404);
            if(!FinancialScopes.Approval(ctx.User,req,c,tx))throw new ApiError("الطلب خارج نطاق القسم/السجل المسند لك.",403);
            AccessPolicy.ValidateApproval(ctx.User,req,input);
            J.Need(req.S("status") == "pending" || req.S("status") == "suspended", "تم البت في هذا الطلب مسبقاً.");
            string decision = input.Required("decision"); // approved | rejected | suspended
            J.Need(decision is "approved" or "rejected" or "suspended", "قرار غير صالح: استخدم approved أو rejected أو suspended.");
            string note = input.S("note");
            if (decision is "rejected" or "suspended") J.Need(!string.IsNullOrWhiteSpace(note), "يلزم تدوين سبب الرفض أو التعليق.");

            req["status"] = decision;
            req["decisionNote"] = note;
            req["administrativeSelfOverride"] = AccessPolicy.Administrator(ctx.User) && input.B("overrideSelf") && string.Equals(req.S("requestedBy",req.S("createdBy")),ctx.User.Identity!.Name,StringComparison.OrdinalIgnoreCase);
            req["decidedBy"] = ctx.User.Identity!.Name!;
            req["decidedAt"] = DateTimeOffset.UtcNow.ToString("O");
            Store.Save(c, "approval_request", req, tx);

            // إذا كان الطلب مرتبطاً بمديونية بانتظار التصديق المتبادل، نفعّلها ونرحّل قيدها المالي
            if (req.S("kind") == "debt_creation" && decision == "approved")
            {
                var debt = Store.Get(c, req.S("referenceId"), "debt", tx);
                if (debt != null && debt.S("status") == "pending_approval")
                {
                    debt["status"] = debt.N("remainingBalance") > 0 ? "active" : "settled";
                    debt["approvedBy"] = ctx.User.Identity!.Name!;
                    debt["approvedAt"] = DateTimeOffset.UtcNow.ToString("O");
                    Store.Save(c, "debt", debt, tx);
                    Debts.PostCreationJournalIfNeeded(c, tx, debt, ctx.User.Identity!.Name!);
                }
            }
            else if (req.S("kind") == "debt_creation" && decision == "rejected")
            {
                var debt = Store.Get(c, req.S("referenceId"), "debt", tx);
                if (debt != null && debt.S("status") == "pending_approval")
                {
                    debt["status"] = "cancelled";
                    Store.Save(c, "debt", debt, tx);
                }
            }

            foreach (var n in Store.List(c, "sys_notification", tx).Where(n => n.S("approvalId") == id))
            {
                n["status"] = "acknowledged";
                n["acknowledgedBy"] = ctx.User.Identity!.Name!;
                n["acknowledgedAt"] = DateTimeOffset.UtcNow.ToString("O");
                Store.Save(c, "sys_notification", n, tx);
            }

            Store.Audit(c, tx, "approval." + decision, id, ctx.User.Identity!.Name!);
            return req;
        }));

        api.MapGet("/notifications/center", (string? tab, string? department,HttpContext ctx) => store.Read(c =>
        {
            var items = Store.List(c, "sys_notification").Where(n=>AccessPolicy.Administrator(ctx.User)||(n.S("targetDepartment")==ctx.User.FindFirst("department")?.Value||n.S("targetDepartment")=="ALL")&&(n.S("approvalId")==""||Store.Get(c,n.S("approvalId"),"approval_request") is {} approval&&FinancialScopes.Approval(ctx.User,approval,c))).ToList();
            if (!string.IsNullOrWhiteSpace(tab)) items = items.Where(n => n.S("tab") == tab).ToList();
            if (!string.IsNullOrWhiteSpace(department)) items = items.Where(n => n.S("targetDepartment") == department || n.S("targetDepartment") == "ALL").ToList();
            return new
            {
                unreadCount = items.Count(n => n.S("status") == "unread"),
                generalCount = items.Count(n => n.S("tab") == "general"),
                approvalsCount = items.Count(n => n.S("tab") == "approvals" && n.S("status") == "unread"),
                todoCount = items.Count(n => n.S("tab") == "todo" && !n.B("completed")),
                items
            };
        }));

        api.MapPost("/notifications/center", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            string tab = input.S("tab", "general");
            J.Need(tab is "general" or "approvals" or "todo", "تبويب الإشعار غير صالح.");
            var notif = new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString("N"),
                ["tab"] = tab,
                ["severity"] = input.S("severity", "info"),
                ["titleAr"] = input.Required("titleAr"),
                ["bodyAr"] = input.Required("bodyAr"),
                ["targetDepartment"] = input.S("targetDepartment", "ALL"),
                ["requiredRankLevel"] = (int)input.N("requiredRankLevel", 1),
                ["assignedTo"] = input.S("assignedTo"),
                ["completed"] = false,
                ["status"] = "unread",
                ["createdBy"] = ctx.User.Identity!.Name!,
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
            };
            Store.Save(c, "sys_notification", notif, tx);
            Store.Audit(c, tx, "notification.created", notif.S("id"), ctx.User.Identity!.Name!);
            return notif;
        }));

        api.MapPost("/notifications/center/{id}/toggle-todo", (string id, HttpContext ctx) => store.Write((c, tx) =>
        {
            var notif = Store.Get(c, id, "sys_notification", tx) ?? throw new ApiError("المهمة غير موجودة.", 404);
            bool done = !notif.B("completed");
            notif["completed"] = done;
            notif["status"] = done ? "acknowledged" : "unread";
            notif["completedBy"] = done ? ctx.User.Identity!.Name! : "";
            notif["completedAt"] = done ? DateTimeOffset.UtcNow.ToString("O") : "";
            Store.Save(c, "sys_notification", notif, tx);
            return notif;
        }));

        // 8. بوابة تطبيق الجوال وخدمات جوجل (Google Play / Firebase Cloud Messaging - FCM)
        app.MapPost("/api/rider-mobile/login", async Task<IResult> (JsonObject input, HttpContext ctx) =>
        {
            string iqama = input.S("iqamaNo");
            if (!await MobileLimits.Allowed(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", TokenHash(iqama), ctx.RequestAborted)) throw new ApiError("حاول مرة أخرى بعد قليل.", 429);
            return store.Write<IResult>((c, tx) =>
            {
                if (!InquiryApi.ConsumeAccess(c, tx, iqama, input.S("phoneLast4"), input.S("code"), "mobile")) throw new ApiError("تعذر التحقق من بيانات الدخول أو انتهت صلاحية الرمز.", 401);
                var rider = Store.Get(c, iqama, "rider", tx)!;
                if (!rider.S("status").Equals("Active", StringComparison.OrdinalIgnoreCase)) throw new ApiError("بيانات الدخول غير صحيحة أو الحساب غير نشط.", 401);
                string token = "rmob_" + CampaignLinks.NewToken(), lang = input.S("language", "ar");
                if (lang is not ("ar" or "en" or "ur" or "bn")) lang = "ar";
                string fcm = input.S("fcmDeviceToken"); J.Need(fcm.Length <= 4096, "رمز جهاز طويل جدًا.");
                var session = new JsonObject { ["id"] = "mobile:" + TokenHash(token), ["iqama"] = iqama, ["language"] = lang, ["fcmDeviceToken"] = fcm, ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"), ["expiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(8).ToString("O") };
                Store.Save(c, "mobile_session", session, tx); Store.Audit(c, tx, "mobile.login", iqama, "rider");
                return Results.Ok(new { accessToken = token, tokenType = "Bearer", expiresAtUtc = session.S("expiresAtUtc"), language = lang, fcmRegistered = false, fcmTokenStored = fcm.Length > 0, rider = UserAccess.RiderView(rider) });
            }, false);
        }).RequireRateLimiting("sensitive");

        api.MapPost("/api/rider-mobile/broadcast".Replace("/api/", "/"), (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            string category = input.S("category", "operational"); // financial | operational | administrative | form_link
            string titleAr = input.Required("titleAr");
            string bodyAr = input.Required("bodyAr");
            string targetIqama = input.S("iqama", "ALL");
            if(targetIqama=="ALL"){if(!FinancialScopes.AllCompanies(ctx.User))throw new ApiError("النشر العام يحتاج نطاقًا على جميع السجلات.",403);}
            else FinancialScopes.DemandRider(ctx.User,Store.Get(c,J.Iqama(targetIqama),"rider",tx)??throw new ApiError("المندوب غير موجود.",404));
            string actionUrl = input.S("actionUrl");
            J.Need(string.IsNullOrWhiteSpace(actionUrl) || (Uri.TryCreate(actionUrl, UriKind.Absolute, out var action) && action.Scheme == "https") || (actionUrl.StartsWith("/") && !actionUrl.StartsWith("//")), "رابط الإشعار غير صالح.");
            bool requiresAck = input.B("requiresAck");

            var msg = new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString("N"),
                ["category"] = category,
                ["titleAr"] = titleAr,
                ["bodyAr"] = bodyAr,
                ["targetIqama"] = targetIqama,
                ["actionUrl"] = actionUrl,
                ["requiresAck"] = requiresAck,
                ["deliveryChannel"] = "in_app",
                ["deliveryStatus"] = "stored",
                ["acknowledgedBy"] = new JsonArray(),
                ["sentBy"] = ctx.User.Identity!.Name!,
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
            };
            Store.Save(c, "mobile_broadcast", msg, tx);
            Store.Audit(c, tx, "mobile.broadcast", msg.S("id"), ctx.User.Identity!.Name!);
            return msg;
        }));

        api.MapGet("/rider-mobile/broadcasts", (HttpContext ctx) => store.Read(c => Store.List(c, "mobile_broadcast").Where(b=>b.S("targetIqama")=="ALL"||Store.Get(c,b.S("targetIqama"),"rider") is {} rider&&FinancialScopes.Rider(ctx.User,rider)||AccessPolicy.Administrator(ctx.User))));

        // إعدادات ربط Firebase Cloud Messaging (FCM) وتطبيق Google Play
        api.MapGet("/rider-mobile/firebase-config", (HttpContext ctx) => store.Read(c =>
        {
            var cfg = Store.Get(c, "firebase:config", "setting");
            var sessions = Store.List(c, "mobile_session");
            string baseUrl = CampaignLinks.Base(app.Configuration, ctx);
            return new
            {
                projectId = cfg?.S("projectId", "albawaba-gate-rider") ?? "albawaba-gate-rider",
                senderId = cfg?.S("senderId", "") ?? "",
                vapidPublicKey = cfg?.S("vapidPublicKey", "") ?? "",
                androidPackageName = cfg?.S("androidPackageName", "com.albawaba.gate.rider") ?? "com.albawaba.gate.rider",
                googlePlayUrl = cfg?.S("googlePlayUrl", "") ?? "",
                hasServiceAccountJson = !string.IsNullOrWhiteSpace(cfg?.S("serviceAccountJson")),
                configured = cfg != null && !string.IsNullOrWhiteSpace(cfg.S("projectId")),
                webMobilePortalUrl = baseUrl + "/rider-app",
                inquiryPortalUrl = baseUrl + "/inquiry",
                activeSessionsCount = sessions.Count,
                fcmRegisteredDevicesCount = sessions.Count(s => !string.IsNullOrWhiteSpace(s.S("fcmDeviceToken"))),
                updatedAt = cfg?.S("updatedAt") ?? ""
            };
        }));

        api.MapPost("/rider-mobile/firebase-config", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            var existing = Store.Get(c, "firebase:config", "setting", tx);
            var doc = new JsonObject
            {
                ["id"] = "firebase:config",
                ["projectId"] = input.Required("projectId").Trim(),
                ["senderId"] = input.S("senderId").Trim(),
                ["vapidPublicKey"] = input.S("vapidPublicKey").Trim(),
                ["androidPackageName"] = input.S("androidPackageName", "com.albawaba.gate.rider").Trim(),
                ["googlePlayUrl"] = input.S("googlePlayUrl", "").Trim(),
                ["serviceAccountJson"] = string.IsNullOrWhiteSpace(input.S("serviceAccountJson")) ? (existing?.S("serviceAccountJson") ?? "") : input.S("serviceAccountJson"),
                ["updatedBy"] = ctx.User.Identity!.Name!,
                ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O")
            };
            Store.Save(c, "setting", doc, tx);
            Store.Audit(c, tx, "mobile.firebase_configured", doc.S("projectId"), ctx.User.Identity!.Name!);
            return new { ok = true, projectId = doc.S("projectId"), androidPackageName = doc.S("androidPackageName") };
        }));

        // واجهة بيانات تطبيق المندوب (يستدعيها تطبيق الجوال بعد تسجيل الدخول بـ rmob_ token)
        app.MapGet("/api/rider-mobile/portal/{iqama}", (string iqama, HttpContext ctx) => store.Read(c =>
        {
            J.Iqama(iqama);
            MobileSession(c, ctx, iqama);

            var rider = Store.Get(c, iqama, "rider") ?? throw new ApiError("المندوب غير موجود.", 404);
            var debts = Store.List(c, "debt").Where(d => d.S("iqama") == iqama).Select(d => new
            {
                id = d.S("id"),
                category = d.S("category"),
                categoryAr = d.S("categoryAr", d.S("category")),
                creditorSponsorNameAr = d.S("creditorSponsorNameAr", "شركة البوابة"),
                totalAmount = d.N("totalAmount"),
                paidAmount = d.N("paidAmount"),
                waivedAmount = d.N("waivedAmount"),
                remainingBalance = d.N("remainingBalance"),
                monthlyInstallment = d.N("monthlyInstallment"),
                status = d.S("status"),
                effectiveFrom = d.S("effectiveFrom")
            }).ToList();

            var broadcasts = Store.List(c, "mobile_broadcast")
                .Where(b => b.S("targetIqama") == "ALL" || b.S("targetIqama") == iqama)
                .OrderByDescending(b => b.S("createdAt"))
                .Select(b => new
                {
                    id = b.S("id"),
                    category = b.S("category"),
                    titleAr = b.S("titleAr"),
                    bodyAr = b.S("bodyAr"),
                    actionUrl = b.S("actionUrl"),
                    requiresAck = b.B("requiresAck"),
                    acknowledged = (b["acknowledgedBy"] as JsonArray)?.Any(x => x?.GetValue<string>() == iqama) ?? false,
                    createdAt = b.S("createdAt")
                }).ToList();

            return new
            {
                rider = UserAccess.RiderView(rider),
                totalRemainingDebt = J.Money(debts.Where(d => d.status is "active" or "paused").Sum(d => d.remainingBalance)),
                totalPaidDebt = J.Money(debts.Sum(d => d.paidAmount)),
                totalWaivedDebt = J.Money(debts.Sum(d => d.waivedAmount)),
                debts,
                broadcasts
            };
        }));

        app.MapPost("/api/rider-mobile/broadcasts/{id}/ack", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            string iqama = J.Iqama(input.Required("iqama"));
            MobileSession(c, ctx, iqama, tx);
            var b = Store.Get(c, id, "mobile_broadcast", tx) ?? throw new ApiError("الإشعار غير موجود.", 404);
            J.Need(b.S("targetIqama") == "ALL" || b.S("targetIqama") == iqama, "الإشعار غير متاح لهذا المندوب.");
            var acks = b["acknowledgedBy"] as JsonArray ?? new JsonArray();
            if (!acks.Any(x => x?.GetValue<string>() == iqama))
            {
                acks.Add(iqama);
                b["acknowledgedBy"] = acks;
                Store.Save(c, "mobile_broadcast", b, tx);
            }
            return new { ok = true, id, acknowledgedCount = acks.Count };
        }));

        app.MapGet("/rider-app", () => Results.Text(RiderAppHtml, "text/html; charset=utf-8"));
        app.MapGet("/rider-app.js", () => Results.Text(RiderAppJs, "text/javascript; charset=utf-8"));

        // 9. أرشيف سندات الأمر التكاملي الموحد (جاهز برمجياً مع مفتاح تفعيل Feature Flag)
        api.MapPost("/finance/features/promissory-notes", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            bool enabled = input.B("enabled");
            var doc = new JsonObject
            {
                ["id"] = "feature:promissory_notes",
                ["enabled"] = enabled,
                ["updatedBy"] = ctx.User.Identity!.Name!,
                ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O")
            };
            Store.Save(c, "setting", doc, tx);
            Store.Audit(c, tx, "feature.promissory_notes", enabled ? "enabled" : "disabled", ctx.User.Identity!.Name!);
            return doc;
        }));

        api.MapGet("/promissory-notes", (HttpContext ctx) => store.Read(c => new
        {
            featureEnabled = PromissoryArchiveEnabled(c),
            items = Store.List(c, "promissory_note").Where(n=>FinancialScopes.Sponsor(ctx.User,n.S("beneficiarySponsorId")))
        }));

        api.MapPost("/promissory-notes", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            J.Need(PromissoryArchiveEnabled(c, tx), "موديول أرشيف سندات الأمر في وضع التعليق حالياً (Dormant Mode) لحين تفعيله أو إتمام الدمج.");
            string iqama = J.Iqama(input.Required("iqama"));
            var sp = ResolveSponsor(input.S("beneficiarySponsorId"));
            decimal amount = Debts.Amount(input, "amount");
            string noteNumber = input.Required("noteNumber");
            J.Need(!Store.List(c, "promissory_note", tx).Any(n => n.S("noteNumber") == noteNumber), "رقم سند الأمر مسجل مسبقاً.");

            var note = new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString("N"),
                ["noteNumber"] = noteNumber,
                ["nafezNumber"] = input.S("nafezNumber", noteNumber),
                ["iqama"] = iqama,
                ["debtorName"] = input.S("debtorName"),
                ["department"] = input.S("department", "HR"), // Fleet | HR | Platforms
                ["beneficiarySponsorId"] = sp.Id,
                ["beneficiarySponsorNameAr"] = sp.NameAr,
                ["amount"] = amount,
                ["issueDate"] = input.S("issueDate", DateTime.UtcNow.ToString("yyyy-MM-dd")),
                ["dueDate"] = input.S("dueDate", "عند الطلب"),
                ["status"] = input.S("status", "ActiveNafez"), // Draft | ActiveNafez | Cleared | NajizCourt
                ["attachmentPath"] = $"wwwroot/private/riders/{iqama}/promissory-notes/{noteNumber}",
                ["notes"] = input.S("notes"),
                ["createdBy"] = ctx.User.Identity!.Name!,
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
            };
            Store.Save(c, "promissory_note", note, tx);
            Store.Audit(c, tx, "promissory_note.created", note.S("id"), ctx.User.Identity!.Name!);
            return note;
        }));
    }

    private const string RiderAppHtml = """
<!doctype html>
<html lang="ar" dir="rtl">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>تطبيق مندوب البوابة — الكشوف والمديونيات والإشعارات</title>
  <link rel="stylesheet" href="/app.css">
  <style>
    body { background: var(--bg); color: var(--ink); font-family: inherit; margin: 0; padding: 16px; }
    .mob-shell { max-width: 560px; margin: 0 auto; }
    .mob-head { display: flex; justify-content: space-between; align-items: center; padding: 14px 18px; background: var(--surface); border: 1px solid var(--line); border-radius: 14px; margin-bottom: 16px; }
    .mob-card { background: var(--surface); border: 1px solid var(--line); border-radius: 14px; padding: 18px; margin-bottom: 14px; }
    .mob-kpis { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; margin-bottom: 14px; }
    .mob-kpi { background: var(--surface); border: 1px solid var(--line); border-radius: 12px; padding: 12px; text-align: center; }
  </style>
</head>
<body>
  <div class="mob-shell" id="rider-app-root">
    <div class="mob-card">جاري تحميل تطبيق مندوب البوابة...</div>
  </div>
  <script src="/rider-app.js"></script>
</body>
</html>
""";

    private const string RiderAppJs = """
(function () {
  const root = document.getElementById('rider-app-root');
  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const money = n => Number(n || 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

  let session = null;
  try { session = JSON.parse(sessionStorage.getItem('gate_rider_mob') || 'null'); } catch {}

  async function api(url, body) {
    const headers = { 'Content-Type': 'application/json' };
    if (session?.accessToken) headers['Authorization'] = 'Bearer ' + session.accessToken;
    const res = await fetch(url, {
      method: body ? 'POST' : 'GET',
      headers,
      body: body ? JSON.stringify(body) : undefined
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) throw new Error(data.error || 'تعذر إتمام الطلب');
    return data;
  }

  function renderLogin(err = '') {
    root.innerHTML = `
      <div class="mob-head">
        <div><strong>بوابة المندوب</strong><div class="muted small">كشوف الحساب · المديونيات · الإشعارات والنماذج</div></div>
        <span class="tag good">متصل</span>
      </div>
      <div class="mob-card">
        <h2 style="margin-top:0">تسجيل دخول المندوب</h2>
        <p class="muted small">أدخل الإقامة وآخر أربعة أرقام من جوالك ورمز الدخول الخاص الذي تسلمه لك الإدارة بعد مراجعة الهوية.</p>
        ${err ? `<div class="notice error">${esc(err)}</div>` : ''}
        <form id="mob-login-form" class="form-grid">
          <label class="full">رقم الهوية / الإقامة (10 أرقام)
            <input name="iqamaNo" required pattern="[12][0-9]{9}" inputmode="numeric" placeholder="2XXXXXXXXX">
          </label>
          <label class="full">آخر 4 أرقام من الجوال المسجل<input name="phoneLast4" required pattern="[0-9]{4}" inputmode="numeric" autocomplete="off"></label>
          <label class="full">رمز دخول خاص من الإدارة<input name="code" required minlength="64" maxlength="64" autocomplete="one-time-code"></label>
          <label class="full">اللغة المفضلة
            <select name="language">
              <option value="ar">العربية (Arabic)</option>
              <option value="en">English</option>
              <option value="bn">বাংলা (Bengali)</option>
            </select>
          </label>
          <button class="full good">التحقق والدخول إلى حسابي</button>
        </form>
      </div>
    `;
    const fragment=new URLSearchParams(location.hash.slice(1));
    if(fragment.has('access_code')){root.querySelector('[name=code]').value=fragment.get('access_code');root.querySelector('[name=iqamaNo]').value=fragment.get('iqama')||'';history.replaceState(null,'',location.pathname);}
    document.getElementById('mob-login-form').onsubmit = async e => {
      e.preventDefault();
      const fd = new FormData(e.target);
      try {
        const res = await api('/api/rider-mobile/login', {
          iqamaNo: fd.get('iqamaNo'),
          language: fd.get('language'),
          phoneLast4: fd.get('phoneLast4'),
          code: fd.get('code'),
          devicePlatform: 'Android-GooglePlay-PWA'
        });
        session = { accessToken: res.accessToken, iqama: res.rider.iqama, name: res.rider.name };
        try { sessionStorage.setItem('gate_rider_mob', JSON.stringify(session)); } catch {}
        await renderPortal();
      } catch (ex) {
        renderLogin(ex.message);
      }
    };
  }

  async function renderPortal() {
    if (!session?.iqama) return renderLogin();
    try {
      const data = await api('/api/rider-mobile/portal/' + encodeURIComponent(session.iqama));
      const r = data.rider || {};
      root.innerHTML = `
        <div class="mob-head">
          <div>
            <strong>${esc(r.name || session.iqama)}</strong>
            <div class="muted small number">${esc(r.iqama)} · ${esc(r.sponsor || 'شركة البوابة')}</div>
          </div>
          <button class="secondary small" id="mob-logout">خروج</button>
        </div>
        <div class="mob-kpis">
          <div class="mob-kpi"><div class="muted small">المديونية المتبقية</div><strong class="number money-negative">${money(data.totalRemainingDebt)}</strong><div class="small muted">ر.س</div></div>
          <div class="mob-kpi"><div class="muted small">إجمالي المسدد</div><strong class="number money-positive">${money(data.totalPaidDebt)}</strong><div class="small muted">ر.س</div></div>
          <div class="mob-kpi"><div class="muted small">إعفاءات ممنوحة</div><strong class="number">${money(data.totalWaivedDebt)}</strong><div class="small muted">ر.س</div></div>
        </div>
        <div class="mob-card">
          <h3 style="margin-top:0">📲 الإشعارات الإدارية والتشغيلية والنماذج (${(data.broadcasts || []).length})</h3>
          ${(data.broadcasts || []).map(b => `
            <div class="notice ${b.acknowledged ? 'good' : 'warn'}" style="margin-bottom:10px">
              <div style="display:flex;justify-content:space-between;align-items:center">
                <strong>${esc(b.titleAr)}</strong>
                <span class="small number muted">${esc((b.createdAt || '').slice(0, 10))}</span>
              </div>
              <p style="margin:6px 0">${esc(b.bodyAr)}</p>
              <div class="actions">
                ${b.actionUrl ? `<a class="button small secondary" href="${esc(b.actionUrl)}" target="_blank">فتح الرابط / الفورم ↗</a>` : ''}
                ${b.requiresAck && !b.acknowledged ? `<button class="small good" data-ack="${esc(b.id)}">✓ قرأت وأوافق على الإشعار</button>` : b.acknowledged ? `<span class="tag good">تم التأكيد ✓</span>` : ''}
              </div>
            </div>
          `).join('') || '<p class="muted">لا توجد إشعارات جديدة حالياً.</p>'}
        </div>
        <div class="mob-card">
          <h3 style="margin-top:0">💳 بيان المديونيات والأقساط (${(data.debts || []).length})</h3>
          ${(data.debts || []).map(d => `
            <div style="border-bottom:1px solid var(--line);padding:10px 0">
              <div style="display:flex;justify-content:space-between">
                <strong>${esc(d.categoryAr)}</strong>
                <span class="tag ${d.status === 'settled' ? 'good' : 'warn'}">${esc(d.status === 'settled' ? 'مسدد' : 'نشط')}</span>
              </div>
              <div class="small muted">السجل الدائن: ${esc(d.creditorSponsorNameAr)} · بداية الاستقطاع: ${esc(d.effectiveFrom)}</div>
              <div class="small number" style="margin-top:4px">
                الإجمالي: <b>${money(d.totalAmount)}</b> · المسدد: <b class="money-positive">${money(d.paidAmount)}</b> · الإعفاء: <b>${money(d.waivedAmount)}</b> · المتبقي: <b class="money-negative">${money(d.remainingBalance)} ر.س</b> (القسط: ${money(d.monthlyInstallment)})
              </div>
            </div>
          `).join('') || '<p class="muted">لا توجد مديونيات مسجلة على حسابك.</p>'}
        </div>
      `;
      document.getElementById('mob-logout').onclick = () => {
        session = null;
        try { sessionStorage.removeItem('gate_rider_mob'); } catch {}
        renderLogin();
      };
      root.querySelectorAll('[data-ack]').forEach(btn => {
        btn.onclick = async () => {
          btn.disabled = true;
          await api('/api/rider-mobile/broadcasts/' + encodeURIComponent(btn.dataset.ack) + '/ack', { iqama: session.iqama });
          await renderPortal();
        };
      });
    } catch (ex) {
      session = null;
      renderLogin(ex.message);
    }
  }

  renderPortal();
})();
""";
}
