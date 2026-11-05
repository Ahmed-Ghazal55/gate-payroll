using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

// BR §25: synthetic users and rider only.
[TestClass]
public sealed class AdvanceRequestsTests
{
    const string Rider="2000000501";
    static ClaimsPrincipal U(string name,params string[] perms)=>UserAccess.Principal(new(){["id"]=name,["username"]=name,["role"]="Reviewer",["customPermissions"]=new JsonArray(perms.Select(p=>(JsonNode?)JsonValue.Create(p)).ToArray())});
    static readonly ClaimsPrincipal Admin=UserAccess.Principal(new(){["id"]="syn-admin",["username"]="syn-admin",["role"]="Admin"});
    static ClaimsPrincipal Sup=>U("syn-supervisor","advances.request");
    static ClaimsPrincipal Acc=>U("syn-accountant","advances.review");
    static ClaimsPrincipal Cus=>U("syn-custodian","advances.payout");
    static ClaimsPrincipal Outsider=>U("syn-outsider","advances.review","advances.payout");
    static void Run(Action<Store> body)
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-adv-"+Guid.NewGuid().ToString("N"));
        try{var store=new Store(dir);Seed(store);body(store);}finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
    static void Seed(Store store)
    {
        string bw=FinancialCore.ResolveSponsor("BW").Id;
        store.Write((c,tx)=>
        {
            foreach(var u in new[]{"syn-admin","syn-supervisor","syn-accountant","syn-custodian","syn-outsider","syn-deputy"})Store.Save(c,"user",new(){["id"]=u,["username"]=u,["role"]="Reviewer"},tx);
            Store.Save(c,"rider",new(){["id"]=Rider,["iqama"]=Rider,["name"]="Synthetic Rider",["sponsorId"]=bw},tx);
            OrgDirectory.AddMembership(c,tx,Admin,new(){["userId"]="syn-accountant",["department"]="FIN",["rank"]="A",["validFrom"]="2026-01-01"});
            OrgDirectory.AddMembership(c,tx,Admin,new(){["userId"]="syn-custodian",["department"]="FIN",["rank"]="C",["validFrom"]="2026-01-01"});
            OrgDirectory.AddRouting(c,tx,Admin,new(){["operation"]="advance.review",["department"]="FIN",["minRank"]="B",["users"]=new JsonArray("syn-accountant"),["validFrom"]="2026-01-01"});
            foreach(var v in new[]{"MAIN.BW.DEBT","BANK.BW"})OrgDirectory.AddRouting(c,tx,Admin,new(){["operation"]="advance.payout:"+v,["department"]="FIN",["minRank"]="C",["users"]=new JsonArray("syn-custodian"),["validFrom"]="2026-01-01"});
            OrgDirectory.AddSupervision(c,tx,Admin,new(){["supervisorUserId"]="syn-supervisor",["riderIqama"]=Rider,["validFrom"]="2026-01-01"});
            return true;
        });
    }
    static JsonObject Create(Store s,string type="cash",decimal amount=500)=>s.Write((c,tx)=>AdvanceRequests.Create(c,tx,Sup,new(){["riderIqama"]=Rider,["type"]=type,["amount"]=amount,["reason"]="Synthetic need"}));
    static JsonObject Review(Store s,ClaimsPrincipal who,string id,JsonObject input)=>s.Write((c,tx)=>AdvanceRequests.ReviewRequest(c,tx,who,id,input));

    [TestMethod]
    public void FullCycleWithProposalCreatesDebtAndJournalFromTheChosenVault()
    {
        Run(s=>{
            AccountingPostingTests.SeedCash(s,"BANK.BW",10000);
            var r=Create(s);Assert.AreEqual("under_review",r.S("status"));Assert.AreEqual("MAIN.BW.DEBT",r.S("vaultCode"));
            Assert.IsTrue(s.Read(c=>Notifications.Inbox(c,Acc)).Any(n=>n.S("entityId")==r.S("id")&&n.B("requiresAction")));
            Assert.IsFalse(s.Read(c=>Notifications.Inbox(c,Outsider)).Any(n=>n.S("entityId")==r.S("id")),"a personal task is invisible to others");
            var p=Review(s,Acc,r.S("id"),new(){["action"]="propose",["note"]="Cash box is low; bank is available",["proposed"]=new JsonObject{["type"]="bank"}});
            Assert.AreEqual("change_proposed",p.S("status"));Assert.AreEqual("BANK.BW",p["proposal"]!.S("vaultCode"));
            var accepted=s.Write((c,tx)=>AdvanceRequests.Respond(c,tx,Sup,r.S("id"),new(){["action"]="accept"}));Assert.AreEqual("under_review",accepted.S("status"));Assert.AreEqual(2m,accepted.N("revision"));Assert.AreEqual("bank",accepted.S("type"));
            Assert.ThrowsExactly<ApiError>(()=>Review(s,Acc,r.S("id"),new(){["action"]="approve",["monthlyInstallment"]=250}),"no forecast => coverage reason required");
            var approved=Review(s,Acc,r.S("id"),new(){["action"]="approve",["monthlyInstallment"]=250,["coverageReason"]="Synthetic: no statement yet, supervisor confirmed workload"});
            Assert.AreEqual("approved",approved.S("status"));
            Assert.AreEqual("decided",s.Read(c=>Store.List(c,"sys_notification").Where(n=>n.S("entityId")==r.S("id")&&n.S("assignedTo")=="syn-accountant").Select(n=>n.S("actionStatus")).Last()));
            var paid=s.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Cus,r.S("id"),new(){["reference"]="SYN-TRF-1"}));
            Assert.AreEqual("paid",paid.S("status"));
            var debt=s.Read(c=>Store.Get(c,paid.S("debtId"),"debt"))!;Assert.AreEqual("BankAdvance",debt.S("category"));Assert.AreEqual(500m,debt.N("remainingBalance"));Assert.AreEqual(250m,debt.N("monthlyInstallment"));Assert.AreEqual("active",debt.S("status"));
            Assert.AreEqual(9500m,s.Read(c=>AccountingPolicy.Balance(c,null,"BANK.BW")));
            CollectionAssert.AreEqual(new[]{"submitted","proposed","accepted_proposal","approved","paid"},paid["decisions"]!.AsArray().OfType<JsonObject>().Select(d=>d.S("action")).ToArray());
        });
    }
    [TestMethod]
    public void PermissionWithoutAssignmentCannotDecideAndRequesterCannotApprove()
    {
        Run(s=>{
            var r=Create(s);
            var e=Assert.ThrowsExactly<ApiError>(()=>Review(s,Outsider,r.S("id"),new(){["action"]="reject",["note"]="not mine"}));StringAssert.Contains(e.Message,"الصلاحية وحدها");
            var both=U("syn-supervisor","advances.request","advances.review");
            Assert.ThrowsExactly<ApiError>(()=>Review(s,both,r.S("id"),new(){["action"]="reject",["note"]="self"}));
            Assert.ThrowsExactly<ApiError>(()=>Create(s),"one open request per rider");
        });
    }
    [TestMethod]
    public void SupervisorOnlyForOwnRidersAndPayoutRefusesInsufficientBalanceKeepingApproval()
    {
        Run(s=>{
            var other=U("syn-outsider","advances.request");
            Assert.ThrowsExactly<ApiError>(()=>s.Write((c,tx)=>AdvanceRequests.Create(c,tx,other,new(){["riderIqama"]=Rider,["type"]="cash",["amount"]=100,["reason"]="Synthetic"})));
            var r=Create(s,"cash",300);Review(s,Acc,r.S("id"),new(){["action"]="approve",["coverageReason"]="Synthetic reason accepted"});
            var e=Assert.ThrowsExactly<ApiError>(()=>s.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Cus,r.S("id"),new(){["reference"]="SYN-CASH-1"})));Assert.AreEqual(409,e.Status);
            Assert.AreEqual("approved",s.Read(c=>Store.Get(c,r.S("id"),AdvanceRequests.Kind)!.S("status")));Assert.AreEqual(0,s.Read(c=>Store.List(c,"debt").Count));
            Assert.ThrowsExactly<ApiError>(()=>s.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Outsider,r.S("id"),new(){["reference"]="SYN-CASH-2"})),"payout only by the vault custodian");
        });
    }
    [TestMethod]
    public void DelegationExpiresAndDecisionSnapshotSurvivesRankChange()
    {
        Run(s=>{
            var deputy=U("syn-deputy","advances.review");
            s.Write((c,tx)=>OrgDirectory.AddDelegation(c,tx,Admin,new(){["fromUser"]="syn-accountant",["toUser"]="syn-deputy",["operation"]="advance.review",["validFrom"]="2026-01-01",["until"]="2026-01-31"}));
            var r=Create(s);
            Assert.ThrowsExactly<ApiError>(()=>Review(s,deputy,r.S("id"),new(){["action"]="reject",["note"]="expired delegation"}));
            s.Write((c,tx)=>OrgDirectory.AddDelegation(c,tx,Admin,new(){["fromUser"]="syn-accountant",["toUser"]="syn-deputy",["operation"]="advance.review",["validFrom"]="2026-01-01",["until"]="2099-12-31"}));
            var rejected=Review(s,deputy,r.S("id"),new(){["action"]="reject",["note"]="Synthetic rejection"});
            var snap=rejected["decisions"]!.AsArray().OfType<JsonObject>().Last()["snapshot"]!.AsObject();Assert.AreEqual("delegation",snap.S("via"));Assert.AreEqual("A",snap.S("rank"));
            var membership=s.Read(c=>Store.List(c,OrgDirectory.Membership).Single(m=>m.S("userId")=="syn-accountant"));
            s.Write((c,tx)=>OrgDirectory.End(c,tx,Admin,OrgDirectory.Membership,membership.S("id"),new(){["validTo"]=OrgDirectory.Today()}));
            Assert.AreEqual("A",s.Read(c=>Store.Get(c,r.S("id"),AdvanceRequests.Kind)!["decisions"]!.AsArray().OfType<JsonObject>().Last()["snapshot"]!.S("rank")));
            Assert.AreEqual(1,s.Read(c=>Store.List(c,OrgDirectory.Membership).Count(m=>m.S("userId")=="syn-accountant")),"ending never deletes history");
        });
    }
}
