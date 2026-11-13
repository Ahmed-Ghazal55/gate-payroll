using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.Json.Nodes;

[TestClass]
public sealed class PayRunInputTests
{
    static string Rider(int i)=>"2"+i.ToString("D9");

    [TestMethod]
    public void TableRoundTripOnSqlite()
    {
        using var c=new SqliteConnection("Data Source=:memory:");c.Open();
        using var tx=c.BeginTransaction();
        PayRunInputs.Ensure(c,tx);
        var body=new JsonObject{["id"]="d1",["orders"]=3};
        var at=DateTimeOffset.Parse("2026-10-15T12:00:00Z");
        var result=PayRunInputs.Replace(c,tx,"cycle-1","ninja","2026-10-01","2026-10-31","maker",[("ninja_daily","d1",body,null)],at);
        Assert.AreEqual(1,result.Counts["ninja_daily"]);
        var listed=PayRunInputs.List(c,tx,"cycle-1","ninja_daily");
        Assert.AreEqual(1,listed.Count);
        Assert.AreEqual(3m,listed[0].N("orders"));
        Assert.AreEqual(PayRunInputs.HashBody(body),listed[0].ContainsKey("id")?PayRunInputs.HashBody(listed[0]):"");
        tx.Commit();
    }

    [TestMethod]
    public void CaptureRecaptureAndSealRefuse()
    {
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            var cycle=f.Store.Write((c,tx)=>PayCycles.Create(c,tx,PayFixture.Maker,input));
            string id=cycle.S("id");
            var first=PayCycleTests.Capture(f,id);
            Assert.AreEqual(id,first.S("cycleId"));
            Assert.IsTrue(first["counts"]!.AsObject().N("platform_account")>=1);
            f.Store.Write((c,tx)=>{Store.Save(c,"platform_account",new(){["id"]="synthetic-account-extra",["companyId"]=FinancialCore.Sponsors[0].Id,["platformCode"]="keeta",["assignments"]=new JsonArray()},tx);return true;});
            var second=PayCycleTests.Capture(f,id);
            Assert.IsTrue(second["counts"]!.AsObject().N("platform_account")>first["counts"]!.AsObject().N("platform_account"));
            for(int s=1;s<=6;s++)PayCycleTests.Advance(f,id,s);
            f.Store.Write((c,tx)=>PayCycles.ReviewAll(c,tx,PayFixture.Maker,id));
            PayCycleTests.Advance(f,id,7,true);
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>PayCycleTests.Capture(f,id)).Status);
        }
    }

    [TestMethod]
    public void FrozenAssignmentsSurviveStoreOverwrite()
    {
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            f.Store.Write((c,tx)=>{
                Store.Save(c,"vehicle",new(){["id"]="synthetic-bike",["vehicleType"]=1,["ownershipType"]=1},tx);
                Store.Save(c,"vehicle_assignment",new(){["id"]="va1",["vehicleId"]="synthetic-bike",["riderId"]=Rider(1),["startedAt"]="2026-10-01T00:00:00+03:00",["endedAt"]="2026-10-05T00:00:00+03:00"},tx);
                return true;
            });
            var cycle=PayCycleTests.Through(f,input,4,review:false);
            string id=cycle.S("id");
            Assert.AreEqual(1,f.Store.Read(c=>PayRunInputs.List(c,null,id,"vehicle_assignment").Count));
            f.Store.Write((c,tx)=>{
                using var del=Store.Command(c,tx,"DELETE FROM records WHERE id=@id AND kind=@kind",("@id","va1"),("@kind","vehicle_assignment"));
                del.ExecuteNonQuery();
                return true;
            });
            Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"vehicle_assignment").Count(a=>a.S("id")=="va1")));
            Assert.AreEqual(1,f.Store.Read(c=>PayRunInputs.List(c,null,id,"vehicle_assignment").Count),"frozen rows survive store deletes");
        }
    }

    [TestMethod]
    public void AdvanceWithoutCaptureIsBlocked()
    {
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            var cycle=f.Store.Write((c,tx)=>PayCycles.Create(c,tx,PayFixture.Maker,input));
            var err=Assert.ThrowsExactly<ApiError>(()=>PayCycleTests.Advance(f,cycle.S("id"),1));
            Assert.IsTrue(err.Message.Contains("المدخلات",StringComparison.Ordinal));
        }
    }
}
