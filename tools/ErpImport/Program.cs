using System.Text.Json;
using System.Text.Json.Nodes;
using ErpImport;
using Payroll;

string phase="arguments";
bool live=args.Contains("--live");int attempts=0;ImportSummary? lastSummary=null;SyncOverrides overrides=SyncOverrides.None;
bool Log(string status,string? code=null)
{
    if(!live)return true;
    try {SyncRunLog.Write(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Private/gate-data/sync-logs"),status,lastSummary,attempts,code,overrides);return true;}
    catch {Console.Error.WriteLine("ERROR sync_log_unavailable");return false;}
}
try
{
    string? Arg(string name)=>args.SkipWhile(a=>a!=name).Skip(1).FirstOrDefault();
    if(args.Contains("--help"))
    {
        Console.WriteLine("ErpImport [--source-env FILE | --source LOCAL_CONNECTION | --local-copy | --companies-input FILE] [--target-env FILE] [--dry-run | --commit] [--live --consistent] [--accept-new-kind KIND]... [--accept-large-change KIND]... [--report COUNTS_JSON] [--schema METADATA_JSON | --target-schema METADATA_JSON | --source-capabilities]");return 0;
    }
    overrides=SyncOverrides.Parse(args);
    if(args.Contains("--commit")&&args.Contains("--dry-run"))throw new ImportFailure("conflicting_modes");
    if(live&&(args.Contains("--local-copy")||args.Contains("--source")||args.Contains("--companies-input")))throw new ImportFailure("conflicting_source_modes");
    if(live&&args.Contains("--commit")&&!args.Contains("--consistent"))throw new ImportFailure("live_commit_requires_consistent");
    string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string targetEnv=Arg("--target-env")??Path.Combine(home,"Private/gate-data/staging.env");
    if(Arg("--companies-input") is {} companyFile)
    {
        phase="previewing_companies";
        using var read=Connections.Target(targetEnv);string connection=read.ConnectionString;read.Open();
        var input=JsonNode.Parse(File.ReadAllText(companyFile)) as JsonObject??throw new ImportFailure("invalid_company_input");
        var actor=UserAccess.Principal(new JsonObject{["username"]="erp-company-import",["role"]="Admin"});
        var preview=CompanyRegistryImport.Preview(read,actor,input);JsonObject result=new(){["committed"]=false,["counts"]=preview["counts"]!.DeepClone()};
        if(args.Contains("--commit"))
        {
            phase="saving_companies";
            var store=new Store(Path.Combine(home,"Private/gate-data/erp/staging-import-runtime"),connection);
            var confirmed=new JsonObject{["rows"]=preview["rows"]!.DeepClone(),["fingerprint"]=preview.S("fingerprint"),["confirm"]=true};
            result=store.Write((c,tx)=>CompanyRegistryImport.Commit(c,tx,actor,confirmed));result["committed"]=true;
        }
        string counts=result.ToJsonString(new JsonSerializerOptions{WriteIndented=true});
        if(Arg("--report") is {} companyReport)File.WriteAllText(companyReport,counts);
        Console.WriteLine(counts);return 0;
    }
    if(Arg("--target-schema") is {} targetSchema)
    {using var target=Connections.Target(targetEnv);target.Open();SchemaInspector.Write(target,targetSchema);Console.WriteLine("PASS target schema metadata exported (no row values)");return 0;}
    string sourceFile=Arg("--source-env")??Path.Combine(home,"Private/gate-data/erp-readonly.env");
    phase="opening_source";
    using var source=args.Contains("--local-copy")?Connections.LocalCopy():Connections.Source(Arg("--source")??Connections.Env(sourceFile,"ERP_READONLY_DB"));source.Open();SourceReader.VerifyReadOnly(source);
    if(args.Contains("--source-capabilities")){Console.WriteLine("snapshot_enabled="+(SourceReader.SnapshotAvailable(source)?1:0));return 0;}
    if(Arg("--schema") is {} schema){SchemaInspector.Write(source,schema);Console.WriteLine("PASS schema metadata exported (no row values)");return 0;}
    // Dry-run opens only read connections: never construct Store until --commit.
    phase="reading_target";
    using var targetRead=Connections.Target(targetEnv);string targetConnection=targetRead.ConnectionString;targetRead.Open();var existing=ImportExecutor.Target(targetRead);
    phase="reading_source";
    if(source.Database!="erp_copy"&&args.Contains("--commit")&&!args.Contains("--consistent"))throw new ImportFailure("live_commit_requires_consistent");
    var snapshot=args.Contains("--consistent")?ConsistentSync.Read(()=>SourceReader.Read(source,false),out attempts):SourceReader.Read(source);
    var plan=ImportPlan.Build(snapshot);
    lastSummary=ImportExecutor.Run(snapshot,plan,existing);
    if(live||args.Contains("--consistent"))ConsistentSync.Validate(lastSummary,existing,overrides);
    Store? targetStore=null;
    phase="initializing_target_store";
    if(args.Contains("--commit"))targetStore=new Store(Path.Combine(home,"Private/gate-data/erp/staging-import-runtime"),targetConnection);
    phase="saving_or_comparing";
    var summary=ImportExecutor.Run(snapshot,plan,existing,targetStore);lastSummary=summary;
    string json=JsonSerializer.Serialize(summary,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true});
    // This type contains only aggregate counts, timestamps, table names and fixed reason codes.
    if(targetStore!=null)
    {
        phase="verifying_saved_snapshot";
        var verification=ImportExecutor.Run(snapshot,plan,targetStore.Read(ImportExecutor.Target));
        var payload=System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        payload["sameSnapshotVerification"]=JsonSerializer.SerializeToNode(verification,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json=payload.ToJsonString(new JsonSerializerOptions{WriteIndented=true});
        if(Arg("--report") is {} verifiedReport)File.WriteAllText(verifiedReport,json);
        Console.WriteLine(json);
        if(verification.Counts.Any(c=>c.New+c.Changed!=0))throw new ImportFailure("post_load_idempotency_failed");
    }
    else {if(Arg("--report") is {} report)File.WriteAllText(report,json);Console.WriteLine(json);}
    return Log("completed")?0:2;
}
catch(ImportFailure failure){Log("refused",failure.Code);Console.Error.WriteLine("ERROR "+failure.Code);return 2;}
catch(Microsoft.Data.SqlClient.SqlException failure){Log("failed","sql_"+failure.Number);Console.Error.WriteLine("ERROR sql_operation_failed phase="+phase+" number="+failure.Number);return 2;}
catch {Log("failed","operation_failed");Console.Error.WriteLine("ERROR operation_failed_no_sensitive_diagnostics");return 2;}
