using System.Data.Common;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace ErpImport;

public sealed record HsFactContract(string Kind,string Key,string Fields);
public static class HsFactsSource
{
    // CR-002 specifies columns, not table names. Match a unique complete table/view; never guess a legacy source.
    public static readonly HsFactContract[] Contracts=[
        new("hs_wallet_event","SourceEventId","SourceEventId IdempotencyKey EventType PlatformAccountId RiderId SupervisorId SponsorId Amount Currency OccurredAt ApprovedAt ApprovedBy ReversesEventId RowVersion"),
        new("hs_remittance","BatchId","BatchId SupervisorId Destination Status SubmittedAt ConfirmedAt ConfirmedBy"),
        new("hs_remittance_allocation","AllocationId","AllocationId BatchId SourceEventId Amount ConfirmedAt"),
        new("hs_handover_review","ReviewId","ReviewId PlatformAccountId FromRiderId ToRiderId ExplicitBalance Direction ApprovalId ConfirmedAt"),
        new("hs_month_close","CloseSnapshotId","CloseSnapshotId PlatformAccountId ClosePeriod C A M MObservedAt InvoiceLineId")
    ];
    public static string Table(string kind)=>"HS:"+kind;
    static string Quote(string value)=>"["+value.Replace("]","]]")+"]";
    public static void Read(DbConnection c,Dictionary<string,List<JsonObject>> target)
    {
        bool sql=c is SqlConnection;var metadata=new Dictionary<(string Schema,string Name),HashSet<string>>();
        if(sql)
        {
            using var cmd=c.CreateCommand();cmd.CommandText="SELECT TABLE_SCHEMA,TABLE_NAME,COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS ORDER BY TABLE_SCHEMA,TABLE_NAME,ORDINAL_POSITION";
            using var reader=cmd.ExecuteReader();while(reader.Read()){var key=(reader.GetString(0),reader.GetString(1));if(!metadata.TryGetValue(key,out var columns))metadata[key]=columns=new(StringComparer.OrdinalIgnoreCase);columns.Add(reader.GetString(2));}
        }
        else
        {
            var names=new List<string>();using(var cmd=c.CreateCommand()){cmd.CommandText="SELECT name FROM sqlite_master WHERE type IN ('table','view') ORDER BY name";using var reader=cmd.ExecuteReader();while(reader.Read())names.Add(reader.GetString(0));}
            foreach(string name in names){var columns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA table_info("+Quote(name)+")";using var reader=cmd.ExecuteReader();while(reader.Read())columns.Add(reader.GetString(1));metadata[("",name)]=columns;}
        }
        foreach(var contract in Contracts)
        {
            string[] fields=contract.Fields.Split(' ');var matches=metadata.Where(t=>fields.All(t.Value.Contains)).ToList();
            if(matches.Count>1)throw new ImportFailure("hs_source_contract_ambiguous:"+contract.Kind);
            if(matches.Count==0)continue; // Missing is distinct from a present but empty source.
            var match=matches[0];var selected=fields.Concat(new[]{"ApprovedAt","ApprovedBy"}.Where(f=>match.Value.Contains(f)&&!fields.Contains(f))).ToArray();
            using var cmd=c.CreateCommand();cmd.CommandTimeout=120;cmd.CommandText="SELECT "+string.Join(',',selected.Select(Quote))+" FROM "+(sql?Quote(match.Key.Schema)+".":"")+Quote(match.Key.Name)+" ORDER BY "+Quote(contract.Key);
            using var reader=cmd.ExecuteReader();var rows=new List<JsonObject>();while(reader.Read())
            {
                var row=new JsonObject();for(int i=0;i<reader.FieldCount;i++){object? value=reader.IsDBNull(i)?null:reader.GetValue(i);row[reader.GetName(i)]=value is byte[] bytes&&reader.GetName(i)=="RowVersion"?JsonValue.Create(Convert.ToHexString(bytes)):SourceReader.Value(value);}rows.Add(row);
            }
            target.Add(Table(contract.Kind),rows);
        }
    }
    public static void AddToPlan(SourceSnapshot source,ImportPlan plan,Dictionary<string,string> companies)
    {
        var wallet=source.Tables.GetValueOrDefault(Table("hs_wallet_event"))??[];var batches=source.Tables.GetValueOrDefault(Table("hs_remittance"))??[];var allocations=source.Tables.GetValueOrDefault(Table("hs_remittance_allocation"))??[];
        var accounts=plan.Documents.Where(d=>d.Kind=="platform_account"&&new[]{"hungerstation","hs"}.Contains(d.Body.S("platformCode").ToLowerInvariant())).ToDictionary(d=>d.Body.S("erpSourceId"),d=>d.Body.S("companyId"));
        string Company(JsonObject row,HsFactContract contract)
        {
            if(contract.Kind=="hs_wallet_event")return companies.GetValueOrDefault(row.S("SponsorId"))??"";
            if(contract.Kind is "hs_handover_review" or "hs_month_close")return accounts.GetValueOrDefault(row.S("PlatformAccountId"))??"";
            string batch=row.S("BatchId");var links=allocations.Where(a=>a.S("BatchId")==batch).ToList();if(links.Count==0)return "";var events=links.Select(a=>wallet.SingleOrDefault(w=>w.S("SourceEventId")==a.S("SourceEventId"))).ToList();if(events.Any(w=>w==null||w.S("ApprovedAt")==""||w.S("ApprovedBy")==""))return "";var ids=events.Select(w=>companies.GetValueOrDefault(w!.S("SponsorId"))??"").Distinct().ToArray();return ids.Length==1?ids[0]:"";
        }
        if(wallet.Where(w=>w.S("IdempotencyKey")!="").GroupBy(w=>w.S("IdempotencyKey"),StringComparer.Ordinal).Any(g=>g.Count()>1))throw new ImportFailure("hs_duplicate_idempotency_key");
        foreach(var contract in Contracts)
        {
            if(!source.Tables.TryGetValue(Table(contract.Kind),out var rows)){plan.Unavailable[contract.Kind]="source not available";continue;}
            if(rows.GroupBy(r=>r.S(contract.Key),StringComparer.Ordinal).Any(g=>g.Key==""||g.Count()>1))throw new ImportFailure("hs_source_identity_invalid:"+contract.Kind);
            foreach(var raw in rows)
            {
                bool approved=contract.Kind switch{
                    "hs_wallet_event"=>raw.S("ApprovedAt")!=""&&raw.S("ApprovedBy")!="",
                    "hs_remittance"=>raw.S("ConfirmedAt")!=""&&raw.S("ConfirmedBy")!="",
                    "hs_remittance_allocation"=>raw.S("ConfirmedAt")!=""&&batches.Any(b=>b.S("BatchId")==raw.S("BatchId")&&b.S("ConfirmedAt")!=""&&b.S("ConfirmedBy")!="")&&wallet.Any(w=>w.S("SourceEventId")==raw.S("SourceEventId")&&w.S("ApprovedAt")!=""&&w.S("ApprovedBy")!=""),
                    _=>raw.S("ApprovedAt")!=""&&raw.S("ApprovedBy")!="" // CR-002 still owes explicit approval evidence for reviews/month closes.
                };
                if(!approved){plan.Reject(contract.Kind,"approval_not_available");continue;}string company=Company(raw,contract);if(company==""){plan.Reject(contract.Kind,"company_relation_unknown");continue;}
                var body=new JsonObject{["id"]="erp:"+contract.Kind+":"+raw.S(contract.Key),["erpSourceId"]=raw.S(contract.Key),["erpSource"]=contract.Kind,["sourceEventId"]=raw.S(contract.Key),["companyId"]=company,["platform"]="HungerStation",["status"]="approved",["sourceFacts"]=raw.Clone()};
                plan.Documents.Add(new(contract.Kind,body));
            }
        }
    }
}
