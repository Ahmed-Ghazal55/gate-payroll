using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Payroll;
namespace ErpImport;

public static partial class DebtSheetImporter
{
    public sealed record ReviewedOpening(DebtSheetPreview Preview,ThroughSixReview Review);
    public static ReviewedOpening PreviewReviewedOpening(byte[] debtBytes,byte[] deductionBytes,JsonObject mapping,List<JsonObject>? riders,
        Dictionary<string,List<JsonObject>>? erpHistory=null)
    {
        if(mapping.S("openingMethod")!="r6c_reviewed_rows_v1"||mapping.S("status")!="approved"||mapping.S("approvedBy")!="Ghazal"||mapping.S("approvedAt")!="2026-11-07"||
            mapping.S("octoberLoadMode")!="included_in_net"||mapping.S("effectiveFrom")!="2026-10"||mapping.S("balanceAsOf")!="2026-10-31")throw new ImportFailure("opening_decisions_not_approved");
        var review=ReviewThroughSix(debtBytes,deductionBytes,mapping,riders);
        var input=FreezeInput(debtBytes);var deductions=FreezeInput(deductionBytes);
        var rows=ReviewIdentities(input,mapping.Required("sheet"));ReviewIntegrity(rows);
        var archiveIssues=new JsonArray();var archives=ReviewArchives(input,rows,archiveIssues);ResolveReviewCreditors(rows,archives,riders);
        var evidence=new Dictionary<int,CreditorHistoryMatch>();
        if(erpHistory!=null)
        {
            foreach(var row in rows.Where(r=>r.Reasons.SetEquals(new[]{"creditor_unresolved"})||r.Reasons.Contains("creditor_unresolved")&&!r.Reasons.Overlaps(new[]{"iqama_invalid","iqama_duplicate","creditor_conflict"})))
            {
                var found=DebtCreditorHistory.Resolve(erpHistory,row.Identity,FinancialCore.Sponsors);evidence[row.Number]=found;
                if(found.Company!=""){row.Creditor=FinancialCore.ResolveSponsor(found.Company).Code;row.CreditorSource="erp_history";row.Reasons.Remove("creditor_unresolved");}
                else if(found.Reason!="creditor_unresolved")row.Reasons.Add(found.Reason);
            }
        }
        var reconciliation=ReconcileDeductions(deductions,mapping);
        var bucketMap=mapping["columns"]!.AsArray().OfType<JsonObject>().Where(c=>c.S("role")=="debt").OrderBy(c=>Column(c.S("column"))).ToList();
        if(bucketMap.Count!=35||bucketMap.Any(c=>!Configured(c)||c.S("status")=="needs_decision"))throw new ImportFailure("opening_bucket_mapping_unresolved");
        if(bucketMap.GroupBy(c=>c.S("category")).Any(g=>g.Select(c=>c.S("account")).Distinct().Count()!=1))throw new ImportFailure("category_has_conflicting_accounts");
        var documents=new List<PlannedDocument>();var newRiders=new List<JsonObject>();var rowReports=review.PrivateReport["rows"]!.AsArray().OfType<JsonObject>().ToDictionary(r=>(int)r.N("row"));
        var riderIndex=(riders??[]).GroupBy(r=>NormalizeReviewIdentity(r.S("iqama",r.S("id")))).ToDictionary(g=>g.Key,g=>g.ToList());
        int accepted=0;
        foreach(var row in rows)
        {
            var report=rowReports[row.Number];
            report["creditor"]=row.Creditor;report["creditorSource"]=row.CreditorSource;
            if(evidence.TryGetValue(row.Number,out var history))report["erpHistoryEvidence"]=new JsonObject{["tables"]=JsonSerializer.SerializeToNode(history.Tables),["columns"]=JsonSerializer.SerializeToNode(history.Columns),["basis"]=history.Basis};
            if(riders==null)row.Reasons.Add("payroll_rider_lookup_not_run");
            bool blocked=row.Reasons.Any(r=>r!="archive_net_differs")||row.Creditor=="blocked";
            riderIndex.TryGetValue(row.Identity,out var matches);var rider=matches?.Count==1?matches[0]:null;
            if(matches?.Count>1){row.Reasons.Add("ambiguous_rider_record");blocked=true;}
            string name=Cell(row.Cells,Column("B"));
            if(!blocked&&rider==null&&(name.Length is <2 or >200||name.Any(char.IsControl))){row.Reasons.Add("missing_or_invalid_debt_rider_name");blocked=true;}
            report["reasons"]=JsonSerializer.SerializeToNode(row.Reasons.Order());report["status"]=blocked?"blocked":"ready";
            if(blocked)continue;
            decimal gross=row.Buckets.Sum(),paid=Money(Cell(row.Cells,Column("BF"))),credit=Math.Max(0,J.Money(paid-gross));
            var remainder=AllocateOpeningFifo(row.Buckets,paid,bucketMap);
            if(!Close(remainder.Values.Sum()-credit,row.Net))throw new ImportFailure("opening_fifo_net_mismatch");
            string company=FinancialCore.ResolveSponsor(row.Creditor).Id;
            var rowPayload=new JsonObject{["row"]=row.Number,["identity"]=row.Identity,["buckets"]=JsonSerializer.SerializeToNode(row.Buckets),["paid"]=paid,["company"]=company,["effectiveFrom"]="2026-10",["mapping"]=JsonSerializer.SerializeToNode(bucketMap.Select(c=>new{category=c.S("category"),account=c.S("account")}))};
            string rowFingerprint=StorageTransfer.CutoverHash(rowPayload);
            string riderId=rider?.S("id")??row.Identity;string observedCompany=rider==null?"":ReviewCompany(rider);
            foreach(var item in remainder.Where(x=>x.Value>0))
            {
                string reference=$"DEBT-SHEET:{row.Number}:{item.Key.Category}";var meta=FinancialCore.ResolveCategory(item.Key.Category);
                documents.Add(new("debt",new JsonObject{["id"]=reference,["sourceRef"]=reference,["sourceRow"]=row.Number,["sourceFingerprint"]=rowFingerprint,["sourceRowFingerprint"]=rowFingerprint,["openingMethod"]="r6c_reviewed_rows_v1",
                    ["iqama"]=row.Identity,["riderId"]=riderId,["riderName"]=rider?.S("name")??name,["debtorType"]="Rider",["category"]=meta.Code,["categoryAr"]=meta.NameAr,["sector"]=meta.Sector,["entryMode"]="OpeningBalance",["allocationMode"]="RiderFull",
                    ["receivableAccountCode"]=item.Key.Account,["targetAccountCode"]=item.Key.Account,["creditorSponsorId"]=company,["originSponsorId"]=company,["sponsorshipSponsorId"]=rider==null?company:Company(rider),["creditorSource"]=row.CreditorSource,["riderCompanyAtPreview"]=observedCompany,
                    ["totalAmount"]=item.Value,["totalCostAmount"]=item.Value,["remainingBalance"]=item.Value,["paidAmount"]=0,["waivedAmount"]=0,["monthlyInstallment"]=0,["status"]="pending_approval",["openingSource"]="debt_sheet",["openingReviewRequired"]=true,["effectiveFrom"]="2026-10",["balanceAsOf"]="2026-10-31",["createdBy"]="debt-sheet-import"}));
            }
            if(credit>0)
            {
                string reference=$"DEBT-SHEET:{row.Number}:CREDIT";
                documents.Add(new("rider_credit",new JsonObject{["id"]=reference,["sourceRef"]=reference,["sourceRow"]=row.Number,["sourceFingerprint"]=rowFingerprint,["sourceRowFingerprint"]=rowFingerprint,["openingMethod"]="r6c_reviewed_rows_v1",["iqama"]=row.Identity,["riderId"]=riderId,["companyId"]=company,["creditorSource"]=row.CreditorSource,["riderCompanyAtPreview"]=observedCompany,["amount"]=credit,["status"]="pending_approval",["openingSource"]="debt_sheet",["openingReviewRequired"]=true,["effectiveFrom"]="2026-10",["balanceAsOf"]="2026-10-31"}));
            }
            if(rider==null&&(credit>0||remainder.Values.Sum()>0))newRiders.Add(new JsonObject{["id"]=row.Identity,["iqama"]=row.Identity,["name"]=name,["financeSponsorId"]=company,["sponsorId"]=company,["status"]="Inactive",["personRole"]="debt_only",["debtOnly"]=true,["isDeliveryRider"]=false,["openingSource"]="debt_sheet",["openingMethod"]="r6c_reviewed_rows_v1"});
            accepted++;
        }
        // The fingerprint binds all accepted financial rows and the exact source/mapping; it does
        // not depend on which rows already exist, so verification/replay use the same plan.
        string fingerprint=StorageTransfer.CutoverHash(new JsonObject{["debtHash"]=input.Sha256,["deductionHash"]=deductions.Sha256,["mapping"]=mapping.DeepClone(),["rows"]=new JsonArray(documents.Select(d=>(JsonNode)new JsonObject{["ref"]=d.Body.S("sourceRef"),["rowFingerprint"]=d.Body.S("sourceRowFingerprint")}).ToArray())});
        var summary=review.Summary;
        summary["completedThroughStep"]=10;summary["stopReason"]="operator_load_only_after_cutover_and_fresh_approval";summary["octoberLoadMode"]="included_in_net";summary["octoberDocumentsToLoad"]=0;
        summary["effectiveFrom"]="2026-10";summary["balanceAsOf"]="2026-10-31";summary["erpHistoryLookupPerformed"]=erpHistory!=null;
        summary["sponsorshipPeriodsAvailable"]=erpHistory?.ContainsKey("EmployeeSponsorshipPeriods")??false;
        summary["acceptedRows"]=accepted;summary["blockedRows"]=rows.Count-accepted;
        summary["reasonCounts"]=JsonSerializer.SerializeToNode(rows.SelectMany(r=>r.Reasons).GroupBy(r=>r).ToDictionary(g=>g.Key,g=>g.Count()));
        summary["openingDebts"]=documents.Count(d=>d.Kind=="debt");summary["riderCredits"]=documents.Count(d=>d.Kind=="rider_credit");summary["inactiveDebtOnlyRiders"]=newRiders.Count;
        summary["openingDebtTotal"]=documents.Where(d=>d.Kind=="debt").Sum(d=>d.Body.N("totalAmount"));summary["riderCreditTotal"]=documents.Where(d=>d.Kind=="rider_credit").Sum(d=>d.Body.N("amount"));
        summary["creditorTotals"]=new JsonArray(rows.GroupBy(r=>(r.Creditor,r.CreditorSource)).OrderBy(g=>g.Key.Creditor).Select(g=>(JsonNode)new JsonObject{["company"]=g.Key.Creditor,["creditorSource"]=g.Key.CreditorSource,["rows"]=g.Count(),["netTotal"]=g.Sum(r=>r.Net)}).ToArray());
        summary["companyCategoryOpeningTotals"]=new JsonArray(documents.Where(d=>d.Kind=="debt").GroupBy(d=>(d.Body.S("creditorSponsorId"),d.Body.S("category"))).Select(g=>(JsonNode)new JsonObject{["company"]=FinancialCore.ResolveSponsor(g.Key.Item1).Code,["category"]=g.Key.Item2,["count"]=g.Count(),["total"]=g.Sum(d=>d.Body.N("totalAmount"))}).ToArray());
        summary["reconciliation"]=reconciliation.Summary.DeepClone();summary["previewFingerprint"]=fingerprint;
        summary["canCommit"]=riders!=null&&reconciliation.Summary.B("matched")&&!summary.B("unexplainedMismatch")&&documents.Count>0;
        review.PrivateReport["summary"]=summary.DeepClone();review.PrivateReport["octoberLineage"]=reconciliation.Lineage;review.PrivateReport["deductionIssues"]=reconciliation.Issues;review.PrivateReport["carriedItems"]=reconciliation.Carried;
        return new(new(summary,documents,fingerprint,newRiders),review);
    }
    public static Dictionary<(string Category,string Account),decimal> AllocateOpeningFifo(decimal[] amounts,decimal payment,List<JsonObject> buckets)
    {
        if(amounts.Length!=buckets.Count||payment<0||amounts.Any(x=>x<0))throw new ImportFailure("negative_input_needs_decision");
        var result=new Dictionary<(string,string),decimal>();decimal left=payment;
        for(int b=0;b<buckets.Count;b++){decimal allocation=Math.Min(left,amounts[b]);left=J.Money(left-allocation);decimal balance=J.Money(amounts[b]-allocation);if(balance==0)continue;var key=(buckets[b].S("category"),buckets[b].S("account"));result[key]=result.GetValueOrDefault(key)+balance;}
        return result;
    }
    public static JsonObject VerifyReviewedOpening(DbConnection c,DebtSheetPreview preview,DbTransaction? tx=null)
    {
        int missing=0,changed=0;decimal debt=0,credit=0;
        foreach(var d in preview.Documents)
        {
            var actual=Store.Get(c,d.Body.S("id"),d.Kind,tx);
            if(actual==null){missing++;continue;}
            foreach(string key in new[]{"sourceRef","sourceRowFingerprint","iqama","category","creditorSponsorId","companyId","effectiveFrom","balanceAsOf","entryMode","totalAmount","amount","targetAccountCode"})
                if(actual[key]?.ToJsonString()!=d.Body[key]?.ToJsonString()){changed++;break;}
            if(d.Kind=="debt")debt+=actual.N("totalAmount");else credit+=actual.N("amount");
        }
        return new(){["verified"]=missing==0&&changed==0,["missing"]=missing,["changed"]=changed,["debts"]=preview.Documents.Count(d=>d.Kind=="debt"),["credits"]=preview.Documents.Count(d=>d.Kind=="rider_credit"),["debtTotal"]=debt,["creditTotal"]=credit};
    }
    public static JsonObject CommitReviewedOpening(Store target,DebtSheetPreview preview,string fingerprint,bool afterDbMove)
    {
        EnsureCommittable(preview);if(!afterDbMove||fingerprint!=preview.Fingerprint)throw new ImportFailure("opening_reviewed_fingerprint_and_cutover_required");
        return target.Write((c,tx)=>
        {
            if(Store.List(c,"cutover_receipt",tx).Count==0)throw new ImportFailure("opening_database_move_receipt_required");
            int added=0,ridersAdded=0;
            var currentRiders=Store.List(c,"rider",tx);
            foreach(var planned in preview.NewRiders)
            {
                var matches=currentRiders.Where(r=>NormalizeReviewIdentity(r.S("iqama",r.S("id")))==planned.S("iqama")).ToList();
                if(matches.Count>1)throw new ImportFailure("opening_target_identity_changed");
                if(matches.Count==1)continue;
                if(OpeningIdExists(c,planned.S("id"),tx))throw new ImportFailure("opening_target_identity_collision");
                Store.Save(c,"rider",planned.Clone(),tx);Store.Audit(c,tx,"debt.sheet.inactive_rider_staged",planned.S("id"),"debt-sheet-import");currentRiders.Add(planned);ridersAdded++;
            }
            foreach(var d in preview.Documents)
            {
                var people=currentRiders.Where(r=>NormalizeReviewIdentity(r.S("iqama",r.S("id")))==d.Body.S("iqama")).ToArray();
                if(people.Length!=1||people[0].S("id")!=d.Body.S("riderId"))throw new ImportFailure("opening_target_rider_changed");
                // Sheet/history creditors may differ from the current rider's company by decision.
                if(d.Body.S("creditorSource")=="rider_record"&&ReviewCompany(people[0])!=d.Body.S("riderCompanyAtPreview"))throw new ImportFailure("opening_target_company_changed");
                var existing=Store.Get(c,d.Body.S("id"),d.Kind,tx);
                if(existing!=null)
                {if(existing.S("sourceRowFingerprint")!=d.Body.S("sourceRowFingerprint"))throw new ImportFailure("opening_previously_imported_row_changed");continue;}
                if(OpeningIdExists(c,d.Body.S("id"),tx))throw new ImportFailure("opening_target_identity_collision");
                // Same row/category cannot exist under another identifier either.
                if(Store.List(c,d.Kind,tx).Any(x=>x.S("sourceRef")==d.Body.S("sourceRef")))throw new ImportFailure("opening_duplicate_source_reference");
                var body=d.Body.Clone();body.Remove("riderCompanyAtPreview");body["createdAt"]=DateTimeOffset.UtcNow.ToString("O");
                Store.Save(c,d.Kind,body,tx);Store.Audit(c,tx,"debt.sheet.opening_staged",body.S("id"),"debt-sheet-import");added++;
            }
            var verified=VerifyReviewedOpening(c,preview,tx);if(!verified.B("verified"))throw new ImportFailure("opening_post_load_verification_failed");
            verified["added"]=added;verified["inactiveRidersAdded"]=ridersAdded;verified["committed"]=true;return verified;
        });
    }
    static bool OpeningIdExists(DbConnection c,string id,DbTransaction tx)
    {
        using var command=c.CreateCommand();command.Transaction=tx;command.CommandText="SELECT kind FROM records WHERE id=@id";
        var parameter=command.CreateParameter();parameter.ParameterName="@id";parameter.Value=id;command.Parameters.Add(parameter);
        return command.ExecuteScalar()!=null;
    }
}
