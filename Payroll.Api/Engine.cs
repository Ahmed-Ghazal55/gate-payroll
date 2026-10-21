using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Payroll;

public sealed class ApiError(string message, int status = 400) : Exception(message) { public int Status { get; } = status; }
public static class J
{
    public static string S(this JsonNode? n, string key, string fallback = "") => n?[key]?.ToString()?.Trim() ?? fallback;
    public static decimal N(this JsonNode? n, string key, decimal fallback = 0) => decimal.TryParse(n.S(key), NumberStyles.Number, CultureInfo.InvariantCulture, out var x) ? x : fallback;
    public static bool B(this JsonNode? n, string key) => bool.TryParse(n.S(key), out var b) && b;
    public static bool? NullableBool(this JsonNode? n, string key) => bool.TryParse(n.S(key), out var b) ? b : null;
    public static JsonObject Clone(this JsonObject x) => (JsonObject)x.DeepClone();
    public static string Required(this JsonNode n, string key) => string.IsNullOrWhiteSpace(n.S(key)) ? throw new ApiError($"الحقل مطلوب: {key}") : n.S(key);
    public static void Need(bool ok, string message) { if (!ok) throw new ApiError(message); }
    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static string Digits(string s) => string.Concat(s.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c is >= '۰' and <= '۹' ? (char)('0' + c - '۰') : c));
    public static string Iqama(string s) { s = Digits(s.Trim()); Need(Regex.IsMatch(s, "^[12][0-9]{9}$"), "الإقامة/الهوية يجب أن تكون 10 أرقام وتبدأ بـ1 أو2؛ لا تستخدم هوية صاحب الحساب بدل المندوب الفعلي."); return s; }
    public static string Period(string s) { Need(DateOnly.TryParseExact(s + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), "الشهر يجب أن يكون بصيغة yyyy-MM"); return s; }
    public static string Phone(string s) { s = Regex.Replace(Digits(s), @"[\s+()-]", ""); if (s.StartsWith("00966")) s = s[2..]; if (s.StartsWith("05")) s = "966" + s[1..]; Need(Regex.IsMatch(s, "^9665[0-9]{8}$"), "أدخل رقم جوال سعودي صحيحًا."); return s; }
    public static string Iban(string s)
    {
        s = Regex.Replace(Digits(s).ToUpperInvariant(), @"[\s-]", "");
        Need(Regex.IsMatch(s, "^SA[0-9]{22}$"), "الآيبان السعودي يجب أن يكون SA ثم22رقمًا.");
        var rearranged = s[4..] + s[..4]; int remainder = 0;
        foreach (char c in rearranged) foreach (char digit in char.IsAsciiLetter(c) ? ((int)c - 55).ToString(CultureInfo.InvariantCulture) : c.ToString()) remainder = (remainder * 10 + digit - '0') % 97;
        Need(remainder == 1, "الآيبان لم يجتز فحص MOD97."); return s;
    }
}

public record WorkLine(string SourceRef, string Iqama, string Name, string Platform, string AccountId, string Period, string From, string To, int Orders, decimal? PlatformRevenue, decimal? RiderPay, bool? IsValid, string Company, string City, bool DatesConfirmed = false);
public record PayLine(string Category, string Description, decimal Amount, string Source);
public record PayrollResult(string Iqama, string Period, string PolicyName, decimal OpeningBalance, decimal PeriodMovement, decimal ClosingBalance, decimal Payable, decimal Debt, int Orders, IReadOnlyList<PayLine> Lines, IReadOnlyList<string> Blockers);

public static class Engine
{
    public static PayrollResult Calculate(string iqama, string period, JsonObject? policy, List<WorkLine> rows, List<JsonObject> adjustments, decimal opening, List<JsonObject>? debts = null)
    {
        var errors = new List<string>(); var lines = new List<PayLine>(); int orders = rows.Sum(x => x.Orders);
        if (rows.Count == 0) errors.Add("لا توجد سجلات تشغيل مستوردة للمندوب في هذه الفترة.");
        if (policy == null || policy.S("status") != "approved") errors.Add("لم تُعتمد سياسة احتساب للمندوب.");
        if (policy != null && string.CompareOrdinal(policy.S("effectiveFrom"), period) > 0) errors.Add("السياسة لم تدخل حيز السريان في هذا الشهر.");
        if (policy != null && policy.S("platform") != "all" && rows.Any(x => x.Platform != policy.S("platform"))) errors.Add("السياسة لا تغطي جميع المنصات المستوردة لهذا المندوب؛ افصل نطاق الحساب أو عيّن سياسة تغطيه.");
        if (rows.Any(x => x.Iqama != iqama || x.Period != period)) errors.Add("تعارض في هوية المندوب أو شهر سجلات التشغيل.");
        var config = policy?["config"] as JsonObject ?? new();
        switch (policy?.S("kind"))
        {
            case "commission":
                foreach (var row in rows)
                    if (row.PlatformRevenue is null) errors.Add($"إيراد التسوية الفعلي مفقود: {row.SourceRef}");
                    else lines.Add(new("platform_settlement", "صافي استحقاق المنصة الفعلي قبل تحملات الشركة", J.Money(row.PlatformRevenue.Value), row.SourceRef));
                lines.Add(new("company_fee", "عمولة الشركة الشهرية مرة واحدة عن الفترة", -J.Money(config.N("companyFee")), policy.S("id")));
                break;
            case "approved_pay":
                foreach (var row in rows)
                    if (row.RiderPay is null) errors.Add($"استحقاق المندوب المعتمد مفقود: {row.SourceRef}");
                    else lines.Add(new("approved_pay", "استحقاق مندوب من مصدر مالي معتمد", J.Money(row.RiderPay.Value), row.SourceRef));
                break;
            case "tiers":
                if (rows.Any(x => !x.DatesConfirmed)) errors.Add("يلزم ربط تاريخي بداية ونهاية العمل من المصدر لحساب الشرائح؛ الشهر الافتراضي ليس دليل حضور.");
                if (rows.Any(x => x.IsValid != true)) errors.Add("الشرائح تحتاج صلاحية تشغيل مؤكدة؛ عدد الطلبات وحده لا يثبت الصلاحية، والحالة غير الصالحة تحتاج تسوية معتمدة.");
                if (rows.Any(x => x.From != period + "-01" || x.To != new DateOnly(int.Parse(period[..4]), int.Parse(period[5..]), DateTime.DaysInMonth(int.Parse(period[..4]), int.Parse(period[5..]))).ToString("yyyy-MM-dd"))) errors.Add("احتساب الشرائح لفترة جزئية يحتاج سياسة تقسيم شهر معتمدة؛ لم يُطبق تناسب تلقائي.");
                var tiers = (config["tiers"] as JsonArray ?? new()).OfType<JsonObject>().OrderByDescending(t => t.N("minOrders")).ToList();
                var tier = tiers.FirstOrDefault(t => orders >= t.N("minOrders"));
                if (tier == null) errors.Add("لا توجد شريحة معتمدة لهذا العدد من الطلبات.");
                else
                {
                    lines.Add(new("base_salary", "راتب الشريحة المعتمدة للشركة", J.Money(tier.N("salary")), policy!.S("id")));
                    lines.Add(new("extra_orders", "طلبات إضافية فوق بداية الشريحة", J.Money((orders - tier.N("minOrders")) * tier.N("extraRate")), policy.S("id")));
                    lines.Add(new("fuel_allowance", "بدل وقود نقدي حسب السياسة", J.Money(tier.N("fuelAllowance")), policy.S("id")));
                    lines.Add(new("internet_allowance", "بدل اتصالات معتمد", J.Money(config.N("internetAllowance")), policy.S("id")));
                }
                break;
            default: if (policy != null) errors.Add("نوع سياسة غير مدعوم."); break;
        }
        foreach (var a in adjustments) lines.Add(new(a.S("category"), a.S("description"), J.Money(a.N("amount")), a.S("id")));
        // Preview is pure. Balances are changed only by Debts.Post inside payroll approval.
        decimal available = Math.Max(0, J.Money(opening + lines.Sum(x => x.Amount)));
        foreach (var debt in (debts ?? []).Where(d => d.S("status") == "active" && d.S("iqama") == iqama && string.CompareOrdinal(d.S("effectiveFrom", "0000-00"), period) <= 0 && string.CompareOrdinal(d.S("lastDeductionPeriod"), period) < 0).OrderBy(d => d.S("createdAt")).ThenBy(d => d.S("id")))
        {
            decimal amount = J.Money(Math.Min(available, Math.Min(debt.N("remainingBalance"), debt.N("monthlyInstallment"))));
            if (amount <= 0) continue;
            lines.Add(new("debt_installment", "قسط دين وفق اتفاق الاستقطاع", -amount, debt.S("id")));
            available -= amount;
        }
        var movement = J.Money(lines.Sum(x => x.Amount)); var closing = J.Money(opening + movement);
        return new(iqama, period, policy?.S("name") ?? "غير محددة", opening, movement, closing, Math.Max(0, closing), Math.Max(0, -closing), orders, lines, errors.Distinct().ToList());
    }
}
