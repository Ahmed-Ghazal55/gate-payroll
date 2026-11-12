using ErpImport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class LiveOperationsQueryTests
{
    static LiveOperationsQuery.Scope All=>new(true,[],[],[],[]);
    static LiveOperationsQuery.Scope OneCompany=>new(false,["1010123456"],["emp-1"],["prof-1"],["keeta"]);
    [TestMethod]
    public void PreviewSqlUsesOnlyAllowlistedIdentifiersAndParameters()
    {
        var page=new LiveOperationsQuery.PageRequest(1,50,"id","abc",0,50);
        foreach(string kind in Operations.Kinds.Where(k=>k!="sponsorship_period"))
        {
            var (countSql,pageSql,names)=LiveOperationsQuery.PreviewSql(kind,OneCompany,page);
            Assert.IsFalse(countSql.Contains("abc",StringComparison.Ordinal),kind);
            Assert.IsFalse(pageSql.Contains("abc",StringComparison.Ordinal),kind);
            Assert.IsTrue(pageSql.Contains("OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY",StringComparison.Ordinal),kind);
            Assert.IsTrue(countSql.StartsWith("SELECT COUNT_BIG(*)",StringComparison.Ordinal),kind);
            Assert.IsTrue(names.Contains("@skip")&&names.Contains("@take"),kind);
            // No request text injected into identifiers.
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(pageSql,@"\[[^\]]*abc"),kind);
            string table=LiveOperationsQuery.MainTable(kind);
            Assert.IsTrue(pageSql.Contains("["+table+"]",StringComparison.Ordinal),kind);
            foreach(string col in LiveOperationsQuery.AllowlistedColumns(kind).Take(3))
                Assert.IsTrue(pageSql.Contains("["+col+"]",StringComparison.Ordinal),kind+":"+col);
        }
    }
    [TestMethod]
    public void ScopedPreviewAddsCompanyOrRiderParameters()
    {
        var page=new LiveOperationsQuery.PageRequest(1,25,"id","",0,25);
        var vehicle=LiveOperationsQuery.PreviewSql("vehicle",OneCompany,page);
        Assert.IsTrue(vehicle.CountSql.Contains("@cr0",StringComparison.Ordinal));
        Assert.IsTrue(vehicle.CountSql.Contains("CommercialRegistrationNumber",StringComparison.Ordinal));
        var residence=LiveOperationsQuery.PreviewSql("housing_residence",OneCompany,page);
        Assert.IsTrue(residence.CountSql.Contains("@e0",StringComparison.Ordinal)||residence.CountSql.Contains("EmployeeId",StringComparison.Ordinal));
        var open=LiveOperationsQuery.PreviewSql("vehicle",All,page);
        Assert.IsFalse(open.CountSql.Contains("@cr0",StringComparison.Ordinal));
    }
    [TestMethod]
    public void ParseRequestEnforcesPageSizeAndLiveSort()
    {
        var ok=LiveOperationsQuery.ParseRequest(new Dictionary<string,string>{{"page","2"},{"pageSize","500"},{"sort","-id"}});
        Assert.AreEqual(2,ok.Page);Assert.AreEqual(200,ok.PageSize);Assert.AreEqual(200,ok.Skip);Assert.AreEqual("-id",ok.Sort);
        Assert.AreEqual("invalid_page",Assert.ThrowsExactly<ImportFailure>(()=>LiveOperationsQuery.ParseRequest(new Dictionary<string,string>{{"page","0"}})).Code);
        Assert.AreEqual("invalid_live_sort",Assert.ThrowsExactly<ImportFailure>(()=>LiveOperationsQuery.ParseRequest(new Dictionary<string,string>{{"sort","plateNumberAr"}})).Code);
    }
    [TestMethod]
    public void EmptyScopeProducesNoRowsClause()
    {
        var empty=new LiveOperationsQuery.Scope(false,[],[],[],[]);
        var (_,pageSql,_)=LiveOperationsQuery.PreviewSql("vehicle",empty,new(1,50,"id","",0,50));
        Assert.IsTrue(pageSql.Contains("1=0",StringComparison.Ordinal));
    }
}
