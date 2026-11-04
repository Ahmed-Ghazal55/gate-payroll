using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class HungerStationReviewTests
{
    [TestMethod]
    public void ExistingUserDocumentsReceiveLiveRoleDefaultsWithoutRewriteAndDenialsStillWin()
    {
        string raw=" { \"id\": \"synthetic-old-user\", \"username\": \"synthetic-checker\", \"role\": \"FinancialAccountant\", \"extra\": [ 1, 2 ] } ";var user=JsonNode.Parse(raw)!.AsObject();string before=user.ToJsonString();var actor=UserAccess.Principal(user);
        foreach(string permission in new[]{"platforms.hs.view","platforms.hs.manage","platforms.hs.approve"})Assert.IsTrue(AccessPolicy.Has(actor,permission));Assert.AreEqual(before,user.ToJsonString());Assert.IsTrue(AccessPolicy.Allowed(actor,"GET","/api/platforms/hs/facts"));Assert.IsFalse(AccessPolicy.Allowed(actor,"POST","/api/platforms/hs/posting"));
        user["deniedPermissions"]=new JsonArray("platforms.hs.approve");var denied=UserAccess.Principal(user);Assert.IsFalse(AccessPolicy.Allowed(denied,"POST","/api/platforms/hs/invoices/synthetic/approve"));Assert.IsTrue(AccessPolicy.Allowed(denied,"POST","/api/platforms/hs/invoices/synthetic/resolve",new(){["mode"]="facts"}));Assert.IsFalse(AccessPolicy.Allowed(denied,"POST","/api/platforms/hs/invoices/synthetic/resolve",new(){["mode"]="dispute"}));
        var accountant=UserAccess.Principal(new(){["username"]="synthetic-maker",["role"]="Accountant"});Assert.IsTrue(AccessPolicy.Has(accountant,"platforms.hs.manage"));Assert.IsFalse(AccessPolicy.Has(accountant,"platforms.hs.approve"));
    }
    [TestMethod]
    public void TargetJournalsRemainReadableOnlyInsideBothCompanyAndPlatformScopes()
    {
        string company=FinancialCore.Sponsors[0].Id;var lines=new JsonArray(FinancialCore.CreateJournalLine("112802",company,100,0,targetChart:true),FinancialCore.CreateJournalLine("411102",company,0,100,targetChart:true));var journal=new JsonObject{["id"]="synthetic-target",["chart"]="target",["lines"]=lines};
        JsonObject User(string platform,string sponsor)=>new(){["username"]="synthetic-scoped",["role"]="FinancialAccountant",["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(sponsor),["platforms"]=new JsonArray(platform)}};
        Assert.IsNotNull(FinancialScopes.Journal(UserAccess.Principal(User("HungerStation",company)),journal));Assert.IsNull(FinancialScopes.Journal(UserAccess.Principal(User("Jahez",company)),journal));Assert.IsNull(FinancialScopes.Journal(UserAccess.Principal(User("HungerStation",FinancialCore.Sponsors[1].Id)),journal));
    }
    [TestMethod]
    public void DisputeResponseIsIndependentAuditedAndCannotRewriteSourceFactsOrChargeAnyone()
    {
        using var f=new HungerStationInvoiceTests.Fixture();var input=f.Input(HungerStationInvoiceTests.Workbook(f.Cr(),car:10));f.Commit(input,f.Preview(input));var invoice=f.Store.Read(c=>Store.List(c,"platform_invoice").Single());var dispute=f.Store.Read(c=>Store.List(c,"platform_dispute").Single());var response=new JsonObject{["mode"]="dispute",["disputeId"]=dispute.S("id"),["outcome"]="platform_credit",["platformReference"]="synthetic-reference",["reason"]="Synthetic response"};
        string raw=f.Store.Read(c=>Store.List(c,"platform_invoice_line").ToArray()[0].ToJsonString());Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>HungerStationInvoices.Resolve(c,tx,HungerStationInvoiceTests.Actor(),invoice.S("id"),response)));
        var actor=HungerStationInvoiceTests.Actor("synthetic-checker");var resolved=f.Store.Write((c,tx)=>HungerStationInvoices.Resolve(c,tx,actor,invoice.S("id"),response));Assert.AreEqual("resolved",resolved.S("status"));int audits=f.Store.Read(c=>Store.List(c,"audit").Count);f.Store.Write((c,tx)=>HungerStationInvoices.Resolve(c,tx,actor,invoice.S("id"),response));Assert.AreEqual(audits,f.Store.Read(c=>Store.List(c,"audit").Count));Assert.AreEqual(raw,f.Store.Read(c=>Store.List(c,"platform_invoice_line").ToArray()[0].ToJsonString()));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count+Store.List(c,"journal").Count));
    }
}
