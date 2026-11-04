using System.Text.Json.Nodes;
namespace Payroll;

// A review-only projection of immutable approved ERP facts, never a second wallet ledger.
public static class HungerStationFacts
{
    public static readonly string[] Kinds=["hs_wallet_event","hs_remittance","hs_remittance_allocation","hs_handover_review","hs_month_close"];
    public static readonly IReadOnlyDictionary<string,string> AccountMapping=new Dictionary<string,string>{
        ["SupervisorChargingFund"]="112306",["RiderAdvance"]="112501",["PendingCollection"]="112513",["PlatformWalletWithdrawal"]="114107",["RemittanceInTransit"]="114101",["InvoiceReceivable"]=PayPlatforms.For("hs").Receivable,["DisputedInvoiceAmount"]="112803",["CommissionRevenue"]="411601",["SalaryRevenue"]="411102",["NegativeRiderNet"]="112507"
    };
    static bool Number(JsonObject facts,string key,out decimal value)
    {value=0;return facts[key]!=null&&decimal.TryParse(facts.S(key),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out value)&&value==J.Money(value);}
    public static JsonObject MonthClose(JsonObject facts)
    {
        bool known=Number(facts,"C",out decimal c)&Number(facts,"A",out decimal a)&Number(facts,"M",out decimal m);
        return known?new(){["status"]="known",["expected"]=-c,["remainingOldCarry"]=c+a,["currentPeriodBalance"]=m-(c+a),["negativeRiderNetAccount"]=m-(c+a)<0?"112507":"",["postingAllowed"]=false}:new(){["status"]="unknown",["rawObservedBalance"]=facts["M"]?.DeepClone(),["postingAllowed"]=false};
    }
    public static JsonObject Proposal(string kind,JsonObject document)
    {
        J.Need(Kinds.Contains(kind),"نوع وقائع هنقر غير معروف.");var facts=document["sourceFacts"] as JsonObject??new();string type=facts.S("EventType");var blockers=new JsonArray("preview_only","counterparty_and_signed_source_contract_required");string account=AccountMapping.GetValueOrDefault(type)??"";
        if(kind=="hs_remittance")account="114101";
        if(type=="ConfirmedReceipt")blockers.Add("explicit_company_cashbox_or_bank_required");
        if(account=="")blockers.Add("event_account_mapping_unknown");
        var result=new JsonObject{["sourceId"]=document.S("id"),["kind"]=kind,["companyId"]=document.S("companyId"),["accountCode"]=account,["sourceAmount"]=facts["Amount"]?.DeepClone(),["blockers"]=blockers,["postingAllowed"]=false};
        if(kind=="hs_month_close")
        {var close=MonthClose(facts);result["monthClose"]=close;result["accountCode"]=close.S("status")=="known"&&close.N("currentPeriodBalance")<0?"112507":"";if(close.S("status")=="unknown")blockers.Add("cma_unknown");else blockers.Add("rider_attribution_and_independent_approval_required");}
        else if(type=="NegativeRiderNet")
        {var close=MonthClose(facts);result["monthClose"]=close;if(close.S("status")!="known"){result["accountCode"]="";blockers.Add("cma_unknown");}else if(close.N("currentPeriodBalance")>=0){result["accountCode"]="";blockers.Add("no_negative_current_period_net");}}
        if(document.S("status")!="approved")blockers.Add("source_not_approved");return result;
    }
}
