using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SubmissionTests
{
    // Release sources may only contain the allow-listed synthetic IBAN; other banks are generated here.
    static string Synthetic(string bank,string account){int r=0;foreach(char c in bank+account+"281000")r=(r*10+c-'0')%97;return "SA"+(98-r).ToString("00")+bank+account;}
    static readonly JsonObject IbanCampaign=new(){["kind"]="iban"};
    static JsonObject IbanInput(string iban,string bank="80") => new() { ["iqama"]="2000000901",["name"]="Synthetic Rider",["phone"]="0500000901",["iban"]=iban,["bankCode"]=bank,["city"]="الرياض",["platforms"]=new JsonArray("keeta") };
    static IbanAnswer Check(JsonObject input) => IbanChoices.Validate(input,IbanCampaign);
    [TestMethod] public void IbanFormNormalizesPastedIbanAndDerivesBank()
    {
        foreach(string pasted in new[]{"IBAN: SA03 8000 0000 6080 1016 7519","\u200fSA0380000000608010167519\u200f","SA0380000000608010167519","sa03 8000 0000 6080 1016 7519","03-8000-0000-6080-1016-7519","٠٣٨٠٠٠٠٠٠٠٦٠٨٠١٠١٦٧٥١٩"})
        {
            var a=Check(IbanInput(pasted));
            Assert.AreEqual("SA0380000000608010167519",a.Iban);Assert.AreEqual("80",a.BankCode);Assert.AreEqual("+966500000901",a.Phone);Assert.AreEqual("الرياض",a.City);CollectionAssert.AreEqual(new[]{"keeta"},a.Platforms);
        }
        Assert.AreEqual("RJHI",BankPayrollFiles.BankFor("80"));Assert.AreEqual("",BankPayrollFiles.BankFor("99"));
        foreach(var (code,swift) in new[]{("78","STCJ"),("36","DBAK"),("93","VIIO"),("82","NBPA"),("84","TCZB"),("85","BNPA"),("40","NCBK")})Assert.AreEqual(swift,BankPayrollFiles.BankFor(code));
        foreach(string gone in new[]{"50","83","98"})Assert.AreEqual("",BankPayrollFiles.BankFor(gone));
        foreach(string code in IbanChoices.SalaryBanks)Assert.AreEqual(code,Check(IbanInput(Synthetic(code,"000000000000000003"),code)).BankCode);
        Assert.AreEqual(15,IbanChoices.SalaryBanks.Length);
    }
    [TestMethod] public void IbanFormRefusesBanksThatCannotReceiveSalaries()
    {
        foreach(string code in new[]{"40","50","82","84","81","99"})
        { var error=Assert.ThrowsExactly<ApiError>(()=>Check(IbanInput(Synthetic(code,"000000000000000003"))));StringAssert.Contains(error.Message,code is "40" or "50"?"اندمج":"غير مقبول"); }
    }
    [TestMethod] public void IbanFormRejectsWrongIbanBankAndApps()
    {
        foreach(string bad in new[]{"SA0080000000608010167519","SA038000000060801016751","AE070331234567890123456","SA03800000006080101675O9",""})
            Assert.ThrowsExactly<ApiError>(()=>Check(IbanInput(bad)));
        var mismatch=IbanInput("SA0380000000608010167519");mismatch["bankCode"]="10";Assert.ThrowsExactly<ApiError>(()=>Check(mismatch));
        var matching=IbanInput("SA0380000000608010167519");matching["bankCode"]="80";Assert.AreEqual("80",Check(matching).BankCode);
        foreach(var apps in new[]{new JsonArray("keeta","keeta"),new JsonArray("uber"),new JsonArray(1),new JsonArray()})
        { var input=IbanInput("SA0380000000608010167519");input["platforms"]=apps;Assert.ThrowsExactly<ApiError>(()=>Check(input)); }
        var multi=IbanInput("SA0380000000608010167519");multi["platforms"]=new JsonArray("jahez","keeta");multi["city"]="جدة";
        var answer=Check(multi);CollectionAssert.AreEqual(new[]{"keeta","jahez"},answer.Platforms);Assert.AreEqual("جدة",answer.City);
        foreach(var (key,value) in new (string,JsonNode?)[]{("city","Riyadh"),("city","Dammam"),("bankCode",""),("platforms",null)})
        { var input=IbanInput("SA0380000000608010167519");if(value==null)input.Remove(key);else input[key]=value;Assert.ThrowsExactly<ApiError>(()=>Check(input)); }
        var custom=new JsonObject{["kind"]="iban",["cities"]=IbanChoices.ValidateCities(new JsonArray(new JsonObject{["ar"]="الدمام",["en"]="Dammam"}))};
        var dammam=IbanInput("SA0380000000608010167519");dammam["city"]="الدمام";Assert.AreEqual("الدمام",IbanChoices.Validate(dammam,custom).City);Assert.ThrowsExactly<ApiError>(()=>IbanChoices.Validate(IbanInput("SA0380000000608010167519"),custom));
        foreach(var bad in new JsonNode?[]{new JsonArray(),new JsonArray(new JsonObject{["ar"]="x"}),new JsonArray(new JsonObject{["ar"]="جدة"},new JsonObject{["ar"]="جدة"}),new JsonObject()})Assert.ThrowsExactly<ApiError>(()=>IbanChoices.ValidateCities(bad));
        var extra=IbanInput("SA0380000000608010167519");extra["workSystem"]="tiers";Assert.ThrowsExactly<ApiError>(()=>Check(extra));
        var noPhone=IbanInput("SA0380000000608010167519");noPhone.Remove("phone");Assert.ThrowsExactly<ApiError>(()=>Check(noPhone));
    }
    [TestMethod] public void IbanLookupNeverExposesFullIbanOrPhone()
    {
        var d=new JsonObject { ["id"]="receipt",["iqama"]="2000000901",["name"]="Synthetic Rider",["phone"]="+966500000901",["iban"]="SA0380000000608010167519",["bankCode"]="80",["platforms"]=new JsonArray("keeta"),["version"]=2 };
        var view=IbanChoices.View(d);Assert.AreEqual("7519",view.S("ibanLast4"));Assert.AreEqual(2,(int)view.N("version"));
        Assert.IsFalse(view.ToJsonString().Contains("SA0380000000608010167519"));Assert.IsFalse(view.ToJsonString().Contains("500000901"));
        foreach(string field in new[]{"id","iban","phone","history"})Assert.IsFalse(view.ContainsKey(field));
    }
    [TestMethod] public void LegacyResponseEditsPreserveReviewAndOriginalIdentity()
    {
        var d=new JsonObject { ["id"]="test",["iqama"]="2000000888",["name"]="Original Rider",["workSystem"]="tiers",["createdAt"]="2026-10-01T00:00:00Z",["status"]="approved",["reviewNote"]="Identity checked",["reviewedBy"]="reviewer",["reviewedAt"]="2026-10-02T00:00:00Z" };
        SubmissionChoices.ArchiveAndEdit(d,new("2000000888","Changed Rider","commission"));
        Assert.AreEqual(2,SubmissionChoices.Version(d));Assert.IsTrue(d.B("isEdited"));Assert.AreEqual("pending",d.S("status"));Assert.AreEqual("2000000888",d.S("iqama"));Assert.AreEqual("2026-10-01T00:00:00Z",d.S("createdAt"));Assert.IsFalse(d.ContainsKey("reviewNote"));Assert.IsFalse(d.ContainsKey("reviewedBy"));
        var original=d["history"]![0]!.AsObject();Assert.AreEqual("approved",original.S("status"));Assert.AreEqual("Identity checked",original.S("reviewNote"));Assert.AreEqual("reviewer",original.S("reviewedBy"));Assert.AreEqual(1,SubmissionChoices.Version(original));Assert.AreEqual(d.S("createdAt"),original.S("submittedAt"));
        SubmissionChoices.ArchiveAndEdit(d,new("2000000888","Third Rider","tiers"));Assert.AreEqual(3,SubmissionChoices.Version(d));Assert.AreEqual(2,d["history"]!.AsArray().Count);Assert.AreEqual("Original Rider",original.S("name"));Assert.AreEqual("Changed Rider",d["history"]![1]!.S("name"));
    }
    [TestMethod] public void VersionGuardsRejectStaleReviewAndEdit()
    {
        var d=new JsonObject { ["version"]=2 };
        SubmissionChoices.CheckVersion(d,new JsonObject { ["version"]=2 });
        var error=Assert.ThrowsExactly<ApiError>(()=>SubmissionChoices.CheckVersion(d,new JsonObject { ["version"]=1 }));Assert.AreEqual(409,error.Status);
        SubmissionChoices.CheckVersion(new JsonObject(),new JsonObject { ["version"]=1 });
    }
    [TestMethod] public void PublicLookupExposesOnlyCurrentChoiceFields()
    {
        var d=new JsonObject { ["id"]="receipt",["iqama"]="2000000888",["name"]="Synthetic Rider",["workSystem"]="tiers",["editTokenHash"]="old-hash",["iban"]="private",["phone"]="private",["reviewNote"]="private",["history"]=new JsonArray() };
        var result=SubmissionChoices.ChoiceView(d);Assert.IsTrue(result.B("alreadySubmitted"));Assert.AreEqual("Synthetic Rider",result.S("name"));Assert.AreEqual("tiers",result.S("currentChoice"));Assert.AreEqual(1,SubmissionChoices.Version(result));Assert.AreEqual(6,result.Count);
        foreach(string field in new[]{"id","receipt","editTokenHash","iban","phone","reviewNote","history"})Assert.IsFalse(result.ContainsKey(field));
    }
    [TestMethod] public void AdministrativeProjectionNeverExposesEditCredential()
    {
        var d=new JsonObject { ["id"]="synthetic",["editTokenHash"]="secret",["history"]=new JsonArray() };
        var result=SubmissionChoices.PublicAdmin(d);Assert.IsFalse(result.ContainsKey("editTokenHash"));Assert.AreEqual("secret",d.S("editTokenHash"));Assert.IsNotNull(result["history"]);
    }
}
