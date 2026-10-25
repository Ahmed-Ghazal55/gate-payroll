using System.Security.Cryptography;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Payroll;

public static class FinancialCore
{
    public sealed record SponsorDef(string Id, string Code, string UnifiedNumber, string NameAr, string ShortNameAr, string CommercialReg);
    public sealed record AccountDef(string Code, string NameAr, string AccountType, string NaturalSide, string? ParentCode, string? DefaultSponsorId = null, bool IsVault = false);
    public sealed record CategoryMeta(string Code, string NameAr, string Sector, string ExpenseAccountCode, string ReceivableAccountCode, decimal DefaultReferenceCost);

    // 1. السجلات التجارية الرسمية المعتمدة في نظام البوابة (Logistic-ERP Sponsor.cs)
    public static readonly SponsorDef[] Sponsors =
    [
        new("019c18d5-62e1-7000-8000-000000000041", "BW", "7015658094", "شركة البوابة المقبلة للتجارة", "سجل البوابا", "4030362130"),
        new("019c18d5-62e1-7000-8000-000000000042", "EX", "7034861059", "شركة إكسبرس جايت", "سجل إكسبرس", "4030491120"),
        new("019c18d5-62e1-7000-8000-000000000040", "TR", "7038745530", "مؤسسة البوابة المقبلة التجارية", "سجل التجارية", "4030512290")
    ];

    // 2. شجرة الحسابات الجزئية القياسية والصناديق والبنوك (متوافقة مع jahez_v3 و AccountingCore)
    public static readonly AccountDef[] ChartOfAccounts =
    [
        // الأصول المتداولة - الخزائن والصندوق الداخلي والبنوك (طبيعتها: مدين Debit - تزيد بالقبض وتنقص بالصرف)
        new("1100", "الأصول المتداولة — الخزائن والبنوك", "Asset", "Debit", null),
        new("1110", "الصندوق الداخلي العام للتحصيلات (MAIN)", "Asset", "Debit", "1100"),
        new("MAIN.BW.SET", "صندوق التحصيلات الداخلي — سجل البوابا", "Asset", "Debit", "1110", Sponsors[0].Id, true),
        new("MAIN.BW.FEE", "صندوق رسوم الحسابات — سجل البوابا", "Asset", "Debit", "1110", Sponsors[0].Id, true),
        new("MAIN.EX.SET", "صندوق التحصيلات الداخلي — سجل إكسبرس", "Asset", "Debit", "1110", Sponsors[1].Id, true),
        new("MAIN.EX.FEE", "صندوق رسوم الحسابات — سجل إكسبرس", "Asset", "Debit", "1110", Sponsors[1].Id, true),
        new("MAIN.TR", "صندوق التجارية الداخلي الموحد — سجل التجارية", "Asset", "Debit", "1110", Sponsors[2].Id, true),

        new("1120", "خزنة تحصيلات جاهز للكاش (SUP)", "Asset", "Debit", "1100"),
        new("SUP.BW.SET", "خزنة كاش جاهز — سجل البوابا", "Asset", "Debit", "1120", Sponsors[0].Id, true),
        new("SUP.EX.SET", "خزنة كاش جاهز — سجل إكسبرس", "Asset", "Debit", "1120", Sponsors[1].Id, true),
        new("SUP.TR.SET", "خزنة كاش جاهز — سجل التجارية", "Asset", "Debit", "1120", Sponsors[2].Id, true),

        new("1130", "حسابات البنوك الرسمية (BANK)", "Asset", "Debit", "1100"),
        new("BANK.BW", "حساب البنك — سجل البوابا", "Asset", "Debit", "1130", Sponsors[0].Id, true),
        new("BANK.EX", "حساب البنك — سجل إكسبرس", "Asset", "Debit", "1130", Sponsors[1].Id, true),
        new("BANK.TR", "حساب البنك — سجل التجارية", "Asset", "Debit", "1130", Sponsors[2].Id, true),

        new("1140", "مقاصة تحصيل الوقود بترو آب (PETRO)", "Asset", "Debit", "1100"),
        new("PETRO.BW", "مقاصة بترو آب — سجل البوابا", "Asset", "Debit", "1140", Sponsors[0].Id, true),
        new("PETRO.EX", "مقاصة بترو آب — سجل إكسبرس", "Asset", "Debit", "1140", Sponsors[1].Id, true),
        new("PETRO.TR", "مقاصة بترو آب — سجل التجارية", "Asset", "Debit", "1140", Sponsors[2].Id, true),

        // الذمم المدينة (طبيعتها: مدين Debit - تزيد بنشوء الدين وتنقص بالسداد أو الإعفاء)
        new("1200", "الذمم المدينة (حسابات المناديب والموظفين)", "Asset", "Debit", null),
        new("1210", "ذمم مدينة — مناديب (سلف وخدمات HR وحوادث)", "Asset", "Debit", "1200"),
        new("1220", "ذمم مدينة — موظفين إداريين (سلف وعهد)", "Asset", "Debit", "1200"),
        new("1230", "ذمم مدينة — مديونيات كاش المنصات المرحلة", "Asset", "Debit", "1200"),

        // حسابات المقاصة وجاري السجلات الشقيقة (عند شراء المديونية أو التحصيل نيابة عن سجل آخر)
        new("1300", "حسابات جاري السجلات الشقيقة والمقاصة", "InterCompany", "Debit", null),
        new("1310", "جاري السجلات الشقيقة (مقاصة بين البوابا وإكسبرس والتجارية)", "InterCompany", "Debit", "1300"),

        // الذمم الدائنة والمستحقات (طبيعتها: دائن Credit - تزيد باعتماد الرواتب وتنقص بالصرف أو استقطاع القسط)
        new("2100", "الذمم الدائنة ومستحقات المناديب والموظفين", "Liability", "Credit", null),
        new("2110", "ذمم دائنة — رواتب ومستحقات معتمدة", "Liability", "Credit", "2100"),
        new("2120", "ذمم دائنة — أمانات وتأمينات محتجزة", "Liability", "Credit", "2100"),

        // حساب أرصدة أول المدة والمديونيات المرحلة (طبيعته: دائن Credit - يُستخدم كطرف مقابل عند رفع رصيد أول المدة للمديونيات دون المساس برصيد الخزينة النقدي الحالي)
        new("3100", "حقوق الملكية وأرصدة أول المدة", "Equity", "Credit", null),
        new("3101", "حساب أرصدة أول المدة والمديونيات المرحلة", "Equity", "Credit", "3100"),

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

    // 3. كتالوج فئات المديونيات وربطها المباشر بحسابات المصروفات والذمم المدينة
    public static readonly CategoryMeta[] CategoriesCatalog =
    [
        // البنود الثمانية + السلف والعهد لقسم الموارد البشرية (HR)
        new("WorkPermit", "رخصة عمل (مكتب العمل)", "HR", "5101", "1210", 9600m),
        new("IqamaRenewal", "إصدار وتجديد الإقامة", "HR", "5102", "1210", 650m),
        new("SponsorshipTransfer", "نقل كفالة", "HR", "5103", "1210", 2000m),
        new("ExitReEntryVisa", "تأشيرة خروج وعودة (إصدار/تمديد)", "HR", "5104", "1210", 200m),
        new("MedicalInsurance", "تأمين طبي وكرت صحي", "HR", "5105", "1210", 1000m),
        new("ProfessionChange", "تغيير مهنة", "HR", "5106", "1210", 1000m),
        new("DriverLicense", "إصدار رخصة قيادة", "HR", "5107", "1210", 400m),
        new("DriverCard", "بطاقة سائق هيئة النقل", "HR", "5108", "1210", 250m),
        new("OtherHrService", "طلبات ورسوم HR أخرى", "HR", "5109", "1210", 0m),

        // السلف النقدية والبنكية والعهد
        new("Advance", "سلفة نقدية / بنكية", "Advances", "5109", "1210", 0m),
        new("CashAdvance", "سلفة نقدية", "Advances", "5109", "1210", 0m),
        new("BankAdvance", "سلفة بنكية", "Advances", "5109", "1210", 0m),
        new("CashCustody", "عهدة نقدية مقابل سند لأمر", "Advances", "5109", "1220", 15000m),

        // الحركة والأسطول ومخالفات تم
        new("TrafficFine", "مخالفة مرورية (تم / ساهر)", "Fleet", "5203", "1210", 0m),
        new("AccidentDamage", "تحمل حادث / تلفيات ورشة", "Fleet", "5201", "1210", 0m),
        new("VehicleInstallment", "قسط مركبة / دباب", "Fleet", "5201", "1210", 0m),
        new("EquipmentLoan", "عهدة جوال / شنطة / تجهيزات", "Fleet", "5201", "1210", 0m),
        new("FuelOverage", "فروقات استهلاك وقود وزيت", "Fleet", "5203", "1210", 0m),

        // المنصات والسكن والخصومات الإدارية
        new("PlatformTransferredDebt", "مديونية كاش منصة مرحلة (جاهز/شيفز/نينجا)", "Platforms", "5203", "1230", 0m),
        new("NegativeSalaryCarryover", "ترحيل راتب بالسالب (عجز مسير)", "Platforms", "5301", "1210", 0m),
        new("HousingDeduction", "استقطاع سكن وإيجارات", "Housing", "5110", "1210", 0m),
        new("AdministrativeDeduction", "جزاء / خصم إداري", "Admin", "5203", "1210", 0m)
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
        if(string.IsNullOrWhiteSpace(key))return null;
        string value=key.Trim();
        return Sponsors.FirstOrDefault(sp=>new[]{sp.Id,sp.Code,sp.UnifiedNumber,sp.CommercialReg,sp.NameAr,sp.ShortNameAr}.Any(x=>string.Equals(x,value,StringComparison.OrdinalIgnoreCase)))
            ?? Sponsors.FirstOrDefault(sp=>sp.Code=="EX"&&(value.Contains("اكسبرس")||value.Contains("إكسبرس")))
            ?? Sponsors.FirstOrDefault(sp=>sp.Code=="TR"&&(value.Contains("مؤسسة")&&value.Contains("تجاري")))
            ?? Sponsors.FirstOrDefault(sp=>sp.Code=="BW"&&value.Contains("شركة")&&value.Contains("بواب"));
    }
    public static SponsorDef ResolveSponsor(string? key)=>string.IsNullOrWhiteSpace(key)?Sponsors[0]:FindSponsor(key)??throw new ApiError("السجل التجاري غير معروف؛ راجع الربط قبل الترحيل.");
    public static AccountDef ResolveVault(string code){var account=ResolveAccount(code);J.Need(account.IsVault,"اختر صندوقًا أو حساب بنك صالحًا.");return account;}
    private static readonly InquiryLimits MobileLimits=new();
    private static string TokenHash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static JsonObject MobileSession(DbConnection c,HttpContext ctx,string iqama,DbTransaction? tx=null)
    {
        string header=ctx.Request.Headers.Authorization.ToString();
        if(!header.StartsWith("Bearer rmob_",StringComparison.Ordinal)||header.Length!=7+5+64)throw new ApiError("جلسة المندوب غير صالحة.",401);
        var session=Store.Get(c,"mobile:"+TokenHash(header[7..]),"mobile_session",tx);
        var rider=Store.Get(c,iqama,"rider",tx);
        if(session==null||session.S("iqama")!=iqama||!DateTimeOffset.TryParse(session.S("expiresAtUtc"),out var expiry)||expiry<=DateTimeOffset.UtcNow||rider==null||!rider.S("status").Equals("Active",StringComparison.OrdinalIgnoreCase))throw new ApiError("جلسة المندوب منتهية أو الحساب موقوف.",401);
        return session;
    }

    public static AccountDef ResolveAccount(string code) =>
        ChartOfAccounts.FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase))
        ?? throw new ApiError("رمز الحساب المالي أو الصندوق غير صالح: " + code);

    public static CategoryMeta ResolveCategory(string category) =>
        CategoriesCatalog.FirstOrDefault(c => string.Equals(c.Code, category, StringComparison.OrdinalIgnoreCase))
        ?? throw new ApiError("فئة المديونية أو الخدمة غير صالحة: " + category);

    public static string DefaultVaultForSponsor(string sponsorId, string kind = "MAIN")
    {
        var sp = ResolveSponsor(sponsorId);
        if (kind.Equals("BANK", StringComparison.OrdinalIgnoreCase)) return "BANK." + sp.Code;
        if (kind.Equals("SUP", StringComparison.OrdinalIgnoreCase)) return $"SUP.{sp.Code}.SET";
        if (kind.Equals("PETRO", StringComparison.OrdinalIgnoreCase)) return "PETRO." + sp.Code;
        return sp.Code == "TR" ? "MAIN.TR" : $"MAIN.{sp.Code}.SET";
    }

    public static JsonObject CreateJournalLine(string accountCode, string sponsorId, decimal debit, decimal credit, string note = "", string iqama = "", string debtId = "")
    {
        var acc = ResolveAccount(accountCode);
        var sp = ResolveSponsor(acc.DefaultSponsorId ?? sponsorId);
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
            l["lineNo"] = idx++;
            totalDebit = J.Money(totalDebit + l.N("debit"));
            totalCredit = J.Money(totalCredit + l.N("credit"));
        }
        J.Need(totalDebit > 0 && totalDebit == totalCredit, $"القيد المحاسبي غير متوازن: إجمالي المدين ({totalDebit:0.00}) لا يساوي إجمالي الدائن ({totalCredit:0.00}).");

        J.Need(!Store.List(c,"journal",tx).Any(j=>j.S("sourceRef")==reference),"مرجع القيد مسجل مسبقًا.");
        foreach(var group in lines.OfType<JsonObject>().GroupBy(l=>l.S("sponsorId")).ToList())
        {
            decimal net=J.Money(group.Sum(l=>l.N("debit")-l.N("credit")));
            if(net!=0)lines.Add(CreateJournalLine("1310",group.Key,net<0?-net:0m,net>0?net:0m,"مقاصة موازنة القيد بين السجلات",iqama,debtId));
        }
        idx=1;foreach(var line in lines.OfType<JsonObject>())line["lineNo"]=idx++;
        totalDebit=lines.OfType<JsonObject>().Sum(l=>l.N("debit"));totalCredit=lines.OfType<JsonObject>().Sum(l=>l.N("credit"));
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
        // 1. استعراض السجلات التجارية وأرصدتها المدينة والدائنة والخزائن
        api.MapGet("/finance/sponsors", () => store.Read(c =>
        {
            var journals = Store.List(c, "journal");
            var debts = Store.List(c, "debt");
            return Sponsors.Select(sp =>
            {
                var spLines = journals.SelectMany(j => j["lines"]?.AsArray() ?? []).Select(x => x!.AsObject()).Where(l => l.S("sponsorId") == sp.Id).ToList();
                decimal vaultBalance = spLines.Where(l => l.S("accountType") == "Asset" && !l.S("accountCode").StartsWith("12", StringComparison.Ordinal)).Sum(l => l.N("debit") - l.N("credit"));
                decimal receivableFromJournals = spLines.Where(l => l.S("accountCode").StartsWith("12", StringComparison.Ordinal)).Sum(l => l.N("debit") - l.N("credit"));
                decimal activeDebtRemaining = debts.Where(d => d.S("status") is "active" or "paused" && ResolveSponsor(d.S("creditorSponsorId", sp.Id)).Id == sp.Id).Sum(d => d.N("remainingBalance"));
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
                    receivablesBalance = J.Money(Math.Max(receivableFromJournals, activeDebtRemaining)),
                    activeDebtsCount = debts.Count(d => d.S("status") is "active" or "paused" && ResolveSponsor(d.S("creditorSponsorId", sp.Id)).Id == sp.Id),
                    payablesBalance = J.Money(payablesTotal),
                    expensesTotal = J.Money(expensesTotal),
                    waiversTotal = J.Money(waivesTotal),
                    interCompanyNet = J.Money(interCompanyNet)
                };
            }).ToList();
        }));

        // 2. شجرة الحسابات الجزئية وأرصدة الصناديق والبنوك والمصروفات
        api.MapGet("/finance/accounts", (string? sponsorId) => store.Read(c =>
        {
            var journals = Store.List(c, "journal");
            var allLines = journals.SelectMany(j => j["lines"]?.AsArray() ?? []).Select(x => x!.AsObject()).ToList();
            if (!string.IsNullOrWhiteSpace(sponsorId))
            {
                var targetSp = ResolveSponsor(sponsorId).Id;
                allLines = allLines.Where(l => l.S("sponsorId") == targetSp).ToList();
            }

            return ChartOfAccounts.Select(acc =>
            {
                var matching = allLines.Where(l => l.S("accountCode") == acc.Code).ToList();
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
                    isVault = acc.IsVault,
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

        // 3. كتالوج فئات المديونيات وخدمات الـ HR والأقسام والرتب
        api.MapGet("/finance/catalog", () => store.Read(c => new
        {
            sponsors = Sponsors,
            categories = CategoriesCatalog,
            vaults = ChartOfAccounts.Where(a => a.IsVault),
            departments = DepartmentsCatalog,
            promissoryNotesArchiveEnabled = PromissoryArchiveEnabled(c)
        }));

        // 4. سجل القيود المحاسبية المزدوجة (Journal Entries)
        api.MapGet("/finance/journals", (string? sponsorId, string? iqama, string? accountCode) => store.Read(c =>
        {
            var list = Store.List(c, "journal");
            if (!string.IsNullOrWhiteSpace(sponsorId))
            {
                var spId = ResolveSponsor(sponsorId).Id;
                list = list.Where(j => j.S("originSponsorId") == spId || j.S("creditorSponsorId") == spId || (j["lines"]?.AsArray().Any(l => l!.AsObject().S("sponsorId") == spId) ?? false)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(iqama)) list = list.Where(j => j.S("iqama") == iqama.Trim()).ToList();
            if (!string.IsNullOrWhiteSpace(accountCode)) list = list.Where(j => j["lines"]?.AsArray().Any(l => l!.AsObject().S("accountCode") == accountCode.Trim()) ?? false).ToList();
            return list;
        }));

        // 5. إنشاء قيد خزينة مباشر (إيداع / تمويل صندوق / تحويل بين الصناديق والبنوك)
        api.MapPost("/finance/vault-transfer", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            string fromVault = input.Required("fromVaultCode");
            string toVault = input.Required("toVaultCode");
            J.Need(fromVault != toVault, "اختر صندوقين مختلفين للتحويل.");
            decimal amount = Debts.Amount(input, "amount");
            string refNo = input.Required("sourceRef");
            J.Need(!Store.List(c, "journal", tx).Any(j => j.S("sourceRef") == refNo), "مرجع التحويل مسجل من قبل.");

            var fromAcc = ResolveVault(fromVault);
            var toAcc = ResolveVault(toVault);
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

        // 6. كشف حساب المندوب أو الموظف الشامل 360 درجة (مدين / دائن / رصيد تراكمي / حسب السجل)
        api.MapGet("/finance/statement/rider/{iqama}", (string iqama, string? sponsorId) => store.Read(c =>
        {
            J.Iqama(iqama);
            var rider = Store.Get(c, iqama, "rider");
            var spFilter = string.IsNullOrWhiteSpace(sponsorId) ? null : ResolveSponsor(sponsorId).Id;

            var debts = Store.List(c, "debt").Where(d => d.S("status") is "active" or "paused" or "settled").Where(d => d.S("iqama") == iqama && (spFilter == null || ResolveSponsor(d.S("creditorSponsorId")).Id == spFilter)).OrderBy(d => d.S("createdAt")).ToList();
            var ledgerRows = Store.List(c, "ledger").Where(l => l.S("iqama") == iqama).OrderBy(l => l.S("period")).ThenBy(l => l.S("createdAt")).ToList();
            var settlements = Store.List(c, "debt_settlement").Where(s => s.S("iqama") == iqama && (spFilter == null || s.S("creditorSponsorId") == spFilter)).OrderBy(s => s.S("createdAt")).ToList();

            // بناء كشف حساب مدين/دائن متسلسل الرصيد للمديونيات
            var statementLines = new List<JsonObject>();
            decimal runningDebtBalance = 0m;

            foreach (var d in debts)
            {
                decimal principal = d.N("totalAmount");
                if (principal <= 0) continue;
                var credSp = ResolveSponsor(d.S("creditorSponsorId"));
                runningDebtBalance = J.Money(runningDebtBalance + principal);
                statementLines.Add(new JsonObject
                {
                    ["date"] = d.S("createdAt").Length >= 10 ? d.S("createdAt")[..10] : d.S("effectiveFrom"),
                    ["period"] = d.S("effectiveFrom"),
                    ["type"] = "debt_created",
                    ["typeAr"] = "إثبات مديونية / استحقاق",
                    ["category"] = d.S("category"),
                    ["categoryAr"] = CategoriesCatalog.FirstOrDefault(x => x.Code == d.S("category"))?.NameAr ?? d.S("category"),
                    ["sponsorId"] = credSp.Id,
                    ["sponsorNameAr"] = credSp.ShortNameAr,
                    ["reference"] = d.S("sourceRef"),
                    ["description"] = d.S("notes", $"تسجيل مديونية ({d.S("category")})"),
                    ["debit"] = principal,
                    ["credit"] = 0m,
                    ["runningBalance"] = runningDebtBalance
                });
            }

            foreach(var debt in debts)
            {
                decimal recorded=settlements.Where(x=>x.S("debtId")==debt.S("id")).Sum(x=>x.N("collectedAmount")+x.N("waivedAmount"));
                decimal historical=J.Money(debt.N("totalAmount")-debt.N("remainingBalance")-recorded);
                if(historical<=0)continue;
                statementLines.Add(new JsonObject{["date"]=debt.S("createdAt"),["period"]=debt.S("effectiveFrom"),["type"]="historical_settlement",["typeAr"]="سداد سابق محفوظ من الرصيد",["reference"]=debt.S("sourceRef"),["sponsorNameAr"]=ResolveSponsor(debt.S("creditorSponsorId")).ShortNameAr,["debit"]=0m,["credit"]=historical});
            }
            foreach (var s in settlements)
            {
                decimal collected = s.N("collectedAmount");
                decimal waived = s.N("waivedAmount");
                var credSp = ResolveSponsor(s.S("creditorSponsorId"));
                if (collected > 0)
                {
                    runningDebtBalance = J.Money(runningDebtBalance - collected);
                    statementLines.Add(new JsonObject
                    {
                        ["date"] = s.S("collectionDate", s.S("createdAt").Length >= 10 ? s.S("createdAt")[..10] : ""),
                        ["period"] = s.S("period"),
                        ["type"] = s.S("method", "cash_collection"),
                        ["typeAr"] = s.S("method") == "payroll_deduction" ? "استقطاع قسط من مسير الراتب" : "تحصيل وسداد مالي",
                        ["category"] = s.S("category"),
                        ["categoryAr"] = $"سداد ({s.S("targetVaultCode")})",
                        ["sponsorId"] = credSp.Id,
                        ["sponsorNameAr"] = credSp.ShortNameAr,
                        ["reference"] = s.S("receiptRef"),
                        ["description"] = s.S("notes", "دفعة سداد مديونية"),
                        ["debit"] = 0m,
                        ["credit"] = collected,
                        ["runningBalance"] = runningDebtBalance
                    });
                }
                if (waived > 0)
                {
                    runningDebtBalance = J.Money(runningDebtBalance - waived);
                    statementLines.Add(new JsonObject
                    {
                        ["date"] = s.S("collectionDate", s.S("createdAt").Length >= 10 ? s.S("createdAt")[..10] : ""),
                        ["period"] = s.S("period"),
                        ["type"] = "administrative_waiver",
                        ["typeAr"] = "إعفاء وتسوية إدارية",
                        ["category"] = s.S("category"),
                        ["categoryAr"] = "مصروف إعفاءات إدارية",
                        ["sponsorId"] = credSp.Id,
                        ["sponsorNameAr"] = credSp.ShortNameAr,
                        ["reference"] = s.S("receiptRef") + "-W",
                        ["description"] = "تنازل وإعفاء إداري معتمد مقابل التسوية",
                        ["debit"] = 0m,
                        ["credit"] = waived,
                        ["runningBalance"] = runningDebtBalance
                    });
                }
            }

            statementLines=statementLines.OrderBy(x=>x.S("date"),StringComparer.Ordinal).ToList();
            runningDebtBalance=0m;foreach(var line in statementLines){runningDebtBalance=J.Money(runningDebtBalance+line.N("debit")-line.N("credit"));line["runningBalance"]=runningDebtBalance;}
            var bySponsor = Sponsors.Select(sp =>
            {
                var spDebts = debts.Where(d => ResolveSponsor(d.S("creditorSponsorId")).Id == sp.Id).ToList();
                return new
                {
                    sponsorId = sp.Id,
                    sponsorNameAr = sp.ShortNameAr,
                    totalPrincipal = J.Money(spDebts.Sum(d => d.N("totalAmount"))),
                    totalPaid = J.Money(spDebts.Sum(Debts.PaidAmount)),
                    totalWaived = J.Money(spDebts.Sum(d => d.N("waivedAmount"))),
                    remainingBalance = J.Money(spDebts.Sum(d => d.N("remainingBalance")))
                };
            }).ToList();

            return new
            {
                iqama,
                riderName = rider?.S("name") ?? "غير مسجل بالدليل",
                sponsorshipSponsor = rider?.S("sponsor") ?? "شركة البوابة المقبلة للتجارة",
                phone = rider?.S("phone") ?? "",
                city = rider?.S("city") ?? "",
                totalPrincipalDebt = J.Money(debts.Sum(d => d.N("totalAmount"))),
                totalPaidAmount = J.Money(debts.Sum(Debts.PaidAmount)),
                totalWaivedAmount = J.Money(debts.Sum(d => d.N("waivedAmount"))),
                totalRemainingDebt = J.Money(debts.Sum(d => d.N("remainingBalance"))),
                payrollLedgerBalance = J.Money(ledgerRows.Sum(l => l.N("amount"))),
                bySponsor,
                debts,
                statementLines,
                payrollLedgerRows = ledgerRows
            };
        }));

        // 7. منظومة الاعتمادات المتبادلة (المالية ⇄ HR ⇄ التشغيل ⇄ الحركة) ومركز الإشعارات والمهام
        api.MapGet("/approvals", (string? department, string? status) => store.Read(c =>
        {
            var list = Store.List(c, "approval_request");
            if (!string.IsNullOrWhiteSpace(department)) list = list.Where(a => a.S("targetDepartment") == department || a.S("fromDepartment") == department).ToList();
            if (!string.IsNullOrWhiteSpace(status)) list = list.Where(a => a.S("status") == status).ToList();
            return list;
        }));

        api.MapPost("/approvals/{id}/decide", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            var req = Store.Get(c, id, "approval_request", tx) ?? throw new ApiError("طلب الاعتماد غير موجود.", 404);
            J.Need(req.S("status") == "pending" || req.S("status") == "suspended", "تم البت في هذا الطلب مسبقاً.");
            string decision = input.Required("decision"); // approved | rejected | suspended
            J.Need(decision is "approved" or "rejected" or "suspended", "قرار غير صالح: استخدم approved أو rejected أو suspended.");
            string note = input.S("note");
            if (decision is "rejected" or "suspended") J.Need(!string.IsNullOrWhiteSpace(note), "يلزم تدوين سبب الرفض أو التعليق.");

            req["status"] = decision;
            req["decisionNote"] = note;
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

        api.MapGet("/notifications/center", (string? tab, string? department) => store.Read(c =>
        {
            var items = Store.List(c, "sys_notification");
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
        app.MapPost("/api/rider-mobile/login", async Task<IResult> (JsonObject input,HttpContext ctx)=>
        {
            string iqama=input.S("iqamaNo");
            if(!await MobileLimits.Allowed(ctx.Connection.RemoteIpAddress?.ToString()??"unknown",TokenHash(iqama),ctx.RequestAborted))throw new ApiError("حاول مرة أخرى بعد قليل.",429);
            return store.Write<IResult>((c,tx)=>
            {
                if(!InquiryApi.ConsumeAccess(c,tx,iqama,input.S("phoneLast4"),input.S("code"),"mobile"))throw new ApiError("تعذر التحقق من بيانات الدخول أو انتهت صلاحية الرمز.",401);
                var rider=Store.Get(c,iqama,"rider",tx)!;
                if(!rider.S("status").Equals("Active",StringComparison.OrdinalIgnoreCase))throw new ApiError("بيانات الدخول غير صحيحة أو الحساب غير نشط.",401);
                string token="rmob_"+CampaignLinks.NewToken(),lang=input.S("language","ar");
                if(lang is not ("ar" or "en" or "ur" or "bn"))lang="ar";
                string fcm=input.S("fcmDeviceToken");J.Need(fcm.Length<=4096,"رمز جهاز طويل جدًا.");
                var session=new JsonObject{["id"]="mobile:"+TokenHash(token),["iqama"]=iqama,["language"]=lang,["fcmDeviceToken"]=fcm,["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["expiresAtUtc"]=DateTimeOffset.UtcNow.AddHours(8).ToString("O")};
                Store.Save(c,"mobile_session",session,tx);Store.Audit(c,tx,"mobile.login",iqama,"rider");
                return Results.Ok(new{accessToken=token,tokenType="Bearer",expiresAtUtc=session.S("expiresAtUtc"),language=lang,fcmRegistered=false,fcmTokenStored=fcm.Length>0,rider=UserAccess.RiderView(rider)});
            },false);
        }).RequireRateLimiting("sensitive");

        api.MapPost("/rider-mobile/broadcast", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            string category = input.S("category", "operational"); // financial | operational | administrative | form_link
            string titleAr = input.Required("titleAr");
            string bodyAr = input.Required("bodyAr");
            string targetIqama = input.S("iqama", "ALL");
            string actionUrl = input.S("actionUrl");
            J.Need(string.IsNullOrWhiteSpace(actionUrl)||(Uri.TryCreate(actionUrl,UriKind.Absolute,out var action)&&action.Scheme=="https")||(actionUrl.StartsWith("/")&&!actionUrl.StartsWith("//")),"رابط الإشعار غير صالح.");
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

        api.MapGet("/rider-mobile/broadcasts", () => store.Read(c => Store.List(c, "mobile_broadcast")));

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
            MobileSession(c,ctx,iqama);

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
            MobileSession(c,ctx,iqama,tx);
            var b = Store.Get(c, id, "mobile_broadcast", tx) ?? throw new ApiError("الإشعار غير موجود.", 404);
            J.Need(b.S("targetIqama")=="ALL"||b.S("targetIqama")==iqama,"الإشعار غير متاح لهذا المندوب.");
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

        api.MapGet("/promissory-notes", () => store.Read(c => new
        {
            featureEnabled = PromissoryArchiveEnabled(c),
            items = Store.List(c, "promissory_note")
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
