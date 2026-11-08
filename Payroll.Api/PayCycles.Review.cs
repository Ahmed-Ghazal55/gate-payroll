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
        if(permission!="pay.view")J.Need(((int)cycle.N("stage")==ReviewStage&&cycle.S("status") is "review" or "needs_decision"||(int)cycle.N("stage")==7&&cycle.S("status")=="partially_approved"),"المراجعة متاحة بعد الدمج (المرحلة 6) وقبل الاعتماد النهائي.",409);
        return cycle;
    }
    static JsonObject ReviewRow(DbConnection c,DbTransaction? tx,JsonObject cycle,JsonObject summary,ClaimsPrincipal actor)
    {
        string rider=summary.S("rider");var lines=Lines(c,tx,cycle.S("id")).Where(l=>l.S("rider")==rider).ToList();
        var items=new JsonArray();
        foreach(var l in lines)
        {
            if(l.N("accountDeduction")!=0)items.Add(Item("platform:"+l.S("id"),1,5,"خصم منصة على اليوزر ("+l.S("app")+")",-l.N("accountDeduction"),true,null,l.S("app")));
            if(l.N("allocatedVehicleCharge")!=0)items.Add(Item("vehicle:"+l.S("id"),1,20,"إيجار / بدل المركبة",-l.N("allocatedVehicleCharge"),true,null,l.S("vehicleType")));
        }
        foreach(var d in Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("rider")==rider))
        {
            bool debt=d.S("type")=="debt";
            items.Add(Item(DeductionKey(d),(int)d.N("phase",3),(int)d.N("order",99),d.S("labelAr",d.S("category")),d.N("amount"),debt||d.S("type") is "review_adjustment" or "adjustment",debt?-d.N("computed",d.N("amount")):null,d.S("source")));
        }
        var ordered=new JsonArray(items.OfType<JsonObject>().OrderBy(i=>i.N("phase")).ThenBy(i=>i.N("order")).Select(i=>(JsonNode)i.DeepClone()).ToArray());
        var person=Store.Get(c,rider,"rider",tx);
        var row=new JsonObject{["rider"]=rider,["name"]=person?.S("name")??"",["status"]=summary.S("status"),["review"]=summary.S("review","pending"),["reviewedBy"]=summary["reviewedBy"]?.DeepClone(),["reviewedAt"]=summary["reviewedAt"]?.DeepClone()};
        foreach(string k in new[]{"gross","commission","afterCommission","phase1","preliminaryDue","phase2","phase3","net"})row[k]=summary[k]?.DeepClone();
        row["blockers"]=Blockers(summary).DeepClone();row["salaryApprovedAt"]=summary.S("salaryApprovedAt");row["debtDeductionStatus"]=summary.S("debtDeductionStatus");
        var actions=new JsonArray();if(AccessPolicy.Has(actor,"pay.manage")&&summary.S("salaryApprovedAt")==""&&cycle.N("stage")>=6&&cycle.S("status") is "review" or "needs_decision" or "partially_approved")
        {actions.Add("adjust");actions.Add("edit");if(Blockers(summary).Count==0)actions.Add("approve");}
        row["allowedActions"]=actions;
        row["items"]=ordered;row["edits"]=(summary["edits"] as JsonArray??[]).DeepClone();return row;
    }
    static string DeductionKey(JsonObject d)=>d.S("type")=="review_adjustment"?"adjust:"+d.S("id"):d.S("type")+":"+d.S("source");
    static JsonObject Item(string key,int phase,int order,string label,decimal amount,bool editable,decimal? max,string source)=>
        new(){["key"]=key,["phase"]=phase,["order"]=order,["labelAr"]=label,["amount"]=J.Money(amount),["editable"]=editable,["max"]=max==null?null:J.Money(max.Value),["source"]=source};
    public static JsonObject ReviewView(DbConnection c,ClaimsPrincipal actor,string id,string review,string status)
    {
        var cycle=Reviewable(c,null,actor,id,"pay.view");var riders=Rows(cycle["riders"]).OfType<JsonObject>().ToList();
        J.Need(review is "" or "pending" or "approved"&&status is "" or "ready" or "needs_decision","مرشح غير صالح.");
        var rows=riders.Where(r=>(review==""||r.S("review","pending")==review)&&(status==""||r.S("status")==status)).Select(r=>(JsonNode)ReviewRow(c,null,cycle,r,actor)).ToArray();
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
        J.Need(summary.S("salaryApprovedAt")=="","الراتب معتمد؛ التصحيح بالعكس فقط.",409);
        string action=input.Required("action");
        if(action=="approve")
        {
            J.Need(Blockers(summary).Count==0,"الصف به مانع غير مالي يحتاج حسم المصدر.",409);
            if(summary.N("net")<0){summary["decisionReason"]=Reason(input);summary["resolution"]="carry_at_final_approval";}
            Approved(summary,actor);Store.Save(c,Kind,cycle,tx);Store.Audit(c,tx,"pay.review_approved",id+":"+iqama,actor.Identity!.Name!);
            return ReviewRow(c,tx,cycle,summary,actor);
        }
        string reason=Reason(input);
        decimal amount=SignedMoney(input,"amount");var deductions=Rows(cycle["deductions"]);JsonObject edit;
        if(action=="edit")
        {
            string key=input.Required("key");
            var d=deductions.OfType<JsonObject>().FirstOrDefault(x=>x.S("rider")==iqama&&DeductionKey(x)==key);
            if(d?.S("type")=="debt")
            {
                decimal max=-d.N("computed");J.Need(amount>=0&&amount<=max,"القسط من صفر إلى المستحق.");
                edit=new(){["key"]=key,["from"]=d.N("requestedAmount"),["to"]=amount};d["requestedAmount"]=amount;summary["debtSelectedBy"]=actor.Identity!.Name;
            }
            else if(d?.S("type") is "review_adjustment" or "adjustment")
            {
                var configured=AdjustmentType(c,tx,input,amount);edit=new(){["key"]=key,["from"]=d.N("amount"),["to"]=amount};
                d["amount"]=amount;d["accountCode"]=configured.Account;d["category"]=input.S("itemType");d["labelAr"]=configured.Label+" — "+reason;
            }
            else
            {
                // Preserve invoiced and vehicle facts. An override is an explicit compensating item.
                var item=ReviewRow(c,tx,cycle,summary,actor)["items"]!.AsArray().OfType<JsonObject>().FirstOrDefault(i=>i.S("key")==key&&i.B("editable"))??throw new ApiError("البند غير موجود أو غير قابل للتعديل.",404);
                J.Need(amount>=0,"قيمة الاستقطاع غير سالبة.");decimal previous=deductions.OfType<JsonObject>().Where(x=>x.S("rider")==iqama&&x.S("replacesKey")==key).Sum(x=>x.N("amount"));decimal delta=-item.N("amount")-previous-amount;J.Need(delta!=0,"لا يوجد تغيير.");
                var configured=AdjustmentType(c,tx,input,delta);var adjustment=AddAdjustment(deductions,actor,iqama,input.S("itemType"),configured.Account,configured.Label,delta,reason);adjustment["replacesKey"]=key;
                edit=new(){["key"]=key,["from"]=-item.N("amount"),["to"]=amount};
            }
        }
        else if(action=="adjust")
        {
            J.Need(amount!=0&&Math.Abs(amount)<=100000,"مبلغ التعديل غير صالح.");var configured=AdjustmentType(c,tx,input,amount);
            var adjustment=AddAdjustment(deductions,actor,iqama,input.S("itemType"),configured.Account,configured.Label,amount,reason);
            edit=new(){["key"]=DeductionKey(adjustment),["from"]=0m,["to"]=amount};
        }
        else throw new ApiError("الإجراء approve أو edit أو adjust.");
        edit["reason"]=reason;edit["by"]=actor.Identity!.Name;edit["at"]=DateTimeOffset.UtcNow.ToString("O");
        var debtSelectors=Rows(cycle["riders"]).OfType<JsonObject>().ToDictionary(r=>r.S("rider"),r=>r.S("debtSelectedBy"));
        var history=new Dictionary<string,(JsonNode? Edits,JsonNode? Review,JsonNode? By,JsonNode? At)>();
        foreach(var r in Rows(cycle["riders"]).OfType<JsonObject>())history[r.S("rider")]=(r["edits"]?.DeepClone(),r["review"]?.DeepClone(),r["reviewedBy"]?.DeepClone(),r["reviewedAt"]?.DeepClone());
        string oldStatus=cycle.S("status");Merge(c,tx,cycle);if(cycle.N("stage")>=7)cycle["status"]=oldStatus;
        foreach(var r in Rows(cycle["riders"]).OfType<JsonObject>())if(history.TryGetValue(r.S("rider"),out var h)){r["edits"]=h.Edits;r["review"]=h.Review;r["reviewedBy"]=h.By;r["reviewedAt"]=h.At;}
        foreach(var r in Rows(cycle["riders"]).OfType<JsonObject>())if(debtSelectors.TryGetValue(r.S("rider"),out var selector)&&selector!="")r["debtSelectedBy"]=selector;
        var updated=Rows(cycle["riders"]).OfType<JsonObject>().Single(r=>r.S("rider")==iqama);
        if(updated["edits"] is not JsonArray edits){edits=[];updated["edits"]=edits;}edits.Add(edit);
        // An edit approves the row when it leaves the row ready; otherwise it stays for a decision.
        if(updated.S("status")=="ready")Approved(updated,actor);else{updated["review"]="pending";updated.Remove("reviewedBy");updated.Remove("reviewedAt");}
        if(cycle.S("status")=="needs_decision"&&Rows(cycle["riders"]).OfType<JsonObject>().All(r=>r.S("status")=="ready"))cycle["status"]="review";
        Store.Save(c,Kind,cycle,tx);Store.Audit(c,tx,"pay.review_"+action,id+":"+iqama,actor.Identity!.Name!);
        return ReviewRow(c,tx,cycle,updated,actor);
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
    static string Reason(JsonObject input)
    {string reason=input.Required("reason").Trim();J.Need(reason.Length is >=3 and <=500&&!reason.Any(char.IsControl),"اكتب سببًا واضحًا للتعديل.");return reason;}
    static (string Account,string Label) AdjustmentType(DbConnection c,DbTransaction tx,JsonObject input,decimal amount)
    {
        var type=ConfigLists.Current(c,tx,"pay_adjustment_types",input.Required("itemType"))??throw new ApiError("بند التعديل غير نشط أو غير معتمد.");
        string direction=type["values"].S("direction"),account=PayPlatforms.Active(type["values"].S("account"));
        J.Need(direction=="both"||amount>0&&direction=="increase"||amount<=0&&direction=="decrease","اتجاه المبلغ لا يطابق بند التعديل.");
        if(input.ContainsKey("accountCode"))J.Need(input.S("accountCode")==account,"حساب التعديل لا يطابق القائمة المعتمدة.");
        return(account,type.S("nameAr"));
    }
    static JsonObject AddAdjustment(JsonArray deductions,ClaimsPrincipal actor,string rider,string type,string account,string label,decimal amount,string reason)
    {
        string id="review-adj:"+Guid.NewGuid().ToString("N");var doc=new JsonObject{["id"]=id,["rider"]=rider,["category"]=type,["amount"]=amount,["source"]=id,["accountCode"]=account,["type"]="review_adjustment",["phase"]=3,["order"]=95,["labelAr"]=label+" — "+reason,["by"]=actor.Identity!.Name};deductions.Add(doc);return doc;
    }
    static void MapReview(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/pay/cycles/{id}/review",(string id,string? review,string? status,HttpContext ctx)=>store.Read(c=>ReviewView(c,ctx.User,id,review??"",status??""))).WithTags("Payroll").WithSummary("Read the one-grid payroll review with items by deduction phase");
        api.MapPost("/pay/cycles/{id}/review/approve-all",(string id,HttpContext ctx)=>store.Write((c,tx)=>ReviewAll(c,tx,ctx.User,id))).WithTags("Payroll").WithSummary("Approve every ready row of the review grid");
        api.MapPost("/pay/cycles/{id}/review/{iqama}",(string id,string iqama,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>ReviewRider(c,tx,ctx.User,id,iqama,input))).WithTags("Payroll").WithSummary("Approve, edit an installment or add a salary adjustment for one rider row");
    }
}
