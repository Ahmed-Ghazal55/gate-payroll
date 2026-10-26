using System.Data.Common;
using System.Text.Json.Nodes;
namespace Payroll;

public static class AccountingPolicy
{
    public static bool PostingAccount(string code)=>!FinancialCore.ChartOfAccounts.Any(x=>x.ParentCode==code);
    public static void ValidateChart()
    {
        var accounts=FinancialCore.ChartOfAccounts;
        J.Need(accounts.Select(x=>x.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count()==accounts.Length,"رموز حسابات مكررة.");
        foreach(var account in accounts)
        {
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var current=account;
            while(current.ParentCode!=null){J.Need(seen.Add(current.Code),"دورة في شجرة الحسابات.");current=FinancialCore.ResolveAccount(current.ParentCode);}
            J.Need(!account.IsVault||PostingAccount(account.Code),"الحساب التجميعي لا يستقبل ترحيلًا مباشرًا.");
        }
    }
    public static FinancialCore.AccountDef ValidateOperation(string code,string category,string operation,string? platform=null)
    {
        J.Need(operation is "disbursement" or "collection","نوع حركة غير صالح.");
        var account=FinancialCore.ResolveVault(code);var item=FinancialCore.ResolveCategory(category);
        J.Need(account.VaultPurpose!="JahezFee","رسوم جاهز تُعالج في مسار الرسوم والتسليم فقط.");
        J.Need(operation!="disbursement"||account.AllowedDirections!="InOnly","الحساب يقبل القبض فقط.");
        J.Need(operation!="collection"||account.AllowedDirections!="OutOnly","الحساب يقبل الصرف فقط.");
        J.Need(item.AllowedVaultKinds==null||item.AllowedVaultKinds.Contains(account.VaultKind)||operation=="collection"&&AccountingEvents.IsAdvance(category)&&account.VaultKind is "MainInternal" or "Bank","نوع الصندوق لا يناسب البند.");
        if(category=="PlatformTransferredDebt")
        {
            J.Need(operation=="collection"&&platform=="Jahez"&&account.VaultPurpose is "JahezSettlement" or "GeneralAndSettlement","إحالة جاهز تُحصّل في حسابات جاهز المخصصة فقط؛ بقية المنصات تحتاج حساب مقاصة مسندًا قبل التحصيل.");
            return account;
        }
        if(account.VaultKind=="JahezCashbox")
        {
            J.Need(operation=="collection"&&category=="PlatformTransferredDebt"&&platform=="Jahez","صندوق جاهز لا يمول المديونيات أو HR؛ يقبل تحصيل مديونية جاهز الموثقة فقط.");
            return account;
        }
        if(account.VaultPurpose is "GeneralAndSettlement" or "JahezSettlement")throw new ApiError("حساب تصفيات جاهز الداخلي/البنكي معزول عن HR والسلف؛ استخدم المسار المخصص.");
        if(account.VaultKind=="PetroApp")J.Need(operation=="collection"&&category=="FuelOverage","بترو آب مقاصة وقود فقط.");
        if(operation=="disbursement")
        {
            if(category=="CashAdvance")J.Need(account.VaultKind=="MainInternal","السلفة النقدية من الصندوق الداخلي فقط.");
            if(category=="BankAdvance")J.Need(account.VaultKind=="Bank","السلفة البنكية من البنك فقط.");
        }
        return account;
    }
    public static decimal Balance(DbConnection c,DbTransaction? tx,string accountCode)=>J.Money(Store.List(c,"journal",tx).SelectMany(j=>(j["lines"] as JsonArray)?.OfType<JsonObject>()??[]).Where(l=>l.S("accountCode")==accountCode).Sum(l=>l.N("debit")-l.N("credit")));
    public static void RequireAvailable(decimal balance,decimal amount){J.Need(amount>=0&&amount==J.Money(amount)&&amount<=Math.Max(0,balance),"المبلغ يتجاوز الرصيد الفعلي المتاح؛ لا يمكن إنشاء رصيد بتحويل أو تسليم.");}
    public static void ValidateCompanyTransfer(FinancialCore.AccountDef from,FinancialCore.AccountDef to)
    {
        J.Need(from.Code!=to.Code&&from.DefaultSponsorId==to.DefaultSponsorId,"التحويل العام بين حسابين مختلفين لنفس السجل فقط.");
        foreach(var account in new[]{from,to})J.Need(account.VaultKind is "MainInternal" or "Bank"&&account.VaultPurpose is not ("JahezFee" or "GeneralAndSettlement" or "JahezSettlement"),"هذه حسابات جاهز أو مقاصة؛ استخدم المسار المخصص وليس تحويل الشركة العام.");
        J.Need(from.AllowedDirections!="InOnly"&&to.AllowedDirections!="OutOnly","اتجاه تحويل غير مسموح.");
    }
    // Business owner confirmed on 2026-10-26: account-opening fees are company revenue.
    public const string FeeBeneficiary = "company";
    public static string FeeCounterpart(string beneficiary)=>beneficiary==FeeBeneficiary?"4101":throw new ApiError("رسوم فتح حساب جاهز إيراد للشركة حسب السياسة المعتمدة؛ لا تقيد لحساب جهة أخرى.");
}
