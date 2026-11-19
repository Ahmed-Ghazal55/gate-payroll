using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Payroll;

namespace ErpImport;

// D1 steps 1–6 only. No Store construction, planned documents, or write path.
public static partial class DebtSheetImporter
{
    public sealed class ReviewRow(int number, string identity, List<string> cells)
    {
        public int Number { get; } = number;
        internal string Identity { get; } = identity;
        internal List<string> Cells { get; } = cells;
        public HashSet<string> Reasons { get; } = [];
        public string Creditor { get; internal set; } = "blocked";
        public string CreditorSource { get; internal set; } = "blocked";
        public bool CrossCompany { get; internal set; }
        public decimal Net { get; internal set; }
        internal decimal SourceNet { get; set; }
        internal decimal[] Buckets { get; set; } = [];
        internal bool FinanciallyReadable { get; set; }
        internal bool MissingCache { get; set; }
    }

    public sealed record ReviewInput(byte[] Bytes, List<Imports.Sheet> Sheets, string Sha256, DateTimeOffset? Modified);
    public sealed record ArchiveEvidence(string Sheet, int Row, string Company, string Identity, decimal? Net, bool MissingCache);
    public sealed record OctoberRequest(string Sheet, int Row, string Identity, string Reference, string Company,
        decimal Amount, DateOnly? Date, bool MissingCache);
    public sealed record OctoberDecision(string Classification, string[] Evidence);
    public sealed record ThroughSixReview(JsonObject Summary, JsonObject PrivateReport);

    // Snapshot the bytes once: hashes, cached values, and formula-cache checks share the exact input.
    public static ReviewInput FreezeInput(byte[] bytes)
    {
        var copy = bytes.ToArray();
        var sheets = Imports.Read(copy, "review.xlsx");
        using var zip = new ZipArchive(new MemoryStream(copy));
        DateTimeOffset? modified = null;
        if (zip.GetEntry("docProps/core.xml") is { } core)
        {
            using var stream = core.Open();
            string? value = XElement.Load(stream).Elements().FirstOrDefault(x => x.Name.LocalName == "modified")?.Value;
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) modified = date;
        }
        return new(copy, sheets, Hash(copy), modified);
    }

    public static string NormalizeReviewIdentity(string value)
    {
        // Cached numeric XLSX identities may use exponent notation. Expand exactly before
        // stripping formatting, otherwise exponent digits corrupt the identity and every join.
        string digits = Identity(value.Trim());
        if (Regex.IsMatch(digits, @"^\d+\.0+$")) digits = digits[..digits.IndexOf('.')];
        return new string(digits.Where(char.IsAsciiDigit).ToArray());
    }

    static bool ValidReviewIdentity(string value) => Regex.IsMatch(value, "^[0-9]{10}$");

    public static List<ReviewRow> ReviewIdentities(ReviewInput input, string sheetName)
    {
        var sheet = Sheet(input.Sheets, sheetName);
        int id = ReviewHeader(sheet, "رقم الإقامة");
        var rows = sheet.Rows.Skip(1).Select((r, i) => new ReviewRow(i + 2, NormalizeReviewIdentity(Cell(r, id)), r))
            .Where(r => Cell(r.Cells, id) != "").ToList();
        var duplicate = rows.GroupBy(r => r.Identity).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var missing = MissingFormulaCache(input.Bytes, sheetName);
        // A formula-only identity is still a data row even when its cache is absent.
        foreach (int n in missing.Where(n => n > 1 && rows.All(r => r.Number != n)))
            rows.Add(new(n, "", sheet.Rows[n - 1]));
        foreach (var row in rows)
        {
            if (!ValidReviewIdentity(row.Identity)) row.Reasons.Add("iqama_invalid");
            if (duplicate.Contains(row.Identity)) row.Reasons.Add("iqama_duplicate");
            row.MissingCache = missing.Contains(row.Number);
            if (row.MissingCache) row.Reasons.Add("formula_without_cached_value");
        }
        return rows.OrderBy(r => r.Number).ToList();
    }

    public static void ReviewIntegrity(List<ReviewRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.MissingCache) continue;
            try
            {
                row.Buckets = Enumerable.Range(Column("E"), Column("AM") - Column("E") + 1).Select(c => Money(Cell(row.Cells, c))).ToArray();
                var payments = Enumerable.Range(Column("AO"), Column("BE") - Column("AO") + 1).Select(c => Money(Cell(row.Cells, c))).ToArray();
                if (row.Buckets.Any(x => x < 0)) row.Reasons.Add("negative_bucket");
                if (payments.Any(x => x < 0)) row.Reasons.Add("negative_payment");
                foreach (string col in new[] { "AN", "BF", "BG" })
                    if (Cell(row.Cells, Column(col)) == "") row.Reasons.Add("financial_total_missing");
                decimal gross = Money(Cell(row.Cells, Column("AN"))), paid = Money(Cell(row.Cells, Column("BF")));
                row.Net = Money(Cell(row.Cells, Column("BG")));
                row.SourceNet = decimal.Parse(Cell(row.Cells, Column("BG")) == "" ? "0" : Cell(row.Cells, Column("BG")), NumberStyles.Number, CultureInfo.InvariantCulture);
                if (!Close(row.Buckets.Sum(), gross)) row.Reasons.Add("bucket_total_mismatch");
                if (!Close(payments.Sum(), paid)) row.Reasons.Add("deduction_total_mismatch");
                if (!Close(gross - paid, row.Net)) row.Reasons.Add("net_mismatch");
                row.FinanciallyReadable = true;
            }
            catch (ImportFailure) { row.Reasons.Add("invalid_numeric_cell"); }
        }
    }

    public static List<ArchiveEvidence> ReviewArchives(ReviewInput input, List<ReviewRow> rows, JsonArray issues)
    {
        var evidence = new List<ArchiveEvidence>();
        var main = rows.GroupBy(r => r.Identity).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var (name, company) in new[] { ("اكسبرس الرياض - ارشيف", "EX"), ("التجارية الرياض - ارشيف", "TR") })
        {
            var sheet = Sheet(input.Sheets, name);
            int id = ReviewHeader(sheet, "رقم الإقامة"), net = ReviewHeader(sheet, "الصافي");
            var missing = MissingFormulaCache(input.Bytes, name);
            for (int i = 1; i < sheet.Rows.Count; i++)
            {
                var cells = sheet.Rows[i];
                if (Cell(cells, id) == "" && !missing.Contains(i + 1)) continue;
                string identity = NormalizeReviewIdentity(Cell(cells, id));
                decimal? amount = null;
                if (!missing.Contains(i + 1) && Cell(cells, net) != "")
                    try { amount = Money(Cell(cells, net)); } catch (ImportFailure) { /* Explicit issue below. */ }
                var item = new ArchiveEvidence(name, i + 1, company, identity, amount, missing.Contains(i + 1));
                evidence.Add(item);
                string? code = !ValidReviewIdentity(identity) ? "archive_iqama_invalid" :
                    item.MissingCache ? "formula_without_cached_value" : !main.ContainsKey(identity) ? "archive_row_missing_from_main" :
                    amount == null ? "archive_net_missing_or_invalid" : null;
                if (code != null)
                {
                    issues.Add(new JsonObject { ["sheet"] = name, ["row"] = i + 1, ["reason"] = code });
                    if (main.TryGetValue(identity, out var affected)) foreach (var r in affected) r.Reasons.Add(code);
                }
            }
        }
        foreach (var row in rows)
        {
            var found = evidence.Where(x => x.Identity == row.Identity).ToList();
            if (found.Select(x => x.Company).Distinct().Count() > 1) row.Reasons.Add("creditor_conflict");
            if (found.GroupBy(x => x.Company).Any(g => g.Count() > 1)) row.Reasons.Add("archive_iqama_duplicate");
            if (row.FinanciallyReadable && found.Any(x => x.Net != null && !Close(x.Net.Value, row.Net))) row.Reasons.Add("archive_net_differs");
        }
        return evidence;
    }

    static string ReviewCompany(JsonObject rider)
    {
        foreach (string field in new[] { "financeSponsorId", "sponsorId" })
        {
            string id = rider.S(field);
            var sponsor = FinancialCore.Sponsors.FirstOrDefault(s => s.Id == id || s.Code == id);
            if (sponsor != null) return sponsor.Code;
        }
        return "";
    }

    public static void ResolveReviewCreditors(List<ReviewRow> rows, List<ArchiveEvidence> archives, List<JsonObject>? riders)
    {
        var index = (riders ?? []).GroupBy(r => NormalizeReviewIdentity(r.S("iqama", r.S("id"))))
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var row in rows)
        {
            // No branch, personal name or deductions-workbook company column is consulted.
            if (!ValidReviewIdentity(row.Identity) || row.Reasons.Contains("iqama_duplicate")) continue;
            index.TryGetValue(row.Identity, out var matches);
            string riderCompany = matches?.Count == 1 ? ReviewCompany(matches[0]) : "";
            var sheets = archives.Where(a => a.Identity == row.Identity).Select(a => a.Company).Distinct().ToArray();
            if (row.Reasons.Contains("creditor_conflict") || row.Reasons.Contains("archive_iqama_duplicate")) continue;
            if (sheets.Length == 1)
            {
                row.Creditor = sheets[0]; row.CreditorSource = "sheet";
                row.CrossCompany = riderCompany != "" && riderCompany != row.Creditor;
            }
            else if (riderCompany != "") { row.Creditor = riderCompany; row.CreditorSource = "rider_record"; }
            else row.Reasons.Add(matches?.Count > 1 ? "ambiguous_rider_record" : "creditor_unresolved");
        }
    }

    static int ReviewHeader(Imports.Sheet sheet, string name)
    {
        var found = sheet.Rows[0].Select((v, i) => (v, i)).Where(x => Text(x.v) == name).ToList();
        if (found.Count != 1) throw new ImportFailure("review_header_missing_or_duplicate");
        return found[0].i;
    }

    static DateOnly? ReviewDate(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double serial) && serial is > 30000 and < 100000)
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? DateOnly.FromDateTime(date) : null;
    }

    public static List<OctoberRequest> ReadOctoberRequests(ReviewInput input, JsonArray issues)
    {
        var requests = new List<OctoberRequest>();
        foreach (var (name, company) in new[] { ("اكسبريس", "EX"), ("البوابا", "BW"), ("التجارية", "TR") })
        {
            var sheet = Sheet(input.Sheets, name);
            int id = ReviewHeader(sheet, "رقم الهوية"), reference = ReviewHeader(sheet, "رقم طلب الدفع"),
                amount = ReviewHeader(sheet, "القيمة"), status = ReviewHeader(sheet, "الحالة"), date = ReviewHeader(sheet, "التاريخ");
            var missing = MissingFormulaCache(input.Bytes, name);
            for (int i = 1; i < sheet.Rows.Count; i++)
            {
                var row = sheet.Rows[i];
                if (missing.Contains(i + 1))
                {
                    issues.Add(new JsonObject { ["sheet"] = name, ["row"] = i + 1, ["reason"] = "formula_without_cached_value" });
                    // Uncached status cannot safely be accepted or silently discarded.
                    continue;
                }
                if (Cell(row, status) is not ("مدير الحسابات" or "تم الإغلاق")) continue;
                DateOnly? when = ReviewDate(Cell(row, date));
                if (when != null && (when.Value.Year != 2026 || when.Value.Month != 10)) continue;
                string identity = NormalizeReviewIdentity(Cell(row, id));
                try
                {
                    decimal value = Money(Cell(row, amount));
                    if (Cell(row, amount) == "" || value <= 0) throw new ImportFailure("invalid_october_amount");
                    requests.Add(new(name, i + 1, identity, Cell(row, reference), company, value, when, false));
                    if (!ValidReviewIdentity(identity) || when == null)
                        issues.Add(new JsonObject { ["sheet"] = name, ["row"] = i + 1, ["reason"] = when == null ? "request_date_unknown" : "iqama_invalid" });
                }
                catch (ImportFailure) { issues.Add(new JsonObject { ["sheet"] = name, ["row"] = i + 1, ["reason"] = "invalid_october_amount" }); }
            }
        }
        // Request references are compared, but never disclosed. No step-7 fallback/dedupe is performed yet.
        foreach (var group in requests.Where(r => r.Reference != "").GroupBy(r => r.Reference).Where(g => g.Count() > 1))
            foreach (var r in group) issues.Add(new JsonObject { ["sheet"] = r.Sheet, ["row"] = r.Row, ["reason"] = "october_request_duplicate" });
        foreach (var r in requests.Where(r => r.Reference == ""))
            issues.Add(new JsonObject { ["sheet"] = r.Sheet, ["row"] = r.Row, ["reason"] = "october_request_reference_missing" });
        return requests;
    }

    public static OctoberDecision ClassifyOctober(List<ReviewRow> rows, List<OctoberRequest> requests, DateTimeOffset? exported)
    {
        var evidence = new HashSet<string>();
        if (requests.Count == 0) throw new ImportFailure("october_requests_empty");
        if (requests.Any(r => !ValidReviewIdentity(r.Identity) || r.Date == null || r.MissingCache || r.Reference == "") ||
            requests.GroupBy(r => r.Reference).Any(g => g.Count() > 1))
            return new("ambiguous", ["request_identity_date_cache_or_reference_unresolved"]);
        if (rows.Count > 1) return new("ambiguous", ["iqama_duplicate"]);
        if (exported == null) evidence.Add("sheet_export_date_unknown");
        else evidence.Add(requests.Any(r => r.Date > DateOnly.FromDateTime(exported.Value.UtcDateTime)) ? "request_after_export" : "request_not_after_export");
        if (rows.Count == 0) { evidence.Add("rider_absent_from_debt_sheet"); return new("separate_october_item", evidence.Order().ToArray()); }
        var row = rows[0];
        if (!row.FinanciallyReadable || row.Reasons.Overlaps(new[] { "formula_without_cached_value", "iqama_invalid", "bucket_total_mismatch", "deduction_total_mismatch", "net_mismatch", "financial_total_missing", "invalid_numeric_cell" }))
            return new("ambiguous", ["debt_row_unreliable"]);
        bool equal = requests.Any(r => row.Buckets.Any(b => Close(b, r.Amount)));
        string note = Cell(row.Cells, Column("BH"));
        bool explicitOctober = Regex.IsMatch(note, @"أكتوبر|اكتوبر|10[/-]2026|2026[/-]10", RegexOptions.IgnoreCase);
        bool mentionsReference = requests.Any(r => Regex.IsMatch(note, @"(?<![\p{L}\p{N}/])" + Regex.Escape(r.Reference) + @"(?![\p{L}\p{N}/])"));
        // A note or a matching value alone cannot prove inclusion. Nothing here authorizes a load.
        if (equal) evidence.Add("equal_amount_in_debt_bucket");
        if (explicitOctober) evidence.Add("note_mentions_october");
        if (mentionsReference) evidence.Add("note_mentions_request_reference");
        if (row.Reasons.Overlaps(new[] { "negative_bucket", "negative_payment" })) evidence.Add("negative_input_needs_decision");
        bool allReferenced = requests.All(r => Regex.IsMatch(note, @"(?<![\p{L}\p{N}/])" + Regex.Escape(r.Reference) + @"(?![\p{L}\p{N}/])"));
        bool explicitlyInNet = Regex.IsMatch(note, @"(?:ضمن|داخل|مدرج في|مشمول في)\s*(?:صافي|الصافي)\s*(?:أكتوبر|اكتوبر|10[/-]2026|2026[/-]10)");
        var available = row.Buckets.ToList();
        bool uniqueAmountEvidence = requests.All(r => { int i = available.FindIndex(b => Close(b, r.Amount)); if (i < 0) return false; available.RemoveAt(i); return true; });
        bool negativeOrUncertainNote = Regex.IsMatch(note, @"غير|ليس|لا |لم |خارج|مستبعد|مرفوض|not |exclude|pending", RegexOptions.IgnoreCase);
        if (explicitlyInNet && !negativeOrUncertainNote && allReferenced && uniqueAmountEvidence && Money(Cell(row.Cells, Column("BF"))) == 0 &&
            row.Net >= requests.Sum(r => r.Amount) && !evidence.Contains("negative_input_needs_decision") &&
            exported != null && !evidence.Contains("request_after_export"))
        { evidence.Add("explicit_net_note_with_all_references_and_distinct_unpaid_buckets"); return new("included_in_net", evidence.Order().ToArray()); }
        if (equal || explicitOctober || mentionsReference || evidence.Contains("negative_input_needs_decision"))
            return new("ambiguous", evidence.Order().ToArray());
        evidence.Add("no_october_amount_or_note_trace");
        return new("separate_october_item", evidence.Order().ToArray());
    }

    public static ThroughSixReview ReviewThroughSix(byte[] debtBytes, byte[] deductionBytes, JsonObject mapping, List<JsonObject>? riders = null)
    {
        if (mapping.N("version") != 1 || mapping.S("sheet") != "المديونيات") throw new ImportFailure("unsupported_review_mapping");
        var debt = FreezeInput(debtBytes); var deductions = FreezeInput(deductionBytes);
        Headers(Sheet(debt.Sheets, mapping.S("sheet")), mapping["columns"]!.AsArray());
        var rows = ReviewIdentities(debt, mapping.S("sheet"));
        ReviewIntegrity(rows);
        var archiveIssues = new JsonArray(); var archives = ReviewArchives(debt, rows, archiveIssues);
        ResolveReviewCreditors(rows, archives, riders);
        var requestIssues = new JsonArray(); var requests = ReadOctoberRequests(deductions, requestIssues);
        var octoberRows = new JsonArray(); var classes = new Dictionary<string, (int Riders, int Requests, decimal Total)>();
        var k5Classes = new Dictionary<string, (int Riders, decimal Total)>();
        var k5Differences = new JsonArray();
        var companyClasses = new Dictionary<(string Company, string Class), (int Riders, int Requests, decimal Total)>();
        foreach (var group in requests.GroupBy(r => r.Identity))
        {
            var matched = rows.Where(r => r.Identity == group.Key).ToList(); var list = group.ToList();
            var decision = ClassifyOctober(matched, list, debt.Modified);
            string heuristic = matched.Any(r => list.Any(q => r.Buckets.Any(b => Close(b, q.Amount)))) ? "ambiguous" : "separate_october_item";
            var k5 = k5Classes.GetValueOrDefault(heuristic);
            k5Classes[heuristic] = (k5.Riders + 1, k5.Total + list.Sum(r => r.Amount));
            if (heuristic != decision.Classification) k5Differences.Add(new JsonObject { ["debtRows"] = JsonSerializer.SerializeToNode(matched.Select(r => r.Number)),
                ["requestRows"] = JsonSerializer.SerializeToNode(list.Select(r => new { sheet = r.Sheet, row = r.Row })),
                ["k5Heuristic"] = heuristic, ["strictClassification"] = decision.Classification, ["reasons"] = JsonSerializer.SerializeToNode(decision.Evidence) });
            var prior = classes.GetValueOrDefault(decision.Classification);
            classes[decision.Classification] = (prior.Riders + 1, prior.Requests + list.Count, prior.Total + list.Sum(r => r.Amount));
            foreach (var company in list.GroupBy(r => r.Company))
            {
                var key = (company.Key, decision.Classification); var old = companyClasses.GetValueOrDefault(key);
                companyClasses[key] = (old.Riders + 1, old.Requests + company.Count(), old.Total + company.Sum(r => r.Amount));
            }
            octoberRows.Add(new JsonObject { ["debtRows"] = JsonSerializer.SerializeToNode(matched.Select(r => r.Number)),
                ["requestRows"] = JsonSerializer.SerializeToNode(list.Select(r => new { sheet = r.Sheet, row = r.Row })),
                ["requestCount"] = list.Count, ["octoberTotal"] = list.Sum(r => r.Amount), ["classification"] = decision.Classification,
                ["evidence"] = JsonSerializer.SerializeToNode(decision.Evidence) });
        }
        JsonArray ClassTotals() => new(new[] { "included_in_net", "separate_october_item", "ambiguous" }.Select(c => (JsonNode)new JsonObject {
            ["classification"] = c, ["riders"] = classes.GetValueOrDefault(c).Riders, ["requests"] = classes.GetValueOrDefault(c).Requests, ["total"] = classes.GetValueOrDefault(c).Total }).ToArray());
        var reasons = rows.SelectMany(r => r.Reasons).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        var flags = rows.Where(r => r.Reasons.Any(c => c is not ("creditor_unresolved" or "ambiguous_rider_record"))).ToList();
        var summary = new JsonObject {
            ["completedThroughStep"] = 6, ["stopReason"] = "october_load_mode_requires_ghazal", ["canCommit"] = false, ["committed"] = false,
            ["riderLookupPerformed"] = riders != null, ["sourceRows"] = rows.Count, ["uniqueIdentities"] = rows.Select(r => r.Identity).Distinct().Count(),
            ["sheetNetTotal"] = J.Money(rows.Sum(r => r.SourceNet)), ["rowRoundedNetTotal"] = rows.Sum(r => r.Net),
            ["sourceVsRowRoundingDelta"] = J.Money(rows.Sum(r => r.SourceNet)) - rows.Sum(r => r.Net),
            ["subcentCachedNetRows"] = rows.Count(r => r.SourceNet != r.Net),
            ["flaggedRows"] = flags.Count, ["reasonCounts"] = JsonSerializer.SerializeToNode(reasons),
            ["nonzeroRowsNeedingRiderLookup"] = rows.Count(r => r.Net != 0 && !archives.Any(a => a.Identity == r.Identity)),
            ["archiveIssueCount"] = archiveIssues.Count, ["octoberRequestIssueCount"] = requestIssues.Count,
            ["archiveNetDifferentComparisons"] = rows.Where(r => r.FinanciallyReadable).Sum(r => archives.Count(a => a.Identity == r.Identity && a.Net != null && !Close(a.Net.Value, r.Net))),
            ["octoberRequestReasonCounts"] = JsonSerializer.SerializeToNode(requestIssues.OfType<JsonObject>().GroupBy(x => x.S("reason")).ToDictionary(g => g.Key, g => g.Count())),
            ["archiveReasonCounts"] = JsonSerializer.SerializeToNode(archiveIssues.OfType<JsonObject>().GroupBy(x => x.S("reason")).ToDictionary(g => g.Key, g => g.Count())),
            ["octoberRequests"] = requests.Count, ["octoberTotal"] = requests.Sum(r => r.Amount), ["octoberClasses"] = ClassTotals(),
            ["k5HeuristicClasses"] = new JsonArray(k5Classes.OrderBy(x => x.Key).Select(x => (JsonNode)new JsonObject { ["classification"] = x.Key, ["riders"] = x.Value.Riders, ["total"] = x.Value.Total }).ToArray()),
            ["strictVsK5ChangedRiders"] = k5Differences.Count,
            ["octoberCompanyClasses"] = new JsonArray(companyClasses.OrderBy(x => x.Key.Company).ThenBy(x => x.Key.Class).Select(x => (JsonNode)new JsonObject {
                ["company"] = x.Key.Company, ["classification"] = x.Key.Class, ["riders"] = x.Value.Riders, ["requests"] = x.Value.Requests, ["total"] = x.Value.Total }).ToArray()),
            ["creditorTotals"] = new JsonArray(rows.GroupBy(r => (r.Creditor, r.CreditorSource)).OrderBy(g => g.Key.Creditor).Select(g => (JsonNode)new JsonObject {
                ["company"] = g.Key.Creditor, ["creditorSource"] = g.Key.CreditorSource, ["rows"] = g.Count(), ["netTotal"] = g.Sum(r => r.Net) }).ToArray()),
            ["crossCompanyRows"] = rows.Count(r => r.CrossCompany), ["exportDateKnown"] = debt.Modified != null,
            ["classificationCaveat"] = "no_trace_is_a_heuristic_not_proof_of_exclusion; equal_amount_is_not_proof_of_inclusion",
            ["unexplainedMismatch"] = rows.Any(r => r.Reasons.Overlaps(new[] { "bucket_total_mismatch", "deduction_total_mismatch", "net_mismatch" })) ||
                archiveIssues.OfType<JsonObject>().Any(x => x.S("reason") is "archive_row_missing_from_main" or "archive_net_missing_or_invalid" or "formula_without_cached_value") ||
                requestIssues.OfType<JsonObject>().Any(x => x.S("reason") is not "iqama_invalid")
        };
        var privateReport = new JsonObject { ["summary"] = summary.DeepClone(), ["balanceAsOf"] = "2026-10-31", ["strictVsK5Differences"] = k5Differences, ["inputManifest"] = new JsonArray(
            Manifest("debt", debt), Manifest("deductions", deductions)), ["archiveIssues"] = archiveIssues, ["octoberRequestIssues"] = requestIssues,
            ["rows"] = new JsonArray(rows.Select(r => (JsonNode)new JsonObject { ["sheet"] = mapping.S("sheet"), ["row"] = r.Number,
                ["reasons"] = JsonSerializer.SerializeToNode(r.Reasons.Order()), ["creditor"] = r.Creditor, ["creditorSource"] = r.CreditorSource,
                ["crossCompany"] = r.CrossCompany, ["mainNet"] = r.FinanciallyReadable ? JsonValue.Create(r.Net) : null,
                ["archiveNets"] = new JsonArray(archives.Where(a => a.Identity == r.Identity).Select(a => (JsonNode)new JsonObject {
                    ["sheet"] = a.Sheet, ["row"] = a.Row, ["net"] = a.Net }).ToArray()) }).ToArray()), ["octoberRows"] = octoberRows };
        return new(summary, privateReport);
    }

    static JsonObject Manifest(string role, ReviewInput input) => new() { ["role"] = role, ["sha256"] = input.Sha256,
        ["sheets"] = JsonSerializer.SerializeToNode(input.Sheets.Select(s => s.Name)), ["modifiedMetadata"] = input.Modified?.ToString("O"),
        ["exportDateEvidence"] = input.Modified == null ? "unavailable" : "file_metadata_not_authoritative_balance_cutoff" };
}
