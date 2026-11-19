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
        var(f,input,_)=PayCycleTests.Setup();using(f){var cycle=PayCycleTests.Through(f,input);PayCycleTests.Advance(f,cycle.S("id"),7,true);PayCycleTests.Advance(f,cycle.S("id"),8,true);var mobile=Session(f,Rider(1));var statement=f.Store.Read(c=>PayStatements.Mobile(c,mobile,"2026-10"));Assert.AreEqual("pay_cycle",statement.S("source"));Assert.AreEqual("approved",statement.S("status"));Assert.AreEqual(2,statement["cards"]!.AsArray().Count);Assert.AreEqual(175m,statement.N("net"));Assert.AreEqual(175m,statement.N("closing"));Assert.AreEqual(1,statement["deductions"]!.AsArray().Count);Assert.IsTrue(statement["cards"]!.AsArray().All(card=>card!["explain"]!.AsArray().Count>0));Assert.IsFalse(statement.ToJsonString().Contains(Rider(2)));Assert.IsNull(statement["debtBefore"]);var shortLines=statement["shortLines"]!.AsArray().OfType<JsonObject>().ToList();Assert.IsTrue(shortLines.Count>0);Assert.AreEqual(statement.N("closing"),shortLines.Last().N("runningBalance"));Assert.IsTrue(statement.B("shortLinesBalanced"));Assert.AreEqual(0m,statement.N("shortLinesMismatch"));}
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
    [TestMethod]
    public void ShortStatementLinesPreserveStoredSignForCreditAdjustmentsAndRefunds()
    {
        using var f=new PayFixture();string id=Rider(9);
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"rider",new(){["id"]=id,["iqama"]=id,["status"]="Active",["sponsorId"]=FinancialCore.Sponsors[0].Id,["name"]="Synthetic Sign Rider"},tx);
            Store.Save(c,PayCycles.Kind,new()
            {
                ["id"]="syn-cycle-sign",["companyId"]=FinancialCore.Sponsors[0].Id,["period"]="2026-10",["status"]="approved",["stage"]=8,["approvedAt"]="2026-11-01T00:00:00Z",
                ["deductions"]=new JsonArray(
                    new JsonObject{["rider"]=id,["category"]="debt_installment",["amount"]=-150m,["labelAr"]="قسط مديونية"},
                    new JsonObject{["rider"]=id,["category"]="adjustment",["amount"]=75m,["labelAr"]="رد مبلغ إضافي"}
                ),
                ["riders"]=new JsonArray(new JsonObject{["rider"]=id,["net"]=925m,["carried"]=0m,["closing"]=925m,["salaryApprovedAt"]="2026-11-01T00:00:00Z"})
            },tx);
            Store.Save(c,PayCycles.LineKind,new()
            {
                ["id"]="syn-line-sign",["cycleId"]="syn-cycle-sign",["companyId"]=FinancialCore.Sponsors[0].Id,["rider"]=id,["period"]="2026-10",
                ["app"]="keeta",["platformGross"]=1200m,["incentives"]=0m,["gross"]=1000m,["companyCommission"]=200m,["entitlement"]=1000m,["vehicleCharge"]=0m,["accountDeduction"]=0m,["status"]="ready"
            },tx);
            return true;
        });
        var mobile=Session(f,id);
        var stmt=f.Store.Read(c=>PayStatements.Mobile(c,mobile,"2026-10"));
        var shortLines=stmt["shortLines"]!.AsArray().OfType<JsonObject>().ToList();
        var refundLine=shortLines.Single(l=>l.S("key").Contains("adjustment"));
        Assert.AreEqual(75m,refundLine.N("amount"));
        Assert.AreEqual("credit",refundLine.S("direction"));
        Assert.AreEqual("لك",refundLine.S("markerAr"));
        Assert.AreEqual(925m,shortLines.Last().N("runningBalance"));
        Assert.AreEqual(stmt.N("closing"),shortLines.Last().N("runningBalance"));
        Assert.IsTrue(stmt.B("shortLinesBalanced"));
    }
    [TestMethod]
    public void LegacyShortLinesKeepNegativePlatformSettlementAndBalanceGuardFlagsMismatch()
    {
        using var f=new PayFixture();string id=Rider(10);
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"rider",new(){["id"]=id,["iqama"]=id,["status"]="Active",["sponsorId"]=FinancialCore.Sponsors[0].Id,["name"]="Synthetic Neg Settlement Rider"},tx);
            Store.Save(c,"run",new()
            {
                ["id"]="syn-legacy-neg-settlement",["iqama"]=id,["period"]="2026-10",["sponsorId"]=FinancialCore.Sponsors[0].Id,["approvedAt"]="2026-11-01T00:00:00Z",
                ["result"]=new JsonObject
                {
                    ["policyName"]="Legacy Policy",["openingBalance"]=50m,["periodMovement"]=320m,["closingBalance"]=370m,
                    ["lines"]=new JsonArray(
                        new JsonObject{["category"]="platform_settlement",["platform"]="Keeta",["amount"]=500m},
                        new JsonObject{["category"]="platform_settlement",["platform"]="Keeta",["amount"]=-80m},
                        new JsonObject{["category"]="company_fee",["platform"]="Keeta",["amount"]=-60m},
                        new JsonObject{["category"]="debt_installment",["amount"]=-40m}
                    )
                }
            },tx);
            // Second period with an intentionally inconsistent stored closingBalance to verify the mismatch guard
            Store.Save(c,"run",new()
            {
                ["id"]="syn-legacy-mismatch",["iqama"]=id,["period"]="2026-09",["sponsorId"]=FinancialCore.Sponsors[0].Id,["approvedAt"]="2026-10-01T00:00:00Z",
                ["result"]=new JsonObject
                {
                    ["policyName"]="Legacy Policy",["openingBalance"]=0m,["periodMovement"]=100m,["closingBalance"]=999m,
                    ["lines"]=new JsonArray(new JsonObject{["category"]="platform_settlement",["platform"]="Keeta",["amount"]=100m})
                }
            },tx);
            return true;
        });
        var mobile=Session(f,id);
        var stmt=f.Store.Read(c=>PayStatements.Mobile(c,mobile,"2026-10"));
        var shortLines=stmt["shortLines"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.IsTrue(shortLines.Any(l=>l.S("key").Contains("platform_settlement")&&l.N("amount")==-80m&&l.S("markerAr")=="عليك"));
        Assert.AreEqual(370m,shortLines.Last().N("runningBalance"));
        Assert.AreEqual(stmt.N("closing"),shortLines.Last().N("runningBalance"));
        Assert.IsTrue(stmt.B("shortLinesBalanced"));
        Assert.AreEqual(0m,stmt.N("shortLinesMismatch"));

        var mismatchStmt=f.Store.Read(c=>PayStatements.Mobile(c,mobile,"2026-09"));
        Assert.IsFalse(mismatchStmt.B("shortLinesBalanced"));
        Assert.AreEqual(899m,mismatchStmt.N("shortLinesMismatch"));
    }
}
