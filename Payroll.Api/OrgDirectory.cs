using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Payroll;

// BR §25.1: department membership and rank decide who responds; permissions alone never do.
// Every record is dated and append-only: ending a row sets validTo, it is never deleted or rewritten.
public static class OrgDirectory
{
    public const string Membership="org_membership",Delegation="org_delegation",Supervision="org_supervision",Routing="org_routing";
    public static readonly string[] Ranks=["C","B","A","A+"];
    public static int RankLevel(string rank)=>Array.IndexOf(Ranks,rank) is var i&&i>=0?i+1:throw new ApiError("الرتبة غير معروفة؛ استخدم A+ أو A أو B أو C.");
    static readonly Regex Operation=new(@"^advance\.(review|payout:[A-Z0-9._-]{2,40})$",RegexOptions.Compiled);
    public static string Today(DateTimeOffset? at=null)=>(at??DateTimeOffset.UtcNow).ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd");
    static string Date(JsonObject input,string key,string fallback="")
    {string value=input.S(key,fallback);J.Need(value==""||DateOnly.TryParseExact(value,"yyyy-MM-dd",out _),"التاريخ يجب أن يكون yyyy-MM-dd.");return value;}
    static bool Active(JsonObject row,string day,string from="validFrom",string to="validTo")=>string.CompareOrdinal(row.S(from),day)<=0&&(row.S(to)==""||string.CompareOrdinal(day,row.S(to))<=0);
    static void Manage(ClaimsPrincipal actor){if(!AccessPolicy.Administrator(actor)&&!AccessPolicy.Has(actor,"org.manage"))throw new ApiError("إدارة الأقسام والرتب لمسؤول النظام فقط.",403);}
    static string User(DbConnection c,DbTransaction? tx,string username)
    {J.Need(Store.List(c,"user",tx).Any(u=>u.S("username")==username),"المستخدم غير موجود.");return username;}
    static string Department(string code){J.Need(FinancialCore.DepartmentsCatalog.OfType<JsonObject>().Any(d=>d.S("code")==code),"القسم غير موجود في الكتالوج.");return code;}
    public static string Op(string operation){J.Need(Operation.IsMatch(operation),"نوع العملية غير معروف.");return operation;}

    public static JsonObject? MembershipOf(DbConnection c,DbTransaction? tx,string username,string department,string day)=>
        Store.List(c,Membership,tx).Where(m=>m.S("userId")==username&&m.S("department")==department&&Active(m,day)).OrderByDescending(m=>m.N("rankLevel")).FirstOrDefault();
    public static JsonObject? ActiveRouting(DbConnection c,DbTransaction? tx,string operation,string day)=>
        Store.List(c,Routing,tx).Where(r=>r.S("operation")==operation&&Active(r,day)).OrderByDescending(r=>r.S("validFrom")).ThenByDescending(r=>r.S("createdAt")).FirstOrDefault();

    // Who may act on an operation today, with the reason they qualify (kept as the decision snapshot).
    public static List<JsonObject> Responsible(DbConnection c,DbTransaction? tx,string operation,DateTimeOffset? at=null)
    {
        string day=Today(at);var routing=ActiveRouting(c,tx,operation,day);var result=new List<JsonObject>();if(routing==null)return result;
        string department=routing.S("department");int min=(int)routing.N("minRankLevel");
        foreach(var user in (routing["users"] as JsonArray??[]).Select(u=>u?.ToString()??"").Where(u=>u!="").Distinct())
            if(MembershipOf(c,tx,user,department,day) is {} m&&m.N("rankLevel")>=min)
                result.Add(new(){["userId"]=user,["department"]=department,["rank"]=m.S("rank"),["rankLevel"]=m.N("rankLevel"),["membershipId"]=m.S("id"),["routingId"]=routing.S("id"),["via"]="assignment"});
        foreach(var d in Store.List(c,Delegation,tx).Where(d=>d.S("operation")==operation&&string.CompareOrdinal(d.S("validFrom"),day)<=0&&string.CompareOrdinal(day,d.S("until"))<=0&&d.S("endedAt")==""))
            if(result.FirstOrDefault(r=>r.S("userId")==d.S("fromUser")&&r.S("via")=="assignment") is {} source&&result.All(r=>r.S("userId")!=d.S("toUser")))
            {var copy=source.Clone();copy["userId"]=d.S("toUser");copy["via"]="delegation";copy["delegationId"]=d.S("id");copy["delegatedBy"]=d.S("fromUser");result.Add(copy);}
        return result;
    }
    public static JsonObject DemandResponsible(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string operation)
    {
        var match=Responsible(c,tx,operation).FirstOrDefault(r=>r.S("userId")==actor.Identity?.Name);
        return match??throw new ApiError("لست المسؤول المكلّف بهذه العملية؛ الصلاحية وحدها لا تكفي (BR §25.1).",403);
    }
    public static bool Supervises(DbConnection c,DbTransaction? tx,string supervisor,string iqama,DateTimeOffset? at=null)=>
        Store.List(c,Supervision,tx).Any(s=>s.S("supervisorUserId")==supervisor&&s.S("riderIqama")==iqama&&Active(s,Today(at)));
    public static List<string> SupervisorsOf(DbConnection c,DbTransaction? tx,string iqama)=>
        Store.List(c,Supervision,tx).Where(s=>s.S("riderIqama")==iqama&&Active(s,Today())).Select(s=>s.S("supervisorUserId")).Distinct().ToList();
    public static List<JsonObject> SupervisedRiders(DbConnection c,DbTransaction? tx,string supervisor)=>
        Store.List(c,Supervision,tx).Where(s=>s.S("supervisorUserId")==supervisor&&Active(s,Today())).ToList();

    static JsonObject Save(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string kind,JsonObject row,string action)
    {row["id"]=kind+":"+Guid.NewGuid().ToString("N");row["createdBy"]=actor.Identity!.Name;row["createdAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,kind,row,tx);Store.Audit(c,tx,action,row.S("id"),actor.Identity!.Name!);return row;}
    public static JsonObject AddMembership(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        Manage(actor);string user=User(c,tx,input.Required("userId")),department=Department(input.Required("department")),rank=input.Required("rank");int level=RankLevel(rank);
        string from=Date(input,"validFrom",Today()),to=Date(input,"validTo");J.Need(to==""||string.CompareOrdinal(from,to)<=0,"تاريخ النهاية قبل البداية.");
        J.Need(!Store.List(c,Membership,tx).Any(m=>m.S("userId")==user&&m.S("department")==department&&(m.S("validTo")==""||string.CompareOrdinal(m.S("validTo"),from)>=0)&&(to==""||string.CompareOrdinal(m.S("validFrom"),to)<=0)),"للمستخدم عضوية متداخلة في نفس القسم؛ أنهِ السابقة أولًا.");
        return Save(c,tx,actor,Membership,new(){["userId"]=user,["department"]=department,["rank"]=rank,["rankLevel"]=level,["validFrom"]=from,["validTo"]=to},"org.membership_added");
    }
    public static JsonObject AddDelegation(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        Manage(actor);string from=User(c,tx,input.Required("fromUser")),to=User(c,tx,input.Required("toUser")),op=Op(input.Required("operation"));J.Need(from!=to,"لا تفوض لنفس المستخدم.");
        string start=Date(input,"validFrom",Today()),until=Date(input,"until");J.Need(until!=""&&string.CompareOrdinal(start,until)<=0,"التفويض يحتاج تاريخ نهاية بعد البداية.");
        return Save(c,tx,actor,Delegation,new(){["fromUser"]=from,["toUser"]=to,["operation"]=op,["validFrom"]=start,["until"]=until,["endedAt"]="",["reason"]=input.S("reason")},"org.delegation_added");
    }
    public static JsonObject AddSupervision(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        Manage(actor);string supervisor=User(c,tx,input.Required("supervisorUserId")),iqama=DirectoryProfiles.Iqama(input.Required("riderIqama"));J.Need(Store.Get(c,iqama,"rider",tx)!=null,"المندوب غير موجود.");
        string from=Date(input,"validFrom",Today()),to=Date(input,"validTo");Span(from,to);
        J.Need(!Store.List(c,Supervision,tx).Any(s=>s.S("riderIqama")==iqama&&s.S("supervisorUserId")==supervisor&&s.S("validTo")==""),"الإشراف مسجل بالفعل.");
        return Save(c,tx,actor,Supervision,new(){["supervisorUserId"]=supervisor,["riderIqama"]=iqama,["validFrom"]=from,["validTo"]=to},"org.supervision_added");
    }
    public static JsonObject AddRouting(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        Manage(actor);string op=Op(input.Required("operation")),department=Department(input.Required("department"));int min=RankLevel(input.S("minRank","C"));
        if(op.StartsWith("advance.payout:",StringComparison.Ordinal)){string vault=op["advance.payout:".Length..];J.Need(vault==AdvanceRequests.Mada||FinancialCore.ChartOfAccounts.Any(a=>a.Code==vault&&a.IsVault),"الخزنة أو البنك غير موجود.");}
        var users=input["users"] as JsonArray??throw new ApiError("حدد المسؤولين بأسمائهم.");J.Need(users.Count is >0 and <=20,"عدد المسؤولين غير صالح.");
        var clean=new JsonArray();foreach(var u in users.Select(x=>x?.ToString()??"").Distinct())clean.Add(User(c,tx,u));
        string from=Date(input,"validFrom",Today()),to=Date(input,"validTo");Span(from,to);
        return Save(c,tx,actor,Routing,new(){["operation"]=op,["department"]=department,["minRank"]=input.S("minRank","C"),["minRankLevel"]=min,["users"]=clean,["validFrom"]=from,["validTo"]=to},"org.routing_added");
    }
    public static JsonObject End(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string kind,string id,JsonObject input)
    {
        Manage(actor);J.Need(kind is Membership or Supervision or Routing or Delegation,"نوع غير معروف.");var row=Store.Get(c,id,kind,tx)?.Clone()??throw new ApiError("السجل غير موجود.",404);
        string day=Date(input,"validTo",Today());
        if(kind==Delegation){J.Need(row.S("endedAt")=="","التفويض منتهٍ.");J.Need(string.CompareOrdinal(row.S("validFrom"),day)<=0,"تاريخ النهاية قبل البداية.");row["endedAt"]=DateTimeOffset.UtcNow.ToString("O");row["until"]=string.CompareOrdinal(day,row.S("until"))<0?day:row.S("until");}
        else{J.Need(row.S("validTo")==""||string.CompareOrdinal(day,row.S("validTo"))<0,"السجل منتهٍ بالفعل.");J.Need(string.CompareOrdinal(row.S("validFrom"),day)<=0,"تاريخ النهاية قبل البداية.");row["validTo"]=day;}
        row["endedBy"]=actor.Identity!.Name;Store.Save(c,kind,row,tx);Store.Audit(c,tx,"org.ended",id,actor.Identity!.Name!);return row;
    }
    static void Span(string from,string to)=>J.Need(from!=""&&(to==""||string.CompareOrdinal(from,to)<=0),"تاريخ النهاية قبل البداية.");
    static object List(DbConnection c,HttpContext ctx,string kind)
    {
        if(kind==Supervision&&ctx.Request.Query["mine"]=="1")return SupervisedRiders(c,null,ctx.User.Identity!.Name!).Select(s=>{var v=s.Clone();v["riderName"]=Store.Get(c,s.S("riderIqama"),"rider")?.S("name")??"";return v;}).ToList();
        Manage(ctx.User);return Store.List(c,kind).OrderByDescending(r=>r.S("createdAt")).ToList();
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        const string T="Administration";
        api.MapGet("/org/catalog",()=>new{departments=FinancialCore.DepartmentsCatalog,ranks=Ranks.Select((r,i)=>new{rank=r,level=i+1}),operations=new[]{"advance.review","advance.payout:<vaultCode>"}}).WithTags(T).WithSummary("Read departments, ranks A+/A/B/C and routed operations");
        api.MapGet("/org/memberships",(HttpContext ctx)=>store.Read(c=>List(c,ctx,Membership))).WithTags(T).WithSummary("Read dated department memberships and ranks");
        api.MapPost("/org/memberships",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>AddMembership(c,tx,ctx.User,input))).WithTags(T).WithSummary("Add a dated membership with rank A+/A/B/C");
        api.MapPost("/org/memberships/{id}/end",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>End(c,tx,ctx.User,Membership,id,input))).WithTags(T).WithSummary("End a membership without deleting history");
        api.MapGet("/org/delegations",(HttpContext ctx)=>store.Read(c=>List(c,ctx,Delegation))).WithTags(T).WithSummary("Read dated delegations");
        api.MapPost("/org/delegations",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>AddDelegation(c,tx,ctx.User,input))).WithTags(T).WithSummary("Add a delegation that expires on its end date");
        api.MapPost("/org/delegations/{id}/end",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>End(c,tx,ctx.User,Delegation,id,input))).WithTags(T).WithSummary("End a delegation early");
        api.MapGet("/org/supervision",(HttpContext ctx)=>store.Read(c=>List(c,ctx,Supervision))).WithTags(T).WithSummary("Read supervisor-rider links; mine=1 lists the caller's active riders");
        api.MapPost("/org/supervision",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>AddSupervision(c,tx,ctx.User,input))).WithTags(T).WithSummary("Link a supervisor to a rider from a date");
        api.MapPost("/org/supervision/{id}/end",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>End(c,tx,ctx.User,Supervision,id,input))).WithTags(T).WithSummary("End a supervisor-rider link");
        api.MapGet("/org/routing",(HttpContext ctx)=>store.Read(c=>List(c,ctx,Routing))).WithTags(T).WithSummary("Read operation routing: department, minimum rank and named responsible users or custodians");
        api.MapPost("/org/routing",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>AddRouting(c,tx,ctx.User,input))).WithTags(T).WithSummary("Add a dated routing row; the newest active row wins");
        api.MapPost("/org/routing/{id}/end",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>End(c,tx,ctx.User,Routing,id,input))).WithTags(T).WithSummary("End a routing row");
        api.MapGet("/org/responsible",(string operation,HttpContext ctx)=>{Manage(ctx.User);return store.Read(c=>Responsible(c,null,Op(operation)));}).WithTags(T).WithSummary("Resolve who is responsible for an operation today");
    }
}
