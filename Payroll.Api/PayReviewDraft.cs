using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
namespace Payroll;

// Read-only proposal. A saved D3 allocation wins; daily data never changes invoiced totals.
public static class PayReviewDraft
{
    static JsonArray Array(IEnumerable<JsonObject> rows)=>new(rows.Select(r=>(JsonNode?)r.Clone()).ToArray());
    public static JsonObject Build(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string app,string period,string companyId,Func<string,IEnumerable<JsonObject>>? liveOps=null)
    {
        J.Need(PayPlatforms.App(app)=="ninja","المراجعة المقترحة متاحة لنينجا فقط.");period=J.Period(period);string company=FinancialCore.ResolveSponsor(companyId).Id;
        OperationsImports.Scope(actor,"ninja",company,"pay.view");
        var blockers=new JsonArray();var warnings=new JsonArray();var proposals=new JsonArray();var reviews=new JsonArray();var evidence=new JsonArray();var unassigned=new JsonArray();var missing=new JsonArray();
        // One account produces one blocker per code, not one per operating day.
        void Block(string code,string message,string action,string source=""){if(!blockers.OfType<JsonObject>().Any(b=>b.S("code")==code&&b.S("sourceLineId")==source))blockers.Add(new JsonObject{["code"]=code,["message"]=message,["fixAction"]=action,["sourceLineId"]=source});}
        void Warn(string code,string message,string source,decimal? erp=null,decimal? invoiced=null)=>warnings.Add(new JsonObject{["code"]=code,["message"]=message,["sourceLineId"]=source,["erpOrders"]=erp,["invoiceOrders"]=invoiced});
        var invoices=Store.List(c,"platform_invoice",tx).Where(i=>InvoiceDiscard.Active(i)&&i.S("platform")=="Ninja"&&i.S("companyId")==company&&i.S("period")==period&&i.S("status")!="reversed"&&i.S("accountingStatus")!="reversed").OrderBy(i=>i.S("id"),StringComparer.Ordinal).ToList();
        if(invoices.Count==0)Block("invoice_missing","ارفع واعتمد فاتورة نينجا للشركة والشهر.","upload_invoice");
        foreach(string kind in new[]{"pay_terms","pay_eligibility"})try{PaySettings.Resolve(c,tx,kind,"ninja",period);}catch(ApiError){Block(kind+"_missing","إعداد نينجا للشهر غير معتمد: "+kind,"approve_settings");}
        try{PaySettings.Global(c,tx,period+"-01");}catch(ApiError){Block("global_settings_missing","اعتمد إعدادات الأيام والإيجار بتاريخ الشهر.","approve_settings");}
        // Captured cycle → freeze. Pre-cycle: liveOps when live mode, else store (B3).
        var openCycle=Store.List(c,PayCycles.Kind,tx).FirstOrDefault(p=>p.S("companyId")==company&&p.S("period")==period&&p.S("status") is not ("cancelled" or "reversed")&&(p.S("app")=="ninja"||(p["invoices"] as JsonArray??[]).OfType<JsonObject>().Any(i=>i.S("app")=="ninja"))&&PayRunInputs.Captured(p));
        List<JsonObject> DailySource()=>openCycle!=null?PayRunInputs.List(c,tx,openCycle.S("id"),"ninja_daily"):(liveOps!=null?liveOps("ninja_daily").ToList():Store.List(c,"ninja_daily",tx));
        var daily=DailySource().Where(d=>d.S("companyId")==company&&d.S("date").StartsWith(period+"-",StringComparison.Ordinal)).ToList();
        var people=Store.List(c,"rider",tx).ToDictionary(r=>r.S("id"),StringComparer.Ordinal);
        var invoicedAccounts=invoices.SelectMany(i=>NinjaInvoices.Lines(c,tx,i.S("id"))).Select(l=>l.S("platformAccountId")).Where(id=>id!="").ToHashSet(StringComparer.Ordinal);
        foreach(var row in daily.Where(d=>!invoicedAccounts.Contains(d.S("platformAccountId"))))
        {
            unassigned.Add(new JsonObject{["sourceLineId"]=row.S("id"),["operationSourceId"]=row.S("id"),["orders"]=row.N("orders")});
            Block("daily_account_not_invoiced","يوجد تشغيل حساب بلا سطر فاتورة مطابق؛ راجع الحساب ومصدر الفاتورة.","review_invoice_account",row.S("platformAccountId"));
            if(!people.ContainsKey(row.S("riderId")))missing.Add(new JsonObject{["sourceLineId"]=row.S("id"),["operationSourceId"]=row.S("id")});
        }
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
                    var local=openCycle!=null?PayRunInputs.Get(c,tx,openCycle.S("id"),"platform_account",accountId):Store.Get(c,accountId,"platform_account",tx);
                    if(local==null||local.S("companyId")!=company||PayPlatforms.For(local.S("platformCode")).App!="ninja"){Block("account_missing","اربط سطر الفاتورة بحساب نينجا للشركة.","link_account",source);continue;}
                    var rows=daily.Where(d=>d.S("platformAccountId")==accountId).ToList();
                    string? onlyAssigned=null;
                    if(rows.Count==0)
                    {
                        // The invoice is final (BR31); operations only say who worked. One operator on the assignment history is enough, shown as a warning.
                        var assigned=(local["assignments"] as JsonArray??[]).OfType<JsonObject>().Where(a=>a.S("riderId")!=""&&AssignmentSpan.Overlaps(a,period)).Select(a=>a.S("riderId")).Distinct(StringComparer.Ordinal).ToList();
                        if(assigned.Count!=1){Block("operations_missing","لا يوجد تشغيل لهذا الحساب ولا مشغل واحد في التكليفات؛ استورد توزيع التشغيل.","import_operations",source);continue;}
                        onlyAssigned=assigned[0];Warn("operations_missing_using_assignment","لا يوجد تشغيل في ERP لهذا الحساب؛ استُخدم مشغل التكليف الوحيد وطلبات الفاتورة.",source,0,line.N("orders"));
                    }
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
                    if(!valid)continue;
                    decimal erpOrders=rows.Sum(d=>d.N("orders"));
                    if(rows.Count>0&&erpOrders!=line.N("orders"))Warn("orders_variance",erpOrders>line.N("orders")?"طلبات ERP أعلى من الفاتورة؛ راجع المصدر. الفاتورة هي المعتمدة.":"طلبات ERP أقل من الفاتورة؛ الفاتورة هي المعتمدة.",source,erpOrders,line.N("orders"));
                    decimal accountDeduction=(line["deductions"] as JsonArray??[]).OfType<JsonObject>().Where(d=>d.S("level")=="account_level").Sum(d=>d.N("amount"));
                    // A multi-operator split is proposed from ERP's share but never accepted without a reviewer.
                    var groups=rows.GroupBy(d=>d.S("riderId")).ToList();
                    if(groups.Count>1)
                    {
                        proposals.Add(new JsonObject{["sourceLineId"]=source,["accountId"]=accountId,["riders"]=PayReviewCategory.Propose(groups.Select(g=>(g.Key,g.Sum(d=>d.N("orders")))).OrderBy(x=>x.Key,StringComparer.Ordinal).ToList(),line.N("orders"),line.N("baseGross"),accountDeduction)});
                        Block("allocation_required","يوجد أكثر من مشغل؛ راجع التوزيع المقترح ثم استورده.","import_operations",source);continue;
                    }
                    string riderId=onlyAssigned??groups.Single().Key;
                    if(!people.TryGetValue(riderId,out var personRow)){missing.Add(new JsonObject{["sourceLineId"]=source});Block("rider_not_found","أضف المندوب المرتبط بمصدر التشغيل.","sync_riders",source);continue;}
                    FinancialScopes.DemandRider(actor,personRow);if(!ErpRiderClassification.ListedAsRider(personRow)||personRow.B("debtOnly")){Block("rider_ineligible","المصدر لا يخص مندوب تشغيل فعال.","review_rider",source);continue;}
                    string category=PayReviewCategory.Derive(c,tx,personRow,period);
                    if(!PaySettings.Categories.Contains(category)){Block("category_missing","تعذر تحديد فئة الكفالة والمركبة؛ حددها في ملف التشغيل المراجع.","import_operations",source);continue;}
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
                    rider["sourceLineId"]=account.S("sourceLineId");
                    rider["operationSourceIds"]=saved!=null?new JsonArray(saved.S("id")):new JsonArray(daily.Where(d=>d.S("platformAccountId")==account.S("accountId")&&d.S("riderId")==rider.S("rider")).Select(d=>(JsonNode?)JsonValue.Create(d.S("id"))).ToArray());
                    if(rider["operationSourceIds"]!.AsArray().Count==0)rider.Remove("operationSourceIds"); // Assignment-only operator: no daily proof exists.
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
        string dataThrough=daily.Select(d=>d.S("date")).Where(d=>d.Length>=10).Select(d=>d[..10]).DefaultIfEmpty("").Max(StringComparer.Ordinal)!;
        return new(){["app"]="ninja",["period"]=period,["companyId"]=company,["invoices"]=reviews,["blockers"]=blockers,["warnings"]=warnings,["allocationProposals"]=proposals,["dataThrough"]=dataThrough,["unassignedOrders"]=unassigned,["ridersNotFound"]=missing,["evidence"]=evidence,["canCreate"]=blockers.Count==0&&reviews.Count>0,["totals"]=new JsonObject{["gross"]=invoices.Sum(i=>i.N("amountExclVat")),["deductions"]=invoices.Sum(i=>i.N("deductionsTotal")),["vat"]=invoices.Sum(i=>i.N("vat"))}};
    }
    static bool Covers(JsonObject assignment,string date)=>AssignmentSpan.Covers(assignment,date);
    public static void Map(RouteGroupBuilder api,Store store,IConfiguration? config=null,IHostEnvironment? env=null)=>api.MapGet("/pay/review-draft",(string app,string period,string companyId,HttpContext ctx)=>
    {
        if(config!=null&&env!=null&&Operations.SourceMode(config,env)==Operations.SourceLive)
        {
            var staged=PayCycles.StagePreCycleKind(ctx,store,config,"ninja_daily",companyId,period,app);
            return store.Read(c=>Build(c,null,ctx.User,app,period,companyId,_=>staged));
        }
        return store.Read(c=>Build(c,null,ctx.User,app,period,companyId));
    }).WithTags("Payroll").WithSummary("Read source-bound Ninja cycle input and actionable blockers without writes");
}
