using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-16: shared rider requests (sal.rider_request: hold / deduction / advance) + shared-rider holds view
/// + two-person release with release-mode payload (bank_file_separate | individual_transfer) and payload hash re-check at release.
/// Chefz holds and deduction requests consume Omar RiderAmount without rebuilding commission (M-0704).</summary>
public static class SalRiderRequests
{
    public const string PermHoldRequest="salaries.hold.request";
    public const string PermHoldApprove="salaries.hold.approve";
    public const string PermHoldRelease="salaries.hold.release";
    public const string PermDeductionRequest="salaries.deduction.request";
    public const string PermDeductionApprove="salaries.deduction.approve";

    public const byte StatusDraft=1,StatusSent=2,StatusApproved=3,StatusRejected=4,StatusReleased=5,StatusCancelled=6;

    public static string NormalizeReleaseMode(string? mode)=>mode?.Trim().ToLowerInvariant() switch
    {
        "bank_file_separate" or "separate_bank_file"=> "bank_file_separate",
        "individual_transfer" or "single_transfer"=> "individual_transfer",
        _=>throw new ApiError("حدد مسار صرف الراتب المفرج عنه: ملف بنكي منفصل (bank_file_separate) أو تحويل فردي (individual_transfer).",400)
    };

    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaHrCompany.Ensure(c,tx);
        SchemaSalPayRun.Ensure(c,tx);
        SchemaAuthCatalog.Ensure(c,tx);
        SchemaApprovals.Ensure(c,tx);
        EnsureColumns(c,tx);
    }

    static void EnsureColumns(DbConnection c,DbTransaction? tx)
    {
        string req=SchemaSql.Q(c,"sal","rider_request");
        string hold=SchemaSql.Q(c,"sal","shared_rider_hold");
        if(SchemaSql.IsSql(c))
        {
            foreach(var (col,type) in new[]
            {
                ("reason_code","varchar(60) NULL"),
                ("note","nvarchar(500) NULL"),
                ("source","varchar(40) NULL"),
                ("approved_by","nvarchar(128) NULL"),
                ("approved_at","datetime2 NULL"),
                ("release_mode","varchar(40) NULL"),
                ("released_by","nvarchar(128) NULL"),
                ("released_at","datetime2 NULL"),
                ("pay_run_rider_id","uniqueidentifier NULL")
            })
                SchemaSql.Exec(c,tx,$"IF COL_LENGTH(N'sal.rider_request',N'{col}') IS NULL ALTER TABLE {req} ADD [{col}] {type};");

            foreach(var (col,type) in new[]
            {
                ("release_mode","varchar(40) NULL"),
                ("released_by","nvarchar(128) NULL"),
                ("released_at","datetime2 NULL"),
                ("release_reason","nvarchar(500) NULL")
            })
                SchemaSql.Exec(c,tx,$"IF COL_LENGTH(N'sal.shared_rider_hold',N'{col}') IS NULL ALTER TABLE {hold} ADD [{col}] {type};");
        }
        else
        {
            foreach(string col in new[]{"reason_code","note","source","approved_by","approved_at","release_mode","released_by","released_at","pay_run_rider_id"})
            {
                object? has=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM pragma_table_info('sal_rider_request') WHERE name='{col}'");
                if(has is null or DBNull)SchemaSql.Exec(c,tx,$"ALTER TABLE {req} ADD COLUMN {col} TEXT NULL;");
            }
            foreach(string col in new[]{"release_mode","released_by","released_at","release_reason"})
            {
                object? has=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM pragma_table_info('sal_shared_rider_hold') WHERE name='{col}'");
                if(has is null or DBNull)SchemaSql.Exec(c,tx,$"ALTER TABLE {hold} ADD COLUMN {col} TEXT NULL;");
            }
        }
    }

    public static JsonObject List(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string? kind,string? appCode,string? periodMonth,string? companyId,string? status,string? erpRiderId,string? tab=null)
    {
        DemandView(actor);
        Ensure(c,tx);
        string? normMonth=string.IsNullOrWhiteSpace(periodMonth)?null:NormalizeMonth(periodMonth!);
        string? normApp=string.IsNullOrWhiteSpace(appCode)?null:NormalizeApp(appCode!,allowAll:true);
        if(!string.IsNullOrWhiteSpace(companyId))FinancialScopes.DemandSponsor(actor,companyId!);

        var items=new List<JsonObject>();
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"""
                SELECT id,erp_rider_id,company_id,period_month,app_code,kind,amount,status,
                       reason_code,note,source,created_by,created_at,approved_by,approved_at,
                       release_mode,released_by,released_at,pay_run_rider_id
                FROM {SchemaSql.Q(c,"sal","rider_request")}
                ORDER BY created_at DESC,id
                """;
            using var r=cmd.ExecuteReader();
            while(r.Read())
            {
                string rowCompany=r["company_id"].ToString()!;
                string rowApp=r["app_code"].ToString()!;
                if(!InScope(actor,rowCompany,rowApp))continue;
                if(!string.IsNullOrWhiteSpace(companyId)&&!string.Equals(rowCompany,companyId,StringComparison.OrdinalIgnoreCase))continue;
                string rowMonth=ReadMonth(r["period_month"]);
                if(normMonth!=null&&rowMonth!=normMonth)continue;
                if(normApp!=null&&normApp!="all"&&rowApp!="all"&&!string.Equals(rowApp,normApp,StringComparison.OrdinalIgnoreCase))continue;
                string rowKind=r["kind"].ToString()!;
                if(!string.IsNullOrWhiteSpace(kind)&&!string.Equals(kind,"all",StringComparison.OrdinalIgnoreCase)&&!string.Equals(rowKind,kind,StringComparison.OrdinalIgnoreCase))continue;
                string rowRider=r["erp_rider_id"].ToString()!;
                if(!string.IsNullOrWhiteSpace(erpRiderId)&&!string.Equals(rowRider,erpRiderId,StringComparison.OrdinalIgnoreCase))continue;
                int st=Convert.ToInt32(r["status"]);
                string statusText=StatusName(st);
                if(!string.IsNullOrWhiteSpace(status)&&!MatchesStatusFilter(st,statusText,status!))continue;
                if(!MatchesTab(tab,rowKind,st))continue;

                var item=new JsonObject
                {
                    ["id"]=r["id"].ToString()!,
                    ["kind"]=rowKind,
                    ["requestType"]=rowKind,
                    ["typeLabelAr"]=KindLabelAr(rowKind),
                    ["erpRiderId"]=rowRider,
                    ["companyId"]=rowCompany,
                    ["periodMonth"]=rowMonth,
                    ["appCode"]=rowApp,
                    ["amount"]=J.Money(Convert.ToDecimal(r["amount"])),
                    ["status"]=statusText,
                    ["statusCode"]=st,
                    ["statusLabelAr"]=StatusLabelAr(st),
                    ["reasonCode"]=Str(r["reason_code"]),
                    ["note"]=Str(r["note"]),
                    ["source"]=Str(r["source"],"supervisor_request"),
                    ["requestedBy"]=Str(r["created_by"]),
                    ["requestedAt"]=Str(r["created_at"]),
                    ["approvedBy"]=Str(r["approved_by"]),
                    ["approvedAt"]=Str(r["approved_at"]),
                    ["releaseMode"]=Str(r["release_mode"]),
                    ["releasedBy"]=Str(r["released_by"]),
                    ["releasedAt"]=Str(r["released_at"]),
                    ["payRunRiderId"]=Str(r["pay_run_rider_id"])
                };
                items.Add(item);
            }
        }
        foreach(var item in items)
            item["allowedActions"]=ActionsForRequest(c,tx,actor,item);

        // Include sal.shared_rider_hold rows when listing all, shared_hold, or held/released tabs.
        if(string.IsNullOrWhiteSpace(kind)||string.Equals(kind,"shared_hold",StringComparison.OrdinalIgnoreCase)||string.Equals(kind,"hold",StringComparison.OrdinalIgnoreCase)||string.Equals(kind,"all",StringComparison.OrdinalIgnoreCase))
        {
            foreach(var hold in ReadSharedHolds(c,tx,actor,normMonth,companyId,erpRiderId,status,tab))
                items.Add(hold);
        }

        var summary=new JsonObject
        {
            ["openHolds"]=items.Count(i=>i.S("kind")=="hold"&&i.N("statusCode") is 1 or 2),
            ["openDeductions"]=items.Count(i=>i.S("kind")=="deduction"&&i.N("statusCode") is 1 or 2),
            ["heldSalaries"]=items.Count(i=>(i.S("kind")=="hold"&&i.N("statusCode")==3)||(i.S("requestType")=="shared_hold"&&i.S("status")=="open")),
            ["waitingOtherApp"]=items.Count(i=>i.S("requestType")=="shared_hold"&&i.S("status")=="open"),
            ["releasedThisMonth"]=items.Count(i=>i.S("status") is "released" or "cleared"&&i.S("releaseMode")!="")
        };
        var topActions=new JsonArray();
        if(AccessPolicy.Has(actor,PermHoldRequest)||AccessPolicy.Has(actor,PermHoldApprove)||AccessPolicy.Has(actor,"pay.manage"))topActions.Add("create_hold");
        if(AccessPolicy.Has(actor,PermDeductionRequest)||AccessPolicy.Has(actor,PermDeductionApprove)||AccessPolicy.Has(actor,"pay.manage"))topActions.Add("create_deduction");
        return new(){["items"]=new JsonArray(items.Select(i=>(JsonNode?)i).ToArray()),["total"]=items.Count,["summary"]=summary,["allowedActions"]=topActions};
    }

    public static JsonObject Create(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        Ensure(c,tx);
        string kind=input.S("kind",input.S("requestType",input.S("request_type"))).Trim().ToLowerInvariant();
        J.Need(kind is "hold" or "deduction" or "advance","نوع الطلب غير صالح (hold أو deduction أو advance).",400);
        if(kind=="hold")DemandAny(actor,PermHoldRequest,PermHoldApprove,"pay.manage");
        else if(kind=="deduction")DemandAny(actor,PermDeductionRequest,PermDeductionApprove,"pay.manage");
        else DemandAny(actor,"advances.request","pay.manage");

        string companyId=input.Required("companyId");
        FinancialScopes.DemandSponsor(actor,companyId);
        string erpRiderId=input.Required("erpRiderId");
        string periodMonth=NormalizeMonth(input.S("periodMonth",input.S("period")));
        string rawApp=input.S("appCode",input.S("app",kind=="hold"?"all":""));
        J.Need(rawApp!="","حدد المنصة للطلب.",400);
        string appCode=NormalizeApp(rawApp,allowAll:kind=="hold");
        if(appCode!="all")
            J.Need(FinancialScopes.Platform(actor,PayPlatforms.For(appCode).Platform),"المنصة خارج نطاق المستخدم.",403);

        decimal amount=J.Money(input.N("amount"));
        if(kind=="hold")
        {
            J.Need(amount==0,"طلب إيقاف الراتب لا يحمل مبلغ خصم.",400);
        }
        else
        {
            J.Need(amount>0,"مبلغ الطلب يجب أن يكون أكبر من صفر.",400);
        }
        string reasonCode=input.S("reasonCode",input.S("reason_code","general")).Trim();
        string note=input.S("note",input.S("reason")).Trim();
        J.Need(reasonCode.Length is >0 and <=60,"رمز السبب مطلوب.",400);
        J.Need(note.Length<=500,"الملاحظة بحد أقصى 500 حرف.",400);

        bool directApprove=kind=="hold"&&(input.B("directApprove")||input.S("source")=="accountant_review")
            &&(AccessPolicy.Has(actor,PermHoldApprove)||AccessPolicy.Has(actor,"pay.approve"));
        byte status=directApprove?StatusApproved:input.B("draft")?StatusDraft:StatusSent;
        string source=directApprove?"accountant_review":"supervisor_request";
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        string actorName=actor.Identity!.Name!;

        try
        {
            Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"sal","rider_request")}
                (id,erp_rider_id,company_id,period_month,app_code,kind,amount,status,reason_code,note,source,approved_by,approved_at,created_by,created_at)
                VALUES(@id,@erp,@c,@pm,@app,@kind,@amt,@st,@rc,@note,@src,@aby,@aat,@by,@at)
                """,
                ("@id",id),("@erp",erpRiderId),("@c",companyId),("@pm",periodMonth),("@app",appCode),
                ("@kind",kind),("@amt",amount),("@st",status),("@rc",reasonCode),("@note",note),("@src",source),
                ("@aby",directApprove?(object)actorName:DBNull.Value),("@aat",directApprove?(object)now:DBNull.Value),
                ("@by",actorName),("@at",now));
        }
        catch(Exception ex) when(ex.GetBaseException().Message.Contains("UQ_sal_request_hold",StringComparison.OrdinalIgnoreCase)
            ||ex.GetBaseException().Message.Contains("UNIQUE",StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiError("يوجد طلب إيقاف راتب نشط لنفس المندوب والشهر والمنصة.",409);
        }

        if(directApprove)
            ApplyHoldToPayRunRiders(c,tx,companyId,erpRiderId,periodMonth,appCode,actorName,now);

        Store.Audit(c,tx,"sal.rider_request_created",id,actorName);
        return LoadRequestView(c,tx,actor,id);
    }

    public static JsonObject Approve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject? input=null)
    {
        Ensure(c,tx);
        var row=LoadRequestRaw(c,tx,id);
        string kind=row.S("kind");
        if(kind=="hold")DemandAny(actor,PermHoldApprove,"pay.approve");
        else if(kind=="deduction")DemandAny(actor,PermDeductionApprove,"pay.approve");
        else DemandAny(actor,"advances.review","pay.approve");

        DemandInScope(actor,row.S("companyId"),row.S("appCode"));
        int st=(int)row.N("statusCode");
        J.Need(st is StatusDraft or StatusSent,"الطلب ليس في حالة انتظار الاعتماد.",409);

        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        string actorName=actor.Identity!.Name!;
        string note=input?.S("note",row.S("note"))?.Trim()??row.S("note");

        Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"sal","rider_request")}
            SET status=@st,approved_by=@by,approved_at=@at,note=@note,updated_by=@by,updated_at=@at
            WHERE id=@id
            """,("@st",StatusApproved),("@by",actorName),("@at",now),("@note",note),("@id",id));

        if(kind=="hold")
            ApplyHoldToPayRunRiders(c,tx,row.S("companyId"),row.S("erpRiderId"),row.S("periodMonth"),row.S("appCode"),actorName,now);

        Store.Audit(c,tx,"sal.rider_request_approved",id,actorName);
        return LoadRequestView(c,tx,actor,id);
    }

    public static JsonObject Reject(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        Ensure(c,tx);
        var row=LoadRequestRaw(c,tx,id);
        string kind=row.S("kind");
        if(kind=="hold")DemandAny(actor,PermHoldApprove,"pay.approve");
        else if(kind=="deduction")DemandAny(actor,PermDeductionApprove,"pay.approve");
        else DemandAny(actor,"advances.review","pay.approve");

        DemandInScope(actor,row.S("companyId"),row.S("appCode"));
        int st=(int)row.N("statusCode");
        J.Need(st is StatusDraft or StatusSent,"الطلب ليس في حالة انتظار الاعتماد.",409);

        string reason=input.S("reason",input.S("note")).Trim();
        J.Need(reason.Length is >=3 and <=500,"سبب الرفض مطلوب (3 إلى 500 حرف).",400);
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        string actorName=actor.Identity!.Name!;

        Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"sal","rider_request")}
            SET status=@st,note=@note,updated_by=@by,updated_at=@at
            WHERE id=@id
            """,("@st",StatusRejected),("@note",reason),("@by",actorName),("@at",now),("@id",id));

        Store.Audit(c,tx,"sal.rider_request_rejected",id,actorName);
        return LoadRequestView(c,tx,actor,id);
    }

    /// <summary>Opens a two-person approval in ApprovalsArea bound to (id, amount, rider, period, app, releaseMode, reason) hash.</summary>
    public static JsonObject RequestRelease(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        Ensure(c,tx);
        DemandAny(actor,PermHoldRelease,PermHoldApprove,"pay.approve",ApprovalsArea.DualSharedRiderRelease);
        string releaseMode=NormalizeReleaseMode(input.S("releaseMode",input.S("release_mode",input.S("payoutMode"))));
        string reason=input.S("reason",input.S("note")).Trim();
        J.Need(reason.Length is >=3 and <=500,"سبب طلب الإفراج مطلوب (3 إلى 500 حرف).",400);

        var target=ResolveReleaseTarget(c,tx,actor,id);
        decimal amount=CurrentHeldAmount(c,tx,target.ErpRiderId,target.PeriodMonth,target.CompanyId,target.AppCode,target.FallbackAmount);
        string periodKey=target.PeriodMonth.Length>=7?target.PeriodMonth[..7]:target.PeriodMonth;
        string payloadHash=ReleasePayloadHash(target.ReferenceId,amount,target.ErpRiderId,periodKey,target.AppCode,releaseMode,reason);

        var req=ApprovalsArea.OpenMoneyOutDual(c,tx,actor,"shared_rider_release",target.ReferenceId,amount,payloadHash,
            "اعتماد إفراج عن راتب موقوف",
            releaseMode=="bank_file_separate"
                ?"طلب إفراج عن راتب موقوف لإدراجه في ملف بنكي منفصل — بانتظار اعتماد شخصين."
                :"طلب إفراج عن راتب موقوف لتحويل فردي — بانتظار اعتماد شخصين.",
            new JsonObject
            {
                ["requestId"]=id,["rider"]=target.ErpRiderId,["period"]=periodKey,["appCode"]=target.AppCode,
                ["companyId"]=target.CompanyId,["sponsorId"]=target.CompanyId,["releaseMode"]=releaseMode,
                ["reason"]=reason,["payloadHash"]=payloadHash,["fileContentHash"]=payloadHash
            });

        Store.Audit(c,tx,"sal.hold_release_requested",id,actor.Identity!.Name!);
        return req;
    }

    /// <summary>Executes the release after verifying the recorded two-person decision and recomputing the payload hash from live run nets.</summary>
    public static JsonObject Release(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        Ensure(c,tx);
        DemandAny(actor,PermHoldRelease,"pay.approve",ApprovalsArea.DualSharedRiderRelease);
        string releaseMode=NormalizeReleaseMode(input.S("releaseMode",input.S("release_mode",input.S("payoutMode"))));
        string reason=input.S("reason",input.S("note")).Trim();
        J.Need(reason.Length is >=3 and <=500,"سبب الإفراج مطلوب (3 إلى 500 حرف).",400);

        var target=ResolveReleaseTarget(c,tx,actor,id);
        decimal amount=CurrentHeldAmount(c,tx,target.ErpRiderId,target.PeriodMonth,target.CompanyId,target.AppCode,target.FallbackAmount);
        string periodKey=target.PeriodMonth.Length>=7?target.PeriodMonth[..7]:target.PeriodMonth;
        string currentHash=ReleasePayloadHash(target.ReferenceId,amount,target.ErpRiderId,periodKey,target.AppCode,releaseMode,reason);

        ApprovalsArea.DemandRecordedMoneyOutDual(c,tx,actor,ApprovalsArea.DualSharedRiderRelease,target.ReferenceId,currentHash);

        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        string actorName=actor.Identity!.Name!;

        if(target.IsSharedHold)
        {
            Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"sal","shared_rider_hold")}
                SET status='cleared',release_mode=@mode,released_by=@by,released_at=@at,release_reason=@reason,updated_by=@by,updated_at=@at
                WHERE id=@id
                """,("@mode",releaseMode),("@by",actorName),("@at",now),("@reason",reason),("@id",id));
            var doc=SharedRiderConsolidation.Ensure(c,tx,target.ErpRiderId,periodKey,target.CompanyId);
            doc["released"]=true;doc["status"]="released";doc["releaseReason"]=reason;doc["releaseMode"]=releaseMode;
            doc["releasedBy"]=actorName;doc["releasedAt"]=now;doc["combinedNet"]=amount;
            Store.Save(c,SharedRiderConsolidation.Kind,doc,tx);
        }
        else
        {
            Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"sal","rider_request")}
                SET status=@st,release_mode=@mode,released_by=@by,released_at=@at,note=@reason,updated_by=@by,updated_at=@at
                WHERE id=@id
                """,("@st",StatusReleased),("@mode",releaseMode),("@by",actorName),("@at",now),("@reason",reason),("@id",id));
        }

        UpdateBankEligibilityAfterRelease(c,tx,target.CompanyId,target.ErpRiderId,target.PeriodMonth,target.AppCode,releaseMode,actorName,now);
        Store.Audit(c,tx,"sal.hold_released",id,actorName);

        if(target.IsSharedHold)
        {
            return new JsonObject
            {
                ["id"]=id,["requestType"]="shared_hold",["erpRiderId"]=target.ErpRiderId,
                ["companyId"]=target.CompanyId,["periodMonth"]=target.PeriodMonth,["appCode"]="all",
                ["status"]="cleared",["released"]=true,["releaseMode"]=releaseMode,
                ["releasedBy"]=actorName,["releasedAt"]=now,["combinedNet"]=amount,
                ["allowedActions"]=new JsonArray()
            };
        }
        return LoadRequestView(c,tx,actor,id);
    }

    public static string ReleasePayloadHash(string referenceId,decimal amountSar,string erpRiderId,string periodKey,string appCode,string releaseMode,string reason)
        =>ApprovalsArea.PayloadContentHash(referenceId,amountSar,
            ("app",appCode),("period",periodKey),("reason",reason),("releaseMode",releaseMode),("rider",erpRiderId));

    /// <summary>Approved deduction requests for this rider, company, period and app (consumed in L1 by SalPayCalc, including Chefz).</summary>
    public static List<(string Id,decimal Amount,string ReasonCode)> ApprovedDeductionRequests(DbConnection c,DbTransaction tx,string companyId,string erpRiderId,string periodMonth,string appCode)
    {
        Ensure(c,tx);
        var list=new List<(string Id,decimal Amount,string ReasonCode)>();
        string app=PayPlatforms.For(appCode).App;
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,amount,reason_code FROM {SchemaSql.Q(c,"sal","rider_request")}
            WHERE company_id=@c AND erp_rider_id=@erp AND period_month=@pm
              AND kind='deduction' AND status=@st AND amount>0
              AND (app_code=@app OR app_code='all')
            ORDER BY created_at,id
            """;
        Add(cmd,"@c",companyId);Add(cmd,"@erp",erpRiderId);Add(cmd,"@pm",periodMonth);
        Add(cmd,"@st",StatusApproved);Add(cmd,"@app",app);
        using var r=cmd.ExecuteReader();
        while(r.Read())
            list.Add((r["id"].ToString()!,J.Money(Convert.ToDecimal(r["amount"])),Str(r["reason_code"],"supervisor_deduction")));
        return list;
    }

    /// <summary>True when the rider has an active hold request (status=3) or a hold released via individual_transfer (status=5, release_mode='individual_transfer')
    /// or a shared_rider_hold released via individual_transfer.</summary>
    public static bool HasActiveBankBlockingHold(DbConnection c,DbTransaction? tx,string erpRiderId,string periodMonth,string appCode)
    {
        if(!Guid.TryParse(erpRiderId,out _))return false;
        if(!SchemaSql.TableExists(c,tx,"sal","rider_request"))return false;
        EnsureColumns(c,tx);
        string app=appCode is "" or "all"?"all":PayPlatforms.For(appCode).App;
        object? hit=SchemaSql.Scalar(c,tx,$"""
            SELECT 1 FROM {SchemaSql.Q(c,"sal","rider_request")}
            WHERE erp_rider_id=@erp AND period_month=@pm AND kind='hold'
              AND (status=3 OR (status=5 AND release_mode='individual_transfer'))
              AND (@app='all' OR app_code='all' OR app_code=@app)
            """,("@erp",erpRiderId),("@pm",periodMonth),("@app",app));
        if(hit is not null and not DBNull)return true;
        if(SchemaSql.TableExists(c,tx,"sal","shared_rider_hold"))
        {
            object? sh=SchemaSql.Scalar(c,tx,$"""
                SELECT 1 FROM {SchemaSql.Q(c,"sal","shared_rider_hold")}
                WHERE erp_rider_id=@erp AND period_month=@pm
                  AND (status='open' OR (status='cleared' AND release_mode='individual_transfer'))
                """,("@erp",erpRiderId),("@pm",periodMonth));
            if(sh is not null and not DBNull)return true;
        }
        return false;
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/sal/rider-requests",(string? kind,string? requestType,string? appCode,string? app,string? periodMonth,string? period,string? companyId,string? status,string? erpRiderId,string? tab,HttpContext ctx)=>
            store.Read(c=>List(c,null,ctx.User,kind??requestType,appCode??app,periodMonth??period,companyId,status,erpRiderId,tab)))
            .WithTags("Payroll").WithSummary("List salary hold and deduction requests and shared-rider holds");
        api.MapPost("/sal/rider-requests",(JsonObject input,HttpContext ctx)=>
            store.Write((c,tx)=>Create(c,tx,ctx.User,input)))
            .WithTags("Payroll").WithSummary("Create a salary hold or deduction request");
        api.MapPost("/sal/rider-requests/{id}/approve",(string id,JsonObject? input,HttpContext ctx)=>
            store.Write((c,tx)=>Approve(c,tx,ctx.User,id,input)))
            .WithTags("Payroll").WithSummary("Confirm and approve a salary hold or deduction request");
        api.MapPost("/sal/rider-requests/{id}/reject",(string id,JsonObject input,HttpContext ctx)=>
            store.Write((c,tx)=>Reject(c,tx,ctx.User,id,input)))
            .WithTags("Payroll").WithSummary("Reject a salary hold or deduction request");
        api.MapPost("/sal/rider-requests/{id}/request-release",(string id,JsonObject input,HttpContext ctx)=>
            store.Write((c,tx)=>RequestRelease(c,tx,ctx.User,id,input)))
            .WithTags("Payroll").WithSummary("Open two-person approval to release a held rider salary with releaseMode");
        api.MapPost("/sal/rider-requests/{id}/release",(string id,JsonObject input,HttpContext ctx)=>
            store.Write((c,tx)=>Release(c,tx,ctx.User,id,input)))
            .WithTags("Payroll").WithSummary("Release a held rider salary after recorded two-person approval bound to releaseMode and payload hash");
    }

    sealed record ReleaseTarget(string Id,string ReferenceId,string CompanyId,string ErpRiderId,string PeriodMonth,string AppCode,decimal FallbackAmount,bool IsSharedHold);

    static ReleaseTarget ResolveReleaseTarget(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"""
                SELECT id,erp_rider_id,company_id,period_month,app_code,kind,amount,status
                FROM {SchemaSql.Q(c,"sal","rider_request")} WHERE id=@id
                """;
            Add(cmd,"@id",id);
            using var r=cmd.ExecuteReader();
            if(r.Read())
            {
                string kind=r["kind"].ToString()!;
                int st=Convert.ToInt32(r["status"]);
                string companyId=r["company_id"].ToString()!;
                string appCode=r["app_code"].ToString()!;
                string erp=r["erp_rider_id"].ToString()!;
                string pm=ReadMonth(r["period_month"]);
                decimal amt=J.Money(Convert.ToDecimal(r["amount"]));
                r.Close();
                DemandInScope(actor,companyId,appCode);
                J.Need(kind=="hold","الإفراج متاح لطلبات إيقاف الراتب فقط.",409);
                J.Need(st==StatusApproved,"طلب الإيقاف ليس في حالة موقوف معتمد.",409);
                return new(id,"sal-rider-hold:"+id,companyId,erp,pm,appCode,amt,false);
            }
        }
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"""
                SELECT id,erp_rider_id,period_month,status,combined_net
                FROM {SchemaSql.Q(c,"sal","shared_rider_hold")} WHERE id=@id
                """;
            Add(cmd,"@id",id);
            using var r=cmd.ExecuteReader();
            J.Need(r.Read(),"طلب الإيقاف أو الحجز المشترك غير موجود.",404);
            string erp=r["erp_rider_id"].ToString()!;
            string pm=ReadMonth(r["period_month"]);
            string st=r["status"].ToString()!;
            decimal combined=J.Money(Convert.ToDecimal(r["combined_net"]));
            r.Close();
            J.Need(st=="open","الحجز المشترك ليس مفتوحاً.",409);
            string companyId=ResolveRiderCompany(c,tx,erp,pm);
            FinancialScopes.DemandSponsor(actor,companyId);
            return new(id,"sal-shared-hold:"+id,companyId,erp,pm,"all",combined,true);
        }
    }

    static decimal CurrentHeldAmount(DbConnection c,DbTransaction tx,string erpRiderId,string periodMonth,string companyId,string appCode,decimal fallbackAmount)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT COALESCE(SUM(r.salary_net),0),COUNT(*)
            FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r
            INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=r.pay_run_id
            WHERE r.erp_rider_id=@erp AND r.period_month=@pm AND pr.status<>4
              AND (@c='' OR r.company_id=@c)
              AND (@app='all' OR pr.app_code=@app)
            """;
        Add(cmd,"@erp",erpRiderId);Add(cmd,"@pm",periodMonth);
        Add(cmd,"@c",companyId);Add(cmd,"@app",appCode);
        using var r=cmd.ExecuteReader();
        if(r.Read()&&Convert.ToInt32(r.GetValue(1))>0)
            return J.Money(Math.Max(0m,Convert.ToDecimal(r.GetValue(0))));
        return J.Money(Math.Max(0m,fallbackAmount));
    }

    static void ApplyHoldToPayRunRiders(DbConnection c,DbTransaction tx,string companyId,string erpRiderId,string periodMonth,string appCode,string actorName,string now)
    {
        Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"sal","pay_run_rider")}
            SET bank_eligible=0,updated_by=@by,updated_at=@at
            WHERE company_id=@c AND erp_rider_id=@erp AND period_month=@pm AND bank_eligible=1
              AND (@app='all' OR pay_run_id IN (SELECT id FROM {SchemaSql.Q(c,"sal","pay_run")} WHERE app_code=@app))
            """,("@by",actorName),("@at",now),("@c",companyId),("@erp",erpRiderId),("@pm",periodMonth),("@app",appCode));
    }

    static void UpdateBankEligibilityAfterRelease(DbConnection c,DbTransaction tx,string companyId,string erpRiderId,string periodMonth,string appCode,string releaseMode,string actorName,string now)
    {
        if(releaseMode=="individual_transfer")
        {
            ApplyHoldToPayRunRiders(c,tx,companyId,erpRiderId,periodMonth,appCode,actorName,now);
            return;
        }
        // bank_file_separate: restore bank_eligible=1 on approved live unblocked pay_run_rider rows when no other hold remains.
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT r.id,pr.app_code,pr.payout_blocked,r.run_mode,r.salary_approved_at,r.debt_confirm_status,r.payable_id,r.salary_net
            FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r
            INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=r.pay_run_id
            WHERE r.erp_rider_id=@erp AND r.period_month=@pm AND pr.status<>4
              AND (@c='' OR r.company_id=@c)
              AND (@app='all' OR pr.app_code=@app)
            """;
        Add(cmd,"@erp",erpRiderId);Add(cmd,"@pm",periodMonth);Add(cmd,"@c",companyId);Add(cmd,"@app",appCode);
        using var r=cmd.ExecuteReader();
        var eligibleIds=new List<(string Id,string App)>();
        while(r.Read())
        {
            bool approved=r["salary_approved_at"] is not DBNull&&!string.IsNullOrEmpty(r["salary_approved_at"].ToString());
            string debtSt=r["debt_confirm_status"].ToString()!;
            bool hasPayable=r["payable_id"] is not DBNull&&!string.IsNullOrEmpty(r["payable_id"].ToString());
            decimal net=Convert.ToDecimal(r["salary_net"]);
            int runMode=Convert.ToInt32(r["run_mode"]);
            bool pb=Convert.ToInt32(r["payout_blocked"])==1;
            if(approved&&debtSt is "confirmed" or "deferred"&&hasPayable&&net>0&&runMode==0&&!pb)
                eligibleIds.Add((r["id"].ToString()!,r["app_code"].ToString()!));
        }
        r.Close();
        foreach(var (riderRowId,rowApp) in eligibleIds)
        {
            if(HasActiveBankBlockingHold(c,tx,erpRiderId,periodMonth,rowApp))continue;
            Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"sal","pay_run_rider")}
                SET bank_eligible=1,updated_by=@by,updated_at=@at
                WHERE id=@id
                """,("@by",actorName),("@at",now),("@id",riderRowId));
        }
    }

    static List<JsonObject> ReadSharedHolds(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string? normMonth,string? companyId,string? erpRiderId,string? status,string? tab)
    {
        var list=new List<JsonObject>();
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,erp_rider_id,period_month,status,combined_net,release_mode,released_by,released_at,release_reason,created_at
            FROM {SchemaSql.Q(c,"sal","shared_rider_hold")}
            ORDER BY period_month DESC,id
            """;
        using var r=cmd.ExecuteReader();
        var raw=new List<(string Id,string Erp,string Month,string Status,decimal Net,string Mode,string RelBy,string RelAt,string Reason,string CreatedAt)>();
        while(r.Read())
            raw.Add((r["id"].ToString()!,r["erp_rider_id"].ToString()!,ReadMonth(r["period_month"]),r["status"].ToString()!,
                J.Money(Convert.ToDecimal(r["combined_net"])),Str(r["release_mode"]),Str(r["released_by"]),Str(r["released_at"]),
                Str(r["release_reason"]),Str(r["created_at"])));
        r.Close();

        foreach(var h in raw)
        {
            if(normMonth!=null&&h.Month!=normMonth)continue;
            if(!string.IsNullOrWhiteSpace(erpRiderId)&&!string.Equals(h.Erp,erpRiderId,StringComparison.OrdinalIgnoreCase))continue;
            if(!string.IsNullOrWhiteSpace(status)&&!string.Equals(h.Status,status,StringComparison.OrdinalIgnoreCase))continue;
            if(tab=="requests")continue;
            if(tab=="held"&&h.Status!="open")continue;
            if(tab=="released"&&h.Status!="cleared")continue;

            string rowCompany=TryResolveRiderCompany(c,tx,h.Erp,h.Month)??companyId??"";
            if(rowCompany!=""&&!FinancialScopes.Sponsor(actor,rowCompany))continue;
            if(!string.IsNullOrWhiteSpace(companyId)&&rowCompany!=""&&!string.Equals(rowCompany,companyId,StringComparison.OrdinalIgnoreCase))continue;

            var awaited=new JsonArray();
            var received=new JsonArray();
            using(var acmd=c.CreateCommand())
            {
                acmd.Transaction=tx;
                acmd.CommandText=$"SELECT app_code,received FROM {SchemaSql.Q(c,"sal","shared_rider_hold_app")} WHERE hold_id=@h ORDER BY app_code";
                Add(acmd,"@h",h.Id);
                using var ar=acmd.ExecuteReader();
                while(ar.Read())
                {
                    string app=ar["app_code"].ToString()!;
                    if(Convert.ToInt32(ar["received"])==1)received.Add(app);
                    else awaited.Add(app);
                }
            }
            var actions=new JsonArray();
            if(h.Status=="open"&&(AccessPolicy.Has(actor,PermHoldRelease)||AccessPolicy.Has(actor,"pay.approve")||AccessPolicy.Has(actor,ApprovalsArea.DualSharedRiderRelease)))
            {
                actions.Add("request_release");
                actions.Add("release");
            }
            list.Add(new JsonObject
            {
                ["id"]=h.Id,
                ["kind"]="shared_hold",
                ["requestType"]="shared_hold",
                ["typeLabelAr"]="انتظار فاتورة تطبيق",
                ["erpRiderId"]=h.Erp,
                ["companyId"]=rowCompany,
                ["periodMonth"]=h.Month,
                ["appCode"]="all",
                ["amount"]=h.Net,
                ["combinedNet"]=h.Net,
                ["status"]=h.Status,
                ["statusCode"]=h.Status=="open"?3:5,
                ["statusLabelAr"]=h.Status=="open"?"بانتظار فاتورة تطبيق":"مفرج عنه / مدمج",
                ["awaitedApps"]=awaited,
                ["receivedApps"]=received,
                ["releaseMode"]=h.Mode,
                ["releasedBy"]=h.RelBy,
                ["releasedAt"]=h.RelAt,
                ["note"]=h.Reason,
                ["requestedAt"]=h.CreatedAt,
                ["allowedActions"]=actions
            });
        }
        return list;
    }

    static JsonObject LoadRequestRaw(DbConnection c,DbTransaction? tx,string id)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,erp_rider_id,company_id,period_month,app_code,kind,amount,status,
                   reason_code,note,source,created_by,created_at,approved_by,approved_at,
                   release_mode,released_by,released_at,pay_run_rider_id
            FROM {SchemaSql.Q(c,"sal","rider_request")} WHERE id=@id
            """;
        Add(cmd,"@id",id);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"الطلب غير موجود.",404);
        int st=Convert.ToInt32(r["status"]);
        string kind=r["kind"].ToString()!;
        return new JsonObject
        {
            ["id"]=r["id"].ToString()!,
            ["kind"]=kind,
            ["requestType"]=kind,
            ["typeLabelAr"]=KindLabelAr(kind),
            ["erpRiderId"]=r["erp_rider_id"].ToString()!,
            ["companyId"]=r["company_id"].ToString()!,
            ["periodMonth"]=ReadMonth(r["period_month"]),
            ["appCode"]=r["app_code"].ToString()!,
            ["amount"]=J.Money(Convert.ToDecimal(r["amount"])),
            ["status"]=StatusName(st),
            ["statusCode"]=st,
            ["statusLabelAr"]=StatusLabelAr(st),
            ["reasonCode"]=Str(r["reason_code"]),
            ["note"]=Str(r["note"]),
            ["source"]=Str(r["source"],"supervisor_request"),
            ["requestedBy"]=Str(r["created_by"]),
            ["requestedAt"]=Str(r["created_at"]),
            ["approvedBy"]=Str(r["approved_by"]),
            ["approvedAt"]=Str(r["approved_at"]),
            ["releaseMode"]=Str(r["release_mode"]),
            ["releasedBy"]=Str(r["released_by"]),
            ["releasedAt"]=Str(r["released_at"]),
            ["payRunRiderId"]=Str(r["pay_run_rider_id"])
        };
    }

    static JsonObject LoadRequestView(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        var item=LoadRequestRaw(c,tx,id);
        DemandInScope(actor,item.S("companyId"),item.S("appCode"));
        item["allowedActions"]=ActionsForRequest(c,tx,actor,item);
        return item;
    }

    static JsonArray ActionsForRequest(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject item)
    {
        _=c;_=tx;
        var actions=new JsonArray();
        int st=(int)item.N("statusCode");
        string kind=item.S("kind");
        if(st is StatusDraft or StatusSent)
        {
            bool canApprove=kind switch
            {
                "hold"=>AccessPolicy.Has(actor,PermHoldApprove)||AccessPolicy.Has(actor,"pay.approve"),
                "deduction"=>AccessPolicy.Has(actor,PermDeductionApprove)||AccessPolicy.Has(actor,"pay.approve"),
                _=>AccessPolicy.Has(actor,"advances.review")||AccessPolicy.Has(actor,"pay.approve")
            };
            if(canApprove){actions.Add("approve");actions.Add("reject");}
        }
        else if(st==StatusApproved&&kind=="hold")
        {
            if(AccessPolicy.Has(actor,PermHoldRelease)||AccessPolicy.Has(actor,"pay.approve")||AccessPolicy.Has(actor,ApprovalsArea.DualSharedRiderRelease))
            {
                actions.Add("request_release");
                actions.Add("release");
            }
        }
        return actions;
    }

    static string ResolveRiderCompany(DbConnection c,DbTransaction? tx,string erpRider,string periodMonth)
    {
        string? company=TryResolveRiderCompany(c,tx,erpRider,periodMonth);
        J.Need(!string.IsNullOrEmpty(company),"لا مسير لهذا المندوب في الشهر.",404);
        return company!;
    }

    static string? TryResolveRiderCompany(DbConnection c,DbTransaction? tx,string erpRider,string periodMonth)
    {
        string sql=SchemaSql.IsSql(c)
            ?$"""
                SELECT TOP 1 pr.company_id FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r
                INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=r.pay_run_id
                WHERE r.erp_rider_id=@erp AND r.period_month=@pm AND pr.status<>4
                """
            :$"""
                SELECT pr.company_id FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r
                INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=r.pay_run_id
                WHERE r.erp_rider_id=@erp AND r.period_month=@pm AND pr.status<>4 LIMIT 1
                """;
        object? company=SchemaSql.Scalar(c,tx,sql,("@erp",erpRider),("@pm",periodMonth));
        return company is null or DBNull?null:company.ToString();
    }

    static bool InScope(ClaimsPrincipal actor,string companyId,string appCode)
        =>FinancialScopes.Sponsor(actor,companyId)&&(appCode=="all"||FinancialScopes.Platform(actor,PayPlatforms.For(appCode).Platform));

    static void DemandInScope(ClaimsPrincipal actor,string companyId,string appCode)
    {
        FinancialScopes.DemandSponsor(actor,companyId);
        if(appCode!="all")
            J.Need(FinancialScopes.Platform(actor,PayPlatforms.For(appCode).Platform),"المنصة خارج نطاق المستخدم.",403);
    }

    static void DemandView(ClaimsPrincipal actor)
        =>DemandAny(actor,"pay.view",PermHoldRequest,PermHoldApprove,PermHoldRelease,PermDeductionRequest,PermDeductionApprove,"advances.request","advances.review");

    static void DemandAny(ClaimsPrincipal actor,params string[] permissions)
    {
        if(AccessPolicy.Administrator(actor)||permissions.Any(p=>AccessPolicy.Has(actor,p)))return;
        AccessPolicy.Demand(actor,permissions[0]);
    }

    static string NormalizeMonth(string value)
    {
        string s=value.Trim();
        if(s.Length==7)s+="-01";
        J.Need(DateOnly.TryParseExact(s,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)&&d.Day==1,"صيغة شهر الفترة غير صالحة (YYYY-MM-01).",400);
        return d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    }

    static string NormalizeApp(string value,bool allowAll)
    {
        string s=value.Trim().ToLowerInvariant();
        if(allowAll&&s is "all" or "كل المنصات")return "all";
        return PayPlatforms.For(s).App;
    }

    static string StatusName(int code)=>code switch
    {
        StatusDraft=>"draft",
        StatusSent=>"sent",
        StatusApproved=>"approved",
        StatusRejected=>"rejected",
        StatusReleased=>"released",
        StatusCancelled=>"cancelled",
        _=>"unknown"
    };

    static string StatusLabelAr(int code)=>code switch
    {
        StatusDraft=>"مسودة",
        StatusSent=>"مرسل",
        StatusApproved=>"معتمد",
        StatusRejected=>"مرفوض",
        StatusReleased=>"مفرج عنه",
        StatusCancelled=>"ملغى",
        _=>"غير معروف"
    };

    static string KindLabelAr(string kind)=>kind switch
    {
        "hold"=>"إيقاف راتب",
        "deduction"=>"خصم",
        "advance"=>"سلفة",
        _=>kind
    };

    static bool MatchesStatusFilter(int code,string statusName,string filter)
        =>string.Equals(statusName,filter,StringComparison.OrdinalIgnoreCase)
          ||(int.TryParse(filter,out int n)&&n==code);

    static bool MatchesTab(string? tab,string kind,int status)
        =>string.IsNullOrWhiteSpace(tab)||tab.Trim().ToLowerInvariant() switch
        {
            "requests"=>status is StatusDraft or StatusSent or StatusRejected,
            "held"=>kind=="hold"&&status==StatusApproved,
            "released"=>kind=="hold"&&status==StatusReleased,
            _=>true
        };

    static string Str(object? value,string fallback="")=>value is null or DBNull?fallback:value.ToString()??fallback;

    static string ReadMonth(object value)=>value switch
    {
        DateTime dt=>DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        DateOnly d=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        string s=>s.Length==7?s+"-01":s[..10],
        _=>Convert.ToDateTime(value,CultureInfo.InvariantCulture).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)
    };

    static int Exec(DbConnection c,DbTransaction tx,string sql,params (string Name,object Value)[] values)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;
        foreach(var (n,v) in values)Add(cmd,n,v);
        return cmd.ExecuteNonQuery();
    }

    static void Add(DbCommand cmd,string name,object value)
    {var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value??DBNull.Value;cmd.Parameters.Add(p);}
}
