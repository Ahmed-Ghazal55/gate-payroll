using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class GlobalSearchTests
{
    static ClaimsPrincipal User(params string[] permissions)=>new(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"synthetic"),new Claim("finance.sponsorIds","SYN-A"),new Claim("finance.categoryCodes","Advance")}.Concat(permissions.Select(p=>new Claim("permission",p))),"synthetic"));
    [TestMethod]
    public void SearchRespectsCollectionPermissionsScopeAndTheTwentyResultLimit()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-search-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            store.Write((c,tx)=>
            {
                Store.Save(c,"company",new(){["id"]="SYN-A",["nameAr"]="Synthetic Company"},tx);
                Store.Save(c,"company",new(){["id"]="SYN-B",["nameAr"]="Synthetic Hidden Company"},tx);
                Store.Save(c,"campaign",new(){["id"]="synthetic-campaign",["title"]="Synthetic Campaign"},tx);
                Store.Save(c,"rider",new(){["id"]="2000000001",["iqama"]="2000000001",["name"]="Synthetic Rider"},tx);
                foreach(string sponsor in new[]{"SYN-A","SYN-B"})Store.Save(c,"debt",new(){["id"]="debt:"+sponsor,["description"]="Synthetic Debt",["category"]="Advance",["creditorSponsorId"]=sponsor},tx);
                return true;
            });
            var user=User("riders.view","companies.view","debts.view","forms.view");
            var hits=store.Read(c=>GlobalSearch.Search(c,user,"Synthetic"));
            CollectionAssert.AreEquivalent(new[]{"rider","company","debt","campaign"},hits.Select(h=>h.Type).ToArray());
            Assert.IsFalse(hits.Any(h=>h.Id.Contains("SYN-B",StringComparison.Ordinal)));
            Assert.AreEqual(0,store.Read(c=>GlobalSearch.Search(c,User(),"Synthetic").Count));
            Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>GlobalSearch.Search(c,user,"x")));
            store.Write((c,tx)=>{for(int i=0;i<25;i++)Store.Save(c,"campaign",new(){["id"]="bulk:"+i,["title"]="Synthetic Campaign"},tx);return true;});
            Assert.AreEqual(20,store.Read(c=>GlobalSearch.Search(c,user,"Synthetic").Count));
            Assert.IsTrue(hits.All(h=>h.Url.StartsWith("/app/",StringComparison.Ordinal)));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
    }
}
