using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Payroll;

public static class SubmissionChoices
{
    public static int Version(JsonObject d) => (int)d.N("version", 1);
    public static JsonObject PublicAdmin(JsonObject d) { var result=d.Clone(); result.Remove("editTokenHash"); return result; }
    public static void CheckVersion(JsonObject d, JsonObject input)
    {
        if (input.ContainsKey("version") && input.N("version") != Version(d))
            throw new ApiError("تغير الرد منذ فتحه؛ حدّث البيانات وراجع النسخة الحالية.", 409);
    }
    public static void ArchiveAndEdit(JsonObject d, WorkChoice choice)
    {
        var history=d["history"] as JsonArray;
        if (history==null) { history=new JsonArray(); d["history"]=history; }
        history.Add(new JsonObject { ["workSystem"]=d.S("workSystem"),["name"]=d.S("name"),["submittedAt"]=d.S("updatedAt",d.S("createdAt")),["version"]=Version(d),["status"]=d.S("status"),["reviewNote"]=d.S("reviewNote"),["reviewedAt"]=d.S("reviewedAt"),["reviewedBy"]=d.S("reviewedBy"),["city"]=d.S("city"),["phone"]=d.S("phone") });
        d["name"]=choice.Name;d["workSystem"]=choice.WorkSystem;d["version"]=Version(d)+1;d["isEdited"]=true;d["status"]="pending";d["updatedAt"]=DateTimeOffset.UtcNow.ToString("O");
        foreach(string field in new[]{"reviewNote","reviewedAt","reviewedBy"})d.Remove(field);
    }
    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    static bool Equal(string a,string b) => a.Length==b.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a),Encoding.UTF8.GetBytes(b));
    static JsonObject Owned(DbConnection c,DbTransaction tx,JsonObject campaign,JsonObject input)
    {
        // Compatibility for old clients; the current form uses Iqama-based lookup.
        var d=Store.Get(c,input.S("receipt"),"submission",tx);
        if(d==null || d.S("campaignId")!=campaign.S("id") || (d.S("editTokenHash")!="" && !Equal(d.S("editTokenHash"),Hash(input.S("editToken")))))
            throw new ApiError("تعذر التحقق من رمز تعديل الرد. استخدم بيانات المتابعة الأصلية أو تواصل مع الإدارة.",403);
        return d;
    }
    static JsonObject Open(DbConnection c,DbTransaction tx,JsonObject campaign)
    {
        var current=Store.Get(c,campaign.S("id"),"campaign",tx)??throw new ApiError("النموذج غير موجود.",404);
        J.Need(current.S("status")=="open" && DateTimeOffset.Parse(current.S("expiresAt"),CultureInfo.InvariantCulture)>DateTimeOffset.UtcNow,"النموذج مغلق أو انتهت صلاحيته.");return current;
    }
    static string Iqama(JsonObject input)
    {
        string iqama=input.Required("iqama");
        J.Need(System.Text.RegularExpressions.Regex.IsMatch(iqama,"^[0-9]{10}$"),"رقم الإقامة يجب أن يكون10أرقام إنجليزية فقط.");return iqama;
    }
    static JsonObject? Find(DbConnection c,DbTransaction tx,JsonObject campaign,string iqama) => Store.List(c,"submission",tx).SingleOrDefault(d=>d.S("campaignId")==campaign.S("id") && d.S("iqama")==iqama);
    public static JsonObject ChoiceView(JsonObject d,JsonObject? campaign=null)
    { var result=new JsonObject() { ["alreadySubmitted"]=true,["iqama"]=d.S("iqama"),["name"]=d.S("name"),["currentChoice"]=d.S("workSystem"),["version"]=Version(d),["message"]="سبق لك تسجيل اختيار. يمكنك تعديله أدناه مباشرة." };
        foreach(string f in campaign==null?[]:CampaignOperations.Fields(campaign))result[f]=d.S(f);return result;
    }
    static JsonObject Receipt(JsonObject d) => new() { ["receipt"]=d.S("id"),["iqama"]=d.S("iqama"),["name"]=d.S("name"),["workSystem"]=d.S("workSystem"),["isEdited"]=d.B("isEdited"),["version"]=Version(d),["status"]=d.S("status"),["submittedAt"]=d.S("updatedAt",d.S("createdAt")),["message"]=d.B("isEdited")?"تم تعديل اختيارك بنجاح.":"تم تسجيل اختيارك بنجاح." };
    public static void Map(WebApplication app,Store store,Func<string,JsonObject> campaignLookup)
    {
        // The campaign explicitly permits Iqama-based choice lookup/editing (v0.5.2).
        // Never project directory, financial, bank or historical review data here.
        app.MapPost("/api/public/forms/{token}/lookup",(string token,JsonObject input)=>
        {
            J.Need(input.All(p=>p.Key=="iqama"),"حقول الاستعلام غير صحيحة.");string iqama=Iqama(input);var campaign=campaignLookup(token);
            return store.Write<IResult>((c,tx)=>{campaign=Open(c,tx,campaign);var d=Find(c,tx,campaign,iqama);return Results.Ok(d==null?new JsonObject { ["alreadySubmitted"]=false,["iqama"]=iqama }:ChoiceView(d,campaign));},invalidateSummaries:false);
        }).RequireRateLimiting("sensitive");
        app.MapPost("/api/public/forms/{token}",(string token,JsonObject input)=>
        {
            var campaign=campaignLookup(token);var choice=WorkChoices.Validate(new JsonObject(input.Where(p=>p.Key is not ("city" or "phone")).Select(p=>new KeyValuePair<string,JsonNode?>(p.Key,p.Value?.DeepClone()))));
            return store.Write<IResult>((c,tx)=>
            {
                campaign=Open(c,tx,campaign);var existing=Find(c,tx,campaign,choice.Iqama);
                if(existing!=null)return Results.Json(ChoiceView(existing,campaign),statusCode:409);
                var answers=CampaignOperations.Answers(campaign,input);
                var d=new JsonObject { ["id"]=Guid.NewGuid().ToString("N"),["campaignId"]=campaign.S("id"),["schema"]="work-choice-v1",["iqama"]=choice.Iqama,["name"]=choice.Name,["workSystem"]=choice.WorkSystem,["status"]="pending",["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["version"]=1,["isEdited"]=false,["history"]=new JsonArray() };
                foreach(var field in answers)d[field.Key]=field.Value?.DeepClone();
                Store.Save(c,"submission",d,tx);Store.Audit(c,tx,"submission.received",d.S("id"),"public-form");return Results.Ok(Receipt(d));
            });
        }).RequireRateLimiting("sensitive");
        // Retain the v0.5.1 recovery route for already-loaded legacy clients only.
        app.MapPost("/api/public/forms/{token}/resume",(string token,JsonObject input)=>
        {
            J.Need(input.All(p=>p.Key is "receipt" or "editToken"),"حقول طلب التعديل غير صحيحة.");var campaign=campaignLookup(token);
            return store.Write((c,tx)=>{Open(c,tx,campaign);var d=Owned(c,tx,campaign,input);var result=Receipt(d);result["editToken"]=input.S("editToken");return Results.Ok(result);},invalidateSummaries:false);
        }).RequireRateLimiting("sensitive");
        app.MapPost("/api/public/forms/{token}/edit",(string token,JsonObject input)=>
        {
            // editToken is accepted but ignored for legacy callers; it is never required or issued.
            J.Need(input.All(p=>p.Key is "receipt" or "editToken" or "version" or "iqama" or "name" or "workSystem" or "city" or "phone"),"حقول طلب التعديل غير صحيحة.");
            var campaign=campaignLookup(token);var choice=WorkChoices.Validate(new JsonObject { ["iqama"]=input.S("iqama"),["name"]=input.S("name"),["workSystem"]=input.S("workSystem") });
            return store.Write((c,tx)=>
            {
                campaign=Open(c,tx,campaign);var answers=CampaignOperations.Answers(campaign,input);var d=Find(c,tx,campaign,choice.Iqama)??throw new ApiError("لم يتم العثور على رد سابق لتعديله.",404);
                J.Need(input.S("receipt")=="" || input.S("receipt")==d.S("id"),"رقم الإيصال لا يطابق الإقامة والحملة.");CheckVersion(d,input);
                bool changed=d.S("name")!=choice.Name || d.S("workSystem")!=choice.WorkSystem || answers.Any(p=>d.S(p.Key)!=p.Value?.ToString());
                if(changed){ArchiveAndEdit(d,choice);foreach(var field in answers)d[field.Key]=field.Value?.DeepClone();Store.Audit(c,tx,"submission.edited",d.S("id"),"public-form");Store.Save(c,"submission",d,tx);}
                return Results.Ok(Receipt(d));
            });
        }).RequireRateLimiting("sensitive");
    }
}
