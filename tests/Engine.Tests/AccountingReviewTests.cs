using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class AccountingReviewTests
{
    private static string Bw => FinancialCore.Sponsors[0].Id;
    private static string Ex => FinancialCore.Sponsors[1].Id;
    private static ClaimsPrincipal User(string role, string name = "reviewer", params string[] extra) => UserAccess.Principal(new JsonObject
    {
        ["id"] = name, ["username"] = name, ["role"] = role,
        ["customPermissions"] = new JsonArray(extra.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray())
    });
    private static void Reject(Action action, int? status = null)
    {
        try { action(); } catch (ApiError e) { if(status.HasValue) Assert.AreEqual(status.Value, e.Status); return; }
        Assert.Fail("The prohibited accounting operation was accepted.");
    }
    private static void WithStore(Action<Store> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gate-accounting-review-" + Guid.NewGuid().ToString("N"));
        try { action(new Store(directory)); }
        finally { SqliteConnection.ClearAllPools(); if(Directory.Exists(directory))Directory.Delete(directory,true); }
    }

    [TestMethod] public void HierarchyPreservesLegacyGroupButRejectsNewGroupPostings()
    {
        AccountingPolicy.ValidateChart();
        Assert.IsFalse(FinancialCore.ResolveAccount("MAIN.TR").IsVault);
        foreach(var code in new[]{"1100","1110","1111","MAIN.TR","1200"})Reject(()=>FinancialCore.CreateJournalLine(code,Bw,10,0));
        Assert.AreEqual("MAIN.TR.DEBT",FinancialCore.DefaultVaultForSponsor("TR","MAIN","WorkPermit"));
        Assert.AreEqual("MAIN.TR",FinancialCore.ResolveAccount("MAIN.TR.SET").ParentCode);
    }

    [TestMethod] public void JahezCannotFundOrCollectUnrelatedDebts()
    {
        foreach(var account in FinancialCore.ChartOfAccounts.Where(a=>a.VaultKind=="JahezCashbox"))
        foreach(var category in FinancialCore.CategoriesCatalog.Where(c=>c.Code!="PlatformTransferredDebt"))
        foreach(var operation in new[]{"disbursement","collection"})
            Reject(()=>AccountingPolicy.ValidateOperation(account.Code,category.Code,operation,"Jahez"));
    }

    [TestMethod] public void JahezCollectionRequiresExactPlatformAndNeverDisburses()
    {
        AccountingPolicy.ValidateOperation("SUP.BW.SET","PlatformTransferredDebt","collection","Jahez");
        foreach(var platform in new string?[]{null,"","Keeta","Ninja"})Reject(()=>AccountingPolicy.ValidateOperation("SUP.BW.SET","PlatformTransferredDebt","collection",platform));
        Reject(()=>AccountingPolicy.ValidateOperation("SUP.BW.SET","PlatformTransferredDebt","disbursement","Jahez"));
        Reject(()=>AccountingPolicy.ValidateOperation("SUP.BW.FEE","PlatformTransferredDebt","collection","Jahez"));
    }

    [TestMethod] public void CashBankAndClearingPurposesStaySeparate()
    {
        AccountingPolicy.ValidateOperation("MAIN.BW.DEBT","CashAdvance","disbursement");
        AccountingPolicy.ValidateOperation("BANK.BW","BankAdvance","disbursement");
        Reject(()=>AccountingPolicy.ValidateOperation("BANK.BW","CashAdvance","disbursement"));
        Reject(()=>AccountingPolicy.ValidateOperation("MAIN.BW.DEBT","BankAdvance","disbursement"));
        Reject(()=>AccountingPolicy.ValidateOperation("MAIN.BW.SET","WorkPermit","disbursement"));
        Reject(()=>AccountingPolicy.ValidateOperation("MAIN.BW.FEE","Advance","collection"));
        AccountingPolicy.ValidateOperation("PETRO.BW","FuelOverage","collection");
        Reject(()=>AccountingPolicy.ValidateOperation("PETRO.BW","FuelOverage","disbursement"));
        Reject(()=>AccountingPolicy.ValidateOperation("PETRO.BW","Advance","collection"));
    }

    [TestMethod] public void TransferCannotLaunderJahezFundsOrCrossCompanies()
    {
        var from=FinancialCore.ResolveVault("MAIN.BW.DEBT");
        AccountingPolicy.ValidateCompanyTransfer(from,FinancialCore.ResolveVault("BANK.BW"));
        foreach(var code in new[]{"SUP.BW.SET","SUP.BW.FEE","MAIN.BW.SET","MAIN.BW.FEE","PETRO.BW","BANK.EX","MAIN.BW.DEBT"})
            Reject(()=>AccountingPolicy.ValidateCompanyTransfer(from,FinancialCore.ResolveVault(code)));
    }

    [TestMethod] public void OutgoingTransfersAndHandoversCannotInventCash()
    {
        AccountingPolicy.RequireAvailable(100m,100m);
        foreach(var amount in new[]{100.01m,-1m,1.001m})Reject(()=>AccountingPolicy.RequireAvailable(100m,amount));
        Reject(()=>AccountingPolicy.RequireAvailable(-200m,1m));
    }

    [TestMethod] public void FeeOwnershipMatchesConfirmedCompanyRevenuePolicy()
    {
        Assert.AreEqual("4101",AccountingPolicy.FeeCounterpart(AccountingPolicy.FeeBeneficiary));
        foreach(var beneficiary in new[]{"","unknown","jahez"})Reject(()=>AccountingPolicy.FeeCounterpart(beneficiary));
    }

    [TestMethod] public void LineCannotChangeOwnerOrSilentlyRoundMoney()
    {
        Reject(()=>FinancialCore.CreateJournalLine("BANK.BW",Ex,10,0));
        Reject(()=>FinancialCore.CreateJournalLine("1210",Bw,1.001m,0));
        Reject(()=>FinancialCore.CreateJournalLine("1210",Bw,10,10));
        Reject(()=>FinancialCore.CreateJournalLine("1210",Bw,-10,0));
    }

    [TestMethod] public void GloballyBalancedButCrossCompanyUnbalancedJournalRollsBack()
    {
        WithStore(store=>
        {
            Reject(()=>store.Write((c,tx)=>
            {
                Store.Save(c,"debt",new JsonObject{["id"]="must-rollback"},tx);
                return FinancialCore.PostJournal(c,tx,"test","test","2026-10","unbalanced",new JsonArray(
                    FinancialCore.CreateJournalLine("1210",Bw,100,0),FinancialCore.CreateJournalLine("BANK.EX",Ex,0,100)),"test");
            }));
            Assert.AreEqual(0,store.Read(c=>Store.List(c,"journal").Count));
            Assert.IsNull(store.Read(c=>Store.Get(c,"must-rollback","debt")));
        });
    }

    [TestMethod] public void ExplicitIntercompanyFundingBalancesEachOwnerAndCannotRepeat()
    {
        WithStore(store=>
        {
            JsonObject Post()=>store.Write((c,tx)=>FinancialCore.PostJournal(c,tx,"test","test","2026-10","explicit",new JsonArray(
                FinancialCore.CreateJournalLine("1210",Bw,100,0),FinancialCore.CreateJournalLine("BANK.EX",Ex,0,100),
                FinancialCore.CreateJournalLine("1310",Ex,100,0),FinancialCore.CreateJournalLine("1310",Bw,0,100)),"test"));
            var journal=Post();Assert.AreEqual(200m,journal.N("totalDebit"));Assert.AreEqual(4,journal["lines"]!.AsArray().Count);
            Reject(()=>Post());Assert.AreEqual(1,store.Read(c=>Store.List(c,"journal").Count));
        });
    }

    [TestMethod] public void SharedCostFundingFromThirdCompanyHasExplicitBalancedClearing()
    {
        WithStore(store=>{AccountingPostingTests.SeedCash(store,"BANK.EX",1000m);store.Write((c,tx)=>
        {
            var debt=new JsonObject{["id"]="split",["iqama"]="2000000001",["category"]="WorkPermit",["entryMode"]="Disbursement",["allocationMode"]="SharedSplit",
                ["totalCostAmount"]=900m,["companyShareAmount"]=300m,["totalAmount"]=600m,["originSponsorId"]=Bw,["creditorSponsorId"]=Bw,
                ["paymentVaultCode"]="BANK.EX",["sourceRef"]="split",["effectiveFrom"]="2026-10"};
            Debts.PostCreationJournalIfNeeded(c,tx,debt,"review");
            var journal=Store.Get(c,debt.S("journalEntryId"),"journal",tx)!;
            Assert.IsTrue(journal["lines"]!.AsArray().OfType<JsonObject>().GroupBy(l=>l.S("sponsorId")).All(g=>g.Sum(l=>l.N("debit")-l.N("credit"))==0));
            Assert.AreEqual(7,journal["lines"]!.AsArray().Count);return true;
        });});
    }

    [TestMethod] public void ReviewerAndOperationalRolesCannotUseUnrelatedFinanceRoutes()
    {
        Assert.IsTrue(AccessPolicy.Allowed(User("Reviewer"),"POST","/api/submissions/s/review"));
        Assert.IsFalse(AccessPolicy.Allowed(User("Reviewer"),"GET","/api/debts"));
        Assert.IsFalse(AccessPolicy.Allowed(User("FleetSupervisor"),"POST","/api/debts",new JsonObject{["category"]="WorkPermit"}));
        Assert.IsTrue(AccessPolicy.Allowed(User("FleetSupervisor"),"POST","/api/debts",new JsonObject{["category"]="TrafficFine"}));
        Assert.IsTrue(AccessPolicy.Allowed(User("HousingSupervisor"),"POST","/api/debts",new JsonObject{["category"]="HousingDeduction"}));
        Assert.IsFalse(AccessPolicy.Allowed(User("HousingSupervisor"),"POST","/api/debts",new JsonObject{["category"]="CashAdvance"}));
        Assert.IsFalse(AccessPolicy.Allowed(new ClaimsPrincipal(),"GET","/api/me"));
    }

    [TestMethod] public void UserSpecificGrantAndDenyActuallyControlServerAuthorization()
    {
        var user=new JsonObject{["id"]="x",["username"]="x",["role"]="Reviewer",["customPermissions"]=new JsonArray("debts.view")};
        Assert.IsTrue(AccessPolicy.Allowed(UserAccess.Principal(user),"GET","/api/debts"));
        user["deniedPermissions"]=new JsonArray("debts.view","forms.review");
        var principal=UserAccess.Principal(user);
        Assert.IsFalse(AccessPolicy.Allowed(principal,"GET","/api/debts"));
        Assert.IsFalse(AccessPolicy.Allowed(principal,"POST","/api/submissions/s/review"));
        Assert.IsFalse(UserAccess.Public(user).ContainsKey("hash"));
    }

    [TestMethod] public void ApprovalNeedsIndependentActorCorrectDepartmentAndRank()
    {
        var request=new JsonObject{["createdBy"]="requester",["targetDepartment"]="FIN",["requiredRankLevel"]=4};
        AccessPolicy.ValidateApproval(User("FinancialAccountant"),request,new());
        Reject(()=>AccessPolicy.ValidateApproval(User("FinancialAccountant","requester"),request,new()),403);
        Reject(()=>AccessPolicy.ValidateApproval(User("HRSpecialist"),request,new()),403);
        Reject(()=>AccessPolicy.ValidateApproval(User("Accountant","reviewer","approvals.decide"),request,new()),403);
        Reject(()=>AccessPolicy.ValidateApproval(User("Admin","requester"),request,new()),403);
        AccessPolicy.ValidateApproval(User("Admin","requester"),request,new JsonObject{["overrideSelf"]=true,["note"]="Emergency override with evidence"});
    }
    [TestMethod] public void CollectionAndOpeningTemplatesKeepTheirOwnExactCanonicalHeaderWidths()
    {
        DebtFileSchemas.Validate(DebtFileSchemas.CollectionEn,false);DebtFileSchemas.Validate(DebtFileSchemas.CollectionAr,false);
        DebtFileSchemas.Validate(DebtFileSchemas.OpeningEn,true);DebtFileSchemas.Validate(DebtFileSchemas.OpeningAr,true);
        DebtFileSchemas.Validate(DebtFileSchemas.OpeningEn.Concat(new[]{"platform"}).ToArray(),true);
        DebtFileSchemas.Validate(DebtFileSchemas.OpeningAr.Concat(new[]{"المنصة (Platform)"}).ToArray(),true);
        Assert.ThrowsExactly<ApiError>(()=>DebtFileSchemas.Validate(DebtFileSchemas.CollectionEn.Concat(new[]{"platform"}).ToArray(),false));
        Assert.ThrowsExactly<ApiError>(()=>DebtFileSchemas.Validate(DebtFileSchemas.OpeningEn.Concat(new[]{"wrong"}).ToArray(),true));
        Assert.ThrowsExactly<ApiError>(()=>DebtFileSchemas.Validate(DebtFileSchemas.OpeningEn,false));
        Assert.ThrowsExactly<ApiError>(()=>DebtFileSchemas.Validate(DebtFileSchemas.CollectionEn.Reverse().ToArray(),false));
    }

}
