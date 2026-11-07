using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
namespace Payroll;

// BR31: differences never become disputes automatically, and never gate approval/posting.
public static partial class NinjaInvoices
{
    const string PreviewKind="ninja-invoice";
    static string Hash(string value)=>StorageTransfer.CutoverHash(JsonValue.Create(value));
    static void Scope(ClaimsPrincipal actor,string permission,string company)
    {AccessPolicy.Demand(actor,permission);FinancialScopes.DemandSponsor(actor,company);if(!FinancialScopes.Platform(actor,"Ninja"))throw new ApiError("المنصة خارج النطاق المالي.",403);}
    public static JsonObject Get(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id,string permission="pay.view")
    {var invoice=Store.Get(c,id,"platform_invoice",tx)??throw new ApiError("الفاتورة غير موجودة.",404);if(invoice.S("platform")!="Ninja")throw new ApiError("الفاتورة غير موجودة.",404);Scope(actor,permission,invoice.S("companyId"));return invoice;}
    public static List<JsonObject> Lines(DbConnection c,DbTransaction? tx,string id)=>Store.List(c,"platform_invoice_line",tx).Where(l=>l.S("invoiceId")==id).OrderBy(l=>l.N("sourceRow")).ToList();
    static void Open(DbConnection c,DbTransaction? tx,JsonObject invoice)=>J.Need(!Store.List(c,"accounting_period",tx).Any(p=>p.S("companyId")==invoice.S("companyId")&&p.S("period")==invoice.S("period")&&p.S("status")=="closed"),"الفترة المحاسبية مغلقة.");
    static void Independent(ClaimsPrincipal actor,string maker)=>J.Need(!string.Equals(actor.Identity!.Name,maker,StringComparison.OrdinalIgnoreCase),"يلزم مراجع مستقل.");
    static void ValidateNumbers(List<Imports.Sheet> sheets)
    {
        var sheet=sheets.First(s=>s.Rows.Count>0&&s.Rows[0].Any(h=>h.Trim().Equals("captain_id",StringComparison.OrdinalIgnoreCase)));
        var header=sheet.Rows[0].Select(h=>h.Replace("\n"," ").Trim()).ToList();
        J.Need(header.Distinct(StringComparer.OrdinalIgnoreCase).Count()==header.Count,"أعمدة مكررة في الفاتورة.");
        string[] fields=["Contract Amount","Total Delivered Orders","Count of First Attended Date to End of Month","Target Total Amount","Attendance Deduction","8 Hour Deductions","Final Amount","VAT","TOTAL COST",..NinjaBilling.PassThrough.Select(x=>x.Column)];
        int captain=header.FindIndex(h=>h.Equals("captain_id",StringComparison.OrdinalIgnoreCase));
        for(int row=1;row<sheet.Rows.Count;row++)
        {
            var cells=sheet.Rows[row];if(captain>=cells.Count||string.IsNullOrWhiteSpace(cells[captain]))continue;
            foreach(string field in fields)
            {
                int index=header.FindIndex(h=>h.Equals(field,StringComparison.OrdinalIgnoreCase));string value=index<cells.Count?cells[index].Trim():"";
                if(value==""||value=="-"&&field=="Contract Amount")continue;
                J.Need(decimal.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out _),"قيمة رقمية غير صالحة في الصف "+(row+1));
            }
        }
    }
    public static JsonObject Parse(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject input,byte[] bytes)
    {
        string company=FinancialCore.ResolveSponsor(input.Required("companyId")).Id,period=J.Period(input.Required("period")),reference=input.Required("reference");Scope(actor,"pay.manage",company);
        J.Need(bytes.Length is >0 and <=8*1024*1024&&reference.Length<=200,"حد الملف8ميجابايت والمرجع200حرف.");
        string hash=Convert.ToHexString(SHA256.HashData(bytes)),id="ninja-invoice:"+Hash(company+":"+period+":"+reference);
        var allInvoices=Store.List(c,"platform_invoice",tx);var existing=allInvoices.FirstOrDefault(i=>i.S("sourceSha256")==hash||i.S("sourceHash")==hash);
        J.Need(existing==null||existing.S("platform")=="Ninja"&&existing.S("companyId")==company&&existing.S("period")==period,"بصمة الملف مسجلة في سياق آخر.");
        J.Need(existing!=null||!allInvoices.Any(i=>i.S("id")==id),"مرجع الفاتورة مسجل لملف آخر.");
        var selection=Selection(c,tx,input,company);
        var sheets=Imports.Read(bytes,"invoice.xlsx");var (terms,termsSource)=NinjaBilling.Terms(c,period,tx);var reconciled=NinjaBilling.Reconcile(sheets,period,terms,hash);ValidateNumbers(sheets);
        var accounts=Store.List(c,"ninja_account",tx);
        if(selection!=null)
        {
            var wrong=reconciled["lines"]!.AsArray().OfType<JsonObject>().Where(row=>!accounts.Any(a=>a.S("CaptainId",a.S("captainId"))==row.S("captainId")&&selection.Contains(a.S("platformAccountId")))).Select(row=>row.N("row")).ToList();
            J.Need(wrong.Count==0,"account_scope_mismatch: rows "+string.Join(",",wrong));
        }
        var lines=new JsonArray();var captains=new HashSet<string>(StringComparer.Ordinal);
        foreach(var comparison in reconciled["lines"]!.AsArray().OfType<JsonObject>())
        {
            J.Need(comparison.S("status")!="error","تعذر حساب المتوقع في أحد صفوف الفاتورة.");string captain=comparison.S("captainId");J.Need(captains.Add(captain),"كابتن مكرر في الفاتورة.");
            var matches=accounts.Where(a=>a.S("CaptainId",a.S("captainId"))==captain).Select(a=>a.S("platformAccountId")).Distinct().ToList();J.Need(matches.Count==1,"يوزر الكابتن غير مربوط أو غير محدد بشكل فريد: row "+comparison.N("row"));
            var account=Store.Get(c,matches[0],"platform_account",tx);J.Need(account!=null&&account.S("companyId")==company&&PayPlatforms.For(account.S("platformCode")).App=="ninja","يوزر الكابتن لا يطابق شركة نينجا المختارة.");
            var v=comparison["invoiceValues"]!.AsObject();var pass=comparison["invoicePassThrough"]!.AsObject();
            decimal baseGross=J.Money(v.N("targetTotal")-v.N("attendanceDeduction")-v.N("shortDayDeduction"));decimal incentives=J.Money(pass.Sum(x=>Math.Max(0,x.Value!.GetValue<decimal>()))),deduction=J.Money(pass.Sum(x=>Math.Max(0,-x.Value!.GetValue<decimal>())));
            decimal invoiced=J.Money(comparison.N("invoiceFinal")),vat=J.Money(v.N("vat"));
            J.Need(baseGross>=0&&v.N("orders")>=0&&v.N("orders")==decimal.Truncate(v.N("orders"))&&vat>=0&&invoiced>=0,"المبلغ أو الطلبات غير صالحة؛ الإشعار الدائن يحتاج مسار عكس.");
            J.Need(Math.Abs(baseGross+incentives-deduction-invoiced)<=0.02m&&Math.Abs(invoiced+vat-J.Money(v.N("totalCost")))<=0.02m&&Math.Abs(J.Money(invoiced*terms.N("vatPercent")/100)-vat)<=0.02m,"إجمالي أو ضريبة السطر لا يطابق مكونات الفاتورة.");
            // Bind every cent to the invoice, retaining rounded component residual explicitly.
            decimal rounding=J.Money(invoiced-(baseGross+incentives-deduction));baseGross+=rounding;
            string reason=comparison.N("engineFinal")==invoiced?"":comparison.S("capability")=="bike"&&(comparison["reasons"] as JsonArray??[]).Any(r=>r!.ToString().StartsWith("capability_label_edited:",StringComparison.OrdinalIgnoreCase))?"vehicle type edited by the platform":"invoice differs from expected engine calculation";
            var deductions=new JsonArray();foreach(var item in pass.Where(x=>x.Value!.GetValue<decimal>()<0))deductions.Add(new JsonObject{["category"]=item.Key,["amount"]=J.Money(-item.Value!.GetValue<decimal>()),["level"]="unclassified"});
            var line=new JsonObject{["id"]="ninja-line:"+Hash(id+":"+comparison.N("row")),["invoiceId"]=id,["companyId"]=company,["period"]=period,["platform"]="Ninja",["sourceRow"]=comparison.N("row"),["captainId"]=captain,["platformAccountId"]=matches[0],["lineType"]="account",["baseGross"]=baseGross,["incentives"]=incentives,["deductionAmount"]=deduction,["deductions"]=deductions,["orders"]=v.N("orders"),["vat"]=vat,["expected"]=comparison.N("engineFinal"),["invoiced"]=invoiced,["difference"]=J.Money(invoiced-comparison.N("engineFinal")),["reason"]=reason,["status"]="invoiced",["roundingResidual"]=rounding};lines.Add(line);
        }
        // Supplier-level adjustment rows carry no captain entitlement and require explicit company classification.
        foreach(var adjustment in reconciled["adjustments"]!.AsArray().OfType<JsonObject>())
        {
            decimal amount=adjustment.N("finalAmount");J.Need(amount<0,"تعديل الشركة الموجب يحتاج مراجعة مصدر مستقلة.");
            lines.Add(new JsonObject{["id"]="ninja-line:"+Hash(id+":"+adjustment.N("row")),["invoiceId"]=id,["companyId"]=company,["period"]=period,["platform"]="Ninja",["sourceRow"]=adjustment.N("row"),["lineType"]="company",["platformAccountId"]="",["baseGross"]=0,["incentives"]=0,["deductionAmount"]=-amount,["deductions"]=new JsonArray(new JsonObject{["category"]="supplier_adjustment",["amount"]=-amount,["level"]="unclassified"}),["orders"]=0,["vat"]=J.Money(adjustment.N("totalCost")-amount),["expected"]=amount,["invoiced"]=amount,["difference"]=0,["reason"]="supplier adjustment as invoiced",["status"]="invoiced"});
        }
        J.Need(lines.Count>0,"الفاتورة فارغة.");
        if(existing!=null){var saved=existing.Clone();saved["lines"]=new JsonArray(Lines(c,tx,existing.S("id")).Select(l=>(JsonNode?)l.Clone()).ToArray());return saved;}
        var otherIds=allInvoices.Where(i=>i.S("platform")=="Ninja"&&i.S("companyId")==company&&i.S("period")==period&&i.S("reversalJournalEntryId")==""&&i.S("status")!="reversed").Select(i=>i.S("id")).ToHashSet();
        var used=Store.List(c,"platform_invoice_line",tx).Where(l=>otherIds.Contains(l.S("invoiceId"))).Select(l=>l.S("platformAccountId")).ToHashSet();
        J.Need(!lines.OfType<JsonObject>().Any(l=>l.S("lineType")=="account"&&used.Contains(l.S("platformAccountId"))),"يوزر مسجل في فاتورة أخرى للشركة والشهر؛ استخدم العكس والتصحيح.");
        return new(){["id"]=id,["platform"]="Ninja",["companyId"]=company,["period"]=period,["reference"]=reference,["sourceSha256"]=hash,["termsSource"]=termsSource,["accountIds"]=selection==null?null:new JsonArray(selection.Order().Select(a=>(JsonNode?)JsonValue.Create(a)).ToArray()),["amountExclVat"]=lines.OfType<JsonObject>().Sum(l=>l.N("baseGross")+l.N("incentives")),["deductionsTotal"]=lines.OfType<JsonObject>().Sum(l=>l.N("deductionAmount")),["vat"]=lines.OfType<JsonObject>().Sum(l=>l.N("vat")),["status"]="invoiced",["approvalStatus"]="pending",["allowedActions"]=new JsonArray("confirm"),["odooVoucherNo"]=input.S("odooVoucherNo"),["lines"]=lines};
    }
    public static JsonObject Preview(DbConnection c,ClaimsPrincipal actor,JsonObject input,byte[] bytes)
    {var rows=new JsonArray(Parse(c,null,actor,input,bytes));return new(){["rows"]=rows,["canCommit"]=true,["fingerprint"]=FinancialImports.Issue(c,PreviewKind,rows,actor.Identity!.Name!)};}
    public static JsonObject Commit(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input,byte[] bytes)
    {
        var parsed=Parse(c,tx,actor,input,bytes);
        if(Store.Get(c,parsed.S("id"),"platform_invoice",tx) is {} saved)
        {J.Need(input.B("confirm"),"أكد إعادة الملف المسجل.");return saved;}
        FinancialImports.Verify(c,tx,input,PreviewKind,actor.Identity!.Name!);
        J.Need(FinancialImports.Canonical(input["rows"])==FinancialImports.Canonical(new JsonArray(parsed.DeepClone())),"التأكيد لا يطابق الملف والمعاينة.");
        var lines=parsed["lines"]!.AsArray().OfType<JsonObject>().Select(l=>l.Clone()).ToList();parsed.Remove("lines");parsed.Remove("allowedActions");parsed["approvalStatus"]="pending";parsed["createdBy"]=actor.Identity!.Name;parsed["createdAt"]=DateTimeOffset.UtcNow.ToString("O");
        foreach(var line in lines)Store.Save(c,"platform_invoice_line",line,tx);Store.Save(c,"platform_invoice",parsed,tx);Store.Audit(c,tx,"platform.ninja.invoice_imported",parsed.S("id"),actor.Identity!.Name!);return parsed;
    }
    public static JsonObject Classify(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        var invoice=Get(c,tx,actor,id,"pay.manage");J.Need(invoice.S("approvalStatus")=="pending"&&!InCycle(c,tx,id),"الفاتورة المعتمدة أو المرتبطة بدورة لا تتعدل.");var line=Lines(c,tx,id).SingleOrDefault(l=>l.S("id")==input.Required("lineId"))??throw new ApiError("السطر غير موجود.",404);
        var deduction=(line["deductions"] as JsonArray??[]).OfType<JsonObject>().SingleOrDefault(d=>d.S("category")==input.Required("category"))??throw new ApiError("بند الخصم غير موجود.");
        string level=input.Required("level");J.Need(level is "company_level" or "account_level"&&(line.S("lineType")!="company"||level=="company_level"),"حدد شركة أو يوزر؛ تعديل المورد على الشركة.");
        if(level=="company_level"){string expense=PayPlatforms.Active(input.Required("expenseAccount"));J.Need(expense.StartsWith('5'),"خصم الشركة يحتاج حساب تكلفة.");deduction["expenseAccount"]=expense;}
        deduction["level"]=level;deduction["classifiedBy"]=actor.Identity!.Name;Store.Save(c,"platform_invoice_line",line,tx);Store.Audit(c,tx,"platform.ninja.deduction_classified",line.S("id"),actor.Identity!.Name!);return line;
    }
    public static JsonObject Approve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        var invoice=Get(c,tx,actor,id,"pay.approve");Independent(actor,invoice.S("createdBy"));Open(c,tx,invoice);
        if(invoice.S("approvalStatus")=="approved")return invoice;
        foreach(var line in Lines(c,tx,id))
        {
            foreach(var d in (line["deductions"] as JsonArray??[]).OfType<JsonObject>()){J.Need(d.S("level") is "company_level" or "account_level","صنف خصومات الشركة واليوزر أولًا.");Independent(actor,d.S("classifiedBy"));}
            line["status"]="final";Store.Save(c,"platform_invoice_line",line,tx);
            PlatformDeductions.ConfirmClassified(c,tx,actor,invoice,line);
        }
        invoice["approvalStatus"]="approved";invoice["status"]="final";invoice["approvedBy"]=actor.Identity!.Name;invoice["approvedAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,"platform_invoice",invoice,tx);Store.Audit(c,tx,"platform.ninja.invoice_approved",id,actor.Identity!.Name!);return invoice;
    }
    public static void DemandResolvedDisputes(DbConnection c,DbTransaction? tx,string id)=>J.Need(!Store.List(c,"platform_dispute",tx).Any(d=>d.S("invoiceId")==id&&d.S("status")!="resolved"),"احسم الاعتراض الرسمي أولًا.");
    public static JsonObject Dispute(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input,bool resolve=false)
    {
        var invoice=Get(c,tx,actor,id,resolve?"pay.approve":"pay.manage");string reason=input.Required("reason");J.Need(reason.Length<=1000,"السبب أطول من الحد.");
        J.Need(invoice.S("journalEntryId")==""&&!InCycle(c,tx,id),"الفاتورة المرحّلة تحتاج عكسًا من مسارها.");
        JsonObject d;
        if(resolve){d=Store.Get(c,input.Required("disputeId"),"platform_dispute",tx)??throw new ApiError("الاعتراض غير موجود.",404);J.Need(d.S("invoiceId")==id&&d.S("status")=="open","الاعتراض لا يطابق الفاتورة.");Independent(actor,d.S("createdBy"));d["status"]="resolved";d["resolutionReason"]=reason;d["resolvedBy"]=actor.Identity!.Name;}
        else d=new(){["id"]="ninja-dispute:"+Guid.NewGuid().ToString("N"),["invoiceId"]=id,["companyId"]=invoice.S("companyId"),["platform"]="Ninja",["status"]="open",["reason"]=reason,["createdBy"]=actor.Identity!.Name};
        Store.Save(c,"platform_dispute",d,tx);Store.Audit(c,tx,resolve?"platform.ninja.dispute_resolved":"platform.ninja.dispute_created",d.S("id"),actor.Identity!.Name!);return d;
    }
    public static JsonObject Post(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        var invoice=Get(c,tx,actor,id,"pay.approve");J.Need(invoice.S("approvalStatus")=="approved","اعتمد الفاتورة أولًا.");Independent(actor,invoice.S("createdBy"));Open(c,tx,invoice);PlatformPosting.Demand(c,"ninja",tx);
        J.Need(!Store.List(c,"platform_dispute",tx).Any(d=>d.S("invoiceId")==id&&d.S("status")!="resolved"),"احسم الاعتراض الرسمي أولًا.");
        J.Need(!Store.List(c,"pay_cycle",tx).Any(p=>p.S("status") is not ("cancelled" or "reversed")&&(p["invoices"] as JsonArray??[]).OfType<JsonObject>().Any(i=>i.S("invoiceId")==id)),"الفاتورة مرتبطة بدورة؛ الترحيل من الدورة فقط.");
        if(invoice.S("journalEntryId")!="")return invoice;
        var lines=new JsonArray();string company=invoice.S("companyId"),period=invoice.S("period");
        void Add(string code,decimal debit,decimal credit,string source="",string account="") {if(debit==0&&credit==0)return;lines.Add(PayPosting.Line(code,company,debit,credit,"ninja","",period,source,account));}
        Add("112806",invoice.N("amountExclVat")+invoice.N("vat"),0,id);Add("411104",0,invoice.N("amountExclVat"),id);Add("213201",0,invoice.N("vat"),id);
        foreach(var line in Lines(c,tx,id))foreach(var d in (line["deductions"] as JsonArray??[]).OfType<JsonObject>())
        {
            J.Need(d.S("level")=="company_level","خصم اليوزر يحتاج توزيع المندوب ودورة الرواتب؛ لا ينشأ دين بالتخمين.");Add(d.Required("expenseAccount"),d.N("amount"),0,line.S("id"));Add("112806",0,d.N("amount"),line.S("id"),line.S("platformAccountId"));
        }
        var journal=FinancialCore.PostJournal(c,tx,"ninja_invoice","فاتورة نينجا المعتمدة",period,"NINJA-INVOICE:"+id,lines,actor.Identity!.Name!,originSponsorId:company,creditorSponsorId:company,targetChart:true,payChart:true);
        invoice["journalEntryId"]=journal.S("id");invoice["accountingStatus"]="posted";Store.Save(c,"platform_invoice",invoice,tx);Store.Audit(c,tx,"platform.ninja.invoice_posted",id,actor.Identity!.Name!);return invoice;
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/platforms/ninja/invoices",async(HttpContext ctx)=>
        {
            AccessPolicy.Demand(ctx.User,"pay.manage");J.Need(ctx.Request.HasFormContentType,"ارفع ملفxlsx كـmultipart.");var form=await ctx.Request.ReadFormAsync();J.Need(form.Files.Count==1,"ملف واحد لكل فاتورة.");var file=form.Files[0];J.Need(file.Length is >0 and <=8*1024*1024&&Path.GetExtension(file.FileName).Equals(".xlsx",StringComparison.OrdinalIgnoreCase),"ملفxlsx حتى8ميجابايت.");
            using var buffer=new MemoryStream();await file.CopyToAsync(buffer,ctx.RequestAborted);byte[] bytes=buffer.ToArray();var input=new JsonObject{["companyId"]=form["companyId"].ToString(),["period"]=form["period"].ToString(),["reference"]=form["reference"].ToString(),["odooVoucherNo"]=form["odooVoucherNo"].ToString()};
            var selected=form["accountIds[]"].Count>0?form["accountIds[]"]:form["accountIds"];
            if(selected.Count>0)input["accountIds"]=selected.Count==1&&selected[0]!.TrimStart().StartsWith('[')?JsonNode.Parse(selected[0]!):new JsonArray(selected.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray());
            if(ctx.Request.Query["commit"]!="1")return store.Read(c=>Preview(c,ctx.User,input,bytes));
            input["confirm"]=form["confirm"]=="true";input["fingerprint"]=form["fingerprint"].ToString();input["rows"]=JsonNode.Parse(form["rows"].ToString());return store.Write((c,tx)=>View(c,tx,ctx.User,Commit(c,tx,ctx.User,input,bytes)));
        }).DisableAntiforgery().WithTags("Platforms").WithSummary("Preview or confirm a signed Ninja XLSX invoice; invoice differences are informational");
        api.MapGet("/platforms/ninja/invoices",(HttpContext ctx)=>{AccessPolicy.Demand(ctx.User,"pay.view");return store.Read(c=>ApiPagination.Apply(Store.List(c,"platform_invoice").Where(i=>i.S("platform")=="Ninja"&&FinancialScopes.Sponsor(ctx.User,i.S("companyId"))&&FinancialScopes.Platform(ctx.User,"Ninja")).Select(i=>View(c,null,ctx.User,i)),ctx.Request.Query,true));}).WithTags("Platforms").WithSummary("List scoped Ninja invoices");
        api.MapGet("/platforms/ninja/invoices/{id}",(string id,HttpContext ctx)=>store.Read(c=>View(c,null,ctx.User,Get(c,null,ctx.User,id)))).WithTags("Platforms").WithSummary("Read a scoped Ninja invoice with available server actions");
        api.MapGet("/platforms/ninja/invoices/{id}/lines",(string id,HttpContext ctx)=>store.Read(c=>{Get(c,null,ctx.User,id);return Lines(c,null,id);})).WithTags("Platforms").WithSummary("Read invoiced Ninja entitlements and expected differences");
        api.MapPost("/platforms/ninja/invoices/{id}/classify",(string id,JsonObject input,HttpContext ctx)=>Act(store,ctx,id,(c,tx)=>Classify(c,tx,ctx.User,id,input))).WithTags("Platforms").WithSummary("Classify an immutable Ninja deduction as company or account level");
        api.MapPost("/platforms/ninja/invoices/{id}/approve",(string id,HttpContext ctx)=>Act(store,ctx,id,(c,tx)=>Approve(c,tx,ctx.User,id))).WithTags("Platforms").WithSummary("Independently approve invoiced Ninja facts; expected differences never block");
        api.MapPost("/platforms/ninja/invoices/{id}/dispute",(string id,JsonObject input,HttpContext ctx)=>Act(store,ctx,id,(c,tx)=>Dispute(c,tx,ctx.User,id,input))).WithTags("Platforms").WithSummary("Record an explicit formal Ninja dispute; expected differences do not create one");
        api.MapPost("/platforms/ninja/invoices/{id}/resolve",(string id,JsonObject input,HttpContext ctx)=>Act(store,ctx,id,(c,tx)=>Dispute(c,tx,ctx.User,id,input,true))).WithTags("Platforms").WithSummary("Independently resolve a formal Ninja dispute without changing invoiced amounts");
        api.MapPost("/platforms/ninja/invoices/{id}/post",(string id,HttpContext ctx)=>Act(store,ctx,id,(c,tx)=>Post(c,tx,ctx.User,id))).WithTags("Platforms").WithSummary("Post an approved Ninja invoice with VAT, dispute and duplicate-cycle guards");
    }
}
