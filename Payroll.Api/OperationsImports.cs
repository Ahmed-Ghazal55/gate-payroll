using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
namespace Payroll;

// Final invoice facts plus explicit actual operators; importing this review never runs payroll.
public static class OperationsImports
{
    public const string Kind="operations_import";
    public static string App(string value)
    {string app=PayPlatforms.For(value).App;J.Need(app is "ninja" or "keeta" or "hs","التطبيق غير مدعوم لاستيراد ملف التشغيل.");return app;}
    public static void Scope(ClaimsPrincipal actor,string app,string company,string permission="pay.manage")
    {AccessPolicy.Demand(actor,permission);FinancialScopes.DemandSponsor(actor,company);J.Need(FinancialScopes.Platform(actor,PayPlatforms.For(app).Platform),"المنصة خارج نطاق المستخدم.");}
    static string SignatureKind(string app,byte[] bytes)=>"operations:"+app+":"+Convert.ToHexString(SHA256.HashData(bytes));
    public static JsonObject Invoice(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string app,string id,string permission="pay.manage")
    {
        var invoice=Store.Get(c,id,"platform_invoice",tx)??throw new ApiError("الفاتورة غير موجودة.",404);
        Scope(actor,app,invoice.S("companyId"),permission);
        J.Need(PayPlatforms.For(invoice.S("platform")).App==app&&invoice.S("approvalStatus")=="approved","يلزم اعتماد الفاتورة لنفس التطبيق أولًا.");
        J.Need(invoice.S("journalEntryId")==""&&invoice.S("reversalJournalEntryId")=="","الفاتورة مرحلة أو معكوسة؛ لا تكرر الإيراد.");J.Period(invoice.Required("period"));
        return invoice;
    }
    public static List<JsonObject> SourceLines(DbConnection c,DbTransaction? tx,string app,string invoice)=>app=="hs"?HungerStationInvoices.EffectiveLines(c,tx,invoice):Store.List(c,"platform_invoice_line",tx).Where(l=>l.S("invoiceId")==invoice).ToList();
    public static JsonObject Account(JsonObject line)
    {
        // A native HS file must first have reviewed normalized facts. Rider salary/net is not account revenue.
        J.Need(line.S("platformAccountId",line.S("accountId"))!=""&&line.ContainsKey("baseGross")&&line.ContainsKey("incentives")&&line.ContainsKey("orders"),"source_line_missing_invoice_gross: يلزم سطر معياري مراجع للاستحقاق والطلبات والحوافز.");
        return new(){["accountId"]=line.S("platformAccountId",line.S("accountId")),["sourceLineId"]=line.S("id"),["baseGross"]=line["baseGross"]!.DeepClone(),["incentives"]=line["incentives"]!.DeepClone(),["orders"]=line["orders"]!.DeepClone(),["riders"]=new JsonArray()};
    }
    public static JsonArray Deductions(IEnumerable<JsonObject> lines)
    {
        var result=new JsonArray();
        foreach(var line in lines)
        {
            if(line["deductions"] is JsonArray parts)
            {
                J.Need(parts.OfType<JsonObject>().Sum(d=>d.N("amount"))==line.N("deductionAmount"),"مكونات خصم السطر لا تطابق الإجمالي.");
                foreach(var part in parts.OfType<JsonObject>())Add(line,part,part.S("category"));
            }
            else if(line.N("deductionAmount")!=0)Add(line,line,"");
        }
        return result;
        void Add(JsonObject line,JsonObject part,string category)
        {
            decimal amount=part==line?line.N("deductionAmount"):part.N("amount");if(amount==0)return;
            string level=part.S("level",part.S("deductionLevel"));J.Need(amount>0&&amount==J.Money(amount)&&level is "company_level" or "account_level" or "rider_level","يلزم تصنيف خصومات الفاتورة المعتمد قبل التشغيل.");
            result.Add(new JsonObject{["sourceLineId"]=line.S("id"),["sourceCategory"]=category,["level"]=level,["accountId"]=level=="account_level"?line.S("platformAccountId",line.S("accountId")):"",["amount"]=amount,["expenseAccount"]=part.S("expenseAccount"),["riderId"]=part.S("riderId")});
        }
    }
    static void Assignment(JsonObject account,string rider,string period,string from,string to)
    {
        var month=DateTimeOffset.Parse(period+"-01T00:00:00+03:00",CultureInfo.InvariantCulture);var next=month.AddMonths(1);
        J.Need((from=="")== (to==""),"from/to مطلوبان معًا أو يتركان فارغين.");
        DateTimeOffset? declaredFrom=null,declaredTo=null;
        if(from!="")
        {
            J.Need(DateOnly.TryParseExact(from,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var first)&&DateOnly.TryParseExact(to,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),"تواريخ التشغيل YYYY-MM-DD.");
            var last=DateOnly.ParseExact(to,"yyyy-MM-dd",CultureInfo.InvariantCulture);J.Need(first<=last&&from.StartsWith(period+"-",StringComparison.Ordinal)&&to.StartsWith(period+"-",StringComparison.Ordinal),"فترة التشغيل يجب أن تكون داخل شهر الفاتورة.");
            declaredFrom=new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(3));declaredTo=new DateTimeOffset(last.AddDays(1).ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(3));
        }
        bool matches=false;
        foreach(var assignment in (account["assignments"] as JsonArray??[]).OfType<JsonObject>().Where(a=>a.S("riderId")==rider))
        {
            if(!DateTimeOffset.TryParse(assignment.S("RawEffectiveFrom"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var start))continue;
            var end=DateTimeOffset.MaxValue;if(assignment.S("RawEffectiveTo")!=""&&!DateTimeOffset.TryParse(assignment.S("RawEffectiveTo"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out end))continue;
            if(end<=start)continue;
            if(declaredFrom.HasValue?start<=declaredFrom&&end>=declaredTo:start<next&&end>month)matches=true;
        }
        J.Need(matches,"actual_operator_unmatched: المشغل الفعلي وتوقيت تكليفه مطلوبان؛ المالك المسجل ليس دليلًا.");
    }
    public static JsonObject Parse(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string app,JsonObject input,byte[] bytes)
    {
        app=App(app);var invoice=Invoice(c,tx,actor,app,input.Required("invoiceId"));string period=invoice.S("period"),company=invoice.S("companyId");
        J.Need(bytes.Length is >0 and <=8*1024*1024,"ملفxlsx حتى8ميجابايت.");
        var sheets=Imports.Read(bytes,"operations.xlsx").Where(s=>s.Rows.Any(r=>r.Any(v=>!string.IsNullOrWhiteSpace(v)))).ToList();J.Need(sheets.Count==1,"ورقة واحدة لكل معاينة تشغيل.");
        var sheet=sheets.Single();var header=sheet.Rows[0].Select(v=>v.Trim()).ToList();var step=ImportCatalog.Steps().Single(s=>s.Key==(app=="hs"?"hungerstation":app)+"_operations");
        J.Need(header.Distinct().Count()==header.Count&&header.All(h=>step.Columns.Any(c=>c.Header==h))&&step.Required.All(header.Contains),"أعمدة التشغيل لا تطابق نموذج الكتالوج.");
        var missingCache=KeetaInvoices.MissingCache(bytes,sheet.Name,Enumerable.Range(0,header.Count).ToHashSet());
        var lines=SourceLines(c,tx,app,invoice.S("id"));J.Need(lines.Count>0,"الفاتورة بلا سطور.");var accounts=new JsonArray();
        foreach(var line in lines)
        {
            if(line.S("platformAccountId",line.S("accountId"))=="")
            {J.Need(line.ContainsKey("baseGross")&&line.ContainsKey("incentives")&&line.ContainsKey("orders")&&line.N("baseGross")==0&&line.N("incentives")==0&&line.N("orders")==0,"سطر شركة باستحقاق غير موزع يحتاج مراجعة.");continue;}
            accounts.Add(Account(line));
        }
        J.Need(accounts.Count>0&&accounts.OfType<JsonObject>().Select(a=>a.S("accountId")).Distinct().Count()==accounts.Count,"يلزم سطر فاتورة واحد لكل يوزر.");
        var localAccounts=Store.List(c,"platform_account",tx).ToDictionary(a=>a.S("id"));var seen=new HashSet<string>();
        for(int i=1;i<sheet.Rows.Count;i++)
        {
            var values=sheet.Rows[i];if(values.All(string.IsNullOrWhiteSpace))continue;
            string V(string key){int col=header.IndexOf(key);return col>=0&&col<values.Count?values[col].Trim():"";}
            J.Need(!missingCache.Contains(i+1),"formula_without_cached_value: row "+(i+1));
            J.Need(V("period (YYYY-MM)")==period,"period_mismatch: row "+(i+1));string accountId=V("platformAccountId"),rider=J.Iqama(V("iqama"));
            var review=accounts.OfType<JsonObject>().SingleOrDefault(a=>a.S("accountId")==accountId)??throw new ApiError("invoice_account_unmatched: row "+(i+1));
            J.Need(seen.Add(accountId+":"+rider),"duplicate_operator: row "+(i+1));
            var account=localAccounts.GetValueOrDefault(accountId)??throw new ApiError("يوزر المنصة غير موجود.");
            J.Need(account.S("companyId")==company&&PayPlatforms.For(account.S("platformCode")).App==app,"يوزر خارج السجل أو التطبيق.");
            var person=Store.Get(c,rider,"rider",tx)??throw new ApiError("rider_not_found: row "+(i+1));FinancialScopes.DemandRider(actor,person);J.Need(ErpRiderClassification.ListedAsRider(person),"الهوية ليست لمندوب تشغيل.");
            Assignment(account,rider,period,V("from (YYYY-MM-DD)"),V("to (YYYY-MM-DD)"));
            decimal Number(string key,bool integer=false,bool money=false)
            {
                J.Need(V(key)!=""&&decimal.TryParse(J.Digits(V(key)),NumberStyles.Number,CultureInfo.InvariantCulture,out _),"invalid_number: row "+(i+1));
                decimal n=decimal.Parse(J.Digits(V(key)),NumberStyles.Number,CultureInfo.InvariantCulture);J.Need(n>=(key=="baseGross"?-100000000:0)&&n<=100000000&&n==decimal.Round(n,4)&&(!integer||n==decimal.Truncate(n))&&(!money||n==J.Money(n)),"invalid_number: row "+(i+1));return n;
            }
            decimal days=Number("days",true),orders=Number("orders",true);J.Need(days<=DateTime.DaysInMonth(int.Parse(period[..4],CultureInfo.InvariantCulture),int.Parse(period[5..],CultureInfo.InvariantCulture)),"أيام التشغيل تتجاوز أيام الشهر.");
            J.Need(PaySettings.Categories.Contains(V("category")),"فئة الكفالة والمركبة مطلوبة؛ car/bike وحدهما غير كافيين.");
            var metrics=new JsonObject();foreach(var column in step.Columns.Where(col=>col.Numeric&&!new[]{"days","orders","baseGross","deduction"}.Contains(col.Header)))if(V(column.Header)!="")metrics[column.Header]=Number(column.Header);
            review["riders"]!.AsArray().Add(new JsonObject{["rider"]=rider,["orders"]=orders,["baseGross"]=Number("baseGross",money:true),["deduction"]=Number("deduction",money:true),["category"]=V("category"),["days"]=days,["metrics"]=metrics});
        }
        foreach(var group in accounts.OfType<JsonObject>().SelectMany(a=>a["riders"]!.AsArray().OfType<JsonObject>()).GroupBy(r=>r.S("rider")))
            J.Need(group.Select(r=>r.S("category")+":"+r.N("days")+":"+FinancialImports.Canonical(r["metrics"])).Distinct().Count()==1,"مقاييس وأيام وفئة المندوب تخص شهر التطبيق كله؛ لا تختلف بين اليوزرات.");
        var result=new JsonObject{["invoiceId"]=invoice.S("id"),["app"]=app,["accounts"]=accounts,["deductions"]=Deductions(lines)};
        PayCycles.ValidateImportedReview(c,tx,actor,company,period,result);return result;
    }
    public static JsonObject Preview(DbConnection c,ClaimsPrincipal actor,string app,JsonObject input,byte[] bytes)
    {
        app=App(app);var rows=new JsonArray(Parse(c,null,actor,app,input,bytes));return new(){["rows"]=rows,["fingerprint"]=FinancialImports.Issue(c,SignatureKind(app,bytes),rows,actor.Identity!.Name!)};
    }
    public static JsonObject Commit(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string app,JsonObject input,byte[] bytes)
    {
        app=App(app);FinancialImports.Verify(c,tx,input,SignatureKind(app,bytes),actor.Identity!.Name!);var review=Parse(c,tx,actor,app,input,bytes);
        J.Need(input["rows"] is JsonArray{Count:1}&&FinancialImports.Canonical(input["rows"]![0])==FinancialImports.Canonical(review),"تغيرت معاينة الملف؛ أعد المعاينة.");
        string sha=Convert.ToHexString(SHA256.HashData(bytes));J.Need(!Store.List(c,Kind,tx).Any(d=>d.S("invoiceId")==review.S("invoiceId")&&d.S("sourceSha256")==sha),"التشغيل محفوظ بالفعل؛ لا تكرره.");
        var invoice=Store.Get(c,review.S("invoiceId"),"platform_invoice",tx)!;
        var doc=new JsonObject{["id"]="operations-import:"+Guid.NewGuid().ToString("N"),["app"]=app,["invoiceId"]=invoice.S("id"),["companyId"]=invoice.S("companyId"),["period"]=invoice.S("period"),["sourceSha256"]=sha,["review"]=review,["createdBy"]=actor.Identity!.Name,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        Store.Save(c,Kind,doc,tx);Store.Audit(c,tx,"pay.operations_imported",doc.S("id"),actor.Identity!.Name!);return doc;
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/pay/apps/{app}/operations/import/preview",(string app,JsonObject input,HttpContext ctx)=>store.Read(c=>Preview(c,ctx.User,app,input,FinancialImports.FileBytes(input)))).WithTags("Payroll").WithSummary("Preview actual operators against approved invoice lines using catalog XLSX columns");
        api.MapPost("/pay/apps/{app}/operations/import/commit",(string app,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Commit(c,tx,ctx.User,app,input,FinancialImports.FileBytes(input)))).WithTags("Payroll").WithSummary("Confirm signed operations review without posting payroll or creating debts");
        api.MapGet("/pay/apps/{app}/operations/imports/{id}",(string app,string id,HttpContext ctx)=>store.Read(c=>{app=App(app);var doc=Store.Get(c,id,Kind)??throw new ApiError("استيراد التشغيل غير موجود.",404);Scope(ctx.User,app,doc.S("companyId"),"pay.view");J.Need(doc.S("app")==app,"التطبيق لا يطابق الاستيراد.");return doc;})).WithTags("Payroll").WithSummary("Read a scoped saved operations review for a payroll cycle");
    }
}
