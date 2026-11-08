using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class R9PayrollHttpTests
{
    const string Positive="2000000901",Negative="2000000902";
    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public async Task InvoiceReviewSalaryApprovalDebtConfirmationAndBankFileTieThroughHttp(bool sql)
    {
        await using var f=await Host.Start(sql);string company=FinancialCore.Sponsors[0].Id;
        var header=NinjaBillingTests.Header.ToList();var positive=NinjaBillingTests.Row("SYN-A","capability_car",31,460,31);var negative=NinjaBillingTests.Row("SYN-B","capability_car",31,1,1);
        void Set(List<string> row,string field,decimal value)=>row[header.IndexOf(field)]=value.ToString(CultureInfo.InvariantCulture);
        Set(negative,"Target Total Amount",10m);Set(negative,"Attendance Deduction",1.5m);Set(negative,"Count of Days Less than 8 Hr",1m);Set(negative,"8 Hour Deductions",31.45m);Set(negative,"Final Amount",-22.95m);Set(negative,"VAT",-3.4425m);Set(negative,"TOTAL COST",-26.3925m);
        var supplier=header.Select(_=>"-").ToList();supplier[header.IndexOf("captain_id")]="SYN-A";supplier[header.IndexOf("Target Level Type")]="DEDUCTIONS";Set(supplier,"Final Amount",-33.12m);Set(supplier,"VAT",-4.968m);Set(supplier,"TOTAL COST",-38.088m);
        byte[] bytes=NinjaInvoiceImportTests.WorkbookRows(header,positive,negative,supplier);
        async Task<JsonObject> Upload(JsonObject? preview=null)
        {
            using var form=new MultipartFormDataContent();form.Add(new StringContent(company),"companyId");form.Add(new StringContent("2026-10"),"period");form.Add(new StringContent("SYN-R9"),"reference");form.Add(new ByteArrayContent(bytes),"file","synthetic.xlsx");
            if(preview!=null){form.Add(new StringContent(preview["rows"]!.ToJsonString()),"rows");form.Add(new StringContent(preview.S("fingerprint")),"fingerprint");form.Add(new StringContent("true"),"confirm");}
            using var response=await f.Maker.PostAsync("/api/platforms/ninja/invoices"+(preview==null?"":"?commit=1"),form);Assert.AreEqual(HttpStatusCode.OK,response.StatusCode,await response.Content.ReadAsStringAsync());return(await response.Content.ReadFromJsonAsync<JsonObject>())!;
        }
        var preview=await Upload();var invoice=await Upload(preview);string invoicePath="/api/platforms/ninja/invoices/"+Uri.EscapeDataString(invoice.S("id"));
        var lines=(await f.Request(f.Maker,invoicePath+"/lines")).AsArray();var adjustment=lines.OfType<JsonObject>().Single(l=>l.S("lineType")=="company");Assert.AreEqual("SYN-A",adjustment.S("captainId"));
        await f.Request(f.Maker,invoicePath+"/classify",new(){["lineId"]=adjustment.S("id"),["category"]="supplier_adjustment",["level"]="rider_level",["riderId"]=Positive});
        await f.Request(f.Maker,invoicePath+"/approve",new(),403);await f.Request(f.Checker,invoicePath+"/approve",new());
        var columns=ImportCatalog.Steps().Single(s=>s.Key=="ninja_operations").Columns;var rows=new List<object?[]>();
        foreach(var line in lines.OfType<JsonObject>().Where(l=>l.S("lineType")!="company"))
        {
            bool pos=line.S("captainId")=="SYN-A";var cells=new Dictionary<string,object?>{["period (YYYY-MM)"]="2026-10",["platformAccountId"]=line.S("platformAccountId"),["iqama"]=pos?Positive:Negative,["days"]=pos?30:1,["orders"]=line.N("orders"),["baseGross"]=line.N("baseGross"),["deduction"]=0m,["category"]="sponsored_private"};rows.Add(columns.Select(c=>cells.GetValueOrDefault(c.Header)??"").ToArray());
        }
        var operationInput=new JsonObject{["invoiceId"]=invoice.S("id"),["dataBase64"]=Convert.ToBase64String(ExcelFiles.Write(columns,rows))};
        var operationPreview=(await f.Request(f.Maker,"/api/pay/apps/ninja/operations/import/preview",operationInput)).AsObject();operationInput["rows"]=operationPreview["rows"]!.DeepClone();operationInput["fingerprint"]=operationPreview.S("fingerprint");operationInput["confirm"]=true;await f.Request(f.Maker,"/api/pay/apps/ninja/operations/import/commit",operationInput);
        var draft=(await f.Request(f.Maker,"/api/pay/review-draft?app=ninja&period=2026-10&companyId="+company)).AsObject();Assert.IsTrue(draft.B("canCreate"));
        var cycle=(await f.Request(f.Maker,"/api/pay/cycles",new(){["period"]="2026-10",["companyId"]=company,["app"]="ninja",["invoices"]=draft["invoices"]!.DeepClone()})).AsObject();string id=cycle.S("id"),path="/api/pay/cycles/"+Uri.EscapeDataString(id);
        for(int stage=1;stage<=6;stage++)await f.Request(f.Maker,path+"/advance",new(){["stage"]=stage});
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count(d=>d.S("category")=="NegativeSalaryCarryover")));
        await f.Request(f.Maker,path+"/review/"+Positive,new(){["action"]="adjust",["itemType"]="bonus",["amount"]=100,["reason"]="Synthetic bonus"});await f.Request(f.Maker,path+"/review/"+Positive,new(){["action"]="adjust",["itemType"]="correction",["amount"]=-200,["reason"]="Synthetic downward correction"});
        cycle=(await f.Request(f.Checker,path+"/advance",new(){["stage"]=7})).AsObject();Assert.AreEqual("partially_approved",cycle.S("status"));
        var beforeDebt=(await f.Request(f.Maker,"/api/finance/bank-file/preview",new(){["sponsorId"]=company,["period"]="2026-10"})).AsObject();Assert.AreEqual(0,beforeDebt["rows"]!.AsArray().Count,"Payout waits for debt confirmation.");
        await f.Request(f.Maker,path+"/review/"+Negative,new(){["action"]="approve",["reason"]="Accept final deficit after all adjustments"});cycle=(await f.Request(f.Checker,path+"/advance",new(){["stage"]=7})).AsObject();Assert.AreEqual("approved",cycle.S("status"));
        var carry=f.Store.Read(c=>Store.List(c,"debt").Single(d=>d.S("category")=="NegativeSalaryCarryover"));Assert.AreEqual(32.95m,carry.N("remainingBalance"));Assert.AreEqual("112507",carry.S("receivableAccountCode"));
        Assert.AreEqual(10000m,f.Store.Read(c=>Store.Get(c,"synthetic-r9-debt","debt")!.N("remainingBalance")));
        await f.Request(f.Maker,path+"/debt-deductions",new(){["action"]="edit",["rider"]=Positive,["key"]="debt:synthetic-r9-skip",["amount"]=0,["reason"]="Skip by agreement this month"});await f.Request(f.Maker,path+"/debt-deductions",new(){["action"]="confirm-all"},403);
        await f.Request(f.Checker,path+"/debt-deductions",new(){["action"]="confirm-all"});Assert.AreEqual(9000m,f.Store.Read(c=>Store.Get(c,"synthetic-r9-debt","debt")!.N("remainingBalance")));Assert.AreEqual(50m,f.Store.Read(c=>Store.Get(c,"synthetic-r9-skip","debt")!.N("remainingBalance")));
        var bank=(await f.Request(f.Maker,"/api/finance/bank-file/preview",new(){["sponsorId"]=company,["period"]="2026-10"})).AsObject();Assert.IsTrue(bank.B("canExport"));Assert.AreEqual(1,bank["rows"]!.AsArray().Count);Assert.AreEqual(5066.88m,bank["rows"]![0]!.N("amount"));
        using(var export=await f.Maker.PostAsJsonAsync("/api/finance/bank-file/export",new JsonObject{["sponsorId"]=company,["period"]="2026-10",["previewToken"]=bank.S("previewToken"),["rows"]=bank["rows"]!.DeepClone()})){Assert.AreEqual(HttpStatusCode.OK,export.StatusCode);StringAssert.Contains(await export.Content.ReadAsStringAsync(),"5066.88");}
        var journals=f.Store.Read(c=>Store.List(c,"journal").Where(j=>j.S("payCycleId")==id).ToList());foreach(var j in journals){Assert.AreEqual(j.N("totalDebit"),j.N("totalCredit"));Assert.IsTrue(j["lines"]!.AsArray().All(l=>l!.N("debit")>=0&&l.N("credit")>=0));}
        var posted=journals.SelectMany(j=>j["lines"]!.AsArray().OfType<JsonObject>()).ToList();Assert.AreEqual(5066.88m,posted.Where(l=>l.S("accountCode")=="212103").Sum(l=>l.N("credit")-l.N("debit")));
        int count=journals.Count;await f.Request(f.Checker,path+"/advance",new(){["stage"]=7});await f.Request(f.Checker,path+"/debt-deductions",new(){["action"]="confirm-all"});Assert.AreEqual(count,f.Store.Read(c=>Store.List(c,"journal").Count(j=>j.S("payCycleId")==id)));
    }
    internal sealed class Host:IAsyncDisposable
    {
        public Store Store=null!;public HttpClient Maker=null!,Checker=null!;Process? child;Task<string>? output,error;SqlConnection? admin;string database="",directory="";
        public static async Task<Host> Start(bool sql,string? projectRoot=null)
        {
            var f=new Host{directory=Path.Combine(Path.GetTempPath(),"gate-r9-http-"+Guid.NewGuid().ToString("N"))};string? connection=null;
            try
            {
                if(sql)
                {
                    string configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION")??"";Assert.IsFalse(string.IsNullOrWhiteSpace(configured),"Explicit local SQL required, zero skipped.");var options=new SqlConnectionStringBuilder(configured){InitialCatalog="master"};Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(options.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$"));
                    f.admin=new(options.ConnectionString);await f.admin.OpenAsync();f.database="GateR9Test_"+Guid.NewGuid().ToString("N");using(var command=f.admin.CreateCommand()){command.CommandText="CREATE DATABASE ["+f.database+"]";await command.ExecuteNonQueryAsync();}options.InitialCatalog=f.database;connection=options.ConnectionString;
                }
                if(OperatingSystem.IsLinux())Directory.CreateDirectory(f.directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
                f.Store=new(f.directory,connection);string password=Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),company=FinancialCore.Sponsors[0].Id;
                f.Store.Write((c,tx)=>
                {
                    foreach(var(name,role) in new[]{("synthetic-pay-maker","Admin"),("synthetic-pay-checker","FinancialAccountant")})Store.Save(c,"user",UserAccess.Create(name,password,role),tx);
                    foreach(var(rider,captain) in new[]{(Positive,"SYN-A"),(Negative,"SYN-B")})
                    {
                        string bban="80000000000000000000",iban="SA"+(98-(int)(BigInteger.Parse(bban+"281000")%97)).ToString("D2")+bban;
                        Store.Save(c,"rider",new(){["id"]=rider,["iqama"]=rider,["name"]="Synthetic rider",["sponsorId"]=company,["isDeliveryRider"]=true,["status"]="Active",["payCategory"]="sponsored_private",["iban"]=iban},tx);Store.Save(c,"platform_account",new(){["id"]="synthetic-r9-"+captain,["externalAccountId"]=captain,["companyId"]=company,["platformCode"]="NINJA",["assignments"]=new JsonArray(new JsonObject{["riderId"]=rider,["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00"})},tx);
                    }
                    foreach(var(id,balance,instalment) in new[]{("synthetic-r9-debt",10000m,1000m),("synthetic-r9-skip",50m,50m)})
                    {
                        Store.Save(c,"debt",new(){["id"]=id,["iqama"]=Positive,["category"]="Advance",["creditorSponsorId"]=company,["receivableAccountCode"]="112501",["status"]="active",["totalAmount"]=balance,["remainingBalance"]=balance,["paidAmount"]=0,["monthlyInstallment"]=instalment,["effectiveFrom"]="2026-01"},tx);
                        FinancialCore.PostJournal(c,tx,"opening","Synthetic approved opening","2026-10",id,new JsonArray(PayPosting.Line("112501",company,balance,0,"ninja",Positive,"2026-10",id,debt:id),PayPosting.Line("311101",company,0,balance,"ninja",Positive,"2026-10",id)),"synthetic-opening-checker",targetChart:true,payChart:true);
                    }
                    var global=PaySettings.Create(c,tx,PayFixture.Maker,"pay_setting","global",new(){["effectiveFrom"]="2026-10-01",["fromDefaults"]=true});PaySettings.Approve(c,tx,PayFixture.Checker,"pay_setting","global",global.S("id"));
                    JsonObject Approve(string kind,JsonObject data,string code="default"){var d=PaySettings.Create(c,tx,PayFixture.Maker,kind,"ninja",new(){["period"]="2026-10",["code"]=code,["data"]=data});return PaySettings.Approve(c,tx,PayFixture.Checker,kind,"ninja",d.S("id"));}
                    Approve("pay_terms",NinjaBilling.Seed());var eligibility=PayFixture.Eligibility();eligibility["rules"]![0]!["threshold"]=0;var rules=Approve("pay_eligibility",eligibility);var schemeData=PayFixture.Scheme("daily_commission",rules.S("id"));schemeData["accountingRole"]="principal";var scheme=Approve("pay_scheme",schemeData,"synthetic");
                    foreach(string rider in new[]{Positive,Negative})Store.Save(c,"pay_assignment",new(){["id"]="synthetic-r9-choice-"+rider,["rider"]=rider,["app"]="ninja",["period"]="2026-10",["status"]="approved",["schemeId"]=scheme.S("id"),["choiceApprovedAt"]="2026-09-29T00:00:00Z"},tx);
                    foreach(var(code,account) in new[]{("bonus","512122"),("correction","512101")}){ConfigLists.Upsert(c,tx,PayFixture.Maker,"pay_adjustment_types",new(){["code"]=code,["nameAr"]="Synthetic "+code,["values"]=new JsonObject{["account"]=account,["direction"]="both"}});ConfigLists.Approve(c,tx,PayFixture.Checker,"pay_adjustment_types",code);}return true;
                });
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();string root=projectRoot??Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
                var start=new ProcessStartInfo("dotnet"){WorkingDirectory=Path.Combine(root,"Payroll.Api"),RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};start.ArgumentList.Add(Path.Combine(root,"Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll"));start.Environment["ConnectionStrings__DefaultConnection"]=connection??"";start.Environment["PAYROLL_DATA_DIR"]=f.directory;start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";start.Environment["ASPNETCORE_URLS"]="http://127.0.0.1:"+port;start.Environment["Jobs__Enabled"]="false";start.Environment["Vouchers__Secret"]=SyntheticVoucherSettings.Key;
                f.child=Process.Start(start)!;f.output=f.child.StandardOutput.ReadToEndAsync();f.error=f.child.StandardError.ReadToEndAsync();HttpClient Client()=>new(new HttpClientHandler{CookieContainer=new CookieContainer()}){BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(30)};f.Maker=Client();f.Checker=Client();bool ready=false;
                for(int n=0;n<150;n++){if(f.child.HasExited)Assert.Fail("Synthetic API exited before readiness.");try{if((await f.Maker.GetAsync("/health")).IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(100);}Assert.IsTrue(ready);
                await f.Request(f.Maker,"/api/auth/login",new(){["username"]="synthetic-pay-maker",["password"]=password});await f.Request(f.Checker,"/api/auth/login",new(){["username"]="synthetic-pay-checker",["password"]=password});await f.Request(f.Maker,"/api/platforms/ninja/posting",new(){["enabled"]=true});return f;
            }
            catch{await f.DisposeAsync();throw;}
        }
        public async Task<JsonNode> Request(HttpClient client,string path,JsonObject? body=null,int expected=200)
        {using var response=body==null?await client.GetAsync(path):await client.PostAsJsonAsync(path,body);string json=await response.Content.ReadAsStringAsync();Assert.AreEqual(expected,(int)response.StatusCode,path+" "+json[..Math.Min(400,json.Length)]);return JsonNode.Parse(json)!;}
        public async ValueTask DisposeAsync()
        {
            Maker?.Dispose();Checker?.Dispose();if(child!=null){if(!child.HasExited)child.Kill(true);await child.WaitForExitAsync();child.Dispose();if(output!=null)await output;if(error!=null)await error;}
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();SqlConnection.ClearAllPools();if(admin!=null){if(database!=""){using var drop=admin.CreateCommand();drop.CommandText="ALTER DATABASE ["+database+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+database+"]";await drop.ExecuteNonQueryAsync();}await admin.DisposeAsync();}if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
}
