using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static class ManualJournals
{
    public const string Kind="manual_journal",CreatePermission="finance.journal.create",ApprovePermission="finance.journal.approve";
    public static string[] Grants(string role)=>role is "FinancialManager" or "FinancialAccountant"?[CreatePermission,ApprovePermission]:[];
    public static void Seed(Store store)=>store.Write((c,tx)=>
    {
        const string marker="migration:manual-journal-permissions:v1";if(Store.Get(c,marker,"setting",tx)!=null)return false;
        foreach(var user in Store.List(c,"user",tx))
        {
            var denied=(user["deniedPermissions"] as JsonArray??[]).Select(p=>p!.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);var custom=user["customPermissions"] as JsonArray??new JsonArray();bool changed=false;
            foreach(string permission in Grants(UserAccess.Role(user)))if(!denied.Contains(permission)&&!custom.Any(p=>p?.ToString()==permission)){custom.Add(permission);changed=true;}
            if(changed){user["customPermissions"]=custom.DeepClone();Store.Save(c,"user",user,tx);Store.Audit(c,tx,"finance.manual_permissions_seeded",user.S("id"),"system-migration");}
        }
        Store.Save(c,"setting",new(){["id"]=marker,["completed"]=true},tx);return true;
    });
    static string Date(string raw)
    {J.Need(DateOnly.TryParseExact(raw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date),"تاريخ القيد مطلوب بصيغةyyyy-MM-dd.");return date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
    static void Open(DbConnection c,DbTransaction? tx,string company,string period)=>J.Need(!Store.List(c,"accounting_period",tx).Any(p=>p.S("companyId")==company&&p.S("period")==period&&p.S("status")=="closed"),"الفترة المحاسبية مغلقة.");
    static void Scope(ClaimsPrincipal actor,JsonObject draft,string permission)
    {AccessPolicy.Demand(actor,permission);FinancialScopes.DemandSponsor(actor,draft.S("companyId"));foreach(var line in draft["lines"]!.AsArray().OfType<JsonObject>())J.Need(FinancialScopes.TargetAccount(actor,draft.S("companyId"),line.S("accountCode")),"الحساب خارج النطاق المالي.",403);}
    static JsonObject Get(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id,string permission)
    {var draft=Store.Get(c,id,Kind,tx)??throw new ApiError("القيد اليدوي غير موجود.",404);Scope(actor,draft,permission);return draft;}
    static JsonArray ValidateLines(ClaimsPrincipal actor,string company,JsonArray source)
    {
        J.Need(source.Count is >=2 and <=200,"القيد من2إلى200سطر.");var lines=new JsonArray();
        foreach(var item in source)
        {
            var line=item as JsonObject??throw new ApiError("سطر غير صالح.");PayValues.Fields(line,"accountCode","debit","credit","note");string code=PayPlatforms.Active(line.Required("accountCode"));
            J.Need(FinancialScopes.TargetAccount(actor,company,code),"الحساب خارج النطاق المالي.",403);
            J.Need(line.S("note").Length<=1000,"ملاحظة السطر أطول من الحد.");
            var canonical=FinancialCore.CreateJournalLine(code,company,line.N("debit"),line.N("credit"),line.S("note"),targetChart:true,payChart:true);canonical["manual"]=true;lines.Add(canonical);
        }
        J.Need(lines.OfType<JsonObject>().Sum(l=>l.N("debit"))==lines.OfType<JsonObject>().Sum(l=>l.N("credit")),"القيد غير متوازن.");return lines;
    }
    static string? Attachment(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string company,JsonNode? node)
    {
        if(node==null)return null;var input=node as JsonObject??throw new ApiError("مرفق غير صالح.");PayValues.Fields(input,"fileName","dataBase64");byte[] data=FinancialImports.FileBytes(input);J.Need(data.Length is >0 and <=2*1024*1024,"حد المرفق2ميجابايت.");
        bool pdf=data.AsSpan().StartsWith("%PDF-"u8),png=data.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10}),jpg=data.AsSpan().StartsWith(new byte[]{255,216,255});J.Need(pdf||png||jpg,"المرفقPDFأوPNGأوJPEG.");
        string id="financial-attachment:"+Guid.NewGuid().ToString("N");Store.Save(c,"financial_attachment",new(){["id"]=id,["companyId"]=company,["fileName"]="attachment"+(pdf?".pdf":png?".png":".jpg"),["contentType"]=pdf?"application/pdf":png?"image/png":"image/jpeg",["dataBase64"]=Convert.ToBase64String(data),["createdBy"]=actor.Identity!.Name},tx);return id;
    }
    public static JsonObject Create(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,CreatePermission);PayValues.Fields(input,"companyId","date","reference","description","lines","attachment","odooVoucherNo");
        string company=FinancialCore.ResolveSponsor(input.Required("companyId")).Id,date=Date(input.Required("date")),reference=input.Required("reference"),description=input.Required("description");FinancialScopes.DemandSponsor(actor,company);Open(c,tx,company,date[..7]);
        J.Need(reference.Length<=200&&description.Length<=1000,"المرجع أو البيان أطول من الحد.");J.Need(!Store.List(c,Kind,tx).Any(d=>d.S("companyId")==company&&d.S("reference")==reference),"مرجع القيد اليدوي مكرر.");
        var lines=ValidateLines(actor,company,input["lines"] as JsonArray??throw new ApiError("سطور القيد مطلوبة."));
        var draft=new JsonObject{["id"]="manual-journal:"+Guid.NewGuid().ToString("N"),["companyId"]=company,["date"]=date,["period"]=date[..7],["reference"]=reference,["description"]=description,["lines"]=lines,["status"]="draft",["createdBy"]=actor.Identity!.Name,["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["attachmentId"]=Attachment(c,tx,actor,company,input["attachment"]),["odooVoucherNo"]=input.S("odooVoucherNo")};
        Store.Save(c,Kind,draft,tx);Store.Audit(c,tx,"finance.manual_drafted",draft.S("id"),actor.Identity!.Name!);return draft;
    }
    public static JsonObject Approve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        var draft=Get(c,tx,actor,id,ApprovePermission);J.Need(!string.Equals(actor.Identity!.Name,draft.S("createdBy"),StringComparison.OrdinalIgnoreCase),"صاحب المسودة لا يعتمدها.",403);if(draft.S("status")=="approved")return draft;
        J.Need(draft.S("status")=="draft","المسودة غير جاهزة للاعتماد.");Open(c,tx,draft.S("companyId"),draft.S("period"));
        string reversal=draft.S("reversesJournalId");if(reversal!="")J.Need(!Store.List(c,"journal",tx).Any(j=>j.S("reversesJournalId")==reversal),"القيد معكوس بالفعل.");
        var journal=FinancialCore.PostJournal(c,tx,"manual"+(reversal==""?"":"_reversal"),draft.S("description"),draft.S("period"),"MANUAL:"+id,draft["lines"]!.DeepClone().AsArray(),actor.Identity!.Name!,originSponsorId:draft.S("companyId"),creditorSponsorId:draft.S("companyId"),targetChart:true,reversesJournalId:reversal==""?null:reversal,payChart:true,journalDate:draft.S("date"),manualJournalId:id);
        journal["date"]=draft.S("date");journal["manualJournalId"]=id;journal["attachmentId"]=draft["attachmentId"]?.DeepClone();journal["odooVoucherNo"]=draft.S("odooVoucherNo");Store.Save(c,"journal",journal,tx);
        draft["status"]="approved";draft["journalEntryId"]=journal.S("id");draft["approvedBy"]=actor.Identity!.Name;draft["approvedAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,Kind,draft,tx);Store.Audit(c,tx,"finance.manual_approved",id,actor.Identity!.Name!);return draft;
    }
    public static JsonObject Reverse(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        var original=Get(c,tx,actor,id,CreatePermission);J.Need(original.S("status")=="approved","العكس لقيد يدوي معتمد فقط.");string reason=input.Required("reason");J.Need(reason.Length<=1000,"سبب العكس أطول من الحد.");
        var journal=Store.Get(c,original.S("journalEntryId"),"journal",tx)??throw new ApiError("القيد الأصلي غير موجود.");J.Need(journal.S("manualJournalId")==id&&journal.S("transactionType")=="manual","لا تستخدم المسار لتعديل قيود النظام أو عكس عكس.");
        J.Need(!Store.List(c,Kind,tx).Any(d=>d.S("reversesJournalId")==journal.S("id")),"يوجد طلب عكس لهذا القيد.");
        var lines=new JsonArray();foreach(var line in journal["lines"]!.AsArray().OfType<JsonObject>())lines.Add(new JsonObject{["accountCode"]=line.S("accountCode"),["debit"]=line.N("credit"),["credit"]=line.N("debit"),["note"]=reason});
        var reversal=Create(c,tx,actor,new(){["companyId"]=original.S("companyId"),["date"]=input.Required("date"),["reference"]="REV:"+id,["description"]=reason,["lines"]=lines});reversal["reversesJournalId"]=journal.S("id");reversal["reversesManualJournalId"]=id;Store.Save(c,Kind,reversal,tx);return reversal;
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/finance/journals/manual",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Create(c,tx,ctx.User,input))).WithTags("Accounting and reports").WithSummary("Draft a balanced manual target-chart journal in an open company period");
        api.MapPost("/finance/journals/manual/{id}/approve",(string id,HttpContext ctx)=>store.Write((c,tx)=>Approve(c,tx,ctx.User,id))).WithTags("Accounting and reports").WithSummary("Independently approve a manual journal or its reversal");
        api.MapPost("/finance/journals/manual/{id}/reverse",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Reverse(c,tx,ctx.User,id,input))).WithTags("Accounting and reports").WithSummary("Create a reasoned open-period reversal draft requiring independent approval");
        api.MapGet("/finance/journals/manual",(HttpContext ctx)=>
        {
            if(!AccessPolicy.Has(ctx.User,CreatePermission)&&!AccessPolicy.Has(ctx.User,ApprovePermission))throw new ApiError("صلاحية القيد اليدوي مطلوبة.",403);
            return store.Read(c=>ApiPagination.Apply(Store.List(c,Kind).Where(d=>(ctx.Request.Query["status"].Count==0||d.S("status")==ctx.Request.Query["status"])&&FinancialScopes.Sponsor(ctx.User,d.S("companyId"))&&d["lines"]!.AsArray().OfType<JsonObject>().All(l=>FinancialScopes.TargetAccount(ctx.User,d.S("companyId"),l.S("accountCode")))),ctx.Request.Query,true));
        }).WithTags("Accounting and reports").WithSummary("List scoped manual journal drafts and approvals");
        api.MapGet("/finance/journals/manual/{id}/attachment",(string id,HttpContext ctx)=>store.Read(c=>
        {
            var draft=Get(c,null,ctx.User,id,AccessPolicy.Has(ctx.User,CreatePermission)?CreatePermission:ApprovePermission);var file=Store.Get(c,draft.S("attachmentId"),"financial_attachment")??throw new ApiError("المرفق غير موجود.",404);FinancialScopes.DemandSponsor(ctx.User,file.S("companyId"));return Results.File(Convert.FromBase64String(file.S("dataBase64")),file.S("contentType"),file.S("fileName"));
        })).WithTags("Accounting and reports").WithSummary("Download a scoped private manual-journal attachment");
    }
}
