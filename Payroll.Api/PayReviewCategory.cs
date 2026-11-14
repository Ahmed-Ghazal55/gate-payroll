using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
namespace Payroll;

// A month's pay category comes from the rider's sponsorship and the vehicles held that month.
// Nothing is written on the ERP-managed rider. An unclear case returns "" and stays a blocker.
public static class PayReviewCategory
{
    public static readonly string[] AllocationWeights=["orders","days","periods","compliance"];
    public readonly record struct OperatorMetrics(string Rider,decimal Orders,decimal Days=0,decimal Periods=0,decimal Compliance=0);

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
    public static string Derive(DbConnection c,DbTransaction? tx,JsonObject rider,string period,Func<string,IEnumerable<JsonObject>>? liveOps=null)
    {
        string saved=rider.S("payCategory");if(PaySettings.Categories.Contains(saved))return saved;
        string prefix=rider.S("engagementType") switch{"SponsoredInternal"=>"sponsored","External"=>"external",_=>""};if(prefix=="")return "";
        var openCycle=Store.List(c,PayCycles.Kind,tx).FirstOrDefault(p=>p.S("period")==period&&p.S("status") is not ("cancelled" or "reversed")&&PayRunInputs.Captured(p)&&(p.S("companyId")==rider.S("sponsorId")||p.S("companyId")==rider.S("companyId")));
        List<JsonObject> Ops(string kind)=>openCycle!=null?PayRunInputs.List(c,tx,openCycle.S("id"),kind):(liveOps!=null?liveOps(kind).ToList():Store.List(c,kind,tx));
        var assignments=Ops("vehicle_assignment");
        var vehicles=Ops("vehicle");
        var held=assignments.Where(a=>a.S("riderId")==rider.S("id")&&Overlaps(a.S("startedAt"),a.S("endedAt"),period)).Select(a=>openCycle!=null?PayRunInputs.Get(c,tx,openCycle.S("id"),"vehicle",a.S("vehicleId")):vehicles.FirstOrDefault(v=>v.S("id")==a.S("vehicleId"))??Store.Get(c,a.S("vehicleId"),"vehicle",tx)).ToList();
        if(held.Count==0)return prefix+"_private";
        var kinds=held.Select(v=>v==null?"":Vehicle(v)).Distinct().ToList();
        return kinds.Count==1&&kinds[0]!=""?prefix+"_"+kinds[0]:"";
    }

    public static (string Weight,string TieBreak) AllocationSetting(DbConnection c,DbTransaction? tx,string app,string period)
    {
        try
        {
            var terms=PaySettings.Resolve(c,tx,"pay_terms",app,period);
            var p=terms["data"]?["parameters"] as JsonObject??new JsonObject();
            string weight=p.S("riderAllocationWeight","orders"),tie=p.S("riderAllocationTieBreak","days");
            J.Need(AllocationWeights.Contains(weight),"وزن توزيع المناديب غير مدعوم.");
            J.Need(AllocationWeights.Contains(tie),"كسر تعادل التوزيع غير مدعوم.");
            return (weight,tie);
        }
        catch(ApiError){return ("orders","days");}
    }

    static decimal Metric(OperatorMetrics o,string weight)=>weight switch
    {
        "orders"=>o.Orders,
        "days"=>o.Days,
        "periods"=>o.Periods,
        "compliance"=>o.Compliance,
        _=>throw new ApiError("وزن توزيع المناديب غير مدعوم.")
    };

    /// <summary>§40.2: largest-remainder shares by weight (default orders); tie-break metric orders remainder recipients.</summary>
    public static List<decimal> Shares(IReadOnlyList<OperatorMetrics> operators,string weight="orders",string tieBreak="days")
    {
        J.Need(operators.Count>0,"مشغّل واحد على الأقل للتوزيع.");
        J.Need(AllocationWeights.Contains(weight)&&AllocationWeights.Contains(tieBreak),"وزن توزيع المناديب غير مدعوم.");
        decimal total=operators.Sum(o=>Metric(o,weight));
        if(total==0)return operators.Select(_=>1m/operators.Count).ToList();
        return operators.Select(o=>Metric(o,weight)/total).ToList();
    }

    public static long[] SplitUnits(IReadOnlyList<OperatorMetrics> operators,long units,string weight="orders",string tieBreak="days")
    {
        var shares=Shares(operators,weight,tieBreak);
        var whole=shares.Select(s=>(long)decimal.Floor(s*units)).ToList();
        long left=units-whole.Sum();
        var order=shares.Select((s,i)=>(Frac:s*units-whole[i],Tie:Metric(operators[i],tieBreak),I:i))
            .OrderByDescending(x=>x.Frac).ThenByDescending(x=>x.Tie).ThenBy(x=>x.I).Take((int)Math.Max(0,left)).Select(x=>x.I);
        foreach(int i in order)whole[i]++;
        return whole.ToArray();
    }

    // Largest-remainder split: whole orders, and money to the halala, summing exactly to the invoice.
    public static JsonArray Propose(IReadOnlyList<(string Rider,decimal Orders)> operators,decimal orders,decimal baseGross,decimal deduction)
        =>Propose(operators.Select(o=>new OperatorMetrics(o.Rider,o.Orders)).ToList(),orders,baseGross,deduction);

    public static JsonArray Propose(IReadOnlyList<OperatorMetrics> operators,decimal orders,decimal baseGross,decimal deduction,string weight="orders",string tieBreak="days")
    {
        var shares=Shares(operators,weight,tieBreak);
        long orderUnits=(long)decimal.Truncate(orders);
        var wholeOrders=SplitUnits(operators,orderUnits,weight,tieBreak);
        // Money: last-rider remainder so sum == totals (existing contract); weight drives earlier shares.
        var result=new JsonArray();decimal gross=0,cut=0;
        for(int i=0;i<operators.Count;i++)
        {
            bool last=i==operators.Count-1;decimal g=last?baseGross-gross:J.Money(baseGross*shares[i]),d=last?deduction-cut:J.Money(deduction*shares[i]);gross+=g;cut+=d;
            result.Add(new JsonObject{["rider"]=operators[i].Rider,["erpOrders"]=operators[i].Orders,["orders"]=wholeOrders[i],["baseGross"]=g,["deduction"]=d,["weight"]=weight,["weightValue"]=Metric(operators[i],weight)});
        }
        return result;
    }
}
