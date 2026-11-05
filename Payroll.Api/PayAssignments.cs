using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

public static class PayAssignments
{
    public const string Kind="pay_assignment";
    static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    static DateTimeOffset Cutoff(string period)=>new(DateTime.ParseExact(J.Period(period)+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture),TimeSpan.FromHours(3));
    static string PreviewKind(string app,string period)=>"pay-choice:"+app+":"+period;
    static void Admin(ClaimsPrincipal actor,string app){PaySettings.Scope(actor,"pay.manage",app);if(!AccessPolicy.Administrator(actor))throw new ApiError("ربط حقل الاختيار بالنظام واعتماد الاستيراد لمسؤول النظام فقط.",403);}
    static JsonObject Scheme(DbConnection c,DbTransaction? tx,string id,string app,string period)
    {var s=Store.Get(c,id,"pay_scheme",tx)??throw new ApiError("النظام المرتبط غير موجود.");J.Need(s.S("app")==app&&s.S("period")==period&&s.S("status")=="approved","الاختيار يحتاج نظامًا معتمدًا لنفس التطبيق والشهر.");return s;}
    public static JsonObject Preview(DbConnection c,ClaimsPrincipal actor,string app,JsonObject input)
    {
        app=PayPlatforms.App(app);Admin(actor,app);string period=J.Period(input.Required("period")),sourceType=input.Required("sourceType"),formId=input.Required("formId"),field=PayValues.Name(input.Required("fieldId"));
        J.Need(sourceType is "campaign" or "custom","مصدر نموذج غير صالح.");string sourceKind=sourceType=="campaign"?"submission":"custom_response";var form=Store.Get(c,formId,sourceType=="campaign"?"campaign":"custom_form")??throw new ApiError("النموذج غير موجود.",404);string riderField="iqama";
        if(sourceType=="campaign")J.Need(field=="workSystem","حقل اختيار نظام الحملة هو workSystem.");
        else
        {
            var fields=PayValues.Array(form["fields"]);J.Need(fields.OfType<JsonObject>().Any(f=>f.S("id")==field&&f.S("type") is "select" or "radio"),"حدد حقل اختيار معرّفًا في النموذج.");var identities=fields.OfType<JsonObject>().Where(f=>f.S("type")=="iqama").ToList();riderField=input.S("riderFieldId",identities.Count==1?identities[0].S("id"):"");J.Need(identities.Any(f=>f.S("id")==riderField),"حدد حقل هوية المندوب الفعلي صراحة إذا للنموذج أكثر من هوية.");
        }
        var mapping=PayValues.Object(input["mapping"]);J.Need(mapping.Count is >0 and <=100&&mapping.All(p=>p.Key.Length is >0 and <=200&&p.Value is JsonValue),"خريطة خيارات غير صالحة.");foreach(var p in mapping)Scheme(c,null,p.Value!.ToString(),app,period);
        var errors=new JsonArray();var candidates=new List<(JsonObject Row,string Rider,string Choice,DateTimeOffset ApprovedAt,string By)>();int late=0,pending=0;
        var sourceRows=Store.List(c,sourceKind).Where(r=>r.S(sourceType=="campaign"?"campaignId":"formId")==formId).ToList();J.Need(sourceRows.Count<=25000,"النموذج أكبر من دفعة الاستيراد المسموحة.");
        void Error(string code)=>errors.Add(new JsonObject{["code"]=code});
        foreach(var original in sourceRows)
        foreach(var response in Snapshots(original,sourceType,formId))
        {
            if(response.S("status")!="approved"){pending++;continue;}string at=response.S("approvedAt",response.S("reviewedAt")),by=response.S("approvedBy",response.S("reviewedBy"));
            if(!at.Contains('T')||!DateTimeOffset.TryParse(at,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var approved)||by==""||response.S("createdBy")!=""&&response.S("createdBy")==by){Error("approval_evidence_missing_or_self");continue;}
            if(approved>=Cutoff(period)){late++;continue;}var answers=sourceType=="campaign"?response:response["answers"] as JsonObject??new();string rider=answers.S(riderField);try{J.Iqama(rider);}catch(ApiError){Error("invalid_rider_identity");continue;}
            if(Store.Get(c,rider,"rider") is not {} person||!ErpRiderClassification.ListedAsRider(person)){Error("actual_rider_missing");continue;}FinancialScopes.DemandRider(actor,person);candidates.Add((response,rider,answers.S(field),approved.ToUniversalTime(),by));
        }
        var rows=new JsonArray();int unchanged=0,fresh=0;
        foreach(var group in candidates.GroupBy(r=>r.Rider))
        {
            var latest=group.Where(r=>r.ApprovedAt==group.Max(x=>x.ApprovedAt)).ToList();if(latest.Select(r=>r.Choice).Distinct().Count()>1){Error("ambiguous_latest_choice");continue;}var selected=latest.OrderBy(r=>r.Row.S("id"),StringComparer.Ordinal).First();
            if(!mapping.ContainsKey(selected.Choice)){Error("latest_choice_unmapped");continue;}string schemeId=mapping.S(selected.Choice);string at=selected.ApprovedAt.ToString("O"),version=selected.Row.N("version",1).ToString(CultureInfo.InvariantCulture);string id="pay-assignment:"+Hash(app+"\n"+period+"\n"+selected.Rider+"\n"+sourceKind+"\n"+selected.Row.S("id")+"\n"+version+"\n"+at);
            var rider=Store.Get(c,selected.Rider,"rider")!;var row=new JsonObject{["id"]=id,["rider"]=selected.Rider,["app"]=app,["schemeId"]=schemeId,["period"]=period,["companyId"]=rider.S("financeSponsorId",rider.S("sponsorId")),["status"]="approved",["choiceApprovedAt"]=at,["source"]=new JsonObject{["kind"]=sourceKind,["formId"]=formId,["responseId"]=selected.Row.S("id"),["version"]=selected.Row.N("version",1),["fieldId"]=field,["approvedBy"]=selected.By,["fingerprint"]=Hash(FinancialImports.Canonical(new JsonObject{["responseId"]=selected.Row.S("id"),["version"]=selected.Row.N("version",1),["rider"]=selected.Rider,["choice"]=selected.Choice,["approvedAt"]=at,["approvedBy"]=selected.By}))}};
            var old=Store.Get(c,id,Kind);if(old!=null){row["companyId"]=old["companyId"]?.DeepClone();var comparable=old.Clone();comparable.Remove("importedAt");comparable.Remove("importedBy");if(!JsonNode.DeepEquals(comparable,row)){Error("approved_choice_mapping_changed");continue;}}
            var tied=Store.List(c,Kind).Where(a=>a.S("rider")==selected.Rider&&a.S("app")==app&&a.S("period")==period&&a.S("status")=="approved"&&a.S("choiceApprovedAt")==at&&a.S("schemeId")!=schemeId);if(tied.Any()){Error("ambiguous_imported_choice");continue;}rows.Add(row);if(old==null)fresh++;else unchanged++;
        }
        return new(){["rows"]=rows,["canCommit"]=errors.Count==0,["errors"]=errors,["fingerprint"]=errors.Count==0?FinancialImports.Issue(c,PreviewKind(app,period),rows,actor.Identity!.Name!):"",["counts"]=new JsonObject{["new"]=fresh,["unchanged"]=unchanged,["errors"]=errors.Count,["lateIgnored"]=late,["unapprovedIgnored"]=pending}};
    }
    static IEnumerable<JsonObject> Snapshots(JsonObject source,string type,string formId)
    {
        yield return source;
        if(source["history"] is not JsonArray history)yield break;
        foreach(var snapshot in history.OfType<JsonObject>())
        {
            var row=new JsonObject{["id"]=source.S("id"),[type=="campaign"?"campaignId":"formId"]=formId,["iqama"]=source.S("iqama"),["createdBy"]=source.S("createdBy")};
            foreach(string key in new[]{"workSystem","answers","version","status","approvedAt","approvedBy","reviewedAt","reviewedBy"})if(snapshot.ContainsKey(key))row[key]=snapshot[key]?.DeepClone();
            yield return row;
        }
    }
    public static JsonObject Commit(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string app,JsonObject input)
    {
        app=PayPlatforms.App(app);Admin(actor,app);string period=J.Period(input.Required("period"));FinancialImports.Verify(c,tx,input,PreviewKind(app,period),actor.Identity!.Name!);int imported=0,unchanged=0;
        foreach(var row in input["rows"]!.AsArray().OfType<JsonObject>())
        {
            J.Need(row.S("app")==app&&row.S("period")==period&&DateTimeOffset.Parse(row.S("choiceApprovedAt"),CultureInfo.InvariantCulture)<Cutoff(period),"الاختيار لا يخص التطبيق والشهر أو جاء بعد بداية الشهر.");Scheme(c,tx,row.S("schemeId"),app,period);if(Store.Get(c,row.S("id"),Kind,tx)!=null){unchanged++;continue;}
            var doc=row.Clone();doc["importedAt"]=DateTimeOffset.UtcNow.ToString("O");doc["importedBy"]=actor.Identity!.Name;Store.Save(c,Kind,doc,tx);Store.Audit(c,tx,"pay.assignment_imported",doc.S("id"),actor.Identity!.Name!);imported++;
        }
        return new(){["imported"]=imported,["unchanged"]=unchanged};
    }
    public static JsonObject Resolve(DbConnection c,DbTransaction? tx,string rider,string app,string period)
    {
        app=PayPlatforms.App(app);J.Period(period);var rows=Store.List(c,Kind,tx).Where(a=>a.S("rider")==rider&&a.S("app")==app&&a.S("period")==period&&a.S("status")=="approved"&&DateTimeOffset.Parse(a.S("choiceApprovedAt"),CultureInfo.InvariantCulture)<Cutoff(period)).ToList();J.Need(rows.Count>0,"اختيار نظام معتمد قبل بداية الشهر غير موجود.");string last=rows.Max(a=>a.S("choiceApprovedAt"))!;var latest=rows.Where(a=>a.S("choiceApprovedAt")==last).ToList();J.Need(latest.Select(a=>a.S("schemeId")).Distinct().Count()==1,"اختيارات معتمدة متعارضة بنفس توقيت الاعتماد؛ راجع المصدر.");var chosen=latest.OrderBy(a=>a.S("id"),StringComparer.Ordinal).First();Scheme(c,tx,chosen.S("schemeId"),app,period);return chosen;
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/pay/apps/{app}/assignments",(string app,HttpContext ctx)=>store.Read(c=>{app=PayPlatforms.App(app);PaySettings.Scope(ctx.User,"pay.view",app);string period=ctx.Request.Query["period"].ToString();if(period!="")J.Period(period);return ApiPagination.Apply(Store.List(c,Kind).Where(a=>a.S("app")==app&&(period==""||a.S("period")==period)&&(a.S("companyId")!=""?FinancialScopes.Sponsor(ctx.User,a.S("companyId")):FinancialScopes.AllCompanies(ctx.User))),ctx.Request.Query,true);})).WithTags("Payroll").WithSummary("Read scoped immutable approved form-based scheme choices");
        api.MapPost("/pay/apps/{app}/assignments/import/preview",(string app,JsonObject input,HttpContext ctx)=>store.Read(c=>Preview(c,ctx.User,app,input))).WithTags("Payroll").WithSummary("Administrator maps approved pre-month form choices to app schemes in a signed preview");
        api.MapPost("/pay/apps/{app}/assignments/import/commit",(string app,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Commit(c,tx,ctx.User,app,input))).WithTags("Payroll").WithSummary("Store signed historical scheme choices without modifying responses or riders");
    }
}
