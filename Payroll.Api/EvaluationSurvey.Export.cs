using System.IO.Compression;
using System.Xml.Linq;

namespace Payroll;

public static partial class EvaluationSurvey
{
    // Reuse the existing inline-string writer; split large exports so every response is included.
    public static byte[] ExportWorkbook(IReadOnlyList<ExcelColumn> columns, IReadOnlyList<object?[]> rows)
    {
        const int sheetLimit = 24999;
        if (rows.Count <= sheetLimit) return ExcelFiles.Write(columns, rows);
        using var firstStream = new MemoryStream(ExcelFiles.Write(columns, rows.Take(sheetLimit).ToList()));
        using var first = new ZipArchive(firstStream, ZipArchiveMode.Read);
        XDocument Read(string name) { using var stream = first.GetEntry(name)!.Open(); return XDocument.Load(stream); }
        var workbook = Read("xl/workbook.xml"); var relations = Read("xl/_rels/workbook.xml.rels"); var content = Read("[Content_Types].xml");
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace docRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";
        workbook.Root!.Element(main + "sheets")!.RemoveNodes();
        relations.Root!.Elements().Where(e => ((string?)e.Attribute("Type"))?.EndsWith("/worksheet", StringComparison.Ordinal) == true).Remove();
        content.Root!.Elements().Where(e => ((string?)e.Attribute("PartName"))?.StartsWith("/xl/worksheets/", StringComparison.Ordinal) == true).Remove();
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Copy(ZipArchiveEntry entry, string name) { using var source = entry.Open(); using var target = zip.CreateEntry(name).Open(); source.CopyTo(target); }
            foreach (var entry in first.Entries.Where(e => e.FullName is not "xl/workbook.xml" and not "xl/_rels/workbook.xml.rels" and not "[Content_Types].xml" and not "xl/worksheets/sheet1.xml")) Copy(entry, entry.FullName);
            int sheets = (rows.Count - 1) / sheetLimit + 1;
            for (int n = 1; n <= sheets; n++)
            {
                string name = $"sheet{n}.xml", rel = $"evaluationSheet{n}";
                if (n == 1) Copy(first.GetEntry("xl/worksheets/sheet1.xml")!, "xl/worksheets/" + name);
                else
                {
                    using var partStream = new MemoryStream(ExcelFiles.Write(columns, rows.Skip((n - 1) * sheetLimit).Take(sheetLimit).ToList()));
                    using var part = new ZipArchive(partStream, ZipArchiveMode.Read);
                    Copy(part.GetEntry("xl/worksheets/sheet1.xml")!, "xl/worksheets/" + name);
                }
                workbook.Root.Element(main + "sheets")!.Add(new XElement(main + "sheet", new XAttribute("name", "التقييمات " + n), new XAttribute("sheetId", n), new XAttribute(docRel + "id", rel)));
                relations.Root.Add(new XElement(packageRel + "Relationship", new XAttribute("Id", rel), new XAttribute("Type", docRel.NamespaceName + "/worksheet"), new XAttribute("Target", "worksheets/" + name)));
                content.Root.Add(new XElement(types + "Override", new XAttribute("PartName", "/xl/worksheets/" + name), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
            }
            foreach (var (name, xml) in new[] { ("xl/workbook.xml", workbook), ("xl/_rels/workbook.xml.rels", relations), ("[Content_Types].xml", content) })
            { using var target = zip.CreateEntry(name).Open(); xml.Save(target); }
        }
        return output.ToArray();
    }
}
