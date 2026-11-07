using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class ChefzOperationsTests
{
    static string Company=>OperationsImportTests.Company;
    const string Rider=OperationsImportTests.Rider,Account=OperationsImportTests.AccountId;
    internal static void Seed(Store store)
    {
        OperationsImportTests.Seed(store,"chefz",false);
        store.Write((c,tx)=>{
            var invoice=Store.Get(c,OperationsImportTests.InvoiceId,"platform_invoice",tx)!;invoice["amountExclVat"]=100;invoice["vat"]=15;Store.Save(c,"platform_invoice",invoice,tx);
            var line=Store.Get(c,"synthetic-ops-line","platform_invoice_line",tx)!;line["incentives"]=0;Store.Save(c,"platform_invoice_line",line,tx);
            Store.Save(c,"rider_account_assignment",new(){["id"]="synthetic-chefz-assignment",["erpSourceId"]="SYN-A",["companyId"]=Company,["platformAccountId"]=Account,["riderId"]=Rider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-15"},tx);
            Store.Save(c,"chefz_settlement",new(){["id"]="synthetic-chefz-wallet",["erpSourceId"]="SYN-W",["companyId"]=Company,["platformAccountId"]=Account,["riderId"]=Rider,["assignmentSourceId"]="SYN-A",["periodFrom"]="2026-10-01",["periodTo"]="2026-10-15",["chargedFrom"]="2026-10-01",["chargedTo"]="2026-10-02",["assignedDays"]=2,["gross"]=100,["deductions"]=0,["vat"]=15,["companyAmount"]=10,["riderAmount"]=90,["bonuses"]=20},tx);
            Store.Save(c,"chefz_daily",new(){["id"]="synthetic-chefz-daily",["erpSourceId"]="SYN-D",["companyId"]=Company,["platformAccountId"]=Account,["riderId"]=Rider,["assignmentSourceId"]="SYN-A",["date"]="2026-10-01",["totalValidOffers"]=10,["acceptedOffers"]=8,["declinedOffers"]=1,["missedOffers"]=1},tx);return true;
        });
    }
    internal static JsonObject Input(bool invoice=true,bool operators=true)=>new(){["companyId"]=Company,["period"]="2026-10",["invoiceId"]=invoice?OperationsImportTests.InvoiceId:"",["operators"]=operators?new JsonArray(new JsonObject{["accountId"]=Account,["rider"]=Rider,["category"]="sponsored_private",["orders"]=5}):new JsonArray()};
    [TestMethod]
    public void PulledFactsProduceReusableCycleReviewWithoutWritingOrRecalculatingBonusOrCommission()
    {
        using var f=new PayFixture();Seed(f.Store);f.Approve("pay_terms","chefz",f.Draft("pay_terms","chefz",PayFixture.Terms()));
        var before=f.Store.Read(c=>Store.List(c,"chefz_settlement").Single().ToJsonString());int audits=f.Store.Read(c=>Store.List(c,"audit").Count);
        var view=f.Store.Read(c=>ChefzOperations.View(c,PayFixture.Maker,Input()));Assert.IsTrue(view.B("readOnly"));Assert.IsTrue(view.B("canUseInCycle"),view["errors"]!.ToJsonString());
        var review=view["review"]!.AsObject();Assert.AreEqual(100m,review["accounts"]![0]!.N("baseGross"));Assert.AreEqual(0m,review["accounts"]![0]!.N("incentives"),"source gross already includes bonuses");
        Assert.AreEqual(5m,review["accounts"]![0]!["riders"]![0]!.N("orders"),"actual delivered orders are explicit, never accepted offers");Assert.AreEqual(8m,review["accounts"]![0]!["riders"]![0]!["metrics"]!.N("acceptedOffers"));
        Assert.AreEqual(2m,review["accounts"]![0]!["riders"]![0]!.N("days"));Assert.AreEqual(10m,view["lineage"]![0]!.N("companyAmount"));
        Assert.AreEqual(before,f.Store.Read(c=>Store.List(c,"chefz_settlement").Single().ToJsonString()));Assert.AreEqual(audits,f.Store.Read(c=>Store.List(c,"audit").Count));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,OperationsImports.Kind).Count));
        var cycle=f.Store.Write((c,tx)=>PayCycles.Create(c,tx,PayFixture.Maker,new(){["companyId"]=Company,["period"]="2026-10",["invoices"]=new JsonArray(review.Clone())}));for(int i=1;i<=3;i++)f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,PayFixture.Maker,cycle.S("id"),new(){["stage"]=i}));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count));
    }
    [TestMethod]
    public void MissingInvoiceActualOrdersAndConflictingSourceFactsRemainReadOnlyAndBlocked()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);
        var missing=f.Store.Read(c=>ChefzOperations.View(c,PayFixture.Maker,Input(false,false)));Assert.IsFalse(missing.B("canUseInCycle"));Assert.IsTrue(missing["errors"]!.AsArray().Any(e=>e.S("code")=="approved_invoice_required"));Assert.AreEqual(0m,missing["review"]!["accounts"]![0]!["riders"]![0]!.N("orders"));
        f.Store.Write((c,tx)=>{var duplicate=Store.Get(c,"synthetic-chefz-daily","chefz_daily",tx)!.Clone();duplicate["id"]="synthetic-chefz-daily-copy";Store.Save(c,"chefz_daily",duplicate,tx);return true;});
        var duplicateView=f.Store.Read(c=>ChefzOperations.View(c,PayFixture.Maker,Input()));Assert.IsFalse(duplicateView.B("canUseInCycle"));Assert.AreEqual(1m,duplicateView["counts"]!.N("daily"));
        Assert.IsTrue(duplicateView["errors"]!.AsArray().Any(e=>e.S("code")=="daily_account_rider_date_duplicate"));
        f.Store.Write((c,tx)=>{var row=Store.Get(c,"synthetic-chefz-wallet","chefz_settlement",tx)!;row["periodFrom"]="2026-09-30";Store.Save(c,"chefz_settlement",row,tx);return true;});
        var crossed=f.Store.Read(c=>ChefzOperations.View(c,PayFixture.Maker,Input()));Assert.IsTrue(crossed["errors"]!.AsArray().Any(e=>e.S("code")=="settlement_crosses_month"));
        Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"platform_invoice").Count));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));
    }
    [TestMethod]
    public void SourceClassifiedCompanyCostIsNotLoadedOntoRiderWalletDeduction()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);
        f.Store.Write((c,tx)=>{
            var wallet=Store.Get(c,"synthetic-chefz-wallet","chefz_settlement",tx)!;wallet["gross"]=97;wallet["deductions"]=3;wallet["riderAmount"]=87;Store.Save(c,"chefz_settlement",wallet,tx);
            var invoice=Store.Get(c,OperationsImportTests.InvoiceId,"platform_invoice",tx)!;invoice["deductionsTotal"]=8;Store.Save(c,"platform_invoice",invoice,tx);
            var line=Store.Get(c,"synthetic-ops-line","platform_invoice_line",tx)!;line["deductionAmount"]=8;line["deductions"]=new JsonArray(new JsonObject{["category"]="synthetic-account-cost",["amount"]=3,["level"]="account_level"},new JsonObject{["category"]="synthetic-company-cost",["amount"]=5,["level"]="company_level",["expenseAccount"]="512101"});Store.Save(c,"platform_invoice_line",line,tx);return true;
        });
        var view=f.Store.Read(c=>ChefzOperations.View(c,PayFixture.Maker,Input()));Assert.IsTrue(view.B("canUseInCycle"),view["errors"]!.ToJsonString());
        Assert.AreEqual(100m,view["review"]!["accounts"]![0]!.N("baseGross"));Assert.AreEqual(3m,view["review"]!["accounts"]![0]!["riders"]![0]!.N("deduction"));Assert.AreEqual(2,view["review"]!["deductions"]!.AsArray().Count);
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count));
    }
    [TestMethod][DataRow("financial")][DataRow("negative_deduction")][DataRow("assignment")][DataRow("overlap")][DataRow("duplicate_source")]
    public void InvalidPulledSettlementEvidenceNeverBecomesReadyOrChangesStore(string failure)
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);
        f.Store.Write((c,tx)=>{
            var row=Store.Get(c,"synthetic-chefz-wallet","chefz_settlement",tx)!.Clone();
            if(failure=="financial")row["companyAmount"]=11;
            if(failure=="negative_deduction")row["deductions"]=-1;
            if(failure=="assignment")row["assignmentSourceId"]="SYN-MISSING";
            if(failure is "overlap" or "duplicate_source"){row["id"]="synthetic-wallet-copy";if(failure=="overlap")row["erpSourceId"]="SYN-SECOND";}
            Store.Save(c,"chefz_settlement",row,tx);return true;
        });
        string before=f.Store.Read(c=>FinancialImports.Canonical(new JsonArray(Store.List(c,"chefz_settlement").Select(r=>(JsonNode)r.Clone()).ToArray())));
        var view=f.Store.Read(c=>ChefzOperations.View(c,PayFixture.Maker,Input()));Assert.IsFalse(view.B("canUseInCycle"));Assert.IsTrue(view["errors"]!.AsArray().Count>0);
        Assert.AreEqual(before,f.Store.Read(c=>FinancialImports.Canonical(new JsonArray(Store.List(c,"chefz_settlement").Select(r=>(JsonNode)r.Clone()).ToArray()))));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"platform_dispute").Count));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count));
    }
    [TestMethod]
    public void ViewerScopeDeletedFactsAndFinalInvoiceDifferencesAreExplicit()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);
        var viewer=UserAccess.Principal(new JsonObject{["username"]="synthetic-chefz-viewer",["role"]="Reviewer",["customPermissions"]=new JsonArray("pay.view"),["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(Company),["platforms"]=new JsonArray("Chefz")}});
        Assert.IsTrue(AccessPolicy.Allowed(viewer,"POST","/api/pay/apps/chefz/operations/review"));Assert.IsTrue(f.Store.Read(c=>ChefzOperations.View(c,viewer,Input())).B("canUseInCycle"));
        var other=Input();other["companyId"]=FinancialCore.Sponsors[1].Id;Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>ChefzOperations.View(c,viewer,other)));
        f.Store.Write((c,tx)=>{var row=Store.Get(c,"synthetic-chefz-daily","chefz_daily",tx)!;row["sourceDeleted"]=true;Store.Save(c,"chefz_daily",row,tx);var invoice=Store.Get(c,OperationsImportTests.InvoiceId,"platform_invoice",tx)!;invoice["amountExclVat"]=101;Store.Save(c,"platform_invoice",invoice,tx);var line=Store.Get(c,"synthetic-ops-line","platform_invoice_line",tx)!;line["baseGross"]=101;Store.Save(c,"platform_invoice_line",line,tx);return true;});
        var view=f.Store.Read(c=>ChefzOperations.View(c,viewer,Input()));Assert.IsFalse(view.B("canUseInCycle"));Assert.AreEqual(1m,view["counts"]!.N("deletedIgnored"));Assert.IsTrue(view["notes"]!.AsArray().Any(n=>n.S("code")=="erp_expected_differs_from_final_invoice"));
        Assert.AreEqual(100m,view["review"]!["accounts"]![0]!.N("baseGross"));Assert.AreEqual(101m,f.Store.Read(c=>Store.Get(c,OperationsImportTests.InvoiceId,"platform_invoice")!.N("amountExclVat")),"read projection never replaces final invoice");
    }
}
