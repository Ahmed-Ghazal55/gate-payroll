using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

// §40.1: each invoice deduction row has bearer allocations (company | rider+). Sum = row amount (halala).
public static class InvoiceDeductionClassification
{
    public const string NeedsReview="needs_review";

    public static JsonArray AllocationsOf(JsonObject deduction)
        =>deduction["allocations"] as JsonArray??[];

    public static void DemandSum(JsonObject deduction)
    {
        decimal amount=J.Money(deduction.N("amount"));
        var parts=AllocationsOf(deduction).OfType<JsonObject>().ToList();
        J.Need(parts.Count>0,"حدد متحمّل الخصم.");
        decimal sum=0;
        foreach(var a in parts)
        {
            Money(a,"amount");
            string bearer=a.Required("bearer");
            J.Need(bearer is "company" or "rider","المتحمّل شركة أو مندوب فقط.");
            if(bearer=="company")
            {
                string expense=PayPlatforms.Active(a.Required("expenseAccount"));
                J.Need(expense.StartsWith('5'),"خصم الشركة يحتاج حساب تكلفة.");
                a["expenseAccount"]=expense;a.Remove("riderId");
            }
            else
            {
                a["riderId"]=J.Iqama(a.Required("riderId"));
                a.Remove("expenseAccount");
            }
            sum+=a.N("amount");
        }
        if(sum!=amount)throw new ApiError("مجموع التوزيع لا يطابق مبلغ الصف.",400);
    }

    /// <summary>Legacy level → allocations. Multi-rider account_level → needs_review (empty allocations + flag).</summary>
    public static JsonObject MigrateLevel(JsonObject deduction,string? singleRiderId=null,bool multiRiderAccount=false)
    {
        if(AllocationsOf(deduction).Count>0){DemandSum(deduction);return deduction;}
        string level=deduction.S("level");
        decimal amount=J.Money(deduction.N("amount"));
        var alloc=new JsonArray();
        if(level=="company_level")
            alloc.Add(new JsonObject{["bearer"]="company",["amount"]=amount,["expenseAccount"]=deduction.S("expenseAccount")});
        else if(level=="rider_level")
            alloc.Add(new JsonObject{["bearer"]="rider",["riderId"]=deduction.S("riderId"),["amount"]=amount});
        else if(level=="account_level")
        {
            string rider=singleRiderId??deduction.S("riderId");
            if(multiRiderAccount||rider==""){deduction["migrationStatus"]=NeedsReview;deduction["allocations"]=new JsonArray();return deduction;}
            alloc.Add(new JsonObject{["bearer"]="rider",["riderId"]=rider,["amount"]=amount});
        }
        else throw new ApiError("صنف الخصم أولًا.");
        deduction["allocations"]=alloc;
        deduction.Remove("migrationStatus");
        DemandSum(deduction);
        return deduction;
    }

    /// <summary>Default proposal (§40.1/§40.2): single operator → rider; dated → assignee that day; else weighted split; company choice stays company.</summary>
    public static JsonArray Propose(IReadOnlyList<(string Rider,decimal Orders)> operators,decimal amount,string? date="",string? assigneeOnDate="",bool companyBearer=false,string expenseAccount="",string weight="orders",string tieBreak="days")
        =>Propose(operators.Select(o=>new PayReviewCategory.OperatorMetrics(o.Rider,o.Orders)).ToList(),amount,date,assigneeOnDate,companyBearer,expenseAccount,weight,tieBreak);

    public static JsonArray Propose(IReadOnlyList<PayReviewCategory.OperatorMetrics> operators,decimal amount,string? date="",string? assigneeOnDate="",bool companyBearer=false,string expenseAccount="",string weight="orders",string tieBreak="days")
    {
        amount=J.Money(amount);
        if(companyBearer)
            return [new JsonObject{["bearer"]="company",["amount"]=amount,["expenseAccount"]=expenseAccount}];
        if(!string.IsNullOrEmpty(assigneeOnDate))
            return [new JsonObject{["bearer"]="rider",["riderId"]=J.Iqama(assigneeOnDate),["amount"]=amount}];
        if(operators.Count==1)
            return [new JsonObject{["bearer"]="rider",["riderId"]=operators[0].Rider,["amount"]=amount}];
        if(operators.Count==0)return [new JsonObject{["bearer"]="company",["amount"]=amount,["expenseAccount"]=expenseAccount}];
        long units=(long)Math.Round(amount*100m,MidpointRounding.AwayFromZero);
        var whole=PayReviewCategory.SplitUnits(operators,units,weight,tieBreak);
        var result=new JsonArray();
        for(int i=0;i<operators.Count;i++)
            result.Add(new JsonObject{["bearer"]="rider",["riderId"]=operators[i].Rider,["amount"]=J.Money(whole[i]/100m)});
        return result;
    }

    public static void Apply(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject invoice,JsonObject line,JsonObject input)
    {
        var deduction=(line["deductions"] as JsonArray??[]).OfType<JsonObject>().SingleOrDefault(d=>d.S("category")==input.Required("category"))??throw new ApiError("بند الخصم غير موجود.");
        deduction.Remove("expenseAccount");deduction.Remove("riderId");deduction.Remove("migrationStatus");
        if(input["allocations"] is JsonArray allocInput && allocInput.Count>0)
        {
            deduction["allocations"]=allocInput.DeepClone();
            DemandSum(deduction);
            // Keep a derived legacy level for older readers during transition.
            deduction["level"]=DerivedLevel(deduction);
            SyncLegacyFields(deduction);
        }
        else
        {
            // Legacy classify body → migrate to allocations.
            string level=input.Required("level");
            J.Need(level is "company_level" or "rider_level","حدد الشركة أو المندوب؛ مستوى اليوزر أُلغي — استخدم التوزيع.");
            if(level=="company_level")
            {
                string expense=PayPlatforms.Active(input.Required("expenseAccount"));
                J.Need(expense.StartsWith('5'),"خصم الشركة يحتاج حساب تكلفة.");
                deduction["allocations"]=new JsonArray(new JsonObject{["bearer"]="company",["amount"]=J.Money(deduction.N("amount")),["expenseAccount"]=expense});
            }
            else
            {
                string rider=J.Iqama(input.Required("riderId"));
                DemandWorkedAccount(c,tx,actor,invoice,line,rider);
                deduction["allocations"]=new JsonArray(new JsonObject{["bearer"]="rider",["riderId"]=rider,["amount"]=J.Money(deduction.N("amount"))});
            }
            DemandSum(deduction);
            deduction["level"]=level;
            SyncLegacyFields(deduction);
        }
        foreach(var a in AllocationsOf(deduction).OfType<JsonObject>().Where(x=>x.S("bearer")=="rider"))
            DemandWorkedAccount(c,tx,actor,invoice,line,a.S("riderId"));
        deduction["classifiedBy"]=actor.Identity!.Name;
    }

    static string DerivedLevel(JsonObject deduction)
    {
        var parts=AllocationsOf(deduction).OfType<JsonObject>().ToList();
        if(parts.All(p=>p.S("bearer")=="company"))return "company_level";
        if(parts.Count==1&&parts[0].S("bearer")=="rider")return "rider_level";
        return "rider_level"; // split = multiple rider/company rows expanded at cycle time
    }

    static void SyncLegacyFields(JsonObject deduction)
    {
        var parts=AllocationsOf(deduction).OfType<JsonObject>().ToList();
        if(parts.Count==1&&parts[0].S("bearer")=="company"){deduction["expenseAccount"]=parts[0].S("expenseAccount");deduction.Remove("riderId");}
        else if(parts.Count==1&&parts[0].S("bearer")=="rider"){deduction["riderId"]=parts[0].S("riderId");deduction.Remove("expenseAccount");}
        else{deduction.Remove("expenseAccount");deduction.Remove("riderId");}
    }

    static void DemandWorkedAccount(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject invoice,JsonObject line,string rider)
    {
        rider=J.Iqama(rider);var person=Store.Get(c,rider,"rider",tx)??throw new ApiError("المندوب غير موجود.",404);FinancialScopes.DemandRider(actor,person);
        string app=PayPlatforms.For(invoice.S("platform")).App,period=invoice.S("period");var month=DateTimeOffset.Parse(period+"-01T00:00:00+03:00",CultureInfo.InvariantCulture);
        string accountId=line.S("platformAccountId");
        J.Need(ErpRiderClassification.ListedAsRider(person)&&Store.List(c,"platform_account",tx).Any(a=>a.S("id")==accountId&&a.S("companyId")==invoice.S("companyId")&&PayPlatforms.For(a.S("platformCode")).App==app&&(a["assignments"] as JsonArray??[]).OfType<JsonObject>().Any(x=>x.S("riderId")==rider&&AssignmentSpan.Overlaps(x,month,month.AddMonths(1)))),"المندوب ليس مشغلًا فعليًا لليوزر في السجل والشهر.");
    }

    static void Money(JsonObject row,string key){decimal n=row.N(key);J.Need(n==J.Money(n)&&n>=0,"المبلغ بدقة هللتين.");}

    /// <summary>Expand allocations for cycle BuildDeductions / posting.</summary>
    public static IEnumerable<JsonObject> ExpandForCycle(JsonObject invoiceDeduction,string app,string receivable)
    {
        EnsureMigrated(invoiceDeduction);
        foreach(var a in AllocationsOf(invoiceDeduction).OfType<JsonObject>())
        {
            if(a.S("bearer")=="rider")
                yield return new JsonObject{["rider"]=a.S("riderId"),["category"]=invoiceDeduction.S("sourceCategory",invoiceDeduction.S("category")),["source"]=invoiceDeduction.S("sourceLineId"),["amount"]=-a.N("amount"),["accountCode"]=receivable,["app"]=app,["type"]="platform_rider",["phase"]=1,["order"]=5,["labelAr"]="خصم فاتورة على المندوب"};
        }
    }

    public static IEnumerable<JsonObject> CompanyAllocations(JsonObject invoiceDeduction)
    {
        EnsureMigrated(invoiceDeduction);
        foreach(var a in AllocationsOf(invoiceDeduction).OfType<JsonObject>().Where(x=>x.S("bearer")=="company"))
            yield return a;
    }

    public static void EnsureMigrated(JsonObject deduction)
    {
        if(AllocationsOf(deduction).Count>0){if(deduction.S("migrationStatus")!=NeedsReview)DemandSum(deduction);return;}
        if(deduction.S("level")!="")MigrateLevel(deduction);
    }

    public static bool Classified(JsonObject deduction)
        =>AllocationsOf(deduction).Count>0||deduction.S("level") is "company_level" or "account_level" or "rider_level";

    public static bool AllCompany(JsonObject deduction)
    {
        if(AllocationsOf(deduction).Count>0)return AllocationsOf(deduction).OfType<JsonObject>().All(a=>a.S("bearer")=="company");
        return deduction.S("level")=="company_level";
    }

    /// <summary>Counts/amounts only for multi-rider account_level leftovers (no identities).</summary>
    public static JsonObject MultiRiderReport(DbConnection c,DbTransaction? tx,string invoiceId)
    {
        var lines=Store.List(c,"platform_invoice_line",tx).Where(l=>l.S("invoiceId")==invoiceId).ToList();
        int rows=0,multi=0;decimal amount=0,multiAmount=0;
        foreach(var line in lines)
        {
            string accountId=line.S("platformAccountId");
            var account=accountId==""?null:Store.Get(c,accountId,"platform_account",tx);
            int operators=account==null?0:(account["assignments"] as JsonArray??[]).OfType<JsonObject>().Select(a=>a.S("riderId")).Where(id=>id!="").Distinct(StringComparer.Ordinal).Count();
            foreach(var d in (line["deductions"] as JsonArray??[]).OfType<JsonObject>())
            {
                rows++;amount+=d.N("amount");
                bool needs=d.S("migrationStatus")==NeedsReview||(d.S("level")=="account_level"&&AllocationsOf(d).Count==0&&operators>1);
                if(needs){multi++;multiAmount+=d.N("amount");}
            }
        }
        return new JsonObject{["invoiceId"]=invoiceId,["deductionRows"]=rows,["deductionAmount"]=J.Money(amount),["multiRiderRows"]=multi,["multiRiderAmount"]=J.Money(multiAmount)};
    }
}
