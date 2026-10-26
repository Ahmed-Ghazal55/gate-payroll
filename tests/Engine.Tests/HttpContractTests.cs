using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class HttpContractTests
{
    [TestMethod]
    [DataRow("integration.mjs",false)]
    [DataRow("enterprise.mjs",false)]
    [DataRow("accounting.mjs",false)]
    [DataRow("accounting.mjs",true)]
    [DataRow("production.mjs",false)]
    [DataRow("campaigns.mjs",false)]
    [DataRow("campaigns.mjs",true)]
    [DataRow("submissions.mjs",false)]
    [DataRow("submissions.mjs",true)]
    [DataRow("integration.mjs",true)]
    [DataRow("enterprise.mjs",true)]
    public async Task HttpContractsRunUnderDotnetTest(string script,bool sqlServer)
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var info=new ProcessStartInfo("node"){WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
        info.ArgumentList.Add(Path.Combine(root,"tests",script));
        SqlConnection? admin=null;string database="GateHttpTest_"+Guid.NewGuid().ToString("N");
        if(sqlServer)
        {
            string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
            if(string.IsNullOrWhiteSpace(configured)){Assert.Inconclusive("SQL HTTP contracts require an explicit isolated CI test connection.");return;}
            var source=new SqlConnectionStringBuilder(configured){InitialCatalog="master"};admin=new SqlConnection(source.ConnectionString);await admin.OpenAsync();
            using var create=admin.CreateCommand();create.CommandText="CREATE DATABASE ["+database+"]";await create.ExecuteNonQueryAsync();
            source.InitialCatalog=database;info.Environment["ConnectionStrings__DefaultConnection"]=source.ConnectionString;
        }
        try
        {
            using var child=Process.Start(info)??throw new InvalidOperationException("Node.js did not start.");
            var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try {await child.WaitForExitAsync(deadline.Token);}
            catch(OperationCanceledException){child.Kill(entireProcessTree:true);throw new TimeoutException("HTTP contract suite timed out: "+script);}
            string stdout=await output,stderr=await error;Console.WriteLine((sqlServer?"SQL Server: ":"SQLite: ")+stdout);
            Assert.AreEqual(0,child.ExitCode,script+"\n"+stdout+"\n"+stderr);
        }
        finally
        {
            if(admin!=null){using var drop=admin.CreateCommand();drop.CommandText="ALTER DATABASE ["+database+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+database+"]";await drop.ExecuteNonQueryAsync();await admin.DisposeAsync();}
        }
    }
}
