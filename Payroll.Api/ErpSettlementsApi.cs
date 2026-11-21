using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.SqlClient;
namespace Payroll;

/// <summary>U-W2-18: Jahez/Chefz live read + 12:00 AM display snapshot + Chefz money freeze + difference report.
/// Display snapshot ≠ money freeze. Freeze only after Omar settlement saved AND our invoice approved (M-0705).
/// Live = Omar readers only (never store kinds). ChefzCommission is check-only (M-0675 / M-0704).</summary>
public static class ErpSettlementsApi
{
    public const string SnapshotKind="erp_settlement_snapshot",FreezeKind="chefz_money_freeze";
    static readonly HashSet<string> Apps=new(StringComparer.OrdinalIgnoreCase){"jahez","chefz"};
    /// <summary>Test seam for Omar live batches. Production leaves null and opens ErpReadonly.</summary>
    public static Func<DbConnection,DbTransaction?,string,string,string,string,LiveBatch>? LiveBatchProvider;
    public sealed record LiveBatch(IReadOnlyList<JsonObject> Items,DateTimeOffset SourceAsOf,string Source);

    public static JsonObject Live(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string app,JsonObject query)
    {
        AccessPolicy.Demand(actor,"pay.view");
        string platform=NormalizeApp(app);
        string companyId=FinancialCore.ResolveSponsor(query.Required("companyId")).Id;
        FinancialScopes.DemandSponsor(actor,companyId);
        DemandPlatform(actor,platform);
        string period=query.S("period");
        string half=query.S("half");
        var batch=ReadLiveBatch(c,tx,platform,companyId,period,half);
        var items=batch.Items.Select(r=>platform=="chefz"?ProjectChefzLive(r):ProjectJahezLive(r)).ToList();
        return new()
        {
            ["app"]=platform,["companyId"]=companyId,["period"]=period,["half"]=half==""?"all":half,
            ["source"]=batch.Source,["asOf"]=batch.SourceAsOf.ToString("O"),
            ["items"]=new JsonArray(items.Select(x=>(JsonNode)x).ToArray()),
            ["total"]=items.Count,
            ["allowedActions"]=LiveActions(actor,platform),
            ["noteAr"]=platform=="chefz"
                ?"قراءة مباشرة للعرض؛ تجميد المال بعد حفظ تسوية عمر واعتماد فاتورتنا."
                :"قراءة مباشرة لعمليات جاهز؛ لقطة الساعة 12:00 ص للعرض فقط."
        };
    }

    public static JsonObject TakeDisplaySnapshot(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string app,JsonObject input)
    {
        AccessPolicy.Demand(actor,"pay.manage");
        string platform=NormalizeApp(app);
        var live=Live(c,tx,actor,platform,input);
        string companyId=live.S("companyId");
        DemandPlatform(actor,platform);
        string period=live.S("period",J.Period(DateTime.UtcNow.ToString("yyyy-MM")));
        string businessDate=input.S("businessDate",DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
        string id=$"snap:{platform}:{companyId}:{businessDate}:{Guid.NewGuid():N}";
        string payload=live.ToJsonString();
        byte[] hash=SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        var doc=new JsonObject
        {
            ["id"]=id,["kind"]=SnapshotKind,["app"]=platform,["companyId"]=companyId,["period"]=period,
            ["businessDate"]=businessDate,["purpose"]="display",["status"]="captured",
            ["sourceAsOf"]=live.S("asOf"),["liveSource"]=live.S("source"),
            ["waitingHandover"]=platform=="jahez"&&HasPendingOmarHandover(c,tx,companyId),
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

    /// <summary>M-0705: seal ONE approved invoice + ONE Omar settlement; links + manifest; no re-freeze.</summary>
    public static JsonObject FreezeChefzMoney(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,"pay.approve");
        string companyId=FinancialCore.ResolveSponsor(input.Required("companyId")).Id;
        FinancialScopes.DemandSponsor(actor,companyId);
        DemandPlatform(actor,"chefz");
        string invoiceId=input.Required("invoiceId");
        string settlementId=input.Required("settlementId");
        var invoice=Store.Get(c,invoiceId,"platform_invoice",tx)??throw new ApiError("الفاتورة غير موجودة.",404);
        J.Need(invoice.S("companyId")==companyId,"الفاتورة خارج نطاق الشركة.");
        J.Need(string.Equals(PayPlatforms.For(invoice.S("platform")).App,"chefz",StringComparison.OrdinalIgnoreCase),"التجميد لفاتورة شيفز فقط.");
        J.Need(invoice.S("approvalStatus") is "approved" or "posted"||invoice.S("status") is "approved" or "posted",
            "تجميد مال شيفز يتطلب اعتماد فاتورتنا أولاً.",409);
        var prior=Store.Get(c,$"freeze:chefz:{companyId}:{invoiceId}",FreezeKind,tx);
        J.Need(prior is null||prior.S("status")!="frozen","تجميد مال شيفز لهذه الفاتورة موجود؛ التصحيح عبر قيد عكسي لاحق.",409);

        var settlement=Store.Get(c,settlementId,"chefz_settlement",tx)
            ??Store.List(c,"chefz_settlement",tx).FirstOrDefault(r=>r.S("erpSourceId")==settlementId||r.S("id")==settlementId)
            ??throw new ApiError("تسوية عمر غير موجودة.",404);
        J.Need(settlement.S("companyId")==companyId&&!settlement.B("sourceDeleted"),"التسوية خارج نطاق الشركة.",409);
        J.Need(InPeriod(settlement.S("periodFrom"),invoice.S("period")),"التسوية خارج فترة الفاتورة.",409);
        // Platform account ids are Omar ERP ids, not CoA vault codes — company+platform scope already enforced.

        SchemaOperationCapture.Ensure(c,tx);
        DateOnly month=DateOnly.ParseExact(invoice.S("period")+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);
        string monthLit=month.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        string capturer=actor.Identity!.Name!;
        try{capturer=SchemaApprovals.EnsureAuthUserId(c,tx,capturer);}catch(ApiError){/* keep username */}
        string erpRaw=settlement.S("erpSourceId",settlement.S("id"));
        string erpRowId=AsGuidText(erpRaw);
        string payload=ProjectChefzFreezePayload(settlement).ToJsonString();
        byte[] fp=SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        string snapId=SchemaSql.GuidText(SchemaSql.NewId());
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"sal","ops_snapshot")}
            (id,period_month,kind,erp_row_id,scope_company_id,scope_app,captured_at,captured_by,source_fingerprint,payload,generation)
            VALUES(@id,@m,@k,@e,@c,'chefz',@at,@by,@f,@p,'new')
            """,
            ("@id",snapId),("@k","chefz_settlement"),("@m",monthLit),
            ("@c",companyId),("@e",erpRowId),("@f",fp),("@p",payload),("@by",capturer),("@at",now));
        string linkId=SchemaSql.GuidText(SchemaSql.NewId());
        string opId=AsGuidText(invoiceId);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","operation_snapshot_link")}
            (id,operation_type,operation_id,snapshot_id,snapshot_kind,erp_row_id,source_fingerprint,period_month)
            VALUES(@id,'chefz_settlement',@op,@snap,'chefz_settlement',@e,@f,@m)
            """,("@id",linkId),("@op",opId),("@snap",snapId),("@e",erpRowId),("@f",fp),("@m",monthLit));
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","operation_capture_manifest")}
            (id,operation_type,operation_id,required_kind,status,empty_reason,snapshot_link_id,period_month)
            VALUES(@id,'chefz_settlement',@op,'chefz_settlement',1,NULL,@link,@m)
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@op",opId),("@link",linkId),("@m",monthLit));

        var freeze=new JsonObject
        {
            ["id"]=$"freeze:chefz:{companyId}:{invoiceId}",["kind"]=FreezeKind,["app"]="chefz",
            ["companyId"]=companyId,["invoiceId"]=invoiceId,["settlementId"]=settlement.S("id"),
            ["settlementErpRowId"]=erpRaw,["period"]=invoice.S("period"),
            ["frozenCount"]=1,["settlementCount"]=1,["snapshotId"]=snapId,["linkId"]=linkId,
            ["status"]="frozen",["frozenAt"]=DateTimeOffset.UtcNow.ToString("O"),["frozenBy"]=actor.Identity!.Name,
            ["allowedActions"]=new JsonArray("difference_report")
        };
        Store.Save(c,FreezeKind,freeze,tx);
        Store.Audit(c,tx,"chefz.money_frozen",freeze.S("id"),actor.Identity!.Name!);
        return freeze;
    }

    /// <summary>M-0706 difference report — deltas only; never replaces Omar amounts.
    /// Mixed rate mirrors Omar FullTimeFeeAmount (ChefzOrderSettlementRules.cs:62-63 / ChefzAccountTypeTimeline.cs:52-54).</summary>
    public static JsonObject ChefzDifferenceReport(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,"pay.view");
        string companyId=FinancialCore.ResolveSponsor(input.Required("companyId")).Id;
        FinancialScopes.DemandSponsor(actor,companyId);
        DemandPlatform(actor,"chefz");
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
                if(row.N("commissionRate")>0&&row.N("dailyFee")==0&&row.N("fullTimeDays")==0)
                {
                    decimal baseAmt=fees-vat;
                    decimal expCompany=J.Money(Math.Round(baseAmt*row.N("commissionRate",0.15m),2,MidpointRounding.AwayFromZero));
                    if(expCompany!=company){freelancerFail++;deltas.Add(Delta("freelancer_orders",expCompany,company));}
                }
            }
            int assignedDays=(int)row.N("assignedDays");
            decimal dailyFee=row.N("dailyFee",ChefzCommission.DailyFee);
            if(row.N("commissionRate")==0&&dailyFee>0&&row.N("fullTimeDays")==0)
            {
                decimal omarEngine=J.Money(assignedDays*dailyFee);
                if(omarEngine!=company){fullTimeFail++;deltas.Add(Delta("fulltime_omar_engine",omarEngine,company));}
                decimal policy=ChefzCommission.FullTimeFeeForMonth(Enumerable.Range(0,Math.Max(0,assignedDays)).Select(i=>first.AddDays(i)),first.Year,first.Month);
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
                // Omar: Money(base*0.15) + (FullTimeFeeAmount ?? fullTimeDays*dailyFee)
                // cite: Logistic-ERP/.../ChefzOrderSettlementRules.cs:62-63 and ChefzAccountTypeTimeline.cs:52-54
                decimal fullTimeFee=row.N("fullTimeFeeAmount");
                if(fullTimeFee==0)fullTimeFee=row.N("fullTimeDays")*row.N("dailyFee",ChefzCommission.DailyFee);
                decimal mixed=J.Money(Math.Round(row.N("freelancerCommissionBase")*0.15m,2,MidpointRounding.AwayFromZero)+fullTimeFee);
                if(mixed!=company){mixedFail++;deltas.Add(Delta("mixed_type",mixed,company));}
            }
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
                    catch(ApiError){/* no facts / scope / live — skip */}
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

    static LiveBatch ReadLiveBatch(DbConnection c,DbTransaction? tx,string platform,string companyId,string period,string half)
    {
        if(LiveBatchProvider!=null)
        {
            var batch=LiveBatchProvider(c,tx,platform,companyId,period,half);
            var filtered=platform=="chefz"
                ?batch.Items.Where(r=>period==""||InPeriod(r.S("periodFrom"),period)).Where(r=>HalfOk(r,half)).ToList()
                :batch.Items.Where(r=>period==""||InPeriod(r.S("executedAt"),period)).ToList();
            return batch with{Items=filtered};
        }
        string cs=Environment.GetEnvironmentVariable("ConnectionStrings__ErpReadonly")
            ??Environment.GetEnvironmentVariable("ERP_READONLY_DB")??"";
        if(string.IsNullOrWhiteSpace(cs))
            throw new ApiError("اتصال القراءة المباشرة من نظام عمر غير مضبوط.",503);
        using var erp=Connections.Source(cs);
        erp.Open();
        SourceReader.VerifyReadOnly(erp);
        DateTimeOffset asOf=DateTimeOffset.UtcNow;
        // Live kinds in Payroll.Sync (Omar readers): chefz_settlement; Jahez day orders (no erp_jahez_operation live kind).
        string kind=platform=="chefz"?"chefz_settlement":"jahez_daily_order";
        if(!LiveOperationsQuery.HasKind(kind))
            throw new ApiError(platform=="chefz"?"قراءة تسويات شيفز المباشرة غير متاحة.":"قراءة عمليات جاهز المباشرة غير متاحة.",503);
        var sponsor=FinancialCore.FindSponsor(companyId);
        var numbers=sponsor?.UnifiedNumber is {Length:>0} n?[n]:Array.Empty<string>();
        var scope=new LiveOperationsQuery.Scope(false,numbers,[],[],[],null,null,PlatformRestricted:false);
        var from=period.Length>=7?DateOnly.ParseExact(period.Length>=10?period[..10]:period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture):DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);
        var to=new DateOnly(from.Year,from.Month,DateTime.DaysInMonth(from.Year,from.Month));
        var items=new List<JsonObject>();
        int page=1,size=LiveOperationsQuery.MaxPageSize;long seen=0;
        while(true)
        {
            var request=new LiveOperationsQuery.PageRequest(page,size,"id","",0,size,"",
                from.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),to.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),platform,"");
            var result=LiveOperationsQuery.Page(erp,kind,request,scope,FinancialCore.Sponsors);
            var projected=LiveOperationsSource.Project(result.Snapshot,FinancialCore.Sponsors,kind);
            items.AddRange(projected.Documents);
            seen+=projected.Documents.Count;
            if(projected.Documents.Count==0||seen>=result.Total||page>1000)break;
            page++;
        }
        if(platform=="chefz")items=items.Where(r=>HalfOk(r,half)).ToList();
        return new(items,asOf,"omar_live");
    }
    static bool HasPendingOmarHandover(DbConnection c,DbTransaction? tx,string companyId)=>
        Store.List(c,"erp_cashbox_handover",tx).Any(h=>(h.S("companyId")==companyId||h.S("sponsorId")==companyId)
            &&(h.S("status") is "pending" or "Pending"||h.S("statusCode") is "1" or "Pending"));
    static void DemandPlatform(ClaimsPrincipal actor,string app)
    {
        string platform=app.Equals("chefz",StringComparison.OrdinalIgnoreCase)?"Chefz":"Jahez";
        if(!FinancialScopes.Platform(actor,platform))throw new ApiError("المنصة خارج النطاق المالي.",403);
    }
    static ClaimsPrincipal SystemActor()
    {
        var id=new ClaimsIdentity("system");
        id.AddClaim(new("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name","system-midnight"));
        id.AddClaim(new("permission","pay.manage"));
        id.AddClaim(new(ClaimTypes.Role,"SystemAdmin"));
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
    static JsonObject ProjectJahezLive(JsonObject row)=>new()
    {
        ["id"]=row.S("id"),["erpRowId"]=row.S("erpSourceId",row.S("id")),["executedAt"]=row.S("executedAt"),
        ["net"]=J.Money(row.N("net")),["commission"]=J.Money(row.N("commission",row.N("calculatedCommission"))),
        ["fund"]=row.S("fund",row.S("fundCode")),["purpose"]=row.S("purpose",row.S("purposeCode")),["status"]=row.S("status")
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
    static decimal CityDailyRate(DbConnection c,DbTransaction? tx,JsonObject row)
    {
        string city=row.S("cityId",row.S("operatingCityId"));
        if(city=="")return 0m;
        var rate=Store.List(c,"platform_daily_commission_rate",tx)
            .Where(r=>r.S("program").Contains("chefz",StringComparison.OrdinalIgnoreCase)&&r.S("operatingCityId")==city)
            .OrderByDescending(r=>r.S("effectiveFrom"))
            .FirstOrDefault();
        return rate==null?0m:J.Money(rate.N("dailyRate"));
    }
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
