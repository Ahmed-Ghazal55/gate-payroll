using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class NextAppTests
{
    [TestMethod]
    public void FallbackServesOnlyExtensionlessRoutesAndRequiresTheExport()
    {
        string root=Path.Combine(Path.GetTempPath(),"gate-next-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"app"));
        try
        {
            Assert.AreEqual(404,((IStatusCodeHttpResult)NextApp.Fallback("forms",root)).StatusCode);
            File.WriteAllText(Path.Combine(root,"app","index.html"),"<html>synthetic</html>");
            Assert.IsInstanceOfType<IFileHttpResult>(NextApp.Fallback("forms/inbox",root));
            Assert.IsInstanceOfType<IFileHttpResult>(NextApp.Fallback(null,root));
            foreach(string asset in new[]{"missing.js","_next/chunk.css","rider.pdf","forms/title.json"})Assert.AreEqual(404,((IStatusCodeHttpResult)NextApp.Fallback(asset,root)).StatusCode);
        }
        finally{Directory.Delete(root,true);}
    }
}
