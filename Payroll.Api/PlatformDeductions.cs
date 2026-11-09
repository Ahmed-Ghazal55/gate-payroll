using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

public static class PlatformDeductions
{
    public const string Kind="platform_deduction";
    static readonly string[] Penalties=["stackingDeduction","declinedPenalty","latePenalty","noShowPenalty","noShowSpecialPenalty","acceptanceRatePenalty","missedDaysPenalty"];
    public static string Category(string value)=>value.ToLowerInvariant().Replace("_","").Replace("-","").Replace(" ","") switch
    {
        "trafficfine" or "tga"=>"tga",
        "platformpenalty" or "penalty" or "administrativededuction"=>"penalty",
        "riderbalance" or "platformtransferreddebt"=>"rider_balance",
        var other=>other
    };
    public static HashSet<string> Categories(IEnumerable<JsonObject>? tags,string rider,string month)
    {
        var all=(tags??[]).ToList();var reversed=all.Where(t=>t.S("status")=="source_reversal"&&t.S("reversesTagId")!="").Select(t=>t.S("reversesTagId")).ToHashSet();
        return all.Where(t=>t.S("status")=="confirmed"&&!reversed.Contains(t.S("id"))&&t["platformDeducted"] is JsonObject key&&key.S("rider")==rider&&key.S("month")==month).Select(t=>(PayPlatforms.For(t.S("platform")).App=="hs"?"":PayPlatforms.For(t.S("platform")).App+":")+Category(t["platformDeducted"].S("category"))).ToHashSet(StringComparer.Ordinal);
    }
    public static bool Skip(HashSet<string> categories,JsonObject deduction)
    {
        string platform=deduction.S("platform");
        string prefix=platform==""||PayPlatforms.For(platform).App=="hs"?"":PayPlatforms.For(platform).App+":";
        return categories.Contains(prefix+Category(deduction.S("category")));
    }
    static bool Tag(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject invoice,string source,string rider,string category,decimal signedAmount)
    {
        if(signedAmount==0)return false;
        J.Need(signedAmount!=decimal.MinValue,"قيمة الخصم خارج النطاق.");
        J.Need(System.Text.RegularExpressions.Regex.IsMatch(rider,@"^[12][0-9]{9}$")&&Store.Get(c,rider,"rider",tx) is {} person&&ErpRiderClassification.ListedAsRider(person),"هوية المندوب الفعلي مطلوبة لوسم خصم المنصة.");
        var key=new JsonObject{["category"]=category,["rider"]=rider,["month"]=invoice.S("period")};
        string id="platform-deducted:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source+"\n"+FinancialImports.Canonical(key))));
        var old=Store.Get(c,id,Kind,tx);decimal amount=Math.Abs(signedAmount);
        if(old!=null){J.Need(old.N("amount")==amount&&JsonNode.DeepEquals(old["platformDeducted"],key),"وسم الخصم موجود بوقائع مختلفة؛ لا تستبدله.");return false;}
        var doc=new JsonObject{["id"]=id,["platform"]="HungerStation",["companyId"]=invoice.S("companyId"),["invoiceId"]=invoice.S("id"),["sourceId"]=source,["platformDeducted"]=key,["amount"]=amount,["sourceSignedAmount"]=signedAmount,["status"]="confirmed",["createdBy"]=actor.Identity!.Name,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        Store.Save(c,Kind,doc,tx);Store.Audit(c,tx,"platform.hs.deduction_tagged",id,actor.Identity!.Name!);return true;
    }
    // Account classifications are provenance, not rider debts: never infer an actual rider.
    public static void ConfirmClassified(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject invoice,JsonObject line)
    {
        string app=PayPlatforms.For(invoice.S("platform")).App;J.Need(app is "ninja" or "keeta","مسار تصنيف منصة غير معتمد.");
        AccessPolicy.Demand(actor,"pay.approve");FinancialScopes.DemandSponsor(actor,invoice.S("companyId"));
        J.Need(FinancialScopes.Platform(actor,PayPlatforms.For(app).Platform),"المنصة خارج النطاق.");
        foreach(var d in (line["deductions"] as JsonArray??[]).OfType<JsonObject>())
        {
            J.Need(d.S("level") is "company_level" or "account_level" or "rider_level","تصنيف الخصم مطلوب.");
            string id="platform-deducted:"+StorageTransfer.CutoverHash(JsonValue.Create(line.S("id")+":"+d.S("category")));
            if(Store.Get(c,id,Kind,tx)!=null)continue;
            var tag=new JsonObject{["id"]=id,["platform"]=PayPlatforms.For(app).Platform,["companyId"]=invoice.S("companyId"),["invoiceId"]=invoice.S("id"),["sourceId"]=line.S("id"),["platformAccountId"]=line.S("platformAccountId"),["category"]=d.S("category"),["level"]=d.S("level"),["riderId"]=d.S("riderId"),["amount"]=d.N("amount"),["expenseAccount"]=d.S("expenseAccount"),["status"]="confirmed",["createdBy"]=actor.Identity!.Name};
            Store.Save(c,Kind,tag,tx);Store.Audit(c,tx,"platform."+app+".deduction_classified_confirmed",id,actor.Identity!.Name!);
        }
    }
    public static int ConfirmInvoice(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string invoiceId)
    {
        AccessPolicy.Demand(actor,"platforms.hs.approve");if(!FinancialScopes.Platform(actor,"HungerStation"))throw new ApiError("المنصة خارج النطاق المالي.",403);
        var invoice=HungerStationInvoices.GetInvoice(c,tx,actor,invoiceId);J.Need(invoice.S("approvalStatus")=="approved","وسوم الخصم تتطلب وقائع فاتورة معتمدة.");
        var request=invoice.Clone();request["requestedBy"]=invoice.S("createdBy");request["targetDepartment"]="FIN";request["requiredRankLevel"]=1;
        AccessPolicy.ValidateApproval(actor,request,new JsonObject(),"platforms.hs.approve",c,tx,"platform.hs.deductions_confirmed",invoice);if(invoice.B("selfApprovedException"))Store.Save(c,"platform_invoice",invoice,tx);int created=0;
        var lines=HungerStationInvoices.EffectiveLines(c,tx,invoiceId);
        var allocations=Store.List(c,"platform_allocation",tx).Where(a=>a.S("invoiceId")==invoiceId&&a.S("status")=="approved").ToList();
        J.Need(lines.Where(l=>l.N("tgaAmount")!=0).All(l=>allocations.Any(a=>a.S("lineId")==l.S("id")&&a.N("amount")==Math.Abs(l.N("tgaAmount")))),"لا يمكن تثبيت وسوم TGA قبل اعتماد التوزيع الكامل.");
        foreach(var line in lines.Where(l=>l.S("lineType")=="rider"&&l.S("riderId")!=""))
        {
            decimal penalty=Penalties.Sum(field=>line.N(field));
            if(penalty!=0&&Tag(c,tx,actor,invoice,line.S("id")+":penalties",line.S("riderId"),"penalty",penalty))created++;
            // A raw negative wallet/application balance is not proof of an already deducted rider amount.
            if(line.N("riderBalance")>0&&Tag(c,tx,actor,invoice,line.S("id")+":balance",line.S("riderId"),"rider_balance",line.N("riderBalance")))created++;
        }
        foreach(var allocation in allocations)
        foreach(var share in (allocation["riderShares"] as JsonArray??[]).OfType<JsonObject>())
            if(Tag(c,tx,actor,invoice,allocation.S("id"),share.S("riderId"),"tga",share.N("amount")))created++;
        return created;
    }
    public static void ReverseSource(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string originalId,JsonObject replacement)
    {
        AccessPolicy.Demand(actor,"platforms.hs.approve");var original=HungerStationInvoices.GetInvoice(c,tx,actor,originalId);
        J.Need(original.S("reversalJournalEntryId")!=""&&replacement.S("replacesInvoiceId")==originalId&&replacement.S("approvalStatus")=="approved","تصحيح الوسوم يحتاج نسخة معتمدة بعد عكس الأصل.");
        foreach(var tag in Store.List(c,Kind,tx).Where(t=>t.S("invoiceId")==originalId&&t.S("status")=="confirmed"))
        {
            string id="platform-deduction-reversal:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(replacement.S("id")+"\n"+tag.S("id"))));if(Store.Get(c,id,Kind,tx)!=null)continue;
            var reversal=new JsonObject{["id"]=id,["platform"]="HungerStation",["companyId"]=original.S("companyId"),["invoiceId"]=replacement.S("id"),["reversesTagId"]=tag.S("id"),["platformDeducted"]=tag["platformDeducted"]!.DeepClone(),["status"]="source_reversal",["createdBy"]=actor.Identity!.Name};
            Store.Save(c,Kind,reversal,tx);Store.Audit(c,tx,"platform.hs.deduction_source_reversed",id,actor.Identity!.Name!);
        }
    }
}
