using System.Data.Common;
using System.Text.Json.Nodes;
using Payroll;
namespace ErpImport;

public sealed record KindCount(string Kind,int Source,int New,int Changed,int Unchanged,int Rejected,int TargetBefore);
public sealed record ImportSummary(DateTimeOffset ReadStartedAt,DateTimeOffset ReadFinishedAt,string Consistency,bool Committed,IReadOnlyList<KindCount> Counts,Dictionary<string,Dictionary<string,int>> Rejected,Dictionary<string,int> Observations,Dictionary<string,string> Unavailable,Dictionary<string,int> SourceTables);
public static class ImportExecutor
{
    public static Dictionary<string,JsonObject> Target(DbConnection c)=>Target(c,null);
    public static Dictionary<string,JsonObject> Target(DbConnection c,DbTransaction? tx)
    {
        bool sqlite=c is Microsoft.Data.Sqlite.SqliteConnection;
        using(var exists=c.CreateCommand())
        {
            exists.Transaction=tx;exists.CommandText=sqlite?"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='records'":"SELECT CASE WHEN OBJECT_ID(N'dbo.records',N'U') IS NULL THEN 0 ELSE 1 END";
            if(Convert.ToInt32(exists.ExecuteScalar())==0)return [];
        }
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT id,kind,CASE WHEN kind IN ("+string.Join(',',ImportPlan.Kinds.Select(k=>"'"+k+"'"))+") THEN body ELSE '{}' END AS body FROM records";
        using var reader=cmd.ExecuteReader();var existing=new Dictionary<string,JsonObject>();
        while(reader.Read())
        {
            string kind=reader.GetString(1);var row=JsonNode.Parse(reader.GetString(2))!.AsObject();
            // Non-import kinds are retained only as an identity collision marker.
            if(!ImportPlan.Kinds.Contains(kind))row=new(){["id"]=reader.GetString(0)};
            row["__targetKind"]=kind;existing.Add(reader.GetString(0),row);
        }
        return existing;
    }
    public static ImportSummary Run(SourceSnapshot source,ImportPlan plan,Dictionary<string,JsonObject> existing,Store? commitTarget=null)
    {
        var sourceKeys=existing.Values.Where(e=>e.S("erpSourceId")!="").GroupBy(e=>(e.S("__targetKind"),e.S("erpSourceId"))).ToDictionary(g=>g.Key,g=>g.Select(e=>e.S("id")).ToHashSet(StringComparer.Ordinal));
        if(plan.Documents.Any(d=>sourceKeys.TryGetValue((d.Kind,d.Body.S("erpSourceId")),out var ids)&&ids.Any(id=>id!=d.Body.S("id"))))throw new ImportFailure("source_identity_changed_requires_review");
        var duplicates=plan.Documents.GroupBy(d=>d.Body.S("id"),StringComparer.Ordinal).Any(g=>g.Count()>1);
        if(duplicates||plan.Documents.Any(d=>existing.TryGetValue(d.Body.S("id"),out var row)&&row.S("__targetKind")!=d.Kind))throw new ImportFailure("target_identity_collision");
        var counters=ImportPlan.Kinds.ToDictionary(k=>k,k=>new int[3]);var changes=new List<PlannedDocument>();
        foreach(var doc in plan.Documents)
        {
            existing.TryGetValue(doc.Body.S("id"),out var previous);
            if(previous!=null){previous=previous.Clone();previous.Remove("__targetKind");}
            var merged=ImportPlan.Merge(previous,doc.Body);
            if(previous==null){counters[doc.Kind][0]++;changes.Add(new(doc.Kind,merged));}
            else if(!JsonNode.DeepEquals(previous,merged)){counters[doc.Kind][1]++;changes.Add(new(doc.Kind,merged));}
            else counters[doc.Kind][2]++;
        }
        if(commitTarget!=null)
        {
            // Recheck under the Store transaction to avoid committing a stale dry-run decision.
            commitTarget.Write((c,tx)=>
            {
                var current=Target(c,tx);
                foreach(var change in changes)
                {
                    var latest=current.GetValueOrDefault(change.Body.S("id"));
                    var expected=existing.GetValueOrDefault(change.Body.S("id"));
                    if(!JsonNode.DeepEquals(latest,expected))throw new ImportFailure("target_changed_retry_dry_run");
                }
                Store.BufferedWrites(c,tx,()=>
                {
                    foreach(var change in changes)
                    {
                        Store.Save(c,change.Kind,change.Body,tx);
                        Store.Audit(c,tx,"erp.import."+change.Kind,change.Body.S("id"),"erp-import");
                    }
                });
                return true;
            });
        }
        string SourceTable(string kind)=>kind switch{"company" or "commercial_registration"=>"Sponsors","city"=>"GlobalCities","rider"=>"Employees","housing"=>"Housing","housing_residence"=>"HousingResidencePeriods","vehicle"=>"Vehicles","vehicle_assignment"=>"RiderVehicleAssignments","custody_handover"=>"RiderVehicleAssignmentEvents","platform_account"=>"PlatformRiderAccounts","legal_case"=>"HrLegalCases",_=>""};
        var counts=ImportPlan.Kinds.Select(k=>new KindCount(k,source.Tables.GetValueOrDefault(SourceTable(k))?.Count??0,counters[k][0],counters[k][1],counters[k][2],plan.Rejected.GetValueOrDefault(k)?.Values.Sum()??0,existing.Values.Count(r=>r.S("__targetKind")==k))).ToList();
        return new(source.StartedAt,source.FinishedAt,source.Consistency,commitTarget!=null,counts,plan.Rejected,plan.Observations,plan.Unavailable,source.Tables.ToDictionary(t=>t.Key,t=>t.Value.Count));
    }
}
