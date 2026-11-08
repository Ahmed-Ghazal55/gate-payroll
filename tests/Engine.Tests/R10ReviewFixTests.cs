using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class R10ReviewFixTests
{
    static JsonObject SecondCompany(PayFixture f,JsonObject input)
    {
        var other=input.Clone();string company=FinancialCore.Sponsors[1].Id;other["companyId"]=company;
        f.Store.Write((c,tx)=>
        {
            foreach(var review in other["invoices"]!.AsArray().OfType<JsonObject>())
            {
                string oldInvoice=review.S("invoiceId");review["invoiceId"]=oldInvoice+"-other";var invoice=Store.Get(c,oldInvoice,"platform_invoice",tx)!.Clone();invoice["id"]=review.S("invoiceId");invoice["companyId"]=company;invoice.Remove("voucherNo");invoice.Remove("voucherVerifyUrl");Store.Save(c,"platform_invoice",invoice,tx);
                foreach(var account in review["accounts"]!.AsArray().OfType<JsonObject>())
                {
                    string oldAccount=account.S("accountId"),oldLine=account.S("sourceLineId");account["accountId"]=oldAccount+"-other";account["sourceLineId"]=oldLine+"-other";
                    var source=Store.Get(c,oldAccount,"platform_account",tx)!.Clone();source["id"]=account.S("accountId");source["companyId"]=company;
                    foreach(var assignment in source["assignments"]!.AsArray().OfType<JsonObject>())assignment["riderId"]=assignment.S("riderId")=="2000000001"?"2000000003":"2000000004";
                    Store.Save(c,"platform_account",source,tx);var line=Store.Get(c,oldLine,"platform_invoice_line",tx)!.Clone();line["id"]=account.S("sourceLineId");line["invoiceId"]=review.S("invoiceId");line["platformAccountId"]=account.S("accountId");Store.Save(c,"platform_invoice_line",line,tx);
                    foreach(var rider in account["riders"]!.AsArray().OfType<JsonObject>())
                    {
                        string original=rider.S("rider"),id=original=="2000000001"?"2000000003":"2000000004";rider["rider"]=id;
                        var person=Store.Get(c,original,"rider",tx)!.Clone();person["id"]=id;person["iqama"]=id;person["sponsorId"]=company;Store.Save(c,"rider",person,tx);
                        var choice=Store.List(c,"pay_assignment",tx).First(a=>a.S("rider")==original&&a.S("app")==review.S("app")).Clone();choice["id"]=choice.S("id")+"-other";choice["rider"]=id;Store.Save(c,"pay_assignment",choice,tx);
                    }
                }
                foreach(var deduction in review["deductions"]!.AsArray().OfType<JsonObject>())deduction["sourceLineId"]=deduction.S("sourceLineId")+"-other";
            }
            return true;
        });return other;
    }
    [TestMethod]
    public void CompanyAApprovalDoesNotInvalidateCompanyBInOneSharedStore()
    {
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            var other=SecondCompany(f,input);var a=f.Store.Write((c,tx)=>PayCycles.Create(c,tx,PayFixture.Maker,input));var b=f.Store.Write((c,tx)=>PayCycles.Create(c,tx,PayFixture.Maker,other));
            for(int s=1;s<=6;s++)PayCycleTests.Advance(f,a.S("id"),s);f.Store.Write((c,tx)=>PayCycles.ReviewAll(c,tx,PayFixture.Maker,a.S("id")));PayCycleTests.Advance(f,a.S("id"),7,true);PayCycleTests.Advance(f,a.S("id"),8,true);
            for(int s=1;s<=6;s++)PayCycleTests.Advance(f,b.S("id"),s);f.Store.Write((c,tx)=>PayCycles.ReviewAll(c,tx,PayFixture.Maker,b.S("id")));Assert.AreEqual("approved",PayCycleTests.Advance(f,b.S("id"),7,true).S("status"));
            Assert.AreEqual(2,f.Store.Read(c=>Store.List(c,"journal").Count(j=>j.S("payCycleId")!="")));Assert.IsTrue(f.Store.Read(c=>Store.List(c,"journal").All(j=>j.N("totalDebit")==j.N("totalCredit"))));
        }
    }
    [TestMethod]
    public void RelevantRiderDebtChangeStillInvalidatesTheCycle()
    {
        var(f,input,debt)=PayCycleTests.Setup();using(f)
        {
            var cycle=f.Store.Write((c,tx)=>PayCycles.Create(c,tx,PayFixture.Maker,input));
            f.Store.Write((c,tx)=>{var d=Store.Get(c,debt,"debt",tx)!;d["remainingBalance"]=d.N("remainingBalance")+1;Store.Save(c,"debt",d,tx);return true;});
            Assert.ThrowsExactly<ApiError>(()=>PayCycleTests.Advance(f,cycle.S("id"),1));
        }
    }
}
