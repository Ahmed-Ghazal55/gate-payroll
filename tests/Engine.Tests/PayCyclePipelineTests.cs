using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class PayCyclePipelineTests
{
    [TestMethod]
    public void PipelineExposesStageAmountsAndStage4Items()
    {
        var(f,input,_)=PayCycleTests.Setup();using(f)
        {
            var cycle=PayCycleTests.Through(f,input,6,review:false);
            string id=cycle.S("id");
            var pipe=f.Store.Read(c=>PayCycles.Pipeline(c,null,PayFixture.Maker,id));
            Assert.AreEqual(id,pipe.S("cycleId"));
            Assert.IsTrue(pipe.S("status") is "review" or "needs_decision" or "draft" or "partially_approved" or "approved");
            Assert.IsTrue(pipe["riders"]!.AsArray().Count>=1);
            var rider=pipe["riders"]!.AsArray().OfType<System.Text.Json.Nodes.JsonObject>().First();
            var stages=rider["stages"]!.AsObject();
            Assert.IsTrue(stages.ContainsKey("1_gross"));
            Assert.IsTrue(stages.ContainsKey("4_items"));
            Assert.IsTrue(stages.ContainsKey("6_net"));
            Assert.IsTrue(stages.N("1_gross")>=0);
        }
    }

    /// <summary>M2 review: a stage-4 item is a deduction, not a company expense line; stage 2 is the rider's share of the
    /// account earnings; the remainder is the positive net minus the old-debt instalment, never the closing balance.</summary>
    [TestMethod]
    public void PipelineSeparatesStage4FromExpensesAndComputesRemainder()
    {
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            string company=input.S("companyId"),rider="2000000001",period="2026-10",monthDebt="syn-pipe-month",oldDebt="syn-pipe-old";
            f.Store.Write((c,tx)=>
            {
                Store.Save(c,"debt",new(){["id"]=monthDebt,["iqama"]=rider,["category"]="Advance",["creditorSponsorId"]=company,["receivableAccountCode"]="112501",["status"]="active",["totalAmount"]=40,["remainingBalance"]=40,["paidAmount"]=0,["monthlyInstallment"]=0,["effectiveFrom"]=period,["lastDeductionPeriod"]="",["createdAt"]="2026-10-02T00:00:00Z"},tx);
                Store.Save(c,"debt",new(){["id"]=oldDebt,["iqama"]=rider,["category"]="TrafficFine",["creditorSponsorId"]=company,["receivableAccountCode"]="112502",["status"]="active",["totalAmount"]=90,["remainingBalance"]=90,["paidAmount"]=0,["monthlyInstallment"]=30,["effectiveFrom"]="2026-09",["lastDeductionPeriod"]="",["createdAt"]="2026-09-02T00:00:00Z"},tx);
                FinancialCore.PostJournal(c,tx,"opening","Synthetic pipeline debts",period,"syn-pipe-open",new System.Text.Json.Nodes.JsonArray(FinancialCore.CreateJournalLine("1210",company,40,0,iqama:rider,debtId:monthDebt),FinancialCore.CreateJournalLine("1210",company,90,0,iqama:rider,debtId:oldDebt),FinancialCore.CreateJournalLine("3101",company,0,130,iqama:rider)),"synthetic-opening-checker");
                return true;
            });
            var cycle=PayCycleTests.Through(f,input);
            var pipe=f.Store.Read(c=>PayCycles.Pipeline(c,null,PayFixture.Maker,cycle.S("id")));
            var row=pipe["riders"]!.AsArray().OfType<System.Text.Json.Nodes.JsonObject>().Single(r=>r.S("rider")==rider);
            var stages=row["stages"]!.AsObject();
            var items=stages["4_items"]!.AsArray().OfType<System.Text.Json.Nodes.JsonObject>().ToList();
            var expenses=stages["4_expenseLines"]!.AsArray().OfType<System.Text.Json.Nodes.JsonObject>().ToList();
            Assert.IsTrue(items.Any(i=>i.S("source")==monthDebt&&i.N("amount")==-40m),"this-month advance is a stage-4 item");
            Assert.IsFalse(expenses.Any(e=>e.S("source")==monthDebt),"a stage-4 deduction must not be listed as a company expense");
            Assert.IsFalse(items.Select(i=>i.S("key")).Intersect(expenses.Select(e=>e.S("key"))).Any(),"items and expense lines are disjoint");
            var lines=row["lines"]!.AsArray().OfType<System.Text.Json.Nodes.JsonObject>().ToList();
            decimal share=lines.SelectMany(l=>l["sources"]!.AsArray().OfType<System.Text.Json.Nodes.JsonObject>()).Sum(src=>src.N("baseGross")+src.N("incentive"));
            Assert.IsTrue(share>0);Assert.AreEqual(share,stages.N("2_accountShare"),"stage 2 = the rider's share of account earnings");
            Assert.AreEqual(lines.Sum(l=>l.N("platformPenalties")),stages.N("2_penaltiesAllocated"));
            decimal net=stages.N("6_net"),instalment=stages.N("8_oldDebtInstallment");
            Assert.AreEqual(30m,instalment,"old debt requests its instalment only");
            Assert.AreEqual(Math.Max(0,Math.Max(0,net)-instalment),stages.N("8_remainder"));
            Assert.IsFalse(stages["8_confirmed"]!.GetValue<bool>());
        }
    }

    /// <summary>Live bug 0.23.0: the review draft sends the company code (BW); the capture scope accepted the sponsor id only.</summary>
    [TestMethod]
    public void CaptureScopeAcceptsCompanyCodeAndSponsorId()
    {
        var(f,_,_)=PayCycleTests.Setup();using(f)
        {
            var sponsor=FinancialCore.Sponsors.First(s=>s.Code=="BW");
            var byCode=f.Store.Read(c=>PayCycles.CaptureScope(c,new System.Text.Json.Nodes.JsonObject{["companyId"]="BW"}));
            var byId=f.Store.Read(c=>PayCycles.CaptureScope(c,new System.Text.Json.Nodes.JsonObject{["companyId"]=sponsor.Id}));
            CollectionAssert.AreEqual(byId.SponsorUnifiedNumbers.ToList(),byCode.SponsorUnifiedNumbers.ToList());
            CollectionAssert.AreEqual(byId.EmployeeIds.ToList(),byCode.EmployeeIds.ToList());
            Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>PayCycles.CaptureScope(c,new System.Text.Json.Nodes.JsonObject{["companyId"]="nope"})));
        }
    }
}
