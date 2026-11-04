using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static class PlatformPosting
{
    public static readonly string[] Codes=["hs","jahez","keeta","ninja","chefz","amazon"];
    static readonly string[] HsAccounts=["112802","112803","212103","411601","411102","213201","112502","526402","112507","112306","112501","112513","114107","114101"];
    public static string Code(string value){string code=value.ToLowerInvariant();if(code=="hungerstation")code="hs";J.Need(Codes.Contains(code),"منصة غير معروفة.");return code;}
    public static bool Enabled(DbConnection c,string code,DbTransaction? tx=null)=>Store.Get(c,"platformPosting."+Code(code),"setting",tx).B("enabled");
    public static JsonObject Set(DbConnection c,DbTransaction tx,ClaimsPrincipal user,string code,JsonObject input)
    {
        if(!AccessPolicy.Administrator(user))throw new ApiError("إعداد الترحيل لمسؤول النظام فقط.",403);
        code=Code(code);if(input["enabled"] is not JsonValue flag||!flag.TryGetValue<bool>(out bool enabled))throw new ApiError("حدد حالة الترحيل بقيمة منطقية.");
        string id="platformPosting."+code;var previous=Store.Get(c,id,"setting",tx);
        if(previous?.B("enabled")==enabled)return previous.Clone();
        var doc=previous?.Clone()??new JsonObject{["id"]=id};doc["enabled"]=enabled;doc["updatedBy"]=user.Identity!.Name;doc["updatedAt"]=DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c,"setting",doc,tx);Store.Audit(c,tx,"platform.posting_gate_changed",id,user.Identity!.Name!);return doc;
    }
    public static void Demand(DbConnection c,string code,DbTransaction? tx=null)
    {if(!Enabled(c,code,tx))throw new ApiError("الترحيل المحاسبي لهذه المنصة متوقف؛ راجع الإعداد المعتمد.",409);}
    public static FinancialCore.AccountDef TargetAccount(string code)
    {
        var row=FinanceViews.Accounts.SingleOrDefault(a=>a.S("code")==code)??throw new ApiError("حساب هنقر غير معتمد أو غير قابل للترحيل.");
        J.Need(HsAccounts.Contains(code)&&row.B("posting")&&row.S("status")=="active","حساب هنقر غير معتمد أو غير قابل للترحيل.");
        string type=code[0] switch{'1'=>"Asset",'2'=>"Liability",'3'=>"Equity",'4'=>"Revenue",_=>"Expense"};
        return new(code,row!.S("nameAr"),type,row.S("nature")=="debit"?"Debit":"Credit",row["parent"]?.ToString());
    }
    public static JsonObject Post(DbConnection c,DbTransaction tx,string period,string reference,JsonArray lines,string actor,string description="وقائع هنقر المعتمدة",string? reversesJournalId=null)
    {
        Demand(c,"hs",tx);
        foreach(var line in lines.OfType<JsonObject>())AccountingPolicy.ValidateOperation(line.S("accountCode"),"PlatformInvoice","platform_posting","HungerStation");
        var doc=FinancialCore.PostJournal(c,tx,"hs_approved_facts",description,period,reference,lines,actor,targetChart:true,reversesJournalId:reversesJournalId);
        Store.Audit(c,tx,"platform.hs.journal_posted",doc.S("id"),actor);return doc;
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/platforms/{code}/posting",(string code,HttpContext ctx)=>
        {if(!AccessPolicy.Administrator(ctx.User))throw new ApiError("إعداد الترحيل لمسؤول النظام فقط.",403);return store.Read(c=>new{code=Code(code),enabled=Enabled(c,code)});}).WithTags("Platforms").WithSummary("Read administrator platform posting gate; absent defaults off");
        api.MapPost("/platforms/{code}/posting",(string code,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Set(c,tx,ctx.User,code,input))).WithTags("Platforms").WithSummary("Set and audit platform posting gate; administrator only");
    }
}
