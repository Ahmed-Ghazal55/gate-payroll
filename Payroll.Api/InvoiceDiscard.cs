using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static class InvoiceDiscard
{
    public static bool Active(JsonObject invoice)=>invoice.S("status")!="discarded"&&invoice.S("approvalStatus")!="discarded";
    public static bool Can(DbConnection c,DbTransaction? tx,JsonObject invoice)=>Active(invoice)&&invoice.S("approvalStatus","pending")=="pending"&&invoice.S("journalEntryId")==""&&invoice.S("payCycleId")==""&&!Store.List(c,PayCycles.Kind,tx).Any(p=>(p["invoices"] as JsonArray??[]).OfType<JsonObject>().Any(i=>i.S("invoiceId")==invoice.S("id")));
    // Stable across preview/commit, while retaining every prior upload and source row.
    public static string ReplacementId(IEnumerable<JsonObject> invoices,string id,string company,string period,string reference,string platform)
    {int discarded=invoices.Count(i=>!Active(i)&&i.S("companyId")==company&&i.S("period")==period&&i.S("reference")==reference&&i.S("platform")==platform);return discarded==0?id:id+":replacement-"+discarded;}
    public static JsonObject Discard(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string app,string id,JsonObject input)
    {
        var invoice=app=="ninja"?NinjaInvoices.Get(c,tx,actor,id,"pay.manage"):KeetaInvoices.Workflow.Get(c,tx,actor,id,"pay.manage");
        J.Need(Can(c,tx,invoice),"الاستبعاد لفاتورة معلقة غير معتمدة ولا مرحلة ولا مرتبطة بدورة فقط.",409);
        string reason=input.Required("reason").Trim();J.Need(reason.Length is >=3 and <=500&&!reason.Any(char.IsControl),"اكتب سبب الاستبعاد.");
        invoice["status"]="discarded";invoice["approvalStatus"]="discarded";invoice["discardReason"]=reason;invoice["discardedBy"]=actor.Identity!.Name;invoice["discardedAt"]=DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c,"platform_invoice",invoice,tx);Store.Audit(c,tx,"platform."+app+".invoice_discarded",id,actor.Identity!.Name!);return invoice;
    }
}
