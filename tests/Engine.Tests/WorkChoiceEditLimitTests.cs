using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class WorkChoiceEditLimitTests
{
    [TestMethod]
    public void ThreeEditsPerCycleAndAReopenedFormStartsANewCount()
    {
        var campaign = new JsonObject { ["id"] = "c1" };
        var reply = new JsonObject { ["editCycle"] = 1, ["editCount"] = 0 };
        Assert.AreEqual(3, SubmissionChoices.EditsLeft(reply, campaign));
        reply["editCount"] = 3;
        Assert.AreEqual(0, SubmissionChoices.EditsLeft(reply, campaign));
        campaign["cycle"] = 2; // closed and reopened
        Assert.AreEqual(0, SubmissionChoices.EditsUsed(reply, campaign));
        Assert.AreEqual(3, SubmissionChoices.EditsLeft(reply, campaign));
        var legacy = new JsonObject(); // replies saved before this change
        Assert.AreEqual(3, SubmissionChoices.EditsLeft(legacy, new JsonObject()));
    }
}
