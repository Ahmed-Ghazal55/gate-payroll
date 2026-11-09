using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
namespace Payroll;

// A month's pay category comes from the rider's sponsorship and the vehicles held that month.
// Nothing is written on the ERP-managed rider. An unclear case returns "" and stays a blocker.
public static class PayReviewCategory
{
    public static bool Overlaps(string start,string end,string period)
    {
        var month=DateTimeOffset.Parse(period+"-01T00:00:00+03:00",CultureInfo.InvariantCulture);
        if(!DateTimeOffset.TryParse(start,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var from)||from>=month.AddMonths(1))return false;
        return end==""||DateTimeOffset.TryParse(end,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var to)&&to>month;
    }
    static string Vehicle(JsonObject vehicle)
    {
        string type=vehicle.S("vehicleType"),owner=vehicle.S("ownershipType");
        if(owner is not ("1" or "2"))return ""; // ERP: 1 Owned, 2 Leased. Third party needs a person to decide.
        return type switch{"1"=>"company_bike","2"=>"company_car",_=>""}; // ERP: 1 Motorcycle, 2 Car.
    }
    public static string Derive(DbConnection c,DbTransaction? tx,JsonObject rider,string period)
    {
        string saved=rider.S("payCategory");if(PaySettings.Categories.Contains(saved))return saved;
        string prefix=rider.S("engagementType") switch{"SponsoredInternal"=>"sponsored","External"=>"external",_=>""};if(prefix=="")return "";
        var held=Store.List(c,"vehicle_assignment",tx).Where(a=>a.S("riderId")==rider.S("id")&&Overlaps(a.S("startedAt"),a.S("endedAt"),period)).Select(a=>Store.Get(c,a.S("vehicleId"),"vehicle",tx)).ToList();
        if(held.Count==0)return prefix+"_private";
        var kinds=held.Select(v=>v==null?"":Vehicle(v)).Distinct().ToList();
        return kinds.Count==1&&kinds[0]!=""?prefix+"_"+kinds[0]:"";
    }
    // Largest-remainder split: whole orders, and money to the halala, summing exactly to the invoice.
    public static JsonArray Propose(IReadOnlyList<(string Rider,decimal Orders)> operators,decimal orders,decimal baseGross,decimal deduction)
    {
        decimal total=operators.Sum(o=>o.Orders);var shares=operators.Select(o=>total==0?1m/operators.Count:o.Orders/total).ToList();
        var whole=shares.Select(s=>decimal.Floor(s*orders)).ToList();int left=(int)(orders-whole.Sum());
        foreach(int i in shares.Select((s,i)=>(Frac:s*orders-whole[i],I:i)).OrderByDescending(x=>x.Frac).ThenBy(x=>x.I).Take(Math.Max(0,left)).Select(x=>x.I))whole[i]++;
        var result=new JsonArray();decimal gross=0,cut=0;
        for(int i=0;i<operators.Count;i++)
        {
            bool last=i==operators.Count-1;decimal g=last?baseGross-gross:J.Money(baseGross*shares[i]),d=last?deduction-cut:J.Money(deduction*shares[i]);gross+=g;cut+=d;
            result.Add(new JsonObject{["rider"]=operators[i].Rider,["erpOrders"]=operators[i].Orders,["orders"]=whole[i],["baseGross"]=g,["deduction"]=d});
        }
        return result;
    }
}
