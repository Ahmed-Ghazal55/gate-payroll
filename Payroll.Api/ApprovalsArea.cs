using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-27: approvals inbox shaping, dual-control (M-0672), SLA escalate-without-auto-approve,
/// and CRUD for approval limits / SLA policies. SQL tables for auth.approval_limit / org.sla_policy /
/// approvals.* await U-W1-18c — dual-write uses document kinds until that DDL lands.</summary>
public static class ApprovalsArea
{
    public const string LimitsKind="approval_limit",SlaKind="sla_policy",DecisionKind="approval_decision";
    public const string OrgPermission="admin.org.manage";
    public const string DualBankFile="approvals.dual.bank_file",DualPaymentOut="approvals.dual.payment_out",DualSharedRiderRelease="approvals.dual.shared_rider_release";
    public static readonly string[] DualPermissions=[DualBankFile,DualPaymentOut,DualSharedRiderRelease];
    static readonly (string Rank,string Department,decimal? Limit)[] DefaultLimits=
    [
        ("C","OPS",0m),("B","OPS",2000m),("A","OPS",20000m),("A+","OPS",100000m),("GM","OPS",null),
        ("C","FIN",0m),("B","FIN",2000m),("A","FIN",20000m),("A+","FIN",100000m),("GM","FIN",null)
    ];

    public static bool RequiresDual(string? dualPermission)=>
        !string.IsNullOrWhiteSpace(dualPermission)&&DualPermissions.Contains(dualPermission,StringComparer.Ordinal);
    /// <summary>M-0672: dual permission is derived on the server from the request kind — never an opt-in client field.</summary>
    public static string? DualForKind(string? kind)=>kind switch
    {
        "bank_file" or "bank_export" or "bank_payroll_payout" or "bank_sent"=>DualBankFile,
        "payment_out" or "payment" or "advance_payout" or "direct_rider_payout"=>DualPaymentOut,
        "shared_rider_release" or "pay_consolidation_release"=>DualSharedRiderRelease,
        _=>null
    };
    public static string? ResolveDual(JsonObject request)
    {
        string? server=DualForKind(request.S("kind",request.S("module")));
        if(server!=null)return server;
        string client=request.S("dualPermission");
        return RequiresDual(client)?client:null;
    }

    /// <summary>M-0672 / WC1H: money-out (bank mark-sent / payout) and shared-rider release need a
    /// persisted approvals-area decision by two different people. Bypass never satisfies this gate.</summary>
    public static void DemandRecordedMoneyOutDual(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string dualPermission,string referenceId)
    {
        AccessPolicy.Demand(actor,dualPermission);
        J.Need(RequiresDual(dualPermission)&&dualPermission is DualBankFile or DualPaymentOut or DualSharedRiderRelease,
            "صرف الأموال أو فك الحجز المشترك يتطلب صلاحية الاعتماد الثنائي المخصصة.",403);
        bool ok=Store.List(c,"approval_request",tx).Any(r=>
        {
            if(r.S("status")!="approved"||r.S("referenceId")!=referenceId||!string.Equals(ResolveDual(r),dualPermission,StringComparison.Ordinal)||r.B("selfApprovedException"))return false;
            string maker=r.S("requestedBy",r.S("createdBy")),checker=r.S("decidedBy");
            if(string.IsNullOrWhiteSpace(maker)||string.IsNullOrWhiteSpace(checker))return false;
            // Two distinct people — use SamePerson so former usernames count; bypass/self-exception already excluded above.
            var checkerPrincipal=new ClaimsPrincipal(new ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name,checker)],"money-out-dual"));
            if(AccessPolicy.SamePerson(checkerPrincipal,maker))return false;
            return Store.List(c,DecisionKind,tx).Any(d=>
                d.S("requestId")==r.S("id")
                &&d.S("outcome")=="approved"
                &&string.IsNullOrWhiteSpace(d.S("bypassGrantId")));
        });
        J.Need(ok,"صرف الأموال يتطلب اعتماد شخصين مختلفين مسجل في منطقة الاعتمادات دون تجاوز.",403);
    }
    static void DemandOrg(ClaimsPrincipal actor)
    {
        if(!AccessPolicy.Has(actor,OrgPermission)&&!AccessPolicy.Has(actor,"org.manage"))
            AccessPolicy.Demand(actor,OrgPermission);
    }

    public static JsonObject ListApprovals(DbConnection c,ClaimsPrincipal actor,string? module,bool mineOnly,string? status)
    {
        AccessPolicy.Demand(actor,"approvals.view");
        EnsureDefaultSla(c,null);
        string me=actor.Identity?.Name??"";
        var list=Store.List(c,"approval_request").Where(a=>FinancialScopes.Approval(actor,a,c))
            .Select(a=>WithActions(c,null,actor,MarkEscalatedView(c,a))).ToList();
        if(!string.IsNullOrWhiteSpace(module))list=list.Where(a=>a.S("kind")==module||a.S("module")==module||a.S("targetDepartment")==module||a.S("fromDepartment")==module).ToList();
        if(mineOnly)list=list.Where(a=>string.Equals(a.S("requestedBy"),me,StringComparison.OrdinalIgnoreCase)||string.Equals(a.S("targetDepartment"),actor.FindFirst("department")?.Value,StringComparison.OrdinalIgnoreCase)).ToList();
        if(!string.IsNullOrWhiteSpace(status))list=list.Where(a=>a.S("status")==status).ToList();
        return new(){["items"]=new JsonArray(list.Select(a=>(JsonNode?)a).ToArray()),["allowedActions"]=new JsonArray()};
    }

    public static JsonObject Decide(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        EscalateOverdue(c,tx);
        var req=Store.Get(c,id,"approval_request",tx)??throw new ApiError("طلب الاعتماد غير موجود.",404);
        if(!FinancialScopes.Approval(actor,req,c,tx))throw new ApiError("الطلب خارج نطاق القسم/السجل المسند لك.",403);
        string decision=input.S("decision",input.S("outcome"));
        // Screen contract uses approve/reject; legacy FinancialCore used approved/rejected/suspended.
        decision=decision switch{"approve"=>"approved","reject"=>"rejected",_=>decision};
        J.Need(decision is "approved" or "rejected" or "suspended","قرار غير صالح.",400);
        J.Need(req.S("status") is "pending" or "escalated" or "suspended","تم البت في هذا الطلب مسبقاً.",409);
        string note=input.S("note",input.S("reason"));
        if(decision is "rejected" or "suspended")J.Need(note.Trim().Length>=3,"سبب الرفض أو التعليق مطلوب.",400);

        string? dual=ResolveDual(req);
        if(dual!=null)req["dualPermission"]=dual;
        AccessPolicy.ValidateApproval(actor,req,input,"approvals.decide",c,tx,"approval.decide",req,dual);
        AccessPolicy.DemandApprovalLimit(actor,req,c,tx);
        // SLA escalation never auto-approves — an escalated row still needs an explicit human decision.
        J.Need(decision!="approved"||req.S("status")!="auto_approved","التصعيد لا يعتمد تلقائياً.",409);

        bool self=AccessPolicy.SamePerson(actor,req.S("requestedBy",req.S("createdBy")));
        if(self){req["selfApprovedException"]=true;req["administrativeSelfOverride"]=true;}
        req["status"]=decision;
        req["decisionNote"]=note;
        req["decidedBy"]=actor.Identity!.Name!;
        req["decidedAt"]=DateTimeOffset.UtcNow.ToString("O");
        req["administrativeSelfOverride"]=req.B("selfApprovedException");
        Store.Save(c,"approval_request",req,tx);
        var grant=dual!=null?AccessPolicy.ActiveBypassGrant(c,tx,actor.Identity!.Name!,dual):AccessPolicy.ActiveBypassGrant(c,tx,actor.Identity!.Name!);
        var decisionRow=new JsonObject
        {
            ["id"]=Guid.NewGuid().ToString("N"),["requestId"]=id,["outcome"]=decision,
            ["decidedBy"]=actor.Identity!.Name!,["decidedAt"]=req.S("decidedAt"),
            ["note"]=note,["dualPermission"]=dual??"",
            ["membershipSnapshot"]=new JsonObject
            {
                ["department"]=actor.FindFirst("department")?.Value??string.Empty,
                ["rankLevel"]=actor.FindFirst("rankLevel")?.Value??string.Empty
            },
            ["bypassGrantId"]=grant?.S("id")??string.Empty
        };
        SchemaApprovals.SaveDecision(c,tx,decisionRow);
        if(self)Store.Audit(c,tx,"approval.self_decision",id,actor.Identity!.Name!);

        // Legacy debt_creation mutual approval side effects (kept from FinancialCore).
        if(req.S("kind")=="debt_creation"&&decision=="approved")
        {
            var debt=Store.Get(c,req.S("referenceId"),"debt",tx);
            if(debt!=null&&debt.S("status")=="pending_approval")
            {
                debt["status"]=debt.N("remainingBalance")>0?"active":"settled";
                debt["approvedBy"]=actor.Identity!.Name!;
                debt["approvedAt"]=req.S("decidedAt");
                Store.Save(c,"debt",debt,tx);
                MoneyWriteGuards.Demand(
                    (byte)(debt.N("runMode")==1?1:0),
                    debt.B("payoutBlocked"),
                    MoneyEffectKind.Debt);
                Debts.PostCreationJournalIfNeeded(c,tx,debt,actor.Identity!.Name!);
            }
        }
        else if(req.S("kind")=="debt_creation"&&decision=="rejected")
        {
            var debt=Store.Get(c,req.S("referenceId"),"debt",tx);
            if(debt!=null&&debt.S("status")=="pending_approval")
            {
                debt["status"]="cancelled";
                Store.Save(c,"debt",debt,tx);
            }
        }
        foreach(var n in Store.List(c,"sys_notification",tx).Where(n=>n.S("approvalId")==id))
        {
            n["status"]="acknowledged";n["acknowledgedBy"]=actor.Identity!.Name!;n["acknowledgedAt"]=req.S("decidedAt");
            Store.Save(c,"sys_notification",n,tx);
        }
        Store.Audit(c,tx,"approval."+decision,id,actor.Identity!.Name!);
        return WithActions(c,tx,actor,req);
    }

    public static JsonArray GetLimits(DbConnection c,ClaimsPrincipal actor,DbTransaction? tx=null)
    {
        DemandOrg(actor);
        EnsureDefaultLimits(c,tx);
        var rows=SchemaApprovals.ListLimits(c,tx);
        if(rows.Count==0)rows=Store.List(c,LimitsKind,tx);
        return new(rows.OrderBy(l=>l.S("rank")).ThenBy(l=>l.S("department")).Select(l=>(JsonNode?)WithLimitActions(actor,l)).ToArray());
    }

    public static JsonArray PutLimits(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        DemandOrg(actor);
        var items=input["items"] as JsonArray??throw new ApiError("قائمة الحدود مطلوبة.");
        EnsureDefaultLimits(c,tx);
        foreach(var node in items.OfType<JsonObject>())
        {
            string rank=node.Required("rank"),department=node.Required("department");
            J.Need(rank.Length is >=1 and <=8&&department.Length is >=2 and <=40,"رتبة أو قسم غير صالح.");
            decimal? limit=node.ContainsKey("limitSar")&&node["limitSar"] is not null&&node.S("limitSar")!=""?node.N("limitSar"):null;
            if(limit.HasValue)J.Need(limit.Value>=0&&limit.Value==J.Money(limit.Value),"حد الاعتماد غير صالح.");
            var row=new JsonObject
            {
                ["id"]="approval-limit:"+rank+":"+department,["rank"]=rank,["department"]=department,
                ["limitSar"]=limit.HasValue?JsonValue.Create(limit.Value):null,["unlimited"]=!limit.HasValue,
                ["updatedBy"]=actor.Identity!.Name!,["updatedAt"]=DateTimeOffset.UtcNow.ToString("O")
            };
            SchemaApprovals.UpsertLimit(c,tx,row);
            Store.Save(c,LimitsKind,row,tx);
        }
        Store.Audit(c,tx,"admin.approval_limits_updated","approval_limit",actor.Identity!.Name!);
        return GetLimits(c,actor,tx);
    }

    public static JsonArray GetSla(DbConnection c,ClaimsPrincipal actor,DbTransaction? tx=null)
    {
        DemandOrg(actor);
        EnsureDefaultSla(c,tx);
        var rows=SchemaApprovals.ListSla(c,tx);
        if(rows.Count==0)rows=Store.List(c,SlaKind,tx);
        return new(rows.OrderBy(s=>s.S("requestType")).Select(s=>(JsonNode?)WithSlaActions(actor,s)).ToArray());
    }

    public static JsonArray PutSla(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        DemandOrg(actor);
        var items=input["items"] as JsonArray??throw new ApiError("قائمة سياسات المهلة مطلوبة.");
        EnsureDefaultSla(c,tx);
        foreach(var node in items.OfType<JsonObject>())
        {
            string type=node.Required("requestType");
            int days=(int)node.N("workingDays",3);
            J.Need(type.Length is >=2 and <=80&&days is >=1 and <=30,"سياسة مهلة غير صالحة.");
            var row=new JsonObject
            {
                ["id"]="sla-policy:"+type,["requestType"]=type,["workingDays"]=days,["autoApprove"]=false,
                ["updatedBy"]=actor.Identity!.Name!,["updatedAt"]=DateTimeOffset.UtcNow.ToString("O")
            };
            SchemaApprovals.UpsertSla(c,tx,row);
            Store.Save(c,SlaKind,row,tx);
        }
        Store.Audit(c,tx,"admin.sla_policies_updated","sla_policy",actor.Identity!.Name!);
        return GetSla(c,actor,tx);
    }

    public static void EscalateOverdue(DbConnection c,DbTransaction tx)
    {
        EnsureDefaultSla(c,tx);
        foreach(var req in Store.List(c,"approval_request",tx).Where(r=>r.S("status")=="pending"&&IsOverdue(c,tx,r)))
        {
            req["status"]="escalated";
            req["escalatedAt"]=DateTimeOffset.UtcNow.ToString("O");
            req["escalationNoteAr"]="تصعيد مهلة — بدون اعتماد تلقائي.";
            Store.Save(c,"approval_request",req,tx);
            Store.Audit(c,tx,"approval.escalated",req.S("id"),"system-sla"); // no auto-approve
        }
    }

    static JsonObject MarkEscalatedView(DbConnection c,JsonObject req)
    {
        var copy=req.DeepClone().AsObject();
        if(copy.S("status")=="pending"&&IsOverdue(c,null,copy))
        {
            copy["status"]="escalated";
            copy["escalationNoteAr"]="تصعيد مهلة — بدون اعتماد تلقائي.";
        }
        return copy;
    }

    static bool IsOverdue(DbConnection c,DbTransaction? tx,JsonObject req)
    {
        if(!DateTimeOffset.TryParse(req.S("createdAt"),out var created))return false;
        var policies=SchemaApprovals.ListSla(c,tx);
        if(policies.Count==0)policies=Store.List(c,SlaKind,tx);
        var byType=policies.ToDictionary(s=>s.S("requestType"),s=>(int)s.N("workingDays",3),StringComparer.OrdinalIgnoreCase);
        int days=byType.GetValueOrDefault(req.S("kind"),byType.GetValueOrDefault("default",3));
        return WorkingDaysBetween(created,DateTimeOffset.UtcNow)>=days;
    }

    static int WorkingDaysBetween(DateTimeOffset from,DateTimeOffset to)
    {
        int days=0;
        for(var d=from.Date;d<to.Date;d=d.AddDays(1))
            if(d.DayOfWeek is not DayOfWeek.Friday and not DayOfWeek.Saturday)days++;
        return days;
    }

    public static void EnsureLimitsSeeded(DbConnection c,DbTransaction? tx)=>EnsureDefaultLimits(c,tx);
    static void EnsureDefaultLimits(DbConnection c,DbTransaction? tx)
    {
        if(SchemaApprovals.HasLimits(c,tx)||Store.List(c,LimitsKind,tx).Count>0)return;
        void Seed(DbTransaction t)
        {
            foreach(var (rank,department,limit) in DefaultLimits)
            {
                var row=new JsonObject
                {
                    ["id"]="approval-limit:"+rank+":"+department,["rank"]=rank,["department"]=department,
                    ["limitSar"]=limit.HasValue?JsonValue.Create(limit.Value):null,["unlimited"]=!limit.HasValue
                };
                SchemaApprovals.UpsertLimit(c,t,row);
                Store.Save(c,LimitsKind,row,t);
            }
        }
        if(tx!=null){Seed(tx);return;}
        using var own=c.BeginTransaction();Seed(own);own.Commit();
    }

    static void EnsureDefaultSla(DbConnection c,DbTransaction? tx)
    {
        if(SchemaApprovals.HasSla(c,tx)||Store.List(c,SlaKind,tx).Any(s=>s.S("requestType")=="default"))return;
        void Seed(DbTransaction t)
        {
            var row=new JsonObject{["id"]="sla-policy:default",["requestType"]="default",["workingDays"]=3,["autoApprove"]=false};
            SchemaApprovals.UpsertSla(c,t,row);
            Store.Save(c,SlaKind,row,t);
        }
        if(tx!=null){Seed(tx);return;}
        using var own=c.BeginTransaction();Seed(own);own.Commit();
    }

    static JsonObject WithActions(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject req)
    {
        var copy=req.DeepClone().AsObject();
        var actions=new JsonArray();var blockers=new JsonArray();
        string status=req.S("status");
        if(status is "pending" or "escalated" or "suspended")
        {
            string? dual=ResolveDual(req);
            bool can=AccessPolicy.Has(actor,RequiresDual(dual)?dual!:"approvals.decide");
            bool bypass=dual!=null?AccessPolicy.BypassForView(c,tx,actor,dual):AccessPolicy.BypassForView(c,tx,actor);
            bool independent=!RequiresDual(dual)||AccessPolicy.IndependentForView(actor,req.S("requestedBy"),bypass);
            bool withinLimit=AccessPolicy.WithinApprovalLimit(actor,req,c,tx);
            if(can&&independent&&withinLimit){actions.Add("approve");actions.Add("reject");actions.Add("suspend");}
            else if(!can)blockers.Add("صلاحية الاعتماد مطلوبة.");
            else if(!withinLimit)blockers.Add("المبلغ يتجاوز حد الاعتماد للرتبة والقسم.");
            else blockers.Add("شرط الشخصين: يلزم مراجع مستقل أو منح تجاوز مؤرخ.");
        }
        copy["allowedActions"]=actions;copy["approvalBlockers"]=blockers;
        return copy;
    }

    static JsonObject WithLimitActions(ClaimsPrincipal actor,JsonObject row)
    {
        var copy=row.DeepClone().AsObject();
        copy["allowedActions"]=AccessPolicy.Has(actor,OrgPermission)||AccessPolicy.Has(actor,"org.manage")?new JsonArray("edit_limits"):new JsonArray();
        return copy;
    }

    static JsonObject WithSlaActions(ClaimsPrincipal actor,JsonObject row)
    {
        var copy=row.DeepClone().AsObject();
        copy["allowedActions"]=AccessPolicy.Has(actor,OrgPermission)||AccessPolicy.Has(actor,"org.manage")?new JsonArray("edit_sla"):new JsonArray();
        copy["autoApprove"]=false;
        return copy;
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        // Return a plain array; ApiPagination.Filter on the API group pages only when ?page= is present (legacy bytes stay stable).
        api.MapGet("/approvals",(string? module,string? department,bool? mineOnly,string? status,HttpContext ctx)=>
            store.Read(c=>ListApprovals(c,ctx.User,module??department,mineOnly==true,status)["items"]!.AsArray().OfType<JsonObject>().ToList()))
            .WithTags("Home").WithSummary("List approval requests with allowedActions and SLA escalation state");
        api.MapPost("/approvals/{id}/decide",(string id,JsonObject input,HttpContext ctx)=>
            store.Write((c,tx)=>Decide(c,tx,ctx.User,id,input))).WithTags("Home").WithSummary("Approve or reject an approval request (SLA never auto-approves)");
        api.MapGet("/admin/org/approval-limits",(HttpContext ctx)=>store.Read(c=>GetLimits(c,ctx.User))).WithTags("Administration").WithSummary("Read rank×department approval limits");
        api.MapPut("/admin/org/approval-limits",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>PutLimits(c,tx,ctx.User,input))).WithTags("Administration").WithSummary("Replace rank×department approval limits");
        api.MapGet("/admin/org/sla-policies",(HttpContext ctx)=>store.Read(c=>GetSla(c,ctx.User))).WithTags("Administration").WithSummary("Read SLA policies (escalate, never auto-approve)");
        api.MapPut("/admin/org/sla-policies",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>PutSla(c,tx,ctx.User,input))).WithTags("Administration").WithSummary("Replace SLA policies");
    }
}
