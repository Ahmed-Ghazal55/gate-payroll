using System.Text.Json;
namespace ErpImport;

public static class SyncRunLog
{
    // The schema accepts only aggregate summary types, never an import plan or a row body.
    public static void Write(string directory,string status,ImportSummary? summary,int attempts,string? errorCode=null,SyncOverrides? overrides=null)
    {
        if(OperatingSystem.IsWindows())Directory.CreateDirectory(directory);
        else {Directory.CreateDirectory(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);}
        string name=Path.Combine(directory,DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")+"-"+Guid.NewGuid().ToString("N")+".json");
        var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
        if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
        using var file=new FileStream(name,options);
        JsonSerializer.Serialize(file,new{finishedAt=DateTimeOffset.UtcNow,status,attempts,errorCode,acceptNewKinds=(overrides??SyncOverrides.None).AcceptNewKinds,acceptLargeChanges=(overrides??SyncOverrides.None).AcceptLargeChanges,summary},new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true});
    }
}
