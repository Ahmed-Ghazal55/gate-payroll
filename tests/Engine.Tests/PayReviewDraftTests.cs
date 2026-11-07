using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class PayReviewDraftTests
{
    internal static readonly string Company=FinancialCore.Sponsors[0].Id;
    internal const string Rider="2000000081",Account="synthetic-r8-account",Invoice="synthetic-r8-invoice",Line="synthetic-r8-line";
    internal static PayFixture Setup()
    {
        var f=new PayFixture();f.Approve("pay_setting","global",f.Global());f.Approve("pay_terms","ninja",f.Draft("pay_terms","ninja",PayFixture.Terms()));
        var eligibility=f.Approve("pay_eligibility","ninja",f.Draft("pay_eligibility","ninja",PayFixture.Eligibility()));var schemeData=PayFixture.Scheme("percent_of_gross",eligibility.S("id"));schemeData["accountingRole"]="principal";var scheme=f.Approve("pay_scheme","ninja",f.Draft("pay_scheme","ninja",schemeData,"synthetic"));
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"rider",new(){["id"]=Rider,["iqama"]=Rider,["sponsorId"]=Company,["isDeliveryRider"]=true,["payCategory"]="sponsored_private"},tx);
            Store.Save(c,"platform_account",new(){["id"]=Account,["companyId"]=Company,["platformCode"]="ninja",["assignments"]=new JsonArray(new JsonObject{["riderId"]=Rider,["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00"})},tx);
            Store.Save(c,"pay_assignment",new(){["id"]="synthetic-r8-choice",["rider"]=Rider,["app"]="ninja",["period"]="2026-10",["status"]="approved",["schemeId"]=scheme.S("id"),["choiceApprovedAt"]="2026-09-29T00:00:00.0000000+00:00"},tx);
            Store.Save(c,"platform_invoice",new(){["id"]=Invoice,["companyId"]=Company,["period"]="2026-10",["platform"]="Ninja",["approvalStatus"]="approved",["status"]="final",["amountExclVat"]=100,["vat"]=15,["deductionsTotal"]=0},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]=Line,["invoiceId"]=Invoice,["platformAccountId"]=Account,["lineType"]="account",["baseGross"]=100,["incentives"]=0,["orders"]=10,["deductionAmount"]=0,["deductions"]=new JsonArray()},tx);
            Store.Save(c,"ninja_daily",new(){["id"]="synthetic-r8-day",["companyId"]=Company,["platformAccountId"]=Account,["riderId"]=Rider,["date"]="2026-10-01",["orders"]=10,["hours"]=8},tx);return true;
        });return f;
    }
    [TestMethod]
    public void DailyRowsWithoutInvoiceAndExclusiveAssignmentEndAreNotSilentlyAccepted()
    {
        using var f=Setup();f.Store.Write((c,tx)=>
        {
            Store.Save(c,"ninja_daily",new(){["id"]="synthetic-r8-uninvoiced-day",["companyId"]=Company,["platformAccountId"]="synthetic-r8-uninvoiced-account",["riderId"]="2000000099",["date"]="2026-10-02",["orders"]=3},tx);
            var account=Store.Get(c,Account,"platform_account",tx)!;account["assignments"]![0]!["RawEffectiveTo"]="2026-10-01T00:00:00+03:00";Store.Save(c,"platform_account",account,tx);return true;
        });
        var draft=f.Store.Read(c=>PayReviewDraft.Build(c,null,PayFixture.Maker,"ninja","2026-10",Company));Assert.IsFalse(draft.B("canCreate"));Assert.AreEqual(2,draft["unassignedOrders"]!.AsArray().Count);Assert.AreEqual(1,draft["ridersNotFound"]!.AsArray().Count);
        Assert.IsTrue(draft["blockers"]!.AsArray().Any(b=>b.S("code")=="daily_account_not_invoiced"));Assert.IsTrue(draft["blockers"]!.AsArray().Any(b=>b.S("code")=="unassigned_orders"));
    }
    [TestMethod]
    public void DailyDraftTiesInvoiceAndActualOperatorWithoutWriting()
    {
        using var f=Setup();string before=f.Store.Read(c=>System.Text.Json.JsonSerializer.Serialize(ReleaseSafety.Capture(c)));
        var draft=f.Store.Read(c=>PayReviewDraft.Build(c,null,PayFixture.Maker,"ninja","2026-10",Company));Assert.IsTrue(draft.B("canCreate"),draft["blockers"]!.ToJsonString());
        var account=draft["invoices"]![0]!["accounts"]![0]!.AsObject();Assert.AreEqual(Line,account.S("sourceLineId"));Assert.AreEqual(100m,account.N("baseGross"));Assert.AreEqual(10m,account["riders"]![0]!.N("orders"));Assert.AreEqual(Line,draft["evidence"]![0].S("sourceLineId"));
        Assert.AreEqual(before,f.Store.Read(c=>System.Text.Json.JsonSerializer.Serialize(ReleaseSafety.Capture(c))));
        var input=new JsonObject{["period"]="2026-10",["companyId"]=Company,["app"]="ninja",["invoices"]=draft["invoices"]!.DeepClone()};var forged=input.Clone();forged["invoices"]![0]!["accounts"]![0]!["riders"]![0]!["operationSourceIds"]=new JsonArray("synthetic-missing-proof");Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>{PayCycles.ValidateImportedReview(c,null,PayFixture.Maker,Company,"2026-10",forged["invoices"]![0]!.AsObject());return true;}));
        Assert.AreEqual("draft",f.Store.Write((c,tx)=>PayCycles.Create(c,tx,PayFixture.Maker,input)).S("status"));
    }
    [TestMethod]
    public void MissingRiderAndUnassignedOrdersReturnActionableSourceBoundBlockers()
    {
        using var f=Setup();f.Store.Write((c,tx)=>{var day=Store.Get(c,"synthetic-r8-day","ninja_daily",tx)!;day["riderId"]="2000000099";Store.Save(c,"ninja_daily",day,tx);return true;});
        var draft=f.Store.Read(c=>PayReviewDraft.Build(c,null,PayFixture.Maker,"ninja","2026-10",Company));Assert.IsFalse(draft.B("canCreate"));Assert.AreEqual(1,draft["unassignedOrders"]!.AsArray().Count);Assert.AreEqual(1,draft["ridersNotFound"]!.AsArray().Count);Assert.IsTrue(draft["blockers"]!.AsArray().OfType<JsonObject>().All(b=>b.S("message")!=""&&b.S("fixAction")!=""));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,PayCycles.Kind).Count));
    }
    [TestMethod]
    public void SavedOperationsAreValidatedAndInvoiceAmountTamperingBlocksCreation()
    {
        using var f=Setup();var draft=f.Store.Read(c=>PayReviewDraft.Build(c,null,PayFixture.Maker,"ninja","2026-10",Company));var review=draft["invoices"]![0]!.DeepClone().AsObject();review["accounts"]![0]!["baseGross"]=101;
        f.Store.Write((c,tx)=>{Store.Save(c,OperationsImports.Kind,new(){["id"]="synthetic-r8-saved-ops",["invoiceId"]=Invoice,["companyId"]=Company,["period"]="2026-10",["app"]="ninja",["review"]=review},tx);return true;});
        var blocked=f.Store.Read(c=>PayReviewDraft.Build(c,null,PayFixture.Maker,"ninja","2026-10",Company));Assert.IsFalse(blocked.B("canCreate"));Assert.IsTrue(blocked["blockers"]!.AsArray().Any(b=>b.S("code")=="operations_review_invalid"));
    }
}
