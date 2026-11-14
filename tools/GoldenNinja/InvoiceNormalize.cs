using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
namespace GoldenNinja;

/// <summary>Rewrite September Ninja supplier workbooks to the 43-column header set NinjaBilling expects.</summary>
public static class InvoiceNormalize
{
    static readonly (string From,string To)[] Renames=
    [
        ("Target Level  & Type","Target Level Type"),
        ("NAQIL BLOCK","NAQIL BLOCKED"),
        ("On demand","on demand"),
        ("Weekend 37 Bonus","Weekend 40 Bonus"),
        ("National Day Bonus","100 SAR BONUS"),
        ("National Day Bonus ","100 SAR BONUS"),
    ];

    public static byte[] Normalize(byte[] source)
    {
        // Read via Imports so shared-string / typed cells match production.
        var sheets=Payroll.Imports.Read(source,"invoice.xlsx");
        var sheet=sheets.First(s=>s.Rows.Count>0&&s.Rows[0].Any(h=>h.Trim().Equals("captain_id",StringComparison.OrdinalIgnoreCase)));
        var header=sheet.Rows[0].Select(h=>h.Replace("\n"," ").Trim()).ToList();

        // Map duplicate Weekend 38 → 42 then 43
        int w38=0;
        for(int i=0;i<header.Count;i++)
        {
            string h=header[i];
            foreach(var (from,to) in Renames)
                if(h.Equals(from,StringComparison.OrdinalIgnoreCase)){header[i]=to;h=to;break;}
            if(h.Equals("Weekend 38 Bonus",StringComparison.OrdinalIgnoreCase)||h.Equals("Weekend 38 Bonus2",StringComparison.OrdinalIgnoreCase))
            {
                header[i]=w38==0?"Weekend 42 Bonus":"Weekend 43 Bonus";
                w38++;
            }
        }

        bool Has(string name)=>header.Any(h=>h.Equals(name,StringComparison.OrdinalIgnoreCase));
        void Ensure(string name)
        {
            if(Has(name))return;
            header.Add(name);
            for(int r=1;r<sheet.Rows.Count;r++)
            {
                while(sheet.Rows[r].Count<header.Count-1)sheet.Rows[r].Add("");
                sheet.Rows[r].Add("0");
            }
        }
        Ensure("CAPTAIN ACTIVE STATUS");
        Ensure("STOPS WITHOUT NOTICE DEDUCTION");
        Ensure("Weekend 40 Bonus");
        Ensure("Weekend 42 Bonus");
        Ensure("Weekend 43 Bonus");
        Ensure("100 SAR BONUS");
        Ensure("NAQIL BLOCKED");
        Ensure("on demand");
        Ensure("Target Level Type");

        // Align every data row to header width
        var rows=new List<List<string>>{header};
        for(int r=1;r<sheet.Rows.Count;r++)
        {
            var row=sheet.Rows[r].ToList();
            while(row.Count<header.Count)row.Add("");
            if(row.Count>header.Count)row=row.Take(header.Count).ToList();
            // empty captain skip later
            rows.Add(row);
        }
        return WriteSheet(rows);
    }

    static byte[] WriteSheet(List<List<string>> rows)
    {
        using var memory=new MemoryStream();
        using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))
        {
            void Entry(string name,string xml){using var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(xml);}
            XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            Entry("[Content_Types].xml","<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='xml' ContentType='application/xml'/><Override PartName='/xl/workbook.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml'/><Override PartName='/xl/worksheets/sheet1.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml'/></Types>");
            Entry("_rels/.rels","<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='xl/workbook.xml'/></Relationships>");
            Entry("xl/workbook.xml",new XElement(ns+"workbook",new XAttribute(XNamespace.Xmlns+"r",rel),new XElement(ns+"sheets",new XElement(ns+"sheet",new XAttribute("name","Sheet1"),new XAttribute(rel+"id","r1")))).ToString());
            Entry("xl/_rels/workbook.xml.rels","<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='r1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='worksheets/sheet1.xml'/></Relationships>");
            string Col(int index){string result="";for(int n=index+1;n>0;n=(n-1)/26)result=(char)('A'+(n-1)%26)+result;return result;}
            XElement Cells(List<string> values,int number)=>new(ns+"row",new XAttribute("r",number),values.Select((v,n)=>new XElement(ns+"c",new XAttribute("r",Col(n)+number),new XAttribute("t","inlineStr"),new XElement(ns+"is",new XElement(ns+"t",v??"")))));
            Entry("xl/worksheets/sheet1.xml",new XElement(ns+"worksheet",new XElement(ns+"sheetData",rows.Select((r,i)=>Cells(r,i+1)))).ToString());
        }
        return memory.ToArray();
    }
}
