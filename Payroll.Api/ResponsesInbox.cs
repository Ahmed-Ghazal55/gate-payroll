using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static class ResponsesInbox
{
    public static List<JsonObject> Rows(DbConnection c,ClaimsPrincipal user,string source,string status,string campaignId)
    {
        AccessPolicy.Demand(user,"forms.view");J.Need(source is "" or "campaign" or "custom","مصدر الرد غير صالح.");
        var campaigns=Store.List(c,"campaign").ToDictionary(x=>x.S("id"));
        var forms=Store.List(c,"custom_form").ToDictionary(x=>x.S("id"));
        var rows=new List<JsonObject>();
        if(source is "" or "campaign")
        foreach(var item in Store.List(c,"submission"))
        {
            if(!SubmissionChoices.InRiderScope(c,user,item.S("iqama")))continue;
            string parent=item.S("campaignId");if(!campaigns.TryGetValue(parent,out var form))continue;
            var view=SubmissionChoices.PublicAdmin(item,user);
            var answers=new JsonObject();
            foreach(string field in new[]{"iqama","name","workSystem","city","phone","bankCode","iban","platforms"}.Concat(CampaignOperations.Fields(form)).Distinct())
                if(view.ContainsKey(field))answers[field]=view[field]?.DeepClone();
            rows.Add(Row(item,"campaign",parent,form.S("title"),item.S("iqama"),item.S("name"),answers,$"/api/submissions/{item.S("id")}/review"));
        }
        if(source is "" or "custom")
        foreach(var item in Store.List(c,"custom_response"))
        {
            string parent=item.S("formId");if(!forms.TryGetValue(parent,out var form))continue;
            var fields=(form["fields"] as JsonArray)?.OfType<JsonObject>().ToList()??[];
            string iqamaField=fields.FirstOrDefault(x=>x.S("type")=="iqama")?.S("id")??"";
            var answers=(item["answers"] as JsonObject)?.Clone()??new JsonObject();
            string iqama=answers.S(iqamaField);
            if(!SubmissionChoices.InRiderScope(c,user,iqama))continue;
            if(!AccessPolicy.Has(user,WorkforceApi.PermIban))
                foreach(var f in fields.Where(x=>x.S("type")=="iban"||x.S("id").Contains("iban",StringComparison.OrdinalIgnoreCase)))
                { string v=answers.S(f.S("id")); if(v.Length>=4) answers[f.S("id")]="••••"+v[^4..]; else if(v!="") answers[f.S("id")]="••••"; }
            rows.Add(Row(item,"custom",parent,form.S("titleAr"),iqama,answers.S("name"),answers,null));
        }
        return rows.Where(r=>(status==""||r.S("status")==status)&&(campaignId==""||r.S("campaignId")==campaignId)).OrderByDescending(r=>r.S("submittedAt"),StringComparer.Ordinal).ThenBy(r=>r.S("id"),StringComparer.Ordinal).ToList();
    }
    static JsonObject Row(JsonObject item,string source,string parent,string title,string iqama,string name,JsonObject answers,string? reviewUrl)=>new()
    {
        ["id"]=item.S("id"),["source"]=source,["campaignId"]=parent,["title"]=title,["status"]=item.S("status","pending"),
        ["submittedAt"]=item.S("createdAt"),["updatedAt"]=item.S("updatedAt",item.S("createdAt")),["iqama"]=iqama,["name"]=name,
        ["answers"]=answers,["version"]=item.N("version",1),["reviewUrl"]=reviewUrl
    };
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/responses",(string? source,string? status,string? campaignId,HttpContext ctx)=>store.Read(c=>ApiPagination.Apply(Rows(c,ctx.User,source??"",status??"",campaignId??""),ctx.Request.Query))).WithTags("Forms and responses").WithSummary("Merge campaign and custom-form responses into a filtered review inbox");
    }
}
