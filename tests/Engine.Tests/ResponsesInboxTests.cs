using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class ResponsesInboxTests
{
    [TestMethod]
    public void InboxHasOneShapeAndFiltersBothSourcesWithoutAddingReviewMutations()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-responses-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);var user=new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission","forms.view"),new Claim("permission",SubmissionChoices.PermAllCompanies)],"synthetic"));
            store.Write((c,tx)=>
            {
                Store.Save(c,"campaign",new(){["id"]="synthetic-campaign",["title"]="Synthetic Campaign"},tx);
                Store.Save(c,"custom_form",new(){["id"]="synthetic-form",["titleAr"]="نموذج مصطنع",["fields"]=new JsonArray(new JsonObject{["id"]="identity",["type"]="iqama"})},tx);
                Store.Save(c,"submission",new(){["id"]="synthetic-submission",["campaignId"]="synthetic-campaign",["status"]="approved",["name"]="Synthetic Rider",["iqama"]="2000000001",["createdAt"]="2026-11-01T00:00:00Z"},tx);
                Store.Save(c,"custom_response",new(){["id"]="synthetic-response",["formId"]="synthetic-form",["answers"]=new JsonObject{["identity"]="2000000002"},["createdAt"]="2026-11-02T00:00:00Z"},tx);return true;
            });
            var rows=store.Read(c=>ResponsesInbox.Rows(c,user,"","",""));Assert.AreEqual(2,rows.Count);
            CollectionAssert.AreEquivalent(rows[0].Select(p=>p.Key).ToArray(),rows[1].Select(p=>p.Key).ToArray());
            Assert.AreEqual("custom",rows[0].S("source"));Assert.AreEqual("2000000002",rows[0].S("iqama"));Assert.IsNull(rows[0]["reviewUrl"]);
            Assert.AreEqual(1,store.Read(c=>ResponsesInbox.Rows(c,user,"campaign","approved","synthetic-campaign").Count));
            Assert.AreEqual(0,store.Read(c=>ResponsesInbox.Rows(c,user,"custom","approved","").Count));
            Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>ResponsesInbox.Rows(c,user,"invalid","","")));
            Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>ResponsesInbox.Rows(c,new ClaimsPrincipal(new ClaimsIdentity([],"synthetic")),"","","")));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
    }

    [TestMethod]
    public void FormsListGivesEachCampaignAndBuilderFormItsOwnColumnsAndScopedCounts()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-responses-forms-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);var user=new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission","forms.view"),new Claim("permission",SubmissionChoices.PermAllCompanies)],"synthetic"));
            store.Write((c,tx)=>
            {
                Store.Save(c,"campaign",new(){["id"]="work",["title"]="Work",["fields"]=new JsonArray("city"),["status"]="open",["createdAt"]="2026-11-01T00:00:00Z"},tx);
                Store.Save(c,"campaign",new(){["id"]="bank",["title"]="Bank",["kind"]="iban",["status"]="open",["createdAt"]="2026-11-02T00:00:00Z"},tx);
                Store.Save(c,"custom_form",new(){["id"]="draft",["titleAr"]="مسودة",["status"]="draft",["fields"]=new JsonArray()},tx);
                Store.Save(c,"custom_form",new(){["id"]="survey",["titleAr"]="استبيان",["status"]="open",["createdAt"]="2026-11-03T00:00:00Z",["fields"]=new JsonArray(
                    new JsonObject{["id"]="identity",["type"]="iqama",["labelAr"]="الإقامة"},
                    new JsonObject{["id"]="name",["type"]="text",["labelAr"]="الاسم"},
                    new JsonObject{["id"]="city",["type"]="select",["labelAr"]="المدينة",["options"]=new JsonArray(new JsonObject{["value"]="r",["labelAr"]="الرياض"})},
                    new JsonObject{["id"]="amount",["type"]="number",["labelAr"]="المبلغ"})},tx);
                Store.Save(c,"submission",new(){["id"]="s1",["campaignId"]="work",["status"]="pending",["iqama"]="2000000001",["createdAt"]="2026-11-01T00:00:00Z"},tx);
                Store.Save(c,"submission",new(){["id"]="s2",["campaignId"]="work",["status"]="approved",["iqama"]="2000000003",["createdAt"]="2026-11-01T00:00:00Z"},tx);
                Store.Save(c,"custom_response",new(){["id"]="r1",["formId"]="survey",["answers"]=new JsonObject{["identity"]="2000000002",["city"]="r",["amount"]=5},["createdAt"]="2026-11-03T00:00:00Z"},tx);return true;
            });
            var forms=store.Read(c=>ResponsesInbox.Forms(c,user));
            CollectionAssert.AreEqual(new[]{"survey","bank","work"},forms.Select(f=>f.S("id")).ToArray(),"newest first, drafts hidden");
            string[] Cols(string id)=>forms.Single(f=>f.S("id")==id)["columns"]!.AsArray().Select(x=>x!["id"]!.ToString()).ToArray();
            CollectionAssert.AreEqual(new[]{"workSystem","city"},Cols("work"));
            CollectionAssert.AreEqual(new[]{"phone","city","iban","platforms"},Cols("bank"));
            CollectionAssert.AreEqual(new[]{"city","amount"},Cols("survey"),"iqama and name stay in the fixed columns");
            var survey=forms.Single(f=>f.S("id")=="survey");
            Assert.AreEqual("list",survey["columns"]![0]!["type"]!.ToString());
            Assert.AreEqual("الرياض",survey["columns"]![0]!["options"]![0]!["labelAr"]!.ToString());
            var work=forms.Single(f=>f.S("id")=="work");
            Assert.AreEqual(2,(int)work.N("responseCount"));Assert.AreEqual(1,(int)work.N("pendingCount"));
            Assert.AreEqual(1,(int)survey.N("responseCount"));
            Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>ResponsesInbox.Forms(c,new ClaimsPrincipal(new ClaimsIdentity([],"synthetic")))));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
    }
}
