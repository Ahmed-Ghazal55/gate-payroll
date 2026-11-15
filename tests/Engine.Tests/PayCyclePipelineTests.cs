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
}
