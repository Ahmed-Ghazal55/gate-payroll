using System.Globalization;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.SqlClient;
namespace Payroll;

/// <summary>U-W2-12 live capture: read-only Omar facts through existing Payroll.Sync live readers.
/// Scheme from approved pay settings; Keeta eligibility from Omar view; other eligibility from pay settings.
/// A zero-row page still confirms a live read (empty ≠ StoreOps fallback).</summary>
public sealed class LiveOpsLiveFactsReader:IOpsLiveFactsReader
{
    readonly Func<SqlConnection?> _open;
    readonly Store? _store;

    public bool ConfirmsLiveRead=>true;

    public LiveOpsLiveFactsReader(IConfiguration config,Store store):this(()=>OpenErp(config),store){}
    public LiveOpsLiveFactsReader(Func<SqlConnection?> open,Store? store=null){_open=open;_store=store;}

    public IEnumerable<JsonObject> RowsFor(string kind,string companyId,string app,string periodMonth)
    {
        if(kind=="scheme")return SchemeFromPaySettings(companyId,app,periodMonth);
        if(kind=="eligibility")return EligibilityFromSources(companyId,app,periodMonth);

        using var erp=_open()??throw new ApiError("اتصال مصدر التشغيل غير مضبوط.",503);
        SourceReader.VerifyReadOnly(erp);
        var scope=ScopeFor(companyId);
        var from=From(periodMonth);var to=To(periodMonth);
        // Materialize while the ERP connection is open.
        return kind switch
        {
            "ninja_daily"=>CollectAllPages((p,s)=>NinjaInputsLiveQuery.DailyPerformance(erp,scope,new NinjaInputsLiveQuery.PeriodRequest(from,to,p,s))).ToList(),
            "ninja_daily_deduction"=>CollectAllPages((p,s)=>NinjaInputsLiveQuery.DailyDeductions(erp,scope,new NinjaInputsLiveQuery.PeriodRequest(from,to,p,s))).ToList(),
            "housing"=>CollectAllPages((p,s)=>PaySourcesLiveQuery.HousingPeriods(erp,from,to,p,s,"",scope)).Select(NormalizeHousing).ToList(),
            "fuel"=>CollectAllPages((p,s)=>PaySourcesLiveQuery.FuelConsumption(erp,from,p,s,"",scope)).Select(NormalizeFuelRow).ToList(),
            "vehicle"=>CollectAllPages((p,s)=>PaySourcesLiveQuery.VehicleAssignmentPeriods(erp,from,to,p,s,"",scope)).Select(NormalizeVehicle).ToList(),
            "assignment"=>LiveKindPages(erp,scope,app,from,to,"rider_work_timeline").Select(NormalizeAssignment).ToList(),
            "platform_account"=>LiveKindPages(erp,scope,app,from,to,"platform_account").ToList(),
            "keeta_invoice_row"=>LiveKindPages(erp,scope,app,from,to,"platform_account").ToList(),
            "hs_ops" or "platform_ops"=>LiveKindPages(erp,scope,app,from,to,"platform_day_usage").ToList(),
            "hs_wallet"=>[],
            _ when LiveOperationsQuery.HasKind(kind)=>LiveKindPages(erp,scope,app,from,to,kind).ToList(),
            _=>[]
        };
    }

    IEnumerable<JsonObject> SchemeFromPaySettings(string companyId,string app,string periodMonth)
    {
        if(_store==null)return [];
        string period=PeriodKey(periodMonth);
        return _store.Read(c=>
        {
            var riders=Store.List(c,"rider").Where(r=>string.Equals(r.S("sponsorId"),companyId,StringComparison.Ordinal))
                .Select(r=>r.S("id")).ToHashSet(StringComparer.Ordinal);
            var rows=new List<JsonObject>();
            foreach(var assignment in Store.List(c,"pay_assignment")
                .Where(a=>string.Equals(a.S("app"),app,StringComparison.Ordinal)
                    &&string.Equals(a.S("period"),period,StringComparison.Ordinal)
                    &&string.Equals(a.S("status"),"approved",StringComparison.Ordinal)))
            {
                string rider=assignment.S("rider");
                if(riders.Count>0&&!riders.Contains(rider))continue;
                var scheme=Store.Get(c,assignment.S("schemeId"),"pay_scheme");
                string code=scheme?.S("code")??"";
                if(code==""&&scheme?["data"] is JsonObject data)code=data.S("type",data.S("name"));
                rows.Add(new JsonObject
                {
                    ["id"]=assignment.S("id"),
                    ["erpRiderId"]=rider,
                    ["schemeCode"]=code,
                    ["source"]="pay_settings"
                });
            }
            return rows;
        });
    }

    IEnumerable<JsonObject> EligibilityFromSources(string companyId,string app,string periodMonth)
    {
        if(string.Equals(PayPlatforms.App(app),"keeta",StringComparison.Ordinal))
            return KeetaEligibilityPages(companyId,periodMonth);
        if(_store==null)return [];
        string period=PeriodKey(periodMonth);
        return _store.Read(c=>
        {
            var riders=Store.List(c,"rider").Where(r=>string.Equals(r.S("sponsorId"),companyId,StringComparison.Ordinal))
                .Select(r=>r.S("id")).ToHashSet(StringComparer.Ordinal);
            var rows=new List<JsonObject>();
            foreach(var assignment in Store.List(c,"pay_assignment")
                .Where(a=>string.Equals(a.S("app"),app,StringComparison.Ordinal)
                    &&string.Equals(a.S("period"),period,StringComparison.Ordinal)
                    &&string.Equals(a.S("status"),"approved",StringComparison.Ordinal)))
            {
                string rider=assignment.S("rider");
                if(riders.Count>0&&!riders.Contains(rider))continue;
                var scheme=Store.Get(c,assignment.S("schemeId"),"pay_scheme");
                string rulesId=scheme?["data"] is JsonObject d?d.S("eligibilitySetId"):"";
                bool eligible=rulesId==""||Store.Get(c,rulesId,"pay_eligibility") is { } rules
                    &&string.Equals(rules.S("status"),"approved",StringComparison.Ordinal);
                rows.Add(new JsonObject
                {
                    ["id"]=assignment.S("id"),
                    ["erpRiderId"]=rider,
                    ["eligible"]=eligible,
                    ["source"]="pay_settings"
                });
            }
            return rows;
        });
    }

    IEnumerable<JsonObject> KeetaEligibilityPages(string companyId,string periodMonth)
    {
        using var erp=_open()??throw new ApiError("اتصال مصدر التشغيل غير مضبوط.",503);
        SourceReader.VerifyReadOnly(erp);
        var scope=ScopeFor(companyId);
        var month=From(periodMonth);
        return CollectAllPages((p,s)=>PaySourcesLiveQuery.KeetaEligibility(erp,month,p,s,"",scope))
            .Select(r=>new JsonObject
            {
                ["id"]=r.S("id"),
                ["erpRiderId"]=r.S("riderProfileId"),
                ["eligible"]=string.Equals(r.S("expectedStatus"),"Valid",StringComparison.OrdinalIgnoreCase),
                ["source"]="keeta_eligibility_view"
            })
            .ToList();
    }

    static IEnumerable<JsonObject> LiveKindPages(SqlConnection erp,LiveOperationsQuery.Scope scope,string app,DateOnly from,DateOnly to,string kind)
    {
        int size=LiveOperationsQuery.MaxPageSize;int page=1;long seen=0;
        while(true)
        {
            var request=new LiveOperationsQuery.PageRequest(page,size,"id","",0,size,"",
                from.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
                to.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),app,"");
            var result=LiveOperationsQuery.Page(erp,kind,request,scope,FinancialCore.Sponsors);
            var projected=LiveOperationsSource.Project(result.Snapshot,FinancialCore.Sponsors,kind);
            var items=projected.Documents.Select(d=>Operations.Project(d,kind)).ToList();
            foreach(var row in items){seen++;yield return row;}
            if(items.Count==0||seen>=result.Total)yield break;
            page++;
        }
    }

    /// <summary>Page until every source row is collected. Uses returned count (not requested size) so a 200-row server cap cannot stop at 400 of 600.</summary>
    public static IEnumerable<JsonObject> CollectAllPages(Func<int,int,(IReadOnlyList<JsonObject> Items,long Total)> page)
    {
        int size=LiveOperationsQuery.MaxPageSize;int n=1;long seen=0;
        while(true)
        {
            var (items,total)=page(n,size);
            foreach(var row in items){seen++;yield return row;}
            if(items.Count==0||seen>=total)yield break;
            n++;
        }
    }

    static IEnumerable<JsonObject> CollectAllPages(Func<int,int,NinjaInputsLiveQuery.PageResult> page)
        =>CollectAllPages((p,s)=>{var r=page(p,s);return (r.Items,r.Total);});

    static IEnumerable<JsonObject> CollectAllPages(Func<int,int,PaySourcesLiveQuery.PageResult> page)
        =>CollectAllPages((p,s)=>{var r=page(p,s);return (r.Items,r.Total);});

    static JsonObject NormalizeHousing(JsonObject r)
    {
        var copy=r.DeepClone()!.AsObject();
        if(copy.S("erpRiderId")=="")copy["erpRiderId"]=copy.S("riderProfileId");
        bool hasFrom=copy.S("effectiveFrom")!="";
        bool hasTo=copy.S("effectiveTo")!="";
        if(!hasFrom&&!hasTo)
        {
            if(copy.S("month")=="")copy["month"]=copy.S("reportMonth",copy.S("period"));
            copy["monthOnly"]=true;
        }
        return copy;
    }

    /// <summary>Fuel/housing month totals keep the month and a flag; never invent day dates.</summary>
    public static JsonObject NormalizeFuelRow(JsonObject r)
    {
        var copy=r.DeepClone()!.AsObject();
        if(copy.S("erpRiderId")=="")copy["erpRiderId"]=copy.S("riderProfileId");
        if(copy.S("month")=="")copy["month"]=copy.S("reportMonth",copy.S("period"));
        bool hasFrom=copy.S("effectiveFrom")!="";
        bool hasTo=copy.S("effectiveTo")!="";
        if(!hasFrom&&!hasTo)
        {
            copy["monthOnly"]=true;
            copy["effectiveFrom"]="";
            copy["effectiveTo"]="";
        }
        return copy;
    }

    public static void DemandDayPeriodsIfRequired(JsonObject row,bool needsDays)
    {
        if(!needsDays)return;
        if(row.B("monthOnly")||(row.S("effectiveFrom")==""&&row.S("effectiveTo")==""))
            throw new ApiError("مصدر الوقود/السكن فيه إجمالي الشهر فقط بدون أيام فعلية؛ قاعدة الأجر تحتاج أياماً.",409);
    }

    static JsonObject NormalizeVehicle(JsonObject r)
    {
        var copy=r.DeepClone()!.AsObject();
        if(copy.S("id")=="")copy["id"]=copy.S("vehicleId");
        copy["actualRiderProfileId"]=copy.S("riderProfileId");
        copy["erpRiderId"]=copy.S("riderProfileId");
        copy["effectiveFrom"]=Day(copy.S("startedAt",copy.S("permissionStartsOn")));
        copy["effectiveTo"]=Day(copy.S("endedAt",copy.S("permissionEndsOn")));
        if(copy.S("sponsorId")=="")copy["sponsorId"]=copy.S("sponsorUnifiedNumber");
        return copy;
    }

    static JsonObject NormalizeAssignment(JsonObject r)
    {
        var copy=r.DeepClone()!.AsObject();
        if(copy.S("actualRiderProfileId")=="")copy["actualRiderProfileId"]=copy.S("riderId",copy.S("riderProfileId"));
        if(copy.S("platformRiderAccountId")=="")copy["platformRiderAccountId"]=copy.S("platformAccountId");
        if(copy.S("effectiveFrom")==""&&copy.S("startedAt")!="")copy["effectiveFrom"]=Day(copy.S("startedAt"));
        if(copy.S("effectiveTo")==""&&copy.S("endedAt")!="")copy["effectiveTo"]=Day(copy.S("endedAt"));
        return copy;
    }

    static string Day(string value)=>value.Length>=10?value[..10]:value;
    static string PeriodKey(string periodMonth)=>periodMonth.Length>=7?periodMonth[..7]:periodMonth;

    static LiveOperationsQuery.Scope ScopeFor(string companyId)
    {
        var sponsor=FinancialCore.FindSponsor(companyId);
        var numbers=sponsor?.UnifiedNumber is {Length:>0} n?new[]{n}:Array.Empty<string>();
        return new(false,numbers,[],[],[],null,null,PlatformRestricted:false);
    }

    static DateOnly From(string periodMonth)
    {
        string s=periodMonth.Length>=10?periodMonth[..10]:periodMonth.Length==7?periodMonth+"-01":periodMonth;
        return DateOnly.Parse(s,CultureInfo.InvariantCulture);
    }

    static DateOnly To(string periodMonth)
    {
        var from=From(periodMonth);
        return new DateOnly(from.Year,from.Month,DateTime.DaysInMonth(from.Year,from.Month));
    }

    static SqlConnection? OpenErp(IConfiguration config)
    {
        string cs=config.GetConnectionString("ErpReadonly")??config["ERP_READONLY_DB"]??"";
        if(string.IsNullOrWhiteSpace(cs))return null;
        var erp=Connections.Source(cs);erp.Open();return erp;
    }
}
