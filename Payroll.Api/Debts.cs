using System.Data.Common;
using System.Text.Json.Nodes;
namespace Payroll;

public static class Debts
{
    public static readonly string[] Categories=["Advance","TrafficFine","EquipmentLoan","VehicleInstallment","AdministrativeDeduction"];
    public static decimal Amount(JsonObject input,string key)
    { J.Need(decimal.TryParse(input.S(key),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var n)&&n>0&&n<100000000&&n==J.Money(n),"قيمة مالية غير صالحة: "+key);return n; }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/debts",()=>store.Read(c=>Store.List(c,"debt")));
        api.MapPost("/debts",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>{
            string iqama=DirectoryProfiles.Iqama(input.Required("iqama"));J.Need(Store.Get(c,iqama,"rider",tx)!=null,"المندوب غير موجود.");
            string category=input.Required("category");J.Need(Categories.Contains(category),"فئة دين غير صالحة.");
            decimal total=Amount(input,"totalAmount"),installment=Amount(input,"monthlyInstallment");J.Need(installment<=total,"القسط يتجاوز إجمالي الدين.");
            J.Need(input.B("agreementReviewed"),"يلزم تأكيد مراجعة اتفاق الاستقطاع.");
            string reference=input.Required("sourceRef");J.Need(!Store.List(c,"debt",tx).Any(d=>d.S("sourceRef")==reference),"مرجع الدين مسجل من قبل.");
            var d=new JsonObject{["id"]=Guid.NewGuid().ToString("N"),["iqama"]=iqama,["category"]=category,["totalAmount"]=total,["remainingBalance"]=total,["monthlyInstallment"]=installment,["status"]="active",["notes"]=input.S("notes"),["sourceRef"]=reference,["effectiveFrom"]=J.Period(input.S("effectiveFrom",DateTime.UtcNow.ToString("yyyy-MM"))),["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
            Store.Save(c,"debt",d,tx);Store.Audit(c,tx,"debt.created",d.S("id"),ctx.User.Identity!.Name!);return d;
        }));
        api.MapPut("/debts/{id}",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>{
            var d=Store.Get(c,id,"debt",tx)??throw new ApiError("الدين غير موجود.",404);J.Need(d.N("remainingBalance")>0,"الدين مسدد.");
            string status=input.S("status",d.S("status"));J.Need(status is "active" or "paused","تسديد الدين يتم بقيد مالي معتمد.");
            if(input.ContainsKey("monthlyInstallment")) { J.Need(input.B("agreementReviewed"),"راجع اتفاق إعادة الجدولة.");d["monthlyInstallment"]=Amount(input,"monthlyInstallment"); }
            d["status"]=status;d["notes"]=input.S("notes",d.S("notes"));d["updatedAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,"debt",d,tx);Store.Audit(c,tx,"debt.rescheduled",id,ctx.User.Identity!.Name!);return d;
        }));
    }
    public static void Post(DbConnection c,DbTransaction tx,PayrollResult result,string runId)
    {
        foreach(var line in result.Lines.Where(x=>x.Category=="debt_installment"))
        {
            var d=Store.Get(c,line.Source,"debt",tx)??throw new ApiError("تغير الدين؛ أعد المعاينة.");
            decimal amount=-line.Amount;J.Need(amount>0&&amount<=d.N("remainingBalance")&&d.S("status")=="active","تغير رصيد الدين؛ أعد المعاينة.");
            string key="debt:"+line.Source+":"+result.Period;J.Need(Store.Get(c,key,"ledger",tx)==null,"قسط الفترة مرحل بالفعل.");
            d["remainingBalance"]=J.Money(d.N("remainingBalance")-amount);if(d.N("remainingBalance")==0)d["status"]="settled";
            d["lastDeductionPeriod"]=result.Period;Store.Save(c,"debt",d,tx);
            Store.Save(c,"ledger",new(){["id"]=key,["iqama"]=result.Iqama,["period"]=result.Period,["amount"]=-amount,["kind"]="debt_installment",["debtId"]=line.Source,["sourceRef"]=runId,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")},tx);
        }
    }
}
