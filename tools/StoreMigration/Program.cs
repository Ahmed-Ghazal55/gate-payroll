using System.Data;
using System.Data.Common;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Payroll;

// Operator-only: no API endpoint. Credentials are supplied by Ghazal's process, never read from a file.
// L2: pay.PayRunInput / pay_run_input is additive dual-engine (not records JSON). Freeze-row cutover is a follow-up; Ensure creates the empty table on target boot.
try { Run(args); }
catch { Console.Error.WriteLine("Store migration refused/failed. No private exception details are printed; retain maintenance and inspect the counts-only plan."); Environment.ExitCode=1; }

static void SavePrivate(string file,JsonNode value)
{
    string full=Path.GetFullPath(file),dir=Path.GetDirectoryName(full)!;
    string allowed=Path.GetFullPath(Environment.GetEnvironmentVariable("PAYROLL_CUTOVER_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Private/gate-data/cutover"));
    if(!full.StartsWith(allowed+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw new InvalidOperationException();
    for(var ancestor=new DirectoryInfo(dir);ancestor!=null;ancestor=ancestor.Parent)
        if(File.Exists(Path.Combine(ancestor.FullName,".git"))||Directory.Exists(Path.Combine(ancestor.FullName,".git")))throw new InvalidOperationException("Private cutover files cannot be written inside a repository.");
    Directory.CreateDirectory(dir);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(dir,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
    using var f=new FileStream(full,FileMode.CreateNew,FileAccess.Write,FileShare.None);
    if(!OperatingSystem.IsWindows())File.SetUnixFileMode(full,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    using var w=new StreamWriter(f);w.Write(value.ToJsonString());
}
static DbConnection Target()
{
    var b=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PAYROLL_MIGRATION_SQL")??throw new InvalidOperationException());
    bool synthetic=b.InitialCatalog.StartsWith("GateCutoverTest_",StringComparison.Ordinal)&&System.Text.RegularExpressions.Regex.IsMatch(b.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if(b.InitialCatalog!="db77738"&&!synthetic)throw new InvalidOperationException();
    var c=new SqlConnection(b.ConnectionString);c.Open();using var session=c.CreateCommand();session.CommandText="SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;";session.ExecuteNonQuery();return c;
}
static void Run(string[] args)
{
    if(args.Length<2)throw new ArgumentException();
    switch(args[0])
    {
        case "snapshot" when args.Length==3:
        {
            var b=new SqliteConnectionStringBuilder{DataSource=Path.GetFullPath(args[1]),Mode=SqliteOpenMode.ReadOnly,Pooling=false};using var c=new SqliteConnection(b.ToString());c.Open();
            using(var q=c.CreateCommand()){q.CommandText="PRAGMA integrity_check";if((string?)q.ExecuteScalar()!="ok")throw new InvalidOperationException();}
            using var tx=c.BeginTransaction(deferred:true);var rows=StorageTransfer.ReadSnapshot(c,tx);tx.Commit();SavePrivate(args[2],rows);
            Console.WriteLine(new JsonObject{["records"]=rows.Count,["snapshotFingerprint"]=StorageTransfer.CutoverHash(rows)}.ToJsonString());return;
        }
        case "merge-preview" when args.Length==3:
        {
            var source=JsonNode.Parse(File.ReadAllText(args[1]))!.AsArray();using var c=Target();using var tx=c.BeginTransaction(IsolationLevel.Serializable);
            var target=StorageTransfer.ReadSnapshot(c,tx);tx.Commit();var plan=StorageTransfer.PlanMerge(source,target);SavePrivate(args[2],plan);
            var summary=plan["summary"]!.AsObject().Clone();summary["planFingerprint"]=StorageTransfer.CutoverHash(plan);Console.WriteLine(summary.ToJsonString());return;
        }
        case "merge-import" when args.Length==4&&args[3]=="--approved":
        {
            var plan=JsonNode.Parse(File.ReadAllText(args[1]))!.AsObject();using var c=Target();Console.WriteLine(StorageTransfer.ApplyMerge(c,plan,args[2]).ToJsonString());return;
        }
        case "merge-verify" when args.Length==3:
        {
            var plan=JsonNode.Parse(File.ReadAllText(args[1]))!.AsObject();using var c=Target();using var tx=c.BeginTransaction(IsolationLevel.Serializable);
            var result=StorageTransfer.VerifyMerge(c,plan,args[2],tx);tx.Commit();Console.WriteLine(result.ToJsonString());if(!result.B("verified"))Environment.ExitCode=1;return;
        }
        case "preview" when args.Length==2:
        {
            var rows=JsonNode.Parse(File.ReadAllText(args[1]))!.AsArray();Console.WriteLine(new JsonObject{["records"]=rows.Count,["fingerprint"]=StorageTransfer.Fingerprint(rows)}.ToJsonString());return;
        }
        default: throw new ArgumentException("snapshot SQLITE FILE | merge-preview SNAPSHOT PLAN | merge-import PLAN FINGERPRINT --approved | merge-verify PLAN FINGERPRINT");
    }
}
