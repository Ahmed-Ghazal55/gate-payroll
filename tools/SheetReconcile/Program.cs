using SheetReconcile;

static string DefaultSheet()=>"/home/ghazal/Projects/pro win/02 بيانات وتشغيل المنصات/01 كيتا/تحويلات رواتب التطبيقات شهر 09-2026 م.xlsx";
static string DefaultReport()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".agents/reviews/W1-golden-september.md");
static string PrivateRoot()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Private/gate-data/golden-2026-09");

if(args.Contains("--load-ninja"))
{
    string root=PrivateRoot();
    Directory.CreateDirectory(root);
    if(OperatingSystem.IsLinux()||OperatingSystem.IsMacOS())
        try{File.SetUnixFileMode(root,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);}catch{/* best-effort */}
    Console.Error.WriteLine("W1-M3 --load-ninja → "+root);
    var loaded=LoadNinja.Run(root);
    File.WriteAllText(Path.Combine(root,"load-notes.txt"),string.Join('\n',loaded.Notes)+"\n");
    string sheetPath=args.SkipWhile(a=>a!="--sheet").Skip(1).FirstOrDefault()??DefaultSheet();
    string reportPath=args.SkipWhile(a=>a!="--report").Skip(1).FirstOrDefault()??DefaultReport();
    var sheetAll=Reconcile.ReadSharedSheet(sheetPath);
    // Ninja-only: AA==AP — reload with AA filter via LoadNinja helper path (Due already AP; filter using notes cohort from ours keys ∩ sheet where we re-check via CompareNinjaOnly)
    var ninjaOnly=LoadNinja.FilterNinjaOnly(sheetPath);
    Console.Error.WriteLine($"ninja_only={ninjaOnly.Count}; ours={loaded.Ours.Count}");
    var report=Reconcile.CompareNinjaOnly(ninjaOnly,loaded.Ours,loaded.Notes);
    Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
    File.WriteAllText(reportPath,Reconcile.RenderGoldenMarkdown(report));
    Console.WriteLine(reportPath);
    foreach(var s in report.Stages)Console.WriteLine($"{s.Stage}\tmatched={s.Matched}\tmismatch={s.Mismatched}\tdiff={s.DiffSum}");
    Console.WriteLine($"classes a={report.ClassA.Count} b={report.ClassB.Count} c={report.ClassC.Count} d={report.ClassD.Count}");
    return;
}

string sheetPath2=args.ElementAtOrDefault(0)??DefaultSheet();
string reportPath2=args.ElementAtOrDefault(1)??DefaultReport();
string missing="Local September cycle DB with real invoices was not loaded in this package. "+
    "Tool reads the workbook (aggregates + hashed iqama only) and compares to an empty/synthetic ours map. "+
    "To complete golden: run with --load-ninja (writes only under ~/Private/gate-data/golden-2026-09/).";

Dictionary<string,SheetRow> ours=new(StringComparer.Ordinal);
if(args.ElementAtOrDefault(2)=="--synthetic")
{
    string hash=Reconcile.HashIqama("2000000001");
    ours[hash]=new SheetRow(hash,100,20,10,70,0,70);
}

List<SheetRow> sheet;
if(File.Exists(sheetPath2))
{
    Console.Error.WriteLine("reading "+sheetPath2);
    sheet=Reconcile.ReadSharedSheet(sheetPath2);
    Console.Error.WriteLine("usable rows "+sheet.Count);
}
else
{
    Console.Error.WriteLine("workbook missing — synthetic sheet only");
    string hash=Reconcile.HashIqama("2000000001");
    sheet=[new SheetRow(hash,100,20,10,70,0,70)];
    missing="Workbook path not found. Used synthetic fixture. "+missing;
}

var report2=Reconcile.Compare(sheet,ours,missing);
Directory.CreateDirectory(Path.GetDirectoryName(reportPath2)!);
File.WriteAllText(reportPath2,Reconcile.RenderMarkdown(report2));
Console.WriteLine(reportPath2);
foreach(var s in report2.Stages)Console.WriteLine($"{s.Stage}\tmatched={s.Matched}\tmismatch={s.Mismatched}\tdiff={s.DiffSum}");
