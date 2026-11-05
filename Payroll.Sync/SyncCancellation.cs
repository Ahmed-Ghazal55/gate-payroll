using System.Data.Common;
namespace ErpImport;

public static class SyncCancellation
{
    public static CancellationTokenRegistration Register(DbCommand command,CancellationToken token)=>token.Register(()=>
    {try{command.Cancel();}catch(ObjectDisposedException){}catch(InvalidOperationException){}});
}
