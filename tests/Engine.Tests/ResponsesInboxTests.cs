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
            var store=new Store(dir);var user=new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission","forms.view")],"synthetic"));
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
}
