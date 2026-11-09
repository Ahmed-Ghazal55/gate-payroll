using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class HousingChargeTests
{
    static JsonObject Values()=>PaySettings.Defaults()["values"]!.AsObject();
    [TestMethod]
    [DataRow("2026-10-01","2026-10-15",15)]
    [DataRow("2026-10-01","2026-10-31",30)]
    [DataRow("2026-09-15","2026-10-04",4)]
    [DataRow("2026-10-01","",30)]
    public void ResidenceDaysClipToPayrollMonth(string from,string to,int expected)
        =>Assert.AreEqual(expected,PayEvaluator.ResidenceDays(from,to,"2026-10",Values()));
    [TestMethod]
    public void HousingChargeProratesAndCapsAtMonthlySetting()
    {
        var v=Values();Assert.AreEqual(200m,v.N("housingMonthly"));
        Assert.AreEqual(100m,PayEvaluator.HousingCharge(15,"2026-10",v));
        Assert.AreEqual(200m,PayEvaluator.HousingCharge(30,"2026-10",v));
        Assert.AreEqual(200m,PayEvaluator.HousingCharge(40,"2026-10",v));
        Assert.AreEqual(0m,PayEvaluator.HousingCharge(0,"2026-10",v));
    }
    [TestMethod]
    public void CycleAppliesHousingFromResidenceAndSkipsManualHousingDebt()
    {
        var (f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            string rider="2000000001",company=input.S("companyId");
            f.Store.Write((c,tx)=>{
                Store.Save(c,"housing",new(){["id"]="synthetic-housing",["companyId"]=company},tx);
                Store.Save(c,"housing_residence",new(){["id"]="synthetic-residence",["housingId"]="synthetic-housing",["riderId"]=rider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-15"},tx);
                Store.Save(c,"debt",new(){["id"]="synthetic-housing-debt",["iqama"]=rider,["category"]="HousingDeduction",["creditorSponsorId"]=company,["status"]="active",["remainingBalance"]=200,["paidAmount"]=0,["totalAmount"]=200,["monthlyInstallment"]=200,["effectiveFrom"]="2026-01"},tx);
                return true;
            });
            var cycle=PayCycleTests.Through(f,input);
            var housing=cycle["deductions"]!.AsArray().OfType<JsonObject>().Where(d=>d.S("type")=="housing"&&d.S("rider")==rider).ToList();
            Assert.AreEqual(1,housing.Count);Assert.AreEqual(-100m,housing[0].N("amount"));Assert.AreEqual(15m,housing[0].N("housingDays"));
            Assert.IsFalse(cycle["deductions"]!.AsArray().OfType<JsonObject>().Any(d=>d.S("type")=="debt"&&d.S("debtCategory")=="HousingDeduction"));
            Assert.AreEqual(cycle["riders"]!.AsArray().OfType<JsonObject>().Single(r=>r.S("rider")==rider).N("net"),
                cycle["riders"]!.AsArray().OfType<JsonObject>().Single(r=>r.S("rider")==rider).N("afterCommission")+cycle["riders"]!.AsArray().OfType<JsonObject>().Single(r=>r.S("rider")==rider).N("phase1")+cycle["riders"]!.AsArray().OfType<JsonObject>().Single(r=>r.S("rider")==rider).N("phase2")+cycle["riders"]!.AsArray().OfType<JsonObject>().Single(r=>r.S("rider")==rider).N("phase3"));
        }
    }
}
