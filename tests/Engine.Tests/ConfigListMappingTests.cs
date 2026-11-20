using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Data.Sqlite;
using Payroll;
using System.Text.Json.Nodes;

[TestClass]
public sealed class ConfigListMappingTests
{
    [TestMethod]
    public void ExplicitWalletKindsSeedOnceAndHistoricAmbiguityKeepsItsAccountForReview()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-config-map-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);ConfigLists.Seed(store);
            store.Write((c,tx)=>
            {
                Store.Save(c,ConfigLists.ItemKind,new(){["id"]="cfg:deduction_types:HsWalletTopUp",["group"]="deduction_types",["code"]="HsWalletTopUp",["active"]=true,["version"]=1,["history"]=new JsonArray(),["current"]=new JsonObject{["values"]=new JsonObject{["account"]="112506"}}},tx);
                SchemaSql.Exec(c,tx,"DELETE FROM records WHERE id='migration:config-lists:v4-hs-event-kinds'");return true;
            });
            ConfigLists.Seed(store);ConfigLists.Seed(store);
            var old=store.Read(c=>Store.Get(c,"cfg:deduction_types:HsWalletTopUp",ConfigLists.ItemKind))!;
            Assert.IsFalse(old.B("active"));Assert.IsTrue(old.B("classificationRequired"));
            Assert.AreEqual("112506",old["current"]!["values"].S("account"));
            Assert.AreEqual("112501",store.Read(c=>ConfigLists.Current(c,null,"deduction_types","HsCustodyAdvance"))!["values"].S("account"));
            Assert.AreEqual("112513",store.Read(c=>ConfigLists.Current(c,null,"deduction_types","HsInvoiceCloseAdvance"))!["values"].S("account"));
            Assert.AreEqual(1,store.Read(c=>Store.List(c,"audit").Count(a=>a.S("action")=="config.wallet_classification_required")));
            Assert.ThrowsExactly<ApiError>(()=>FinancialCore.ResolveCategory("HsWalletTopUp"));
            Assert.ThrowsExactly<ApiError>(()=>FinancialCore.ResolveCategory("HsCollectedTopUp"));
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
