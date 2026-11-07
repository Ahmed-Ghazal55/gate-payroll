using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Payroll;

namespace ErpImport;

public static partial class DebtSheetImporter
{
    public static void WriteThroughSixReport(ThroughSixReview review, string jsonPath)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Private/gate-data/sync-logs");
        string path = Path.GetFullPath(jsonPath), dir = Path.GetDirectoryName(path)!;
        if (dir != root || Path.GetExtension(path) != ".json") throw new ImportFailure("review_report_requires_private_sync_logs_json");
        // Reject symlinked parents and files before opening either output; never follow a redirect into a repo.
        for (var current = new DirectoryInfo(dir); current != null; current = current.Parent)
            if (current.LinkTarget != null) throw new ImportFailure("review_report_symlink_refused");
        string xlsx = Path.ChangeExtension(path, ".xlsx");
        foreach (string output in new[] { path, xlsx })
            if (new FileInfo(output).LinkTarget != null) throw new ImportFailure("review_report_symlink_refused");
        Directory.CreateDirectory(dir);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        WritePrivate(path, stream => JsonSerializer.Serialize(stream, review.PrivateReport, new JsonSerializerOptions { WriteIndented = true }));
        WritePrivate(xlsx, stream => WriteReviewWorkbook(review, stream));
    }

    static void WritePrivate(string path, Action<Stream> write)
    {
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(path, options);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        write(stream);
    }

    public static void WriteReviewWorkbook(ThroughSixReview review, Stream destination)
    {
        // Same review sections as K5 through step 6, with row references replacing personal identifiers.
        // Raw notes, names, iqamas, request references and source rows are never copied.
        var sheets = new List<(string Name, List<string[]> Rows)>();
        var summary = new List<string[]> { new[] { "البند", "القيمة" } };
        foreach (var item in review.Summary) summary.Add([item.Key, item.Value?.ToJsonString() ?? ""]);
        foreach (var manifest in review.PrivateReport["inputManifest"]!.AsArray().OfType<JsonObject>())
        { summary.Add([manifest.S("role") + " SHA-256", manifest.S("sha256")]); summary.Add([manifest.S("role") + " sheets", manifest["sheets"]!.ToJsonString()]); }
        sheets.Add(("ملخص", summary));
        var flags = new List<string[]> { new[] { "الورقة", "صف Excel", "الأسباب", "صافي الرئيسية", "صافي الأرشيف" } };
        var creditors = new List<string[]> { new[] { "صف Excel", "مصدر التحديد", "شركة التحصيل", "cross_company" } };
        foreach (var row in review.PrivateReport["rows"]!.AsArray().OfType<JsonObject>())
        {
            if (row["reasons"]!.AsArray().Count > 0) flags.Add([row.S("sheet"), row.N("row").ToString(), row["reasons"]!.ToJsonString(), row["mainNet"]?.ToJsonString() ?? "", row["archiveNets"]!.ToJsonString()]);
            creditors.Add([row.N("row").ToString(), row.S("creditorSource"), row.S("creditor"), row.B("crossCompany").ToString()]);
        }
        sheets.Add(("صفوف للمراجعة", flags)); sheets.Add(("تحصيل حسب الورقة", creditors));
        var october = new List<string[]> { new[] { "صفوف المديونيات", "صفوف الطلبات", "عدد الطلبات", "مجموع أكتوبر", "التصنيف", "الدليل" } };
        foreach (var row in review.PrivateReport["octoberRows"]!.AsArray().OfType<JsonObject>())
            october.Add([row["debtRows"]!.ToJsonString(), row["requestRows"]!.ToJsonString(), row.N("requestCount").ToString(), row.N("octoberTotal").ToString(), row.S("classification"), row["evidence"]!.ToJsonString()]);
        sheets.Add(("أكتوبر داخل الصافي؟", october));
        foreach(var (key,title) in new[]{("deductionIssues","مطابقة الاستقطاعات"),("carriedItems","ما لم يخصم سابقا"),("octoberLineage","مراجع أكتوبر فقط")})
        {
            if(review.PrivateReport[key] is not JsonArray entries)continue;
            var reportRows=new List<string[]>{new[]{"الورقة","صف Excel","الدليل — للمطابقة فقط"}};
            foreach(var entry in entries.OfType<JsonObject>())reportRows.Add([entry.S("sheet"),entry.N("row").ToString(),entry.ToJsonString()]);
            sheets.Add((title,reportRows));
        }
        if(review.Summary["companyCategoryOpeningTotals"] is JsonArray categories)
        {
            var reportRows=new List<string[]>{new[]{"شركة التحصيل","الفئة","العدد","إجمالي الافتتاح"}};
            foreach(var row in categories.OfType<JsonObject>())reportRows.Add([row.S("company"),row.S("category"),row.N("count").ToString(),row.N("total").ToString()]);
            sheets.Add(("شركة التحصيل × النوع",reportRows));
        }
        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, true);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main", rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships", pkg = "http://schemas.openxmlformats.org/package/2006/relationships", types = "http://schemas.openxmlformats.org/package/2006/content-types";
        void Xml(string name, XElement root) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); root.Save(writer); }
        Xml("[Content_Types].xml", new(types + "Types",
            new XElement(types + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(types + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(types + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            sheets.Select((_, i) => new XElement(types + "Override", new XAttribute("PartName", $"/xl/worksheets/s{i}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
        Xml("_rels/.rels", new(pkg + "Relationships", new XElement(pkg + "Relationship", new XAttribute("Id", "root"), new XAttribute("Type", rel.NamespaceName + "/officeDocument"), new XAttribute("Target", "xl/workbook.xml"))));
        Xml("xl/workbook.xml", new(ns + "workbook", new XAttribute(XNamespace.Xmlns + "r", rel), new XElement(ns + "sheets",
            sheets.Select((s, i) => new XElement(ns + "sheet", new XAttribute("name", s.Name), new XAttribute("sheetId", i + 1), new XAttribute(rel + "id", "r" + i))))));
        Xml("xl/_rels/workbook.xml.rels", new(pkg + "Relationships", sheets.Select((_, i) => new XElement(pkg + "Relationship", new XAttribute("Id", "r" + i), new XAttribute("Type", rel.NamespaceName + "/worksheet"), new XAttribute("Target", $"worksheets/s{i}.xml")))));
        for (int i = 0; i < sheets.Count; i++)
            Xml($"xl/worksheets/s{i}.xml", new(ns + "worksheet", new XElement(ns + "sheetViews", new XElement(ns + "sheetView", new XAttribute("workbookViewId", 0), new XAttribute("rightToLeft", 1),
                new XElement(ns + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("state", "frozen")))),
                new XElement(ns + "sheetData", sheets[i].Rows.Select((r, n) => new XElement(ns + "row", new XAttribute("r", n + 1), r.Select((v, c) =>
                    new XElement(ns + "c", new XAttribute("r", ((char)('A' + c)).ToString() + (n + 1)), new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", v)))))))));
    }
}
