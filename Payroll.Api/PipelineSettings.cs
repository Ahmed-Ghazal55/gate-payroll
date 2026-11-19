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
        ["salaryFuelCompanyPaysWhenNoCap"]=false, // m1: never silent "company pays all" when fuelCap is 0
        ["fuelSupplierAccount"]="114106", // §22.9 PetroApp clearing (target CoA)
        ["payrollBankAccount"]="111101", // M1 default payout bank (target CoA)
        ["hsWalletAccount"]="112513", // U-W1-29: HS wallet; HR services remain 112506
        ["keeta95CompanyOnly"]=true, // decision 7 ✅
        ["freezeInvoiceWithInputs"]=true // decision 10 ✅
    };

    public static JsonObject Current(JsonObject? overrideBag=null)
    {
        var d=Defaults();
        if(overrideBag!=null)foreach(var p in overrideBag)d[p.Key]=p.Value?.DeepClone();
        return d;
    }

    public static string HsWalletAccount(JsonObject? bag=null)=>Current(bag).S("hsWalletAccount","112513");
    public static string FuelSupplierAccount(JsonObject? bag=null)=>Current(bag).S("fuelSupplierAccount","114106");
    public static bool SalaryFuelOverageOnRider(JsonObject? bag=null)
    {var cur=Current(bag);return !cur.ContainsKey("salaryFuelOverageOnRider")||cur.B("salaryFuelOverageOnRider");}
    /// <summary>m1: when salary fuelCap is 0, company pays the whole fuel row only if this setting is true (default false).</summary>
    public static bool SalaryFuelCompanyPaysWhenNoCap(JsonObject? bag=null)=>Current(bag).B("salaryFuelCompanyPaysWhenNoCap");
}
