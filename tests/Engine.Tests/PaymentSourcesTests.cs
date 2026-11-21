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
    public void PurchaseAccrualAndPartialSupplierPaymentTieAndVatNeedsEvidence(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        var supplier=f.Store.Write((c,tx)=>PaymentDocuments.Supplier(c,tx,f.Maker,new(){["companyId"]=f.Company,["nameAr"]="مورد اختبار"}));
        var input=f.Input("purchase",100);input["supplierId"]=supplier.S("id");input["vatAmount"]=15;
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Create(input)).Status);
        input["vatAmount"]=0;var doc=f.Create(input);f.Approve(doc.S("id"));var p=f.Reserve(doc.S("id"),40);f.Dual(p);f.Pay(p.S("id"));
        var ledger=f.Store.Read(c=>PaymentDocuments.SupplierLedger(c,f.Maker,supplier.S("id")));Assert.AreEqual(100m,ledger.N("invoiced"));Assert.AreEqual(40m,ledger.N("paid"));Assert.AreEqual(60m,ledger.N("outstanding"));
    }

}
