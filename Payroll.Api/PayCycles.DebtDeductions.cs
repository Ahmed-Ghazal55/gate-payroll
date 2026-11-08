using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PayCycles
{
    static void SnapshotDebtAfter(DbConnection c,DbTransaction tx,JsonObject cycle)
    {
        var ids=Rows(cycle["debtBefore"]).OfType<JsonObject>().Select(d=>d.S("id")).Concat(Rows(cycle["riders"]).OfType<JsonObject>().Select(r=>r.S("carryDebtId"))).Where(id=>id!="").Distinct();
        cycle["debtAfter"]=new JsonArray(ids.Select(id=>(JsonNode?)Store.Get(c,id,"debt",tx)!.Clone()).ToArray());
    }
    static void CreateCarry(DbConnection c,DbTransaction tx,JsonObject cycle,JsonObject row,ClaimsPrincipal actor)
    {
        J.Need(Blockers(row).Count==0,"العجز يحتاج حسم الحالة قبل ترحيله.");
        string id="pay-carry:"+cycle.S("id")+":"+row.S("rider");decimal amount=-row.N("net");
        J.Need(Store.Get(c,id,"debt",tx)==null,"دين العجز مسجل بالفعل.");
        Store.Save(c,"debt",new(){["id"]=id,["iqama"]=row.S("rider"),["category"]="NegativeSalaryCarryover",["creditorSponsorId"]=cycle.S("companyId"),["receivableAccountCode"]="112507",["status"]="active",["totalAmount"]=amount,["remainingBalance"]=amount,["paidAmount"]=0,["monthlyInstallment"]=0,["effectiveFrom"]=DateTime.Parse(cycle.S("period")+"-01").AddMonths(1).ToString("yyyy-MM"),["sourceRef"]=cycle.S("id"),["createdBy"]=cycle.S("createdBy"),["approvedBy"]=actor.Identity!.Name,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")},tx);
        row["carryDebtId"]=id;row["carryAmount"]=amount;
    }
    static void SettleOperatingFuel(DbConnection c,DbTransaction tx,JsonObject cycle,JsonObject row,ClaimsPrincipal actor)
    {
        foreach(var d in Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("rider")==row.S("rider")&&d.S("type")=="fuel_recovery"))
        {
            var debt=Store.Get(c,d.S("source"),"debt",tx)!;FinancialScopes.DemandDebt(actor,debt);
            var before=Rows(cycle["debtBefore"]).OfType<JsonObject>().Single(b=>b.S("id")==debt.S("id"));J.Need(JsonNode.DeepEquals(before,debt),"تغير مصدر الوقود؛ أعد المراجعة.");
            J.Need(DebtLedgerBalance(c,tx,debt)>=d.N("fuelTotal"),"ذمة الوقود غير مثبتة في الأستاذ.");
            debt["paidAmount"]=debt.N("paidAmount")-d.N("amount");debt["companyExpenseAmount"]=debt.N("companyExpenseAmount")+d.N("companyCost");debt["remainingBalance"]=0;debt["status"]="settled";debt["lastDeductionPeriod"]=cycle.S("period");Store.Save(c,"debt",debt,tx);
            d["approvedBy"]=actor.Identity!.Name;d["status"]="posted";
        }
    }
    static decimal DebtLedgerBalance(DbConnection c,DbTransaction tx,JsonObject debt)
    {
        string account=PayPosting.DebtAccount(debt),company=FinancialCore.RequireDebtSponsor(debt).Id;
        return Store.List(c,"journal",tx).Where(FinanceViews.Posted).SelectMany(j=>Rows(j["lines"]).OfType<JsonObject>().Select(l=>(J:j,L:l))).Where(x=>x.L.S("debtId")==debt.S("id")&&x.L.S("sponsorId")==company&&FinanceViews.MapAccount(x.L,x.J,debt,null,null)==account).Sum(x=>x.L.N("debit")-x.L.N("credit"));
    }
    static decimal DebtSelectionLimit(JsonObject row,JsonObject debt)=>J.Money(Math.Min(debt.N("remainingBalance"),Math.Max(0,row.N("salaryNet",row.N("net")))));
    static JsonArray ProposedDebtItems(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject cycle,JsonObject row)
    {
        decimal available=Math.Max(0,row.N("salaryNet",row.N("net")));var items=new JsonArray();
        foreach(var d in Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("rider")==row.S("rider")&&d.S("type")=="debt").OrderBy(d=>d.N("phase")).ThenBy(d=>d.N("order")).ThenBy(d=>d.S("source"),StringComparer.Ordinal))
        {
            var debt=Store.Get(c,d.S("source"),"debt",tx)??throw new ApiError("مصدر الدين غير موجود.");FinancialScopes.DemandDebt(actor,debt);
            decimal due=Math.Max(0,-d.N("computed")),max=J.Money(Math.Min(available,debt.N("remainingBalance"))),amount=row.S("debtDeductionStatus")=="confirmed"?-d.N("amount"):J.Money(Math.Min(max,d.N("requestedAmount",due)));
            items.Add(new JsonObject{["key"]=DeductionKey(d),["source"]=d.S("source"),["category"]=d.S("debtCategory"),["labelAr"]=d.S("labelAr"),["due"]=due,["max"]=max,["amount"]=amount,["remainingBalance"]=debt.N("remainingBalance"),["accountCode"]=d.S("accountCode")});available-=amount;
        }
        return items;
    }
    public static JsonObject DebtDeductionsView(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        var cycle=Store.Get(c,id,Kind,tx)??throw new ApiError("الدورة غير موجودة.",404);Scope(actor,cycle,"pay.view");var rows=new JsonArray();
        foreach(var row in Rows(cycle["riders"]).OfType<JsonObject>())
        {
            if(Store.Get(c,row.S("rider"),"rider",tx) is {} person)FinancialScopes.DemandRider(actor,person);
            bool approved=row.S("salaryApprovedAt")!="",confirmed=row.S("debtDeductionStatus")=="confirmed";var actions=new JsonArray();
            if(approved&&!confirmed&&cycle.S("status") is "approved" or "partially_approved")
            {if(AccessPolicy.Has(actor,"pay.manage"))actions.Add("edit");if(AccessPolicy.Has(actor,"pay.approve")&&!row.S("debtSelectedBy",cycle.S("createdBy")).Equals(actor.Identity!.Name,StringComparison.OrdinalIgnoreCase))actions.Add("confirm");}
            rows.Add(new JsonObject{["rider"]=row.S("rider"),["salaryNet"]=row.N("salaryNet",row.N("net")),["status"]=confirmed?"confirmed":approved?"pending":"salary_pending",["items"]=ProposedDebtItems(c,tx,actor,cycle,row),["net"]=row.N("net"),["allowedActions"]=actions});
        }
        return new(){["cycleId"]=id,["stage"]=cycle.N("stage"),["status"]=cycle.S("debtDeductionStatus","salary_pending"),["rows"]=rows};
    }
    public static JsonObject DebtDeductionsAct(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        var cycle=Store.Get(c,id,Kind,tx)?.Clone()??throw new ApiError("الدورة غير موجودة.",404);string action=input.Required("action");
        J.Need(action is "edit" or "confirm" or "confirm-all","الإجراء edit أو confirm أو confirm-all.");Scope(actor,cycle,action=="edit"?"pay.manage":"pay.approve");Open(c,tx,cycle);
        J.Need(cycle.S("status") is "approved" or "partially_approved","اعتمد الرواتب قبل استقطاع الديون.",409);
        var rows=Rows(cycle["riders"]).OfType<JsonObject>().Where(r=>action=="confirm-all"||r.S("rider")==input.Required("rider")).ToList();J.Need(rows.Count>0,"المندوب ليس في الدورة.",404);
        if(action=="edit")
        {
            var row=rows.Single();J.Need(row.S("salaryApprovedAt")!=""&&row.S("debtDeductionStatus")!="confirmed","القسط متاح بعد اعتماد الراتب وقبل تأكيد الخصم.",409);
            var debt=Rows(cycle["deductions"]).OfType<JsonObject>().SingleOrDefault(d=>d.S("rider")==row.S("rider")&&d.S("type")=="debt"&&DeductionKey(d)==input.Required("key"))??throw new ApiError("القسط غير موجود.",404);
            var source=Store.Get(c,debt.S("source"),"debt",tx)!;FinancialScopes.DemandDebt(actor,source);decimal amount=Money(input,"amount");J.Need(amount>=0&&amount<=DebtSelectionLimit(row,source),"الخصم من صفر إلى الأقل من الرصيد والصافي المتاح.");
            string reason=amount!=-debt.N("computed")?Reason(input):input.S("reason");if(row["debtSelectionHistory"] is not JsonArray)row["debtSelectionHistory"]=new JsonArray();row["debtSelectionHistory"]!.AsArray().Add(new JsonObject{["source"]=debt.S("source"),["from"]=debt.N("requestedAmount"),["to"]=amount,["reason"]=reason,["by"]=actor.Identity!.Name,["at"]=DateTimeOffset.UtcNow.ToString("O")});debt["requestedAmount"]=amount;debt["selectionReason"]=reason;row["debtSelectedBy"]=actor.Identity!.Name;row["debtSelectedAt"]=DateTimeOffset.UtcNow.ToString("O");
        }
        else
        {
            var payables=Store.List(c,"journal",tx).Where(FinanceViews.Posted).SelectMany(j=>Rows(j["lines"]).OfType<JsonObject>()).Where(l=>l.S("sponsorId")==cycle.S("companyId")&&l.S("accountCode")=="212103").GroupBy(l=>l.S("iqama")).ToDictionary(g=>g.Key,g=>g.Sum(l=>l.N("credit")-l.N("debit")));
            foreach(var row in rows.Where(r=>r.S("salaryApprovedAt")!=""&&r.S("debtDeductionStatus")!="confirmed"))
            {
                J.Need(!row.S("debtSelectedBy",cycle.S("createdBy")).Equals(actor.Identity!.Name,StringComparison.OrdinalIgnoreCase),"مؤكد الخصم غير من اختار القسط.",403);
                var proposed=ProposedDebtItems(c,tx,actor,cycle,row);var payLines=new List<PayLine>();decimal total=0;
                foreach(var item in proposed.OfType<JsonObject>())
                {
                    var d=Rows(cycle["deductions"]).OfType<JsonObject>().Single(d=>d.S("rider")==row.S("rider")&&d.S("type")=="debt"&&DeductionKey(d)==item.S("key"));
                    var source=Store.Get(c,d.S("source"),"debt",tx)!;var before=Rows(cycle["debtBefore"]).OfType<JsonObject>().Single(b=>b.S("id")==source.S("id"));
                    J.Need(JsonNode.DeepEquals(source,before),"تغير رصيد أو قسط الدين منذ المراجعة؛ أعد معاينة الاستقطاع.");
                    decimal amount=item.N("amount");d["amount"]=-amount;d["confirmedBy"]=actor.Identity!.Name;
                    if(amount>0){payLines.Add(new("debt_installment","قسط معتمد بعد الراتب",-amount,d.S("source")));total+=amount;}
                }
                J.Need(total<=Math.Max(0,row.N("salaryNet")),"الاستقطاع يتجاوز الصافي الموجب.");
                decimal payable=payables.GetValueOrDefault(row.S("rider"));
                J.Need(total<=Math.Max(0,payable),"الصافي المتاح تغير بعد صرف أو تسوية؛ أعد المراجعة.");
                cycle["debtDeductionApprovedBy"]=actor.Identity!.Name;Store.Save(c,Kind,cycle,tx);
                Debts.Post(c,tx,new(row.S("rider"),cycle.S("period"),"pay_cycle",0,row.N("salaryNet"),row.N("salaryNet"),row.N("salaryNet"),0,0,payLines,[]),id,payCycle:true);
                row["net"]=J.Money(Math.Max(0,row.N("salaryNet"))-total);row["closing"]=row.N("carried")+row.N("net");row["debtDeductionStatus"]="confirmed";row["debtConfirmedBy"]=actor.Identity!.Name;row["debtConfirmedAt"]=DateTimeOffset.UtcNow.ToString("O");
                Phases(row,Lines(c,tx,id).Where(l=>l.S("rider")==row.S("rider")),Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("rider")==row.S("rider")));
            }
            if(Rows(cycle["riders"]).OfType<JsonObject>().All(r=>r.S("debtDeductionStatus")=="confirmed")){cycle["stage"]=8;cycle["debtDeductionStatus"]="confirmed";}
            SnapshotDebtAfter(c,tx,cycle);
        }
        cycle["sourceFingerprint"]=Fingerprint(c,tx,cycle);Store.Save(c,Kind,cycle,tx);Store.Audit(c,tx,"pay.debt_"+action,id,actor.Identity!.Name!);return DebtDeductionsView(c,tx,actor,id);
    }
}
