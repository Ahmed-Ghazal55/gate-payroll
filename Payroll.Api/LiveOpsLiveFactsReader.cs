using System.Globalization;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.SqlClient;
namespace Payroll;

/// <summary>U-W2-12 live capture: read-only Omar facts through existing Payroll.Sync live readers.
/// A zero-row page still confirms a live read (empty ≠ StoreOps fallback).</summary>
public sealed class LiveOpsLiveFactsReader:IOpsLiveFactsReader
{
    readonly Func<SqlConnection?> _open;

    public bool ConfirmsLiveRead=>true;

    public LiveOpsLiveFactsReader(IConfiguration config):this(()=>OpenErp(config)){}
    public LiveOpsLiveFactsReader(Func<SqlConnection?> open)=>_open=open;

    public IEnumerable<JsonObject> RowsFor(string kind,string companyId,string app,string periodMonth)
    {
        using var erp=_open()??throw new ApiError("اتصال مصدر التشغيل غير مضبوط.",503);
        SourceReader.VerifyReadOnly(erp);
        var scope=ScopeFor(companyId);
        var from=From(periodMonth);var to=To(periodMonth);
        // Materialize while the ERP connection is open.
        return kind switch
        {
            "ninja_daily"=>AllPages((p,s)=>NinjaInputsLiveQuery.DailyPerformance(erp,scope,new NinjaInputsLiveQuery.PeriodRequest(from,to,p,s))).ToList(),
            "ninja_daily_deduction"=>AllPages((p,s)=>NinjaInputsLiveQuery.DailyDeductions(erp,scope,new NinjaInputsLiveQuery.PeriodRequest(from,to,p,s))).ToList(),
            "housing"=>AllPages((p,s)=>PaySourcesLiveQuery.HousingPeriods(erp,from,to,p,s,"",scope)).Select(NormalizeHousing).ToList(),
            "fuel"=>AllPages((p,s)=>PaySourcesLiveQuery.FuelConsumption(erp,from,p,s,"",scope)).Select(NormalizeFuel).ToList(),
            "vehicle"=>AllPages((p,s)=>PaySourcesLiveQuery.VehicleAssignmentPeriods(erp,from,to,p,s,"",scope)).Select(NormalizeVehicle).ToList(),
            "assignment"=>LiveKindPages(erp,scope,app,from,to,"rider_work_timeline").ToList(),
            "platform_account"=>LiveKindPages(erp,scope,app,from,to,"platform_account").ToList(),
            "scheme" or "eligibility"=>[], // no Omar scheme/eligibility table — confirmed empty live
            "keeta_invoice_row"=>LiveKindPages(erp,scope,app,from,to,"platform_account").ToList(),
            "hs_ops" or "platform_ops"=>LiveKindPages(erp,scope,app,from,to,"platform_day_usage").ToList(),
            "hs_wallet"=>[],
            _ when LiveOperationsQuery.HasKind(kind)=>LiveKindPages(erp,scope,app,from,to,kind).ToList(),
            _=>[]
        };
    }

    static IEnumerable<JsonObject> LiveKindPages(SqlConnection erp,LiveOperationsQuery.Scope scope,string app,DateOnly from,DateOnly to,string kind)
    {
        const int size=500;int page=1;
        while(true)
        {
            var request=new LiveOperationsQuery.PageRequest(page,size,"id","",0,size,"",
                from.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
                to.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),app,"");
            var result=LiveOperationsQuery.Page(erp,kind,request,scope,FinancialCore.Sponsors);
            var projected=LiveOperationsSource.Project(result.Snapshot,FinancialCore.Sponsors,kind);
            var items=projected.Documents.Select(d=>Operations.Project(d,kind)).ToList();
            foreach(var row in items)yield return row;
            if(items.Count==0||(long)page*size>=result.Total)yield break;
            page++;
        }
    }

    static IEnumerable<JsonObject> AllPages(Func<int,int,(IReadOnlyList<JsonObject> Items,long Total)> page)
    {
        const int size=500;int n=1;
        while(true)
        {
            var (items,total)=page(n,size);
            foreach(var row in items)yield return row;
            if(items.Count==0||(long)n*size>=total)yield break;
            n++;
        }
    }

    static IEnumerable<JsonObject> AllPages(Func<int,int,NinjaInputsLiveQuery.PageResult> page)
        =>AllPages((p,s)=>{var r=page(p,s);return (r.Items,r.Total);});

    static IEnumerable<JsonObject> AllPages(Func<int,int,PaySourcesLiveQuery.PageResult> page)
        =>AllPages((p,s)=>{var r=page(p,s);return (r.Items,r.Total);});

    static JsonObject NormalizeHousing(JsonObject r)
    {
        var copy=r.DeepClone()!.AsObject();
        if(copy.S("erpRiderId")=="")copy["erpRiderId"]=copy.S("riderProfileId");
        return copy;
    }

    static JsonObject NormalizeFuel(JsonObject r)
    {
        var copy=r.DeepClone()!.AsObject();
        if(copy.S("erpRiderId")=="")copy["erpRiderId"]=copy.S("riderProfileId");
        if(copy.S("month")=="")copy["month"]=copy.S("reportMonth",copy.S("period"));
        string month=copy.S("month",copy.S("reportMonth"));
        if(copy.S("effectiveFrom")=="")copy["effectiveFrom"]=month.Length>=10?month[..10]:month;
        if(copy.S("effectiveTo")=="")copy["effectiveTo"]=copy.S("effectiveFrom");
        return copy;
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

    static string Day(string value)=>value.Length>=10?value[..10]:value;

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
