using SheetReconcile;

static string DefaultSheet()=>"/home/ghazal/Projects/pro win/02 بيانات وتشغيل المنصات/01 كيتا/تحويلات رواتب التطبيقات شهر 09-2026 م.xlsx";
static string DefaultReport()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".agents/reviews/W1-golden-september.md");

string sheetPath=args.ElementAtOrDefault(0)??DefaultSheet();
string reportPath=args.ElementAtOrDefault(1)??DefaultReport();
string missing="Local September cycle DB was not loaded here. "+
    "Compare workbook (aggregates + hashed iqama) to an empty/synthetic ours map. "+
    "Full golden: run tools/GoldenNinja (writes only under ~/Private/gate-data/golden-2026-09/).";

Dictionary<string,SheetRow> ours=new(StringComparer.Ordinal);
if(args.ElementAtOrDefault(2)=="--synthetic")
{
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
