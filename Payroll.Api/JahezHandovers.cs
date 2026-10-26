using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static class JahezHandovers
{
    public static bool CanRead(ClaimsPrincipal actor,JsonObject request)
    {
        if(!FinancialScopes.Sponsor(actor,request.S("sponsorId")))return false;string code=request.S("sponsorCode");
        bool source=new[]{("SET",request.N("settlementAmount")),("FEE",request.N("feeAmount"))}.Where(p=>p.Item2>0).All(p=>FinancialScopes.Account(actor,$"SUP.{code}.{p.Item1}"));
        bool target=AccessPolicy.Has(actor,"treasury.jahez.receive")&&new[]{("SET",request.N("settlementAmount")),("FEE",request.N("feeAmount"))}.Where(p=>p.Item2>0).All(p=>FinancialScopes.Account(actor,$"MAIN.{code}.{p.Item1}"));return source||target;
    }
    public static JsonObject Submit(DbConnection c,DbTransaction tx,JsonObject input,ClaimsPrincipal actor)
    {
        AccessPolicy.Demand(actor,"treasury.jahez.handover");var owner=FinancialCore.ResolveSponsor(input.Required("sponsorId"));FinancialScopes.DemandSponsor(actor,owner.Id);
        J.Need(!Store.List(c,"jahez_handover",tx).Any(h=>h.S("sponsorId")==owner.Id&&h.S("status")=="pending"),"يوجد تسليم معلق لهذا السجل؛ أكمل استلامه أو ارفضه أولًا.");
        string reference=input.Required("sourceRef");J.Need(!Store.List(c,"journal",tx).Any(j=>j.S("sourceRef")==reference)&&!Store.List(c,"jahez_handover",tx).Any(h=>h.S("sourceRef")==reference),"مرجع التسليم مستخدم.");
        decimal settlement=AccountingPolicy.Balance(c,tx,$"SUP.{owner.Code}.SET"),fee=AccountingPolicy.Balance(c,tx,$"SUP.{owner.Code}.FEE");
        decimal requestedSettlement=input.ContainsKey("settlementAmount")&&input.S("settlementAmount")!=""?Debts.OptionalAmount(input,"settlementAmount"):(FinancialScopes.Account(actor,$"SUP.{owner.Code}.SET")?Math.Max(0,settlement):0),requestedFee=input.ContainsKey("feeAmount")&&input.S("feeAmount")!=""?Debts.OptionalAmount(input,"feeAmount"):(FinancialScopes.Account(actor,$"SUP.{owner.Code}.FEE")?Math.Max(0,fee):0);
        AccountingPolicy.RequireAvailable(settlement,requestedSettlement);AccountingPolicy.RequireAvailable(fee,requestedFee);J.Need(requestedSettlement+requestedFee>0,"لا توجد عهدة موجبة للتسليم.");
        if(requestedSettlement>0)FinancialScopes.DemandAccount(actor,$"SUP.{owner.Code}.SET");if(requestedFee>0)FinancialScopes.DemandAccount(actor,$"SUP.{owner.Code}.FEE");
        var request=new JsonObject{["id"]=Guid.NewGuid().ToString("N"),["sponsorId"]=owner.Id,["sponsorCode"]=owner.Code,["sponsorNameAr"]=owner.ShortNameAr,["status"]="pending",["settlementAmount"]=requestedSettlement,["feeAmount"]=requestedFee,["sourceRef"]=reference,["submittedBy"]=actor.Identity!.Name!,["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["version"]=1};
        Store.Save(c,"jahez_handover",request,tx);Store.Audit(c,tx,"jahez.handover_submitted",request.S("id"),actor.Identity.Name!);return request;
    }
    public static JsonObject Decide(DbConnection c,DbTransaction tx,string id,JsonObject input,ClaimsPrincipal actor,bool approve)
    {
        AccessPolicy.Demand(actor,"treasury.jahez.receive");var request=Store.Get(c,id,"jahez_handover",tx)??throw new ApiError("طلب التسليم غير موجود.",404);var owner=FinancialCore.ResolveSponsor(request.S("sponsorId"));FinancialScopes.DemandSponsor(actor,owner.Id);
        J.Need(request.S("status")=="pending","تم البت في التسليم؛ لا يُرحّل مرة ثانية.");
        if(!AccessPolicy.Administrator(actor)&&(actor.FindFirstValue("department")!="FIN"||!int.TryParse(actor.FindFirstValue("rankLevel"),out int rank)||rank<2))throw new ApiError("استلام العهدة للمالية بالرتبة المناسبة.",403);
        if(request.N("settlementAmount")>0)FinancialScopes.DemandAccount(actor,$"MAIN.{owner.Code}.SET");if(request.N("feeAmount")>0)FinancialScopes.DemandAccount(actor,$"MAIN.{owner.Code}.FEE");
        bool same=string.Equals(request.S("submittedBy"),actor.Identity!.Name,StringComparison.OrdinalIgnoreCase);
        if(same&&(!AccessPolicy.Administrator(actor)||!input.B("overrideSelf")||input.S("note")==""))throw new ApiError("يلزم مستلم مستقل؛ تجاوز المدير يحتاج تأكيدًا صريحًا وسببًا مسجلًا.",403);
        if(approve)
        {
            J.Need(input.ContainsKey("settlementCounted")&&input.ContainsKey("feeCounted"),"أدخل العد الفعلي لكل من التصفيات والرسوم، بما فيه الصفر.");
            decimal settlement=Debts.OptionalAmount(input,"settlementCounted"),fee=Debts.OptionalAmount(input,"feeCounted");
            J.Need(settlement==request.N("settlementAmount")&&fee==request.N("feeAmount"),"العد الفعلي لا يطابق غرضي العهدة؛ لا تُخلط حصيلة الرسوم بالتصفيات.");
            var lines=new JsonArray();
            foreach(var pair in new[]{("SET",settlement),("FEE",fee)})
            {
                if(pair.Item2==0)continue;string source=$"SUP.{owner.Code}.{pair.Item1}",target=$"MAIN.{owner.Code}.{pair.Item1}";FinancialScopes.DemandAccount(actor,target);AccountingEvents.DemandAvailable(c,tx,source,pair.Item2);
                lines.Add(FinancialCore.CreateJournalLine(target,owner.Id,pair.Item2,0,"استلام عهدة معدودة دون إيراد جديد"));lines.Add(FinancialCore.CreateJournalLine(source,owner.Id,0,pair.Item2,"تسليم عهدة مؤكدة من المالية"));
            }
            var journal=FinancialCore.PostJournal(c,tx,"jahez_eod_handover","استلام عهدة جاهز بعد العد والمراجعة المستقلة",DateTime.UtcNow.ToString("yyyy-MM"),request.S("sourceRef"),lines,actor.Identity.Name!,"","",owner.Id,owner.Id);
            request["journalEntryId"]=journal.S("id");request["journalNumber"]=journal.S("journalNumber");request["settlementCounted"]=settlement;request["feeCounted"]=fee;
        }
        else input.Required("note");
        request["status"]=approve?"approved":"rejected";request["decidedBy"]=actor.Identity.Name;request["decidedAt"]=DateTimeOffset.UtcNow.ToString("O");request["decisionNote"]=input.S("note");request["administrativeSelfOverride"]=same;request["version"]=2;
        Store.Save(c,"jahez_handover",request,tx);Store.Audit(c,tx,approve?"jahez.handover_received":"jahez.handover_rejected",id,actor.Identity.Name!);return request;
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/finance/jahez-handover",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Submit(c,tx,input,ctx.User)));
        api.MapGet("/finance/jahez-handovers",(HttpContext ctx)=>store.Read(c=>Store.List(c,"jahez_handover").Where(h=>CanRead(ctx.User,h))));
        api.MapPost("/finance/jahez-handovers/{id}/confirm",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Decide(c,tx,id,input,ctx.User,true)));
        api.MapPost("/finance/jahez-handovers/{id}/reject",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Decide(c,tx,id,input,ctx.User,false)));
    }
}
