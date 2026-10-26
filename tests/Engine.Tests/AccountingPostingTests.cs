using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class AccountingPostingTests
{
    private const string Iqama="2000000001";
    private static string Bw=>FinancialCore.Sponsors[0].Id;
    private static string Ex=>FinancialCore.Sponsors[1].Id;
    private static string Tr=>FinancialCore.Sponsors[2].Id;
    private static ClaimsPrincipal Admin=>UserAccess.Principal(new(){["role"]="Admin",["username"]="review-admin"});
    private static void InStore(Action<Store> action){string path=Path.Combine(Path.GetTempPath(),"gate-posting-"+Guid.NewGuid().ToString("N"));try{action(new Store(path));}finally{SqliteConnection.ClearAllPools();if(Directory.Exists(path))Directory.Delete(path,true);}}
    public static void SeedCash(Store store,string account,decimal amount)=>store.Write((c,tx)=>FinancialCore.PostJournal(c,tx,"test_opening","Synthetic opening","2026-10","seed:"+account,new JsonArray(FinancialCore.CreateJournalLine(account,FinancialCore.ResolveVault(account).DefaultSponsorId!,amount,0),FinancialCore.CreateJournalLine("3101",FinancialCore.ResolveVault(account).DefaultSponsorId!,0,amount)),"synthetic"));
    private static JsonObject Debt(string id,string category="Advance",string mode="Disbursement",decimal amount=600m)=>new(){["id"]=id,["iqama"]=Iqama,["riderName"]="Synthetic Rider",["category"]=category,["entryMode"]=mode,["totalAmount"]=amount,["totalCostAmount"]=amount,["remainingBalance"]=amount,["paidAmount"]=0m,["monthlyInstallment"]=100m,["originSponsorId"]=Bw,["creditorSponsorId"]=Bw,["originalCreditorSponsorId"]=Bw,["paymentVaultCode"]="MAIN.BW.DEBT",["sourceRef"]=id,["effectiveFrom"]="2026-10",["createdAt"]="2026-10-01T00:00:00Z",["status"]="active",["accountingVersion"]=2,["receivableAccountCode"]=category=="PlatformTransferredDebt"?"1230":"1210"};
    private static void Create(Store store,JsonObject debt)=>store.Write((c,tx)=>{Store.Save(c,"debt",debt,tx);Debts.PostCreationJournalIfNeeded(c,tx,debt,"synthetic");return true;});
    private static PayrollResult Result(decimal opening,params PayLine[] lines){decimal movement=lines.Sum(l=>l.Amount),closing=opening+movement;return new(Iqama,"2026-10","Synthetic",opening,movement,closing,Math.Max(0,closing),Math.Max(0,-closing),0,lines,[]);}
    private static JsonObject Run(string id)=>new(){["id"]=id,["iqama"]=Iqama,["sourceRows"]=new JsonArray(new JsonObject{["sourceRef"]="work",["platform"]="Jahez"})};

    [TestMethod] public void AccrualDoesNotSpendCashAndRecognizesSharedCostAndVendorPayable()
    {
        InStore(store=>{var debt=Debt("invoice","WorkPermit","Accrual");debt["totalCostAmount"]=900m;debt["companyShareAmount"]=300m;debt["allocationMode"]="SharedSplit";debt["recognitionAccountCode"]="2130";Create(store,debt);
            store.Read(c=>{Assert.AreEqual(0m,AccountingPolicy.Balance(c,null,"MAIN.BW.DEBT"));Assert.AreEqual(600m,AccountingEvents.DebtBalance(c,null,debt,Bw));Assert.AreEqual(-900m,AccountingPolicy.Balance(c,null,"2130"));return true;});});
    }
    [TestMethod] public void DisbursementWithoutAvailableCashRollsBackDebtAndJournal()
    {
        InStore(store=>{Assert.ThrowsExactly<ApiError>(()=>Create(store,Debt("no-funds")));Assert.AreEqual(0,store.Read(c=>Store.List(c,"debt").Count));Assert.AreEqual(0,store.Read(c=>Store.List(c,"journal").Count));});
    }
    [TestMethod] public void CashDisbursementReducesFundsExactlyOnce()
    {
        InStore(store=>{SeedCash(store,"MAIN.BW.DEBT",1000);var debt=Debt("advance");Create(store,debt);store.Write((c,tx)=>{Debts.PostCreationJournalIfNeeded(c,tx,Store.Get(c,"advance","debt",tx)!,"synthetic");return true;});Assert.AreEqual(400m,store.Read(c=>AccountingPolicy.Balance(c,null,"MAIN.BW.DEBT")));Assert.AreEqual(2,store.Read(c=>Store.List(c,"journal").Count));});
    }
    [TestMethod] public void TransferredPlatformDebtCreatesPlatformLiabilityWithoutFictitiousCash()
    {
        InStore(store=>{var debt=Debt("platform","PlatformTransferredDebt","Accrual",1200);debt["platform"]="Jahez";Create(store,debt);store.Read(c=>{Assert.AreEqual(1200m,AccountingEvents.DebtBalance(c,null,debt,Bw));Assert.AreEqual(-1200m,AccountingPolicy.Balance(c,null,"2140"));Assert.AreEqual(0m,AccountingPolicy.Balance(c,null,"SUP.BW.SET"));return true;});});
        Assert.ThrowsExactly<ApiError>(()=>AccountingEvents.EntryMode(new(){["entryMode"]="Disbursement"},"PlatformTransferredDebt"));
        Assert.ThrowsExactly<ApiError>(()=>AccountingEvents.EntryMode(new(){["entryMode"]="OpeningBalance"},"NegativeSalaryCarryover"));
    }
    [TestMethod] public void SalaryAccrualAndPaymentReconcileWithRealCashAndPreventOverpayment()
    {
        InStore(store=>{SeedCash(store,"BANK.BW",2000);store.Write((c,tx)=>{var run=Run("salary");AccountingEvents.PostPayroll(c,tx,Result(0,new PayLine("base_salary","salary",1500,"policy")),run,Bw,"synthetic");Store.Save(c,"run",run,tx);var payment=new JsonObject{["id"]="payment",["iqama"]=Iqama,["amount"]=-1000m,["period"]="2026-10",["sourceRef"]="payment"};AccountingEvents.PostPayment(c,tx,payment,Bw,"BANK.BW","synthetic");return true;});
            Assert.AreEqual(-500m,store.Read(c=>AccountingEvents.Balance(c,null,"2110",Iqama,Bw)));Assert.AreEqual(1000m,store.Read(c=>AccountingPolicy.Balance(c,null,"BANK.BW")));
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>AccountingEvents.PostPayment(c,tx,new(){["iqama"]=Iqama,["amount"]=-501m,["period"]="2026-10",["sourceRef"]="over"},Bw,"BANK.BW","synthetic")));
        });
    }
    [TestMethod] public void CommissionSeparatesPlatformReceivableCompanyIncomeAndPayrollDeficit()
    {
        InStore(store=>{store.Write((c,tx)=>{AccountingEvents.PostPayroll(c,tx,Result(0,new PayLine("platform_settlement","proceeds",1000,"work"),new PayLine("company_fee","company fee",-2500,"policy")),Run("commission"),Bw,"synthetic");return true;});
            store.Read(c=>{Assert.AreEqual(1000m,AccountingPolicy.Balance(c,null,"1240"));Assert.AreEqual(-2500m,AccountingPolicy.Balance(c,null,"4102"));Assert.AreEqual(1500m,AccountingEvents.Balance(c,null,"1211",Iqama,Bw));Assert.AreEqual(0m,AccountingEvents.Balance(c,null,"2110",Iqama,Bw));return true;});
            store.Write((c,tx)=>{AccountingEvents.PostPayroll(c,tx,Result(-1500,new PayLine("base_salary","new earnings",2000,"policy")),Run("next"),Bw,"synthetic");return true;});Assert.AreEqual(0m,store.Read(c=>AccountingEvents.Balance(c,null,"1211",Iqama,Bw)));Assert.AreEqual(-500m,store.Read(c=>AccountingEvents.Balance(c,null,"2110",Iqama,Bw)));
        });
    }
    [TestMethod] public void PayrollDoesNotInventLegacyOpeningOrReassignAnotherCompanyBalance()
    {
        InStore(store=>{Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>{AccountingEvents.PostPayroll(c,tx,Result(500,new PayLine("base_salary","salary",1000,"policy")),Run("legacy"),Bw,"synthetic");return true;}));Assert.AreEqual(0,store.Read(c=>Store.List(c,"journal").Count));});
    }
    [TestMethod] public void AlreadyPaidCurrentPeriodDoesNotPostAgainOrIncreaseInstallmentCapacity()
    {
        var policy=new JsonObject{["id"]="p",["kind"]="approved_pay",["status"]="approved",["platform"]="all",["effectiveFrom"]="2026-10"};
        var work=new WorkLine("work",Iqama,"Synthetic","Jahez","synthetic","2026-10","2026-10-01","2026-10-31",0,null,500,true,"Synthetic","Synthetic",true);
        var result=Engine.Calculate(Iqama,"2026-10",policy,[work],[],500,[],[new JsonObject{["id"]="already-paid",["amount"]=-100}]);
        Assert.AreEqual(900m,result.ClosingBalance);Assert.AreEqual(400m,result.PeriodMovement);
        InStore(store=>{store.Write((c,tx)=>{FinancialCore.PostJournal(c,tx,"test_opening","Synthetic payable","2026-10","prior",new JsonArray(FinancialCore.CreateJournalLine("3101",Bw,400,0,"",Iqama),FinancialCore.CreateJournalLine("2110",Bw,0,400,"",Iqama)),"synthetic");AccountingEvents.PostPayroll(c,tx,result,Run("already-paid"),Bw,"synthetic");AccountingEvents.VerifyPayrollClosing(c,tx,result,Bw);return true;});Assert.AreEqual(-900m,store.Read(c=>AccountingEvents.Balance(c,null,"2110",Iqama,Bw)));});
    }
    [TestMethod] public void PayrollInstallmentAcrossCompaniesUsesExplicitIntercompanyPair()
    {
        InStore(store=>{var debt=Debt("external-owner","WorkPermit","Accrual",600);debt["originSponsorId"]=Ex;debt["creditorSponsorId"]=Ex;debt["originalCreditorSponsorId"]=Ex;Create(store,debt);
            store.Write((c,tx)=>{var result=Result(0,new PayLine("base_salary","salary",1000,"policy"),new PayLine("debt_installment","installment",-100,"external-owner"));var run=Run("run");AccountingEvents.PostPayroll(c,tx,result,run,Bw,"synthetic");Store.Save(c,"run",run,tx);Debts.Post(c,tx,result,"run");return true;});
            store.Read(c=>{Assert.AreEqual(-900m,AccountingEvents.Balance(c,null,"2110",Iqama,Bw));Assert.AreEqual(500m,AccountingEvents.DebtBalance(c,null,Store.Get(c,"external-owner","debt")!,Ex));Assert.IsTrue(Store.List(c,"journal").All(j=>j["lines"]!.AsArray().OfType<JsonObject>().GroupBy(l=>l.S("sponsorId")).All(g=>g.Sum(l=>l.N("debit")-l.N("credit"))==0)));return true;});
        });
    }
    [TestMethod] public void MultipleDebtTransfersKeepImmutableHistoryAndOneCurrentReceivable()
    {
        InStore(store=>{Create(store,Debt("movable","WorkPermit","Accrual"));
            foreach(var pair in new[]{(Bw,Ex),(Ex,Tr)})store.Write((c,tx)=>DebtTransfers.Post(c,tx,"movable",new(){["newCreditorSponsorId"]=pair.Item2,["expectedCreditorSponsorId"]=pair.Item1,["expectedRemainingBalance"]=600,["transferRef"]="move:"+pair.Item2},Admin));
            store.Read(c=>{Assert.AreEqual(2,Store.List(c,"debt_transfer").Count);var d=Store.Get(c,"movable","debt")!;Assert.AreEqual(2,d["ownershipHistory"]!.AsArray().Count);Assert.AreEqual(Tr,d.S("creditorSponsorId"));Assert.AreEqual(0m,AccountingEvents.DebtBalance(c,null,d,Bw));Assert.AreEqual(0m,AccountingEvents.DebtBalance(c,null,d,Ex));Assert.AreEqual(600m,AccountingEvents.DebtBalance(c,null,d,Tr));
                foreach(var owner in new[]{Bw,Ex,Tr}){var statement=RiderStatements.Build(c,Iqama,owner,Admin);Assert.AreEqual(owner==Tr?600m:0m,statement.N("totalRemainingDebt"));Assert.AreEqual(0m,statement.N("reconciliationDifference"));}
                Assert.AreEqual(600m,RiderStatements.Build(c,Iqama,null,Admin).N("statementBalance"));return true;});});
    }
    [TestMethod] public void CashPurchaseCannotOverdrawAndStaleOwnerCannotBeTransferred()
    {
        InStore(store=>{Create(store,Debt("sale","WorkPermit","Accrual"));var input=new JsonObject{["newCreditorSponsorId"]=Ex,["expectedCreditorSponsorId"]=Bw,["expectedRemainingBalance"]=600,["transferRef"]="sale",["settlementMode"]="cash_purchase"};
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>DebtTransfers.Post(c,tx,"sale",input,Admin)));Assert.AreEqual(Bw,store.Read(c=>Store.Get(c,"sale","debt"))!.S("creditorSponsorId"));Assert.AreEqual(0,store.Read(c=>Store.List(c,"debt_transfer").Count));
            SeedCash(store,"MAIN.EX.DEBT",1000);store.Write((c,tx)=>DebtTransfers.Post(c,tx,"sale",input,Admin));Assert.AreEqual(400m,store.Read(c=>AccountingPolicy.Balance(c,null,"MAIN.EX.DEBT")));
            input["newCreditorSponsorId"]=Tr;input["transferRef"]="stale";Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>DebtTransfers.Post(c,tx,"sale",input,Admin)));
        });
    }
    [TestMethod] public void OperationalUsersStartWithoutCompanyScopeAndCannotReadOtherAccounts()
    {
        var u=new JsonObject{["role"]="PlatformsSupervisor",["username"]="platform"};var user=UserAccess.Principal(u);Assert.IsFalse(FinancialScopes.Sponsor(user,Bw));
        u["financialScope"]=FinancialScopes.Normalize(new(){["sponsorIds"]=new JsonArray("BW"),["accountCodes"]=new JsonArray("SUP.BW.SET"),["categoryCodes"]=new JsonArray("PlatformTransferredDebt"),["platforms"]=new JsonArray("Jahez")});user=UserAccess.Principal(u);
        Assert.IsTrue(FinancialScopes.Account(user,"SUP.BW.SET"));Assert.IsFalse(FinancialScopes.Account(user,"BANK.BW"));Assert.IsFalse(FinancialScopes.Account(user,"SUP.EX.SET"));var d=Debt("d","PlatformTransferredDebt","Accrual");d["platform"]="Jahez";Assert.IsTrue(FinancialScopes.Debt(user,d));d["platform"]="Keeta";Assert.IsFalse(FinancialScopes.Debt(user,d));
    }
    [TestMethod] public void WarehouseEquipmentPermissionDoesNotPermitTrafficFinesOrHr()
    {
        var user=UserAccess.Principal(new(){["role"]="WarehouseOfficer",["username"]="warehouse"});
        Assert.IsTrue(AccessPolicy.Allowed(user,"POST","/api/debts",new(){["category"]="EquipmentLoan"}));Assert.IsFalse(AccessPolicy.Allowed(user,"POST","/api/debts",new(){["category"]="TrafficFine"}));Assert.IsFalse(AccessPolicy.Allowed(user,"POST","/api/debts",new(){["category"]="WorkPermit"}));
    }
    [TestMethod] public void ScopedJournalProjectionDoesNotReturnAnotherCompanyLines()
    {
        var userDoc=new JsonObject{["role"]="FinancialAccountant",["username"]="finance",["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray("BW"),["accountCodes"]=new JsonArray("BANK.BW"),["categoryCodes"]=new JsonArray("Advance"),["platforms"]=new JsonArray("Jahez")}};var user=UserAccess.Principal(userDoc);
        var journal=new JsonObject{["lines"]=new JsonArray(FinancialCore.CreateJournalLine("1210",Bw,100,0),FinancialCore.CreateJournalLine("1310",Bw,0,100),FinancialCore.CreateJournalLine("1310",Ex,100,0),FinancialCore.CreateJournalLine("BANK.EX",Ex,0,100))};
        var projected=FinancialScopes.Journal(user,journal)!;Assert.AreEqual(2,projected["lines"]!.AsArray().Count);Assert.AreEqual(100m,projected.N("totalDebit"));Assert.IsTrue(projected.B("scopeFiltered"));
    }
    [TestMethod] public void OpeningPreviewValidatesOwnershipAndRequiresExactLegacyPayable()
    {
        InStore(store=>{store.Write((c,tx)=>{Store.Save(c,"rider",new(){["id"]=Iqama,["iqama"]=Iqama},tx);Store.Save(c,"ledger",new(){["id"]="legacy",["iqama"]=Iqama,["amount"]=500},tx);return true;});
            store.Read(c=>{var rows=new JsonArray(new JsonObject{["accountCode"]="2110",["sponsorCode"]="BW",["iqama"]=Iqama,["verifiedBalance"]=499,["sourceRef"]="opening",["notes"]="Reviewed source"});Assert.ThrowsExactly<ApiError>(()=>TreasuryEntries.ValidateOpening(c,null,rows));rows[0]!["verifiedBalance"]=500;Assert.AreEqual(500m,TreasuryEntries.ValidateOpening(c,null,rows)[0].N("adjustment"));return true;});
        });
    }
    [TestMethod] public void SessionCookieStaysSmallWhileServerPermissionsAndScopeRemainCurrent()
    {
        var user=UserAccess.Create("synthetic-user","LongSyntheticPassword123!","Accountant");
        Assert.AreEqual(4,UserAccess.SessionPrincipal(user).Claims.Count());Assert.IsFalse(UserAccess.SessionPrincipal(user).HasClaim("permission","debts.collect"));Assert.IsTrue(AccessPolicy.Has(UserAccess.Principal(user),"debts.collect"));
        user["deniedPermissions"]=new JsonArray("debts.collect");Assert.IsFalse(AccessPolicy.Has(UserAccess.Principal(user),"debts.collect"));
    }
    [TestMethod] public void SameExternalReferenceOnDifferentPlatformsKeepsSeparateReceivables()
    {
        var policy=new JsonObject{["id"]="p",["kind"]="commission",["status"]="approved",["platform"]="all",["effectiveFrom"]="2026-10",["config"]=new JsonObject{["companyFee"]=0}};
        var work=new WorkLine("same-reference",Iqama,"Synthetic","Jahez","first","2026-10","2026-10-01","2026-10-31",0,1000,null,true,"Synthetic","Synthetic",true);
        var result=Engine.Calculate(Iqama,"2026-10",policy,[work,work with{Platform="Keeta",AccountId="second",PlatformRevenue=2000}],[],0);
        InStore(store=>{store.Write((c,tx)=>AccountingEvents.PostPayroll(c,tx,result,Run("two-platforms"),Bw,"synthetic"));Assert.AreEqual(1000m,store.Read(c=>AccountingPolicy.Balance(c,null,"1240")));Assert.AreEqual(2000m,store.Read(c=>AccountingPolicy.Balance(c,null,"1241")));});
    }
    [TestMethod] public void FutureInstallmentStartDoesNotPostTodaysDisbursementInFutureMonth()
    {
        InStore(store=>{SeedCash(store,"MAIN.BW.DEBT",1000);var debt=Debt("future-start");debt["effectiveFrom"]=DateTime.UtcNow.AddMonths(3).ToString("yyyy-MM");Create(store,debt);store.Read(c=>{var posted=Store.Get(c,debt.S("journalEntryId"),"journal")!;Assert.AreEqual(DateTime.UtcNow.ToString("yyyy-MM"),posted.S("period"));Assert.AreNotEqual(debt.S("effectiveFrom"),posted.S("period"));return true;});});
    }
}
