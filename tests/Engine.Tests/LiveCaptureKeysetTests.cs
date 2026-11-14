using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class LiveCaptureKeysetTests
{
    [TestMethod]
    public void ProjectReportsLastSourceIdEvenWhenAllRowsRejected()
    {
        // Two source rows with no Employees/profiles → ImportPlan rejects riders; Documents empty; cursor must still advance.
        string id1=Guid.NewGuid().ToString("D"),id2=Guid.NewGuid().ToString("D");
        var snapshot=new SourceSnapshot(new Dictionary<string,List<JsonObject>>(StringComparer.Ordinal)
        {
            ["NinjaDailyPerformance"]=[
                new(){["Id"]=id1,["PlatformRiderAccountId"]="",["RiderClientAssignmentId"]="",["RiderProfileId"]="",["ReportDate"]="2026-10-01",["CaptainId"]="c1",["IsDeleted"]=false},
                new(){["Id"]=id2,["PlatformRiderAccountId"]="",["RiderClientAssignmentId"]="",["RiderProfileId"]="",["ReportDate"]="2026-10-02",["CaptainId"]="c2",["IsDeleted"]=false},
            ],
            ["Sponsors"]=[],
        },DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"test","test");
        var page=LiveOperationsSource.Project(snapshot,FinancialCore.Sponsors,"ninja_daily");
        Assert.AreEqual(0,page.Documents.Count);
        Assert.AreEqual(2,page.SourceRowCount);
        Assert.AreEqual(id2,page.LastSourceId);
        Assert.IsTrue(page.Rejected.Count>0||page.Documents.Count==0);
    }

    [TestMethod]
    public void KeysetPreviewUsesAfterAsGuidNotProjectedId()
    {
        string guid=Guid.NewGuid().ToString("D");
        var page=new LiveOperationsQuery.PageRequest(1,25,"id","",0,25,"","","","",guid);
        var scope=new LiveOperationsQuery.Scope(true,[],[],[],[]);
        var (_,sql,names)=LiveOperationsQuery.PreviewSql("ninja_daily",scope,page);
        StringAssert.Contains(sql,"@after");
        Assert.IsTrue(names.Contains("@after"));
        Assert.IsFalse(sql.Contains("erp:",StringComparison.Ordinal));
    }
}
