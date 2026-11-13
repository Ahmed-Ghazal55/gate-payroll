using System.Data.Common;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PayCycles
{
    // Fingerprint only dependencies read by this company/month/rider review. Cache each kind
    // once: posting another company's payroll must not invalidate an unrelated draft.
    static string Fingerprint(DbConnection c,DbTransaction? tx,JsonObject cycle)
    {
        string company=cycle.S("companyId"),period=cycle.S("period"),postingPeriod=cycle.S("postingPeriod",period);
        var reviews=Rows(cycle["invoices"]).OfType<JsonObject>().ToList();
        var invoiceIds=reviews.Select(i=>i.S("invoiceId")).ToHashSet();
        var accountIds=Accounts(cycle).Select(a=>a.Account.S("accountId")).ToHashSet();
        var apps=reviews.Select(i=>i.S("app")).ToHashSet();
        var riders=Accounts(cycle).SelectMany(a=>Rows(a.Account["riders"]).OfType<JsonObject>()).Select(r=>r.S("rider")).ToHashSet();
        var cache=new Dictionary<string,List<JsonObject>>();var selected=new SortedDictionary<string,JsonObject>(StringComparer.Ordinal);
        List<JsonObject> All(string kind){if(!cache.TryGetValue(kind,out var docs))cache[kind]=docs=Store.List(c,kind,tx);return docs;}
        // Mirrored ops kinds: after capture, fingerprint only frozen pay_run_input rows (never live Store).
        List<JsonObject> Ops(string kind)=>PayRunInputs.Captured(cycle)?PayRunInputs.List(c,tx,cycle.S("id"),kind):[];
        void Add(string kind,IEnumerable<JsonObject> docs){foreach(var doc in docs)selected[kind+":"+doc.S("id")]=doc;}
        void Pick(string kind,Func<JsonObject,bool> predicate)=>Add(kind,All(kind).Where(predicate));
        bool Rider(JsonObject doc)=>riders.Contains(doc.S("rider",doc.S("riderId",doc.S("iqama"))));
        var assignments=All("pay_assignment").Where(d=>Rider(d)&&apps.Contains(d.S("app"))&&d.S("period")==period).ToList();
        var schemeIds=assignments.Select(d=>d.S("schemeId")).ToHashSet();
        var schemes=All("pay_scheme").Where(d=>schemeIds.Contains(d.S("id"))).ToList();
        var rulesIds=schemes.Select(d=>d["data"].S("eligibilitySetId")).Where(id=>id!="").ToHashSet();
        var debts=All("debt").Where(d=>Rider(d)&&string.CompareOrdinal(d.S("effectiveFrom","0000-00"),period)<=0).ToList();
        var debtIds=debts.Select(d=>d.S("id")).ToHashSet();
        var vehicleAssignments=Ops("vehicle_assignment").Where(d=>Rider(d)&&Overlaps(d,period,"startedAt","endedAt")).ToList();
        var vehicleIds=vehicleAssignments.Select(d=>d.S("vehicleId")).ToHashSet();
        Add("pay_assignment",assignments);Add("pay_scheme",schemes);Add("debt",debts);Add("vehicle_assignment",vehicleAssignments);
        Pick("pay_eligibility",d=>rulesIds.Contains(d.S("id")));
        Add("vehicle",Ops("vehicle").Where(d=>vehicleIds.Contains(d.S("id"))));
        // Only what the calculation reads: a routine ERP sync of a status or a registration must not strand an open cycle.
        Add("rider",All("rider").Where(d=>riders.Contains(d.S("id"))).Select(d=>new JsonObject{["id"]=d.S("id"),["sponsorId"]=d.S("sponsorId"),["engagementType"]=d.S("engagementType"),["personRole"]=d.S("personRole"),["debtOnly"]=d.B("debtOnly")}));
        Add("platform_account",Ops("platform_account").Where(d=>accountIds.Contains(d.S("id"))).Select(d=>new JsonObject{["id"]=d.S("id"),["companyId"]=d.S("companyId"),["platformCode"]=d.S("platformCode"),
            ["assignments"]=new JsonArray((d["assignments"] as JsonArray??[]).OfType<JsonObject>().Where(a=>riders.Contains(a.S("riderId"))&&Overlaps(a,period)).Select(a=>(JsonNode?)new JsonObject{["riderId"]=a.S("riderId"),["from"]=a.S("RawEffectiveFrom",a.S("EffectiveFrom")),["to"]=a.S("RawEffectiveTo",a.S("EffectiveTo"))}).OrderBy(a=>a.S("riderId"),StringComparer.Ordinal).ThenBy(a=>a.S("from"),StringComparer.Ordinal).ToArray())}));
        Pick("platform_invoice",d=>invoiceIds.Contains(d.S("id")));Pick("platform_invoice_line",d=>invoiceIds.Contains(d.S("invoiceId")));
        foreach(string kind in new[]{"platform_invoice_action","platform_dispute","platform_allocation","operations_import"})Pick(kind,d=>invoiceIds.Contains(d.S("invoiceId")));
        Add("ninja_daily",Ops("ninja_daily").Where(d=>accountIds.Contains(d.S("platformAccountId"))&&d.S("companyId")==company&&d.S("date").StartsWith(period+"-",StringComparison.Ordinal)));
        Pick("adjustment",d=>Rider(d)&&d.S("period")==period);Pick("run",d=>Rider(d)&&d.S("period")==period);
        var ledger=All("ledger").Where(d=>Rider(d)&&d.S("period")==period).ToList();var ledgerIds=ledger.Select(d=>d.S("id")).ToHashSet();
        Add("ledger",ledger);Pick("ledger_reversal",d=>ledgerIds.Contains(d.S("reversesLedgerId")));
        Pick("journal",j=>FinanceViews.Posted(j)&&(j.S("payCycleId")==cycle.S("id")||Rows(j["lines"]).OfType<JsonObject>().Any(l=>l.S("sponsorId")==company&&(debtIds.Contains(l.S("debtId"))||riders.Contains(l.S("iqama"))&&l.S("accountCode") is "2110" or "212103" or "1211"&&string.CompareOrdinal(j.S("period"),postingPeriod)<=0))));
        Pick("accounting_period",d=>d.S("companyId")==company&&d.S("period")==postingPeriod);
        // The posting gate is not a calculation fact and is checked when posting; opening it must not void a review.
        var tags=All("platform_deduction").Where(d=>invoiceIds.Contains(d.S("invoiceId"))||riders.Contains(d["platformDeducted"].S("rider"))&&d["platformDeducted"].S("month")==period).ToList();
        var tagIds=tags.Select(d=>d.S("id")).ToHashSet();Add("platform_deduction",tags);Pick("platform_deduction",d=>tagIds.Contains(d.S("reversesTagId")));
        var categories=debts.Select(d=>d.S("category")).Concat(All("adjustment").Where(d=>Rider(d)&&d.S("period")==period).Select(d=>d.S("category"))).ToHashSet();
        var adjustmentTypes=Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("type")=="review_adjustment").Select(d=>d.S("category")).ToHashSet();
        Pick("config_list_item",d=>d.S("group")=="deduction_types"&&categories.Contains(d.S("code"))||d.S("group")=="pay_adjustment_types"&&adjustmentTypes.Contains(d.S("code")));
        // Resolve, rather than hash every version or another month/app's settings. Missing
        // settings retain their existing stage-specific validation; newly resolved facts block.
        foreach(string app in apps)try{Add("pay_terms",[PaySettings.Resolve(c,tx,"pay_terms",app,period)]);}catch(ApiError){}
        try{Add("pay_setting",[PaySettings.Global(c,tx,period+"-01")]);}catch(ApiError){}
        return Hash(string.Join("\n",selected.Select(d=>d.Key+":"+FinancialImports.Canonical(d.Value))));
    }
}
