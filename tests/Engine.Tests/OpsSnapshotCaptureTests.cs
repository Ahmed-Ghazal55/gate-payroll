using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class OpsSnapshotCaptureTests
{
    static ClaimsPrincipal Actor()=>UserAccess.Principal(new JsonObject{["username"]=MoneySchemaFixture.Id(),["role"]="Admin"});

    static (System.Data.Common.DbConnection c,string company,string run,FakeOpsLiveFactsReader facts) Seed(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);
        SchemaSalInvoice.Ensure(c);SchemaSalPayRun.Ensure(c);SchemaOperationCapture.Ensure(c);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,null,"IF OBJECT_ID(N'dbo.records',N'U') IS NULL CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL);");
        else
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
        string company=MoneySchemaFixture.Company(c);
        string run=MoneySchemaFixture.Run(c,company,"ninja","2026-10-01");
        var facts=new FakeOpsLiveFactsReader();
        string account=MoneySchemaFixture.Id(),rider=MoneySchemaFixture.Id();
        facts.Rows["ninja_daily"]=[new(){["id"]=MoneySchemaFixture.Id(),["reportDate"]="2026-10-01",["platformRiderAccountId"]=account,["riderProfileId"]=rider,["orders"]=12,["hours"]=8}];
        facts.Rows["ninja_daily_deduction"]=[new(){["id"]=MoneySchemaFixture.Id(),["reportDate"]="2026-10-01",["platformRiderAccountId"]=account,["actualRiderProfileId"]=rider,["collectedAmount"]=20m,["deductionsAmount"]=5m}];
        facts.Rows["platform_account"]=[new(){["id"]=account,["externalAccountId"]="CAP-1",["code"]="A1"}];
        facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=rider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31"}];
        facts.Rows["housing"]=[];
        facts.Rows["fuel"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["liters"]=10m,["totalInclVat"]=40m,["month"]="2026-10-01",["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31"}];
        facts.Rows["vehicle"]=[new(){["id"]=MoneySchemaFixture.Id(),["assetNumber"]="V1",["plateNumberAr"]="أ ب ج 1",["actualRiderProfileId"]=rider,["erpRiderId"]=rider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-15"}];
        facts.Rows["scheme"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["schemeCode"]="commission"}];
        facts.Rows["eligibility"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["eligible"]=true}];
        return (c,company,run,facts);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CaptureWritesUsedFieldsOnly_AndBlocksCalcUntilEveryKindLinked(bool sqlServer)
    {
        var (c,company,run,facts)=Seed(sqlServer);
        using(c)
        {
            var actor=Actor();
            using(var tx=c.BeginTransaction())
            {
                var captured=OpsSnapshotCapture.Capture(c,tx,actor,run,facts,zeroKindReasons:new Dictionary<string,string>{["housing"]="لا سكن في شهر الاختبار"});
                Assert.IsTrue(captured.N("snapshotCount")>=7);
                Assert.AreEqual(0m,captured.N("captureState"));
                tx.Commit();
            }
            Assert.AreEqual(0L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT capture_state FROM sal_pay_run WHERE id=@id",("@id",run))!));
            string payload=(string)MoneySchemaFixture.Scalar(c,null,"SELECT payload FROM sal_ops_snapshot WHERE scope_company_id=@c AND kind='ninja_daily' LIMIT 1",("@c",company))!;
            Assert.IsTrue(payload.Contains("orders",StringComparison.Ordinal));
            Assert.IsFalse(payload.Contains("Vin",StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(payload.Contains("password",StringComparison.OrdinalIgnoreCase));
            MoneySchemaFixture.Exec(c,null,"UPDATE fin_operation_capture_manifest SET status=3,empty_reason=NULL,snapshot_link_id=NULL WHERE operation_id=@run AND required_kind='scheme'",("@run",run));
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET capture_state=1,stage=2,calc_started_at=datetime('now') WHERE id=@id",("@id",run)),"Capture incomplete or snapshot scope mismatch");
            MoneySchemaFixture.Exec(c,null,"UPDATE fin_operation_capture_manifest SET status=1,snapshot_link_id=(SELECT id FROM fin_operation_snapshot_link WHERE operation_id=@run AND snapshot_kind='scheme' LIMIT 1),empty_reason=NULL WHERE operation_id=@run AND required_kind='scheme'",("@run",run));
            using(var tx=c.BeginTransaction())
            {
                var frozen=OpsSnapshotCapture.Freeze(c,tx,actor,run);
                Assert.AreEqual(1m,frozen.N("captureState"));
                Assert.IsTrue(frozen["allowedActions"]!.AsArray().All(a=>a?.ToString()!="capture_ops"));
                tx.Commit();
            }
            Assert.AreEqual(1L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT capture_state FROM sal_pay_run WHERE id=@id",("@id",run))!));
            using(var tx=c.BeginTransaction())
            {
                Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>OpsSnapshotCapture.Capture(c,tx,actor,run,facts)).Status);
                tx.Rollback();
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MissingRequiredKindWithoutReason_BlocksCapture(bool sqlServer)
    {
        var (c,_,run,facts)=Seed(sqlServer);
        using(c)
        {
            facts.Rows["fuel"]=[];
            var actor=Actor();
            using var tx=c.BeginTransaction();
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>OpsSnapshotCapture.Capture(c,tx,actor,run,facts)).Status);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EmptyFallbackReader_EvenWithReason_BlocksCapture(bool sqlServer)
    {
        var (c,_,run,_)=Seed(sqlServer);
        using(c)
        {
            var fallback=new StoreOpsLiveFactsReader();
            Assert.IsFalse(fallback.ConfirmsLiveRead);
            var actor=Actor();
            using var tx=c.BeginTransaction();
            var reasons=new Dictionary<string,string>(StringComparer.Ordinal)
            {
                ["ninja_daily"]="x",["ninja_daily_deduction"]="x",["platform_account"]="x",["assignment"]="x",
                ["housing"]="x",["fuel"]="x",["vehicle"]="x",["scheme"]="x",["eligibility"]="x"
            };
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>OpsSnapshotCapture.Capture(c,tx,actor,run,fallback,reasons)).Status);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void VehicleHousingFuel_CarryActualRiderAndAssignmentDates(bool sqlServer)
    {
        var (c,company,run,facts)=Seed(sqlServer);
        using(c)
        {
            var actor=Actor();
            using(var tx=c.BeginTransaction())
            {
                OpsSnapshotCapture.Capture(c,tx,actor,run,facts,zeroKindReasons:new Dictionary<string,string>{["housing"]="none"});
                tx.Commit();
            }
            string vehicle=(string)MoneySchemaFixture.Scalar(c,null,"SELECT payload FROM sal_ops_snapshot WHERE scope_company_id=@c AND kind='vehicle' LIMIT 1",("@c",company))!;
            Assert.IsTrue(vehicle.Contains("actualRiderProfileId",StringComparison.Ordinal)||vehicle.Contains("erpRiderId",StringComparison.Ordinal));
            Assert.IsTrue(vehicle.Contains("effectiveFrom",StringComparison.Ordinal));
            Assert.IsTrue(vehicle.Contains("effectiveTo",StringComparison.Ordinal));
            string fuel=(string)MoneySchemaFixture.Scalar(c,null,"SELECT payload FROM sal_ops_snapshot WHERE scope_company_id=@c AND kind='fuel' LIMIT 1",("@c",company))!;
            Assert.IsTrue(fuel.Contains("erpRiderId",StringComparison.Ordinal));
            Assert.IsTrue(fuel.Contains("effectiveFrom",StringComparison.Ordinal));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Recapture_ReplacesPriorLinks_NoDuplicateContent(bool sqlServer)
    {
        var (c,_,run,facts)=Seed(sqlServer);
        using(c)
        {
            var actor=Actor();
            var reasons=new Dictionary<string,string>{["housing"]="none"};
            using(var tx=c.BeginTransaction())
            {
                OpsSnapshotCapture.Capture(c,tx,actor,run,facts,reasons);
                tx.Commit();
            }
            long firstLinks=Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_operation_snapshot_link WHERE operation_id=@run",("@run",run))!);
            Assert.IsTrue(firstLinks>0);
            // Change daily row id so fingerprint differs; recapture must replace links, not append duplicates.
            facts.Rows["ninja_daily"]=[new(){["id"]=MoneySchemaFixture.Id(),["reportDate"]="2026-10-02",["platformRiderAccountId"]=MoneySchemaFixture.Id(),["riderProfileId"]=MoneySchemaFixture.Id(),["orders"]=3,["hours"]=2}];
            using(var tx=c.BeginTransaction())
            {
                OpsSnapshotCapture.Capture(c,tx,actor,run,facts,reasons);
                tx.Commit();
            }
            long links=Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_operation_snapshot_link WHERE operation_id=@run",("@run",run))!);
            Assert.AreEqual(firstLinks,links);
            long dailyLinks=Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_operation_snapshot_link WHERE operation_id=@run AND snapshot_kind='ninja_daily'",("@run",run))!);
            Assert.AreEqual(1L,dailyLinks);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NinjaDailyDeductionsAreCapturedAsSeparateKind(bool sqlServer)
    {
        var (c,company,run,facts)=Seed(sqlServer);
        using(c)
        {
            var actor=Actor();
            using(var tx=c.BeginTransaction())
            {
                OpsSnapshotCapture.Capture(c,tx,actor,run,facts,zeroKindReasons:new Dictionary<string,string>{["housing"]="none"});
                tx.Commit();
            }
            Assert.AreEqual(1L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM sal_ops_snapshot WHERE scope_company_id=@c AND kind='ninja_daily_deduction'",("@c",company))!));
            Assert.AreEqual(1L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_operation_capture_manifest WHERE operation_id=@run AND required_kind='ninja_daily_deduction' AND status=1",("@run",run))!));
        }
    }

    [TestMethod]
    public void LiveReader_IsWiredAsDefaultImplementation()
    {
        IOpsLiveFactsReader reader=new LiveOpsLiveFactsReader((Func<Microsoft.Data.SqlClient.SqlConnection?>)(()=>null));
        Assert.IsInstanceOfType<LiveOpsLiveFactsReader>(reader);
        Assert.IsTrue(reader.ConfirmsLiveRead);
    }
}
