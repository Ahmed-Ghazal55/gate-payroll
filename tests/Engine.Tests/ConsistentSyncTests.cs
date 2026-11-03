using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using ErpImport;
using Payroll;

[TestClass]
public sealed class ConsistentSyncTests
{
    static SourceSnapshot Snapshot(int value,bool reverse=false)=>new(new(){["SyntheticTable"]=(reverse?new[]{2,1}:new[]{1,2}).Select(i=>new JsonObject{["Id"]="synthetic-"+i,["value"]=value}).ToList()},DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"synthetic_sqlite");
    [TestMethod]
    public void OrderedDoubleReadRetriesWholePairsAndStopsAtThree()
    {
        int calls=0;int[] values=[1,2,3,3];var result=ConsistentSync.Read(()=>Snapshot(values[calls++],calls%2==0),out int attempts);
        Assert.AreEqual(4,calls);Assert.AreEqual(2,attempts);Assert.AreEqual("ordered_double_read_stable",result.Consistency);
        calls=0;Assert.ThrowsExactly<ImportFailure>(()=>ConsistentSync.Read(()=>Snapshot(++calls),out _));Assert.AreEqual(6,calls);
    }
    static Dictionary<string,JsonObject> Existing(int n)=>Enumerable.Range(0,n).ToDictionary(i=>"synthetic-"+i,i=>new JsonObject{["id"]="synthetic-"+i,["__targetKind"]="rider",["erpSource"]="rider",["erpSourceId"]="source-"+i});
    static ImportSummary Summary(int added,int changed,int unchanged)=>new(DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"synthetic",false,[new("rider",added+changed+unchanged,added,changed,unchanged,0,100)],[],[],[],[]);
    [TestMethod]
    public void CountAndChangeThresholdsAreInclusiveAndManualRecordsAreExcluded()
    {
        var existing=Existing(100);existing["manual"]=new(){["id"]="manual",["__targetKind"]="rider"};
        ConsistentSync.Validate(Summary(0,10,90),existing);ConsistentSync.Validate(Summary(0,0,98),existing);
        Assert.ThrowsExactly<ImportFailure>(()=>ConsistentSync.Validate(Summary(0,11,89),existing));
        Assert.ThrowsExactly<ImportFailure>(()=>ConsistentSync.Validate(Summary(0,0,97),existing));
        Assert.ThrowsExactly<ImportFailure>(()=>ConsistentSync.Validate(Summary(1,0,0),[]));
    }
    [TestMethod]
    public void VerifiedCertificateFieldsSurviveAnOperationalRefresh()
    {
        var before=new JsonObject{["id"]="synthetic",["registrySource"]="company-certificates",["city"]="Verified city",["nameAr"]="Verified legal name",["policy"]="preserved"};
        var merged=ImportPlan.Merge(before,new(){["city"]="",["nameAr"]="Older ERP name",["sourceDeleted"]=true});
        Assert.AreEqual("Verified city",merged.S("city"));Assert.AreEqual("Verified legal name",merged.S("nameAr"));Assert.AreEqual("preserved",merged.S("policy"));Assert.IsTrue(merged.B("sourceDeleted"));
    }
    [TestMethod]
    public void RunLogsContainTypedCountsAndPrivatePermissionsOnly()
    {
        string directory=Path.Combine(Path.GetTempPath(),"gate-sync-log-"+Guid.NewGuid().ToString("N"));
        try
        {
            SyncRunLog.Write(directory,"completed",Summary(0,0,100),1);SyncRunLog.Write(directory,"refused",null,3,"source_inconsistent_after_three_attempts");
            Assert.AreEqual(2,Directory.GetFiles(directory).Length);
            foreach(string path in Directory.GetFiles(directory)){var json=JsonNode.Parse(File.ReadAllText(path))!.AsObject();Assert.IsFalse(json.ContainsKey("rows"));Assert.IsFalse(json.ContainsKey("fingerprints"));if(!OperatingSystem.IsWindows())Assert.AreEqual(UnixFileMode.UserRead|UnixFileMode.UserWrite,File.GetUnixFileMode(path));}
            if(!OperatingSystem.IsWindows())Assert.AreEqual(UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute,File.GetUnixFileMode(directory));
        }
        finally{Directory.Delete(directory,true);}
    }
}
