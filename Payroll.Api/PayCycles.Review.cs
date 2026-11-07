using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

// BR §32: one grid per cycle. The reviewer sees every item by phase, edits what needs editing
// (an edit approves the row), approves one row or all ready rows. Final approval stays pay.approve.
public static partial class PayCycles
{
    const int ReviewStage=6;
    static JsonObject Reviewable(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id,string permission)
    {
        var cycle=Store.Get(c,id,Kind,tx)?.Clone()??throw new ApiError("الدورة غير موجودة.",404);Scope(actor,cycle,permission);
        if(permission!="pay.view")J.Need((int)cycle.N("stage")==ReviewStage&&cycle.S("status") is "review" or "needs_decision","المراجعة متاحة بعد الدمج (المرحلة 6) وقبل الاعتماد النهائي.",409);
        return cycle;
    }
    static JsonObject ReviewRow(DbConnection c,DbTransaction? tx,JsonObject cycle,JsonObject summary)
    {
        string rider=summary.S("rider");var lines=Lines(c,tx,cycle.S("id")).Where(l=>l.S("rider")==rider).ToList();
        var items=new JsonArray();
        foreach(var l in lines)
        {
            if(l.N("accountDeduction")!=0)items.Add(Item("platform:"+l.S("id"),1,5,"خصم منصة على اليوزر ("+l.S("app")+")",-l.N("accountDeduction"),false,null,l.S("app")));
            if(l.N("allocatedVehicleCharge")!=0)items.Add(Item("vehicle:"+l.S("id"),1,20,"إيجار / بدل المركبة",-l.N("allocatedVehicleCharge"),false,null,l.S("vehicleType")));
        }
        foreach(var d in Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("rider")==rider))
        {
            bool debt=d.S("type")=="debt";
            items.Add(Item(DeductionKey(d),(int)d.N("phase",3),(int)d.N("order",99),d.S("labelAr",d.S("category")),d.N("amount"),debt,debt?-d.N("computed",d.N("amount")):null,d.S("source")));
        }
        var ordered=new JsonArray(items.OfType<JsonObject>().OrderBy(i=>i.N("phase")).ThenBy(i=>i.N("order")).Select(i=>(JsonNode)i.DeepClone()).ToArray());
        var person=Store.Get(c,rider,"rider",tx);
        var row=new JsonObject{["rider"]=rider,["name"]=person?.S("name")??"",["status"]=summary.S("status"),["review"]=summary.S("review","pending"),["reviewedBy"]=summary["reviewedBy"]?.DeepClone(),["reviewedAt"]=summary["reviewedAt"]?.DeepClone()};
        foreach(string k in new[]{"gross","commission","afterCommission","phase1","preliminaryDue","phase2","phase3","net"})row[k]=summary[k]?.DeepClone();
        row["items"]=ordered;row["edits"]=(summary["edits"] as JsonArray??[]).DeepClone();return row;
    }
    static string DeductionKey(JsonObject d)=>d.S("type")=="review_adjustment"?"adjust:"+d.S("id"):d.S("type")+":"+d.S("source");
    static JsonObject Item(string key,int phase,int order,string label,decimal amount,bool editable,decimal? max,string source)=>
        new(){["key"]=key,["phase"]=phase,["order"]=order,["labelAr"]=label,["amount"]=J.Money(amount),["editable"]=editable,["max"]=max==null?null:J.Money(max.Value),["source"]=source};
    public static JsonObject ReviewView(DbConnection c,ClaimsPrincipal actor,string id,string review,string status)
    {
        var cycle=Reviewable(c,null,actor,id,"pay.view");var riders=Rows(cycle["riders"]).OfType<JsonObject>().ToList();
        J.Need(review is "" or "pending" or "approved"&&status is "" or "ready" or "needs_decision","مرشح غير صالح.");
        var rows=riders.Where(r=>(review==""||r.S("review","pending")==review)&&(status==""||r.S("status")==status)).Select(r=>(JsonNode)ReviewRow(c,null,cycle,r)).ToArray();
        return new(){
            ["cycle"]=new JsonObject{["id"]=cycle.S("id"),["period"]=cycle.S("period"),["companyId"]=cycle.S("companyId"),["stage"]=cycle.N("stage"),["status"]=cycle.S("status")},
            ["totals"]=new JsonObject{["riders"]=riders.Count,["approved"]=riders.Count(r=>r.S("review")=="approved"),["pending"]=riders.Count(r=>r.S("review","pending")!="approved"),
                ["needsDecision"]=riders.Count(r=>r.S("status")!="ready"),["net"]=J.Money(riders.Sum(r=>r.N("net")))},
            ["rows"]=new JsonArray(rows)};
    }
    static void Approved(JsonObject summary,ClaimsPrincipal actor){summary["review"]="approved";summary["reviewedBy"]=actor.Identity!.Name;summary["reviewedAt"]=DateTimeOffset.UtcNow.ToString("O");}
    public static JsonObject ReviewRider(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,string iqama,JsonObject input)
    {
        var cycle=Reviewable(c,tx,actor,id,"pay.manage");Open(c,tx,cycle);
        var summary=Rows(cycle["riders"]).OfType<JsonObject>().FirstOrDefault(r=>r.S("rider")==iqama)??throw new ApiError("المندوب ليس في هذه الدورة.",404);
        if(Store.Get(c,iqama,"rider",tx) is {} person)FinancialScopes.DemandRider(actor,person);
        string action=input.Required("action");
        if(action=="approve")
        {
            J.Need(summary.S("status")=="ready","الصف يحتاج قرارًا (صافي سالب أو بند غير جاهز)؛ عدّل البنود أولًا.",409);
            Approved(summary,actor);Store.Save(c,Kind,cycle,tx);Store.Audit(c,tx,"pay.review_approved",id+":"+iqama,actor.Identity!.Name!);
            return ReviewRow(c,tx,cycle,summary);
        }
        string reason=input.Required("reason").Trim();J.Need(reason.Length is >=3 and <=500&&!reason.Any(char.IsControl),"اكتب سببًا واضحًا للتعديل.");
        decimal amount=Debts.Amount(input,"amount");var deductions=Rows(cycle["deductions"]);JsonObject edit;
        if(action=="edit")
        {
            string key=input.Required("key");
            var d=deductions.OfType<JsonObject>().FirstOrDefault(x=>x.S("rider")==iqama&&DeductionKey(x)==key)??throw new ApiError("البند غير موجود أو غير قابل للتعديل.",404);
            J.Need(d.S("type")=="debt","التعديل متاح لأقساط المديونيات فقط؛ أضف تعديل راتب لغير ذلك.",409);
            decimal max=-d.N("computed",d.N("amount"));J.Need(amount>=0&&amount<=max,$"القسط بين 0 و {max:0.00}.");
            edit=new JsonObject{["key"]=key,["from"]=-d.N("amount"),["to"]=amount};d["amount"]=-amount;
        }
        else if(action=="adjust")
        {
            J.Need(amount!=0&&Math.Abs(amount)<=100000,"مبلغ التعديل غير صالح.");
            // BR §29: an increase is Dr 512122 / Cr 212103; a correction of an overpayment is Dr 212103 / Cr 512101.
            string adjustId="review-adj:"+Guid.NewGuid().ToString("N");
            deductions.Add(new JsonObject{["id"]=adjustId,["rider"]=iqama,["category"]="SalaryAdjustment",["amount"]=amount,["source"]=adjustId,["accountCode"]=amount>0?"512122":"512101",["type"]="review_adjustment",
                ["phase"]=3,["order"]=95,["labelAr"]=amount>0?"تعديل راتب (زيادة) — "+reason:"تعديل راتب (تصحيح) — "+reason,["by"]=actor.Identity!.Name});
            edit=new JsonObject{["key"]="adjust:"+adjustId,["from"]=0m,["to"]=amount};
        }
        else throw new ApiError("الإجراء approve أو edit أو adjust.");
        edit["reason"]=reason;edit["by"]=actor.Identity!.Name;edit["at"]=DateTimeOffset.UtcNow.ToString("O");
        var history=new Dictionary<string,(JsonNode? Edits,JsonNode? Review,JsonNode? By,JsonNode? At)>();
        foreach(var r in Rows(cycle["riders"]).OfType<JsonObject>())history[r.S("rider")]=(r["edits"]?.DeepClone(),r["review"]?.DeepClone(),r["reviewedBy"]?.DeepClone(),r["reviewedAt"]?.DeepClone());
        Merge(c,tx,cycle);
        foreach(var r in Rows(cycle["riders"]).OfType<JsonObject>())if(history.TryGetValue(r.S("rider"),out var h)){r["edits"]=h.Edits;r["review"]=h.Review;r["reviewedBy"]=h.By;r["reviewedAt"]=h.At;}
        var updated=Rows(cycle["riders"]).OfType<JsonObject>().Single(r=>r.S("rider")==iqama);
        if(updated["edits"] is not JsonArray edits){edits=[];updated["edits"]=edits;}edits.Add(edit);
        // An edit approves the row when it leaves the row ready; otherwise it stays for a decision.
        if(updated.S("status")=="ready")Approved(updated,actor);else{updated["review"]="pending";updated.Remove("reviewedBy");updated.Remove("reviewedAt");}
        if(cycle.S("status")=="needs_decision"&&Rows(cycle["riders"]).OfType<JsonObject>().All(r=>r.S("status")=="ready"))cycle["status"]="review";
        Store.Save(c,Kind,cycle,tx);Store.Audit(c,tx,"pay.review_"+action,id+":"+iqama,actor.Identity!.Name!);
        return ReviewRow(c,tx,cycle,updated);
    }
    public static JsonObject ReviewAll(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        var cycle=Reviewable(c,tx,actor,id,"pay.manage");Open(c,tx,cycle);int approved=0,skipped=0;
        foreach(var r in Rows(cycle["riders"]).OfType<JsonObject>().Where(r=>r.S("review")!="approved"))
        {
            if(r.S("status")!="ready"||Store.Get(c,r.S("rider"),"rider",tx) is {} p&&!FinancialScopes.Rider(actor,p)){skipped++;continue;}
            Approved(r,actor);approved++;
        }
        Store.Save(c,Kind,cycle,tx);Store.Audit(c,tx,"pay.review_approved_all",id,actor.Identity!.Name!);
        return new(){["approved"]=approved,["skipped"]=skipped};
    }
    static void MapReview(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/pay/cycles/{id}/review",(string id,string? review,string? status,HttpContext ctx)=>store.Read(c=>ReviewView(c,ctx.User,id,review??"",status??""))).WithTags("Payroll").WithSummary("Read the one-grid payroll review with items by deduction phase");
        api.MapPost("/pay/cycles/{id}/review/approve-all",(string id,HttpContext ctx)=>store.Write((c,tx)=>ReviewAll(c,tx,ctx.User,id))).WithTags("Payroll").WithSummary("Approve every ready row of the review grid");
        api.MapPost("/pay/cycles/{id}/review/{iqama}",(string id,string iqama,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>ReviewRider(c,tx,ctx.User,id,iqama,input))).WithTags("Payroll").WithSummary("Approve, edit an installment or add a salary adjustment for one rider row");
    }
}
