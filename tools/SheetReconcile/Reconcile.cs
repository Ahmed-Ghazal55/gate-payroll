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
public sealed record ClassifiedMismatch(string IqamaHash,string Stage,decimal Sheet,decimal Ours,decimal Diff,char Class,string Note);
public sealed record GoldenReport(
    int ScopeCount,
    IReadOnlyList<StageDiff> Stages,
    IReadOnlyList<ClassifiedMismatch> ClassA,
    IReadOnlyList<ClassifiedMismatch> ClassB,
    IReadOnlyList<ClassifiedMismatch> ClassC,
    IReadOnlyList<ClassifiedMismatch> ClassD,
    IReadOnlyList<string> Notes,
    decimal Tolerance=1m);

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
    /// <summary>Collapse line breaks and repeated spaces so a header can be matched exactly.</summary>
    public static string NormalizeHeader(string? raw)
        => Regex.Replace((raw??"").Replace('\n',' ').Replace('\r',' '),@"\s+"," ").Trim();

    /// <summary>Last column whose normalised header equals <paramref name="name"/>. Never a substring match.</summary>
    public static int ExactColumn(IReadOnlyList<string> headers,string name)
    {
        int found=-1;
        for(int i=0;i<headers.Count;i++)
            if(NormalizeHeader(headers[i])==name)found=i;
        return found;
    }

    /// <summary>
    /// Sheet salary: day value × days + per-order × orders + bonus (orders−450)×8 when orders &gt; 450 + net allowance.
    /// Net allowance must be ≥ 0 because a salary band's internet field cannot be negative.
    /// </summary>
    public static bool TrySalaryEarned(decimal dayValue,decimal orderRate,decimal allowance,int orders,int days,out decimal earned)
    {
        earned=0;
        if(dayValue<0||orderRate<0||allowance<0||orders<0||days<0)return false;
        if(dayValue==0&&orderRate==0)return false;
        decimal bonus=orders>450?(orders-450)*8m:0;
        earned=dayValue*days+orderRate*orders+bonus+allowance;
        return true;
    }

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

    /// <summary>±1 SAR per rider (§17 m1). Classifies every mismatch a–d.</summary>
    public static GoldenReport CompareNinjaOnly(IReadOnlyList<SheetRow> sheet,IReadOnlyDictionary<string,SheetRow> ours,IReadOnlyList<string> notes,decimal tolerance=1m)
    {
        (string Name,Func<SheetRow,decimal> Pick)[] stages=[("due",r=>r.Due),("deductions",r=>r.Deductions),("fuel",r=>r.Fuel),("stage4_total",r=>r.Deductions+r.Fuel),("net",r=>r.Net),("instalment",r=>r.Instalment),("transfer",r=>r.Transfer)];
        var diffs=new List<StageDiff>();
        var classified=new List<ClassifiedMismatch>();
        foreach(var stage in stages)
        {
            int matched=0,mis=0;decimal sum=0;
            foreach(var s in sheet)
            {
                ours.TryGetValue(s.IqamaHash,out var o);
                o??=new SheetRow(s.IqamaHash,0,0,0,0,0,0,"missing_ours");
                decimal a=stage.Pick(s),b=stage.Pick(o),d=a-b;
                if(Math.Abs(d)<=tolerance){matched++;continue;}
                mis++;sum+=d;
                classified.Add(Classify(s.IqamaHash,stage.Name,a,b,d,o.SkipReason,notes));
            }
            diffs.Add(new StageDiff(stage.Name,matched,mis,sum));
        }
        return new GoldenReport(sheet.Count,diffs,
            classified.Where(c=>c.Class=='a').ToList(),
            classified.Where(c=>c.Class=='b').ToList(),
            classified.Where(c=>c.Class=='c').ToList(),
            classified.Where(c=>c.Class=='d').ToList(),
            notes.ToList(),tolerance);
    }

    static ClassifiedMismatch Classify(string hash,string stage,decimal sheet,decimal ours,decimal diff,string skip,IReadOnlyList<string> notes)
    {
        // (b) only when the loader already recomputed both numbers and named the rule and the sheet column.
        string? hand=notes.FirstOrDefault(n=>n.StartsWith("classb:"+hash+":"+stage+":",StringComparison.Ordinal));
        if(hand!=null)
        {
            string text=hand[("classb:"+hash+":"+stage+":").Length..];
            if(text.Contains('§')&&text.Contains("sheet ",StringComparison.Ordinal))
                return new(hash,stage,sheet,ours,diff,'b',text);
        }
        string? sheetErr=notes.FirstOrDefault(n=>n.StartsWith("classc:"+hash+":"+stage+":",StringComparison.Ordinal));
        if(sheetErr!=null)
            return new(hash,stage,sheet,ours,diff,'c',sheetErr[("classc:"+hash+":"+stage+":").Length..]);
        if(skip=="missing_ours")
        {
            string? why=notes.FirstOrDefault(n=>n.StartsWith("missing:"+hash+":",StringComparison.Ordinal));
            return new(hash,stage,sheet,ours,diff,'d',why==null?"ours missing — no cycle row":why[("missing:"+hash+":").Length..]);
        }
        return new(hash,stage,sheet,ours,diff,'d',"unexplained — not relabelled");
    }

    public static string RenderGoldenMarkdown(GoldenReport report)
    {
        var sb=new StringBuilder();
        sb.AppendLine("# W1 golden September reconciliation (Ninja-only)");
        sb.AppendLine();
        sb.AppendLine("Aggregates only — iqama values are SHA-256 prefixes (16 hex).");
        sb.AppendLine($"Scope (AA==AP): **{report.ScopeCount}** riders. Tolerance ±{report.Tolerance:0.##} SAR.");
        sb.AppendLine();
        sb.AppendLine("## Stage match");
        sb.AppendLine("| Stage | Matched | Mismatched | Diff sum | Match % |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach(var s in report.Stages)
        {
            int total=s.Matched+s.Mismatched;
            decimal pct=total==0?0:100m*s.Matched/total;
            sb.AppendLine($"| {s.Stage} | {s.Matched} | {s.Mismatched} | {s.DiffSum:0.00} | {pct:0.0}% |");
        }
        sb.AppendLine();
        sb.AppendLine("## Classes");
        sb.AppendLine($"| Class | Count | Meaning |");
        sb.AppendLine($"|---|---:|---|");
        sb.AppendLine($"| (a) our bug | {report.ClassA.Count} | engine defect — fix with test |");
        sb.AppendLine($"| (b) §40 by design | {report.ClassB.Count} | intentional rule difference |");
        sb.AppendLine($"| (c) sheet error | {report.ClassC.Count} | arithmetic / paste / SUBTOTAL |");
        sb.AppendLine($"| (d) unknown | {report.ClassD.Count} | question for Ghazal |");
        sb.AppendLine();
        void Examples(string title,IReadOnlyList<ClassifiedMismatch> rows)
        {
            sb.AppendLine($"### {title}");
            if(rows.Count==0){sb.AppendLine("_none_");sb.AppendLine();return;}
            foreach(var r in rows.Take(20))
                sb.AppendLine($"- `{r.IqamaHash}` {r.Stage}: sheet={r.Sheet:0.00} ours={r.Ours:0.00} diff={r.Diff:0.00} — {r.Note}");
            if(rows.Count>20)sb.AppendLine($"- … +{rows.Count-20} more");
            sb.AppendLine();
        }
        Examples("(a) examples",report.ClassA);
        Examples("(b) examples",report.ClassB);
        Examples("(c) examples",report.ClassC);
        Examples("(d) questions",report.ClassD);
        sb.AppendLine("## Loader notes");
        foreach(var n in report.Notes.Take(40))sb.AppendLine("- "+n);
        sb.AppendLine();
        sb.AppendLine("## Sources");
        sb.AppendLine("- Inputs: original Ninja invoices + ops (Private store only).");
        sb.AppendLine("- Stage-4 AQ–BE and fuel BN: from transfers workbook («من ملف التحويلات، بدون مصدر أصلي»).");
        sb.AppendLine("- Answer key: المناديب المشتركين AA/AP/BF/BN/BO/BP/BQ.");
        sb.AppendLine();
        sb.AppendLine("## Done criteria");
        int stage3=report.Stages.First(s=>s.Stage=="due").Matched;
        int stage3Mis=report.Stages.First(s=>s.Stage=="due").Mismatched;
        int bq=report.Stages.First(s=>s.Stage=="transfer").Matched;
        int bqMis=report.Stages.First(s=>s.Stage=="transfer").Mismatched;
        decimal s3pct=100m*stage3/Math.Max(1,stage3+stage3Mis),bqPct=100m*bq/Math.Max(1,bq+bqMis);
        bool explained=report.ClassA.Count==0&&report.ClassD.Count==0;
        bool explainedLoose=report.ClassA.Count==0&&report.ClassD.All(c=>c.Note.Contains("ours missing",StringComparison.Ordinal));
        sb.AppendLine($"- (a) count: {report.ClassA.Count} (must be 0)");
        sb.AppendLine($"- Stage-3 (due) match: {s3pct:0.0}% (matched {stage3}/{stage3+stage3Mis})");
        sb.AppendLine($"- BQ (transfer) match: {bqPct:0.0}% (matched {bq}/{bq+bqMis})");
        sb.AppendLine($"- Gap explained by (b)+(c) only: {(explained?"yes":explainedLoose?"mostly — remaining (d) are missing-ours binds":"no — see (d)")}");
        sb.AppendLine($"- BF match: {report.Stages.First(s=>s.Stage=="deductions").Matched}/{report.ScopeCount}; BN/stage4 ≈ {report.Stages.First(s=>s.Stage=="fuel").Matched}/{report.ScopeCount}; BP ≈ {report.Stages.First(s=>s.Stage=="instalment").Matched}/{report.ScopeCount}");
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
