using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class PayAssignmentTests
{
    public static string Rider(int n=1)=>"2"+n.ToString("D9");
    static JsonObject Input(string scheme)=>new(){["period"]="2026-10",["sourceType"]="campaign",["formId"]="synthetic-pay-form",["fieldId"]="workSystem",["mapping"]=new JsonObject{["commission"]=scheme}};
    static JsonObject Row(string id,string choice,string at,string status="approved",int version=1)=>new(){["id"]=id,["iqama"]=Rider(),["campaignId"]="synthetic-pay-form",["workSystem"]=choice,["status"]=status,["reviewedAt"]=at,["reviewedBy"]="synthetic-reviewer",["version"]=version};
    static string Seed(PayFixture f)
    {
        f.Store.Write((c,tx)=>{Store.Save(c,"campaign",new(){["id"]="synthetic-pay-form"},tx);Store.Save(c,"rider",new(){["id"]=Rider(),["iqama"]=Rider(),["isDeliveryRider"]=true,["sponsorId"]=FinancialCore.Sponsors[0].Id},tx);return true;});var scheme=f.Draft("pay_scheme","keeta",PayFixture.Scheme(),"commission");f.Approve("pay_scheme","keeta",scheme);return scheme.S("id");
    }
    static void Source(PayFixture f,params JsonObject[] rows)=>f.Store.Write((c,tx)=>{foreach(var row in rows)Store.Save(c,"submission",row,tx);return true;});
    static JsonObject Preview(PayFixture f,JsonObject input)=>f.Store.Read(c=>PayAssignments.Preview(c,PayFixture.Maker,"keeta",input));
    static JsonObject Commit(PayFixture f,JsonObject input,JsonObject preview)=>f.Store.Write((c,tx)=>PayAssignments.Commit(c,tx,PayFixture.Maker,"keeta",new(){["period"]=input.S("period"),["rows"]=preview["rows"]!.DeepClone(),["fingerprint"]=preview.S("fingerprint"),["confirm"]=true}));
    [TestMethod]
    public void LatestApprovedPreMonthChoiceWinsAndTheImportPreservesTheSource()
    {
        using var f=new PayFixture();string scheme=Seed(f);var current=Row("synthetic-latest","unmapped","2026-09-30T21:00:00Z",version:3);current["history"]=new JsonArray(Row("synthetic-old","commission","2026-09-20T10:00:00Z"),Row("synthetic-latest","commission","2026-09-30T20:59:59Z",version:2));var pending=Row("synthetic-pending","unmapped","2026-09-29T12:00:00Z","pending");pending["iqama"]=Rider(2);Source(f,current,pending);string source=f.Store.Read(c=>Store.Get(c,"synthetic-latest","submission")!.ToJsonString());var input=Input(scheme);var preview=Preview(f,input);Assert.IsTrue(preview.B("canCommit"));Assert.AreEqual(1m,preview["counts"].N("lateIgnored"));Assert.AreEqual(1m,preview["counts"].N("unapprovedIgnored"));Assert.AreEqual("synthetic-latest",preview["rows"]![0]!["source"].S("responseId"));Assert.AreEqual(1m,Commit(f,input,preview).N("imported"));Assert.AreEqual(source,f.Store.Read(c=>Store.Get(c,"synthetic-latest","submission")!.ToJsonString()));Assert.AreEqual(scheme,f.Store.Read(c=>PayAssignments.Resolve(c,null,Rider(),"keeta","2026-10").S("schemeId")));
        int audits=f.Store.Read(c=>Store.List(c,"audit").Count);var repeated=Preview(f,input);Assert.AreEqual(0m,Commit(f,input,repeated).N("imported"));Assert.AreEqual(audits,f.Store.Read(c=>Store.List(c,"audit").Count));
    }
    [TestMethod]
    public void SourceChangesInvalidateTheSignatureAndOnlyAnAdministratorCanMapChoices()
    {
        using var f=new PayFixture();string scheme=Seed(f);Source(f,Row("synthetic-source","commission","2026-09-20T10:00:00Z"));var input=Input(scheme);var preview=Preview(f,input);Source(f,Row("synthetic-source","unmapped","2026-09-21T10:00:00Z"));Assert.ThrowsExactly<ApiError>(()=>Commit(f,input,preview));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,PayAssignments.Kind).Count));Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>PayAssignments.Preview(c,PayFixture.Checker,"keeta",input)));Assert.IsFalse(Preview(f,input).B("canCommit"));
    }
    [TestMethod]
    public void ANewApprovedResponseVersionAddsAChoiceRatherThanRewritingHistory()
    {
        using var f=new PayFixture();string scheme=Seed(f);var first=Row("synthetic-source","commission","2026-09-20T10:00:00Z");Source(f,first);var input=Input(scheme);Commit(f,input,Preview(f,input));string old=f.Store.Read(c=>Store.List(c,PayAssignments.Kind).Single().ToJsonString());var oldId=f.Store.Read(c=>Store.List(c,PayAssignments.Kind).Single().S("id"));Source(f,Row("synthetic-source","commission","2026-09-21T10:00:00Z",version:2));Commit(f,input,Preview(f,input));Assert.AreEqual(2,f.Store.Read(c=>Store.List(c,PayAssignments.Kind).Count));Assert.AreEqual(old,f.Store.Read(c=>Store.Get(c,oldId,PayAssignments.Kind)!.ToJsonString()));Assert.AreNotEqual(oldId,f.Store.Read(c=>PayAssignments.Resolve(c,null,Rider(),"keeta","2026-10").S("id")));
    }
    [TestMethod]
    public void LatestUnmappedOrAmbiguousChoicesNeverFallBackToOlderRows()
    {
        using var f=new PayFixture();string scheme=Seed(f);var current=Row("synthetic-new","unmapped","2026-09-21T10:00:00Z",version:2);current["history"]=new JsonArray(Row("synthetic-old","commission","2026-09-20T10:00:00Z"));Source(f,current);var preview=Preview(f,Input(scheme));Assert.IsFalse(preview.B("canCommit"));Assert.AreEqual(0,preview["rows"]!.AsArray().Count);current["history"]!.AsArray().Add(Row("synthetic-conflict","commission","2026-09-21T10:00:00Z",version:3));Source(f,current);Assert.IsFalse(Preview(f,Input(scheme)).B("canCommit"));
    }
    [TestMethod]
    public void APostMonthEditAndCurrentCompanyTransferDoNotChangeTheHistoricalChoice()
    {
        using var f=new PayFixture();string scheme=Seed(f);var oldSource=Row("synthetic-source","commission","2026-09-20T10:00:00Z");Source(f,oldSource);var input=Input(scheme);Commit(f,input,Preview(f,input));string raw=f.Store.Read(c=>Store.List(c,PayAssignments.Kind).Single().ToJsonString());
        var changed=Row("synthetic-source","unmapped","2026-10-02T10:00:00Z",version:2);changed["history"]=new JsonArray(oldSource.Clone());Source(f,changed);f.Store.Write((c,tx)=>{var rider=Store.Get(c,Rider(),"rider",tx)!.Clone();rider["sponsorId"]=FinancialCore.Sponsors[1].Id;Store.Save(c,"rider",rider,tx);return true;});var preview=Preview(f,input);Assert.IsTrue(preview.B("canCommit"));Assert.AreEqual(1m,preview["counts"].N("unchanged"));Commit(f,input,preview);Assert.AreEqual(raw,f.Store.Read(c=>Store.List(c,PayAssignments.Kind).Single().ToJsonString()));
    }
    [TestMethod]
    public void CustomFormsNeedAnExplicitActualRiderFieldWhenSeveralIdentitiesExist()
    {
        using var f=new PayFixture();string scheme=Seed(f);f.Store.Write((c,tx)=>{Store.Save(c,"custom_form",new(){["id"]="synthetic-custom",["fields"]=new JsonArray(new JsonObject{["id"]="owner",["type"]="iqama"},new JsonObject{["id"]="operator",["type"]="iqama"},new JsonObject{["id"]="scheme",["type"]="select"})},tx);Store.Save(c,"custom_response",new(){["id"]="synthetic-custom-response",["formId"]="synthetic-custom",["answers"]=new JsonObject{["owner"]=Rider(2),["operator"]=Rider(),["scheme"]="commission"},["status"]="approved",["approvedAt"]="2026-09-20T10:00:00Z",["approvedBy"]="synthetic-reviewer"},tx);return true;});
        var input=Input(scheme);input["sourceType"]="custom";input["formId"]="synthetic-custom";input["fieldId"]="scheme";Assert.ThrowsExactly<ApiError>(()=>Preview(f,input));input["riderFieldId"]="operator";var preview=Preview(f,input);Assert.IsTrue(preview.B("canCommit"));Assert.AreEqual(Rider(),preview["rows"]![0].S("rider"));Commit(f,input,preview);
    }
    [TestMethod]
    public void DraftAndForeignAppSchemesCannotBeAssigned()
    {
        using var f=new PayFixture();Seed(f);Source(f,Row("synthetic-source","commission","2026-09-20T10:00:00Z"));var draft=f.Draft("pay_scheme","keeta",PayFixture.Scheme(),"draft");Assert.ThrowsExactly<ApiError>(()=>Preview(f,Input(draft.S("id"))));var foreign=f.Draft("pay_scheme","hs",PayFixture.Scheme(),"foreign");f.Approve("pay_scheme","hs",foreign);Assert.ThrowsExactly<ApiError>(()=>Preview(f,Input(foreign.S("id"))));
    }
}
