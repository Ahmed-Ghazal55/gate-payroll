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
    static LiveOperationsQuery.PageRequest Page1=>new(1,25,"id","",0,25);
    [TestMethod]
    public void CustodyHandoverIsScopedThroughAssignmentVehicleCompanyAndRider()
    {
        var sql=LiveOperationsQuery.PreviewSql("custody_handover",OneCompany,Page1);
        StringAssert.Contains(sql.CountSql,"[RiderVehicleAssignments] a");
        StringAssert.Contains(sql.CountSql,"av.[SponsorId] IN");
        StringAssert.Contains(sql.CountSql,"a.[RiderProfileId] IN (@rp0)");
        StringAssert.Contains(sql.PageSql,"ORDER BY t.[Id]");
        Assert.IsFalse(LiveOperationsQuery.PreviewSql("custody_handover",All,Page1).CountSql.Contains("EXISTS",StringComparison.Ordinal));
    }
    [TestMethod]
    public void VehicleAssignmentNeedsRiderAndVehicleCompany()
    {
        var sql=LiveOperationsQuery.PreviewSql("vehicle_assignment",OneCompany,Page1).CountSql;
        StringAssert.Contains(sql,"t.[RiderProfileId] IN (@rp0)");
        StringAssert.Contains(sql,"av.[SponsorId] IN");
        var noRiders=new LiveOperationsQuery.Scope(false,["1010123456"],[],[],[]);
        StringAssert.Contains(LiveOperationsQuery.PreviewSql("vehicle_assignment",noRiders,Page1).CountSql,"1=0");
    }
    [TestMethod]
    public void PlatformLimitAppliesInSqlToEveryNonAdministrator()
    {
        var allButLimited=new LiveOperationsQuery.Scope(true,[],[],[],["ninja"],PlatformRestricted:true);
        StringAssert.Contains(LiveOperationsQuery.PreviewSql("platform_account",allButLimited,Page1).CountSql,"cp.[Code] IN (@pc0)");
        var noPlatforms=new LiveOperationsQuery.Scope(true,[],[],[],[],PlatformRestricted:true);
        StringAssert.Contains(LiveOperationsQuery.PreviewSql("platform_account",noPlatforms,Page1).CountSql,"1=0");
        Assert.IsFalse(LiveOperationsQuery.PreviewSql("platform_account",All,Page1).CountSql.Contains("cp.[Code] IN",StringComparison.Ordinal));
    }
    [TestMethod]
    public void LargeScopeUsesOneJsonParameterUnderSqlServerLimit()
    {
        var many=Enumerable.Range(0,3000).Select(i=>"emp-"+i).ToList();
        var scope=new LiveOperationsQuery.Scope(false,["1010123456"],many,many,[]);
        var (countSql,_,names)=LiveOperationsQuery.PreviewSql("housing_residence",scope,Page1);
        StringAssert.Contains(countSql,"OPENJSON(@ejson)");
        Assert.IsTrue(names.Count<2100,"parameters: "+names.Count);
    }
    [TestMethod]
    public void SearchUsesTypedAndNormalisedText()
    {
        var both=LiveOperationsQuery.PreviewSql("vehicle",All,new(1,25,"id","أحمد",0,25,"احمد"));
        StringAssert.Contains(both.CountSql,"LIKE @q ");StringAssert.Contains(both.CountSql,"LIKE @qa ");
        var same=LiveOperationsQuery.PreviewSql("vehicle",All,new(1,25,"id","abc",0,25,"abc"));
        Assert.IsFalse(same.CountSql.Contains("@qa",StringComparison.Ordinal));
    }
    [TestMethod]
    public void EmptyScopeProducesNoRowsClause()
    {
        var empty=new LiveOperationsQuery.Scope(false,[],[],[],[]);
        var (_,pageSql,_)=LiveOperationsQuery.PreviewSql("vehicle",empty,new(1,50,"id","",0,50));
        Assert.IsTrue(pageSql.Contains("1=0",StringComparison.Ordinal));
    }
}
