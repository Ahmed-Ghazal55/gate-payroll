using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class MadaAccountingTests
{
    static string Company=>FinancialCore.Sponsors[0].Id;
    static string Rider=>HungerStationInvoiceTests.Rider;
    static ClaimsPrincipal Admin=>HungerStationInvoiceTests.Actor("synthetic-maker");
    static ClaimsPrincipal Checker=>HungerStationInvoiceTests.Actor("synthetic-checker");
    static ClaimsPrincipal Custodian=>UserAccess.Principal(new JsonObject{["username"]="synthetic-custodian",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=3,["customPermissions"]=new JsonArray("advances.payout",ApprovalsArea.DualPaymentOut,"approvals.decide")});
    static ClaimsPrincipal DualChecker=>UserAccess.Principal(new JsonObject{["username"]="synthetic-dual-checker",["role"]="Admin",["department"]="FIN",["rankLevel"]=5});
    static string Date=>DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd");
    static void Seed(Store store)
    {
        store.Write((c,tx)=>
        {
            var rider=Store.Get(c,Rider,"rider",tx)!;rider["sponsorId"]=Company;Store.Save(c,"rider",rider,tx);
            Store.Save(c,"user",new(){["id"]="synthetic-custodian",["username"]="synthetic-custodian",["role"]="FinancialAccountant",["customPermissions"]=new JsonArray("advances.payout",ApprovalsArea.DualPaymentOut,"approvals.decide")},tx);
            Store.Save(c,"user",new(){["id"]="synthetic-dual-checker",["username"]="synthetic-dual-checker",["role"]="Admin"},tx);
            OrgDirectory.AddMembership(c,tx,Admin,new(){["userId"]="synthetic-custodian",["department"]="FIN",["rank"]="C",["validFrom"]="2026-01-01"});
            foreach(string vault in new[]{"MADA","MAIN.BW.DEBT","BANK.BW"})OrgDirectory.AddRouting(c,tx,Admin,new(){["operation"]="advance.payout:"+vault,["department"]="FIN",["minRank"]="C",["users"]=new JsonArray("synthetic-custodian"),["validFrom"]="2026-01-01"});
            FinancialCore.PostJournal(c,tx,"synthetic_bank_opening","Synthetic target bank opening",Date[..7],"SYNTHETIC-BANK-OPENING",new JsonArray(PayPosting.Line("111201",Company,1000,0,"","",Date[..7],"synthetic"),PayPosting.Line("212103",Company,0,1000,"","",Date[..7],"synthetic")),"synthetic-maker",originSponsorId:Company,creditorSponsorId:Company,targetChart:true,payChart:true);return true;
        });
    }
    static void ApprovePayoutDual(Store store,string id)=>store.Write((c,tx)=>{
        var opened=AdvanceRequests.RequestPayout(c,tx,Custodian,id);
        ApprovalsArea.Decide(c,tx,DualChecker,opened.S("id"),new(){["decision"]="approve",["note"]="independent dual"});
        return true;
    });
    // K2 owns creation/review. Seed its exact approved contract to test our payout boundary independently.
    static JsonObject Request(Store store,string id="synthetic-internet",decimal amount=100,string fulfilment="recharge_card",string vault="MADA")=>store.Write((c,tx)=>
    {
        var r=new JsonObject{["id"]=id,["type"]="internet",["fulfilment"]=fulfilment,["vaultCode"]=vault,["networkCode"]="synthetic-network",["packageCode"]="synthetic-package",["companyId"]=Company,["riderIqama"]=Rider,["riderName"]="Synthetic Rider",["amount"]=amount,["monthlyInstallment"]=50,["requestedBy"]="synthetic-supervisor",["approvedBy"]="synthetic-independent-reviewer",["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["status"]="approved",["decisions"]=new JsonArray()};Store.Save(c,AdvanceRequests.Kind,r,tx);return r;
    });
    /// <summary>Seed Mada wallet funds via a synthetic target journal (old adjustment routes are gone).</summary>
    static JsonObject Topup(Store store,decimal amount=250)=>store.Write((c,tx)=>
    {
        string reference="SYNTHETIC-MADA-TOPUP:"+Guid.NewGuid().ToString("N");
        var lines=new JsonArray(PayPosting.Line("112404",Company,amount,0,"","",Date[..7],reference),PayPosting.Line("111201",Company,0,amount,"","",Date[..7],reference));
        foreach(var line in lines.OfType<JsonObject>())line["structuredFinance"]=true;
        lines[1]!["legacyVaultCode"]="BANK."+FinancialCore.ResolveSponsor(Company).Code;
        return FinancialCore.PostJournal(c,tx,"mada_topup","Synthetic Mada top-up",Date[..7],reference,lines,"synthetic-maker",originSponsorId:Company,creditorSponsorId:Company,targetChart:true,payChart:true,journalDate:Date);
    });
    [TestMethod]
    public void AdjustmentRoutesAreGoneWithArabicMessage()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);
        var draftErr=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>MadaAccounting.Draft(c,tx,Admin,new(){["type"]="mada_topup"})));
        Assert.AreEqual(410,draftErr.Status);Assert.AreEqual(MadaAccounting.AdjustmentsGone,draftErr.Message);
        var approveErr=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>MadaAccounting.Approve(c,tx,Checker,"finance-adjustment:gone")));
        Assert.AreEqual(410,approveErr.Status);
    }
    [TestMethod]
    public void MadaTopupAndRechargeUseRealTargetAccountsAndSeparateCompanyBalance()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);Assert.AreEqual(0m,f.Store.Read(c=>MadaAccounting.Balance(c,null,Company,"112404")));
        Topup(f.Store,250);
        Assert.AreEqual(250m,f.Store.Read(c=>MadaAccounting.Balance(c,null,Company,"112404")));Assert.AreEqual(750m,f.Store.Read(c=>MadaAccounting.Balance(c,null,Company,"111201")));Assert.AreEqual(0m,f.Store.Read(c=>MadaAccounting.Balance(c,null,FinancialCore.Sponsors[1].Id,"112404")));
        // RWP7a3: legacy recharge_card payout is gone (410); cards use /payments/mada/cards/issue.
        // RWD1b: after refusal, machine balance / debts / request status stay unchanged.
        var request=Request(f.Store);ApprovePayoutDual(f.Store,request.S("id"));
        var gone=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Custodian,request.S("id"),new(){["reference"]="SYNTHETIC-CARD-RECEIPT"})));
        Assert.AreEqual(410,gone.Status);Assert.AreEqual(250m,f.Store.Read(c=>MadaAccounting.Balance(c,null,Company,"112404")));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));
        Assert.AreEqual("approved",f.Store.Read(c=>Store.Get(c,request.S("id"),AdvanceRequests.Kind))!.S("status"));
    }
    [TestMethod]
    public void InsufficientMachineFundsLeaveApprovedRequestAndEveryBalanceUnchanged()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);Topup(f.Store,50);var request=Request(f.Store,amount:100);ApprovePayoutDual(f.Store,request.S("id"));int journals=f.Store.Read(c=>Store.List(c,"journal").Count);
        var gone=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Custodian,request.S("id"),new(){["reference"]="SYNTHETIC-OVERDRAFT"})));
        Assert.AreEqual(410,gone.Status);
        Assert.AreEqual("approved",f.Store.Read(c=>Store.Get(c,request.S("id"),AdvanceRequests.Kind))!.S("status"));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));Assert.AreEqual(journals,f.Store.Read(c=>Store.List(c,"journal").Count));Assert.AreEqual(50m,f.Store.Read(c=>MadaAccounting.Balance(c,null,Company,"112404")));
    }
    [TestMethod]
    public void InternetCashAndBankAlwaysDebitInternetReceivableAndDoNotSpendMachineBalance()
    {
        foreach(string method in new[]{"cash","bank"})
        {
            using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);if(method=="cash")f.Store.Write((c,tx)=>FinancialCore.PostJournal(c,tx,"synthetic_cash_opening","Synthetic",Date[..7],"SYNTHETIC-CASH",new JsonArray(FinancialCore.CreateJournalLine("MAIN.BW.DEBT",Company,200,0),FinancialCore.CreateJournalLine("3101",Company,0,200)),"synthetic-maker"));
            var request=Request(f.Store,fulfilment:method,vault:method=="cash"?"MAIN.BW.DEBT":"BANK.BW");ApprovePayoutDual(f.Store,request.S("id"));
            // RWD1b H2: PayOut blocked; cash/bank vaults and machine balance unchanged.
            var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Custodian,request.S("id"),new(){["reference"]="SYNTHETIC-INTERNET-RECEIPT",["bankAccountCode"]="111201"})));
            Assert.AreEqual(409,err.Status);Assert.IsTrue(err.Message.Contains("غير متاح",StringComparison.Ordinal));
            Assert.AreEqual(0m,f.Store.Read(c=>MadaAccounting.Balance(c,null,Company,"112404")));
            if(method=="cash")Assert.AreEqual(200m,f.Store.Read(c=>AccountingPolicy.Balance(c,null,"MAIN.BW.DEBT")));
        }
    }
    [TestMethod]
    public void ConcurrentMachinePayoutsCannotSpendTheSameBalanceAcrossStores()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);Topup(f.Store,150);var one=Request(f.Store,"synthetic-one");var two=Request(f.Store,"synthetic-two");ApprovePayoutDual(f.Store,one.S("id"));ApprovePayoutDual(f.Store,two.S("id"));var other=new Store(f.Store.DirectoryPath);
        // RWP7a3 + RWD1b: legacy card PayOut refuses (410) even across concurrent stores; balance/debts unchanged.
        bool Pay(Store store,string id){try{store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Custodian,id,new(){["reference"]="SYNTHETIC-"+id}));return true;}catch(ApiError e)when(e.Status is 409 or 410){return false;}}
        var tasks=new[]{Task.Run(()=>Pay(f.Store,one.S("id"))),Task.Run(()=>Pay(other,two.S("id")))};Task.WaitAll(tasks);
        Assert.AreEqual(0,tasks.Count(t=>t.Result));Assert.AreEqual(150m,f.Store.Read(c=>MadaAccounting.Balance(c,null,Company,"112404")));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));
    }
    [TestMethod]
    public void MissingAssignmentWrongCompanyAndClosedPeriodRefusePayoutAndTopup()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);Topup(f.Store);var request=Request(f.Store);Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Checker,request.S("id"),new(){["reference"]="SYNTHETIC-NOT-ASSIGNED"})));
        var otherCompanyCustodian=UserAccess.Principal(new JsonObject{["username"]="synthetic-custodian",["role"]="FinancialAccountant",["customPermissions"]=new JsonArray("advances.payout",ApprovalsArea.DualPaymentOut),["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(FinancialCore.Sponsors[1].Id),["accountCodes"]=new JsonArray(),["categoryCodes"]=new JsonArray(),["platforms"]=new JsonArray()}});
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,otherCompanyCustodian,request.S("id"),new(){["reference"]="SYNTHETIC-WRONG-COMPANY"})));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>AdvanceRequests.Read(c,otherCompanyCustodian,request.S("id"))));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>AdvanceRequests.Summary(c,null,otherCompanyCustodian,request)));
        ApprovePayoutDual(f.Store,request.S("id"));
        f.Store.Write((c,tx)=>{Store.Save(c,"accounting_period",new(){["id"]="synthetic-closed-period",["companyId"]=Company,["period"]=Date[..7],["status"]="closed"},tx);return true;});Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Custodian,request.S("id"),new(){["reference"]="SYNTHETIC-CLOSED"})));
        var gone=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>MadaAccounting.Draft(c,tx,Admin,new(){["type"]="salary_increase"})));Assert.AreEqual(410,gone.Status);
    }
}
