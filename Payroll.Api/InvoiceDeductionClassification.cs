using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

// Classification changes who bears the original amount, never the invoiced amount itself.
public static class InvoiceDeductionClassification
{
    public static void Apply(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject invoice,JsonObject line,JsonObject input)
    {
        var deduction=(line["deductions"] as JsonArray??[]).OfType<JsonObject>().SingleOrDefault(d=>d.S("category")==input.Required("category"))??throw new ApiError("بند الخصم غير موجود.");
        string level=input.Required("level");J.Need(level is "company_level" or "account_level" or "rider_level","حدد الشركة أو اليوزر أو المندوب.");
        deduction.Remove("expenseAccount");deduction.Remove("riderId");
        if(level=="company_level")
        {string expense=PayPlatforms.Active(input.Required("expenseAccount"));J.Need(expense.StartsWith('5'),"خصم الشركة يحتاج حساب تكلفة.");deduction["expenseAccount"]=expense;}
        else if(level=="account_level")J.Need(line.S("platformAccountId")!=""&&line.S("lineType")!="company","سطر المورد بلا يوزر؛ اختر المندوب صراحة.");
        else
        {
            string rider=J.Iqama(input.Required("riderId"));var person=Store.Get(c,rider,"rider",tx)??throw new ApiError("المندوب غير موجود.",404);FinancialScopes.DemandRider(actor,person);
            string app=PayPlatforms.For(invoice.S("platform")).App,period=invoice.S("period");var month=DateTimeOffset.Parse(period+"-01T00:00:00+03:00");
            J.Need(ErpRiderClassification.ListedAsRider(person)&&Store.List(c,"platform_account",tx).Any(a=>a.S("companyId")==invoice.S("companyId")&&PayPlatforms.For(a.S("platformCode")).App==app&&(a["assignments"] as JsonArray??[]).OfType<JsonObject>().Any(x=>x.S("riderId")==rider&&DateTimeOffset.TryParse(x.S("RawEffectiveFrom"),out var from)&&from<month.AddMonths(1)&&(x.S("RawEffectiveTo")==""||DateTimeOffset.TryParse(x.S("RawEffectiveTo"),out var to)&&to>month))),"المندوب ليس مشغلًا فعليًا للتطبيق في السجل والشهر.");
            deduction["riderId"]=rider;
        }
        deduction["level"]=level;deduction["classifiedBy"]=actor.Identity!.Name;
    }
}
