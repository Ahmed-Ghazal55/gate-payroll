using System.Globalization;
using System.Text.Json.Nodes;
namespace Payroll;

public sealed record PayFacts(string Rider,string App,string Period,string Category,int Orders,decimal PlatformGross,decimal Incentives,decimal Days,string VehicleType="private",decimal? VehicleDays=null,IReadOnlyDictionary<string,decimal?>? Metrics=null);
public sealed record PayExplain(string Label,string Formula,JsonObject Inputs,decimal Amount,string Category,bool AffectsNet=true,string? AccountCode=null);
public sealed record PayRiderShare(string Rider,int Orders,decimal BaseGross);
public sealed record PayAllocatedShare(string Rider,int Orders,decimal BaseGross,decimal Incentive);
public sealed class PayEvaluation
{
    public string Rider {get;init;}="";public string App {get;init;}="";public string Period {get;init;}="";public string Category {get;init;}="";public string SchemeType {get;init;}="";public string AccountingRole {get;init;}="";
    public int Orders {get;init;}public decimal Days {get;init;}public decimal PlatformGross {get;init;}public decimal Incentives {get;init;}public decimal Gross {get;init;}public decimal CompanyCommission {get;init;}public decimal OperatingCost {get;init;}public string OperatingCostAccount {get;init;}="";
    public decimal VehicleCharge {get;init;}public decimal FuelCap {get;init;}public decimal Entitlement {get;init;}public decimal NetBeforePersonal {get;init;}public string Status {get;init;}="needs_decision";public JsonObject? Band {get;init;}
    public bool IneligibleKeetaSalary {get;init;}public decimal RecoveredOperatingCost {get;init;}public decimal UnrecoveredOperatingCost {get;init;}public decimal UnrecoveredVehicleCharge {get;init;}
    public IReadOnlyList<PayExplain> Lines {get;init;}=[];public IReadOnlyList<string> Blockers {get;init;}=[];
}

// Formula shapes only. All rates, bands, day counts, cutoff, rent and precision come from versioned data.
public static class PayEvaluator
{
    public static decimal Round(decimal amount,JsonObject values)
    {J.Need(values.S("roundingMode")=="AwayFromZero","قاعدة التقريب غير معتمدة.");return decimal.Round(amount,(int)PayValues.Number(values,"roundingDigits",0,6),MidpointRounding.AwayFromZero);}
    public static int DaysInPayrollMonth(string period,JsonObject values)
    {J.Period(period);return (int)PayValues.Number(values,period[5..]=="02"?"februaryDays":"standardDays",1,31);}
    public static int AssignmentDays(DateTimeOffset from,DateTimeOffset to,string period,JsonObject values)
    {
        J.Need(to>=from,"نهاية الحيازة تسبق بدايتها.");J.Period(period);var zone=TimeZoneInfo.FindSystemTimeZoneById(values.Required("timeZone"));var cutoff=TimeOnly.ParseExact(values.Required("billingCutoff"),"HH:mm",CultureInfo.InvariantCulture);
        int Boundary(DateTimeOffset time){var local=TimeZoneInfo.ConvertTime(time,zone);return DateOnly.FromDateTime(local.DateTime).DayNumber+(TimeOnly.FromDateTime(local.DateTime)>cutoff?1:0);}
        var start=DateOnly.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);int first=Math.Max(Boundary(from),start.DayNumber),last=Math.Min(Boundary(to),start.AddMonths(1).DayNumber);
        return Math.Min(DaysInPayrollMonth(period,values),Math.Max(0,last-first));
    }
    public static decimal MonthlyVehicleRent(string type,JsonObject values)
    {J.Need(type is "company_car" or "company_bike" or "private","نوع مركبة غير محدد.");return PayValues.Number(PayValues.Object(values["vehicleRent"]),type);}
    /// <summary>BR §24.4 prorated rent, hard-capped at the type's monthly allocated value (§38.4).</summary>
    public static decimal VehicleCharge(string type,decimal days,string period,JsonObject values)
    {
        J.Need(days>=0&&days==decimal.Truncate(days),"أيام الحيازة عدد صحيح غير سالب.");decimal rent=MonthlyVehicleRent(type,values);if(type=="private"||rent==0)return 0;
        int monthDays=DaysInPayrollMonth(period,values);return Math.Min(rent,Round(rent/monthDays*Math.Min(days,monthDays),values));
    }
    /// <summary>BR §38.4: total rent/deduction for one vehicle never exceeds its type monthly value, even across riders.</summary>
    public static decimal CapVehicleRentTotal(decimal charged,string type,JsonObject values)=>Math.Min(Math.Max(0,charged),MonthlyVehicleRent(type,values));
    /// <summary>Inclusive calendar overlap of a residence period with the payroll month, capped at configured month days. effectiveTo empty = open-ended.</summary>
    public static int ResidenceDays(string effectiveFrom,string effectiveTo,string period,JsonObject values)
    {
        J.Period(period);var start=DateOnly.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);var endExclusive=start.AddMonths(1);
        J.Need(DateOnly.TryParse(effectiveFrom,CultureInfo.InvariantCulture,DateTimeStyles.None,out var from),"بداية الإقامة غير صالحة.");
        DateOnly toInclusive=effectiveTo==""?endExclusive.AddDays(-1):DateOnly.TryParse(effectiveTo,CultureInfo.InvariantCulture,DateTimeStyles.None,out var to)?to:throw new ApiError("نهاية الإقامة غير صالحة.");
        J.Need(toInclusive>=from,"نهاية الإقامة تسبق بدايتها.");
        int first=Math.Max(from.DayNumber,start.DayNumber),lastExclusive=Math.Min(toInclusive.DayNumber+1,endExclusive.DayNumber);
        return Math.Min(DaysInPayrollMonth(period,values),Math.Max(0,lastExclusive-first));
    }
    /// <summary>BR §38.4 / §16 housing charge from residence days; monthly amount is a dated global setting.</summary>
    public static decimal HousingCharge(decimal days,string period,JsonObject values)
    {
        J.Need(days>=0&&days==decimal.Truncate(days),"أيام الإقامة عدد صحيح غير سالب.");decimal monthly=PayValues.Number(values,"housingMonthly");if(monthly==0||days==0)return 0;
        int monthDays=DaysInPayrollMonth(period,values);return Math.Min(monthly,Round(monthly/monthDays*Math.Min(days,monthDays),values));
    }
    public static IReadOnlyList<PayAllocatedShare> SplitIncentives(IEnumerable<PayRiderShare> riders,decimal accountBase,decimal incentive,JsonObject values)
    {
        var rows=riders.OrderBy(r=>r.Rider,StringComparer.Ordinal).ToList();J.Need(rows.Count>0&&rows.Count<=25000&&rows.All(r=>r.Rider!=""&&r.Orders>=0)&&rows.Select(r=>r.Rider).Distinct().Count()==rows.Count,"بيانات استحقاق المناديب غير صالحة.");
        J.Need(incentive>=0&&Round(accountBase,values)==accountBase&&Round(incentive,values)==incentive&&rows.Sum(r=>r.BaseGross)==accountBase,"الإجمالي الأساسي يحتاج استحقاقًا صريحًا لكل مندوب ومطابقة الفاتورة.");
        long totalOrders=rows.Sum(r=>(long)r.Orders);J.Need(incentive==0||totalOrders>0,"لا توزع الحوافز بلا طلبات فعلية.");decimal factor=1;for(int i=0;i<(int)values.N("roundingDigits");i++)factor*=10;decimal units=incentive*factor;
        var shares=rows.Select(r=>{decimal exact=totalOrders==0?0:units*r.Orders/totalOrders;return new{Row=r,Whole=decimal.Floor(exact),Remainder=exact-decimal.Floor(exact)};}).ToList();int remainder=(int)(units-shares.Sum(r=>r.Whole));var extra=shares.OrderByDescending(r=>r.Remainder).ThenBy(r=>r.Row.Rider,StringComparer.Ordinal).Take(remainder).Select(r=>r.Row.Rider).ToHashSet();
        return shares.Select(r=>new PayAllocatedShare(r.Row.Rider,r.Row.Orders,r.Row.BaseGross,(r.Whole+(extra.Contains(r.Row.Rider)?1:0))/factor)).ToArray();
    }
    static bool Compare(decimal value,string op,decimal threshold)=>op switch{"gt"=>value>threshold,"gte"=>value>=threshold,"eq"=>value==threshold,"neq"=>value!=threshold,"lt"=>value<threshold,"lte"=>value<=threshold,_=>throw new ApiError("عامل أهلية غير مدعوم.")};
    static decimal? Metric(PayFacts facts,string key,decimal days)=>key switch{"orders"=>facts.Orders,"days"=>days,"gross"=>facts.PlatformGross+facts.Incentives,"incentives"=>facts.Incentives,_=>facts.Metrics?.GetValueOrDefault(key)};
    static void Approved(JsonObject doc,string app,string period)
    {J.Need(doc.S("status")=="approved"&&doc.S("app")==app&&doc.S("period")==period,"الإعداد يحتاج نسخة معتمدة تخص التطبيق والشهر.");}
    public static PayEvaluation Evaluate(PayFacts facts,JsonObject scheme,JsonObject? eligibility,JsonObject global)
    {
        string app=PayPlatforms.App(facts.App);J.Period(facts.Period);Approved(scheme,app,facts.Period);J.Need(global.S("status")=="approved"&&string.CompareOrdinal(global.S("effectiveFrom"),facts.Period+"-01")<=0,"إعداد الأيام والإيجار غير معتمد للشهر.");
        var values=PayValues.Object(global["data"]?["values"]);int monthDays=DaysInPayrollMonth(facts.Period,values);J.Need(facts.Orders>=0&&facts.Days>=0&&facts.Days==decimal.Truncate(facts.Days),"الطلبات والأيام غير صالحة.");decimal days=Math.Min(facts.Days,monthDays);
        var data=PayValues.Object(scheme["data"]);PaySettings.Validate("pay_scheme",app,data);J.Need(PaySettings.Categories.Contains(facts.Category),"فئة مندوب غير معروفة.");string type=data.S("type"),role=data.S("accountingRole");var p=PayValues.Object(data["parameters"]?["categories"]?[facts.Category]);var lines=new List<PayExplain>();var blockers=new List<string>();decimal total=Round(facts.PlatformGross+facts.Incentives,values),earned=0,fee=0,cost=0,rent=0,fuelCap=0;JsonObject? chosen=null;
        void Line(string label,string formula,JsonObject inputs,decimal amount,string category,bool affects=true,string? account=null)=>lines.Add(new(label,formula,inputs,Round(amount,values),category,affects,account));
        Line("استحقاق المنصة قبل النظام الداخلي","platformGross + incentives",new(){["platformGross"]=facts.PlatformGross,["incentives"]=facts.Incentives},total,"source_gross",false);
        if(!p.B("enabled"))blockers.Add("category_not_enabled");
        bool zero=false,fallback=false;int nextBands=0;
        if(data.S("eligibilitySetId")!="")
        {
            if(eligibility==null)blockers.Add("eligibility_missing");
            else
            {
                Approved(eligibility,app,facts.Period);J.Need(eligibility.S("id")==data.S("eligibilitySetId"),"نسخة الأهلية لا تطابق النظام.");PaySettings.Validate("pay_eligibility",app,eligibility["data"]!.AsObject());
                foreach(var rule in eligibility["data"]!["rules"]!.AsArray().OfType<JsonObject>().Where(r=>!r.ContainsKey("included")||r.B("included")))
                {
                    var observed=Metric(facts,rule.S("metric"),days);if(observed==null){blockers.Add("metric_missing:"+rule.S("metric"));continue;}if(Compare(observed.Value,rule.S("operator"),rule.N("threshold")))continue;
                    string effect=rule["effect"].S("type");zero|=effect=="zero";fallback|=effect=="per_order";if(effect=="next_band")nextBands++;
                    Line("شرط أهلية غير متحقق",rule.S("metric")+" "+rule.S("operator")+" threshold",new(){["metric"]=rule.S("metric"),["observed"]=observed.Value,["threshold"]=rule.N("threshold"),["effect"]=effect},0,"eligibility",false);
                }
            }
        }
        else J.Need(eligibility==null,"لم يرتبط هذا النظام بنسخة أهلية.");
        bool active=p.B("enabled");
        if(active)
        {
            if(!zero&&nextBands>0&&type!="target_salary")blockers.Add("next_band_requires_salary_bands");
            if(type=="target_salary"&&!fallback&&!zero)
            {
                var bands=p["bands"]!.AsArray().OfType<JsonObject>().OrderByDescending(b=>b.N("min")).ToList();int index=bands.FindIndex(b=>facts.Orders>=b.N("min")&&(!b.ContainsKey("max")||facts.Orders<=b.N("max")));if(index<0)fallback=true;
                else if(index+nextBands>=bands.Count)blockers.Add("next_band_missing");
                else
                {
                    chosen=bands[index+nextBands].Clone();decimal basis=chosen.ContainsKey("base")?chosen.N("base"):facts.Orders*chosen.N("perOrderRate"),extra=Math.Max(0,facts.Orders-chosen.N("min"))*chosen.N("extraRate"),internet=chosen.N("internet",p.N("internet"));
                    if(chosen.S("multiplierMetric")!=""){var multiplier=Metric(facts,chosen.S("multiplierMetric"),days);if(multiplier==null)blockers.Add("metric_missing:"+chosen.S("multiplierMetric"));else basis*=multiplier.Value/chosen.N("multiplierDivisor");}
                    basis=Round(basis,values);extra=Round(extra,values);internet=Round(internet,values);earned=basis+extra+internet;fuelCap=chosen.N("fuelCap",p.N("fuelCap"));
                    Line("أساس الشريحة","base OR orders × perOrderRate; optional metric/divisor",chosen.Clone(),basis,"band_base");Line("الطلبات فوق بداية الشريحة","max(orders - min,0) × extraRate",new(){["orders"]=facts.Orders,["min"]=chosen.N("min"),["extraRate"]=chosen.N("extraRate")},extra,"extra_orders");Line("اتصالات معتمدة","configured internet",new(){["internet"]=internet},internet,"internet");
                    Line("سقف الوقود وليس بدلًا نقديًا","approved band fuelCap",new(){["fuelCap"]=fuelCap},fuelCap,"fuel_cap",false);
                }
            }
            else if(!fallback&&!zero)
            {
                fee=type switch{"daily_commission"=>p.N("dailyCommission")*days,"monthly_commission"=>p.N("monthlyCommission")*(p.B("prorate")?days/monthDays:1),"percent_of_gross" or "percent_monthly_with_vehicle"=>total*p.N("companyPercent")/100,_=>throw new ApiError("نوع نظام غير مدعوم.")};fee=Round(fee,values);earned=total-fee;
                Line("الإجمالي قبل العمولة","platformGross + incentives",new(){["gross"]=facts.PlatformGross,["incentives"]=facts.Incentives},total,"earnings");Line("عمولة الشركة",type switch{"daily_commission"=>"dailyCommission × days","monthly_commission"=>"monthlyCommission × (prorate ? days/monthDays : 1)",_=>"gross × companyPercent /100 before deductions"},new(){["parameters"]=p.Clone(),["days"]=days,["monthDays"]=monthDays,["gross"]=total},-fee,"company_commission");
            }
            if(fallback&&!zero)
            {
                if(!p.ContainsKey("fallbackRate"))blockers.Add("fallback_rate_missing");else{earned=Round(facts.Orders*p.N("fallbackRate"),values);Line("البديل بالطلب","orders × fallbackRate",new(){["orders"]=facts.Orders,["fallbackRate"]=p.N("fallbackRate")},earned,"fallback");cost=Round(p.N("operatingCost"),values);}
            }
            if(zero){Line("أثر الأهلية: استحقاق صفر","earnings ×0",new(){["before"]=earned},-earned,"eligibility_zero");earned=0;cost=0;}
            if(p.B("chargeCompanyVehicle"))
            {
                string expected=facts.Category.EndsWith("_company_car",StringComparison.Ordinal)?"company_car":facts.Category.EndsWith("_company_bike",StringComparison.Ordinal)?"company_bike":"private";J.Need(expected==facts.VehicleType,"المركبة لا تطابق فئة المندوب.");
                if(expected!="private"&&facts.VehicleDays==null)blockers.Add("vehicle_period_missing");else rent=VehicleCharge(expected,facts.VehicleDays??0,facts.Period,values);
                Line("مقابل حيازة المركبة","monthlyRent / monthDays × min(vehicleDays,monthDays)",new(){["type"]=expected,["vehicleDays"]=facts.VehicleDays,["monthDays"]=monthDays,["monthlyRent"]=values["vehicleRent"]?[expected]?.DeepClone()},-rent,"vehicle_charge");
            }
        }
        // BR22.9.2 applies only to the ineligible Keeta salary fallback, never to commission or Ninja.
        bool protectedSalary=app=="keeta"&&type=="target_salary"&&fallback&&!zero;
        decimal recovered=protectedSalary?Math.Min(Math.Max(0,earned),cost):cost,unrecovered=cost-recovered;
        string costAccount=p.S("operatingCostAccount");if(cost!=0){if(costAccount=="")blockers.Add("operating_cost_account_missing");else PayPlatforms.Active(costAccount);Line("تكلفة التشغيل المستردة","min(earnings, operatingCost) for BR22.9.2",new(){["operatingCost"]=cost,["companyCost"]=unrecovered},-recovered,"operating_cost",account:costAccount);}
        if(unrecovered>0)Line("تكلفة عدم الصلاحية على الشركة","operatingCost - recoveredOperatingCost",new(){["operatingCost"]=cost,["recovered"]=recovered},unrecovered,"unrecovered_operating_cost",false,"512121");
        decimal unrecoveredRent=protectedSalary?Math.Max(0,rent-Math.Max(0,earned-recovered)):0;
        if(unrecoveredRent>0){rent-=unrecoveredRent;int i=lines.FindIndex(l=>l.Category=="vehicle_charge");if(i>=0)lines[i]=lines[i] with{Amount=-rent};Line("حيازة غير مستردة على الشركة","vehicle charge - recovered charge",new(),unrecoveredRent,"unrecovered_vehicle_cost",false,"512121");}
        decimal entitlement=Round(earned-recovered,values),net=Round(entitlement-rent,values),commission=role=="agent"?Round(total-earned,values):fee;
        if(net<0)blockers.Add("negative_net_requires_decision");if(role=="agent"&&commission<0&&total>=0)blockers.Add("negative_company_share_requires_decision");
        return new(){Rider=facts.Rider,App=app,Period=facts.Period,Category=facts.Category,SchemeType=type,AccountingRole=role,Orders=facts.Orders,Days=days,PlatformGross=facts.PlatformGross,Incentives=facts.Incentives,Gross=Round(earned,values),CompanyCommission=commission,OperatingCost=cost,OperatingCostAccount=costAccount,RecoveredOperatingCost=recovered,UnrecoveredOperatingCost=unrecovered,UnrecoveredVehicleCharge=unrecoveredRent,IneligibleKeetaSalary=protectedSalary,VehicleCharge=rent,FuelCap=fuelCap,Entitlement=entitlement,NetBeforePersonal=net,Status=blockers.Count==0?"ready":"needs_decision",Band=chosen,Lines=lines,Blockers=blockers.Distinct().ToArray()};
    }
}
