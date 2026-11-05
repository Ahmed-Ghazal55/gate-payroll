using Microsoft.Data.SqlClient;
namespace Payroll;

public sealed class JobsOptions
{
    public bool Enabled {get;}
    public bool ErpSyncEnabled {get;}
    internal string StorageConnection {get;}="";
    private JobsOptions(bool enabled,bool erpSyncEnabled,string storageConnection="")
    {Enabled=enabled;ErpSyncEnabled=enabled&&erpSyncEnabled;StorageConnection=storageConnection;}
    public static JobsOptions Read(IConfiguration config,IHostEnvironment environment)
    {
        if(!config.GetValue<bool>("Jobs:Enabled"))return new(false,false);
        string jobs=config.GetConnectionString("Jobs")??"";
        if(string.IsNullOrWhiteSpace(jobs))return new(false,false);
        try
        {
            var storage=new SqlConnectionStringBuilder(jobs);
            var target=new SqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection")??"");
            bool synthetic=environment.IsEnvironment("Testing")&&storage.InitialCatalog.StartsWith("GateJobsTest_",StringComparison.Ordinal)&&System.Text.RegularExpressions.Regex.IsMatch(storage.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if((storage.InitialCatalog!="db77738"&&!synthetic)||target.InitialCatalog!=storage.InitialCatalog||!target.DataSource.Equals(storage.DataSource,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException();
        }
        catch {throw new InvalidOperationException("Jobs require the approved staging target (or an isolated Testing loopback database).");}
        return new(true,config.GetValue<bool>("Jobs:ErpSync:Enabled"),jobs);
    }
}
