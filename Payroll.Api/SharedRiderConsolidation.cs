using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>§40.5 / §34: riders who worked more than one app in the month are held until all app nets arrive (or an approver releases).</summary>
public static class SharedRiderConsolidation
{
    public const string Kind="pay_consolidation",Tag="shared_rider",QueueLabelAr="مندوب عمل مشترك";

    public static string Id(string rider,string period)=>"consol:"+rider+":"+period;

    /// <summary>Apps the rider worked this month outside the current cycle (other companies/apps still missing).</summary>
    public static List<string> AppsInMonth(DbConnection c,DbTransaction? tx,JsonObject cycle,string rider,string period)
    {
        var apps=new HashSet<string>(StringComparer.Ordinal);
        string cycleId=cycle.S("id"),company=cycle.S("companyId");
        // Other open cycles for the same rider+month (typically another company).
        foreach(var other in Store.List(c,PayCycles.Kind,tx).Where(p=>p.S("id")!=cycleId&&p.S("period")==period&&p.S("status") is not ("cancelled" or "reversed")))
            foreach(var line in PayCycles.Lines(c,tx,other.S("id")).Where(l=>l.S("rider")==rider))
                if(line.S("app")!="")apps.Add(line.S("app"));
        // Captured assignments on accounts whose company is not this cycle's company.
        foreach(var acc in PayCycles.CycleOps(c,tx,cycle,"platform_account").Where(a=>a.S("companyId")!=""&&a.S("companyId")!=company))
        {
            string app=PayPlatforms.For(acc.S("platformCode")).App;if(app=="")continue;
            bool worked=(acc["assignments"] as JsonArray??[]).OfType<JsonObject>().Any(a=>a.S("riderId")==rider&&PayReviewCategory.Overlaps(a.S("RawEffectiveFrom",a.S("EffectiveFrom",a.S("effectiveFrom"))),a.S("RawEffectiveTo",a.S("EffectiveTo",a.S("effectiveTo"))),period));
            if(worked)apps.Add(app);
        }
        return apps.OrderBy(a=>a,StringComparer.Ordinal).ToList();
    }

    public static JsonObject Ensure(DbConnection c,DbTransaction tx,string rider,string period,string companyId)
    {
        string id=Id(rider,period);
        var doc=Store.Get(c,id,Kind,tx)?.Clone()??new JsonObject{["id"]=id,["rider"]=rider,["period"]=period,["companyId"]=companyId,["status"]="open",["apps"]=new JsonObject(),["missingApps"]=new JsonArray(),["released"]=false,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        return doc;
    }

    public static void RecordAppNet(DbConnection c,DbTransaction tx,JsonObject cycle,string rider,decimal net,IReadOnlyList<string> cycleApps,IReadOnlyList<string> monthApps)
    {
        string period=cycle.S("period");
        var doc=Ensure(c,tx,rider,period,cycle.S("companyId"));
        var apps=doc["apps"] as JsonObject??new JsonObject();
        string key=string.Join("+",cycleApps.OrderBy(a=>a,StringComparer.Ordinal));
        if(key=="")key=cycle.S("id");
        apps[key]=new JsonObject{["cycleId"]=cycle.S("id"),["net"]=J.Money(net),["companyId"]=cycle.S("companyId"),["apps"]=new JsonArray(cycleApps.Select(a=>(JsonNode?)JsonValue.Create(a)).ToArray()),["recordedAt"]=DateTimeOffset.UtcNow.ToString("O")};
        doc["apps"]=apps;
        var recorded=apps.SelectMany(p=>(p.Value as JsonObject)?["apps"] as JsonArray??[]).OfType<JsonValue>().Select(v=>v.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var missing=monthApps.Where(a=>!recorded.Contains(a)).ToList();
        doc["missingApps"]=new JsonArray(missing.Select(a=>(JsonNode?)JsonValue.Create(a)).ToArray());
        if(missing.Count==0&&!doc.B("released"))doc["status"]="ready";
        Store.Save(c,Kind,doc,tx);
    }

    public static decimal CombinedNet(JsonObject doc)=> (doc["apps"] as JsonObject??new JsonObject()).Select(p=>p.Value as JsonObject).Where(a=>a!=null).Sum(a=>a!.N("net"));

    public static JsonObject Release(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string rider,string period,string reason)
    {
        J.Need(reason.Trim().Length is >2 and <=500,"سبب الإفراج مطلوب.");
        var doc=Store.Get(c,Id(rider,period),Kind,tx)?.Clone()??throw new ApiError("سجل التجميع غير موجود.",404);
        AccessPolicy.Demand(actor,ApprovalsArea.DualSharedRiderRelease);
        var approval=new JsonObject
        {
            ["id"]=doc.S("id"),["kind"]="shared_rider_release",["requestedBy"]=doc.S("createdBy",doc.S("heldBy")),
            ["targetDepartment"]="FIN",["requiredRankLevel"]=1,["dualPermission"]=ApprovalsArea.DualSharedRiderRelease,
            ["amountSar"]=CombinedNet(doc)
        };
        AccessPolicy.ValidateApproval(actor,approval,new(),"approvals.decide",c,tx,"pay.consolidation_released",doc,ApprovalsArea.DualSharedRiderRelease);
        doc["released"]=true;doc["status"]="released";doc["releaseReason"]=reason.Trim();doc["releasedBy"]=actor.Identity!.Name;doc["releasedAt"]=DateTimeOffset.UtcNow.ToString("O");
        doc["combinedNet"]=J.Money(CombinedNet(doc));
        if(doc.B("selfApprovedException")||approval.B("selfApprovedException"))doc["selfApprovedException"]=true;
        Store.Save(c,Kind,doc,tx);Store.Audit(c,tx,"pay.consolidation_released",doc.S("id"),actor.Identity!.Name!);
        return doc;
    }

    public static bool Held(JsonObject riderRow)=>riderRow.S("tag")==Tag||riderRow.B("sharedRider");

    public static JsonObject QueueItem(JsonObject doc)=>new()
    {
        ["rider"]=doc.S("rider"),["period"]=doc.S("period"),["status"]=doc.S("status"),
        ["labelAr"]=QueueLabelAr,["missingApps"]=doc["missingApps"]?.DeepClone()??new JsonArray(),
        ["apps"]=doc["apps"]?.DeepClone()??new JsonObject(),["combinedNet"]=CombinedNet(doc),["released"]=doc.B("released")
    };

    static JsonArray Rows(JsonNode? n)=>n as JsonArray??[];
}
