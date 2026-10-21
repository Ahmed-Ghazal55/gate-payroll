using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Payroll;

public record ExcelColumn(string Header, bool Numeric = false, string[]? Choices = null, bool Identity = false);

// Runtime XLSX writer: values only, never formulas supplied by a browser or an uploaded file.
public static class ExcelFiles
{
    public const string Mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public static byte[] Write(IReadOnlyList<ExcelColumn> columns, IReadOnlyList<object?[]> rows)
    {
        J.Need(columns.Count is > 0 and <= 100 && rows.Count <= 24999, "حد التصدير 100 عمود و24999 سجل.");
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        string Col(int index) { string value = ""; do { value = (char)('A' + index % 26) + value; index = index / 26 - 1; } while (index >= 0); return value; }
        XElement Cell(int column, int row, object? value, int style)
        {
            var cell = new XElement(s + "c", new XAttribute("r", Col(column) + row), new XAttribute("s", style));
            if (value is decimal or int or long) cell.Add(new XElement(s + "v", Convert.ToString(value, CultureInfo.InvariantCulture)));
            else
            {
                string text = value?.ToString() ?? "";
                J.Need(text.Length <= 32767, "نص خلية يتجاوز حد Excel.");
                try { XmlConvert.VerifyXmlChars(text); } catch (XmlException) { throw new ApiError("النص يحتوي محارف غير صالحة للتصدير."); }
                cell.Add(new XAttribute("t", "inlineStr"), new XElement(s + "is", new XElement(s + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
            }
            return cell;
        }
        var data = new XElement(s + "sheetData", new XElement(s + "row", new XAttribute("r", 1), new XAttribute("ht", 30), new XAttribute("customHeight", 1), columns.Select((c, i) => Cell(i, 1, c.Header, 1))));
        for (int index = 0; index < rows.Count; index++)
        {
            J.Need(rows[index].Length == columns.Count, "عدد خلايا الصف لا يطابق الأعمدة.");
            data.Add(new XElement(s + "row", new XAttribute("r", index + 2), new XAttribute("ht", rows[index].Any(v => v?.ToString()?.Contains('\n') == true) ? 42 : 24), new XAttribute("customHeight", 1), rows[index].Select((v, c) => Cell(c, index + 2, v, columns[c].Numeric ? 3 : 2))));
        }
        var root = new XElement(s + "worksheet", new XElement(s + "sheetViews", new XElement(s + "sheetView", new XAttribute("workbookViewId", 0), new XAttribute("rightToLeft", 1), new XElement(s + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))), new XElement(s + "cols", columns.Select((c, i) => new XElement(s + "col", new XAttribute("min", i + 1), new XAttribute("max", i + 1), new XAttribute("width", c.Identity ? 23 : c.Header.Contains("اسم") || c.Header.Contains("مندوب") || c.Header.Contains("معرف") ? 38 : 29), new XAttribute("customWidth", 1), new XAttribute("style", c.Numeric ? 3 : 2)))), data, new XElement(s + "autoFilter", new XAttribute("ref", $"A1:{Col(columns.Count - 1)}{Math.Max(rows.Count + 1, 2)}")));
        var validations = new List<XElement>();
        foreach (var (column, index) in columns.Select((c, i) => (c, i)))
        {
            if (column.Choices == null && !column.Identity) continue;
            string formula = column.Choices != null ? "\"" + string.Join(',', column.Choices) + "\"" : $"AND(ISTEXT({Col(index)}2),LEN({Col(index)}2)=10,SUMPRODUCT(--ISNUMBER(FIND(MID({Col(index)}2,ROW($1:$10),1),\"0123456789\")))=10)";
            validations.Add(new XElement(s + "dataValidation", new XAttribute("type", column.Choices != null ? "list" : "custom"), new XAttribute("sqref", $"{Col(index)}2:{Col(index)}25000"), new XAttribute("allowBlank", 1), new XAttribute("showErrorMessage", 1), new XAttribute("errorTitle", "قيمة غير صحيحة"), new XAttribute("error", column.Identity ? "اكتب10أرقام إنجليزية فقط." : "اختر قيمة من القائمة."), new XElement(s + "formula1", formula)));
        }
        if (validations.Count > 0) root.Add(new XElement(s + "dataValidations", new XAttribute("count", validations.Count), validations));
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Entry(string path, string xml) { using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(xml); }
            Entry("[Content_Types].xml", "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='xml' ContentType='application/xml'/><Override PartName='/xl/workbook.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml'/><Override PartName='/xl/worksheets/sheet1.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml'/><Override PartName='/xl/styles.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml'/></Types>");
            Entry("_rels/.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='xl/workbook.xml'/></Relationships>");
            Entry("xl/workbook.xml", new XElement(s + "workbook", new XAttribute(XNamespace.Xmlns + "r", r), new XElement(s + "sheets", new XElement(s + "sheet", new XAttribute("name", "البيانات"), new XAttribute("sheetId", 1), new XAttribute(r + "id", "rId1")))).ToString());
            Entry("xl/_rels/workbook.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='worksheets/sheet1.xml'/><Relationship Id='rId2' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles' Target='styles.xml'/></Relationships>");
            Entry("xl/worksheets/sheet1.xml", root.ToString());
            Entry("xl/styles.xml", "<styleSheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><fonts count='2'><font><sz val='11'/><name val='Tahoma'/></font><font><b/><color rgb='FFFFFFFF'/><sz val='11'/><name val='Tahoma'/></font></fonts><fills count='3'><fill><patternFill patternType='none'/></fill><fill><patternFill patternType='gray125'/></fill><fill><patternFill patternType='solid'><fgColor rgb='FF007E72'/><bgColor indexed='64'/></patternFill></fill></fills><borders count='1'><border/></borders><cellStyleXfs count='1'><xf numFmtId='0' fontId='0' fillId='0' borderId='0'/></cellStyleXfs><cellXfs count='4'><xf numFmtId='0' fontId='0' fillId='0' borderId='0' xfId='0'/><xf numFmtId='49' fontId='1' fillId='2' borderId='0' xfId='0' applyAlignment='1'><alignment horizontal='right' vertical='center' wrapText='1'/></xf><xf numFmtId='49' fontId='0' fillId='0' borderId='0' xfId='0' applyNumberFormat='1' applyAlignment='1'><alignment horizontal='right' vertical='center' wrapText='1'/></xf><xf numFmtId='4' fontId='0' fillId='0' borderId='0' xfId='0' applyNumberFormat='1'/></cellXfs><cellStyles count='1'><cellStyle name='Normal' xfId='0' builtinId='0'/></cellStyles></styleSheet>");
        }
        return output.ToArray();
    }
}
