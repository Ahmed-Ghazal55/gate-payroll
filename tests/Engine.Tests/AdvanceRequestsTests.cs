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

    [TestMethod]
    public void FormerSupervisorCannotReadOrRespondAndNewSupervisorCannotTakeOverAnOldRequest()
    {
        Run(s=>{
            var r=Create(s);Review(s,Acc,r.S("id"),new(){["action"]="propose",["note"]="Synthetic proposal",["proposed"]=new JsonObject{["amount"]=400}});
            var supervision=s.Read(c=>Store.List(c,OrgDirectory.Supervision).Single());
            s.Write((c,tx)=>OrgDirectory.End(c,tx,Admin,OrgDirectory.Supervision,supervision.S("id"),new(){["validTo"]=OrgDirectory.Today(DateTimeOffset.UtcNow.AddDays(-1))}));
            Assert.AreEqual(404,Assert.ThrowsExactly<ApiError>(()=>s.Read(c=>AdvanceRequests.Read(c,Sup,r.S("id")))).Status);
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>s.Write((c,tx)=>AdvanceRequests.Respond(c,tx,Sup,r.S("id"),new(){["action"]="accept"}))).Status);
            Assert.AreEqual("change_proposed",s.Read(c=>Store.Get(c,r.S("id"),AdvanceRequests.Kind)!.S("status")));
            var replacement=U("syn-outsider","advances.request");
            s.Write((c,tx)=>OrgDirectory.AddSupervision(c,tx,Admin,new(){["supervisorUserId"]="syn-outsider",["riderIqama"]=Rider}));
            Assert.AreEqual(404,Assert.ThrowsExactly<ApiError>(()=>s.Read(c=>AdvanceRequests.Read(c,replacement,r.S("id")))).Status);
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>s.Write((c,tx)=>AdvanceRequests.Respond(c,tx,replacement,r.S("id"),new(){["action"]="accept"}))).Status);
        });
    }

    [TestMethod]
    public void PersonalTasksArePrivateEvenFromCreatorAdminAndCompanyWideApprovers()
    {
        Run(s=>{
            var r=Create(s);
            var task=s.Read(c=>Store.List(c,"sys_notification").Single(n=>n.S("entityId")==r.S("id")));
            foreach(var who in new[]{Admin,Sup,U("syn-outsider","approvals.view")})
            {
                Assert.IsFalse(s.Read(c=>Notifications.Visible(c,who,task)),"only the addressee may see the task");
                Assert.IsFalse(s.Read(c=>Notifications.Center(c,who)).Any(n=>n.S("id")==task.S("id")),"legacy center must use the same personal visibility");
            }
            Assert.IsTrue(s.Read(c=>Notifications.Visible(c,Acc,task)));
            Assert.IsTrue(s.Read(c=>Notifications.Center(c,Acc)).Any(n=>n.S("id")==task.S("id")));
            s.Write((c,tx)=>{Notifications.Mark(c,tx,Acc,task);return true;});
            Assert.AreEqual("open",s.Read(c=>Store.Get(c,task.S("id"),"sys_notification")!.S("actionStatus")),"reading is not a decision");
        });
    }

    [TestMethod]
    public void ConcurrentPayoutAndReplayHaveOneDebtOneJournalAndNoOverdraftAcrossStores()
    {
        Run(s=>{
            AccountingPostingTests.SeedCash(s,"MAIN.BW.DEBT",700);
            var first=Create(s,"cash",500);Review(s,Acc,first.S("id"),new(){["action"]="approve",["coverageReason"]="Synthetic reason"});
            // Independent Store objects model separate API processes, bypassing the in-process write gate.
            var peer=new Store(s.DirectoryPath);int paid=0,blocked=0;
            Parallel.Invoke(()=>Attempt(s),()=>Attempt(peer));
            void Attempt(Store store){try{store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Cus,first.S("id"),new(){["reference"]="SYN-PARALLEL"}));Interlocked.Increment(ref paid);}catch(ApiError e)when(e.Status==409){Interlocked.Increment(ref blocked);}}
            Assert.AreEqual(1,paid);Assert.AreEqual(1,blocked);
            Assert.AreEqual(1,s.Read(c=>Store.List(c,"debt").Count(d=>d.S("sourceRef")=="ADV-REQ:"+first.S("id"))));
            string debtId=s.Read(c=>Store.Get(c,first.S("id"),AdvanceRequests.Kind)!.S("debtId"));
            Assert.AreEqual(1,s.Read(c=>Store.List(c,"journal").Count(j=>j.S("debtId")==debtId)));
            Assert.AreEqual(200m,s.Read(c=>AccountingPolicy.Balance(c,null,"MAIN.BW.DEBT")));
            var second=Create(s,"cash",300);Review(s,Acc,second.S("id"),new(){["action"]="approve",["coverageReason"]="Synthetic reason"});
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>s.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Cus,second.S("id"),new(){["reference"]="SYN-NO-CASH"}))).Status);
            Assert.AreEqual("approved",s.Read(c=>Store.Get(c,second.S("id"),AdvanceRequests.Kind)!.S("status")));
        });
    }

    [TestMethod]
    public void BodiesCannotAssignWorkflowStateDebtIdRevisionOrApprovalIdentity()
    {
        Run(s=>{
            var r=s.Write((c,tx)=>AdvanceRequests.Create(c,tx,Sup,new(){["riderIqama"]=Rider,["type"]="cash",["amount"]=100,["reason"]="Synthetic need",["status"]="paid",["revision"]=99,["debtId"]="injected",["approvedBy"]="injected",["requestedBy"]="injected"}));
            Assert.AreEqual("under_review",r.S("status"));Assert.AreEqual(1m,r.N("revision"));Assert.AreEqual("syn-supervisor",r.S("requestedBy"));Assert.AreEqual("",r.S("debtId"));Assert.AreEqual("",r.S("approvedBy"));
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Review(s,Acc,r.S("id"),new(){["action"]="approve",["revision"]=99})).Status);
            r=Review(s,Acc,r.S("id"),new(){["action"]="approve",["coverageReason"]="Synthetic reason",["debtId"]="injected",["status"]="paid",["approvedBy"]="injected"});
            Assert.AreEqual("approved",r.S("status"));Assert.AreEqual("syn-accountant",r.S("approvedBy"));Assert.AreEqual("",r.S("debtId"));
        });
    }

    [TestMethod]
    public void CompetingApprovedRequestsCannotSpendTheSameVaultBalance()
    {
        Run(s=>{
            const string other="2000000502";
            AccountingPostingTests.SeedCash(s,"MAIN.BW.DEBT",700);
            s.Write((c,tx)=>{
                Store.Save(c,"rider",new(){["id"]=other,["iqama"]=other,["name"]="Synthetic second rider",["sponsorId"]=FinancialCore.ResolveSponsor("BW").Id},tx);
                return OrgDirectory.AddSupervision(c,tx,Admin,new(){["supervisorUserId"]="syn-supervisor",["riderIqama"]=other});
            });
            var first=Create(s,"cash",500);
            var second=s.Write((c,tx)=>AdvanceRequests.Create(c,tx,Sup,new(){["riderIqama"]=other,["type"]="cash",["amount"]=500,["reason"]="Synthetic competing request"}));
            foreach(var r in new[]{first,second})Review(s,Acc,r.S("id"),new(){["action"]="approve",["coverageReason"]="Synthetic reason"});
            var peer=new Store(s.DirectoryPath);int paid=0,blocked=0;
            Parallel.Invoke(()=>Attempt(s,first),()=>Attempt(peer,second));
            void Attempt(Store store,JsonObject request){try{store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,Cus,request.S("id"),new(){["reference"]="SYN-COMPETING"}));Interlocked.Increment(ref paid);}catch(ApiError e)when(e.Status==409){Interlocked.Increment(ref blocked);}}
            Assert.AreEqual(1,paid);Assert.AreEqual(1,blocked);
            Assert.AreEqual(1,s.Read(c=>Store.List(c,AdvanceRequests.Kind).Count(r=>r.S("status")=="approved")));
            Assert.AreEqual(1,s.Read(c=>Store.List(c,"debt").Count));
            Assert.AreEqual(200m,s.Read(c=>AccountingPolicy.Balance(c,null,"MAIN.BW.DEBT")));
        });
    }

    [TestMethod]
    public void EveryOrgWriteRequiresManagePermissionBeforeProcessingClientFields()
    {
        Run(s=>{
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>s.Write((c,tx)=>OrgDirectory.AddMembership(c,tx,Sup,new()))).Status);
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>s.Write((c,tx)=>OrgDirectory.AddDelegation(c,tx,Sup,new()))).Status);
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>s.Write((c,tx)=>OrgDirectory.AddSupervision(c,tx,Sup,new()))).Status);
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>s.Write((c,tx)=>OrgDirectory.AddRouting(c,tx,Sup,new()))).Status);
            foreach(string kind in new[]{OrgDirectory.Membership,OrgDirectory.Delegation,OrgDirectory.Supervision,OrgDirectory.Routing})
                Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>s.Write((c,tx)=>OrgDirectory.End(c,tx,Sup,kind,"synthetic-id",new()))).Status);
        });
    }
    [TestMethod]
    public void OrgDatesMustBeOrderedAndSettlementCutoffIsPerRider()
    {
        Run(s=>{
            Assert.ThrowsExactly<ApiError>(()=>s.Write((c,tx)=>OrgDirectory.AddRouting(c,tx,Admin,new(){["operation"]="advance.review",["department"]="FIN",["minRank"]="B",["users"]=new JsonArray("syn-accountant"),["validFrom"]="2026-05-01",["validTo"]="2026-04-01"})));
            Assert.ThrowsExactly<ApiError>(()=>s.Write((c,tx)=>OrgDirectory.AddSupervision(c,tx,Admin,new(){["supervisorUserId"]="syn-deputy",["riderIqama"]=Rider,["validFrom"]="2026-05-01",["validTo"]="2026-04-01"})));
            var r=Create(s);
            s.Write((c,tx)=>{Store.Save(c,"run",new(){["id"]="syn-run-other",["iqama"]="2000000999",["period"]="2099-01",["approvedAt"]="2099-01-31T00:00:00Z"},tx);return true;});
            var summary=s.Read(c=>AdvanceRequests.Summary(c,null,Acc,Store.Get(c,r.S("id"),AdvanceRequests.Kind)!));
            Assert.AreEqual("",summary.S("lastPayrollSettlementAt"),"another rider's run is never this rider's cutoff");
        });
    }
}
