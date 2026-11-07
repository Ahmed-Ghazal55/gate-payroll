using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class StorageTransfer
{
    // Explicit ownership, deliberately not a catch-all: a new field must be reviewed.
    static readonly HashSet<string> HrFields = new(StringComparer.Ordinal)
    {
        "name","nameEn","nameAr","iqama","nationality","phone","engagementType","isEmployee","personRole","isDeliveryRider",
        "sponsorId","sponsor","companyId","cityId","city","hireDate","status","erpRiderProfileIds","erpSource","sourceId","sourceSystem",
        "sourceFingerprint","sourceRef","importedAt","erpManaged","erpFingerprint","erpImportedAt","createdAt","updatedAt","debtOnly",
        "plateNumberAr","plateNumberEn","registrationType","plate","vehicleType","model","modelYear","make","color","vin"
    };
    static readonly HashSet<string> PayrollFields = new(StringComparer.Ordinal)
    {"iban","ibanChoice","ibanSource","ibanVerified","bankName","bankCode","accountName","selectedIban","financeSponsorId","policy","policyId","payPolicy","paySchemeId","payrollPolicy","salary","salaryType","salarySystem","odooVoucherNo"};
    static readonly HashSet<string> PayrollKinds = new(StringComparer.Ordinal)
    {"user","setting","debt","debt_settlement","debt_transfer","journal","ledger","run","campaign","submission","custom_form","custom_response","form","pay_cycle","pay_cycle_line","pay_terms","pay_scheme","pay_setting","pay_assignment","pay_eligibility","advance_request","rider_ask","accounting_period","opening_reconciliation","promissory_note","rider_credit","platform_invoice","platform_invoice_line","platform_invoice_action","platform_dispute","platform_allocation","platform_deduction"};

    public static JsonArray ReadSnapshot(DbConnection c, DbTransaction? tx=null)
    {
        using var q=Store.Command(c,tx,"SELECT id,kind,body FROM records ORDER BY id");using var reader=q.ExecuteReader();var rows=new JsonArray();
        while(reader.Read())rows.Add(new JsonObject{["id"]=reader.GetString(0),["kind"]=reader.GetString(1),["body"]=JsonNode.Parse(reader.GetString(2))});
        return rows;
    }
    public static string CutoverHash(JsonNode? node)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FinancialImports.Canonical(node))));
    static string RowsHash(JsonArray rows)=>CutoverHash(new JsonArray(rows.OfType<JsonObject>().OrderBy(r=>r.S("id"),StringComparer.Ordinal).Select(r=>(JsonNode?)r.DeepClone()).ToArray()));
    static Dictionary<string,JsonObject> Index(JsonArray rows)
    {
        var result=new Dictionary<string,JsonObject>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in rows)
        {
            var row=item as JsonObject??throw new ApiError("cutover_invalid_record");string id=row.Required("id"),kind=row.Required("kind");
            J.Need(id.Length<=128&&System.Text.RegularExpressions.Regex.IsMatch(kind,"^[a-z_]{1,40}$")&&row["body"] is JsonObject b&&b.S("id")==id&&result.TryAdd(id,row),"cutover_invalid_or_duplicate_id");
        }
        return result;
    }
    // Contains private source bodies: only save this plan under the operator's private directory.
    // Summary is the only object that can be printed.
    public static JsonObject PlanMerge(JsonArray sqlite, JsonArray target)
    {
        var left=Index(sqlite);var right=Index(target);var output=right.ToDictionary(x=>x.Key,x=>x.Value.Clone(),StringComparer.OrdinalIgnoreCase);
        var collisions=new JsonArray();var conflicts=new JsonArray();int added=0,merged=0;
        void Conflict(string kind,string id,string reason)=>conflicts.Add(new JsonObject{["kind"]=kind,["idHash"]=CutoverHash(JsonValue.Create(kind+":"+id)),["reason"]=reason});
        foreach(var (id,src) in left.OrderBy(x=>x.Key,StringComparer.Ordinal))
        {
            if(!right.TryGetValue(id,out var dst)){output[id]=src.Clone();added++;continue;}
            string kind=src.S("kind");collisions.Add(new JsonObject{["kind"]=kind,["idHash"]=CutoverHash(JsonValue.Create(kind+":"+id))});
            if(kind!=dst.S("kind")||id!=dst.S("id")){Conflict(kind,id,"kind_or_id_case_collision");continue;}
            var sb=(JsonObject)src["body"]!;var tb=(JsonObject)dst["body"]!;
            if(CutoverHash(sb)==CutoverHash(tb))continue;
            if(sb.S("voucherNo")!=""&&tb.S("voucherNo")!=""&&sb.S("voucherNo")!=tb.S("voucherNo")){Conflict(kind,id,"issued_voucher_collision");continue;}
            var body=PayrollKinds.Contains(kind)?sb.Clone():tb.Clone();bool unresolved=false;
            foreach(var field in sb)
            {
                if(!body.ContainsKey(field.Key)||JsonNode.DeepEquals(body[field.Key],field.Value)){body[field.Key]=field.Value?.DeepClone();continue;}
                if(PayrollKinds.Contains(kind)||PayrollFields.Contains(field.Key))body[field.Key]=field.Value?.DeepClone();
                else if(!HrFields.Contains(field.Key)){unresolved=true;}
            }
            if(unresolved){Conflict(kind,id,"unclassified_field_conflict");continue;}
            // Both entire originals are retained separately; losing fields are never discarded.
            body["mergedFrom"]=new JsonObject{["policy"]="erp-hr-sqlite-payroll-v1",["sqliteFingerprint"]=CutoverHash(sb),["targetFingerprint"]=CutoverHash(tb)};
            output[id]=new JsonObject{["id"]=id,["kind"]=kind,["body"]=body};merged++;
        }
        var rows=new JsonArray(output.Values.OrderBy(r=>r.S("id"),StringComparer.Ordinal).Select(r=>(JsonNode?)r).ToArray());
        // SQL Server has no legacy SQLite uniqueness indexes. Validate logical uniqueness before merging.
        foreach(var group in rows.OfType<JsonObject>().Where(r=>r.S("kind") is "user" or "run" or "submission" or "rider").GroupBy(r=>
        {
            var b=(JsonObject)r["body"]!;return r.S("kind") switch
            {"user"=>"user:"+UserAccess.Normalize(b.S("username")),"run"=>"run:"+b.S("iqama")+"|"+b.S("period"),"submission"=>"submission:"+b.S("campaignId")+"|"+b.S("iqama"),_=>"rider:"+b.S("iqama",b.S("id"))};
        },StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))foreach(var row in group)Conflict(row.S("kind"),row.S("id"),"logical_unique_collision");
        JsonObject Counts(JsonArray a)=>new(a.OfType<JsonObject>().GroupBy(r=>r.S("kind")).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new KeyValuePair<string,JsonNode?>(g.Key,JsonValue.Create(g.Count()))));
        var summary=new JsonObject{["sourceCounts"]=Counts(sqlite),["targetCounts"]=Counts(target),["resultCounts"]=Counts(rows),["added"]=added,["merged"]=merged,["collisions"]=collisions,["conflicts"]=conflicts,["ready"]=conflicts.Count==0};
        return new(){["policy"]="erp-hr-sqlite-payroll-v1",["sourceHash"]=RowsHash(sqlite),["targetHash"]=RowsHash(target),["source"]=sqlite.DeepClone(),["target"]=target.DeepClone(),["rows"]=rows,["summary"]=summary};
    }
    static JsonObject ValidatePlan(JsonObject plan,string fingerprint)
    {
        J.Need(CutoverHash(plan)==fingerprint,"cutover_plan_fingerprint_mismatch");
        var fresh=PlanMerge((JsonArray)plan["source"]!,(JsonArray)plan["target"]!);
        J.Need(CutoverHash(fresh)==fingerprint&&fresh["summary"]!.AsObject().B("ready"),"cutover_unresolved_or_modified_plan");return fresh;
    }
    public static JsonObject VerifyMerge(DbConnection c, JsonObject plan, string fingerprint,DbTransaction? tx=null)
    {
        ValidatePlan(plan,fingerprint);var actual=Index(ReadSnapshot(c,tx));int missing=0,changed=0;
        foreach(var row in ((JsonArray)plan["rows"]!).OfType<JsonObject>())
        {if(!actual.TryGetValue(row.S("id"),out var found))missing++;else if(CutoverHash(found)!=CutoverHash(row))changed++;}
        bool receipt=actual.TryGetValue("cutover:"+fingerprint,out var record)&&record.S("kind")=="cutover_receipt";
        return new(){["verified"]=missing==0&&changed==0&&receipt,["expected"]=plan["rows"]!.AsArray().Count,["missing"]=missing,["changed"]=changed,["receiptPresent"]=receipt};
    }
    // Caller provides an already-open connection; preview never creates Store, indexes or checkpoints.
    // The only write entry point is explicitly invoked by the operator after approval.
    public static JsonObject ApplyMerge(DbConnection c,JsonObject plan,string fingerprint)
    {
        var reviewed=ValidatePlan(plan,fingerprint);
        using var tx=c.BeginTransaction(System.Data.IsolationLevel.Serializable);
        if(c is Microsoft.Data.SqlClient.SqlConnection)
        {
            using var guard=Store.Command(c,tx,"DECLARE @r int; EXEC @r=sp_getapplock @Resource='GatePayroll.DocumentWrite',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000; IF @r<0 THROW 51000,'Cutover busy',1;");guard.ExecuteNonQuery();
        }
        if(Store.Get(c,"cutover:"+fingerprint,"cutover_receipt",tx)!=null)
        {var prior=VerifyMerge(c,reviewed,fingerprint,tx);J.Need(prior.B("verified"),"cutover_previously_applied_records_changed");tx.Commit();return prior;}
        J.Need(RowsHash(ReadSnapshot(c,tx))==reviewed.S("targetHash"),"cutover_target_changed_preview_again");
        var target=Index((JsonArray)reviewed["target"]!);var source=Index((JsonArray)reviewed["source"]!);
        foreach(var row in ((JsonArray)reviewed["rows"]!).OfType<JsonObject>())
        {
            string id=row.S("id");if(target.TryGetValue(id,out var old)&&CutoverHash(old)==CutoverHash(row))continue;
            // Archive both sides once, inside the same transaction, before updating any record.
            foreach(string side in new[]{"source","target"})
            {
                if(!(side=="source"?source:target).TryGetValue(id,out var original))continue;
                string archiveId="cutover-origin:"+CutoverHash(JsonValue.Create(fingerprint+":"+side+":"+id));
                Store.Save(c,"cutover_origin",new(){["id"]=archiveId,["side"]=side,["record"]=original.DeepClone(),["planFingerprint"]=fingerprint},tx);
            }
            Store.Save(c,row.S("kind"),(JsonObject)row["body"]!,tx,preserveSnapshot:true);
        }
        Store.Save(c,"cutover_receipt",new(){["id"]="cutover:"+fingerprint,["planFingerprint"]=fingerprint,["sourceHash"]=reviewed["sourceHash"]!.DeepClone(),["targetHash"]=reviewed["targetHash"]!.DeepClone(),["at"]=DateTimeOffset.UtcNow.ToString("O")},tx);
        var result=VerifyMerge(c,reviewed,fingerprint,tx);J.Need(result.B("verified"),"cutover_verification_failed");tx.Commit();return result;
    }
}
