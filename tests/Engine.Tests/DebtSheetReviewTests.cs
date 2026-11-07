using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ErpImport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class DebtSheetReviewTests
{
    const string Id = "2000000801";
    static JsonObject Mapping()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return JsonNode.Parse(File.ReadAllText(Path.Combine(root, "docs/data/debt-sheet-mapping.v1.json")))!.AsObject();
    }
    static string[] DebtRow(string id = Id, decimal bucket = 100, decimal payment = 0, string note = "")
    {
        var row = Enumerable.Repeat("", 62).ToArray(); row[0] = id; row[1] = "SYNTHETIC_NAME"; row[3] = "EX (ignored branch)";
        row[4] = bucket.ToString(System.Globalization.CultureInfo.InvariantCulture);
        row[39] = row[4]; row[40] = row[57] = payment.ToString(System.Globalization.CultureInfo.InvariantCulture);
        row[58] = (bucket - payment).ToString(System.Globalization.CultureInfo.InvariantCulture); row[59] = note;
        return row;
    }
    static string[] Header() => Mapping()["columns"]!.AsArray().OfType<JsonObject>().Select(c => c.S("header")).ToArray();
    static string[] Request(string id = Id, string amount = "30", string reference = "SYN/2026/001", string status = "مدير الحسابات", string date = "2026-10-15")
        => ["SYNTHETIC_NAME", date, status, "TR (ignored rider company)", amount, id, reference, "SYNTHETIC_MAKER", "", "112501 Cash", "SyntheticApp"];
    static string[] RequestHeader() => ["الاسم", "التاريخ", "الحالة", "الشركة", "القيمة", "رقم الهوية", "رقم طلب الدفع", "منشئ الطلب", "نوع الطلب", "نوع العملية", "التطبيق"];
    static byte[] Book((string Name, string[][] Rows)[] sheets, string? modified = null)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main", rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            void Xml(string path, XElement xml) { using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); xml.Save(writer); }
            Xml("xl/workbook.xml", new(ns + "workbook", new XAttribute(XNamespace.Xmlns + "r", rel), new XElement(ns + "sheets", sheets.Select((s, i) => new XElement(ns + "sheet", new XAttribute("name", s.Name), new XAttribute(rel + "id", "r" + i))))));
            Xml("xl/_rels/workbook.xml.rels", new("Relationships", sheets.Select((_, i) => new XElement("Relationship", new XAttribute("Id", "r" + i), new XAttribute("Target", $"worksheets/s{i}.xml")))));
            string Col(int n) { string value = ""; for (n++; n > 0; n = (n - 1) / 26) value = (char)('A' + (n - 1) % 26) + value; return value; }
            for (int i = 0; i < sheets.Length; i++)
                Xml($"xl/worksheets/s{i}.xml", new(ns + "worksheet", new XElement(ns + "sheetData", sheets[i].Rows.Select((r, n) => new XElement(ns + "row", new XAttribute("r", n + 1), r.Select((v, c) =>
                    v == "=NOCACHE" ? new XElement(ns + "c", new XAttribute("r", Col(c) + (n + 1)), new XElement(ns + "f", "1+1")) :
                    v == "=CACHED" ? new XElement(ns + "c", new XAttribute("r", Col(c) + (n + 1)), new XElement(ns + "f", "2+2"), new XElement(ns + "v", "100")) :
                    new XElement(ns + "c", new XAttribute("r", Col(c) + (n + 1)), new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", v)))))))));
            if (modified != null) Xml("docProps/core.xml", new("core", new XElement(XName.Get("modified", "http://purl.org/dc/terms/"), modified)));
        }
        return output.ToArray();
    }
    static byte[] Debt(string[][]? main = null, string[][]? ex = null, string[][]? tr = null, string? modified = null) => Book([
        ("المديونيات", [Header(), ..main ?? [DebtRow()]]),
        ("اكسبرس الرياض - ارشيف", [["الصافي", "رقم الإقامة"], ..ex ?? []]),
        ("التجارية الرياض - ارشيف", [["رقم الإقامة", "الصافي"], ..tr ?? []])], modified);
    static byte[] Deductions(string[][]? ex = null, string[][]? bw = null, string[][]? tr = null) => Book([
        ("اكسبريس", [RequestHeader(), ..ex ?? [Request()]]), ("البوابا", [RequestHeader(), ..bw ?? []]), ("التجارية", [RequestHeader(), ..tr ?? []])]);
    static JsonObject Rider(string finance = "BW", string sponsor = "TR") => new() { ["id"] = Id, ["iqama"] = Id, ["name"] = "SYNTHETIC_DB_NAME", ["financeSponsorId"] = finance, ["sponsorId"] = sponsor };
    static JsonObject Row(DebtSheetImporter.ThroughSixReview r, int n = 0) => r.PrivateReport["rows"]![n]!.AsObject();
    static string Classification(DebtSheetImporter.ThroughSixReview r) => r.PrivateReport["octoberRows"]![0]!.S("classification");
    static decimal Reason(DebtSheetImporter.ThroughSixReview r, string key) => r.Summary["reasonCounts"].N(key);

    [TestMethod]
    public void Step1FreezesHashesSheetListAndCachedValuesWithoutExecutingFormulas()
    {
        var row = DebtRow(); row[4] = "=CACHED";
        byte[] debt = Debt([row]); var input = DebtSheetImporter.FreezeInput(debt);
        string expected = Convert.ToHexString(SHA256.HashData(debt)); debt[0] ^= 1;
        Assert.AreEqual(expected, input.Sha256); Assert.AreEqual(3, input.Sheets.Count);
        var rows = DebtSheetImporter.ReviewIdentities(input, "المديونيات"); DebtSheetImporter.ReviewIntegrity(rows);
        Assert.AreEqual(0, rows[0].Reasons.Count);
        row[4] = "=NOCACHE";
        var review = DebtSheetImporter.ReviewThroughSix(Debt([row]), Deductions(), Mapping());
        Assert.AreEqual(1m, Reason(review, "formula_without_cached_value")); Assert.AreEqual("ambiguous", Classification(review));
        row = DebtRow(); row[0] = "=NOCACHE";
        Assert.AreEqual(1m, Reason(DebtSheetImporter.ReviewThroughSix(Debt([row]), Deductions(), Mapping()), "formula_without_cached_value"));
    }

    [TestMethod]
    public void Step2NormalizesDigitFormsAndBlocksBothDuplicateRowsAndInvalidIds()
    {
        Assert.AreEqual(Id, DebtSheetImporter.NormalizeReviewIdentity(" ٢٠٠٠٠٠٠٨٠١.00 "));
        Assert.AreEqual(Id, DebtSheetImporter.NormalizeReviewIdentity("2000-000-801"));
        Assert.AreEqual(Id, DebtSheetImporter.NormalizeReviewIdentity("2.000000801E+09"));
        var r = DebtSheetImporter.ReviewThroughSix(Debt([DebtRow(), DebtRow(Id + ".0"), DebtRow("123")]), Deductions(), Mapping());
        Assert.AreEqual(2m, Reason(r, "iqama_duplicate")); Assert.AreEqual(1m, Reason(r, "iqama_invalid"));
        Assert.AreEqual("blocked", Row(r).S("creditorSource")); Assert.AreEqual("ambiguous", Classification(r));
    }

    [TestMethod]
    public void Step3ChecksEachEquationIndependentlyWithoutResigningNegativeInputs()
    {
        var row = DebtRow(); row[39] = "110"; row[57] = "2"; row[58] = "107";
        var r = DebtSheetImporter.ReviewThroughSix(Debt([row]), Deductions(), Mapping());
        foreach (string reason in new[] { "bucket_total_mismatch", "deduction_total_mismatch", "net_mismatch" }) Assert.AreEqual(1m, Reason(r, reason));
        Assert.IsTrue(r.Summary.B("unexplainedMismatch"));
        r = DebtSheetImporter.ReviewThroughSix(Debt([DebtRow(bucket: -100, payment: -10)]), Deductions(), Mapping());
        Assert.AreEqual(1m, Reason(r, "negative_bucket")); Assert.AreEqual(1m, Reason(r, "negative_payment"));
        Assert.AreEqual(-90m, Row(r).N("mainNet"));
        row = DebtRow(); row[58] = "100.01";
        Assert.AreEqual(0m, Reason(DebtSheetImporter.ReviewThroughSix(Debt([row]), Deductions(), Mapping()), "net_mismatch"));
        row[58] = "";
        Assert.AreEqual(1m, Reason(DebtSheetImporter.ReviewThroughSix(Debt([row]), Deductions(), Mapping()), "financial_total_missing"));
    }

    [TestMethod]
    public void Step4UsesArchiveHeadersAndRetainsBothNetsWithoutReplacingDebtRow()
    {
        var r = DebtSheetImporter.ReviewThroughSix(Debt(ex: [["75", Id]], tr: [[Id, "70"]]), Deductions(), Mapping(), [Rider()]);
        Assert.AreEqual(1m, Reason(r, "archive_net_differs")); Assert.AreEqual(1m, Reason(r, "creditor_conflict"));
        Assert.AreEqual(100m, Row(r).N("mainNet")); Assert.AreEqual(75m, Row(r)["archiveNets"]![0]!.N("net"));
        Assert.AreEqual("blocked", Row(r).S("creditorSource"));
        r = DebtSheetImporter.ReviewThroughSix(Debt(ex: [["100", "2000000899"]]), Deductions(), Mapping());
        Assert.AreEqual("archive_row_missing_from_main", r.PrivateReport["archiveIssues"]![0]!.S("reason"));
        r = DebtSheetImporter.ReviewThroughSix(Debt(ex: [["100", Id], ["100", Id]]), Deductions(), Mapping());
        Assert.AreEqual(1m, Reason(r, "archive_iqama_duplicate"));
        r = DebtSheetImporter.ReviewThroughSix(Debt(ex: [["=NOCACHE", Id]]), Deductions(), Mapping());
        Assert.AreEqual(1m, Reason(r, "formula_without_cached_value"));
    }

    [TestMethod]
    public void Step5SheetWinsCrossCompanyThenFinanceThenSponsorAndNeverBranchOrNames()
    {
        var r = DebtSheetImporter.ReviewThroughSix(Debt(ex: [["100", Id]]), Deductions(), Mapping(), [Rider()]);
        Assert.AreEqual("EX", Row(r).S("creditor")); Assert.AreEqual("sheet", Row(r).S("creditorSource")); Assert.IsTrue(Row(r).B("crossCompany"));
        r = DebtSheetImporter.ReviewThroughSix(Debt(), Deductions(), Mapping(), [Rider()]);
        Assert.AreEqual("BW", Row(r).S("creditor")); Assert.AreEqual("rider_record", Row(r).S("creditorSource"));
        r = DebtSheetImporter.ReviewThroughSix(Debt(), Deductions(), Mapping(), [Rider("unknown")]); Assert.AreEqual("TR", Row(r).S("creditor"));
        r = DebtSheetImporter.ReviewThroughSix(Debt(), Deductions(), Mapping(), [Rider("unknown", "")]); Assert.AreEqual("blocked", Row(r).S("creditor"));
        r = DebtSheetImporter.ReviewThroughSix(Debt(), Deductions(), Mapping()); Assert.AreEqual("blocked", Row(r).S("creditor")); Assert.IsFalse(r.Summary.B("riderLookupPerformed"));
        r = DebtSheetImporter.ReviewThroughSix(Debt(), Deductions(), Mapping(), [Rider(), Rider()]); Assert.AreEqual(1m, Reason(r, "ambiguous_rider_record"));
    }

    [TestMethod]
    public void Step6AbsentAndNoTraceAreSeparateButEqualValuesAloneStayAmbiguous()
    {
        Assert.AreEqual("separate_october_item", Classification(DebtSheetImporter.ReviewThroughSix(Debt([]), Deductions(), Mapping())));
        var r = DebtSheetImporter.ReviewThroughSix(Debt(), Deductions(), Mapping());
        Assert.AreEqual("separate_october_item", Classification(r)); Assert.IsFalse(r.Summary.B("exportDateKnown"));
        r = DebtSheetImporter.ReviewThroughSix(Debt([DebtRow(bucket: 30)]), Deductions(), Mapping()); Assert.AreEqual("ambiguous", Classification(r));
        r = DebtSheetImporter.ReviewThroughSix(Debt([DebtRow(note: "مراجعة أكتوبر")]), Deductions(), Mapping()); Assert.AreEqual("ambiguous", Classification(r));
        Assert.IsFalse(r.Summary.B("canCommit")); Assert.AreEqual(6m, r.Summary.N("completedThroughStep"));
    }

    [TestMethod]
    public void Step6StrongNetEvidenceNeedsAllReferencesDistinctUnpaidBucketsAndKnownDate()
    {
        string note = "مدرج في الصافي أكتوبر SYN/2026/001";
        byte[] debt = Debt([DebtRow(bucket: 30, note: note)], modified: "2026-11-01T00:00:00Z");
        Assert.AreEqual("included_in_net", Classification(DebtSheetImporter.ReviewThroughSix(debt, Deductions(), Mapping())));
        Assert.AreEqual("ambiguous", Classification(DebtSheetImporter.ReviewThroughSix(Debt([DebtRow(bucket: 30, note: note)]), Deductions(), Mapping())));
        Assert.AreEqual("ambiguous", Classification(DebtSheetImporter.ReviewThroughSix(Debt([DebtRow(bucket: 30, payment: 1, note: note)], modified: "2026-11-01T00:00:00Z"), Deductions(), Mapping())));
        // Two requests of the same amount cannot claim the same bucket twice.
        var d = Deductions([Request(), Request(reference: "SYN/2026/002")]);
        Assert.AreEqual("ambiguous", Classification(DebtSheetImporter.ReviewThroughSix(Debt([DebtRow(bucket: 30, note: note + " SYN/2026/002")], modified: "2026-11-01T00:00:00Z"), d, Mapping())));
        Assert.AreEqual("ambiguous", Classification(DebtSheetImporter.ReviewThroughSix(Debt([DebtRow(bucket: 30, note: note)], modified: "2026-10-01T00:00:00Z"), Deductions(), Mapping())));
        Assert.AreEqual("ambiguous", Classification(DebtSheetImporter.ReviewThroughSix(Debt([DebtRow(bucket: 30, note: "غير " + note)], modified: "2026-11-01T00:00:00Z"), Deductions(), Mapping())));
    }

    [TestMethod]
    public void Step6OnlyOctoberApprovedCompanySheetsCountAndCompanyColumnIsIgnored()
    {
        var d = Deductions([Request(), Request(status: "مرفوض"), Request(status: "مسودة"), Request(date: "2026-09-30")],
            [Request(reference: "SYN/2026/002", status: "تم الإغلاق")], [Request(reference: "SYN/2026/003")]);
        var r = DebtSheetImporter.ReviewThroughSix(Debt(), d, Mapping());
        Assert.AreEqual(3m, r.Summary.N("octoberRequests")); Assert.AreEqual(90m, r.Summary.N("octoberTotal"));
        var classes = r.Summary["octoberCompanyClasses"]!.AsArray(); Assert.AreEqual(3, classes.Count);
        foreach (string code in new[] { "EX", "BW", "TR" }) Assert.AreEqual(30m, classes.Single(c => c.S("company") == code).N("total"));
        d = Deductions([Request(), Request()]);
        r = DebtSheetImporter.ReviewThroughSix(Debt(), d, Mapping());
        Assert.AreEqual(2m, r.Summary.N("octoberRequestIssueCount")); Assert.AreEqual("ambiguous", Classification(r)); Assert.IsTrue(r.Summary.B("unexplainedMismatch"));
        d = Deductions([Request(date: "not a date")]);
        Assert.AreEqual("ambiguous", Classification(DebtSheetImporter.ReviewThroughSix(Debt(), d, Mapping())));
    }

    [TestMethod]
    public void ReviewSerializationDoesNotLeakIdentityNamesReferencesOrNotesAndXlsxRoundTrips()
    {
        var r = DebtSheetImporter.ReviewThroughSix(Debt([DebtRow(note: "SYNTHETIC_PRIVATE_NOTE")]), Deductions(), Mapping(), [Rider()]);
        string json = r.PrivateReport.ToJsonString();
        foreach (string secret in new[] { Id, "SYNTHETIC_NAME", "SYNTHETIC_DB_NAME", "SYN/2026/001", "SYNTHETIC_PRIVATE_NOTE" }) Assert.DoesNotContain(secret, json);
        using var output = new MemoryStream(); DebtSheetImporter.WriteReviewWorkbook(r, output);
        var sheets = Imports.Read(output.ToArray(), "review.xlsx"); Assert.AreEqual(4, sheets.Count);
        string all = string.Join(" ", sheets.SelectMany(s => s.Rows).SelectMany(row => row));
        Assert.DoesNotContain(Id, all); Assert.DoesNotContain("SYNTHETIC_PRIVATE_NOTE", all);
        Assert.ThrowsExactly<ImportFailure>(() => DebtSheetImporter.WriteThroughSixReport(r, Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")), "forbidden.json")));
    }

    [TestMethod]
    public void CachedSubcentNetExplainsSourceTotalVersusRowRoundingAndArchiveCountsAreExplicit()
    {
        var first = DebtRow(bucket: 1); first[58] = "1.004";
        var second = DebtRow("2000000802", bucket: 1); second[58] = "1.004";
        var r = DebtSheetImporter.ReviewThroughSix(Debt([first, second]), Deductions(), Mapping());
        Assert.AreEqual(2.01m, r.Summary.N("sheetNetTotal")); Assert.AreEqual(2m, r.Summary.N("rowRoundedNetTotal"));
        Assert.AreEqual(.01m, r.Summary.N("sourceVsRowRoundingDelta")); Assert.IsFalse(r.Summary.B("unexplainedMismatch"));
        r = DebtSheetImporter.ReviewThroughSix(Debt(ex: [["75", Id]], tr: [[Id, "70"]]), Deductions(), Mapping());
        Assert.AreEqual(2m, r.Summary.N("archiveNetDifferentComparisons")); Assert.AreEqual(1m, Reason(r, "archive_net_differs"));
    }
}
