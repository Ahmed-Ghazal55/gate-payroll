using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class HungerStationInvoices
{
    public static JsonObject Split(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string lineId,JsonObject input)
    {
        Scope(actor,"platforms.hs.manage");var line=GetLine(c,tx,actor,lineId);decimal total=Math.Abs(line.N("tgaAmount"));J.Need(total>0,"السطر لا يحتوي مبلغ TGA قابلًا للتوزيع.");
        J.Need(!(input.ContainsKey("riderPercent")&&(input.ContainsKey("riderAmount")||input.ContainsKey("companyAmount")||input.ContainsKey("riders")))&&!(input.ContainsKey("riders")&&input.ContainsKey("riderAmount")),"اختر طريقة توزيع واحدة صريحة.");
        var previous=Store.List(c,"platform_allocation",tx).Where(a=>a.S("lineId")==lineId).ToList();J.Need(!previous.Any(a=>a.S("status")=="approved"),"التوزيع المعتمد ثابت؛ التصحيح يحتاج عكسًا مستقلًا.");
        var shares=new JsonArray();decimal company;
        void Rider(string id,decimal amount)
        {
            J.Need(amount>=0&&amount==J.Money(amount),"نصيب المندوب غير صالح.");if(amount==0)return;
            J.Need(System.Text.RegularExpressions.Regex.IsMatch(id,@"^[12][0-9]{9}$")&&Store.Get(c,id,"rider",tx) is {} rider&&ErpRiderClassification.ListedAsRider(rider),"المندوب الفعلي مطلوب لنصيبه من TGA.");
            J.Need(!shares.OfType<JsonObject>().Any(s=>s.S("riderId")==id),"المندوب مكرر في التوزيع.");shares.Add(new JsonObject{["riderId"]=id,["amount"]=amount});
        }
        if(input["riders"] is JsonArray riders)
        {J.Need(riders.Count<=100&&riders.All(r=>r is JsonObject),"قائمة توزيع المناديب غير صالحة.");foreach(var item in riders.OfType<JsonObject>())Rider(item.S("riderId"),Amount(item.Required("amount"),false));company=Amount(input.Required("companyAmount"),false);}
        else
        {
            decimal riderAmount;
            if(input.ContainsKey("riderPercent")){decimal percent=Amount(input.S("riderPercent"),false);J.Need(percent is >=0 and <=100,"نسبة التوزيع غير صالحة.");riderAmount=J.Money(total*percent/100);company=total-riderAmount;}
            else {riderAmount=Amount(input.Required("riderAmount"),false);company=Amount(input.Required("companyAmount"),false);}
            Rider(input.S("riderId",line.S("riderId")),riderAmount);
        }
        J.Need(company>=0&&shares.OfType<JsonObject>().Sum(s=>s.N("amount"))+company==total,"نصيب الشركة والمناديب يجب أن يساوي TGA بالكامل.");
        var allocation=new JsonObject{["id"]="hs-allocation:"+Guid.NewGuid().ToString("N"),["lineId"]=lineId,["invoiceId"]=line.S("invoiceId"),["companyId"]=line.S("companyId"),["period"]=line.S("period"),["sourceAmount"]=line.N("tgaAmount"),["amount"]=total,["riderShares"]=shares,["companyAmount"]=company,["revision"]=previous.Count+1,["status"]="pending",["requestedBy"]=actor.Identity!.Name,["targetDepartment"]="FIN",["requiredRankLevel"]=1,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        Store.Save(c,"platform_allocation",allocation,tx);Store.Audit(c,tx,"platform.hs.allocation_proposed",allocation.S("id"),actor.Identity!.Name!);return allocation;
    }
    public static JsonObject ApproveAllocation(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        Scope(actor,"platforms.hs.approve");var allocation=Store.Get(c,id,"platform_allocation",tx)?.Clone()??throw new ApiError("التوزيع غير موجود.",404);var line=GetLine(c,tx,actor,allocation.S("lineId"));
        J.Need(allocation.S("status")=="pending","التوزيع ليس بانتظار الاعتماد.");
        var siblings=Store.List(c,"platform_allocation",tx).Where(a=>a.S("lineId")==line.S("id")).ToList();J.Need(!siblings.Any(a=>a.S("status")=="approved")&&allocation.N("revision")==siblings.Max(a=>a.N("revision")),"تغير التوزيع؛ راجع أحدث معاينة.");
        AccessPolicy.ValidateApproval(actor,allocation,new JsonObject(),"platforms.hs.approve",c,tx,"platform.hs.allocation_approved",allocation);
        allocation["status"]="approved";allocation["approvedBy"]=actor.Identity!.Name;allocation["approvedAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,"platform_allocation",allocation,tx);Store.Audit(c,tx,"platform.hs.allocation_approved",id,actor.Identity!.Name!);
        var updated=line.Clone();updated["allocationId"]=id;updated["status"]="pending_review";Store.Save(c,"platform_invoice_line",updated,tx);Store.Audit(c,tx,"platform.hs.invoice_line_allocated",line.S("id"),actor.Identity!.Name!);
        var invoice=GetInvoice(c,tx,actor,line.S("invoiceId")).Clone();
        if(invoice.S("status")=="pending_allocation"&&!Store.List(c,"platform_invoice_line",tx).Any(l=>l.S("invoiceId")==invoice.S("id")&&l.S("status")=="pending_allocation"))
        {invoice["status"]="pending_review";Store.Save(c,"platform_invoice",invoice,tx);Store.Audit(c,tx,"platform.hs.invoice_allocation_completed",invoice.S("id"),actor.Identity!.Name!);}
        return allocation;
    }
}
