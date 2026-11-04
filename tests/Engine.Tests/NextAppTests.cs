using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class NextAppTests
{
    [TestMethod]
    public void FallbackServesDeepLinksAnd404InOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), "gate-next-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "app", "forms"));
        try
        {
            string home = Path.Combine(root, "app", "index.html");
            string forms = Path.Combine(root, "app", "forms", "index.html");
            string notFound = Path.Combine(root, "app", "404.html");

            File.WriteAllText(home, "<html>home</html>");
            File.WriteAllText(forms, "<html>forms</html>");
            File.WriteAllText(notFound, "<html>not found</html>");

            // 1. Root /app serves app/index.html
            var rootRes = NextApp.Fallback(null, root);
            Assert.IsInstanceOfType<PhysicalFileHttpResult>(rootRes);
            Assert.AreEqual(home, ((PhysicalFileHttpResult)rootRes).FileName);

            // 2. Deep link /app/forms/ serves app/forms/index.html
            var formsRes = NextApp.Fallback("forms", root);
            Assert.IsInstanceOfType<PhysicalFileHttpResult>(formsRes);
            Assert.AreEqual(forms, ((PhysicalFileHttpResult)formsRes).FileName);

            var formsTrailingRes = NextApp.Fallback("forms/", root);
            Assert.IsInstanceOfType<PhysicalFileHttpResult>(formsTrailingRes);
            Assert.AreEqual(forms, ((PhysicalFileHttpResult)formsTrailingRes).FileName);

            // 3. Unknown route serves app/404.html with status 404
            var unknownRes = NextApp.Fallback("notifications", root);
            Assert.AreEqual(404, ((IStatusCodeHttpResult)unknownRes).StatusCode);

            // 4. Assets with extensions return 404
            foreach (string asset in new[] { "missing.js", "_next/chunk.css", "rider.pdf", "forms/title.json" })
                Assert.AreEqual(404, ((IStatusCodeHttpResult)NextApp.Fallback(asset, root)).StatusCode);

            // 5. Path traversal is rejected with 404
            Assert.AreEqual(404, ((IStatusCodeHttpResult)NextApp.Fallback("x/../../appsettings.json", root)).StatusCode);
            Assert.AreEqual(404, ((IStatusCodeHttpResult)NextApp.Fallback("../outside", root)).StatusCode);

            // 6. When 404.html is absent, falls back to app/index.html
            File.Delete(notFound);
            var spaFallbackRes = NextApp.Fallback("notifications", root);
            Assert.IsInstanceOfType<PhysicalFileHttpResult>(spaFallbackRes);
            Assert.AreEqual(home, ((PhysicalFileHttpResult)spaFallbackRes).FileName);
        }
        finally { Directory.Delete(root, true); }
    }
    [TestMethod]
    public void PolicyAllowsOnlyTheInlineScriptsOfThePageByHash()
    {
        string html = "<html><head><script>boot()</script><script src=\"/app/_next/a.js\"></script></head><body><script id=\"x\">self.__next_f.push([1])</script></body></html>";
        string policy = NextApp.PolicyFor(html);
        string Hash(string v) => "'sha256-" + Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(v))) + "'";
        StringAssert.Contains(policy, Hash("boot()"));
        StringAssert.Contains(policy, Hash("self.__next_f.push([1])"));
        Assert.IsFalse(policy.Contains("unsafe-inline'; img") && policy.Contains("script-src 'self' 'unsafe-inline'"));
        Assert.IsFalse(policy.Contains("script-src 'unsafe-inline'"));
        StringAssert.Contains(policy, "frame-ancestors 'none'");
        Assert.AreEqual(NextApp.BasePolicy, NextApp.PolicyFor("<html><script src=\"/a.js\"></script></html>"));
    }
}
