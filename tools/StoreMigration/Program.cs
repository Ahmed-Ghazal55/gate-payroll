using System.Text.Json.Nodes;
using Payroll;
if(args.Length<2 || args[0] is not ("export" or "preview" or "import"))throw new ArgumentException("Usage: export|preview|import PRIVATE-SNAPSHOT.json [FINGERPRINT]. Set PAYROLL_MIGRATION_DATA_DIR and optional PAYROLL_MIGRATION_SQL privately.");
string dir=Environment.GetEnvironmentVariable("PAYROLL_MIGRATION_DATA_DIR")??throw new InvalidOperationException("Explicit PAYROLL_MIGRATION_DATA_DIR required.");
string file=Path.GetFullPath(args[1]);
if(args[0]=="preview")
{
    var records=JsonNode.Parse(File.ReadAllText(file))!.AsArray();Console.WriteLine($"Records: {records.Count}; fingerprint: {StorageTransfer.Fingerprint(records)}");return;
}
var store=new Store(dir,Environment.GetEnvironmentVariable("PAYROLL_MIGRATION_SQL"));
if(args[0]=="export")
{
    var records=StorageTransfer.Export(store);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    using var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None);
    if(!OperatingSystem.IsWindows())File.SetUnixFileMode(file,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    using var writer=new StreamWriter(stream);writer.Write(records.ToJsonString());Console.WriteLine($"Exported {records.Count} records. Keep snapshot and Data Protection keys private.");
}
else
{
    if(args.Length!=3)throw new ArgumentException("Import requires the reviewed snapshot fingerprint.");
    var records=JsonNode.Parse(File.ReadAllText(file))!.AsArray();Console.WriteLine($"Imported {StorageTransfer.Import(store,records,args[2])} records atomically. No existing destination records were replaced.");
}
