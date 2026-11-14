using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.Json.Nodes;
using ErpImport;

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
        var result=PayRunInputs.Replace(c,tx,"cycle-1","maker",[("ninja_daily","d1",body)],at);
        Assert.AreEqual(1,result.Counts["ninja_daily"]);
        var listed=PayRunInputs.List(c,tx,"cycle-1","ninja_daily");
        Assert.AreEqual(1,listed.Count);
        Assert.AreEqual(3m,listed[0].N("orders"));
        Assert.AreEqual(body.S("id"),PayRunInputs.Get(c,tx,"cycle-1","ninja_daily","d1")!.S("id"));
        Assert.AreEqual(PayRunInputs.HashBody(body),PayRunInputs.HashBody(listed[0]));
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
    public void RecaptureAfterCalcRefusedWithoutFingerprintRewrite()
    {
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            var cycle=PayCycleTests.Through(f,input,4,review:false);
            string id=cycle.S("id");
            string fp=cycle.S("sourceFingerprint");
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>PayCycleTests.Capture(f,id)).Status);
            f.Store.Write((c,tx)=>{Store.Save(c,"platform_account",new(){["id"]="synthetic-account-drift",["companyId"]=FinancialCore.Sponsors[0].Id,["platformCode"]="keeta",["assignments"]=new JsonArray()},tx);return true;});
            Assert.AreEqual(fp,f.Store.Read(c=>Store.Get(c,id,PayCycles.Kind)!.S("sourceFingerprint")));
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>PayCycleTests.Capture(f,id)).Status);
            Assert.AreEqual(fp,f.Store.Read(c=>Store.Get(c,id,PayCycles.Kind)!.S("sourceFingerprint")));
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

    [TestMethod]
    public void RequiredKindZeroNeedsReason()
    {
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            var cycle=f.Store.Write((c,tx)=>PayCycles.Create(c,tx,PayFixture.Maker,input));
            string id=cycle.S("id");
            var staged=new Dictionary<string,List<JsonObject>>(StringComparer.Ordinal)
            {
                ["platform_account"]=[new(){["id"]="a1",["companyId"]=FinancialCore.Sponsors[0].Id,["platformCode"]="keeta",["assignments"]=new JsonArray()}],
                ["vehicle_assignment"]=[],
                ["housing_residence"]=[],
                ["vehicle"]=[]
            };
            Func<string,IEnumerable<JsonObject>> live=kind=>staged.TryGetValue(kind,out var rows)?rows:Array.Empty<JsonObject>();
            var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PayCycles.CaptureInputs(c,tx,PayFixture.Maker,id,liveRows:live)));
            Assert.AreEqual(409,err.Status);
            Assert.IsTrue(err.Message.Contains("vehicle_assignment",StringComparison.Ordinal));
            var ok=f.Store.Write((c,tx)=>PayCycles.CaptureInputs(c,tx,PayFixture.Maker,id,liveRows:live,input:new(){["zeroKindReasons"]=new JsonObject{["vehicle_assignment"]="none this month"}}));
            Assert.AreEqual(0m,ok["counts"]!.AsObject().N("vehicle_assignment"));
            Assert.AreEqual("none this month",f.Store.Read(c=>Store.Get(c,id,PayCycles.Kind)!["inputsCaptureGaps"]!.AsObject().S("vehicle_assignment")));
        }
    }

    [TestMethod]
    public void StagedDictionaryMissingKindErrors()
    {
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            var cycle=f.Store.Write((c,tx)=>PayCycles.Create(c,tx,PayFixture.Maker,input));
            string id=cycle.S("id");
            var staged=new Dictionary<string,List<JsonObject>>(StringComparer.Ordinal)
            {
                ["platform_account"]=[new(){["id"]="a1",["companyId"]=FinancialCore.Sponsors[0].Id,["platformCode"]="keeta",["assignments"]=new JsonArray()}],
                ["housing_residence"]=[]
            };
            var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PayCycles.CaptureInputs(c,tx,PayFixture.Maker,id,liveRows:kind=>staged.TryGetValue(kind,out var rows)?rows:throw new ApiError("مصدر التقاط ناقص لنوع: "+kind,503),input:new(){["zeroKindReasons"]=new JsonObject{["vehicle_assignment"]="n/a",["ninja_daily"]="n/a"}})));
            Assert.AreEqual(503,err.Status);
        }
    }

    [TestMethod]
    public void NarrowAndWideScopeSameFreezeViaStaged()
    {
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            var cycle=f.Store.Write((c,tx)=>PayCycles.Create(c,tx,PayFixture.Maker,input));
            string id=cycle.S("id");
            var staged=new Dictionary<string,List<JsonObject>>(StringComparer.Ordinal)
            {
                ["platform_account"]=[new(){["id"]="scoped-a",["companyId"]=FinancialCore.Sponsors[0].Id,["platformCode"]="keeta",["assignments"]=new JsonArray()}],
                ["vehicle_assignment"]=[new(){["id"]="va-s",["riderId"]=Rider(1),["vehicleId"]="v1",["startedAt"]="2026-10-01T00:00:00+03:00",["endedAt"]=""}],
                ["housing_residence"]=[],
                ["vehicle"]=[new(){["id"]="v1",["vehicleType"]=1,["ownershipType"]=1}]
            };
            Func<string,IEnumerable<JsonObject>> live=kind=>staged[kind];
            var a=f.Store.Write((c,tx)=>PayCycles.CaptureInputs(c,tx,PayFixture.Maker,id,liveRows:live));
            f.Store.Write((c,tx)=>{var cy=Store.Get(c,id,PayCycles.Kind,tx)!;cy.Remove("inputsCapturedAt");cy["stage"]=1;Store.Save(c,PayCycles.Kind,cy,tx);return true;});
            var b=f.Store.Write((c,tx)=>PayCycles.CaptureInputs(c,tx,PayFixture.Checker,id,liveRows:live));
            Assert.AreEqual(a["counts"]!.ToJsonString(),b["counts"]!.ToJsonString());
        }
    }

    [TestMethod]
    public void AuditStoresCaptureCounts()
    {
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            var cycle=f.Store.Write((c,tx)=>PayCycles.Create(c,tx,PayFixture.Maker,input));
            PayCycleTests.Capture(f,cycle.S("id"));
            var audit=f.Store.Read(c=>Store.List(c,"audit").Last(a=>a.S("action")=="pay.inputs_captured"));
            Assert.IsTrue(audit["counts"]!.AsObject().N("platform_account")>=1);
        }
    }

    [TestMethod]
    public void PreCycleDraftUsesStagedLiveOps()
    {
        var(f,_,_)=PayCycleTests.Setup(false);using(f)
        {
            string company=FinancialCore.Sponsors[0].Id,period="2026-10";
            f.Approve("pay_terms","ninja",f.Draft("pay_terms","ninja",PayFixture.Terms()));
            f.Approve("pay_eligibility","ninja",f.Draft("pay_eligibility","ninja",PayFixture.Eligibility()));
            var daily=new List<JsonObject>{new(){["id"]="nd1",["companyId"]=company,["platformAccountId"]="missing",["riderId"]=Rider(1),["date"]="2026-10-05",["orders"]=2}};
            var live=PayReviewDraft.KindAwareLiveOps(daily,[],[]);
            var draft=f.Store.Read(c=>PayReviewDraft.Build(c,null,PayFixture.Maker,"ninja",period,company,live));
            Assert.IsTrue(draft["unassignedOrders"]!.AsArray().Count>=1);
        }
    }

    [TestMethod]
    public void DeriveUsesStagedVehicleNotEmptyStoreMirror()
    {
        using var f=new PayFixture();
        var rider=new JsonObject{["id"]="2000000077",["engagementType"]="SponsoredInternal"};
        var assignments=new List<JsonObject>{new(){["id"]="va1",["riderId"]="2000000077",["vehicleId"]="v1",["startedAt"]="2026-10-05T00:00:00Z",["endedAt"]=""}};
        var vehicles=new List<JsonObject>{new(){["id"]="v1",["vehicleType"]=1,["ownershipType"]=1}};
        var live=PayReviewDraft.KindAwareLiveOps([],assignments,vehicles);
        // Store mirror stays empty — liveOps must supply the hold.
        string got=f.Store.Read(c=>PayReviewCategory.Derive(c,null,rider,"2026-10",live));
        Assert.AreEqual("sponsored_company_bike",got);
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"vehicle_assignment").Count));
    }

    [TestMethod]
    public void KindAwareLiveOpsThrowsOnUnknownKind()
    {
        var live=PayReviewDraft.KindAwareLiveOps([],[],[]);
        var err=Assert.ThrowsExactly<ApiError>(()=>live("platform_account").ToList());
        Assert.AreEqual(503,err.Status);
        var gaps=PayCycles.ValidateGapsLiveOps([],[]);
        Assert.ThrowsExactly<ApiError>(()=>gaps("vehicle").ToList());
    }

    [TestMethod]
    public void PreviewSqlIncludesKeysetAfterClause()
    {
        var page=new LiveOperationsQuery.PageRequest(1,50,"id","",0,50,"","2026-10-01","2026-10-31","ninja","abc");
        var scope=new LiveOperationsQuery.Scope(true,[],[],[],[],PlatformRestricted:false);
        var (count,sql,names)=LiveOperationsQuery.PreviewSql("ninja_daily",scope,page);
        Assert.IsTrue(sql.Contains("[Id] > @after",StringComparison.Ordinal)||sql.Contains("t.[Id] > @after",StringComparison.Ordinal),sql);
        Assert.IsTrue(names.Contains("@after"));
        Assert.IsTrue(count.Contains("COUNT_BIG",StringComparison.Ordinal));
    }

    [TestMethod]
    public void CaptureThenApproveIgnoresLaterStoreDrift()
    {
        var(f,input,_)=PayCycleTests.Setup(false);using(f)
        {
            var cycle=PayCycleTests.Through(f,input,6,review:true);
            string id=cycle.S("id");
            string fp=cycle.S("sourceFingerprint");
            int lines=f.Store.Read(c=>PayCycles.Lines(c,null,id).Count);
            f.Store.Write((c,tx)=>{Store.Save(c,"platform_account",new(){["id"]="post-capture-drift",["companyId"]=FinancialCore.Sponsors[0].Id,["platformCode"]="keeta",["assignments"]=new JsonArray()},tx);return true;});
            Assert.AreEqual(fp,f.Store.Read(c=>Store.Get(c,id,PayCycles.Kind)!.S("sourceFingerprint")),"store drift must not change freeze fingerprint");
            Assert.AreEqual(0,f.Store.Read(c=>PayRunInputs.List(c,null,id,"platform_account").Count(a=>a.S("id")=="post-capture-drift")));
            PayCycleTests.Advance(f,id,7,true);
            Assert.AreEqual(lines,f.Store.Read(c=>PayCycles.Lines(c,null,id).Count));
            Assert.AreEqual("approved",f.Store.Read(c=>Store.Get(c,id,PayCycles.Kind)!.S("status")));
        }
    }
}
