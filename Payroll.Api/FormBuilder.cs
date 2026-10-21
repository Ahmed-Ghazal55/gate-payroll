using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using QRCoder;
namespace Payroll;

public static class CampaignLinks
{
    public static string NewToken()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static string Base(IConfiguration config,HttpContext ctx)
    {
        string value=config["Payroll:PublicBaseUrl"]??("https://"+ctx.Request.Host);
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo!=""||uri.AbsolutePath!="/"||uri.Query!=""||uri.Fragment!="") throw new InvalidOperationException("Payroll:PublicBaseUrl must be an HTTPS origin.");
        return value.TrimEnd('/');
    }
    public static void Migrate(Store store)=>store.Write((c,tx)=>{
        foreach(var d in Store.List(c,"campaign",tx).Where(x=>x.S("sharingToken")=="")) { d["sharingToken"]=NewToken();Store.Save(c,"campaign",d,tx); }
        return true;
    });
    public static string Svg(string url)
    {
        using var generator=new QRCodeGenerator();using var data=generator.CreateQrCode(url,QRCodeGenerator.ECCLevel.Q);using var qr=new SvgQRCode(data);return qr.GetGraphic(8);
    }
}

public static class FormSchemas
{
    public static readonly string[] Types=["text","number","select","radio","iqama","phone","date"];
    public static JsonObject Validate(JsonObject input)
    {
        string Text(JsonObject obj,string key,int max,bool required=false) { string v=obj.S(key);J.Need(v.Length<=max&&(!required||v.Length>0),"بيانات النموذج غير مكتملة أو طويلة: "+key);return v; }
        string title=Text(input,"titleAr",200,true);
        var fields=input["fields"] as JsonArray??throw new ApiError("أسئلة النموذج مطلوبة.");J.Need(fields.Count is >0 and <=50,"حدد من1إلى50سؤالًا.");
        var names=new HashSet<string>();var result=new JsonArray();
        foreach(var item in fields)
        {
            var f=item as JsonObject??throw new ApiError("تعريف سؤال غير صالح.");string id=f.Required("id"),type=f.Required("type");
            J.Need(Regex.IsMatch(id,"^[A-Za-z][A-Za-z0-9_]{0,39}$")&&names.Add(id)&&Types.Contains(type),"معرف سؤال مكرر أو نوع غير صالح.");
            string label=Text(f,"labelAr",200,true);var options=new JsonArray();
            if(type is "select" or "radio")
            {
                var raw=f["options"] as JsonArray??throw new ApiError("خيارات السؤال مطلوبة.");J.Need(raw.Count is >0 and <=100,"عدد خيارات غير صالح.");var seen=new HashSet<string>();
                foreach(var option in raw)
                {
                    var o=option as JsonObject??new JsonObject{["value"]=option?.ToString()??""};string value=Text(o,"value",100,true);J.Need(seen.Add(value),"خيار مكرر.");
                    options.Add(new JsonObject{["value"]=value,["labelAr"]=o.S("labelAr",value),["labelEn"]=o.S("labelEn",o.S("labelAr",value)),["labelUr"]=o.S("labelUr",o.S("labelAr",value))});
                }
            }
            result.Add(new JsonObject{["id"]=id,["type"]=type,["labelAr"]=label,["labelEn"]=Text(f,"labelEn",200) is {Length:>0} en?en:label,["labelUr"]=Text(f,"labelUr",200) is {Length:>0} ur?ur:label,["required"]=f.B("required"),["options"]=options});
        }
        J.Need(DateTimeOffset.TryParse(input.S("expiresAt"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var expires)&&expires>DateTimeOffset.UtcNow&&expires<DateTimeOffset.UtcNow.AddYears(2),"تاريخ انتهاء النموذج غير صالح.");
        decimal limit=input.N("maxSubmissions",10000);J.Need(limit is >=1 and <=100000&&decimal.Truncate(limit)==limit,"حد الردود غير صالح.");
        return new(){["titleAr"]=title,["titleEn"]=Text(input,"titleEn",200) is {Length:>0} titleEn?titleEn:title,["titleUr"]=Text(input,"titleUr",200) is {Length:>0} titleUr?titleUr:title,["descriptionAr"]=Text(input,"descriptionAr",3000),["descriptionEn"]=Text(input,"descriptionEn",3000),["descriptionUr"]=Text(input,"descriptionUr",3000),["expiresAt"]=expires.ToString("O"),["maxSubmissions"]=(int)limit,["fields"]=result,["schemaVersion"]=1};
    }
    public static JsonObject Answers(JsonObject form,JsonObject input)
    {
        var fields=((JsonArray)form["fields"]!).OfType<JsonObject>().ToList();J.Need(input.All(p=>fields.Any(f=>f.S("id")==p.Key)),"حقول غير موجودة في النموذج.");var answers=new JsonObject();
        foreach(var f in fields)
        {
            string id=f.S("id"),value=input.S(id);J.Need(value.Length<=2000,"إجابة طويلة جدًا.");J.Need(!f.B("required")||value!="","إجابة مطلوبة: "+id);if(value==""){answers[id]="";continue;}
            switch(f.S("type"))
            {
                case "iqama": J.Need(Regex.IsMatch(value,"^[0-9]{10}$"),"الإقامة10أرقام إنجليزية.");break;
                case "phone": value=RiderDirectory.Phone(value);break;
                case "number": J.Need(decimal.TryParse(value,NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var n)&&Math.Abs(n)<=1000000000,"رقم غير صالح.");answers[id]=n;continue;
                case "date": J.Need(DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),"تاريخ غير صالح.");break;
                case "select":case "radio":J.Need(((JsonArray)f["options"]!).OfType<JsonObject>().Any(x=>x.S("value")==value),"خيار غير صالح.");break;
            }
            answers[id]=value;
        }
        return answers;
    }
}

public static class FormBuilderApi
{
    public static void Map(WebApplication app,RouteGroupBuilder api,Store store)
    {
        JsonObject View(JsonObject f,HttpContext ctx)
        { var d=f.Clone();if(d.S("sharingToken")!="") {d["publicUrl"]=CampaignLinks.Base(app.Configuration,ctx)+"/questionnaires/"+d.S("sharingToken");d["qrCodeUrl"]="/api/forms/builder/"+d.S("id")+"/qr.svg";}return d; }
        api.MapGet("/forms/catalog",()=>store.Read(c=>Store.List(c,"custom_form").Where(f=>f.S("status")!="draft").Select(f=>new{id=f.S("id"),titleAr=f.S("titleAr"),fields=f["fields"]?.DeepClone()}).ToList()));
        api.MapGet("/forms/builder",(HttpContext ctx)=>store.Read(c=>Store.List(c,"custom_form").Select(f=>View(f,ctx)).ToList()));
        api.MapPost("/forms/builder",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>{
            var f=FormSchemas.Validate(input);f["id"]=Guid.NewGuid().ToString("N");f["status"]="draft";f["createdAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,"custom_form",f,tx);Store.Audit(c,tx,"builder.created",f.S("id"),ctx.User.Identity!.Name!);return View(f,ctx);
        }));
        api.MapPut("/forms/builder/{id}",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>{
            var prior=Store.Get(c,id,"custom_form",tx)??throw new ApiError("النموذج غير موجود.",404);J.Need(prior.S("status")=="draft","الأسئلة المنشورة ثابتة؛ أنشئ إصدارًا جديدًا للتعديل.");var f=FormSchemas.Validate(input);f["id"]=id;f["createdAt"]=prior.S("createdAt");f["status"]="draft";Store.Save(c,"custom_form",f,tx);Store.Audit(c,tx,"builder.updated",id,ctx.User.Identity!.Name!);return View(f,ctx);
        }));
        api.MapPost("/forms/builder/{id}/publish",(string id,HttpContext ctx)=>store.Write((c,tx)=>{
            var f=Store.Get(c,id,"custom_form",tx)??throw new ApiError("النموذج غير موجود.",404);J.Need(f.S("status")=="draft","النموذج منشور بالفعل.");FormSchemas.Validate(f);f["sharingToken"]=CampaignLinks.NewToken();f["status"]="open";f["publishedAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,"custom_form",f,tx);Store.Audit(c,tx,"builder.published",id,ctx.User.Identity!.Name!);return View(f,ctx);
        }));
        api.MapPost("/forms/builder/{id}/close",(string id,HttpContext ctx)=>store.Write((c,tx)=>{var f=Store.Get(c,id,"custom_form",tx)??throw new ApiError("النموذج غير موجود.",404);f["status"]="closed";Store.Save(c,"custom_form",f,tx);Store.Audit(c,tx,"builder.closed",id,ctx.User.Identity!.Name!);return View(f,ctx);}));
        api.MapGet("/forms/builder/{id}/qr.svg",(string id,HttpContext ctx)=>store.Read(c=>{var f=Store.Get(c,id,"custom_form")??throw new ApiError("النموذج غير موجود.",404);J.Need(f.S("sharingToken")!="","انشر النموذج أولًا.");return Results.Text(CampaignLinks.Svg(View(f,ctx).S("publicUrl")),"image/svg+xml");}));
        api.MapGet("/forms/responses/{id}",(string id)=>store.Read(c=>Store.List(c,"custom_response").Where(x=>x.S("formId")==id).ToList()));
        api.MapGet("/forms/analytics/{id}",(string id)=>store.Read(c=>Analytics(c,id)));
        JsonObject Load(string token,DbConnection c,DbTransaction? tx=null)
        { J.Need(token.Length==64&&token.All(Uri.IsHexDigit),"رابط غير صالح.");var f=Store.List(c,"custom_form",tx).FirstOrDefault(x=>x.S("sharingToken")==token)??throw new ApiError("النموذج غير متاح.",404);J.Need(f.S("status")=="open"&&DateTimeOffset.Parse(f.S("expiresAt"),CultureInfo.InvariantCulture)>DateTimeOffset.UtcNow,"النموذج مغلق.");return f; }
        app.MapGet("/api/public/questionnaires/{token}",(string token)=>store.Read(c=>{
            var f=Load(token,c);return Results.Ok(new{titleAr=f.S("titleAr"),titleEn=f.S("titleEn"),titleUr=f.S("titleUr"),descriptionAr=f.S("descriptionAr"),descriptionEn=f.S("descriptionEn"),descriptionUr=f.S("descriptionUr"),expiresAt=f.S("expiresAt"),fields=f["fields"]?.DeepClone(),schema="custom-v1"});
        })).RequireRateLimiting("sensitive");
        app.MapPost("/api/public/questionnaires/{token}",(string token,JsonObject input)=>store.Write((c,tx)=>{
            var f=Load(token,c,tx);var answers=FormSchemas.Answers(f,input);var responses=Store.List(c,"custom_response",tx).Where(x=>x.S("formId")==f.S("id")).ToList();J.Need(responses.Count<f.N("maxSubmissions"),"اكتمل عدد الردود.");
            var iqamaField=((JsonArray)f["fields"]!).OfType<JsonObject>().FirstOrDefault(x=>x.S("type")=="iqama");
            if(iqamaField!=null&&answers.S(iqamaField.S("id"))!="") J.Need(!responses.Any(r=>r["answers"].S(iqamaField.S("id"))==answers.S(iqamaField.S("id"))),"سبق تسجيل رد لهذه الإقامة.");
            var response=new JsonObject{["id"]=Guid.NewGuid().ToString("N"),["formId"]=f.S("id"),["schemaVersion"]=f.N("schemaVersion"),["answers"]=answers,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};Store.Save(c,"custom_response",response,tx);Store.Audit(c,tx,"builder.response_received",response.S("id"),"public-form");return Results.Ok(new{receipt=response.S("id")});
        })).RequireRateLimiting("sensitive");
        app.MapGet("/questionnaires/{token}",(string token)=>{store.Read(c=>Load(token,c));return Results.File(Path.Combine(AppContext.BaseDirectory,"wwwroot","rider-form.html"),"text/html; charset=utf-8");});
    }
    private static object Analytics(DbConnection c,string id)
    {
        var f=Store.Get(c,id,"custom_form")??throw new ApiError("النموذج غير موجود.",404);var rows=Store.List(c,"custom_response").Where(x=>x.S("formId")==id).ToList();
        double hours=rows.Count==0?1:Math.Max(1d/60,(DateTimeOffset.UtcNow-rows.Select(x=>DateTimeOffset.Parse(x.S("createdAt"),CultureInfo.InvariantCulture)).Min()).TotalHours);
        return new{formId=id,totalResponses=rows.Count,responsesPerHour=Math.Round(rows.Count/hours,2),fields=((JsonArray)f["fields"]!).OfType<JsonObject>().Where(q=>q.S("type") is "select" or "radio").Select(q=>new{id=q.S("id"),label=q.S("labelAr"),options=((JsonArray)q["options"]!).OfType<JsonObject>().Select(o=>{int count=rows.Count(r=>r["answers"].S(q.S("id"))==o.S("value"));return new{value=o.S("value"),label=o.S("labelAr"),count,percentage=rows.Count==0?0:Math.Round(100m*count/rows.Count,2)};})})};
    }
}
