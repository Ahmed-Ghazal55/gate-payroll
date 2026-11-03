using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Payroll;

public static class CompanyRegistryImport
{
    const string PreviewKind="company-certificates";
    static readonly string[] TextFields=["nameAr","nameEn","nameOnCertificateAr","unifiedNumber","crNumber","crType","entityType","characteristic","city","address","status"];
    static readonly string[] GregorianFields=["issueDate","annualConfirmation"];
    static readonly string[] HijriFields=["issueDateHijri","annualConfirmationHijri"];
    static void Admin(ClaimsPrincipal actor){if(!AccessPolicy.Administrator(actor))throw new ApiError("استيراد بيانات الشركات لمسؤول النظام فقط.",403);}
    static decimal Number(JsonObject input,string key)
    {if(!decimal.TryParse(input[key]?.ToJsonString(),NumberStyles.Number,CultureInfo.InvariantCulture,out decimal value))throw new ApiError("قيمة رقمية غير صالحة.");return value;}
    public static void Extend(JsonObject doc,JsonObject input)
    {
        foreach(string key in TextFields)
            if(input.ContainsKey(key)){string value=input.S(key).Trim();J.Need(value.Length<=500,"حقل الشركة أطول من الحد المسموح.");doc[key]=value;}
        foreach(string key in GregorianFields)
            if(input.ContainsKey(key)){string value=input.S(key);J.Need(value==""||DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),"تاريخ السجل يجب أن يكون yyyy-MM-dd.");doc[key]=value;}
        foreach(string key in HijriFields)
            if(input.ContainsKey(key)){string value=input.S(key);J.Need(value==""||Regex.IsMatch(value,@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$"),"التاريخ الهجري غير صالح.");doc[key]=value;}
        if(input.ContainsKey("capitalSar")){decimal value=Number(input,"capitalSar");J.Need(value>=0&&decimal.Round(value,2)==value,"رأس المال غير صالح.");doc["capitalSar"]=value;}
        if(input.ContainsKey("crVersion")){decimal value=Number(input,"crVersion");J.Need(value>=0&&value==decimal.Truncate(value)&&value<=int.MaxValue,"نسخة السجل غير صالحة.");doc["crVersion"]=(int)value;}
        if(input.ContainsKey("activitiesIncomplete")){if(input["activitiesIncomplete"] is not JsonValue flag||!flag.TryGetValue<bool>(out bool incomplete))throw new ApiError("حالة اكتمال الأنشطة غير صالحة.");doc["activitiesIncomplete"]=incomplete;}
        if(input.ContainsKey("activities"))
        {
            var source=input["activities"] as JsonArray??throw new ApiError("أنشطة الشركة يجب أن تكون قائمة.");J.Need(source.Count<=200,"عدد الأنشطة يتجاوز الحد.");var activities=new JsonArray();var seen=new HashSet<string>();
            foreach(var item in source){var row=item as JsonObject??throw new ApiError("نشاط غير صالح.");string isic=row.S("isic"),name=row.S("nameAr").Trim();J.Need(Regex.IsMatch(isic,@"^[0-9]{6}$")&&name.Length is >0 and <=300&&seen.Add(isic),"رمز النشاط أو اسمه غير صالح أو مكرر.");activities.Add(new JsonObject{["isic"]=isic,["nameAr"]=name});}
            doc["activities"]=activities;
        }
        if(doc.S("issueDate")!=""&&doc.S("annualConfirmation")!="")J.Need(string.CompareOrdinal(doc.S("issueDate"),doc.S("annualConfirmation"))<=0,"التأكيد السنوي يجب ألا يسبق تاريخ القيد.");
    }
    static JsonArray Normalize(JsonObject input)
    {
        var source=(input["companies"]??input["rows"]) as JsonArray??throw new ApiError("قائمة الشركات مطلوبة.");J.Need(source.Count is >0 and <=100,"عدد الشركات غير صالح.");
        var rows=new JsonArray();var codes=new HashSet<string>();var unified=new HashSet<string>();var registrations=new HashSet<string>();
        foreach(var item in source)
        {
            var row=item as JsonObject??throw new ApiError("صف الشركة غير صالح.");var doc=new JsonObject();Extend(doc,row);string code=row.S("code").ToUpperInvariant();
            J.Need(FinancialCore.Sponsors.Any(s=>s.Code==code)&&codes.Add(code),"كود الشركة غير معروف أو مكرر.");
            J.Need(Regex.IsMatch(doc.S("unifiedNumber"),@"^[0-9]{10}$")&&unified.Add(doc.S("unifiedNumber")),"الرقم الوطني الموحد غير صالح أو مكرر.");
            J.Need(Regex.IsMatch(doc.S("crNumber"),@"^[0-9]{10}$")&&registrations.Add(doc.S("crNumber")),"رقم السجل غير صالح أو مكرر.");
            J.Need(doc.S("nameAr")!="","اسم الشركة مطلوب.");J.Need(doc.S("status","active") is "active" or "inactive","حالة الشركة غير صالحة.");
            doc["code"]=code;if(!doc.ContainsKey("status"))doc["status"]="active";rows.Add(doc);
        }
        return rows;
    }
    sealed record Change(string Kind,JsonObject Doc,bool IsNew,bool Changed);
    static List<Change> Plan(DbConnection c,DbTransaction? tx,JsonArray rows)
    {
        var companies=Store.List(c,"company",tx);var registrations=Store.List(c,"commercial_registration",tx);var plan=new List<Change>();
        foreach(var item in rows)
        {
            var row=item as JsonObject??throw new ApiError("صف الشركة غير صالح.");
            string sponsorId=FinancialCore.Sponsors.Single(s=>s.Code==row.S("code")).Id;
            var matches=companies.Where(x=>x.S("unifiedNumber")==row.S("unifiedNumber")).ToList();
            J.Need(matches.Count<=1&&matches.All(x=>x.S("id")==sponsorId),"الرقم الموحد مرتبط بدفتر آخر؛ راجع الربط.");
            var prior=companies.SingleOrDefault(x=>x.S("id")==sponsorId);
            J.Need(prior==null||prior.S("unifiedNumber")==""||prior.S("unifiedNumber")==row.S("unifiedNumber"),"تغيير هوية الشركة يتطلب مراجعة مستقلة.");
            var company=prior?.Clone()??new JsonObject{["id"]=sponsorId,["sponsorId"]=sponsorId};
            Extend(company,row);company["code"]=row.S("code");company["commercialReg"]=row.S("crNumber");company["registrySource"]="company-certificates";
            plan.Add(new("company",company,prior==null,!JsonNode.DeepEquals(prior,company)));
            var crMatches=registrations.Where(x=>x.S("unifiedNumber")==row.S("unifiedNumber")||x.S("number")==row.S("crNumber")).ToList();
            J.Need(crMatches.Count<=1&&crMatches.All(x=>x.S("companyId")==sponsorId),"السجل مرتبط بشركة أخرى أو مكرر.");
            var oldCr=crMatches.SingleOrDefault();var cr=oldCr?.Clone()??new JsonObject{["id"]="cr:unified:"+row.S("unifiedNumber"),["companyId"]=sponsorId,["company"]=sponsorId};
            Extend(cr,row);cr["number"]=row.S("crNumber");cr["registrySource"]="company-certificates";
            plan.Add(new("commercial_registration",cr,oldCr==null,!JsonNode.DeepEquals(oldCr,cr)));
        }
        return plan;
    }
    static JsonObject Counts(List<Change> changes)
    {
        var counts=new JsonObject();foreach(string kind in new[]{"company","commercial_registration"})counts[kind]=new JsonObject{["new"]=changes.Count(c=>c.Kind==kind&&c.IsNew),["changed"]=changes.Count(c=>c.Kind==kind&&!c.IsNew&&c.Changed),["unchanged"]=changes.Count(c=>c.Kind==kind&&!c.Changed)};return counts;
    }
    public static JsonObject Preview(DbConnection c,ClaimsPrincipal actor,JsonObject input)
    {
        Admin(actor);var rows=Normalize(input);return new(){["rows"]=rows,["fingerprint"]=FinancialImports.Issue(c,PreviewKind,rows,actor.Identity!.Name!),["counts"]=Counts(Plan(c,null,rows))};
    }
    public static JsonObject Commit(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        Admin(actor);FinancialImports.Verify(c,tx,input,PreviewKind,actor.Identity!.Name!);var rows=Normalize(input);var plan=Plan(c,tx,rows);
        foreach(var change in plan.Where(x=>x.Changed)){var doc=change.Doc.Clone();if(change.IsNew)doc["createdAt"]=DateTimeOffset.UtcNow.ToString("O");doc["updatedAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,change.Kind,doc,tx);Store.Audit(c,tx,"company.certificates_imported",doc.S("id"),actor.Identity!.Name!);}
        return new(){["counts"]=Counts(plan)};
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/companies/import/preview",(JsonObject input,HttpContext ctx)=>store.Read(c=>Preview(c,ctx.User,input))).WithTags("Companies and registrations").WithSummary("Preview signed company certificates; administrator only");
        api.MapPost("/companies/import/commit",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Commit(c,tx,ctx.User,input))).WithTags("Companies and registrations").WithSummary("Commit signed company certificates with audit; administrator only");
    }
}
