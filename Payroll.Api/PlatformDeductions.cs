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
    public static HashSet<string> Categories(IEnumerable<JsonObject>? tags,string rider,string month)=>
        (tags??[]).Where(t=>t.S("status")=="confirmed"&&t.S("platform")=="HungerStation"&&t["platformDeducted"] is JsonObject key&&key.S("rider")==rider&&key.S("month")==month)
        .Select(t=>Category(t["platformDeducted"].S("category"))).ToHashSet(StringComparer.Ordinal);
    public static bool Skip(HashSet<string> categories,JsonObject deduction)
    {
        string platform=deduction.S("platform");
        if(platform!=""&&!platform.Equals("HungerStation",StringComparison.OrdinalIgnoreCase)&&!platform.Equals("hs",StringComparison.OrdinalIgnoreCase))return false;
        return categories.Contains(Category(deduction.S("category")));
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
    public static int ConfirmInvoice(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string invoiceId)
    {
        AccessPolicy.Demand(actor,"platforms.hs.approve");if(!FinancialScopes.Platform(actor,"HungerStation"))throw new ApiError("المنصة خارج النطاق المالي.",403);
        var invoice=HungerStationInvoices.GetInvoice(c,tx,actor,invoiceId);J.Need(invoice.S("approvalStatus")=="approved","وسوم الخصم تتطلب وقائع فاتورة معتمدة.");
        var request=invoice.Clone();request["requestedBy"]=invoice.S("createdBy");request["targetDepartment"]="FIN";request["requiredRankLevel"]=1;
        AccessPolicy.ValidateApproval(actor,request,new JsonObject(),"platforms.hs.approve");int created=0;
        var lines=Store.List(c,"platform_invoice_line",tx).Where(l=>l.S("invoiceId")==invoiceId).ToList();
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
}
