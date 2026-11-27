using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class ImportsStreamingTests
{
    static byte[] Book((string Name, string[][] Rows)[] sheets, string? shared = null)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main", rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            void Xml(string path, XElement xml) { using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); xml.Save(writer); }
            Xml("xl/workbook.xml", new(ns + "workbook", new XAttribute(XNamespace.Xmlns + "r", rel), new XElement(ns + "sheets", sheets.Select((s, i) => new XElement(ns + "sheet", new XAttribute("name", s.Name), new XAttribute(rel + "id", "r" + i))))));
            Xml("xl/_rels/workbook.xml.rels", new("Relationships", sheets.Select((_, i) => new XElement("Relationship", new XAttribute("Id", "r" + i), new XAttribute("Target", $"worksheets/s{i}.xml")))));
            string Col(int n) { string value = ""; for (n++; n > 0; n = (n - 1) / 26) value = (char)('A' + (n - 1) % 26) + value; return value; }
            if (shared != null)
                Xml("xl/sharedStrings.xml", new(ns + "sst", new XAttribute("count", 1), new XAttribute("uniqueCount", 1), new XElement(ns + "si", new XElement(ns + "t", shared))));
            for (int i = 0; i < sheets.Length; i++)
                Xml($"xl/worksheets/s{i}.xml", new(ns + "worksheet", new XElement(ns + "sheetData", sheets[i].Rows.Select((r, n) => new XElement(ns + "row", new XAttribute("r", n + 1), r.Select((v, c) =>
                    v.StartsWith("#s:", StringComparison.Ordinal)
                        ? new XElement(ns + "c", new XAttribute("r", Col(c) + (n + 1)), new XAttribute("t", "s"), new XElement(ns + "v", v[3..]))
                        : new XElement(ns + "c", new XAttribute("r", Col(c) + (n + 1)), new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", v)))))))));
        }
        return output.ToArray();
    }

    static byte[] PaddedSheetWorkbook(long uncompressedTarget)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main", rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            void Xml(string path, XElement xml) { using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); xml.Save(writer); }
            Xml("xl/workbook.xml", new(ns + "workbook", new XAttribute(XNamespace.Xmlns + "r", rel), new XElement(ns + "sheets", new XElement(ns + "sheet", new XAttribute("name", "SyntheticLarge"), new XAttribute(rel + "id", "r0")))));
            Xml("xl/_rels/workbook.xml.rels", new("Relationships", new XElement("Relationship", new XAttribute("Id", "r0"), new XAttribute("Target", "worksheets/s0.xml"))));
            var entry = zip.CreateEntry("xl/worksheets/s0.xml", CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
            writer.Write("""<row r="1"><c r="A1" t="inlineStr"><is><t>SYN_HEADER</t></is></c><c r="B1" t="inlineStr"><is><t>SYN_VALUE</t></is></c></row>""");
            writer.Write("""<row r="2"><c r="A2" t="inlineStr"><is><t>SYN_A2</t></is></c><c r="B2" t="inlineStr"><is><t>""");
            // Highly compressible padding so the zip stays small while uncompressed length exceeds the old 40 MB DOM cap.
            long remaining = Math.Max(0, uncompressedTarget - 512);
            var block = new string('Z', 64 * 1024);
            while (remaining > 0)
            {
                int take = (int)Math.Min(block.Length, remaining);
                writer.Write(block.AsSpan(0, take));
                remaining -= take;
            }
            writer.Write("""</t></is></c></row></sheetData></worksheet>""");
        }
        return output.ToArray();
    }

    [TestMethod]
    public void StreamingMatchesSmallInlineAndSharedStringWorkbooks()
    {
        var inline = Book([("Sheet1", [["H1", "H2"], ["A", "B"], ["", "C"]])]);
        var sheets = Imports.Read(inline, "small.xlsx");
        Assert.AreEqual(1, sheets.Count);
        Assert.AreEqual("Sheet1", sheets[0].Name);
        Assert.AreEqual(3, sheets[0].Rows.Count);
        CollectionAssert.AreEqual(new[] { "H1", "H2" }, sheets[0].Rows[0]);
        CollectionAssert.AreEqual(new[] { "A", "B" }, sheets[0].Rows[1]);
        CollectionAssert.AreEqual(new[] { "", "C" }, sheets[0].Rows[2]);

        var shared = Book([("Shared", [["#s:0", "plain"]])], shared: "SYN_SHARED");
        var sharedSheets = Imports.Read(shared, "shared.xlsx");
        Assert.AreEqual("SYN_SHARED", sharedSheets[0].Rows[0][0]);
        Assert.AreEqual("plain", sharedSheets[0].Rows[0][1]);
    }

    [TestMethod]
    public void StreamingReadsSheetPastOldTwentyFiveThousandRowCap()
    {
        const int rows = 26_000;
        var data = new string[rows][];
        data[0] = ["COL"];
        for (int i = 1; i < rows; i++) data[i] = ["R" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        var bytes = Book([("ManyRows", data)]);
        var sheets = Imports.Read(bytes, "many.xlsx");
        Assert.AreEqual(rows, sheets[0].Rows.Count);
        Assert.AreEqual("R25999", sheets[0].Rows[25999][0]);
    }

    [TestMethod]
    public void StreamingReadsSheetLargerThanOldFortyMegabyteDomCap()
    {
        var bytes = PaddedSheetWorkbook(45L * 1024 * 1024);
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
        {
            var entry = zip.GetEntry("xl/worksheets/s0.xml")!;
            Assert.IsTrue(entry.Length > 40L * 1024 * 1024, "fixture must exceed the old 40 MB uncompressed cap");
            Assert.IsTrue(entry.Length <= Imports.MaxSheetUncompressedBytes);
            Assert.IsTrue(bytes.Length < 2 * 1024 * 1024, "fixture zip must stay small");
        }
        var sheets = Imports.Read(bytes, "large.xlsx");
        Assert.AreEqual(1, sheets.Count);
        Assert.AreEqual(2, sheets[0].Rows.Count);
        Assert.AreEqual("SYN_HEADER", sheets[0].Rows[0][0]);
        Assert.AreEqual("SYN_A2", sheets[0].Rows[1][0]);
        Assert.IsTrue(sheets[0].Rows[1][1].Length > 40 * 1024 * 1024);
    }

    [TestMethod]
    public void SheetOverSafeUncompressedCapFailsWithArabicMessage()
    {
        var bytes = PaddedSheetWorkbook(Imports.MaxSheetUncompressedBytes + (2L * 1024 * 1024));
        var error = Assert.ThrowsExactly<ApiError>(() => Imports.Read(bytes, "too-large.xlsx"));
        Assert.Contains("250", error.Message);
        Assert.Contains("ميجابايت", error.Message);
    }

    [TestMethod]
    public void MissingFormulaCacheRowsStreamsAndKeetaCommitBatchesManyLines()
    {
        var headers = new[] { "المعرف", "الطلبات المُسلمة", "الإجمالي", "الخصم", "إجمالي المبلغ المستحق" };
        var rows = new List<string[]> { headers };
        for (int i = 1; i <= 250; i++)
            rows.Add(["SYN-K" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), "1", "10", "0", "11.5"]);
        var bytes = DebtSheetReviewTests.Book([("Synthetic", rows.ToArray())]);
        Assert.AreEqual(0, Imports.MissingFormulaCacheRows(bytes, "Synthetic", [0, 1, 2, 4]).Count);

        using var f = new HungerStationInvoiceTests.Fixture();
        f.Store.Write((c, tx) =>
        {
            for (int i = 1; i <= 250; i++)
            {
                string key = "SYN-K" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Store.Save(c, "platform_account", new() { ["id"] = "acc-" + key, ["externalAccountId"] = key, ["platformCode"] = "keeta", ["companyId"] = KeetaInvoiceTests.Company }, tx);
            }
            return true;
        });
        var input = KeetaInvoiceTests.Input();
        input["reference"] = "SYNTHETIC-KEETA-BATCH";
        var preview = f.Store.Read(c => KeetaInvoices.Preview(c, KeetaInvoiceTests.Actor(), input, bytes));
        var confirmed = input.Clone();
        confirmed["rows"] = preview["rows"]!.DeepClone();
        confirmed["fingerprint"] = preview.S("fingerprint");
        confirmed["confirm"] = true;
        var invoice = f.Store.Write((c, tx) => KeetaInvoices.Commit(c, tx, KeetaInvoiceTests.Actor(), confirmed, bytes));
        Assert.AreEqual(250, f.Store.Read(c => Store.List(c, "platform_invoice_line").Count(l => l.S("invoiceId") == invoice.S("id"))));
    }

    [TestMethod]
    public void FormulaCacheKeepsValueAfterFormulaElementAndFlagsMissingValue()
    {
        // <f>1+1</f><v>2</v> and shared <f t="shared"/><v>9</v> must keep the cached value (no Skip on <f>).
        var withCache = DebtSheetReviewTests.Book([("Synthetic", [
            ["المعرف", "الطلبات المُسلمة", "الإجمالي", "إجمالي المبلغ المستحق"],
            ["SYN-F1", "1", "=CACHED", "115"],
            ["SYN-F2", "1", "10", "11.5"]
        ])]);
        Assert.AreEqual(0, Imports.MissingFormulaCacheRows(withCache, "Synthetic", [0, 1, 2, 3]).Count);

        var sharedFormula = BookWithExplicitFormulaCells("Synthetic", [
            ("A1", "inline", "H"), ("B1", "inline", "V"),
            ("A2", "inline", "R"), ("B2", "shared-f", "9")
        ]);
        Assert.AreEqual(0, Imports.MissingFormulaCacheRows(sharedFormula, "Synthetic", [1]).Count);

        var missing = DebtSheetReviewTests.Book([("Synthetic", [
            ["المعرف", "الطلبات المُسلمة", "الإجمالي", "إجمالي المبلغ المستحق"],
            ["SYN-F3", "1", "=NOCACHE", "115"]
        ])]);
        var rows = Imports.MissingFormulaCacheRows(missing, "Synthetic", [0, 1, 2, 3]);
        Assert.AreEqual(1, rows.Count);
        Assert.IsTrue(rows.Contains(2));
    }

    [TestMethod]
    public void CountingStreamEnforcesCapEvenWhenCallerPassesLowerLimit()
    {
        var data = new byte[256];
        using var capped = Imports.LimitSheetBytes(new MemoryStream(data), 100);
        var buf = new byte[64];
        Assert.AreEqual(64, capped.Read(buf, 0, 64));
        var error = Assert.ThrowsExactly<ApiError>(() =>
        {
            while (capped.Read(buf, 0, 64) > 0) { }
        });
        Assert.Contains("250", error.Message);
        Assert.Contains("ميجابايت", error.Message);
    }

    static byte[] BookWithExplicitFormulaCells(string sheetName, (string Ref, string Kind, string Value)[] cells)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main", rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            void Xml(string path, XElement xml) { using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); xml.Save(writer); }
            Xml("xl/workbook.xml", new(ns + "workbook", new XAttribute(XNamespace.Xmlns + "r", rel), new XElement(ns + "sheets", new XElement(ns + "sheet", new XAttribute("name", sheetName), new XAttribute(rel + "id", "r0")))));
            Xml("xl/_rels/workbook.xml.rels", new("Relationships", new XElement("Relationship", new XAttribute("Id", "r0"), new XAttribute("Target", "worksheets/s0.xml"))));
            var byRow = cells.GroupBy(c => int.Parse(new string(c.Ref.SkipWhile(char.IsAsciiLetter).ToArray())));
            Xml("xl/worksheets/s0.xml", new(ns + "worksheet", new XElement(ns + "sheetData", byRow.Select(g =>
                new XElement(ns + "row", new XAttribute("r", g.Key), g.Select(c => c.Kind switch
                {
                    "inline" => new XElement(ns + "c", new XAttribute("r", c.Ref), new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", c.Value))),
                    "shared-f" => new XElement(ns + "c", new XAttribute("r", c.Ref), new XElement(ns + "f", new XAttribute("t", "shared"), new XAttribute("si", "0")), new XElement(ns + "v", c.Value)),
                    _ => new XElement(ns + "c", new XAttribute("r", c.Ref), new XElement(ns + "f", "1+1"), new XElement(ns + "v", c.Value))
                }))))));
        }
        return output.ToArray();
    }
}
