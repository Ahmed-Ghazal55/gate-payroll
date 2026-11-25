using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>TRIAL11 / TRIAL11-decisions: expense, fuel_topup, prepaid_topup, mada_topup —
/// document approve stays permission-based; VOUCHER payout needs a second person.
/// Prove one-person vs two-person over live payment HTTP routes. grok-TRIAL10: store
/// adjustment createdBy so Independent cannot skip the adjuster.</summary>
[TestClass]
public sealed class Trial11FixesTests
{
    sealed class BodyDetection:IHttpRequestBodyDetectionFeature{public bool CanHaveBody=>true;}

    static async Task<(int Status,JsonObject Body)> Http(WebApplication app,string method,string path,ClaimsPrincipal actor,JsonObject? body=null)
    {
        var matches=((IEndpointRouteBuilder)app).DataSources.SelectMany(s=>s.Endpoints).OfType<RouteEndpoint>()
            .Where(e=>e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method))
            .Select(e=>{var values=new RouteValueDictionary();bool match=new TemplateMatcher(TemplateParser.Parse(e.RoutePattern.RawText!),new()).TryMatch(path,values);return(Endpoint:e,Values:values,Match:match);})
            .Where(x=>x.Match).ToList();
        Assert.AreEqual(1,matches.Count,"Exactly one live endpoint must match "+method+" "+path);
        using var scope=app.Services.CreateScope();
        var ctx=new DefaultHttpContext{RequestServices=scope.ServiceProvider,User=actor};
        ctx.Request.Method=method;ctx.Request.Path=path;ctx.Request.RouteValues=matches[0].Values;ctx.SetEndpoint(matches[0].Endpoint);
        var bytes=Encoding.UTF8.GetBytes(body?.ToJsonString()??"{}");
        ctx.Request.Body=new MemoryStream(bytes);ctx.Request.ContentLength=bytes.Length;ctx.Request.ContentType="application/json";
        ctx.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection());ctx.Response.Body=new MemoryStream();
        try{await matches[0].Endpoint.RequestDelegate!(ctx);}catch(ApiError error){ctx.Response.StatusCode=error.Status;}
        ctx.Response.Body.Position=0;string text=await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        return(ctx.Response.StatusCode,text==""?new():JsonNode.Parse(text)!.AsObject());
    }

    /// <summary>TRIAL11 binding (b): for each type, one person must not reach payout; two people must.</summary>
    [TestMethod]
    [DataRow(false,"expense")]
    [DataRow(false,"fuel_topup")]
    [DataRow(false,"prepaid_topup")]
    [DataRow(false,"mada_topup")]
    [DataRow(true,"expense")]
    [DataRow(true,"fuel_topup")]
    [DataRow(true,"prepaid_topup")]
    [DataRow(true,"mada_topup")]
    public async Task VoucherPayout_FourTypes_OnePersonBlocked_TwoPersonPays_ViaLiveHttp(bool sql,string type)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions{ContentRootPath=f.Store.DirectoryPath,EnvironmentName="Development"});
        await using var app=builder.Build();
        var api=app.MapGroup("/api");
        api.AddEndpointFilter(async (context,next)=>AccessPolicy.Allowed(context.HttpContext.User,context.HttpContext.Request.Method,context.HttpContext.Request.Path)?await next(context):Results.StatusCode(403));
        PaymentsApi.Map(api,f.Store);
        ApprovalsArea.Map(api,f.Store);

        string docId=await CreateApprovedDocument(app,f,type);
        // Document may be approved by one person for these types (permission-based) — already done.
        // VOUCHER: reserve opens dual; pay without a second person must not payout.
        var reserved=await Http(app,"POST","/api/payments/documents/"+docId+"/payment-requests",f.Maker,new()
        {
            ["idempotencyKey"]="t11-"+type+"-"+Guid.NewGuid().ToString("N"),
            ["amount"]=50,["bankAccountCode"]="111101",["paidOn"]="2026-11-20",["reference"]="SYN-T11-"+type
        });
        Assert.AreEqual(200,reserved.Status,type+" reserve: "+reserved.Body.ToJsonString());
        string voucherId=reserved.Body.S("id");
        string payPath="/api/payments/vouchers/"+voucherId+"/pay";
        Assert.AreEqual(403,(await Http(app,"POST",payPath,f.Maker)).Status,
            type+": one person must not pay the voucher before an independent dual decision");
        // Maker cannot unlock payout alone: either decide is refused, or decide is recorded as self and Pay still 403.
        string approvalId=reserved.Body.S("approvalRequestId");
        var selfDecide=await Http(app,"POST","/api/approvals/"+approvalId+"/decide",f.Maker,new(){["decision"]="approve",["note"]="self"});
        if(selfDecide.Status==200)
            Assert.AreEqual(403,(await Http(app,"POST",payPath,f.Maker)).Status,
                type+": self-approved dual must not unlock voucher payout");
        else
            Assert.AreEqual(403,selfDecide.Status,type+": maker self-decide must be refused or leave Pay blocked");
        // Independent checker on a clean dual → payout succeeds.
        if(selfDecide.Status==200)
        {
            await Http(app,"POST","/api/payments/vouchers/"+voucherId+"/cancel",f.Maker);
            var reserved2=await Http(app,"POST","/api/payments/documents/"+docId+"/payment-requests",f.Maker,new()
            {
                ["idempotencyKey"]="t11b-"+type+"-"+Guid.NewGuid().ToString("N"),
                ["amount"]=50,["bankAccountCode"]="111101",["paidOn"]="2026-11-20",["reference"]="SYN-T11B-"+type
            });
            Assert.AreEqual(200,reserved2.Status,type+" re-reserve: "+reserved2.Body.ToJsonString());
            voucherId=reserved2.Body.S("id");payPath="/api/payments/vouchers/"+voucherId+"/pay";approvalId=reserved2.Body.S("approvalRequestId");
            Assert.AreEqual(403,(await Http(app,"POST",payPath,f.Maker)).Status);
        }
        var decide=await Http(app,"POST","/api/approvals/"+approvalId+"/decide",f.Checker,new(){["decision"]="approve",["note"]="independent checker"});
        Assert.AreEqual(200,decide.Status,type+" checker decide: "+decide.Body.ToJsonString());
        var paid=await Http(app,"POST",payPath,f.Maker);
        Assert.AreEqual(200,paid.Status,type+" two-person pay: "+paid.Body.ToJsonString());
        Assert.AreEqual(2m,paid.Body.N("status"),type+": voucher status must be paid");
        Assert.AreEqual(1L,f.Scalar("SELECT COUNT(*) FROM fin_journal WHERE source_type='payment_document' AND purpose='payment_out'"),
            type+": payout must post exactly one payment_out journal");
    }

    static async Task<string> CreateApprovedDocument(WebApplication app,PaymentsEngineTests.Fixture f,string type)
    {
        ClaimsPrincipal maker=f.Maker,checker=f.Checker;
        if(type=="expense")
        {
            var created=await Http(app,"POST","/api/payments/documents",maker,f.Input("expense",50m));
            Assert.AreEqual(200,created.Status,created.Body.ToJsonString());
            string path="/api/payments/documents/"+created.Body.S("id");
            // Binding: document approve stays permission-based — maker alone may approve.
            Assert.AreEqual(200,(await Http(app,"POST",path+"/submit",maker)).Status);
            Assert.AreEqual(200,(await Http(app,"POST",path+"/approve",maker,new())).Status,
                "expense document approve must stay one-person (permission-based)");
            return created.Body.S("id");
        }
        // mada/topups reads the fuel_wallet table; prepaid/fuel fund routes use their own tables.
        string tableKind=type=="prepaid_topup"?"prepaid":"fuel";
        string account=type=="mada_topup"?"112404":type=="fuel_topup"?"112401":"112402";
        string walletPath=tableKind=="fuel"?"/api/payments/fuel-wallets":"/api/payments/prepaid-wallets";
        var wallet=await Http(app,"POST",walletPath,maker,new()
        {
            ["companyId"]=f.Company,["provider"]="مزود تجريبي",["accountCode"]=account,
            ["kind"]=type=="mada_topup"?"mada":tableKind
        });
        Assert.AreEqual(200,wallet.Status,type+" wallet: "+wallet.Body.ToJsonString());
        string fundPath=type=="mada_topup"
            ?"/api/payments/mada/topups"
            :tableKind=="fuel"
                ?"/api/payments/fuel-wallets/"+wallet.Body.S("id")+"/fund"
                :"/api/payments/prepaid-wallets/"+wallet.Body.S("id")+"/movements";
        var fundBody=new JsonObject
        {
            ["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["type"]=type,
            ["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",
            ["reference"]="SYN-T11-"+type,["amount"]=50m
        };
        if(type=="mada_topup")fundBody["walletId"]=wallet.Body.S("id");
        var funded=await Http(app,"POST",fundPath,maker,fundBody);
        Assert.AreEqual(200,funded.Status,type+" fund: "+funded.Body.ToJsonString());
        Assert.AreEqual(type,funded.Body.S("type"));
        string docPath="/api/payments/documents/"+funded.Body.S("id");
        Assert.AreEqual(200,(await Http(app,"POST",docPath+"/submit",maker)).Status);
        Assert.AreEqual(200,(await Http(app,"POST",docPath+"/approve",maker,new())).Status,
            type+" document approve must stay one-person (permission-based)");
        return funded.Body.S("id");
    }

    /// <summary>grok-TRIAL10 open: POST /adjustments must stamp createdBy so Independent blocks the adjuster.</summary>
    [TestMethod]
    public async Task AdjustmentCreatedBy_Stored_BlocksAdjusterApprove_ViaHttp()
    {
        string directory=Path.Combine(Path.GetTempPath(),"gate-t11-adj-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        System.Diagnostics.Process? child=null;Task<string>? output=null,error=null;
        try
        {
            var store=new Store(directory);
            string password=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            const string iq="2000000111",period="2026-10",preparer="t11-work-maker",adjuster="t11-adj-maker",checker="t11-adj-checker";
            store.Write((c,tx)=>
            {
                foreach(var name in new[]{preparer,adjuster,checker})
                    Store.Save(c,"user",UserAccess.Create(name,password,"Admin"),tx);
                var policy=new JsonObject
                {
                    ["id"]=Guid.NewGuid().ToString("N"),["name"]="عمولة تجريبية",["kind"]="commission",
                    ["platform"]="all",["effectiveFrom"]="2026-10",["status"]="approved",
                    ["config"]=new JsonObject{["companyFee"]=0},["createdBy"]=preparer,
                    ["createdAt"]=DateTimeOffset.UtcNow.ToString("O")
                };
                Store.Save(c,"policy",policy,tx);
                Store.Save(c,"rider",new()
                {
                    ["id"]=iq,["iqama"]=iq,["name"]="مندوب تجريبي",["sponsorId"]=FinancialCore.Sponsors[0].Id,
                    ["financeSponsorId"]=FinancialCore.Sponsors[0].Id,["isDeliveryRider"]=true,["status"]="Active",
                    ["policyId"]=policy.S("id"),["address"]="جدة"
                },tx);
                Store.Save(c,"work",new()
                {
                    ["id"]="t11-work-1",["sourceRef"]="t11-row-1",["iqama"]=iq,["name"]="مندوب تجريبي",
                    ["platform"]="Keeta",["accountId"]="1234567890123456",["period"]=period,
                    ["from"]="2026-10-01",["to"]="2026-10-31",["orders"]=600,["platformRevenue"]=3000,
                    ["riderPay"]=1500,["isValid"]=false,["company"]="Synthetic",["city"]="Test",
                    ["datesConfirmed"]=true,["createdBy"]=preparer
                },tx);
                return true;
            });
            var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
            var start=new System.Diagnostics.ProcessStartInfo("dotnet")
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
            child=System.Diagnostics.Process.Start(start)!;output=child.StandardOutput.ReadToEndAsync();error=child.StandardError.ReadToEndAsync();
            HttpClient Client()=>new(new HttpClientHandler{CookieContainer=new CookieContainer()}){BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(30)};
            using var adjClient=Client();using var checkClient=Client();
            bool ready=false;
            for(int n=0;n<150;n++)
            {
                if(child.HasExited)Assert.Fail("API exited before readiness.");
                try{if((await adjClient.GetAsync("/health")).IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}
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
            await Request(adjClient,"/api/auth/login",new(){["username"]=adjuster,["password"]=password});
            await Request(checkClient,"/api/auth/login",new(){["username"]=checker,["password"]=password});
            var adj=(await Request(adjClient,"/api/adjustments",new()
            {
                ["iqama"]=iq,["period"]=period,["amount"]=10,["category"]="bonus",
                ["description"]="إضافة تجريبية",["sourceRef"]="t11-adj-1"
            })).AsObject();
            Assert.AreEqual(adjuster,adj.S("createdBy"),"POST /adjustments must store the caller as createdBy");
            var preview=(await Request(adjClient,"/api/payroll/preview?period="+period)).AsArray();
            var row=preview.OfType<JsonObject>().First(p=>p.S("iqama")==iq);
            Assert.AreEqual(0,row["blockers"]!.AsArray().Count,row["blockers"]!.ToJsonString());
            string fingerprint=row.S("fingerprint");
            // Work preparer is different; without stored adjuster createdBy the adjuster would pass Independent.
            await Request(adjClient,"/api/payroll/approve",new()
            {
                ["iqama"]=iq,["period"]=period,["fingerprint"]=fingerprint,
                ["sponsorId"]=FinancialCore.Sponsors[0].Id
            },403);
            await Request(checkClient,"/api/payroll/approve",new()
            {
                ["iqama"]=iq,["period"]=period,["fingerprint"]=fingerprint,
                ["sponsorId"]=FinancialCore.Sponsors[0].Id
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
}
