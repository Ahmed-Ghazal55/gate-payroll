using System.Data.Common;
using System.Text.Json.Nodes;
using Payroll;
namespace ErpImport;

public static class ImportExecutor
{
    public const int BatchSize=2000;
    public static Dictionary<string,JsonObject> Target(DbConnection c)=>Target(c,null);
    public static Dictionary<string,JsonObject> Target(DbConnection c,DbTransaction? tx,CancellationToken cancellationToken=default)
    {
        bool sqlite=c is Microsoft.Data.Sqlite.SqliteConnection;
        using(var exists=c.CreateCommand())
        {
            exists.Transaction=tx;exists.CommandText=sqlite?"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='records'":"SELECT CASE WHEN OBJECT_ID(N'dbo.records',N'U') IS NULL THEN 0 ELSE 1 END";
            if(Convert.ToInt32(exists.ExecuteScalar())==0)return [];
        }
        using var cmd=c.CreateCommand();using var cancel=SyncCancellation.Register(cmd,cancellationToken);cmd.Transaction=tx;cmd.CommandText="SELECT id,kind,CASE WHEN kind IN ("+string.Join(',',ImportPlan.Kinds.Select(k=>"'"+k+"'"))+") THEN body ELSE '{}' END AS body FROM records";
        using var reader=cmd.ExecuteReader();var existing=new Dictionary<string,JsonObject>();
        while(reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();string kind=reader.GetString(1);var row=JsonNode.Parse(reader.GetString(2))!.AsObject();
            // Non-import kinds are retained only as an identity collision marker.
            if(!ImportPlan.Kinds.Contains(kind))row=new(){["id"]=reader.GetString(0)};
            row["__targetKind"]=kind;existing.Add(reader.GetString(0),row);
        }
        return existing;
    }
    public static ImportSummary Run(SourceSnapshot source,ImportPlan plan,Dictionary<string,JsonObject> existing,Store? commitTarget=null,CancellationToken cancellationToken=default,Action<DbConnection,DbTransaction>? beforeCommit=null)
    {
        var sourceKeys=existing.Values.Where(e=>e.S("erpSourceId")!="").GroupBy(e=>(e.S("__targetKind"),e.S("erpSourceId"))).ToDictionary(g=>g.Key,g=>g.Select(e=>e.S("id")).ToHashSet(StringComparer.Ordinal));
        if(plan.Documents.Any(d=>sourceKeys.TryGetValue((d.Kind,d.Body.S("erpSourceId")),out var ids)&&ids.Any(id=>id!=d.Body.S("id"))))throw new ImportFailure("source_identity_changed_requires_review");
        var duplicates=plan.Documents.GroupBy(d=>d.Body.S("id"),StringComparer.Ordinal).Any(g=>g.Count()>1);
        if(duplicates||plan.Documents.Any(d=>existing.TryGetValue(d.Body.S("id"),out var row)&&row.S("__targetKind")!=d.Kind))throw new ImportFailure("target_identity_collision");
        var counters=ImportPlan.Kinds.ToDictionary(k=>k,k=>new int[3]);var changes=new List<PlannedDocument>();var vehicleFields=new Dictionary<string,int>();var changedFields=new Dictionary<string,Dictionary<string,int>>();
        var changeLog=new List<JsonObject>();
        foreach(var doc in plan.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();existing.TryGetValue(doc.Body.S("id"),out var previous);
            if(previous!=null){previous=previous.Clone();previous.Remove("__targetKind");}
            if(doc.Kind.StartsWith("hs_",StringComparison.Ordinal)&&previous!=null&&!JsonNode.DeepEquals(StripHash(previous),StripHash(doc.Body)))throw new ImportFailure("hs_approved_fact_changed_requires_reversal:"+doc.Kind);
            if(doc.Kind=="vehicle_odometer_reading"&&previous!=null&&!JsonNode.DeepEquals(StripHash(previous),StripHash(doc.Body)))throw new ImportFailure("append_only_fact_changed_requires_review:"+doc.Kind);
            var merged=ImportPlan.Merge(previous,doc.Body);
            var hashed=ContentHash.WithHash(merged);
            if(doc.Kind=="vehicle"&&previous!=null)
                foreach(var field in doc.Body.Where(f=>!JsonNode.DeepEquals(previous[f.Key],f.Value)))
                    vehicleFields[field.Key]=vehicleFields.GetValueOrDefault(field.Key)+1;
            string newHash=hashed.S("contentHash"),oldHash=previous?.S("contentHash")??"";
            bool same=previous!=null&&oldHash!=""&&oldHash==newHash;
            if(previous==null){counters[doc.Kind][0]++;changes.Add(new(doc.Kind,hashed));changeLog.Add(LogRow(hashed.S("id"),"insert","",newHash));}
            else if(!same)
            {
                // Fallback when an older row has no hash yet: compare canonical bodies once, then store the hash.
                if(oldHash==""&&ContentHash.Of(previous)==newHash){counters[doc.Kind][2]++;continue;}
                counters[doc.Kind][1]++;changes.Add(new(doc.Kind,hashed));
                changeLog.Add(LogRow(hashed.S("id"),"update",oldHash==""?ContentHash.Of(previous):oldHash,newHash));
                if(!changedFields.TryGetValue(doc.Kind,out var names))changedFields[doc.Kind]=names=[];
                foreach(var field in hashed.Where(f=>!IgnoreCompare.Contains(f.Key)&&!JsonNode.DeepEquals(previous![f.Key],f.Value)))names[field.Key]=names.GetValueOrDefault(field.Key)+1;
            }
            else counters[doc.Kind][2]++;
        }
        if(commitTarget!=null&&(changes.Count>0||beforeCommit!=null))
        {
            // Recheck + write in batches under the ERP mirror lock, not the global document lock for the whole run.
            if(changes.Count==0)
            {
                commitTarget.Write((c,tx)=>{beforeCommit!(c,tx);return true;},cancellationToken:cancellationToken,lockResource:Store.ErpMirrorLock);
            }
            else for(int offset=0;offset<changes.Count;offset+=BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch=changes.Skip(offset).Take(BatchSize).ToList();
                var logBatch=changeLog.Skip(offset).Take(BatchSize).ToList();
                bool first=offset==0;
                commitTarget.Write((c,tx)=>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if(first)beforeCommit?.Invoke(c,tx);
                    var current=Target(c,tx,cancellationToken);
                    foreach(var change in batch)
                    {
                        var latest=current.GetValueOrDefault(change.Body.S("id"));
                        var expected=existing.GetValueOrDefault(change.Body.S("id"));
                        if(!SameForConcurrency(latest,expected))throw new ImportFailure("target_changed_retry_dry_run");
                    }
                    Store.BufferedWrites(c,tx,()=>
                    {
                        foreach(var change in batch)
                        {
                            cancellationToken.ThrowIfCancellationRequested();Store.Save(c,change.Kind,change.Body,tx);
                        }
                        foreach(var entry in logBatch)
                        {
                            entry["id"]="erp-change:"+entry.S("recordId")+":"+entry.S("newHash")[..16];
                            Store.Save(c,"erp_sync_change",entry,tx);
                        }
                        if(first)Store.Audit(c,tx,"erp.import.run","erp-import:"+source.StartedAt.ToString("yyyyMMddTHHmmssfff")+"Z","erp-import");
                    });
                    cancellationToken.ThrowIfCancellationRequested();return true;
                },cancellationToken:cancellationToken,lockResource:Store.ErpMirrorLock);
            }
        }
        string SourceTable(string kind)=>kind switch{"company" or "commercial_registration"=>"Sponsors","city"=>"GlobalCities","rider"=>"Employees","housing"=>"Housing","housing_residence"=>"HousingResidencePeriods","vehicle"=>"Vehicles","vehicle_assignment"=>"RiderVehicleAssignments","custody_handover"=>"RiderVehicleAssignmentEvents","platform_account"=>"PlatformRiderAccounts","legal_case"=>"HrLegalCases",_=>HungerStationFacts.Kinds.Contains(kind)?HsFactsSource.Table(kind):PullContracts.Table(kind)};
        var counts=ImportPlan.Kinds.Select(k=>new KindCount(k,source.Tables.GetValueOrDefault(SourceTable(k))?.Count??0,counters[k][0],counters[k][1],counters[k][2],plan.Rejected.GetValueOrDefault(k)?.Values.Sum()??0,existing.Values.Count(r=>r.S("__targetKind")==k))).ToList();
        return new(source.StartedAt,source.FinishedAt,source.Consistency,commitTarget!=null,counts,plan.Rejected,plan.Observations,plan.Unavailable,source.Tables.ToDictionary(t=>t.Key,t=>t.Value.Count)) {VehicleChangedFields=vehicleFields,ChangedFields=changedFields,Totals=PullContracts.Catalog.Where(x=>x.Totals!="").ToDictionary(x=>x.Kind,x=>x.Totals.Split(' ').ToDictionary(f=>f,f=>plan.Documents.Where(d=>d.Kind==x.Kind).Sum(d=>d.Body.N(f)))),ReadMode=source.ReadMode,TablesSkipped=source.TablesSkipped};
    }
    static readonly HashSet<string> IgnoreCompare=new(StringComparer.Ordinal){"contentHash","importedAt","importedBy","__targetKind"};
    static JsonObject StripHash(JsonObject body){var copy=body.Clone();foreach(var key in IgnoreCompare)copy.Remove(key);return copy;}
    static bool SameForConcurrency(JsonObject? latest,JsonObject? expected)
    {
        if(latest==null&&expected==null)return true;
        if(latest==null||expected==null)return false;
        string a=latest.S("contentHash"),b=expected.S("contentHash");
        if(a!=""&&b!="")return a==b;
        return JsonNode.DeepEquals(StripHash(latest),StripHash(expected));
    }
    static JsonObject LogRow(string id,string op,string oldHash,string newHash)=>new(){["recordId"]=id,["op"]=op,["oldHash"]=oldHash,["newHash"]=newHash,["runId"]="erp-import",["at"]=DateTimeOffset.UtcNow.ToString("O")};
}
