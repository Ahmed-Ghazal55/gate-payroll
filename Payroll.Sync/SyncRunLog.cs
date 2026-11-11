using System.Text.Json;
namespace ErpImport;

public static class SyncRunLog
{
    // The schema accepts only aggregate summary types, never an import plan or a row body.
    public static void Write(string directory,string status,ImportSummary? summary,int attempts,string? errorCode=null,SyncOverrides? overrides=null,DateTimeOffset? startedAt=null)
    {
        if(OperatingSystem.IsWindows())Directory.CreateDirectory(directory);
        else {Directory.CreateDirectory(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);}
        string name=Path.Combine(directory,DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")+"-"+Guid.NewGuid().ToString("N")+".json");
        var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
        if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
        using var file=new FileStream(name,options);
        var phases=summary?.Phases??SyncPhaseTiming.Zero;
        var o=overrides??SyncOverrides.None;
        JsonSerializer.Serialize(file,new{startedAt=startedAt??summary?.ReadStartedAt??DateTimeOffset.UtcNow,finishedAt=DateTimeOffset.UtcNow,status,attempts,errorCode,acceptNewKinds=o.AcceptNewKinds,acceptLargeChanges=o.AcceptLargeChanges,acceptAppendOnlyChanges=o.AcceptAppendOnlyChanges,fullResync=o.FullResync,phases=new{readMs=phases.ReadMs,planMs=phases.PlanMs,writeMs=phases.WriteMs,rptRefreshMs=phases.RptRefreshMs,totalMs=phases.TotalMs},summary},new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true});
    }
}
