using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class NinjaInvoices
{
    static HashSet<string>? Selection(DbConnection c,DbTransaction? tx,JsonObject input,string company)
    {
        if(!input.ContainsKey("accountIds"))return null;
        J.Need(input["accountIds"] is JsonArray,"accountIds يجب أن تكون قائمة.");
        var values=input["accountIds"]!.AsArray();J.Need(values.Count is >0 and <=1000&&values.All(v=>v is JsonValue j&&j.TryGetValue<string>(out var s)&&!string.IsNullOrWhiteSpace(s)),"حدد حسابات صالحة.");
        var selected=values.Select(v=>v!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);J.Need(selected.Count==values.Count,"حساب مكرر في الاختيار.");
        foreach(string id in selected)
        {var account=Store.Get(c,id,"platform_account",tx);J.Need(account!=null&&account.S("companyId")==company&&PayPlatforms.For(account.S("platformCode")).App=="ninja","الحساب المختار خارج شركة نينجا.");}
        return selected;
    }
    // ERP pull `ninja_account` is optional: the platform account's external id is the captain id (PullContracts sets CaptainId from it).
    static List<(string Captain,string Account)> CaptainAccounts(DbConnection c,DbTransaction? tx)
    {
        var pulled=Store.List(c,"ninja_account",tx).Select(a=>(Captain:a.S("CaptainId",a.S("captainId")),Account:a.S("platformAccountId")));
        var own=Store.List(c,"platform_account",tx).Where(a=>PayPlatforms.For(a.S("platformCode")).App=="ninja"&&!a.B("sourceDeleted")).Select(a=>(Captain:a.S("externalAccountId"),Account:a.S("id")));
        return pulled.Concat(own).Where(a=>a.Captain!=""&&a.Account!="").Distinct().ToList();
    }
    static bool InCycle(DbConnection c,DbTransaction? tx,string id)=>Store.List(c,PayCycles.Kind,tx).Any(p=>p.S("status") is not ("cancelled" or "reversed")&&(p["invoices"] as JsonArray??[]).OfType<JsonObject>().Any(i=>i.S("invoiceId")==id));
    public static JsonObject View(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject invoice)
    {
        Scope(actor,"pay.view",invoice.S("companyId"));var result=invoice.Clone();var actions=new JsonArray();string name=actor.Identity!.Name!;
        var lines=Lines(c,tx,invoice.S("id"));var parts=lines.SelectMany(l=>(l["deductions"] as JsonArray??[]).OfType<JsonObject>()).ToList();
        var disputes=Store.List(c,"platform_dispute",tx).Where(d=>d.S("invoiceId")==invoice.S("id")).ToList();
        bool manage=AccessPolicy.Has(actor,"pay.manage"),approve=AccessPolicy.Has(actor,"pay.approve"),independent=AccessPolicy.Independent(actor,invoice.S("createdBy"),"platform.invoice_approve"),cycle=InCycle(c,tx,invoice.S("id")),posted=invoice.S("journalEntryId")!="",open=!Store.List(c,"accounting_period",tx).Any(p=>p.S("companyId")==invoice.S("companyId")&&p.S("period")==invoice.S("period")&&p.S("status")=="closed");
        if(InvoiceDiscard.Active(invoice)&&!posted&&!cycle)
        {
            if(manage&&InvoiceDiscard.Can(c,tx,invoice))actions.Add("discard");
            if(manage&&invoice.S("approvalStatus")=="pending"&&parts.Count>0)actions.Add("classify");
            if(approve&&independent&&open&&invoice.S("approvalStatus")=="pending"&&parts.All(d=>(d.S("level") is "company_level" or "account_level" or "rider_level")&&AccessPolicy.Independent(actor,d.S("classifiedBy"),"platform.classification_approve")))actions.Add("approve");
            if(manage)actions.Add("dispute");
            if(approve&&disputes.Any(d=>d.S("status")=="open"&&AccessPolicy.Independent(actor,d.S("createdBy"),"platform.dispute_resolve")))actions.Add("resolve");
            if(approve&&independent&&open&&invoice.S("approvalStatus")=="approved"&&disputes.All(d=>d.S("status")=="resolved")&&parts.All(d=>d.S("level")=="company_level"))
            {try{PlatformPosting.Demand(c,"ninja",tx);actions.Add("post");}catch(ApiError){}}
        }
        var blockers=new JsonArray();
        if(!posted&&!cycle&&invoice.S("approvalStatus","pending")=="pending")
        {
            if(!approve)blockers.Add("حسابك لا يملك صلاحية اعتماد فواتير الرواتب.");
            if(!independent)blockers.Add("أنت من رفع هذه الفاتورة؛ يعتمدها مستخدم آخر (فصل الصلاحيات).");
            if(!open)blockers.Add("الفترة المحاسبية مغلقة.");
            int unclassified=parts.Count(d=>d.S("level") is not ("company_level" or "account_level" or "rider_level"));if(unclassified>0)blockers.Add($"{unclassified} خصم بلا تصنيف؛ صنّفها أولًا (على اليوزر أو على الشركة).");
            int ownClass=parts.Count(d=>!AccessPolicy.Independent(actor,d.S("classifiedBy"),"platform.classification_approve"));if(ownClass>0)blockers.Add($"أنت صنّفت {ownClass} خصم؛ يعتمدها مستخدم غير الذي صنّف.");
        }
        result["approvalBlockers"]=blockers;result["approvalStatus"]=invoice.S("approvalStatus","pending");result["allowedActions"]=actions;result["lines"]=new JsonArray(lines.Select(l=>(JsonNode?)l.Clone()).ToArray());result["disputes"]=new JsonArray(disputes.Select(d=>(JsonNode?)d.Clone()).ToArray());return result;
    }
    static JsonObject Act(Store store,HttpContext ctx,string id,Func<DbConnection,DbTransaction,JsonObject> action)=>store.Write((c,tx)=>{action(c,tx);return View(c,tx,ctx.User,Get(c,tx,ctx.User,id));});
}
