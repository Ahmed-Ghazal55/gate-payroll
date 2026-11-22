using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>WC4D/RWC4a1 H11 (§40.5, M-0587, M-0672): a rider who worked more than one app in the month is held by default.
/// Worked apps come from every live run of the month: riders already calculated AND the frozen work timelines.
/// When every hold app is received, nets consolidate (shared fuel/vehicle/housing once) and the hold clears.
/// Exception release needs the dedicated dual permission before every app is in.</summary>
public static class SalSharedRiders
{
    static readonly HashSet<string> SharedDeductionCodes=new(StringComparer.Ordinal){"fuel","vehicle","housing"};

    public static Dictionary<string,HashSet<string>> WorkedApps(DbConnection c,DbTransaction tx,string periodMonth)
    {
        var map=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string rider,string app)
        {
            if(rider==""||app=="")return;
            if(!map.TryGetValue(rider,out var set))map[rider]=set=new(StringComparer.Ordinal);
            set.Add(PayPlatforms.For(app).App);
        }
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"""
                SELECT r.erp_rider_id,pr.app_code FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r
                INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=r.pay_run_id
                WHERE r.period_month=@pm AND pr.status<>4
                """;
            Param(cmd,"@pm",periodMonth);
            using var r=cmd.ExecuteReader();
            while(r.Read())Add(r["erp_rider_id"].ToString()!,r["app_code"].ToString()!);
        }
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"""
                SELECT pr.app_code,s.payload FROM {SchemaSql.Q(c,"fin","operation_snapshot_link")} l
                INNER JOIN {SchemaSql.Q(c,"sal","ops_snapshot")} s ON s.id=l.snapshot_id
                INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=l.operation_id
                WHERE l.operation_type='pay_run' AND l.snapshot_kind='assignment' AND pr.period_month=@pm AND pr.status<>4
                """;
            Param(cmd,"@pm",periodMonth);
            using var r=cmd.ExecuteReader();
            while(r.Read())
            {
                if(JsonNode.Parse(r["payload"].ToString()!) is not JsonObject row)continue;
                string app=SalPayCalc.AssignmentApp(row);
                Add(SalPayCalc.AssignmentRider(row),app==""?r["app_code"].ToString()!:app);
            }
        }
        return map;
    }

    public static HashSet<string> AppsFor(Dictionary<string,HashSet<string>> worked,string erpRider,string runApp)
    {
        var apps=new HashSet<string>(worked.TryGetValue(erpRider,out var set)?set:[],StringComparer.Ordinal){PayPlatforms.For(runApp).App};
        return apps;
    }

    /// <summary>Open (or extend) the hold with one row per worked app; an app is "received" once its run has calculated the rider.
    /// When every app is received, consolidate (§40.5) and clear the hold.</summary>
    public static void OpenHold(DbConnection c,DbTransaction tx,string erpRider,string periodMonth,IEnumerable<string> apps,string calculatingApp)
    {
        object? cleared=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM {SchemaSql.Q(c,"sal","shared_rider_hold")} WHERE erp_rider_id=@erp AND period_month=@pm AND status='cleared'",
            ("@erp",erpRider),("@pm",periodMonth));
        if(cleared is not null and not DBNull)return; // consolidation or exception release already decided
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"sal","shared_rider_hold")} WHERE erp_rider_id=@erp AND period_month=@pm AND status='open'",
            ("@erp",erpRider),("@pm",periodMonth));
        string holdId;
        if(existing is null or DBNull)
        {
            holdId=SchemaSql.GuidText(SchemaSql.NewId());
            SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"sal","shared_rider_hold")}(id,erp_rider_id,period_month,status,combined_net,created_at) VALUES(@id,@erp,@pm,'open',0,@at)",
                ("@id",holdId),("@erp",erpRider),("@pm",periodMonth),("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));
        }
        else holdId=existing.ToString()!;
        foreach(string app in apps)
        {
            object? has=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM {SchemaSql.Q(c,"sal","shared_rider_hold_app")} WHERE hold_id=@h AND app_code=@a",("@h",holdId),("@a",app));
            if(has is null or DBNull)
                SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"sal","shared_rider_hold_app")}(id,hold_id,app_code,received) VALUES(@id,@h,@a,0)",
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@h",holdId),("@a",app));
            object? calculated=SchemaSql.Scalar(c,tx,$"""
                SELECT 1 FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=r.pay_run_id
                WHERE r.erp_rider_id=@erp AND r.period_month=@pm AND pr.app_code=@a AND pr.status<>4
                """,("@erp",erpRider),("@pm",periodMonth),("@a",app));
            if(app==calculatingApp||calculated is not null and not DBNull)
                SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","shared_rider_hold_app")} SET received=1 WHERE hold_id=@h AND app_code=@a",("@h",holdId),("@a",app));
        }
        ConsolidateIfComplete(c,tx,holdId,erpRider,periodMonth);
    }

    /// <summary>§40.5: when every hold app is received, sum each app's net and add back duplicate shared L1 (fuel/vehicle/housing), then clear.</summary>
    public static void ConsolidateIfComplete(DbConnection c,DbTransaction tx,string holdId,string erpRider,string periodMonth)
    {
        object? pending=SchemaSql.Scalar(c,tx,$"SELECT COUNT(*) FROM {SchemaSql.Q(c,"sal","shared_rider_hold_app")} WHERE hold_id=@h AND received=0",("@h",holdId));
        if(pending is not null and not DBNull&&Convert.ToInt64(pending)>0)return;

        decimal combined=0;
        var sharedSeen=new HashSet<string>(StringComparer.Ordinal);
        decimal sharedDuplicate=0;
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"""
                SELECT r.id,r.salary_net,pr.app_code FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r
                INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=r.pay_run_id
                INNER JOIN {SchemaSql.Q(c,"sal","shared_rider_hold_app")} ha ON ha.hold_id=@h AND ha.app_code=pr.app_code
                WHERE r.erp_rider_id=@erp AND r.period_month=@pm AND pr.status<>4
                """;
            Param(cmd,"@h",holdId);Param(cmd,"@erp",erpRider);Param(cmd,"@pm",periodMonth);
            using var r=cmd.ExecuteReader();
            var rows=new List<(string RiderId,decimal Net,string App)>();
            while(r.Read())rows.Add((r["id"].ToString()!,Convert.ToDecimal(r["salary_net"]),r["app_code"].ToString()!));
            r.Close();
            foreach(var row in rows)
            {
                combined=J.Money(combined+row.Net);
                using var items=c.CreateCommand();items.Transaction=tx;
                items.CommandText=$"""
                    SELECT item_code,amount FROM {SchemaSql.Q(c,"sal","pay_run_item")}
                    WHERE pay_run_rider_id=@r AND stage_code='level1' AND affects_net=1 AND amount>0
                    """;
                Param(items,"@r",row.RiderId);
                using var ir=items.ExecuteReader();
                while(ir.Read())
                {
                    string code=ir["item_code"].ToString()!;
                    if(!SharedDeductionCodes.Contains(code))continue;
                    decimal amt=Convert.ToDecimal(ir["amount"]);
                    if(!sharedSeen.Add(code))sharedDuplicate=J.Money(sharedDuplicate+amt);
                }
            }
        }
        combined=J.Money(combined+sharedDuplicate);
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"sal","shared_rider_hold")}
            SET status='cleared',combined_net=@net,updated_at=@at WHERE id=@id
            """,("@net",combined),("@at",now),("@id",holdId));
        // Keep the legacy queue document in sync for the shared-riders UI (period key yyyy-MM).
        string periodKey=periodMonth.Length>=7?periodMonth[..7]:periodMonth;
        try
        {
            string companyId=CompanyForRiderMonth(c,tx,erpRider,periodMonth);
            var doc=SharedRiderConsolidation.Ensure(c,tx,erpRider,periodKey,companyId);
            doc["status"]="ready";doc["combinedNet"]=combined;doc["released"]=false;
            doc["missingApps"]=new JsonArray();
            Store.Save(c,SharedRiderConsolidation.Kind,doc,tx);
        }
        catch(ApiError){ /* no live run yet — table hold is still cleared */ }
        Store.Audit(c,tx,"sal.shared_rider_consolidated",holdId,"system");
    }

    /// <summary>Approval gate: a multi-app rider needs a cleared hold (all apps received + consolidated, or second-person exception); an open hold always blocks.</summary>
    public static void DemandApprovable(DbConnection c,DbTransaction tx,Dictionary<string,HashSet<string>> worked,string erpRider,string periodMonth,string runApp)
    {
        object? open=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM {SchemaSql.Q(c,"sal","shared_rider_hold")} WHERE erp_rider_id=@erp AND period_month=@pm AND status='open'",
            ("@erp",erpRider),("@pm",periodMonth));
        J.Need(open is null or DBNull,"مندوب عمل مشترك معلّق — أكمل دمج التطبيقات قبل الاعتماد.",409);
        if(AppsFor(worked,erpRider,runApp).Count<=1)return;
        object? cleared=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM {SchemaSql.Q(c,"sal","shared_rider_hold")} WHERE erp_rider_id=@erp AND period_month=@pm AND status='cleared'",
            ("@erp",erpRider),("@pm",periodMonth));
        J.Need(cleared is not null and not DBNull,"المندوب عمل على أكثر من تطبيق هذا الشهر؛ موقوف حتى تكتمل كل التطبيقات ويُدمج (§40.5).",409);
    }

    /// <summary>M-0587 / M-0672 / U-W2-16: open dual approval to release a still-open hold before every app is in.</summary>
    public static JsonObject RequestRelease(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string erpRider,string periodMonth,string reason,string? releaseMode=null)
    {
        J.Need(reason.Trim().Length is >2 and <=500,"سبب طلب فك الحجز مطلوب.");
        AccessPolicy.Demand(actor,"pay.approve");
        string? normMode=string.IsNullOrWhiteSpace(releaseMode)?null:SalRiderRequests.NormalizeReleaseMode(releaseMode);
        var hold=LoadOpenHold(c,tx,erpRider,periodMonth);
        string companyId=CompanyForRiderMonth(c,tx,erpRider,periodMonth);
        FinancialScopes.DemandSponsor(actor,companyId);
        decimal amount=J.Money(hold.N("combinedNet"));
        string periodKey=periodMonth.Length>=7?periodMonth[..7]:periodMonth;
        var doc=SharedRiderConsolidation.Ensure(c,tx,erpRider,periodKey,companyId);
        Store.Save(c,SharedRiderConsolidation.Kind,doc,tx);
        string payloadHash=normMode is null
            ?ApprovalsArea.PayloadContentHash(doc.S("id"),amount,("rider",erpRider),("period",periodKey),("reason",reason.Trim()))
            :ApprovalsArea.PayloadContentHash(doc.S("id"),amount,("rider",erpRider),("period",periodKey),("reason",reason.Trim()),("releaseMode",normMode));
        var payload=new JsonObject{["rider"]=erpRider,["period"]=periodKey,["reason"]=reason.Trim(),["companyId"]=companyId,["sponsorId"]=companyId,["payloadHash"]=payloadHash,["holdId"]=hold.S("id")};
        if(normMode is not null)payload["releaseMode"]=normMode;
        var req=ApprovalsArea.OpenMoneyOutDual(c,tx,actor,"shared_rider_release",doc.S("id"),amount,payloadHash,
            "اعتماد فك حجز مندوب مشترك","طلب فك حجز مندوب عمل مشترك — بانتظار اعتماد شخصين.",payload);
        doc["releaseRequestedAt"]=DateTimeOffset.UtcNow.ToString("O");
        doc["releaseRequestedBy"]=actor.Identity!.Name;
        doc["releaseRequestId"]=req.S("id");
        doc["releasePayloadHash"]=payloadHash;
        if(normMode is not null)doc["releaseMode"]=normMode;
        Store.Save(c,SharedRiderConsolidation.Kind,doc,tx);
        Store.Audit(c,tx,"sal.shared_rider_release_requested",hold.S("id"),actor.Identity!.Name!);
        return req;
    }

    /// <summary>M-0587 / M-0672 / U-W2-16: after recorded dual decision, clear the open hold (exception path).</summary>
    public static JsonObject Release(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string erpRider,string periodMonth,string reason,string? releaseMode=null)
    {
        J.Need(reason.Trim().Length is >2 and <=500,"سبب الإفراج مطلوب.");
        string? normMode=string.IsNullOrWhiteSpace(releaseMode)?null:SalRiderRequests.NormalizeReleaseMode(releaseMode);
        var hold=LoadOpenHold(c,tx,erpRider,periodMonth);
        string companyId=CompanyForRiderMonth(c,tx,erpRider,periodMonth);
        string periodKey=periodMonth.Length>=7?periodMonth[..7]:periodMonth;
        var doc=SharedRiderConsolidation.Ensure(c,tx,erpRider,periodKey,companyId);
        decimal amount=J.Money(hold.N("combinedNet"));
        string currentHash=normMode is null&&string.IsNullOrWhiteSpace(doc.S("releaseMode"))
            ?ApprovalsArea.PayloadContentHash(doc.S("id"),amount,("rider",erpRider),("period",periodKey),("reason",reason.Trim()))
            :ApprovalsArea.PayloadContentHash(doc.S("id"),amount,("rider",erpRider),("period",periodKey),("reason",reason.Trim()),("releaseMode",normMode??""));
        ApprovalsArea.DemandRecordedMoneyOutDual(c,tx,actor,ApprovalsArea.DualSharedRiderRelease,doc.S("id"),currentHash);
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        SalRiderRequests.Ensure(c,tx);
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"sal","shared_rider_hold")}
            SET status='cleared',release_mode=@mode,released_by=@by,released_at=@at,release_reason=@reason,updated_by=@by,updated_at=@at WHERE id=@id
            """,("@mode",(object?)normMode??DBNull.Value),("@by",actor.Identity!.Name!),("@at",now),("@reason",reason.Trim()),("@id",hold.S("id")));
        doc["released"]=true;doc["status"]="released";doc["releaseReason"]=reason.Trim();
        if(normMode is not null)doc["releaseMode"]=normMode;
        doc["releasedBy"]=actor.Identity!.Name;doc["releasedAt"]=now;
        Store.Save(c,SharedRiderConsolidation.Kind,doc,tx);
        Store.Audit(c,tx,"sal.shared_rider_released",hold.S("id"),actor.Identity!.Name!);
        return new(){["id"]=hold.S("id"),["status"]="cleared",["erpRiderId"]=erpRider,["periodMonth"]=periodMonth,["released"]=true,["releaseMode"]=normMode??""};
    }

    static JsonObject LoadOpenHold(DbConnection c,DbTransaction tx,string erpRider,string periodMonth)
    {
        string id;
        decimal combined;
        string status;
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"SELECT id,combined_net,status FROM {SchemaSql.Q(c,"sal","shared_rider_hold")} WHERE erp_rider_id=@erp AND period_month=@pm AND status='open'";
            Param(cmd,"@erp",erpRider);Param(cmd,"@pm",periodMonth);
            using var r=cmd.ExecuteReader();
            J.Need(r.Read(),"لا حجز مفتوح لهذا المندوب والشهر.",404);
            id=r["id"].ToString()!;
            combined=Convert.ToDecimal(r["combined_net"]);
            status=r["status"].ToString()!;
        }
        if(combined==0m)
        {
            object? live=SchemaSql.Scalar(c,tx,$"""
                SELECT COALESCE(SUM(r.salary_net),0) FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r
                INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=r.pay_run_id
                WHERE r.erp_rider_id=@erp AND r.period_month=@pm AND pr.status<>4
                """,("@erp",erpRider),("@pm",periodMonth));
            if(live is not null and not DBNull)
                combined=J.Money(Math.Max(0m,Convert.ToDecimal(live)));
        }
        return new(){["id"]=id,["combinedNet"]=combined,["status"]=status};
    }

    static string CompanyForRiderMonth(DbConnection c,DbTransaction tx,string erpRider,string periodMonth)
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
        J.Need(company is not null and not DBNull,"لا مسير لهذا المندوب في الشهر.",404);
        return company!.ToString()!;
    }

    static void Param(DbCommand cmd,string name,object value){var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value;cmd.Parameters.Add(p);}
}
