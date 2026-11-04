using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Payroll;

public static class PayValues
{
    public static decimal Number(JsonObject row,string key,decimal min=0,decimal max=1000000000)
    {J.Need(row[key] is JsonValue&&decimal.TryParse(row.S(key),NumberStyles.Number,CultureInfo.InvariantCulture,out _),"قيمة رقمية مطلوبة: "+key);decimal value=decimal.Parse(row.S(key),NumberStyles.Number,CultureInfo.InvariantCulture);J.Need(value>=min&&value<=max&&decimal.Round(value,4)==value,"قيمة رقمية خارج النطاق أو الدقة: "+key);return value;}
    public static bool Flag(JsonObject row,string key)
    {if(row[key] is not JsonValue flag||!flag.TryGetValue<bool>(out bool value))throw new ApiError("قيمة منطقية مطلوبة: "+key);return value;}
    public static string Name(string value)
    {J.Need(Regex.IsMatch(value,"^[A-Za-z][A-Za-z0-9_.-]{0,63}$"),"رمز الحقل غير صالح.");return value;}
    public static string Date(string value)
    {J.Need(DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),"التاريخ يجب أن يكون yyyy-MM-dd.");return value;}
    public static JsonObject Object(JsonNode? value)=>value as JsonObject??throw new ApiError("كائن إعدادات مطلوب.");
    public static JsonArray Array(JsonNode? value,int max=100)=>value is JsonArray a&&a.Count<=max&&a.All(n=>n is JsonObject)?a:throw new ApiError("قائمة إعدادات غير صالحة.");
    public static void Fields(JsonObject row,params string[] allowed)=>J.Need(row.All(p=>allowed.Contains(p.Key)),"حقل غير مدعوم في الإعدادات.");
}

public static partial class PaySettings
{
    public static readonly string[] SchemeTypes=["daily_commission","percent_of_gross","target_salary","monthly_commission","percent_monthly_with_vehicle"];
    public static readonly string[] Categories=["sponsored_company_car","sponsored_company_bike","sponsored_private","external_company_car","external_company_bike","external_private"];
    public static readonly string[] Operators=["gt","gte","eq","neq","lt","lte"];
    public static readonly string[] Kinds=["pay_terms","pay_scheme","pay_eligibility","pay_setting"];
    public static JsonObject Defaults()=>JsonNode.Parse(typeof(PaySettings).Assembly.GetManifestResourceStream("Payroll.PayDefaults.v1.json")!)!.AsObject();
    static void Bands(JsonNode? node)
    {
        var bands=PayValues.Array(node);J.Need(bands.Count>0,"شرائح الإعداد مطلوبة.");var starts=new HashSet<decimal>();
        foreach(var band in bands.OfType<JsonObject>())
        {
            PayValues.Fields(band,"min","max","base","perOrderRate","extraRate","fuelCap","internet","multiplierMetric","multiplierDivisor");decimal min=PayValues.Number(band,"min");J.Need(starts.Add(min),"بداية شريحة مكررة.");if(band.ContainsKey("max"))J.Need(PayValues.Number(band,"max")>=min,"نهاية الشريحة تسبق بدايتها.");
            foreach(string key in new[]{"base","perOrderRate","extraRate","fuelCap","internet"})if(band.ContainsKey(key))PayValues.Number(band,key);
            J.Need(band.ContainsKey("base")||band.ContainsKey("perOrderRate"),"حدد أساس مبلغ الشريحة.");
            if(band.ContainsKey("multiplierMetric")){PayValues.Name(band.Required("multiplierMetric"));PayValues.Number(band,"multiplierDivisor",0.0001m);}
        }
        var ordered=bands.OfType<JsonObject>().OrderBy(b=>b.N("min")).ToList();for(int i=0;i<ordered.Count-1;i++)if(ordered[i].ContainsKey("max"))J.Need(ordered[i].N("max")<ordered[i+1].N("min"),"الشرائح متداخلة.");
    }
    static void ValidateTerms(JsonObject data)
    {
        PayValues.Fields(data,"name","billingModel","parameters");string model=data.Required("billingModel");var p=PayValues.Object(data["parameters"]);
        switch(model)
        {
            case "per_order":
                PayValues.Fields(p,"pricePerOrder","perKmRate","includedKm","maxBillableKm","classRates","companyCostAccount");PayValues.Number(p,"pricePerOrder");
                if(p.ContainsKey("perKmRate")){PayValues.Number(p,"perKmRate");PayValues.Number(p,"includedKm");J.Need(PayValues.Number(p,"maxBillableKm")>=p.N("includedKm"),"حد المسافة غير صالح.");}
                if(p.ContainsKey("classRates")){var classes=PayValues.Object(p["classRates"]);foreach(var field in classes){PayValues.Name(field.Key);PayValues.Number(classes,field.Key);}}
                break;
            case "slabs":PayValues.Fields(p,"metric","bands","companyCostAccount");PayValues.Name(p.Required("metric"));Bands(p["bands"]);break;
            case "flat_target":PayValues.Fields(p,"targetOrders","monthlyAmount","extraRate","shortfallPerOrder","prorate","companyCostAccount");PayValues.Number(p,"targetOrders",1);PayValues.Number(p,"monthlyAmount");PayValues.Number(p,"extraRate");PayValues.Number(p,"shortfallPerOrder");PayValues.Flag(p,"prorate");break;
            default:throw new ApiError("نموذج محاسبة الشركة الأم غير مدعوم.");
        }
        if(p.ContainsKey("companyCostAccount")){string account=PayPlatforms.Active(p.S("companyCostAccount"));J.Need(account.StartsWith('5'),"خصم الشركة يحتاج حساب تكلفة.");}
    }
    static void ValidateScheme(JsonObject data)
    {
        PayValues.Fields(data,"name","type","parameters","eligibilitySetId","accountingRole");string type=data.Required("type");J.Need(SchemeTypes.Contains(type),"نظام داخلي غير مدعوم.");J.Need(data.S("accountingRole") is "principal" or "agent","حدد صفة الأصيل أو الوكيل للنظام.");
        var parameters=PayValues.Object(data["parameters"]);PayValues.Fields(parameters,"categories");var cells=PayValues.Object(parameters["categories"]);J.Need(cells.Count==Categories.Length&&Categories.All(cells.ContainsKey),"حدد خلايا الكفالة والمركبة الست صراحة؛ لا يوجد بديل تلقائي.");
        foreach(var pair in cells)
        {
            var cell=PayValues.Object(pair.Value);J.Need(cell.S("enabled")==""||cell["enabled"] is JsonValue,"حالة الخلية غير صالحة.");bool enabled=PayValues.Flag(cell,"enabled");
            if(!enabled){PayValues.Fields(cell,"enabled");continue;}
            PayValues.Fields(cell,"enabled","dailyCommission","monthlyCommission","companyPercent","prorate","chargeCompanyVehicle","bands","fallbackRate","operatingCost","fuelCap","internet");PayValues.Flag(cell,"chargeCompanyVehicle");
            if(type=="daily_commission")PayValues.Number(cell,"dailyCommission");
            if(type is "percent_of_gross" or "percent_monthly_with_vehicle")PayValues.Number(cell,"companyPercent",0,100);
            if(type=="monthly_commission"){PayValues.Number(cell,"monthlyCommission");PayValues.Flag(cell,"prorate");}
            if(type=="target_salary")Bands(cell["bands"]);
            if(type=="percent_monthly_with_vehicle"&&!pair.Key.EndsWith("_private",StringComparison.Ordinal))J.Need(cell.B("chargeCompanyVehicle"),"هذا النظام يشمل مركبة الشركة صراحة.");
            foreach(string key in new[]{"dailyCommission","monthlyCommission","companyPercent","fallbackRate","operatingCost","fuelCap","internet"})if(cell.ContainsKey(key))PayValues.Number(cell,key,0,key=="companyPercent"?100:1000000000);
            if(cell.ContainsKey("prorate"))PayValues.Flag(cell,"prorate");if(type!="target_salary"&&cell.ContainsKey("bands"))Bands(cell["bands"]);
        }
    }
    static void ValidateEligibility(JsonObject data,string app)
    {
        PayValues.Fields(data,"name","rules");var rules=PayValues.Array(data["rules"]);
        foreach(var rule in rules.OfType<JsonObject>())
        {
            PayValues.Fields(rule,"metric","operator","threshold","effect","app");if(rule.ContainsKey("app"))J.Need(PayPlatforms.App(rule.S("app"))==app,"الأهلية تخص تطبيقًا واحدًا؛ المشاركة بين التطبيقات ممنوعة.");PayValues.Name(rule.Required("metric"));J.Need(Operators.Contains(rule.S("operator")),"عامل مقارنة غير مدعوم.");PayValues.Number(rule,"threshold",-1000000000);
            var effect=PayValues.Object(rule["effect"]);PayValues.Fields(effect,"type");J.Need(effect.S("type") is "next_band" or "per_order" or "zero","أثر شرط الأهلية غير مدعوم.");
        }
    }
    static void ValidateGlobal(JsonObject data)
    {
        PayValues.Fields(data,"values");var v=PayValues.Object(data["values"]);PayValues.Fields(v,"standardDays","februaryDays","billingCutoff","timeZone","roundingDigits","roundingMode","vehicleRent","accounts");
        foreach(string key in new[]{"standardDays","februaryDays"}){decimal n=PayValues.Number(v,key,1,31);J.Need(n==decimal.Truncate(n),"عدد الأيام صحيح موجب.");}
        J.Need(TimeOnly.TryParseExact(v.S("billingCutoff"),"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),"موعد قطع اليوم غير صالح.");J.Need(v.S("timeZone")=="Asia/Riyadh","قاعدة اليوم بتوقيت الرياض.");J.Need(PayValues.Number(v,"roundingDigits",0,6)==decimal.Truncate(v.N("roundingDigits"))&&v.S("roundingMode")=="AwayFromZero","قاعدة التقريب غير مدعومة.");
        var rent=PayValues.Object(v["vehicleRent"]);PayValues.Fields(rent,"company_car","company_bike","private");foreach(string vehicle in new[]{"company_car","company_bike","private"})PayValues.Number(rent,vehicle);J.Need(rent.N("private")==0,"المركبة الخاصة بلا إيجار للشركة.");
        var accounts=PayValues.Object(v["accounts"]);PayValues.Fields(accounts,"salaryExpense","vehicleIncomeCar","vehicleIncomeBike","outputVat");foreach(string key in new[]{"salaryExpense","vehicleIncomeCar","vehicleIncomeBike","outputVat"}){string code=PayPlatforms.Active(accounts.Required(key));J.Need(code[0]==(key=="salaryExpense"?'5':key=="outputVat"?'2':'4'),"نوع الحساب لا يناسب القيد.");}
    }
    public static void Validate(string kind,string app,JsonObject data)
    {
        if(kind!="pay_setting"){J.Need(data["name"] is JsonValue name&&name.TryGetValue<string>(out _)&&data.S("name").Length is >0 and <=200,"اسم الإعداد مطلوب.");}
        switch(kind){case "pay_terms":ValidateTerms(data);break;case "pay_scheme":ValidateScheme(data);break;case "pay_eligibility":ValidateEligibility(data,app);break;case "pay_setting":ValidateGlobal(data);break;default:throw new ApiError("نوع إعداد غير مدعوم.");}
    }
}
