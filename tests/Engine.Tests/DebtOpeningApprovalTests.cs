using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class DebtOpeningApprovalTests
{
    private static string Bw => FinancialCore.Sponsors[0].Id;
    private static void InStore(Action<Store> action){string path=Path.Combine(Path.GetTempPath(),"gate-opening-"+Guid.NewGuid().ToString("N"));try{action(new Store(path));}finally{SqliteConnection.ClearAllPools();if(Directory.Exists(path))Directory.Delete(path,true);}}
    private static JsonObject Opening(string id,decimal amount=250m)=>new(){["id"]=id,["sourceRef"]=id,["iqama"]="2000000009",["riderName"]="Synthetic Rider",["debtorType"]="Rider",["category"]="CashAdvance",["entryMode"]="OpeningBalance",["allocationMode"]="RiderFull",["receivableAccountCode"]="1210",["creditorSponsorId"]=Bw,["originSponsorId"]=Bw,["totalAmount"]=amount,["totalCostAmount"]=amount,["remainingBalance"]=amount,["paidAmount"]=0,["waivedAmount"]=0,["monthlyInstallment"]=0,["status"]="pending_approval",["effectiveFrom"]="2026-10",["balanceAsOf"]="2026-10-31",["createdBy"]="debt-sheet-import"};
    private static void Seed(Store store,params JsonObject[] debts)=>store.Write((c,tx)=>{foreach(var d in debts)Store.Save(c,"debt",d,tx);return true;});

    [TestMethod] public void ApprovalActivatesPostsOneJournalInBalanceMonthAndIsIdempotent()
    {
        InStore(store=>{
            Seed(store,Opening("DEBT-SHEET:2:CashAdvance"),Opening("DEBT-SHEET:3:CashAdvance",100m));
            var r=store.Write((c,tx)=>DebtOpeningApproval.Decide(c,tx,["DEBT-SHEET:2:CashAdvance","DEBT-SHEET:3:CashAdvance"],"approved","",  "reviewer"));
            Assert.AreEqual(2,(int)r["processed"]!);Assert.AreEqual(350m,(decimal)r["total"]!);
            store.Read(c=>{
                var d=Store.Get(c,"DEBT-SHEET:2:CashAdvance","debt")!;Assert.AreEqual("active",d.S("status"));Assert.AreEqual("2026-10",d.S("postingPeriod"));
                var journals=Store.List(c,"journal");Assert.AreEqual(2,journals.Count);Assert.IsTrue(journals.All(j=>j.S("period")=="2026-10"));
                Assert.AreEqual(250m,AccountingEvents.DebtBalance(c,null,d,Bw));return true;});
            var again=store.Write((c,tx)=>DebtOpeningApproval.Decide(c,tx,["DEBT-SHEET:2:CashAdvance"],"approved","","reviewer"));
            Assert.AreEqual(0,(int)again["processed"]!);Assert.AreEqual(1,(int)again["skipped"]!);
            Assert.AreEqual(2,store.Read(c=>Store.List(c,"journal").Count));
        });
    }

    [TestMethod] public void RejectionNeedsReasonCancelsWithoutJournalAndLoaderCannotApprove()
    {
        InStore(store=>{
            Seed(store,Opening("DEBT-SHEET:4:CashAdvance"));
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>DebtOpeningApproval.Decide(c,tx,["DEBT-SHEET:4:CashAdvance"],"rejected","","reviewer")));
            Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>DebtOpeningApproval.Decide(c,tx,["DEBT-SHEET:4:CashAdvance"],"approved","","debt-sheet-import")));
            store.Write((c,tx)=>DebtOpeningApproval.Decide(c,tx,["DEBT-SHEET:4:CashAdvance"],"rejected","duplicate in sheet","reviewer"));
            store.Read(c=>{Assert.AreEqual("cancelled",Store.Get(c,"DEBT-SHEET:4:CashAdvance","debt")!.S("status"));Assert.AreEqual(0,Store.List(c,"journal").Count);return true;});
        });
    }

    [TestMethod] public void BatchSizeIsBounded()
    {
        InStore(store=>Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>DebtOpeningApproval.Decide(c,tx,Enumerable.Range(0,101).Select(i=>"x"+i).ToArray(),"approved","","reviewer"))));
    }
}
