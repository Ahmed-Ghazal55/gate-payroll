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
        app=PayPlatforms.App(app);
        J.Need(app is "ninja" or "keeta","المراجعة المقترحة متاحة لنينجا وكيتا.");
        period=J.Period(period);string company=FinancialCore.ResolveSponsor(companyId).Id;
        OperationsImports.Scope(actor,app,company,"pay.view");
        string platformName=PayPlatforms.For(app).Platform;
        var blockers=new JsonArray();var warnings=new JsonArray();var proposals=new JsonArray();var reviews=new JsonArray();var evidence=new JsonArray();var unassigned=new JsonArray();var missing=new JsonArray();var accountantLines=new JsonArray();var setupChecklist=new JsonArray();
        // One account produces one blocker per code, not one per operating day.
        void Block(string code,string message,string action,string source=""){if(!blockers.OfType<JsonObject>().Any(b=>b.S("code")==code&&b.S("sourceLineId")==source))blockers.Add(new JsonObject{["code"]=code,["message"]=message,["fixAction"]=action,["sourceLineId"]=source});}
        void Warn(string code,string message,string source,decimal? erp=null,decimal? invoiced=null)=>warnings.Add(new JsonObject{["code"]=code,["message"]=message,["sourceLineId"]=source,["erpOrders"]=erp,["invoiceOrders"]=invoiced});
        void Accountant(string kind,string message,string source,decimal? erp=null,decimal? invoiced=null)=>accountantLines.Add(new JsonObject{["kind"]=kind,["message"]=message,["sourceLineId"]=source,["erpOrders"]=erp,["invoiceOrders"]=invoiced,["decisionDefault"]=kind=="orders_variance"?"accept_invoice":"hold_for_accountant"});
        void Setup(string code,string message,string action,string source=""){if(!setupChecklist.OfType<JsonObject>().Any(b=>b.S("code")==code&&b.S("sourceLineId")==source))setupChecklist.Add(new JsonObject{["code"]=code,["message"]=message,["fixAction"]=action,["sourceLineId"]=source});}
        var invoices=Store.List(c,"platform_invoice",tx).Where(i=>InvoiceDiscard.Active(i)&&i.S("platform")==platformName&&i.S("companyId")==company&&i.S("period")==period&&i.S("status")!="reversed"&&i.S("accountingStatus")!="reversed").OrderBy(i=>i.S("id"),StringComparer.Ordinal).ToList();
        if(invoices.Count==0)Block("invoice_missing","ارفع واعتمد فاتورة "+(app=="keeta"?"كيتا":"نينجا")+" للشركة والشهر.","upload_invoice");
        // Parent terms (الشروط الأصلية) are display-only and never block calculation (Ghazal 2026-11-27).
        // Eligibility gaps → setup checklist (Ninja) or approve_settings (Keeta).
        try{PaySettings.Resolve(c,tx,"pay_eligibility",app,period);}
        catch(ApiError)
        {
            string msg="إعداد "+(app=="keeta"?"كيتا":"نينجا")+" للشهر غير معتمد: قواعد الاستحقاق";
            if(app=="ninja"){Setup("pay_eligibility_missing",msg,"apply_default_settings");Block("pay_eligibility_missing",msg,"apply_default_settings");}
            else Block("pay_eligibility_missing",msg,"approve_settings");
        }
        try{PaySettings.Global(c,tx,period+"-01");}
        catch(ApiError)
        {
            if(app=="ninja"){Setup("global_settings_missing","اعتمد إعدادات الأيام والإيجار بتاريخ الشهر.","apply_default_settings");Block("global_settings_missing","اعتمد إعدادات الأيام والإيجار بتاريخ الشهر.","apply_default_settings");}
            else Block("global_settings_missing","اعتمد إعدادات الأيام والإيجار بتاريخ الشهر.","approve_settings");
        }
        // Captured cycle → freeze. Pre-cycle: liveOps when live mode, else store (B3). Ninja uses daily ERP; Keeta uses register ops import only.
        var openCycle=Store.List(c,PayCycles.Kind,tx).FirstOrDefault(p=>p.S("companyId")==company&&p.S("period")==period&&p.S("status") is not ("cancelled" or "reversed")&&(p.S("app")==app||(p["invoices"] as JsonArray??[]).OfType<JsonObject>().Any(i=>i.S("app")==app))&&PayRunInputs.Captured(p));
        List<JsonObject> DailySource()=>app!="ninja"?[]:(openCycle!=null?PayRunInputs.List(c,tx,openCycle.S("id"),"ninja_daily"):(liveOps!=null?liveOps("ninja_daily").ToList():Store.List(c,"ninja_daily",tx)));

        var daily=DailySource().Where(d=>d.S("companyId")==company&&d.S("date").StartsWith(period+"-",StringComparison.Ordinal)).ToList();
        var people=Store.List(c,"rider",tx).ToDictionary(r=>r.S("id"),StringComparer.Ordinal);
        IEnumerable<JsonObject> InvoiceLines(string invoiceId)=>app=="keeta"?KeetaInvoices.Workflow.Lines(c,tx,invoiceId):NinjaInvoices.Lines(c,tx,invoiceId);
        var invoicedAccounts=invoices.SelectMany(i=>InvoiceLines(i.S("id"))).Select(l=>l.S("platformAccountId")).Where(id=>id!="").ToHashSet(StringComparer.Ordinal);
        if(app=="ninja")
        {
            foreach(var row in daily.Where(d=>!invoicedAccounts.Contains(d.S("platformAccountId"))))
            {
                unassigned.Add(new JsonObject{["sourceLineId"]=row.S("id"),["operationSourceId"]=row.S("id"),["orders"]=row.N("orders")});
                Block("daily_account_not_invoiced","يوجد تشغيل حساب بلا سطر فاتورة مطابق؛ راجع الحساب ومصدر الفاتورة.","review_invoice_account",row.S("platformAccountId"));
                if(!people.ContainsKey(row.S("riderId")))missing.Add(new JsonObject{["sourceLineId"]=row.S("id"),["operationSourceId"]=row.S("id")});
            }
        }
        foreach(var invoice in invoices)
        {
            string id=invoice.S("id");
            if(invoice.S("approvalStatus")!="approved"){Block("invoice_not_approved","اعتمد الفاتورة بمراجع مستقل أولًا.","approve_invoice",id);continue;}
            if(invoice.S("journalEntryId")!=""||invoice.S("reversalJournalEntryId")!=""){Block("invoice_already_posted","الفاتورة مرحّلة؛ راجع مسار العكس قبل إنشاء الدورة.","review_posting",id);continue;}
            try
            {
                if(app=="keeta")KeetaInvoices.Workflow.DemandResolvedDisputes(c,tx,id);
                else NinjaInvoices.DemandResolvedDisputes(c,tx,id);
            }
            catch(ApiError){Block("open_dispute","احسم الاعتراض الرسمي على الفاتورة.","resolve_dispute",id);}
            var lines=InvoiceLines(id).ToList();
            var saved=Store.List(c,OperationsImports.Kind,tx).Where(o=>o.S("invoiceId")==id&&o.S("companyId")==company&&o.S("period")==period&&o.S("app")==app).OrderByDescending(o=>o.S("createdAt"),StringComparer.Ordinal).ThenBy(o=>o.S("id"),StringComparer.Ordinal).FirstOrDefault();
            JsonObject review;
            if(saved!=null)
            {
                review=saved["review"]!.DeepClone().AsObject();
                try{PayCycles.ValidateImportedReview(c,tx,actor,company,period,review,"pay.view");}catch(ApiError e){Block("operations_review_invalid",e.Message,"import_operations",id);}
            }
            else if(app=="keeta")
            {
                // Keeta operations source is the register sheet import (expected eligibility stays expected-only).
                Block("operations_missing","استورد تشغيل كيتا من ورقة السجل قبل إنشاء الدورة.","import_operations",id);
                review=new(){["invoiceId"]=id,["app"]="keeta",["accounts"]=new JsonArray(),["deductions"]=new JsonArray()};
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
                    if(rows.Count==0)
                    {
                        // Brief I / NFLOW1d: no daily ops → accountant line only. Never auto-pay a sole assignee.
                        var assigned=(local["assignments"] as JsonArray??[]).OfType<JsonObject>().Where(a=>a.S("riderId")!=""&&AssignmentSpan.Overlaps(a,period)).Select(a=>a.S("riderId")).Distinct(StringComparer.Ordinal).ToList();
                        accountantLines.Add(new JsonObject
                        {
                            ["kind"]="no_ops_account",
                            ["message"]="حساب بلا بيانات تشغيل؛ سطر منفصل للمحاسب ولا يوقف بقية الحسابات.",
                            ["sourceLineId"]=source,
                            ["erpOrders"]=0,
                            ["invoiceOrders"]=line.N("orders"),
                            ["assigneeCount"]=assigned.Count,
                            ["decisionDefault"]="hold_for_accountant"
                        });
                        continue;
                    }
                    if(rows.Any(d=>d.S("date").Length<10)){Block("daily_date_invalid","تاريخ التشغيل غير صالح.","review_operations",source);continue;}
                    if(rows.GroupBy(d=>d.S("date")[..10]+":"+d.S("riderId")).Any(g=>g.Count()>1)){Block("daily_duplicate","توجد بيانات يومية مكررة؛ راجع مصدر التشغيل.","review_operations",source);continue;}
                    bool valid=true;
                    foreach(var row in rows)
                    {
                        string rider=row.S("riderId");bool assigned=(local["assignments"] as JsonArray??[]).OfType<JsonObject>().Any(a=>a.S("riderId")==rider&&Covers(a,row.S("date")));
                        if(!assigned){unassigned.Add(new JsonObject{["sourceLineId"]=source,["operationSourceId"]=row.S("id"),["orders"]=row.N("orders")});Block("unassigned_orders","اربط الحساب بمشغله الفعلي في تاريخ التشغيل.","link_actual_operator",source);valid=false;}
                        if(!people.TryGetValue(rider,out var person)){missing.Add(new JsonObject{["sourceLineId"]=source,["operationSourceId"]=row.S("id")});Block("rider_not_found","أضف المندوب المرتبط بمصدر التشغيل.","review_rider_record",source);valid=false;}
                        else {FinancialScopes.DemandRider(actor,person);if(!ErpRiderClassification.ListedAsRider(person)||person.B("debtOnly")){Block("rider_ineligible","المصدر لا يخص مندوب تشغيل فعال.","review_rider",source);valid=false;}}
                        if(row.N("orders")<0||row.N("orders")!=decimal.Truncate(row.N("orders"))){Block("orders_invalid","عدد طلبات التشغيل غير صالح.","review_operations",source);valid=false;}
                    }
                    if(!valid)continue;
                    decimal erpOrders=rows.Sum(d=>d.N("orders"));
                    if(rows.Count>0&&erpOrders!=line.N("orders"))
                    {
                        Warn("orders_variance",erpOrders>line.N("orders")?"طلبات ERP أعلى من الفاتورة؛ راجع المصدر. الفاتورة هي المعتمدة.":"طلبات ERP أقل من الفاتورة؛ الفاتورة هي المعتمدة.",source,erpOrders,line.N("orders"));
                        Accountant("orders_variance","فرق طلبات الفاتورة مقابل التشغيل؛ قبول الفاتورة افتراضيًا أو تصنيف للمحاسب — دون تغيير الفاتورة.",source,erpOrders,line.N("orders"));
                    }
                    decimal accountDeduction=(line["deductions"] as JsonArray??[]).OfType<JsonObject>().Where(d=>d.S("level")=="account_level").Sum(d=>d.N("amount"));
                    // A multi-operator split is proposed from ERP's share but never accepted without a reviewer (§40.2).
                    var groups=rows.GroupBy(d=>d.S("riderId")).ToList();
                    if(groups.Count>1)
                    {
                        // Fixed default (orders→days); parent terms never change the split weight (brief D).
                        var (weight,tie)=PayReviewCategory.AllocationSetting(c,tx,"ninja",period);
                        var metrics=groups.Select(g=>new PayReviewCategory.OperatorMetrics(g.Key,g.Sum(d=>d.N("orders")),g.Select(d=>d.S("date")[..10]).Distinct().Count(),1m,0m)).OrderBy(x=>x.Rider,StringComparer.Ordinal).ToList();
                        proposals.Add(new JsonObject{["sourceLineId"]=source,["accountId"]=accountId,["weight"]=weight,["tieBreak"]=tie,["riders"]=PayReviewCategory.Propose(metrics,line.N("orders"),line.N("baseGross"),accountDeduction,weight,tie)});
                        Block("allocation_required","يوجد أكثر من مشغل؛ راجع التوزيع المقترح ثم استورده.","import_operations",source);continue;
                    }
                    string riderId=groups.Single().Key;
                    if(!people.TryGetValue(riderId,out var personRow)){missing.Add(new JsonObject{["sourceLineId"]=source});Block("rider_not_found","أضف المندوب المرتبط بمصدر التشغيل.","review_rider_record",source);continue;}
                    FinancialScopes.DemandRider(actor,personRow);if(!ErpRiderClassification.ListedAsRider(personRow)||personRow.B("debtOnly")){Block("rider_ineligible","المصدر لا يخص مندوب تشغيل فعال.","review_rider",source);continue;}
                    // Prefer stored platform_account.vehicleType / invoice vehicleType; نوع المركبة before المركبة (rider text).
                    string platformVehicle=local.S("vehicleType");
                    if(platformVehicle=="")platformVehicle=line.S("vehicleType");
                    if(platformVehicle==""&&line["sourceMetrics"] is JsonObject sm)
                        platformVehicle=sm.S("نوع المركبة",sm.S("vehicleType",sm.S("المركبة")));
                    string category=PayReviewCategory.Derive(c,tx,personRow,period,liveOps,platformVehicle);
                    if(!PaySettings.AllCategories.Contains(category)){Block("category_missing","تعذر تحديد فئة الكفالة والمركبة؛ حددها في ملف التشغيل المراجع.","import_operations",source);continue;}
                    var riderDays=daily.Where(d=>d.S("riderId")==riderId).Select(d=>d.S("date")[..10]).Distinct().Count();
                    var riderShare=new JsonObject{["rider"]=riderId,["orders"]=line.N("orders"),["baseGross"]=line.N("baseGross"),["deduction"]=accountDeduction,["category"]=category,["days"]=riderDays,["metrics"]=new JsonObject(),["platformVehicleType"]=platformVehicle,["accountVehicleType"]=platformVehicle};
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
                    // Keeta expected eligibility stays expected-only (never treated as final).
                    if(app=="keeta")
                    {
                        var metrics=rider["metrics"] as JsonObject??new();
                        if(metrics.S("expectedApproval")=="")metrics["expectedApproval"]="expected_only";
                        metrics["expectedFinal"]=false;
                        rider["metrics"]=metrics;
                    }
                    try
                    {
                        var assignment=PayAssignments.Resolve(c,tx,rider.S("rider"),app,period);var scheme=Store.Get(c,assignment.S("schemeId"),"pay_scheme",tx)!;var metrics=rider["metrics"] as JsonObject??new();
                        string rulesId=scheme["data"].S("eligibilitySetId");var rules=rulesId==""?null:Store.Get(c,rulesId,"pay_eligibility",tx);
                        foreach(var rule in (rules?["data"]?["rules"] as JsonArray??[]).OfType<JsonObject>().Where(r=>!r.ContainsKey("included")||r.B("included")))
                            if(rule.S("metric") is not ("orders" or "days" or "gross" or "incentives")&&!metrics.ContainsKey(rule.S("metric")))Block("metric_missing","أكمل مقياس الأهلية من مصدر التشغيل: "+rule.S("metric"),"import_operations",account.S("sourceLineId"));
                    }
                    catch(ApiError e){Setup("scheme_assignment_missing",e.Message,"import_scheme_choices",account.S("sourceLineId"));Block("scheme_assignment_missing",e.Message,"import_scheme_choices",account.S("sourceLineId"));}
                }
                evidence.Add(new JsonObject{["invoiceId"]=id,["sourceLineId"]=account.S("sourceLineId"),["fields"]=new JsonArray("baseGross","incentives","orders","riders[].baseGross","riders[].orders","riders[].deduction"),["operationsImportId"]=saved?.S("id"),["operationSourceIds"]=new JsonArray(daily.Where(d=>d.S("platformAccountId")==account.S("accountId")).Select(d=>(JsonNode?)JsonValue.Create(d.S("id"))).ToArray())});
            }
            reviews.Add(review);
            if(blockers.Count==0)try{PayCycles.ValidateImportedReview(c,tx,actor,company,period,review,"pay.view");}catch(ApiError e){Block("review_invalid",e.Message,"import_operations",id);}
        }
        if(Store.List(c,PayCycles.Kind,tx).Any(p=>p.S("companyId")==company&&p.S("period")==period&&p.S("status") is not ("cancelled" or "reversed")))Block("cycle_exists","توجد دورة للشركة والشهر؛ أكملها أو ألغ المسودة قبل الإعادة.","open_cycle");
        string dataThrough=daily.Select(d=>d.S("date")).Where(d=>d.Length>=10).Select(d=>d[..10]).DefaultIfEmpty("").Max(StringComparer.Ordinal)!;
        var allowed=new JsonArray("refresh");
        if(app=="ninja")allowed.Add("split_dues");
        if(setupChecklist.OfType<JsonObject>().Any(s=>s.S("fixAction")=="apply_default_settings"))allowed.Add("apply_default_settings");
        return new(){["app"]=app,["period"]=period,["companyId"]=company,["invoices"]=reviews,["blockers"]=blockers,["warnings"]=warnings,["accountantLines"]=accountantLines,["setupChecklist"]=setupChecklist,["allocationProposals"]=proposals,["dataThrough"]=dataThrough,["unassignedOrders"]=unassigned,["ridersNotFound"]=missing,["evidence"]=evidence,["canCreate"]=blockers.Count==0&&reviews.Count>0,["allowedActions"]=allowed,["totals"]=new JsonObject{["gross"]=invoices.Sum(i=>i.N("amountExclVat")),["deductions"]=invoices.Sum(i=>i.N("deductionsTotal")),["vat"]=invoices.Sum(i=>i.N("vat"))}};

    }
    static bool Covers(JsonObject assignment,string date)=>AssignmentSpan.Covers(assignment,date);

    /// <summary>Pre-cycle live reader: known kinds only. Unknown kind = error (never silent empty).</summary>
    public static Func<string,IEnumerable<JsonObject>> KindAwareLiveOps(IReadOnlyList<JsonObject> daily,IReadOnlyList<JsonObject> assignments,IReadOnlyList<JsonObject> vehicles)
        =>kind=>kind switch
        {
            "ninja_daily"=>daily,
            "vehicle_assignment"=>assignments,
            "vehicle"=>vehicles,
            _=>throw new ApiError("مصدر تشغيل غير متوقع: "+kind,503)
        };

    public static void Map(RouteGroupBuilder api,Store store,IConfiguration? config=null,IHostEnvironment? env=null)=>api.MapGet("/pay/review-draft",(string app,string period,string companyId,HttpContext ctx)=>
    {
        string resolved=PayPlatforms.App(app);
        if(config!=null&&env!=null&&Operations.SourceMode(config,env)==Operations.SourceLive&&resolved=="ninja")
        {
            var daily=PayCycles.StagePreCycleKind(ctx,store,config,"ninja_daily",companyId,period,app);
            var assignments=PayCycles.StagePreCycleKind(ctx,store,config,"vehicle_assignment",companyId,period,app);
            var vehicleIds=assignments.Select(a=>a.S("vehicleId")).Where(id=>id!="").ToHashSet(StringComparer.Ordinal);
            var allVehicles=vehicleIds.Count==0?[]:PayCycles.StagePreCycleKind(ctx,store,config,"vehicle",companyId,period,app);
            var vehicles=allVehicles.Where(v=>vehicleIds.Contains(v.S("id"))).ToList();
            var live=KindAwareLiveOps(daily,assignments,vehicles);
            return store.Read(c=>Build(c,null,ctx.User,app,period,companyId,live));
        }
        return store.Read(c=>Build(c,null,ctx.User,app,period,companyId));
    }).WithTags("Payroll").WithSummary("Read source-bound Ninja or Keeta cycle input and actionable blockers without writes");
}
