using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace SheetReconcile;

public sealed record SheetRow(string IqamaHash,decimal Due,decimal Deductions,decimal Fuel,decimal Net,decimal Instalment,decimal Transfer,string SkipReason="");
public sealed record StageDiff(string Stage,int Matched,int Mismatched,decimal DiffSum);
public sealed record ReconcileReport(IReadOnlyList<StageDiff> Stages,IReadOnlyList<(string IqamaHash,string Stage,decimal Sheet,decimal Ours,decimal Diff)> Mismatches,string MissingNote);

public static class Reconcile
{
    // المناديب المشتركين columns (0-based): A iqama, AP due, BF deductions, BN fuel, BO net, BP instalment, BQ transfer.
    public const int ColIqama=0,ColDue=41,ColDeductions=57,ColFuel=65,ColNet=66,ColInstalment=67,ColTransfer=68;
    public const string SharedSheet="المناديب المشتركين";
    static readonly HashSet<int> WantedCols=new(){ColIqama,ColDue,ColDeductions,ColFuel,ColNet,ColInstalment,ColTransfer};

    public static string HashIqama(string raw)
    {
        string digits=NormalizeIqamaDigits(raw);
        if(digits.Length==0)return "";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digits)))[..16];
    }

    public static bool IsExcludedLabel(string? cell)
    {
        if(string.IsNullOrWhiteSpace(cell))return true;
        string t=cell.Trim();
        if(t.Contains("SUBTOTAL",StringComparison.OrdinalIgnoreCase))return true;
        if(t.Contains("#REF!",StringComparison.OrdinalIgnoreCase))return true;
        if(t.Equals("Grand Total",StringComparison.OrdinalIgnoreCase))return true;
        if(t.Equals("Row Labels",StringComparison.OrdinalIgnoreCase))return true;
        return false;
    }

    public static List<SheetRow> ReadSharedSheet(string path)
    {
        using var zip=ZipFile.OpenRead(path);
        string sheetPath=ResolveSheetPath(zip,SharedSheet);
        var shared=LoadSharedStrings(zip);
        using var stream=zip.GetEntry(sheetPath)!.Open();
        using var reader=XmlReader.Create(stream,new XmlReaderSettings{IgnoreComments=true,IgnoreWhitespace=true});
        var rows=new List<SheetRow>();var seen=new HashSet<string>(StringComparer.Ordinal);
        bool headerSkipped=false;
        while(reader.Read())
        {
            if(reader.NodeType!=XmlNodeType.Element||reader.LocalName!="row")continue;
            Dictionary<int,string> cells;
            using(var rowXml=reader.ReadSubtree()){rowXml.Read();cells=ReadRowCells(rowXml,shared);}
            if(!headerSkipped){headerSkipped=true;continue;}
            string raw=cells.GetValueOrDefault(ColIqama,"");
            if(IsExcludedLabel(raw)){rows.Add(new SheetRow("",0,0,0,0,0,0,"excluded_label"));continue;}
            string digits=NormalizeIqamaDigits(raw);
            if(digits.Length!=10||digits[0] is not ('1' or '2')){rows.Add(new SheetRow("",0,0,0,0,0,0,"bad_iqama"));continue;}
            if(!seen.Add(digits)){rows.Add(new SheetRow(HashIqama(digits),0,0,0,0,0,0,"duplicate_iqama"));continue;}
            rows.Add(new SheetRow(
                HashIqama(digits),
                Money(cells.GetValueOrDefault(ColDue,"")),
                Money(cells.GetValueOrDefault(ColDeductions,"")),
                Money(cells.GetValueOrDefault(ColFuel,"")),
                Money(cells.GetValueOrDefault(ColNet,"")),
                Money(cells.GetValueOrDefault(ColInstalment,"")),
                Money(cells.GetValueOrDefault(ColTransfer,""))));
        }
        return rows.Where(r=>r.SkipReason=="").ToList();
    }

    /// <summary>Excel often stores 10-digit iqama as a float (e.g. 2.640335853E9).</summary>
    public static string NormalizeIqamaDigits(string raw)
    {
        string t=(raw??"").Trim();
        if(t.Length==0)return "";
        if(Regex.IsMatch(t,@"^\d+(\.\d+)?[Ee][+\-]?\d+$")||(t.Contains('.')&&decimal.TryParse(t,NumberStyles.Float,CultureInfo.InvariantCulture,out _)))
        {
            if(decimal.TryParse(t,NumberStyles.Float,CultureInfo.InvariantCulture,out var n))
                return decimal.Truncate(n).ToString("0",CultureInfo.InvariantCulture);
        }
        return Regex.Replace(t,"[^0-9]","");
    }

    public static ReconcileReport Compare(IReadOnlyList<SheetRow> sheet,IReadOnlyDictionary<string,SheetRow> ours,string missingNote="")
    {
        (string Name,Func<SheetRow,decimal> Pick)[] stages=[("due",r=>r.Due),("deductions",r=>r.Deductions),("fuel",r=>r.Fuel),("net",r=>r.Net),("instalment",r=>r.Instalment),("transfer",r=>r.Transfer)];
        var diffs=new List<StageDiff>();var mismatches=new List<(string IqamaHash,string Stage,decimal Sheet,decimal Ours,decimal Diff)>();
        foreach(var stage in stages)
        {
            int matched=0,mis=0;decimal sum=0;
            foreach(var s in sheet)
            {
                if(!ours.TryGetValue(s.IqamaHash,out var o)){mis++;sum+=stage.Pick(s);mismatches.Add((s.IqamaHash,stage.Name,stage.Pick(s),0,stage.Pick(s)));continue;}
                decimal a=stage.Pick(s),b=stage.Pick(o),d=a-b;
                if(d==0)matched++;else{mis++;sum+=d;mismatches.Add((s.IqamaHash,stage.Name,a,b,d));}
            }
            diffs.Add(new StageDiff(stage.Name,matched,mis,sum));
        }
        return new ReconcileReport(diffs,mismatches,missingNote);
    }

    public static string RenderMarkdown(ReconcileReport report)
    {
        var sb=new StringBuilder();
        sb.AppendLine("# W1 golden September reconciliation");
        sb.AppendLine();
        sb.AppendLine("Aggregates only — iqama values are SHA-256 prefixes.");
        sb.AppendLine();
        if(report.MissingNote!=""){sb.AppendLine("## Missing inputs");sb.AppendLine(report.MissingNote);sb.AppendLine();}
        sb.AppendLine("| Stage | Matched | Mismatched | Diff sum |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach(var s in report.Stages)sb.AppendLine($"| {s.Stage} | {s.Matched} | {s.Mismatched} | {s.DiffSum:0.00} |");
        sb.AppendLine();
        sb.AppendLine($"Mismatch rows (hashed): {report.Mismatches.Count}");
        sb.AppendLine($"Sheet riders (usable): {report.Stages.FirstOrDefault()?.Matched+report.Stages.FirstOrDefault()?.Mismatched ?? 0}");
        return sb.ToString();
    }

    static string ResolveSheetPath(ZipArchive zip,string sheetName)
    {
        using var wbStream=zip.GetEntry("xl/workbook.xml")!.Open();
        using var wb=XmlReader.Create(wbStream);
        string? rId=null;
        while(wb.Read())
        {
            if(wb.NodeType!=XmlNodeType.Element||wb.LocalName!="sheet")continue;
            if(wb.GetAttribute("name")==sheetName){rId=wb.GetAttribute("id","http://schemas.openxmlformats.org/officeDocument/2006/relationships");break;}
        }
        if(rId==null)throw new InvalidOperationException("sheet missing: "+sheetName);
        using var relStream=zip.GetEntry("xl/_rels/workbook.xml.rels")!.Open();
        using var rels=XmlReader.Create(relStream);
        while(rels.Read())
        {
            if(rels.NodeType!=XmlNodeType.Element||rels.LocalName!="Relationship")continue;
            if(rels.GetAttribute("Id")==rId)
            {
                string target=rels.GetAttribute("Target")??throw new InvalidOperationException("empty Target");
                return "xl/"+target.TrimStart('/');
            }
        }
        throw new InvalidOperationException("relationship missing: "+rId);
    }

    static List<string> LoadSharedStrings(ZipArchive zip)
    {
        var list=new List<string>();
        var entry=zip.GetEntry("xl/sharedStrings.xml");
        if(entry==null)return list;
        using var stream=entry.Open();
        using var reader=XmlReader.Create(stream,new XmlReaderSettings{IgnoreComments=true});
        while(reader.Read())
        {
            if(reader.NodeType!=XmlNodeType.Element||reader.LocalName!="si")continue;
            list.Add(ReadSharedStringItem(reader));
        }
        return list;
    }

    static string ReadSharedStringItem(XmlReader reader)
    {
        var sb=new StringBuilder();
        if(reader.IsEmptyElement)return "";
        int depth=reader.Depth;
        while(reader.Read()&&!(reader.NodeType==XmlNodeType.EndElement&&reader.LocalName=="si"&&reader.Depth==depth))
        {
            if(reader.NodeType==XmlNodeType.Element&&reader.LocalName=="t")sb.Append(reader.ReadElementContentAsString());
        }
        return sb.ToString();
    }

    static Dictionary<int,string> ReadRowCells(XmlReader rowXml,IReadOnlyList<string> shared)
    {
        var cells=new Dictionary<int,string>();
        while(rowXml.Read())
        {
            if(rowXml.NodeType!=XmlNodeType.Element||rowXml.LocalName!="c")continue;
            int col=ColumnIndex(rowXml.GetAttribute("r"));
            string? type=rowXml.GetAttribute("t");
            // Always ReadSubtree so Skip()+Read does not double-advance past the next cell.
            string value="";
            using(var cellXml=rowXml.ReadSubtree())
            {
                if(!WantedCols.Contains(col))continue;
                while(cellXml.Read())
                {
                    if(cellXml.NodeType!=XmlNodeType.Element)continue;
                    if(cellXml.LocalName=="v")value=cellXml.ReadElementContentAsString();
                    else if(cellXml.LocalName=="t")value=cellXml.ReadElementContentAsString();
                }
            }
            if(!WantedCols.Contains(col))continue;
            if(type=="s"&&int.TryParse(value,out int si)&&si>=0&&si<shared.Count)value=shared[si];
            cells[col]=value;
        }
        return cells;
    }

    static decimal Money(string s)
    {
        if(string.IsNullOrWhiteSpace(s)||s.Contains("#REF!",StringComparison.Ordinal))return 0;
        s=s.Replace(",","").Replace("\u00a0","").Trim();
        return decimal.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var n)?decimal.Round(n,2,MidpointRounding.AwayFromZero):0;
    }

    static int ColumnIndex(string? cellRef)
    {
        if(string.IsNullOrEmpty(cellRef))return -1;
        int i=0,col=0;while(i<cellRef.Length&&char.IsLetter(cellRef[i])){col=col*26+(char.ToUpperInvariant(cellRef[i])-'A'+1);i++;}
        return col-1;
    }
}
