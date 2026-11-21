using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class PaymentSourcesTests
{
    [TestMethod][DataRow(false)][DataRow(true)]
    public void FinanceScheduleUsesEffectiveInterestAndExactPrincipalAndCalendar(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        var financier=f.Store.Write((c,tx)=>PaymentContracts.Create(c,tx,f.Maker,"financiers",new(){["companyId"]=f.Company,["nameAr"]="ممول اختبار"}));
        var contract=f.Store.Write((c,tx)=>PaymentContracts.Create(c,tx,f.Maker,"finance-contracts",new(){["companyId"]=f.Company,["nameAr"]="تمويل اختبار",["financierId"]=financier.S("id"),["erpVehicleId"]=Guid.NewGuid().ToString(),["startOn"]="2026-11-01",["months"]=2,["principal"]=1000m,["monthlyEffectiveRate"]=0.01m}));
        var rows=f.Store.Read(c=>PaymentContracts.Schedule(c,f.Maker,"finance-contracts",contract.S("id"))["items"]!.AsArray());
        Assert.AreEqual(2,rows.Count);Assert.AreEqual(497.51m,rows[0]!.N("principal"));Assert.AreEqual(10m,rows[0]!.N("interest"));Assert.AreEqual(502.49m,rows[1]!.N("principal"));Assert.AreEqual(5.02m,rows[1]!.N("interest"));
        var calendar=f.Store.Read(c=>PaymentContracts.DueCalendar(c,f.Maker,f.Company,"2026-11-01","2026-12-31"));Assert.AreEqual(1015.02m,calendar.N("amountDue"));
    }
    [TestMethod][DataRow(false)][DataRow(true)]
    public void WalletFundingExistsOnlyAfterApprovedVoucherAndReconcileDoesNotInventMoney(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        var wallet=f.Store.Write((c,tx)=>PaymentWallets.Create(c,tx,f.Maker,"prepaid",new(){["companyId"]=f.Company,["provider"]="مقدم اختبار",["accountCode"]="112404",["kind"]="mada"}));
        var i=f.Input("mada_topup",50m);i.Remove("lines");i["amount"]=50m;var doc=f.Store.Write((c,tx)=>PaymentWallets.Fund(c,tx,f.Maker,"prepaid",wallet.S("id"),i));
        Assert.AreEqual(0L,f.Scalar("SELECT COUNT(*) FROM pay_prepaid_wallet_movement"));f.Approve(doc.S("id"));var p=f.Reserve(doc.S("id"),50);f.Dual(p);f.Pay(p.S("id"));
        var rec=f.Store.Write((c,tx)=>PaymentWallets.Reconcile(c,tx,f.Maker,"prepaid",wallet.S("id"),new(){["period"]="2026-11",["statementBalance"]=49m,["reference"]="SYN-STMT"}));
        Assert.AreEqual(50m,rec.N("bookBalance"));Assert.AreEqual(1m,rec.N("difference"));Assert.AreEqual("difference",rec.S("result"));Assert.AreEqual(1L,f.Scalar("SELECT COUNT(*) FROM pay_prepaid_wallet_movement"));
    }
    [TestMethod][DataRow(false)][DataRow(true)]
    public void PurchaseAccrualAndPartialSupplierPaymentTieAndVatNeedsEvidence(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        var supplier=f.Store.Write((c,tx)=>PaymentDocuments.Supplier(c,tx,f.Maker,new(){["companyId"]=f.Company,["nameAr"]="مورد اختبار"}));
        var input=f.Input("purchase",100);input["supplierId"]=supplier.S("id");input["vatAmount"]=15;
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Create(input)).Status);
        input["vatAmount"]=0;var doc=f.Create(input);f.Approve(doc.S("id"));var p=f.Reserve(doc.S("id"),40);f.Dual(p);f.Pay(p.S("id"));
        var ledger=f.Store.Read(c=>PaymentDocuments.SupplierLedger(c,f.Maker,supplier.S("id")));Assert.AreEqual(100m,ledger.N("invoiced"));Assert.AreEqual(40m,ledger.N("paid"));Assert.AreEqual(60m,ledger.N("outstanding"));
    }
    [TestMethod][DataRow(false)][DataRow(true)]
    public void PrepaidRentRecognizesOnlyFundedMonthsOnce(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        var supplier=f.Store.Write((c,tx)=>PaymentDocuments.Supplier(c,tx,f.Maker,new(){["companyId"]=f.Company,["nameAr"]="مؤجر اختبار"}));
        var property=f.Store.Write((c,tx)=>PaymentContracts.Create(c,tx,f.Maker,"properties",new(){["companyId"]=f.Company,["nameAr"]="مكتب اختبار",["propertyType"]="office",["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D")}));
        var contract=f.Store.Write((c,tx)=>PaymentContracts.Create(c,tx,f.Maker,"leases",new(){["companyId"]=f.Company,["nameAr"]="إيجار اختبار",["propertyId"]=property.S("id"),["landlordSupplierId"]=supplier.S("id"),["startOn"]="2026-11-01",["months"]=3,["frequencyMonths"]=3,["rentPerPayment"]=300m}));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaymentContracts.RecognizeRent(c,tx,f.Maker,contract.S("id"),"2026-11"))).Status);
        var due=f.Store.Read(c=>PaymentContracts.Schedule(c,f.Maker,"leases",contract.S("id")))["items"]![0]!;f.Approve(due.S("documentId"));var p=f.Reserve(due.S("documentId"),300);f.Dual(p);f.Pay(p.S("id"));
        var first=f.Store.Write((c,tx)=>PaymentContracts.RecognizeRent(c,tx,f.Maker,contract.S("id"),"2026-11"));var again=f.Store.Write((c,tx)=>PaymentContracts.RecognizeRent(c,tx,f.Maker,contract.S("id"),"2026-11"));
        Assert.AreEqual(100m,first.N("amount"));Assert.AreEqual(first.S("journalId"),again.S("journalId"));Assert.AreEqual(1L,f.Scalar("SELECT COUNT(*) FROM pay_rent_recognition"));
    }
    [TestMethod][DataRow(false)][DataRow(true)]
    public void HrFeeAndTrafficBearerSplitsStayExactAndRejectMismatches(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        string employee=Guid.NewGuid().ToString("D"),platform=Guid.NewGuid().ToString("D");
        var bad=new JsonObject{["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-HR",["bearer"]="split",["amount"]=100m,["riderShare"]=40m,["companyShare"]=50m,["erpEmployeeId"]=employee,["erpPlatformAccountId"]=platform,["category"]="WorkPermit",["companyExpenseAccountCode"]="526101"};
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaymentDocuments.BearerDocument(c,tx,f.Maker,"hr_fee",bad))).Status);
        bad["companyShare"]=60m;bad["idempotencyKey"]=Guid.NewGuid().ToString("N");
        var hr=f.Store.Write((c,tx)=>PaymentDocuments.BearerDocument(c,tx,f.Maker,"hr_fee",bad));
        Assert.AreEqual("hr_fee",hr.S("type"));Assert.AreEqual(100m,hr.N("amount"));Assert.AreEqual(2,hr["lines"]!.AsArray().Count);
        Assert.AreEqual(1L,f.Scalar("SELECT COUNT(*) FROM pay_hr_fee_request"));
        Assert.AreEqual(40m,Convert.ToDecimal(f.Scalar("SELECT rider_share FROM pay_hr_fee_request")));
        Assert.AreEqual(60m,Convert.ToDecimal(f.Scalar("SELECT company_share FROM pay_hr_fee_request")));
        var traffic=f.Store.Write((c,tx)=>PaymentDocuments.BearerDocument(c,tx,f.Maker,"traffic_violation",new(){["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-TR",["bearer"]="rider",["amount"]=75m,["riderShare"]=75m,["companyShare"]=0m,["erpEmployeeId"]=employee,["erpPlatformAccountId"]=platform}));
        Assert.AreEqual("traffic_violation",traffic.S("type"));Assert.AreEqual(75m,traffic.N("amount"));
        Assert.AreEqual("112502",f.Store.Read(c=>MoneySchemaFixture.Scalar(c,null,"SELECT a.code FROM pay_document_line l JOIN fin_account a ON a.id=l.account_id WHERE l.document_id=@id AND l.active=1",("@id",traffic.S("id")))!.ToString()));
    }

}
