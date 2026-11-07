using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class R8LookupAndJournalTests
{
    static System.Security.Claims.ClaimsPrincipal Actor(string name="synthetic-maker",string role="Admin")=>UserAccess.Principal(new JsonObject{["username"]=name,["role"]=role});
    static string Company=>FinancialCore.Sponsors[0].Id;
    // Permission-only principals (no role defaults), as the route policy and handlers see them.
    static System.Security.Claims.ClaimsPrincipal Perms(string name,params string[] permissions)=>new(new System.Security.Claims.ClaimsIdentity(
        new[]{new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name,name),new System.Security.Claims.Claim("finance.sponsorIds",FinancialCore.Sponsors[0].Id)}.Concat(permissions.Select(p=>new System.Security.Claims.Claim("permission",p))),"synthetic"));
    static JsonObject Input(string reference="SYN-R8-C7")=>new(){["companyId"]=Company,["date"]="2026-10-20",["reference"]=reference,["description"]="Synthetic",["lines"]=new JsonArray(new JsonObject{["accountCode"]="512101",["debit"]=25,["credit"]=0},new JsonObject{["accountCode"]="212103",["debit"]=0,["credit"]=25})};

    [TestMethod] public void NormaliserFoldsArabicVariantsDiacriticsTatweelAndDigits()
    {
        Assert.AreEqual("احمد محمد", TextSearch.Normalize("أَحْمَـد  مُحَمَّد"));
        Assert.AreEqual("فاطمه علي", TextSearch.Normalize("فاطمة علىٰ"));
        Assert.AreEqual("2000000001", TextSearch.Normalize("٢٠٠٠٠٠٠٠٠١"));
        Assert.IsTrue(TextSearch.Matches("احمد 2000", "أحمد السيد", "2000000001"));
        Assert.IsTrue(TextSearch.Matches("0501234567", "+966501234567"));
        Assert.IsFalse(TextSearch.Matches("خالد", "أحمد السيد"));
    }

    [TestMethod] public void LookupCoversAllRidersRanksIqamaFirstAndHidesPhoneWithoutRiderView()
    {
        var riders=new List<JsonObject>{
            new(){["id"]="2000000001",["iqama"]="2000000001",["name"]="أحمد سالم",["phone"]="0501112233",["status"]="Active",["engagementType"]="SponsoredInternal",["sponsorId"]=Company},
            new(){["id"]="2000000002",["iqama"]="2000000002",["name"]="احمد ناصر",["status"]="Terminated",["engagementType"]="External",["sponsorId"]=Company},
            new(){["id"]="2000000003",["iqama"]="2000000003",["name"]="مندوب مديونية",["status"]="Inactive",["debtOnly"]=true,["personRole"]="debt_only",["sponsorId"]=Company,["erpRiderProfileIds"]=new JsonArray("prof-3")}};
        var accounts=new List<JsonObject>{new(){["id"]="acc",["code"]="NINJA-555777",["externalAccountId"]="555777",["registeredRiderId"]="prof-3"}};
        var admin=Actor();
        var byName=RiderLookup.Search(riders,accounts,admin,"أحمد",20);
        Assert.AreEqual(2,byName.Count,"both spellings of the name, active and terminated");
        Assert.IsTrue(byName.Any(r=>r["chips"]!.AsArray().Any(c=>c!.ToString()=="منتهي")));
        Assert.AreEqual("2000000003",RiderLookup.Search(riders,accounts,admin,"555777",20).Single().S("iqama"),"platform account id finds its rider");
        Assert.IsTrue(RiderLookup.Search(riders,accounts,admin,"2000000003",20).Single()["chips"]!.AsArray().Any(c=>c!.ToString()=="مديونية فقط"));
        Assert.AreEqual("2000000002",RiderLookup.Search(riders,accounts,admin,"2000000002",20)[0].S("iqama"),"exact iqama ranks first");
        var supervisor=Perms("synthetic-sup","advances.request");
        Assert.IsTrue(RiderLookup.Allowed(supervisor));
        var bySup=RiderLookup.Search(riders,accounts,supervisor,"أحمد سالم",20);Assert.AreEqual(1,bySup.Count);Assert.IsFalse(bySup[0].ContainsKey("phone"),"phone needs riders.view");
        Assert.AreEqual(0,RiderLookup.Search(riders,accounts,supervisor,"0501112233",20).Count,"a supervisor cannot search by a phone it cannot see");
        Assert.IsFalse(RiderLookup.Allowed(Perms("synthetic-none","forms.review")));
        Assert.ThrowsExactly<ApiError>(()=>RiderLookup.Search(riders,accounts,admin,"ا",20));
    }

    [TestMethod] public void CheckerRejectsWithReasonAuthorDeletesAndActionsFollowTheRules()
    {
        using var f=new HungerStationInvoiceTests.Fixture();
        var draft=f.Store.Write((c,tx)=>ManualJournals.Create(c,tx,Actor("synthetic-maker","FinancialAccountant"),Input()));
        var checker=Actor("synthetic-checker","FinancialAccountant");
        var seen=f.Store.Read(c=>ManualJournals.WithActions(checker,draft,c,null));
        CollectionAssert.AreEquivalent(new[]{"approve","reject"},seen["allowedActions"]!.AsArray().Select(a=>a!.ToString()).ToArray());
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Reject(c,tx,checker,draft.S("id"),new(){["reason"]=""})));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Reject(c,tx,Actor("synthetic-maker","FinancialAccountant"),draft.S("id"),new(){["reason"]="own draft"})));
        var rejected=f.Store.Write((c,tx)=>ManualJournals.Reject(c,tx,checker,draft.S("id"),new(){["reason"]="wrong account"}));
        Assert.AreEqual("rejected",rejected.S("status"));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,checker,draft.S("id"))));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Delete(c,tx,Actor("synthetic-other","FinancialAccountant"),draft.S("id"))));
        f.Store.Write((c,tx)=>ManualJournals.Delete(c,tx,Actor("synthetic-maker","FinancialAccountant"),draft.S("id")));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,ManualJournals.Kind).Count));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count),"nothing was ever posted");
        var posted=f.Store.Write((c,tx)=>ManualJournals.Create(c,tx,Actor("synthetic-maker","FinancialAccountant"),Input("SYN-R8-C7-B")));
        f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,checker,posted.S("id")));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Delete(c,tx,Actor(),posted.S("id"))),"an approved journal is reversed, never deleted");
    }

    [TestMethod] public void RoutePolicyLetsCheckerRejectAndAuthorDelete()
    {
        var checker=Perms("c","finance.journal.approve");
        var maker=Perms("m","finance.journal.create");
        Assert.IsTrue(AccessPolicy.Allowed(checker,"POST","/api/finance/journals/manual/x/reject"));
        Assert.IsFalse(AccessPolicy.Allowed(maker,"POST","/api/finance/journals/manual/x/reject"));
        Assert.IsTrue(AccessPolicy.Allowed(maker,"DELETE","/api/finance/journals/manual/x"));
        Assert.IsFalse(AccessPolicy.Allowed(checker,"DELETE","/api/finance/journals/manual/x"));
        Assert.IsTrue(AccessPolicy.Allowed(checker,"GET","/api/riders/lookup"));
    }
}
