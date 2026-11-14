using GoldenNinja;
using SheetReconcile;

static string DefaultSheet()=>"/home/ghazal/Projects/pro win/02 بيانات وتشغيل المنصات/01 كيتا/تحويلات رواتب التطبيقات شهر 09-2026 م.xlsx";
static string DefaultReport()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".agents/reviews/W1-golden-september.md");
static string PrivateRoot()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Private/gate-data/golden-2026-09");

string root=args.SkipWhile(a=>a!="--root").Skip(1).FirstOrDefault()??PrivateRoot();
Directory.CreateDirectory(root);
if(OperatingSystem.IsLinux()||OperatingSystem.IsMacOS())
    try{File.SetUnixFileMode(root,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);}catch{/* best-effort */}
Console.Error.WriteLine("W1-M3 GoldenNinja → "+root);
var loaded=LoadNinja.Run(root);
File.WriteAllText(Path.Combine(root,"load-notes.txt"),string.Join('\n',loaded.Notes)+"\n");
string sheetPath=args.SkipWhile(a=>a!="--sheet").Skip(1).FirstOrDefault()??DefaultSheet();
string reportPath=args.SkipWhile(a=>a!="--report").Skip(1).FirstOrDefault()??DefaultReport();
var ninjaOnly=LoadNinja.FilterNinjaOnly(sheetPath);
Console.Error.WriteLine($"ninja_only={ninjaOnly.Count}; ours={loaded.Ours.Count}");
var report=Reconcile.CompareNinjaOnly(ninjaOnly,loaded.Ours,loaded.Notes);
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.WriteAllText(reportPath,Reconcile.RenderGoldenMarkdown(report));
Console.WriteLine(reportPath);
foreach(var s in report.Stages)Console.WriteLine($"{s.Stage}\tmatched={s.Matched}\tmismatch={s.Mismatched}\tdiff={s.DiffSum}");
Console.WriteLine($"classes a={report.ClassA.Count} b={report.ClassB.Count} c={report.ClassC.Count} d={report.ClassD.Count}");
