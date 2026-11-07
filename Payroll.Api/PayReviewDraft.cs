using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

// Read-only proposal. A saved D3 allocation wins; daily data never changes invoiced totals.
public static class PayReviewDraft
{
    static JsonArray Array(IEnumerable<JsonObject> rows)=>new(rows.Select(r=>(JsonNode?)r.Clone()).ToArray());
    public static JsonObject Build(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string app,string period,string companyId)
    {
        J.Need(PayPlatforms.App(app)=="ninja","المراجعة المقترحة متاحة لنينجا فقط.");period=J.Period(period);string company=FinancialCore.ResolveSponsor(companyId).Id;
        OperationsImports.Scope(actor,"ninja",company,"pay.view");
        var blockers=new JsonArray();var reviews=new JsonArray();var evidence=new JsonArray();var unassigned=new JsonArray();var missing=new JsonArray();
        void Block(string code,string message,string action,string source="")=>blockers.Add(new JsonObject{["code"]=code,["message"]=message,["fixAction"]=action,["sourceLineId"]=source});
        var invoices=Store.List(c,"platform_invoice",tx).Where(i=>i.S("platform")=="Ninja"&&i.S("companyId")==company&&i.S("period")==period&&i.S("status")!="reversed").OrderBy(i=>i.S("id"),StringComparer.Ordinal).ToList();
        if(invoices.Count==0)Block("invoice_missing","ارفع واعتمد فاتورة نينجا للشركة والشهر.","upload_invoice");
        foreach(string kind in new[]{"pay_terms","pay_eligibility"})try{PaySettings.Resolve(c,tx,kind,"ninja",period);}catch(ApiError){Block(kind+"_missing","إعداد نينجا للشهر غير معتمد: "+kind,"approve_settings");}
        try{PaySettings.Global(c,tx,period+"-01");}catch(ApiError){Block("global_settings_missing","اعتمد إعدادات الأيام والإيجار بتاريخ الشهر.","approve_settings");}
        var daily=Store.List(c,"ninja_daily",tx).Where(d=>d.S("companyId")==company&&d.S("date").StartsWith(period+"-",StringComparison.Ordinal)).ToList();
        var people=Store.List(c,"rider",tx).ToDictionary(r=>r.S("id"),StringComparer.Ordinal);
        foreach(var invoice in invoices)
        {
            string id=invoice.S("id");
            if(invoice.S("approvalStatus")!="approved"){Block("invoice_not_approved","اعتمد الفاتورة بمراجع مستقل أولًا.","approve_invoice",id);continue;}
            if(invoice.S("journalEntryId")!=""||invoice.S("reversalJournalEntryId")!=""){Block("invoice_already_posted","الفاتورة مرحّلة؛ راجع مسار العكس قبل إنشاء الدورة.","review_posting",id);continue;}
            try{NinjaInvoices.DemandResolvedDisputes(c,tx,id);}catch(ApiError){Block("open_dispute","احسم الاعتراض الرسمي على الفاتورة.","resolve_dispute",id);}
            var lines=NinjaInvoices.Lines(c,tx,id);var saved=Store.List(c,OperationsImports.Kind,tx).Where(o=>o.S("invoiceId")==id&&o.S("companyId")==company&&o.S("period")==period&&o.S("app")=="ninja").OrderByDescending(o=>o.S("createdAt"),StringComparer.Ordinal).ThenBy(o=>o.S("id"),StringComparer.Ordinal).FirstOrDefault();
            JsonObject review;
            if(saved!=null)
            {
                review=saved["review"]!.DeepClone().AsObject();
                try{PayCycles.ValidateImportedReview(c,tx,actor,company,period,review,"pay.view");}catch(ApiError e){Block("operations_review_invalid",e.Message,"import_operations",id);}
            }
            else
            {
                var accounts=new JsonArray();
                foreach(var line in lines.Where(l=>l.S("lineType")!="company"))
                {
                    var account=OperationsImports.Account(line);string accountId=account.S("accountId"),source=line.S("id");accounts.Add(account);
                    var local=Store.Get(c,accountId,"platform_account",tx);
                    if(local==null||local.S("companyId")!=company||PayPlatforms.For(local.S("platformCode")).App!="ninja"){Block("account_missing","اربط سطر الفاتورة بحساب نينجا للشركة.","link_account",source);continue;}
                    var rows=daily.Where(d=>d.S("platformAccountId")==accountId).ToList();
                    if(rows.Count==0){Block("operations_missing","استورد تشغيل الحساب وتوزيع المناديب الفعليين.","import_operations",source);continue;}
                    if(rows.Any(d=>d.S("date").Length<10)){Block("daily_date_invalid","تاريخ التشغيل غير صالح.","review_operations",source);continue;}
                    if(rows.GroupBy(d=>d.S("date")[..10]+":"+d.S("riderId")).Any(g=>g.Count()>1)){Block("daily_duplicate","توجد بيانات يومية مكررة؛ راجع مصدر التشغيل.","review_operations",source);continue;}
                    bool valid=true;
                    foreach(var row in rows)
                    {
                        string rider=row.S("riderId");bool assigned=(local["assignments"] as JsonArray??[]).OfType<JsonObject>().Any(a=>a.S("riderId")==rider&&Covers(a,row.S("date")));
                        if(!assigned){unassigned.Add(new JsonObject{["sourceLineId"]=source,["operationSourceId"]=row.S("id"),["orders"]=row.N("orders")});Block("unassigned_orders","اربط الحساب بمشغله الفعلي في تاريخ التشغيل.","link_actual_operator",source);valid=false;}
                        if(!people.TryGetValue(rider,out var person)){missing.Add(new JsonObject{["sourceLineId"]=source,["operationSourceId"]=row.S("id")});Block("rider_not_found","أضف المندوب المرتبط بمصدر التشغيل.","sync_riders",source);valid=false;}
                        else {FinancialScopes.DemandRider(actor,person);if(!ErpRiderClassification.ListedAsRider(person)||person.B("debtOnly")){Block("rider_ineligible","المصدر لا يخص مندوب تشغيل فعال.","review_rider",source);valid=false;}}
                        if(row.N("orders")<0||row.N("orders")!=decimal.Truncate(row.N("orders"))){Block("orders_invalid","عدد طلبات التشغيل غير صالح.","review_operations",source);valid=false;}
                    }
                    if(rows.Sum(d=>d.N("orders"))!=line.N("orders")){Block("orders_mismatch","طلبات التشغيل لا تطابق الفاتورة؛ استورد توزيعًا مراجعًا دون تغيير الفاتورة.","import_operations",source);valid=false;}
                    if(!valid)continue;
                    decimal accountDeduction=(line["deductions"] as JsonArray??[]).OfType<JsonObject>().Where(d=>d.S("level")=="account_level").Sum(d=>d.N("amount"));
                    // A multi-operator base and deduction need explicit D3 review; never infer them from ownership or time.
                    var groups=rows.GroupBy(d=>d.S("riderId")).ToList();
                    if(groups.Count!=1){Block("allocation_required","يوجد أكثر من مشغل؛ استورد توزيع الاستحقاق والخصم المراجع.","import_operations",source);continue;}
                    var group=groups.Single();string riderId=group.Key;var personRow=people[riderId];string category=personRow.S("payCategory");
                    if(!PaySettings.Categories.Contains(category)){Block("category_missing","حدد فئة الكفالة والمركبة في ملف التشغيل المراجع.","import_operations",source);continue;}
                    var riderDays=daily.Where(d=>d.S("riderId")==riderId).Select(d=>d.S("date")[..10]).Distinct().Count();
                    var riderShare=new JsonObject{["rider"]=riderId,["orders"]=line.N("orders"),["baseGross"]=line.N("baseGross"),["deduction"]=accountDeduction,["category"]=category,["days"]=riderDays,["metrics"]=new JsonObject()};
                    account["riders"]!.AsArray().Add(riderShare);
                }
                JsonArray deductions;
                try{deductions=OperationsImports.Deductions(lines);}catch(ApiError e){deductions=[];Block("classification_required",e.Message,"classify_invoice",id);}
                review=new(){["invoiceId"]=id,["app"]="ninja",["accounts"]=accounts,["deductions"]=deductions};
            }
            foreach(var account in review["accounts"]!.AsArray().OfType<JsonObject>())
            {
                foreach(var rider in account["riders"]!.AsArray().OfType<JsonObject>())
                {
                    try
                    {
                        var assignment=PayAssignments.Resolve(c,tx,rider.S("rider"),"ninja",period);var scheme=Store.Get(c,assignment.S("schemeId"),"pay_scheme",tx)!;var metrics=rider["metrics"] as JsonObject??new();
                        string rulesId=scheme["data"].S("eligibilitySetId");var rules=rulesId==""?null:Store.Get(c,rulesId,"pay_eligibility",tx);
                        foreach(var rule in (rules?["data"]?["rules"] as JsonArray??[]).OfType<JsonObject>().Where(r=>!r.ContainsKey("included")||r.B("included")))
                            if(rule.S("metric") is not ("orders" or "days" or "gross" or "incentives")&&!metrics.ContainsKey(rule.S("metric")))Block("metric_missing","أكمل مقياس الأهلية من مصدر التشغيل: "+rule.S("metric"),"import_operations",account.S("sourceLineId"));
                    }
                    catch(ApiError e){Block("scheme_assignment_missing",e.Message,"import_scheme_choices",account.S("sourceLineId"));}
                }
                evidence.Add(new JsonObject{["invoiceId"]=id,["sourceLineId"]=account.S("sourceLineId"),["fields"]=new JsonArray("baseGross","incentives","orders","riders[].baseGross","riders[].orders","riders[].deduction"),["operationsImportId"]=saved?.S("id"),["operationSourceIds"]=new JsonArray(daily.Where(d=>d.S("platformAccountId")==account.S("accountId")).Select(d=>(JsonNode?)JsonValue.Create(d.S("id"))).ToArray())});
            }
            reviews.Add(review);
            if(blockers.Count==0)try{PayCycles.ValidateImportedReview(c,tx,actor,company,period,review,"pay.view");}catch(ApiError e){Block("review_invalid",e.Message,"import_operations",id);}
        }
        if(Store.List(c,PayCycles.Kind,tx).Any(p=>p.S("companyId")==company&&p.S("period")==period&&p.S("status") is not ("cancelled" or "reversed")))Block("cycle_exists","توجد دورة للشركة والشهر؛ أكملها أو ألغ المسودة قبل الإعادة.","open_cycle");
        return new(){["app"]="ninja",["period"]=period,["companyId"]=company,["invoices"]=reviews,["blockers"]=blockers,["unassignedOrders"]=unassigned,["ridersNotFound"]=missing,["evidence"]=evidence,["canCreate"]=blockers.Count==0&&reviews.Count>0,["totals"]=new JsonObject{["gross"]=invoices.Sum(i=>i.N("amountExclVat")),["deductions"]=invoices.Sum(i=>i.N("deductionsTotal")),["vat"]=invoices.Sum(i=>i.N("vat"))}};
    }
    static bool Covers(JsonObject assignment,string date)
    {
        if(date.Length<10||!DateOnly.TryParse(date[..10],CultureInfo.InvariantCulture,out var day)||!DateTimeOffset.TryParse(assignment.S("RawEffectiveFrom"),CultureInfo.InvariantCulture,DateTimeStyles.None,out var from))return false;
        DateOnly start=DateOnly.FromDateTime(from.ToOffset(TimeSpan.FromHours(3)).DateTime);if(start>day)return false;
        return assignment.S("RawEffectiveTo")==""||DateTimeOffset.TryParse(assignment.S("RawEffectiveTo"),CultureInfo.InvariantCulture,DateTimeStyles.None,out var end)&&DateOnly.FromDateTime(end.ToOffset(TimeSpan.FromHours(3)).DateTime)>=day;
    }
    public static void Map(RouteGroupBuilder api,Store store)=>api.MapGet("/pay/review-draft",(string app,string period,string companyId,HttpContext ctx)=>store.Read(c=>Build(c,null,ctx.User,app,period,companyId))).WithTags("Payroll").WithSummary("Read source-bound Ninja cycle input and actionable blockers without writes");
}
