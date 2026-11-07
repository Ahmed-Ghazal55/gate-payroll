using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class ManualJournalTests
{
    static System.Security.Claims.ClaimsPrincipal Actor(string name="synthetic-maker",string role="Admin")=>UserAccess.Principal(new JsonObject{["username"]=name,["role"]=role});
    static string Company=>FinancialCore.Sponsors[0].Id;
    static JsonObject Input(string reference="SYNTHETIC-MANUAL",string date="2026-10-20")=>new(){["companyId"]=Company,["date"]=date,["reference"]=reference,["description"]="Synthetic accounting operation",["lines"]=new JsonArray(new JsonObject{["accountCode"]="512101",["debit"]=25,["credit"]=0},new JsonObject{["accountCode"]="212103",["debit"]=0,["credit"]=25})};
    [TestMethod]
    public void DraftHasNoLedgerEffectAndApprovalPostsExactlyOnceWithActualDocumentDate()
    {
        using var f=new HungerStationInvoiceTests.Fixture();var draft=f.Store.Write((c,tx)=>ManualJournals.Create(c,tx,Actor(),Input()));Assert.AreEqual("draft",draft.S("status"));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,Actor("SYNTHETIC-MAKER"),draft.S("id"))));
        var approved=f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,Actor("synthetic-checker","FinancialAccountant"),draft.S("id")));
        var journal=f.Store.Read(c=>Store.Get(c,approved.S("journalEntryId"),"journal"))!;Assert.AreEqual("2026-10-20",journal.S("date"));Assert.AreEqual(25m,journal.N("totalDebit"));Assert.AreEqual(journal.N("totalDebit"),journal.N("totalCredit"));Assert.AreEqual("target",journal.S("chart"));
        f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,Actor("synthetic-checker","FinancialAccountant"),draft.S("id")));Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"journal").Count));
    }
    [TestMethod]
    public void UnbalancedInactiveSummaryAndClosedPeriodFailWithoutPersistence()
    {
        using var f=new HungerStationInvoiceTests.Fixture();var bad=Input();bad["lines"]![1]!["credit"]=24;Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Create(c,tx,Actor(),bad)));
        foreach(string code in new[]{"5","nonexistent",FinanceViews.Accounts.First(a=>!a.B("posting")).S("code")}){var input=Input();input["lines"]![0]!["accountCode"]=code;Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Create(c,tx,Actor(),input)));}
        f.Store.Write((c,tx)=>{Store.Save(c,"accounting_period",new(){["id"]="synthetic-closed",["companyId"]=Company,["period"]="2026-10",["status"]="closed"},tx);return true;});Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Create(c,tx,Actor(),Input())));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,ManualJournals.Kind).Count));
    }
    [TestMethod]
    public void ScopeIsRecheckedAtApprovalAndFinancePermissionsDoNotGrantOtherCompanies()
    {
        using var f=new HungerStationInvoiceTests.Fixture();var draft=f.Store.Write((c,tx)=>ManualJournals.Create(c,tx,Actor(),Input()));
        var restricted=UserAccess.Principal(new JsonObject{["username"]="synthetic-checker",["role"]="FinancialAccountant",["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(FinancialCore.Sponsors[1].Id),["accountCodes"]=new JsonArray(),["platforms"]=new JsonArray("Ninja"),["categoryCodes"]=new JsonArray()}});
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,restricted,draft.S("id"))));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Create(c,tx,restricted,Input())));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count));
        var reviewer=Actor("synthetic-reviewer","Reviewer");Assert.IsFalse(AccessPolicy.Allowed(reviewer,"POST","/api/finance/journals/manual",Input()));
    }
    [TestMethod]
    public void OpenPeriodReversalRequiresNewIndependentApprovalAndNeverChangesOriginalJournal()
    {
        using var f=new HungerStationInvoiceTests.Fixture();var draft=f.Store.Write((c,tx)=>ManualJournals.Create(c,tx,Actor(),Input()));var approved=f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,Actor("synthetic-checker"),draft.S("id")));string original=approved.S("journalEntryId"),before=f.Store.Read(c=>Store.Get(c,original,"journal"))!.ToJsonString();
        f.Store.Write((c,tx)=>{Store.Save(c,"accounting_period",new(){["id"]="synthetic-closed",["companyId"]=Company,["period"]="2026-10",["status"]="closed"},tx);return true;});
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Reverse(c,tx,Actor(),draft.S("id"),new(){["reason"]="Synthetic correction",["date"]="2026-10-21"})));
        var reversal=f.Store.Write((c,tx)=>ManualJournals.Reverse(c,tx,Actor(),draft.S("id"),new(){["reason"]="Synthetic correction",["date"]="2026-11-01"}));Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"journal").Count));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,Actor(),reversal.S("id"))));f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,Actor("synthetic-checker"),reversal.S("id")));
        Assert.AreEqual(before,f.Store.Read(c=>Store.Get(c,original,"journal"))!.ToJsonString());Assert.AreEqual(2,f.Store.Read(c=>Store.List(c,"journal").Count));
        Assert.AreEqual(0m,f.Store.Read(c=>Store.List(c,"journal").SelectMany(j=>j["lines"]!.AsArray().OfType<JsonObject>()).Where(l=>l.S("accountCode")=="512101").Sum(l=>l.N("debit")-l.N("credit"))));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Reverse(c,tx,Actor(),original,new(){["reason"]="Synthetic forbidden system-id path",["date"]="2026-11-01"})));
    }
    [TestMethod]
    public void ExistingDatabasePermissionSeedHonorsExplicitDenialsAndLaterRevocation()
    {
        using var f=new HungerStationInvoiceTests.Fixture();f.Store.Write((c,tx)=>{Store.Save(c,"user",new(){["id"]="synthetic-old-finance",["username"]="synthetic-old-finance",["role"]="FinancialManager",["deniedPermissions"]=new JsonArray(ManualJournals.ApprovePermission)},tx);return true;});ManualJournals.Seed(f.Store);
        var user=f.Store.Read(c=>Store.Get(c,"synthetic-old-finance","user"))!;Assert.IsTrue(UserAccess.EffectivePermissions(user).Contains(ManualJournals.CreatePermission));Assert.IsFalse(UserAccess.EffectivePermissions(user).Contains(ManualJournals.ApprovePermission));
        f.Store.Write((c,tx)=>{user["deniedPermissions"]!.AsArray().Add(ManualJournals.CreatePermission);Store.Save(c,"user",user,tx);return true;});ManualJournals.Seed(f.Store);Assert.IsFalse(UserAccess.EffectivePermissions(f.Store.Read(c=>Store.Get(c,"synthetic-old-finance","user"))!).Contains(ManualJournals.CreatePermission));
    }
    [TestMethod]
    public void AttachmentIsPrivateBoundedAndSavedAtomicallyWithoutExposingItsContentInDraft()
    {
        using var f=new HungerStationInvoiceTests.Fixture();var input=Input();input["attachment"]=new JsonObject{["fileName"]="synthetic.pdf",["dataBase64"]=Convert.ToBase64String("%PDF-Synthetic"u8.ToArray())};var draft=f.Store.Write((c,tx)=>ManualJournals.Create(c,tx,Actor(),input));Assert.IsFalse(draft.ToJsonString().Contains("dataBase64"));Assert.IsNotNull(f.Store.Read(c=>Store.Get(c,draft.S("attachmentId"),"financial_attachment")));
        var bad=Input("SYNTHETIC-BAD-ATTACHMENT");bad["attachment"]=new JsonObject{["fileName"]="synthetic.html",["dataBase64"]=Convert.ToBase64String("<html>synthetic</html>"u8.ToArray())};Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Create(c,tx,Actor(),bad)));Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,ManualJournals.Kind).Count));
    }
}
