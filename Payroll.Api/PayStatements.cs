using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
namespace Payroll;

public static class PayStatements
{
    public static ILogger Logger { get; set; } = NullLogger.Instance;
    static JsonArray Array(IEnumerable<JsonObject> rows)=>new(rows.Select(r=>(JsonNode?)r.Clone()).ToArray());
    static readonly string[] PhaseKeys=["gross","commission","afterCommission","phase1","preliminaryDue","phase2","phase3"];
    static JsonObject Pick(JsonObject source,params string[] keys){var result=new JsonObject();foreach(string key in keys)if(source.ContainsKey(key))result[key]=source[key]?.DeepClone();return result;}
    static bool RunVisible(ClaimsPrincipal actor,JsonObject run)
    {
        if(!FinancialScopes.Ledger(actor,run))return false;var platforms=(run["sourceRows"] as JsonArray??[]).OfType<JsonObject>().Select(r=>r.S("platform")).Where(p=>p!="").Distinct().ToList();return platforms.Count>0?platforms.All(p=>FinancialScopes.Platform(actor,p)):PayPlatforms.Table.All(p=>FinancialScopes.Platform(actor,p.Platform))&&FinancialScopes.Platform(actor,"Other");
    }
    public static JsonObject Build(DbConnection c,string rider,string period,ClaimsPrincipal? actor,bool approvedOnly=false,DbTransaction? tx=null)
    {
        rider=J.Iqama(rider);period=J.Period(period);var person=Store.Get(c,rider,"rider",tx)??throw new ApiError("المندوب غير موجود.",404);
        if(actor!=null){AccessPolicy.Demand(actor,"pay.view");AccessPolicy.Demand(actor,"payroll.view");AccessPolicy.Demand(actor,"debts.view");}
        var allLines=Store.ListWhere(c,PayCycles.LineKind,tx,null,("rider",rider),("period",period));
        var lineCycleIds=allLines.Select(l=>l.S("cycleId")).Where(id=>id!="").Distinct(StringComparer.Ordinal).ToList();
        var candidates=lineCycleIds.Count==0
            ?new List<JsonObject>()
            :Store.ListByIds(c,PayCycles.Kind,lineCycleIds,tx).Where(d=>d.S("period")==period&&d.S("status") is not ("cancelled" or "reversed")&&(!approvedOnly||d.S("status")=="approved"||d.S("status")=="partially_approved"&&(d["riders"] as JsonArray??[]).OfType<JsonObject>().Any(r=>r.S("rider")==rider&&r.S("salaryApprovedAt")!=""))).ToList();
        var cycles=candidates.Where(d=>actor==null||PayCycles.Visible(actor,d)).ToList();if(candidates.Count>0&&cycles.Count==0)throw new ApiError("الكشف الشهري خارج نطاق الشركة أو التطبيقات.",403);
        var cards=new List<JsonObject>();var deductions=new List<JsonObject>();var refs=new List<JsonObject>();decimal net=0,carried=0,closing=0;string source="none",status="no_approved_cycle";var phases=new JsonObject();
        if(cycles.Count>0)
        {
            source="pay_cycle";status=cycles.All(d=>d.S("status")=="approved"||(d["riders"] as JsonArray??[]).OfType<JsonObject>().Any(r=>r.S("rider")==rider&&r.S("salaryApprovedAt")!=""))?"approved":"preview";
            foreach(var cycle in cycles.OrderBy(d=>d.S("companyId"),StringComparer.Ordinal))
            {
                refs.Add(Pick(cycle,"id","companyId","period","postingPeriod","status","stage","approvedAt"));var lines=allLines.Where(l=>l.S("cycleId")==cycle.S("id")).ToList();
                foreach(var line in lines)
                {
                    var card=Pick(line,"app","schemeId","schemeName","schemeType","accountingRole","category","companyId","cycleId","orders","days","incentives","companyCommission","band","operatingCost","recoveredOperatingCost","accountDeduction","platformPenalties","penaltiesInCommissionBase","entitlement","fuelCap","status","blockers");
                    card["sources"]=(line["sources"] as JsonArray??[]).DeepClone();card["platform"]=PayPlatforms.For(line.S("app")).Platform;card["gross"]=line.N("platformGross")+line.N("incentives");card["earnedGross"]=line.N("gross");card["vehicleCharge"]=line.N("allocatedVehicleCharge",line.N("vehicleCharge"));card["netBeforePersonal"]=line.N("entitlement")-card.N("vehicleCharge")-line.N("accountDeduction");
                    var explain=(line["lines"] as JsonArray??[]).OfType<JsonObject>().Select(l=>l.Clone()).ToList();foreach(var e in explain.Where(e=>e.S("category")=="vehicle_charge")){e["amount"]=-card.N("vehicleCharge");e["formula"]="monthly rent allocated once per rider across apps";}
                    if(line.N("accountDeduction")!=0)explain.Add(new(){["label"]="خصم اليوزر المراجع",["formula"]="reviewed actual operator deduction; excludes company-level costs",["inputs"]=new JsonObject{["reviewedAmount"]=line.N("accountDeduction")},["amount"]=-line.N("accountDeduction"),["category"]="account_deduction",["affectsNet"]=true});card["explain"]=Array(explain);cards.Add(card);
                }
                foreach(var d in (cycle["deductions"] as JsonArray??[]).OfType<JsonObject>().Where(d=>d.S("rider")==rider))
                {
                    if(actor!=null&&d.S("type")=="debt"){var debt=Store.Get(c,d.S("source"),"debt",tx)??throw new ApiError("دليل الدين غير موجود.",409);FinancialScopes.DemandDebt(actor,debt);}
                    deductions.Add(Pick(d,"category","amount","source","type","accountCode","phase","order","labelAr"));
                }
                var summary=(cycle["riders"] as JsonArray??[]).OfType<JsonObject>().SingleOrDefault(r=>r.S("rider")==rider);decimal movement=summary?.N("net")??lines.Sum(l=>l.N("entitlement")-l.N("accountDeduction")-l.N("allocatedVehicleCharge",l.N("vehicleCharge")))+(cycle["deductions"] as JsonArray??[]).OfType<JsonObject>().Where(d=>d.S("rider")==rider).Sum(d=>d.N("amount"));
                // BR §29: phase subtotals from the cycle summary, summed when a rider has several cycles.
                if(summary!=null)foreach(string k in PhaseKeys)phases[k]=phases.N(k)+summary.N(k);
                net+=movement;carried+=summary?.N("carried")??0;closing+=summary?.N("closing")??movement;
            }
        }
        else
        {
            var allRuns=Store.ListWhere(c,"run",tx,null,("iqama",rider),("period",period)).Where(r=>r.S("approvedAt")!="").ToList();var runs=allRuns.Where(r=>actor==null||RunVisible(actor,r)).ToList();if(allRuns.Count>0&&runs.Count==0)throw new ApiError("المسير القديم خارج النطاق المالي.",403);
            if(runs.Count>0){source="legacy_run";status="approved";}
            foreach(var run in runs)
            {
                refs.Add(Pick(run,"id","sponsorId","period","approvedAt"));var result=run["result"] as JsonObject??new();var work=(run["sourceRows"] as JsonArray??[]).OfType<JsonObject>().ToList();var pay=(result["lines"] as JsonArray??[]).OfType<JsonObject>().ToList();var earnings=pay.Where(l=>l.S("category") is not ("debt_installment" or "already_paid")&&(l.N("amount")>=0||l.S("category") is "company_fee" or "platform_settlement")).ToList();
                string App(JsonObject line){string platform=line.S("platform");if(platform=="")platform=work.FirstOrDefault(w=>w.S("sourceRef")==line.S("source")).S("platform");return platform==""?"legacy_unattributed":PayPlatforms.For(platform).App;}
                foreach(var group in earnings.GroupBy(App))
                {
                    var explain=group.Select(l=>new JsonObject{["label"]=l.S("description"),["formula"]="immutable approved legacy run line",["inputs"]=new JsonObject{["category"]=l.S("category"),["source"]=l.S("source")},["amount"]=l.N("amount"),["category"]=l.S("category"),["affectsNet"]=true}).ToList();
                    decimal negSettlement=group.Where(l=>l.S("category")=="platform_settlement"&&l.N("amount")<0m).Sum(l=>l.N("amount"));
                    cards.Add(new(){["app"]=group.Key,["platform"]=group.Key=="legacy_unattributed"?"Legacy":PayPlatforms.For(group.Key).Platform,["companyId"]=run.S("sponsorId"),["schemeName"]=result.S("policyName"),["schemeType"]="legacy",["gross"]=group.Where(l=>l.S("category")!="company_fee"&&l.N("amount")>0m).Sum(l=>l.N("amount")),["platformSettlementAdjustment"]=negSettlement,["earnedGross"]=group.Sum(l=>l.N("amount")),["companyCommission"]=-group.Where(l=>l.S("category")=="company_fee").Sum(l=>l.N("amount")),["vehicleCharge"]=0,["incentives"]=0,["band"]=null,["explain"]=Array(explain),["status"]="approved"});
                }
                foreach(var d in pay.Except(earnings)){if(actor!=null&&d.S("category")=="debt_installment"&&Store.Get(c,d.S("source"),"debt",tx) is {} debt)FinancialScopes.DemandDebt(actor,debt);deductions.Add(Pick(d,"category","description","amount","source"));}
                net+=result.N("periodMovement");carried+=result.N("openingBalance");closing+=result.N("closingBalance");
            }
        }
        if(actor!=null&&source=="none")FinancialScopes.DemandRider(actor,person);
        string iban=person.S("iban");var header=new JsonObject{["rider"]=rider,["name"]=person.S("name"),["period"]=period,["status"]=person.S("status"),["ibanMasked"]=iban.Length>=4?"••••"+iban[^4..]:"",["cycles"]=Array(refs)};
        decimal closingMoney=J.Money(closing),netMoney=J.Money(net),carriedMoney=J.Money(carried);
        var (shortLines,shortLinesBalanced,shortLinesMismatch)=BuildShortLines(cards,deductions,carriedMoney,closingMoney);
        return new(){["header"]=header,["source"]=source,["status"]=status,["cards"]=Array(cards),["deductions"]=Array(deductions),["shortLines"]=shortLines,["shortLinesBalanced"]=shortLinesBalanced,["shortLinesMismatch"]=shortLinesMismatch,["phases"]=phases.Count==0?null:phases,["net"]=netMoney,["carried"]=carriedMoney,["closing"]=closingMoney,
            ["closingDirection"]=Direction(closingMoney),["closingMarkerAr"]=MarkerAr(closingMoney),["closingMarkerEn"]=MarkerEn(closingMoney),["closingMarkerUr"]=MarkerUr(closingMoney),
            ["netDirection"]=Direction(netMoney),["netMarkerAr"]=MarkerAr(netMoney),["netMarkerEn"]=MarkerEn(netMoney),["netMarkerUr"]=MarkerUr(netMoney),
            ["legalDocument"]=false,
            ["disclaimerAr"]="بيان استرشادي مختصر للمندوب — لا يُعد مستنداً رسمياً أو قانونياً.",
            ["disclaimerEn"]="Short informational summary for the rider — not an official or legal document.",
            ["disclaimerUr"]="رائیڈر کے لیے مختصر معلوماتی خلاصہ — یہ سرکاری یا قانونی دستاویز نہیں ہے۔",
            ["balanceBasis"]=source=="pay_cycle"?"cycle_snapshot":"approved_legacy_snapshot",["approvedOnly"]=approvedOnly};
    }

    static string Direction(decimal amount)=>amount>0?"credit":amount<0?"debit":"neutral";
    static string MarkerAr(decimal amount)=>amount>0?"لك":amount<0?"عليك":"متوازن";
    static string MarkerEn(decimal amount)=>amount>0?"For you (لك)":amount<0?"On you (عليك)":"Balanced";
    static string MarkerUr(decimal amount)=>amount>0?"آپ کے حق میں (لك)":amount<0?"آپ کے ذمے (عليك)":"متوازن";

    static (string Ar,string En,string Ur) PlatformNames(string raw)
    {
        string key=raw.Trim().ToLowerInvariant();
        return key switch
        {
            "keeta"=>("كيتا","Keeta","کیٹا"),
            "hungerstation" or "hs"=>("هنقرستيشن","HungerStation","ہنگر اسٹیشن"),
            "ninja"=>("نينجا","Ninja","ننجا"),
            "jahez"=>("جاهز","Jahez","جاہز"),
            "chefz" or "thechefz"=>("ذا شفز","The Chefz","دی شیفز"),
            "amazon"=>("أمازون","Amazon","ایمازون"),
            "legacy" or "legacy_unattributed"=>("مستحقات معتمدة","Approved earnings","منظور شدہ مستحقات"),
            _=>("تطبيق العمل","Work app","کام کی ایپ")
        };
    }

    static (string Ar,string En,string Ur) DeductionLabels(string category,string fallbackAr)
    {
        string key=category.Trim().ToLowerInvariant();
        return key switch
        {
            "debt_installment" or "debt"=>("قسط مديونية","Debt instalment","قرض کی قسط"),
            "already_paid" or "advance" or "cashadvance" or "bankadvance"=>("سلفة مستلمة","Advance deduction","وصول شدہ ایڈوانس"),
            "internetadvance" or "internet"=>("سلفة إنترنت","Internet advance","انٹرنیٹ ایڈوانس"),
            "vehicle_charge" or "vehicleinstallment"=>("إيجار المركبة","Vehicle charge","گاڑی کا کرایہ"),
            "account_deduction"=>("خصم تشغيل الحساب","Account deduction","اکاؤنٹ کٹوتی"),
            "fuel" or "fueloverage" or "fuel_deduction"=>("استهلاك الوقود","Fuel deduction","پیٹرول کی کٹوتی"),
            "housing" or "housingdeduction"=>("استقطاع السكن","Housing deduction","رہائش کی کٹوتی"),
            "penalty" or "trafficfine" or "violation" or "tga"=>("مخالفة أو غرامة","Violation or fine","خلاف ورزی یا جرمانہ"),
            "administrativededuction" or "administrative"=>("خصم إداري","Administrative deduction","انتظامی کٹوتی"),
            "workpermit" or "iqamarenewal" or "driverlicense" or "drivercard" or "medicalinsurance"=>("رسوم خدمات وإقامة","Service & permit fees","سروس اور اقامہ فیس"),
            "adjustment"=>("تسوية مالية","Financial adjustment","مالیاتی تصفیہ"),
            _=> (string.IsNullOrWhiteSpace(fallbackAr)?"استقطاع":fallbackAr,"Deduction","کٹوتی")
        };
    }

    static (JsonArray Items,bool Balanced,decimal Mismatch) BuildShortLines(List<JsonObject> cards,List<JsonObject> deductions,decimal carried,decimal expectedClosing)
    {
        var items=new JsonArray();decimal running=0m;
        void AddLine(string key,string ar,string en,string ur,decimal signedAmount)
        {
            decimal amt=J.Money(signedAmount);if(amt==0m)return;
            running=J.Money(running+amt);
            items.Add(new JsonObject
            {
                ["key"]=key,["labelAr"]=ar,["labelEn"]=en,["labelUr"]=ur,
                ["amount"]=amt,["absAmount"]=Math.Abs(amt),
                ["direction"]=Direction(amt),["markerAr"]=MarkerAr(amt),["markerEn"]=MarkerEn(amt),["markerUr"]=MarkerUr(amt),
                ["runningBalance"]=running,["runningDirection"]=Direction(running),
                ["runningMarkerAr"]=MarkerAr(running),["runningMarkerEn"]=MarkerEn(running),["runningMarkerUr"]=MarkerUr(running)
            });
        }
        if(carried!=0m)AddLine("carried_balance","رصيد مرحّل من شهر سابق","Carried balance from previous month","پچھلے مہینے کا منتقل شدہ بیلنس",carried);
        int cardIdx=0;
        foreach(var card in cards)
        {
            cardIdx++;
            var (pAr,pEn,pUr)=PlatformNames(card.S("platform",card.S("app")));
            decimal orders=card.N("orders");
            string ordAr=orders>0?$" ({orders:0} طلب)":"";
            string ordEn=orders>0?$" ({orders:0} orders)":"";
            string ordUr=orders>0?$" ({orders:0} آرڈرز)":"";
            string schemeType=card.S("schemeType");
            if(schemeType=="legacy")
            {
                decimal gross=card.N("gross");
                if(gross!=0m)AddLine($"card_{cardIdx}_gross",$"مستحقات التوصيل — {pAr}{ordAr}",$"Delivery earnings — {pEn}{ordEn}",$"ڈیلیوری کی آمدنی — {pUr}{ordUr}",gross);
                decimal negSettlement=card.N("platformSettlementAdjustment");
                if(negSettlement!=0m)AddLine($"card_{cardIdx}_platform_settlement",$"تسوية المنصة — {pAr}",$"Platform settlement — {pEn}",$"پلیٹ فارم تصفیہ — {pUr}",negSettlement);
                decimal comm=card.N("companyCommission");
                if(comm!=0m)AddLine($"card_{cardIdx}_commission",$"عمولة التشغيل — {pAr}",$"Operating commission — {pEn}",$"آپریٹنگ کمیشن — {pUr}",-comm);
            }
            else
            {
                bool salaryPrincipal=schemeType=="target_salary"&&card.S("accountingRole")!="agent";
                if(salaryPrincipal)
                {
                    decimal earned=card.N("earnedGross");
                    if(earned!=0m)AddLine($"card_{cardIdx}_gross",$"مستحقات التوصيل — {pAr}{ordAr}",$"Delivery earnings — {pEn}{ordEn}",$"ڈیلیوری کی آمدنی — {pUr}{ordUr}",earned);
                }
                else
                {
                    decimal gross=card.N("gross");
                    if(gross!=0m)AddLine($"card_{cardIdx}_gross",$"مستحقات التوصيل — {pAr}{ordAr}",$"Delivery earnings — {pEn}{ordEn}",$"ڈیلیوری کی آمدنی — {pUr}{ordUr}",gross);
                    if(card.B("penaltiesInCommissionBase"))
                    {
                        decimal pen=Math.Abs(card.N("platformPenalties"));
                        if(pen>0m)AddLine($"card_{cardIdx}_penalties",$"غرامات المنصة — {pAr}",$"Platform penalties — {pEn}",$"پلیٹ فارم جرمانے — {pUr}",-pen);
                    }
                    decimal comm=card.N("companyCommission");
                    if(comm!=0m)AddLine($"card_{cardIdx}_commission",$"عمولة التشغيل — {pAr}",$"Operating commission — {pEn}",$"آپریٹنگ کمیشن — {pUr}",-comm);
                }
                decimal recoveredOpCost=card.ContainsKey("recoveredOperatingCost")
                    ?Math.Abs(card.N("recoveredOperatingCost"))
                    :(card.ContainsKey("entitlement")?Math.Max(0m,card.N("earnedGross")-card.N("entitlement")):0m);
                if(recoveredOpCost>0m)AddLine($"card_{cardIdx}_operating_cost","تكلفة تشغيل مستردة","Recovered operating cost","وصول شدہ آپریٹنگ لاگت",-recoveredOpCost);
            }
            decimal veh=Math.Abs(card.N("vehicleCharge"));
            if(veh>0m)AddLine($"card_{cardIdx}_vehicle","إيجار المركبة","Vehicle charge","گاڑی کا کرایہ",-veh);
            decimal accDed=Math.Abs(card.N("accountDeduction"));
            if(accDed>0m)AddLine($"card_{cardIdx}_account","خصم تشغيل الحساب","Account deduction","اکاؤنٹ کٹوتی",-accDed);
        }
        int dedIdx=0;
        foreach(var d in deductions)
        {
            dedIdx++;
            string cat=d.S("category");
            var (dAr,dEn,dUr)=DeductionLabels(cat,d.S("labelAr",d.S("description")));
            decimal signed=d.N("amount");
            AddLine($"ded_{dedIdx}_{cat}",dAr,dEn,dUr,signed);
        }
        decimal mismatch=J.Money(expectedClosing-running);
        bool balanced=mismatch==0m;
        if(!balanced)Logger.LogWarning("PayStatements.BuildShortLines mismatch: running={Running}, expectedClosing={ExpectedClosing}, diff={Diff}",running,expectedClosing,mismatch);
        return (items,balanced,mismatch);
    }

    public static JsonObject LatestApprovedSummary(DbConnection c,string rider)
    {
        rider=J.Iqama(rider);
        var lineCycles=Store.ListWhere(c,PayCycles.LineKind,null,null,("rider",rider)).Select(l=>l.S("cycleId")).Where(id=>id!="").Distinct(StringComparer.Ordinal).ToList();
        var cyclePeriods=lineCycles.Count==0
            ?Enumerable.Empty<string>()
            :Store.ListByIds(c,PayCycles.Kind,lineCycles)
                .Where(d=>d.S("status") is not ("cancelled" or "reversed")&&(d.S("status")=="approved"||d.S("status")=="partially_approved"&&(d["riders"] as JsonArray??[]).OfType<JsonObject>().Any(r=>r.S("rider")==rider&&r.S("salaryApprovedAt")!="")))
                .Select(d=>d.S("period"));
        var runPeriods=Store.ListWhere(c,"run",null,null,("iqama",rider)).Where(r=>r.S("approvedAt")!="").Select(r=>r.S("period"));
        string? latest=cyclePeriods.Concat(runPeriods).Where(p=>p.Length==7).OrderByDescending(p=>p,StringComparer.Ordinal).FirstOrDefault();
        if(string.IsNullOrEmpty(latest))return new JsonObject{["available"]=false};
        var built=Build(c,rider,latest,null,true);
        if(built.S("source")=="none")return new JsonObject{["available"]=false};
        decimal closing=built.N("closing"),net=built.N("net"),carried=built.N("carried");
        return new JsonObject
        {
            ["available"]=true,["period"]=latest,["source"]=built.S("source"),["status"]=built.S("status"),
            ["net"]=net,["carried"]=carried,["closing"]=closing,
            ["direction"]=Direction(closing),["markerAr"]=MarkerAr(closing),["markerEn"]=MarkerEn(closing),["markerUr"]=MarkerUr(closing),
            ["itemsCount"]=(built["shortLines"] as JsonArray)?.Count??0
        };
    }

    public static JsonObject Mobile(DbConnection c,HttpContext ctx,string period)
    {
        if(ctx.Request.Query.Any(q=>q.Key!="period"))throw new ApiError("هوية المندوب تؤخذ من الجلسة فقط.");
        var result=Build(c,FinancialCore.AuthenticatedMobileRider(c,ctx),period,null,true);
        // Strip company/sponsor identifiers from rider-facing statement output (not a legal/company document).
        foreach(var card in (result["cards"] as JsonArray??[]).OfType<JsonObject>()){card.Remove("companyId");card.Remove("sponsorId");}
        if(result["header"] is JsonObject h)foreach(var cyc in (h["cycles"] as JsonArray??[]).OfType<JsonObject>()){cyc.Remove("companyId");cyc.Remove("sponsorId");}
        return result;
    }
    // Web and forms send 1/0; OpenAPI clients send true/false. ASP.NET bool? binding rejects "1".
    public static bool ParseApprovedOnly(string? raw)
    {
        if(string.IsNullOrWhiteSpace(raw))return false;
        raw=raw.Trim();
        if(raw is "1" or "true" or "True" or "TRUE")return true;
        if(raw is "0" or "false" or "False" or "FALSE")return false;
        throw new ApiError("قيمة approvedOnly غير صالحة.",400);
    }

    public static void Map(WebApplication app,RouteGroupBuilder api,Store store)
    {
        api.MapGet("/finance/statement/rider/{iqama}/monthly",(string iqama,string period,HttpContext ctx)=>
        {
            bool approvedOnly=ParseApprovedOnly(ctx.Request.Query["approvedOnly"].FirstOrDefault());
            return store.Read(c=>Build(c,iqama,period,ctx.User,approvedOnly));
        }).WithTags("Payroll").WithSummary("Read scoped monthly rider app cards and explainable net with legacy fallback");
        app.MapGet("/api/rider-mobile/statement",(string period,HttpContext ctx)=>{PushNotifications.AuthenticateAndRateLimit(store,ctx,write:false);return store.Read(c=>Mobile(c,ctx,period));}).WithTags("Riders").WithSummary("Read own approved monthly statement using rider bearer session only");
    }
}
