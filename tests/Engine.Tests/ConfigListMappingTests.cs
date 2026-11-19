using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Data.Sqlite;
using Payroll;

[TestClass]
public sealed class ConfigListMappingTests
{
    [TestMethod]
    public void FreshSeedUsesCorrectMappingWithoutRewritingExistingApprovedConfiguration()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-config-map-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);ConfigLists.Seed(store);
            Assert.AreEqual("112513",store.Read(c=>ConfigLists.Current(c,null,"deduction_types","HsWalletTopUp"))!["values"].S("account"));
            Assert.AreEqual("112506",store.Read(c=>ConfigLists.Current(c,null,"deduction_types","WorkPermit"))!["values"].S("account"));
            store.Write((c,tx)=>
            {
                var item=Store.Get(c,"cfg:deduction_types:HsWalletTopUp",ConfigLists.ItemKind,tx)!;
                item["current"]!["values"]!["account"]="112505";Store.Save(c,ConfigLists.ItemKind,item,tx);return true;
            });
            ConfigLists.Seed(store);
            Assert.AreEqual("112505",store.Read(c=>ConfigLists.Current(c,null,"deduction_types","HsWalletTopUp"))!["values"].S("account"));
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
    [TestMethod]
    public void LiveSeededHsWalletMovesOnceFrom112506To112513WithAudit()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-config-wallet-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);ConfigLists.Seed(store);
            // Simulate live: seeded before U-W1-29 with the old account and no v3 marker.
            store.Write((c,tx)=>
            {
                var item=Store.Get(c,"cfg:deduction_types:HsWalletTopUp",ConfigLists.ItemKind,tx)!;
                item["current"]!["values"]!["account"]="112506";Store.Save(c,ConfigLists.ItemKind,item,tx);
                SchemaSql.Exec(c,tx,"DELETE FROM records WHERE id='migration:config-lists:v3-hs-wallet-112513'");return true;
            });
            long version=store.Read(c=>(long)Store.Get(c,"cfg:deduction_types:HsWalletTopUp",ConfigLists.ItemKind,null)!.N("version"));
            ConfigLists.Seed(store);ConfigLists.Seed(store);
            var migrated=store.Read(c=>Store.Get(c,"cfg:deduction_types:HsWalletTopUp",ConfigLists.ItemKind,null))!;
            Assert.AreEqual("112513",migrated["current"]!["values"].S("account"));
            Assert.AreEqual(version+1,(long)migrated.N("version"));
            Assert.AreEqual("112506",(migrated["history"] as System.Text.Json.Nodes.JsonArray)!.Last()!["values"].S("account"));
            Assert.AreEqual(1,store.Read(c=>Store.List(c,"audit").Count(a=>a.S("action")=="config.item_migrated"&&a.S("entityId")=="cfg:deduction_types:HsWalletTopUp")));
            Assert.AreEqual("112506",store.Read(c=>ConfigLists.Current(c,null,"deduction_types","WorkPermit"))!["values"].S("account"));
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
    [TestMethod]
    public void HrServicesAndHsWalletHaveDistinctPostableAccounts()
    {
        Assert.AreEqual("112513",ConfigLists.DefaultDeductions.Single(d=>d.Category=="HsWalletTopUp").Account);
        Assert.AreEqual("112513",PipelineSettings.HsWalletAccount());
        Assert.AreEqual("112513",FinanceViews.MapAccount(new(){["accountCode"]="1210"},new(),new(){["category"]="HsWalletTopUp"},null,null));
        foreach(string category in new[]{"WorkPermit","IqamaRenewal","SponsorshipTransfer","ExitReEntryVisa","MedicalInsurance","ProfessionChange","DriverLicense","DriverCard","OtherHrService"})
        {
            Assert.AreEqual("112506",ConfigLists.DefaultDeductions.Single(d=>d.Category==category).Account);
            Assert.AreEqual("112506",FinanceViews.MapAccount(new(){["accountCode"]="1210"},new(),new(){["category"]=category},null,null));
        }
        using var c=MoneySchemaFixture.Payments();
        Assert.AreEqual(2L,SchemaSql.Scalar(c,null,"SELECT COUNT(*) FROM fin_account WHERE code IN ('112506','112513') AND is_postable=1 AND is_active=1"));
    }
}
