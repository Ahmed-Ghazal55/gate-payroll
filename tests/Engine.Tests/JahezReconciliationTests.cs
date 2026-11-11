using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class JahezReconciliationTests
{
    static JsonObject Input(string company,string period="2026-10",decimal net=0,decimal vat=0)=>new(){["companyId"]=company,["period"]=period,["invoiceNet"]=net,["invoiceVat"]=vat,["lastTransactionDate"]=period+"-15",["invoiceReference"]="synthetic-jahez-invoice"};
    static ClaimsPrincipal Viewer()=>UserAccess.Principal(new JsonObject{["username"]="synthetic-jahez-viewer",["role"]="FinancialManager",["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(FinancialCore.Sponsors[0].Id)}});
    static ClaimsPrincipal Writer(string name)=>UserAccess.Principal(new JsonObject{["username"]=name,["role"]="Accountant",["department"]="FIN",["rankLevel"]=3,["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(FinancialCore.Sponsors[0].Id)}});
    [TestMethod]
    public void BuildComparesCollectionsAndPaymentsThroughLastTransactionDateWithVatLine()
    {
        using var f=new PayFixture();string company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>{
            Store.Save(c,"platform_account",new(){["id"]="synthetic-jahez-account",["companyId"]=company,["platformCode"]="jahez"},tx);
            Store.Save(c,"jahez_transaction",new(){["id"]="synthetic-tx-1",["accountId"]="synthetic-jahez-account",["companyId"]=company,["riderId"]="2000000001",["occurredAt"]="2026-10-10T12:00:00Z",["deliveryPrice"]=500,["cashAmount"]=100,["driverAdjustment"]=0,["net"]=400},tx);
            Store.Save(c,"jahez_transaction",new(){["id"]="synthetic-tx-after",["accountId"]="synthetic-jahez-account",["companyId"]=company,["riderId"]="2000000001",["occurredAt"]="2026-10-25T12:00:00Z",["deliveryPrice"]=999,["cashAmount"]=0,["driverAdjustment"]=0,["net"]=999},tx);
            Store.Save(c,"erp_money_movement",new(){["id"]="synthetic-mm-1",["accountId"]="synthetic-jahez-account",["companyId"]=company,["riderId"]="2000000001",["occurredAt"]="2026-10-12T12:00:00Z",["directionCode"]="Out",["purposeCode"]="OnBehalf",["amount"]=50,["dashboardDebtPaid"]=0},tx);
            Store.Save(c,"erp_jahez_operation",new(){["id"]="synthetic-op-1",["accountId"]="synthetic-jahez-account",["companyId"]=company,["riderId"]="2000000001",["executedAt"]="2026-10-11T12:00:00Z",["commission"]=15,["calculatedCommission"]=15},tx);
            return true;
        });
        // companyDue = 500 delivery + 15 commission = 515; −100 cash +50 on behalf = 465 expected
        var doc=f.Store.Write((c,tx)=>JahezReconciliation.Build(c,tx,PayFixture.Maker,new(){["companyId"]=company,["period"]="2026-10",["invoiceNet"]=465,["invoiceVat"]=69.75m,["lastTransactionDate"]="2026-10-20",["invoiceReference"]="synthetic-jahez-invoice"}));
        Assert.AreEqual("draft",doc.S("status"));
        var totals=doc["totals"]!.AsObject();
        Assert.AreEqual(500m,totals.N("deliveryFees"));Assert.AreEqual(100m,totals.N("cashCollections"));Assert.AreEqual(50m,totals.N("paidOnBehalf"));Assert.AreEqual(15m,totals.N("commission"));
        Assert.AreEqual(465m,totals.N("expectedNet"));Assert.AreEqual(0m,totals.N("variance"));
        Assert.IsFalse(doc["details"]!.AsArray().OfType<JsonObject>().Any(d=>d.S("sourceId")=="synthetic-tx-after"),"rows after last-transaction date stay for next invoice");
        var vat=doc["proposedJournals"]!.AsArray().OfType<JsonObject>().Single(j=>j.S("code")=="vat");
        Assert.AreEqual("112801",vat.S("debit"));Assert.AreEqual("213201",vat.S("credit"));Assert.AreEqual(69.75m,vat.N("amount"));
        f.Store.Write((c,tx)=>JahezReconciliation.Submit(c,tx,PayFixture.Maker,doc.S("id")));
        var approved=f.Store.Write((c,tx)=>JahezReconciliation.Approve(c,tx,PayFixture.Checker,doc.S("id")));
        Assert.AreEqual("approved",approved.S("status"));Assert.IsTrue(approved["journalEntryIds"]!.AsArray().Count>=1);
        Assert.IsTrue(f.Store.Read(c=>Store.List(c,"journal").Any(j=>j["lines"]!.AsArray().OfType<JsonObject>().Any(l=>l.S("accountCode")=="213201"))));
    }
    [TestMethod]
    public void VarianceBlocksApprovalUntilWrittenDecision()
    {
        using var f=new PayFixture();string company=FinancialCore.Sponsors[0].Id;
        var doc=f.Store.Write((c,tx)=>JahezReconciliation.Build(c,tx,PayFixture.Maker,Input(company,net:10,vat:1.5m)));
        Assert.AreNotEqual(0m,doc["totals"]!.AsObject().N("variance"));
        f.Store.Write((c,tx)=>JahezReconciliation.Submit(c,tx,PayFixture.Maker,doc.S("id")));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>JahezReconciliation.Approve(c,tx,PayFixture.Checker,doc.S("id"))));
        f.Store.Write((c,tx)=>JahezReconciliation.DecideVariance(c,tx,PayFixture.Maker,doc.S("id"),new(){["varianceDecision"]="فرق مقبول للمراجعة اليدوية"}));
        Assert.AreEqual("approved",f.Store.Write((c,tx)=>JahezReconciliation.Approve(c,tx,PayFixture.Checker,doc.S("id"))).S("status"));
    }
    [TestMethod]
    public void ViewOnlyPrincipalCannotBuildJahezReconciliation()
    {
        using var f=new PayFixture();string company=FinancialCore.Sponsors[0].Id;
        Assert.IsFalse(AccessPolicy.Allowed(Viewer(),"POST","/api/finance/jahez-reconciliations"));
        Assert.IsTrue(AccessPolicy.Allowed(Viewer(),"GET","/api/finance/jahez-reconciliations"));
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>JahezReconciliation.Build(c,tx,Viewer(),Input(company))));
        Assert.AreEqual(403,err.Status);
    }
    [TestMethod]
    public void SecondReportForSameCompanyPeriodIsRefusedAndApproveCannotDoublePost()
    {
        using var f=new PayFixture();string company=FinancialCore.Sponsors[0].Id;
        var first=f.Store.Write((c,tx)=>JahezReconciliation.Build(c,tx,PayFixture.Maker,Input(company,net:0,vat:0)));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>JahezReconciliation.Build(c,tx,PayFixture.Maker,Input(company,net:0,vat:0)))).Status);
        f.Store.Write((c,tx)=>JahezReconciliation.Submit(c,tx,PayFixture.Maker,first.S("id")));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>JahezReconciliation.Build(c,tx,PayFixture.Maker,Input(company,net:0,vat:0)))).Status);
        f.Store.Write((c,tx)=>JahezReconciliation.Approve(c,tx,PayFixture.Checker,first.S("id")));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>JahezReconciliation.Build(c,tx,PayFixture.Maker,Input(company,net:0,vat:0)))).Status);
        int journals=f.Store.Read(c=>Store.List(c,"journal").Count(j=>j.S("transactionType")=="jahez_reconciliation"));
        // Inject a second pending twin that slipped past Build; Approve must still refuse.
        f.Store.Write((c,tx)=>{
            var twin=first.Clone();twin["id"]="jahez-recon:synthetic-duplicate";twin["status"]="pending_approval";twin["submittedBy"]="synthetic-other";Store.Save(c,JahezReconciliation.Kind,twin,tx);return true;
        });
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>JahezReconciliation.Approve(c,tx,PayFixture.Checker,"jahez-recon:synthetic-duplicate"))).Status);
        Assert.AreEqual(journals,f.Store.Read(c=>Store.List(c,"journal").Count(j=>j.S("transactionType")=="jahez_reconciliation")));
    }
    [TestMethod]
    public void ApproverMustDifferFromCreatorAndSubmitterUnlessBypassGrant()
    {
        using var f=new PayFixture();string company=FinancialCore.Sponsors[0].Id;
        var creator=Writer("synthetic-jahez-creator");var submitter=Writer("synthetic-jahez-submitter");var stranger=Writer("synthetic-jahez-stranger");
        // CompatibilityDefaults give Accountant treasury.jahez.receive; DemandWrite accepts it.
        var doc=f.Store.Write((c,tx)=>JahezReconciliation.Build(c,tx,creator,Input(company,net:0,vat:0)));
        f.Store.Write((c,tx)=>JahezReconciliation.Submit(c,tx,submitter,doc.S("id")));
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>JahezReconciliation.Approve(c,tx,creator,doc.S("id")))).Status,"creator cannot approve");
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>JahezReconciliation.Approve(c,tx,submitter,doc.S("id")))).Status,"submitter cannot approve");
        Assert.AreEqual("approved",f.Store.Write((c,tx)=>JahezReconciliation.Approve(c,tx,stranger,doc.S("id"))).S("status"));
        // Second month: creator==submitter can approve only with §35 bypass grant.
        var same=f.Store.Write((c,tx)=>JahezReconciliation.Build(c,tx,creator,Input(company,"2026-11",0,0)));
        f.Store.Write((c,tx)=>JahezReconciliation.Submit(c,tx,creator,same.S("id")));
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>JahezReconciliation.Approve(c,tx,creator,same.S("id")))).Status);
        string day=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd");
        f.Store.Write((c,tx)=>{Store.Save(c,"user",new(){["id"]="synthetic-jahez-creator-user",["username"]="synthetic-jahez-creator",["role"]="Accountant"},tx);AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="synthetic-jahez-creator",["from"]=day,["to"]=day,["reason"]="§35 grant for Jahez self-approve test"});return true;});
        Assert.AreEqual("approved",f.Store.Write((c,tx)=>JahezReconciliation.Approve(c,tx,creator,same.S("id"))).S("status"));
    }
    [TestMethod]
    public void BuildExcludesRowsBeforeInvoiceMonthAcrossEverySource()
    {
        using var f=new PayFixture();string company=FinancialCore.Sponsors[0].Id,account="synthetic-jahez-month-account";
        f.Store.Write((c,tx)=>{
            Store.Save(c,"platform_account",new(){["id"]=account,["companyId"]=company,["platformCode"]="jahez"},tx);
            Store.Save(c,"jahez_transaction",new(){["id"]="synthetic-september-tx",["accountId"]=account,["companyId"]=company,["occurredAt"]="2026-09-20T12:00:00Z",["deliveryPrice"]=100},tx);
            Store.Save(c,"jahez_transaction",new(){["id"]="synthetic-october-tx",["accountId"]=account,["companyId"]=company,["occurredAt"]="2026-10-10T12:00:00Z",["deliveryPrice"]=500},tx);
            Store.Save(c,"erp_money_movement",new(){["id"]="synthetic-september-money",["accountId"]=account,["companyId"]=company,["occurredAt"]="2026-09-20T12:00:00Z",["directionCode"]="Out",["amount"]=20},tx);
            Store.Save(c,"erp_money_movement",new(){["id"]="synthetic-october-money",["accountId"]=account,["companyId"]=company,["occurredAt"]="2026-10-12T12:00:00Z",["directionCode"]="Out",["amount"]=50},tx);
            Store.Save(c,"erp_jahez_operation",new(){["id"]="synthetic-september-op",["accountId"]=account,["companyId"]=company,["executedAt"]="2026-09-20T12:00:00Z",["commission"]=5},tx);
            Store.Save(c,"erp_jahez_operation",new(){["id"]="synthetic-october-op",["accountId"]=account,["companyId"]=company,["executedAt"]="2026-10-11T12:00:00Z",["commission"]=15},tx);
            Store.Save(c,"jahez_earnings_statement",new(){["id"]="synthetic-september-earnings",["accountId"]=account,["companyId"]=company,["from"]="2026-09-01",["to"]="2026-09-30",["tips"]=7},tx);
            Store.Save(c,"jahez_earnings_statement",new(){["id"]="synthetic-october-earnings",["accountId"]=account,["companyId"]=company,["from"]="2026-10-01",["to"]="2026-10-15",["tips"]=10},tx);return true;
        });
        var doc=f.Store.Write((c,tx)=>JahezReconciliation.Build(c,tx,PayFixture.Maker,new(){["companyId"]=company,["period"]="2026-10",["invoiceNet"]=575,["invoiceVat"]=0,["lastTransactionDate"]="2026-10-20",["invoiceReference"]="synthetic-month-bounds"}));
        var totals=doc["totals"]!.AsObject();Assert.AreEqual(500m,totals.N("deliveryFees"));Assert.AreEqual(50m,totals.N("paidOnBehalf"));Assert.AreEqual(15m,totals.N("commission"));Assert.AreEqual(10m,totals.N("bonusesTips"));Assert.AreEqual(575m,totals.N("expectedNet"));
        Assert.IsFalse(doc["details"]!.AsArray().OfType<JsonObject>().Any(d=>d.S("sourceId").Contains("september")));
    }
}
