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
    /// <summary>
    /// One picker list for the per-form responses table: every campaign and builder form, each with its
    /// own column definitions (one per question) so the screen loops over them. Counts are in the caller's scope.
    /// </summary>
    public static List<JsonObject> Forms(DbConnection c,ClaimsPrincipal user)
    {
        AccessPolicy.Demand(user,"forms.view");
        var counts=Rows(c,user,"","","").GroupBy(r=>r.S("campaignId")).ToDictionary(g=>g.Key,g=>(Total:g.Count(),Pending:g.Count(r=>r.S("status","pending")=="pending")));
        var list=new List<JsonObject>();
        foreach(var form in Store.List(c,"campaign"))
        {
            var cols=new JsonArray();
            if(IbanChoices.Is(form))
            {
                cols.Add(Col("phone","رقم الجوال","phone"));cols.Add(Col("city","المدينة","text"));
                cols.Add(Col("iban","الآيبان","iban"));cols.Add(Col("platforms","تطبيقات العمل","list"));
            }
            else
            {
                cols.Add(Col("workSystem","نظام العمل","workSystem"));
                foreach(string f in CampaignOperations.Fields(form))
                    cols.Add(f=="city"?Col("city","المدينة","text"):Col("phone","رقم الجوال","phone"));
            }
            list.Add(Item(form,"campaign",form.S("title"),form.S("kind",""),cols,counts));
        }
        foreach(var form in Store.List(c,"custom_form").Where(f=>f.S("status")!="draft"))
        {
            var cols=new JsonArray();
            foreach(var f in (form["fields"] as JsonArray)?.OfType<JsonObject>()??[])
            {
                string type=f.S("type");
                if(type=="iqama"||f.S("id")=="name")continue; // shown in the fixed name/iqama columns
                var col=Col(f.S("id"),f.S("labelAr",f.S("id")),type is "select" or "radio"?"list":type);
                if(f["options"] is JsonArray opts&&opts.Count>0)col["options"]=opts.DeepClone();
                cols.Add(col);
            }
            list.Add(Item(form,"custom",form.S("titleAr"),"custom",cols,counts));
        }
        return list.OrderByDescending(x=>x.S("createdAt"),StringComparer.Ordinal).ToList();
    }
    static JsonObject Col(string id,string labelAr,string type)=>new(){["id"]=id,["labelAr"]=labelAr,["type"]=type};
    static JsonObject Item(JsonObject form,string source,string title,string kind,JsonArray cols,Dictionary<string,(int Total,int Pending)> counts)
    {
        counts.TryGetValue(form.S("id"),out var n);
        return new(){["id"]=form.S("id"),["source"]=source,["kind"]=kind,["title"]=title,["status"]=form.S("status"),
            ["createdAt"]=form.S("createdAt"),["expiresAt"]=form.S("expiresAt"),["responseCount"]=n.Total,["pendingCount"]=n.Pending,["columns"]=cols};
    }
    static JsonObject Row(JsonObject item,string source,string parent,string title,string iqama,string name,JsonObject answers,string? reviewUrl)=>new()
    {
        ["id"]=item.S("id"),["source"]=source,["campaignId"]=parent,["title"]=title,["status"]=item.S("status","pending"),
        ["submittedAt"]=item.S("createdAt"),["updatedAt"]=item.S("updatedAt",item.S("createdAt")),["iqama"]=iqama,["name"]=name,
        ["answers"]=answers,["version"]=item.N("version",1),["reviewUrl"]=reviewUrl
    };
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/responses/forms",(HttpContext ctx)=>store.Read(c=>Forms(c,ctx.User))).WithTags("Forms and responses").WithSummary("List campaigns and builder forms with their response columns");
        api.MapGet("/responses",(string? source,string? status,string? campaignId,HttpContext ctx)=>store.Read(c=>ApiPagination.Apply(Rows(c,ctx.User,source??"",status??"",campaignId??""),ctx.Request.Query))).WithTags("Forms and responses").WithSummary("Merge campaign and custom-form responses into a filtered review inbox");
    }
}
