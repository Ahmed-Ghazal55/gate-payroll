using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>Open-decision defaults (OPEN-DECISIONS-2026-11-14). Flip via settings later; never hard-code call sites.</summary>
public static class PipelineSettings
{
    public static JsonObject Defaults()=>new()
    {
        ["mudadSource"]="monthly_list_import", // decision 1 ✅
        ["iqamaFeeSource"]="hr_deduction_request", // decision 2 ✅
        ["housingScope"]="all_commission_apps", // decision 3 ✅
        ["billingCutoff"]="14:00", // decision 4 ✅
        ["salaryFuelOverageOnRider"]=true, // decision 5 ✅
        ["hsWalletAccount"]="112506", // decision 6 ✅
        ["keeta95CompanyOnly"]=true, // decision 7 ✅
        ["freezeInvoiceWithInputs"]=true // decision 10 ✅
    };

    public static JsonObject Current(JsonObject? overrideBag=null)
    {
        var d=Defaults();
        if(overrideBag!=null)foreach(var p in overrideBag)d[p.Key]=p.Value?.DeepClone();
        return d;
    }

    public static string HsWalletAccount(JsonObject? bag=null)=>Current(bag).S("hsWalletAccount","112506");
    public static bool SalaryFuelOverageOnRider(JsonObject? bag=null)
    {var cur=Current(bag);return !cur.ContainsKey("salaryFuelOverageOnRider")||cur.B("salaryFuelOverageOnRider");}
}
