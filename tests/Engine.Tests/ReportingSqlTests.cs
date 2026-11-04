using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class ReportingSqlTests
{
    [TestMethod]
    public void IsolatedSqlSchemaBulkRefreshRollbackRebuildAndTamperDetection()
    {
        string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");if(string.IsNullOrWhiteSpace(configured)){Assert.Inconclusive("Reporting SQL integration requires an explicit isolated local test connection; SQLite is not SQL validation.");return;}
        var options=new SqlConnectionStringBuilder(configured);if(!System.Text.RegularExpressions.Regex.IsMatch(options.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)){Assert.Inconclusive("Reporting SQL integration requires loopback, not a staging/production connection.");return;}
        string name="GateReportingTest_"+Guid.NewGuid().ToString("N"),directory=Path.Combine(Path.GetTempPath(),name);options.InitialCatalog="master";using var admin=new SqlConnection(options.ConnectionString);admin.Open();using(var create=admin.CreateCommand()){create.CommandText="CREATE DATABASE ["+name+"]";create.ExecuteNonQuery();}
        try
        {
            options.InitialCatalog=name;var store=new Store(directory,options.ConnectionString);var account=new JsonObject{["id"]="synthetic-sql-account",["companyId"]="synthetic-company",["platformCode"]="keeta",["assignments"]=new JsonArray(new JsonObject{["riderId"]="synthetic-a",["RawEffectiveFrom"]="2026-10-01"},new JsonObject{["riderId"]="synthetic-b",["RawEffectiveFrom"]="2026-10-10"})};
            store.Write((c,tx)=>{Store.Save(c,"platform_account",account,tx);return true;});string before=store.Read(c=>JsonSerializer.Serialize(ReleaseSafety.Capture(c)));store.Write((c,tx)=>Reporting.Rebuild(c,tx));Assert.AreEqual(before,store.Read(c=>JsonSerializer.Serialize(ReleaseSafety.Capture(c))));Assert.IsTrue(store.Read(c=>Reporting.Check(c)).B("consistent"));
            Assert.ThrowsExactly<ApiError>(()=>store.Write<int>((c,tx)=>{var changed=account.Clone();changed["assignments"]=new JsonArray();Store.Save(c,"platform_account",changed,tx);throw new ApiError("Synthetic rollback");}));Assert.IsTrue(store.Read(c=>Reporting.Check(c)).B("consistent"));Assert.AreEqual(2,store.Read(c=>{using var q=Store.Command(c,null,"SELECT COUNT(*) FROM rpt.AccountAssignments");return Convert.ToInt32(q.ExecuteScalar());}));
            store.Write((c,tx)=>{Store.BufferedWrites(c,tx,()=>{var changed=account.Clone();changed["assignments"]=new JsonArray(new JsonObject{["riderId"]="synthetic-c",["RawEffectiveFrom"]="2026-10-15"});Store.Save(c,"platform_account",changed,tx);Store.Save(c,"debt",new(){["id"]="synthetic-sql-debt",["remainingBalance"]=25},tx);});return true;});Assert.IsTrue(store.Read(c=>Reporting.Check(c)).B("consistent"));Assert.AreEqual(1,store.Read(c=>{using var q=Store.Command(c,null,"SELECT COUNT(*) FROM rpt.AccountAssignments");return Convert.ToInt32(q.ExecuteScalar());}));
            store.Write((c,tx)=>{using var q=Store.Command(c,tx,"UPDATE rpt.Debts SET RemainingBalance=26");q.ExecuteNonQuery();return true;});Assert.IsFalse(store.Read(c=>Reporting.Check(c)).B("consistent"));store.Write((c,tx)=>Reporting.Rebuild(c,tx));Assert.IsTrue(store.Read(c=>Reporting.Check(c)).B("consistent"));Assert.AreEqual(25m,store.Read(c=>Store.Get(c,"synthetic-sql-debt","debt")!.N("remainingBalance")));
        }
        finally
        {
            SqlConnection.ClearAllPools();using var drop=admin.CreateCommand();drop.CommandText="ALTER DATABASE ["+name+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+name+"]";drop.ExecuteNonQuery();if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
}
