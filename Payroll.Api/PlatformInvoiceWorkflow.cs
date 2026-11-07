using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

// App-scoped invoice lifecycle, using the same independent approval and reversal boundaries as Ninja.
public sealed class PlatformInvoiceWorkflow(string app)
{
    PayPlatforms.Accounts Platform=>PayPlatforms.For(app);
    void Scope(ClaimsPrincipal actor,string permission,string company)
    {AccessPolicy.Demand(actor,permission);FinancialScopes.DemandSponsor(actor,company);if(!FinancialScopes.Platform(actor,Platform.Platform))throw new ApiError("المنصة خارج النطاق المالي.",403);}
    public JsonObject Get(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id,string permission="pay.view")
    {var invoice=Store.Get(c,id,"platform_invoice",tx)??throw new ApiError("الفاتورة غير موجودة.",404);if(invoice.S("platform")!=Platform.Platform)throw new ApiError("الفاتورة غير موجودة.",404);Scope(actor,permission,invoice.S("companyId"));return invoice;}
    public List<JsonObject> Lines(DbConnection c,DbTransaction? tx,string id)=>Store.List(c,"platform_invoice_line",tx).Where(l=>l.S("invoiceId")==id).OrderBy(l=>l.N("sourceRow")).ToList();
    void Open(DbConnection c,DbTransaction? tx,JsonObject invoice)=>J.Need(!Store.List(c,"accounting_period",tx).Any(p=>p.S("companyId")==invoice.S("companyId")&&p.S("period")==invoice.S("period")&&p.S("status")=="closed"),"الفترة المحاسبية مغلقة.");
    void Independent(ClaimsPrincipal actor,string maker)=>J.Need(!string.Equals(actor.Identity!.Name,maker,StringComparison.OrdinalIgnoreCase),"يلزم مراجع مستقل.");
    public JsonObject Classify(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        var invoice=Get(c,tx,actor,id,"pay.manage");J.Need(invoice.S("approvalStatus")=="pending","الفاتورة المعتمدة لا تتعدل.");var line=Lines(c,tx,id).SingleOrDefault(l=>l.S("id")==input.Required("lineId"))??throw new ApiError("السطر غير موجود.",404);
        var deduction=(line["deductions"] as JsonArray??[]).OfType<JsonObject>().SingleOrDefault(d=>d.S("category")==input.Required("category"))??throw new ApiError("بند الخصم غير موجود.");
        string level=input.Required("level");J.Need(level is "company_level" or "account_level"&&(line.S("lineType")!="company"||level=="company_level"),"حدد شركة أو يوزر؛ تعديل المورد على الشركة.");
        if(level=="company_level"){string expense=PayPlatforms.Active(input.Required("expenseAccount"));J.Need(expense.StartsWith('5'),"خصم الشركة يحتاج حساب تكلفة.");deduction["expenseAccount"]=expense;}
        deduction["level"]=level;deduction["classifiedBy"]=actor.Identity!.Name;Store.Save(c,"platform_invoice_line",line,tx);Store.Audit(c,tx,"platform."+app+".deduction_classified",line.S("id"),actor.Identity!.Name!);return line;
    }
    public JsonObject Approve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        var invoice=Get(c,tx,actor,id,"pay.approve");Independent(actor,invoice.S("createdBy"));Open(c,tx,invoice);
        if(invoice.S("approvalStatus")=="approved")return invoice;
        foreach(var line in Lines(c,tx,id))
        {
            foreach(var d in (line["deductions"] as JsonArray??[]).OfType<JsonObject>()){J.Need(d.S("level") is "company_level" or "account_level","صنف خصومات الشركة واليوزر أولًا.");Independent(actor,d.S("classifiedBy"));}
            line["status"]="final";Store.Save(c,"platform_invoice_line",line,tx);
            PlatformDeductions.ConfirmClassified(c,tx,actor,invoice,line);
        }
        invoice["approvalStatus"]="approved";invoice["status"]="final";invoice["approvedBy"]=actor.Identity!.Name;invoice["approvedAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,"platform_invoice",invoice,tx);Store.Audit(c,tx,"platform."+app+".invoice_approved",id,actor.Identity!.Name!);return invoice;
    }
    public void DemandResolvedDisputes(DbConnection c,DbTransaction? tx,string id)=>J.Need(!Store.List(c,"platform_dispute",tx).Any(d=>d.S("invoiceId")==id&&d.S("status")!="resolved"),"احسم الاعتراض الرسمي أولًا.");
    public JsonObject Dispute(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input,bool resolve=false)
    {
        var invoice=Get(c,tx,actor,id,resolve?"pay.approve":"pay.manage");string reason=input.Required("reason");J.Need(reason.Length<=1000,"السبب أطول من الحد.");
        J.Need(invoice.S("journalEntryId")==""&&!Store.List(c,"pay_cycle",tx).Any(p=>p.S("status")=="approved"&&(p["invoices"] as JsonArray??[]).OfType<JsonObject>().Any(i=>i.S("invoiceId")==id)),"الفاتورة المرحّلة تحتاج عكسًا من مسارها.");
        JsonObject d;
        if(resolve){d=Store.Get(c,input.Required("disputeId"),"platform_dispute",tx)??throw new ApiError("الاعتراض غير موجود.",404);J.Need(d.S("invoiceId")==id&&d.S("status")=="open","الاعتراض لا يطابق الفاتورة.");Independent(actor,d.S("createdBy"));d["status"]="resolved";d["resolutionReason"]=reason;d["resolvedBy"]=actor.Identity!.Name;}
        else d=new(){["id"]=app+"-dispute:"+Guid.NewGuid().ToString("N"),["invoiceId"]=id,["companyId"]=invoice.S("companyId"),["platform"]=Platform.Platform,["status"]="open",["reason"]=reason,["createdBy"]=actor.Identity!.Name};
        Store.Save(c,"platform_dispute",d,tx);Store.Audit(c,tx,resolve?"platform."+app+".dispute_resolved":"platform."+app+".dispute_created",d.S("id"),actor.Identity!.Name!);return d;
    }
    public JsonObject Post(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        var invoice=Get(c,tx,actor,id,"pay.approve");J.Need(invoice.S("accountingRole")=="principal","الفاتورة الوكيلة أو المختلطة تترحل من دورة الرواتب.");J.Need(invoice.S("approvalStatus")=="approved","اعتمد الفاتورة أولًا.");Independent(actor,invoice.S("createdBy"));Open(c,tx,invoice);PlatformPosting.Demand(c,app,tx);
        J.Need(!Store.List(c,"platform_dispute",tx).Any(d=>d.S("invoiceId")==id&&d.S("status")!="resolved"),"احسم الاعتراض الرسمي أولًا.");
        J.Need(!Store.List(c,"pay_cycle",tx).Any(p=>p.S("status") is not ("cancelled" or "reversed")&&(p["invoices"] as JsonArray??[]).OfType<JsonObject>().Any(i=>i.S("invoiceId")==id)),"الفاتورة مرتبطة بدورة؛ الترحيل من الدورة فقط.");
        if(invoice.S("journalEntryId")!="")return invoice;
        var lines=new JsonArray();string company=invoice.S("companyId"),period=invoice.S("period");
        void Add(string code,decimal debit,decimal credit,string source="",string account="") {if(debit==0&&credit==0)return;lines.Add(PayPosting.Line(code,company,debit,credit,app,"",period,source,account));}
        Add(Platform.Receivable,invoice.N("amountExclVat")+invoice.N("vat"),0,id);Add(PayPlatforms.Revenue(app,"principal"),0,invoice.N("amountExclVat"),id);Add("213201",0,invoice.N("vat"),id);
        foreach(var line in Lines(c,tx,id))foreach(var d in (line["deductions"] as JsonArray??[]).OfType<JsonObject>())
        {
            J.Need(d.S("level")=="company_level","خصم اليوزر يحتاج توزيع المندوب ودورة الرواتب؛ لا ينشأ دين بالتخمين.");Add(d.Required("expenseAccount"),d.N("amount"),0,line.S("id"));Add(Platform.Receivable,0,d.N("amount"),line.S("id"),line.S("platformAccountId"));
        }
        var journal=FinancialCore.PostJournal(c,tx,app+"_invoice","فاتورة المنصة المعتمدة",period,app.ToUpperInvariant()+"-INVOICE:"+id,lines,actor.Identity!.Name!,originSponsorId:company,creditorSponsorId:company,targetChart:true,payChart:true);
        invoice["journalEntryId"]=journal.S("id");invoice["accountingStatus"]="posted";Store.Save(c,"platform_invoice",invoice,tx);Store.Audit(c,tx,"platform."+app+".invoice_posted",id,actor.Identity!.Name!);return invoice;
    }
}
