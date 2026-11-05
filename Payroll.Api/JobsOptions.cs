using Microsoft.Data.SqlClient;
namespace Payroll;

public sealed class JobsOptions
{
    public bool Enabled {get;}
    public bool ErpSyncEnabled {get;}
    public bool CatchUpEnabled {get;private init;}=true;
    public double CatchUpHours {get;private init;}=24;
    public bool DailyEnabled {get;private init;}
    internal string StorageConnection {get;}="";
    // Set when Jobs:Enabled=true but the target is not approved. Jobs stay off; the API keeps serving.
    public string? BlockedReason {get;private init;}
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
        catch {return new(false,false){BlockedReason="unapproved_target"};}
        double hours=config.GetValue<double?>("Jobs:ErpSync:CatchUpHours")??24;
        if(!double.IsFinite(hours)||hours<=0||hours>8760)return new(false,false){BlockedReason="invalid_catch_up_hours"};
        return new(true,config.GetValue<bool>("Jobs:ErpSync:Enabled"),jobs)
        {CatchUpEnabled=config.GetValue<bool?>("Jobs:ErpSync:CatchUpEnabled")??true,CatchUpHours=hours,DailyEnabled=config.GetValue<bool>("Jobs:ErpSync:DailyEnabled")};
    }
}
