using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PaySettings
{
    public static string Kind(string section)=>section switch{"terms"=>"pay_terms","schemes"=>"pay_scheme","eligibility"=>"pay_eligibility",_=>throw new ApiError("نوع إعداد التطبيق غير صالح.")};
    public static void Scope(ClaimsPrincipal actor,string permission,string? app=null,bool globalWrite=false)
    {
        AccessPolicy.Demand(actor,permission);if(app!=null&&!FinancialScopes.Platform(actor,PayPlatforms.For(app).Platform))throw new ApiError("التطبيق خارج نطاق المستخدم.",403);
        if(globalWrite&&!FinancialScopes.AllCompanies(actor))throw new ApiError("الإعداد المشترك يحتاج نطاق جميع الشركات.",403);
    }
    static void References(DbConnection c,DbTransaction? tx,JsonObject doc,bool approving)
    {
        if(doc["data"].S("eligibilitySetId")!="")
        {
            var rules=Store.Get(c,doc["data"].S("eligibilitySetId"),"pay_eligibility",tx)??throw new ApiError("شروط الأهلية غير موجودة.");
            J.Need(rules.S("app")==doc.S("app")&&rules.S("period")==doc.S("period"),"مشاركة شروط الأهلية بين التطبيقات أو الشهور ممنوعة.");if(approving)J.Need(rules.S("status")=="approved","اعتمد شروط الأهلية أولًا.");
        }
        if(approving&&doc.S("kind")=="pay_scheme")PayPlatforms.Revenue(doc.S("app"),doc["data"].S("accountingRole"));
    }
    public static JsonObject Create(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string kind,string app,JsonObject input,string? period=null,string? copiedFrom=null)
    {
        J.Need(Kinds.Contains(kind),"نوع إعداد غير معروف.");bool global=kind=="pay_setting";app=global?"global":PayPlatforms.App(app);Scope(actor,"pay.manage",global?null:app,true);
        PayValues.Fields(input,"app","period","code","data","effectiveFrom","fromDefaults","baseVersionId");if(input.ContainsKey("app"))J.Need(input.S("app")==app,"الإعداد يخص تطبيقًا آخر.");string effective=global?PayValues.Date(input.Required("effectiveFrom")):"";period=global?effective[..7]:J.Period(period??input.Required("period"));if(input.ContainsKey("period"))J.Need(input.S("period")==period,"الشهر لا يطابق المسار.");
        string code=PayValues.Name(input.S("code",global?"global":kind=="pay_scheme"?"":"default"));var data=input.B("fromDefaults")&&global?Defaults():PayValues.Object(input["data"]).Clone();Validate(kind,app,data);
        var siblings=Store.List(c,kind,tx).Where(d=>d.S("app")==app&&d.S("period")==period&&d.S("code")==code).ToList();
        if(input.S("baseVersionId")!="")J.Need(siblings.OrderByDescending(d=>d.N("version")).FirstOrDefault()?.S("id")==input.S("baseVersionId"),"تغير إصدار الإعداد؛ راجع أحدث نسخة.");
        var doc=new JsonObject{["id"]="pay-config:"+Guid.NewGuid().ToString("N"),["kind"]=kind,["app"]=app,["period"]=period,["code"]=code,["version"]=(siblings.Count==0?0:siblings.Max(d=>d.N("version")))+1,["status"]="draft",["data"]=data,["createdBy"]=actor.Identity!.Name,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        if(global)doc["effectiveFrom"]=effective;if(copiedFrom!=null)doc["copiedFromId"]=copiedFrom;References(c,tx,doc,false);Store.Save(c,kind,doc,tx);Store.Audit(c,tx,"pay.config_drafted",doc.S("id"),actor.Identity!.Name!);return doc;
    }
    public static JsonObject Approve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string kind,string app,string id)
    {
        bool global=kind=="pay_setting";app=global?"global":PayPlatforms.App(app);Scope(actor,"pay.approve",global?null:app,true);var doc=Store.Get(c,id,kind,tx)?.Clone()??throw new ApiError("الإعداد غير موجود.",404);J.Need(doc.S("app")==app,"الإعداد يخص تطبيقًا آخر.");
        AccessPolicy.ValidateApproval(actor,new(){["requestedBy"]=doc.S("createdBy"),["targetDepartment"]="FIN",["requiredRankLevel"]=1},new JsonObject(),"pay.approve");if(doc.S("status")=="approved")return doc;
        J.Need(doc.S("status")=="draft"&&!Store.List(c,kind,tx).Any(d=>d.S("app")==app&&d.S("period")==doc.S("period")&&d.S("code")==doc.S("code")&&d.N("version")>doc.N("version")),"اعتمد أحدث مسودة فقط.");Validate(kind,app,doc["data"]!.AsObject());References(c,tx,doc,true);doc["status"]="approved";doc["approvedBy"]=actor.Identity!.Name;doc["approvedAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,kind,doc,tx);Store.Audit(c,tx,"pay.config_approved",id,actor.Identity!.Name!);return doc;
    }
    public static JsonObject Resolve(DbConnection c,DbTransaction? tx,string kind,string app,string period,string code="default")=>Store.List(c,kind,tx).Where(d=>d.S("app")==PayPlatforms.App(app)&&d.S("period")==J.Period(period)&&d.S("code")==code&&d.S("status")=="approved").OrderByDescending(d=>d.N("version")).FirstOrDefault()??throw new ApiError("لا توجد نسخة إعداد معتمدة للتطبيق والشهر.",409);
    public static JsonObject Global(DbConnection c,DbTransaction? tx,string on)=>Store.List(c,"pay_setting",tx).Where(d=>d.S("status")=="approved"&&string.CompareOrdinal(d.S("effectiveFrom"),PayValues.Date(on))<=0).OrderByDescending(d=>d.S("effectiveFrom"),StringComparer.Ordinal).ThenByDescending(d=>d.N("version")).FirstOrDefault()??throw new ApiError("إعدادات الأيام والإيجار لم تعتمد بتاريخ السريان.",409);
    public static JsonObject Copy(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string app,string from,string to)
    {
        app=PayPlatforms.App(app);Scope(actor,"pay.manage",app,true);J.Period(from);J.Period(to);J.Need(string.CompareOrdinal(from,to)<0,"النسخ من شهر سابق إلى شهر لاحق.");var drafts=new JsonArray();var linked=new Dictionary<string,string>();int created=0,unchanged=0;
        var sourceLists=new[]{"pay_eligibility","pay_terms","pay_scheme"}.ToDictionary(k=>k,k=>Store.List(c,k,tx).Where(d=>d.S("app")==app&&d.S("period")==from&&d.S("status")=="approved").GroupBy(d=>d.S("code")).Select(g=>g.MaxBy(d=>d.N("version"))!).ToList());
        foreach(string rulesId in sourceLists["pay_scheme"].Select(d=>d["data"].S("eligibilitySetId")).Where(id=>id!="").Distinct())
        {
            if(sourceLists["pay_eligibility"].Any(d=>d.S("id")==rulesId))continue;
            var snapshot=Store.Get(c,rulesId,"pay_eligibility",tx)?.Clone()??throw new ApiError("نسخة أهلية مرتبطة غير موجودة.");J.Need(snapshot.S("app")==app&&snapshot.S("period")==from&&snapshot.S("status")=="approved","نسخة الأهلية لا تخص الشهر والتطبيق.");
            snapshot["code"]="snapshot_"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rulesId)))[..12];sourceLists["pay_eligibility"].Add(snapshot);
        }
        foreach(string kind in new[]{"pay_eligibility","pay_terms","pay_scheme"})
        foreach(var source in sourceLists[kind])
        {
            var data=source["data"]!.DeepClone().AsObject();if(data.S("eligibilitySetId")!=""){J.Need(linked.ContainsKey(data.S("eligibilitySetId")),"تعذر نسخ نسخة أهلية مرتبطة؛ راجع الإصدار المعتمد.");data["eligibilitySetId"]=linked[data.S("eligibilitySetId")];}
            var existing=Store.List(c,kind,tx).FirstOrDefault(d=>d.S("app")==app&&d.S("period")==to&&d.S("status")=="draft"&&d.S("copiedFromId")==source.S("id")&&JsonNode.DeepEquals(d["data"],data));
            var draft=existing??Create(c,tx,actor,kind,app,new(){["period"]=to,["code"]=source.S("code"),["data"]=data},copiedFrom:source.S("id"));if(existing==null)created++;else unchanged++;linked[source.S("id")]=draft.S("id");drafts.Add(draft.Clone());
        }
        J.Need(drafts.Count>0,"الشهر السابق بلا إعدادات معتمدة.");return new(){["drafts"]=drafts,["created"]=created,["unchanged"]=unchanged};
    }
    static object List(Store store,HttpContext ctx,string kind,string app)
    {
        bool global=kind=="pay_setting";app=global?"global":PayPlatforms.App(app);Scope(ctx.User,"pay.view",global?null:app);string period=ctx.Request.Query["period"].ToString();if(period!="")J.Period(period);
        return store.Read(c=>ApiPagination.Apply(Store.List(c,kind).Where(d=>d.S("app")==app&&(period==""||d.S("period")==period)).OrderBy(d=>d.S("code")).ThenByDescending(d=>d.N("version")),ctx.Request.Query,true));
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        MapLifecycle(api,store);
        api.MapGet("/pay/catalog",(HttpContext ctx)=>{Scope(ctx.User,"pay.view");return new{apps=PayPlatforms.Table.Where(a=>FinancialScopes.Platform(ctx.User,a.Platform)),schemeTypes=SchemeTypes,categories=Categories,operators=Operators,ninjaMetrics=NinjaMetrics,defaults=Defaults(),defaultsAreDraftOnly=true};}).WithTags("Payroll").WithSummary("Read pay framework catalog and unapproved default settings template");
        api.MapGet("/pay/apps/{app}/terms",(string app,HttpContext ctx)=>List(store,ctx,"pay_terms",app)).WithTags("Payroll").WithSummary("Read app monthly parent terms and version history");
        api.MapGet("/pay/apps/{app}/schemes",(string app,HttpContext ctx)=>List(store,ctx,"pay_scheme",app)).WithTags("Payroll").WithSummary("Read app internal schemes and category matrix versions");
        api.MapGet("/pay/apps/{app}/eligibility",(string app,HttpContext ctx)=>List(store,ctx,"pay_eligibility",app)).WithTags("Payroll").WithSummary("Read app-owned eligibility rules without cross-app sharing");
        api.MapPost("/pay/apps/{app}/terms",(string app,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Create(c,tx,ctx.User,"pay_terms",app,input,ctx.Request.Query["period"].FirstOrDefault()))).WithTags("Payroll").WithSummary("Append draft version of parent billing terms");
        api.MapPost("/pay/apps/{app}/schemes",(string app,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Create(c,tx,ctx.User,"pay_scheme",app,input,ctx.Request.Query["period"].FirstOrDefault()))).WithTags("Payroll").WithSummary("Append draft internal scheme with six explicit category cells");
        api.MapPost("/pay/apps/{app}/eligibility",(string app,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Create(c,tx,ctx.User,"pay_eligibility",app,input,ctx.Request.Query["period"].FirstOrDefault()))).WithTags("Payroll").WithSummary("Append draft app-only eligibility rule set");
        api.MapPost("/pay/apps/{app}/terms/{id}/approve",(string app,string id,HttpContext ctx)=>store.Write((c,tx)=>Approve(c,tx,ctx.User,"pay_terms",app,id))).WithTags("Payroll").WithSummary("Independently approve latest parent terms draft");
        api.MapPost("/pay/apps/{app}/schemes/{id}/approve",(string app,string id,HttpContext ctx)=>store.Write((c,tx)=>Approve(c,tx,ctx.User,"pay_scheme",app,id))).WithTags("Payroll").WithSummary("Independently approve latest scheme and validated eligibility reference");
        api.MapPost("/pay/apps/{app}/eligibility/{id}/approve",(string app,string id,HttpContext ctx)=>store.Write((c,tx)=>Approve(c,tx,ctx.User,"pay_eligibility",app,id))).WithTags("Payroll").WithSummary("Independently approve app-owned eligibility");
        api.MapPost("/pay/apps/{app}/copy",(string app,HttpContext ctx)=>store.Write((c,tx)=>Copy(c,tx,ctx.User,app,ctx.Request.Query["from"].ToString(),ctx.Request.Query["to"].ToString()))).WithTags("Payroll").WithSummary("Copy approved prior month into new drafts without overwriting approvals");
        api.MapGet("/pay/settings",(HttpContext ctx)=>List(store,ctx,"pay_setting","global")).WithTags("Payroll").WithSummary("Read effective-dated global pay settings versions");
        api.MapPost("/pay/settings",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Create(c,tx,ctx.User,"pay_setting","global",input))).WithTags("Payroll").WithSummary("Append draft global values; defaults never auto-approve");
        api.MapPost("/pay/settings/{id}/approve",(string id,HttpContext ctx)=>store.Write((c,tx)=>Approve(c,tx,ctx.User,"pay_setting","global",id))).WithTags("Payroll").WithSummary("Independently approve latest effective-dated global settings");
    }
}
