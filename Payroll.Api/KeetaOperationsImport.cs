using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Payroll;

/// <summary>
/// Keeta operations register sheet (A..AH Arabic headers). One sheet = one register.
/// Expected eligibility is stored as expected only (never final; invoice is final).
/// </summary>
public static class KeetaOperationsImport
{
    public static readonly string[] Headers =
    [
        "المعرف","الاسم بالكامل","رقم بطاقة الهوية","كفالة اليوزر","المركبة","الحالة",
        "رقم الهوية1","البديل 1","رقم التواصل","نوع البديل","نوع المركبة","رقم كرت البنزين1",
        "الأدوات1","الجنسية 1","الرقم المسجل","الايبان 1","تاريخ الاستلام1","تاريخ التسليم",
        "الأيام الصالحة","الطلبات","المخالفات","المسافة المدفوعة","عدد ايام عمل المندوب",
        "الصلاحية المتوقعة","اجمالي المسافه","مطابقة المسافة","الملاحظات",
        "اجمالي طلبات المعادلة","اجمالي طلبات اليوزر","مطابقة الطلبات","عدد البدلاء",
        "اجمالي ايام المعادلة","اجمالي ايام اليوزر","مطابقة الايام"
    ];
    public static readonly string[] Required = ["المعرف","رقم بطاقة الهوية","الحالة","الأيام الصالحة","الطلبات","الصلاحية المتوقعة"];
    public static readonly ExcelColumn[] Columns = Headers.Select(h => new ExcelColumn(h,
        Numeric: h is "الأيام الصالحة" or "الطلبات" or "المخالفات" or "المسافة المدفوعة" or "عدد ايام عمل المندوب"
            or "اجمالي المسافه" or "اجمالي طلبات المعادلة" or "اجمالي طلبات اليوزر" or "عدد البدلاء"
            or "اجمالي ايام المعادلة" or "اجمالي ايام اليوزر",
        Identity: h is "المعرف" or "رقم بطاقة الهوية" or "رقم الهوية1" or "الايبان 1" or "رقم التواصل")).ToArray();

    public static string ResolveRegister(string raw)
    {
        string key=raw.Trim();
        var sponsor=FinancialCore.FindSponsor(key);
        if(sponsor!=null)return sponsor.Code;
        if(string.Equals(key,"Express",StringComparison.OrdinalIgnoreCase)||string.Equals(key,"EX",StringComparison.OrdinalIgnoreCase))return "EX";
        if(string.Equals(key,"Albwaba",StringComparison.OrdinalIgnoreCase)||string.Equals(key,"BW",StringComparison.OrdinalIgnoreCase))return "BW";
        if(string.Equals(key,"Togary",StringComparison.OrdinalIgnoreCase)||string.Equals(key,"TR",StringComparison.OrdinalIgnoreCase))return "TR";
        throw new ApiError("اختر السجل قبل الرفع: إكسبرس أو البوابا أو التجارية.");
    }

    public static string SheetNameFor(string registerCode)=>registerCode switch
    {
        "EX"=>"Express","BW"=>"Albwaba","TR"=>"Togary",
        _=>throw new ApiError("سجل تشغيل كيتا غير معروف.")
    };

    public static JsonObject Parse(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject input,byte[] bytes,out JsonObject envelope)
    {
        var invoice=OperationsImports.Invoice(c,tx,actor,"keeta",input.Required("invoiceId"));
        string period=invoice.S("period"),company=invoice.S("companyId");
        string register=ResolveRegister(input.Required("register"));
        var sponsor=FinancialCore.Sponsors.Single(s=>s.Code==register);
        J.Need(company==sponsor.Id||FinancialCore.FindSponsor(company)?.Code==register,"السجل المختار لا يطابق فاتورة كيتا المعتمدة.");
        J.Need(bytes.Length is >0 and <=8*1024*1024,"ملف إكسل حتى 8 ميجابايت.");

        var sheets=Imports.Read(bytes,"keeta-operations.xlsx").Where(s=>s.Rows.Any(r=>r.Any(v=>!string.IsNullOrWhiteSpace(v)))).ToList();
        J.Need(sheets.Count>=1,"لا توجد ورقة بيانات في الملف.");
        string want=SheetNameFor(register);
        Imports.Sheet sheet;
        if(sheets.Count==1)sheet=sheets[0];
        else
        {
            sheet=sheets.FirstOrDefault(s=>string.Equals(s.Name,want,StringComparison.OrdinalIgnoreCase))
                ??throw new ApiError($"الملف متعدد الأوراق؛ يلزم ورقة السجل «{want}» أو ملف بورقة واحدة فقط.");
        }

        J.Need(sheet.Rows.Count>=2,"الورقة بلا صفوف تشغيل.");
        var header=sheet.Rows[0].Select(v=>v.Trim()).ToList();
        // Allow trailing empty / comparison columns after AH; require A..AH exact order for the first 34.
        J.Need(header.Count>=Headers.Length,"أعمدة تشغيل كيتا ناقصة؛ حمّل النموذج العربي.");
        for(int i=0;i<Headers.Length;i++)
            J.Need(i<header.Count&&header[i]==Headers[i],$"عمود {(i+1)} يجب أن يكون «{Headers[i]}» بنفس ترتيب ورقة السجل.");
        var missingCache=KeetaInvoices.MissingCache(bytes,sheet.Name,Enumerable.Range(0,Headers.Length).ToHashSet());

        var lines=OperationsImports.SourceLines(c,tx,"keeta",invoice.S("id"));
        J.Need(lines.Count>0,"الفاتورة بلا سطور.");
        var accounts=new JsonArray();
        foreach(var line in lines)
        {
            if(line.S("platformAccountId",line.S("accountId"))=="")
            {
                J.Need(line.ContainsKey("baseGross")&&line.ContainsKey("incentives")&&line.ContainsKey("orders")&&line.N("baseGross")==0&&line.N("incentives")==0&&line.N("orders")==0,"سطر شركة باستحقاق غير موزع يحتاج مراجعة.");
                continue;
            }
            accounts.Add(OperationsImports.Account(line));
        }
        J.Need(accounts.Count>0&&accounts.OfType<JsonObject>().Select(a=>a.S("accountId")).Distinct().Count()==accounts.Count,"يلزم سطر فاتورة واحد لكل يوزر.");

        var localAccounts=Store.List(c,"platform_account",tx)
            .Where(a=>a.S("companyId")==company&&PayPlatforms.For(a.S("platformCode")).App=="keeta")
            .ToList();
        string ResolveAccountId(string key)
        {
            var matches=localAccounts.Where(a=>a.S("id")==key||a.S("externalAccountId")==key||a.S("code")==key).ToArray();
            J.Need(matches.Length==1,"يوزر المنصة غير موجود أو مكرر للمعرف في السجل.");
            return matches[0].S("id");
        }

        var displayRows=new JsonArray();
        var pending=new List<(JsonObject Review,JsonObject Share,int Row)>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        var idCounts=new Dictionary<string,int>(StringComparer.Ordinal);
        int dismissedCount=0;

        for(int i=1;i<sheet.Rows.Count;i++)
        {
            var values=sheet.Rows[i];
            if(values.Take(Headers.Length).All(string.IsNullOrWhiteSpace))continue;
            J.Need(!missingCache.Contains(i+1),$"الصف {i+1}: خلية بصيغة بلا قيمة مخزّنة؛ أعد تصدير القيم.");
            string V(string key)
            {
                int col=Array.IndexOf(Headers,key);
                return col>=0&&col<values.Count?values[col].Trim():"";
            }
            string accountKey=V("المعرف");
            J.Need(accountKey!="",$"الصف {i+1}: المعرف مطلوب.");
            idCounts[accountKey]=idCounts.GetValueOrDefault(accountKey)+1;

            string ownerIqamaRaw=V("رقم بطاقة الهوية");
            string riderIqamaRaw=V("رقم الهوية1");
            J.Need(ownerIqamaRaw!=""||riderIqamaRaw!="",$"الصف {i+1}: رقم الهوية مطلوب.");
            string rider=J.Iqama(riderIqamaRaw!=""?riderIqamaRaw:ownerIqamaRaw);
            string ownerIqama=ownerIqamaRaw!=""?J.Iqama(ownerIqamaRaw):rider;

            bool active=IsActive(V("الحالة"));
            decimal validDays=Number(V("الأيام الصالحة"),i+1,"الأيام الصالحة",true);
            decimal orders=Number(V("الطلبات"),i+1,"الطلبات",true);
            decimal workDays=V("عدد ايام عمل المندوب")==""?0:Number(V("عدد ايام عمل المندوب"),i+1,"عدد ايام عمل المندوب",true);
            decimal distancePaid=V("المسافة المدفوعة")==""?0:Number(V("المسافة المدفوعة"),i+1,"المسافة المدفوعة");
            decimal distanceTotal=V("اجمالي المسافه")==""?0:Number(V("اجمالي المسافه"),i+1,"اجمالي المسافه");
            decimal violations=V("المخالفات")==""?0:Number(V("المخالفات"),i+1,"المخالفات",true);
            bool hasPerf=orders>0||validDays>0||workDays>0||distancePaid>0||distanceTotal>0;
            bool dismissed=!active;
            if(dismissed)
            {
                J.Need(hasPerf,$"الصف {i+1}: يوزر مفصول بلا أداء في الشهر؛ لا يُستورد.");
                dismissedCount++;
            }

            string from=NormalizeDate(V("تاريخ الاستلام1"),period,i+1,"تاريخ الاستلام1");
            string to=NormalizeDate(V("تاريخ التسليم"),period,i+1,"تاريخ التسليم");
            J.Need((from=="")==(to==""),$"الصف {i+1}: تاريخا الاستلام والتسليم معًا أو فارغان.");
            if(from!="")
            {
                J.Need(from.StartsWith(period+"-",StringComparison.Ordinal)&&to.StartsWith(period+"-",StringComparison.Ordinal),$"الصف {i+1}: فترة التشغيل داخل شهر الفاتورة.");
                J.Need(string.CompareOrdinal(from,to)<=0,$"الصف {i+1}: تاريخ الاستلام بعد التسليم.");
            }

            string accountId=ResolveAccountId(accountKey);
            var review=accounts.OfType<JsonObject>().SingleOrDefault(a=>a.S("accountId")==accountId)
                ??throw new ApiError($"الصف {i+1}: المعرف غير موجود في فاتورة كيتا المعتمدة.");
            J.Need(seen.Add(accountId+":"+rider),$"الصف {i+1}: تكرار نفس المندوب على نفس المعرف.");

            var account=localAccounts.Single(a=>a.S("id")==accountId);
            var person=Store.Get(c,rider,"rider",tx)??throw new ApiError($"الصف {i+1}: المندوب غير موجود.");
            FinancialScopes.DemandRider(actor,person);
            J.Need(ErpRiderClassification.ListedAsRider(person),$"الصف {i+1}: الهوية ليست لمندوب تشغيل.");
            if(!dismissed)OperationsImports.AssignmentPublic(account,rider,period,from,to);
            else if(from!="")
            {
                // Dismissed with performance: keep declared dates; assignment may already have ended.
                J.Need(DateOnly.TryParseExact(from,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)
                    &&DateOnly.TryParseExact(to,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),$"الصف {i+1}: تواريخ التشغيل بصيغة سنة-شهر-يوم.");
            }

            string expectedEligibility=V("الصلاحية المتوقعة");
            J.Need(expectedEligibility!="",$"الصف {i+1}: الصلاحية المتوقعة مطلوبة (متوقع فقط؛ النهائي من الفاتورة).");
            string category=DeriveCategory(person,company,V("المركبة"),V("نوع المركبة"));
            J.Need(PaySettings.Categories.Contains(category),$"الصف {i+1}: تعذر استنتاج فئة الكفالة والمركبة من الورقة.");

            int daysInMonth=DateTime.DaysInMonth(int.Parse(period[..4],CultureInfo.InvariantCulture),int.Parse(period[5..],CultureInfo.InvariantCulture));
            J.Need(validDays<=daysInMonth,$"الصف {i+1}: الأيام الصالحة تتجاوز أيام الشهر.");

            // Extra Keeta fields live under metrics so PayCycles.ValidateImportedReview field lists stay closed.
            var metrics=new JsonObject
            {
                ["validOnlineDays"]=validDays,
                ["workDays"]=workDays,
                ["distancePaid"]=distancePaid,
                ["distanceTotal"]=distanceTotal,
                ["violations"]=violations,
                ["vehicle"]=V("المركبة"),
                ["vehicleType"]=V("نوع المركبة"),
                ["substituteType"]=V("نوع البديل"),
                ["notes"]=V("الملاحظات"),
                ["ownerIqama"]=ownerIqama,
                ["ownerRegister"]=V("كفالة اليوزر"),
                ["idStart"]=from,
                ["idStop"]=to,
                ["riderStart"]=from,
                ["riderStop"]=to,
                ["dismissed"]=dismissed,
                ["active"]=active,
                ["expectedEligibility"]=expectedEligibility,
                ["expectedApproval"]="expected_only",
                ["expectedFinal"]=false
            };
            var share=new JsonObject
            {
                ["rider"]=rider,
                ["orders"]=orders,
                ["baseGross"]=0m, // filled after proportional allocation
                ["deduction"]=0m, // account-level invoice deductions allocated below
                ["category"]=category,
                ["days"]=validDays,
                ["metrics"]=metrics
            };
            pending.Add((review,share,i+1));

            var display=new JsonObject{["row"]=i+1,["dismissed"]=dismissed,["active"]=active,["expectedApproval"]="expected_only"};
            for(int col=0;col<Headers.Length;col++)
            {
                string key=Headers[col];
                string raw=col<values.Count?values[col].Trim():"";
                if(key is "تاريخ الاستلام1" or "تاريخ التسليم"&&raw!="")raw=NormalizeDate(raw,period,i+1,key);
                display[key]=key is "الأيام الصالحة" or "الطلبات" or "المخالفات" or "المسافة المدفوعة" or "عدد ايام عمل المندوب"
                    or "اجمالي المسافه" or "اجمالي طلبات المعادلة" or "اجمالي طلبات اليوزر" or "عدد البدلاء"
                    or "اجمالي ايام المعادلة" or "اجمالي ايام اليوزر"
                    ? (JsonNode)(raw==""?0m:Number(raw,i+1,key,key is "الأيام الصالحة" or "الطلبات" or "المخالفات" or "عدد ايام عمل المندوب" or "اجمالي طلبات المعادلة" or "اجمالي طلبات اليوزر" or "عدد البدلاء" or "اجمالي ايام المعادلة" or "اجمالي ايام اليوزر"))
                    : raw;
            }
            displayRows.Add(display);
        }

        J.Need(pending.Count>0,"لا توجد صفوف تشغيل بعد التحقق.");
        AllocateGross(pending);
        foreach(var (review,share,_) in pending)review["riders"]!.AsArray().Add(share);

        foreach(var group in accounts.OfType<JsonObject>().SelectMany(a=>a["riders"]!.AsArray().OfType<JsonObject>()).GroupBy(r=>r.S("rider")))
            J.Need(group.Select(r=>r.S("category")+":"+r.N("days")+":"+FinancialImports.Canonical(r["metrics"])).Distinct().Count()==1,
                "مقاييس وأيام وفئة المندوب تخص شهر التطبيق كله؛ لا تختلف بين اليوزرات.");

        // Rider order totals must match the approved invoice account orders.
        foreach(var reviewAcc in accounts.OfType<JsonObject>())
        {
            var shares=reviewAcc["riders"]!.AsArray().OfType<JsonObject>().ToList();
            if(shares.Count==0)continue;
            decimal invoiceOrders=reviewAcc.N("orders");
            decimal sheetOrders=shares.Sum(s=>s.N("orders"));
            J.Need(sheetOrders==invoiceOrders,$"مجموع طلبات التشغيل للمعرف {reviewAcc.S("accountId")} ({sheetOrders}) لا يطابق فاتورة كيتا ({invoiceOrders}).");
        }

        var deductions=OperationsImports.Deductions(lines);
        AllocateAccountDeductions(accounts,deductions);

        int duplicateIdRows=idCounts.Where(kv=>kv.Value>1).Sum(kv=>kv.Value);
        // Closed review shape for PayCycles; display envelope is returned separately.
        var result=new JsonObject
        {
            ["invoiceId"]=invoice.S("id"),
            ["app"]="keeta",
            ["accounts"]=accounts,
            ["deductions"]=deductions
        };
        PayCycles.ValidateImportedReview(c,tx,actor,company,period,result);
        envelope=new JsonObject
        {
            ["register"]=register,
            ["registerSheet"]=want,
            ["columns"]=new JsonArray(Headers.Select(h=>(JsonNode)h).ToArray()),
            ["displayRows"]=displayRows,
            ["counts"]=new JsonObject
            {
                ["rows"]=displayRows.Count,
                ["dismissed"]=dismissedCount,
                ["duplicateIdRows"]=duplicateIdRows,
                ["uniqueIds"]=idCounts.Count
            }
        };
        return result;
    }

    static void AllocateAccountDeductions(JsonArray accounts,JsonArray deductions)
    {
        foreach(var review in accounts.OfType<JsonObject>())
        {
            decimal due=deductions.OfType<JsonObject>().Where(d=>d.S("level")=="account_level"&&d.S("accountId")==review.S("accountId")).Sum(d=>d.N("amount"));
            var shares=review["riders"]!.AsArray().OfType<JsonObject>().ToList();
            if(shares.Count==0){J.Need(due==0,"خصم على يوزر بلا مناديب في ورقة التشغيل.");continue;}
            decimal orderSum=shares.Sum(s=>s.N("orders"));
            decimal allocated=0;
            for(int i=0;i<shares.Count;i++)
            {
                decimal part=i==shares.Count-1?due-allocated:orderSum>0?J.Money(due*(shares[i].N("orders")/orderSum)):J.Money(due/shares.Count);
                shares[i]["deduction"]=part;allocated+=part;
            }
        }
    }

    static void AllocateGross(List<(JsonObject Review,JsonObject Share,int Row)> pending)
    {
        foreach(var group in pending.GroupBy(p=>p.Review.S("accountId")))
        {
            var review=group.First().Review;
            decimal accountGross=review.N("baseGross");
            var shares=group.Select(g=>g.Share).ToList();
            decimal orderSum=shares.Sum(s=>s.N("orders"));
            decimal daySum=shares.Sum(s=>s.N("days"));
            if(orderSum>0)
            {
                decimal allocated=0;
                for(int i=0;i<shares.Count;i++)
                {
                    decimal part=i==shares.Count-1?accountGross-allocated:J.Money(accountGross*(shares[i].N("orders")/orderSum));
                    shares[i]["baseGross"]=part;allocated+=part;
                }
            }
            else if(daySum>0)
            {
                decimal allocated=0;
                for(int i=0;i<shares.Count;i++)
                {
                    decimal part=i==shares.Count-1?accountGross-allocated:J.Money(accountGross*(shares[i].N("days")/daySum));
                    shares[i]["baseGross"]=part;allocated+=part;
                }
            }
            else
            {
                decimal each=shares.Count==0?0:J.Money(accountGross/shares.Count);
                decimal allocated=0;
                for(int i=0;i<shares.Count;i++)
                {
                    decimal part=i==shares.Count-1?accountGross-allocated:each;
                    shares[i]["baseGross"]=part;allocated+=part;
                }
            }
        }
    }

    static string DeriveCategory(JsonObject person,string company,string vehicle,string vehicleType)
    {
        bool sponsored=person.S("sponsorId")==company||FinancialCore.FindSponsor(person.S("sponsorId"))?.Id==company;
        string blob=(vehicle+" "+vehicleType).Trim();
        bool bike=ContainsAny(blob,"دراجة","bike","motorbike","scooter");
        bool companyVehicle=ContainsAny(blob,"شركة","company","مؤسسة")&&!ContainsAny(blob,"خاص","خاصة","private");
        bool privateVehicle=ContainsAny(blob,"خاص","خاصة","private")||(!companyVehicle&&blob!="");
        if(privateVehicle||(!companyVehicle&&!bike&&blob!=""))
            return sponsored?"sponsored_private":"external_private";
        if(bike)return sponsored?"sponsored_company_bike":"external_company_bike";
        return sponsored?"sponsored_company_car":"external_company_car";
    }

    static bool ContainsAny(string text,params string[] parts)=>parts.Any(p=>text.Contains(p,StringComparison.OrdinalIgnoreCase));

    static bool IsActive(string raw)
    {
        string v=raw.Trim();
        if(v is "" )throw new ApiError("الحالة مطلوبة (نشط أو مفصول).");
        string u=v.ToUpperInvariant();
        if(u is "TRUE" or "1" or "T" or "YES"||v is "نعم" or "نشط" or "فعال")return true;
        if(u is "FALSE" or "0" or "F" or "NO"||v is "لا" or "مفصول" or "منتهي" or "موقوف" or "غير نشط")return false;
        throw new ApiError("قيمة الحالة غير معروفة؛ استخدم نشط/مفصول أو TRUE/FALSE.");
    }

    static decimal Number(string raw,int row,string field,bool integer=false)
    {
        J.Need(raw!="",$"الصف {row}: قيمة رقمية مطلوبة في «{field}».");
        J.Need(decimal.TryParse(J.Digits(raw),NumberStyles.Number,CultureInfo.InvariantCulture,out var n),$"الصف {row}: قيمة رقمية غير صالحة في «{field}».");
        J.Need(n>=0&&n<=100000000&&n==decimal.Round(n,4)&&(!integer||n==decimal.Truncate(n)),$"الصف {row}: «{field}» خارج النطاق المسموح.");
        return n;
    }

    static string NormalizeDate(string raw,string period,int row,string field)
    {
        if(string.IsNullOrWhiteSpace(raw))return "";
        string v=raw.Trim();
        if(double.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out var oa)&&oa is >20000 and <80000)
            v=DateTime.FromOADate(oa).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        v=v.Replace('/','-');
        if(Regex.IsMatch(v,@"^\d{1,2}-\d{1,2}-\d{4}$"))
        {
            var p=v.Split('-');v=$"{p[2]}-{p[1].PadLeft(2,'0')}-{p[0].PadLeft(2,'0')}";
        }
        J.Need(DateOnly.TryParseExact(v,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),$"الصف {row}: تاريخ غير صالح في «{field}»؛ استخدم سنة-شهر-يوم.");
        return v;
    }
}
