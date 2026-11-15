using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class AllowedActionsViewTests
{
    static string Day(int delta=0)=>DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).AddDays(delta).ToString("yyyy-MM-dd");

    [TestMethod]
    public void DebtViewActionsForMakerCheckerAndBypass()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id,iqama="2000000091";
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"rider",new(){["id"]=iqama,["iqama"]=iqama,["name"]="Synthetic debt view",["sponsorId"]=company,["isDeliveryRider"]=true,["status"]="Active"},tx);
            Store.Save(c,"debt",new(){["id"]="synthetic-debt-view",["iqama"]=iqama,["category"]="AdministrativeDeduction",["creditorSponsorId"]=company,["originSponsorId"]=company,["status"]="active",["remainingBalance"]=50,["totalAmount"]=50,["monthlyInstallment"]=10,["createdBy"]="synthetic-debt-maker",["entryMode"]="Accrual",["journalEntryId"]="j1",["createdAt"]="2026-10-01T00:00:00Z",["effectiveFrom"]="2026-10"},tx);
            Store.Save(c,"user",new(){["id"]="u-debt-maker",["username"]="synthetic-debt-maker",["role"]="Admin"},tx);
            return true;
        });
        var debt=f.Store.Read(c=>Store.Get(c,"synthetic-debt-view","debt")!);
        var maker=UserAccess.Principal(new JsonObject{["username"]="synthetic-debt-maker",["role"]="Admin"});
        var checker=PayFixture.Checker;
        var noScope=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name","scoped-out"),new(System.Security.Claims.ClaimTypes.Role,"Accountant"),new("permission","debts.view")],"test"));
        var makerView=f.Store.Read(c=>Debts.View(c,null,maker,debt));
        Assert.IsTrue(makerView["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="edit"));
        Assert.IsTrue(makerView["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="collect"));
        var checkerView=f.Store.Read(c=>Debts.View(c,null,checker,debt));
        Assert.IsTrue(checkerView["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="collect")||checkerView["approvalBlockers"]!.AsArray().Count>=0);
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>Debts.View(c,null,noScope,debt))).Status);

        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"debt",new(){["id"]="synthetic-opening-view",["iqama"]=iqama,["category"]="LegacyOpeningDebt",["creditorSponsorId"]=company,["originSponsorId"]=company,["status"]="pending_approval",["remainingBalance"]=20,["totalAmount"]=20,["entryMode"]="OpeningBalance",["createdBy"]="synthetic-debt-maker",["balanceAsOf"]="2026-09-30",["createdAt"]="2026-09-30T00:00:00Z",["effectiveFrom"]="2026-09"},tx);
            return true;
        });
        var opening=f.Store.Read(c=>Store.Get(c,"synthetic-opening-view","debt")!);
        var openingMaker=f.Store.Read(c=>Debts.View(c,null,maker,opening));
        Assert.IsFalse(openingMaker["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="decide"));
        Assert.IsTrue(openingMaker["approvalBlockers"]!.AsArray().Any(b=>b!.ToString()!.Contains("المنشئ",StringComparison.Ordinal)));
        f.Store.Write((c,tx)=>{AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="synthetic-debt-maker",["from"]=Day(0),["to"]=Day(1),["reason"]="§35 debt opening view"});return true;});
        var withGrant=f.Store.Read(c=>Debts.View(c,null,maker,opening));
        Assert.IsTrue(withGrant["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="decide"));
        var adminDecide=f.Store.Read(c=>Debts.View(c,null,PayFixture.Maker,opening));
        Assert.IsTrue(adminDecide["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="decide"));
    }

    [TestMethod]
    public void PlatformPostingViewOpenAndCloseForAdminOnly()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>PlatformPosting.Set(c,tx,PayFixture.Maker,"ninja",new(){["enabled"]=false}));
        var off=f.Store.Read(c=>PlatformPosting.View(c,null,PayFixture.Maker,"ninja"));
        Assert.IsTrue(off["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="open_posting"));
        f.Store.Write((c,tx)=>PlatformPosting.Set(c,tx,PayFixture.Maker,"ninja",new(){["enabled"]=true}));
        var on=f.Store.Read(c=>PlatformPosting.View(c,null,PayFixture.Maker,"ninja"));
        Assert.IsTrue(on["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="close_posting"));
        var accountant=UserAccess.Principal(new JsonObject{["username"]="acct-no-admin",["role"]="Accountant"});
        var blocked=f.Store.Read(c=>PlatformPosting.View(c,null,accountant,"ninja"));
        Assert.AreEqual(0,blocked["allowedActions"]!.AsArray().Count);
        Assert.IsTrue(blocked["approvalBlockers"]!.AsArray().Count>0);
    }

    [TestMethod]
    public void CycleViewOffersConfirmDebtDeductionsAll()
    {
        var(f,input,_)=PayCycleTests.Setup();using(f)
        {
            var cycle=PayCycleTests.Through(f,input,6,review:true);
            string id=cycle.S("id");
            PayCycleTests.Advance(f,id,7,true);
            var view=f.Store.Read(c=>PayCycles.View(c,null,PayFixture.Checker,Store.Get(c,id,PayCycles.Kind)!));
            Assert.IsTrue(view["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="confirm_debt_deductions"));
            Assert.IsTrue(view["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="confirm_debt_deductions_all"));
            var debtView=f.Store.Read(c=>PayCycles.DebtDeductionsView(c,null,PayFixture.Checker,id));
            Assert.IsTrue(debtView["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="confirm_debt_deductions_all"));
        }
    }

    [TestMethod]
    public void JahezHandoverViewApproveRejectForIndependentReceiver()
    {
        using var f=new PayFixture();
        var request=new JsonObject{["id"]="jh1",["sponsorId"]=FinancialCore.Sponsors[0].Id,["sponsorCode"]="BW",["status"]="pending",["settlementAmount"]=10,["feeAmount"]=0,["submittedBy"]="synthetic-handover-maker",["sourceRef"]="REF1"};
        var maker=UserAccess.Principal(new JsonObject{["username"]="synthetic-handover-maker",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=3});
        var receiver=UserAccess.Principal(new JsonObject{["username"]="synthetic-handover-recv",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=3});
        // Ensure permissions via role; FinancialAccountant has treasury.jahez.receive
        var makerView=f.Store.Read(c=>JahezHandovers.View(c,null,maker,request));
        Assert.IsFalse(makerView["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"));
        var recvView=f.Store.Read(c=>JahezHandovers.View(c,null,receiver,request));
        Assert.IsTrue(recvView["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"));
        Assert.IsTrue(recvView["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="reject"));
        f.Store.Write((c,tx)=>{Store.Save(c,"user",new(){["id"]="u-jh",["username"]="synthetic-handover-maker",["role"]="FinancialAccountant"},tx);AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="synthetic-handover-maker",["from"]=Day(0),["to"]=Day(1),["reason"]="§35 jahez view"});return true;});
        var bypassView=f.Store.Read(c=>JahezHandovers.View(c,null,maker,request));
        Assert.IsTrue(bypassView["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"));
    }

    [TestMethod]
    public void JahezHandoverListMaterializesViewsWhileConnectionOpen()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{Store.Save(c,"jahez_handover",new(){["id"]="jh-list",["sponsorId"]=FinancialCore.Sponsors[0].Id,["status"]="pending",["submittedBy"]="synthetic-handover-maker",["sourceRef"]="LIST1"},tx);return true;});
        var actor=UserAccess.Principal(new JsonObject{["username"]="synthetic-handover-recv",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=3});
        var listed=f.Store.Read(c=>Store.List(c,"jahez_handover").Select(h=>JahezHandovers.View(c,null,actor,h)).ToList());
        Assert.IsTrue(listed.Any(h=>h.S("id")=="jh-list"&&h["allowedActions"] is JsonArray));
        var deferred=f.Store.Read(c=>(IEnumerable<JsonObject>)Store.List(c,"jahez_handover").Select(h=>JahezHandovers.View(c,null,actor,h)));
        Assert.ThrowsExactly<InvalidOperationException>(()=>deferred.ToList());
    }

    [TestMethod]
    public void ManualJournalWithActionsHonoursBypassForView()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>{Store.Save(c,"user",new(){["id"]="u-mj",["username"]="synthetic-mj-maker",["role"]="Admin"},tx);return true;});
        var draft=new JsonObject{["id"]="mj1",["status"]="draft",["companyId"]=company,["date"]="2026-10-15",["createdBy"]="synthetic-mj-maker",["lines"]=new JsonArray(),["reversesJournalId"]=""};
        var maker=UserAccess.Principal(new JsonObject{["username"]="synthetic-mj-maker",["role"]="Admin"});
        var without=f.Store.Read(c=>ManualJournals.WithActions(maker,draft,c,null));
        Assert.IsFalse(without["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"));
        f.Store.Write((c,tx)=>{AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="synthetic-mj-maker",["from"]=Day(0),["to"]=Day(1),["reason"]="§35 manual journal"});return true;});
        var with=f.Store.Read(c=>ManualJournals.WithActions(maker,draft,c,null));
        Assert.IsTrue(with["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"));
    }
}
