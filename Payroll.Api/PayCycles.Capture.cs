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

    /// <summary>Copy store/live rows into pay_run_input for this cycle. Recapture replaces. Sealed → 409.</summary>
    public static JsonObject CaptureInputs(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,IConfiguration? config=null,IHostEnvironment? env=null,Func<string,IEnumerable<JsonObject>>? liveRows=null)
    {
        var cycle=Store.Get(c,id,Kind,tx)?.Clone()??throw new ApiError("الدورة غير موجودة.",404);
        Scope(actor,cycle,"pay.manage");
        if(PayRunInputs.Sealed(cycle))throw new ApiError("لا يمكن التقاط المدخلات بعد الاعتماد.",409);
        var cycleApps=Accounts(cycle).Select(a=>PayPlatforms.App(a.App)).Distinct(StringComparer.Ordinal).ToList();
        string app=cycle.S("app");if(app=="")app=cycleApps.FirstOrDefault()??"";
        J.Need(app!="","التطبيق مطلوب لالتقاط المدخلات.");
        app=PayPlatforms.App(app);
        if(cycleApps.Count==0)cycleApps=[app];
        string company=cycle.S("companyId"),period=cycle.S("period");
        var (from,to)=PeriodBounds(period);
        var kinds=cycleApps.SelectMany(PayRunInputs.KindsForApp).Distinct(StringComparer.Ordinal).ToList();
        var bag=new List<(string Kind,string DocId,JsonObject Body,string? Rowversion)>();
        var appSet=cycleApps.ToHashSet(StringComparer.Ordinal);

        IEnumerable<JsonObject> Source(string kind)
        {
            if(liveRows!=null)return liveRows(kind);
            // CI / Development store mode: freeze from payroll store (fixtures). Live ERP path uses liveRows from Map.
            return Store.List(c,kind,tx);
        }

        foreach(string kind in kinds)
        {
            if(kind=="vehicle")continue; // filled from assignment vehicle ids below
            IEnumerable<JsonObject> rows=Source(kind);
            rows=kind switch
            {
                "platform_account"=>rows.Where(a=>a.S("companyId")==company&&appSet.Contains(PayPlatforms.For(a.S("platformCode")).App)),
                "ninja_daily" or "ninja_daily_deduction" or "chefz_daily"=>rows.Where(d=>d.S("companyId")==company&&d.S("date").StartsWith(period+"-",StringComparison.Ordinal)),
                "vehicle_assignment"=>rows.Where(a=>PeriodOverlap(a,period,"startedAt","endedAt")),
                "housing_residence"=>rows.Where(r=>PeriodOverlap(r,period,"effectiveFrom","effectiveTo")),
                _=>rows
            };
            foreach(var row in rows)
            {
                // Keep full body for payroll calc (assignments etc.); Project only when live ops shape.
                var projected=row.Clone();
                if(projected.S("id")=="")projected["id"]=row.S("id");
                string docId=projected.S("id");if(docId=="")continue;
                string? rv=row.S("sourceRowversion");if(rv=="")rv=null;
                bag.Add((kind,docId,projected,rv));
            }
        }

        var vehicleIds=bag.Where(x=>x.Kind=="vehicle_assignment").Select(x=>x.Body.S("vehicleId")).Where(id=>id!="").ToHashSet(StringComparer.Ordinal);
        foreach(var vehicle in Source("vehicle").Where(v=>vehicleIds.Contains(v.S("id"))))
        {
            var projected=Operations.Kinds.Contains("vehicle")?Operations.Project(vehicle.Clone(),"vehicle"):vehicle.Clone();
            if(projected.S("id")=="")projected["id"]=vehicle.S("id");
            string docId=projected.S("id");if(docId=="")continue;
            string? rv=vehicle.S("sourceRowversion");if(rv=="")rv=null;
            bag.Add(("vehicle",docId,projected,rv));
        }

        var result=PayRunInputs.Replace(c,tx,id,app,from,to,actor.Identity!.Name!,bag);
        cycle["inputsCapturedAt"]=result.CapturedAt.ToString("O",CultureInfo.InvariantCulture);
        cycle["inputsCapturedBy"]=actor.Identity!.Name;
        cycle["inputsCaptureCounts"]=new JsonObject(result.Counts.OrderBy(kv=>kv.Key).Select(kv=>KeyValuePair.Create(kv.Key,(JsonNode?)JsonValue.Create(kv.Value))));
        cycle["sourceFingerprint"]=Fingerprint(c,tx,cycle);
        Store.Save(c,Kind,cycle,tx);
        var auditDetail=new JsonObject{["counts"]=cycle["inputsCaptureCounts"]!.DeepClone()};
        Store.Audit(c,tx,"pay.inputs_captured",id,actor.Identity!.Name!);
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

    /// <summary>Stream all live pages for capture kinds (period + platform in SQL). Runs outside the payroll write TX.</summary>
    internal static Dictionary<string,List<JsonObject>> StageLiveCapture(HttpContext ctx,Store store,IConfiguration config,JsonObject cycle)
    {
        string app=cycle.S("app");if(app=="")app=Accounts(cycle).Select(a=>a.App).FirstOrDefault()??"";
        app=PayPlatforms.App(app);
        var (from,to)=PeriodBounds(cycle.S("period"));
        var bag=new Dictionary<string,List<JsonObject>>(StringComparer.Ordinal);
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);cancel.CancelAfter(TimeSpan.FromSeconds(120));
        string sourceConnection=config.GetConnectionString("ErpReadonly")??config["ERP_READONLY_DB"]??"";
        if(string.IsNullOrWhiteSpace(sourceConnection))throw new ApiError("اتصال مصدر التشغيل غير مضبوط.",503);
        using var erp=Connections.Source(sourceConnection);erp.Open();SourceReader.VerifyReadOnly(erp);
        var scope=store.Read(c=>OperationsLiveScope(c,ctx.User));
        foreach(string kind in PayRunInputs.KindsForApp(app).Where(k=>k!="vehicle"&&Operations.Kinds.Contains(k)))
        {
            var rows=new List<JsonObject>();
            int page=1;long total=long.MaxValue;
            while((page-1)*(long)PayRunInputs.CapturePageSize<total)
            {
                var request=new LiveOperationsQuery.PageRequest(page,PayRunInputs.CapturePageSize,"id","", (page-1)*PayRunInputs.CapturePageSize,PayRunInputs.CapturePageSize,"",from,to,app);
                var result=LiveOperationsQuery.Page(erp,kind,request,scope,FinancialCore.Sponsors,cancel.Token);
                total=result.Total;
                foreach(var doc in LiveOperationsSource.Documents(result.Snapshot,FinancialCore.Sponsors,kind))
                    rows.Add(Operations.Project(doc,kind));
                if(result.Snapshot.Tables.Count==0||rows.Count>=total)break;
                page++;
                if(page>10000)throw new ApiError("التقاط المدخلات تجاوز حد الصفحات.",503);
            }
            bag[kind]=rows;
        }
        // Vehicles referenced by assignments
        var vehicleIds=bag.GetValueOrDefault("vehicle_assignment")?.Select(a=>a.S("vehicleId")).Where(id=>id!="").ToHashSet(StringComparer.Ordinal)??[];
        if(vehicleIds.Count>0&&Operations.Kinds.Contains("vehicle"))
        {
            var vehicles=new List<JsonObject>();
            int page=1;long total=long.MaxValue;
            while((page-1)*(long)PayRunInputs.CapturePageSize<total)
            {
                var request=new LiveOperationsQuery.PageRequest(page,PayRunInputs.CapturePageSize,"id","",(page-1)*PayRunInputs.CapturePageSize,PayRunInputs.CapturePageSize,"",from,to,app);
                var result=LiveOperationsQuery.Page(erp,"vehicle",request,scope,FinancialCore.Sponsors,cancel.Token);
                total=result.Total;
                foreach(var doc in LiveOperationsSource.Documents(result.Snapshot,FinancialCore.Sponsors,"vehicle"))
                {
                    var projected=Operations.Project(doc,"vehicle");
                    if(vehicleIds.Contains(projected.S("id")))vehicles.Add(projected);
                }
                page++;
                if(page>10000)break;
            }
            bag["vehicle"]=vehicles;
        }
        return bag;
    }

    // Mirrors Operations.LiveScope without making it public.
    static LiveOperationsQuery.Scope OperationsLiveScope(DbConnection payroll,ClaimsPrincipal actor)
    {
        bool all=FinancialScopes.AllCompanies(actor);
        var sponsors=FinancialCore.Sponsors.Where(s=>FinancialScopes.Sponsor(actor,s.Id)).ToList();
        var regs=sponsors.Select(s=>s.CommercialReg).Where(r=>r!="").Distinct(StringComparer.Ordinal).ToList();
        var riders=all?[]:Store.List(payroll,"rider").Where(r=>FinancialScopes.Rider(actor,r)).ToList();
        var employees=riders.Select(r=>r.S("erpSourceId")).Where(id=>id!="").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var profiles=riders.SelectMany(r=>(r["erpRiderProfileIds"] as JsonArray??[]).Select(n=>n?.ToString()??"")).Where(id=>id!="").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var platforms=actor.FindAll("finance.platforms").Select(c=>c.Value)
            .Select(p=>PayPlatforms.Table.FirstOrDefault(a=>a.Platform==p)?.App??p.Trim().ToLowerInvariant())
            .Where(p=>p!="").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new(all,regs,employees,profiles,platforms,null,null,PlatformRestricted:!AccessPolicy.Administrator(actor));
    }
}
