using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PayCycles
{
    static string FuelMonth(JsonObject fuel)
    {
        string m=fuel.S("month",fuel.S("period",fuel.S("ReportMonth")));
        return m.Length>=7?m[..7]:m;
    }
    static (string Type,decimal Days,JsonArray Sources) Vehicle(DbConnection c,DbTransaction tx,JsonObject cycle,string rider,string period,string category,JsonObject values)
    {
        string type=category.EndsWith("_company_car",StringComparison.Ordinal)?"company_car":category.EndsWith("_company_bike",StringComparison.Ordinal)?"company_bike":"private";var sources=new JsonArray();if(type=="private")return(type,0,sources);
        var intervals=new List<(DateTimeOffset From,DateTimeOffset To)>();var month=Time(period+"-01T00:00:00+03:00");
        foreach(var a in CycleOps(c,tx,cycle,"vehicle_assignment").Where(a=>a.S("riderId")==rider&&Overlaps(a,period,"startedAt","endedAt")))
        {
            var vehicle=CycleOpsGet(c,tx,cycle,"vehicle",a.S("vehicleId"))??throw new ApiError("المركبة المكلف بها غير موجودة.");string actual=vehicle.S("vehicleType") switch{"1" or "Motorcycle"=>"company_bike","2" or "Car"=>"company_car",_=>"unknown"};
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
            var vehicle=Vehicle(c,tx,cycle,rider,period,category,values);var metrics=(rows[0]["metrics"] as JsonObject??new()).ToDictionary(p=>p.Key,p=>(decimal?)PayValues.Number(rows[0]["metrics"]!.AsObject(),p.Key,-1000000000));
            var chefzFees=rows.Where(r=>r.ContainsKey("chefzFullTimeCommission")).Select(r=>r.N("chefzFullTimeCommission")).Distinct().ToList();J.Need(chefzFees.Count<=1,"عمولة دوام شيفز متضاربة بين حسابات المندوب.");decimal? chefzFee=chefzFees.Count==1?chefzFees[0]:null;
            decimal penalties=rows.Sum(r=>r.N("deduction"));
            var result=PayEvaluator.Evaluate(new(rider,app,period,category,rows.Sum(r=>(int)r.N("orders")),rows.Sum(r=>r.N("baseGross")),rows.Sum(r=>r.N("incentive")),rows[0].N("days"),vehicle.Type,vehicle.Days,metrics,chefzFee,penalties),scheme,rules,global);
            var doc=Json(result);doc["id"]="pay-line:"+Guid.NewGuid().ToString("N");doc["cycleId"]=cycle.S("id");doc["companyId"]=cycle.S("companyId");doc["schemeId"]=scheme.S("id");doc["schemeName"]=scheme["data"].S("name");doc["globalSettingId"]=global.S("id");doc["assignmentId"]=assignment.S("id");doc["termsId"]=PaySettings.Resolve(c,tx,"pay_terms",app,period).S("id");doc["sources"]=new JsonArray(rows.Select(r=>(JsonNode?)r.Clone()).ToArray());doc["vehicleSources"]=vehicle.Sources;
            // §40.3 percent: penalties already in commission base — do not subtract again at Merge or in PostEarnings source.deduction.
            doc["accountDeduction"]=result.PenaltiesInCommissionBase?0:penalties;doc["platformPenalties"]=penalties;doc["penaltiesInCommissionBase"]=result.PenaltiesInCommissionBase;
            doc["vehicleType"]=vehicle.Type;Store.Save(c,LineKind,doc,tx);
        }
        if(Lines(c,tx,cycle.S("id")).Any(l=>l.S("status")!="ready"))cycle["status"]="needs_decision";
    }
    static string? VehicleIdOf(DbConnection c,DbTransaction tx,JsonObject cycle,JsonObject line)
    {
        if(line["vehicleSources"] is not JsonArray sources)return null;
        foreach(var node in sources)
        {
            string id=node switch{JsonValue v when v.TryGetValue<string>(out var s)=>s,_=>""};if(id=="")continue;
            var assignment=CycleOpsGet(c,tx,cycle,"vehicle_assignment",id);if(assignment!=null)return assignment.S("vehicleId");
        }
        return null;
    }
    /// <summary>BR §38.4: after per-rider once-across-apps allocation, scale shared vehicles so total ≤ type monthly rent.</summary>
    static void CapSharedVehicleRent(DbConnection c,DbTransaction tx,JsonObject cycle,IReadOnlyList<JsonObject> lines,JsonObject values)
    {
        foreach(var group in lines.Where(l=>l.N("allocatedVehicleCharge")>0).GroupBy(l=>VehicleIdOf(c,tx,cycle,l)??"").Where(g=>g.Key!=""))
        {
            var rows=group.OrderBy(l=>l.S("rider"),StringComparer.Ordinal).ThenBy(l=>l.S("app"),StringComparer.Ordinal).ToList();
            string type=rows[0].S("vehicleType");decimal total=rows.Sum(l=>l.N("allocatedVehicleCharge")),cap=PayEvaluator.MonthlyVehicleRent(type,values);
            if(total<=cap)continue;decimal kept=0;
            for(int i=0;i<rows.Count;i++)
            {
                decimal next=i==rows.Count-1?PayEvaluator.CapVehicleRentTotal(cap-kept,type,values):PayEvaluator.Round(rows[i].N("allocatedVehicleCharge")*(cap/total),values);
                next=PayEvaluator.CapVehicleRentTotal(next,type,values);if(kept+next>cap)next=cap-kept;rows[i]["allocatedVehicleCharge"]=next;kept+=next;Store.Save(c,LineKind,rows[i],tx);
            }
        }
    }
    static int HousingDaysForRider(DbConnection c,DbTransaction tx,JsonObject cycle,string rider,string period,JsonObject values)
    {
        var dates=new HashSet<DateOnly>();
        foreach(var r in CycleOps(c,tx,cycle,"housing_residence").Where(r=>r.S("riderId")==rider&&Overlaps(r,period,"effectiveFrom","effectiveTo")))
            dates.UnionWith(PayEvaluator.ResidenceDates(r.S("effectiveFrom"),r.S("effectiveTo"),period));
        return Math.Min(dates.Count,PayEvaluator.DaysInPayrollMonth(period,values));
    }
    static void BuildDeductions(DbConnection c,DbTransaction tx,JsonObject cycle,ClaimsPrincipal actor)
    {
        var deductions=new JsonArray();var debtBefore=new JsonArray();string period=cycle.S("period"),company=cycle.S("companyId");var lines=Lines(c,tx,cycle.S("id"));
        var global=PaySettings.Global(c,tx,period+"-01");var values=global["data"]!["values"]!.AsObject();
        foreach(var group in lines.GroupBy(l=>l.S("rider")))
        {
            J.Need(group.Select(l=>l.S("category")).Distinct().Count()==1,"فئة المندوب بين التطبيقات متضاربة؛ راجع التصنيف.");var charged=group.Where(l=>l.N("vehicleCharge")>0).OrderBy(l=>l.S("app"),StringComparer.Ordinal).ToList();decimal rent=charged.Count==0?0:charged[0].N("vehicleCharge");J.Need(charged.All(l=>l.N("vehicleCharge")==rent),"فترات إيجار المركبة متضاربة.");
            foreach(var l in charged){l["allocatedVehicleCharge"]=l.S("id")==charged[0].S("id")?rent:0;Store.Save(c,LineKind,l,tx);}
        }
        CapSharedVehicleRent(c,tx,cycle,lines,values);lines=Lines(c,tx,cycle.S("id"));
        foreach(var group in lines.GroupBy(l=>l.S("rider")))
        {
            // §40.3 salary (non-§22.9.2): operational items are company expenses, not rider deductions.
            bool salaryExpense=group.All(l=>l.B("salaryOperationalExpense")||(l.S("schemeType")=="target_salary"&&!l.B("ineligibleKeetaSalary")));
            decimal rent=group.Sum(l=>l.N("allocatedVehicleCharge"));
            if(salaryExpense&&rent>0)
            {
                deductions.Add(new JsonObject{["rider"]=group.Key,["category"]="VehicleExpense",["amount"]=0,["companyCost"]=rent,["source"]="vehicle-expense:"+group.Key+":"+period,["accountCode"]=PayExpenseAccounts.SalaryVehicle,["type"]="company_expense",["phase"]=1,["order"]=40,["labelAr"]="مصروف مركبة لمندوب الراتب",["affectsNet"]=false});
                foreach(var l in group){l["allocatedVehicleCharge"]=0;Store.Save(c,LineKind,l,tx);}
                rent=0;
            }
            decimal available=group.Sum(l=>l.N("entitlement")-l.N("accountDeduction"))-rent;string rider=group.Key;var tags=PlatformDeductions.Categories(Store.List(c,PlatformDeductions.Kind,tx),rider,period);
            int residenceDays=HousingDaysForRider(c,tx,cycle,rider,period,values);decimal housing=0;bool housingRateReady=residenceDays==0;
            if(residenceDays>0)
            {
                try{housingRateReady=PayValues.Number(values,"housingMonthly")>0;}catch(ApiError){housingRateReady=false;}
                if(housingRateReady)housing=PayEvaluator.HousingCharge(residenceDays,period,values);
                else
                {
                    var line=group.First();Blockers(line).Add("قيمة السكن الشهرية المعتمدة مفقودة أو صفر رغم وجود أيام إقامة.");line["status"]="needs_decision";Store.Save(c,LineKind,line,tx);cycle["status"]="needs_decision";
                }
            }
            if(housing>0)
            {
                if(salaryExpense)
                    deductions.Add(new JsonObject{["rider"]=rider,["category"]="HousingExpense",["amount"]=0,["companyCost"]=housing,["source"]="housing:"+rider+":"+period,["accountCode"]=PayExpenseAccounts.SalaryHousing,["type"]="company_expense",["phase"]=1,["order"]=50,["labelAr"]="مصروف سكن لمندوب الراتب",["housingDays"]=residenceDays,["affectsNet"]=false});
                else
                {
                    deductions.Add(new JsonObject{["rider"]=rider,["category"]="HousingDeduction",["amount"]=-housing,["source"]="housing:"+rider+":"+period,["accountCode"]="112505",["type"]="housing",["phase"]=1,["order"]=50,["labelAr"]="استقطاع سكن",["housingDays"]=residenceDays});
                    available-=housing;
                }
            }
            foreach(var a in Store.List(c,"adjustment",tx).Where(a=>a.S("iqama")==rider&&a.S("period")==period&&(a.N("amount")>=0||!PlatformDeductions.Skip(tags,a))))
            {
                string account=cycle["adjustmentAccounts"].S(a.S("id"));J.Need(account!="","كل تعديل يحتاج حسابًا محددًا على الشجرة الجديدة.");PayPlatforms.Active(account);J.Need(account.StartsWith(a.N("amount")>0?'5':'4'),"نوع حساب التعديل لا يطابق الإضافة أو الخصم.");var ph=a.N("amount")>0?(3,90,"تعديل راتب / حافز"):ConfigLists.Phase(c,tx,a.S("category"));deductions.Add(new JsonObject{["rider"]=rider,["category"]=a.S("category"),["amount"]=a.N("amount"),["source"]=a.S("id"),["accountCode"]=account,["type"]="adjustment",["phase"]=ph.Item1,["order"]=ph.Item2,["labelAr"]=ph.Item3});available+=a.N("amount");
            }
            // §40.4: this-month items before net; §40.6 stage 8 keeps debts opened before the cycle month only.
            foreach(var debt in Store.List(c,"debt",tx).Where(d=>d.S("iqama")==rider&&d.S("status")=="active"&&d.N("remainingBalance")>0&&!PlatformDeductions.Skip(tags,d)&&string.CompareOrdinal(d.S("effectiveFrom","0000-00"),period)<=0&&string.CompareOrdinal(d.S("lastDeductionPeriod"),period)<0&&!(residenceDays>0&&d.S("category")=="HousingDeduction")).OrderBy(d=>ConfigLists.Phase(c,tx,d.S("category")).Phase).ThenBy(d=>ConfigLists.Phase(c,tx,d.S("category")).Order).ThenBy(d=>d.S("createdAt")).ThenBy(d=>d.S("id")))
            {
                FinancialScopes.DemandDebt(actor,debt);J.Need(FinancialCore.RequireDebtSponsor(debt).Id==company,"قسط على سجل آخر يحتاج مقاصة مستقلة.");string account=PayPosting.DebtAccount(debt);var dp=ConfigLists.Phase(c,tx,debt.S("category"));
                // §40.4 stage-4 sources are month-created operational debts; NegativeSalaryCarryover stays stage 8 (§40.6 instalment / confirm).
                bool thisMonth=debt.S("effectiveFrom")==period&&debt.S("category")!="NegativeSalaryCarryover";
                // M4: this-month takes full remaining (still capped by available); old debts keep the ledger instalment (RWD1c H7).
                decimal instalment=DebtLedger.InstalmentForPayCycle(c,tx,debt);
                decimal amount=J.Money(thisMonth||(debt.S("category")=="FuelOverage"&&debt.S("effectiveFrom")==period&&group.Any(l=>l.B("ineligibleKeetaSalary")))?debt.N("remainingBalance"):Math.Min(debt.N("remainingBalance"),instalment));
                if(thisMonth)
                {
                    decimal take=J.Money(Math.Min(amount,Math.Max(0,available)));
                    deductions.Add(new JsonObject{["rider"]=rider,["category"]=debt.S("category"),["debtCategory"]=debt.S("category"),["amount"]=-take,["requestedAmount"]=amount,["computed"]=-amount,["source"]=debt.S("id"),["accountCode"]=account,["type"]="stage4",["phase"]=dp.Phase,["order"]=dp.Order,["labelAr"]=dp.LabelAr,["app"]="",["accountId"]="",["month"]=period});
                    available-=take;debtBefore.Add(debt.Clone());
                }
                else
                {
                    deductions.Add(new JsonObject{["rider"]=rider,["category"]="debt_installment",["debtCategory"]=debt.S("category"),["amount"]=0,["requestedAmount"]=amount,["computed"]=-amount,["source"]=debt.S("id"),["accountCode"]=account,["type"]="debt",["phase"]=dp.Phase,["order"]=dp.Order,["labelAr"]=dp.LabelAr});debtBefore.Add(debt.Clone());
                }
            }
            // Captured fuel_usage (ERP FuelCardMonthlyUsages) for the cycle month → stage 4.
            foreach(var fuel in CycleOps(c,tx,cycle,"fuel_usage").Where(f=>f.S("riderId")==rider&&FuelMonth(f)==period))
            {
                decimal fuelAmt=J.Money(fuel.N("amount",fuel.N("totalAmount",fuel.N("TotalAmount"))));if(fuelAmt<=0)continue;
                decimal cap=group.Max(l=>l.N("fuelCap"));bool salary=group.All(l=>l.B("salaryOperationalExpense")||(l.S("schemeType")=="target_salary"&&!l.B("ineligibleKeetaSalary")));
                // m1: cap==0 must not silently make the company pay all — only when the setting says so.
                decimal companyPart=salary?(cap>0?Math.Min(fuelAmt,cap):(PipelineSettings.SalaryFuelCompanyPaysWhenNoCap()?fuelAmt:0m)):0m,riderPart=fuelAmt-companyPart;
                if(companyPart>0)deductions.Add(new JsonObject{["rider"]=rider,["category"]="FuelExpense",["amount"]=0,["companyCost"]=companyPart,["source"]=fuel.S("id"),["accountCode"]=PayExpenseAccounts.SalaryFuel,["type"]="company_expense",["phase"]=1,["order"]=15,["labelAr"]="مصروف وقود لمندوب الراتب",["month"]=period,["affectsNet"]=false});
                if(riderPart>0){decimal take=J.Money(Math.Min(riderPart,Math.Max(0,available)));deductions.Add(new JsonObject{["rider"]=rider,["category"]="FuelOverage",["amount"]=-take,["requestedAmount"]=riderPart,["computed"]=-riderPart,["source"]=fuel.S("id"),["accountCode"]="112511",["type"]="stage4",["phase"]=1,["order"]=10,["labelAr"]="وقود على المندوب",["month"]=period,["app"]="",["accountId"]=""});available-=take;}
            }
        }
        foreach(var invoice in Rows(cycle["invoices"]).OfType<JsonObject>())
        foreach(var d in Rows(invoice["deductions"]).OfType<JsonObject>())
        {
            InvoiceDeductionClassification.EnsureMigrated(d);
            string app=invoice.S("app");
            foreach(var row in InvoiceDeductionClassification.ExpandForCycle(d,app,PayPlatforms.For(app).Receivable))
                deductions.Add(row);
        }
        // Current-month Keeta fallback fuel is operating cost, not a personal instalment.
        foreach(var d in deductions.OfType<JsonObject>().Where(d=>(d.S("type") is "debt" or "stage4")&&d.S("debtCategory")=="FuelOverage"))
        {
            var debt=Store.Get(c,d.S("source"),"debt",tx);if(debt==null)continue;
            if(debt.S("effectiveFrom")==period&&groupProtected(d.S("rider")))
            {J.Need(d.S("accountCode")=="112511","وقود راتب كيتا غير المؤهل يحتاج ذمة الوقود 112511 وفق §22.9.2.");d["type"]="fuel_recovery";d["fuelTotal"]=debt.N("remainingBalance");d["amount"]=0;}
        }
        bool groupProtected(string rider)=>lines.Any(l=>l.S("rider")==rider&&l.B("ineligibleKeetaSalary"));
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
        var prior=Rows(cycle["riders"]).OfType<JsonObject>().ToDictionary(r=>r.S("rider"));var summaries=new JsonArray();var lines=Lines(c,tx,cycle.S("id"));foreach(var group in lines.GroupBy(l=>l.S("rider")))
        {
            string rider=group.Key,period=cycle.S("period");J.Need(!Store.List(c,Kind,tx).Any(d=>d.S("id")!=cycle.S("id")&&d.S("status") is "approved" or "partially_approved"&&Lines(c,tx,d.S("id")).Any(l=>l.S("rider")==rider&&l.S("period")==period)),"المندوب معتمد بالفعل في دورة أخرى للشهر؛ لا تكرر الاستقطاع.");
            if(prior.TryGetValue(rider,out var saved)&&saved.S("salaryApprovedAt")!=""){summaries.Add(saved.Clone());continue;}
            var fuel=Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("rider")==rider&&d.S("type")=="fuel_recovery").ToList();
            foreach(var d in fuel){d["amount"]=0;d["companyCost"]=0;}
            decimal net=group.Sum(l=>l.N("entitlement")-l.N("accountDeduction")-l.N("allocatedVehicleCharge"))+Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("rider")==rider).Sum(d=>d.N("amount"));
            foreach(var d in fuel){decimal recovered=Math.Min(Math.Max(0,net),d.N("fuelTotal"));d["amount"]=-recovered;d["companyCost"]=d.N("fuelTotal")-recovered;net-=recovered;}
            decimal carried=Store.List(c,"journal",tx).Where(FinanceViews.Posted).Where(j=>string.CompareOrdinal(j.S("period"),cycle.S("postingPeriod",period))<=0).SelectMany(j=>(j["lines"] as JsonArray??[]).OfType<JsonObject>()).Where(l=>l.S("sponsorId")==cycle.S("companyId")&&l.S("iqama")==rider&&l.S("accountCode") is "2110" or "212103").Sum(l=>l.N("credit")-l.N("debit"));
            bool deficit=Store.List(c,"journal",tx).Where(FinanceViews.Posted).SelectMany(j=>(j["lines"] as JsonArray??[]).OfType<JsonObject>()).Where(l=>l.S("sponsorId")==cycle.S("companyId")&&l.S("iqama")==rider&&l.S("accountCode")=="1211").Sum(l=>l.N("debit")-l.N("credit"))>0;
            // JsonNode.ToString() quotes strings — read the raw value so §40.6 can drop negative_net_requires_decision.
            static string BlockerText(JsonNode? b)=>b is JsonValue v&&v.TryGetValue<string>(out var s)?s:"";
            var hard=group.SelectMany(l=>Blockers(l).Select(BlockerText)).Where(b=>b!=""&&b!="negative_net_requires_decision").Distinct().ToList();
            if(deficit)hard.Add("legacy_deficit_requires_resolution");
            // §40.6: negative net is allowed for every app (including Ninja salary); carry to 112507 at final approval.
            // §40.5: hold when the rider also worked another app/company account this month outside this cycle.
            var cycleApps=group.Select(l=>l.S("app")).Where(a=>a!="").Distinct(StringComparer.Ordinal).OrderBy(a=>a,StringComparer.Ordinal).ToList();
            var outside=SharedRiderConsolidation.AppsInMonth(c,tx,cycle,rider,period);
            var missing=outside.Where(a=>!cycleApps.Contains(a,StringComparer.Ordinal)).ToList();
            bool shared=missing.Count>0,consolReady=false;
            if(shared)
            {
                var monthApps=cycleApps.Concat(missing).Distinct(StringComparer.Ordinal).OrderBy(a=>a,StringComparer.Ordinal).ToList();
                SharedRiderConsolidation.RecordAppNet(c,tx,cycle,rider,net,cycleApps,monthApps);
                var consol=Store.Get(c,SharedRiderConsolidation.Id(rider,period),SharedRiderConsolidation.Kind,tx);
                consolReady=consol!=null&&(consol.B("released")||(consol.S("status")=="ready"&&(consol["missingApps"] as JsonArray??[]).Count==0));
                if(!consolReady)hard.Add("shared_rider_hold");
            }
            bool blocked=hard.Count>0;
            var summary=new JsonObject{["rider"]=rider,["net"]=J.Money(net),["carried"]=J.Money(carried),["closing"]=J.Money(carried+net),["status"]=blocked?"needs_decision":"ready",["blockers"]=new JsonArray(hard.Select(b=>(JsonNode?)JsonValue.Create(b)).ToArray())};
            if(shared){summary["tag"]=SharedRiderConsolidation.Tag;summary["sharedRider"]=true;summary["sharedLabelAr"]=SharedRiderConsolidation.QueueLabelAr;summary["missingApps"]=new JsonArray(missing.Select(a=>(JsonNode?)JsonValue.Create(a)).ToArray());summary["consolidationReady"]=consolReady;}
            Phases(summary,group,Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>d.S("rider")==rider));summaries.Add(summary);
        }
        cycle["riders"]=summaries;if(summaries.Count==0||summaries.OfType<JsonObject>().Any(r=>r.S("status")!="ready"))cycle["status"]="needs_decision";
    }
    static bool IndependentReview(ClaimsPrincipal actor,JsonObject row,DbConnection? c=null,DbTransaction? tx=null,JsonObject? cycle=null)=>
        AccessPolicy.Independent(actor,row.S("reviewedBy"),"pay.salary_review",c,tx,cycle,scope:AccessPolicy.ScopePayApprovals)&&Rows(row["edits"]).OfType<JsonObject>().All(e=>AccessPolicy.Independent(actor,e.S("by"),"pay.salary_edit",c,tx,cycle,scope:AccessPolicy.ScopePayApprovals));
    static bool Approve(DbConnection c,DbTransaction tx,JsonObject cycle,ClaimsPrincipal actor)
    {
        AccessPolicy.ValidateApproval(actor,new(){["requestedBy"]=cycle.S("createdBy"),["targetDepartment"]="FIN",["requiredRankLevel"]=1},new JsonObject(),"pay.approve",c,tx,"pay.salary_approved",cycle);
        J.Need(Rows(cycle["history"]).OfType<JsonObject>().Where(h=>h.N("stage")<7).All(h=>AccessPolicy.Independent(actor,h.S("advancedBy"),"pay.salary_preparation",c,tx,cycle,scope:AccessPolicy.ScopePayApprovals)),"المعتمد مستقل عن تجهيز الدورة.",403);
        // Shared riders stay out of the approval batch until consolidation is ready or released (§40.5).
        var heldShared=Rows(cycle["riders"]).OfType<JsonObject>().Where(r=>r.S("salaryApprovedAt")==""&&SharedRiderConsolidation.Held(r)&&!r.B("consolidationReady")).ToList();
        var batch=Rows(cycle["riders"]).OfType<JsonObject>().Where(r=>r.S("salaryApprovedAt")==""&&r.S("review")=="approved"&&Blockers(r).Count==0&&!(SharedRiderConsolidation.Held(r)&&!r.B("consolidationReady"))).ToList();
        if(batch.Count==0)
        {
            if(heldShared.Count>0){cycle["approvalResult"]=new JsonObject{["approved"]=0,["skipped"]=heldShared.Count,["skippedRows"]=new JsonArray(heldShared.Select(r=>(JsonNode)new JsonObject{["rider"]=r.S("rider"),["reason"]="shared_rider_hold"}).ToArray())};Store.Audit(c,tx,"pay.salary_rows_skipped",cycle.S("id"),actor.Identity!.Name!);return false;}
            J.Need(Rows(cycle["riders"]).OfType<JsonObject>().All(r=>r.S("salaryApprovedAt")!=""),"راجع واعتمد صفًا جاهزًا أولًا؛ الحالات المعلقة لا توقف الآخرين.");return true;
        }
        // Pass c/tx so §35 ActiveBypassGrant applies when the same actor reviewed the rows.
        var skipped=batch.Where(r=>!IndependentReview(actor,r,c,tx,cycle)).ToList();batch=batch.Where(r=>IndependentReview(actor,r,c,tx,cycle)).ToList();
        cycle["approvalResult"]=new JsonObject{["approved"]=batch.Count,["skipped"]=skipped.Count+heldShared.Count,["skippedRows"]=new JsonArray(skipped.Select(r=>(JsonNode)new JsonObject{["rider"]=r.S("rider"),["reason"]="reviewer_or_editor_is_approver"}).Concat(heldShared.Select(r=>(JsonNode)new JsonObject{["rider"]=r.S("rider"),["reason"]="shared_rider_hold"})).ToArray())};
        if(batch.Count==0){Store.Audit(c,tx,"pay.salary_rows_skipped",cycle.S("id"),actor.Identity!.Name!);return false;}
        foreach(var (app,_) in Accounts(cycle))PlatformPosting.Demand(c,app,tx);
        bool first=cycle.S("journalEntryId")=="";string now=DateTimeOffset.UtcNow.ToString("O");cycle["approvedBy"]=actor.Identity!.Name;cycle["approvedAt"]=now;
        foreach(var row in batch)
        {
            J.Need(IndependentReview(actor,row,c,tx,cycle),"يلزم مراجع مستقل.",403);
            // M-0670 / §32 / §40.6: instalment confirm or defer BEFORE final salary approval (same order as feat/w2-money-core).
            bool hasDebt=Rows(cycle["deductions"]).OfType<JsonObject>().Any(d=>d.S("rider")==row.S("rider")&&d.S("type")=="debt");
            J.Need(!hasDebt||row.S("debtDeductionStatus") is "confirmed" or "deferred","أكد استقطاع الدين أو أجّله قبل الاعتماد النهائي للراتب.",409);
            if(row.N("net")<0)CreateCarry(c,tx,cycle,row,actor);
            SettleOperatingFuel(c,tx,cycle,row,actor);
            SettleStage4(c,tx,cycle,row,actor);
            // Apply confirmed instalment to net at approval (movement was deferred until here only for legacy store debts via Debts.Post).
            decimal take=row.N("oldDebtInstalment");
            if(take>0)
            {
                var payLines=Rows(cycle["deductions"]).OfType<JsonObject>()
                    .Where(d=>d.S("rider")==row.S("rider")&&d.S("type")=="debt"&&-d.N("amount")>0)
                    .Select(d=>new PayLine("debt_installment","قسط معتمد قبل الاعتماد",d.N("amount"),d.S("source"))).ToList();
                Debts.Post(c,tx,new(row.S("rider"),cycle.S("period"),"pay_cycle",0,row.N("net"),row.N("net"),row.N("net"),0,0,payLines,[]),cycle.S("id"),payCycle:true);
                row["net"]=J.Money(Math.Max(0,row.N("net"))-take);row["closing"]=row.N("carried")+row.N("net");
            }
            row["salaryNet"]=row.N("net");row["salaryApprovedAt"]=now;row["salaryApprovedBy"]=actor.Identity!.Name;
            if(row.S("debtDeductionStatus")=="")row["debtDeductionStatus"]=hasDebt?"confirmed":"deferred";
        }
        Store.Save(c,Kind,cycle,tx);
        var journal=PostEarnings(c,tx,cycle,actor,batch.Select(r=>r.S("rider")).ToHashSet(),first);if(first)cycle["journalEntryId"]=journal.S("id");
        foreach(var row in batch)
        {
            row["salaryJournalEntryId"]=journal.S("id");row["status"]="ready";
            if(row.N("salaryNet")<0)
            {
                decimal carry=-row.N("salaryNet");row["net"]=0;row["phase3"]=row.N("phase3")+carry;row["closing"]=row.N("carried");
                Rows(cycle["deductions"]).Add(new JsonObject{["rider"]=row.S("rider"),["category"]="NegativeSalaryCarryover",["amount"]=carry,["type"]="carryover",["source"]=row.S("carryDebtId"),["accountCode"]="112507",["phase"]=3,["order"]=100,["labelAr"]="ترحيل العجز عند الاعتماد النهائي"});
            }
            // U-W2-21 / X10: freeze short rider statement at final salary approval (no Omar live-read later).
            string period=cycle.S("period");
            var facing=RiderStatements.BuildRiderFacing(c,row.S("rider"),period,period,null,"ar",actor,tx);
            RiderStatements.SaveApprovalSnapshot(c,tx,row.S("rider"),period,period,null,actor,facing);
        }
        bool complete=Rows(cycle["riders"]).OfType<JsonObject>().All(r=>r.S("salaryApprovedAt")!="");
        cycle["status"]=complete?"approved":"partially_approved";cycle["debtDeductionStatus"]="pending";
        foreach(var review in Rows(cycle["invoices"]).OfType<JsonObject>())
        {
            var invoice=Store.Get(c,review.S("invoiceId"),"platform_invoice",tx)!;
            if(invoice.S("approvalStatus")=="cycle_pending"){invoice["approvalStatus"]="approved";invoice["approvedBy"]=actor.Identity!.Name;invoice["approvedAt"]=now;}
            invoice["payCycleId"]=cycle.S("id");if(first)invoice["journalEntryId"]=journal.S("id");invoice["accountingStatus"]=complete?"posted":"partially_posted";Store.Save(c,"platform_invoice",invoice,tx);
        }
        SnapshotDebtAfter(c,tx,cycle);cycle["sourceFingerprint"]=Fingerprint(c,tx,cycle);return true;
    }
    static JsonObject PostEarnings(DbConnection c,DbTransaction tx,JsonObject cycle,ClaimsPrincipal actor,HashSet<string> selected,bool first)
    {
        var journalLines=new JsonArray();string company=cycle.S("companyId"),period=cycle.S("period");var global=PaySettings.Global(c,tx,period+"-01");var accounts=global["data"]!["values"]!["accounts"]!;
        void Add(string code,decimal debit,decimal credit,string app,string rider,string source,string account="",string debt=""){decimal signed=J.Money(debit-credit);if(signed==0)return;journalLines.Add(PayPosting.Line(code,company,Math.Max(0,signed),Math.Max(0,-signed),app,rider,period,source,account,debt));}
        foreach(var line in Lines(c,tx,cycle.S("id")).Where(l=>selected.Contains(l.S("rider"))))
        {
            string app=line.S("app"),rider=line.S("rider"),id=line.S("id");decimal total=line.N("platformGross")+line.N("incentives"),earned=line.N("gross");string recv=PayPlatforms.For(app).Receivable;
            bool penaltiesInBase=line.B("penaltiesInCommissionBase");
            foreach(var source in Rows(line["sources"]).OfType<JsonObject>())
            {
                decimal grossPart=source.N("baseGross")+source.N("incentive"),ded=source.N("deduction");
                // §40.3 percent: Gross/212103 already net of penalties — take receivable net of ded (skip the 212103↔recv deduction pair).
                if(penaltiesInBase)Add(recv,grossPart-ded,0,app,rider,source.S("sourceLineId"),source.S("accountId"));
                else
                {
                    Add(recv,grossPart,0,app,rider,source.S("sourceLineId"),source.S("accountId"));
                    Add("212103",ded,0,app,rider,source.S("sourceLineId"),source.S("accountId"));Add(recv,0,ded,app,rider,source.S("sourceLineId"),source.S("accountId"));
                }
            }
            if(line.S("accountingRole")=="agent"){Add("212103",0,earned,app,rider,id);Add(PayPlatforms.Revenue(app,"agent"),0,total-earned,app,rider,id);}
            else
            {
                // §40.3: revenue matches receivable intake (gross − platform penalties already in commission base).
                decimal revenue=penaltiesInBase?J.Money(total-line.N("platformPenalties")):total;
                Add(PayPlatforms.Revenue(app,"principal"),0,revenue,app,rider,id);Add(accounts.S("salaryExpense"),earned,0,app,rider,id);Add("212103",0,earned,app,rider,id);
            }
            decimal rent=line.N("allocatedVehicleCharge"),cost=line.N("operatingCost"),deduction=line.N("accountDeduction");
            Add("212103",line.N("recoveredOperatingCost",cost),0,app,rider,id);Add("512121",line.N("unrecoveredOperatingCost"),0,app,rider,id);Add(line.S("operatingCostAccount"),0,cost,app,rider,id);
            string vehicleIncome=accounts.S(line.S("vehicleType")=="company_car"?"vehicleIncomeCar":"vehicleIncomeBike");Add("212103",rent,0,app,rider,id);Add("512121",line.N("unrecoveredVehicleCharge"),0,app,rider,id);Add(vehicleIncome,0,rent+line.N("unrecoveredVehicleCharge"),app,rider,id);
        }
        foreach(var invoice in Rows(cycle["invoices"]).OfType<JsonObject>().Where(_=>first))
        {
            string app=invoice.S("app"),id=invoice.S("invoiceId"),recv=PayPlatforms.For(app).Receivable;var source=Store.Get(c,id,"platform_invoice",tx)!;Add(recv,source.N("vat"),0,app,"",id);Add(accounts.S("outputVat"),0,source.N("vat"),app,"",id);
            foreach(var d in Rows(invoice["deductions"]).OfType<JsonObject>())
            {
                InvoiceDeductionClassification.EnsureMigrated(d);
                foreach(var a in InvoiceDeductionClassification.CompanyAllocations(d))
                {Add(a.S("expenseAccount"),a.N("amount"),0,app,"",d.S("sourceLineId"));Add(recv,0,a.N("amount"),app,"",d.S("sourceLineId"));}
            }
        }
        foreach(var d in Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>selected.Contains(d.S("rider"))&&d.S("type")=="housing"))
        {decimal amount=-d.N("amount");Add("212103",amount,0,"",d.S("rider"),d.S("source"));Add(accounts.S("housingIncome"),0,amount,"",d.S("rider"),d.S("source"));}
        foreach(var d in Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>selected.Contains(d.S("rider"))&&d.S("type") is "adjustment" or "review_adjustment")){decimal amount=d.N("amount");Add(d.S("accountCode"),amount>0?amount:0,amount<0?-amount:0,"",d.S("rider"),d.S("source"));Add("212103",amount<0?-amount:0,amount>0?amount:0,"",d.S("rider"),d.S("source"));}
        foreach(var d in Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>selected.Contains(d.S("rider"))&&d.S("type") is "platform_rider" or "fuel_recovery"))
        {
            string rider=d.S("rider"),source=d.S("source");decimal recovered=-d.N("amount"),companyCost=d.N("companyCost");
            Add("212103",recovered,0,d.S("app"),rider,source,debt:d.S("type")=="fuel_recovery"?source:"");Add("512121",companyCost,0,d.S("app"),rider,source,debt:d.S("type")=="fuel_recovery"?source:"");Add(d.S("accountCode"),0,recovered+companyCost,d.S("app"),rider,source,debt:d.S("type")=="fuel_recovery"?source:"");
        }
        // M2 §22.9: once per fuel_usage row — Dr expense (company) + Dr 112511 (rider) / Cr fuel supplier.
        string fuelSupplier=PipelineSettings.FuelSupplierAccount();
        var fuelPosted=new HashSet<string>(StringComparer.Ordinal);
        foreach(var d in Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>selected.Contains(d.S("rider"))&&(d.S("type")=="stage4"&&d.S("category")=="FuelOverage"||d.S("type")=="company_expense"&&d.S("category")=="FuelExpense")))
        {
            string source=d.S("source");if(source==""||!fuelPosted.Add(source))continue;
            string ledgerId="fuel_usage:"+source+":"+period+":"+cycle.S("id");
            if(Store.Get(c,ledgerId,"ledger",tx)!=null)continue;
            var siblings=Rows(cycle["deductions"]).OfType<JsonObject>().Where(x=>x.S("source")==source).ToList();
            decimal companyCost=siblings.Where(x=>x.S("type")=="company_expense").Sum(x=>x.N("companyCost"));
            decimal riderPart=siblings.Where(x=>x.S("type")=="stage4"&&x.S("category")=="FuelOverage").Sum(x=>-x.N("computed",x.N("amount")));
            if(riderPart<=0)riderPart=siblings.Where(x=>x.S("type")=="stage4"&&x.S("category")=="FuelOverage").Sum(x=>-x.N("amount"));
            // When stage4 was capped by available, still recognize the full rider share from companyCost+take when computed is set.
            if(riderPart<=0&&companyCost<=0)continue;
            string rider=d.S("rider"),app=d.S("app");
            Add(PayExpenseAccounts.SalaryFuel,companyCost,0,app,rider,source);Add("112511",riderPart,0,app,rider,source);Add(fuelSupplier,0,companyCost+riderPart,app,rider,source);
            Store.Save(c,"ledger",new(){["id"]=ledgerId,["iqama"]=rider,["period"]=period,["amount"]=-(companyCost+riderPart),["kind"]="fuel_usage",["sourceRef"]=cycle.S("id"),["fuelSource"]=source,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")},tx);
        }
        // B1: stage-4 this-month debt / fuel rider part — Dr 212103 / Cr receivable (debt link when source is a debt).
        foreach(var d in Rows(cycle["deductions"]).OfType<JsonObject>().Where(d=>selected.Contains(d.S("rider"))&&d.S("type")=="stage4"))
        {
            decimal take=-d.N("amount");if(take<=0)continue;
            string debtId=Store.Get(c,d.S("source"),"debt",tx)!=null?d.S("source"):"";
            Add("212103",take,0,d.S("app"),d.S("rider"),d.S("source"),debt:debtId);Add(d.S("accountCode"),0,take,d.S("app"),d.S("rider"),d.S("source"),debt:debtId);
        }
        foreach(var row in Rows(cycle["riders"]).OfType<JsonObject>().Where(r=>selected.Contains(r.S("rider"))&&r.N("salaryNet")<0))
        {Add("112507",-row.N("salaryNet"),0,"",row.S("rider"),row.S("carryDebtId"),debt:row.S("carryDebtId"));Add("212103",0,-row.N("salaryNet"),"",row.S("rider"),row.S("carryDebtId"),debt:row.S("carryDebtId"));}
        J.Need(journalLines.Count>0,"لا توجد مبالغ للترحيل.");return PayPosting.Post(c,tx,cycle,journalLines,actor.Identity!.Name!,first?"":":salary:"+Hash(string.Join(",",selected.Order())));
    }
}
