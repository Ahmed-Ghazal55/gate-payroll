using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Payroll;

public sealed class PayFixture:IDisposable
{
    public string DirectoryPath {get;}=Path.Combine(Path.GetTempPath(),"gate-pay-"+Guid.NewGuid().ToString("N"));
    public Store Store {get;}
    public static ClaimsPrincipal Actor(string name="synthetic-pay-maker",string role="Admin")=>UserAccess.Principal(new JsonObject{["username"]=name,["role"]=role});
    public static ClaimsPrincipal Maker=>Actor();public static ClaimsPrincipal Checker=>Actor("synthetic-pay-checker","FinancialAccountant");
    public PayFixture(){Store=new Store(DirectoryPath);}
    public JsonObject Draft(string kind,string app,JsonObject data,string code="default",string period="2026-10")=>Store.Write((c,tx)=>PaySettings.Create(c,tx,Maker,kind,app,new(){["code"]=code,["period"]=period,["data"]=data}));
    public JsonObject Approve(string kind,string app,JsonObject doc)=>Store.Write((c,tx)=>PaySettings.Approve(c,tx,Checker,kind,app,doc.S("id")));
    public static JsonObject Terms(string model="per_order")=>new(){["name"]="Synthetic parent terms",["billingModel"]=model,["parameters"]=model switch{
        "slabs"=>new JsonObject{["metric"]="orders",["bands"]=new JsonArray(new JsonObject{["min"]=0,["base"]=100,["perOrderRate"]=2})},
        "flat_target"=>new JsonObject{["targetOrders"]=100,["monthlyAmount"]=1000,["extraRate"]=2,["shortfallPerOrder"]=3,["prorate"]=true},
        _=>new JsonObject{["pricePerOrder"]=10,["companyCostAccount"]="526402"}}};
    public static JsonObject Scheme(string type="daily_commission",string rules="")
    {
        var cells=new JsonObject();foreach(string category in PaySettings.Categories)
        {
            var p=new JsonObject{["enabled"]=true,["chargeCompanyVehicle"]=false};switch(type){case "daily_commission":p["dailyCommission"]=10;break;case "monthly_commission":p["monthlyCommission"]=300;p["prorate"]=true;break;case "percent_of_gross":p["companyPercent"]=10;break;case "percent_monthly_with_vehicle":p["companyPercent"]=10;p["chargeCompanyVehicle"]=!category.EndsWith("_private",StringComparison.Ordinal);break;case "target_salary":p["bands"]=new JsonArray(new JsonObject{["min"]=0,["base"]=100,["extraRate"]=2,["fuelCap"]=50,["internet"]=10});break;}cells[category]=p;
        }
        var data=new JsonObject{["name"]="Synthetic internal scheme",["type"]=type,["accountingRole"]=type=="target_salary"?"principal":"agent",["parameters"]=new JsonObject{["categories"]=cells}};if(rules!="")data["eligibilitySetId"]=rules;return data;
    }
    public static JsonObject Eligibility()=>new(){["name"]="Synthetic own-app eligibility",["rules"]=new JsonArray(new JsonObject{["metric"]="orders",["operator"]="gte",["threshold"]=10,["effect"]=new JsonObject{["type"]="zero"}})};
    public JsonObject Global(string date="2026-10-01",JsonObject? values=null)=>Store.Write((c,tx)=>PaySettings.Create(c,tx,Maker,"pay_setting","global",values==null?new(){["effectiveFrom"]=date,["fromDefaults"]=true}:new(){["effectiveFrom"]=date,["data"]=values}));
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(DirectoryPath))Directory.Delete(DirectoryPath,true);}
}

[TestClass]
public sealed class PaySettingsTests
{
    [TestMethod]
    public void ApprovedVersionsAreImmutableAndTheNextVersionRequiresAnotherChecker()
    {
        using var f=new PayFixture();var first=f.Draft("pay_terms","hs",PayFixture.Terms());Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaySettings.Approve(c,tx,PayFixture.Maker,"pay_terms","hs",first.S("id"))));f.Approve("pay_terms","hs",first);string raw=f.Store.Read(c=>Store.Get(c,first.S("id"),"pay_terms")!.ToJsonString());
        var changed=PayFixture.Terms();changed["parameters"]!["pricePerOrder"]=12;var second=f.Draft("pay_terms","hs",changed);Assert.AreEqual(2m,second.N("version"));Assert.AreEqual("draft",second.S("status"));Assert.AreEqual(raw,f.Store.Read(c=>Store.Get(c,first.S("id"),"pay_terms")!.ToJsonString()));Assert.AreEqual(first.S("id"),f.Store.Read(c=>PaySettings.Resolve(c,null,"pay_terms","hs","2026-10").S("id")));
        f.Approve("pay_terms","hs",second);Assert.AreEqual(second.S("id"),f.Store.Read(c=>PaySettings.Resolve(c,null,"pay_terms","hs","2026-10").S("id")));int audits=f.Store.Read(c=>Store.List(c,"audit").Count);f.Approve("pay_terms","hs",second);Assert.AreEqual(audits,f.Store.Read(c=>Store.List(c,"audit").Count));Assert.AreEqual(raw,f.Store.Read(c=>Store.Get(c,first.S("id"),"pay_terms")!.ToJsonString()));
    }
    [TestMethod]
    [DataRow("daily_commission")][DataRow("percent_of_gross")][DataRow("target_salary")][DataRow("monthly_commission")][DataRow("percent_monthly_with_vehicle")]
    public void EachSchemeAcceptsItsExplicitSixCellMatrix(string type)
    {using var f=new PayFixture();var draft=f.Draft("pay_scheme","keeta",PayFixture.Scheme(type),"synthetic-scheme");Assert.AreEqual("approved",f.Approve("pay_scheme","keeta",draft).S("status"));}
    [TestMethod]
    [DataRow("per_order")][DataRow("slabs")][DataRow("flat_target")]
    public void ParentBillingModelsAreValidatedIndependentlyFromInternalSchemes(string model)
    {using var f=new PayFixture();var draft=f.Draft("pay_terms","ninja",PayFixture.Terms(model));Assert.AreEqual("approved",f.Approve("pay_terms","ninja",draft).S("status"));}
    [TestMethod]
    public void MissingRatesCellsAndCrossAppEligibilityAreRefusedBeforeAnyWrites()
    {
        using var f=new PayFixture();var broken=PayFixture.Scheme();broken["parameters"]!["categories"]!.AsObject().Remove(PaySettings.Categories[0]);Assert.ThrowsExactly<ApiError>(()=>f.Draft("pay_scheme","hs",broken,"missing-cell"));
        broken=PayFixture.Scheme();broken["parameters"]!["categories"]![PaySettings.Categories[0]]!.AsObject().Remove("dailyCommission");Assert.ThrowsExactly<ApiError>(()=>f.Draft("pay_scheme","hs",broken,"missing-rate"));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"pay_scheme").Count));
        var rules=f.Draft("pay_eligibility","hs",PayFixture.Eligibility());f.Approve("pay_eligibility","hs",rules);Assert.ThrowsExactly<ApiError>(()=>f.Draft("pay_scheme","keeta",PayFixture.Scheme(rules:rules.S("id")),"foreign-rules"));
        var foreign=PayFixture.Eligibility();foreign["rules"]![0]!["app"]="hs";Assert.ThrowsExactly<ApiError>(()=>f.Draft("pay_eligibility","keeta",foreign));
    }
    [TestMethod]
    public void MalformedOptionalParametersAreNeverSilentlyIgnored()
    {
        using var f=new PayFixture();var terms=PayFixture.Terms();terms["parameters"]!["classRates"]="invalid";Assert.ThrowsExactly<ApiError>(()=>f.Draft("pay_terms","hs",terms));var scheme=PayFixture.Scheme();scheme["parameters"]!["categories"]![PaySettings.Categories[0]]!["companyPercent"]="invalid";Assert.ThrowsExactly<ApiError>(()=>f.Draft("pay_scheme","hs",scheme,"invalid"));
    }
    [TestMethod]
    public void CopyIsDraftOnlyIdempotentAndDoesNotOverwriteApprovedTargetVersions()
    {
        using var f=new PayFixture();var rules=f.Draft("pay_eligibility","hs",PayFixture.Eligibility());f.Approve("pay_eligibility","hs",rules);var scheme=f.Draft("pay_scheme","hs",PayFixture.Scheme(rules:rules.S("id")),"synthetic");f.Approve("pay_scheme","hs",scheme);var terms=f.Draft("pay_terms","hs",PayFixture.Terms());f.Approve("pay_terms","hs",terms);
        var target=f.Draft("pay_terms","hs",PayFixture.Terms(),period:"2026-11");f.Approve("pay_terms","hs",target);string raw=f.Store.Read(c=>Store.Get(c,target.S("id"),"pay_terms")!.ToJsonString());
        var copy=f.Store.Write((c,tx)=>PaySettings.Copy(c,tx,PayFixture.Maker,"hs","2026-10","2026-11"));Assert.AreEqual(3m,copy.N("created"));Assert.IsTrue(copy["drafts"]!.AsArray().All(d=>d.S("status")=="draft"));var draftRules=copy["drafts"]!.AsArray().Single(d=>d.S("kind")=="pay_eligibility")!;var draftScheme=copy["drafts"]!.AsArray().Single(d=>d.S("kind")=="pay_scheme")!;Assert.AreEqual(draftRules.S("id"),draftScheme["data"].S("eligibilitySetId"));
        Assert.ThrowsExactly<ApiError>(()=>f.Approve("pay_scheme","hs",draftScheme.AsObject()));f.Approve("pay_eligibility","hs",draftRules.AsObject());f.Approve("pay_scheme","hs",draftScheme.AsObject());
        int before=f.Store.Read(c=>Store.List(c,"pay_terms").Count);var repeat=f.Store.Write((c,tx)=>PaySettings.Copy(c,tx,PayFixture.Maker,"hs","2026-10","2026-11"));Assert.AreEqual(before,f.Store.Read(c=>Store.List(c,"pay_terms").Count));Assert.AreEqual(raw,f.Store.Read(c=>Store.Get(c,target.S("id"),"pay_terms")!.ToJsonString()));
    }
    [TestMethod]
    public void CopyPreservesAReferencedOlderEligibilitySnapshotAsItsOwnDraft()
    {
        using var f=new PayFixture();var old=f.Draft("pay_eligibility","hs",PayFixture.Eligibility());f.Approve("pay_eligibility","hs",old);var scheme=f.Draft("pay_scheme","hs",PayFixture.Scheme(rules:old.S("id")),"synthetic");f.Approve("pay_scheme","hs",scheme);
        var newer=f.Draft("pay_eligibility","hs",PayFixture.Eligibility());f.Approve("pay_eligibility","hs",newer);var copy=f.Store.Write((c,tx)=>PaySettings.Copy(c,tx,PayFixture.Maker,"hs","2026-10","2026-11"));var linked=copy["drafts"]!.AsArray().OfType<JsonObject>().Single(d=>d.S("copiedFromId")==old.S("id"));Assert.IsTrue(linked.S("code").StartsWith("snapshot_",StringComparison.Ordinal));var copiedScheme=copy["drafts"]!.AsArray().OfType<JsonObject>().Single(d=>d.S("kind")=="pay_scheme");Assert.AreEqual(linked.S("id"),copiedScheme["data"].S("eligibilitySetId"));f.Approve("pay_eligibility","hs",linked);f.Approve("pay_scheme","hs",copiedScheme);
    }
    [TestMethod]
    public void DefaultsRequireApprovalEffectiveDatesApplyAndOldGlobalVersionsStayUnchanged()
    {
        using var f=new PayFixture();var global=f.Global();Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>PaySettings.Global(c,null,"2026-10-01")));f.Approve("pay_setting","global",global);string raw=f.Store.Read(c=>Store.Get(c,global.S("id"),"pay_setting")!.ToJsonString());
        var data=PaySettings.Defaults();data["values"]!["vehicleRent"]!["company_car"]=1900;var next=f.Global("2026-11-01",data);f.Approve("pay_setting","global",next);Assert.AreEqual(global.S("id"),f.Store.Read(c=>PaySettings.Global(c,null,"2026-10-31").S("id")));Assert.AreEqual(next.S("id"),f.Store.Read(c=>PaySettings.Global(c,null,"2026-11-01").S("id")));Assert.AreEqual(raw,f.Store.Read(c=>Store.Get(c,global.S("id"),"pay_setting")!.ToJsonString()));
    }
    [TestMethod]
    public void ExistingRoleDefaultsRefreshAndCompanyLimitedUsersCannotChangeSharedSettings()
    {
        var old=new JsonObject{["username"]="synthetic-existing",["role"]="FinancialAccountant"};string raw=old.ToJsonString();var principal=UserAccess.Principal(old);foreach(string p in new[]{"pay.view","pay.manage","pay.approve"})Assert.IsTrue(AccessPolicy.Has(principal,p));Assert.AreEqual(raw,old.ToJsonString());Assert.IsTrue(AccessPolicy.Allowed(principal,"POST","/api/pay/apps/hs/terms/synthetic/approve"));
        old["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(FinancialCore.Sponsors[0].Id),["platforms"]=new JsonArray("HungerStation")};principal=UserAccess.Principal(old);Assert.ThrowsExactly<ApiError>(()=>PaySettings.Scope(principal,"pay.manage","hs",true));Assert.ThrowsExactly<ApiError>(()=>PaySettings.Scope(principal,"pay.view","keeta"));PaySettings.Scope(principal,"pay.view","hs");
    }
    [TestMethod]
    public void FinancialRoundingPrecisionCannotDriftFromTheTwoDecimalPostingRule()
    {using var f=new PayFixture();foreach(int precision in new[]{0,1,3,6}){var data=PaySettings.Defaults();data["values"]!["roundingDigits"]=precision;Assert.ThrowsExactly<ApiError>(()=>f.Global(values:data));}Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"pay_setting").Count));}

}
