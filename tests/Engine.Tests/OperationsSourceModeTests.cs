using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>C0: hosted always reads ERP live; only Development/CI may fall back to the payroll store.</summary>
[TestClass]
public sealed class OperationsSourceModeTests
{
    sealed class Env(string name):IHostEnvironment
    {
        public string EnvironmentName{get;set;}=name;
        public string ApplicationName{get;set;}="Gate Payroll";
        public string ContentRootPath{get;set;}="/";
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
    }
    static IConfiguration Config(params (string Key,string Value)[] values)=>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v=>new KeyValuePair<string,string?>(v.Key,v.Value))).Build();
    const string Conn="Server=x;Database=db67927;User Id=db67927_readonly;Password=synthetic";

    [TestMethod]
    public void ProductionAlwaysLiveEvenWithoutSecretOrWithStoreOverride()
    {
        Assert.AreEqual(Operations.SourceLive,Operations.SourceMode(Config(),new Env("Production")));
        Assert.AreEqual(Operations.SourceLive,Operations.SourceMode(Config(("Operations:Source","store")),new Env("Production")));
        Assert.IsFalse(Operations.ErpConfigured(Config()));
    }
    [TestMethod]
    public void DevelopmentFallsBackToStoreWithoutErpLogin()
    {
        Assert.AreEqual(Operations.SourceStore,Operations.SourceMode(Config(),new Env("Development")));
        Assert.AreEqual(Operations.SourceLive,Operations.SourceMode(Config(("ConnectionStrings:ErpReadonly",Conn)),new Env("Development")));
        Assert.AreEqual(Operations.SourceLive,Operations.SourceMode(Config(("ERP_READONLY_DB",Conn)),new Env("Development")));
    }
    [TestMethod]
    public void DevelopmentExplicitSettingWins()
    {
        Assert.AreEqual(Operations.SourceStore,Operations.SourceMode(Config(("ConnectionStrings:ErpReadonly",Conn),("Operations:Source","store")),new Env("Development")));
        Assert.AreEqual(Operations.SourceLive,Operations.SourceMode(Config(("Operations:Source","erp_live")),new Env("Development")));
    }
}
