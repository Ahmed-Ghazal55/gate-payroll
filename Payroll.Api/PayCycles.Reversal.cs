using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PayCycles
{
    static JsonObject Reverse(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject cycle,JsonObject input)
    {
        PayValues.Fields(input,"action","period","note");string action=input.S("action");Scope(actor,cycle,action=="reversal_request"?"pay.manage":"pay.approve");J.Need(cycle.S("status") is "approved" or "partially_approved","العكس لدورة لها اعتماد رواتب فقط.");
        if(action=="reversal_request")
        {
            J.Need(cycle["reversalRequest"]==null,"طلب عكس موجود بالفعل.");string note=input.Required("note");J.Need(note.Length<=2000,"الملاحظة أطول من المسموح.");cycle["reversalRequest"]=new JsonObject{["requestedBy"]=actor.Identity!.Name,["period"]=J.Period(input.Required("period")),["note"]=note};Store.Save(c,Kind,cycle,tx);Store.Audit(c,tx,"pay.cycle_reversal_requested",cycle.S("id"),actor.Identity!.Name!);return cycle;
        }
        J.Need(action=="reversal_approve"&&cycle["reversalRequest"] is JsonObject,"طلب عكس مستقل مطلوب.");var request=cycle["reversalRequest"]!.AsObject();AccessPolicy.ValidateApproval(actor,new(){["requestedBy"]=request.S("requestedBy"),["targetDepartment"]="FIN",["requiredRankLevel"]=1},new JsonObject(),"pay.approve",c,tx,"pay.cycle_reversed",cycle);
        // Later debt activity must be resolved explicitly; never overwrite it with an old snapshot.
        foreach(var after in Rows(cycle["debtAfter"]).OfType<JsonObject>())J.Need(JsonNode.DeepEquals(after,Store.Get(c,after.S("id"),"debt",tx)),"حدثت حركة لاحقة على الدين؛ راجعها قبل عكس الدورة.");
        J.Need(!Store.List(c,Kind,tx).Any(d=>d.S("id")!=cycle.S("id")&&d.S("status") is "approved" or "partially_approved"&&string.CompareOrdinal(d.S("period"),cycle.S("period"))>0&&Lines(c,tx,d.S("id")).Any(l=>Lines(c,tx,cycle.S("id")).Any(old=>old.S("rider")==l.S("rider")))),"توجد دورة لاحقة؛ سوّ الحركات اللاحقة قبل العكس.");
        foreach(var rider in Rows(cycle["riders"]).OfType<JsonObject>())
        {
            decimal payable=Store.List(c,"journal",tx).Where(FinanceViews.Posted).SelectMany(j=>(j["lines"] as JsonArray??[]).OfType<JsonObject>()).Where(l=>l.S("sponsorId")==cycle.S("companyId")&&l.S("iqama")==rider.S("rider")&&l.S("accountCode") is "2110" or "212103").Sum(l=>l.N("credit")-l.N("debit"));
            J.Need(payable>=rider.N("net"),"تم صرف المستحق؛ سوّ الصرف قبل عكس الدورة.");
        }
        var journals=Store.List(c,"journal",tx).Where(j=>j.S("payCycleId")==cycle.S("id")&&j.S("reversesJournalId")=="").ToList();J.Need(journals.Count>0,"قيود الدورة غير موجودة.");
        var reversalIds=new Dictionary<string,string>();
        foreach(var journal in journals)
        {
            var reverse=new JsonArray();foreach(var old in journal["lines"]!.AsArray().OfType<JsonObject>()){var line=old.Clone();line["debit"]=old.N("credit");line["credit"]=old.N("debit");line["period"]=request.S("period");reverse.Add(line);}var posted=PayPosting.Post(c,tx,cycle,reverse,actor.Identity!.Name!,":reverse:"+journal.S("id"),journal.S("id"),request.S("period"));reversalIds[journal.S("id")]=posted.S("id");
        }
        foreach(var review in Rows(cycle["invoices"]).OfType<JsonObject>())
        {
            var invoice=Store.Get(c,review.S("invoiceId"),"platform_invoice",tx)!;
            if(invoice.S("payCycleId")!=cycle.S("id"))continue;
            invoice["reversalJournalEntryId"]=reversalIds[invoice.S("journalEntryId")];invoice["accountingStatus"]="reversed";Store.Save(c,"platform_invoice",invoice,tx);
        }
        foreach(var before in Rows(cycle["debtBefore"]).OfType<JsonObject>())Store.Save(c,"debt",before.Clone(),tx);
        foreach(var row in Rows(cycle["riders"]).OfType<JsonObject>().Where(r=>r.S("carryDebtId")!=""))
        {var debt=Store.Get(c,row.S("carryDebtId"),"debt",tx)!;debt["status"]="reversed";debt["remainingBalance"]=0;debt["reversedBy"]=actor.Identity!.Name;debt["reversalCycleId"]=cycle.S("id");Store.Save(c,"debt",debt,tx);}
        foreach(var ledger in Store.List(c,"ledger",tx).Where(l=>l.S("sourceRef")==cycle.S("id")&&l.S("kind")=="debt_installment"))Store.Save(c,"ledger_reversal",new(){["id"]="pay-reverse:"+ledger.S("id"),["reversesLedgerId"]=ledger.S("id"),["cycleId"]=cycle.S("id"),["amount"]=-ledger.N("amount"),["approvedBy"]=actor.Identity!.Name,["period"]=request.S("period")},tx);
        cycle["status"]="reversed";cycle["reversedBy"]=actor.Identity!.Name;cycle["reversalPeriod"]=request.S("period");Store.Save(c,Kind,cycle,tx);Store.Audit(c,tx,"pay.cycle_reversed",cycle.S("id"),actor.Identity!.Name!);return cycle;
    }
    public static void GuardLegacyRun(DbConnection c,DbTransaction tx,JsonObject run)
    {if(run.S("approvedAt")=="")return;J.Need(!Store.List(c,Kind,tx).Any(cycle=>cycle.S("status") is "approved" or "partially_approved"&&cycle.S("period")==run.S("period")&&Lines(c,tx,cycle.S("id")).Any(l=>l.S("rider")==run.S("iqama"))),"المندوب معتمد في المسار الجديد لنفس الشهر؛ لا تنشئ مسيرًا ثانيًا.");}
}
