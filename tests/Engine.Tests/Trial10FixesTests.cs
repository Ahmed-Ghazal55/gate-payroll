using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>TRIAL10 / grok-TRIAL9: forged calculatedBy ignored on POST /payroll/approve;
/// PaymentsEngine approve refuses creator+editors; sweep hits live routes (HTTP / Transition).</summary>
[TestClass]
public sealed class Trial10FixesTests
{
    [TestMethod]
    public void PaymentsEngine_RewriterCannotApprove_Purchase_WithoutGrant()
    {
        using var f=new PaymentsEngineTests.Fixture(false);
        var creator=PaymentsEngineTests.Fixture.User("t10-creator");
        var rewriter=PaymentsEngineTests.Fixture.User("t10-rewriter");
        string supplierId=MoneySchemaFixture.Id();
        f.Store.Write((c,tx)=>
        {
            foreach(string name in new[]{"t10-creator","t10-rewriter"})
                if(UserAccess.Find(c,name,tx)==null)
                    Store.Save(c,"user",new(){["id"]=name,["username"]=name,["role"]="Admin",["department"]="FIN",["rankLevel"]=5},tx);
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"pay","supplier")}(id,company_id,name_ar,status,created_by,created_at)
                VALUES(@id,@c,'مورد تجريبي',1,@by,{SchemaSql.NowLiteral(c)})
                """,("@id",supplierId),("@c",f.Company),("@by","t10-creator"));
            return true;
        });
        var input=f.Input("purchase",100);
        input["supplierId"]=supplierId;
        var doc=f.Store.Write((c,tx)=>PaymentsEngine.Create(c,tx,creator,input));
        var patch=new JsonObject
        {
            ["expectedHash"]=doc.S("contentHash"),
            ["lines"]=new JsonArray(new JsonObject{["accountCode"]="526101",["amount"]=90})
        };
        doc=f.Store.Write((c,tx)=>PaymentsEngine.Edit(c,tx,rewriter,doc.S("id"),patch));
        Assert.AreEqual("t10-rewriter",doc.S("updatedBy"),"Edit must stamp updatedBy so approve can refuse the editor");
        f.Store.Write((c,tx)=>PaymentsEngine.Transition(c,tx,rewriter,doc.S("id"),"submit",new()));
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>
            f.Store.Write((c,tx)=>PaymentsEngine.Transition(c,tx,rewriter,doc.S("id"),"approve",new()))).Status,
            "rewriter is a maker (updatedBy) and must not approve without approve_own");
        // Creator also blocked (createdBy).
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>
            f.Store.Write((c,tx)=>PaymentsEngine.Transition(c,tx,creator,doc.S("id"),"approve",new()))).Status);
        // Third person succeeds.
        var checker=PaymentsEngineTests.Fixture.User("t10-checker");
        f.Store.Write((c,tx)=>
        {
            if(UserAccess.Find(c,"t10-checker",tx)==null)
                Store.Save(c,"user",new(){["id"]="t10-checker",["username"]="t10-checker",["role"]="Admin",["department"]="FIN",["rankLevel"]=5},tx);
            return PaymentsEngine.Transition(c,tx,checker,doc.S("id"),"approve",new());
        });
    }

    [TestMethod]
    public async Task PayrollApprove_Http_ForgedCalculatedBy_Ignored_SelfBlocked()
    {
        // Live POST /api/payroll/approve: body calculatedBy must not skip Independent.
        string directory=Path.Combine(Path.GetTempPath(),"gate-t10-pay-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Process? child=null;Task<string>? output=null,error=null;
        try
        {
            var store=new Store(directory);
            string password=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            const string iq="2000000110",period="2026-10",makerName="t10-pay-maker",checkerName="t10-pay-checker";
            store.Write((c,tx)=>
            {
                // Both Admins so /api/payroll/approve permission is held; Independent still requires two people.
                foreach(var (name,role) in new[]{(makerName,"Admin"),(checkerName,"Admin")})
                    Store.Save(c,"user",UserAccess.Create(name,password,role),tx);
                var policy=new JsonObject
                {
                    ["id"]=Guid.NewGuid().ToString("N"),["name"]="عمولة تجريبية",["kind"]="commission",
                    ["platform"]="all",["effectiveFrom"]="2026-10",["status"]="approved",
                    ["config"]=new JsonObject{["companyFee"]=0},["createdBy"]=makerName,
                    ["createdAt"]=DateTimeOffset.UtcNow.ToString("O")
                };
                Store.Save(c,"policy",policy,tx);
                Store.Save(c,"rider",new()
                {
                    ["id"]=iq,["iqama"]=iq,["name"]="مندوب تجريبي",["sponsorId"]=FinancialCore.Sponsors[0].Id,
                    ["financeSponsorId"]=FinancialCore.Sponsors[0].Id,["isDeliveryRider"]=true,["status"]="Active",
                    ["policyId"]=policy.S("id"),["address"]="جدة"
                },tx);
                // Stored preparer on the work row — body must not replace this.
                Store.Save(c,"work",new()
                {
                    ["id"]="t10-work-1",["sourceRef"]="t10-row-1",["iqama"]=iq,["name"]="مندوب تجريبي",
                    ["platform"]="Keeta",["accountId"]="1234567890123456",["period"]=period,
                    ["from"]="2026-10-01",["to"]="2026-10-31",["orders"]=600,["platformRevenue"]=3000,
                    ["riderPay"]=1500,["isValid"]=false,["company"]="Synthetic",["city"]="Test",
                    ["datesConfirmed"]=true,["createdBy"]=makerName
                },tx);
                return true;
            });
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
            var start=new ProcessStartInfo("dotnet")
            {
                WorkingDirectory=Path.Combine(root,"Payroll.Api"),
                RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false
            };
            start.ArgumentList.Add(Path.Combine(root,"Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll"));
            start.Environment["PAYROLL_DATA_DIR"]=directory;
            start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";
            start.Environment["ASPNETCORE_URLS"]="http://127.0.0.1:"+port;
            start.Environment["Jobs__Enabled"]="false";
            start.Environment["Vouchers__Secret"]=SyntheticVoucherSettings.Key;
            child=Process.Start(start)!;output=child.StandardOutput.ReadToEndAsync();error=child.StandardError.ReadToEndAsync();
            HttpClient Client()=>new(new HttpClientHandler{CookieContainer=new CookieContainer()}){BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(30)};
            using var maker=Client();using var checker=Client();
            bool ready=false;
            for(int n=0;n<150;n++)
            {
                if(child.HasExited)Assert.Fail("API exited before readiness.");
                try{if((await maker.GetAsync("/health")).IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}
                await Task.Delay(100);
            }
            Assert.IsTrue(ready,"API readiness timeout.");
            async Task<JsonNode> Request(HttpClient client,string path,JsonObject? body=null,int expected=200)
            {
                using var response=body==null?await client.GetAsync(path):await client.PostAsJsonAsync(path,body);
                string json=await response.Content.ReadAsStringAsync();
                Assert.AreEqual(expected,(int)response.StatusCode,path+" "+json[..Math.Min(400,json.Length)]);
                return JsonNode.Parse(json)!;
            }
            await Request(maker,"/api/auth/login",new(){["username"]=makerName,["password"]=password});
            await Request(checker,"/api/auth/login",new(){["username"]=checkerName,["password"]=password});
            var preview=(await Request(maker,"/api/payroll/preview?period="+period)).AsArray();
            Assert.IsTrue(preview.Count>=1,"preview must include the seeded rider");
            var row=preview.OfType<JsonObject>().First(p=>p.S("iqama")==iq);
            Assert.AreEqual(0,row["blockers"]!.AsArray().Count,row["blockers"]!.ToJsonString());
            string fingerprint=row.S("fingerprint");
            // Forged body maker — must still refuse the real preparer (maker).
            await Request(maker,"/api/payroll/approve",new()
            {
                ["iqama"]=iq,["period"]=period,["fingerprint"]=fingerprint,
                ["calculatedBy"]="forged-other-person",["sponsorId"]=FinancialCore.Sponsors[0].Id
            },403);
            // Independent checker succeeds (body calculatedBy still ignored; stored maker is makerName).
            await Request(checker,"/api/payroll/approve",new()
            {
                ["iqama"]=iq,["period"]=period,["fingerprint"]=fingerprint,
                ["calculatedBy"]="forged-other-person",["sponsorId"]=FinancialCore.Sponsors[0].Id
            });
        }
        finally
        {
            if(child!=null)
            {
                if(!child.HasExited)child.Kill(entireProcessTree:true);
                await child.WaitForExitAsync();child.Dispose();
                if(output!=null)await output;if(error!=null)await error;
            }
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    public void Sweep_LiveRoutes_PayrollHttpAndPurchaseTransition(bool sql)
    {
        // STEP 4: exercise live PaymentsEngine.Transition (not DemandIndependentOfMakers helper) for a rewritten purchase.
        // Payroll forged-body case is covered by PayrollApprove_Http_ForgedCalculatedBy_Ignored_SelfBlocked (real HTTP).
        _=sql;
        PaymentsEngine_RewriterCannotApprove_Purchase_WithoutGrant();
    }
}
