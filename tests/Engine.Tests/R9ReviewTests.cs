using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class R9ReviewTests
{
    const string Rider="2000000001";
    static JsonObject Review(PayFixture f,string id,JsonObject input)=>f.Store.Write((c,tx)=>PayCycles.ReviewRider(c,tx,PayFixture.Maker,id,Rider,input));
    [TestMethod]
    public void CommissionDeficitWaitsForExplicitReviewThenCarriesOnceAndReversalPreservesOriginalJournal()
    {
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            var data=PayFixture.Scheme("monthly_commission");data["parameters"]!["categories"]!["sponsored_private"]!["prorate"]=false;
            var scheme=f.Approve("pay_scheme","hs",f.Draft("pay_scheme","hs",data,"negative"));
            f.Store.Write((c,tx)=>{var assignment=Store.Get(c,"synthetic-choice-hs1","pay_assignment",tx)!;assignment["schemeId"]=scheme.S("id");Store.Save(c,"pay_assignment",assignment,tx);return true;});
            var cycle=PayCycleTests.Through(f,input);string id=cycle.S("id");Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));
            Assert.AreEqual("partially_approved",PayCycleTests.Advance(f,id,7,true).S("status"),"The ready rider must post while the negative rider waits.");
            Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>{Store.Save(c,"run",new(){["id"]="synthetic-partial-duplicate-run",["iqama"]="2000000002",["period"]="2026-10",["approvedAt"]="2026-11-01"},tx);return true;}),"Partial approval must also prevent duplicate legacy payroll.");
            Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));
            Review(f,id,new(){["action"]="approve",["reason"]="Final deficit accepted after review"});
            cycle=PayCycleTests.Advance(f,id,7,true);Assert.AreEqual("approved",cycle.S("status"));Assert.IsTrue(cycle["riders"]!.AsArray().All(r=>r!.S("status")=="ready"));
            var debt=f.Store.Read(c=>Store.List(c,"debt").Single());Assert.AreEqual("NegativeSalaryCarryover",debt.S("category"));Assert.AreEqual(100m,debt.N("remainingBalance"));Assert.AreEqual("112507",debt.S("receivableAccountCode"));
            var originals=f.Store.Read(c=>Store.List(c,"journal").ToDictionary(j=>j.S("id"),j=>j.ToJsonString()));
            PayCycleTests.Advance(f,id,7,true);Assert.AreEqual(originals.Count,f.Store.Read(c=>Store.List(c,"journal").Count));
            f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,PayFixture.Maker,id,new(){["action"]="reversal_request",["period"]="2026-11",["note"]="Synthetic carry reversal"}));
            f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,PayFixture.Checker,id,new(){["action"]="reversal_approve"}));
            Assert.AreEqual("reversed",f.Store.Read(c=>Store.Get(c,debt.S("id"),"debt")!.S("status")));
            foreach(var old in originals)Assert.AreEqual(old.Value,f.Store.Read(c=>Store.Get(c,old.Key,"journal")!.ToJsonString()));
        }
    }
    [TestMethod]
    public void PersonalDebtIsCappedAfterSalaryAndTheUncoveredBalanceStaysOpen()
    {
        var(f,input,debt)=PayCycleTests.Setup();using(f)
        {
            f.Store.Write((c,tx)=>{var d=Store.Get(c,debt,"debt",tx)!;d["remainingBalance"]=500;d["totalAmount"]=500;d["monthlyInstallment"]=500;Store.Save(c,"debt",d,tx);
                string company=input.S("companyId");FinancialCore.PostJournal(c,tx,"opening","Synthetic additional debt opening","2026-10","synthetic-more-opening",new JsonArray(FinancialCore.CreateJournalLine("1210",company,450,0,iqama:Rider,debtId:debt),FinancialCore.CreateJournalLine("3101",company,0,450)),"synthetic-checker");return true;});
            var cycle=PayCycleTests.Through(f,input);string id=cycle.S("id");Assert.AreEqual(190m,cycle["riders"]![0]!.N("net"));
            PayCycleTests.Advance(f,id,7,true);Assert.AreEqual(500m,f.Store.Read(c=>Store.Get(c,debt,"debt")!.N("remainingBalance")));
            cycle=PayCycleTests.Advance(f,id,8,true);Assert.AreEqual(310m,f.Store.Read(c=>Store.Get(c,debt,"debt")!.N("remainingBalance")));Assert.AreEqual("active",f.Store.Read(c=>Store.Get(c,debt,"debt")!.S("status")));
            var row=cycle["riders"]!.AsArray().OfType<JsonObject>().Single(r=>r.S("rider")==Rider);Assert.AreEqual(0m,row.N("net"));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count(d=>d.S("category")=="NegativeSalaryCarryover")));
            int journals=f.Store.Read(c=>Store.List(c,"journal").Count);PayCycleTests.Advance(f,id,8,true);Assert.AreEqual(journals,f.Store.Read(c=>Store.List(c,"journal").Count));
        }
    }
    [TestMethod]
    public void IneligibleKeetaSalaryCostAndFuelAreCompanyExpenseNotNewPersonalDebt()
    {
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            input["invoices"]!.AsArray().RemoveAt(1);var rules=PayFixture.Eligibility();rules["rules"]![0]!["threshold"]=999;rules["rules"]![0]!["effect"]!["type"]="per_order";
            var approvedRules=f.Approve("pay_eligibility","keeta",f.Draft("pay_eligibility","keeta",rules));var data=PayFixture.Scheme("target_salary",approvedRules.S("id"));
            foreach(var p in data["parameters"]!["categories"]!.AsObject()){p.Value!["fallbackRate"]=2;p.Value!["operatingCost"]=1000;p.Value!["operatingCostAccount"]="421101";}
            var scheme=f.Approve("pay_scheme","keeta",f.Draft("pay_scheme","keeta",data,"protected"));
            f.Store.Write((c,tx)=>
            {
                foreach(int i in new[]{1,2}){var a=Store.Get(c,"synthetic-choice-keeta"+i,"pay_assignment",tx)!;a["schemeId"]=scheme.S("id");Store.Save(c,"pay_assignment",a,tx);}
                string company=input.S("companyId");Store.Save(c,"debt",new(){["id"]="synthetic-fuel",["iqama"]=Rider,["category"]="FuelOverage",["creditorSponsorId"]=company,["receivableAccountCode"]="112511",["effectiveFrom"]="2026-10",["monthlyInstallment"]=0,["remainingBalance"]=150,["totalAmount"]=150,["status"]="active"},tx);
                FinancialCore.PostJournal(c,tx,"opening","Synthetic current fuel source","2026-10","synthetic-fuel-opening",new JsonArray(PayPosting.Line("112511",company,150,0,"keeta",Rider,"2026-10","synthetic-fuel",debt:"synthetic-fuel"),PayPosting.Line("311101",company,0,150,"keeta",Rider,"2026-10","synthetic-fuel")),"synthetic-checker",targetChart:true,payChart:true);return true;
            });
            var cycle=PayCycleTests.Through(f,input);string id=cycle.S("id");Assert.IsTrue(cycle["riders"]!.AsArray().All(r=>r!.N("net")==0));Assert.AreEqual(150m,f.Store.Read(c=>Store.Get(c,"synthetic-fuel","debt")!.N("remainingBalance")));
            PayCycleTests.Advance(f,id,7,true);var journal=f.Store.Read(c=>Store.List(c,"journal").Single(j=>j.S("payCycleId")==id));var lines=journal["lines"]!.AsArray().OfType<JsonObject>().ToList();
            Assert.AreEqual(2000m,lines.Where(l=>l.S("accountCode")=="421101").Sum(l=>l.N("credit")),"D is reported gross for both riders.");
            Assert.AreEqual(2090m,lines.Where(l=>l.S("accountCode")=="512121").Sum(l=>l.N("debit")),"Unrecovered D 1940 plus fuel 150 belongs to the company.");
            Assert.AreEqual(0m,lines.Where(l=>l.S("accountCode")=="212103").Sum(l=>l.N("credit")-l.N("debit")));
            Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count(d=>d.S("category")=="NegativeSalaryCarryover")));Assert.AreEqual(0m,f.Store.Read(c=>Store.Get(c,"synthetic-fuel","debt")!.N("remainingBalance")));Assert.AreEqual(journal.N("totalDebit"),journal.N("totalCredit"));
        }
    }
    [TestMethod]
    public void AdjustmentListRequiresIndependentApprovalAndSignedChangesUseItsChosenAccount()
    {
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            f.Store.Write((c,tx)=>ConfigLists.Upsert(c,tx,PayFixture.Maker,"pay_adjustment_types",new(){["code"]="unapproved",["nameAr"]="Synthetic pending",["values"]=new JsonObject{["account"]="512120",["direction"]="both"}}));
            Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ConfigLists.Approve(c,tx,PayFixture.Maker,"pay_adjustment_types","unapproved")));
            var cycle=PayCycleTests.Through(f,input);string id=cycle.S("id");
            Assert.ThrowsExactly<ApiError>(()=>Review(f,id,new(){["action"]="adjust",["amount"]=10,["itemType"]="unapproved",["reason"]="Pending type refused"}));
            Assert.ThrowsExactly<ApiError>(()=>Review(f,id,new(){["action"]="adjust",["amount"]=10,["itemType"]="bonus",["accountCode"]="512101",["reason"]="Wrong account refused"}));
            Review(f,id,new(){["action"]="adjust",["amount"]=-20,["itemType"]="correction",["reason"]="Correct overpayment"});Review(f,id,new(){["action"]="adjust",["amount"]=30,["itemType"]="compensation",["reason"]="Approved compensation"});
            PayCycleTests.Advance(f,id,7,true);var lines=f.Store.Read(c=>Store.List(c,"journal").Where(j=>j.S("payCycleId")==id).SelectMany(j=>j["lines"]!.AsArray().OfType<JsonObject>()).ToList());
            Assert.AreEqual(20m,lines.Where(l=>l.S("accountCode")=="512101").Sum(l=>l.N("credit")));Assert.AreEqual(30m,lines.Where(l=>l.S("accountCode")=="512120").Sum(l=>l.N("debit")));
        }
    }
}
