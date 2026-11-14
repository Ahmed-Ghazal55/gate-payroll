using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class R10ReviewFixTests
{
    const string Rider="2000000001";
    [TestMethod]
    public void ApproverSkipsOwnReviewsPostsOthersAndUnapprovedSalaryCannotConfirmDebt()
    {
        var(f,input,debt)=PayCycleTests.Setup();using(f)
        {
            var cycle=PayCycleTests.Through(f,input,review:false);string id=cycle.S("id");
            f.Store.Write((c,tx)=>PayCycles.ReviewAll(c,tx,PayFixture.Checker,id));
            var skipped=PayCycleTests.Advance(f,id,7,true);Assert.AreEqual(6m,skipped.N("stage"));Assert.AreEqual(2m,skipped["approvalResult"]!.N("skipped"));
            Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count(j=>j.S("payCycleId")==id)));
            f.Store.Write((c,tx)=>PayCycles.ReviewRider(c,tx,PayFixture.Maker,id,"2000000002",new(){["action"]="approve"}));
            var view=f.Store.Read(c=>PayCycles.View(c,null,PayFixture.Checker,Store.Get(c,id,PayCycles.Kind)!));Assert.IsTrue(view["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve_salaries"));
            var partial=PayCycleTests.Advance(f,id,7,true);Assert.AreEqual("partially_approved",partial.S("status"));Assert.AreEqual(1m,partial["approvalResult"]!.N("approved"));Assert.AreEqual(1m,partial["approvalResult"]!.N("skipped"));
            Assert.AreEqual(Rider,partial["approvalResult"]!["skippedRows"]![0]!.S("rider"));
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PayCycles.DebtDeductionsAct(c,tx,PayFixture.Checker,id,new(){["action"]="confirm",["rider"]=Rider}))).Status);
            var review=f.Store.Read(c=>PayCycles.ReviewView(c,PayFixture.Maker,id,"",""));var item=review["rows"]!.AsArray().Single(r=>r!.S("rider")==Rider)!["items"]!.AsArray().Single(i=>i!.S("key")=="debt:"+debt)!;
            Assert.AreEqual(0m,item.N("amount"));Assert.AreEqual(15m,item.N("requestedAmount"));
            var completed=f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,PayFixture.Actor("synthetic-third-checker","FinancialAccountant"),id,new(){["stage"]=7}));Assert.AreEqual("approved",completed.S("status"));
            Assert.AreEqual(2,f.Store.Read(c=>Store.List(c,"journal").Count(j=>j.S("payCycleId")==id)));
        }
    }
    [TestMethod]
    public void ZeroInstallmentOpeningIsListedAndChosenDeductionPostsWithoutChangingSchedule()
    {
        var(f,input,debt)=PayCycleTests.Setup();using(f)
        {
            f.Store.Write((c,tx)=>{var d=Store.Get(c,debt,"debt",tx)!;d["monthlyInstallment"]=0;Store.Save(c,"debt",d,tx);return true;});
            var cycle=PayCycleTests.Through(f,input);string id=cycle.S("id");PayCycleTests.Advance(f,id,7,true);
            var view=f.Store.Read(c=>PayCycles.DebtDeductionsView(c,null,PayFixture.Maker,id));var item=view["rows"]!.AsArray().Single(r=>r!.S("rider")==Rider)!["items"]!.AsArray().Single()!;
            Assert.AreEqual(0m,item.N("due"));Assert.AreEqual(0m,item.N("amount"));Assert.AreEqual(50m,item.N("max"));
            Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PayCycles.DebtDeductionsAct(c,tx,PayFixture.Maker,id,new(){["action"]="edit",["rider"]=Rider,["key"]="debt:"+debt,["amount"]=25})));
            f.Store.Write((c,tx)=>PayCycles.DebtDeductionsAct(c,tx,PayFixture.Maker,id,new(){["action"]="edit",["rider"]=Rider,["key"]="debt:"+debt,["amount"]=25,["reason"]="Opening debt agreed deduction"}));
            PayCycleTests.Advance(f,id,8,true);var saved=f.Store.Read(c=>Store.Get(c,debt,"debt")!);
            Assert.AreEqual(25m,saved.N("remainingBalance"));Assert.AreEqual(0m,saved.N("monthlyInstallment"));
            var journals=f.Store.Read(c=>Store.List(c,"journal").Where(j=>j.S("payCycleId")==id).ToList());Assert.IsTrue(journals.All(j=>j.N("totalDebit")==j.N("totalCredit")));
            Assert.AreEqual(25m,journals.SelectMany(j=>j["lines"]!.AsArray()).Where(l=>l!.S("debtId")==debt).Sum(l=>l!.N("credit")));
        }
    }
    [TestMethod]
    public void FinalSalaryCarryAppearsAsZeroScheduledDebtInNextMonth()
    {
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            var data=PayFixture.Scheme("monthly_commission");data["parameters"]!["categories"]!["sponsored_private"]!["prorate"]=false;
            var scheme=f.Approve("pay_scheme","hs",f.Draft("pay_scheme","hs",data,"carry-test"));
            f.Store.Write((c,tx)=>{var a=Store.Get(c,"synthetic-choice-hs1","pay_assignment",tx)!;a["schemeId"]=scheme.S("id");Store.Save(c,"pay_assignment",a,tx);return true;});
            var first=PayCycleTests.Through(f,input);string id=first.S("id");f.Store.Write((c,tx)=>PayCycles.ReviewRider(c,tx,PayFixture.Maker,id,Rider,new(){["action"]="approve",["reason"]="Reviewed month one carry"}));PayCycleTests.Advance(f,id,7,true);
            string carry=f.Store.Read(c=>Store.List(c,"debt").Single().S("id"));var next=input.Clone();next["period"]="2026-11";
            var novemberSchemes=new Dictionary<string,string>();foreach(string app in new[]{"keeta","hs"}){f.Approve("pay_terms",app,f.Draft("pay_terms",app,PayFixture.Terms(),period:"2026-11"));foreach(var old in f.Store.Read(c=>Store.List(c,"pay_scheme").Where(s=>s.S("app")==app).ToList()))novemberSchemes[old.S("id")]=f.Approve("pay_scheme",app,f.Draft("pay_scheme",app,old["data"]!.AsObject().Clone(),old.S("code"),"2026-11")).S("id");}
            f.Store.Write((c,tx)=>
            {
                foreach(var review in next["invoices"]!.AsArray().OfType<JsonObject>())
                {
                    string old=review.S("invoiceId");review["invoiceId"]=old+"-november";var invoice=Store.Get(c,old,"platform_invoice",tx)!.Clone();
                    foreach(string key in new[]{"voucherNo","voucherVerifyUrl","payCycleId","journalEntryId","approvedAt","approvedBy","accountingStatus"})invoice.Remove(key);
                    invoice["id"]=review.S("invoiceId");invoice["period"]="2026-11";Store.Save(c,"platform_invoice",invoice,tx);
                    foreach(var account in review["accounts"]!.AsArray().OfType<JsonObject>())
                    {string oldLine=account.S("sourceLineId");account["sourceLineId"]=oldLine+"-november";var line=Store.Get(c,oldLine,"platform_invoice_line",tx)!.Clone();line["id"]=account.S("sourceLineId");line["invoiceId"]=review.S("invoiceId");Store.Save(c,"platform_invoice_line",line,tx);}
                    foreach(var deduction in review["deductions"]!.AsArray().OfType<JsonObject>())deduction["sourceLineId"]=deduction.S("sourceLineId")+"-november";
                }
                foreach(var a in Store.List(c,"pay_assignment",tx)){var copy=a.Clone();copy["id"]=a.S("id")+"-november";copy["period"]="2026-11";copy["schemeId"]=novemberSchemes[a.S("schemeId")];Store.Save(c,"pay_assignment",copy,tx);}return true;
            });
            var second=PayCycleTests.Through(f,next);var debt=second["deductions"]!.AsArray().OfType<JsonObject>().Single(d=>d.S("source")==carry);
            Assert.AreEqual("debt",debt.S("type"));Assert.AreEqual(0m,debt.N("requestedAmount"));Assert.AreEqual(0m,debt.N("computed"));
            var view=f.Store.Read(c=>PayCycles.DebtDeductionsView(c,null,PayFixture.Maker,second.S("id")));Assert.IsTrue(view["rows"]!.AsArray().SelectMany(r=>r!["items"]!.AsArray()).Any(d=>d!.S("source")==carry&&d.N("due")==0));
        }
    }
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
            PayCycleTests.Capture(f,a.S("id"));PayCycleTests.Capture(f,b.S("id"));
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
            PayCycleTests.Capture(f,cycle.S("id"));
            f.Store.Write((c,tx)=>{var d=Store.Get(c,debt,"debt",tx)!;d["remainingBalance"]=d.N("remainingBalance")+1;Store.Save(c,"debt",d,tx);return true;});
            Assert.ThrowsExactly<ApiError>(()=>PayCycleTests.Advance(f,cycle.S("id"),1));
        }
    }
}
