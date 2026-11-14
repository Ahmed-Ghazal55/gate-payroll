using SheetReconcile;

static string DefaultSheet()=>"/home/ghazal/Projects/pro win/02 بيانات وتشغيل المنصات/01 كيتا/تحويلات رواتب التطبيقات شهر 09-2026 م.xlsx";
static string DefaultReport()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".agents/reviews/W1-golden-september.md");

string sheetPath=args.ElementAtOrDefault(0)??DefaultSheet();
string reportPath=args.ElementAtOrDefault(1)??DefaultReport();
string missing="Local September cycle DB with real invoices was not loaded in this package. "+
    "Tool reads the workbook (aggregates + hashed iqama only) and compares to an empty/synthetic ours map. "+
    "To complete golden: load September invoices + frozen inputs into a local DB (never commit) and pass cycle results.";

Dictionary<string,SheetRow> ours=new(StringComparer.Ordinal);
if(args.ElementAtOrDefault(2)=="--synthetic")
{
    // Fixture: one hashed rider matching a synthetic sheet row for self-test when workbook absent.
    string hash=Reconcile.HashIqama("2000000001");
    ours[hash]=new SheetRow(hash,100,20,10,70,0,70);
}

List<SheetRow> sheet;
if(File.Exists(sheetPath))
{
    Console.Error.WriteLine("reading "+sheetPath);
    sheet=Reconcile.ReadSharedSheet(sheetPath);
    Console.Error.WriteLine("usable rows "+sheet.Count);
}
else
{
    Console.Error.WriteLine("workbook missing — synthetic sheet only");
    string hash=Reconcile.HashIqama("2000000001");
    sheet=[new SheetRow(hash,100,20,10,70,0,70)];
    missing="Workbook path not found. Used synthetic fixture. "+missing;
}

var report=Reconcile.Compare(sheet,ours,missing);
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.WriteAllText(reportPath,Reconcile.RenderMarkdown(report));
Console.WriteLine(reportPath);
foreach(var s in report.Stages)Console.WriteLine($"{s.Stage}\tmatched={s.Matched}\tmismatch={s.Mismatched}\tdiff={s.DiffSum}");
