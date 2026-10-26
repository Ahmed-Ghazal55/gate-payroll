using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class TreasuryControlTests
{
    private static System.Security.Claims.ClaimsPrincipal Actor(string name,string role="Admin")=>UserAccess.Principal(new(){["id"]=name,["username"]=name,["role"]=role});
    private static void Run(Action<Store> action){string dir=Path.Combine(Path.GetTempPath(),"gate-custody-"+Guid.NewGuid().ToString("N"));try{action(new Store(dir));}finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}}
    private static void Legacy(Store store,decimal balance)=>store.Write((c,tx)=>
    {
        string owner=FinancialCore.ResolveSponsor("TR").Id;
        // Exact former document shape: stored before MAIN.TR became a non-posting group.
        Store.Save(c,"journal",new(){["id"]="legacy-main-tr",["transactionType"]="legacy_opening",["sourceRef"]="old-tr",["createdAt"]="2026-09-01T00:00:00Z",["lines"]=new JsonArray(
            new JsonObject{["accountCode"]="MAIN.TR",["sponsorId"]=owner,["accountType"]="Asset",["debit"]=Math.Max(0,balance),["credit"]=Math.Max(0,-balance)},
            new JsonObject{["accountCode"]="3101",["sponsorId"]=owner,["accountType"]="Equity",["debit"]=Math.Max(0,-balance),["credit"]=Math.Max(0,balance)})},tx);return true;
    });
    [TestMethod] public void LegacyCashIsClosedOnceWithoutDuplicatingNewChildrenOrRewritingOldDocument()
    {
        Run(store=>{Legacy(store,1000);string original=store.Read(c=>Store.Get(c,"legacy-main-tr","journal")!.ToJsonString());
            store.Read(c=>{Assert.ThrowsExactly<ApiError>(()=>TreasuryEntries.ValidateOpening(c,null,new JsonArray(new JsonObject{["accountCode"]="MAIN.TR.DEBT",["sponsorCode"]="TR",["verifiedBalance"]=500,["sourceRef"]="bad",["notes"]="Reviewed"})));return true;});
            var input=new JsonObject{["debtBalance"]=500,["settlementBalance"]=300,["feeBalance"]=200,["sourceRef"]="allocate",["notes"]="Verified physical count and purpose evidence"};
            var result=store.Write((c,tx)=>LegacyCashAllocation.Post(c,tx,input,Actor("finance")));Assert.AreEqual(0m,result.N("equityAdjustment"));
            store.Read(c=>{Assert.AreEqual(original,Store.Get(c,"legacy-main-tr","journal")!.ToJsonString());Assert.AreEqual(0m,AccountingPolicy.Balance(c,null,"MAIN.TR"));Assert.AreEqual(500m,AccountingPolicy.Balance(c,null,"MAIN.TR.DEBT"));Assert.AreEqual(300m,AccountingPolicy.Balance(c,null,"MAIN.TR.SET"));Assert.AreEqual(200m,AccountingPolicy.Balance(c,null,"MAIN.TR.FEE"));return true;});
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>LegacyCashAllocation.Post(c,tx,input,Actor("finance"))));
        });
    }
    [TestMethod] public void LegacyAllocationUsesFinalVerifiedBalancesAndDoesNotRepeatExistingChildMoney()
    {
        Run(store=>{Legacy(store,1000);AccountingPostingTests.SeedCash(store,"MAIN.TR.SET",500);
            var result=store.Write((c,tx)=>LegacyCashAllocation.Post(c,tx,new(){["debtBalance"]=500,["settlementBalance"]=800,["feeBalance"]=200,["sourceRef"]="existing-child",["notes"]="Current child receipts counted separately"},Actor("finance")));
            Assert.AreEqual(1500m,result.N("currentTotal"));Assert.AreEqual(0m,result.N("equityAdjustment"));Assert.AreEqual(800m,store.Read(c=>AccountingPolicy.Balance(c,null,"MAIN.TR.SET")));
        });
    }
    [TestMethod] public void NegativeLegacyCashNeedsExplicitVerifiedOpeningAndNeverCreatesAutomaticSpendableCash()
    {
        Run(store=>{Legacy(store,-600);var result=store.Write((c,tx)=>LegacyCashAllocation.Post(c,tx,new(){["debtBalance"]=0,["settlementBalance"]=0,["feeBalance"]=0,["sourceRef"]="verified-zero",["differenceAccountCode"]="3101",["notes"]="Verified zero cash; prior unreconciled opening corrected"},Actor("finance")));
            Assert.AreEqual(600m,result.N("equityAdjustment"));Assert.AreEqual(0m,store.Read(c=>AccountingPolicy.Balance(c,null,"MAIN.TR")));Assert.AreEqual(0m,store.Read(c=>AccountingPolicy.Balance(c,null,"MAIN.TR.DEBT")));
        });
    }
    [TestMethod] public void HandoverSubmissionDoesNotMoveCashAndCannotBeSubmittedTwiceWhilePending()
    {
        Run(store=>{AccountingPostingTests.SeedCash(store,"SUP.BW.SET",1200);var input=new JsonObject{["sponsorId"]="BW",["sourceRef"]="submit"};
            var h=store.Write((c,tx)=>JahezHandovers.Submit(c,tx,input,Actor("sender")));Assert.AreEqual("pending",h.S("status"));Assert.AreEqual(1200m,store.Read(c=>AccountingPolicy.Balance(c,null,"SUP.BW.SET")));Assert.AreEqual(0m,store.Read(c=>AccountingPolicy.Balance(c,null,"MAIN.BW.SET")));
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>JahezHandovers.Submit(c,tx,new(){["sponsorId"]="BW",["sourceRef"]="second"},Actor("sender"))));
        });
    }
    [TestMethod] public void ReceiverMustBeIndependentAndCountEachPurposeBeforePostingExactlyOnce()
    {
        Run(store=>{AccountingPostingTests.SeedCash(store,"SUP.BW.SET",1200);AccountingPostingTests.SeedCash(store,"SUP.BW.FEE",200);var sender=Actor("sender");var receiver=Actor("receiver","Accountant");
            var h=store.Write((c,tx)=>JahezHandovers.Submit(c,tx,new(){["sponsorId"]="BW",["sourceRef"]="receive"},sender));var counts=new JsonObject{["settlementCounted"]=1200,["feeCounted"]=200};
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>JahezHandovers.Decide(c,tx,h.S("id"),counts,sender,true)));
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>JahezHandovers.Decide(c,tx,h.S("id"),new(){["settlementCounted"]=1400,["feeCounted"]=0},receiver,true)));
            Assert.AreEqual("pending",store.Read(c=>Store.Get(c,h.S("id"),"jahez_handover"))!.S("status"));
            var received=store.Write((c,tx)=>JahezHandovers.Decide(c,tx,h.S("id"),counts,receiver,true));Assert.AreEqual("approved",received.S("status"));Assert.AreEqual(0m,store.Read(c=>AccountingPolicy.Balance(c,null,"SUP.BW.SET")));Assert.AreEqual(1200m,store.Read(c=>AccountingPolicy.Balance(c,null,"MAIN.BW.SET")));Assert.AreEqual(200m,store.Read(c=>AccountingPolicy.Balance(c,null,"MAIN.BW.FEE")));
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>JahezHandovers.Decide(c,tx,h.S("id"),counts,receiver,true)));
        });
    }
    [TestMethod] public void LateReceiptsStayWithSupervisorAndRejectionNeverPostsCash()
    {
        Run(store=>{AccountingPostingTests.SeedCash(store,"SUP.BW.SET",500);var h=store.Write((c,tx)=>JahezHandovers.Submit(c,tx,new(){["sponsorId"]="BW",["sourceRef"]="late"},Actor("sender")));
            store.Write((c,tx)=>FinancialCore.PostJournal(c,tx,"test_receipt","Late synthetic receipt","2026-10","late-receipt",new JsonArray(FinancialCore.CreateJournalLine("SUP.BW.SET",FinancialCore.Sponsors[0].Id,100,0),FinancialCore.CreateJournalLine("3101",FinancialCore.Sponsors[0].Id,0,100)),"synthetic"));
            store.Write((c,tx)=>JahezHandovers.Decide(c,tx,h.S("id"),new(){["settlementCounted"]=500,["feeCounted"]=0},Actor("receiver","FinancialAccountant"),true));Assert.AreEqual(100m,store.Read(c=>AccountingPolicy.Balance(c,null,"SUP.BW.SET")));
            var next=store.Write((c,tx)=>JahezHandovers.Submit(c,tx,new(){["sponsorId"]="BW",["sourceRef"]="reject"},Actor("sender")));store.Write((c,tx)=>JahezHandovers.Decide(c,tx,next.S("id"),new(){["note"]="Count requires correction"},Actor("receiver","FinancialAccountant"),false));Assert.AreEqual(100m,store.Read(c=>AccountingPolicy.Balance(c,null,"SUP.BW.SET")));
        });
    }
}
