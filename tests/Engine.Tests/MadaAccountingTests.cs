using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class MadaAccountingTests
{
    static string Company=>FinancialCore.Sponsors[0].Id;
    static string Rider=>HungerStationInvoiceTests.Rider;
    static ClaimsPrincipal Admin=>HungerStationInvoiceTests.Actor("synthetic-maker");
    static ClaimsPrincipal Checker=>HungerStationInvoiceTests.Actor("synthetic-checker");
    static ClaimsPrincipal Custodian=>UserAccess.Principal(new JsonObject{["username"]="synthetic-custodian",["role"]="FinancialAccountant",["customPermissions"]=new JsonArray("advances.payout")});
    static string Date=>DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd");
    static JsonObject Adjustment(string type,decimal amount=100)=>new(){["type"]=type,["companyId"]=Company,["date"]=Date,["reference"]="SYN-"+type,["description"]="Synthetic adjustment",["amount"]=amount,["riderIqama"]=Rider,["bankAccountCode"]="111201"};
    static void Seed(Store store)
    {
        store.Write((c,tx)=>
        {
            var rider=Store.Get(c,Rider,"rider",tx)!;rider["sponsorId"]=Company;Store.Save(c,"rider",rider,tx);
            Store.Save(c,"user",new(){["id"]="synthetic-custodian",["username"]="synthetic-custodian",["role"]="FinancialAccountant"},tx);
            OrgDirectory.AddMembership(c,tx,Admin,new(){["userId"]="synthetic-custodian",["department"]="FIN",["rank"]="C",["validFrom"]="2026-01-01"});
            foreach(string vault in new[]{"MADA","MAIN.BW.DEBT","BANK.BW"})OrgDirectory.AddRouting(c,tx,Admin,new(){["operation"]="advance.payout:"+vault,["department"]="FIN",["minRank"]="C",["users"]=new JsonArray("synthetic-custodian"),["validFrom"]="2026-01-01"});
            FinancialCore.PostJournal(c,tx,"synthetic_bank_opening","Synthetic target bank opening",Date[..7],"SYNTHETIC-BANK-OPENING",new JsonArray(PayPosting.Line("111201",Company,1000,0,"","",Date[..7],"synthetic"),PayPosting.Line("212103",Company,0,1000,"","",Date[..7],"synthetic")),"synthetic-maker",originSponsorId:Company,creditorSponsorId:Company,targetChart:true,payChart:true);return true;
        });
    }
    // K2 owns creation/review. Seed its exact approved contract to test our payout boundary independently.
    static JsonObject Request(Store store,string id="synthetic-internet",decimal amount=100,string fulfilment="recharge_card",string vault="MADA")=>store.Write((c,tx)=>
    {
        var r=new JsonObject{["id"]=id,["type"]="internet",["fulfilment"]=fulfilment,["vaultCode"]=vault,["networkCode"]="synthetic-network",["packageCode"]="synthetic-package",["companyId"]=Company,["riderIqama"]=Rider,["riderName"]="Synthetic Rider",["amount"]=amount,["monthlyInstallment"]=50,["requestedBy"]="synthetic-supervisor",["approvedBy"]="synthetic-independent-reviewer",["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["status"]="approved",["decisions"]=new JsonArray()};Store.Save(c,AdvanceRequests.Kind,r,tx);return r;
    });
    static JsonObject Topup(Store store,decimal amount=250)
    {var d=store.Write((c,tx)=>MadaAccounting.Draft(c,tx,Admin,Adjustment("mada_topup",amount)));return store.Write((c,tx)=>MadaAccounting.Approve(c,tx,Checker,d.S("id")));}
    [TestMethod]
    public void MadaTopupAndRechargeUseRealTargetAccountsAndSeparateCompanyBalance()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);var draft=f.Store.Write((c,tx)=>MadaAccounting.Draft(c,tx,Admin,Adjustment("mada_topup",250)));Assert.AreEqual(0m,f.Store.Read(c=>MadaAccounting.Balance(c,null,Company,"112404")));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>MadaAccounting.Approve(c,tx,Admin,draft.S("id"))));f.Store.Write((c,tx)=>MadaAccounting.Approve(c,tx,Checker,draft.S("id")));
        Assert.AreEqual(250m,f.Store.Read(c=>MadaAccounting.Balance(c,null,Company,"112404")));Assert.AreEqual(750m,f.Store.Read(c=>MadaAccounting.Balance(c,null,Company,"111201")));Assert.AreEqual(0m,f.Store.Read(c=>MadaAccounting.Balance(c,null,FinancialCore.Sponsors[1].Id,"112404")));
        var request=Request(f.Store);var paid=f.Store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Custodian,request.S("id"),new(){["reference"]="SYNTHETIC-CARD-RECEIPT"}));Assert.AreEqual("paid",paid.S("status"));Assert.IsTrue(paid.S("payoutVoucherNo").StartsWith("PV-"));
        var debt=f.Store.Read(c=>Store.Get(c,paid.S("debtId"),"debt"))!;Assert.AreEqual("InternetAdvance",debt.S("category"));Assert.AreEqual("112503",PayPosting.DebtAccount(debt));Assert.AreEqual(100m,debt.N("remainingBalance"));Assert.AreEqual(150m,f.Store.Read(c=>MadaAccounting.Balance(c,null,Company,"112404")));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Custodian,request.S("id"),new(){["reference"]="SYNTHETIC-REPLAY"})));
    }
    [TestMethod]
    public void InsufficientMachineFundsLeaveApprovedRequestAndEveryBalanceUnchanged()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);Topup(f.Store,50);var request=Request(f.Store,amount:100);int journals=f.Store.Read(c=>Store.List(c,"journal").Count);
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Custodian,request.S("id"),new(){["reference"]="SYNTHETIC-OVERDRAFT"})));
        Assert.AreEqual("approved",f.Store.Read(c=>Store.Get(c,request.S("id"),AdvanceRequests.Kind))!.S("status"));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));Assert.AreEqual(journals,f.Store.Read(c=>Store.List(c,"journal").Count));Assert.AreEqual(50m,f.Store.Read(c=>MadaAccounting.Balance(c,null,Company,"112404")));
    }
    [TestMethod]
    public void InternetCashAndBankAlwaysDebitInternetReceivableAndDoNotSpendMachineBalance()
    {
        foreach(string method in new[]{"cash","bank"})
        {
            using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);if(method=="cash")f.Store.Write((c,tx)=>FinancialCore.PostJournal(c,tx,"synthetic_cash_opening","Synthetic",Date[..7],"SYNTHETIC-CASH",new JsonArray(FinancialCore.CreateJournalLine("MAIN.BW.DEBT",Company,200,0),FinancialCore.CreateJournalLine("3101",Company,0,200)),"synthetic-maker"));
            var request=Request(f.Store,fulfilment:method,vault:method=="cash"?"MAIN.BW.DEBT":"BANK.BW");var paid=f.Store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Custodian,request.S("id"),new(){["reference"]="SYNTHETIC-INTERNET-RECEIPT",["bankAccountCode"]="111201"}));var journal=f.Store.Read(c=>Store.Get(c,paid.S("journalEntryId"),"journal"))!;Assert.AreEqual("112503",journal["lines"]![0]!.S("accountCode"));Assert.AreEqual(method=="cash"?"111101":"111201",journal["lines"]![1]!.S("accountCode"));Assert.AreEqual(0m,f.Store.Read(c=>MadaAccounting.Balance(c,null,Company,"112404")));
            if(method=="cash")Assert.AreEqual(100m,f.Store.Read(c=>AccountingPolicy.Balance(c,null,"MAIN.BW.DEBT")));
        }
    }
    [TestMethod]
    public void SalaryIncreaseDecreaseAndAdministrativeChargeUseApprovedCounterparts()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);
        foreach(var pair in new[]{("salary_increase","512122","212103"),("salary_decrease","212103","512101"),("administrative_charge","112505","421302")})
        {
            var draft=f.Store.Write((c,tx)=>MadaAccounting.Draft(c,tx,Admin,Adjustment(pair.Item1)));var posted=f.Store.Write((c,tx)=>MadaAccounting.Approve(c,tx,Checker,draft.S("id")));var journal=f.Store.Read(c=>Store.Get(c,posted.S("journalEntryId"),"journal"))!;Assert.AreEqual(pair.Item2,journal["lines"]![0]!.S("accountCode"));Assert.AreEqual(pair.Item3,journal["lines"]![1]!.S("accountCode"));Assert.AreEqual(100m,journal.N("totalDebit"));Assert.AreEqual(100m,journal.N("totalCredit"));
        }
    }
    [TestMethod]
    public void ConcurrentMachinePayoutsCannotSpendTheSameBalanceAcrossStores()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);Topup(f.Store,150);var one=Request(f.Store,"synthetic-one");var two=Request(f.Store,"synthetic-two");var other=new Store(f.Store.DirectoryPath);
        bool Pay(Store store,string id){try{store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Custodian,id,new(){["reference"]="SYNTHETIC-"+id}));return true;}catch(ApiError e)when(e.Status==409){return false;}}
        var tasks=new[]{Task.Run(()=>Pay(f.Store,one.S("id"))),Task.Run(()=>Pay(other,two.S("id")))};Task.WaitAll(tasks);Assert.AreEqual(1,tasks.Count(t=>t.Result));Assert.AreEqual(50m,f.Store.Read(c=>MadaAccounting.Balance(c,null,Company,"112404")));Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"debt").Count));
    }
    [TestMethod]
    public void MissingAssignmentWrongCompanyAndClosedPeriodRefusePayoutAndTopup()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);Topup(f.Store);var request=Request(f.Store);Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Checker,request.S("id"),new(){["reference"]="SYNTHETIC-NOT-ASSIGNED"})));
        var otherCompanyCustodian=UserAccess.Principal(new JsonObject{["username"]="synthetic-custodian",["role"]="FinancialAccountant",["customPermissions"]=new JsonArray("advances.payout"),["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(FinancialCore.Sponsors[1].Id),["accountCodes"]=new JsonArray(),["categoryCodes"]=new JsonArray(),["platforms"]=new JsonArray()}});
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,otherCompanyCustodian,request.S("id"),new(){["reference"]="SYNTHETIC-WRONG-COMPANY"})));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>AdvanceRequests.Read(c,otherCompanyCustodian,request.S("id"))));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>AdvanceRequests.Summary(c,null,otherCompanyCustodian,request)));
        f.Store.Write((c,tx)=>{Store.Save(c,"accounting_period",new(){["id"]="synthetic-closed-period",["companyId"]=Company,["period"]=Date[..7],["status"]="closed"},tx);return true;});Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Custodian,request.S("id"),new(){["reference"]="SYNTHETIC-CLOSED"})));Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>MadaAccounting.Draft(c,tx,Admin,Adjustment("salary_increase"))));
    }
}
