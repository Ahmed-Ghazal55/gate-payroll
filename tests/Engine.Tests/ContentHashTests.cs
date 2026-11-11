using System.Text.Json.Nodes;
using ErpImport;

[TestClass]
public class ContentHashTests
{
    [TestMethod]
    public void HashIgnoresVolatileFieldsAndIsStable()
    {
        var a=new JsonObject{["id"]="erp:city:1",["nameAr"]="أ",["contentHash"]="old",["importedAt"]="2026-01-01"};
        var b=new JsonObject{["importedAt"]="2026-02-02",["nameAr"]="أ",["id"]="erp:city:1"};
        Assert.AreEqual(ContentHash.Of(a),ContentHash.Of(b));
        var hashed=ContentHash.WithHash(a);
        string hash=hashed["contentHash"]!.GetValue<string>();
        Assert.AreEqual(64,hash.Length);
        Assert.AreEqual(hash,ContentHash.Of(b));
    }
}
