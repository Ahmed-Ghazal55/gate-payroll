using System.Data;
using System.Text.Json;
using ErpImport;
using Payroll;

// Do not construct Store here: its startup checkpoint/schema changes are not a dry run.
try
{
    var modes=args.Where(a=>a is "--dry-run" or "--check" or "--commit").ToList();
    if(modes.Count>1||args.Any(a=>a is not ("--dry-run" or "--check" or "--commit")))throw new ImportFailure("invalid_arguments");
    string mode=modes.SingleOrDefault()??"--dry-run";
    string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Private/gate-data/staging.env");
    using var c=Connections.Target(path);c.Open();using var tx=c.BeginTransaction(IsolationLevel.Serializable);
    if(mode=="--commit")
    {
        using(var guard=Store.Command(c,tx,"DECLARE @r int; EXEC @r=sp_getapplock @Resource='GatePayroll.DocumentWrite',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000; IF @r<0 THROW 51000,'Reporting transaction busy',1;"))guard.ExecuteNonQuery();
        var result=Reporting.Rebuild(c,tx);tx.Commit();Console.WriteLine(JsonSerializer.Serialize(new{mode="commit",result}));
    }
    else if(mode=="--check")
    {var result=Reporting.Check(c,tx);tx.Commit();Console.WriteLine(result.ToJsonString());if(!result.B("consistent"))return 2;}
    else{var result=Reporting.DryRun(c,tx);tx.Commit();Console.WriteLine(JsonSerializer.Serialize(result));}
    return 0;
}
catch(ImportFailure e){Console.Error.WriteLine("ReportRebuild stopped: "+e.Code);return 2;}
catch(Exception e){Console.Error.WriteLine("ReportRebuild stopped: "+e.GetType().Name+"; no connection values or source rows logged.");return 2;}
