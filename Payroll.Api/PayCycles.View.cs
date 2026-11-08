using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PayCycles
{
    public static JsonObject View(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject cycle)
    {
        Scope(actor,cycle,"pay.view");var result=cycle.Clone();var actions=new JsonArray();var blockers=new JsonArray();string state=cycle.S("status"),name=actor.Identity!.Name!;int stage=(int)cycle.N("stage");
        bool manage=AccessPolicy.Has(actor,"pay.manage"),approve=AccessPolicy.Has(actor,"pay.approve"),mutable=state is "draft" or "review" or "needs_decision";
        if(mutable&&manage){actions.Add("cancel");if(stage<6)actions.Add("advance");if(stage==6)actions.Add("review");}
        var batch=Rows(cycle["riders"]).OfType<JsonObject>().Where(r=>r.S("salaryApprovedAt")==""&&r.S("review")=="approved"&&Blockers(r).Count==0).ToList();
        if(stage>=6&&(mutable||state=="partially_approved"))
        {
            if(state=="partially_approved"&&manage)actions.Add("review");
            if(batch.Count==0)blockers.Add("لا توجد صفوف مراجعة جاهزة للاعتماد.");
            if(!approve)blockers.Add("صلاحية pay.approve مطلوبة.");
            bool independent=AccessPolicy.Independent(actor,cycle.S("createdBy"),"pay.salary_approved")&&Rows(cycle["history"]).OfType<JsonObject>().Where(h=>h.N("stage")<7).All(h=>AccessPolicy.Independent(actor,h.S("advancedBy"),"pay.salary_preparation"))&&batch.Any(r=>IndependentReview(actor,r));
            if(!independent)blockers.Add("اعتماد الراتب يحتاج مستخدمًا مستقلًا عن التجهيز والمراجعة.");
            foreach(string app in Accounts(cycle).Select(a=>a.App).Distinct())try{PlatformPosting.Demand(c,app,tx);}catch(ApiError){blockers.Add("بوابة الترحيل مغلقة: "+app);}
            if(approve&&independent&&batch.Count>0&&blockers.Count==0)actions.Add("approve_salaries");
        }
        if(state is "approved" or "partially_approved")
        {
            if(manage)actions.Add("review_debt_deductions");
            if(approve&&Rows(cycle["riders"]).OfType<JsonObject>().Any(r=>r.S("salaryApprovedAt")!=""&&r.S("debtDeductionStatus")!="confirmed"&&AccessPolicy.Independent(actor,r.S("debtSelectedBy",cycle.S("createdBy")),"pay.debt_confirm")))actions.Add("confirm_debt_deductions");
            if(manage)actions.Add("reversal_request");
        }
        result["allowedActions"]=actions;result["approvalBlockers"]=blockers;return result;
    }
    public static JsonObject UnrecoveredCosts(DbConnection c,ClaimsPrincipal actor,string id)
    {
        var cycle=Store.Get(c,id,Kind)??throw new ApiError("الدورة غير موجودة.",404);Scope(actor,cycle,"pay.view");var rows=new JsonArray();
        foreach(var group in Lines(c,null,id).Where(l=>l.B("ineligibleKeetaSalary")).GroupBy(l=>l.S("rider")))
        {
            if(Store.Get(c,group.Key,"rider") is {} rider)FinancialScopes.DemandRider(actor,rider);
            decimal operating=group.Sum(l=>l.N("unrecoveredOperatingCost")+l.N("unrecoveredVehicleCharge")),fuel=Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("rider")==group.Key&&d.S("type")=="fuel_recovery").Sum(d=>d.N("companyCost"));
            rows.Add(new JsonObject{["rider"]=group.Key,["operatingCost"]=operating,["fuelCost"]=fuel,["total"]=operating+fuel,["accountCode"]="512121",["sourceLineIds"]=new JsonArray(group.Select(l=>(JsonNode?)JsonValue.Create(l.S("id"))).ToArray())});
        }
        return new(){["cycleId"]=id,["status"]=cycle.S("status"),["rows"]=rows,["total"]=rows.OfType<JsonObject>().Sum(r=>r.N("total"))};
    }
}
