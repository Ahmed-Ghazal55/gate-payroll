using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
namespace Payroll;

/// <summary>
/// NFLOW1 next stage after invoice approval: split each user/ID invoice due across riders who worked it,
/// reading live ERP ops (assignments, daily, fuel, vehicle custody) and applying internal schemes.
/// Invoice amounts never change; differences and no-ops accounts are accountant lines.
/// </summary>
public static class PayDuesSplit
{
    public static JsonObject Build(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string app,string period,string companyId,string? invoiceId=null,Func<string,IEnumerable<JsonObject>>? liveOps=null)
    {
        app=PayPlatforms.App(app);period=J.Period(period);string company=FinancialCore.ResolveSponsor(companyId).Id;
        OperationsImports.Scope(actor,app,company,"pay.view");
        var invoices=Store.List(c,"platform_invoice",tx).Where(i=>InvoiceDiscard.Active(i)&&PayPlatforms.For(i.S("platform")).App==app&&i.S("companyId")==company&&i.S("period")==period&&i.S("status")!="reversed"&&i.S("accountingStatus")!="reversed"&&(invoiceId==null||i.S("id")==invoiceId)).OrderBy(i=>i.S("id"),StringComparer.Ordinal).ToList();
        J.Need(invoices.Count>0,"ارفع واعتمد فاتورة للشركة والشهر أولًا.",404);

        var openCycle=Store.List(c,PayCycles.Kind,tx).FirstOrDefault(p=>p.S("companyId")==company&&p.S("period")==period&&p.S("status") is not ("cancelled" or "reversed")&&(p.S("app")==app||(p["invoices"] as JsonArray??[]).OfType<JsonObject>().Any(i=>i.S("app")==app))&&PayRunInputs.Captured(p));
        List<JsonObject> Ops(string kind)
        {
            if(openCycle!=null)return PayRunInputs.List(c,tx,openCycle.S("id"),kind);
            if(liveOps==null)return Store.List(c,kind,tx);
            try{return liveOps(kind).ToList();}catch(ApiError){return [];}
        }
        var daily=Ops("ninja_daily").Where(d=>d.S("companyId")==company&&d.S("date").StartsWith(period+"-",StringComparison.Ordinal)).ToList();
        var vehicleAssignments=Ops("vehicle_assignment");
        var vehicles=Ops("vehicle");
        // Live Map returns the same ERP fuel list for fuel_usage/fuel_consumption/fuel aliases — keep each source record once.
        var fuel=DedupeBySourceRecordId(Ops("fuel_usage").Concat(Ops("fuel_consumption")).Concat(Ops("fuel")));
        var people=Store.List(c,"rider",tx).ToDictionary(r=>r.S("id"),StringComparer.Ordinal);

        var accounts=new JsonArray();var riderSplits=new JsonArray();var accountantLines=new JsonArray();var setup=new JsonArray();
        int userCount=0,riderSplitCount=0,fuelRows=0,custodyRows=0;
        var (weight,tie)=PayReviewCategory.AllocationSetting(c,tx,app,period);
        // Fuel/custody attach once per rider across accounts (never double-count the same ERP rows).
        var fuelAttributed=new HashSet<string>(StringComparer.Ordinal);
        var custodyAttributed=new HashSet<string>(StringComparer.Ordinal);

        bool termsOk=TryResolve(c,tx,"pay_terms",app,period,out _);
        bool eligibilityOk=TryResolve(c,tx,"pay_eligibility",app,period,out _);
        bool globalOk=TryGlobal(c,tx,period+"-01",out _);
        if(!termsOk)setup.Add(SetupItem("pay_terms_missing","شروط الأجر غير معتمدة لهذا الشهر","apply_default_settings","يمكن اعتماد قالب افتراضي بنقرة واحدة."));
        if(!eligibilityOk)setup.Add(SetupItem("pay_eligibility_missing","قواعد الاستحقاق غير معتمدة لهذا الشهر","apply_default_settings","يمكن اعتماد قالب افتراضي بنقرة واحدة."));
        if(!globalOk)setup.Add(SetupItem("global_settings_missing","الإعدادات العامة للأيام والإيجار غير معتمدة","apply_default_settings","يمكن اعتماد قالب افتراضي بنقرة واحدة."));

        foreach(var invoice in invoices)
        {
            if(invoice.S("approvalStatus")!="approved")
            {
                accountantLines.Add(new JsonObject{["kind"]="invoice_pending",["invoiceId"]=invoice.S("id"),["message"]="الفاتورة بانتظار الاعتماد؛ لا يوزع المستحق قبل الاعتماد.",["decision"]="wait"});
                continue;
            }
            foreach(var line in NinjaInvoices.Lines(c,tx,invoice.S("id")).Where(l=>l.S("lineType")!="company"))
            {
                userCount++;
                string accountId=line.S("platformAccountId"),source=line.S("id");
                // User invoice due = base + incentives − all line cuts (account- and company-level).
                decimal due=J.Money(line.N("baseGross")+line.N("incentives")-line.N("deductionAmount"));
                decimal splitGross=J.Money(line.N("baseGross")+line.N("incentives"));
                decimal splitDeduction=line.N("deductionAmount");
                var local=openCycle!=null?PayRunInputs.Get(c,tx,openCycle.S("id"),"platform_account",accountId):Store.Get(c,accountId,"platform_account",tx);
                var rows=daily.Where(d=>d.S("platformAccountId")==accountId).ToList();
                var erpOrders=rows.Sum(d=>d.N("orders"));
                var invoiceOrders=line.N("orders");

                if(line.N("difference")!=0||(rows.Count>0&&erpOrders!=invoiceOrders))
                {
                    accountantLines.Add(new JsonObject
                    {
                        ["kind"]="orders_or_amount_difference",
                        ["invoiceId"]=invoice.S("id"),
                        ["sourceLineId"]=source,
                        ["invoiceOrders"]=invoiceOrders,
                        ["erpOrders"]=rows.Count>0?erpOrders:null,
                        ["invoiceDue"]=due,
                        ["engineDifference"]=line.N("difference"),
                        ["decisionDefault"]="accept_invoice",
                        ["message"]="فرق بين الفاتورة ومصدر التشغيل؛ الفاتورة هي المعتمدة (قبول افتراضي أو تصنيف للمحاسب)."
                    });
                }

                if(rows.Count==0)
                {
                    // Brief I: accounts without ops → separate accountant line; never auto-pay even with one assignment.
                    var assigned=(local?["assignments"] as JsonArray??[]).OfType<JsonObject>().Where(a=>a.S("riderId")!=""&&AssignmentSpan.Overlaps(a,period)).Select(a=>a.S("riderId")).Distinct(StringComparer.Ordinal).ToList();
                    accountantLines.Add(new JsonObject
                    {
                        ["kind"]="no_ops_account",
                        ["invoiceId"]=invoice.S("id"),
                        ["sourceLineId"]=source,
                        ["invoiceOrders"]=invoiceOrders,
                        ["invoiceDue"]=due,
                        ["assigneeCount"]=assigned.Count,
                        ["decisionDefault"]="hold_for_accountant",
                        ["message"]="حساب بلا بيانات تشغيل؛ سطر منفصل للمحاسب ولا يوقف بقية التوزيع."
                    });
                    accounts.Add(AccountRow(source,accountId,line,due,[]));
                    continue;
                }

                var groups=rows.GroupBy(d=>d.S("riderId")).OrderBy(g=>g.Key,StringComparer.Ordinal).ToList();
                var metrics=groups.Select(g=>new PayReviewCategory.OperatorMetrics(g.Key,g.Sum(d=>d.N("orders")),g.Select(d=>d.S("date")[..10]).Distinct().Count(),1m,0m)).ToList();
                // Parts must sum exactly to invoiceDue (incentives in pool; company-level cuts in deduction).
                var proposed=PayReviewCategory.Propose(metrics,invoiceOrders,splitGross,splitDeduction,weight,tie);
                var shares=new JsonArray();
                foreach(var prop in proposed.OfType<JsonObject>())
                {
                    string riderId=prop.S("rider");
                    decimal partDue=J.Money(prop.N("baseGross")-prop.N("deduction"));
                    var share=RiderShare(c,tx,people,riderId,period,app,local,liveOps,prop.N("orders"),prop.N("baseGross"),prop.N("deduction"),partDue,weight,tie,vehicleAssignments,vehicles,fuel,fuelAttributed,custodyAttributed,ref fuelRows,ref custodyRows,setup);
                    share["sourceLineId"]=source;share["accountId"]=accountId;share["erpOrders"]=prop.N("erpOrders");
                    shares.Add(share);riderSplits.Add(share.DeepClone());riderSplitCount++;
                }
                accounts.Add(AccountRow(source,accountId,line,due,shares.OfType<JsonObject>().ToList()));
            }
        }

        var actions=new JsonArray("refresh");
        if(setup.OfType<JsonObject>().Any(s=>s.S("fixAction")=="apply_default_settings"))actions.Add("apply_default_settings");
        if(accounts.Count>0)actions.Add("open_cycle_draft");

        return new()
        {
            ["app"]=app,
            ["period"]=period,
            ["companyId"]=company,
            ["stage"]="split_dues",
            ["titleAr"]="فصل مستحقات اليوزرات وتوزيعها على المناديب",
            ["invoiceIds"]=new JsonArray(invoices.Select(i=>(JsonNode?)i.S("id")).ToArray()),
            ["accounts"]=accounts,
            ["riderSplits"]=riderSplits,
            ["accountantLines"]=accountantLines,
            ["setupChecklist"]=setup,
            ["counts"]=new JsonObject
            {
                ["users"]=userCount,
                ["riderSplits"]=riderSplitCount,
                ["fuelRows"]=fuelRows,
                ["custodyRows"]=custodyRows,
                ["accountantLines"]=accountantLines.Count,
                ["setupLeft"]=setup.Count
            },
            ["allocation"]=new JsonObject{["weight"]=weight,["tieBreak"]=tie},
            ["allowedActions"]=actions
        };
    }

    static JsonObject AccountRow(string source,string accountId,JsonObject line,decimal due,IReadOnlyList<JsonObject> shares)
        =>new()
        {
            ["sourceLineId"]=source,
            ["accountId"]=accountId,
            ["captainId"]=line.S("captainId"),
            ["orders"]=line.N("orders"),
            ["baseGross"]=line.N("baseGross"),
            ["incentives"]=line.N("incentives"),
            ["deductionAmount"]=line.N("deductionAmount"),
            ["invoiceDue"]=due,
            ["difference"]=line.N("difference"),
            ["riders"]=new JsonArray(shares.Select(s=>(JsonNode?)s.Clone()).ToArray())
        };

    static JsonObject RiderShare(DbConnection c,DbTransaction? tx,Dictionary<string,JsonObject> people,string riderId,string period,string app,JsonObject? account,Func<string,IEnumerable<JsonObject>>? liveOps,decimal orders,decimal baseGross,decimal deduction,decimal due,string weight,string tie,List<JsonObject> vehicleAssignments,List<JsonObject> vehicles,List<JsonObject> fuel,HashSet<string> fuelAttributed,HashSet<string> custodyAttributed,ref int fuelRows,ref int custodyRows,JsonArray setup)
    {
        string category="";string schemeId="";string payModel="";string ninjaType="";string schemeType="";string omarPayment="";
        JsonObject? schemeCell=null;
        JsonObject? assignmentOnAccount=null;
        if(account!=null)
        {
            assignmentOnAccount=(account["assignments"] as JsonArray??[]).OfType<JsonObject>()
                .Where(a=>a.S("riderId")==riderId&&AssignmentSpan.Overlaps(a,period))
                .OrderByDescending(a=>a.S("RawEffectiveFrom",a.S("EffectiveFrom",a.S("effectiveFrom"))),StringComparer.Ordinal)
                .FirstOrDefault();
            omarPayment=NormalizeOmarPayment(
                assignmentOnAccount?.S("PaymentModel",assignmentOnAccount.S("paymentModelCode",assignmentOnAccount.S("assignmentPaymentModel")))
                ??account.S("paymentModelCode",account.S("accountPaymentModel")));
            ninjaType=NormalizeNinjaRiderType(
                assignmentOnAccount?.S("NinjaRiderType",assignmentOnAccount.S("ninjaRiderTypeCode",assignmentOnAccount.S("ninjaRiderType")))??"");
        }

        if(people.TryGetValue(riderId,out var person))
        {
            category=PayReviewCategory.Derive(c,tx,person,period,liveOps);
            if(ninjaType==""&&PaySettings.Categories.Contains(category))ninjaType=NinjaTypeFromCategory(category);
            try
            {
                var assignment=PayAssignments.Resolve(c,tx,riderId,app,period);
                schemeId=assignment.S("schemeId");
                var scheme=Store.Get(c,schemeId,"pay_scheme",tx);
                if(scheme!=null)
                {
                    schemeType=scheme["data"].S("type");
                    payModel=PayModelFromScheme(schemeType,omarPayment);
                    if(PaySettings.Categories.Contains(category))
                        schemeCell=scheme["data"]?["parameters"]?["categories"]?[category] as JsonObject;
                }
            }
            catch(ApiError)
            {
                // No approved scheme choice yet — still apply Omar payment method (brief J).
                payModel=PayModelFromOmar(omarPayment);
                if(!setup.OfType<JsonObject>().Any(s=>s.S("code")=="scheme_assignment_missing"&&s.S("sourceLineId")==riderId))
                    setup.Add(SetupItem("scheme_assignment_missing","اختيار نظام داخلي معتمد قبل بداية الشهر غير موجود لهذا المندوب","import_scheme_choices","يُستورد من نماذج الاختيار أو يُربط يدويًا؛ لا يُخترع من الفاتورة.",riderId));
            }
        }
        else setup.Add(SetupItem("rider_not_found","مندوب التشغيل غير موجود في السجلات","review_rider_record","",riderId));

        if(payModel==""&&omarPayment!="")payModel=PayModelFromOmar(omarPayment);

        var custodyDetail=new JsonArray();
        if(custodyAttributed.Add(riderId))
        {
            var custody=vehicleAssignments.Where(a=>a.S("riderId")==riderId&&PayReviewCategory.Overlaps(a.S("startedAt",a.S("EffectiveFrom")),a.S("endedAt",a.S("EffectiveTo")),period)).ToList();
            custodyRows+=custody.Count;
            foreach(var a in custody)
            {
                var vehicle=vehicles.FirstOrDefault(v=>v.S("id")==a.S("vehicleId"))??(a.S("vehicleId")==""?null:Store.Get(c,a.S("vehicleId"),"vehicle",tx));
                string type=vehicle?.S("vehicleType")??"";
                custodyDetail.Add(new JsonObject{["vehicleId"]=a.S("vehicleId"),["vehicleType"]=type is "1" or "Motorcycle"?"bike":type is "2" or "Car"?"car":type,["from"]=a.S("startedAt",a.S("EffectiveFrom")),["to"]=a.S("endedAt",a.S("EffectiveTo"))});
            }
        }

        JsonObject fuelNode;
        if(fuelAttributed.Add(riderId))
        {
            var fuelForRider=fuel.Where(f=>f.S("riderId")==riderId||f.S("iqama")==riderId).ToList();
            fuelRows+=fuelForRider.Count;
            decimal fuelLiters=fuelForRider.Sum(f=>f.N("liters",f.N("quantity",f.N("consumptionLiters"))));
            decimal fuelAmount=fuelForRider.Sum(f=>f.N("amount",f.N("cost",f.N("totalAmount"))));
            fuelNode=new JsonObject{["rows"]=fuelForRider.Count,["liters"]=J.Money(fuelLiters),["amount"]=J.Money(fuelAmount),["attributed"]=true};
        }
        else fuelNode=new JsonObject{["rows"]=0,["liters"]=0,["amount"]=0,["attributed"]=false,["alreadyCountedOnOtherAccount"]=true};

        // Bike-on-car: rider holds bike while account category implies car work — flag for scheme cell (brief E).
        bool bikeOnCar=category.Contains("car",StringComparison.OrdinalIgnoreCase)
            &&custodyDetail.OfType<JsonObject>().Any(d=>d.S("vehicleType")=="bike");

        var share=new JsonObject
        {
            ["rider"]=riderId,
            ["orders"]=orders,
            ["baseGross"]=baseGross,
            ["deduction"]=deduction,
            ["due"]=due,
            ["category"]=category,
            ["schemeId"]=schemeId,
            ["schemeType"]=schemeType,
            ["payModel"]=payModel,
            ["omarPaymentMethod"]=omarPayment,
            ["ninjaRiderType"]=ninjaType,
            ["weight"]=weight,
            ["tieBreak"]=tie,
            ["fuel"]=fuelNode,
            ["custody"]=custodyDetail,
            ["bikeOnCar"]=bikeOnCar
        };
        if(schemeCell!=null)share["schemeCell"]=schemeCell.DeepClone();
        return share;
    }

    /// <summary>Omar «طريقة الدفع»: 1/PayPerOrder = commission path; 2/Salary = salary.</summary>
    internal static string NormalizeOmarPayment(string raw)
    {
        raw=(raw??"").Trim();
        return raw switch
        {
            "1" or "PayPerOrder" or "pay_per_order" or "commission" => "PayPerOrder",
            "2" or "Salary" or "salary" => "Salary",
            _ => raw
        };
    }

    internal static string NormalizeNinjaRiderType(string raw)
    {
        raw=(raw??"").Trim();
        return raw switch
        {
            "1" => "InternalCar",
            "2" => "ExternalCar",
            "3" => "InternalMotorcycle",
            "4" => "ExternalMotorcycle",
            _ => raw
        };
    }

    internal static string NinjaTypeFromCategory(string category)=>category switch
    {
        "sponsored_company_car" or "sponsored_private"=>"InternalCar",
        "sponsored_company_bike"=>"InternalMotorcycle",
        "external_company_car" or "external_private"=>"ExternalCar",
        "external_company_bike"=>"ExternalMotorcycle",
        _=>""
    };

    /// <summary>Brief J: internal scheme type by kafala/external/vehicle → commission / percentage / salary.</summary>
    internal static string PayModelFromScheme(string schemeType,string omarPayment)
    {
        if(omarPayment=="Salary"||schemeType=="target_salary")return "salary";
        return schemeType switch
        {
            "percent_of_gross" or "percent_monthly_with_vehicle" => "percentage",
            "daily_commission" or "monthly_commission" => "commission",
            "target_salary" => "salary",
            _ => PayModelFromOmar(omarPayment)
        };
    }

    internal static string PayModelFromOmar(string omarPayment)=>omarPayment switch
    {
        "Salary"=>"salary",
        "PayPerOrder"=>"commission",
        _=>""
    };

    /// <summary>Keep each ERP fuel/ops row once when kind aliases return the same source list (id / sourceId / sourceRecordId).</summary>
    internal static List<JsonObject> DedupeBySourceRecordId(IEnumerable<JsonObject> rows)
    {
        var seen=new HashSet<string>(StringComparer.Ordinal);
        var list=new List<JsonObject>();
        foreach(var row in rows)
        {
            string sourceId=row.S("id",row.S("sourceId",row.S("sourceRecordId")));
            if(sourceId!=""&&!seen.Add(sourceId))continue;
            list.Add(row);
        }
        return list;
    }

    static JsonObject SetupItem(string code,string message,string action,string hint,string source="")
        =>new(){["code"]=code,["message"]=message,["fixAction"]=action,["hint"]=hint,["sourceLineId"]=source};

    static bool TryResolve(DbConnection c,DbTransaction? tx,string kind,string app,string period,out JsonObject? doc)
    {
        try{doc=PaySettings.Resolve(c,tx,kind,app,period);return true;}
        catch(ApiError){doc=null;return false;}
    }
    static bool TryGlobal(DbConnection c,DbTransaction? tx,string on,out JsonObject? doc)
    {
        try{doc=PaySettings.Global(c,tx,on);return true;}
        catch(ApiError){doc=null;return false;}
    }

    /// <summary>One-click: draft+approve missing Ninja pay_terms / pay_eligibility / global settings from catalog defaults (synthetic-safe; never invent rider schemes).</summary>
    public static JsonObject ApplyDefaultSettings(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string app,string period)
    {
        app=PayPlatforms.App(app);period=J.Period(period);
        PaySettings.Scope(actor,"pay.manage",app,true);
        AccessPolicy.Demand(actor,"pay.approve");
        var created=new JsonArray();
        if(!TryResolve(c,tx,"pay_eligibility",app,period,out _))
        {
            // Empty rules = no eligibility gate (display/compare path); avoids inventing thresholds.
            var data=new JsonObject{["name"]="افتراضي — أهلية "+app,["rules"]=new JsonArray()};
            var draft=PaySettings.Create(c,tx,actor,"pay_eligibility",app,new(){["period"]=period,["code"]="default",["data"]=data});
            // Self-approve only with approve_own / bypass (Independent inside ValidateApproval).
            created.Add(PaySettings.Approve(c,tx,actor,"pay_eligibility",app,draft.S("id")).S("id"));
        }
        if(!TryResolve(c,tx,"pay_terms",app,period,out _))
        {
            JsonObject data;
            if(app=="ninja")
            {
                // Parent terms are display/compare only (brief D); seed from Omar/Ninja contract shell so Resolve succeeds.
                var seed=NinjaBilling.Seed();
                data=new(){["name"]=seed.S("name","افتراضي — شروط نينجا"),["billingModel"]="ninja_target",["parameters"]=seed["parameters"]!.DeepClone()};
            }
            else data=new(){["name"]="افتراضي — شروط "+app,["billingModel"]="per_order",["parameters"]=new JsonObject{["pricePerOrder"]=0,["companyCostAccount"]="526402"}};
            var draft=PaySettings.Create(c,tx,actor,"pay_terms",app,new(){["period"]=period,["code"]="default",["data"]=data});
            created.Add(PaySettings.Approve(c,tx,actor,"pay_terms",app,draft.S("id")).S("id"));
        }
        if(!TryGlobal(c,tx,period+"-01",out _))
        {
            var draft=PaySettings.Create(c,tx,actor,"pay_setting","global",new(){["effectiveFrom"]=period+"-01",["fromDefaults"]=true});
            created.Add(PaySettings.Approve(c,tx,actor,"pay_setting","global",draft.S("id")).S("id"));
        }
        Store.Audit(c,tx,"pay.setup_defaults_applied",app+":"+period,actor.Identity!.Name!);
        return new(){["app"]=app,["period"]=period,["approvedIds"]=created,["allowedActions"]=new JsonArray("refresh")};
    }

    public static void Map(RouteGroupBuilder api,Store store,IConfiguration? config=null,IHostEnvironment? env=null)
    {
        api.MapGet("/pay/dues-split",(string app,string period,string companyId,HttpContext ctx)=>
        {
            string? invoiceId=ctx.Request.Query["invoiceId"].FirstOrDefault();
            if(config!=null&&env!=null&&Operations.SourceMode(config,env)==Operations.SourceLive)
            {
                var daily=PayCycles.StagePreCycleKind(ctx,store,config,"ninja_daily",companyId,period,app);
                var assignments=PayCycles.StagePreCycleKind(ctx,store,config,"vehicle_assignment",companyId,period,app);
                var vehicleIds=assignments.Select(a=>a.S("vehicleId")).Where(id=>id!="").ToHashSet(StringComparer.Ordinal);
                var allVehicles=vehicleIds.Count==0?[]:PayCycles.StagePreCycleKind(ctx,store,config,"vehicle",companyId,period,app);
                var vehicles=allVehicles.Where(v=>vehicleIds.Contains(v.S("id"))).ToList();
                List<JsonObject> fuel=[];
                try{fuel=PayCycles.StagePreCycleKind(ctx,store,config,"fuel_usage",companyId,period,app);}catch(ApiError){}
                var live=PayReviewDraft.KindAwareLiveOps(daily,assignments,vehicles);
                Func<string,IEnumerable<JsonObject>> withFuel=kind=>kind switch
                {
                    "fuel_usage" or "fuel_consumption" or "fuel"=>fuel,
                    _=>live(kind)
                };
                return store.Read(c=>Build(c,null,ctx.User,app,period,companyId,invoiceId,withFuel));
            }
            return store.Read(c=>Build(c,null,ctx.User,app,period,companyId,invoiceId));
        }).WithTags("Payroll").WithSummary("Split approved invoice dues across riders from ERP live ops (read-only; invoice unchanged)");

        api.MapPost("/pay/setup-defaults",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>ApplyDefaultSettings(c,tx,ctx.User,input.Required("app"),input.Required("period"))))
            .WithTags("Payroll").WithSummary("One-click draft+approve missing pay terms/eligibility/global defaults for the month");
    }
}
