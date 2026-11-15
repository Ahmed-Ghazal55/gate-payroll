using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class AssignmentDayRuleTests
{
    static JsonObject Values()=>PaySettings.Defaults()["values"]!.AsObject();

    [TestMethod]
    public void SameDayHandoverAfter1400CountsForPreviousHolderOnly()
    {
        // 2026-10-15 15:00 Riyadh = 12:00Z. Previous ends after cutoff; next starts after cutoff → day stays with previous.
        var previous=new JsonObject
        {
            ["RawEffectiveFrom"]="2026-10-14T08:00:00Z",
            ["RawEffectiveTo"]="2026-10-15T12:00:00Z",
            ["riderId"]="prev",
            ["platformAccountId"]="acc-1"
        };
        var next=new JsonObject
        {
            ["RawEffectiveFrom"]="2026-10-15T12:00:00Z",
            ["RawEffectiveTo"]="",
            ["riderId"]="next",
            ["platformAccountId"]="acc-1"
        };
        Assert.IsTrue(AssignmentSpan.Covers(previous,"2026-10-15",Values(),null));
        Assert.IsFalse(AssignmentSpan.Covers(next,"2026-10-15",Values(),null));
        Assert.IsTrue(AssignmentSpan.Covers(next,"2026-10-16",Values(),null));
    }

    [TestMethod]
    public void DayContributionsOverrideTimedGuess()
    {
        var assignment=new JsonObject{["RawEffectiveFrom"]="2026-10-15T12:00:00Z",["riderId"]="next",["platformAccountId"]="acc-1"};
        var contrib=new[]{new JsonObject{["PlatformRiderAccountId"]="acc-1",["BusinessDate"]="2026-10-15",["RiderProfileId"]="prev"}};
        Assert.IsFalse(AssignmentSpan.Covers(assignment,"2026-10-15",Values(),contrib));
        Assert.IsTrue(AssignmentSpan.Covers(new JsonObject{["riderId"]="prev",["platformAccountId"]="acc-1",["EffectiveFrom"]="2026-10-01",["EffectiveTo"]=""},"2026-10-15",Values(),contrib));
    }

    [TestMethod]
    public void PermissionRenewedEventTypeIsMappedNotRejected()
    {
        // Smoke: SQL allow-list includes type 5 (PermissionRenewed).
        var sql=ErpImport.LiveOperationsQuery.PreviewSql("custody_handover",new(true,[],[],[],[]),new(1,25,"id","",0,25)).CountSql;
        StringAssert.Contains(sql,"IN (1,2,3,4,5)");
    }
}
