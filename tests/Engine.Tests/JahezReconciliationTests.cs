using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class JahezReconciliationTests
{
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
        var doc=f.Store.Write((c,tx)=>JahezReconciliation.Build(c,tx,PayFixture.Maker,new(){["companyId"]=company,["period"]="2026-10",["invoiceNet"]=10,["invoiceVat"]=1.5m,["lastTransactionDate"]="2026-10-15"}));
        Assert.AreNotEqual(0m,doc["totals"]!.AsObject().N("variance"));
        f.Store.Write((c,tx)=>JahezReconciliation.Submit(c,tx,PayFixture.Maker,doc.S("id")));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>JahezReconciliation.Approve(c,tx,PayFixture.Checker,doc.S("id"))));
        f.Store.Write((c,tx)=>JahezReconciliation.DecideVariance(c,tx,PayFixture.Maker,doc.S("id"),new(){["varianceDecision"]="فرق مقبول للمراجعة اليدوية"}));
        Assert.AreEqual("approved",f.Store.Write((c,tx)=>JahezReconciliation.Approve(c,tx,PayFixture.Checker,doc.S("id"))).S("status"));
    }
}
