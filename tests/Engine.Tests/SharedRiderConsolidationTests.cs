using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.Json.Nodes;

[TestClass]
public sealed class SharedRiderConsolidationTests
{
    [TestMethod]
    public void CombinedNetSumsAppsIncludingNegative()
    {
        var doc=new JsonObject{["apps"]=new JsonObject{
            ["ninja"]=new JsonObject{["net"]=100m},
            ["keeta"]=new JsonObject{["net"]=-30m}}};
        Assert.AreEqual(70m,SharedRiderConsolidation.CombinedNet(doc));
    }

    [TestMethod]
    public void ReleaseMarksReleasedWithReason()
    {
        using var f=new PayFixture();
        string rider="2000000001",period="2026-10",company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>
        {
            var doc=SharedRiderConsolidation.Ensure(c,tx,rider,period,company);
            doc["apps"]=new JsonObject{["a"]=new JsonObject{["net"]=50,["apps"]=new JsonArray("ninja")}};
            doc["missingApps"]=new JsonArray("keeta");
            Store.Save(c,SharedRiderConsolidation.Kind,doc,tx);
            return true;
        });
        var actor=HungerStationInvoiceTests.Actor("synthetic-checker");
        var released=f.Store.Write((c,tx)=>SharedRiderConsolidation.Release(c,tx,actor,rider,period,"وصلت فاتورة كيتا يدويًا"));
        Assert.IsTrue(released.B("released"));
        Assert.AreEqual("released",released.S("status"));
        Assert.AreEqual(50m,released.N("combinedNet"));
    }

    [TestMethod]
    public void HeldTagDetected()
    {
        Assert.IsTrue(SharedRiderConsolidation.Held(new JsonObject{["tag"]=SharedRiderConsolidation.Tag}));
        Assert.IsTrue(SharedRiderConsolidation.Held(new JsonObject{["sharedRider"]=true}));
        Assert.IsFalse(SharedRiderConsolidation.Held(new JsonObject{["rider"]="2000000001"}));
    }

    [TestMethod]
    public void QueueItemHasArabicLabelNoPersonalNames()
    {
        var item=SharedRiderConsolidation.QueueItem(new JsonObject{["rider"]="2000000001",["period"]="2026-10",["status"]="open",["missingApps"]=new JsonArray("keeta"),["apps"]=new JsonObject()});
        Assert.AreEqual(SharedRiderConsolidation.QueueLabelAr,item.S("labelAr"));
        Assert.IsFalse(item.ToJsonString().Contains("Synthetic",StringComparison.Ordinal));
    }
}
