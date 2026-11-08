using System.Data.Common;
using System.Text.Json.Nodes;
namespace Payroll;

// Target chart validation is explicit and separate from the historical HS posting allowlist.
public static class PayPosting
{
    public static FinancialCore.AccountDef Account(string code)
    {
        PayPlatforms.Active(code);var row=FinanceViews.Accounts.Single(a=>a.S("code")==code);
        return new(code,row.S("nameAr"),code[0] switch{'1'=>"Asset",'2'=>"Liability",'3'=>"Equity",'4'=>"Revenue",_=>"Expense"},row.S("nature")=="debit"?"Debit":"Credit",row.S("parent"));
    }
    public static JsonObject Line(string code,string company,decimal debit,decimal credit,string app,string rider,string period,string source,string account="",string debt="")
    {
        var line=FinancialCore.CreateJournalLine(code,company,debit,credit,iqama:rider,debtId:debt,targetChart:true,payChart:true);
        line["platform"]=app==""?"":PayPlatforms.For(app).Platform;line["app"]=app;line["period"]=period;line["platformAccountId"]=account;line["sourceId"]=source;return line;
    }
    public static JsonObject Post(DbConnection c,DbTransaction tx,JsonObject cycle,JsonArray lines,string actor,string suffix="",string? reverses=null,string? postingPeriod=null)
    {
        string period=postingPeriod??cycle.S("postingPeriod",cycle.S("period")),company=cycle.S("companyId");
        J.Need(!Store.List(c,"accounting_period",tx).Any(p=>p.S("companyId")==company&&p.S("period")==period&&p.S("status")=="closed"),"الفترة المحاسبية مغلقة.");
        var cycleLines=PayCycles.Lines(c,tx,cycle.S("id"));
        foreach(var line in lines.OfType<JsonObject>())
        {
            var related=cycleLines.Where(l=>(line.S("iqama")==""||l.S("rider")==line.S("iqama"))&&(line.S("app")==""||l.S("app")==line.S("app"))).ToList();
            line["platforms"]=new JsonArray(related.Select(l=>PayPlatforms.For(l.S("app")).Platform).Distinct().Select(p=>(JsonNode?)JsonValue.Create(p)).ToArray());
            line["platformAccountIds"]=new JsonArray(related.SelectMany(l=>(l["sources"] as JsonArray??[]).OfType<JsonObject>()).Select(s=>s.S("accountId")).Where(s=>s!="").Distinct().Select(p=>(JsonNode?)JsonValue.Create(p)).ToArray());
        }
        var journal=FinancialCore.PostJournal(c,tx,"pay_cycle"+(reverses==null?"":"_reversal"),"دورة رواتب التطبيقات المعتمدة",period,"PAY-CYCLE:"+cycle.S("id")+suffix,lines,actor,originSponsorId:company,creditorSponsorId:company,targetChart:true,reversesJournalId:reverses,payChart:true);
        journal["payCycleId"]=cycle.S("id");Store.Save(c,"journal",journal,tx);return journal;
    }
    public static string DebtAccount(JsonObject debt)
    {
        string code=debt.S("receivableAccountCode");if(code!=""&&FinanceViews.Accounts.Any(a=>a.S("code")==code))return PayPlatforms.Active(code);
        return PayPlatforms.Active(FinanceViews.MapAccount(new(){["accountCode"]=AccountingEvents.ReceivableCode(debt)},new JsonObject(),debt,null,null)??throw new ApiError("تصنيف ذمة الدين غير مثبت على الشجرة الجديدة."));
    }
    public static JsonObject Installment(DbConnection c,DbTransaction tx,JsonObject debt,PayrollResult result,string cycleId,decimal amount,string reference)
    {
        var cycle=Store.Get(c,cycleId,"pay_cycle",tx)??throw new ApiError("دورة القسط غير موجودة.");string company=cycle.S("companyId"),creditor=FinancialCore.RequireDebtSponsor(debt).Id;
        J.Need(company==creditor,"الدين على سجل مختلف؛ يلزم تحويل أو مقاصة معتمدة قبل دورة الرواتب.");string code=DebtAccount(debt);
        decimal balance=Store.List(c,"journal",tx).Where(FinanceViews.Posted).SelectMany(j=>(j["lines"] as JsonArray??[]).OfType<JsonObject>().Select(l=>(J:j,L:l))).Where(x=>x.L.S("debtId")==debt.S("id")&&x.L.S("sponsorId")==company&&FinanceViews.MapAccount(x.L,x.J,debt,null,null)==code).Sum(x=>x.L.N("debit")-x.L.N("credit"));
        J.Need(balance>=amount,"رصيد ذمة الدين غير مطابق للأستاذ؛ راجع الافتتاح قبل الاستقطاع.");
        var lines=new JsonArray(Line("212103",company,amount,0,"",result.Iqama,result.Period,reference,debt:debt.S("id")),Line(code,company,0,amount,"",result.Iqama,result.Period,reference,debt:debt.S("id")));
        return Post(c,tx,cycle,lines,cycle.S("debtDeductionApprovedBy",cycle.S("approvedBy")),":"+reference);
    }
}
