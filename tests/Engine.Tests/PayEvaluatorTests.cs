using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class PayEvaluatorTests
{
    static JsonObject Values()=>PaySettings.Defaults()["values"]!.AsObject();
    static JsonObject Global()=>new(){["id"]="synthetic-global",["status"]="approved",["effectiveFrom"]="2026-01-01",["data"]=PaySettings.Defaults()};
    static JsonObject Scheme(string type="daily_commission",string app="keeta",string period="2026-10")=>new(){["id"]="synthetic-scheme",["app"]=app,["period"]=period,["status"]="approved",["data"]=PayFixture.Scheme(type)};
    static PayFacts Facts(string category="sponsored_private",int orders=100,decimal gross=1000,decimal incentive=100,decimal days=30,string period="2026-10",string app="keeta",string vehicle="private",decimal? vehicleDays=null,IReadOnlyDictionary<string,decimal?>? metrics=null)=>new("synthetic-rider",app,period,category,orders,gross,incentive,days,vehicle,vehicleDays,metrics);
    static PayEvaluation Eval(string type,PayFacts? facts=null,Action<JsonObject>? change=null)
    {var scheme=Scheme(type);change?.Invoke(scheme["data"]!["parameters"]!["categories"]![facts?.Category??"sponsored_private"]!.AsObject());return PayEvaluator.Evaluate(facts??Facts(),scheme,null,Global());}
    static JsonObject Rules(string effect,string metric="validity",decimal threshold=1)=>new(){["id"]="synthetic-rules",["app"]="keeta",["period"]="2026-10",["status"]="approved",["data"]=new JsonObject{["name"]="Synthetic eligibility",["rules"]=new JsonArray(new JsonObject{["metric"]=metric,["operator"]="gte",["threshold"]=threshold,["effect"]=new JsonObject{["type"]=effect}})}};
    [TestMethod]
    [DataRow("2026-01",30)][DataRow("2026-02",28)][DataRow("2028-02",28)][DataRow("2026-04",30)][DataRow("2026-12",30)]
    public void MonthDenominatorAndFullMonthBilledDaysFollowTheConfiguredCap(string period,int expected)
    {
        var first=DateTimeOffset.Parse(period+"-01T00:00:00+03:00");var next=first.AddMonths(1);Assert.AreEqual(expected,PayEvaluator.DaysInPayrollMonth(period,Values()));Assert.AreEqual(expected,PayEvaluator.AssignmentDays(first,next,period,Values()));
    }
    [TestMethod]
    [DataRow("2026-10-01T11:00:00Z","2026-10-02T11:00:00Z",1)][DataRow("2026-10-01T11:00:01Z","2026-10-02T11:00:00Z",0)][DataRow("2026-10-01T11:00:00Z","2026-10-02T11:00:01Z",2)][DataRow("2026-09-29T00:00:00Z","2026-10-03T11:00:00Z",2)]
    public void AssignmentDaysApplyTheStrictAfter1400RiyadhBoundaryAndClipToThePeriod(string start,string end,int days)
    {Assert.AreEqual(days,PayEvaluator.AssignmentDays(DateTimeOffset.Parse(start),DateTimeOffset.Parse(end),"2026-10",Values()));}
    [TestMethod]
    [DataRow("company_car",15,900)][DataRow("company_bike",15,400)][DataRow("private",30,0)][DataRow("company_car",31,1800)]
    public void VehicleChargeUsesVersionedRentAndCapsDays(string type,int days,int expected)=>Assert.AreEqual((decimal)expected,PayEvaluator.VehicleCharge(type,days,"2026-10",Values()));
    [TestMethod]
    public void VehicleProrationRoundsMidpointsAwayFromZeroAndRatesComeOnlyFromData()
    {
        var v=Values();Assert.AreEqual(28.57m,PayEvaluator.VehicleCharge("company_bike",1,"2028-02",v));v["vehicleRent"]!["company_car"]=1000.05m;Assert.AreEqual(33.34m,PayEvaluator.VehicleCharge("company_car",1,"2026-10",v));v["standardDays"]=20;Assert.AreEqual(20,PayEvaluator.DaysInPayrollMonth("2026-10",v));Assert.AreEqual(1000.05m,PayEvaluator.VehicleCharge("company_car",30,"2026-10",v));Assert.ThrowsExactly<ApiError>(()=>PayEvaluator.VehicleCharge("unknown",1,"2026-10",v));
    }
    [TestMethod]
    [DataRow("daily_commission",800)][DataRow("percent_of_gross",990)][DataRow("monthly_commission",800)]
    public void CommissionFormulaShapesUseExplicitParameters(string type,int expected)
    {var result=Eval(type);Assert.AreEqual((decimal)expected,result.NetBeforePersonal);Assert.AreEqual(1100m-result.Gross,result.CompanyCommission);Assert.AreEqual("ready",result.Status);Assert.AreEqual(result.NetBeforePersonal,result.Lines.Where(l=>l.AffectsNet).Sum(l=>l.Amount));}
    [TestMethod]
    public void PercentMonthlyVehicleUsesGrossBeforeAllDeductionsAndChargesConfiguredVehicleRent()
    {var result=Eval("percent_monthly_with_vehicle",Facts("external_company_bike",gross:2000,incentive:0,days:15,vehicle:"company_bike",vehicleDays:15));Assert.AreEqual(200m,result.CompanyCommission);Assert.AreEqual(400m,result.VehicleCharge);Assert.AreEqual(1400m,result.NetBeforePersonal);}
    [TestMethod]
    public void MonthlyCommissionProrationIsExplicitRatherThanAssumed()
    {var half=Eval("monthly_commission",Facts(days:15));Assert.AreEqual(150m,half.CompanyCommission);var fixedFee=Eval("monthly_commission",Facts(days:15),p=>p["prorate"]=false);Assert.AreEqual(300m,fixedFee.CompanyCommission);}
    [TestMethod]
    public void SalaryBandsExplainExtrasInternetAndFuelCapWithoutPayingUnusedFuel()
    {var result=Eval("target_salary");Assert.AreEqual(310m,result.Gross);Assert.AreEqual(50m,result.FuelCap);Assert.AreEqual(310m,result.NetBeforePersonal);Assert.IsTrue(result.Lines.Any(l=>l.Category=="fuel_cap"&&!l.AffectsNet));Assert.AreEqual(result.NetBeforePersonal,result.Lines.Where(l=>l.AffectsNet).Sum(l=>l.Amount));}
    [TestMethod]
    public void OwnCategoryIsSelectedWithoutBorrowingAnotherCategorysValue()
    {var facts=Facts("external_private");var result=Eval("daily_commission",facts,p=>p["dailyCommission"]=5);Assert.AreEqual(950m,result.NetBeforePersonal);var disabled=Eval("daily_commission",facts,p=>{p.Clear();p["enabled"]=false;});Assert.AreEqual("needs_decision",disabled.Status);CollectionAssert.Contains(disabled.Blockers.ToArray(),"category_not_enabled");}
    [TestMethod]
    public void IncentivesFollowActualOrdersWithExactCentReconciliation()
    {var allocated=PayEvaluator.SplitIncentives([new("synthetic-a",1,10),new("synthetic-b",2,20)],30,10,Values());Assert.AreEqual(1,allocated[0].Orders);Assert.AreEqual(2,allocated[1].Orders);Assert.AreEqual(3.33m,allocated[0].Incentive);Assert.AreEqual(6.67m,allocated[1].Incentive);Assert.AreEqual(10m,allocated.Sum(r=>r.Incentive));Assert.AreEqual(30m,allocated.Sum(r=>r.BaseGross));}
    [TestMethod]
    public void TinyIncentiveSplitsNeverBecomeNegativeOrLoseMoney()
    {var rows=Enumerable.Range(0,1000).Select(i=>new PayRiderShare("synthetic-"+i.ToString("D4"),1,1));var allocated=PayEvaluator.SplitIncentives(rows,1000,5,Values());Assert.AreEqual(5m,allocated.Sum(r=>r.Incentive));Assert.IsTrue(allocated.All(r=>r.Incentive is 0m or 0.01m));Assert.ThrowsExactly<ApiError>(()=>PayEvaluator.SplitIncentives([new("synthetic",0,1)],1,1,Values()));Assert.ThrowsExactly<ApiError>(()=>PayEvaluator.SplitIncentives([new("synthetic",1,1)],2,1,Values()));}
    [TestMethod]
    [DataRow("zero",0)][DataRow("per_order",180)][DataRow("next_band",60)]
    public void FailedEligibilityAppliesTheConfiguredEffectWithinTheOwningApp(string effect,int expected)
    {
        var scheme=Scheme("target_salary");scheme["data"]!["eligibilitySetId"]="synthetic-rules";var p=scheme["data"]!["parameters"]!["categories"]!["sponsored_private"]!.AsObject();p["bands"]=new JsonArray(new JsonObject{["min"]=100,["base"]=200},new JsonObject{["min"]=0,["base"]=50,["internet"]=10});p["fallbackRate"]=2;p["operatingCost"]=20;p["operatingCostAccount"]="421101";
        var result=PayEvaluator.Evaluate(Facts(metrics:new Dictionary<string,decimal?>{["validity"]=0}),scheme,Rules(effect),Global());Assert.AreEqual((decimal)expected,result.NetBeforePersonal);Assert.AreEqual("ready",result.Status);
    }
    [TestMethod]
    public void MissingMetricsAndForeignAppRulesCannotSilentlyBecomeEligible()
    {
        var scheme=Scheme("target_salary");scheme["data"]!["eligibilitySetId"]="synthetic-rules";var missing=PayEvaluator.Evaluate(Facts(),scheme,Rules("zero"),Global());Assert.AreEqual("needs_decision",missing.Status);CollectionAssert.Contains(missing.Blockers.ToArray(),"metric_missing:validity");var foreign=Rules("zero");foreign["app"]="hs";Assert.ThrowsExactly<ApiError>(()=>PayEvaluator.Evaluate(Facts(),scheme,foreign,Global()));
    }
    [TestMethod]
    public void NegativeNetRemainsAnExplainableDecisionWithoutAnyAutomaticDebtOrMutation()
    {
        var scheme=Scheme();var facts=Facts(gross:100,incentive:0);var global=Global();string before=scheme.ToJsonString()+global.ToJsonString();var result=PayEvaluator.Evaluate(facts,scheme,null,global);Assert.AreEqual(-200m,result.NetBeforePersonal);Assert.AreEqual("needs_decision",result.Status);CollectionAssert.Contains(result.Blockers.ToArray(),"negative_net_requires_decision");Assert.AreEqual(before,scheme.ToJsonString()+global.ToJsonString());Assert.IsTrue(result.Lines.All(l=>l.Formula!=""&&l.Label!=""));
    }
    [TestMethod]
    public void MissingVehicleEvidenceDoesNotInferAFullMonthAndDraftSettingsAreRejected()
    {var result=Eval("percent_monthly_with_vehicle",Facts("sponsored_company_car",vehicle:"company_car"));Assert.AreEqual("needs_decision",result.Status);CollectionAssert.Contains(result.Blockers.ToArray(),"vehicle_period_missing");var draft=Scheme();draft["status"]="draft";Assert.ThrowsExactly<ApiError>(()=>PayEvaluator.Evaluate(Facts(),draft,null,Global()));}
}
