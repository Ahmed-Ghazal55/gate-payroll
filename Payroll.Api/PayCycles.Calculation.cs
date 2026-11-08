using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PayCycles
{
    static (string Type,decimal Days,JsonArray Sources) Vehicle(DbConnection c,DbTransaction tx,string rider,string period,string category,JsonObject values)
    {
        string type=category.EndsWith("_company_car",StringComparison.Ordinal)?"company_car":category.EndsWith("_company_bike",StringComparison.Ordinal)?"company_bike":"private";var sources=new JsonArray();if(type=="private")return(type,0,sources);
        var intervals=new List<(DateTimeOffset From,DateTimeOffset To)>();var month=Time(period+"-01T00:00:00+03:00");
        foreach(var a in Store.List(c,"vehicle_assignment",tx).Where(a=>a.S("riderId")==rider&&Overlaps(a,period,"startedAt","endedAt")))
        {
            var vehicle=Store.Get(c,a.S("vehicleId"),"vehicle",tx)??throw new ApiError("المركبة المكلف بها غير موجودة.");string actual=vehicle.S("vehicleType") switch{"1" or "Motorcycle"=>"company_bike","2" or "Car"=>"company_car",_=>"unknown"};
            J.Need(actual==type&&vehicle.S("ownershipType") is "1" or "2" or "Owned" or "Leased","نوع أو ملكية المركبة لا يطابق فئة الإيجار؛ راجع المصدر.");var from=Time(a.S("startedAt"));var to=a.S("endedAt")==""?month.AddMonths(1):Time(a.S("endedAt"));intervals.Add((from,to));sources.Add(a.S("id"));
        }
        J.Need(intervals.Count>0,"حيازة مركبة الشركة غير مثبتة بالفترة.");var merged=new List<(DateTimeOffset From,DateTimeOffset To)>();foreach(var i in intervals.OrderBy(i=>i.From)){if(merged.Count>0&&i.From<=merged[^1].To){var last=merged[^1];merged[^1]=(last.From,i.To>last.To?i.To:last.To);}else merged.Add(i);}
        decimal days=Math.Min(PayEvaluator.DaysInPayrollMonth(period,values),merged.Sum(i=>PayEvaluator.AssignmentDays(i.From,i.To,period,values)));return(type,days,sources);
    }
    static void BuildLines(DbConnection c,DbTransaction tx,JsonObject cycle,ClaimsPrincipal actor)
    {
        ValidateRiders(c,tx,cycle,actor);string period=cycle.S("period");var global=PaySettings.Global(c,tx,period+"-01");var values=global["data"]!["values"]!.AsObject();var facts=new List<JsonObject>();
        foreach(var (app,a) in Accounts(cycle))
        {
            var shares=Rows(a["riders"]).OfType<JsonObject>().ToList();var split=PayEvaluator.SplitIncentives(shares.Select(r=>new PayRiderShare(r.S("rider"),(int)r.N("orders"),r.N("baseGross"))),a.N("baseGross"),a.N("incentives"),values);
            foreach(var share in split){var f=shares.Single(r=>r.S("rider")==share.Rider).Clone();f["app"]=app;f["accountId"]=a.S("accountId");f["sourceLineId"]=a.S("sourceLineId");f["incentive"]=share.Incentive;facts.Add(f);}
        }
        foreach(var group in facts.GroupBy(f=>(App:f.S("app"),Rider:f.S("rider"))))
        {
            var rows=group.ToList();J.Need(rows.Select(r=>r.S("category")).Distinct().Count()==1&&rows.Select(r=>r.N("days")).Distinct().Count()==1,"حدد نفس فئة المندوب وأيامه الفعلية على التطبيق؛ لا تجمع الأيام لكل يوزر.");
            J.Need(rows.Select(r=>FinancialImports.Canonical(r["metrics"]??new JsonObject())).Distinct().Count()==1,"مقاييس أهلية المندوب يجب أن تخص التطبيق كله دون تضارب.");string rider=group.Key.Rider,app=group.Key.App,category=rows[0].S("category");
            J.Need(!Store.List(c,"run",tx).Any(r=>r.S("iqama")==rider&&r.S("period")==period),"المندوب لديه مسير قديم لنفس الشهر؛ يلزم تسوية معتمدة قبل المسار الجديد.");
            var assignment=PayAssignments.Resolve(c,tx,rider,app,period);var scheme=Store.Get(c,assignment.S("schemeId"),"pay_scheme",tx)!;string rulesId=scheme["data"].S("eligibilitySetId");var rules=rulesId==""?null:Store.Get(c,rulesId,"pay_eligibility",tx);
            var vehicle=Vehicle(c,tx,rider,period,category,values);var metrics=(rows[0]["metrics"] as JsonObject??new()).ToDictionary(p=>p.Key,p=>(decimal?)PayValues.Number(rows[0]["metrics"]!.AsObject(),p.Key,-1000000000));
            var result=PayEvaluator.Evaluate(new(rider,app,period,category,rows.Sum(r=>(int)r.N("orders")),rows.Sum(r=>r.N("baseGross")),rows.Sum(r=>r.N("incentive")),rows[0].N("days"),vehicle.Type,vehicle.Days,metrics),scheme,rules,global);
            var doc=Json(result);doc["id"]="pay-line:"+Guid.NewGuid().ToString("N");doc["cycleId"]=cycle.S("id");doc["companyId"]=cycle.S("companyId");doc["schemeId"]=scheme.S("id");doc["schemeName"]=scheme["data"].S("name");doc["globalSettingId"]=global.S("id");doc["assignmentId"]=assignment.S("id");doc["termsId"]=PaySettings.Resolve(c,tx,"pay_terms",app,period).S("id");doc["sources"]=new JsonArray(rows.Select(r=>(JsonNode?)r.Clone()).ToArray());doc["vehicleSources"]=vehicle.Sources;doc["accountDeduction"]=rows.Sum(r=>r.N("deduction"));
            doc["vehicleType"]=vehicle.Type;Store.Save(c,LineKind,doc,tx);
        }
        if(Lines(c,tx,cycle.S("id")).Any(l=>l.S("status")!="ready"))cycle["status"]="needs_decision";
    }
    static void BuildDeductions(DbConnection c,DbTransaction tx,JsonObject cycle,ClaimsPrincipal actor)
    {
        var deductions=new JsonArray();var debtBefore=new JsonArray();string period=cycle.S("period"),company=cycle.S("companyId");var lines=Lines(c,tx,cycle.S("id"));
        foreach(var group in lines.GroupBy(l=>l.S("rider")))
        {
            J.Need(group.Select(l=>l.S("category")).Distinct().Count()==1,"فئة المندوب بين التطبيقات متضاربة؛ راجع التصنيف.");var charged=group.Where(l=>l.N("vehicleCharge")>0).OrderBy(l=>l.S("app"),StringComparer.Ordinal).ToList();decimal rent=charged.Count==0?0:charged[0].N("vehicleCharge");J.Need(charged.All(l=>l.N("vehicleCharge")==rent),"فترات إيجار المركبة متضاربة.");
            foreach(var l in charged){l["allocatedVehicleCharge"]=l.S("id")==charged[0].S("id")?rent:0;Store.Save(c,LineKind,l,tx);}
            decimal available=group.Sum(l=>l.N("entitlement")-l.N("accountDeduction"))-rent;string rider=group.Key;var tags=PlatformDeductions.Categories(Store.List(c,PlatformDeductions.Kind,tx),rider,period);
            foreach(var a in Store.List(c,"adjustment",tx).Where(a=>a.S("iqama")==rider&&a.S("period")==period&&(a.N("amount")>=0||!PlatformDeductions.Skip(tags,a))))
            {
                string account=cycle["adjustmentAccounts"].S(a.S("id"));J.Need(account!="","كل تعديل يحتاج حسابًا محددًا على الشجرة الجديدة.");PayPlatforms.Active(account);J.Need(account.StartsWith(a.N("amount")>0?'5':'4'),"نوع حساب التعديل لا يطابق الإضافة أو الخصم.");var ph=a.N("amount")>0?(3,90,"تعديل راتب / حافز"):ConfigLists.Phase(c,tx,a.S("category"));deductions.Add(new JsonObject{["rider"]=rider,["category"]=a.S("category"),["amount"]=a.N("amount"),["source"]=a.S("id"),["accountCode"]=account,["type"]="adjustment",["phase"]=ph.Item1,["order"]=ph.Item2,["labelAr"]=ph.Item3});available+=a.N("amount");
            }
            foreach(var debt in Store.List(c,"debt",tx).Where(d=>d.S("iqama")==rider&&d.S("status")=="active"&&!PlatformDeductions.Skip(tags,d)&&string.CompareOrdinal(d.S("effectiveFrom","0000-00"),period)<=0&&string.CompareOrdinal(d.S("lastDeductionPeriod"),period)<0).OrderBy(d=>ConfigLists.Phase(c,tx,d.S("category")).Phase).ThenBy(d=>ConfigLists.Phase(c,tx,d.S("category")).Order).ThenBy(d=>d.S("createdAt")).ThenBy(d=>d.S("id")))
            {
                decimal amount=J.Money(Math.Min(Math.Max(0,available),Math.Min(debt.N("remainingBalance"),debt.N("monthlyInstallment"))));if(amount<=0)continue;FinancialScopes.DemandDebt(actor,debt);J.Need(FinancialCore.RequireDebtSponsor(debt).Id==company,"قسط على سجل آخر يحتاج مقاصة مستقلة.");string account=PayPosting.DebtAccount(debt);var dp=ConfigLists.Phase(c,tx,debt.S("category"));deductions.Add(new JsonObject{["rider"]=rider,["category"]="debt_installment",["debtCategory"]=debt.S("category"),["amount"]=-amount,["computed"]=-amount,["source"]=debt.S("id"),["accountCode"]=account,["type"]="debt",["phase"]=dp.Phase,["order"]=dp.Order,["labelAr"]=dp.LabelAr});debtBefore.Add(debt.Clone());available-=amount;
            }
        }
        cycle["deductions"]=deductions;cycle["debtBefore"]=debtBefore;
    }
    // BR §29: gross → commission → phase 1 → «المستحق الأولي» → phase 2 → phase 3 → net. Deductions are negative.
    static void Phases(JsonObject summary,IEnumerable<JsonObject> lines,IEnumerable<JsonObject> deductions)
    {
        var ls=lines.ToList();var ds=deductions.ToList();decimal Sum(int phase)=>ds.Where(d=>(int)d.N("phase",3)==phase).Sum(d=>d.N("amount"));
        decimal gross=ls.Sum(l=>l.N("gross")),after=ls.Sum(l=>l.N("entitlement"));
        decimal p1=Sum(1)-ls.Sum(l=>l.N("accountDeduction")+l.N("allocatedVehicleCharge")),p2=Sum(2),p3=Sum(3);
        summary["gross"]=J.Money(gross);summary["commission"]=J.Money(gross-after);summary["afterCommission"]=J.Money(after);
        summary["phase1"]=J.Money(p1);summary["preliminaryDue"]=J.Money(after+p1);summary["phase2"]=J.Money(p2);summary["phase3"]=J.Money(p3);
    }
    static void Merge(DbConnection c,DbTransaction tx,JsonObject cycle)
    {
        var summaries=new JsonArray();var lines=Lines(c,tx,cycle.S("id"));foreach(var group in lines.GroupBy(l=>l.S("rider")))
        {
            string rider=group.Key,period=cycle.S("period");J.Need(!Store.List(c,Kind,tx).Any(d=>d.S("id")!=cycle.S("id")&&d.S("status")=="approved"&&Lines(c,tx,d.S("id")).Any(l=>l.S("rider")==rider&&l.S("period")==period)),"المندوب معتمد بالفعل في دورة أخرى للشهر؛ لا تكرر الاستقطاع.");
            decimal net=group.Sum(l=>l.N("entitlement")-l.N("accountDeduction")-l.N("allocatedVehicleCharge"))+Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("rider")==rider).Sum(d=>d.N("amount"));
            decimal carried=Store.List(c,"journal",tx).Where(FinanceViews.Posted).Where(j=>string.CompareOrdinal(j.S("period"),cycle.S("postingPeriod",period))<=0).SelectMany(j=>(j["lines"] as JsonArray??[]).OfType<JsonObject>()).Where(l=>l.S("sponsorId")==cycle.S("companyId")&&l.S("iqama")==rider&&l.S("accountCode") is "2110" or "212103").Sum(l=>l.N("credit")-l.N("debit"));
            bool deficit=Store.List(c,"journal",tx).Where(FinanceViews.Posted).SelectMany(j=>(j["lines"] as JsonArray??[]).OfType<JsonObject>()).Where(l=>l.S("sponsorId")==cycle.S("companyId")&&l.S("iqama")==rider&&l.S("accountCode")=="1211").Sum(l=>l.N("debit")-l.N("credit"))>0;
            bool blocked=net<0||deficit||group.Any(l=>l.S("status")!="ready");var summary=new JsonObject{["rider"]=rider,["net"]=J.Money(net),["carried"]=J.Money(carried),["closing"]=J.Money(carried+net),["status"]=blocked?"needs_decision":"ready"};
            Phases(summary,group,Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("rider")==rider));summaries.Add(summary);
        }
        cycle["riders"]=summaries;if(summaries.Count==0||summaries.OfType<JsonObject>().Any(r=>r.S("status")!="ready"))cycle["status"]="needs_decision";
    }
    static void Approve(DbConnection c,DbTransaction tx,JsonObject cycle,ClaimsPrincipal actor)
    {
        AccessPolicy.ValidateApproval(actor,new(){["requestedBy"]=cycle.S("createdBy"),["targetDepartment"]="FIN",["requiredRankLevel"]=1},new JsonObject(),"pay.approve");J.Need(!Rows(cycle["history"]).OfType<JsonObject>().Any(h=>h.S("advancedBy").Equals(actor.Identity!.Name,StringComparison.OrdinalIgnoreCase)),"المعتمد مستقل عن كل من جهز أو راجع مراحل الدورة.");
        J.Need(Rows(cycle["riders"]).Count>0&&Rows(cycle["riders"]).OfType<JsonObject>().All(r=>r.S("status")=="ready"),"توجد مبالغ تحتاج قرارًا؛ لا اعتماد ولا إنشاء مديونية تلقائية.");
        J.Need(Rows(cycle["riders"]).OfType<JsonObject>().All(r=>r.S("review")=="approved"),"راجع واعتمد كل المناديب في جدول المراجعة أولًا (اعتماد واحد أو اعتماد الكل).");
        foreach(var (app,_) in Accounts(cycle))PlatformPosting.Demand(c,app,tx);
        cycle["approvedBy"]=actor.Identity!.Name;cycle["approvedAt"]=DateTimeOffset.UtcNow.ToString("O");Store.Save(c,Kind,cycle,tx);
        var journal=PostEarnings(c,tx,cycle,actor);cycle["journalEntryId"]=journal.S("id");Store.Save(c,Kind,cycle,tx);
        foreach(var rider in Rows(cycle["riders"]).OfType<JsonObject>())
        {
            string id=rider.S("rider");var payLines=Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("rider")==id&&d.S("type")=="debt"&&d.N("amount")!=0).Select(d=>new PayLine("debt_installment","قسط وفق الاتفاق",d.N("amount"),d.S("source"))).ToList();
            Debts.Post(c,tx,new(id,cycle.S("period"),"pay_cycle",0,rider.N("net"),rider.N("net"),rider.N("net"),0,0,payLines,[]),cycle.S("id"),payCycle:true);
        }
        foreach(var review in Rows(cycle["invoices"]).OfType<JsonObject>())
        {
            var invoice=Store.Get(c,review.S("invoiceId"),"platform_invoice",tx)!;
            if(invoice.S("approvalStatus")=="cycle_pending"){invoice["approvalStatus"]="approved";invoice["approvedBy"]=actor.Identity!.Name;invoice["approvedAt"]=cycle.S("approvedAt");}
            invoice["payCycleId"]=cycle.S("id");invoice["journalEntryId"]=journal.S("id");invoice["accountingStatus"]="posted";Store.Save(c,"platform_invoice",invoice,tx);
        }
        cycle["debtAfter"]=new JsonArray(Rows(cycle["debtBefore"]).OfType<JsonObject>().Select(d=>(JsonNode?)Store.Get(c,d.S("id"),"debt",tx)!.Clone()).ToArray());
    }
    static JsonObject PostEarnings(DbConnection c,DbTransaction tx,JsonObject cycle,ClaimsPrincipal actor)
    {
        var journalLines=new JsonArray();string company=cycle.S("companyId"),period=cycle.S("period");var global=PaySettings.Global(c,tx,period+"-01");var accounts=global["data"]!["values"]!["accounts"]!;
        void Add(string code,decimal debit,decimal credit,string app,string rider,string source,string account=""){if(debit==0&&credit==0)return;journalLines.Add(PayPosting.Line(code,company,J.Money(debit),J.Money(credit),app,rider,period,source,account));}
        foreach(var line in Lines(c,tx,cycle.S("id")))
        {
            string app=line.S("app"),rider=line.S("rider"),id=line.S("id");decimal total=line.N("platformGross")+line.N("incentives"),earned=line.N("gross");string recv=PayPlatforms.For(app).Receivable;
            foreach(var source in Rows(line["sources"]).OfType<JsonObject>())Add(recv,source.N("baseGross")+source.N("incentive"),0,app,rider,source.S("sourceLineId"),source.S("accountId"));
            if(line.S("accountingRole")=="agent"){Add("212103",0,earned,app,rider,id);Add(PayPlatforms.Revenue(app,"agent"),0,total-earned,app,rider,id);}else{Add(PayPlatforms.Revenue(app,"principal"),0,total,app,rider,id);Add(accounts.S("salaryExpense"),earned,0,app,rider,id);Add("212103",0,earned,app,rider,id);}
            decimal rent=line.N("allocatedVehicleCharge"),cost=line.N("operatingCost"),deduction=line.N("accountDeduction");
            Add("212103",cost,0,app,rider,id);Add(line.S("operatingCostAccount"),0,cost,app,rider,id);
            string vehicleIncome=accounts.S(line.S("vehicleType")=="company_car"?"vehicleIncomeCar":"vehicleIncomeBike");Add("212103",rent,0,app,rider,id);Add(vehicleIncome,0,rent,app,rider,id);
            foreach(var source in Rows(line["sources"]).OfType<JsonObject>()){Add("212103",source.N("deduction"),0,app,rider,source.S("sourceLineId"),source.S("accountId"));Add(recv,0,source.N("deduction"),app,rider,source.S("sourceLineId"),source.S("accountId"));}
        }
        foreach(var invoice in Rows(cycle["invoices"]).OfType<JsonObject>())
        {
            string app=invoice.S("app"),id=invoice.S("invoiceId"),recv=PayPlatforms.For(app).Receivable;var source=Store.Get(c,id,"platform_invoice",tx)!;Add(recv,source.N("vat"),0,app,"",id);Add(accounts.S("outputVat"),0,source.N("vat"),app,"",id);
            foreach(var d in Rows(invoice["deductions"]).OfType<JsonObject>().Where(d=>d.S("level")=="company_level")){Add(d.S("expenseAccount"),d.N("amount"),0,app,"",d.S("sourceLineId"));Add(recv,0,d.N("amount"),app,"",d.S("sourceLineId"));}
        }
        foreach(var d in Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("type") is "adjustment" or "review_adjustment")){decimal amount=d.N("amount");Add(d.S("accountCode"),amount>0?amount:0,amount<0?-amount:0,"",d.S("rider"),d.S("source"));Add("212103",amount<0?-amount:0,amount>0?amount:0,"",d.S("rider"),d.S("source"));}
        J.Need(journalLines.Count>0,"لا توجد مبالغ للترحيل.");return PayPosting.Post(c,tx,cycle,journalLines,actor.Identity!.Name!);
    }
}
