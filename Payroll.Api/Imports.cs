using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace Payroll;
public static class Imports
{
    public const long MaxSheetUncompressedBytes = 250L * 1024 * 1024;
    public const long MaxWorkbookUncompressedBytes = 400L * 1024 * 1024;
    public const int MaxSheetRows = 120_000;
    public const int MaxSheetColumns = 500;
    static readonly XmlReaderSettings SafeXml = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        XmlResolver = null
    };
    public sealed record Sheet(string Name, List<List<string>> Rows);
    public static List<Sheet> Read(byte[] data, string filename)
    {
        J.Need(data.Length <= 12 * 1024 * 1024, "حد الملف12ميجابايت.");
        if (Path.GetExtension(filename).Equals(".csv", StringComparison.OrdinalIgnoreCase)) return [new("CSV", Csv(Encoding.UTF8.GetString(data).TrimStart('\uFEFF')))];
        J.Need(Path.GetExtension(filename).Equals(".xlsx", StringComparison.OrdinalIgnoreCase), "استخدم ملفxlsxأوCSVبترميزUTF-8.");
        using var stream = new MemoryStream(data); using var zip = new ZipArchive(stream);
        J.Need(zip.Entries.Count <= 3000 && zip.Entries.Sum(x => x.Length) <= MaxWorkbookUncompressedBytes, "المصنف أكبر من حد المعالجة الآمن.");
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var strings = ReadSharedStrings(zip);
        XDocument XmlSmall(string path)
        {
            var e = zip.GetEntry(path) ?? throw new ApiError("المصنف غير صالح: " + path);
            J.Need(e.Length < 8 * 1024 * 1024, "ملف وصف المصنف أكبر من الحد الآمن.");
            using var s = e.Open(); return XDocument.Load(s);
        }
        var rels = XmlSmall("xl/_rels/workbook.xml.rels").Root!.Elements().ToDictionary(x => x.Attribute("Id")!.Value, x => x.Attribute("Target")!.Value);
        var sheets = new List<Sheet>();
        foreach (var sheet in XmlSmall("xl/workbook.xml").Descendants(ns + "sheet"))
        {
            string target = rels[sheet.Attribute(rel + "id")!.Value]; string path = target.StartsWith('/') ? target[1..] : "xl/" + target;
            sheets.Add(new(sheet.Attribute("name")!.Value, ReadSheetRows(zip, path, strings)));
        }
        return sheets;
    }
    const string SheetOversizeMessage = "ورقة غير مضغوطة أكبر من الحد الآمن (250 ميجابايت). صغّر الورقة أو اقسم الملف.";
    static void NeedSheetSize(ZipArchiveEntry entry)
    {
        J.Need(entry.Length <= MaxSheetUncompressedBytes, SheetOversizeMessage);
    }
    /// <summary>Enforces the uncompressed sheet byte cap while reading (Zip entry Length can understate).</summary>
    public static Stream LimitSheetBytes(Stream stream, long? maxBytes = null)
        => new CountingLimitStream(stream, maxBytes ?? MaxSheetUncompressedBytes);
    static Stream OpenCapped(ZipArchiveEntry entry)
    {
        NeedSheetSize(entry);
        return LimitSheetBytes(entry.Open());
    }
    sealed class CountingLimitStream : Stream
    {
        readonly Stream _inner;
        readonly long _limit;
        long _read;
        public CountingLimitStream(Stream inner, long limit) { _inner = inner; _limit = limit; }
        void Count(int n)
        {
            if (n <= 0) return;
            _read += n;
            if (_read > _limit) throw new ApiError(SheetOversizeMessage);
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _inner.Read(buffer, offset, count); Count(n); return n;
        }
        public override int Read(Span<byte> buffer)
        {
            int n = _inner.Read(buffer); Count(n); return n;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int n = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); Count(n); return n;
        }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
    static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return [];
        var strings = new List<string>();
        using var stream = OpenCapped(entry);
        using var reader = XmlReader.Create(stream, SafeXml);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "si") continue;
            using var subtree = reader.ReadSubtree();
            var text = new StringBuilder();
            while (subtree.Read())
            {
                if (subtree.NodeType == XmlNodeType.Element && subtree.LocalName == "t")
                    text.Append(subtree.ReadElementContentAsString());
            }
            strings.Add(text.ToString());
        }
        return strings;
    }
    static List<List<string>> ReadSheetRows(ZipArchive zip, string path, List<string> strings)
    {
        var entry = zip.GetEntry(path) ?? throw new ApiError("المصنف غير صالح: " + path);
        var rows = new List<List<string>>();
        using var stream = OpenCapped(entry);
        using var reader = XmlReader.Create(stream, SafeXml);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row") continue;
            int rowIndex = int.TryParse(reader.GetAttribute("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRow)
                ? parsedRow
                : rows.Count + 1;
            J.Need(rowIndex <= MaxSheetRows, $"حد الصفوف{MaxSheetRows}للورقة؛ قسّم المصدر.");
            while (rows.Count < rowIndex - 1) rows.Add([]);
            var cells = new List<string>();
            using var row = reader.ReadSubtree();
            row.Read();
            while (row.Read())
            {
                if (row.NodeType != XmlNodeType.Element || row.LocalName != "c") continue;
                string cellRef = row.GetAttribute("r") ?? "A1";
                string cellType = row.GetAttribute("t") ?? "";
                int col = 0; foreach (char ch in cellRef.TakeWhile(char.IsAsciiLetter)) col = col * 26 + char.ToUpperInvariant(ch) - 'A' + 1;
                J.Need(col <= MaxSheetColumns, $"حد الأعمدة{MaxSheetColumns}.");
                while (cells.Count < col) cells.Add("");
                string value = "";
                if (cellType == "inlineStr")
                {
                    using var cell = row.ReadSubtree();
                    var text = new StringBuilder();
                    while (cell.Read())
                    {
                        if (cell.NodeType == XmlNodeType.Element && cell.LocalName == "t")
                            text.Append(cell.ReadElementContentAsString());
                    }
                    value = text.ToString();
                }
                else
                {
                    using var cell = row.ReadSubtree();
                    while (cell.Read())
                    {
                        if (cell.NodeType == XmlNodeType.Element && cell.LocalName == "v")
                        { value = cell.ReadElementContentAsString(); break; }
                    }
                    if (cellType == "s" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index < strings.Count)
                        value = strings[index];
                }
                if (col > 0) cells[col - 1] = value;
            }
            rows.Add(cells);
        }
        return rows;
    }
    /// <summary>True when any formula cell in the selected columns has no cached value.</summary>
    public static HashSet<int> MissingFormulaCacheRows(byte[] bytes, string sheetName, HashSet<int> selectedColumns)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes));
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main", rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XElement XmlSmall(string path)
        {
            var e = zip.GetEntry(path) ?? throw new ApiError("مصنف غير صالح.");
            J.Need(e.Length < 8 * 1024 * 1024, "ملف وصف المصنف أكبر من الحد الآمن.");
            using var stream = e.Open(); return XElement.Load(stream);
        }
        var sheet = XmlSmall("xl/workbook.xml").Descendants(ns + "sheet").Single(s => (string?)s.Attribute("name") == sheetName);
        string id = (string)sheet.Attribute(rel + "id")!;
        string target = (string)XmlSmall("xl/_rels/workbook.xml.rels").Elements().Single(x => (string?)x.Attribute("Id") == id).Attribute("Target")!;
        string path = target.StartsWith('/') ? target[1..] : "xl/" + target;
        var entry = zip.GetEntry(path) ?? throw new ApiError("مصنف غير صالح.");
        var missing = new HashSet<int>();
        using var stream = OpenCapped(entry);
        using var reader = XmlReader.Create(stream, SafeXml);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row") continue;
            int rowIndex = int.Parse(reader.GetAttribute("r") ?? "0", CultureInfo.InvariantCulture);
            using var row = reader.ReadSubtree();
            row.Read();
            while (row.Read())
            {
                if (row.NodeType != XmlNodeType.Element || row.LocalName != "c") continue;
                string cellRef = row.GetAttribute("r") ?? "A1";
                int col = 0; foreach (char ch in cellRef.TakeWhile(char.IsAsciiLetter)) col = col * 26 + char.ToUpperInvariant(ch) - 'A' + 1;
                int zeroBased = col - 1;
                bool hasFormula = false; bool hasValue = false;
                using var cell = row.ReadSubtree();
                while (cell.Read())
                {
                    if (cell.NodeType != XmlNodeType.Element) continue;
                    // Do not Skip <f>: Skip advances past a following sibling <v> and loses the cached value.
                    if (cell.LocalName == "f") hasFormula = true;
                    else if (cell.LocalName == "v")
                    {
                        string text = cell.ReadElementContentAsString();
                        hasValue = !string.IsNullOrEmpty(text);
                    }
                }
                if (selectedColumns.Contains(zeroBased) && hasFormula && !hasValue) missing.Add(rowIndex);
            }
        }
        return missing;
    }
    public static bool SheetContainsFormulas(ZipArchiveEntry entry)
    {
        using var stream = OpenCapped(entry);
        using var reader = XmlReader.Create(stream, SafeXml);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "f") return true;
        }
        return false;
    }
    private static List<List<string>> Csv(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"') { if (quoted && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = !quoted; }
            else if (c == ',' && !quoted) { row.Add(cell.ToString()); cell.Clear(); }
            else if ((c == '\n' || c == '\r') && !quoted) { if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = []; }
            else cell.Append(c);
        }
        J.Need(!quoted, "CSVبهعلامةاقتباسغيرمغلقة."); if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        J.Need(rows.All(r => r.Count <= MaxSheetColumns), $"حد الأعمدة{MaxSheetColumns}.");
        J.Need(rows.Count <= MaxSheetRows, $"حد الصفوف{MaxSheetRows}.");
        return rows;
    }
    public sealed record Preview(List<WorkLine> Rows, List<string> Errors);
    public static Preview Normalize(Sheet sheet, JsonObject spec)
    {
        var period = J.Period(spec.Required("period")); var platform = spec.Required("platform");
        J.Need(new[] { "Keeta", "HungerStation", "Ninja", "Chefz", "Jahez", "Amazon", "Other" }.Contains(platform), "منصة غير مدعومة.");
        J.Need(spec.B("confirmActualRider"), "أكد أن عمود الهوية يخص المندوب الفعلي/البديل وليس صاحب الحساب.");
        var map = spec["mapping"] as JsonObject ?? throw new ApiError("حدد ربط الأعمدة.");
        J.Need(int.TryParse(map.S("iqama"), out _), "ربط عمود إقامة المندوب الفعلي إلزامي.");
        int first = (int)spec.N("headerRow", 1); int last = (int)spec.N("lastRow", sheet.Rows.Count); J.Need(first >= 1 && last <= sheet.Rows.Count && last > first, "نطاق صفوف غير صالح.");
        var ignored = spec.S("ignoreRows").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x.Trim(), out int n) ? n : -1).ToHashSet();
        var rows = new List<WorkLine>(); var errors = new List<string>(); var refs = new HashSet<string>();
        for (int n = first; n < last; n++)
        {
            var row = sheet.Rows[n]; if (ignored.Contains(n + 1) || row.All(string.IsNullOrWhiteSpace)) continue;
            try
            {
                string V(string field) => int.TryParse(map.S(field), out int idx) && idx >= 0 && idx < row.Count ? row[idx].Trim() : "";
                decimal? M(string field) { var v = J.Digits(V(field)).Replace(",", "").Replace("٬", "").Replace("٫", "."); if (v == "") return null; J.Need(decimal.TryParse(v, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out decimal value), $"قيمة رقمية غير صحيحة في{field}"); J.Need(Math.Abs(value) < 100000000 && value == J.Money(value), $"القيمة تحتاج مراجعة أو تتجاوز خانتين عشريتين:{field}"); return value; }
                string D(string field, string fallback) { var v = V(field); if (v == "") return fallback; if (double.TryParse(v, CultureInfo.InvariantCulture, out var oa) && oa > 30000 && oa < 100000) v = DateTime.FromOADate(oa).ToString("yyyy-MM-dd"); J.Need(DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), $"تاريخ غير صالح:{field}؛ استخدمyyyy-MM-dd"); return v; }
                string iqama = J.Iqama(V("iqama")); string account = V("accountId");
                J.Need(!account.Contains('E', StringComparison.OrdinalIgnoreCase), "معرف الحساب بصيغة علمية؛ أعد تصديره كنص حتى لا نفقد الدقة.");
                var from = D("from", period + "-01"); var to = D("to", DateOnly.Parse(period + "-01").AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd"));
                J.Need(from.StartsWith(period) && to.StartsWith(period) && string.CompareOrdinal(from, to) <= 0, "فترة العمل يجب أن تكون داخل الشهر وبترتيب صحيح.");
                decimal orderNumber = M("orders") ?? 0; J.Need(orderNumber >= 0 && orderNumber <= 100000 && decimal.Truncate(orderNumber) == orderNumber, "الطلبات يجب أن تكون عددًا صحيحًا غير سالب.");
                string reference = V("sourceRef"); if (reference == "") reference = $"{platform}:{account}:{iqama}:{from}:{to}";
                J.Need(refs.Add(reference + ":" + iqama + ":" + account), "مرجع مصدر مكرر للمندوب؛ افصل الفترات أو اختر مصدرًا مجمعًا ولا تضاعف الحساب.");
                bool? valid = V("isValid").ToLowerInvariant() switch { "true" or "1" or "valid" or "صالح" => true, "false" or "0" or "invalid" or "غير صالح" => false, "" => null, _ => throw new ApiError("قيمة الصلاحية يجب أن تكونtrueأوfalseأوفارغة.") };
                rows.Add(new(reference, iqama, V("name"), platform, account, period, from, to, (int)orderNumber, M("platformRevenue"), M("riderPay"), valid, V("company"), V("city"), V("from") != "" && V("to") != ""));
            }
            catch (ApiError e) { errors.Add($"الصف{n + 1}: {e.Message}"); }
        }
        J.Need(rows.Count + errors.Count > 0, "لا توجد بيانات في النطاق المختار."); return new(rows, errors);
    }
}
