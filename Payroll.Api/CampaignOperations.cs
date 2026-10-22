using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;

namespace Payroll;

public static class CampaignOperations
{
    public static string[] Fields(JsonObject campaign) => (campaign["fields"] as JsonArray)?.Select(x=>x?.ToString()??"").ToArray() ?? [];
    public static JsonArray ValidateFields(JsonNode? value)
    {
        J.Need(value is JsonArray, "قائمة حقول النموذج غير صحيحة.");
        var fields=((JsonArray)value!).Select(x=>x?.ToString()??"").ToArray();
        J.Need(fields.Length<=2 && fields.Distinct().Count()==fields.Length && fields.All(x=>x is "city" or "phone"),"الحقول الإضافية المدعومة: المدينة والجوال فقط.");
        return new JsonArray(fields.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray());
    }
    public static JsonObject Answers(JsonObject campaign,JsonObject input)
    {
        var result=new JsonObject();
        foreach(string field in Fields(campaign))
        {
            string value=input.Required(field).Trim();
            if(field=="city") J.Need(value.Length<=100 && !value.Any(char.IsControl),"اسم المدينة مطلوب وبحد أقصى100حرف.");
            else value=RiderDirectory.Phone(value);
            result[field]=value;
        }
        J.Need(new[]{"city","phone"}.All(f=>!input.ContainsKey(f)||Fields(campaign).Contains(f)),"يوجد حقل غير مفعّل في النموذج.");
        return result;
    }
    public static void Review(DbConnection c,DbTransaction tx,JsonObject d,JsonObject input,string actor)
    {
        J.Need(d.S("status")=="pending","تمت مراجعة الرد بالفعل.");SubmissionChoices.CheckVersion(d,input);
        string status=input.Required("status");J.Need(status is "approved" or "rejected","حالة مراجعة غير صحيحة.");
        if(status=="approved")
        {
            J.Need(input.NullableBool("identityReviewed")!=false,"أكد مراجعة هوية المندوب والاسم خارج النموذج.");
            if(d.S("iban")!="")J.Need(input.B("ibanOwnershipReviewed"),"أكد مراجعة ملكية الآيبان؛ فحص الصيغة لا يثبت صاحب الحساب.");
            var rider=Store.Get(c,d.S("iqama"),"rider",tx)?.Clone()??new JsonObject{["id"]=d.S("iqama"),["iqama"]=d.S("iqama"),["name"]=d.S("name"),["policyId"]="",["status"]="needs_review"};
            rider["workPreference"]=d.S("workSystem");rider["workPreferenceSubmissionId"]=d.S("id");
            foreach(string field in new[]{"phone","city"})if(d.S(field)!="")rider[field]=d.S(field);
            if(d.S("vehicle")!="")rider["vehiclePreference"]=d.S("vehicle");
            if(d.S("housing")!="")rider["housingPreference"]=d.S("housing");
            if(d.S("iban")!=""){rider["verifiedIban"]=d.S("iban");rider["ibanReviewedAt"]=DateTimeOffset.UtcNow.ToString("O");}
            Store.Save(c,"rider",rider,tx);
        }
        d["status"]=status;d["reviewNote"]=input.S("note");d["reviewedAt"]=DateTimeOffset.UtcNow.ToString("O");d["reviewedBy"]=actor;
        Store.Save(c,"submission",d,tx);Store.Audit(c,tx,"submission."+status,d.S("id"),actor);
    }
    static void Admin(HttpContext ctx){if(ctx.User.FindFirstValue(ClaimTypes.Role)!="Admin")throw new ApiError("الحذف وتعديل الحملات لمسؤول النظام فقط.",403);}
    static string Actor(HttpContext ctx)=>ctx.User.Identity?.Name??"administrator";
    internal static void DeleteSubmissions(DbConnection c,DbTransaction tx,List<JsonObject> rows,string actor)
    {
        var all=Store.List(c,"submission",tx);var ids=rows.Select(d=>d.S("id")).ToHashSet();
        foreach(var d in rows){using var cmd=Store.Command(c,tx,"DELETE FROM records WHERE id=@id AND kind='submission'",("@id",d.S("id")));cmd.ExecuteNonQuery();Store.Audit(c,tx,"submission.deleted",d.S("id"),actor);}
        foreach(string iqama in rows.Select(d=>d.S("iqama")).Distinct())
        {
            var rider=Store.Get(c,iqama,"rider",tx);if(rider==null)continue;
            var latest=all.Where(d=>d.S("iqama")==iqama&&d.S("status")=="approved").OrderByDescending(d=>d.S("reviewedAt",d.S("createdAt"))).ThenByDescending(d=>d.S("id")).FirstOrDefault();
            bool owned=ids.Contains(rider.S("workPreferenceSubmissionId")) || (rider.S("workPreferenceSubmissionId")=="" && latest!=null && ids.Contains(latest.S("id")) && rider.S("workPreference")==latest.S("workSystem"));
            if(!owned)continue;
            var next=all.Where(d=>d.S("iqama")==iqama&&d.S("status")=="approved"&&!ids.Contains(d.S("id"))).OrderByDescending(d=>d.S("reviewedAt",d.S("createdAt"))).ThenByDescending(d=>d.S("id")).FirstOrDefault();
            rider["workPreference"]=next?.S("workSystem")??"";rider["workPreferenceSubmissionId"]=next?.S("id")??"";Store.Save(c,"rider",rider,tx);
        }
    }
    static List<JsonObject> Scope(DbConnection c,DbTransaction tx,JsonObject input,bool requireCampaign)
    {
        string campaignId=input.S("campaignId");J.Need(!requireCampaign||campaignId!="","حدد الحملة قبل الحذف الجماعي.");
        if(campaignId!="")_ = Store.Get(c,campaignId,"campaign",tx)??throw new ApiError("الحملة غير موجودة.",404);
        var rows=Store.List(c,"submission",tx).Where(d=>campaignId==""||d.S("campaignId")==campaignId).ToList();
        if(input.ContainsKey("ids"))
        {
            J.Need(input["ids"] is JsonArray,"قائمة الردود غير صحيحة.");var ids=((JsonArray)input["ids"]!).Select(x=>x?.ToString()??"").ToHashSet();
            J.Need(ids.Count is >0 and <=10000 && ids.All(id=>rows.Any(r=>r.S("id")==id)),"تغير نطاق الردود؛ حدّث القائمة قبل المحاولة.");rows=rows.Where(d=>ids.Contains(d.S("id"))).ToList();
        }
        if(input.ContainsKey("versions"))
        {
            J.Need(input["versions"] is JsonObject,"نسخ الردود غير صحيحة.");var versions=(JsonObject)input["versions"]!;
            foreach(var row in rows){J.Need(versions.ContainsKey(row.S("id")),"أعد تحميل نسخ الردود قبل الإجراء الجماعي.");SubmissionChoices.CheckVersion(row,new(){["version"]=versions[row.S("id")]?.DeepClone()});}
        }
        return rows;
    }
    public static object Analytics(DbConnection c,string id)
    {
        _=Store.Get(c,id,"campaign")??throw new ApiError("الحملة غير موجودة.",404);
        var rows=Store.List(c,"submission").Where(d=>d.S("campaignId")==id).ToList();
        var riders=Store.List(c,"rider").ToDictionary(d=>d.S("iqama"));
        object[] Distribution(Func<JsonObject,string> key)=>rows.GroupBy(key).Select(g=>(object)new{value=g.Key,count=g.Count(),percentage=rows.Count==0?0:Math.Round(100m*g.Count()/rows.Count,2)}).ToArray();
        return new{campaignId=id,totalResponses=rows.Count,approved=rows.Count(d=>d.S("status")=="approved"),pending=rows.Count(d=>d.S("status")=="pending"),rejected=rows.Count(d=>d.S("status")=="rejected"),edited=rows.Count(d=>d.B("isEdited")),
            workSystem=Distribution(d=>d.S("workSystem")),city=Distribution(d=>d.S("city")!=""?d.S("city"):(riders.GetValueOrDefault(d.S("iqama"))?.S("city") is string city && city!=""?city:"غير محدد")),
            engagementType=Distribution(d=>{string value=riders.GetValueOrDefault(d.S("iqama"))?.S("engagementType")??"";return value is "SponsoredInternal" or "External"?value:"Unknown";}),
            recent=rows.OrderByDescending(d=>d.S("updatedAt",d.S("createdAt"))).ThenBy(d=>d.S("id")).Take(5).Select(d=>new{id=d.S("id"),name=d.S("name"),iqama=d.S("iqama"),workSystem=d.S("workSystem"),status=d.S("status"),createdAt=d.S("createdAt"),updatedAt=d.S("updatedAt"),isEdited=d.B("isEdited")})};
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPut("/campaigns/{id}",(string id,JsonObject input,HttpContext ctx)=>{Admin(ctx);return store.Write((c,tx)=>{
            J.Need(input.All(p=>p.Key is "title" or "expiresAt" or "status" or "fields"),"حقول تعديل النموذج غير صحيحة.");var d=Store.Get(c,id,"campaign",tx)??throw new ApiError("النموذج غير موجود.",404);
            if(input.ContainsKey("title")){string title=input.Required("title");J.Need(title.Length<=200,"العنوان طويل جدًا.");d["title"]=title;}
            if(input.ContainsKey("expiresAt")){J.Need(DateTimeOffset.TryParse(input.S("expiresAt"),CultureInfo.InvariantCulture,DateTimeStyles.None,out var expiry),"تاريخ الانتهاء غير صالح.");d["expiresAt"]=expiry.ToUniversalTime().ToString("O");}
            if(input.ContainsKey("status")){J.Need(input.S("status") is "open" or "closed","حالة النموذج غير صحيحة.");d["status"]=input.S("status");}
            if(input.ContainsKey("fields"))d["fields"]=ValidateFields(input["fields"]);d["updatedAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,"campaign",d,tx);Store.Audit(c,tx,"campaign.updated",id,Actor(ctx));return new{ok=true};});});
        api.MapGet("/campaigns/{id}/analytics",(string id)=>store.Read(c=>Analytics(c,id)));
        api.MapDelete("/submissions/{id}",(string id,HttpContext ctx)=>{Admin(ctx);return store.Write((c,tx)=>{var d=Store.Get(c,id,"submission",tx)??throw new ApiError("الرد غير موجود.",404);DeleteSubmissions(c,tx,[d],Actor(ctx));return new{ok=true,message="تم حذف الرد نهائيًا ويمكن للمندوب التسجيل مجددًا."};});});
        api.MapPost("/submissions/bulk-delete",(JsonObject input,HttpContext ctx)=>{Admin(ctx);J.Need(input.B("confirm"),"أكد الحذف النهائي أولًا.");return store.Write((c,tx)=>{var rows=Scope(c,tx,input,true);DeleteSubmissions(c,tx,rows,Actor(ctx));Store.Audit(c,tx,"submission.bulk_deleted",input.S("campaignId"),Actor(ctx));return new{ok=true,count=rows.Count};});});
        api.MapPost("/submissions/bulk-approve",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>{
            var rows=Scope(c,tx,input,false).Where(d=>d.S("status")=="pending").ToList();
            // Bulk approval cannot attest ownership of legacy banking details.
            J.Need(rows.All(d=>d.S("iban")==""),"تتضمن الدفعة آيبانًا قديمًا؛ راجع ملكيته فرديًا قبل الاعتماد الجماعي.");
            foreach(var d in rows)Review(c,tx,d,new(){["status"]="approved",["identityReviewed"]=true},Actor(ctx));Store.Audit(c,tx,"submission.bulk_approved",input.S("campaignId"),Actor(ctx));return new{ok=true,count=rows.Count};}));
    }
}
