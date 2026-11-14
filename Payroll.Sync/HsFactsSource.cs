using System.Data.Common;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace ErpImport;

/// <param name="Fields">CR-002 / fixture column set (SQLite contract discovery).</param>
/// <param name="PreferSchema">When set with PreferName, prefer this live view if its PreferFields are present (A8).</param>
public sealed record HsFactContract(string Kind,string Key,string Fields,string? PreferSchema=null,string? PreferName=null,string? PreferKey=null,string? PreferFields=null);
public static class HsFactsSource
{
    public static readonly HsFactContract[] Contracts=[
        new("hs_wallet_event","SourceEventId","SourceEventId IdempotencyKey EventType PlatformAccountId RiderId SupervisorId SponsorId Amount Currency OccurredAt ApprovedAt ApprovedBy ReversesEventId RowVersion",
            "finance_read","vw_hunger_financial_events_v1","EventId","EventId Kind PlatformRiderAccountId RiderProfileId Amount BusinessDate ApprovalStatus ApprovedAtUtc ApprovedByUserId Status"),
        new("hs_remittance","BatchId","BatchId SupervisorId Destination Status SubmittedAt ConfirmedAt ConfirmedBy",
            "finance_read","vw_hunger_adjustments_v1","DocumentId","DocumentId ClientPlatformId PlatformRiderAccountId InvoicePeriod A Status ApprovalDate ObservedOn"),
        new("hs_remittance_allocation","AllocationId","AllocationId BatchId SourceEventId Amount ConfirmedAt",
            "finance_read","vw_hunger_adjustments_v1","DocumentId","DocumentId MonthCloseDocumentId PlatformRiderAccountId A Status SourceReference"),
        new("hs_handover_review","ReviewId","ReviewId PlatformAccountId FromRiderId ToRiderId ExplicitBalance Direction ApprovalId ConfirmedAt",
            "finance_read","vw_hunger_wallet_reviews_v1","ReviewId","ReviewId PlatformRiderAccountId Direction ExplicitBalance ApprovalId Status RiderProfileId"),
        new("hs_month_close","CloseSnapshotId","CloseSnapshotId PlatformAccountId ClosePeriod C A M MObservedAt InvoiceLineId",
            "finance_read","vw_hunger_month_close_v1","DocumentId","DocumentId PlatformRiderAccountId PeriodStart PeriodEnd C A Status ObservedOn"),
        new("hs_custody","SponsorId","ClientPlatformId SupervisorEmployeeId SponsorId CustodySubAccountId ExpectedBalance PostingBalance Difference",
            "finance_read","vw_hunger_custody_v1","SponsorId","ClientPlatformId SupervisorEmployeeId SponsorId CustodySubAccountId ExpectedBalance PostingBalance Difference"),
        new("hs_daily_operation","Id","Id PlatformRiderAccountId ReportDate CompletedOrders ActualWorkingHours RejectedOrders CancelledOrders",
            "hunger","DailyOperations","Id","Id PlatformRiderAccountId ReportDate CompletedOrders ActualWorkingHours RejectedOrders CancelledOrders"),
    ];
    public static string Table(string kind)=>"HS:"+kind;
    static string Quote(string value)=>"["+value.Replace("]","]]")+"]";
    public static void Read(DbConnection c,Dictionary<string,List<JsonObject>> target,CancellationToken cancellationToken=default)
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
            cancellationToken.ThrowIfCancellationRequested();
            string[] legacy=contract.Fields.Split(' ');
            string[] prefer=(contract.PreferFields??contract.Fields).Split(' ');
            string orderKey=contract.Key;
            KeyValuePair<(string Schema,string Name),HashSet<string>>? match=null;
            string[] selectedFields=legacy;
            if(contract.PreferSchema!=null&&contract.PreferName!=null
                &&metadata.TryGetValue((contract.PreferSchema,contract.PreferName),out var preferred)
                &&prefer.All(preferred.Contains))
            {
                match=new((contract.PreferSchema,contract.PreferName),preferred);
                selectedFields=prefer;
                orderKey=contract.PreferKey??contract.Key;
            }
            else
            {
                var matches=metadata.Where(t=>legacy.All(t.Value.Contains)).ToList();
                if(matches.Count>1)throw new ImportFailure("hs_source_contract_ambiguous:"+contract.Kind);
                if(matches.Count==1){match=matches[0];selectedFields=legacy;orderKey=contract.Key;}
            }
            if(match is null)continue;
            var hit=match.Value;
            var selected=selectedFields.Where(hit.Value.Contains).Concat(new[]{"ApprovedAt","ApprovedBy","ApprovedAtUtc","ApprovedByUserId","RowVersion"}.Where(f=>hit.Value.Contains(f)&&!selectedFields.Contains(f))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            using var cmd=c.CreateCommand();using var cancel=SyncCancellation.Register(cmd,cancellationToken);cmd.CommandTimeout=120;cmd.CommandText="SELECT "+string.Join(',',selected.Select(Quote))+" FROM "+(sql?Quote(hit.Key.Schema)+".":"")+Quote(hit.Key.Name)+" ORDER BY "+Quote(orderKey);
            using var reader=cmd.ExecuteReader();var rows=new List<JsonObject>();while(reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();var row=new JsonObject();for(int i=0;i<reader.FieldCount;i++){object? value=reader.IsDBNull(i)?null:reader.GetValue(i);row[reader.GetName(i)]=value is byte[] bytes&&reader.GetName(i)=="RowVersion"?JsonValue.Create(Convert.ToHexString(bytes)):SourceReader.Value(value);}rows.Add(row);
            }
            target.Add(Table(contract.Kind),rows);
        }
    }
    static string RowKey(JsonObject raw,HsFactContract contract)
    {
        string id=raw.S(contract.Key);if(id!="")return id;
        if(contract.PreferKey!=null){id=raw.S(contract.PreferKey);if(id!="")return id;}
        return "";
    }
    public static void AddToPlan(SourceSnapshot source,ImportPlan plan,Dictionary<string,string> companies)
    {
        var wallet=source.Tables.GetValueOrDefault(Table("hs_wallet_event"))??[];var batches=source.Tables.GetValueOrDefault(Table("hs_remittance"))??[];var allocations=source.Tables.GetValueOrDefault(Table("hs_remittance_allocation"))??[];
        var accounts=plan.Documents.Where(d=>d.Kind=="platform_account"&&(ErpPlatformCode.AppFromErp(d.Body.S("platformCode"))=="hs"||string.Equals(d.Body.S("platformCode"),"hs",StringComparison.OrdinalIgnoreCase)||string.Equals(d.Body.S("platformCode"),"hungerstation",StringComparison.OrdinalIgnoreCase)||string.Equals(d.Body.S("platformCode"),"HUNGER",StringComparison.OrdinalIgnoreCase))).ToDictionary(d=>d.Body.S("erpSourceId"),d=>d.Body.S("companyId"));
        string Company(JsonObject row,HsFactContract contract)
        {
            if(contract.Kind=="hs_wallet_event")
            {
                string sponsor=row.S("SponsorId");if(sponsor!="")return companies.GetValueOrDefault(sponsor)??"";
                return accounts.GetValueOrDefault(row.S("PlatformRiderAccountId",row.S("PlatformAccountId")))??"";
            }
            if(contract.Kind is "hs_handover_review" or "hs_month_close" or "hs_daily_operation" or "hs_remittance" or "hs_remittance_allocation")
                return accounts.GetValueOrDefault(row.S("PlatformRiderAccountId",row.S("PlatformAccountId")))??"";
            if(contract.Kind=="hs_custody")return companies.GetValueOrDefault(row.S("SponsorId"))??"";
            string batch=row.S("BatchId");var links=allocations.Where(a=>a.S("BatchId")==batch).ToList();if(links.Count==0)return "";var events=links.Select(a=>wallet.SingleOrDefault(w=>RowKey(w,Contracts[0])==a.S("SourceEventId"))).ToList();if(events.Any(w=>w==null||w.S("ApprovedAt")==""||w.S("ApprovedBy")==""))return "";var ids=events.Select(w=>companies.GetValueOrDefault(w!.S("SponsorId"))??"").Distinct().ToArray();return ids.Length==1?ids[0]:"";
        }
        if(wallet.Where(w=>w.S("IdempotencyKey")!="").GroupBy(w=>w.S("IdempotencyKey"),StringComparer.Ordinal).Any(g=>g.Count()>1))throw new ImportFailure("hs_duplicate_idempotency_key");
        foreach(var contract in Contracts)
        {
            if(!source.Tables.TryGetValue(Table(contract.Kind),out var rows)){plan.Unavailable[contract.Kind]="source not available";continue;}
            if(rows.GroupBy(r=>RowKey(r,contract),StringComparer.Ordinal).Any(g=>g.Key==""||g.Count()>1))throw new ImportFailure("hs_source_identity_invalid:"+contract.Kind);
            foreach(var raw in rows)
            {
                bool approved=contract.Kind switch{
                    "hs_wallet_event"=>raw.S("ApprovedAt")!=""&&raw.S("ApprovedBy")!=""||raw.S("ApprovedAtUtc")!=""||raw.S("ApprovalStatus").Equals("Approved",StringComparison.OrdinalIgnoreCase),
                    "hs_remittance"=>raw.S("ConfirmedAt")!=""&&raw.S("ConfirmedBy")!=""||raw.S("Status")!=""&&!raw.S("Status").Equals("Draft",StringComparison.OrdinalIgnoreCase),
                    "hs_remittance_allocation"=>raw.S("ConfirmedAt")!=""||raw.S("Status")!="",
                    "hs_custody" or "hs_daily_operation"=>true,
                    _=>raw.S("ApprovedAt")!=""&&raw.S("ApprovedBy")!=""||raw.S("ApprovalId")!=""||raw.S("ApprovedAtUtc")!=""
                };
                if(!approved){plan.Reject(contract.Kind,"approval_not_available");continue;}
                string company=Company(raw,contract);if(company==""&&contract.Kind is not ("hs_custody" or "hs_daily_operation")){plan.Reject(contract.Kind,"company_relation_unknown");continue;}
                string key=RowKey(raw,contract);
                var body=new JsonObject{["id"]="erp:"+contract.Kind+":"+key,["erpSourceId"]=key,["erpSource"]=contract.Kind,["sourceEventId"]=key,["companyId"]=company,["platform"]="HungerStation",["status"]="approved",["sourceFacts"]=raw.Clone()};
                plan.Documents.Add(new(contract.Kind,body));
            }
        }
    }
}
