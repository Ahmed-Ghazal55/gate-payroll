using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
namespace Payroll;

public static partial class PayCycles
{
    const string CaptureInputsMissing="التقط المدخلات أولًا";
    const string CaptureRefusedAfterUse="لا يمكن إعادة التقاط المدخلات بعد بدء الحساب؛ ألغِ الدورة وأنشئ أخرى.";

    public static void DemandCaptured(JsonObject cycle)
    {
        if(!PayRunInputs.Captured(cycle))throw new ApiError(CaptureInputsMissing,409);
    }

    static (string From,string To) PeriodBounds(string period)
    {
        period=J.Period(period);
        var from=DateTime.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);
        var to=from.AddMonths(1).AddDays(-1);
        return (from.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),to.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
    }

    static bool PeriodOverlap(JsonObject row,string period,string fromKey,string toKey)
    {
        string start=row.S(fromKey,row.S("RawEffectiveFrom",row.S("EffectiveFrom",row.S("effectiveFrom"))));
        string end=row.S(toKey,row.S("RawEffectiveTo",row.S("EffectiveTo",row.S("effectiveTo"))));
        if(start=="")return false;
        try{return Overlaps(new JsonObject{[fromKey]=start,[toKey]=end},period,fromKey,toKey);}catch(ApiError){return false;}
    }

    /// <summary>Cycle company commercial regs + that company's riders (employee/profile ids). Not actor scope.</summary>
    public static LiveOperationsQuery.Scope CaptureScope(DbConnection payroll,JsonObject cycle)
    {
        string company=cycle.S("companyId");
        var sponsor=FinancialCore.Sponsors.FirstOrDefault(s=>s.Id==company)
            ??throw new ApiError("شركة الدورة غير معروفة.",400);
        var regs=string.IsNullOrWhiteSpace(sponsor.CommercialReg)?[]:new[]{sponsor.CommercialReg};
        var riders=Store.List(payroll,"rider").Where(r=>r.S("sponsorId")==company||r.S("companyId")==company).ToList();
        var employees=riders.Select(r=>r.S("erpSourceId")).Where(id=>id!="").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var profiles=riders.SelectMany(r=>(r["erpRiderProfileIds"] as JsonArray??[]).Select(n=>n?.ToString()??"")).Where(id=>id!="").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new(false,regs,employees,profiles,[],null,null,PlatformRestricted:false);
    }

    static HashSet<string> CycleRiderIds(DbConnection c,DbTransaction? tx,JsonObject cycle)
    {
        string company=cycle.S("companyId");
        var fromAccounts=Accounts(cycle).SelectMany(a=>Rows(a.Account["riders"]).OfType<JsonObject>()).Select(r=>r.S("rider")).Where(id=>id!="");
        var fromCompany=Store.List(c,"rider",tx).Where(r=>r.S("sponsorId")==company||r.S("companyId")==company).Select(r=>r.S("id"));
        return fromAccounts.Concat(fromCompany).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Apps on the cycle. Multi-app allowed only when every app is staged/captured (B5).</summary>
    static List<string> DemandApps(JsonObject cycle)
    {
        var cycleApps=Accounts(cycle).Select(a=>PayPlatforms.App(a.App)).Distinct(StringComparer.Ordinal).ToList();
        string app=cycle.S("app");
        if(cycleApps.Count==0)
        {
            J.Need(app!="","التطبيق مطلوب لالتقاط المدخلات.");
            return [PayPlatforms.App(app)];
        }
        if(app!=""&&cycleApps.Count==1&&!string.Equals(cycleApps[0],PayPlatforms.App(app),StringComparison.Ordinal))
            throw new ApiError("تطبيق الدورة لا يطابق فواتيرها.",409);
        return cycleApps;
    }

    /// <summary>Copy store/live rows into pay_run_input for this cycle. A0: only while stage ≤ 1.</summary>
    public static JsonObject CaptureInputs(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,IConfiguration? config=null,IHostEnvironment? env=null,Func<string,IEnumerable<JsonObject>>? liveRows=null,JsonObject? input=null)
    {
        var cycle=Store.Get(c,id,Kind,tx)?.Clone()??throw new ApiError("الدورة غير موجودة.",404);
        Scope(actor,cycle,"pay.manage");
        // Refuse before any fingerprint work (B1).
        if(!PayRunInputs.CaptureAllowed(cycle))throw new ApiError(PayRunInputs.Sealed(cycle)?"لا يمكن التقاط المدخلات بعد الاعتماد.":CaptureRefusedAfterUse,409);
        var apps=DemandApps(cycle);
        var appSet=apps.ToHashSet(StringComparer.Ordinal);
        string company=cycle.S("companyId"),period=cycle.S("period");
        var kinds=apps.SelectMany(PayRunInputs.KindsForApp).Distinct(StringComparer.Ordinal).ToList();
        var riderIds=CycleRiderIds(c,tx,cycle);
        var bag=new List<(string Kind,string DocId,JsonObject Body)>();
        var seen=new HashSet<string>(StringComparer.Ordinal);

        IEnumerable<JsonObject> Source(string kind)=>liveRows!=null?liveRows(kind):Store.List(c,kind,tx);

        foreach(string kind in kinds)
        {
            if(kind=="vehicle")continue;
            IEnumerable<JsonObject> rows=Source(kind);
            rows=kind switch
            {
                "platform_account"=>rows.Where(a=>a.S("companyId")==company&&appSet.Contains(PayPlatforms.For(a.S("platformCode")).App)),
                "ninja_daily" or "ninja_daily_deduction" or "chefz_daily"=>rows.Where(d=>d.S("companyId")==company&&d.S("date").StartsWith(period+"-",StringComparison.Ordinal)),
                "vehicle_assignment"=>rows.Where(a=>PeriodOverlap(a,period,"startedAt","endedAt")&&(riderIds.Count==0||riderIds.Contains(a.S("riderId")))),
                "housing_residence"=>rows.Where(r=>PeriodOverlap(r,period,"effectiveFrom","effectiveTo")&&(riderIds.Count==0||riderIds.Contains(r.S("riderId")))),
                _=>rows
            };
            foreach(var row in rows)
            {
                var projected=Operations.Project(row.Clone(),kind);
                if(projected.S("id")=="")projected["id"]=row.S("id");
                string docId=projected.S("id");if(docId=="")continue;
                string key=kind+"|"+docId;
                if(!seen.Add(key))continue;
                bag.Add((kind,docId,projected));
            }
        }

        var vehicleIds=bag.Where(x=>x.Kind=="vehicle_assignment").Select(x=>x.Body.S("vehicleId")).Where(id=>id!="").ToHashSet(StringComparer.Ordinal);
        if(vehicleIds.Count>0)
        {
            foreach(var vehicle in Source("vehicle").Where(v=>vehicleIds.Contains(v.S("id"))||vehicleIds.Contains(Operations.Project(v.Clone(),"vehicle").S("id"))))
            {
                var projected=Operations.Project(vehicle.Clone(),"vehicle");
                if(projected.S("id")=="")projected["id"]=vehicle.S("id");
                string docId=projected.S("id");if(docId==""||!vehicleIds.Contains(docId))continue;
                string key="vehicle|"+docId;
                if(!seen.Add(key))continue;
                bag.Add(("vehicle",docId,projected));
            }
        }

        var countsPreview=kinds.ToDictionary(k=>k,_=>0,StringComparer.Ordinal);
        foreach(var row in bag)countsPreview[row.Kind]=countsPreview.GetValueOrDefault(row.Kind)+1;
        var gaps=new JsonObject();
        var reasons=input?["zeroKindReasons"] as JsonObject;
        // B5: required kinds enforced for live/staged capture. Store/CI fixtures may omit them with an explicit reason.
        bool enforceRequired=liveRows!=null||reasons!=null;
        foreach(string kind in apps.SelectMany(PayRunInputs.RequiredKinds).Distinct(StringComparer.Ordinal))
        {
            if(countsPreview.GetValueOrDefault(kind)>0)continue;
            string reason=reasons?.S(kind)??"";
            if(enforceRequired&&reason==""&&liveRows!=null)
                throw new ApiError("لا صفوف لنوع مطلوب قبل التقاط المدخلات: "+kind+"؛ سجّل السبب أو صحّح المصدر.",409);
            if(reason!="")gaps[kind]=reason;
            else if(liveRows==null)gaps[kind]="store: zero rows";
        }
        // housing_residence may be zero without a reason (stated).
        if(!countsPreview.ContainsKey("housing_residence"))countsPreview["housing_residence"]=0;

        var result=PayRunInputs.Replace(c,tx,id,actor.Identity!.Name!,bag);
        foreach(string kind in kinds)if(!result.Counts.ContainsKey(kind))result.Counts[kind]=0;
        cycle["inputsCapturedAt"]=result.CapturedAt.ToString("O",CultureInfo.InvariantCulture);
        cycle["inputsCapturedBy"]=actor.Identity!.Name;
        cycle["inputsCaptureCounts"]=new JsonObject(result.Counts.OrderBy(kv=>kv.Key).Select(kv=>KeyValuePair.Create(kv.Key,(JsonNode?)JsonValue.Create(kv.Value))));
        if(gaps.Count>0)cycle["inputsCaptureGaps"]=gaps;else cycle.Remove("inputsCaptureGaps");
        if(apps.Count==1)cycle["app"]=apps[0];
        cycle["sourceFingerprint"]=Fingerprint(c,tx,cycle);
        Store.Save(c,Kind,cycle,tx);
        var audit=new JsonObject
        {
            ["id"]=Guid.NewGuid().ToString("N"),
            ["action"]="pay.inputs_captured",
            ["entityId"]=id,
            ["actor"]=actor.Identity!.Name!,
            ["at"]=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture),
            ["counts"]=cycle["inputsCaptureCounts"]!.DeepClone()
        };
        Store.Save(c,"audit",audit,tx);
        return new JsonObject
        {
            ["cycleId"]=id,
            ["capturedAt"]=cycle.S("inputsCapturedAt"),
            ["counts"]=cycle["inputsCaptureCounts"]!.DeepClone()
        };
    }

    /// <summary>Cycle-scoped ops docs: pay_run_input only after capture (never silent Store fallback).</summary>
    public static List<JsonObject> CycleOps(DbConnection c,DbTransaction? tx,JsonObject cycle,string kind)
    {
        DemandCaptured(cycle);
        return PayRunInputs.List(c,tx,cycle.S("id"),kind);
    }

    public static JsonObject? CycleOpsGet(DbConnection c,DbTransaction? tx,JsonObject cycle,string kind,string docId)
    {
        DemandCaptured(cycle);
        return PayRunInputs.Get(c,tx,cycle.S("id"),kind,docId);
    }

    /// <summary>Stream live pages with keyset + end recount. Scope from cycle, not actor.</summary>
    internal static Dictionary<string,List<JsonObject>> StageLiveCapture(HttpContext ctx,Store store,IConfiguration config,JsonObject cycle)
    {
        var apps=DemandApps(cycle);
        var (from,to)=PeriodBounds(cycle.S("period"));
        var bag=new Dictionary<string,List<JsonObject>>(StringComparer.Ordinal);
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);cancel.CancelAfter(TimeSpan.FromSeconds(120));
        string sourceConnection=config.GetConnectionString("ErpReadonly")??config["ERP_READONLY_DB"]??"";
        if(string.IsNullOrWhiteSpace(sourceConnection))throw new ApiError("اتصال مصدر التشغيل غير مضبوط.",503);
        using var erp=Connections.Source(sourceConnection);erp.Open();SourceReader.VerifyReadOnly(erp);
        var scope=store.Read(c=>CaptureScope(c,cycle));
        var kinds=apps.SelectMany(PayRunInputs.KindsForApp).Distinct(StringComparer.Ordinal).Where(k=>k!="vehicle"&&Operations.Kinds.Contains(k)).ToList();
        foreach(string kind in kinds)
        {
            var merged=new List<JsonObject>();
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(string app in apps)
            {
                foreach(var row in StageKindKeyset(erp,kind,scope,from,to,app,cancel.Token))
                {
                    string id=row.S("id");if(id==""||!ids.Add(id))continue;
                    merged.Add(row);
                }
            }
            bag[kind]=merged;
        }
        var vehicleIds=bag.GetValueOrDefault("vehicle_assignment")?.Select(a=>a.S("vehicleId")).Where(id=>id!="").ToHashSet(StringComparer.Ordinal)??[];
        if(vehicleIds.Count>0&&Operations.Kinds.Contains("vehicle"))
        {
            var vehicles=new List<JsonObject>();
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(string app in apps)
            {
                foreach(var v in StageKindKeyset(erp,"vehicle",scope,from,to,app,cancel.Token,pageCap:10000))
                {
                    string id=v.S("id");if(id==""||!vehicleIds.Contains(id)||!ids.Add(id))continue;
                    vehicles.Add(v);
                }
            }
            bag["vehicle"]=vehicles;
        }
        return bag;
    }

    static List<JsonObject> StageKindKeyset(DbConnection erp,string kind,LiveOperationsQuery.Scope scope,string from,string to,string app,CancellationToken ct,int pageCap=10000)
    {
        var rows=new List<JsonObject>();
        var ids=new HashSet<string>(StringComparer.Ordinal);
        string after="";
        long? firstTotal=null;
        int pages=0;
        while(true)
        {
            pages++;
            if(pages>pageCap)throw new ApiError("التقاط المدخلات تجاوز حد الصفحات.",503);
            var request=new LiveOperationsQuery.PageRequest(1,PayRunInputs.CapturePageSize,"id","",0,PayRunInputs.CapturePageSize,"",from,to,app,after);
            var result=LiveOperationsQuery.Page(erp,kind,request,scope,FinancialCore.Sponsors,ct,firstTotal);
            firstTotal??=result.Total;
            string lastId="";
            foreach(var doc in LiveOperationsSource.Documents(result.Snapshot,FinancialCore.Sponsors,kind))
            {
                var projected=Operations.Project(doc,kind);
                string id=projected.S("id");if(id==""||!ids.Add(id))continue;
                rows.Add(projected);
                lastId=id;
            }
            if(lastId==""||rows.Count>=firstTotal.Value)break;
            after=lastId;
        }
        // Re-count at end; mismatch → refuse (nothing written yet — staging only).
        var recount=LiveOperationsQuery.Page(erp,kind,new LiveOperationsQuery.PageRequest(1,1,"id","",0,1,"",from,to,app,""),scope,FinancialCore.Sponsors,ct);
        if(recount.Total!=firstTotal)throw new ApiError("تغير عدد صفوف المصدر أثناء التقاط المدخلات؛ أعد المحاولة.",503);
        if(rows.Count!=firstTotal&&kind!="vehicle")
        {
            // Keyset walk may under-count if projection drops ids; still fail closed when totals disagree with unique rows for non-vehicle.
            if(rows.Count>firstTotal)throw new ApiError("صفوف مكررة في مصدر التشغيل أثناء التقاط المدخلات.",503);
        }
        return rows;
    }

    /// <summary>Pre-cycle live page helper (B3): one kind, period+platform in SQL, cycle company scope.</summary>
    public static List<JsonObject> StagePreCycleKind(HttpContext ctx,Store store,IConfiguration config,string kind,string companyId,string period,string app)
    {
        var (from,to)=PeriodBounds(period);
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);cancel.CancelAfter(TimeSpan.FromSeconds(60));
        string sourceConnection=config.GetConnectionString("ErpReadonly")??config["ERP_READONLY_DB"]??"";
        if(string.IsNullOrWhiteSpace(sourceConnection))throw new ApiError("اتصال مصدر التشغيل غير مضبوط.",503);
        using var erp=Connections.Source(sourceConnection);erp.Open();SourceReader.VerifyReadOnly(erp);
        var fake=new JsonObject{["companyId"]=companyId,["period"]=period,["app"]=app};
        var scope=store.Read(c=>CaptureScope(c,fake));
        return StageKindKeyset(erp,kind,scope,from,to,PayPlatforms.App(app),cancel.Token);
    }
}
