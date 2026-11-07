using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Payroll;

// BR §27: admin lists with custom fields, linked to the accounting structure.
// Items keep every version; financial groups need a second person to approve a change.
public static class ConfigLists
{
    public const string ItemKind="config_list_item",FieldKind="config_list_fields";
    public sealed record FieldDef(string Key,string LabelAr,string Type,bool Required,string[]? Options=null);
    public sealed record GroupDef(string Group,string LabelAr,bool Financial,FieldDef[] Fields);
    static readonly string[] Types=["text","number","select","account","bool"];
    public static readonly GroupDef[] Groups=
    [
        new("recharge_networks","شبكات الشحن",false,[]),
        new("recharge_packages","باقات الشحن",true,[new("network","الشبكة","select",true),new("price","السعر","number",true)]),
        new("deduction_types","أنواع الاستقطاع",true,[new("category","فئة الدين","select",true),new("account","الحساب","account",true),new("phase","المرحلة","number",true,["1","2","3"]),new("order","الترتيب داخل المرحلة","number",false)]),
        new("operating_systems","أنظمة التشغيل",false,[]),
        new("pay_systems","أنظمة الرواتب",false,[]),
        new("salary_classifications","تصنيفات الرواتب",false,[]),
    ];
    // BR §29 defaults: the deduction phase of each debt category.
    public static readonly (string Category,string NameAr,string Account,int Phase,int Order)[] DefaultDeductions=
    [
        ("FuelOverage","فرق الوقود","112510",1,10),("VehicleInstallment","إيجار / قسط المركبة","112505",1,20),
        ("CashAdvance","سلفة نقدية","112501",1,30),("BankAdvance","سلفة بنكية","112501",1,31),("Advance","سلفة","112501",1,32),
        ("InternetAdvance","سلفة إنترنت","112503",1,40),("HsWalletTopUp","شحن محفظة هنقرستيشن","112513",1,45),("HousingDeduction","استقطاع سكن","112505",1,50),
        ("AdministrativeDeduction","استقطاع إداري","112505",1,60),("EquipmentLoan","عهدة معدات","112505",1,70),("CashCustody","عهدة نقدية","112505",1,80),
        ("WorkPermit","رخصة عمل","112506",2,10),("IqamaRenewal","تجديد إقامة","112506",2,11),("SponsorshipTransfer","نقل كفالة","112506",2,12),("ExitReEntryVisa","تأشيرة خروج وعودة","112506",2,13),
        ("MedicalInsurance","تأمين طبي","112506",2,14),("ProfessionChange","تغيير مهنة","112506",2,15),("DriverLicense","رخصة قيادة","112506",2,16),("DriverCard","كرت سائق","112506",2,17),("OtherHrService","خدمة HR أخرى","112506",2,18),
        ("TrafficFine","مخالفة مرورية","112502",2,30),("PlatformTransferredDebt","مديونية منصة محولة","112508",2,40),("LegacyOpeningDebt","مديونية افتتاحية قديمة","112505",2,50),("NegativeSalaryCarryover","عجز راتب مرحل","112507",2,60),
        ("AccidentDamage","أضرار حادث","112509",3,10),
    ];
    public static GroupDef Group(string group)=>Groups.FirstOrDefault(g=>g.Group==group)??throw new ApiError("القائمة غير موجودة.",404);
    static string ItemId(string group,string code)=>"cfg:"+group+":"+code;
    static string Me(ClaimsPrincipal u)=>u.Identity!.Name!;
    static string Now()=>DateTimeOffset.UtcNow.ToString("O");
    public static List<FieldDef> Fields(DbConnection c,DbTransaction? tx,string group)
    {
        var def=Group(group);var custom=(Store.Get(c,"cfg-fields:"+group,FieldKind,tx)?["fields"] as JsonArray??[]).OfType<JsonObject>()
            .Select(f=>new FieldDef(f.S("key"),f.S("labelAr"),f.S("type"),f.B("required"),(f["options"] as JsonArray)?.Select(o=>o!.ToString()).ToArray()));
        return [..def.Fields,..custom];
    }
    public static List<JsonObject> Items(DbConnection c,DbTransaction? tx,string group)=>Store.List(c,ItemKind,tx).Where(i=>i.S("group")==group).OrderBy(i=>i.S("code"),StringComparer.Ordinal).ToList();
    // Current, approved and active values for one item; null while it has never been approved.
    public static JsonObject? Current(DbConnection c,DbTransaction? tx,string group,string code)
    {var item=Store.Get(c,ItemId(group,code),ItemKind,tx);return item?["current"] is JsonObject cur&&item.B("active")?cur:null;}
    static JsonObject View(GroupDef def,List<FieldDef> fields,List<JsonObject> items)=>new()
    {
        ["group"]=def.Group,["labelAr"]=def.LabelAr,["financial"]=def.Financial,
        ["fields"]=new JsonArray(fields.Select(f=>(JsonNode)new JsonObject{["key"]=f.Key,["labelAr"]=f.LabelAr,["type"]=f.Type,["required"]=f.Required,["options"]=f.Options==null?null:new JsonArray(f.Options.Select(o=>(JsonNode)o).ToArray())}).ToArray()),
        ["items"]=new JsonArray(items.Select(i=>(JsonNode)new JsonObject{
            ["code"]=i.S("code"),["nameAr"]=i["current"].S("nameAr",i["pending"].S("nameAr")),["active"]=i.B("active"),
            ["values"]=(i["current"]?["values"] ?? i["pending"]?["values"] ?? new JsonObject()).DeepClone(),
            ["status"]=i["pending"] is JsonObject?"pending_approval":"active",["pending"]=i["pending"]?.DeepClone(),["version"]=i.N("version")}).ToArray())
    };
    static JsonObject Values(DbConnection c,DbTransaction tx,string group,List<FieldDef> fields,JsonObject? input)
    {
        input??=new();var values=new JsonObject();
        foreach(var key in input.Select(p=>p.Key))J.Need(fields.Any(f=>f.Key==key),"حقل غير معرّف: "+key);
        foreach(var f in fields)
        {
            string v=input.S(f.Key);if(v==""){J.Need(!f.Required,"الحقل مطلوب: "+f.LabelAr);continue;}
            J.Need(v.Length<=200,"قيمة طويلة جدًا.");
            switch(f.Type)
            {
                case "number":J.Need(decimal.TryParse(v,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var n)&&n>=0,"رقم غير صالح: "+f.LabelAr);values[f.Key]=n;continue;
                case "bool":J.Need(v is "true" or "false","قيمة نعم/لا غير صالحة.");values[f.Key]=v=="true";continue;
                case "account":values[f.Key]=PayPlatforms.Active(v);continue;
                case "select":if(f.Options!=null)J.Need(f.Options.Contains(v),"اختيار غير صالح: "+f.LabelAr);break;
            }
            values[f.Key]=v;
        }
        if(group=="recharge_packages")J.Need(Current(c,tx,"recharge_networks",values.S("network")) is not null,"الشبكة غير موجودة أو غير معتمدة.");
        if(group=="deduction_types")
        {
            J.Need(FinancialCore.CategoriesCatalog.Any(x=>x.Code==values.S("category")),"فئة الدين غير موجودة.");
            J.Need(values.S("account").StartsWith("1125",StringComparison.Ordinal),"حساب الاستقطاع يكون من ذمم المناديب 1125xx.");
            J.Need(values.N("phase") is 1 or 2 or 3,"المرحلة 1 أو 2 أو 3.");
        }
        return values;
    }
    public static JsonObject Upsert(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string group,JsonObject input)
    {
        AccessPolicy.Demand(actor,"pay.manage");var def=Group(group);
        string code=input.Required("code");J.Need(Regex.IsMatch(code,"^[A-Za-z0-9_-]{1,40}$"),"الكود حروف إنجليزية وأرقام فقط.");
        string name=input.Required("nameAr");J.Need(name.Length<=120&&!name.Any(char.IsControl),"الاسم طويل جدًا.");
        var values=Values(c,tx,group,Fields(c,tx,group),input["values"] as JsonObject);bool active=input["active"] is null||input.B("active");
        var item=Store.Get(c,ItemId(group,code),ItemKind,tx)?.Clone()??new JsonObject{["id"]=ItemId(group,code),["group"]=group,["code"]=code,["version"]=0,["history"]=new JsonArray(),["active"]=active};
        var proposal=new JsonObject{["nameAr"]=name,["values"]=values,["active"]=active,["by"]=Me(actor),["at"]=Now()};
        if(def.Financial)item["pending"]=proposal;else Apply(item,proposal,Me(actor));
        Store.Save(c,ItemKind,item,tx);Store.Audit(c,tx,def.Financial?"config.item_proposed":"config.item_saved",item.S("id"),Me(actor));return item;
    }
    static void Apply(JsonObject item,JsonObject proposal,string approvedBy)
    {
        if(item["current"] is JsonObject old)(item["history"] as JsonArray)!.Add(old.DeepClone());
        var cur=new JsonObject{["nameAr"]=proposal.S("nameAr"),["values"]=proposal["values"]!.DeepClone(),["by"]=proposal.S("by"),["approvedBy"]=approvedBy,["validFrom"]=Now()};
        item["current"]=cur;item["active"]=proposal.B("active");item["version"]=item.N("version")+1;item.Remove("pending");
    }
    public static JsonObject Approve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string group,string code)
    {
        AccessPolicy.Demand(actor,"pay.approve");Group(group);
        var item=Store.Get(c,ItemId(group,code),ItemKind,tx)?.Clone()??throw new ApiError("البند غير موجود.",404);
        var pending=item["pending"] as JsonObject??throw new ApiError("لا يوجد تعديل بانتظار الاعتماد.",409);
        J.Need(pending.S("by")!=Me(actor),"الاعتماد لشخص غير مقدم التعديل.",403);
        Apply(item,pending,Me(actor));Store.Save(c,ItemKind,item,tx);Store.Audit(c,tx,"config.item_approved",item.S("id"),Me(actor));return item;
    }
    public static JsonObject AddField(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string group,JsonObject input)
    {
        AccessPolicy.Demand(actor,"pay.manage");Group(group);
        string key=input.Required("key");J.Need(Regex.IsMatch(key,"^[a-z][a-zA-Z0-9]{0,30}$"),"مفتاح الحقل إنجليزي يبدأ بحرف صغير.");
        J.Need(Fields(c,tx,group).All(f=>f.Key!=key),"الحقل موجود بالفعل.");
        string type=input.Required("type");J.Need(Types.Contains(type),"نوع الحقل غير صالح.");
        var options=input["options"] as JsonArray;J.Need(type!="select"||options is {Count:>0},"حقل الاختيار يحتاج خيارات.");
        var doc=Store.Get(c,"cfg-fields:"+group,FieldKind,tx)?.Clone()??new JsonObject{["id"]="cfg-fields:"+group,["fields"]=new JsonArray()};
        // A new custom field is never required for items saved before it existed.
        (doc["fields"] as JsonArray)!.Add(new JsonObject{["key"]=key,["labelAr"]=input.Required("labelAr"),["type"]=type,["required"]=false,["options"]=options?.DeepClone(),["by"]=Me(actor),["at"]=Now()});
        Store.Save(c,FieldKind,doc,tx);Store.Audit(c,tx,"config.field_added",doc.S("id"),Me(actor));return doc;
    }
    public static void Seed(Store store)
    {
        const string marker="migration:config-lists:v1";
        store.Write((c,tx)=>
        {
            if(Store.Get(c,marker,"setting",tx)!=null)return false;
            void Put(string group,string code,string name,JsonObject values)
            {
                if(Store.Get(c,ItemId(group,code),ItemKind,tx)!=null)return;
                var item=new JsonObject{["id"]=ItemId(group,code),["group"]=group,["code"]=code,["version"]=0,["history"]=new JsonArray(),["active"]=true};
                Apply(item,new JsonObject{["nameAr"]=name,["values"]=values,["active"]=true,["by"]="system-seed"},"system-seed (BR §27/§29)");Store.Save(c,ItemKind,item,tx);
            }
            foreach(var d in DefaultDeductions)Put("deduction_types",d.Category,d.NameAr,new(){["category"]=d.Category,["account"]=d.Account,["phase"]=d.Phase,["order"]=d.Order});
            foreach(var (code,name) in new[]{("kafala","كفالة"),("company_car","سيارة شركة"),("own_car","سيارة خاصة"),("bike","دباب")})Put("operating_systems",code,name,new());
            foreach(var (code,name) in new[]{("commission","عمولة"),("salary","راتب"),("percentage","نسبة")})Put("pay_systems",code,name,new());
            foreach(var (code,name) in new[]{("STC","STC"),("MOBILY","موبايلي"),("ZAIN","زين")})Put("recharge_networks",code,name,new());
            Store.Save(c,"setting",new(){["id"]=marker,["version"]=1},tx);return true;
        },false);
    }
    // BR §29: the phase and order of a deduction category; approved list first, then the built-in default.
    public static (int Phase,int Order,string LabelAr) Phase(DbConnection c,DbTransaction? tx,string category)
    {
        var cur=Current(c,tx,"deduction_types",category);
        if(cur!=null)return ((int)cur["values"].N("phase",1),(int)cur["values"].N("order",99),cur.S("nameAr"));
        var d=DefaultDeductions.FirstOrDefault(x=>x.Category==category);
        return d.Category==null?(3,99,category):(d.Phase,d.Order,d.NameAr);
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/config/lists",(HttpContext ctx)=>store.Read(c=>Groups.Select(g=>View(g,Fields(c,null,g.Group),Items(c,null,g.Group))).ToList())).WithTags("Administration").WithSummary("Read admin lists with fields and item versions");
        api.MapGet("/config/lists/{group}",(string group)=>store.Read(c=>View(Group(group),Fields(c,null,group),Items(c,null,group)))).WithTags("Administration").WithSummary("Read one admin list");
        api.MapPost("/config/lists/{group}/items",(string group,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Upsert(c,tx,ctx.User,group,input))).WithTags("Administration").WithSummary("Add or change a list item; financial lists wait for approval");
        api.MapPost("/config/lists/{group}/items/{code}/approve",(string group,string code,HttpContext ctx)=>store.Write((c,tx)=>Approve(c,tx,ctx.User,group,code))).WithTags("Administration").WithSummary("Approve a pending financial list change by another user");
        api.MapPost("/config/lists/{group}/fields",(string group,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>AddField(c,tx,ctx.User,group,input))).WithTags("Administration").WithSummary("Add a custom field to a list");
    }
}
