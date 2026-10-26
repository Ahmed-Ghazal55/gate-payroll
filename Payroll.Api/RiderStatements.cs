using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static class RiderStatements
{
    private static bool Receivable(string code)=>code is "1210" or "1220" or "1230";
    public static JsonObject Build(DbConnection c,string iqama,string? sponsorId,ClaimsPrincipal user)
    {
        J.Iqama(iqama);string? sponsor=string.IsNullOrWhiteSpace(sponsorId)?null:FinancialCore.ResolveSponsor(sponsorId).Id;
        if(sponsor!=null)FinancialScopes.DemandSponsor(user,sponsor);
        var rider=Store.Get(c,iqama,"rider");
        var allDebts=Store.List(c,"debt").Where(d=>d.S("iqama")==iqama).ToList();
        var visible=allDebts.Where(d=>FinancialScopes.Category(user,d.S("category"))&&(d.S("platform")==""||user.HasClaim("finance.platforms",d.S("platform"))||AccessPolicy.Administrator(user))).ToDictionary(d=>d.S("id"));
        bool Owner(string id)=>FinancialScopes.Sponsor(user,id)&&(sponsor==null||sponsor==id);
        var journals=Store.List(c,"journal").Where(j=>j.S("iqama")==iqama).OrderBy(j=>j.S("createdAt")).ThenBy(j=>j.S("journalNumber")).ToList();
        var settlements=Store.List(c,"debt_settlement").Where(s=>s.S("iqama")==iqama).ToList();
        var lines=new List<JsonObject>();
        JsonObject Row(string owner,JsonObject debt,string type,string description,string timestamp,string period,string reference,decimal debit,decimal credit,string journalId="")=>new(){
            ["date"]=timestamp.Length>=10?timestamp[..10]:timestamp,["timestamp"]=timestamp,["period"]=period,["type"]=type,["typeAr"]=description,["description"]=description,
            ["category"]=debt.S("category"),["categoryAr"]=debt.S("categoryAr",debt.S("category")),["debtId"]=debt.S("id"),["sponsorId"]=owner,["sponsorNameAr"]=FinancialCore.FindSponsor(owner)?.ShortNameAr??"غير مربوط",
            ["reference"]=reference,["debit"]=debit,["credit"]=credit,["companyBorneAmount"]=0m,["journalEntryId"]=journalId};
        foreach(var journal in journals)
        {
            var journalLines=journal["lines"]!.AsArray().OfType<JsonObject>().ToList();
            foreach(var group in journalLines.Where(l=>Receivable(l.S("accountCode"))&&l.S("iqama")==iqama&&visible.ContainsKey(l.S("debtId"))).GroupBy(l=>(Owner:l.S("sponsorId"),Debt:l.S("debtId"))))
            {
                if(!Owner(group.Key.Owner))continue;decimal delta=J.Money(group.Sum(l=>l.N("debit")-l.N("credit")));if(delta==0)continue;
                string type=journal.S("transactionType");string desc=type=="intercompany_debt_transfer"?(delta>0?"نقل المديونية إلى السجل":"نقل المديونية من السجل"):type=="payroll_deduction"?"استقطاع قسط من راتب معتمد":journal.S("description");
                var row=Row(group.Key.Owner,visible[group.Key.Debt],type,desc,journal.S("createdAt"),journal.S("period"),journal.S("sourceRef"),Math.Max(0,delta),Math.Max(0,-delta),journal.S("id"));row["order"]=journal.S("journalNumber");lines.Add(row);
            }
            if(journal.S("transactionType") is "company_expense" or "debt_accrual")
            {
                string debtId=journal.S("debtId");
                if(visible.TryGetValue(debtId,out var debt)&&debt.N("totalAmount")==0)
                foreach(var group in journalLines.Where(l=>l.S("accountType")=="Expense"&&Owner(l.S("sponsorId"))).GroupBy(l=>l.S("sponsorId")))
                {
                    var row=Row(group.Key,debt,"company_borne_service","خدمة متحملة بالكامل على الشركة دون دين على المندوب",journal.S("createdAt"),journal.S("period"),journal.S("sourceRef"),0,0,journal.S("id"));row["companyBorneAmount"]=group.Sum(l=>l.N("debit")-l.N("credit"));lines.Add(row);
                }
            }
        }
        // Legacy records remain readable. Missing evidence is shown explicitly, never assigned to every company.
        var legacy=visible.Values.Where(d=>d.S("status") is "active" or "paused" or "settled" && !AccountingEvents.HasDebtOpening(d)).ToList();
        foreach(var debt in legacy)
        {
            var firstTransfer=journals.Where(j=>j.S("debtId")==debt.S("id")&&j.S("transactionType")=="intercompany_debt_transfer").FirstOrDefault();
            string owner=debt.S("originalCreditorSponsorId",firstTransfer?.S("originSponsorId",debt.S("creditorSponsorId"))??debt.S("creditorSponsorId"));
            if(!Owner(owner)&&!(sponsor==null&&owner==""&&AccessPolicy.Administrator(user)))continue;
            var initial=Row(owner,debt,"legacy_opening_unverified","رصيد تاريخي محفوظ — يحتاج مطابقة افتتاح، دون تعديل الأصل",debt.S("createdAt"),debt.S("effectiveFrom"),debt.S("sourceRef"),debt.N("totalAmount"),0);initial["legacyEvidenceIncomplete"]=true;lines.Add(initial);
            decimal known=settlements.Where(s=>s.S("debtId")==debt.S("id")).Sum(s=>s.N("collectedAmount")+s.N("waivedAmount"));
            decimal prior=J.Money(debt.N("totalAmount")-debt.N("remainingBalance")-known);
            if(prior>0)lines.Add(Row(owner,debt,"historical_settlement","سداد سابق محفوظ في الرصيد التاريخي",debt.S("createdAt"),debt.S("effectiveFrom"),debt.S("sourceRef")+":prior",0,prior));
        }
        foreach(var settlement in settlements.Where(s=>s.S("journalEntryId")==""&&Owner(s.S("creditorSponsorId"))))
            if(visible.TryGetValue(settlement.S("debtId"),out var debt))lines.Add(Row(settlement.S("creditorSponsorId"),debt,"legacy_settlement","تحصيل تاريخي محفوظ دون قيد مربوط",settlement.S("createdAt"),settlement.S("period"),settlement.S("receiptRef"),0,settlement.N("collectedAmount")+settlement.N("waivedAmount")));
        lines=lines.OrderBy(l=>l.S("timestamp"),StringComparer.Ordinal).ThenBy(l=>l.S("order"),StringComparer.Ordinal).ThenBy(l=>l.S("reference"),StringComparer.Ordinal).ToList();
        decimal balance=0;foreach(var line in lines){balance=J.Money(balance+line.N("debit")-line.N("credit"));line["runningBalance"]=balance;}
        var current=visible.Values.Where(d=>d.S("status") is "active" or "paused" or "settled").Where(d=>Owner(d.S("creditorSponsorId"))||(sponsor==null&&d.S("creditorSponsorId")==""&&AccessPolicy.Administrator(user))).ToList();
        var paid=settlements.Where(s=>visible.ContainsKey(s.S("debtId"))&&Owner(s.S("creditorSponsorId"))).ToList();
        bool payrollVisible=AccessPolicy.Has(user,"payroll.view");
        var ledger=Store.List(c,"ledger").Where(l=>payrollVisible&&l.S("iqama")==iqama&&(Owner(l.S("sponsorId"))||(l.S("sponsorId")==""&&sponsor==null&&FinancialScopes.AllCompanies(user)))).ToList();
        var bySponsor=new JsonArray(FinancialCore.Sponsors.Where(s=>Owner(s.Id)).Select(s=>(JsonNode?)new JsonObject{
            ["sponsorId"]=s.Id,["sponsorNameAr"]=s.ShortNameAr,["totalPrincipal"]=lines.Where(l=>l.S("sponsorId")==s.Id).Sum(l=>l.N("debit")),
            ["totalPaid"]=paid.Where(p=>p.S("creditorSponsorId")==s.Id).Sum(p=>p.N("collectedAmount")),["totalWaived"]=paid.Where(p=>p.S("creditorSponsorId")==s.Id).Sum(p=>p.N("waivedAmount")),
            ["totalCompanyShare"]=current.Where(d=>d.S("originSponsorId")==s.Id).Sum(d=>d.N("companyShareAmount")),
            ["remainingBalance"]=current.Where(d=>d.S("creditorSponsorId")==s.Id).Sum(d=>d.N("remainingBalance")),
            ["statementBalance"]=lines.Where(l=>l.S("sponsorId")==s.Id).Sum(l=>l.N("debit")-l.N("credit"))}).ToArray());
        decimal remaining=current.Sum(d=>d.N("remainingBalance"));
        return new JsonObject{
            ["iqama"]=iqama,["riderName"]=rider.S("name",current.FirstOrDefault().S("riderName","غير مسجل")),["sponsorshipSponsor"]=rider.S("sponsor"),["phone"]=rider.S("phone"),["city"]=rider.S("city"),["status"]=rider.S("status"),
            ["totalPrincipalDebt"]=current.Sum(d=>d.N("totalAmount")),["totalCompanyBorneCost"]=current.Sum(d=>d.N("companyShareAmount")),["totalPaidAmount"]=paid.Sum(s=>s.N("collectedAmount")),["totalWaivedAmount"]=paid.Sum(s=>s.N("waivedAmount")),
            ["totalRemainingDebt"]=remaining,["statementBalance"]=balance,["reconciliationDifference"]=J.Money(balance-remaining),["historyCoverage"]=legacy.Count>0?"legacy_requires_review":"journal_events",
            ["payrollVisible"]=payrollVisible,["payrollLedgerBalance"]=payrollVisible?JsonValue.Create(ledger.Sum(l=>l.N("amount"))):null,["bySponsor"]=bySponsor,["debts"]=new JsonArray(current.Select(d=>(JsonNode?)d.Clone()).ToArray()),
            ["statementLines"]=new JsonArray(lines.Select(l=>(JsonNode?)l).ToArray()),["journalEntries"]=new JsonArray(journals.Where(j=>visible.ContainsKey(j.S("debtId"))||payrollVisible).Select(j=>FinancialScopes.Journal(user,j)).Where(j=>j!=null).Select(j=>(JsonNode?)j).ToArray()),
            ["payrollLedgerRows"]=new JsonArray(ledger.Select(l=>(JsonNode?)l.Clone()).ToArray())
        };
    }
}
