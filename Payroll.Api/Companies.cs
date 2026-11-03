using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Payroll;

// Additive documents. The existing sponsor key remains the legal-entity key.
public static class Companies
{
    static string Actor(ClaimsPrincipal user)=>user.Identity?.Name??"system";
    public static void Seed(Store store)=>store.Write((c,tx)=>
    {
        foreach(var sponsor in FinancialCore.Sponsors)
        {
            if(Store.Get(c,sponsor.Id,"company",tx)!=null)continue;
            Store.Save(c,"company",new(){["id"]=sponsor.Id,["sponsorId"]=sponsor.Id,["code"]=sponsor.Code,["nameAr"]=sponsor.NameAr,["shortNameAr"]=sponsor.ShortNameAr,["unifiedNumber"]=sponsor.UnifiedNumber,["commercialReg"]=sponsor.CommercialReg,["status"]="active",["createdAt"]=DateTimeOffset.UtcNow.ToString("O")},tx);
            Store.Audit(c,tx,"company.seeded",sponsor.Id,"system");
        }
        return true;
    });
    public static List<JsonObject> List(DbConnection c,ClaimsPrincipal user,DbTransaction? tx=null)=>Store.List(c,"company",tx).Where(x=>FinancialScopes.Sponsor(user,x.S("id"))).ToList();
    public static JsonObject Get(DbConnection c,DbTransaction? tx,string id,ClaimsPrincipal user)
    {
        var doc=Store.Get(c,id,"company",tx);
        if(doc==null||!FinancialScopes.Sponsor(user,id))throw new ApiError("الشركة غير موجودة.",404);
        return doc;
    }
    static string Text(JsonObject input,string key,string fallback="",int limit=200)
    {
        string value=input.S(key,fallback).Trim();J.Need(value.Length<=limit,"النص أطول من الحد المسموح.");return value;
    }
    static string Date(JsonObject input,string key,string fallback="")
    {
        string value=input.S(key,fallback);if(value=="")return value;
        J.Need(DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),"التاريخ يجب أن يكون yyyy-MM-dd.");return value;
    }
    static void Dates(JsonObject doc)
    {
        if(doc.S("issueDate")!=""&&doc.S("expiryDate")!="")J.Need(StringComparer.Ordinal.Compare(doc.S("issueDate"),doc.S("expiryDate"))<=0,"الانتهاء يجب ألا يسبق الإصدار.");
    }
    public static JsonObject SaveCompany(DbConnection c,DbTransaction tx,ClaimsPrincipal user,JsonObject input,string? id=null)
    {
        AccessPolicy.Demand(user,"companies.manage");
        var previous=id==null?null:Get(c,tx,id,user);
        string key=id??Text(input,"id",Guid.NewGuid().ToString("N"),64);
        J.Need(Regex.IsMatch(key,"^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$"),"معرف الشركة غير صالح.");
        if(previous==null&&Store.Get(c,key,"company",tx)!=null)throw new ApiError("معرف الشركة موجود بالفعل.",409);
        if(!AccessPolicy.Administrator(user)&&!FinancialScopes.Sponsor(user,key))throw new ApiError("الشركة خارج نطاقك.",403);
        var doc=previous?.Clone()??new JsonObject{["id"]=key,["sponsorId"]=key,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        foreach(string field in new[]{"nameAr","shortNameAr","code","unifiedNumber","commercialReg","city"})doc[field]=Text(input,field,doc.S(field));
        J.Need(doc.S("nameAr")!="","اسم الشركة مطلوب.");
        string status=Text(input,"status",doc.S("status","active"));J.Need(status is "active" or "inactive" or "archived","حالة الشركة غير صالحة.");doc["status"]=status;doc["updatedAt"]=DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c,"company",doc,tx);Store.Audit(c,tx,previous==null?"company.created":"company.updated",key,Actor(user));return doc;
    }
    static JsonObject Child(DbConnection c,DbTransaction? tx,string companyId,string id,string kind,ClaimsPrincipal user)
    {
        Get(c,tx,companyId,user);var doc=Store.Get(c,id,kind,tx);
        if(doc==null||doc.S("companyId")!=companyId||doc.S("status")=="archived")throw new ApiError("المستند غير موجود.",404);return doc;
    }
    public static JsonObject SaveRegistration(DbConnection c,DbTransaction tx,string companyId,ClaimsPrincipal user,JsonObject input,string? id=null)
    {
        AccessPolicy.Demand(user,"companies.manage");Get(c,tx,companyId,user);
        var doc=id==null?new JsonObject{["id"]="cr:"+Guid.NewGuid().ToString("N"),["companyId"]=companyId,["company"]=companyId,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")}:Child(c,tx,companyId,id,"commercial_registration",user).Clone();
        foreach(string field in new[]{"number","city"})doc[field]=Text(input,field,doc.S(field),100);
        J.Need(doc.S("number")!="","رقم السجل مطلوب.");
        if(Store.List(c,"commercial_registration",tx).Any(r=>r.S("number")==doc.S("number")&&r.S("id")!=doc.S("id")&&r.S("status")!="archived"))throw new ApiError("رقم السجل موجود بالفعل.",409);
        doc["issueDate"]=Date(input,"issueDate",doc.S("issueDate"));doc["expiryDate"]=Date(input,"expiryDate",doc.S("expiryDate"));Dates(doc);
        string status=Text(input,"status",doc.S("status","active"));J.Need(status is "active" or "inactive" or "expired","حالة السجل غير صالحة.");doc["status"]=status;
        Store.Save(c,"commercial_registration",doc,tx);Store.Audit(c,tx,id==null?"company.registration_created":"company.registration_updated",doc.S("id"),Actor(user));return doc;
    }
    public static JsonObject SaveDocument(Store store,DbConnection c,DbTransaction tx,string companyId,ClaimsPrincipal user,JsonObject input,string? id=null)
    {
        AccessPolicy.Demand(user,"companies.manage");Get(c,tx,companyId,user);
        var doc=id==null?new JsonObject{["id"]="company-doc:"+Guid.NewGuid().ToString("N"),["companyId"]=companyId,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")}:Child(c,tx,companyId,id,"company_document",user).Clone();
        foreach(string field in new[]{"type","number"})doc[field]=Text(input,field,doc.S(field),100);
        J.Need(doc.S("type")!="","نوع المستند مطلوب.");doc["expiryDate"]=Date(input,"expiryDate",doc.S("expiryDate"));doc["status"]="active";
        if(input.S("dataBase64")!="")
        {
            string filename=Path.GetFileName(input.Required("fileName"));J.Need(filename.Length is >0 and <=200&&!filename.Contains('\\'),"اسم الملف غير صالح.");
            string extension=Path.GetExtension(filename).ToLowerInvariant();J.Need(new[]{".pdf",".png",".jpg",".jpeg"}.Contains(extension),"المرفقات PDF أو PNG أو JPG فقط.");
            J.Need(input.S("dataBase64").Length<=14*1024*1024,"المرفق طويل جدًا.");byte[] bytes=Convert.FromBase64String(input.S("dataBase64"));J.Need(bytes.Length is >0 and <=10*1024*1024,"حجم المرفق غير صالح.");
            string relative=Path.Combine("company-documents",companyId,Guid.NewGuid().ToString("N")+extension);string path=Path.Combine(store.DirectoryPath,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);
            doc["fileRelativePath"]=relative;doc["fileName"]=filename;doc["contentType"]=extension==".pdf"?"application/pdf":extension==".png"?"image/png":"image/jpeg";
        }
        J.Need(doc.S("fileRelativePath")!="","ارفع ملف المستند.");doc["file"]=$"/api/companies/{companyId}/documents/{doc.S("id")}/download";
        Store.Save(c,"company_document",doc,tx);Store.Audit(c,tx,id==null?"company.document_created":"company.document_updated",doc.S("id"),Actor(user));return doc;
    }
    static object Archive(DbConnection c,DbTransaction tx,string companyId,string id,string kind,ClaimsPrincipal user)
    {
        AccessPolicy.Demand(user,"companies.manage");var doc=kind=="company"?Get(c,tx,id,user):Child(c,tx,companyId,id,kind,user);doc["status"]="archived";
        Store.Save(c,kind,doc,tx);Store.Audit(c,tx,kind+".archived",id,Actor(user));return new{ok=true}; // Retain financial references and original attachments.
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/companies",(HttpContext ctx)=>store.Read(c=>ApiPagination.Apply(List(c,ctx.User),ctx.Request.Query))).WithTags("Companies and registrations").WithSummary("List companies within the current legal-entity scope");
        api.MapGet("/companies/expiring",(int? days,HttpContext ctx)=>store.Read(c=>
        {
            int window=days??60;J.Need(window is >=0 and <=3650,"عدد الأيام غير صالح.");var end=DateOnly.FromDateTime(DateTime.UtcNow).AddDays(window);
            var visible=List(c,ctx.User).Where(x=>x.S("status")!="archived").Select(x=>x.S("id")).ToHashSet();
            return new[]{"commercial_registration","company_document"}.SelectMany(kind=>Store.List(c,kind).Where(x=>visible.Contains(x.S("companyId"))&&x.S("status")!="archived"&&DateOnly.TryParseExact(x.S("expiryDate"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var expiry)&&expiry<=end).Select(x=>{var row=x.Clone();row["kind"]=kind;return row;})).ToList();
        })).WithTags("Companies and registrations").WithSummary("List expired or soon-expiring company records and documents");
        api.MapGet("/companies/{id}",(string id,HttpContext ctx)=>store.Read(c=>Get(c,null,id,ctx.User))).WithTags("Companies and registrations").WithSummary("Read one company within scope");
        api.MapPost("/companies",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>SaveCompany(c,tx,ctx.User,input))).WithTags("Companies and registrations").WithSummary("Create and audit a company");
        api.MapPut("/companies/{id}",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>SaveCompany(c,tx,ctx.User,input,id))).WithTags("Companies and registrations").WithSummary("Update and audit company metadata");
        api.MapDelete("/companies/{id}",(string id,HttpContext ctx)=>store.Write((c,tx)=>Archive(c,tx,id,id,"company",ctx.User))).WithTags("Companies and registrations").WithSummary("Archive a company while preserving financial references");
        api.MapGet("/companies/{id}/registrations",(string id,HttpContext ctx)=>store.Read(c=>{Get(c,null,id,ctx.User);return Store.List(c,"commercial_registration").Where(r=>r.S("companyId")==id&&r.S("status")!="archived");})).WithTags("Companies and registrations").WithSummary("List commercial registrations for a company");
        api.MapGet("/companies/{id}/registrations/{registrationId}",(string id,string registrationId,HttpContext ctx)=>store.Read(c=>Child(c,null,id,registrationId,"commercial_registration",ctx.User))).WithTags("Companies and registrations").WithSummary("Read a company commercial registration");
        api.MapPost("/companies/{id}/registrations",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>SaveRegistration(c,tx,id,ctx.User,input))).WithTags("Companies and registrations").WithSummary("Create and audit a commercial registration");
        api.MapPut("/companies/{id}/registrations/{registrationId}",(string id,string registrationId,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>SaveRegistration(c,tx,id,ctx.User,input,registrationId))).WithTags("Companies and registrations").WithSummary("Update a commercial registration");
        api.MapDelete("/companies/{id}/registrations/{registrationId}",(string id,string registrationId,HttpContext ctx)=>store.Write((c,tx)=>Archive(c,tx,id,registrationId,"commercial_registration",ctx.User))).WithTags("Companies and registrations").WithSummary("Archive a commercial registration");
        api.MapGet("/companies/{id}/documents",(string id,HttpContext ctx)=>store.Read(c=>{Get(c,null,id,ctx.User);return Store.List(c,"company_document").Where(r=>r.S("companyId")==id&&r.S("status")!="archived");})).WithTags("Companies and registrations").WithSummary("List company documents");
        api.MapGet("/companies/{id}/documents/{documentId}",(string id,string documentId,HttpContext ctx)=>store.Read(c=>Child(c,null,id,documentId,"company_document",ctx.User))).WithTags("Companies and registrations").WithSummary("Read company document metadata");
        api.MapPost("/companies/{id}/documents",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>SaveDocument(store,c,tx,id,ctx.User,input))).WithTags("Companies and registrations").WithSummary("Upload and audit a company document");
        api.MapPut("/companies/{id}/documents/{documentId}",(string id,string documentId,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>SaveDocument(store,c,tx,id,ctx.User,input,documentId))).WithTags("Companies and registrations").WithSummary("Update a company document and retain prior files");
        api.MapDelete("/companies/{id}/documents/{documentId}",(string id,string documentId,HttpContext ctx)=>store.Write((c,tx)=>Archive(c,tx,id,documentId,"company_document",ctx.User))).WithTags("Companies and registrations").WithSummary("Archive a company document without deleting its file");
        api.MapGet("/companies/{id}/documents/{documentId}/download",(string id,string documentId,HttpContext ctx)=>store.Read(c=>
        {
            var doc=Child(c,null,id,documentId,"company_document",ctx.User);string basePath=Path.GetFullPath(Path.Combine(store.DirectoryPath,"company-documents"))+Path.DirectorySeparatorChar;string file=Path.GetFullPath(Path.Combine(store.DirectoryPath,doc.S("fileRelativePath")));
            if(!file.StartsWith(basePath,StringComparison.Ordinal)||!File.Exists(file))throw new ApiError("الملف غير موجود.",404);return Results.File(file,doc.S("contentType"),doc.S("fileName"));
        })).WithTags("Companies and registrations").WithSummary("Download an authorized company attachment");
    }
}
