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
    [TestMethod]
    [DataRow("Reviewer","GET","/api/payroll/runs",false)]
    [DataRow("Reviewer","POST","/api/payments",false)]
    [DataRow("Reviewer","POST","/api/submissions/test/review",true)]
    [DataRow("Accountant","POST","/api/payroll/approve",false)]
    [DataRow("Accountant","POST","/api/adjustments",true)]
    [DataRow("Accountant","POST","/api/payments",true)]
    [DataRow("Accountant","PUT","/api/users/test/status",false)]
    [DataRow("Reviewer","POST","/api/excel/riders/commit",false)]
    [DataRow("Admin","POST","/api/riders/sync-baseline",true)]
    [DataRow("Reviewer","DELETE","/api/submissions/test",false)]
    [DataRow("Accountant","DELETE","/api/submissions/test",false)]
    [DataRow("Reviewer","POST","/api/submissions/bulk-delete",false)]
    [DataRow("Reviewer","POST","/api/submissions/bulk-approve",true)]
    [DataRow("Reviewer","PUT","/api/campaigns/test",false)]
    [DataRow("Reviewer","GET","/api/campaigns/test/analytics",true)]
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
    [TestMethod] public void LegacyCampaignHashAndNewSharingLinkSurviveMigration()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-legacy-"+Guid.NewGuid().ToString("N"));
        try {var store=new Store(dir);store.Write((c,tx)=>{Store.Save(c,"campaign",new(){["id"]="legacy",["tokenHash"]="original-hash",["title"]="Legacy",["status"]="open"},tx);return true;});CampaignLinks.Migrate(store);string token=store.Read(c=>Store.Get(c,"legacy","campaign"))!.S("sharingToken");Assert.AreEqual(64,token.Length);CampaignLinks.Migrate(store);var campaign=store.Read(c=>Store.Get(c,"legacy","campaign"))!;Assert.AreEqual(token,campaign.S("sharingToken"));Assert.AreEqual("original-hash",campaign.S("tokenHash"));}
        finally {Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
    [TestMethod] public void DebtPostingRollsBackAllLinesIfOneBalanceChanged()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-debt-"+Guid.NewGuid().ToString("N"));
        try {var store=new Store(dir);var first=Debt("a",300,200);var second=Debt("b",100,100);store.Write((c,tx)=>{Store.Save(c,"debt",first,tx);Store.Save(c,"debt",second,tx);return true;});var result=Engine.Calculate("2000000001","2026-10",Policy(),[Work(500)],[],0,[first,second]);store.Write((c,tx)=>{var changed=Store.Get(c,"b","debt",tx)!;changed["remainingBalance"]=0;Store.Save(c,"debt",changed,tx);return true;});Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>{Debts.Post(c,tx,result,"test-run");return true;}));Assert.AreEqual(300m,store.Read(c=>Store.Get(c,"a","debt"))!.N("remainingBalance"));Assert.AreEqual(0,store.Read(c=>Store.List(c,"ledger").Count));}
        finally {Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
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
        try {var target=new SqlConnectionStringBuilder(configured){InitialCatalog=name};var store=new Store(dir,target.ConnectionString);Assert.AreEqual("mssql",store.Provider);store.Write((db,tx)=>{Store.Save(db,"test",new(){["id"]="roundtrip",["name"]="اسم تجريبي"},tx);return true;});Assert.AreEqual("اسم تجريبي",store.Read(db=>Store.Get(db,"roundtrip","test"))!.S("name"));Assert.ThrowsExactly<ApiError>(()=>store.Write<int>((db,tx)=>{Store.Save(db,"test",new(){["id"]="rollback"},tx);throw new ApiError("test");}));Assert.IsNull(store.Read(db=>Store.Get(db,"rollback","test")));var upgraded=new Store(dir,target.ConnectionString);Assert.IsTrue(upgraded.DataPreservationVerified);Assert.IsTrue(upgraded.ReleaseCheckpointPresent);Assert.AreEqual("اسم تجريبي",upgraded.Read(db=>Store.Get(db,"roundtrip","test"))!.S("name"));}
        finally {SqlConnection.ClearAllPools();using var drop=c.CreateCommand();drop.CommandText="ALTER DATABASE ["+name+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+name+"]";drop.ExecuteNonQuery();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
    [TestMethod] public void CampaignFieldsRejectHiddenOrInvalidInputs()
    {
        Assert.ThrowsExactly<ApiError>(()=>CampaignOperations.ValidateFields(new JsonArray("iban")));
        Assert.ThrowsExactly<ApiError>(()=>CampaignOperations.ValidateFields(new JsonArray("city","city")));
        var campaign=new JsonObject{["fields"]=CampaignOperations.ValidateFields(new JsonArray("city","phone"))};
        Assert.ThrowsExactly<ApiError>(()=>CampaignOperations.Answers(campaign,new(){["city"]="Riyadh"}));
        Assert.ThrowsExactly<ApiError>(()=>CampaignOperations.Answers(new(),new(){["city"]="Riyadh"}));
        var answers=CampaignOperations.Answers(campaign,new(){["city"]=" Riyadh ",["phone"]="0500000001"});Assert.AreEqual("Riyadh",answers.S("city"));Assert.AreEqual("+966500000001",answers.S("phone"));
    }
    [TestMethod] public void ExtraAnswersRemainInPriorVersionAndProjectionHonorsActiveFields()
    {
        var d=new JsonObject{["name"]="Synthetic Rider",["workSystem"]="tiers",["city"]="Riyadh",["phone"]="+966500000001",["status"]="approved",["version"]=1};
        SubmissionChoices.ArchiveAndEdit(d,new("2000000001","Synthetic Updated","commission"));d["city"]="Jeddah";
        Assert.AreEqual("Riyadh",d["history"]![0]!.S("city"));Assert.AreEqual("pending",d.S("status"));Assert.AreEqual(2,SubmissionChoices.Version(d));
        Assert.IsFalse(SubmissionChoices.ChoiceView(d,new()).ContainsKey("phone"));
        Assert.AreEqual("Jeddah",SubmissionChoices.ChoiceView(d,new(){["fields"]=new JsonArray("city")}).S("city"));
    }
    [TestMethod] public void BatchReviewRollbackPreservesEarlierRepliesOnLegacyBankingFailure()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-review-"+Guid.NewGuid().ToString("N"));
        try {var store=new Store(dir);store.Write((c,tx)=>{foreach(string id in new[]{"first","second"})Store.Save(c,"submission",new(){["id"]=id,["iqama"]="2000000001",["name"]="Synthetic Rider",["workSystem"]="tiers",["status"]="pending",["iban"]=id=="second"?"legacy-test-value":""},tx);return true;});
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>{foreach(string id in new[]{"first","second"})CampaignOperations.Review(c,tx,Store.Get(c,id,"submission",tx)!,new(){["status"]="approved"},"test-admin");return true;}));
            Assert.AreEqual("pending",store.Read(c=>Store.Get(c,"first","submission"))!.S("status"));Assert.AreEqual(0,store.Read(c=>Store.List(c,"rider").Count));Assert.AreEqual(0,store.Read(c=>Store.List(c,"audit").Count));
        }finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    [TestMethod] public void FinancialDoubleEntryAndAllocationAndInterSponsorSettlementWork()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-finance-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            Assert.AreEqual(3,FinancialCore.Sponsors.Length);
            Assert.AreEqual("019c18d5-62e1-7000-8000-000000000041",FinancialCore.ResolveSponsor("BW").Id);
            Assert.AreEqual("019c18d5-62e1-7000-8000-000000000042",FinancialCore.ResolveSponsor("EX").Id);
            Assert.AreEqual("019c18d5-62e1-7000-8000-000000000040",FinancialCore.ResolveSponsor("TR").Id);

            // Verify HR Expense Accounts are prefixed with "مصروف "
            foreach(var code in new[]{"5101","5102","5103","5104","5105","5106","5107","5108"})
            {
                var acc=FinancialCore.ResolveAccount(code);
                Assert.IsTrue(acc.NameAr.StartsWith("مصروف "),$"Account {code} must start with مصروف");
                Assert.AreEqual("Debit",acc.NaturalSide);
            }

            // Test SharedSplit Journal: WorkPermit 9600 SAR (Company 4600 + Rider 5000) paid from MAIN.BW.SET
            store.Write((c,tx)=>
            {
                var debt=new JsonObject
                {
                    ["id"]="debt-wp-1",
                    ["iqama"]="2491234567",
                    ["riderName"]="محمد أحمد",
                    ["debtorType"]="Rider",
                    ["category"]="WorkPermit",
                    ["allocationMode"]="SharedSplit",
                    ["totalCostAmount"]=9600m,
                    ["companyShareAmount"]=4600m,
                    ["totalAmount"]=5000m,
                    ["remainingBalance"]=5000m,
                    ["monthlyInstallment"]=1000m,
                    ["originSponsorId"]=FinancialCore.Sponsors[0].Id,
                    ["creditorSponsorId"]=FinancialCore.Sponsors[0].Id,
                    ["paymentVaultCode"]="MAIN.BW.SET",
                    ["sourceRef"]="WP-2026-001",
                    ["effectiveFrom"]="2026-10"
                };
                Store.Save(c,"debt",debt,tx);
                Debts.PostCreationJournalIfNeeded(c,tx,debt,"admin");
                var journal=Store.Get(c,debt.S("journalEntryId"),"journal",tx)!;
                Assert.AreEqual(9600m,journal.N("totalDebit"));
                Assert.AreEqual(9600m,journal.N("totalCredit"));
                var lines=journal["lines"]!.AsArray();
                Assert.AreEqual(3,lines.Count);
                Assert.AreEqual("5101",lines[0]!.AsObject().S("accountCode"));
                Assert.AreEqual("increase",lines[0]!.AsObject().S("effectDirection"));
                Assert.AreEqual("1210",lines[1]!.AsObject().S("accountCode"));
                Assert.AreEqual("increase",lines[1]!.AsObject().S("effectDirection"));
                Assert.AreEqual("MAIN.BW.SET",lines[2]!.AsObject().S("accountCode"));
                Assert.AreEqual("decrease",lines[2]!.AsObject().S("effectDirection"));
                return true;
            });
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if(Directory.Exists(dir))Directory.Delete(dir,true);
        }
    }

}
