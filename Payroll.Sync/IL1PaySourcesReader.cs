using System.Data.Common;
using System.Text.Json.Nodes;
namespace ErpImport;

/// <summary>U-W2-34 L1 deduction sources for the pay run (M-0701). Live Omar reads only; fakes in tests.</summary>
public interface IL1PaySourcesReader
{
    PaySourcesLiveQuery.PageResult FuelConsumption(DbConnection source,DateOnly month,int page,int pageSize,string search,LiveOperationsQuery.Scope scope,CancellationToken ct=default);
    PaySourcesLiveQuery.PageResult VehicleAssignmentPeriods(DbConnection source,DateOnly from,DateOnly to,int page,int pageSize,string search,LiveOperationsQuery.Scope scope,CancellationToken ct=default);
    PaySourcesLiveQuery.PageResult HousingPeriods(DbConnection source,DateOnly from,DateOnly to,int page,int pageSize,string search,LiveOperationsQuery.Scope scope,CancellationToken ct=default);
}

/// <summary>Production reader — delegates to allowlisted SQL against Omar's ERP.</summary>
public sealed class LiveL1PaySourcesReader:IL1PaySourcesReader
{
    public PaySourcesLiveQuery.PageResult FuelConsumption(DbConnection source,DateOnly month,int page,int pageSize,string search,LiveOperationsQuery.Scope scope,CancellationToken ct=default)
        =>PaySourcesLiveQuery.FuelConsumption(source,month,page,pageSize,search,scope,ct);
    public PaySourcesLiveQuery.PageResult VehicleAssignmentPeriods(DbConnection source,DateOnly from,DateOnly to,int page,int pageSize,string search,LiveOperationsQuery.Scope scope,CancellationToken ct=default)
        =>PaySourcesLiveQuery.VehicleAssignmentPeriods(source,from,to,page,pageSize,search,scope,ct);
    public PaySourcesLiveQuery.PageResult HousingPeriods(DbConnection source,DateOnly from,DateOnly to,int page,int pageSize,string search,LiveOperationsQuery.Scope scope,CancellationToken ct=default)
        =>PaySourcesLiveQuery.HousingPeriods(source,from,to,page,pageSize,search,scope,ct);
}

/// <summary>Synthetic in-memory L1 facts for SQLite/SQL Server unit tests — never touches Omar.</summary>
public sealed class FakeL1PaySourcesReader:IL1PaySourcesReader
{
    public List<JsonObject> FuelRows{get;}=[];
    public List<JsonObject> VehicleRows{get;}=[];
    public List<JsonObject> HousingRows{get;}=[];

    public PaySourcesLiveQuery.PageResult FuelConsumption(DbConnection source,DateOnly month,int page,int pageSize,string search,LiveOperationsQuery.Scope scope,CancellationToken ct=default)
        =>Page(FuelRows,page,pageSize,new(){["litersTotal"]=FuelRows.Sum(r=>(decimal)r.N("liters")),["amountTotal"]=FuelRows.Sum(r=>(decimal)r.N("totalInclVat",r.N("amount")))});
    public PaySourcesLiveQuery.PageResult VehicleAssignmentPeriods(DbConnection source,DateOnly from,DateOnly to,int page,int pageSize,string search,LiveOperationsQuery.Scope scope,CancellationToken ct=default)
        =>Page(VehicleRows,page,pageSize,new(){["assignments"]=VehicleRows.Count});
    public PaySourcesLiveQuery.PageResult HousingPeriods(DbConnection source,DateOnly from,DateOnly to,int page,int pageSize,string search,LiveOperationsQuery.Scope scope,CancellationToken ct=default)
        =>Page(HousingRows,page,pageSize,new(){["residences"]=HousingRows.Count});

    static PaySourcesLiveQuery.PageResult Page(IReadOnlyList<JsonObject> rows,int page,int pageSize,JsonObject summary)
    {
        page=Math.Max(1,page);pageSize=Math.Clamp(pageSize,1,PaySourcesLiveQuery.MaxPageSize);
        int skip=(page-1)*pageSize;
        var items=rows.Skip(skip).Take(pageSize).Select(r=>r.DeepClone()!.AsObject()).ToList();
        return new(items,rows.Count,summary,DateTimeOffset.UtcNow);
    }
}
