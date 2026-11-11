using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.Sqlite;

[TestClass]
public class SourceWatermarkTests
{
    [TestMethod]
    public void WatermarksRoundTripOnSqlite()
    {
        using var c=new SqliteConnection("Data Source=:memory:");c.Open();
        SourceWatermarks.Ensure(c);
        var at=DateTimeOffset.Parse("2026-11-09T18:00:00Z");
        SourceWatermarks.Save(c,null,[new("app.Employees","rowversion","0A0B0C0D0E0F0102","run-1",at),new("dataset:chefz","dataset_version","3","run-1",at)]);
        var loaded=SourceWatermarks.Load(c);
        Assert.AreEqual(2,loaded.Count);
        Assert.AreEqual("0A0B0C0D0E0F0102",loaded["app.Employees"].LastValue);
        Assert.AreEqual("3",loaded["dataset:chefz"].LastValue);
    }
    [TestMethod]
    public void FullResyncRequiresReason()
    {
        Assert.AreEqual("full_resync_reason_required",Assert.ThrowsExactly<ImportFailure>(()=>SyncOverrides.Parse(["--full-resync"])).Code);
        Assert.AreEqual("full_resync_reason_required",Assert.ThrowsExactly<ImportFailure>(()=>SyncOverrides.Named([],[],true,"")).Code);
        var ok=SyncOverrides.Parse(["--full-resync","rebuild after CR-004"]);
        Assert.IsTrue(ok.FullResync);Assert.AreEqual("rebuild after CR-004",ok.FullResyncReason);
    }
    [TestMethod]
    public void OpenPeriodMarksMissingRowsSoftDeleted()
    {
        var existing=new Dictionary<string,JsonObject>
        {
            ["erp:ninja_daily:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"]=new(){["id"]="erp:ninja_daily:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",["__targetKind"]="ninja_daily",["erpSourceId"]="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",["date"]="2026-11-01",["sourceDeleted"]=false}
        };
        var snapshot=new SourceSnapshot([],DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"incremental","incremental");
        var plan=new ImportPlan();
        var missing=OpenPeriodReconcile.MissingAsSoftDeleted(snapshot,plan,existing).ToList();
        Assert.AreEqual(1,missing.Count);
        Assert.IsTrue(missing[0].Body["sourceDeleted"]!.GetValue<bool>());
        Assert.IsTrue(missing[0].Body["erpMissingInOpenPeriod"]!.GetValue<bool>());
    }
    [TestMethod]
    public void OpenPeriodWindowCoversPreviousAndCurrentMonth()
    {
        var (from,to)=SourceReadProfiles.OpenPeriodWindow(DateTimeOffset.Parse("2026-11-09T12:00:00+03:00"));
        Assert.AreEqual(new DateTime(2026,10,1),from);Assert.AreEqual(new DateTime(2026,12,1),to);
    }
}
