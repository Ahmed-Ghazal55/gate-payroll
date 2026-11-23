using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Payroll;

public static class SubmissionChoices
{
    public static int Version(JsonObject d) => (int)d.N("version", 1);
    /// <summary>Ghazal 2026-11-23: a rider may edit a work-system choice at most 3 times per form cycle.
    /// A new monthly link starts a new campaign (new count); closing and reopening the same link starts a new cycle.</summary>
    public const int MaxEdits = 3;
    public static int Cycle(JsonObject campaign) => (int)campaign.N("cycle", 1);
    public static int EditsUsed(JsonObject d, JsonObject campaign) => (int)d.N("editCycle", 1) == Cycle(campaign) ? (int)d.N("editCount", 0) : 0;
    public static int EditsLeft(JsonObject d, JsonObject campaign) => Math.Max(0, MaxEdits - EditsUsed(d, campaign));
    /// <summary>Staff projection: drop the edit-token hash and mask IBAN (and history) unless the caller has <see cref="WorkforceApi.PermIban"/>.</summary>
    public static JsonObject PublicAdmin(JsonObject d, ClaimsPrincipal? user = null)
    {
        var result = d.Clone();
        result.Remove("editTokenHash");
        if (user == null || AccessPolicy.Has(user, WorkforceApi.PermIban)) return result;
        MaskIban(result);
        if (result["history"] is JsonArray history)
            foreach (var item in history.OfType<JsonObject>()) MaskIban(item);
        return result;
    }
    static void MaskIban(JsonObject d)
    {
        string iban = d.S("iban");
        if (iban == "") return;
        d["iban"] = iban.Length >= 4 ? "••••" + iban[^4..] : "••••";
    }
    /// <summary>See responses of every company when the staff account has no company scope.</summary>
    public const string PermAllCompanies = "forms.responses.allCompanies";
    /// <summary>
    /// Company scope for a submission/custom-response iqama.
    /// Unscoped staff (no <c>finance.sponsorIds</c>) see all responses only with <see cref="PermAllCompanies"/>; without it they see nothing.
    /// Company-scoped callers must match <see cref="FinancialScopes.Rider"/> (out of scope → 404).
    /// </summary>
    public static bool InRiderScope(DbConnection c, ClaimsPrincipal user, string iqama, DbTransaction? tx = null)
    {
        if (AccessPolicy.Administrator(user) || FinancialScopes.AllCompanies(user)) return true;
        // No sponsor claims: need the dedicated all-companies permission; otherwise empty lists / 404.
        if (!user.FindAll("finance.sponsorIds").Any())
            return AccessPolicy.Has(user, PermAllCompanies);
        // Custom-form answers without an iqama field are not company-scoped for scoped staff.
        if (iqama == "") return true;
        var rider = Store.Get(c, iqama, "rider", tx);
        if (rider == null) return false;
        return FinancialScopes.Rider(user, rider);
    }
    public static void DemandInScope(DbConnection c, ClaimsPrincipal user, JsonObject submission, DbTransaction? tx = null)
    {
        if (!InRiderScope(c, user, submission.S("iqama"), tx))
            throw new ApiError("الرد غير موجود.", 404);
    }
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
    /// <summary>Issue a one-time-readable edit token; only the hash is stored on the submission.</summary>
    public static string IssueEditToken(JsonObject d)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        d["editTokenHash"] = Hash(token);
        return token;
    }
    /// <summary>Constant-time edit-token check; missing or wrong token → 404 (no existence leak).</summary>
    public static void RequireEditToken(JsonObject d, string editToken)
    {
        string hash = d.S("editTokenHash");
        if (editToken == "" || hash == "" || !Equal(hash, Hash(editToken)))
            throw new ApiError("الرد غير موجود.", 404);
    }
    static JsonObject Owned(DbConnection c,DbTransaction tx,JsonObject campaign,JsonObject input)
    {
        // Compatibility for old clients that still call /resume with receipt + editToken.
        var d=Store.Get(c,input.S("receipt"),"submission",tx);
        if(d==null || d.S("campaignId")!=campaign.S("id"))
            throw new ApiError("الرد غير موجود.",404);
        RequireEditToken(d, input.S("editToken"));
        return d;
    }
    internal static JsonObject Open(DbConnection c,DbTransaction tx,JsonObject campaign)
    {
        var current=Store.Get(c,campaign.S("id"),"campaign",tx)??throw new ApiError("النموذج غير موجود.",404);
        J.Need(current.S("status")=="open" && DateTimeOffset.Parse(current.S("expiresAt"),CultureInfo.InvariantCulture)>DateTimeOffset.UtcNow,"النموذج مغلق أو انتهت صلاحيته.");return current;
    }
    static string Iqama(JsonObject input)
    {
        string iqama=input.Required("iqama");
        J.Need(System.Text.RegularExpressions.Regex.IsMatch(iqama,"^[0-9]{10}$"),"رقم الإقامة يجب أن يكون10أرقام إنجليزية فقط.");return iqama;
    }
    internal static JsonObject? Find(DbConnection c,DbTransaction tx,JsonObject campaign,string iqama) => Store.List(c,"submission",tx).SingleOrDefault(d=>d.S("campaignId")==campaign.S("id") && d.S("iqama")==iqama);
    public static JsonObject ChoiceView(JsonObject d,JsonObject? campaign=null)
    {
        // Public lookup never returns phone or IBAN of any rider (H20 / public edit IDOR).
        var result=new JsonObject() { ["alreadySubmitted"]=true,["iqama"]=d.S("iqama"),["name"]=d.S("name"),["currentChoice"]=d.S("workSystem"),["version"]=Version(d),["message"]="سبق لك تسجيل اختيار. يمكنك تعديله أدناه مباشرة." };
        if(campaign!=null){result["editsLeft"]=EditsLeft(d,campaign);result["maxEdits"]=MaxEdits;}
        foreach(string f in campaign==null?[]:CampaignOperations.Fields(campaign))
            if(f is not ("phone" or "iban")) result[f]=d.S(f);
        return result;
    }
    static JsonObject Receipt(JsonObject d, string? editToken = null, JsonObject? campaign = null)
    {
        var result = new JsonObject { ["receipt"]=d.S("id"),["iqama"]=d.S("iqama"),["name"]=d.S("name"),["workSystem"]=d.S("workSystem"),["isEdited"]=d.B("isEdited"),["version"]=Version(d),["status"]=d.S("status"),["submittedAt"]=d.S("updatedAt",d.S("createdAt")),["message"]=d.B("isEdited")?"تم تعديل اختيارك بنجاح.":"تم تسجيل اختيارك بنجاح." };
        if (editToken != null) result["editToken"] = editToken;
        if (campaign != null) { result["editsLeft"] = EditsLeft(d, campaign); result["maxEdits"] = MaxEdits; }
        return result;
    }
    public static void Map(WebApplication app,Store store,Func<string,JsonObject> campaignLookup)
    {
        // Lookup is iqama-based and never returns phone/IBAN. Edits require the per-submission editToken.
        app.MapPost("/api/public/forms/{token}/lookup",(string token,JsonObject input)=>
        {
            J.Need(input.All(p=>p.Key=="iqama"),"حقول الاستعلام غير صحيحة.");string iqama=Iqama(input);var campaign=campaignLookup(token);
            return store.Write<IResult>((c,tx)=>{campaign=Open(c,tx,campaign);var d=Find(c,tx,campaign,iqama);return Results.Ok(d==null?new JsonObject { ["alreadySubmitted"]=false,["iqama"]=iqama }:IbanChoices.Is(campaign)?IbanChoices.View(d):ChoiceView(d,campaign));},invalidateSummaries:false);
        }).WithTags("Forms and responses").WithSummary("Submit public forms {token} lookup").RequireRateLimiting("sensitive");
        app.MapPost("/api/public/forms/{token}",(string token,JsonObject input)=>
        {
            var campaign=campaignLookup(token);if(IbanChoices.Is(campaign))return IbanChoices.Create(store,campaign,input);
            var choice=WorkChoices.Validate(new JsonObject(input.Where(p=>p.Key is not ("city" or "phone")).Select(p=>new KeyValuePair<string,JsonNode?>(p.Key,p.Value?.DeepClone()))));
            return store.Write<IResult>((c,tx)=>
            {
                campaign=Open(c,tx,campaign);var existing=Find(c,tx,campaign,choice.Iqama);
                if(existing!=null)return Results.Json(ApiErrors.Duplicate(ChoiceView(existing,campaign)),statusCode:409);
                var answers=CampaignOperations.Answers(campaign,input);
                var d=new JsonObject { ["id"]=Guid.NewGuid().ToString("N"),["campaignId"]=campaign.S("id"),["schema"]="work-choice-v1",["iqama"]=choice.Iqama,["name"]=choice.Name,["workSystem"]=choice.WorkSystem,["status"]="pending",["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["version"]=1,["isEdited"]=false,["history"]=new JsonArray() };
                foreach(var field in answers)d[field.Key]=field.Value?.DeepClone();
                string editToken=IssueEditToken(d);
                Store.Save(c,"submission",d,tx);d["editCycle"]=Cycle(campaign);d["editCount"]=0;
                Store.Audit(c,tx,"submission.received",d.S("id"),"public-form");return Results.Ok(Receipt(d, editToken, campaign));
            });
        }).WithTags("Forms and responses").WithSummary("Submit public forms {token}").RequireRateLimiting("sensitive");
        // Retain the v0.5.1 recovery route for already-loaded legacy clients only.
        app.MapPost("/api/public/forms/{token}/resume",(string token,JsonObject input)=>
        {
            J.Need(input.All(p=>p.Key is "receipt" or "editToken"),"حقول طلب التعديل غير صحيحة.");var campaign=campaignLookup(token);
            return store.Write((c,tx)=>{Open(c,tx,campaign);var d=Owned(c,tx,campaign,input);var result=Receipt(d, input.S("editToken"));return Results.Ok(result);},invalidateSummaries:false);
        }).WithTags("Forms and responses").WithSummary("Submit public forms {token} resume").RequireRateLimiting("sensitive");
        app.MapPost("/api/public/forms/{token}/edit",(string token,JsonObject input)=>
        {
            var campaign=campaignLookup(token);if(IbanChoices.Is(campaign))return IbanChoices.Edit(store,campaign,input);
            J.Need(input.All(p=>p.Key is "receipt" or "editToken" or "version" or "iqama" or "name" or "workSystem" or "city" or "phone"),"حقول طلب التعديل غير صحيحة.");
            var choice=WorkChoices.Validate(new JsonObject { ["iqama"]=input.S("iqama"),["name"]=input.S("name"),["workSystem"]=input.S("workSystem") });
            return store.Write((c,tx)=>
            {
                campaign=Open(c,tx,campaign);var answers=CampaignOperations.Answers(campaign,input);var d=Find(c,tx,campaign,choice.Iqama)??throw new ApiError("الرد غير موجود.",404);
                RequireEditToken(d, input.S("editToken"));
                J.Need(input.S("receipt")=="" || input.S("receipt")==d.S("id"),"رقم الإيصال لا يطابق الإقامة والحملة.");CheckVersion(d,input);
                bool changed=d.S("name")!=choice.Name || d.S("workSystem")!=choice.WorkSystem || answers.Any(p=>d.S(p.Key)!=p.Value?.ToString());
                if(changed){int used=EditsUsed(d,campaign);if(used>=MaxEdits)return Results.Json(new JsonObject{["error"]="وصلت للحد الأقصى لتعديل اختيارك ("+MaxEdits+" مرات) في هذا النموذج.",["code"]="edit_limit",["editsLeft"]=0,["maxEdits"]=MaxEdits},statusCode:409);
                    ArchiveAndEdit(d,choice);foreach(var field in answers)d[field.Key]=field.Value?.DeepClone();d["editCycle"]=Cycle(campaign);d["editCount"]=used+1;Store.Audit(c,tx,"submission.edited",d.S("id")+"#"+(used+1),"public-form");Store.Save(c,"submission",d,tx);}
                return Results.Ok(Receipt(d, input.S("editToken"), campaign));
            });
        }).WithTags("Forms and responses").WithSummary("Submit public forms {token} edit").RequireRateLimiting("sensitive");
    }
}
