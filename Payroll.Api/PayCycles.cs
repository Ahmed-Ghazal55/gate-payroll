using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PayCycles
{
    public const string Kind="pay_cycle",LineKind="pay_cycle_line";
    static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    static JsonArray Rows(JsonNode? node,int max=25000)=>PayValues.Array(node??new JsonArray(),max);
    static JsonArray Blockers(JsonObject row)=>row["blockers"] as JsonArray??[];
    static decimal Money(JsonObject row,string key){decimal n=PayValues.Number(row,key);J.Need(n==J.Money(n),"المبلغ بدقة هللتين.");return n;}
    // A captain's invoiced base can be negative (BR31); deductions and incentives stay non-negative.
    static decimal SignedMoney(JsonObject row,string key){decimal n=PayValues.Number(row,key,-1000000000);J.Need(n==J.Money(n),"المبلغ بدقة هللتين.");return n;}
    static void Open(DbConnection c,DbTransaction? tx,JsonObject cycle)=>J.Need(!Store.List(c,"accounting_period",tx).Any(p=>p.S("companyId")==cycle.S("companyId")&&p.S("period")==cycle.S("postingPeriod",cycle.S("period"))&&p.S("status")=="closed"),"الفترة مغلقة؛ لا تتجاوز الإقفال.");
    static void Scope(ClaimsPrincipal actor,JsonObject cycle,string permission)
    {AccessPolicy.Demand(actor,permission);FinancialScopes.DemandSponsor(actor,cycle.S("companyId"));foreach(string app in Rows(cycle["invoices"]).OfType<JsonObject>().Select(i=>i.S("app")).Distinct())PaySettings.Scope(actor,permission,app);}
    public static bool Visible(ClaimsPrincipal actor,JsonObject cycle)=>FinancialScopes.Sponsor(actor,cycle.S("companyId"))&&Rows(cycle["invoices"]).OfType<JsonObject>().All(i=>FinancialScopes.Platform(actor,PayPlatforms.For(i.S("app")).Platform));
    public static JsonObject Create(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,"pay.manage");PayValues.Fields(input,"period","companyId","app","invoices","adjustmentAccounts","replacesCycleId");string period=J.Period(input.Required("period")),company=FinancialCore.ResolveSponsor(input.Required("companyId")).Id;
        J.Need(!Store.List(c,Kind,tx).Any(d=>d.S("period")==period&&d.S("companyId")==company&&d.S("status") is not ("reversed" or "cancelled")),"توجد دورة للسجل والشهر؛ ادمج كل التطبيقات فيها.");
        var invoices=Rows(input["invoices"],1000).DeepClone().AsArray();J.Need(invoices.Count>0,"الدورة تحتاج فاتورة معتمدة.");
        var cycle=new JsonObject{["id"]="pay-cycle:"+Guid.NewGuid().ToString("N"),["period"]=period,["companyId"]=company,["sponsorId"]=company,["app"]=input.S("app")==""?"":PayPlatforms.App(input.S("app")),["stage"]=0,["status"]="draft",["invoices"]=invoices,["adjustmentAccounts"]=(input["adjustmentAccounts"] as JsonObject??new()).DeepClone(),["createdBy"]=actor.Identity!.Name,["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["history"]=new JsonArray()};
        if(input.S("replacesCycleId")!="")
        {var prior=Store.Get(c,input.S("replacesCycleId"),Kind,tx)??throw new ApiError("الدورة الأصلية غير موجودة.");J.Need(prior.S("status")=="reversed"&&prior.S("period")==period&&prior.S("companyId")==company,"التصحيح يحتاج أصلًا معكوسًا لنفس السجل والشهر.");cycle["replacesCycleId"]=prior.S("id");cycle["postingPeriod"]=prior.S("reversalPeriod");}
        else J.Need(!Store.List(c,Kind,tx).Any(d=>d.S("status")=="reversed"&&d.S("period")==period&&d.S("companyId")==company),"حدد الدورة المعكوسة صراحة للتصحيح.");
        Scope(actor,cycle,"pay.manage");Open(c,tx,cycle);UploadInvoices(c,tx,cycle);ReviewInvoices(c,tx,cycle);cycle["sourceFingerprint"]=Fingerprint(c,tx,cycle);Store.Save(c,Kind,cycle,tx);Store.Audit(c,tx,"pay.cycle_created",cycle.S("id"),actor.Identity!.Name!);return cycle;
    }
    // Read-only validation shared by operation imports and the normal staged cycle workflow.
    public static void ValidateImportedReview(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string company,string period,JsonObject review,string permission="pay.manage")
    {
        var cycle=new JsonObject{["companyId"]=company,["period"]=period,["invoices"]=new JsonArray(review.Clone())};
        Scope(actor,cycle,permission);ReviewInvoices(c,tx,cycle);ValidateRiders(c,tx,cycle,actor);
    }
    static void ReviewInvoices(DbConnection c,DbTransaction? tx,JsonObject cycle)
    {
        var invoices=Rows(cycle["invoices"]).OfType<JsonObject>().ToList();J.Need(invoices.Select(i=>i.S("invoiceId")).Distinct().Count()==invoices.Count,"فاتورة مكررة.");
        foreach(var review in invoices)
        {
            PayValues.Fields(review,"invoiceId","app","accounts","deductions");string app=PayPlatforms.App(review.Required("app"));J.Need(app!="jahez"&&(cycle.S("app")==""||cycle.S("app")==app),"جاهز له تصفية منفصلة؛ التطبيق لا يطابق الدورة.");
            var source=Store.Get(c,review.Required("invoiceId"),"platform_invoice",tx)??throw new ApiError("الفاتورة المصدر غير موجودة.");
            J.Need(PayPlatforms.For(source.S("platform")).App==app&&source.S("period")==cycle.S("period")&&source.S("companyId")==cycle.S("companyId")&&(source.S("approvalStatus")=="approved"||source.S("approvalStatus")=="cycle_pending"&&source.S("payCycleId")==cycle.S("id")),"المصدر يحتاج اعتمادًا لنفس التطبيق والسجل والشهر.");
            J.Need(source.S("journalEntryId")==""&&source.S("reversalJournalEntryId")=="","المصدر سبق ترحيله أو عكسه؛ لا تسجل الإيراد مرتين.");
            J.Need(!Store.List(c,Kind,tx).Any(d=>d.S("id")!=cycle.S("id")&&d.S("status") is "approved" or "partially_approved"&&Rows(d["invoices"]).OfType<JsonObject>().Any(i=>i.S("invoiceId")==source.S("id"))),"المصدر مستخدم في دورة معتمدة.");
            if(app=="hs")HungerStationInvoices.DemandResolvedDisputes(c,tx,source.S("id"));
            if(app=="ninja")NinjaInvoices.DemandResolvedDisputes(c,tx,source.S("id"));
            if(app=="keeta")KeetaInvoices.Workflow.DemandResolvedDisputes(c,tx,source.S("id"));
            var accounts=Rows(review["accounts"]).OfType<JsonObject>().ToList();var deductions=Rows(review["deductions"]).OfType<JsonObject>().ToList();J.Need(accounts.Count>0&&accounts.Select(a=>a.S("accountId")).Distinct().Count()==accounts.Count,"استحقاقات اليوزرات مطلوبة بلا تكرار.");
            foreach(var a in accounts){PayValues.Fields(a,"accountId","sourceLineId","baseGross","incentives","orders","riders");SignedMoney(a,"baseGross");Money(a,"incentives");decimal orders=PayValues.Number(a,"orders",0,int.MaxValue);J.Need(orders==decimal.Truncate(orders),"طلبات فعلية صحيحة مطلوبة.");}
            foreach(var d in deductions){PayValues.Fields(d,"sourceLineId","sourceCategory","level","accountId","amount","expenseAccount","riderId");Money(d,"amount");J.Need(d.S("level") is "company_level" or "account_level" or "rider_level","صنف كل خصم: شركة أو يوزر.");if(d.S("level")=="company_level")J.Need(PayPlatforms.Active(d.Required("expenseAccount")).StartsWith('5'),"خصم الشركة يحتاج حساب تكلفة.");else if(d.S("level")=="account_level")J.Need(accounts.Any(a=>a.S("accountId")==d.Required("accountId")),"يوزر الخصم غير موجود.");}
            J.Need(source.ContainsKey("deductionsTotal"),"إجمالي خصومات المصدر غير مثبت؛ ارفع نسخة معيارية مراجعة قبل الاحتساب.");
            decimal gross=accounts.Sum(a=>a.N("baseGross")+a.N("incentives"));
            J.Need(gross==source.N("amountExclVat")&&source.N("vat")>=0,"مجموع استحقاقات اليوزرات لا يطابق الفاتورة قبل الضريبة والخصومات.");
            // Each classified amount is tied to a source line, never inferred from the registered owner.
            var sourceLines=app=="hs"?HungerStationInvoices.EffectiveLines(c,tx,source.S("id")):Store.List(c,"platform_invoice_line",tx).Where(l=>l.S("invoiceId")==source.S("id")).ToList();
            foreach(var a in accounts)
            {
                var line=sourceLines.SingleOrDefault(l=>l.S("id")==a.S("sourceLineId"));
                if(line?.ContainsKey("baseGross")==true)J.Need(line.N("baseGross")==a.N("baseGross")&&line.N("incentives")==a.N("incentives")&&line.N("orders")==a.N("orders"),"استحقاق اليوزر لا يطابق سطر المصدر.");
            }
            foreach(var a in accounts)J.Need(sourceLines.Any(l=>l.S("id")==a.Required("sourceLineId")&&(l.S("platformAccountId")==a.S("accountId")||l.S("accountId")==a.S("accountId"))),"استحقاق اليوزر يحتاج سطر مصدر مطابقًا.");
            foreach(var d in deductions)
            {
                var sourceLine=sourceLines.SingleOrDefault(l=>l.S("id")==d.Required("sourceLineId"));J.Need(sourceLine!=null,"الخصم يحتاج سطر مصدر.");
                if((app is "ninja" or "keeta"||(app is "hs" or "chefz")&&sourceLine!["deductions"] is JsonArray)&&source.S("status")=="final")
                {
                    var components=(sourceLine!["deductions"] as JsonArray??[]).OfType<JsonObject>().ToList();
                    var selected=components.Where(x=>d.S("sourceCategory")==""||x.S("category")==d.S("sourceCategory")).ToList();
                    J.Need(selected.Count>0&&selected.Sum(x=>x.N("amount"))==d.N("amount")&&selected.All(x=>x.S("level")==d.S("level")&&(x.S("level")!="company_level"||x.S("expenseAccount")==d.S("expenseAccount"))),"الخصم لا يطابق تصنيف الفاتورة المعتمد.");
                    if(d.S("level")=="account_level")J.Need(d.S("accountId")==sourceLine.S("platformAccountId"),"يوزر الخصم لا يطابق مصدره.");
                    if(d.S("level")=="rider_level")J.Need(selected.All(x=>x.S("riderId")==d.Required("riderId"))&&accounts.SelectMany(a=>Rows(a["riders"]).OfType<JsonObject>()).Any(r=>r.S("rider")==d.S("riderId")),"مندوب الخصم لا يطابق التصنيف أو توزيع التشغيل.");
                }
                else J.Need(!sourceLine!.ContainsKey("deductionAmount")||sourceLine.N("deductionAmount")==d.N("amount"),"الخصم يحتاج سطر مصدر.");
            }
            foreach(var group in deductions.GroupBy(d=>d.S("sourceLineId")))J.Need(!(group.Any(d=>d.S("sourceCategory")=="")&&group.Count()>1),"لا تجمع إجمالي السطر مع مكوناته.");
            J.Need(deductions.Select(d=>d.S("sourceLineId")+":"+d.S("sourceCategory")).Distinct().Count()==deductions.Count,"مصدر خصم مكرر.");
            J.Need(deductions.Sum(d=>d.N("amount"))==source.N("deductionsTotal"),"تصنيف الخصومات لا يطابق إجمالي المصدر؛ راجع الفاتورة.");
        }
    }
    public static JsonObject Advance(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        var cycle=Store.Get(c,id,Kind,tx)?.Clone()??throw new ApiError("الدورة غير موجودة.",404);int current=(int)cycle.N("stage");
        if(input.S("action")=="cancel")
        {Scope(actor,cycle,"pay.manage");J.Need(cycle.S("status") is "draft" or "review" or "needs_decision","لا تلغ دورة معتمدة؛ اطلب العكس.");cycle["status"]="cancelled";Store.Save(c,Kind,cycle,tx);Store.Audit(c,tx,"pay.cycle_cancelled",id,actor.Identity!.Name!);return cycle;}
        if(current>=7&&input.N("stage")==8){DebtDeductionsAct(c,tx,actor,id,new(){["action"]="confirm-all"});return Store.Get(c,id,Kind,tx)!;}
        if(current>=7&&input.N("stage")==7)
        {
            Scope(actor,cycle,"pay.approve");J.Need(cycle.S("status") is "approved" or "partially_approved","الدورة غير متاحة للاعتماد.");
            if(cycle.S("status")=="approved"){J.Need(cycle.S("approvedBy")==actor.Identity!.Name,"اعتماد موجود بمعتمد آخر.");return cycle;}
            Open(c,tx,cycle);J.Need(cycle.S("sourceFingerprint")==Fingerprint(c,tx,cycle),"تغير المصدر؛ راجع العكس والتصحيح.");Approve(c,tx,cycle,actor);Store.Save(c,Kind,cycle,tx);return cycle;
        }
        if(input.S("action").StartsWith("reversal",StringComparison.Ordinal))return Reverse(c,tx,actor,cycle,input);
        PayValues.Fields(input,"stage");J.Need(PayValues.Number(input,"stage",1,7)==current+1,"المراحل بالترتيب؛ أرسل المرحلة التالية المتوقعة.");Scope(actor,cycle,current==6?"pay.approve":"pay.manage");J.Need(cycle.S("status") is "draft" or "review" or "needs_decision","الدورة غير متاحة للمراحل.");
        J.Need(cycle.S("sourceFingerprint")==Fingerprint(c,tx,cycle),"تغير المصدر منذ بدء المراجعة؛ أعد إنشاء دورة مراجعة مستقلة.");Open(c,tx,cycle);ReviewInvoices(c,tx,cycle);int next=current+1;
        if(next==2)ValidateAccounts(c,tx,cycle);
        if(next==3)ValidateRiders(c,tx,cycle,actor);
        if(next==4)BuildLines(c,tx,cycle,actor);
        if(next==5)BuildDeductions(c,tx,cycle,actor);
        if(next==6)Merge(c,tx,cycle);
        if(next==7&&!Approve(c,tx,cycle,actor)){Store.Save(c,Kind,cycle,tx);return cycle;}
        cycle["stage"]=next;cycle["advancedBy"]=actor.Identity!.Name;if(next!=7)cycle["status"]=cycle.S("status")=="needs_decision"?"needs_decision":"review";
        cycle["history"]!.AsArray().Add(new JsonObject{["stage"]=next,["advancedBy"]=actor.Identity!.Name,["at"]=DateTimeOffset.UtcNow.ToString("O")});Store.Save(c,Kind,cycle,tx);Store.Audit(c,tx,"pay.cycle_advanced",id,actor.Identity!.Name!);return cycle;
    }
    static IEnumerable<(string App,JsonObject Account)> Accounts(JsonObject cycle)=>Rows(cycle["invoices"]).OfType<JsonObject>().SelectMany(i=>Rows(i["accounts"]).OfType<JsonObject>().Select(a=>(i.S("app"),a)));
    static void ValidateAccounts(DbConnection c,DbTransaction tx,JsonObject cycle)
    {
        foreach(var (app,a) in Accounts(cycle))
        {
            var source=Store.Get(c,a.Required("accountId"),"platform_account",tx)??throw new ApiError("يوزر المنصة غير موجود.");
            J.Need(source.S("companyId")==cycle.S("companyId")&&PayPlatforms.For(source.S("platformCode")).App==app,"السجل أو التطبيق لا يطابق اليوزر.");PaySettings.Resolve(c,tx,"pay_terms",app,cycle.S("period"));
        }
    }
    static DateTimeOffset Time(string value){J.Need(DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var time),"توقيت المصدر غير صالح.");return time;}
    static bool Overlaps(JsonObject a,string period)=>AssignmentSpan.Overlaps(a,period);
    static bool Overlaps(JsonObject a,string period,string from,string to)
    {string start=a.S(from),end=a.S(to);J.Need(start!="","توقيت التكليف الفعلي مفقود.");var month=Time(period+"-01T00:00:00+03:00");return Time(start)<month.AddMonths(1)&&(end==""||Time(end)>month);}
    static void ValidateRiders(DbConnection c,DbTransaction? tx,JsonObject cycle,ClaimsPrincipal actor)
    {
        foreach(var (app,a) in Accounts(cycle))
        {
            var source=Store.Get(c,a.S("accountId"),"platform_account",tx)!;var shares=Rows(a["riders"]).OfType<JsonObject>().ToList();J.Need(shares.Count>0&&shares.Select(r=>r.S("rider")).Distinct().Count()==shares.Count,"وزع الاستحقاق على المندوب الفعلي دون تكرار.");
            foreach(var r in shares)
            {
                PayValues.Fields(r,"rider","orders","baseGross","deduction","category","days","metrics","sourceLineId","operationSourceIds");
                if(r.ContainsKey("sourceLineId"))J.Need(r.S("sourceLineId")==a.S("sourceLineId"),"مصدر استحقاق المندوب لا يطابق سطر الفاتورة.");
                if(r.ContainsKey("operationSourceIds"))
                {
                    J.Need(r["operationSourceIds"] is JsonArray proof&&proof.Count>0&&proof.All(p=>p is JsonValue j&&j.TryGetValue<string>(out _)),"مراجع مصدر التشغيل مطلوبة.");
                    foreach(string proofId in r["operationSourceIds"]!.AsArray().Select(p=>p!.GetValue<string>()))
                    {
                        var day=Store.Get(c,proofId,"ninja_daily",tx);var import=Store.Get(c,proofId,OperationsImports.Kind,tx);
                        bool dailyMatch=day!=null&&day.S("riderId")==r.S("rider")&&day.S("platformAccountId")==a.S("accountId")&&day.S("companyId")==cycle.S("companyId")&&day.S("date").StartsWith(cycle.S("period")+"-",StringComparison.Ordinal);
                        bool importedMatch=import!=null&&import.S("invoiceId")==Store.Get(c,a.S("sourceLineId"),"platform_invoice_line",tx)?.S("invoiceId")&&(import["review"]?["accounts"] as JsonArray??[]).OfType<JsonObject>().Any(ac=>ac.S("accountId")==a.S("accountId")&&(ac["riders"] as JsonArray??[]).OfType<JsonObject>().Any(share=>share.S("rider")==r.S("rider")&&new[]{"orders","baseGross","deduction","category","days","metrics"}.All(key=>JsonNode.DeepEquals(share[key],r[key]))));
                        J.Need(dailyMatch||importedMatch,"مرجع التشغيل لا يطابق توزيع المندوب.");
                    }
                }
                string id=J.Iqama(r.Required("rider"));var person=Store.Get(c,id,"rider",tx)??throw new ApiError("المندوب غير موجود.");FinancialScopes.DemandRider(actor,person);J.Need(ErpRiderClassification.ListedAsRider(person),"الموظفون خارج إطار المناديب.");
                J.Need((source["assignments"] as JsonArray??[]).OfType<JsonObject>().Any(x=>x.S("riderId")==id&&Overlaps(x,cycle.S("period"))),"المندوب ليس مشغلًا فعليًا لليوزر بالفترة؛ المالك المسجل ليس دليلًا.");
                J.Need(PaySettings.Categories.Contains(r.Required("category")),"فئة الكفالة والمركبة مطلوبة للمراجعة.");SignedMoney(r,"baseGross");Money(r,"deduction");decimal orders=PayValues.Number(r,"orders",0,int.MaxValue),days=PayValues.Number(r,"days",0,31);J.Need(orders==decimal.Truncate(orders)&&days==decimal.Truncate(days),"طلبات وأيام صحيحة مطلوبة.");
            }
            J.Need(shares.Sum(r=>r.N("orders"))==a.N("orders")&&shares.Sum(r=>r.N("baseGross"))==a.N("baseGross"),"طلبات واستحقاقات المشغلين لا تطابق اليوزر.");
            decimal deductions=Rows(cycle["invoices"]).OfType<JsonObject>().Where(i=>i.S("app")==app).SelectMany(i=>Rows(i["deductions"]).OfType<JsonObject>()).Where(d=>d.S("level")=="account_level"&&d.S("accountId")==a.S("accountId")).Sum(d=>d.N("amount"));J.Need(shares.Sum(r=>r.N("deduction"))==deductions,"خصم اليوزر يحتاج توزيعًا صريحًا على مشغليه؛ لا تحمل خصم الشركة عليهم.");
        }
    }
    static JsonObject Json(object value)=>JsonSerializer.SerializeToNode(value,new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
    public static List<JsonObject> Lines(DbConnection c,DbTransaction? tx,string id)=>Store.List(c,LineKind,tx).Where(l=>l.S("cycleId")==id).ToList();
    public static void Map(RouteGroupBuilder api,Store store)
    {
        MapReview(api,store);
        api.MapGet("/pay/validate-gaps",(HttpContext ctx)=>
        {
            string company=ctx.Request.Query["companyId"].ToString(),period=ctx.Request.Query["period"].ToString(),app=ctx.Request.Query["app"].ToString();
            J.Need(company!=""&&period!="","الشركة والشهر مطلوبان.");if(app=="")app="ninja";
            return store.Read(c=>DiagnoseValidateGaps(c,ctx.User,company,period,app));
        }).WithTags("Payroll").WithSummary("Counts-only ValidateRiders gap report (no identities)");
        api.MapGet("/pay/cycles/{id}/unrecovered-costs",(string id,HttpContext ctx)=>store.Read(c=>UnrecoveredCosts(c,ctx.User,id))).WithTags("Payroll").WithSummary("Read Keeta ineligibility operating and fuel costs borne by the company");
        api.MapGet("/pay/cycles/{id}/debt-deductions",(string id,HttpContext ctx)=>store.Read(c=>DebtDeductionsView(c,null,ctx.User,id))).WithTags("Payroll").WithSummary("Review due debts after salary approval, capped by positive net");
        api.MapPost("/pay/cycles/{id}/debt-deductions",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>DebtDeductionsAct(c,tx,ctx.User,id,input))).WithTags("Payroll").WithSummary("Choose zero or a due installment and independently confirm deductions");
        api.MapGet("/pay/cycles",(HttpContext ctx)=>{AccessPolicy.Demand(ctx.User,"pay.view");string period=ctx.Request.Query["period"].ToString();if(period!="")J.Period(period);return store.Read(c=>ApiPagination.Apply(Store.List(c,Kind).Where(d=>Visible(ctx.User,d)&&(period==""||d.S("period")==period)).Select(d=>View(c,null,ctx.User,d)),ctx.Request.Query,true));}).WithTags("Payroll").WithSummary("Read scoped monthly app cycles and stage history");
        api.MapPost("/pay/cycles",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>View(c,tx,ctx.User,Create(c,tx,ctx.User,input)))).WithTags("Payroll").WithSummary("Review approved invoices and classify company versus account deductions");
        api.MapPost("/pay/cycles/{id}/advance",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>View(c,tx,ctx.User,Advance(c,tx,ctx.User,id,input)))).WithTags("Payroll").WithSummary("Advance one gated stage or independently approve target payroll posting");
        api.MapGet("/pay/cycles/{id}/lines",(string id,HttpContext ctx)=>store.Read(c=>{var cycle=Store.Get(c,id,Kind)??throw new ApiError("الدورة غير موجودة.",404);Scope(ctx.User,cycle,"pay.view");string app=ctx.Request.Query["app"].ToString(),rider=ctx.Request.Query["rider"].ToString();return ApiPagination.Apply(Lines(c,null,id).Where(l=>(app==""||l.S("app")==app)&&(rider==""||l.S("rider")==rider)),ctx.Request.Query,true);})).WithTags("Payroll").WithSummary("Read scoped cycle calculations with formula evidence");
    }
}
