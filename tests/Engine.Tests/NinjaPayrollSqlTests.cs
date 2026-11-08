using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class NinjaPayrollSqlTests
{
    [TestMethod]
    public async Task SyntheticNinjaMonthThroughRealHttpOnSqlServerTiesPostsOnceAndReverses()
    {
        string configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION")??"";
        Assert.IsFalse(string.IsNullOrWhiteSpace(configured),"Explicit isolated SQL connection required; this deliverable must never skip.");
        var options=new SqlConnectionStringBuilder(configured){InitialCatalog="master"};
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(options.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase),"Loopback SQL only.");
        string database="GateR8NinjaTest_"+Guid.NewGuid().ToString("N"),directory=Path.Combine(Path.GetTempPath(),database);
        await using var admin=new SqlConnection(options.ConnectionString);await admin.OpenAsync();
        using(var create=admin.CreateCommand()){create.CommandText="CREATE DATABASE ["+database+"]";await create.ExecuteNonQueryAsync();}
        Process? child=null;Task<string>? output=null,error=null;
        try
        {
            options.InitialCatalog=database;var store=new Store(directory,options.ConnectionString);string company=FinancialCore.Sponsors[0].Id,rider="2000000082",account="synthetic-r8-sql-account",password=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            store.Write((c,tx)=>
            {
                foreach(var (name,role) in new[]{("synthetic-r8-maker","Admin"),("synthetic-r8-checker","FinancialAccountant")})Store.Save(c,"user",UserAccess.Create(name,password,role),tx);
                Store.Save(c,"rider",new(){["id"]=rider,["iqama"]=rider,["name"]="Synthetic SQL rider",["sponsorId"]=company,["isDeliveryRider"]=true,["status"]="Active",["payCategory"]="sponsored_private"},tx);
                Store.Save(c,"platform_account",new(){["id"]=account,["companyId"]=company,["platformCode"]="ninja",["assignments"]=new JsonArray(new JsonObject{["riderId"]=rider,["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00"})},tx);
                Store.Save(c,"platform_account",new(){["id"]="synthetic-r8-wrong-account",["companyId"]=company,["platformCode"]="ninja"},tx);
                Store.Save(c,"ninja_account",new(){["id"]="synthetic-r8-captain",["platformAccountId"]=account,["CaptainId"]="SYN-A"},tx);
                return true;
            });
            JsonObject Draft(string kind,JsonObject data,string code="default")=>store.Write((c,tx)=>PaySettings.Create(c,tx,PayFixture.Maker,kind,"ninja",new(){["period"]="2026-10",["code"]=code,["data"]=data}));
            JsonObject Approve(string kind,string app,JsonObject doc)=>store.Write((c,tx)=>PaySettings.Approve(c,tx,PayFixture.Checker,kind,app,doc.S("id")));
            var global=store.Write((c,tx)=>PaySettings.Create(c,tx,PayFixture.Maker,"pay_setting","global",new(){["effectiveFrom"]="2026-10-01",["fromDefaults"]=true}));Approve("pay_setting","global",global);Approve("pay_terms","ninja",Draft("pay_terms",NinjaBilling.Seed()));
            var rules=Approve("pay_eligibility","ninja",Draft("pay_eligibility",PayFixture.Eligibility()));var schemeData=PayFixture.Scheme("percent_of_gross",rules.S("id"));schemeData["accountingRole"]="principal";
            foreach(var cell in schemeData["parameters"]!["categories"]!.AsObject())cell.Value!["companyPercent"]=0;
            var scheme=Approve("pay_scheme","ninja",Draft("pay_scheme",schemeData,"synthetic"));
            store.Write((c,tx)=>{Store.Save(c,"pay_assignment",new(){["id"]="synthetic-r8-sql-choice",["rider"]=rider,["app"]="ninja",["period"]="2026-10",["status"]="approved",["schemeId"]=scheme.S("id"),["choiceApprovedAt"]="2026-09-29T00:00:00.0000000+00:00"},tx);return true;});
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
            var start=new ProcessStartInfo("dotnet"){WorkingDirectory=Path.Combine(root,"Payroll.Api"),RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};start.ArgumentList.Add(Path.Combine(root,"Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll"));
            start.Environment["ConnectionStrings__DefaultConnection"]=options.ConnectionString;start.Environment["PAYROLL_DATA_DIR"]=directory;start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";start.Environment["ASPNETCORE_URLS"]="http://127.0.0.1:"+port;start.Environment["Jobs__Enabled"]="false";start.Environment["Vouchers__Secret"]=SyntheticVoucherSettings.Key;
            child=Process.Start(start)!;output=child.StandardOutput.ReadToEndAsync();error=child.StandardError.ReadToEndAsync();
            HttpClient Client()=>new(new HttpClientHandler{CookieContainer=new CookieContainer()}){BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(30)};
            using var maker=Client();using var checker=Client();bool ready=false;
            for(int n=0;n<150;n++){if(child.HasExited)Assert.Fail("Synthetic SQL API exited before readiness.");try{if((await maker.GetAsync("/health")).IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(100);}Assert.IsTrue(ready,"Synthetic SQL API readiness timeout.");
            async Task<JsonNode> Request(HttpClient client,string path,JsonObject? body=null,int expected=200)
            {
                using var response=body==null?await client.GetAsync(path):await client.PostAsJsonAsync(path,body);string json=await response.Content.ReadAsStringAsync();Assert.AreEqual(expected,(int)response.StatusCode,path+" "+json[..Math.Min(300,json.Length)]);return JsonNode.Parse(json)!;
            }
            await Request(maker,"/api/auth/login",new(){["username"]="synthetic-r8-maker",["password"]=password});await Request(checker,"/api/auth/login",new(){["username"]="synthetic-r8-checker",["password"]=password});
            await Request(maker,"/api/platforms/ninja/posting",new(){["enabled"]=true});
            byte[] bytes=NinjaInvoiceImportTests.Workbook(penalty:10,blocked:20);
            async Task<JsonObject> Upload(JsonObject? preview=null,string? selected=null,int expected=200)
            {
                using var form=new MultipartFormDataContent();form.Add(new StringContent(company),"companyId");form.Add(new StringContent("2026-10"),"period");form.Add(new StringContent("SYN-R8-SQL"),"reference");form.Add(new ByteArrayContent(bytes),"file","synthetic.xlsx");form.Add(new StringContent(selected??account),"accountIds[]");
                if(preview!=null){form.Add(new StringContent(preview["rows"]!.ToJsonString()),"rows");form.Add(new StringContent(preview.S("fingerprint")),"fingerprint");form.Add(new StringContent("true"),"confirm");}
                using var response=await maker.PostAsync("/api/platforms/ninja/invoices"+(preview==null?"":"?commit=1"),form);string json=await response.Content.ReadAsStringAsync();Assert.AreEqual(expected,(int)response.StatusCode,json[..Math.Min(300,json.Length)]);return JsonNode.Parse(json)!.AsObject();
            }
            var wrong=await Upload(selected:"synthetic-r8-wrong-account",expected:400);StringAssert.Contains(wrong.S("error"),"rows 2");
            var preview=await Upload();var invoice=await Upload(preview);string invoiceId=invoice.S("id"),invoicePath="/api/platforms/ninja/invoices/"+Uri.EscapeDataString(invoiceId);Assert.AreEqual("pending",invoice.S("approvalStatus"));
            var sourceLines=(await Request(maker,invoicePath+"/lines")).AsArray();string source=sourceLines[0].S("id");
            await Request(maker,invoicePath+"/approve",new(),403);
            foreach(var (category,level) in new[]{("penalty","account_level"),("naqilBlocked","company_level")})
                await Request(maker,invoicePath+"/classify",new(){["lineId"]=source,["category"]=category,["level"]=level,["expenseAccount"]="526402"});
            invoice=(await Request(checker,invoicePath+"/approve",new())).AsObject();Assert.AreEqual("approved",invoice.S("approvalStatus"));
            var dispute=(await Request(maker,invoicePath+"/dispute",new(){["reason"]="Synthetic formal clarification"})).AsObject();string disputeId=dispute["disputes"]![0].S("id");
            await Request(checker,invoicePath+"/resolve",new(){["disputeId"]=disputeId,["reason"]="Synthetic resolved as invoiced"});
            var missingDraft=(await Request(maker,"/api/pay/review-draft?app=ninja&period=2026-10&companyId="+company)).AsObject();Assert.IsFalse(missingDraft.B("canCreate"));
            var columns=ImportCatalog.Steps().Single(s=>s.Key=="ninja_operations").Columns;
            var cells=new Dictionary<string,object?>{["period (YYYY-MM)"]="2026-10",["platformAccountId"]=account,["iqama"]=rider,["days"]=30,["orders"]=460,["baseGross"]=6500m,["deduction"]=10m,["category"]="sponsored_private"};
            byte[] operations=ExcelFiles.Write(columns,[(columns.Select(col=>cells.GetValueOrDefault(col.Header)??"").ToArray())]);
            var operationInput=new JsonObject{["invoiceId"]=invoiceId,["dataBase64"]=Convert.ToBase64String(operations)};
            var operationPreview=(await Request(maker,"/api/pay/apps/ninja/operations/import/preview",operationInput)).AsObject();operationInput["rows"]=operationPreview["rows"]!.DeepClone();operationInput["fingerprint"]=operationPreview.S("fingerprint");operationInput["confirm"]=true;
            await Request(maker,"/api/pay/apps/ninja/operations/import/commit",operationInput);
            string beforeDraft=store.Read(c=>System.Text.Json.JsonSerializer.Serialize(ReleaseSafety.Capture(c)));
            var review=(await Request(maker,"/api/pay/review-draft?app=ninja&period=2026-10&companyId="+company)).AsObject();Assert.IsTrue(review.B("canCreate"),review["blockers"]!.ToJsonString());Assert.AreEqual(beforeDraft,store.Read(c=>System.Text.Json.JsonSerializer.Serialize(ReleaseSafety.Capture(c))));
            var reviewAccount=review["invoices"]![0]!["accounts"]![0]!.AsObject();Assert.AreEqual(6500m,reviewAccount.N("baseGross"));Assert.AreEqual(source,reviewAccount.S("sourceLineId"));
            var cycleInput=new JsonObject{["period"]="2026-10",["companyId"]=company,["app"]="ninja",["invoices"]=review["invoices"]!.DeepClone()};
            var cycle=(await Request(maker,"/api/pay/cycles",cycleInput)).AsObject();string id=cycle.S("id"),cyclePath="/api/pay/cycles/"+Uri.EscapeDataString(id);
            for(int stage=1;stage<=6;stage++)cycle=(await Request(maker,cyclePath+"/advance",new(){["stage"]=stage})).AsObject();
            var linePage=(await Request(maker,cyclePath+"/lines")).AsObject();var payLine=linePage["items"]![0]!.AsObject();Assert.AreEqual(6500m,payLine.N("platformGross"));Assert.AreEqual(source,payLine["sources"]![0].S("sourceLineId"));Assert.AreEqual(1,payLine["sources"]![0]!["operationSourceIds"]!.AsArray().Count);
            decimal net=cycle["riders"]![0]!.N("net");Assert.AreEqual(6490m,net);Assert.AreEqual(6470m,net-20m,"The company bears its classified 20; the rider bears only 10.");
            await Request(maker,cyclePath+"/review/approve-all",new());await Request(maker,cyclePath+"/advance",new(){["stage"]=7},403);
            cycle=(await Request(checker,cyclePath+"/advance",new(){["stage"]=7})).AsObject();Assert.AreEqual("approved",cycle.S("status"));await Request(checker,cyclePath+"/debt-deductions",new(){["action"]="confirm-all"});
            var statement=(await Request(checker,"/api/finance/statement/rider/"+rider+"/monthly?period=2026-10")).AsObject();Assert.AreEqual(net,statement.N("net"));Assert.AreEqual("approved",statement.S("status"));Assert.AreEqual(1,statement["cards"]!.AsArray().Count);Assert.AreEqual(source,statement["cards"]![0]!["sources"]![0].S("sourceLineId"));
            var journals=store.Read(c=>Store.List(c,"journal").Where(j=>j.S("payCycleId")==id).ToList());Assert.AreEqual(1,journals.Count);Assert.AreEqual(journals[0].N("totalDebit"),journals[0].N("totalCredit"));string journalBody=journals[0].ToJsonString();
            decimal receivable=journals[0]["lines"]!.AsArray().OfType<JsonObject>().Where(l=>l.S("accountCode")=="112806").Sum(l=>l.N("debit")-l.N("credit"));Assert.AreEqual(6470m+970.50m,receivable);
            invoice=(await Request(checker,invoicePath)).AsObject();Assert.AreEqual(cycle.S("journalEntryId"),invoice.S("journalEntryId"),"Invoice must link to its payroll posting.");
            var replay=await Upload(preview);Assert.AreEqual(invoiceId,replay.S("id"));await Request(checker,cyclePath+"/advance",new(){["stage"]=7});await Request(maker,"/api/pay/cycles",cycleInput,400);Assert.AreEqual(1,store.Read(c=>Store.List(c,"journal").Count));
            await Request(maker,cyclePath+"/advance",new(){["action"]="reversal_request",["period"]="2026-11",["note"]="Synthetic correction"});
            await Request(maker,cyclePath+"/advance",new(){["action"]="reversal_approve"},403);
            var reversed=(await Request(checker,cyclePath+"/advance",new(){["action"]="reversal_approve"})).AsObject();Assert.AreEqual("reversed",reversed.S("status"));Assert.AreEqual(2,store.Read(c=>Store.List(c,"journal").Count));Assert.AreEqual(journalBody,store.Read(c=>Store.Get(c,journals[0].S("id"),"journal")!.ToJsonString()));
            var reversedInvoice=(await Request(checker,invoicePath)).AsObject();Assert.AreEqual("reversed",reversedInvoice.S("accountingStatus"));Assert.AreNotEqual("",reversedInvoice.S("reversalJournalEntryId"));
            var afterStatement=(await Request(checker,"/api/finance/statement/rider/"+rider+"/monthly?period=2026-10")).AsObject();Assert.AreEqual(0m,afterStatement.N("net"));
            Console.WriteLine("R8 SQL HTTP: invoice, classification, independent approval, dispute, D3, draft, 7 stages, lines, statement, one posting, replay and reversal passed.");
        }
        finally
        {
            if(child!=null){if(!child.HasExited)child.Kill(entireProcessTree:true);await child.WaitForExitAsync();child.Dispose();if(output!=null)await output;if(error!=null)await error;}
            SqlConnection.ClearAllPools();using var drop=admin.CreateCommand();drop.CommandText="ALTER DATABASE ["+database+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+database+"]";await drop.ExecuteNonQueryAsync();if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
}
