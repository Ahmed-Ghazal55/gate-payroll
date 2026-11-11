using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.SqlClient;
using Payroll;

try{return Run(args);}
catch(ImportFailure ex){Console.Error.WriteLine("error="+ex.Code);return 2;}

static int Run(string[] args)
{
string? Arg(string name)=>args.SkipWhile(x=>x!=name).Skip(1).FirstOrDefault();
bool commit=args.Contains("--commit",StringComparer.Ordinal);
if(args.Contains("--help")||args.Length==0)
{
    Console.WriteLine("RiderIdentityRepair --dry-run [--target-env FILE] [--source-env FILE] | --commit --preview-fingerprint SHA256 [same env files]");
    return 0;
}
if(commit==args.Contains("--dry-run",StringComparer.Ordinal))throw new ImportFailure("choose_exactly_one_mode");
if(commit&&string.IsNullOrWhiteSpace(Arg("--preview-fingerprint")))throw new ImportFailure("preview_fingerprint_required");

string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
string targetEnv=Arg("--target-env")??Path.Combine(home,"Private/gate-data/staging.env");
string sourceEnv=Arg("--source-env")??Path.Combine(home,"Private/gate-data/erp-readonly.env");
using var source=Connections.Source(Connections.Env(sourceEnv,"ERP_READONLY_DB"));source.Open();SourceReader.VerifyReadOnly(source);
using var target=Connections.Target(targetEnv);string targetConnection=target.ConnectionString;target.Open();
var snapshot=ConsistentSync.Read(()=>SourceReader.Read(source),out int attempts);
var plan=ImportPlan.Build(snapshot,FinancialCore.Sponsors);
var existing=ImportExecutor.Target(target);
var sourceOwners=existing.Values.Where(x=>x.S("__targetKind")=="rider"&&x.S("erpSourceId")!="")
    .GroupBy(x=>x.S("erpSourceId"),StringComparer.OrdinalIgnoreCase)
    .ToDictionary(g=>g.Key,g=>g.ToList(),StringComparer.OrdinalIgnoreCase);
var collisions=plan.Documents.Where(x=>x.Kind=="rider"&&sourceOwners.TryGetValue(x.Body.S("erpSourceId"),out var owners)&&owners.Any(o=>o.S("id")!=x.Body.S("id"))).ToList();
if(collisions.Count!=1)throw new ImportFailure("expected_one_rider_identity_collision_found_"+collisions.Count);
var proposed=collisions[0].Body;
var owner=sourceOwners[proposed.S("erpSourceId")].Single();
string oldId=owner.S("id"),newId=proposed.S("id");
if(existing.ContainsKey(newId))throw new ImportFailure("current_identity_already_exists");
if(plan.Documents.Any(x=>x.Kind=="rider"&&x.Body.S("id")==oldId))throw new ImportFailure("old_identity_still_belongs_to_erp_rider");

var merged=ContentHash.WithHash(ImportPlan.Merge(WithoutTargetKind(owner),proposed));
var changes=new List<(string Id,string Kind,string Before,string After)>();
foreach(var pair in existing)
{
    string kind=pair.Value.S("__targetKind");
    if(kind is "audit" or "rider"&&pair.Key==oldId)continue;
    var body=WithoutTargetKind(pair.Value);string before=body.ToJsonString();
    if(!ReplaceIdentityReferences(body,oldId,newId))continue;
    if(body.ContainsKey("contentHash")||body.S("erpSource")!="")body=ContentHash.WithHash(body);
    changes.Add((pair.Key,kind,before,body.ToJsonString()));
}
string fingerprint=Fingerprint(owner,merged,changes);
var differing=owner.Where(x=>x.Key!="__targetKind"&&!JsonNode.DeepEquals(x.Value,merged[x.Key])).Select(x=>x.Key).Union(merged.Where(x=>!owner.ContainsKey(x.Key)).Select(x=>x.Key)).Order().ToArray();
Console.WriteLine("mode="+(commit?"commit":"dry-run"));
Console.WriteLine("collision_count=1");
Console.WriteLine("stored_document_count=1");
Console.WriteLine("erp_document_count=1");
Console.WriteLine("changed_reference_documents="+changes.Count);
Console.WriteLine("preserved_audit_references="+CountAuditReferences(existing,oldId));
Console.WriteLine("differing_field_count="+differing.Length);
Console.WriteLine("differing_fields="+string.Join(',',differing));
Console.WriteLine("preview_fingerprint="+fingerprint);
Console.WriteLine("consistent_read_attempts="+attempts);
if(!commit)return 0;
if(!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(fingerprint),Encoding.ASCII.GetBytes(Arg("--preview-fingerprint")!.ToUpperInvariant())))throw new ImportFailure("preview_fingerprint_changed");

target.Close();
using var write=new SqlConnection(new SqlConnectionStringBuilder(targetConnection){MultipleActiveResultSets=false}.ConnectionString);write.Open();
using var tx=write.BeginTransaction(IsolationLevel.Serializable);
using(var guard=Command(write,tx,"DECLARE @r int; EXEC @r=sp_getapplock @Resource='GatePayroll.DocumentWrite',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000; IF @r<0 THROW 51000,'Payroll transaction busy',1;"))guard.ExecuteNonQuery();
foreach(var change in changes)
{
    using var update=Command(write,tx,"UPDATE dbo.records SET body=@after WHERE id=@id AND kind=@kind AND body=@before",("@after",change.After),("@id",change.Id),("@kind",change.Kind),("@before",change.Before));
    if(update.ExecuteNonQuery()!=1)throw new ImportFailure("target_changed_retry_dry_run");
}
using(var insert=Command(write,tx,"INSERT dbo.records(id,kind,body) VALUES(@id,'rider',@body)",("@id",newId),("@body",merged.ToJsonString())))insert.ExecuteNonQuery();
using(var delete=Command(write,tx,"DELETE dbo.records WHERE id=@id AND kind='rider' AND body=@body",("@id",oldId),("@body",WithoutTargetKind(owner).ToJsonString())))if(delete.ExecuteNonQuery()!=1)throw new ImportFailure("target_changed_retry_dry_run");
var audit=new JsonObject{{"id",Guid.NewGuid().ToString("N")},{"action","rider.identity.rekey"},{"entityId",newId},{"actor","r12-approved-repair"},{"at",DateTimeOffset.UtcNow.ToString("O")}};
using(var insertAudit=Command(write,tx,"INSERT dbo.records(id,kind,body) VALUES(@id,'audit',@body)",("@id",audit.S("id")),("@body",audit.ToJsonString())))insertAudit.ExecuteNonQuery();
tx.Commit();Console.WriteLine("committed=1");return 0;

static JsonObject WithoutTargetKind(JsonObject value){var copy=(JsonObject)value.DeepClone();copy.Remove("__targetKind");return copy;}
static bool ReplaceIdentityReferences(JsonNode? node,string oldId,string newId,string key="")
{
    bool changed=false;
    if(node is JsonObject obj)foreach(var item in obj.ToList())
    {
        if(item.Value is JsonValue&&ReferenceField(item.Key)&&item.Value.ToString()==oldId){obj[item.Key]=newId;changed=true;}
        else changed|=ReplaceIdentityReferences(item.Value,oldId,newId,item.Key);
    }
    else if(node is JsonArray array)foreach(var item in array)changed|=ReplaceIdentityReferences(item,oldId,newId,key);
    return changed;
}
static bool ReferenceField(string key)=>key is "iqama" or "rider" or "riderId" or "registeredRiderId" or "nextRiderId" or "captainIqama";
static int CountAuditReferences(Dictionary<string,JsonObject> rows,string oldId)=>rows.Values.Count(x=>x.S("__targetKind")=="audit"&&ContainsValue(x,oldId));
static bool ContainsValue(JsonNode? node,string value)=>node switch{JsonValue v=>v.ToString()==value,JsonObject o=>o.Any(x=>ContainsValue(x.Value,value)),JsonArray a=>a.Any(x=>ContainsValue(x,value)),_=>false};
static string Fingerprint(JsonObject owner,JsonObject merged,List<(string Id,string Kind,string Before,string After)> changes)
{
    var material=ContentHash.Canonical(owner)+"\n"+ContentHash.Canonical(merged)+"\n"+string.Join('\n',changes.OrderBy(x=>x.Id,StringComparer.Ordinal).Select(x=>x.Id+"\n"+x.Kind+"\n"+x.Before+"\n"+x.After));
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
}
static DbCommand Command(DbConnection c,DbTransaction tx,string sql,params (string Name,object Value)[] values)
{
    var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;foreach(var (name,value) in values){var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value;cmd.Parameters.Add(p);}return cmd;
}
}
