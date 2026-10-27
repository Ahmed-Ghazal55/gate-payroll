using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;

namespace Payroll;

public static partial class RiderStatements
{
    private static readonly string[] SimpleBuckets = ["earned", "deducted", "received", "transferred", "debt_added", "debt_collected", "jahez_cash", "other"];
    private static readonly string[] StatementPlatforms = ["Jahez", "Keeta", "HungerStation", "Ninja", "Other"];
    private static readonly HashSet<string> SimpleTransactionTypes = new(StringComparer.Ordinal)
    {
        "payroll_accrual", "payroll_deduction", "payroll_payment", "intercompany_debt_transfer",
        "opening_balance_debt", "debt_accrual", "company_expense", "debt_creation", "debt_recognition", "supplier_payment",
        "cash_collection", "collection_with_waiver", "administrative_waiver", "bulk_collection",
        "legacy_debt_opening", "reviewed_opening", "legacy_cash_allocation", "vault_transfer",
        "jahez_account_fee", "jahez_custody_transfer", "jahez_platform_settlement", "company_fee_deposit", "jahez_eod_handover"
    };

    // Only posted documents are projected. No ledger/debt balances are manufactured as journals.
    public static JsonObject BuildSimple(DbConnection c, string iqama, string from, string to, string? sponsorId, ClaimsPrincipal user)
    {
        iqama = J.Iqama(iqama); from = J.Period(from); to = J.Period(to);
        J.Need(string.CompareOrdinal(from, to) <= 0, "شهر البداية يجب ألا يتجاوز شهر النهاية.");
        string? sponsor = string.IsNullOrWhiteSpace(sponsorId) ? null : FinancialCore.ResolveSponsor(sponsorId).Id;
        if (sponsor != null) FinancialScopes.DemandSponsor(user, sponsor);
        bool admin = AccessPolicy.Administrator(user), payrollVisible = AccessPolicy.Has(user, "payroll.view");
        bool Owner(string owner) => FinancialScopes.Sponsor(user, owner) && (sponsor == null || owner == sponsor);
        bool Platform(string platform) => platform == "" || admin || user.HasClaim("finance.platforms", platform);
        bool AllPlatforms() => StatementPlatforms.All(Platform);
        var rider = Store.Get(c, iqama, "rider");
        var debts = Store.List(c, "debt").Where(d => d.S("iqama") == iqama).ToDictionary(d => d.S("id"));
        bool DebtVisible(JsonObject debt) => FinancialScopes.Category(user, debt.S("category")) && Platform(debt.S("platform"));
        var runs = Store.List(c, "run").Where(r => r.S("iqama") == iqama).ToList();
        var journals = Store.List(c, "journal").Where(j => j.S("status", "posted") == "posted").ToList();
        var rows = new List<JsonObject>();
        foreach (var journal in journals)
        {
            string period = journal.S("period");
            // Posted journals use a validated accounting period, not the wall-clock creation month.
            // Keep malformed historical periods visible as an explicit error, rather than losing lines.
            var sourceLines = (journal["lines"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
            if (!sourceLines.Any(l => l.S("iqama") == iqama && Owner(l.S("sponsorId")))) continue;
            string type = journal.S("transactionType");
            var run = runs.FirstOrDefault(r => r.S("journalEntryId") == journal.S("id") || journal.S("sourceRef") == "PAYROLL:" + r.S("id"));
            for (int index = 0; index < sourceLines.Count; index++)
            {
                var line = sourceLines[index];
                if (line.S("iqama") != iqama || !Owner(line.S("sponsorId"))) continue;
                string code = line.S("accountCode");
                string debtId = FirstNonEmpty(line.S("debtId"), journal.S("debtId"));
                debts.TryGetValue(debtId, out var debt);
                if (debt != null && !DebtVisible(debt)) continue;
                if (debtId != "" && debt == null && !admin) continue; // Cannot infer a missing debt's category or platform.
                if ((code is "2110" or "1211" || type is "payroll_accrual" or "payroll_payment") && !payrollVisible) continue;
                string category = FirstNonEmpty(debt.S("category"), line.S("category"), journal.S("category"));
                if (category != "" && !FinancialScopes.Category(user, category)) continue;
                if (debt == null && code is "1210" or "1230" && category == "" && !admin &&
                    !FinancialCore.CategoriesCatalog.All(cat => FinancialScopes.Category(user, cat.Code))) continue;
                string platform = FirstNonEmpty(debt.S("platform"), line.S("platform"), journal.S("platform"));
                if (platform == "" && (code is "1240" or "2140" || code.StartsWith("SUP.", StringComparison.Ordinal) ||
                    code.EndsWith(".JAHEZ", StringComparison.Ordinal) || type.StartsWith("jahez_", StringComparison.Ordinal))) platform = "Jahez";
                if (platform != "" && !Platform(platform)) continue;
                if (platform == "" && debt == null && !admin)
                {
                    var platforms = (run?["sourceRows"] as JsonArray)?.OfType<JsonObject>().Select(r => r.S("platform")).Where(p => p != "").Distinct().ToList() ?? [];
                    // A mixed or unlabelled posted line cannot be attributed to just the permitted platform.
                    if (platforms.Count > 0 ? platforms.Any(p => !Platform(p)) : !AllPlatforms()) continue;
                }
                J.Period(period);
                if (string.CompareOrdinal(period, to) > 0) continue;
                bool control = code is "2110" or "1210" or "1211" or "1230";
                bool jahezControl = code == "1230" && platform == "Jahez";
                bool jahez = jahezControl || code == "2140" || code.StartsWith("SUP.", StringComparison.Ordinal) && code.EndsWith(".SET", StringComparison.Ordinal) ||
                    code.EndsWith(".JAHEZ", StringComparison.Ordinal) || type is "jahez_custody_transfer" or "jahez_platform_settlement" or "jahez_eod_handover";
                decimal amount = J.Money(line.N("credit") - line.N("debit"));
                string bucket = SimpleBucket(type, code, amount, jahez, line.S("note"));
                string description = FirstNonEmpty(line.S("note"), journal.S("description"), "حركة أخرى تحتاج مراجعة الوصف");
                string date = FirstNonEmpty(journal.S("date"), journal.S("createdAt"));
                rows.Add(new JsonObject
                {
                    ["date"] = date.Length >= 10 ? date[..10] : date, ["period"] = period,
                    ["bucket"] = bucket, ["labelAr"] = SimpleLabel(bucket, type, code, amount, description), ["description"] = description,
                    ["amount"] = amount, ["netAmount"] = control ? amount : 0m, ["affectsNet"] = control,
                    ["salaryAmount"] = code is "2110" or "1211" ? amount : 0m,
                    ["jahezCashAmount"] = jahezControl ? amount : 0m,
                    ["journalId"] = journal.S("id"), ["lineNumber"] = index + 1, ["transactionType"] = type,
                    ["sponsorId"] = line.S("sponsorId"), ["sponsorNameAr"] = FinancialCore.FindSponsor(line.S("sponsorId"))?.ShortNameAr ?? "",
                    ["legacyEvidenceIncomplete"] = line.B("legacyEvidenceIncomplete") || journal.B("legacyEvidenceIncomplete") || debt != null && (debt.B("legacyEvidenceIncomplete") || !AccountingEvents.HasDebtOpening(debt))
                });
            }
        }
        rows = rows.OrderBy(r => r.S("period"), StringComparer.Ordinal).ThenBy(r => r.S("date"), StringComparer.Ordinal)
            .ThenBy(r => r.S("journalId"), StringComparer.Ordinal).ThenBy(r => r.N("lineNumber")).ToList();
        var legacy = new JsonArray();
        foreach (var debt in debts.Values.Where(d => d.S("status") is "active" or "paused" or "settled" && DebtVisible(d) && (!AccountingEvents.HasDebtOpening(d) || d.B("legacyEvidenceIncomplete"))))
        {
            string owner = debt.S("creditorSponsorId"); // This warning describes the current legacy record, not historical ownership.
            if (!Owner(owner) && !(sponsor == null && owner == "" && admin)) continue;
            legacy.Add(new JsonObject
            {
                ["debtId"] = debt.S("id"), ["sponsorId"] = owner, ["remainingBalance"] = debt.N("remainingBalance"),
                ["legacyEvidenceIncomplete"] = true, ["labelAr"] = "دين قديم يحتاج مطابقة؛ غير مضاف إلى صافي القيود المثبتة"
            });
        }
        if (rows.Count == 0 && legacy.Count == 0 && !(rider != null && (FinancialScopes.Rider(user, rider) || FinancialScopes.AllCompanies(user))))
            throw new ApiError("المندوب غير موجود أو خارج النطاق المالي للمستخدم.", 403);
        var before = rows.Where(r => string.CompareOrdinal(r.S("period"), from) < 0).ToList();
        var details = rows.Where(r => string.CompareOrdinal(r.S("period"), from) >= 0).ToList();
        decimal opening = J.Money(before.Sum(r => r.N("netAmount"))), balance = opening;
        var months = new JsonArray();
        var grouped = details.GroupBy(r => r.S("period")).ToDictionary(g => g.Key, g => g.ToList());
        var month = DateOnly.ParseExact(from + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        while (true)
        {
            string key = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var monthRows = grouped.GetValueOrDefault(key) ?? [];
            decimal movement = J.Money(monthRows.Sum(r => r.N("netAmount")));
            months.Add(new JsonObject { ["period"] = key, ["openingBalance"] = balance, ["totals"] = SimpleTotals(monthRows), ["netMovement"] = movement, ["closingNet"] = J.Money(balance + movement) });
            balance = J.Money(balance + movement);
            if (key == to) break;
            month = month.AddMonths(1);
        }
        bool incomplete = legacy.Count > 0 || rows.Any(r => r.B("legacyEvidenceIncomplete"));
        return new JsonObject
        {
            ["rider"] = new JsonObject { ["iqama"] = iqama, ["name"] = rider.S("name", "غير مسجل"), ["city"] = rider.S("city"), ["status"] = rider.S("status") },
            ["from"] = from, ["to"] = to, ["sponsorId"] = sponsor, ["currency"] = "SAR", ["balanceBasis"] = "posted_journal_lines",
            ["openingBalance"] = opening, ["months"] = months, ["details"] = new JsonArray(details.Select(r => (JsonNode?)r).ToArray()),
            ["periodTotals"] = SimpleTotals(details), ["netMovement"] = J.Money(details.Sum(r => r.N("netAmount"))), ["closingNet"] = balance,
            ["closingSalaryNet"] = J.Money(rows.Sum(r => r.N("salaryAmount"))),
            ["openingJahezCashNet"] = J.Money(before.Sum(r => r.N("jahezCashAmount"))), ["closingJahezCashNet"] = J.Money(rows.Sum(r => r.N("jahezCashAmount"))),
            ["closingOtherDebtNet"] = J.Money(rows.Sum(r => r.N("netAmount") - r.N("salaryAmount") - r.N("jahezCashAmount"))),
            ["closingLabelAr"] = balance > 0 ? "مستحق لك لدى الشركة" : balance < 0 ? "مستحق عليك للشركة" : "لا يوجد رصيد متبقٍ",
            ["legacyEvidenceIncomplete"] = incomplete,
            ["historyCoverage"] = incomplete ? "legacy_requires_review" : "journal_events", ["legacyUnverifiedDebts"] = legacy,
            ["transactionTypes"] = new JsonArray(details.GroupBy(r => r.S("transactionType")).Select(g => (JsonNode?)new JsonObject
            {
                ["transactionType"] = g.Key, ["buckets"] = new JsonArray(g.Select(r => r.S("bucket")).Distinct().Order(StringComparer.Ordinal).Select(b => (JsonNode?)JsonValue.Create(b)).ToArray())
            }).ToArray())
        };
    }

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => v != "") ?? "";
    private static JsonObject SimpleTotals(IEnumerable<JsonObject> rows)
    {
        var totals = new JsonObject();
        foreach (string bucket in SimpleBuckets) totals[bucket] = J.Money(rows.Where(r => r.S("bucket") == bucket).Sum(r => r.N("netAmount")));
        return totals;
    }
    private static string SimpleBucket(string type, string code, decimal amount, bool jahez, string note)
    {
        if (!SimpleTransactionTypes.Contains(type)) return "other";
        if (type == "intercompany_debt_transfer" || code == "1310") return "transferred";
        if (jahez) return "jahez_cash";
        if (code == "2110")
        {
            if (type == "payroll_payment") return "received";
            if (type == "payroll_deduction") return "deducted";
            if (type != "payroll_accrual") return "other";
            // Deficit reclassification/offset pairs are not additional earnings or deductions.
            if (note == "فصل الذمة المدينة عن الرواتب الدائنة") return "debt_added";
            if (note == "مقاصة عجز راتب سابق موثق") return "debt_collected";
            return amount > 0 ? "earned" : "deducted";
        }
        if (code is "1210" or "1211" or "1230") return amount < 0 ? "debt_added" : "debt_collected";
        return "other"; // Preserve company counterpart lines without counting them a second time.
    }
    private static string SimpleLabel(string bucket, string type, string code, decimal amount, string description) => bucket switch
    {
        "earned" => "مستحقات عمل معتمدة", "deducted" => "خصم من مستحقاتك",
        "received" => "مبلغ استلمته من الشركة", "transferred" => "نقل بين الشركات",
        "debt_added" => "مبلغ أضيف إلى مديونيتك", "debt_collected" => type is "administrative_waiver" or "collection_with_waiver" ? "تخفيض المديونية بسداد أو إعفاء معتمد" : "تخفيض مديونيتك بسداد أو تسوية",
        "jahez_cash" => code == "1230" ? amount < 0 ? "كاش جاهز المطلوب تسليمه — منفصل عن الراتب" : "تسليم أو تسوية كاش جاهز — منفصل عن الراتب" : "حركة أموال جاهز — لا تغير صافي راتبك",
        _ => !SimpleTransactionTypes.Contains(type) || code is "2110" or "1210" or "1211" or "1230" ? description : "تفصيل مقابل لحركة الشركة — لا يغير رصيدك"
    };
}
