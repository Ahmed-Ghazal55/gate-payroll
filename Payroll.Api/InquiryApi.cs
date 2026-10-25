using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
namespace Payroll;

// Both limits apply, so changing an identity cannot bypass the IP limit and a distributed attack cannot bypass the identity limit.
public sealed class InquiryLimits : IDisposable
{
    private readonly PartitionedRateLimiter<string> ip=Create();
    private readonly PartitionedRateLimiter<string> identity=Create();
    private static PartitionedRateLimiter<string> Create()=>PartitionedRateLimiter.Create<string,string>(key=>RateLimitPartition.GetSlidingWindowLimiter(key,_=>new SlidingWindowRateLimiterOptions{PermitLimit=5,Window=TimeSpan.FromMinutes(1),SegmentsPerWindow=6,QueueLimit=2,QueueProcessingOrder=QueueProcessingOrder.OldestFirst}));
    public async Task<bool> Allowed(string address,string iqama,CancellationToken cancellation)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(TimeSpan.FromMilliseconds(250));
        try {
            using var first=await ip.AcquireAsync(address,1,deadline.Token);if(!first.IsAcquired)return false;
            using var second=await identity.AcquireAsync(iqama,1,deadline.Token);return second.IsAcquired;
        } catch(OperationCanceledException) when(!cancellation.IsCancellationRequested) {return false;}
    }
    public void Dispose(){ip.Dispose();identity.Dispose();}
}
public static class InquiryApi
{
    private static string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    public static bool ConsumeAccess(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,string iqama,string phone,string code,string scope)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(iqama,"^[12][0-9]{9}$")||code.Length!=64||!System.Text.RegularExpressions.Regex.IsMatch(phone,"^[0-9]{4}$"))return false;
        var access=Store.Get(c,"inquiry:"+Hash(code),"inquiry_code",tx);var rider=Store.Get(c,iqama,"rider",tx);
        if(access==null||rider==null||access.B("used")||access.S("iqama")!=iqama||access.S("scope")!=scope||!DateTimeOffset.TryParse(access.S("expiresAt"),out var expiry)||expiry<=DateTimeOffset.UtcNow||rider.S("phone").Length<4||!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(rider.S("phone")[^4..]),Encoding.UTF8.GetBytes(phone)))return false;
        access["used"]=true;Store.Save(c,"inquiry_code",access,tx);return true;
    }
    public static void Map(WebApplication app,RouteGroupBuilder api,Store store)
    {
        api.MapPost("/inquiry-access",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>{
            string iqama=DirectoryProfiles.Iqama(input.Required("iqama")),scope=input.Required("scope");J.Need(scope is "payslip" or "debt" or "mobile","نطاق غير صالح.");
            var rider=Store.Get(c,iqama,"rider",tx)??throw new ApiError("المندوب غير موجود.",404);J.Need(rider.S("phone").Length>=8,"يلزم جوال مسجل ومراجع.");J.Need(input.B("identityReviewed"),"راجع هوية صاحب الطلب قبل تسليمه الرمز.");
            string code=CampaignLinks.NewToken();string expiry=DateTimeOffset.UtcNow.AddMinutes(10).ToString("O");
            Store.Save(c,"inquiry_code",new(){["id"]="inquiry:"+Hash(code),["iqama"]=iqama,["scope"]=scope,["expiresAt"]=expiry,["used"]=false},tx);Store.Audit(c,tx,"inquiry.access_issued",iqama,ctx.User.Identity!.Name!);
            return new{iqama,scope,code,expiresAt=expiry,url=CampaignLinks.Base(app.Configuration,ctx)+(scope=="mobile"?"/rider-app#access_code="+code+"&iqama="+iqama:"/inquiry")};
        },false));
        foreach(string scope in new[]{"payslip","debt"})
        {
            string currentScope=scope;
            app.MapPost("/api/public/inquiry/"+scope,async Task<IResult> (JsonObject input,HttpContext ctx,IMemoryCache cache,InquiryLimits limits)=>{
                string iqama=input.S("iqama"),code=input.S("code"),phone=input.S("phoneLast4");
                if(!await limits.Allowed(ctx.Connection.RemoteIpAddress?.ToString()??"unknown",Hash(iqama),ctx.RequestAborted))
                {ctx.Response.Headers.RetryAfter="60";return Results.Json(new{error="حاول مرة أخرى بعد قليل.",code="rate_limited"},statusCode:429);}
                const string denied="تعذر التحقق من بيانات الدخول أو انتهت صلاحية الرمز.";
                if(!System.Text.RegularExpressions.Regex.IsMatch(iqama,"^[0-9]{10}$")||code.Length!=64||phone.Length!=4) return Results.Json(new{error=denied,code="verification_failed"},statusCode:401);
                bool valid=store.Write((c,tx)=>{
                    var access=Store.Get(c,"inquiry:"+Hash(code),"inquiry_code",tx);var rider=Store.Get(c,iqama,"rider",tx);
                    if(access==null||rider==null||access.B("used")||access.S("iqama")!=iqama||access.S("scope")!=currentScope||!DateTimeOffset.TryParse(access.S("expiresAt"),out var expires)||expires<=DateTimeOffset.UtcNow||rider.S("phone").Length<4||!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(rider.S("phone")[^4..]),Encoding.UTF8.GetBytes(phone)))return false;
                    access["used"]=true;Store.Save(c,"inquiry_code",access,tx);return true;
                },false);
                if(!valid)return Results.Json(new{error=denied,code="verification_failed"},statusCode:401);
                string period=currentScope=="payslip"?J.Period(input.Required("period")):"all";
                string key=$"inquiry:{currentScope}:{iqama}:{period}:{store.Revision}";
                string payload=cache.GetOrCreate(key,entry=>{
                    entry.AbsoluteExpirationRelativeToNow=TimeSpan.FromMinutes(15);entry.Size=1;
                    return store.Read(c=>{
                        if(currentScope=="payslip")
                        {var run=Store.List(c,"run").FirstOrDefault(x=>x.S("iqama")==iqama&&x.S("period")==period);return JsonSerializer.Serialize(new{iqama,period,available=run!=null,approvedAt=run?.S("approvedAt"),result=run?["result"]?.DeepClone()});}
                        var rows=Store.List(c,"debt").Where(x=>x.S("iqama")==iqama).Select(x=>new{category=x.S("category"),totalAmount=x.N("totalAmount"),remainingBalance=x.N("remainingBalance"),monthlyInstallment=x.N("monthlyInstallment"),status=x.S("status")}).ToList();return JsonSerializer.Serialize(new{iqama,debts=rows,totalRemaining=rows.Sum(x=>x.remainingBalance)});
                    });
                })!;
                ctx.Response.Headers.CacheControl="private, max-age=300";ctx.Response.Headers.ETag="\""+Hash(payload)+"\"";
                return Results.Text(payload,"application/json");
            });
        }
        app.MapGet("/inquiry",()=>Results.File(Path.Combine(AppContext.BaseDirectory,"wwwroot","rider-form.html"),"text/html; charset=utf-8"));
    }
}
