using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class PayStatementTests
{
    static string Rider(int n)=>"2"+n.ToString("D9");
    static DefaultHttpContext Session(PayFixture f,string rider,string expiry="")
    {
        string token="rmob_"+Convert.ToHexString(RandomNumberGenerator.GetBytes(32));f.Store.Write((c,tx)=>{Store.Save(c,"mobile_session",new(){["id"]="mobile:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),["iqama"]=rider,["expiresAtUtc"]=expiry==""?DateTimeOffset.UtcNow.AddHours(1).ToString("O"):expiry},tx);return true;});var ctx=new DefaultHttpContext();ctx.Request.Headers.Authorization="Bearer "+token;return ctx;
    }
    [TestMethod]
    public void ApprovedMonthlyStatementShowsOwnTwoAppsExplainLinesDeductionsAndSnapshotNet()
    {
        var(f,input,_)=PayCycleTests.Setup();using(f){var cycle=PayCycleTests.Through(f,input);PayCycleTests.Advance(f,cycle.S("id"),7,true);PayCycleTests.Advance(f,cycle.S("id"),8,true);var mobile=Session(f,Rider(1));var statement=f.Store.Read(c=>PayStatements.Mobile(c,mobile,"2026-10"));Assert.AreEqual("pay_cycle",statement.S("source"));Assert.AreEqual("approved",statement.S("status"));Assert.AreEqual(2,statement["cards"]!.AsArray().Count);Assert.AreEqual(175m,statement.N("net"));Assert.AreEqual(175m,statement.N("closing"));Assert.AreEqual(1,statement["deductions"]!.AsArray().Count);Assert.IsTrue(statement["cards"]!.AsArray().All(card=>card!["explain"]!.AsArray().Count>0));Assert.IsFalse(statement.ToJsonString().Contains(Rider(2)));Assert.IsNull(statement["debtBefore"]);}
    }
    [TestMethod]
    public void MobileCannotSeeDraftsWhileAdministratorCanReviewThemAndScopeIsEnforced()
    {var(f,input,_)=PayCycleTests.Setup(false);using(f){var cycle=PayCycleTests.Through(f,input);var mobile=Session(f,Rider(1));Assert.AreEqual("none",f.Store.Read(c=>PayStatements.Mobile(c,mobile,"2026-10")).S("source"));var admin=f.Store.Read(c=>PayStatements.Build(c,Rider(1),"2026-10",PayFixture.Maker));Assert.AreEqual("preview",admin.S("status"));Assert.AreEqual(2,admin["cards"]!.AsArray().Count);var user=UserAccess.Principal(new JsonObject{["username"]="synthetic-statement-scope",["role"]="FinancialAccountant",["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(FinancialCore.Sponsors[0].Id),["platforms"]=new JsonArray("HungerStation"),["accountCodes"]=new JsonArray(),["categoryCodes"]=new JsonArray()}});Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>PayStatements.Build(c,Rider(1),"2026-10",user)));}}
    [TestMethod]
    public void LegacyFallbackUsesOnlyApprovedOwnRunAndLeavesItsBodyUnchanged()
    {
        using var f=new PayFixture();string id=Rider(1);f.Store.Write((c,tx)=>{Store.Save(c,"rider",new(){["id"]=id,["status"]="Active",["sponsorId"]=FinancialCore.Sponsors[0].Id,["name"]="Synthetic legacy rider",["iban"]="synthetic-bank-TAIL"},tx);Store.Save(c,"run",new(){["id"]="synthetic-legacy-month",["iqama"]=id,["period"]="2026-10",["sponsorId"]=FinancialCore.Sponsors[0].Id,["approvedAt"]="2026-11-01",["result"]=new JsonObject{["policyName"]="Synthetic approved legacy",["openingBalance"]=20,["periodMovement"]=85,["closingBalance"]=105,["lines"]=new JsonArray(new JsonObject{["category"]="platform_settlement",["platform"]="Keeta",["amount"]=100},new JsonObject{["category"]="company_fee",["platform"]="Keeta",["amount"]=-10},new JsonObject{["category"]="already_paid",["amount"]=-5})}},tx);Store.Save(c,"run",new(){["id"]="synthetic-other-month",["iqama"]=Rider(2),["period"]="2026-10",["approvedAt"]="2026-11-01",["result"]=new JsonObject{["periodMovement"]=999}},tx);return true;});string before=f.Store.Read(c=>Store.Get(c,"synthetic-legacy-month","run")!.ToJsonString());var mobile=Session(f,id);var statement=f.Store.Read(c=>PayStatements.Mobile(c,mobile,"2026-10"));Assert.AreEqual("legacy_run",statement.S("source"));Assert.AreEqual(85m,statement.N("net"));Assert.AreEqual(20m,statement.N("carried"));Assert.AreEqual(105m,statement.N("closing"));Assert.AreEqual("••••TAIL",statement["header"].S("ibanMasked"));Assert.IsFalse(statement.ToJsonString().Contains("synthetic-bank-TAIL"));Assert.IsFalse(statement.ToJsonString().Contains(Rider(2)));Assert.AreEqual(before,f.Store.Read(c=>Store.Get(c,"synthetic-legacy-month","run")!.ToJsonString()));
    }
    [TestMethod]
    public void TokenIdentityCannotBeSwappedAndExpiredOrInactiveSessionsAreRefused()
    {
        using var f=new PayFixture();f.Store.Write((c,tx)=>{Store.Save(c,"rider",new(){["id"]=Rider(1),["status"]="Active"},tx);return true;});var valid=Session(f,Rider(1));valid.Request.QueryString=new("?period=2026-10&iqama="+Rider(2));Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>PayStatements.Mobile(c,valid,"2026-10")));var expired=Session(f,Rider(1),DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));Assert.AreEqual(401,Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>PayStatements.Mobile(c,expired,"2026-10"))).Status);Assert.AreEqual(401,Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>PayStatements.Mobile(c,new DefaultHttpContext(),"2026-10"))).Status);var active=Session(f,Rider(1));f.Store.Write((c,tx)=>{var person=Store.Get(c,Rider(1),"rider",tx)!;person["status"]="Suspended";Store.Save(c,"rider",person,tx);return true;});Assert.AreEqual(401,Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>PayStatements.Mobile(c,active,"2026-10"))).Status);
    }
}
