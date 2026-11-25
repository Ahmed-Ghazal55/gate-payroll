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
    public void ReleaseRequiresRecordedTwoPersonDualWithoutBypass()
    {
        using var f=new PayFixture();
        string rider="2000000001",period="2026-10",company=FinancialCore.Sponsors[0].Id,reason="وصلت فاتورة كيتا يدويًا";
        string consolId=SharedRiderConsolidation.Id(rider,period);
        string payloadHash=ApprovalsArea.PayloadContentHash(consolId,50m,("rider",rider),("period",period),("reason",reason));
        f.Store.Write((c,tx)=>
        {
            var doc=SharedRiderConsolidation.Ensure(c,tx,rider,period,company);
            doc["apps"]=new JsonObject{["a"]=new JsonObject{["net"]=50,["apps"]=new JsonArray("ninja")}};
            doc["missingApps"]=new JsonArray("keeta");
            doc["heldBy"]="synthetic-hold-maker";
            Store.Save(c,SharedRiderConsolidation.Kind,doc,tx);
            return true;
        });
        var actor=HungerStationInvoiceTests.Actor("synthetic-checker");
        // Permission alone is not enough.
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>SharedRiderConsolidation.Release(c,tx,actor,rider,period,reason))).Status);
        // Bypass / self-approval must not unlock release.
        f.Store.Write((c,tx)=>{
            Store.Save(c,"user",new(){["id"]="u-shared-checker",["username"]="synthetic-checker",["role"]="Admin",["customPermissions"]=new JsonArray(ApprovalsArea.DualSharedRiderRelease,"approvals.decide")},tx);
            Store.Save(c,"user",new(){["id"]="u-shared-maker",["username"]="synthetic-hold-maker",["role"]="Admin",["customPermissions"]=new JsonArray(ApprovalsArea.DualSharedRiderRelease,"approvals.decide")},tx);
            Store.Save(c,"user",new(){["id"]="u-shared-checker",["username"]="synthetic-checker",["role"]="Admin",["department"]="FIN",["rankLevel"]=5,["customPermissions"]=new JsonArray(ApprovalsArea.DualSharedRiderRelease,"approvals.decide")},tx);
            AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="synthetic-hold-maker",["from"]=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd"),["to"]=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).AddDays(7).ToString("yyyy-MM-dd"),["reason"]="shared rider bypass",["scope"]=ApprovalsArea.DualSharedRiderRelease});
            var req=FinancialCore.CreateMutualApproval(c,tx,"shared_rider_release","اعتماد فك حجز مشترك","مرجع اختبار","OPS","FIN",1,consolId,new(){["amountSar"]=50,["fileContentHash"]=payloadHash},"synthetic-hold-maker");
            ApprovalsArea.Decide(c,tx,UserAccess.Principal(new JsonObject{["username"]="synthetic-hold-maker",["role"]="Admin",["department"]="FIN",["rankLevel"]=5,["customPermissions"]=new JsonArray(ApprovalsArea.DualSharedRiderRelease,"approvals.decide")}),req.S("id"),new(){["decision"]="approve",["note"]="self via bypass"});
            return true;
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>SharedRiderConsolidation.Release(c,tx,actor,rider,period,reason))).Status);
        // Two different people, no bypass — release may proceed (hash recomputed from current nets).
        f.Store.Write((c,tx)=>{
            var req=FinancialCore.CreateMutualApproval(c,tx,"shared_rider_release","اعتماد فك حجز مشترك","مرجع اختبار ٢","OPS","FIN",1,consolId,new(){["amountSar"]=50,["fileContentHash"]=payloadHash},"synthetic-hold-maker");
            ApprovalsArea.Decide(c,tx,actor,req.S("id"),new(){["decision"]="approve",["note"]="independent checker"});
            return true;
        });
        var released=f.Store.Write((c,tx)=>SharedRiderConsolidation.Release(c,tx,actor,rider,period,reason));
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
