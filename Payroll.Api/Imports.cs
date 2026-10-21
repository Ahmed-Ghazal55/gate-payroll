using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Payroll;
public static class Imports
{
    public sealed record Sheet(string Name, List<List<string>> Rows);
    public static List<Sheet> Read(byte[] data, string filename)
    {
        J.Need(data.Length <= 8 * 1024 * 1024, "حد الملف8ميجابايت.");
        if (Path.GetExtension(filename).Equals(".csv", StringComparison.OrdinalIgnoreCase)) return [new("CSV", Csv(Encoding.UTF8.GetString(data).TrimStart('\uFEFF')))];
        J.Need(Path.GetExtension(filename).Equals(".xlsx", StringComparison.OrdinalIgnoreCase), "استخدم ملفxlsxأوCSVبترميزUTF-8.");
        using var stream = new MemoryStream(data); using var zip = new ZipArchive(stream);
        J.Need(zip.Entries.Count <= 3000 && zip.Entries.Sum(x => x.Length) <= 160L * 1024 * 1024, "المصنف أكبر من حد المعالجة الآمن.");
        XDocument Xml(string path) { var e = zip.GetEntry(path) ?? throw new ApiError("المصنف غير صالح: " + path); J.Need(e.Length < 40 * 1024 * 1024, "ورقة كبيرة جدًا؛ استوردهاCSV."); using var s = e.Open(); return XDocument.Load(s); }
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var strings = zip.GetEntry("xl/sharedStrings.xml") == null ? [] : Xml("xl/sharedStrings.xml").Descendants(ns + "si").Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value))).ToList();
        var rels = Xml("xl/_rels/workbook.xml.rels").Root!.Elements().ToDictionary(x => x.Attribute("Id")!.Value, x => x.Attribute("Target")!.Value);
        var sheets = new List<Sheet>();
        foreach (var sheet in Xml("xl/workbook.xml").Descendants(ns + "sheet"))
        {
            string target = rels[sheet.Attribute(rel + "id")!.Value]; string path = target.StartsWith('/') ? target[1..] : "xl/" + target;
            var rows = new List<List<string>>();
            foreach (var row in Xml(path).Descendants(ns + "sheetData").Elements(ns + "row"))
            {
                int rowIndex = int.Parse(row.Attribute("r")?.Value ?? (rows.Count + 1).ToString(CultureInfo.InvariantCulture));
                J.Need(rowIndex <= 25000, "حد المعاينة25000صف للورقة؛ قسّم المصدر.");
                while (rows.Count < rowIndex - 1) rows.Add([]);
                var cells = new List<string>();
                foreach (var cell in row.Elements(ns + "c"))
                {
                    string cellRef = cell.Attribute("r")?.Value ?? "A1"; int col = 0; foreach (char ch in cellRef.TakeWhile(char.IsAsciiLetter)) col = col * 26 + char.ToUpperInvariant(ch) - 'A' + 1;
                    J.Need(col <= 500, "حد الأعمدة500."); while (cells.Count < col) cells.Add("");
                    string value = cell.Element(ns + "v")?.Value ?? "";
                    if (cell.Attribute("t")?.Value == "s" && int.TryParse(value, out int index) && index < strings.Count) value = strings[index];
                    else if (cell.Attribute("t")?.Value == "inlineStr") value = string.Concat(cell.Descendants(ns + "t").Select(x => x.Value));
                    if (col > 0) cells[col - 1] = value;
                }
                rows.Add(cells);
            }
            sheets.Add(new(sheet.Attribute("name")!.Value, rows));
        }
        return sheets;
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
        J.Need(!quoted, "CSVبهعلامةاقتباسغيرمغلقة."); if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); } J.Need(rows.All(r => r.Count <= 500), "حد الأعمدة500."); J.Need(rows.Count <= 25000, "حد الصفوف25000."); return rows;
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
