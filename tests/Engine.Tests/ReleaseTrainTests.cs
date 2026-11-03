using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class ReleaseTrainTests
{
    [TestMethod]
    public void ReturnUrlsStayInsideAdministrationPaths()
    {
        foreach(string value in new[]{"/app/","/app/riders/?page=2","/legacy/","/legacy/#/riders"})Assert.IsTrue(LegacyUi.SafeReturnUrl(value));
        foreach(string value in new[]{"","/","//outside.invalid/app/","https://outside.invalid/app/","/app/../inquiry","/app/%2e%2e/inquiry","/app/%252e%252e/inquiry","/app/\\outside.invalid","/app/\r\nLocation: other","/application/","/legacy/./"})Assert.IsFalse(LegacyUi.SafeReturnUrl(value),value);
    }
}
