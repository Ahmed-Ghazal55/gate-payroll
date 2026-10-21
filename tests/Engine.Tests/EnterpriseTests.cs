using System.Data.Common;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
[assembly:DoNotParallelize]

[TestClass]
public sealed class EnterpriseTests
{
    [TestMethod] public void Original52AssertionsStillRun()=>Assert.AreEqual(52,LegacyAssertions.Run());
    [DataTestMethod]
    [DataRow("Reviewer","GET","/api/payroll/runs",false)]
    [DataRow("Reviewer","POST","/api/payments",false)]
    [DataRow("Reviewer","POST","/api/submissions/test/review",true)]
    [DataRow("Accountant","POST","/api/payroll/approve",false)]
    [DataRow("Accountant","POST","/api/adjustments",true)]
    [DataRow("Accountant","POST","/api/payments",true)]
    [DataRow("Accountant","PUT","/api/users/test/status",false)]
    [DataRow("Reviewer","POST","/api/excel/riders/commit",false)]
    [DataRow("Admin","POST","/api/riders/sync-baseline",true)]
    public void PermissionMatrix(string role,string method,string path,bool allowed)=>Assert.AreEqual(allowed,UserAccess.Allowed(role,method,path));
    [TestMethod] public void PasswordAndProjection()
    {
        var u=UserAccess.Create("synthetic-user","Synthetic password 123","Reviewer");
        Assert.IsTrue(UserAccess.Password(u,"Synthetic password 123"));Assert.IsFalse(UserAccess.Password(u,"wrong"));
        u["status"]="suspended";Assert.IsFalse(UserAccess.Password(u,"Synthetic password 123"));
        var projection=UserAccess.Public(u);Assert.IsFalse(projection.ContainsKey("hash"));Assert.IsFalse(projection.ContainsKey("salt"));
        Assert.AreEqual("SYNTHETIC-USER",UserAccess.Normalize(" synthetic-user "));
    }
    [TestMethod] public void LegacyAdministratorRetainsRole()
    { var legacy=new JsonObject{["id"]="administrator"};Assert.AreEqual("Admin",UserAccess.Role(legacy));Assert.AreEqual("active",UserAccess.Status(legacy)); }
    private static JsonObject Policy()=>new(){["id"]="test-policy",["name"]="Test",["kind"]="approved_pay",["platform"]="all",["effectiveFrom"]="2026-01",["status"]="approved"};
    private static WorkLine Work(decimal pay)=>new("row","2000000001","Synthetic Rider","Keeta","test","2026-10","2026-10-01","2026-10-31",10,null,pay,true,"Test","Test",true);
    private static JsonObject Debt(string id,decimal remaining,decimal installment)=>new(){["id"]=id,["iqama"]="2000000001",["remainingBalance"]=remaining,["monthlyInstallment"]=installment,["status"]="active",["effectiveFrom"]="2026-10"};
    [TestMethod] public void DebtPreviewCapsTotalAndDoesNotMutate()
    {
        var first=Debt("a",500,300);var second=Debt("b",800,400);
        var r=Engine.Calculate("2000000001","2026-10",Policy(),[Work(450)],[],0,[first,second]);
        Assert.AreEqual(0m,r.Payable);Assert.AreEqual(-450m,r.Lines.Where(x=>x.Category=="debt_installment").Sum(x=>x.Amount));Assert.AreEqual(500m,first.N("remainingBalance"));
    }
    [TestMethod] public void DebtNeverExceedsOutstandingOrRunsWhenPaused()
    {
        var debt=Debt("a",40,500);var r=Engine.Calculate("2000000001","2026-10",Policy(),[Work(500)],[],0,[debt]);Assert.AreEqual(460m,r.Payable);
        debt["status"]="paused";Assert.AreEqual(500m,Engine.Calculate("2000000001","2026-10",Policy(),[Work(500)],[],0,[debt]).Payable);
        debt["status"]="active";debt["lastDeductionPeriod"]="2026-10";Assert.AreEqual(500m,Engine.Calculate("2000000001","2026-10",Policy(),[Work(500)],[],0,[debt]).Payable);
    }
    [TestMethod] public void NoDebtDeductionOnNegativeBalance()
    { var result=Engine.Calculate("2000000001","2026-10",Policy(),[Work(100)],[],-300,[Debt("a",500,200)]);Assert.AreEqual(200m,result.Debt);Assert.AreEqual(0,result.Lines.Count(x=>x.Category=="debt_installment")); }
    private static JsonObject Schema()=>new(){["titleAr"]="Test",["expiresAt"]=DateTimeOffset.UtcNow.AddDays(1).ToString("O"),["maxSubmissions"]=2,["fields"]=new JsonArray(new JsonObject{["id"]="identity",["labelAr"]="Identity",["type"]="iqama",["required"]=true},new JsonObject{["id"]="choice",["labelAr"]="Choice",["type"]="radio",["options"]=new JsonArray("tiers","commission")})};
    [TestMethod] public void BuilderRequiresUniqueFieldIdsAndEnumeratedChoices()
    {
        var schema=FormSchemas.Validate(Schema());var answers=FormSchemas.Answers(schema,new(){["identity"]="2000000001",["choice"]="tiers"});Assert.AreEqual("tiers",answers.S("choice"));
        Assert.ThrowsExactly<ApiError>(()=>FormSchemas.Answers(schema,new(){["identity"]="٢٠٠٠٠٠٠٠٠١"}));
        Assert.ThrowsExactly<ApiError>(()=>FormSchemas.Answers(schema,new(){["identity"]="2000000001",["choice"]="other"}));
        var invalid=Schema();((JsonArray)invalid["fields"]!)[1]!["id"]="identity";Assert.ThrowsExactly<ApiError>(()=>FormSchemas.Validate(invalid));
    }
    [TestMethod] public void DirectoryPreservesStateAndNormalizesRelationship()
    {
        var r=RiderDirectory.Normalize(new(){["iqama"]="2000000001",["name"]="Synthetic Rider",["engagement_type"]="OutsideRider",["status"]="في إجازة",["phone"]="0501234567"});
        Assert.AreEqual("External",r.S("engagementType"));Assert.AreEqual("OnLeave",r.S("status"));Assert.AreEqual("+966501234567",r.S("phone"));
    }
    [TestMethod] public void SqliteTransactionsRollbackAndAllowParallelReads()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-store-"+Guid.NewGuid().ToString("N"));
        try {
            var store=new Store(dir);Assert.ThrowsExactly<ApiError>(()=>store.Write<int>((c,tx)=>{Store.Save(c,"test",new(){["id"]="rollback"},tx);throw new ApiError("test");}));Assert.IsNull(store.Read(c=>Store.Get(c,"rollback","test")));
            Parallel.For(0,120,i=>{if(i%5==0)store.Write((c,tx)=>{Store.Save(c,"test",new(){["id"]="row-"+i},tx);return true;});else store.Read(c=>Store.List(c,"test").Count);});Assert.AreEqual(24,store.Read(c=>Store.List(c,"test").Count));
        } finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true); }
    }
    [TestMethod] public void StorageSnapshotPreservesDocumentsAndRejectsOverwrite()
    {
        string one=Path.Combine(Path.GetTempPath(),"gate-source-"+Guid.NewGuid().ToString("N")),two=Path.Combine(Path.GetTempPath(),"gate-target-"+Guid.NewGuid().ToString("N"));
        try { var source=new Store(one);source.Write((c,tx)=>{Store.Save(c,"rider",new(){["id"]="2000000001",["name"]="Synthetic Rider"},tx);return true;});var snapshot=StorageTransfer.Export(source);var target=new Store(two);Assert.AreEqual(1,StorageTransfer.Import(target,snapshot,StorageTransfer.Fingerprint(snapshot)));Assert.AreEqual("Synthetic Rider",target.Read(c=>Store.Get(c,"2000000001","rider"))!.S("name"));Assert.ThrowsExactly<ApiError>(()=>StorageTransfer.Import(target,snapshot,StorageTransfer.Fingerprint(snapshot)));}
        finally {Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(one))Directory.Delete(one,true);if(Directory.Exists(two))Directory.Delete(two,true);}
    }
    [TestMethod] public void SqlServerRoundTripAndTransactionRollback()
    {
        string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");if(string.IsNullOrWhiteSpace(configured)){Assert.Inconclusive("SQL integration requires the explicit isolated CI test connection.");return;}
        var master=new SqlConnectionStringBuilder(configured){InitialCatalog="master"};string name="GatePayrollTest_"+Guid.NewGuid().ToString("N");
        using var c=new SqlConnection(master.ConnectionString);c.Open();using(var create=c.CreateCommand()){create.CommandText="CREATE DATABASE ["+name+"]";create.ExecuteNonQuery();}
        string dir=Path.Combine(Path.GetTempPath(),name);
        try {var target=new SqlConnectionStringBuilder(configured){InitialCatalog=name};var store=new Store(dir,target.ConnectionString);Assert.AreEqual("mssql",store.Provider);store.Write((db,tx)=>{Store.Save(db,"test",new(){["id"]="roundtrip",["name"]="اسم تجريبي"},tx);return true;});Assert.AreEqual("اسم تجريبي",store.Read(db=>Store.Get(db,"roundtrip","test"))!.S("name"));Assert.ThrowsExactly<ApiError>(()=>store.Write<int>((db,tx)=>{Store.Save(db,"test",new(){["id"]="rollback"},tx);throw new ApiError("test");}));Assert.IsNull(store.Read(db=>Store.Get(db,"rollback","test")));}
        finally {SqlConnection.ClearAllPools();using var drop=c.CreateCommand();drop.CommandText="ALTER DATABASE ["+name+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+name+"]";drop.ExecuteNonQuery();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
