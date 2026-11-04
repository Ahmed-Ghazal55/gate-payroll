using System.Data.Common;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PayCycles
{
    // Normalized manual upload for apps without a spreadsheet adapter. The independent
    // stage-7 checker approves this source and the payroll together; never auto-approve it.
    static void UploadInvoices(DbConnection c,DbTransaction tx,JsonObject cycle)
    {
        foreach(var review in Rows(cycle["invoices"]).OfType<JsonObject>())
        {
            if(review["invoice"] is not JsonObject upload)continue;
            PayValues.Fields(upload,"reference","sourceSha256","amountExclVat","vat","deductionsTotal","lines");string app=PayPlatforms.App(review.Required("app")),reference=upload.Required("reference"),sha=upload.Required("sourceSha256");
            J.Need(app!="jahez"&&reference.Length<=200&&System.Text.RegularExpressions.Regex.IsMatch(sha,"^[A-Fa-f0-9]{64}$"),"مرجع وبصمة مصدر أصلية مطلوبان للرفع المعياري.");
            var sources=Rows(upload["lines"]).OfType<JsonObject>().ToList();J.Need(sources.Count>0&&sources.Select(l=>l.S("lineId")).Distinct().Count()==sources.Count,"سطور مصدر فريدة مطلوبة.");
            J.Need(!Store.List(c,"platform_invoice",tx).Any(i=>i.S("companyId")==cycle.S("companyId")&&PayPlatforms.For(i.S("platform")).App==app&&i.S("reference")==reference&&(i.S("approvalStatus")=="approved"||i.S("payCycleId")!=""&&Store.Get(c,i.S("payCycleId"),Kind,tx)?.S("status") is "draft" or "review" or "needs_decision")),"مرجع فاتورة مستخدم؛ لا تكرر الرفع.");
            string id="pay-invoice:"+Guid.NewGuid().ToString("N");var source=new JsonObject{["id"]=id,["platform"]=PayPlatforms.For(app).Platform,["companyId"]=cycle.S("companyId"),["period"]=cycle.S("period"),["reference"]=reference,["sourceSha256"]=sha.ToUpperInvariant(),["amountExclVat"]=Money(upload,"amountExclVat"),["vat"]=Money(upload,"vat"),["deductionsTotal"]=Money(upload,"deductionsTotal"),["approvalStatus"]="cycle_pending",["payCycleId"]=cycle.S("id"),["createdBy"]=cycle.S("createdBy"),["createdAt"]=cycle.S("createdAt")};
            var ids=new Dictionary<string,string>();foreach(var l in sources)
            {
                PayValues.Fields(l,"lineId","accountId","baseGross","incentives","orders","deductionAmount");string key=l.Required("lineId");J.Need(key.Length<=128,"مرجع سطر المصدر أطول من المسموح.");string lineId="pay-invoice-line:"+Guid.NewGuid().ToString("N");ids[key]=lineId;var row=new JsonObject{["id"]=lineId,["invoiceId"]=id,["sourceLineRef"]=key,["platformAccountId"]=l.S("accountId"),["baseGross"]=Money(l,"baseGross"),["incentives"]=Money(l,"incentives"),["deductionAmount"]=Money(l,"deductionAmount"),["orders"]=PayValues.Number(l,"orders",0,int.MaxValue)};Store.Save(c,"platform_invoice_line",row,tx);
            }
            J.Need(sources.Sum(l=>l.N("baseGross")+l.N("incentives"))==source.N("amountExclVat")&&sources.Sum(l=>l.N("deductionAmount"))==source.N("deductionsTotal"),"سطور الرفع لا تطابق الإجماليات.");
            foreach(var row in Rows(review["accounts"]).Concat(Rows(review["deductions"])) .OfType<JsonObject>()){J.Need(ids.TryGetValue(row.S("sourceLineId"),out _),"مرجع المراجعة لا يطابق سطر الرفع.");row["sourceLineId"]=ids[row.S("sourceLineId")];}
            review["invoiceId"]=id;review.Remove("invoice");Store.Save(c,"platform_invoice",source,tx);Store.Audit(c,tx,"pay.invoice_uploaded",id,cycle.S("createdBy"));
        }
    }
    public static void GuardInvoicePosting(DbConnection c,DbTransaction tx,string reference)
    {
        if(!reference.StartsWith("HS-INVOICE:",StringComparison.Ordinal))return;string id=reference["HS-INVOICE:".Length..];
        J.Need(!Store.List(c,Kind,tx).Any(d=>d.S("status")=="approved"&&Rows(d["invoices"]).OfType<JsonObject>().Any(i=>i.S("invoiceId")==id)),"الفاتورة مرحّلة من دورة الرواتب؛ لا ترحلها ثانية في المسار القديم.");
    }
}
