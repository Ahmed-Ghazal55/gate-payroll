using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-18: Jahez/Chefz live read + 12:00 AM display snapshot + Chefz money freeze + difference report.
/// Display snapshot ≠ money freeze. Freeze only after Omar settlement saved AND our invoice approved (M-0705).
/// ChefzCommission remains a check helper only — never a posting source (M-0675 / M-0704).</summary>
public static class ErpSettlementsApi
{
    public const string SnapshotKind="erp_settlement_snapshot",FreezeKind="chefz_money_freeze";
    static readonly HashSet<string> Apps=new(StringComparer.OrdinalIgnoreCase){"jahez","chefz"};

    public static JsonObject Live(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string app,JsonObject query)
    {
        AccessPolicy.Demand(actor,"pay.view");
        string platform=NormalizeApp(app);
        string companyId=FinancialCore.ResolveSponsor(query.Required("companyId")).Id;
        FinancialScopes.DemandSponsor(actor,companyId);
        string period=query.S("period");
        string half=query.S("half"); // 1 | 2 | all
        if(platform=="chefz")
        {
            var settlements=Store.List(c,"chefz_settlement",tx)
                .Where(r=>r.S("companyId")==companyId&&!r.B("sourceDeleted"))
                .Where(r=>period==""||InPeriod(r.S("periodFrom"),period))
                .Where(r=>HalfOk(r,half))
                .Select(ProjectChefzLive)
                .ToList();
            return new()
            {
                ["app"]="chefz",["companyId"]=companyId,["period"]=period,["half"]=half==""?"all":half,
                ["source"]="omar_live",["asOf"]=DateTimeOffset.UtcNow.ToString("O"),
                ["items"]=new JsonArray(settlements.Select(x=>(JsonNode)x).ToArray()),
                ["total"]=settlements.Count,
                ["allowedActions"]=LiveActions(actor,platform),
                ["noteAr"]="قراءة مباشرة للعرض؛ تجميد المال بعد حفظ تسوية عمر واعتماد فاتورتنا."
            };
        }
        // Jahez live: operations + movements + handovers already pulled into store kinds.
        var ops=Store.List(c,"erp_jahez_operation",tx).Where(r=>r.S("companyId")==companyId||AccountCompany(c,tx,r)==companyId)
            .Where(r=>period==""||InPeriod(r.S("executedAt"),period))
            .Select(r=>new JsonObject
            {
                ["id"]=r.S("id"),["erpRowId"]=r.S("erpSourceId",r.S("id")),["executedAt"]=r.S("executedAt"),
                ["net"]=J.Money(r.N("net")),["commission"]=J.Money(r.N("commission",r.N("calculatedCommission"))),
                ["fund"]=r.S("fund"),["purpose"]=r.S("purpose"),["status"]=r.S("status")
            }).ToList();
        return new()
        {
            ["app"]="jahez",["companyId"]=companyId,["period"]=period,
            ["source"]="omar_live",["asOf"]=DateTimeOffset.UtcNow.ToString("O"),
            ["items"]=new JsonArray(ops.Select(x=>(JsonNode)x).ToArray()),
            ["total"]=ops.Count,
            ["allowedActions"]=LiveActions(actor,platform),
            ["noteAr"]="قراءة مباشرة لعمليات جاهز؛ لقطة الساعة 12:00 ص للعرض فقط."
        };
    }

    public static JsonObject TakeDisplaySnapshot(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string app,JsonObject input)
    {
        AccessPolicy.Demand(actor,"pay.manage");
        string platform=NormalizeApp(app);
        var live=Live(c,tx,actor,platform,input);
        string companyId=live.S("companyId");
        string period=live.S("period",J.Period(DateTime.UtcNow.ToString("yyyy-MM")));
        string businessDate=input.S("businessDate",DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
        string id=$"snap:{platform}:{companyId}:{businessDate}:{Guid.NewGuid():N}";
        string payload=live.ToJsonString();
        byte[] hash=SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        var doc=new JsonObject
        {
            ["id"]=id,["kind"]=SnapshotKind,["app"]=platform,["companyId"]=companyId,["period"]=period,
            ["businessDate"]=businessDate,["purpose"]="display",["status"]="captured",
            ["waitingHandover"]=platform=="jahez"&&Store.List(c,"jahez_handover",tx).Any(h=>h.S("sponsorId")==companyId&&h.S("status")=="pending"),
            ["itemCount"]=live.N("total"),["payload"]=live.DeepClone(),
            ["sourceFingerprint"]=Convert.ToHexString(hash),
            ["capturedAt"]=DateTimeOffset.UtcNow.ToString("O"),["capturedBy"]=actor.Identity!.Name,
            ["moneyFrozen"]=false
        };
        if(doc.B("waitingHandover"))doc["status"]="waiting_handover";
        Store.Save(c,SnapshotKind,doc,tx);
        Store.Audit(c,tx,"erp.settlement_display_snapshot",id,actor.Identity!.Name!);
        doc["allowedActions"]=new JsonArray("view");
        return doc;
    }

    /// <summary>M-0705 money freeze: Omar settlement saved + our invoice approved. Writes ops_snapshot kinds for Chefz.</summary>
    public static JsonObject FreezeChefzMoney(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,"pay.approve");
        string companyId=FinancialCore.ResolveSponsor(input.Required("companyId")).Id;
        FinancialScopes.DemandSponsor(actor,companyId);
        string invoiceId=input.Required("invoiceId");
        var invoice=Store.Get(c,invoiceId,"platform_invoice",tx)??throw new ApiError("الفاتورة غير موجودة.",404);
        J.Need(invoice.S("companyId")==companyId,"الفاتورة خارج نطاق الشركة.");
        J.Need(string.Equals(PayPlatforms.For(invoice.S("platform")).App,"chefz",StringComparison.OrdinalIgnoreCase),"التجميد لفاتورة شيفز فقط.");
        J.Need(invoice.S("approvalStatus") is "approved" or "posted"||invoice.S("status") is "approved" or "posted",
            "تجميد مال شيفز يتطلب اعتماد فاتورتنا أولاً.",409);
        var settlements=Store.List(c,"chefz_settlement",tx)
            .Where(r=>r.S("companyId")==companyId&&!r.B("sourceDeleted")&&InPeriod(r.S("periodFrom"),invoice.S("period")))
            .ToList();
        J.Need(settlements.Count>0,"لا توجد تسويات عمر محفوظة لهذه الفترة.",409);

        SchemaOperationCapture.Ensure(c,tx);
        DateOnly month=DateOnly.ParseExact(invoice.S("period")+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);
        string monthLit=month.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        string capturer=actor.Identity!.Name!;
        try{capturer=SchemaApprovals.EnsureAuthUserId(c,tx,capturer);}catch(ApiError){/* keep username when auth.user bridge missing */}
        int frozen=0;
        foreach(var row in settlements)
        {
            string erpRaw=row.S("erpSourceId",row.S("id"));
            string erpRowId=AsGuidText(erpRaw);
            string payload=ProjectChefzFreezePayload(row).ToJsonString();
            byte[] fp=SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            // Append-only; skip if same erp_row_id + fingerprint already frozen this month.
            object? exists=SchemaSql.Scalar(c,tx,$"""
                SELECT id FROM {SchemaSql.Q(c,"sal","ops_snapshot")}
                WHERE kind=@k AND erp_row_id=@e AND period_month=@m AND source_fingerprint=@f
                """,("@k","chefz_settlement"),("@e",erpRowId),("@m",monthLit),("@f",fp));
            if(exists is not null and not DBNull)continue;
            string snapId=SchemaSql.GuidText(SchemaSql.NewId());
            string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"sal","ops_snapshot")}
                (id,period_month,kind,erp_row_id,scope_company_id,scope_app,captured_at,captured_by,source_fingerprint,payload,generation)
                VALUES(@id,@m,@k,@e,@c,'chefz',@at,@by,@f,@p,'new')
                """,
                ("@id",snapId),("@k","chefz_settlement"),("@m",monthLit),
                ("@c",companyId),("@e",erpRowId),("@f",fp),("@p",payload),("@by",capturer),("@at",now));
            frozen++;
        }
        var freeze=new JsonObject
        {
            ["id"]=$"freeze:chefz:{companyId}:{invoiceId}",["kind"]=FreezeKind,["app"]="chefz",
            ["companyId"]=companyId,["invoiceId"]=invoiceId,["period"]=invoice.S("period"),
            ["frozenCount"]=frozen,["settlementCount"]=settlements.Count,
            ["status"]="frozen",["frozenAt"]=DateTimeOffset.UtcNow.ToString("O"),["frozenBy"]=actor.Identity!.Name,
            ["allowedActions"]=new JsonArray("difference_report")
        };
        Store.Save(c,FreezeKind,freeze,tx);
        Store.Audit(c,tx,"chefz.money_frozen",freeze.S("id"),actor.Identity!.Name!);
        return freeze;
    }

    /// <summary>M-0706 difference report — deltas only; never replaces Omar amounts.</summary>
    public static JsonObject ChefzDifferenceReport(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,"pay.view");
        string companyId=FinancialCore.ResolveSponsor(input.Required("companyId")).Id;
        FinancialScopes.DemandSponsor(actor,companyId);
        string period=J.Period(input.Required("period"));
        var first=DateOnly.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);
        var rows=new JsonArray();
        int identityFail=0,walletFail=0,ordersFail=0,freelancerFail=0,fullTimeFail=0,dayCapFail=0,cityRateFail=0,mixedFail=0;
        foreach(var row in Store.List(c,"chefz_settlement",tx).Where(r=>r.S("companyId")==companyId&&!r.B("sourceDeleted")&&InPeriod(r.S("periodFrom"),period)))
        {
            var deltas=new JsonArray();
            decimal gross=J.Money(row.N("gross",row.N("grossEarnings")));
            decimal company=J.Money(row.N("companyAmount"));
            decimal rider=J.Money(row.N("riderAmount"));
            // Identity: RiderAmount = Gross - Company
            if(J.Money(rider-(gross-company))!=0){identityFail++;deltas.Add(Delta("identity",gross-company,rider));}
            string source=row.S("source","Orders");
            if(source.Equals("Wallet",StringComparison.OrdinalIgnoreCase))
            {
                decimal expected=J.Money(row.N("endWalletAmount")-row.N("startWalletAmount"));
                if(expected!=gross){walletFail++;deltas.Add(Delta("wallet_gross",expected,gross));}
                if(row.N("commissionRate")>0)
                {
                    decimal expCompany=J.Money(Math.Round(gross*row.N("commissionRate",0.15m),2,MidpointRounding.AwayFromZero));
                    if(expCompany!=company){freelancerFail++;deltas.Add(Delta("freelancer_wallet",expCompany,company));}
                }
            }
            else
            {
                decimal fees=row.N("deliveryFees"),vat=row.N("vatAmount",row.N("vat"));
                decimal expected=J.Money(fees-vat+row.N("tips")+row.N("compensation")+row.N("bonuses")-row.N("deductions"));
                if(expected!=0&&expected!=gross){ordersFail++;deltas.Add(Delta("orders_gross",expected,gross));}
                if(row.N("commissionRate")>0&&row.N("dailyFee")==0)
                {
                    decimal baseAmt=fees-vat;
                    decimal expCompany=J.Money(Math.Round(baseAmt*row.N("commissionRate",0.15m),2,MidpointRounding.AwayFromZero));
                    if(expCompany!=company){freelancerFail++;deltas.Add(Delta("freelancer_orders",expCompany,company));}
                }
            }
            int assignedDays=(int)row.N("assignedDays");
            decimal dailyFee=row.N("dailyFee",ChefzCommission.DailyFee);
            if(row.N("commissionRate")==0&&dailyFee>0)
            {
                decimal omarEngine=J.Money(assignedDays*dailyFee);
                if(omarEngine!=company){fullTimeFail++;deltas.Add(Delta("fulltime_omar_engine",omarEngine,company));}
                // §36 policy check (ours): min(days,15)*25 per half — report only
                decimal policy=ChefzCommission.FullTimeFeeForMonth(Enumerable.Range(0,Math.Max(0,assignedDays)).Select(i=>first.AddDays(i)),first.Year,first.Month);
                // Prefer charged window when present
                if(DateOnly.TryParseExact(row.S("chargedFrom").Length>=10?row.S("chargedFrom")[..10]:"", "yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var cf)
                   &&DateOnly.TryParseExact(row.S("chargedTo").Length>=10?row.S("chargedTo")[..10]:"", "yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var ct))
                {
                    var days=new List<DateOnly>();
                    for(var d=cf;d<=ct;d=d.AddDays(1))days.Add(d);
                    policy=ChefzCommission.FullTimeFeeForMonth(days,first.Year,first.Month);
                }
                if(policy!=company){dayCapFail++;deltas.Add(Delta("section36_day_cap",policy,company));}
            }
            if(row.N("fullTimeDays")>0&&row.N("freelancerCommissionBase")!=0)
            {
                decimal mixed=J.Money(Math.Round(row.N("freelancerCommissionBase")*0.15m,2,MidpointRounding.AwayFromZero)+row.N("fullTimeDays")*ChefzCommission.DailyFee);
                if(mixed!=company){mixedFail++;deltas.Add(Delta("mixed_type",mixed,company));}
            }
            // City daily rate vs hardcoded 25 (report only)
            decimal cityRate=CityDailyRate(c,tx,row);
            if(cityRate>0&&dailyFee>0&&cityRate!=dailyFee){cityRateFail++;deltas.Add(Delta("city_daily_rate",cityRate,dailyFee));}

            if(deltas.Count>0)
                rows.Add(new JsonObject
                {
                    ["erpRowId"]=row.S("erpSourceId",row.S("id")),["accountId"]=row.S("platformAccountId"),
                    ["riderId"]=row.S("riderId"),["source"]=source,["gross"]=gross,["companyAmount"]=company,["riderAmount"]=rider,
                    ["deltas"]=deltas
                });
        }
        return new()
        {
            ["app"]="chefz",["companyId"]=companyId,["period"]=period,
            ["summary"]=new JsonObject
            {
                ["rowsWithDeltas"]=rows.Count,
                ["identity"]=identityFail,["walletGross"]=walletFail,["ordersGross"]=ordersFail,
                ["freelancer"]=freelancerFail,["fullTimeOmar"]=fullTimeFail,["section36DayCap"]=dayCapFail,
                ["cityRate"]=cityRateFail,["mixed"]=mixedFail
            },
            ["items"]=rows,
            ["allowedActions"]=new JsonArray("export"),
            ["noteAr"]="تقرير فروق للفحص فقط؛ مبالغ عمر تبقى مصدر الترحيل."
        };
    }

    public static int CaptureMidnightDisplay(Store store)
    {
        // Hosted timer (no Hangfire): display snapshots for yesterday Asia/Riyadh for every sponsor that has live facts.
        var riyadh=TimeSpan.FromHours(3);
        var yesterday=DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(riyadh).DateTime).AddDays(-1);
        int n=0;
        store.Write((c,tx)=>
        {
            foreach(var company in FinancialCore.Sponsors)
            {
                foreach(string app in Apps)
                {
                    var actor=SystemActor();
                    try
                    {
                        TakeDisplaySnapshot(c,tx,actor,app,new(){["companyId"]=company.Id,["period"]=yesterday.ToString("yyyy-MM",CultureInfo.InvariantCulture),["businessDate"]=yesterday.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)});
                        n++;
                    }
                    catch(ApiError){/* no facts / scope — skip */}
                }
            }
            return n;
        },invalidateSummaries:false);
        return n;
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/sal/erp-settlements/{app}/live",(string app,string companyId,string? period,string? half,HttpContext ctx)=>
            store.Read(c=>Live(c,null,ctx.User,app,new(){["companyId"]=companyId,["period"]=period??"",["half"]=half??""})))
            .WithTags("Payroll").WithSummary("Live Jahez/Chefz settlements from Omar (display)");
        api.MapPost("/sal/erp-settlements/{app}/snapshot",(string app,JsonObject input,HttpContext ctx)=>
            store.Write((c,tx)=>TakeDisplaySnapshot(c,tx,ctx.User,app,input)))
            .WithTags("Payroll").WithSummary("Capture display snapshot (not money freeze)");
        api.MapPost("/sal/erp-settlements/chefz/freeze",(JsonObject input,HttpContext ctx)=>
            store.Write((c,tx)=>FreezeChefzMoney(c,tx,ctx.User,input)))
            .WithTags("Payroll").WithSummary("Freeze Chefz money after Omar settlement + invoice approve");
        api.MapGet("/sal/erp-settlements/chefz/difference-report",(string companyId,string period,HttpContext ctx)=>
            store.Read(c=>ChefzDifferenceReport(c,null,ctx.User,new(){["companyId"]=companyId,["period"]=period})))
            .WithTags("Payroll").WithSummary("Chefz difference report vs Omar (check only)");
        api.MapPost("/sal/erp-settlements/midnight-capture",(HttpContext ctx)=>
        {
            AccessPolicy.Demand(ctx.User,"admin.settings.manage");
            return new{ok=true,captured=CaptureMidnightDisplay(store)};
        }).WithTags("Administration").WithSummary("Manual trigger for midnight display snapshot (no Hangfire)");
    }

    static ClaimsPrincipal SystemActor()
    {
        var id=new System.Security.Claims.ClaimsIdentity("system");
        id.AddClaim(new("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name","system-midnight"));
        id.AddClaim(new("permission","pay.manage"));
        id.AddClaim(new(System.Security.Claims.ClaimTypes.Role,"SystemAdmin"));
        return new ClaimsPrincipal(id);
    }
    static string NormalizeApp(string app)
    {
        string a=app.Trim().ToLowerInvariant();
        J.Need(Apps.Contains(a),"التطبيق يجب أن يكون jahez أو chefz.");
        return a;
    }
    static JsonArray LiveActions(ClaimsPrincipal actor,string app)
    {
        var a=new JsonArray("refresh");
        if(AccessPolicy.Has(actor,"pay.manage")||AccessPolicy.Administrator(actor))a.Add("snapshot");
        if(app=="chefz"&&(AccessPolicy.Has(actor,"pay.approve")||AccessPolicy.Administrator(actor)))a.Add("freeze");
        if(app=="chefz")a.Add("difference_report");
        return a;
    }
    static JsonObject ProjectChefzLive(JsonObject row)=>new()
    {
        ["id"]=row.S("id"),["erpRowId"]=row.S("erpSourceId",row.S("id")),
        ["platformAccountId"]=row.S("platformAccountId"),["riderId"]=row.S("riderId"),
        ["accountType"]=row.S("accountType"),["periodFrom"]=row.S("periodFrom"),["periodTo"]=row.S("periodTo"),
        ["chargedFrom"]=row.S("chargedFrom"),["chargedTo"]=row.S("chargedTo"),
        ["assignedDays"]=row.N("assignedDays"),["source"]=row.S("source"),
        ["gross"]=J.Money(row.N("gross",row.N("grossEarnings"))),
        ["companyAmount"]=J.Money(row.N("companyAmount")),["riderAmount"]=J.Money(row.N("riderAmount")),
        ["commissionRate"]=row.N("commissionRate"),["dailyFee"]=row.N("dailyFee"),
        ["fullTimeDays"]=row.N("fullTimeDays"),["freelancerCommissionBase"]=row.N("freelancerCommissionBase"),
        ["fullTimeFeeAmount"]=row.N("fullTimeFeeAmount")
    };
    static JsonObject ProjectChefzFreezePayload(JsonObject row)
    {
        var o=ProjectChefzLive(row);
        foreach(string f in new[]{"deliveryFees","vatAmount","tips","compensation","bonuses","deductions","importId","sourceRows","startWalletAmount","endWalletAmount","riderClientAssignmentId","riderProfileId"})
            if(row[f]!=null)o[f]=row[f]!.DeepClone();
        return o;
    }
    static JsonObject Delta(string check,decimal expected,decimal actual)=>new(){["check"]=check,["expected"]=J.Money(expected),["actual"]=J.Money(actual),["delta"]=J.Money(actual-expected)};
    static bool InPeriod(string date,string period)
    {
        if(period==""||date.Length<7)return true;
        return date.StartsWith(period,StringComparison.Ordinal);
    }
    static bool HalfOk(JsonObject row,string half)
    {
        if(half is "" or "all")return true;
        if(!DateOnly.TryParseExact(row.S("periodFrom").Length>=10?row.S("periodFrom")[..10]:row.S("periodFrom"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var from))return true;
        return half=="1"?from.Day<=15:from.Day>=16;
    }
    static string AccountCompany(DbConnection c,DbTransaction? tx,JsonObject row)
    {
        string account=row.S("accountId",row.S("platformAccountId"));
        if(account=="")return "";
        return Store.Get(c,account,"platform_account",tx)?.S("companyId")??"";
    }
    static decimal CityDailyRate(DbConnection c,DbTransaction? tx,JsonObject row)
    {
        // Optional store kind from Omar PlatformDailyCommissionRates pull; 0 = unknown.
        string city=row.S("cityId",row.S("operatingCityId"));
        if(city=="")return 0m;
        var rate=Store.List(c,"platform_daily_commission_rate",tx)
            .Where(r=>r.S("program").Contains("chefz",StringComparison.OrdinalIgnoreCase)&&r.S("operatingCityId")==city)
            .OrderByDescending(r=>r.S("effectiveFrom"))
            .FirstOrDefault();
        return rate==null?0m:J.Money(rate.N("dailyRate"));
    }
    /// <summary>ops_snapshot.erp_row_id is GUID; hash non-GUID Omar keys to a stable id without inventing business meaning.</summary>
    static string AsGuidText(string raw)
    {
        if(Guid.TryParse(raw,out var g))return SchemaSql.GuidText(g);
        byte[] hash=SHA256.HashData(Encoding.UTF8.GetBytes("chefz:"+raw));
        Span<byte> bytes=stackalloc byte[16];
        hash.AsSpan(0,16).CopyTo(bytes);
        bytes[6]=(byte)((bytes[6]&0x0F)|0x50);bytes[8]=(byte)((bytes[8]&0x3F)|0x80);
        return SchemaSql.GuidText(new Guid(bytes));
    }
}

/// <summary>Hosted timer (NO Hangfire) for 12:00 AM Asia/Riyadh display snapshots.</summary>
public sealed class ErpSettlementSnapshotHostedService(Store store,ILogger<ErpSettlementSnapshotHostedService> log):BackgroundService
{
    DateOnly? _lastRun;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var riyadh=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3));
                var today=DateOnly.FromDateTime(riyadh.DateTime);
                if(riyadh.Hour==0&&_lastRun!=today)
                {
                    int n=ErpSettlementsApi.CaptureMidnightDisplay(store);
                    _lastRun=today;
                    log.LogInformation("ERP settlement display snapshots captured: {Count}",n);
                }
            }
            catch(Exception ex){log.LogWarning(ex,"ERP settlement midnight snapshot failed");}
            try{await Task.Delay(TimeSpan.FromMinutes(1),stoppingToken);}
            catch(OperationCanceledException){break;}
        }
    }
}
