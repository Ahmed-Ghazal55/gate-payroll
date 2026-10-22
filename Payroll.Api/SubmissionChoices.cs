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
        history.Add(new JsonObject { ["workSystem"]=d.S("workSystem"),["name"]=d.S("name"),["submittedAt"]=d.S("updatedAt",d.S("createdAt")),["version"]=Version(d),["status"]=d.S("status"),["reviewNote"]=d.S("reviewNote"),["reviewedAt"]=d.S("reviewedAt"),["reviewedBy"]=d.S("reviewedBy") });
        d["name"]=choice.Name;d["workSystem"]=choice.WorkSystem;d["version"]=Version(d)+1;d["isEdited"]=true;d["status"]="pending";d["updatedAt"]=DateTimeOffset.UtcNow.ToString("O");
        foreach(string field in new[]{"reviewNote","reviewedAt","reviewedBy"})d.Remove(field);
    }
    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    static string IssueToken(JsonObject d)
    {
        string token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();d["editTokenHash"]=Hash(token);return token;
    }
    static bool Equal(string a,string b) => a.Length==b.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a),Encoding.UTF8.GetBytes(b));
    static JsonObject Owned(DbConnection c,DbTransaction tx,JsonObject campaign,JsonObject input)
    {
        // Receipts issued before v0.5.1 are secret random capabilities. Never disclose them on a duplicate lookup.
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
    public static void Map(WebApplication app,Store store,Func<string,JsonObject> campaignLookup)
    {
        app.MapPost("/api/public/forms/{token}",(string token,JsonObject input)=>
        {
            var campaign=campaignLookup(token);var choice=WorkChoices.Validate(input);
            return store.Write<IResult>((c,tx)=>
            {
                Open(c,tx,campaign);
                if(Store.List(c,"submission",tx).Any(x=>x.S("campaignId")==campaign.S("id") && x.S("iqama")==choice.Iqama))
                    return Results.Json(new { alreadySubmitted=true,message="سبق لك تسجيل اختيار. يمكنك تعديل اختيارك باستخدام رمز المتابعة الخاص." },statusCode:409);
                var d=new JsonObject { ["id"]=Guid.NewGuid().ToString("N"),["campaignId"]=campaign.S("id"),["schema"]="work-choice-v1",["iqama"]=choice.Iqama,["name"]=choice.Name,["workSystem"]=choice.WorkSystem,["status"]="pending",["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["version"]=1,["isEdited"]=false,["history"]=new JsonArray() };
                string editToken=IssueToken(d);Store.Save(c,"submission",d,tx);Store.Audit(c,tx,"submission.received",d.S("id"),"public-form");
                return Results.Ok(new { receipt=d.S("id"),editToken,version=1,message="تم تسجيل اختيارك بنجاح." });
            });
        }).RequireRateLimiting("sensitive");
        app.MapPost("/api/public/forms/{token}/resume",(string token,JsonObject input)=>
        {
            J.Need(input.All(p=>p.Key is "receipt" or "editToken"),"حقول طلب التعديل غير صحيحة.");var campaign=campaignLookup(token);
            return store.Write((c,tx)=> { Open(c,tx,campaign);var d=Owned(c,tx,campaign,input);string editToken=input.S("editToken");if(d.S("editTokenHash")==""){editToken=IssueToken(d);Store.Save(c,"submission",d,tx);Store.Audit(c,tx,"submission.edit_access_upgraded",d.S("id"),"public-form");}return Results.Ok(new { receipt=d.S("id"),editToken,iqama=d.S("iqama"),name=d.S("name"),workSystem=d.S("workSystem"),version=Version(d),isEdited=d.B("isEdited") }); });
        }).RequireRateLimiting("sensitive");
        app.MapPost("/api/public/forms/{token}/edit",(string token,JsonObject input)=>
        {
            J.Need(input.All(p=>p.Key is "receipt" or "editToken" or "version" or "iqama" or "name" or "workSystem"),"حقول طلب التعديل غير صحيحة.");
            var campaign=campaignLookup(token);var choice=WorkChoices.Validate(new JsonObject { ["iqama"]=input.S("iqama"),["name"]=input.S("name"),["workSystem"]=input.S("workSystem") });
            return store.Write((c,tx)=>
            {
                Open(c,tx,campaign);var d=Owned(c,tx,campaign,input);J.Need(d.S("iqama")==choice.Iqama,"لا يمكن تغيير إقامة الرد.");CheckVersion(d,input);
                bool changed=d.S("name")!=choice.Name || d.S("workSystem")!=choice.WorkSystem;
                if(changed){ArchiveAndEdit(d,choice);Store.Audit(c,tx,"submission.edited",d.S("id"),"public-form");}
                string editToken=input.S("editToken");if(d.S("editTokenHash")=="")editToken=IssueToken(d);
                Store.Save(c,"submission",d,tx);
                return Results.Ok(new { receipt=d.S("id"),editToken,isEdited=d.B("isEdited"),version=Version(d),message="تم تعديل اختيارك بنجاح." });
            });
        }).RequireRateLimiting("sensitive");
    }
}
