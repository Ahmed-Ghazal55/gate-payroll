using System.Data.Common;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace ErpImport;

public sealed record CreditorHistoryMatch(string Company, string Reason, string[] Tables, string[] Columns, string Basis);

// Legal sponsorship evidence only. No profile/status/deletion filters and no payroll/ERP writes.
public static class DebtCreditorHistory
{
    public static readonly SourceTable[] Catalog = [
        new("app","Sponsors","Id EmployerIdentityNumber CommercialRegistrationNumber UnifiedNationalNumber"),
        new("app","Employees","Id IqamaNo SponsorId Status IsDeleted"),
        new("app","PayrollEmployees","Id NationalId SponsorId Status IsDeleted",true),
        new("app","RiderProfiles","Id EmployeeId IsDeleted",true),
        new("app","EmployeeSponsorshipPeriods","Id EmployeeId SponsorId EffectiveFrom EffectiveTo Status",true)
    ];
    public static Dictionary<string,List<JsonObject>> Read(DbConnection source)
    {
        if(source is SqlConnection sql) SourceReader.VerifyReadOnly(sql);
        return SourceReader.ReadTables(source,Catalog);
    }
    static string S(JsonObject row,string key)=>row[key]?.ToString()??"";
    static DateOnly? Date(JsonObject row,string key)=>DateOnly.TryParse(S(row,key).Split('T')[0],out var date)?date:null;
    public static CreditorHistoryMatch Resolve(Dictionary<string,List<JsonObject>> tables,string identity,IReadOnlyList<ISyncSponsor> sponsors)
    {
        List<JsonObject> Rows(string name)=>tables.GetValueOrDefault(name)??[];
        string Sponsor(string id)
        {
            var rows=Rows("Sponsors").Where(r=>S(r,"Id")==id).ToArray(); if(rows.Length!=1)return "";
            var r=rows[0]; var matches=sponsors.Where(s=>s.Id==id||S(r,"CommercialRegistrationNumber")!=""&&s.CommercialReg==S(r,"CommercialRegistrationNumber")||
                S(r,"EmployerIdentityNumber")!=""&&s.UnifiedNumber==S(r,"EmployerIdentityNumber")||S(r,"UnifiedNationalNumber")!=""&&s.UnifiedNumber==S(r,"UnifiedNationalNumber")).ToArray();
            return matches.Length==1?matches[0].Id:"";
        }
        var employees=Rows("Employees").Where(r=>S(r,"IqamaNo")==identity).ToArray();
        var ids=employees.Select(r=>S(r,"Id")).ToHashSet();
        var periods=Rows("EmployeeSponsorshipPeriods").Where(r=>ids.Contains(S(r,"EmployeeId"))).ToArray();
        if(periods.Any(r=>Date(r,"EffectiveFrom")==null||S(r,"EffectiveTo")!=""&&Date(r,"EffectiveTo")==null||Date(r,"EffectiveTo")<Date(r,"EffectiveFrom")))
            return new("","erp_history_invalid_period",["app.EmployeeSponsorshipPeriods"],["EffectiveFrom","EffectiveTo"],"invalid_period");
        var cutoff=new DateOnly(2026,10,31);
        // The creditor is the sponsor on the balance date; an ended period only when none covers it.
        var covering=periods.Where(r=>Date(r,"EffectiveFrom")<=cutoff&&(Date(r,"EffectiveTo") is not {} to||to>=cutoff)).ToArray();
        var ended=periods.Where(r=>Date(r,"EffectiveTo") is {} d&&d<=cutoff).ToArray();
        JsonObject[] selected=[];string basis="latest_known";
        if(covering.Length>0){selected=covering;basis="period_covering_balance_date";}
        else if(ended.Length>0){var latest=ended.Max(r=>Date(r,"EffectiveTo"));selected=ended.Where(r=>Date(r,"EffectiveTo")==latest).ToArray();basis="latest_period_ended_by_balance_date";}
        else if(periods.Length>0){var latest=periods.Max(r=>Date(r,"EffectiveFrom"));selected=periods.Where(r=>Date(r,"EffectiveFrom")==latest).ToArray();}
        string[] names,columns;
        if(selected.Length>0){names=["app.Employees","app.EmployeeSponsorshipPeriods","app.Sponsors"];columns=["Employees.IqamaNo","EmployeeSponsorshipPeriods.EmployeeId","EmployeeSponsorshipPeriods.SponsorId","EmployeeSponsorshipPeriods.EffectiveFrom","EmployeeSponsorshipPeriods.EffectiveTo","Sponsors.EmployerIdentityNumber","Sponsors.CommercialRegistrationNumber","Sponsors.UnifiedNationalNumber"];}
        else
        {
            selected=[..employees,..Rows("PayrollEmployees").Where(r=>S(r,"NationalId")==identity)];
            names=["app.Employees","app.PayrollEmployees","app.RiderProfiles","app.Sponsors"];
            columns=["Employees.IqamaNo","Employees.SponsorId","PayrollEmployees.NationalId","PayrollEmployees.SponsorId","RiderProfiles.EmployeeId","Sponsors.EmployerIdentityNumber","Sponsors.CommercialRegistrationNumber","Sponsors.UnifiedNationalNumber"];
            basis="latest_known_current_record_no_period_available";
        }
        var companies=selected.Select(r=>Sponsor(S(r,"SponsorId"))).Distinct().ToArray();
        if(companies.Length==0)return new("","creditor_unresolved",names,columns,basis);
        if(companies.Length!=1||companies[0]=="")return new("","erp_history_ambiguous_company",names,columns,basis);
        return new(companies[0],"",names,columns,basis);
    }
}
