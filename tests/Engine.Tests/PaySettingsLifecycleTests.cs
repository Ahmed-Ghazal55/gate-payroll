using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class PaySettingsLifecycleTests
{
    [TestMethod]
    [DataRow("pay_terms")][DataRow("pay_scheme")][DataRow("pay_eligibility")][DataRow("pay_setting")]
    public void RejectAndDeleteAreDraftOnlyOwnerOrAdminAndAudited(string kind)
    {
        using var f=new PayFixture();string app=kind=="pay_setting"?"global":"ninja";
        JsonObject Draft()=>kind=="pay_setting"?f.Global():f.Draft(kind,app,kind=="pay_terms"?PayFixture.Terms():kind=="pay_scheme"?PayFixture.Scheme("target_salary"):PayFixture.Eligibility());
        var draft=Draft();var other=PayFixture.Actor("synthetic-stranger","FinancialAccountant");
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaySettings.Discard(c,tx,other,kind,app,draft.S("id"),delete:true)));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaySettings.Discard(c,tx,PayFixture.Maker,kind,app,draft.S("id"),new())));
        var rejected=f.Store.Write((c,tx)=>PaySettings.Discard(c,tx,PayFixture.Maker,kind,app,draft.S("id"),new(){["reason"]="Synthetic correction"}));Assert.AreEqual("rejected",rejected.S("status"));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaySettings.Discard(c,tx,PayFixture.Maker,kind,app,draft.S("id"),delete:true)));
        var deletable=Draft();Assert.IsTrue(f.Store.Write((c,tx)=>PaySettings.Discard(c,tx,PayFixture.Maker,kind,app,deletable.S("id"),delete:true)).B("deleted"));Assert.IsNull(f.Store.Read(c=>Store.Get(c,deletable.S("id"),kind)));
        var final=f.Approve(kind,app,Draft());Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaySettings.Discard(c,tx,PayFixture.Maker,kind,app,final.S("id"),delete:true)));
        Assert.IsTrue(f.Store.Read(c=>Store.List(c,"audit").Any(a=>a.S("action")=="pay.config_deleted"&&a.S("entityId")==deletable.S("id"))));
    }
    [TestMethod]
    public void NinjaMetricsAreValidatedAndExcludedConditionsDoNotApply()
    {
        using var f=new PayFixture();var rules=PayFixture.Eligibility();rules["rules"]![0]!["metric"]="unknown_ninja_metric";Assert.ThrowsExactly<ApiError>(()=>f.Draft("pay_eligibility","ninja",rules.Clone()));
        rules["rules"]![0]!["metric"]="workingHours";rules["rules"]![0]!["included"]=false;
        var approved=f.Approve("pay_eligibility","ninja",f.Draft("pay_eligibility","ninja",rules.Clone()));var scheme=f.Approve("pay_scheme","ninja",f.Draft("pay_scheme","ninja",PayFixture.Scheme("target_salary",approved.S("id"))));var global=f.Approve("pay_setting","global",f.Global());
        var result=PayEvaluator.Evaluate(new("2000000001","ninja","2026-10","sponsored_private",10,1000,0,1),scheme,approved,global);Assert.AreEqual("ready",result.Status);Assert.IsFalse(result.Blockers.Any(b=>b.StartsWith("metric_missing:",StringComparison.Ordinal)));
        Assert.IsTrue(AccessPolicy.Allowed(PayFixture.Checker,"DELETE","/api/pay/apps/ninja/terms/synthetic"));Assert.IsFalse(AccessPolicy.Allowed(PayFixture.Checker,"DELETE","/api/pay/cycles/synthetic"));
    }
}
