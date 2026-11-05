using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using ErpImport;
using Payroll;

[TestClass]
public sealed class ConsistentSyncTests
{
    static SourceSnapshot Snapshot(int value,bool reverse=false)=>new(new(){["SyntheticTable"]=(reverse?new[]{2,1}:new[]{1,2}).Select(i=>new JsonObject{["Id"]="synthetic-"+i,["value"]=value}).ToList()},DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"synthetic_sqlite");
    [TestMethod]
    public void VehicleReadingsAppendFreelyButCannotRewriteFactsOrBypassFirstAcceptance()
    {
        const string kind="vehicle_odometer_reading";
        ConsistentSync.Validate(Summary(1000,0,100,kind),Existing(100,kind));
        Assert.ThrowsExactly<ImportFailure>(()=>ConsistentSync.Validate(Summary(1,0,0,kind),[]));
        Assert.ThrowsExactly<ImportFailure>(()=>ConsistentSync.Validate(Summary(0,11,89,kind),Existing(100,kind)));
        var old=new JsonObject{["id"]="reading",["erpSourceId"]="source",["reading"]=100,["__targetKind"]=kind};
        var plan=new ImportPlan();plan.Documents.Add(new(kind,new(){["id"]="reading",["erpSourceId"]="source",["reading"]=101}));
        Assert.ThrowsExactly<ImportFailure>(()=>ImportExecutor.Run(Snapshot(1),plan,new(){["reading"]=old}));
    }
    [TestMethod]
    public void VehicleMasterOmitsDailyFieldsAndCountsOnlyItsStableChanges()
    {
        var snapshot=Snapshot(1);snapshot.Tables["Vehicles"]=[new(){["Id"]="v",["CurrentOdometer"]=999,["CurrentOperationalStatus"]=2,["CurrentAssignmentId"]="a",["OperatingCityId"]="c",["AssetNumber"]="synthetic"}];
        var plan=ImportPlan.Build(snapshot,[]);var vehicle=plan.Documents.Single(d=>d.Kind=="vehicle");
        foreach(string key in new[]{"currentOdometer","currentOperationalStatus","currentAssignmentId","cityId"})Assert.IsFalse(vehicle.Body.ContainsKey(key));
        var previous=vehicle.Body.Clone();previous["currentOdometer"]=10;previous["__targetKind"]="vehicle";
        var summary=ImportExecutor.Run(snapshot,plan,new(){[previous.S("id")]=previous});Assert.AreEqual(0,summary.Counts.Single(c=>c.Kind=="vehicle").Changed);Assert.AreEqual(0,summary.VehicleChangedFields.Count);
        vehicle.Body["assetNumber"]="corrected";summary=ImportExecutor.Run(snapshot,plan,new(){[previous.S("id")]=previous});Assert.AreEqual(1,summary.VehicleChangedFields["assetNumber"]);
    }
    [TestMethod]
    public void OrderedDoubleReadRetriesWholePairsAndStopsAtThree()
    {
        int calls=0;int[] values=[1,2,3,3];var result=ConsistentSync.Read(()=>Snapshot(values[calls++],calls%2==0),out int attempts);
        Assert.AreEqual(4,calls);Assert.AreEqual(2,attempts);Assert.AreEqual("ordered_double_read_stable",result.Consistency);
        calls=0;Assert.ThrowsExactly<ImportFailure>(()=>ConsistentSync.Read(()=>Snapshot(++calls),out _));Assert.AreEqual(6,calls);
    }
    static Dictionary<string,JsonObject> Existing(int n,string kind="rider")=>Enumerable.Range(0,n).ToDictionary(i=>"synthetic-"+i,i=>new JsonObject{["id"]="synthetic-"+i,["__targetKind"]=kind,["erpSource"]=kind,["erpSourceId"]="source-"+i});
    static ImportSummary Summary(int added,int changed,int unchanged,string kind="rider")=>new(DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"synthetic",false,[new(kind,added+changed+unchanged,added,changed,unchanged,0,100)],[],[],[],[]);
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
    public void SmallKindsAndAbsoluteBoundariesAllowNormalDailyChanges()
    {
        foreach(string kind in new[]{"company","commercial_registration","city"})
        {ConsistentSync.Validate(Summary(0,1,2,kind),Existing(3,kind));ConsistentSync.Validate(Summary(0,0,2,kind),Existing(3,kind));Assert.ThrowsExactly<ImportFailure>(()=>ConsistentSync.Validate(Summary(0,0,1,kind),Existing(3,kind)));}
        ConsistentSync.Validate(Summary(0,5,25),Existing(30));Assert.ThrowsExactly<ImportFailure>(()=>ConsistentSync.Validate(Summary(0,6,24),Existing(30)));
        ConsistentSync.Validate(Summary(0,0,49),Existing(50));Assert.ThrowsExactly<ImportFailure>(()=>ConsistentSync.Validate(Summary(0,0,48),Existing(50)));
    }
    [TestMethod]
    public void NewKindAcceptanceIsExplicitPerKindAndSeparateFromLargeChangeAcceptance()
    {
        foreach(string kind in new[]{"legal_case","city"}.Concat(ImportPlan.Kinds.Where(k=>k.StartsWith("hs_",StringComparison.Ordinal))))
        {
            var summary=Summary(6,0,0,kind);Assert.ThrowsExactly<ImportFailure>(()=>ConsistentSync.Validate(summary,[]));
            Assert.ThrowsExactly<ImportFailure>(()=>ConsistentSync.Validate(summary,[],SyncOverrides.Parse(["--accept-large-change",kind])));
            ConsistentSync.Validate(summary,[],SyncOverrides.Parse(["--accept-new-kind",kind]));
        }
        Assert.ThrowsExactly<ImportFailure>(()=>ConsistentSync.Validate(Summary(1,0,0,"legal_case"),[],SyncOverrides.Parse(["--accept-new-kind","city"])));
        ConsistentSync.Validate(Summary(0,0,0),[]);
    }
    [TestMethod]
    public void ReviewedLargeChangesArePerKindAndDoNotRemoveMissingTargetRows()
    {
        var options=SyncOverrides.Parse(["--accept-large-change","rider"]);var before=Existing(100);
        ConsistentSync.Validate(Summary(0,11,89),before,options);ConsistentSync.Validate(Summary(0,0,97),before,options);
        Assert.ThrowsExactly<ImportFailure>(()=>ConsistentSync.Validate(Summary(0,11,89),before,SyncOverrides.Parse(["--accept-large-change","city"])));
        string directory=Path.Combine(Path.GetTempPath(),"gate-sync-retain-"+Guid.NewGuid().ToString("N"));
        try
        {
            var target=new Store(directory);target.Write((c,tx)=>{foreach(var row in before.Values){var doc=row.Clone();doc.Remove("__targetKind");Store.Save(c,"rider",doc,tx);}return true;});
            var source=Snapshot(1);var plan=new ImportPlan();var existing=target.Read(ImportExecutor.Target);string raw=System.Text.Json.JsonSerializer.Serialize(existing);
            var preview=ImportExecutor.Run(source,plan,existing);ConsistentSync.Validate(preview,existing,options);
            var committed=ImportExecutor.Run(source,plan,existing,target);Assert.IsTrue(committed.Committed);Assert.AreEqual(raw,System.Text.Json.JsonSerializer.Serialize(target.Read(ImportExecutor.Target)));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(directory))Directory.Delete(directory,true);}

    }
    [TestMethod]
    public void OverrideArgumentsAreRepeatableDeduplicatedAndValidatedBeforeConnections()
    {
        var options=SyncOverrides.Parse(["--live","--accept-new-kind","city","--accept-new-kind","legal_case","--accept-new-kind","city","--accept-large-change","rider","--accept-large-change","city"]);
        CollectionAssert.AreEqual(new[]{"city","legal_case"},options.AcceptNewKinds.ToArray());CollectionAssert.AreEqual(new[]{"city","rider"},options.AcceptLargeChanges.ToArray());
        foreach(string[] invalid in new[]{new[]{"--accept-new-kind"},new[]{"--accept-large-change"},new[]{"--accept-new-kind","unknown"},new[]{"--accept-large-change","--live"}})Assert.AreEqual("invalid_sync_override_kind",Assert.ThrowsExactly<ImportFailure>(()=>SyncOverrides.Parse(invalid)).Code);
    }
    [TestMethod]
    public void VerifiedCertificateFieldsSurviveAnOperationalRefresh()
    {
        var before=new JsonObject{["id"]="synthetic",["registrySource"]="company-certificates",["city"]="Verified city",["nameAr"]="Verified legal name",["policy"]="preserved"};
        var merged=ImportPlan.Merge(before,new(){["city"]="",["nameAr"]="Older ERP name",["sourceDeleted"]=true});
        var reactivated=ImportPlan.Merge(new(){["id"]="synthetic",["debtOnly"]=true,["personRole"]="debt_only"},new(){["erpSource"]="rider",["personRole"]="rider"});Assert.IsFalse(reactivated.B("debtOnly"));Assert.IsTrue(ErpRiderClassification.ListedAsRider(reactivated));
        Assert.AreEqual("Verified city",merged.S("city"));Assert.AreEqual("Verified legal name",merged.S("nameAr"));Assert.AreEqual("preserved",merged.S("policy"));Assert.IsTrue(merged.B("sourceDeleted"));
    }
    [TestMethod]
    public void RunLogsContainTypedCountsAndPrivatePermissionsOnly()
    {
        string directory=Path.Combine(Path.GetTempPath(),"gate-sync-log-"+Guid.NewGuid().ToString("N"));
        try
        {
            var options=SyncOverrides.Parse(["--accept-new-kind","city","--accept-large-change","rider"]);
            SyncRunLog.Write(directory,"completed",Summary(0,0,100),1,overrides:options);SyncRunLog.Write(directory,"refused",null,3,"source_inconsistent_after_three_attempts",options);
            Assert.AreEqual(2,Directory.GetFiles(directory).Length);
            foreach(string path in Directory.GetFiles(directory)){var json=JsonNode.Parse(File.ReadAllText(path))!.AsObject();Assert.IsFalse(json.ContainsKey("rows"));Assert.IsFalse(json.ContainsKey("fingerprints"));Assert.AreEqual("city",json["acceptNewKinds"]![0]!.ToString());Assert.AreEqual("rider",json["acceptLargeChanges"]![0]!.ToString());if(!OperatingSystem.IsWindows())Assert.AreEqual(UnixFileMode.UserRead|UnixFileMode.UserWrite,File.GetUnixFileMode(path));}
            if(!OperatingSystem.IsWindows())Assert.AreEqual(UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute,File.GetUnixFileMode(directory));
        }
        finally{Directory.Delete(directory,true);}
    }
}
